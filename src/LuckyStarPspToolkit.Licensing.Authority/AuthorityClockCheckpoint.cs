using System.Security.Cryptography;

namespace LuckyStarPspToolkit.Licensing.Authority;

/// <summary>Small authenticated server-time watermark. It protects restarts without rewriting the entitlement database on heartbeats.</summary>
internal sealed class AuthorityClockCheckpoint : IDisposable
{
    /// <summary>Authenticated clock payload; unlike a lease it grants no permission.</summary>
    /// <param name="Schema">Clock format version.</param>
    /// <param name="Issuer">Authority binding.</param>
    /// <param name="ObservedAt">Highest durably observed UTC second.</param>
    private sealed record ClockPayload(int Schema, string Issuer, long ObservedAt);
    /// <summary>Private same-directory checkpoint destination.</summary>
    private readonly string path;
    /// <summary>Expected authority binding.</summary>
    private readonly string issuer;
    /// <summary>Owned copy of the integrity secret, cleared on disposal.</summary>
    private readonly byte[] secret;
    /// <summary>Serializes writes and destruction of secret material.</summary>
    private readonly object sync = new();
    /// <summary>Highest checkpoint already flushed to disk; reads within that second do not write again.</summary>
    private long persisted;
    /// <summary>Prevents use after clearing the integrity secret.</summary>
    private bool disposed;

    /// <summary>Loads and authenticates the clock file, or migrates a legacy schema-one store before its schema is advanced.</summary>
    /// <param name="directory">Existing private authority directory under the store's exclusive process lock.</param>
    /// <param name="configuration">Matching issuer and HMAC secret.</param>
    /// <param name="mandatory">True for schema two: deleting the checkpoint must fail closed.</param>
    /// <param name="lastWrite">Authenticated database timestamp used as a minimum floor.</param>
    /// <param name="now">Fresh server time checked before recording anything.</param>
    /// <exception cref="LicenseException">Missing mandatory checkpoint, integrity failure or backward startup clock.</exception>
    public AuthorityClockCheckpoint(string directory, AuthorityConfiguration configuration, bool mandatory, long lastWrite, long now)
    {
        path = Path.Combine(directory, "server-clock.json");
        issuer = configuration.Issuer;
        secret = Convert.FromBase64String(configuration.Pepper);
        try
        {
            persisted = lastWrite;
            if (File.Exists(PrivateFiles.SafePath(path)))
            {
                var envelope = LicenseJson.Read<DatabaseEnvelope>(PrivateFiles.Read(path, 2048), 2048);
                byte[] payload = Convert.FromBase64String(envelope.Payload);
                byte[] mac = Convert.FromHexString(envelope.Mac);
                if (envelope.Schema != 1 || payload.Length > 512 || mac.Length != 32 ||
                    !CryptographicOperations.FixedTimeEquals(mac, Authenticate(payload)))
                    throw new LicenseException("SERVER_CLOCK_INTEGRITY", "Server clock checkpoint authentication failed.");
                var record = LicenseJson.Read<ClockPayload>(payload, 512);
                if (record.Schema != 1 || record.Issuer != issuer || record.ObservedAt < lastWrite || record.ObservedAt > 253402300799L)
                    throw new LicenseException("SERVER_CLOCK_INTEGRITY", "Server clock checkpoint does not match the authority database.");
                persisted = record.ObservedAt;
            }
            else if (mandatory)
                throw new LicenseException("SERVER_CLOCK_MISSING", "Required server clock checkpoint is missing. Restore the complete authority backup.");
            if (now < persisted)
                throw new LicenseException("SERVER_CLOCK_ROLLBACK", "Server UTC predates its last authenticated observation. Correct the server clock before restart.");
            // Create even when now equals the legacy timestamp; publish schema two only afterwards.
            if (!File.Exists(path)) Write(now);
            else Observe(now);
        }
        catch (Exception ex) when (ex is FormatException or LicenseException or IOException)
        {
            CryptographicOperations.ZeroMemory(secret);
            if (ex is FormatException) throw new LicenseException("SERVER_CLOCK_INTEGRITY", "Invalid server clock checkpoint encoding.");
            throw;
        }
        catch { CryptographicOperations.ZeroMemory(secret); throw; }
    }

    /// <summary>Persists a newly observed second before a timed decision, including an expiry refusal, can be returned.</summary>
    /// <param name="now">Nondecreasing authority UTC second.</param>
    /// <exception cref="LicenseException">A backward observation or disposed checkpoint is rejected.</exception>
    public void Observe(long now)
    {
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (now < persisted) throw new LicenseException("SERVER_CLOCK_ROLLBACK", "Authority time predates its durable checkpoint.");
            if (now > persisted) Write(now);
        }
    }

    /// <summary>Atomically flushes a small clock envelope and only then publishes its in-memory watermark.</summary>
    /// <param name="now">Second being committed.</param>
    private void Write(long now)
    {
        byte[] payload = LicenseJson.Write(new ClockPayload(1, issuer, now));
        var envelope = new DatabaseEnvelope(1, Convert.ToBase64String(payload), Convert.ToHexString(Authenticate(payload)));
        PrivateFiles.Write(path, LicenseJson.Write(envelope));
        persisted = now;
    }

    /// <summary>Separates clock MACs from database MACs even though both are protected by the same private authority secret.</summary>
    /// <param name="payload">Bounded canonical clock payload.</param>
    /// <returns>HMAC-SHA256 over a clock-specific domain and the payload.</returns>
    private byte[] Authenticate(ReadOnlySpan<byte> payload)
    {
        using IncrementalHash mac = IncrementalHash.CreateHMAC(HashAlgorithmName.SHA256, secret);
        mac.AppendData("LSP-SERVER-CLOCK-v1\0"u8);
        mac.AppendData(payload);
        return mac.GetHashAndReset();
    }

    /// <summary>Clears the owned integrity key once. Shutdown needs no extra write because observations were persisted before use.</summary>
    public void Dispose()
    {
        lock (sync)
        {
            if (disposed) return;
            disposed = true;
            CryptographicOperations.ZeroMemory(secret);
        }
    }
}
