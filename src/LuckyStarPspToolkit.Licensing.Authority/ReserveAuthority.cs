namespace LuckyStarPspToolkit.Licensing.Authority;

/// <summary>Administrative generation change; each real transition invalidates every older offline permission for future renewal.</summary>
/// <param name="RequestId">Idempotency UUID.</param>
/// <param name="Enabled">Whether new reserve grants and renewals are permitted.</param>
public sealed record ReservePolicyRequest(string RequestId, bool Enabled);

/// <summary>Owner reserve issuance tied to an already activated installation and its active license.</summary>
/// <param name="Id">Client-generated issuance UUID, reused on retries.</param>
/// <param name="LicenseId">Underlying license UUID.</param>
/// <param name="DeviceId">Activated installation digest.</param>
/// <param name="Hours">Offline window from one to 168 hours; a shorter parent expiry always wins.</param>
/// <param name="Reason">Non-secret owner rationale, recorded for later review.</param>
public sealed record ReserveIssueRequest(string Id, string LicenseId, string DeviceId, int Hours, string Reason);

/// <summary>Irreversible revocation of a reserve permission, independent of its normal online license.</summary>
/// <param name="RequestId">Idempotency UUID.</param>
/// <param name="GrantId">Permission to retire.</param>
public sealed record ReserveRevokeRequest(string RequestId, string GrantId);

/// <summary>Persistent reserve allow-list entry, containing no private key or executable bypass.</summary>
/// <param name="Id">Permission UUID.</param>
/// <param name="LicenseId">Parent license.</param>
/// <param name="DeviceId">Activated device.</param>
/// <param name="Epoch">Policy generation at issuance.</param>
/// <param name="WindowSeconds">Maximum signed offline interval.</param>
/// <param name="CreatedAt">Owner issuance time.</param>
/// <param name="RevokedAt">Irreversible retirement time, or null.</param>
/// <param name="Reason">Owner rationale.</param>
/// <param name="Fingerprint">Issuance digest for exact retries.</param>
public sealed record ReserveGrant(string Id, string LicenseId, string DeviceId, long Epoch, int WindowSeconds,
    long CreatedAt, long? RevokedAt, string Reason, string Fingerprint);

/// <summary>Detached view of reserve policy and registered permissions.</summary>
/// <param name="Enabled">Current global switch.</param>
/// <param name="Epoch">Current generation.</param>
/// <param name="MaximumHours">Hard ceiling displayed to the owner.</param>
/// <param name="Grants">Historical permissions; an older epoch is retired even if RevokedAt is null.</param>
public sealed record ReserveOverview(bool Enabled, long Epoch, int MaximumHours, ReserveGrant[] Grants);

