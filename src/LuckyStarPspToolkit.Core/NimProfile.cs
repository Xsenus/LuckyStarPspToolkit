namespace LuckyStarPspToolkit;

/// <summary>Identifies the one NIM executable revision authenticated on the supplied game image.</summary>
public static class NimProfile
{
    /// <summary>The NIM disc identifier observed in the supplied image.</summary>
    public const string DiscId = "ULJM05542";

    /// <summary>SHA-256 of the encrypted executable in the supplied image.</summary>
    public const string KnownEncryptedSha256 =
        "6b76cfe1b34cccc7c900f33f8cd927d9c6d73f4faf3245ab04f586321898020f";

    /// <summary>SHA-256 of the authenticated, decompressed PSP MIPS ELF.</summary>
    public const string KnownDecryptedSha256 =
        "8beb897623a9e9146b2229f1508d7992cb7ae8f69c0441b89f5921e87783e9e1";

    /// <summary>Decrypted ELF byte count for the verified revision.</summary>
    public const int KnownDecryptedSize = 2_013_341;
}
