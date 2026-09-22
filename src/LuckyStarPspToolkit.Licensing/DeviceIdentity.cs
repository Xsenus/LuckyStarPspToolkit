using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace LuckyStarPspToolkit.Licensing;

/// <summary>Installation key storage descriptor; activation keys and authority secrets are never retained here.</summary>
/// <param name="Schema">Storage schema.</param>
/// <param name="CngName">Non-exportable Windows CNG key name, or empty on Unix.</param>
/// <param name="UnixPkcs8">Unix software private key protected by file permissions; empty on Windows.</param>
/// <param name="PublicKey">Canonical installation public key.</param>
/// <param name="HostBinding">Product-scoped OS-installation binding.</param>
internal sealed record DeviceKeyState(int Schema, string CngName, string UnixPkcs8, string PublicKey, string HostBinding);

/// <summary>Proof-of-possession installation identity. Windows uses a non-exportable user CNG key; Unix uses a private 0600 file.</summary>
public sealed class DeviceIdentity : IDisposable
{
    /// <summary>Private signing key; never leaves this identity as a protocol field.</summary>
    private readonly ECDsa key;
    /// <summary>Canonical installation public key sent to the license server.</summary>
    public string PublicKey { get; }
    /// <summary>Product-scoped, hashed installation identifier; no raw machine identifier is sent.</summary>
    public string HostBinding { get; }
    /// <summary>Stable public-key fingerprint used for device limits.</summary>
    public string Id { get; }

    /// <summary>Creates an in-memory identity from an owned private key.</summary>
    /// <param name="key">Owned P-256 signing key.</param>
    /// <param name="hostBinding">Validated binding digest.</param>
    public DeviceIdentity(ECDsa key, string hostBinding)
    {
        LicenseCrypto.ValidateDigest(hostBinding);
        this.key = key;
        PublicKey = Convert.ToBase64String(key.ExportSubjectPublicKeyInfo());
        Id = LicenseCrypto.DeviceId(PublicKey);
        HostBinding = hostBinding;
    }

    /// <summary>Opens or initializes one identity while the caller holds the installation state lock.</summary>
    /// <param name="directory">Private installation state directory.</param>
    /// <returns>Disposable identity matching both the stored public key and current host.</returns>
    public static DeviceIdentity Open(string directory)
    {
        PrivateFiles.Directory(directory);
        string path = Path.Combine(directory, "device.json");
        string binding = ReadHostBinding();
        if (File.Exists(path))
        {
            var state = LicenseJson.Read<DeviceKeyState>(PrivateFiles.Read(path));
            if (state.Schema != 1 || state.HostBinding != binding)
                throw new LicenseException("DEVICE_CHANGED", "This installation identity belongs to another host.");
            ECDsa restored;
            if (OperatingSystem.IsWindows()) restored = OpenWindows(state.CngName);
            else
            {
                restored = ECDsa.Create();
                byte[] privateKey = Convert.FromBase64String(state.UnixPkcs8);
                try
                {
                    restored.ImportPkcs8PrivateKey(privateKey, out int used);
                    if (used != privateKey.Length) throw new LicenseException("DEVICE_KEY", "Invalid device private key.");
                }
                catch { restored.Dispose(); throw; }
                finally { CryptographicOperations.ZeroMemory(privateKey); }
            }
            var identity = new DeviceIdentity(restored, binding);
            if (identity.PublicKey != state.PublicKey) { identity.Dispose(); throw new LicenseException("DEVICE_KEY", "Device keys do not match."); }
            return identity;
        }
        string cngName = "", unixKey = "";
        ECDsa created;
        if (OperatingSystem.IsWindows())
        {
            cngName = "LSP-" + Guid.NewGuid().ToString("N");
            created = CreateWindows(cngName);
        }
        else
        {
            created = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            byte[] bytes = created.ExportPkcs8PrivateKey();
            try { unixKey = Convert.ToBase64String(bytes); }
            finally { CryptographicOperations.ZeroMemory(bytes); }
        }
        var result = new DeviceIdentity(created, binding);
        try
        {
            PrivateFiles.Write(path, LicenseJson.Write(new DeviceKeyState(1, cngName, unixKey, result.PublicKey, binding)), false);
            return result;
        }
        catch { result.Dispose(); throw; }
    }

