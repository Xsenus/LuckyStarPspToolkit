using System.Text;

namespace LuckyStarPspToolkit;

/// <summary>
/// Represents the toolkit's diagnostic case result model or service.
/// </summary>
public sealed class DiagnosticCaseResult
{
    /// <summary>The name value used by this model or operation.</summary>
    public required string Name { get; init; }
    /// <summary>The passed value used by this model or operation.</summary>
    public required bool Passed { get; init; }
    /// <summary>The error value used by this model or operation.</summary>
    public string? Error { get; init; }
}

/// <summary>
/// Represents the toolkit's runtime self test report model or service.
/// </summary>
public sealed class RuntimeSelfTestReport
{
    /// <summary>The schema value used by this model or operation.</summary>
    public string Schema { get; init; } = "lucky-star-psp.self-test.v1";
    /// <summary>The version used in CLI reports and release metadata.</summary>
    public required string Version { get; init; }
    /// <summary>The passed value used by this model or operation.</summary>
    public required bool Passed { get; init; }
    /// <summary>The cases value used by this model or operation.</summary>
    public required IReadOnlyList<DiagnosticCaseResult> Cases { get; init; }
}

/// <summary>
/// Provides the toolkit's self diagnostics workflow.
/// </summary>
public static class SelfDiagnostics
{
    /// <summary>The toolkit version value used by this model or operation.</summary>
    public static string ToolkitVersion { get; } = ResolveToolkitVersion();

    /// <summary>
    /// Resolves toolkit version while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <returns>The resulting text, path, identifier, or hexadecimal digest.</returns>
    private static string ResolveToolkitVersion()
    {
        Version? version = typeof(SelfDiagnostics).Assembly.GetName().Version;
        return version is null ? "unknown" : $"{version.Major}.{version.Minor}.{version.Build}";
    }

    /// <summary>
    /// Runs the built-in diagnostic cases and returns their individual results and aggregate status.
    /// </summary>
    /// <returns>A diagnostic report containing every executed case and its outcome.</returns>
    public static RuntimeSelfTestReport Run()
    {
        var results = new List<DiagnosticCaseResult>();
        RunCase(results, "SHA-256 known vector", TestSha256);
        RunCase(results, "AES-128-CBC known vector", TestAesCbc);
        RunCase(results, "AES-CMAC empty-message vector", TestCmacEmpty);
        RunCase(results, "AES-CMAC complete-block vector", TestCmacBlock);
        RunCase(results, "PARAM.SFO parser", TestSfo);
        RunCase(results, "ELF32 MIPS parser", TestElf);
        RunCase(results, "controlled range failure", TestRangeFailure);
        RunCase(results, "RGO VWF patch profile integrity", TestRgoVwfProfile);

        return new RuntimeSelfTestReport
        {
            Version = ToolkitVersion,
            Passed = results.All(result => result.Passed),
            Cases = results.AsReadOnly()
        };
    }

    /// <summary>
    /// Formats the current result as human-readable diagnostic text.
    /// </summary>
    /// <param name="report">The report value.</param>
    /// <returns>The resulting text, path, identifier, or hexadecimal digest.</returns>
    public static string ToHumanText(RuntimeSelfTestReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        var output = new StringBuilder();
        output.AppendLine($"Lucky Star PSP Toolkit {report.Version} self-test");
        foreach (DiagnosticCaseResult result in report.Cases)
        {
            output.AppendLine($"  [{(result.Passed ? "PASS" : "FAIL")}] {result.Name}");
            if (!result.Passed && !string.IsNullOrWhiteSpace(result.Error))
            {
                output.AppendLine($"         {result.Error}");
            }
        }
        output.AppendLine($"Result: {(report.Passed ? "PASS" : "FAIL")}");
        return output.ToString();
    }

    /// <summary>
    /// Verifies SHA 256 behavior and invariants.
    /// </summary>
    private static void TestSha256()
    {
        byte[] digest = CryptoUtilities.Sha256("abc"u8);
        RequireEqual(
            HexUtilities.ToHex(digest),
            "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad",
            "SHA-256 digest");
    }

