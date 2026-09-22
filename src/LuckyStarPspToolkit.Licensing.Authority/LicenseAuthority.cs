using System.Security.Cryptography;

namespace LuckyStarPspToolkit.Licensing.Authority;

/// <summary>Server-side entitlement state machine; all time and device-limit decisions are made here, not by the customer UI.</summary>
public sealed partial class LicenseAuthority : IDisposable
{
    /// <summary>Challenge cache entry held only in memory, invalidated by consumption or server restart.</summary>
    /// <param name="Binding">Operation, product and installation that requested the challenge.</param>
    /// <param name="Until">Exclusive server deadline.</param>
    private sealed record Challenge(ChallengeRequest Binding, long Until);
    /// <summary>Authoritative persistent state.</summary>
    private readonly LicenseStore store;
    /// <summary>Nondecreasing server timeline.</summary>
    private readonly AuthorityClock clock;
    /// <summary>Authenticated restart watermark, written before timed access decisions.</summary>
    private readonly AuthorityClockCheckpoint checkpoint;
    /// <summary>Authority private signer; serialized through sync because provider handles are not assumed thread-safe.</summary>
    private readonly ECDsa signer;
    /// <summary>Validated private authority settings.</summary>
    private readonly AuthorityConfiguration config;
    /// <summary>One synchronization boundary covers nonce consumption and entitlement mutation.</summary>
    private readonly object sync = new();
    /// <summary>Bounded one-use challenge cache.</summary>
    private readonly Dictionary<string, Challenge> challenges = new(StringComparer.Ordinal);
    /// <summary>Expiry queue makes challenge cleanup amortized rather than scanning the complete dictionary on each request.</summary>
    private readonly PriorityQueue<string, long> challengeExpiry = new();
    /// <summary>Activation-key digest to license-ID lookup rebuilt only after committed owner mutations.</summary>
    private Dictionary<string, string> keyIndex;

    /// <summary>Starts an authority over an existing authenticated store and encrypted private key.</summary>
    /// <param name="directory">Private authority directory.</param>
    /// <param name="password">PKCS#8 passphrase supplied privately by the service manager.</param>
    /// <param name="time">Optional deterministic time source for tests.</param>
    public LicenseAuthority(string directory, string password, TimeProvider? time = null)
    {
        config = LicenseJson.Read<AuthorityConfiguration>(PrivateFiles.Read(Path.Combine(directory, "authority.json")));
        if (config.Schema != 1 || config.LeaseSeconds is < 15 or > LicenseCrypto.MaximumLeaseSeconds)
            throw new LicenseException("AUTHORITY_CONFIG", "Invalid authority configuration.");
        clock = new AuthorityClock(time ?? TimeProvider.System);
        signer = ECDsa.Create();
        byte[] encrypted = Convert.FromBase64String(config.EncryptedPkcs8);
        LicenseStore? openedStore = null;
        AuthorityClockCheckpoint? openedCheckpoint = null;
        try
        {
            signer.ImportEncryptedPkcs8PrivateKey(password, encrypted, out int used);
            if (used != encrypted.Length || Convert.ToBase64String(signer.ExportSubjectPublicKeyInfo()) != config.PublicKey)
                throw new LicenseException("AUTHORITY_KEY", "Authority keys do not match.");
            openedStore = new LicenseStore(directory, config);
            long now = clock.Now();
            var metadata = openedStore.ReadCommitted(db => (db.Schema, db.LastWriteUtc));
            openedCheckpoint = new AuthorityClockCheckpoint(directory, config, metadata.Schema >= 2, metadata.LastWriteUtc, now);
            openedStore.RequireClockCheckpoint(now);
            store = openedStore;
            checkpoint = openedCheckpoint;
            keyIndex = store.ReadCommitted(db => db.Licenses.Values.ToDictionary(x => x.KeyDigest, x => x.Id, StringComparer.Ordinal));
        }
        catch { openedCheckpoint?.Dispose(); openedStore?.Dispose(); signer.Dispose(); throw; }
    }

