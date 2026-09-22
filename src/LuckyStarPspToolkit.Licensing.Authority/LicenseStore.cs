using System.Security.Cryptography;

namespace LuckyStarPspToolkit.Licensing.Authority;

/// <summary>Authenticated, atomic single-process store. Uses an exclusive lifetime lock; failover requires stopping the previous writer.</summary>
public sealed class LicenseStore : IDisposable
{
    /// <summary>Maximum database payload before envelope encoding.</summary>
    private const int MaximumPayload = 32 * 1024 * 1024;
    /// <summary>Private snapshot path.</summary>
    private readonly string path;
    /// <summary>Random HMAC key, owned by this store.</summary>
    private readonly byte[] pepper;
    /// <summary>Prevents two processes from silently overwriting each other's activations.</summary>
    private readonly FileStream processLock;
    /// <summary>Serializes transactions and matching reads.</summary>
    private readonly object sync = new();
    /// <summary>Most recent authenticated committed state.</summary>
    private LicenseDatabase state;
    /// <summary>Guards operations after the process lock and integrity secret have been released.</summary>
    private bool disposed;

    /// <summary>Opens an existing database; missing/corrupt data is never silently reset.</summary>
    /// <param name="directory">Private authority directory.</param>
    /// <param name="configuration">Matching authority secrets.</param>
    public LicenseStore(string directory, AuthorityConfiguration configuration)
    {
        path = Path.Combine(directory, "licenses.json");
        pepper = Convert.FromBase64String(configuration.Pepper);
        if (pepper.Length != 32) throw new LicenseException("AUTHORITY_CONFIG", "Invalid database integrity key.");
        processLock = PrivateFiles.Lock(Path.Combine(directory, "authority.lock"));
        try
        {
            var envelope = LicenseJson.Read<DatabaseEnvelope>(PrivateFiles.Read(path, MaximumPayload * 2), MaximumPayload * 2);
            byte[] bytes = Convert.FromBase64String(envelope.Payload);
            if (bytes.Length > MaximumPayload) throw new LicenseException("DATABASE_LIMIT", "License database payload exceeds its limit.");
            byte[] mac = Convert.FromHexString(envelope.Mac);
            if (envelope.Schema != 1 || mac.Length != 32 || !CryptographicOperations.FixedTimeEquals(mac, HMACSHA256.HashData(pepper, bytes)))
                throw new LicenseException("DATABASE_INTEGRITY", "License database authentication failed.");
            state = LicenseJson.Read<LicenseDatabase>(bytes, MaximumPayload);
            LicenseDatabaseSnapshot.Validate(state, configuration.Issuer);
        }
        catch { processLock.Dispose(); CryptographicOperations.ZeroMemory(pepper); throw; }
    }

    /// <summary>Creates the first authenticated empty snapshot. Only owner initialization calls this.</summary>
    /// <param name="directory">New private authority directory.</param>
    /// <param name="configuration">New authority configuration.</param>
    /// <param name="now">Initialization time.</param>
    public static void Initialize(string directory, AuthorityConfiguration configuration, long now)
    {
        var database = new LicenseDatabase(1, configuration.Issuer, 0, now, new(), new(), new());
        byte[] bytes = LicenseJson.Write(database);
        byte[] secret = Convert.FromBase64String(configuration.Pepper);
        try
        {
            var envelope = new DatabaseEnvelope(1, Convert.ToBase64String(bytes), Convert.ToHexString(HMACSHA256.HashData(secret, bytes)));
            PrivateFiles.Write(Path.Combine(directory, "licenses.json"), LicenseJson.Write(envelope), false);
        }
        finally { CryptographicOperations.ZeroMemory(secret); }
    }

    /// <summary>Runs a caller-supplied query on an isolated snapshot; retained or mutated results cannot affect committed state.</summary>
    /// <typeparam name="T">Read result.</typeparam>
    /// <param name="read">Reader over a detached copy; this public API has a copying cost proportional to database size.</param>
    /// <returns>Caller result without exposing mutable committed collections.</returns>
    public T Read<T>(Func<LicenseDatabase, T> read)
    {
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            return read(LicenseDatabaseSnapshot.Copy(state));
        }
    }

    /// <summary>Runs a trusted authority query without copying the database; the callback must not mutate or expose committed collections.</summary>
    /// <typeparam name="T">Result restricted by the internal caller's contract.</typeparam>
    /// <param name="read">Pure internal query.</param>
    /// <returns>Internal result, consumed while the authority serializes entitlement operations.</returns>
    internal T ReadCommitted<T>(Func<LicenseDatabase, T> read)
    {
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            return read(state);
        }
    }

    /// <summary>Applies a mutation to a detached snapshot and publishes a second isolated snapshot only after file replacement succeeds.</summary>
    /// <typeparam name="T">Transaction result.</typeparam>
    /// <param name="now">Authoritative timestamp, previously checkpointed by the authority.</param>
    /// <param name="change">Mutation callback; retaining its snapshot or result cannot modify committed data afterwards.</param>
    /// <returns>Result only after flush and replacement; failures preserve the prior memory/disk state.</returns>
    public T Change<T>(long now, Func<LicenseDatabase, T> change)
    {
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            var copy = LicenseDatabaseSnapshot.Copy(state);
            T result = change(copy);
            Commit(copy, now);
            return result;
        }
    }

    /// <summary>Marks the clock checkpoint as mandatory only after its initial file has been durably created.</summary>
    /// <param name="now">Startup timestamp already recorded by the checkpoint.</param>
    internal void RequireClockCheckpoint(long now)
    {
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (state.Schema < 3) Commit(state with { Schema = 3 }, now);
        }
    }

    /// <summary>Validates, detaches and atomically commits a candidate while the store lock is held.</summary>
    /// <param name="candidate">Potentially aliased callback snapshot; never published directly.</param>
    /// <param name="now">Nonnegative supported server UTC second.</param>
    private void Commit(LicenseDatabase candidate, long now)
    {
        if (now is < 0 or > 253402300799L)
            throw new LicenseException("DATABASE_INVALID", "Invalid transaction timestamp.");
        var stamped = candidate with { Revision = checked(state.Revision + 1), LastWriteUtc = Math.Max(state.LastWriteUtc, now) };
        LicenseDatabaseSnapshot.Validate(stamped, state.Issuer);
        var committed = LicenseDatabaseSnapshot.Copy(stamped);
        byte[] payload = LicenseJson.Write(committed);
        if (payload.Length > MaximumPayload) throw new LicenseException("DATABASE_LIMIT", "License database size limit reached.");
        var envelope = new DatabaseEnvelope(1, Convert.ToBase64String(payload), Convert.ToHexString(HMACSHA256.HashData(pepper, payload)));
        PrivateFiles.Write(path, LicenseJson.Write(envelope));
        state = committed;
    }

    /// <summary>Returns a nonreversible server-peppered credential index.</summary>
    /// <param name="key">Validated plaintext access key received over TLS.</param>
    /// <returns>HMAC-SHA256 digest.</returns>
    public string KeyDigest(string key)
    {
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            LicenseCrypto.ValidateAccessKey(key);
            return Convert.ToHexString(HMACSHA256.HashData(pepper, System.Text.Encoding.ASCII.GetBytes(key))).ToLowerInvariant();
        }
    }

    /// <summary>Releases the exclusive writer lock and clears the HMAC secret.</summary>
    public void Dispose()
    {
        lock (sync)
        {
            if (disposed) return;
            disposed = true;
            processLock.Dispose();
            CryptographicOperations.ZeroMemory(pepper);
        }
    }
}