    /// <summary>Signs only the fixed, validated possession-proof transcript.</summary>
    /// <param name="request">Request before its Proof field is populated.</param>
    /// <returns>Canonical base64url 64-byte P-256 signature.</returns>
    public string Prove(LicenseRequest request) => LicenseCrypto.Encode(key.SignData(LicenseCrypto.Transcript(request),
        HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation));

    /// <summary>Signs a bounded local licensing checkpoint in a domain distinct from network proofs.</summary>
    /// <param name="bytes">Local state bytes, at most ten KiB.</param>
    /// <returns>Canonical P-256 signature.</returns>
    public string SignLocalState(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length > 10000) throw new LicenseException("RESERVE_CACHE", "Local checkpoint exceeds its size limit.");
        byte[] payload = Encoding.UTF8.GetBytes("LSP-LOCAL-RESERVE-1\n").Concat(bytes.ToArray()).ToArray();
        lock (key) return LicenseCrypto.Encode(key.SignData(payload, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
    }

    /// <summary>Verifies a local reserve checkpoint using the installation public key.</summary>
    /// <param name="bytes">Stored state bytes.</param>
    /// <param name="signature">Bounded base64url signature.</param>
    /// <returns>Whether the checkpoint belongs to the current installation.</returns>
    public bool VerifyLocalState(ReadOnlySpan<byte> bytes, string signature)
    {
        if (bytes.Length > 10000) return false;
        byte[] payload = Encoding.UTF8.GetBytes("LSP-LOCAL-RESERVE-1\n").Concat(bytes.ToArray()).ToArray();
        using ECDsa verifier = LicenseCrypto.ImportPublic(PublicKey);
        return verifier.VerifyData(payload, LicenseCrypto.Decode(signature, 64, 64), HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
    }

    /// <summary>Creates a user-scoped software-KSP key with no private export permission.</summary>
    /// <param name="name">Unique persistent CNG key name.</param>
    /// <returns>Disposable signer owning its CNG handle.</returns>
    [SupportedOSPlatform("windows")]
    private static ECDsa CreateWindows(string name)
    {
        using CngKey created = CngKey.Create(CngAlgorithm.ECDsaP256, name, new CngKeyCreationParameters
        {
            ExportPolicy = CngExportPolicies.None,
            KeyUsage = CngKeyUsages.Signing,
            Provider = CngProvider.MicrosoftSoftwareKeyStorageProvider
        });
        return new ECDsaCng(created);
    }

    /// <summary>Reopens the current user's persisted non-exportable CNG key.</summary>
    /// <param name="name">Stored CNG key name.</param>
    /// <returns>Disposable signer.</returns>
    [SupportedOSPlatform("windows")]
    private static ECDsa OpenWindows(string name)
    {
        if (!name.StartsWith("LSP-", StringComparison.Ordinal)) throw new LicenseException("DEVICE_KEY", "Invalid CNG key identity.");
        using CngKey opened = CngKey.Open(name, CngProvider.MicrosoftSoftwareKeyStorageProvider);
        return new ECDsaCng(opened);
    }

    /// <summary>Hashes the OS-installation identifier. This is a cloning deterrent, not a hardware attestation.</summary>
    /// <returns>Product-scoped SHA-256; fails closed if no stable OS identifier is available.</returns>
    private static string ReadHostBinding()
    {
        string raw;
        if (OperatingSystem.IsWindows()) raw = ReadWindowsMachineId();
        else
        {
            string path = File.Exists("/etc/machine-id") ? "/etc/machine-id" : "/var/lib/dbus/machine-id";
            raw = Encoding.UTF8.GetString(PrivateFiles.Read(path, 256)).Trim();
        }
        if (raw.Length is < 16 or > 128) throw new LicenseException("DEVICE_ID_UNAVAILABLE", "Stable OS installation ID is unavailable.");
        return LicenseCrypto.Digest(Encoding.UTF8.GetBytes(LicenseCrypto.Product + "\n" + raw));
    }

    /// <summary>Reads the 64-bit Windows MachineGuid view without exposing the value in diagnostics.</summary>
    /// <returns>OS installation ID.</returns>
    [SupportedOSPlatform("windows")]
    private static string ReadWindowsMachineId()
    {
        using RegistryKey root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using RegistryKey? sub = root.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
        return sub?.GetValue("MachineGuid") as string ?? "";
    }

    /// <summary>Releases private-key resources but does not delete a persisted activation.</summary>
    public void Dispose() => key.Dispose();
}
