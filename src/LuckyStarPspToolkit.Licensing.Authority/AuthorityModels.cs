namespace LuckyStarPspToolkit.Licensing.Authority;

/// <summary>Owner-only configuration. The signing key is encrypted, and administrator requests are authenticated by a token digest.</summary>
/// <param name="Schema">Configuration version.</param>
/// <param name="Issuer">Unique authority ID.</param>
/// <param name="ServerUrl">Customer-facing HTTPS endpoint.</param>
/// <param name="EncryptedPkcs8">Password-encrypted authority P-256 PKCS#8.</param>
/// <param name="PublicKey">Canonical public key.</param>
/// <param name="Pepper">Random HMAC secret for access keys and database integrity.</param>
/// <param name="AdminTokenHash">SHA-256 of the independent owner API token.</param>
/// <param name="LeaseSeconds">Execution lease length, 15 through 120 seconds.</param>
public sealed record AuthorityConfiguration(int Schema, string Issuer, string ServerUrl, string EncryptedPkcs8,
    string PublicKey, string Pepper, string AdminTokenHash, int LeaseSeconds = 90);

/// <summary>Owner credential file: never put this in a customer package or public repository.</summary>
/// <param name="AdminUrl">Local admin listener, normally accessed on the VPS or through an SSH tunnel.</param>
/// <param name="Token">Independent 256-bit administrative credential.</param>
public sealed record OwnerConnection(string AdminUrl, string Token);

/// <summary>Idempotent issue request; saved privately by the owner before transmission so a dropped reply never loses the access key.</summary>
/// <param name="Id">Client-generated issuance UUID, reused for retries.</param>
/// <param name="AccessKey">Random credential delivered separately to the customer.</param>
/// <param name="Label">Optional human label, at most 120 characters.</param>
/// <param name="Unit">hours, days, years or permanent.</param>
/// <param name="Amount">Positive duration, or zero for perpetual entitlement.</param>
/// <param name="Starts">issue or activation.</param>
/// <param name="MaxDevices">Allowed installation slots.</param>
/// <param name="ActivateBefore">Optional exclusive UTC activation deadline in Unix seconds.</param>
public sealed record IssueLicenseRequest(string Id, string AccessKey, string Label, string Unit, int Amount,
    string Starts, int MaxDevices = 1, long? ActivateBefore = null);

/// <summary>Administrator mutation. Revocation is irreversible; reset-device invalidates that installation's future checks.</summary>
/// <param name="RequestId">Idempotency UUID reused on retry.</param>
/// <param name="LicenseId">License selected by UUID, not an access key.</param>
/// <param name="Action">suspend, resume, revoke, extend, permanent or reset-device.</param>
/// <param name="Unit">Extension duration unit.</param>
/// <param name="Amount">Extension amount.</param>
/// <param name="DeviceId">Exact installation to release for reset-device.</param>
public sealed record ChangeLicenseRequest(string RequestId, string LicenseId, string Action,
    string Unit = "", int Amount = 0, string DeviceId = "");

/// <summary>One explicitly activated installation.</summary>
/// <param name="Id">SHA-256 of the public key.</param>
/// <param name="PublicKey">P-256 public key.</param>
/// <param name="HostBinding">Hashed OS-installation identity.</param>
/// <param name="ActivatedAt">First activation time in UTC Unix seconds.</param>
public sealed record LicensedDevice(string Id, string PublicKey, string HostBinding, long ActivatedAt);