    /// <summary>
    /// Verifies aes cbc behavior and invariants.
    /// </summary>
    private static void TestAesCbc()
    {
        byte[] key = HexUtilities.Parse("000102030405060708090a0b0c0d0e0f");
        byte[] plaintext = HexUtilities.Parse("00112233445566778899aabbccddeeff");
        byte[] ciphertext = HexUtilities.Parse("69c4e0d86a7b0430d8cdb78070b4c55a");
        RequireEqual(
            HexUtilities.ToHex(CryptoUtilities.Aes128CbcEncrypt(plaintext, key)),
            HexUtilities.ToHex(ciphertext),
            "AES encryption");
        RequireEqual(
            HexUtilities.ToHex(CryptoUtilities.Aes128CbcDecrypt(ciphertext, key)),
            HexUtilities.ToHex(plaintext),
            "AES decryption");
    }

    /// <summary>
    /// Verifies CMAC empty behavior and invariants.
    /// </summary>
    private static void TestCmacEmpty()
    {
        byte[] key = HexUtilities.Parse("2b7e151628aed2a6abf7158809cf4f3c");
        RequireEqual(
            HexUtilities.ToHex(CryptoUtilities.Aes128Cmac(ReadOnlySpan<byte>.Empty, key)),
            "bb1d6929e95937287fa37d129b756746",
            "empty-message AES-CMAC");
    }

    /// <summary>
    /// Verifies CMAC block behavior and invariants.
    /// </summary>
    private static void TestCmacBlock()
    {
        byte[] key = HexUtilities.Parse("2b7e151628aed2a6abf7158809cf4f3c");
        byte[] message = HexUtilities.Parse("6bc1bee22e409f96e93d7e117393172a");
        RequireEqual(
            HexUtilities.ToHex(CryptoUtilities.Aes128Cmac(message, key)),
            "070a16b46b4d4144f79bdd9dd04a287c",
            "single-block AES-CMAC");
    }

    /// <summary>
    /// Verifies SFO behavior and invariants.
    /// </summary>
    private static void TestSfo()
    {
        SfoFile sfo = SfoReader.Parse(CreateSyntheticSfo());
        RequireEqual(sfo.GetString("DISC_ID"), RgoProfile.DiscId, "SFO DISC_ID");
        RequireEqual(sfo.GetString("TITLE"), "Test title", "SFO TITLE");
        Guard.Require(sfo.GetUInt32("BOOTABLE") == 1, "SFO BOOTABLE mismatch.");
    }

    /// <summary>
    /// Verifies ELF behavior and invariants.
    /// </summary>
    private static void TestElf()
    {
        byte[] bytes = new byte[52];
        new byte[] { 0x7F, (byte)'E', (byte)'L', (byte)'F' }.CopyTo(bytes, 0);
        bytes[4] = 1;
        bytes[5] = 1;
        bytes[6] = 1;
        BinaryData.WriteUInt16LittleEndian(bytes, 16, 0xFFA0, "test ELF type");
        BinaryData.WriteUInt16LittleEndian(bytes, 18, 8, "test ELF machine");
        BinaryData.WriteUInt32LittleEndian(bytes, 20, 1, "test ELF version");
        BinaryData.WriteUInt32LittleEndian(bytes, 24, 0x528E0, "test ELF entry");
        BinaryData.WriteUInt16LittleEndian(bytes, 40, 52, "test ELF header size");
        Elf32Info info = ElfReader.Parse32LittleEndian(bytes);
        Guard.Require(info.IsPspMips, "Synthetic ELF was not recognized as MIPS.");
        Guard.Require(info.EntryPoint == 0x528E0, "Synthetic ELF entry point mismatch.");
    }

