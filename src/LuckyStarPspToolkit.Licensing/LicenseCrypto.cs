using System.Security.Cryptography;
using System.Text;

namespace LuckyStarPspToolkit.Licensing;

/// <summary>Fixed P-256/SHA-256 protocol primitives. No caller-selected algorithm or executable-supplied private authority key.</summary>
public static class LicenseCrypto
{
    /// <summary>The only accepted product in this distribution.</summary>
    public const string Product = "lucky-star-psp-toolkit";
    /// <summary>Maximum grant lifetime; bounds continued operation after a server outage or revocation.</summary>
    public const int MaximumLeaseSeconds = 120;

    /// <summary>Encodes canonical base64url without padding.</summary>
    /// <param name="bytes">Bytes to encode.</param>
    /// <returns>ASCII URL-safe encoding.</returns>
    public static string Encode(ReadOnlySpan<byte> bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>Decodes canonical base64url with an allocation bound and optional exact size.</summary>
    /// <param name="value">Unpadded base64url.</param>
    /// <param name="limit">Maximum decoded size.</param>
    /// <param name="exact">Required decoded size, or zero for variable length.</param>
    /// <returns>Decoded bytes.</returns>
    public static byte[] Decode(string value, int limit, int exact = 0)
    {
        if (string.IsNullOrEmpty(value) || value.Length > (limit * 4 + 2) / 3 ||
            value.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-' && c != '_'))
            throw new LicenseException("ENCODING_INVALID", "Invalid licensing encoding.");
        try
        {
            string padded = value.Replace('-', '+').Replace('_', '/');
            padded += new string('=', (4 - padded.Length % 4) % 4);
            byte[] bytes = Convert.FromBase64String(padded);
            if (bytes.Length > limit || (exact != 0 && bytes.Length != exact) || Encode(bytes) != value)
                throw new LicenseException("ENCODING_INVALID", "Non-canonical licensing encoding.");
            return bytes;
        }
        catch (FormatException) { throw new LicenseException("ENCODING_INVALID", "Invalid licensing encoding."); }
    }

    /// <summary>Computes the lowercase SHA-256 identity of public, non-secret data.</summary>
    /// <param name="data">Canonical bytes to hash.</param>
    /// <returns>64 hexadecimal characters.</returns>
    public static string Digest(ReadOnlySpan<byte> data) => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

    /// <summary>Creates a 256-bit unpredictable nonce using the operating-system random generator.</summary>
    /// <returns>Canonical 43-character base64url nonce.</returns>
    public static string Nonce() => Encode(RandomNumberGenerator.GetBytes(32));

    /// <summary>Generates the separately delivered 256-bit access credential.</summary>
    /// <returns>Case-sensitive access key; this value is never embedded in the customer binary.</returns>
    public static string NewAccessKey() => "LSP-" + Nonce();

    /// <summary>Checks a key's exact format without changing case or collapsing internal whitespace.</summary>
    /// <param name="key">Access key after removal of surrounding whitespace by the UI.</param>
    public static void ValidateAccessKey(string key)
    {
        if (key is null || key.Length != 47 || !key.StartsWith("LSP-", StringComparison.Ordinal))
            throw new LicenseException("LICENSE_INVALID", "The access key is not valid.");
        _ = Decode(key[4..], 32, 32);
    }

    /// <summary>Imports only a complete NIST P-256 public key, rejecting trailing bytes and other curves.</summary>
    /// <param name="base64">Base64 SubjectPublicKeyInfo, at most 160 characters.</param>
    /// <returns>Disposable verifier.</returns>
    public static ECDsa ImportPublic(string base64)
    {
        if (string.IsNullOrEmpty(base64) || base64.Length > 160) throw new LicenseException("KEY_INVALID", "Invalid public key.");
        ECDsa key = ECDsa.Create();
        try
        {
            byte[] bytes = Convert.FromBase64String(base64);
            key.ImportSubjectPublicKeyInfo(bytes, out int consumed);
            if (consumed != bytes.Length || key.KeySize != 256 || key.ExportParameters(false).Curve.Oid.Value != "1.2.840.10045.3.1.7" ||
                Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()) != base64)
                throw new LicenseException("KEY_INVALID", "Only canonical P-256 public keys are accepted.");
            return key;
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException or LicenseException)
        {
            key.Dispose();
            throw new LicenseException("KEY_INVALID", "Invalid P-256 public key.");
        }
    }

    /// <summary>Computes a device ID only from its validated canonical public key.</summary>
    /// <param name="publicKey">Canonical base64 SubjectPublicKeyInfo.</param>
    /// <returns>Device identity digest.</returns>
    public static string DeviceId(string publicKey)
    {
        using ECDsa key = ImportPublic(publicKey);
        return Digest(key.ExportSubjectPublicKeyInfo());
    }

    /// <summary>Validates a SHA-256 hexadecimal binding without accepting uppercase or special characters.</summary>
    /// <param name="value">Digest string.</param>
    public static void ValidateDigest(string value)
    {
        if (value is null || value.Length != 64 || value.Any(c => !char.IsAsciiHexDigit(c) || char.IsUpper(c)))
            throw new LicenseException("BINDING_INVALID", "Invalid installation binding.");
    }

    /// <summary>Builds a domain-separated, unambiguous possession-proof transcript; no JSON canonicalization is assumed.</summary>
    /// <param name="request">Validated request fields, with Proof ignored.</param>
    /// <returns>UTF-8 transcript including a digest, not the plaintext value, of the activation credential.</returns>
    public static byte[] Transcript(LicenseRequest request)
    {
        if (request.ProductId != Product || request.Action is not ("activate" or "check"))
            throw new LicenseException("REQUEST_INVALID", "Unknown product or operation.");
        _ = Decode(request.Challenge, 32, 32);
        _ = Decode(request.ClientNonce, 32, 32);
        _ = DeviceId(request.DevicePublicKey);
        ValidateDigest(request.HostBinding);
        if (request.Action == "activate")
        {
            if (request.LicenseId != "") throw new LicenseException("REQUEST_INVALID", "Activation must not select a license ID.");
            ValidateAccessKey(request.AccessKey);
        }
        else if (!Guid.TryParseExact(request.LicenseId, "D", out _) || request.AccessKey != "")
            throw new LicenseException("REQUEST_INVALID", "Invalid check request.");
        return Encoding.UTF8.GetBytes(string.Join('\n', "LSP-POSSESSION-v1", request.ProductId,
            request.Action, request.Challenge, request.ClientNonce, request.LicenseId,
            Digest(Encoding.UTF8.GetBytes(request.AccessKey)), request.DevicePublicKey, request.HostBinding));
    }

    /// <summary>Signs an execution grant with the authority's private key using one fixed signature format.</summary>
    /// <param name="lease">Fully validated claims.</param>
    /// <param name="key">Authority private key, never distributed to a customer.</param>
    /// <returns>Compact versioned token.</returns>
    public static string SignLease(ExecutionLease lease, ECDsa key)
    {
        string kid = Digest(key.ExportSubjectPublicKeyInfo())[..16];
        string input = "LSP1." + kid + "." + Encode(LicenseJson.Write(lease));
        return input + "." + Encode(key.SignData(Encoding.ASCII.GetBytes(input), HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
    }

    /// <summary>Authenticates a grant before reading claims, then binds it to the issuer, product, installation and fresh request.</summary>
    /// <param name="token">Untrusted compact token, bounded to 8192 characters.</param>
    /// <param name="trust">Embedded public trust configuration.</param>
    /// <param name="request">The exact outgoing request.</param>
    /// <returns>Verified short-lived lease. Its execution lifetime must be tracked using monotonic time.</returns>
    public static ExecutionLease VerifyLease(string token, LicenseTrust trust, LicenseRequest request)
    {
        if (token is null || token.Length > 8192) throw new LicenseException("LEASE_INVALID", "Invalid execution grant.");
        string[] parts = token.Split('.');
        if (parts.Length != 4 || parts[0] != "LSP1" || !trust.PublicKeys.TryGetValue(parts[1], out string? publicKey))
            throw new LicenseException("LEASE_UNTRUSTED", "Unknown license signing authority.");
        using ECDsa verifier = ImportPublic(publicKey);
        byte[] signature = Decode(parts[3], 64, 64);
        if (!verifier.VerifyData(Encoding.ASCII.GetBytes(string.Join('.', parts.Take(3))), signature,
            HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation))
            throw new LicenseException("LEASE_SIGNATURE", "The execution grant signature is invalid.");
        ExecutionLease lease = LicenseJson.Read<ExecutionLease>(Decode(parts[2], 4096));
        if (lease.Schema != 1 || lease.Issuer != trust.Issuer || lease.ProductId != trust.ProductId ||
            lease.DeviceId != DeviceId(request.DevicePublicKey) || lease.HostBinding != request.HostBinding ||
            lease.ClientNonce != request.ClientNonce || lease.Action != request.Action ||
            !Guid.TryParseExact(lease.LicenseId, "D", out _) ||
            (request.Action == "check" && lease.LicenseId != request.LicenseId))
            throw new LicenseException("LEASE_BINDING", "The grant does not match this request or installation.");
        if (lease.ServerNow < 0 || lease.ValidUntil <= lease.ServerNow ||
            lease.ValidUntil - lease.ServerNow > MaximumLeaseSeconds ||
            (lease.EntitlementExpires.HasValue && lease.ValidUntil > lease.EntitlementExpires.Value))
            throw new LicenseException("LEASE_TIME", "Invalid execution grant deadline.");
        return lease;
    }

    /// <summary>Validates embedded trust and HTTPS policy; no runtime flag can override production trust.</summary>
    /// <param name="trust">Build-time public trust profile.</param>
    /// <returns>Absolute validated service URI.</returns>
    public static Uri ValidateTrust(LicenseTrust trust)
    {
        if (trust.Schema != 1 || trust.ProductId != Product || !Guid.TryParseExact(trust.Issuer, "D", out _) ||
            trust.PublicKeys is null || trust.PublicKeys.Count is < 1 or > 3)
            throw new LicenseException("LICENSE_NOT_CONFIGURED", "The executable has no valid vendor trust profile.");
        if (!Uri.TryCreate(trust.ServerUrl, UriKind.Absolute, out Uri? uri) || uri.UserInfo != "" ||
            uri.Query != "" || uri.Fragment != "" || uri.AbsolutePath != "/")
            throw new LicenseException("LICENSE_ENDPOINT", "Invalid licensing service address.");
        bool testLoopback = trust.DevelopmentLoopback && uri.Scheme == "http" &&
            System.Net.IPAddress.TryParse(uri.Host, out var ip) && System.Net.IPAddress.IsLoopback(ip);
        if (uri.Scheme != "https" && !testLoopback)
            throw new LicenseException("LICENSE_HTTPS_REQUIRED", "Licensing requires HTTPS.");
        foreach (var entry in trust.PublicKeys)
        {
            using ECDsa key = ImportPublic(entry.Value);
            if (entry.Key != Digest(key.ExportSubjectPublicKeyInfo())[..16])
                throw new LicenseException("LICENSE_KEY_ID", "Signing key ID does not match its public key.");
        }
        return uri;
    }
}
