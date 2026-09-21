using System.Security.Cryptography;
using System.Text;

namespace LuckyStarPspToolkit;

/// <summary>
/// Represents the toolkit's PSP module header model or service.
/// </summary>
public sealed class PspModuleHeader
{
    /// <summary>The attribute value used by this model or operation.</summary>
    public ushort Attribute { get; init; }
    /// <summary>The compression attribute value used by this model or operation.</summary>
    public ushort CompressionAttribute { get; init; }
    /// <summary>The module version low value used by this model or operation.</summary>
    public byte ModuleVersionLow { get; init; }
    /// <summary>The module version high value used by this model or operation.</summary>
    public byte ModuleVersionHigh { get; init; }
    /// <summary>The module name value used by this model or operation.</summary>
    public required string ModuleName { get; init; }
    /// <summary>The header version value used by this model or operation.</summary>
    public byte HeaderVersion { get; init; }
    /// <summary>The segment count value used by this model or operation.</summary>
    public byte SegmentCount { get; init; }
    /// <summary>The elf size value used by this model or operation.</summary>
    public uint ElfSize { get; init; }
    /// <summary>The psp size value used by this model or operation.</summary>
    public uint PspSize { get; init; }
    /// <summary>The entry point value used by this model or operation.</summary>
    public uint EntryPoint { get; init; }
    /// <summary>The module info offset value used by this model or operation.</summary>
    public uint ModuleInfoOffset { get; init; }
    /// <summary>The bss size value used by this model or operation.</summary>
    public int BssSize { get; init; }
    /// <summary>The segment alignment value used by this model or operation.</summary>
    public required ushort[] SegmentAlignment { get; init; }
    /// <summary>The segment address value used by this model or operation.</summary>
    public required uint[] SegmentAddress { get; init; }
    /// <summary>The segment size value used by this model or operation.</summary>
    public required int[] SegmentSize { get; init; }
    /// <summary>The devkit version value used by this model or operation.</summary>
    public uint DevkitVersion { get; init; }
    /// <summary>The decrypt mode value used by this model or operation.</summary>
    public uint DecryptMode { get; init; }
    /// <summary>The compressed size value used by this model or operation.</summary>
    public int CompressedSize { get; init; }
    /// <summary>The tag value used by this model or operation.</summary>
    public uint Tag { get; init; }
    /// <summary>The oe tag value used by this model or operation.</summary>
    public uint OeTag { get; init; }
    /// <summary>The supported by this build value used by this model or operation.</summary>
    public bool SupportedByThisBuild { get; init; }

    /// <summary>The module version value used by this model or operation.</summary>
    public string ModuleVersion => $"{ModuleVersionHigh}.{ModuleVersionLow}";
}

/// <summary>
/// Represents the toolkit's PSP decryption result model or service.
/// </summary>
public sealed class PspDecryptionResult
{
    /// <summary>The header value used by this model or operation.</summary>
    public required PspModuleHeader Header { get; init; }
    /// <summary>The elf value used by this model or operation.</summary>
    public required byte[] Elf { get; init; }
    /// <summary>The encrypted sha256 value used by this model or operation.</summary>
    public required string EncryptedSha256 { get; init; }
    /// <summary>The decrypted sha256 value used by this model or operation.</summary>
    public required string DecryptedSha256 { get; init; }
}

/// <summary>
/// Provides PSP PRX reader operations with strict bounds and format validation.
/// </summary>
public static class PspPrxReader
{
    /// <summary>The fixed supported rgo tag value used by this format or revision.</summary>
    public const uint SupportedRgoTag = 0xD91613F0;
    /// <summary>The fixed header size value used by this format or revision.</summary>
    public const int HeaderSize = 0x150;

    /// <summary>The fixed kirk mode command1 value used by this format or revision.</summary>
    private const uint KirkModeCommand1 = 1;
    /// <summary>The fixed kirk command header size value used by this format or revision.</summary>
    private const int KirkCommandHeaderSize = 0x90;
    /// <summary>The fixed prx header prefix size value used by this format or revision.</summary>
    private const int PrxHeaderPrefixSize = 0x80;
    /// <summary>The fixed kirk command offset value used by this format or revision.</summary>
    private const int KirkCommandOffset = HeaderSize - KirkCommandHeaderSize - PrxHeaderPrefixSize;

    /// <summary>The tag key d91613 f0 value used by this model or operation.</summary>
    private static readonly byte[] TagKeyD91613F0 =
    [
        0xEB, 0xFF, 0x40, 0xD8, 0xB4, 0x1A, 0xE1, 0x66,
        0x91, 0x3B, 0x8F, 0x64, 0xB6, 0xFC, 0xB7, 0x12
    ];

