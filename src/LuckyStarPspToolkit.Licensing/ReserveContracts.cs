using System.Security.Cryptography;
using System.Text;

namespace LuckyStarPspToolkit.Licensing;

/// <summary>A proof-bearing request for a bounded offline grant; the ordinary one-use check proof authenticates the installation.</summary>
/// <param name="GrantId">Owner-created reserve permission UUID, never a universal master credential.</param>
/// <param name="Request">Fresh check challenge and installation signature.</param>
public sealed record ReserveRefreshRequest(string GrantId, LicenseRequest Request);

/// <summary>Server-signed offline execution permission scoped to one product, license, host and installation.</summary>
/// <param name="Schema">Fixed schema one.</param>
/// <param name="Issuer">Authority UUID.</param>
/// <param name="ProductId">Exact product audience.</param>
/// <param name="GrantId">Revocable reserve permission UUID.</param>
/// <param name="LicenseId">Underlying entitlement UUID.</param>
/// <param name="DeviceId">Installation public-key digest.</param>
/// <param name="HostBinding">Product-scoped OS identity digest.</param>
/// <param name="PolicyEpoch">Increasing generation; disabling or rotating reserve mode retires all earlier generations.</param>
/// <param name="ClientNonce">Fresh nonce binding the response to an online exchange.</param>
/// <param name="ServerNow">Signing time in authoritative UTC seconds.</param>
/// <param name="ValidUntil">Exclusive offline deadline, at most seven days after ServerNow and never beyond entitlement expiry.</param>
public sealed record ReserveLease(int Schema, string Issuer, string ProductId, string GrantId, string LicenseId,
    string DeviceId, string HostBinding, long PolicyEpoch, string ClientNonce, long ServerNow, long ValidUntil);

/// <summary>Domain-separated signing and verification of bounded offline leases; ordinary LSP1 grants cannot be substituted.</summary>
public static class ReserveCrypto
{
    /// <summary>Hard client and server ceiling, in seconds; perpetual licenses do not change this limit.</summary>
    public const int MaximumSeconds = 7 * 24 * 60 * 60;

    /// <summary>Signs an offline lease using a server-owned P-256 key.</summary>
    /// <param name="lease">Validated claims.</param>
    /// <param name="signer">Private authority signer, never sent to customers.</param>
    /// <returns>Fixed-format LSPR1 token.</returns>
    public static string Sign(ReserveLease lease, ECDsa signer)
    {
        string keyId = LicenseCrypto.Digest(signer.ExportSubjectPublicKeyInfo())[..16];
        string prefix = "LSPR1." + keyId + "." + LicenseCrypto.Encode(LicenseJson.Write(lease));
        return prefix + "." + LicenseCrypto.Encode(signer.SignData(Encoding.ASCII.GetBytes(prefix),
            HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
    }

    /// <summary>Verifies a complete token before parsing its claims and checks all immutable scope and duration constraints.</summary>
    /// <param name="token">Untrusted compact token, bounded to eight KiB.</param>
    /// <param name="trust">Embedded public issuer profile.</param>
    /// <param name="identity">Current installation and host.</param>
    /// <param name="licenseId">Activated underlying license.</param>
    /// <param name="grantId">Expected reserve permission.</param>
    /// <param name="nonce">Expected exchange nonce; null only when revalidating a previously authenticated local cache.</param>
    /// <returns>Verified claims; validity against elapsed local time is checked separately.</returns>
    public static ReserveLease Verify(string token, LicenseTrust trust, DeviceIdentity identity, string licenseId,
        string grantId, string? nonce = null)
    {
        if (token is null || token.Length > 8192) throw new LicenseException("RESERVE_TOKEN", "Invalid reserve token.");
        string[] parts = token.Split('.');
        if (parts.Length != 4 || parts[0] != "LSPR1" || !trust.PublicKeys.TryGetValue(parts[1], out string? publicKey))
            throw new LicenseException("RESERVE_SIGNATURE", "Unknown reserve signing key or token type.");
        using ECDsa verifier = LicenseCrypto.ImportPublic(publicKey);
        if (!verifier.VerifyData(Encoding.ASCII.GetBytes(string.Join('.', parts[..3])),
            LicenseCrypto.Decode(parts[3], 64, 64), HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation))
            throw new LicenseException("RESERVE_SIGNATURE", "Reserve signature is invalid.");
        var lease = LicenseJson.Read<ReserveLease>(LicenseCrypto.Decode(parts[2], 4096));
        if (lease.Schema != 1 || lease.Issuer != trust.Issuer || lease.ProductId != trust.ProductId ||
            lease.GrantId != grantId || lease.LicenseId != licenseId || lease.DeviceId != identity.Id ||
            lease.HostBinding != identity.HostBinding || lease.PolicyEpoch < 1 ||
            !Guid.TryParseExact(lease.GrantId, "D", out _) || !Guid.TryParseExact(lease.LicenseId, "D", out _) ||
            (nonce is not null && nonce != lease.ClientNonce))
            throw new LicenseException("RESERVE_BINDING", "Reserve permission belongs to another request or installation.");
        _ = LicenseCrypto.Decode(lease.ClientNonce, 32, 32);
        if (lease.ServerNow < 0 || lease.ValidUntil <= lease.ServerNow || lease.ValidUntil > 253402300799L ||
            lease.ValidUntil - lease.ServerNow > MaximumSeconds)
            throw new LicenseException("RESERVE_TIME", "Reserve deadline exceeds the seven-day bound.");
        return lease;
    }
}
