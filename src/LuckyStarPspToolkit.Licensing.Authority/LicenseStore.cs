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
            byte[] mac = Convert.FromHexString(envelope.Mac);
            if (envelope.Schema != 1 || mac.Length != 32 || !CryptographicOperations.FixedTimeEquals(mac, HMACSHA256.HashData(pepper, bytes)))
                throw new LicenseException("DATABASE_INTEGRITY", "License database authentication failed.");
            state = LicenseJson.Read<LicenseDatabase>(bytes, MaximumPayload);
            if (state.Schema != 1 || state.Issuer != configuration.Issuer || state.Revision < 0 ||
                state.Licenses is null || state.Requests is null || state.Audit is null || state.Licenses.Count > 10000)
                throw new LicenseException("DATABASE_INVALID", "Invalid license database schema.");
            foreach (var pair in state.Licenses)
            {
                if (pair.Key != pair.Value.Id || !Guid.TryParseExact(pair.Key, "D", out _) ||
                    pair.Value.Status is not ("active" or "suspended" or "revoked") || pair.Value.Devices is null ||
                    pair.Value.MaxDevices is < 1 or > 100 || pair.Value.Devices.Count > pair.Value.MaxDevices)
                    throw new LicenseException("DATABASE_INVALID", "Inconsistent entitlement record.");
                LicenseCrypto.ValidateDigest(pair.Value.KeyDigest);
            }
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

    /// <summary>Runs a read while no writer can publish a new state. The callback must not mutate the snapshot.</summary>
    /// <typeparam name="T">Read result.</typeparam>
    /// <param name="read">Pure reader.</param>
    /// <returns>Reader result.</returns>
    public T Read<T>(Func<LicenseDatabase, T> read) { lock (sync) return read(state); }

    /// <summary>Applies a mutation to a detached copy, flushes it before publication, and preserves old memory/disk state on failure.</summary>
    /// <typeparam name="T">Transaction result.</typeparam>
    /// <param name="now">Authoritative commit timestamp.</param>
    /// <param name="change">Mutation of the detached snapshot.</param>
    /// <returns>Result only after durable file flush and atomic replacement succeeded.</returns>
    public T Change<T>(long now, Func<LicenseDatabase, T> change)
    {
        lock (sync)
        {
            var copy = LicenseJson.Read<LicenseDatabase>(LicenseJson.Write(state), MaximumPayload);
            T result = change(copy);
            if (copy.Licenses.Count > 10000 || copy.Requests.Count > 100000 || copy.Audit.Count > 100000)
                throw new LicenseException("DATABASE_LIMIT", "Authority capacity reached; archive and migrate before adding records.");
            copy = copy with { Revision = checked(state.Revision + 1), LastWriteUtc = Math.Max(state.LastWriteUtc, now) };
            byte[] payload = LicenseJson.Write(copy);
            if (payload.Length > MaximumPayload) throw new LicenseException("DATABASE_LIMIT", "License database size limit reached.");
            var envelope = new DatabaseEnvelope(1, Convert.ToBase64String(payload), Convert.ToHexString(HMACSHA256.HashData(pepper, payload)));
            PrivateFiles.Write(path, LicenseJson.Write(envelope));
            state = copy;
            return result;
        }
    }

    /// <summary>Returns a nonreversible server-peppered credential index.</summary>
    /// <param name="key">Validated plaintext access key received over TLS.</param>
    /// <returns>HMAC-SHA256 digest.</returns>
    public string KeyDigest(string key)
    {
        LicenseCrypto.ValidateAccessKey(key);
        return Convert.ToHexString(HMACSHA256.HashData(pepper, System.Text.Encoding.ASCII.GetBytes(key))).ToLowerInvariant();
    }

    /// <summary>Releases the exclusive writer lock and clears the HMAC secret.</summary>
    public void Dispose() { processLock.Dispose(); CryptographicOperations.ZeroMemory(pepper); }
}
