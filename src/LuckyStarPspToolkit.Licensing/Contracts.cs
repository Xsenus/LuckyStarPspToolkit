namespace LuckyStarPspToolkit.Licensing;

/// <summary>A controlled licensing failure. Codes, never access keys or private material, cross the CLI/API boundary.</summary>
public sealed class LicenseException : Exception
{
    /// <summary>The stable machine-readable reason for refusing access.</summary>
    public string Code { get; }
    /// <summary>Creates a licensing error safe to return to a client.</summary>
    /// <param name="code">Stable uppercase error identifier.</param>
    /// <param name="message">Non-secret diagnostic text.</param>
    public LicenseException(string code, string message) : base(message) => Code = code;
}

/// <summary>Immutable public trust anchors embedded into a customer executable at build time.</summary>
/// <param name="Schema">Must equal one.</param>
/// <param name="ProductId">Exact licensed product, preventing cross-product grants.</param>
/// <param name="Issuer">Exact authority identifier, generated during owner initialization.</param>
/// <param name="ServerUrl">HTTPS public service URL. Plain HTTP is limited to explicitly marked loopback test builds.</param>
/// <param name="PublicKeys">Key ID to base64 SubjectPublicKeyInfo; contains no private key.</param>
/// <param name="DevelopmentLoopback">Permits only literal loopback HTTP for isolated tests; production packager rejects it.</param>
public sealed record LicenseTrust(int Schema, string ProductId, string Issuer, string ServerUrl,
    Dictionary<string, string> PublicKeys, bool DevelopmentLoopback = false);

/// <summary>Binding of a fresh server challenge to one client installation and operation.</summary>
/// <param name="ProductId">Requested product identifier.</param>
/// <param name="Action">Exactly activate or check.</param>
/// <param name="DevicePublicKey">P-256 public key in base64 SubjectPublicKeyInfo format.</param>
/// <param name="HostBinding">SHA-256 of a product-scoped operating-system installation identifier.</param>
public sealed record ChallengeRequest(string ProductId, string Action, string DevicePublicKey, string HostBinding);

/// <summary>A random one-use challenge, held by the server for no more than sixty seconds.</summary>
/// <param name="Challenge">Cryptographically random base64url nonce.</param>
public sealed record ChallengeResponse(string Challenge);

/// <summary>Proof of installation-key possession and, on first activation, possession of the separately delivered access key.</summary>
/// <param name="ProductId">Exact product identifier.</param>
/// <param name="Action">activate or check.</param>
/// <param name="DevicePublicKey">Installation public key.</param>
/// <param name="HostBinding">Product-scoped host binding digest.</param>
/// <param name="Challenge">Fresh server nonce, consumed once even across concurrent requests.</param>
/// <param name="ClientNonce">Fresh client nonce, binding the signed response to this request.</param>
/// <param name="LicenseId">Previously activated license ID; empty for activation.</param>
/// <param name="AccessKey">Separately delivered random key; empty for ordinary checks.</param>
/// <param name="Proof">P-256 SHA-256 signature of the fixed protocol transcript.</param>
public sealed record LicenseRequest(string ProductId, string Action, string DevicePublicKey, string HostBinding,
    string Challenge, string ClientNonce, string LicenseId, string AccessKey, string Proof);

/// <summary>A short-lived, server-signed execution grant. Permanent entitlement does not mean permanent offline execution.</summary>
/// <param name="Schema">Must equal one.</param>
/// <param name="Issuer">Signing authority identifier.</param>
/// <param name="ProductId">Exact audience/product.</param>
/// <param name="LicenseId">Authorized license.</param>
/// <param name="DeviceId">SHA-256 of the installation public key.</param>
/// <param name="HostBinding">Bound host digest.</param>
/// <param name="ClientNonce">Nonce of the request being answered.</param>
/// <param name="Action">Operation being authorized.</param>
/// <param name="ServerNow">Authoritative UTC Unix second at signing time.</param>
/// <param name="ValidUntil">Exclusive UTC lease deadline, no more than 120 seconds after signing.</param>
/// <param name="EntitlementExpires">Exclusive license deadline; null for perpetual entitlement.</param>
public sealed record ExecutionLease(int Schema, string Issuer, string ProductId, string LicenseId,
    string DeviceId, string HostBinding, string ClientNonce, string Action,
    long ServerNow, long ValidUntil, long? EntitlementExpires);

/// <summary>Signed response envelope; clients verify the signature before reading claims.</summary>
/// <param name="Token">Versioned, fixed-algorithm LSP1 compact signed token.</param>
public sealed record LeaseResponse(string Token);

/// <summary>Public, non-secret refusal body.</summary>
/// <param name="Code">Stable error identifier.</param>
/// <param name="Message">Sanitized explanation.</param>
public sealed record LicenseError(string Code, string Message);

/// <summary>Persisted activation contains only identifiers, never the raw access key or a reusable execution lease.</summary>
/// <param name="Schema">Storage schema.</param>
/// <param name="Issuer">Authority scope.</param>
/// <param name="ProductId">Product scope.</param>
/// <param name="LicenseId">Activated license.</param>
/// <param name="DeviceId">Installation identity digest.</param>
public sealed record ActivationState(int Schema, string Issuer, string ProductId, string LicenseId, string DeviceId);