    /// <summary>
    /// Verifies RGO VWF profile behavior and invariants.
    /// </summary>
    private static void TestRgoVwfProfile()
    {
        Guard.Require(RgoVwfPatchProfile.Patches.Count == 133,
            $"RGO VWF profile patch count mismatch: {RgoVwfPatchProfile.Patches.Count}.");
        Guard.Require(RgoVwfPatchProfile.Groups.Count == 5,
            $"RGO VWF profile group count mismatch: {RgoVwfPatchProfile.Groups.Count}.");
        Guard.Require(RgoVwfPatchProfile.Patches.Select(patch => patch.Offset).Distinct().Count()
            == RgoVwfPatchProfile.Patches.Count,
            "RGO VWF profile contains duplicate offsets.");
        Guard.Require(RgoVwfPatchProfile.Patches.All(patch => patch.Expected != patch.Replacement),
            "RGO VWF profile contains a no-op patch word.");
        Guard.Require(RgoVwfPatchProfile.Patches.All(patch =>
            patch.Offset >= 0 && patch.Offset <= RgoProfile.KnownDecryptedSize - sizeof(uint)),
            "RGO VWF profile contains an out-of-range patch offset.");
        Guard.Require(RgoVwfPatchProfile.Patches.Select(patch => patch.Name).Distinct(StringComparer.Ordinal).Count()
            == RgoVwfPatchProfile.Patches.Count,
            "RGO VWF profile contains duplicate patch names.");

        int tableEnd = checked(
            RgoProfile.ScriptSizeTableOffset
            + (RgoProfile.ScriptLastId - RgoProfile.ScriptFirstId + 1) * 4);
        Guard.Require(RgoVwfPatchProfile.Patches.All(patch =>
            !RangesOverlap(patch.Offset, patch.Offset + sizeof(uint), RgoProfile.ScriptSizeTableOffset, tableEnd)
            && !RangesOverlap(
                patch.Offset,
                patch.Offset + sizeof(uint),
                RgoProfile.ScriptHeapLuiOffset,
                RgoProfile.ScriptHeapLuiOffset + sizeof(uint))
            && !RangesOverlap(
                patch.Offset,
                patch.Offset + sizeof(uint),
                RgoProfile.ScriptHeapAddiuOffset,
                RgoProfile.ScriptHeapAddiuOffset + sizeof(uint))),
            "RGO VWF words overlap dynamic script metadata.");

        int decodedHeap = checked(
            ((int)(RgoProfile.KnownScriptHeapLuiInstruction & 0xFFFFU) << 16)
            + unchecked((short)(RgoProfile.KnownScriptHeapAddiuInstruction & 0xFFFFU)));
        Guard.Require(decodedHeap == RgoProfile.KnownScriptHeapBytes,
            $"RGO script-heap instruction pair decodes to {decodedHeap}, expected {RgoProfile.KnownScriptHeapBytes}.");

        Dictionary<string, int> expected = new(StringComparer.OrdinalIgnoreCase)
        {
            ["vwf-core"] = 66,
            ["message-layout"] = 7,
            ["speaker-layout"] = 50,
            ["choice-layout"] = 3,
            ["name-layout"] = 7
        };
        foreach (RgoEbootPatchGroupInfo group in RgoVwfPatchProfile.Groups)
        {
            Guard.Require(expected.TryGetValue(group.Id, out int count) && group.PatchCount == count,
                $"RGO VWF group '{group.Id}' count mismatch: {group.PatchCount}.");
        }
        Guard.Require(RgoVwfPatchProfile.ResolveGroups(["core-only"]).SequenceEqual(["vwf-core"]),
            "RGO VWF core-only alias did not resolve correctly.");
        Guard.Require(RgoVwfPatchProfile.ResolveGroups(["russian-text"]).Count == 5,
            "RGO VWF russian-text preset did not select every group.");
    }

    /// <summary>
    /// Determines whether two half-open numeric ranges overlap.
    /// </summary>
    /// <param name="firstStart">The first start value.</param>
    /// <param name="firstEndExclusive">The first end exclusive value.</param>
    /// <param name="secondStart">The second start value.</param>
    /// <param name="secondEndExclusive">The second end exclusive value.</param>
    /// <returns><see langword="true"/> when the condition is satisfied; otherwise <see langword="false"/>.</returns>
    private static bool RangesOverlap(
        int firstStart,
        int firstEndExclusive,
        int secondStart,
        int secondEndExclusive)
        => firstStart < secondEndExclusive && secondStart < firstEndExclusive;

    /// <summary>
    /// Verifies range failure behavior and invariants.
    /// </summary>
    private static void TestRangeFailure()
    {
        bool failedSafely = false;
        try
        {
            _ = BinaryData.ReadUInt32LittleEndian(new byte[] { 1, 2, 3 }, 0, "short test value");
        }
        catch (ToolkitException)
        {
            failedSafely = true;
        }
        Guard.Require(failedSafely, "Short read did not raise ToolkitException.");
    }