/// <summary>Private authoritative entitlement. Only key digests are stored, never raw access credentials.</summary>
/// <param name="Id">Stable license ID.</param>
/// <param name="KeyDigest">Server-peppered HMAC of the activation key.</param>
/// <param name="IssueDigest">Fingerprint of the issuance request for idempotency.</param>
/// <param name="Label">Owner label.</param>
/// <param name="Unit">Original duration unit.</param>
/// <param name="Amount">Original duration.</param>
/// <param name="Starts">Original starting policy.</param>
/// <param name="CreatedAt">Issue time.</param>
/// <param name="ActivatedAt">First activation, retained after resetting devices.</param>
/// <param name="ExpiresAt">Exclusive expiry, or null before timed activation/perpetual entitlement.</param>
/// <param name="ActivateBefore">Optional first/new-device activation deadline.</param>
/// <param name="MaxDevices">Device limit.</param>
/// <param name="Status">active, suspended or revoked.</param>
/// <param name="Devices">Active device slots.</param>
public sealed record LicenseRecord(string Id, string KeyDigest, string IssueDigest, string Label,
    string Unit, int Amount, string Starts, long CreatedAt, long? ActivatedAt, long? ExpiresAt,
    long? ActivateBefore, int MaxDevices, string Status, Dictionary<string, LicensedDevice> Devices);

/// <summary>Audit entry without access keys, passwords, raw hardware identifiers or request bodies.</summary>
/// <param name="At">UTC Unix timestamp.</param>
/// <param name="Action">Administrative or activation event.</param>
/// <param name="LicenseId">Affected license.</param>
/// <param name="DeviceId">Affected installation, if any.</param>
public sealed record LicenseAudit(long At, string Action, string LicenseId, string DeviceId);

/// <summary>Authenticated single-writer snapshot. Periodic lease checks do not rewrite entitlements; a separate small server-time checkpoint protects restarts.</summary>
/// <param name="Schema">One for legacy stores; two requires the clock checkpoint; three adds opt-in reserve policy and grants.</param>
/// <param name="Issuer">Authority owning the database.</param>
/// <param name="Revision">Monotonically increasing committed write number.</param>
/// <param name="LastWriteUtc">Last commit time, guarding accidental backward-clock startup.</param>
/// <param name="Licenses">Indexed entitlements.</param>
/// <param name="Requests">Mutation UUID to request digest for exactly-once administrative retries.</param>
/// <param name="Audit">Bounded audit history; mutation stops rather than silently dropping events when full.</param>
public sealed record LicenseDatabase(int Schema, string Issuer, long Revision, long LastWriteUtc,
    Dictionary<string, LicenseRecord> Licenses, Dictionary<string, string> Requests, List<LicenseAudit> Audit)
{
    /// <summary>Opt-in global reserve mode; false after migration or initialization.</summary>
    public bool ReserveEnabled { get; set; }
    /// <summary>Increasing reserve generation. A disabled/re-enabled generation never resurrects older grants.</summary>
    public long ReserveEpoch { get; set; }
    /// <summary>Bounded reserve allow-list; each immutable entry is tied to one license and installation.</summary>
    public Dictionary<string, ReserveGrant> ReserveGrants { get; init; } = new(StringComparer.Ordinal);
}

/// <summary>On-disk envelope; HMAC is verified before the database payload is parsed.</summary>
/// <param name="Schema">Envelope version.</param>
/// <param name="Payload">Base64 serialized database.</param>
/// <param name="Mac">Hexadecimal HMAC-SHA256.</param>
internal sealed record DatabaseEnvelope(int Schema, string Payload, string Mac);

/// <summary>Non-secret owner view with computed pending/expired state.</summary>
/// <param name="Id">License ID.</param>
/// <param name="Label">Owner label.</param>
/// <param name="Status">active, pending, expired, suspended or revoked.</param>
/// <param name="CreatedAt">Issue time.</param>
/// <param name="ActivatedAt">First activation time.</param>
/// <param name="ExpiresAt">Exclusive license expiry.</param>
/// <param name="Permanent">Whether the entitlement has no expiry.</param>
/// <param name="MaxDevices">Installation limit.</param>
/// <param name="Devices">Assigned installations; no private key is present.</param>
public sealed record LicenseOverview(string Id, string Label, string Status, long CreatedAt, long? ActivatedAt,
    long? ExpiresAt, bool Permanent, int MaxDevices, LicensedDevice[] Devices);