    /// <summary>Initializes new owner-only secrets, database, admin connection and public build profile without overwriting any previous authority.</summary>
    /// <param name="directory">New, not-yet-existing private directory outside the repository.</param>
    /// <param name="serverUrl">Public HTTPS URL, or literal loopback HTTP in an isolated development setup.</param>
    /// <param name="password">At least sixteen characters protecting the exported private key.</param>
    /// <param name="development">Explicitly marks loopback-only test trust, which the production publisher refuses.</param>
    /// <param name="leaseSeconds">Bounded maximum run grant.</param>
    /// <returns>Non-secret trust profile to embed in client builds.</returns>
    public static LicenseTrust Initialize(string directory, string serverUrl, string password, bool development = false, int leaseSeconds = 90)
    {
        if (password.Length < 16 || leaseSeconds is < 15 or > 120)
            throw new LicenseException("INIT_POLICY", "Use a passphrase of at least sixteen characters and leases of 15..120 seconds.");
        directory = PrivateFiles.SafePath(directory);
        if (Directory.Exists(directory) || File.Exists(directory))
            throw new LicenseException("INIT_EXISTS", "Authority initialization requires a path that does not yet exist.");
        using ECDsa signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        string issuer = Guid.NewGuid().ToString("D");
        string publicKey = Convert.ToBase64String(signer.ExportSubjectPublicKeyInfo());
        var trust = new LicenseTrust(1, LicenseCrypto.Product, issuer, serverUrl,
            new() { [LicenseCrypto.Digest(signer.ExportSubjectPublicKeyInfo())[..16]] = publicKey }, development);
        _ = LicenseCrypto.ValidateTrust(trust);
        string token = LicenseCrypto.Nonce();
        var config = new AuthorityConfiguration(1, issuer, serverUrl,
            Convert.ToBase64String(signer.ExportEncryptedPkcs8PrivateKey(password,
                new PbeParameters(PbeEncryptionAlgorithm.Aes256Cbc, HashAlgorithmName.SHA256, 600000))),
            publicKey, Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
            LicenseCrypto.Digest(System.Text.Encoding.ASCII.GetBytes(token)), leaseSeconds);
        string parent = Path.GetDirectoryName(directory)!;
        PrivateFiles.Directory(parent);
        string staging = Path.Combine(parent, ".authority-init-" + Guid.NewGuid().ToString("N"));
        try
        {
            PrivateFiles.Directory(staging);
            PrivateFiles.Write(Path.Combine(staging, "authority.json"), LicenseJson.Write(config), false);
            LicenseStore.Initialize(staging, config, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            PrivateFiles.Write(Path.Combine(staging, "owner-connection.json"),
                LicenseJson.Write(new OwnerConnection("http://127.0.0.1:17841/", token)), false);
            PrivateFiles.Write(Path.Combine(staging, "client-trust.json"), LicenseJson.Write(trust), false);
            _ = PrivateFiles.SafePath(directory);
            // Same-parent rename publishes all initial secrets together. Never replace an existing authority.
            Directory.Move(staging, directory);
            return trust;
        }
        finally { if (Directory.Exists(staging)) Directory.Delete(staging, true); }
    }

    /// <summary>Issues a bounded, fresh one-time challenge. Reusing a challenge is rejected even with a valid signature.</summary>
    /// <param name="request">Exact operation and installation binding.</param>
    /// <returns>Random challenge response.</returns>
    public ChallengeResponse ChallengeFor(ChallengeRequest request)
    {
        if (request.ProductId != LicenseCrypto.Product || request.Action is not ("activate" or "check"))
            throw new LicenseException("REQUEST_INVALID", "Unknown licensing operation.");
        _ = LicenseCrypto.DeviceId(request.DevicePublicKey);
        LicenseCrypto.ValidateDigest(request.HostBinding);
        lock (sync)
        {
            long now = Now();
            while (challengeExpiry.TryPeek(out _, out long deadline) && deadline <= now)
                challenges.Remove(challengeExpiry.Dequeue());
            if (challengeExpiry.Count >= 8192)
            {
                // Consumed nonces can remain in the queue until expiry; periodically compact it with a strict bound.
                challengeExpiry.Clear();
                foreach (var outstanding in challenges) challengeExpiry.Enqueue(outstanding.Key, outstanding.Value.Until);
            }
            if (challenges.Count >= 4096) throw new LicenseException("CHALLENGE_BUSY", "Too many outstanding challenges; retry later.");
            string value = LicenseCrypto.Nonce();
            challenges.Add(value, new(request, now + 60));
            challengeExpiry.Enqueue(value, now + 60);
            return new(value);
        }
    }

    /// <summary>Consumes a possession proof, validates the entitlement and returns a short-lived signed grant.</summary>
    /// <param name="request">Complete untrusted request.</param>
    /// <returns>Signed execution lease, never an unsigned boolean permission.</returns>
    public LeaseResponse Authorize(LicenseRequest request)
    {
        byte[] transcript = LicenseCrypto.Transcript(request);
        byte[] proof = LicenseCrypto.Decode(request.Proof, 64, 64);
        using ECDsa verifier = LicenseCrypto.ImportPublic(request.DevicePublicKey);
        if (!verifier.VerifyData(transcript, proof, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation))
            throw new LicenseException("DEVICE_PROOF", "Installation proof is invalid.");
        string deviceId = LicenseCrypto.DeviceId(request.DevicePublicKey);
        lock (sync)
        {
            long now = Now();
            if (!challenges.Remove(request.Challenge, out Challenge? challenge) || challenge.Until <= now ||
                challenge.Binding != new ChallengeRequest(request.ProductId, request.Action, request.DevicePublicKey, request.HostBinding))
                throw new LicenseException("CHALLENGE_INVALID", "The challenge is missing, expired, consumed or bound to another installation.");
            string licenseId = request.LicenseId;
            if (request.Action == "activate" && !keyIndex.TryGetValue(store.KeyDigest(request.AccessKey), out licenseId!))
                throw new LicenseException("LICENSE_INVALID", "The access key is not valid.");
            LicenseRecord license = store.ReadCommitted(db => db.Licenses.TryGetValue(licenseId, out var item) ? item :
                throw new LicenseException("LICENSE_INVALID", "License not found."));
            RequireActive(license, now);
            if (request.Action == "activate")
            {
                if (license.Devices.TryGetValue(deviceId, out var existing))
                {
                    if (existing.HostBinding != request.HostBinding || existing.PublicKey != request.DevicePublicKey)
                        throw new LicenseException("DEVICE_CHANGED", "Installation binding changed.");
                }
                else
                {
                    if (license.ActivateBefore.HasValue && now >= license.ActivateBefore.Value)
                        throw new LicenseException("ACTIVATION_EXPIRED", "The activation window has ended.");
                    if (license.Devices.Count >= license.MaxDevices)
                        throw new LicenseException("DEVICE_LIMIT", "All installation slots are occupied; contact the owner.");
                    license = store.Change(now, db =>
                    {
                        var current = db.Licenses[licenseId];
                        long? expiry = current.ExpiresAt;
                        if (current.ActivatedAt is null && current.Starts == "activation") expiry = LicenseDurations.Expires(now, current.Unit, current.Amount);
                        var devices = new Dictionary<string, LicensedDevice>(current.Devices, StringComparer.Ordinal)
                        { [deviceId] = new(deviceId, request.DevicePublicKey, request.HostBinding, now) };
                        var updated = current with { ActivatedAt = current.ActivatedAt ?? now, ExpiresAt = expiry, Devices = devices };
                        db.Licenses[licenseId] = updated;
                        db.Audit.Add(new(now, "activate", licenseId, deviceId));
                        return updated;
                    });
                }
            }
            else if (!license.Devices.TryGetValue(deviceId, out var device) || device.HostBinding != request.HostBinding || device.PublicKey != request.DevicePublicKey)
                throw new LicenseException("DEVICE_NOT_ACTIVATED", "This installation is not activated.");
            RequireActive(license, now);
            long until = Math.Min(now + config.LeaseSeconds, license.ExpiresAt ?? long.MaxValue);
            return new(LicenseCrypto.SignLease(new(1, config.Issuer, LicenseCrypto.Product, license.Id, deviceId,
                request.HostBinding, request.ClientNonce, request.Action, now, until, license.ExpiresAt), signer));
        }
    }

    /// <summary>Creates an entitlement exactly once for an issuance UUID and credential fingerprint.</summary>
    /// <param name="request">Owner request whose raw key is not stored in the database.</param>
    /// <returns>Non-secret current overview.</returns>
    public LicenseOverview Issue(IssueLicenseRequest request)
    {
        if (!Guid.TryParseExact(request.Id, "D", out _) || request.Label is null || request.Label.Length > 120 ||
            request.Label.Any(char.IsControl) || request.Starts is not ("issue" or "activation") || request.MaxDevices is < 1 or > 100)
            throw new LicenseException("ISSUE_INVALID", "Invalid issuance parameters.");
        LicenseDurations.Validate(request.Unit, request.Amount);
        string keyDigest = store.KeyDigest(request.AccessKey);
        // The fingerprint includes a credential digest; it never retains the request's plaintext key.
        string fingerprint = LicenseCrypto.Digest(LicenseJson.Write(request with { AccessKey = keyDigest }));
        lock (sync)
        {
            long now = Now();
            LicenseRecord? prior = store.ReadCommitted(db => db.Licenses.GetValueOrDefault(request.Id));
            if (prior is not null)
            {
                if (prior.IssueDigest != fingerprint) throw new LicenseException("IDEMPOTENCY_CONFLICT", "Issuance ID was already used for another request.");
                return Overview(prior, now);
            }
            if (keyIndex.ContainsKey(keyDigest)) throw new LicenseException("KEY_DUPLICATE", "This credential is already assigned.");
            if (request.ActivateBefore.HasValue && request.ActivateBefore.Value <= now)
                throw new LicenseException("ACTIVATION_WINDOW", "Activation deadline must be in the future.");
            long? expiry = request.Starts == "issue" ? LicenseDurations.Expires(now, request.Unit, request.Amount) : null;
            var record = new LicenseRecord(request.Id, keyDigest, fingerprint, request.Label, request.Unit, request.Amount,
                request.Starts, now, null, expiry, request.ActivateBefore, request.MaxDevices, "active", new());
            store.Change(now, db => { db.Licenses.Add(record.Id, record); db.Audit.Add(new(now, "issue", record.Id, "")); return 0; });
            keyIndex.Add(keyDigest, record.Id);
            return Overview(record, now);
        }
    }

    /// <summary>Applies an idempotent owner mutation without allowing revocation to be reversed.</summary>
    /// <param name="request">Requested administrative action.</param>
    /// <returns>Updated overview.</returns>
    public LicenseOverview Change(ChangeLicenseRequest request)
    {
        if (!Guid.TryParseExact(request.RequestId, "D", out _) || !Guid.TryParseExact(request.LicenseId, "D", out _) ||
            request.Action is not ("suspend" or "resume" or "revoke" or "extend" or "permanent" or "reset-device"))
            throw new LicenseException("CHANGE_INVALID", "Invalid administrative mutation.");
        if (request.Action == "extend")
        {
            LicenseDurations.Validate(request.Unit, request.Amount);
            if (request.Unit == "permanent") throw new LicenseException("CHANGE_INVALID", "Use the permanent action for perpetual access.");
        }
        else if (request.Unit != "" || request.Amount != 0)
            throw new LicenseException("CHANGE_INVALID", "Unexpected duration fields.");
        if (request.Action == "reset-device") LicenseCrypto.ValidateDigest(request.DeviceId);
        else if (request.DeviceId != "") throw new LicenseException("CHANGE_INVALID", "Unexpected device identifier.");
        string fingerprint = LicenseCrypto.Digest(LicenseJson.Write(request));
        lock (sync)
        {
            long now = Now();
            string? prior = store.ReadCommitted(db => db.Requests.GetValueOrDefault(request.RequestId));
            if (prior is not null)
            {
                if (prior != fingerprint) throw new LicenseException("IDEMPOTENCY_CONFLICT", "Mutation ID was reused with different parameters.");
                return Get(request.LicenseId);
            }
            return store.Change(now, db =>
            {
                if (!db.Licenses.TryGetValue(request.LicenseId, out var record)) throw new LicenseException("LICENSE_INVALID", "License not found.");
                if (record.Status == "revoked" && request.Action != "revoke")
                    throw new LicenseException("LICENSE_REVOKED", "Revocation is permanent. Issue a new license instead.");
                switch (request.Action)
                {
                    case "revoke": record = record with { Status = "revoked" }; break;
                    case "suspend": record = record with { Status = "suspended" }; break;
                    case "resume": record = record with { Status = "active" }; break;
                    case "permanent": record = record with { Unit = "permanent", Amount = 0, ExpiresAt = null }; break;
                    case "extend":
                        if (record.Unit == "permanent" || (record.Starts == "activation" && record.ActivatedAt is null))
                            throw new LicenseException("EXTENSION_STATE", "Only an already-started timed license can be extended.");
                        record = record with { ExpiresAt = LicenseDurations.Expires(Math.Max(now, record.ExpiresAt ?? now), request.Unit, request.Amount) };
                        break;
                    case "reset-device":
                        var devices = new Dictionary<string, LicensedDevice>(record.Devices, StringComparer.Ordinal);
                        if (!devices.Remove(request.DeviceId)) throw new LicenseException("DEVICE_UNKNOWN", "Installation not found.");
                        record = record with { Devices = devices };
                        break;
                }
                db.Licenses[record.Id] = record;
                db.Requests[request.RequestId] = fingerprint;
                db.Audit.Add(new(now, request.Action, record.Id, request.DeviceId));
                return Overview(record, now);
            });
        }
    }

    /// <summary>Checks independent administrative bearer tokens using constant-time digest comparison.</summary>
    /// <param name="token">Untrusted token from the separate loopback admin listener.</param>
    /// <returns>True only for the configured owner credential.</returns>
    public bool IsOwner(string token)
    {
        if (token.Length != 43) return false;
        byte[] digest = SHA256.HashData(System.Text.Encoding.ASCII.GetBytes(token));
        return CryptographicOperations.FixedTimeEquals(digest, Convert.FromHexString(config.AdminTokenHash));
    }

    /// <summary>Returns a bounded administrative listing without raw credentials.</summary>
    /// <returns>Current overviews ordered by issue time then ID.</returns>
    public LicenseOverview[] List()
    {
        long now = Now();
        return store.ReadCommitted(db => db.Licenses.Values.OrderBy(x => x.CreatedAt).ThenBy(x => x.Id, StringComparer.Ordinal)
            .Select(x => Overview(x, now)).ToArray());
    }

    /// <summary>Returns one non-secret overview using one durable timestamp for the decision.</summary>
    /// <param name="id">License UUID.</param>
    /// <returns>Current entitlement state.</returns>
    public LicenseOverview Get(string id)
    {
        long now = Now();
        return store.ReadCommitted(db => db.Licenses.TryGetValue(id, out var record)
            ? Overview(record, now) : throw new LicenseException("LICENSE_INVALID", "License not found."));
    }

    /// <summary>Exports the owner audit trail; reads never include credentials or private key material.</summary>
    /// <returns>Detached audit array.</returns>
    public LicenseAudit[] Audit() => store.ReadCommitted(db => db.Audit.ToArray());

    /// <summary>Computes an owner overview without mutating authoritative state.</summary>
    /// <param name="record">Committed entitlement.</param>
    /// <param name="now">Current authoritative time.</param>
    /// <returns>Non-secret view with effective status.</returns>
    private static LicenseOverview Overview(LicenseRecord record, long now)
    {
        string status = record.Status;
        if (status == "active") status = record.ExpiresAt.HasValue && now >= record.ExpiresAt.Value ? "expired"
            : record.Starts == "activation" && record.ActivatedAt is null ? "pending" : "active";
        return new(record.Id, record.Label, status, record.CreatedAt, record.ActivatedAt, record.ExpiresAt,
            record.Unit == "permanent", record.MaxDevices, record.Devices.Values.OrderBy(x => x.Id, StringComparer.Ordinal).ToArray());
    }

    /// <summary>Rejects revoked, suspended and expired licenses using an exclusive expiry boundary.</summary>
    /// <param name="record">Entitlement.</param>
    /// <param name="now">Server UTC.</param>
    private static void RequireActive(LicenseRecord record, long now)
    {
        if (record.Status == "revoked") throw new LicenseException("LICENSE_REVOKED", "Access was revoked by the owner.");
        if (record.Status == "suspended") throw new LicenseException("LICENSE_SUSPENDED", "Access is suspended by the owner.");
        if (record.ExpiresAt.HasValue && now >= record.ExpiresAt.Value) throw new LicenseException("LICENSE_EXPIRED", "The license has expired.");
    }

    /// <summary>Advances and persists the authoritative timeline before any license decision is returned.</summary>
    /// <returns>UTC seconds protected against both live rollback and a restart behind the last observed time.</returns>
    private long Now()
    {
        // Ordering matters: concurrent public queries must not persist an older observation after a newer one.
        lock (sync)
        {
            long now = clock.Now();
            checkpoint.Observe(now);
            return now;
        }
    }

    /// <summary>Closes the store and clears private-key handles after all HTTP requests have stopped.</summary>
    public void Dispose() { lock (sync) { checkpoint.Dispose(); store.Dispose(); signer.Dispose(); } }
}