    /// <summary>The kirk7 key5 d value used by this model or operation.</summary>
    private static readonly byte[] Kirk7Key5D =
    [
        0x11, 0x5A, 0x5D, 0x20, 0xD5, 0x3A, 0x8D, 0xD3,
        0x9C, 0xC5, 0xAF, 0x41, 0x0F, 0x0F, 0x18, 0x6F
    ];

    /// <summary>The kirk1 key value used by this model or operation.</summary>
    private static readonly byte[] Kirk1Key =
    [
        0x98, 0xC9, 0x40, 0x97, 0x5C, 0x1D, 0x10, 0xE8,
        0x7F, 0xE6, 0x0E, 0xA3, 0xFD, 0x03, 0xA8, 0xBA
    ];

    /// <summary>
    /// Determines whether the supplied data matches the expected format signature.
    /// </summary>
    /// <param name="data">The binary data to process.</param>
    /// <returns><see langword="true"/> when the condition is satisfied; otherwise <see langword="false"/>.</returns>
    public static bool LooksLike(ReadOnlySpan<byte> data) => BinaryData.StartsWithAscii(data, "~PSP");

    /// <summary>
    /// Checks whether the PRX tag is supported; authentication still occurs during decryption.
    /// </summary>
    /// <param name="header">The header value.</param>
    /// <returns><see langword="true"/> when the condition is satisfied; otherwise <see langword="false"/>.</returns>
    public static bool CanDecrypt(PspModuleHeader header)
    {
        ArgumentNullException.ThrowIfNull(header);
        return header.Tag == SupportedRgoTag;
    }

    /// <summary>
    /// Parses header while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="data">The binary data to process.</param>
    /// <returns>The validated operation result.</returns>
    /// <remarks>Malformed input or violated preconditions are rejected before a persistent output is committed.</remarks>
    public static PspModuleHeader ParseHeader(ReadOnlySpan<byte> data)
    {
        Guard.RequireRange(data.Length, 0, HeaderSize, "PSP encrypted module header");
        Guard.Require(LooksLike(data), "Not a PSP encrypted module: invalid ~PSP signature.");

        ushort[] segmentAlignment = new ushort[4];
        uint[] segmentAddress = new uint[4];
        int[] segmentSize = new int[4];
        for (int index = 0; index < 4; index++)
        {
            segmentAlignment[index] = BinaryData.ReadUInt16LittleEndian(
                data, 0x3C + index * 2, "PSP segment alignment");
            segmentAddress[index] = BinaryData.ReadUInt32LittleEndian(
                data, 0x44 + index * 4, "PSP segment address");
            segmentSize[index] = BinaryData.ReadInt32LittleEndian(
                data, 0x54 + index * 4, "PSP segment size");
        }

        uint pspSize = BinaryData.ReadUInt32LittleEndian(data, 0x2C, "PSP container size");
        uint elfSize = BinaryData.ReadUInt32LittleEndian(data, 0x28, "PSP ELF size");
        Guard.Require(pspSize <= data.Length,
            "PSP header declares a container larger than the supplied file.");
        Guard.Require(elfSize != 0, "PSP header declares an empty ELF payload.");

        uint tag = BinaryData.ReadUInt32LittleEndian(data, 0xD0, "PSP tag");
        return new PspModuleHeader
        {
            Attribute = BinaryData.ReadUInt16LittleEndian(data, 0x04, "PSP attribute"),
            CompressionAttribute = BinaryData.ReadUInt16LittleEndian(data, 0x06, "PSP compression attribute"),
            ModuleVersionLow = data[0x08],
            ModuleVersionHigh = data[0x09],
            ModuleName = ReadModuleName(data.Slice(0x0A, 28)),
            HeaderVersion = data[0x26],
            SegmentCount = data[0x27],
            ElfSize = elfSize,
            PspSize = pspSize,
            EntryPoint = BinaryData.ReadUInt32LittleEndian(data, 0x30, "PSP entry point"),
            ModuleInfoOffset = BinaryData.ReadUInt32LittleEndian(data, 0x34, "PSP module-info offset"),
            BssSize = BinaryData.ReadInt32LittleEndian(data, 0x38, "PSP BSS size"),
            SegmentAlignment = segmentAlignment,
            SegmentAddress = segmentAddress,
            SegmentSize = segmentSize,
            DevkitVersion = BinaryData.ReadUInt32LittleEndian(data, 0x78, "PSP devkit version"),
            DecryptMode = BinaryData.ReadUInt32LittleEndian(data, 0x7C, "PSP decrypt mode"),
            CompressedSize = BinaryData.ReadInt32LittleEndian(data, 0xB0, "PSP compressed size"),
            Tag = tag,
            OeTag = BinaryData.ReadUInt32LittleEndian(data, 0x130, "PSP OE tag"),
            SupportedByThisBuild = tag == SupportedRgoTag
        };
    }

