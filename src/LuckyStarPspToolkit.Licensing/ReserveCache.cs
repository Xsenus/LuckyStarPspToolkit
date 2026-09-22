using System.Diagnostics;

namespace LuckyStarPspToolkit.Licensing;

/// <summary>Installation-signed local watermark; edits are detected, but a full machine snapshot rollback needs external attestation to prevent.</summary>
/// <param name="Schema">Storage schema one.</param>
/// <param name="Token">Authority-signed LSPR1 token.</param>
/// <param name="GrantId">Permission identifier.</param>
/// <param name="EffectiveUtc">Last accepted effective server time.</param>
/// <param name="LocalUtc">Local wall-clock observation at that checkpoint.</param>
/// <param name="Blocked">Sticky denial learned from an authenticated online exchange; never cleared by an offline read.</param>
/// <param name="BlockReason">Sticky local clock/expiry reason; empty for an ordinary online denial and older caches.</param>
internal sealed record ReserveState(int Schema, string Token, string GrantId, long EffectiveUtc, long LocalUtc, bool Blocked, string BlockReason = "");

/// <summary>Compact signed-state envelope. The installation key is a different trust domain from authority signing.</summary>
/// <param name="Payload">Base64 encoded local state.</param>
/// <param name="Signature">Installation signature over the domain-separated payload.</param>
internal sealed record ReserveStateEnvelope(string Payload, string Signature);

/// <summary>Fail-closed seven-day reserve cache with signature, installation binding, monotonic live time and durable rollback watermark.</summary>
public sealed class ReserveCache
{
    /// <summary>Private state file; the caller holds the installation lifetime lock.</summary>
    private readonly string path;
    /// <summary>Embedded authority trust.</summary>
    private readonly LicenseTrust trust;
    /// <summary>Current installation signer and verifier.</summary>
    private readonly DeviceIdentity identity;
    /// <summary>Parent license identity.</summary>
    private readonly string licenseId;
    /// <summary>Clock dependency, injectable only through library API for deterministic tests.</summary>
    private readonly TimeProvider time;
    /// <summary>Protects renewal and fallback against heartbeat races.</summary>
    private readonly object sync = new();
    /// <summary>Authenticated local snapshot, or null when no reserve permission was configured.</summary>
    private ReserveState? state;
    /// <summary>Verified authority claims corresponding to the snapshot.</summary>
    private ReserveLease? lease;
    /// <summary>Live monotonic reference timestamp.</summary>
    private long anchor;
    /// <summary>Effective time at the live reference, including retained milliseconds.</summary>
    private double anchorUtc;
    /// <summary>Most recent local wall-clock second seen by this process.</summary>
    private long wallHigh;

    /// <summary>Loads and verifies state without granting access; absence is allowed, corruption is not silently ignored.</summary>
    /// <param name="directory">Private per-installation directory.</param>
    /// <param name="trust">Embedded public trust.</param>
    /// <param name="identity">Opened installation identity.</param>
    /// <param name="licenseId">Activated license UUID.</param>
    /// <param name="time">Optional deterministic clock.</param>
    public ReserveCache(string directory, LicenseTrust trust, DeviceIdentity identity, string licenseId, TimeProvider? time = null)
    {
        path = Path.Combine(directory, "reserve.json"); this.trust = trust; this.identity = identity;
        this.licenseId = licenseId; this.time = time ?? TimeProvider.System;
        if (!File.Exists(path)) return;
        var envelope = LicenseJson.Read<ReserveStateEnvelope>(PrivateFiles.Read(path, 16384), 16384);
        byte[] bytes = LicenseCrypto.Decode(envelope.Payload, 10000);
        if (!identity.VerifyLocalState(bytes, envelope.Signature)) throw new LicenseException("RESERVE_CACHE", "Reserve state was changed or belongs to another installation.");
        state = LicenseJson.Read<ReserveState>(bytes, 10000);
        lease = ReserveCrypto.Verify(state.Token, trust, identity, licenseId, state.GrantId);
        if (state.Schema != 1 || state.EffectiveUtc < lease.ServerNow || state.LocalUtc < 0 ||
            state.BlockReason is not ("" or "RESERVE_EXPIRED" or "RESERVE_CLOCK") || (!state.Blocked && state.BlockReason != ""))
            throw new LicenseException("RESERVE_CACHE", "Invalid reserve clock checkpoint.");
        anchor = this.time.GetTimestamp(); anchorUtc = state.EffectiveUtc; wallHigh = state.LocalUtc;
    }

    /// <summary>Configured permission UUID, including blocked permissions so their state can be displayed without hiding a denial.</summary>
    public string? GrantId { get { lock (sync) return state?.GrantId; } }
    /// <summary>Whether an offline fallback is explicitly configured and not retired locally.</summary>
    public bool Enabled { get { lock (sync) return state is { Blocked: false }; } }
    /// <summary>Exclusive signed UTC deadline, independent of any displayed remaining duration.</summary>
    public long? ValidUntil { get { lock (sync) return lease?.ValidUntil; } }