/// <summary>Reserve permission operations share the authority lock and the authenticated entitlement database.</summary>
public sealed partial class LicenseAuthority
{
    /// <summary>Returns policy and detached immutable permission records without allocating the entire entitlement database.</summary>
    /// <returns>Owner-only current reserve state.</returns>
    public ReserveOverview ReserveStatus()
    {
        lock (sync) return store.ReadCommitted(db => new ReserveOverview(db.ReserveEnabled, db.ReserveEpoch, 168,
            db.ReserveGrants.Values.OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id, StringComparer.Ordinal).ToArray()));
    }

    /// <summary>Changes the reserve global switch with an epoch increase; re-enabling never revives retired grants.</summary>
    /// <param name="request">Exactly-once administrative transition.</param>
    /// <returns>Committed policy.</returns>
    public ReserveOverview SetReservePolicy(ReservePolicyRequest request)
    {
        ValidateUuid(request.RequestId);
        string fingerprint = LicenseCrypto.Digest(LicenseJson.Write(request));
        lock (sync)
        {
            long now = Now();
            if (!WasRequest(request.RequestId, fingerprint)) store.Change(now, db =>
            {
                // No-op requests are logged/idempotent but do not accidentally retire current grants.
                if (db.ReserveEnabled != request.Enabled)
                { db.ReserveEnabled = request.Enabled; db.ReserveEpoch = checked(db.ReserveEpoch + 1); }
                db.Requests.Add(request.RequestId, fingerprint);
                db.Audit.Add(new(now, request.Enabled ? "reserve-enable" : "reserve-disable", "", ""));
                return 0;
            });
            return ReserveStatus();
        }
    }

    /// <summary>Creates a reserve allow-list entry; ordinary license state, expiry and activated device are always required.</summary>
    /// <param name="request">Owner permission parameters.</param>
    /// <returns>Committed immutable permission; its UUID is not a secret and cannot authorize a different device.</returns>
    public ReserveGrant IssueReserve(ReserveIssueRequest request)
    {
        ValidateUuid(request.Id); ValidateUuid(request.LicenseId); LicenseCrypto.ValidateDigest(request.DeviceId);
        if (request.Hours is < 1 or > 168 || string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Length > 200 || request.Reason.Any(char.IsControl))
            throw new LicenseException("RESERVE_POLICY", "Provide 1..168 hours and a non-secret reason of at most 200 characters.");
        string fingerprint = LicenseCrypto.Digest(LicenseJson.Write(request));
        lock (sync)
        {
            long now = Now();
            var existing = store.ReadCommitted(db => db.ReserveGrants.GetValueOrDefault(request.Id));
            if (existing is not null)
            {
                if (existing.Fingerprint != fingerprint) throw new LicenseException("IDEMPOTENCY_CONFLICT", "Reserve ID has different parameters.");
                return existing;
            }
            return store.Change(now, db =>
            {
                if (!db.ReserveEnabled) throw new LicenseException("RESERVE_DISABLED", "Reserve access is disabled globally.");
                if (!db.Licenses.TryGetValue(request.LicenseId, out var license)) throw new LicenseException("LICENSE_INVALID", "License not found.");
                RequireActive(license, now);
                if (!license.Devices.ContainsKey(request.DeviceId)) throw new LicenseException("DEVICE_NOT_ACTIVATED", "Activate this installation online first.");
                var grant = new ReserveGrant(request.Id, request.LicenseId, request.DeviceId, db.ReserveEpoch,
                    checked(request.Hours * 3600), now, null, request.Reason, fingerprint);
                db.ReserveGrants.Add(grant.Id, grant);
                db.Audit.Add(new(now, "reserve-issue", grant.LicenseId, grant.DeviceId));
                return grant;
            });
        }
    }

    /// <summary>Retires one reserve permission permanently. Existing disconnected grants remain valid only until their signed deadline.</summary>
    /// <param name="request">Idempotent retirement.</param>
    /// <returns>Retired record.</returns>
    public ReserveGrant RevokeReserve(ReserveRevokeRequest request)
    {
        ValidateUuid(request.RequestId); ValidateUuid(request.GrantId);
        string fingerprint = LicenseCrypto.Digest(LicenseJson.Write(request));
        lock (sync)
        {
            long now = Now();
            if (WasRequest(request.RequestId, fingerprint)) return store.ReadCommitted(db => db.ReserveGrants[request.GrantId]);
            return store.Change(now, db =>
            {
                if (!db.ReserveGrants.TryGetValue(request.GrantId, out var grant)) throw new LicenseException("RESERVE_UNKNOWN", "Reserve permission not found.");
                var revoked = grant with { RevokedAt = grant.RevokedAt ?? now };
                db.ReserveGrants[grant.Id] = revoked; db.Requests.Add(request.RequestId, fingerprint);
                db.Audit.Add(new(now, "reserve-revoke", grant.LicenseId, grant.DeviceId));
                return revoked;
            });
        }
    }

    /// <summary>Issues a fresh signed offline lease only after consuming a valid online installation proof and checking the live global policy.</summary>
    /// <param name="request">Reserve ID and fresh check proof.</param>
    /// <returns>Nonce-bound signed permission, with an expiry capped by both the reserve window and parent license.</returns>
    public LeaseResponse RefreshReserve(ReserveRefreshRequest request)
    {
        ValidateUuid(request.GrantId);
        if (request.Request.Action != "check") throw new LicenseException("REQUEST_INVALID", "Reserve renewal requires an activated installation check.");
        VerifyPossession(request.Request);
        lock (sync)
        {
            ExecutionLease authorization = AuthorizeVerified(request.Request); // same checks, no unused LSP1 token/signature
            long now = authorization.ServerNow;
            return store.ReadCommitted(db =>
            {
                if (!db.ReserveEnabled) throw new LicenseException("RESERVE_DISABLED", "Reserve access is disabled globally.");
                if (!db.ReserveGrants.TryGetValue(request.GrantId, out var grant)) throw new LicenseException("RESERVE_UNKNOWN", "Reserve permission not found.");
                if (grant.RevokedAt.HasValue || grant.Epoch != db.ReserveEpoch) throw new LicenseException("RESERVE_REVOKED", "Reserve permission was retired.");
                if (grant.LicenseId != request.Request.LicenseId || grant.DeviceId != LicenseCrypto.DeviceId(request.Request.DevicePublicKey))
                    throw new LicenseException("RESERVE_BINDING", "Reserve permission belongs to another installation.");
                var license = db.Licenses[grant.LicenseId]; RequireActive(license, now);
                long until = Math.Min(checked(now + grant.WindowSeconds), license.ExpiresAt ?? long.MaxValue);
                return new LeaseResponse(ReserveCrypto.Sign(new(1, config.Issuer, LicenseCrypto.Product, grant.Id, grant.LicenseId,
                    grant.DeviceId, request.Request.HostBinding, grant.Epoch, request.Request.ClientNonce, now, until), signer));
            });
        }
    }

    /// <summary>Validates a canonical administrative UUID without accepting ambiguous string representations.</summary>
    /// <param name="value">Identifier to check.</param>
    private static void ValidateUuid(string value)
    { if (!Guid.TryParseExact(value, "D", out _)) throw new LicenseException("REQUEST_INVALID", "A UUID is required."); }

    /// <summary>Detects exact retries or conflicting reuse before any database changes.</summary>
    /// <param name="id">Request UUID.</param>
    /// <param name="digest">Digest of all mutation parameters.</param>
    /// <returns>True only when the identical request has committed before.</returns>
    private bool WasRequest(string id, string digest)
    {
        string? prior = store.ReadCommitted(db => db.Requests.GetValueOrDefault(id));
        if (prior is not null && prior != digest) throw new LicenseException("IDEMPOTENCY_CONFLICT", "Mutation ID was reused with different parameters.");
        return prior is not null;
    }
}