    /// <summary>
    /// Authenticates a supported PSP PRX and returns its verified decrypted ELF payload.
    /// </summary>
    /// <param name="input">The input binary data or object.</param>
    /// <returns>The validated operation result.</returns>
    /// <remarks>Malformed input or violated preconditions are rejected before a persistent output is committed.</remarks>
    public static PspDecryptionResult DecryptVerified(ReadOnlySpan<byte> input)
    {
        PspModuleHeader header = ParseHeader(input);
        Guard.Require(CanDecrypt(header),
            $"Unsupported PSP PRX tag {HexUtilities.UInt32(header.Tag)}; " +
            "this build intentionally supports only the verified ULJM05752 game tag.");
        Guard.Require(header.PspSize == input.Length,
            "PSP PRX file size differs from the size stored in its authenticated header.");
        Guard.Require(BinaryData.IsAllZero(BinaryData.Slice(
                input, 0xD4, 0x58, "PSP Type-2 reserved signature area")),
            "Unsupported Type-2 PRX layout: reserved signature area is not empty.");

        byte[] expandedSeed = ExpandSeed();
        byte[] authenticatedTail = new byte[0x64];
        CopyField(input, 0x140, authenticatedTail, 0x00, 0x10, "PSP Type-2 ID");
        CopyField(input, 0x12C, authenticatedTail, 0x10, 0x14, "PSP Type-2 SHA-1");
        CopyField(input, 0x080, authenticatedTail, 0x24, 0x30, "PSP Type-2 KIRK header part 1");
        CopyField(input, 0x0C0, authenticatedTail, 0x54, 0x10, "PSP Type-2 KIRK header part 2");

        byte[] decryptedTail = CryptoUtilities.Aes128CbcDecrypt(
            authenticatedTail.AsSpan(0, 0x60), Kirk7Key5D);
        decryptedTail.CopyTo(authenticatedTail, 0);

        ReadOnlySpan<byte> identifier = authenticatedTail.AsSpan(0x00, 0x10);
        ReadOnlySpan<byte> expectedSha1 = authenticatedTail.AsSpan(0x10, 0x14);
        ReadOnlySpan<byte> encryptedKirkHeader = authenticatedTail.AsSpan(0x24, 0x40);
        ReadOnlySpan<byte> kirkMetadata = BinaryData.Slice(input, 0xB0, 0x10, "PSP KIRK metadata");
        ReadOnlySpan<byte> prxHeader = BinaryData.Slice(input, 0x00, 0x80, "PSP PRX header prefix");

        byte[] actualSha1;
        using (IncrementalHash hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA1))
        {
            hasher.AppendData(BinaryData.Slice(input, 0xD0, 4, "PSP tag bytes"));
            hasher.AppendData(expandedSeed.AsSpan(0, 0x10));
            hasher.AppendData(new byte[0x58]);
            hasher.AppendData(identifier);
            hasher.AppendData(encryptedKirkHeader);
            hasher.AppendData(kirkMetadata);
            hasher.AppendData(prxHeader);
            actualSha1 = hasher.GetHashAndReset();
        }

        Guard.Require(CryptographicOperations.FixedTimeEquals(actualSha1, expectedSha1),
            "PSP Type-2 SHA-1 authentication failed; the EBOOT may be damaged or unsupported.");

        byte[] stage = encryptedKirkHeader.ToArray();
        XorInPlace(stage, expandedSeed.AsSpan(0x10, 0x40));
        stage = CryptoUtilities.Aes128CbcDecrypt(stage, Kirk7Key5D);
        XorInPlace(stage, expandedSeed.AsSpan(0x50, 0x40));

        byte[] command = new byte[KirkCommandHeaderSize];
        stage.CopyTo(command, 0);
        kirkMetadata.CopyTo(command.AsSpan(0x70, 0x10));
        BinaryData.WriteUInt32LittleEndian(command, 0x60, KirkModeCommand1, "KIRK mode");

        byte[] keyPair = CryptoUtilities.Aes128CbcDecrypt(command.AsSpan(0, 0x20), Kirk1Key);
        Guard.Require(keyPair.Length == 0x20, "KIRK key decryption returned an unexpected size.");
        ReadOnlySpan<byte> payloadKey = keyPair.AsSpan(0, 0x10);
        ReadOnlySpan<byte> cmacKey = keyPair.AsSpan(0x10, 0x10);

        int dataSize = ToInt32(BinaryData.ReadUInt32LittleEndian(command, 0x70, "KIRK data size"),
            "KIRK data size");
        int dataOffset = ToInt32(BinaryData.ReadUInt32LittleEndian(command, 0x74, "KIRK data offset"),
            "KIRK data offset");
        Guard.Require(dataSize == header.ElfSize,
            "Authenticated KIRK data size does not match the PSP ELF size.");
        Guard.Require(dataOffset == PrxHeaderPrefixSize,
            "Unsupported KIRK data offset for the verified Type-2 EBOOT layout.");