    /// <summary>
    /// Creates synthetic SFO while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <returns>The resulting binary or typed sequence.</returns>
    private static byte[] CreateSyntheticSfo()
    {
        const int count = 3;
        const int headerSize = 20;
        const int indexSize = count * 16;
        byte[] keys = Encoding.ASCII.GetBytes("DISC_ID\0TITLE\0BOOTABLE\0");
        int keyOffset = headerSize + indexSize;
        int dataOffset = BinaryData.AlignUp(keyOffset + keys.Length, 4);
        byte[] disc = Encoding.UTF8.GetBytes(RgoProfile.DiscId + "\0");
        byte[] title = Encoding.UTF8.GetBytes("Test title\0");
        byte[] output = new byte[dataOffset + 36];

        BinaryData.WriteUInt32LittleEndian(output, 0, 0x46535000, "test SFO magic");
        BinaryData.WriteUInt32LittleEndian(output, 4, 0x00000101, "test SFO version");
        BinaryData.WriteUInt32LittleEndian(output, 8, (uint)keyOffset, "test SFO key offset");
        BinaryData.WriteUInt32LittleEndian(output, 12, (uint)dataOffset, "test SFO data offset");
        BinaryData.WriteUInt32LittleEndian(output, 16, (uint)count, "test SFO count");

        WriteIndex(output, 20, 0, 0x0204, (uint)disc.Length, 16, 0);
        WriteIndex(output, 36, 8, 0x0204, (uint)title.Length, 16, 16);
        WriteIndex(output, 52, 14, 0x0404, 4, 4, 32);
        keys.CopyTo(output, keyOffset);
        disc.CopyTo(output, dataOffset);
        title.CopyTo(output, dataOffset + 16);
        BinaryData.WriteUInt32LittleEndian(output, dataOffset + 32, 1, "test SFO BOOTABLE");
        return output;
    }

    /// <summary>
    /// Writes index while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="data">The binary data to process.</param>
    /// <param name="offset">The zero-based byte or element offset.</param>
    /// <param name="keyOffset">The key offset value.</param>
    /// <param name="format">The format value.</param>
    /// <param name="length">The number of bytes or elements to process.</param>
    /// <param name="maximumLength">The maximum length value.</param>
    /// <param name="dataOffset">The data offset value.</param>
    private static void WriteIndex(
        Span<byte> data,
        int offset,
        ushort keyOffset,
        ushort format,
        uint length,
        uint maximumLength,
        uint dataOffset)
    {
        BinaryData.WriteUInt16LittleEndian(data, offset, keyOffset, "test SFO key index");
        BinaryData.WriteUInt16LittleEndian(data, offset + 2, format, "test SFO format");
        BinaryData.WriteUInt32LittleEndian(data, offset + 4, length, "test SFO length");
        BinaryData.WriteUInt32LittleEndian(data, offset + 8, maximumLength, "test SFO maximum length");
        BinaryData.WriteUInt32LittleEndian(data, offset + 12, dataOffset, "test SFO value offset");
    }

    /// <summary>
    /// Runs one deterministic self-test case and records its result.
    /// </summary>
    /// <param name="results">The results value.</param>
    /// <param name="name">The logical name used for lookup or diagnostics.</param>
    /// <param name="test">The test value.</param>
    private static void RunCase(List<DiagnosticCaseResult> results, string name, Action test)
    {
        try
        {
            test();
            results.Add(new DiagnosticCaseResult { Name = name, Passed = true });
        }
        catch (Exception exception)
        {
            results.Add(new DiagnosticCaseResult
            {
                Name = name,
                Passed = false,
                Error = exception.Message
            });
        }
    }

    /// <summary>
    /// Requires equal while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="actual">The actual value.</param>
    /// <param name="expected">The expected value.</param>
    /// <param name="context">A diagnostic label included in validation errors.</param>
    private static void RequireEqual(string? actual, string? expected, string context)
    {
        Guard.Require(string.Equals(actual, expected, StringComparison.Ordinal),
            $"{context}: expected '{expected}', got '{actual}'.");
    }
}