    /// <summary>Persists a freshly verified online grant, clearing a local denial only through successful reauthorization.</summary>
    /// <param name="token">Signed response token.</param>
    /// <param name="grantId">Expected permission UUID.</param>
    /// <param name="nonce">Expected fresh request nonce.</param>
    /// <param name="elapsed">Full online exchange duration subtracted conservatively from remaining access.</param>
    public void Accept(string token, string grantId, string nonce, TimeSpan elapsed)
    {
        ReserveLease verified = ReserveCrypto.Verify(token, trust, identity, licenseId, grantId, nonce);
        if (elapsed < TimeSpan.Zero || elapsed.TotalSeconds >= verified.ValidUntil - verified.ServerNow)
            throw new LicenseException("RESERVE_EXPIRED", "Reserve grant expired during delivery.");
        lock (sync)
        {
            long now = time.GetUtcNow().ToUnixTimeSeconds();
            long effective = checked(verified.ServerNow + (long)Math.Ceiling(elapsed.TotalSeconds));
            var candidate = new ReserveState(1, token, grantId, effective, now, false);
            Persist(candidate); state = candidate; lease = verified;
            anchor = time.GetTimestamp(); anchorUtc = effective; wallHigh = now;
        }
    }

    /// <summary>Computes a conservative remaining interval and optionally persists a new watermark before starting offline work.</summary>
    /// <param name="checkpoint">Whether to persist progress; call at process boundaries and bounded heartbeat intervals, not every 200 ms.</param>
    /// <returns>Remaining interval; no automatic renewal or grace extension occurs here.</returns>
    public TimeSpan Remaining(bool checkpoint = true)
    {
        lock (sync)
        {
            if (state is null || lease is null) throw new LicenseException("RESERVE_UNAVAILABLE", "No valid reserve permission is configured.");
            if (state.Blocked) throw new LicenseException(state.BlockReason == "" ? "RESERVE_UNAVAILABLE" : state.BlockReason,
                "Reserve access was denied locally. Reconnect for fresh authorization.");
            long wall = time.GetUtcNow().ToUnixTimeSeconds();
            if (wall < wallHigh) Retire("RESERVE_CLOCK", "Local clock moved backwards. Reconnect to renew permission.");
            wallHigh = wall;
            double effective = Math.Max(state.EffectiveUtc + (double)(wall - state.LocalUtc),
                anchorUtc + time.GetElapsedTime(anchor).TotalSeconds);
            if (effective >= lease.ValidUntil) Retire("RESERVE_EXPIRED", "Seven-day or shorter reserve deadline has expired; reconnect.");
            if (checkpoint && effective > state.EffectiveUtc)
            {
                var candidate = state with { EffectiveUtc = (long)Math.Ceiling(effective), LocalUtc = wall };
                Persist(candidate); state = candidate;
            }
            // Round checkpoints upward: short process restarts must not recover a discarded fractional second.
            effective = Math.Max(effective, state.EffectiveUtc);
            if (effective >= lease.ValidUntil) Retire("RESERVE_EXPIRED", "Reserve deadline has expired; reconnect.");
            return TimeSpan.FromSeconds(lease.ValidUntil - effective);
        }
    }

    /// <summary>Persists denial before permitting any future offline attempt; network failures must never call this method.</summary>
    public void Block()
    {
        lock (sync)
        {
            if (state is null || state.Blocked) return;
            var candidate = state with { Blocked = true };
            state = candidate; Persist(candidate);
        }
    }

    /// <summary>Retires a known-invalid token even on a read-only timer check, preventing a later restart from reviving it.</summary>
    /// <param name="code">Observed expiry or clock rollback; never a transient network failure.</param>
    /// <param name="message">Safe diagnostic explaining why online renewal is required.</param>
    private void Retire(string code, string message)
    {
        // Memory is denied first: failed disk persistence must not grant access to a racing caller.
        var candidate = state! with { Blocked = true, BlockReason = code };
        state = candidate;
        Persist(candidate);
        throw new LicenseException(code, message);
    }

    /// <summary>Updates the cache online; explicit reserve retirement blocks fallback but leaves normal online entitlement independent.</summary>
    /// <param name="transport">HTTPS verifier for the same installation.</param>
    /// <param name="cancellation">Operation cancellation.</param>
    /// <returns>Completion; temporary network errors leave the existing signed deadline unchanged.</returns>
    public async Task RenewAsync(LicenseTransport transport, CancellationToken cancellation = default)
    {
        string? id = GrantId;
        if (id is null || !Enabled) return;
        try
        {
            var grant = await transport.RefreshReserveAsync(licenseId, id, cancellation).ConfigureAwait(false);
            Accept(grant.Token, id, grant.Nonce, grant.Elapsed);
        }
        catch (LicenseException ex) when (ex.Code is "RESERVE_DISABLED" or "RESERVE_REVOKED" or "RESERVE_UNKNOWN" or "RESERVE_BINDING")
        { Block(); }
        catch (LicenseException ex) when (IsTransient(ex.Code)) { }
    }

    /// <summary>Classifies only availability failures as candidates for already-signed offline fallback.</summary>
    /// <param name="code">Controlled transport code.</param>
    /// <returns>True for bounded network/overload failures, never signature, policy, expiry or device errors.</returns>
    public static bool IsTransient(string code) => code is "LICENSE_NETWORK" or "RATE_LIMIT" or "CHALLENGE_BUSY" or "SERVER_BUSY";

    /// <summary>Signs and atomically saves a state snapshot without exposing the installation private key.</summary>
    /// <param name="candidate">Validated local state to persist.</param>
    private void Persist(ReserveState candidate)
    {
        byte[] bytes = LicenseJson.Write(candidate);
        PrivateFiles.Write(path, LicenseJson.Write(new ReserveStateEnvelope(LicenseCrypto.Encode(bytes), identity.SignLocalState(bytes))));
    }
}