        int encryptedSize = BinaryData.AlignUp(dataSize, 16);
        int encryptedOffset = checked(KirkCommandOffset + KirkCommandHeaderSize + dataOffset);
        ReadOnlySpan<byte> encryptedPayload = BinaryData.Slice(
            input, encryptedOffset, encryptedSize, "PSP encrypted ELF payload");

        ReadOnlySpan<byte> expectedHeaderCmac = command.AsSpan(0x20, 0x10);
        ReadOnlySpan<byte> expectedDataCmac = command.AsSpan(0x30, 0x10);
        byte[] headerCmac = CryptoUtilities.Aes128Cmac(command.AsSpan(0x60, 0x30), cmacKey);
        Guard.Require(CryptographicOperations.FixedTimeEquals(headerCmac, expectedHeaderCmac),
            "KIRK CMD1 header CMAC authentication failed.");

        byte[] dataCmacMaterial = new byte[checked(0x30 + PrxHeaderPrefixSize + encryptedPayload.Length)];
        command.AsSpan(0x60, 0x30).CopyTo(dataCmacMaterial);
        prxHeader.CopyTo(dataCmacMaterial.AsSpan(0x30));
        encryptedPayload.CopyTo(dataCmacMaterial.AsSpan(0x30 + PrxHeaderPrefixSize));
        byte[] dataCmac = CryptoUtilities.Aes128Cmac(dataCmacMaterial, cmacKey);
        Guard.Require(CryptographicOperations.FixedTimeEquals(dataCmac, expectedDataCmac),
            "KIRK CMD1 payload CMAC authentication failed; the EBOOT may be damaged.");

        byte[] paddedPlaintext = CryptoUtilities.Aes128CbcDecrypt(encryptedPayload, payloadKey);
        byte[] plaintext = paddedPlaintext.AsSpan(0, dataSize).ToArray();
        Guard.Require(ElfReader.LooksLike(plaintext),
            "PRX decryption completed but the result is not an ELF executable.");

        return new PspDecryptionResult
        {
            Header = header,
            Elf = plaintext,
            EncryptedSha256 = CryptoUtilities.Sha256Hex(input),
            DecryptedSha256 = CryptoUtilities.Sha256Hex(plaintext)
        };
    }

    /// <summary>
    /// Expands seed while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <returns>The resulting binary or typed sequence.</returns>
    private static byte[] ExpandSeed()
    {
        byte[] encoded = new byte[0x90];
        for (int offset = 0; offset < encoded.Length; offset += 0x10)
        {
            TagKeyD91613F0.CopyTo(encoded, offset);
            encoded[offset] = (byte)(offset / 0x10);
        }

        return CryptoUtilities.Aes128CbcDecrypt(encoded, Kirk7Key5D);
    }

    /// <summary>
    /// Copies field while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="source">The source binary data or object.</param>
    /// <param name="sourceOffset">The source offset value.</param>
    /// <param name="destination">The destination stream or buffer.</param>
    /// <param name="destinationOffset">The destination offset value.</param>
    /// <param name="length">The number of bytes or elements to process.</param>
    /// <param name="context">A diagnostic label included in validation errors.</param>
    private static void CopyField(
        ReadOnlySpan<byte> source,
        int sourceOffset,
        Span<byte> destination,
        int destinationOffset,
        int length,
        string context)
    {
        BinaryData.Slice(source, sourceOffset, length, context)
            .CopyTo(destination.Slice(destinationOffset, length));
    }

    /// <summary>
    /// XORs equal-length byte spans for PRX key derivation, rejecting inconsistent lengths.
    /// </summary>
    /// <param name="value">The value to process.</param>
    /// <param name="mask">The mask value.</param>
    private static void XorInPlace(Span<byte> value, ReadOnlySpan<byte> mask)
    {
        Guard.Require(value.Length == mask.Length, "Internal XOR length mismatch.");
        for (int index = 0; index < value.Length; index++)
        {
            value[index] ^= mask[index];
        }
    }

    /// <summary>
    /// Reads module name while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="data">The binary data to process.</param>
    /// <returns>The resulting text, path, identifier, or hexadecimal digest.</returns>
    private static string ReadModuleName(ReadOnlySpan<byte> data)
    {
        int zero = data.IndexOf((byte)0);
        ReadOnlySpan<byte> name = zero >= 0 ? data[..zero] : data;
        return Encoding.ASCII.GetString(name).TrimEnd();
    }

    /// <summary>
    /// Converts int 32 while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="value">The value to process.</param>
    /// <param name="context">A diagnostic label included in validation errors.</param>
    /// <returns>The validated operation result.</returns>
    private static int ToInt32(uint value, string context)
    {
        Guard.Require(value <= int.MaxValue, $"{context} is too large.");
        return (int)value;
    }
}
