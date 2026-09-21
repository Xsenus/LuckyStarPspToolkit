using System.IO.Compression;
using LuckyStarPspToolkit;

namespace LuckyStarPspToolkit.SelfTests;

/// <summary>
/// Represents the toolkit's program model or service.
/// </summary>
internal static partial class Program
{
    /// <summary>Stores the failures state owned by this instance or type.</summary>
    private static int _failures;

    /// <summary>
    /// Runs the process entry point.
    /// </summary>
    /// <param name="args">The command-line arguments to parse and execute.</param>
    /// <returns>The validated operation result.</returns>
    public static int Main(string[] args)
    {
        Run("runtime self-test", TestRuntimeSelfTest);
        Run("streaming CMAC independent vectors and source immutability", TestCmacIndependentVectors);
        Run("streaming CMAC pooled allocation bound", TestCmacAllocation);
        Run("ZIP path traversal rejection", TestZipTraversalRejected);
        Run("ZIP case-insensitive duplicate rejection", TestZipDuplicateRejected);
        Run("truncated SFO rejection", TestTruncatedSfoRejected);
        Run("atomic file write", TestAtomicWrite);
        Run("verified binary patch engine", TestBinaryPatchEngine);
        Run("decrypted ELF profile negative check", TestUnknownElfRejected);

        string? customerArchive = ParseCustomerArchive(args);
        if (customerArchive is not null)
        {
            Run("customer archive exact audit", () => TestCustomerArchive(customerArchive));
            Run("customer EBOOT tamper rejection", () => TestCustomerTamper(customerArchive));
            Run("customer script table update", () => TestCustomerScriptTableUpdate(customerArchive));
            Run("customer VWF full patch", () => TestCustomerVwfFullPatch(customerArchive));
            Run("customer VWF partial and idempotent patch", () => TestCustomerVwfPartialPatch(customerArchive));
            Run("customer VWF size-table lineage and tamper rejection", () => TestCustomerVwfLineage(customerArchive));
        }

        if (_failures != 0)
        {
            Console.Error.WriteLine($"{_failures} test(s) failed.");
            return 1;
        }

        Console.WriteLine("All tests passed.");
        return 0;
    }

    /// <summary>
    /// Verifies runtime self test behavior and invariants.
    /// </summary>
    private static void TestRuntimeSelfTest()
    {
        RuntimeSelfTestReport report = SelfDiagnostics.Run();
        Require(report.Passed,
            string.Join("; ", report.Cases.Where(item => !item.Passed).Select(item => item.Error)));
    }

    /// <summary>
    /// Verifies ZIP traversal rejected behavior and invariants.
    /// </summary>
    private static void TestZipTraversalRejected()
    {
        WithTemporaryDirectory(root =>
        {
            string zipPath = Path.Combine(root, "traversal.zip");
            using (FileStream stream = File.Create(zipPath))
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
            {
                ZipArchiveEntry entry = archive.CreateEntry("../escape.bin");
                using Stream content = entry.Open();
                content.WriteByte(1);
            }

            RequireThrows<ToolkitException>(() => InputInspector.Inspect(zipPath));
        });
    }

    /// <summary>
    /// Verifies ZIP duplicate rejected behavior and invariants.
    /// </summary>
    private static void TestZipDuplicateRejected()
    {
        WithTemporaryDirectory(root =>
        {
            string zipPath = Path.Combine(root, "duplicate.zip");
            using (FileStream stream = File.Create(zipPath))
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
            {
                foreach (string name in new[] { "EBOOT.BIN", "eboot.bin" })
                {
                    ZipArchiveEntry entry = archive.CreateEntry(name, CompressionLevel.NoCompression);
                    using Stream content = entry.Open();
                    content.WriteByte(1);
                }
            }

            RequireThrows<ToolkitException>(() => InputInspector.Inspect(zipPath));
        });
    }

    /// <summary>
    /// Verifies truncated SFO rejected behavior and invariants.
    /// </summary>
    private static void TestTruncatedSfoRejected()
    {
        byte[] data = new byte[20];
        BinaryData.WriteUInt32LittleEndian(data, 0, 0x46535000, "test SFO magic");
        BinaryData.WriteUInt32LittleEndian(data, 16, 1, "test SFO entry count");
        RequireThrows<ToolkitException>(() => SfoReader.Parse(data));
    }

    /// <summary>
    /// Verifies atomic write behavior and invariants.
    /// </summary>
    private static void TestAtomicWrite()
    {
        WithTemporaryDirectory(root =>
        {
            string path = Path.Combine(root, "atomic.bin");
            AtomicFile.WriteAllBytes(path, new byte[] { 1, 2, 3 });
            AtomicFile.WriteAllBytes(path, new byte[] { 4, 5 });
            Require(File.ReadAllBytes(path).SequenceEqual(new byte[] { 4, 5 }),
                "Atomic replacement produced unexpected bytes.");
            Require(!Directory.EnumerateFiles(root, ".*.tmp").Any(),
                "Atomic writer left a temporary file behind.");
        });
    }


    /// <summary>
    /// Verifies binary patch engine behavior and invariants.
    /// </summary>
    private static void TestBinaryPatchEngine()
    {
        byte[] source = { 0x10, 0x20, 0x30, 0x40 };
        BinaryPatchResult result = BinaryPatchEngine.Apply(
            source,
            new[]
            {
                new BinaryPatch
                {
                    Name = "test",
                    Offset = 1,
                    ExpectedHex = "2030",
                    ReplacementHex = "aabb"
                }
            },
            CryptoUtilities.Sha256Hex(source));
        Require(result.Output.SequenceEqual(new byte[] { 0x10, 0xAA, 0xBB, 0x40 }),
            "Binary patch output mismatch.");
        RequireThrows<ToolkitException>(() => BinaryPatchEngine.Apply(
            source,
            new[]
            {
                new BinaryPatch
                {
                    Name = "bad precondition",
                    Offset = 1,
                    ExpectedHex = "ffff",
                    ReplacementHex = "0000"
                }
            }));
    }

    /// <summary>
    /// Verifies unknown ELF rejected behavior and invariants.
    /// </summary>
    private static void TestUnknownElfRejected()
    {
        byte[] elf = new byte[52];
        new byte[] { 0x7F, (byte)'E', (byte)'L', (byte)'F' }.CopyTo(elf, 0);
        elf[4] = 1;
        elf[5] = 1;
        elf[6] = 1;
        BinaryData.WriteUInt16LittleEndian(elf, 18, 8, "test machine");
        BinaryData.WriteUInt32LittleEndian(elf, 20, 1, "test version");
        BinaryData.WriteUInt16LittleEndian(elf, 40, 52, "test header size");
        RgoExecutableCheck check = RgoProfile.VerifyExecutable(elf);
        Require(check.Compatibility == ExecutableCompatibility.Unsupported,
            "Unknown tiny ELF must not be considered patch-compatible.");
    }

    /// <summary>
    /// Verifies customer archive behavior and invariants.
    /// </summary>
    /// <param name="archivePath">The archive path value.</param>
    private static void TestCustomerArchive(string archivePath)
    {
        InspectionResult inspection = InputInspector.Inspect(archivePath);
        Require(inspection.ContainerKind == InputContainerKind.ZipArchive,
            "Customer input must be recognized as ZIP.");
        Require(inspection.Files.Count == 8,
            $"Expected 8 customer files, got {inspection.Files.Count}.");

        FileProbe gameEboot = inspection.Files.Single(file =>
            file.LogicalPath.Equals("EBOOT.BIN", StringComparison.Ordinal));
        Require(gameEboot.Sha256 == RgoProfile.KnownEncryptedSha256,
            "Customer EBOOT encrypted hash mismatch.");
        Require(gameEboot.PspHeader?.ModuleName == "user_main",
            "Customer EBOOT module name mismatch.");

        FileProbe zeroBoot = inspection.Files.Single(file =>
            file.LogicalPath.Equals("BOOT.BIN", StringComparison.Ordinal));
        Require(zeroBoot.Kind == FileKind.ZeroFilled,
            "Customer BOOT.BIN must be detected as zero-filled.");

        RgoAudit audit = RgoProfile.Audit(inspection);
        Require(audit.Status == "executable-verified-resources-missing",
            $"Unexpected customer audit status: {audit.Status}.");
        Require(audit.GameMetadataFound, "Game PARAM.SFO was not identified.");
        Require(audit.GameExecutableFound, "Game EBOOT was not identified.");
        Require(audit.FirmwareUpdateFilesFound, "Mixed firmware files were not detected.");
        Require(audit.MixedContentDetected, "Mixed-content warning was not raised.");
        Require(!audit.Resources.ScCpk && !audit.Resources.LtBin,
            "Missing translation resources were incorrectly reported as present.");

        RgoExecutableCheck check = audit.Executable
            ?? throw new InvalidOperationException("Customer audit has no executable result.");
        Require(check.Compatibility == ExecutableCompatibility.ExactVerifiedRevision,
            check.Message);
        Require(check.DecryptedSha256 == RgoProfile.KnownDecryptedSha256,
            "Customer decrypted hash mismatch.");
        Require(check.PatchPoints.Count == 15 && check.PatchPoints.All(point => point.Matches),
            "Not all customer patch preconditions match.");
        Require(check.ScriptSlots.Count == 11 && check.ScriptSizeTableConsistent,
            "Customer script size table is invalid.");
        Require(check.ScriptSlots[0].SizeBytes == 2_318_336,
            "Customer script slot 0 size mismatch.");
        Require(check.ScriptSlots[1].SizeBytes == 225_280,
            "Customer script slot 1 size mismatch.");
        Require(check.ScriptSlots.Skip(2).All(slot => slot.SizeBytes == 126_976),
            "Customer script slot 2..10 size mismatch.");
    }

    /// <summary>
    /// Verifies customer tamper behavior and invariants.
    /// </summary>
    /// <param name="archivePath">The archive path value.</param>
    private static void TestCustomerTamper(string archivePath)
    {
        InspectionResult inspection = InputInspector.Inspect(archivePath);
        byte[] eboot = inspection.Files.Single(file =>
            file.LogicalPath.Equals("EBOOT.BIN", StringComparison.Ordinal)).Data.ToArray();
        eboot[0x200] ^= 0x01;
        RgoExecutableCheck check = RgoProfile.VerifyExecutable(eboot);
        Require(!check.DecryptionSucceeded,
            "Tampered customer EBOOT was incorrectly accepted.");
        Require(check.Compatibility == ExecutableCompatibility.Invalid,
            "Tampered customer EBOOT received a safe compatibility level.");
    }


    /// <summary>
    /// Verifies customer script table update behavior and invariants.
    /// </summary>
    /// <param name="archivePath">The archive path value.</param>
    private static void TestCustomerScriptTableUpdate(string archivePath)
    {
        InspectionResult inspection = InputInspector.Inspect(archivePath);
        byte[] eboot = inspection.Files.Single(file =>
            file.LogicalPath.Equals("EBOOT.BIN", StringComparison.Ordinal)).Data;
        byte[] elf = RgoProfile.DecryptForPatching(eboot);
        int originalId1 = checked((int)RgoProfile.ArchiveBlockSize * 110);
        byte[] updated = RgoScriptSizeTable.Update(
            elf, scriptId: 1, newSizeBytes: originalId1 + 2 * (int)RgoProfile.ArchiveBlockSize);

        int table = RgoProfile.ScriptSizeTableOffset;
        Require(BinaryData.ReadUInt16LittleEndian(updated, table, "slot 0") == 1132,
            "Updating slot 1 changed slot 0 size.");
        Require(BinaryData.ReadUInt16LittleEndian(updated, table + 4, "slot 1") == 112,
            "Slot 1 size was not updated.");
        Require(BinaryData.ReadUInt16LittleEndian(updated, table + 2, "slot 0 cumulative") == 1132,
            "Slot 0 cumulative value changed.");
        Require(BinaryData.ReadUInt16LittleEndian(updated, table + 6, "slot 1 cumulative") == 1244,
            "Slot 1 cumulative value was not adjusted.");
        Require(BinaryData.ReadUInt16LittleEndian(updated, table + 10 * 4 + 2, "slot 10 cumulative") == 1802,
            "Following cumulative values were not adjusted.");
    }

    /// <summary>
    /// Verifies customer VWF full patch behavior and invariants.
    /// </summary>
    /// <param name="archivePath">The archive path value.</param>
    private static void TestCustomerVwfFullPatch(string archivePath)
    {
        byte[] eboot = ReadCustomerEboot(archivePath);
        RgoVwfPatchInspection original = RgoVwfPatchProfile.Inspect(eboot);
        Require(original.SourceWasEncrypted, "Customer VWF inspection did not recognize encrypted input.");
        Require(original.ExactKnownOriginal && original.CompatibleKnownLineage,
            original.Message);
        Require(original.SelectedPatchCount == 133,
            $"Expected 133 selected VWF patch words, got {original.SelectedPatchCount}.");
        Require(original.OriginalWordCount == 133
            && original.PatchedWordCount == 0
            && original.MismatchWordCount == 0,
            "Pristine customer EBOOT has an unexpected VWF patch state.");

        RgoVwfPatchResult result = RgoVwfPatchProfile.Apply(eboot);
        Require(result.SourceWasEncrypted, "VWF patch result lost encrypted-source provenance.");
        Require(result.NewlyAppliedPatchCount == 133 && result.PreviouslyAppliedPatchCount == 0,
            "Full VWF patch applied an unexpected number of words.");
        Require(result.OutputSha256 == RgoVwfPatchProfile.KnownFullPatchedSha256,
            $"Full VWF output hash mismatch: {result.OutputSha256}.");
        Require(result.Output.Length == RgoProfile.KnownDecryptedSize,
            "Full VWF patch changed the decrypted ELF length.");

        RgoVwfPatchInspection patched = RgoVwfPatchProfile.Inspect(result.Output);
        Require(patched.ExactKnownFullPatch && patched.CompatibleKnownLineage,
            patched.Message);
        Require(patched.OriginalWordCount == 0
            && patched.PatchedWordCount == 133
            && patched.MismatchWordCount == 0,
            "Patched EBOOT did not report all VWF words as applied.");

        RgoExecutableCheck genericCheck = RgoProfile.VerifyExecutable(result.Output);
        Require(genericCheck.Compatibility == ExecutableCompatibility.ExactVerifiedVwfRevision,
            $"Generic verifier did not recognize the exact VWF revision: {genericCheck.Message}");
        Require(genericCheck.ExactFullVwfPatch
            && genericCheck.VwfPatchLineageCompatible
            && genericCheck.VwfPatchedWordCount == 133,
            "Generic verifier reported incomplete VWF provenance for the exact patched EBOOT.");

        RgoVwfPatchResult idempotent = RgoVwfPatchProfile.Apply(result.Output);
        Require(idempotent.NewlyAppliedPatchCount == 0
            && idempotent.PreviouslyAppliedPatchCount == 133,
            "Second VWF application was not idempotent.");
        Require(idempotent.Output.SequenceEqual(result.Output),
            "Idempotent VWF application changed output bytes.");
    }

    /// <summary>
    /// Verifies customer VWF partial patch behavior and invariants.
    /// </summary>
    /// <param name="archivePath">The archive path value.</param>
    private static void TestCustomerVwfPartialPatch(string archivePath)
    {
        byte[] eboot = ReadCustomerEboot(archivePath);
        RgoVwfPatchResult core = RgoVwfPatchProfile.Apply(eboot, ["core-only"]);
        Require(core.SelectedGroups.SequenceEqual(["vwf-core"]),
            "core-only selected an unexpected group set.");
        Require(core.SelectedPatchCount == 66 && core.NewlyAppliedPatchCount == 66,
            "core-only did not apply exactly 66 words.");

        RgoVwfPatchInspection partial = RgoVwfPatchProfile.Inspect(core.Output);
        Require(partial.CompatibleKnownLineage,
            "Core-only output no longer belongs to the verified EBOOT lineage.");
        Require(partial.PatchedWordCount == 66 && partial.OriginalWordCount == 67,
            "Core-only output has an unexpected global patch state.");

        RgoExecutableCheck partialGenericCheck = RgoProfile.VerifyExecutable(core.Output);
        Require(partialGenericCheck.Compatibility == ExecutableCompatibility.RecognizedVwfPatchLineage,
            $"Generic verifier did not classify a partial VWF state: {partialGenericCheck.Message}");

        RgoVwfPatchResult completed = RgoVwfPatchProfile.Apply(core.Output, ["russian-text"]);
        Require(completed.PreviouslyAppliedPatchCount == 66
            && completed.NewlyAppliedPatchCount == 67,
            "Completing a partial patch did not preserve previously applied words.");
        Require(completed.OutputSha256 == RgoVwfPatchProfile.KnownFullPatchedSha256,
            "Completing a partial patch did not produce the canonical full output.");
    }

    /// <summary>
    /// Verifies customer VWF lineage behavior and invariants.
    /// </summary>
    /// <param name="archivePath">The archive path value.</param>
    private static void TestCustomerVwfLineage(string archivePath)
    {
        byte[] eboot = ReadCustomerEboot(archivePath);
        byte[] elf = RgoProfile.DecryptForPatching(eboot);
        int originalId1 = checked((int)RgoProfile.ArchiveBlockSize * 110);
        byte[] sizeAdjusted = RgoScriptSizeTable.Update(
            elf,
            scriptId: 1,
            newSizeBytes: originalId1 + 2 * (int)RgoProfile.ArchiveBlockSize);

        RgoVwfPatchInspection adjustedInspection = RgoVwfPatchProfile.Inspect(sizeAdjusted);
        Require(adjustedInspection.ScriptSizeTableModified,
            "Modified script table was not detected.");
        Require(adjustedInspection.ScriptSizeTableConsistent,
            "Valid modified script table was rejected.");
        Require(adjustedInspection.CompatibleKnownLineage,
            adjustedInspection.Message);

        byte[] originalAdjustedTable = sizeAdjusted.AsSpan(
            RgoProfile.ScriptSizeTableOffset,
            (RgoProfile.ScriptLastId - RgoProfile.ScriptFirstId + 1) * 4).ToArray();
        RgoVwfPatchResult patched = RgoVwfPatchProfile.Apply(sizeAdjusted);
        byte[] patchedTable = patched.Output.AsSpan(
            RgoProfile.ScriptSizeTableOffset,
            originalAdjustedTable.Length).ToArray();
        Require(patchedTable.SequenceEqual(originalAdjustedTable),
            "VWF application changed a valid adjusted script size table.");
        Require(patched.OutputSha256 != RgoVwfPatchProfile.KnownFullPatchedSha256,
            "Size-adjusted EBOOT unexpectedly has the pristine-table full patch hash.");

        RgoVwfPatchInspection patchedInspection = RgoVwfPatchProfile.Inspect(patched.Output);
        Require(patchedInspection.CompatibleKnownLineage
            && patchedInspection.ScriptSizeTableModified
            && patchedInspection.PatchedWordCount == 133,
            patchedInspection.Message);

        RgoExecutableCheck adjustedGenericCheck = RgoProfile.VerifyExecutable(patched.Output);
        Require(adjustedGenericCheck.Compatibility == ExecutableCompatibility.RecognizedVwfPatchLineage
            && adjustedGenericCheck.VwfPatchLineageCompatible
            && adjustedGenericCheck.ScriptSizeTableModified
            && adjustedGenericCheck.ScriptHeapConsistent
            && adjustedGenericCheck.ScriptHeapBytes == RgoProfile.KnownScriptHeapBytes,
            $"Generic verifier rejected a valid VWF + size-table lineage: {adjustedGenericCheck.Message}");

        const int expandedBlocks = 1200;
        const int expandedHeapBytes = expandedBlocks * (int)RgoProfile.ArchiveBlockSize;
        byte[] oversizedTableOnly = RgoScriptSizeTable.Update(
            elf,
            scriptId: 1,
            newSizeBytes: expandedHeapBytes);
        RgoVwfPatchInspection missingHeap = RgoVwfPatchProfile.Inspect(oversizedTableOnly);
        Require(missingHeap.ScriptSizeTableConsistent
            && !missingHeap.ScriptHeapConsistent
            && !missingHeap.CompatibleKnownLineage
            && missingHeap.RequiredScriptHeapBytes == expandedHeapBytes,
            "An oversized script table without a matching heap expansion was incorrectly accepted.");

        byte[] expanded = oversizedTableOnly.ToArray();
        BinaryData.WriteUInt32LittleEndian(
            expanded,
            RgoProfile.ScriptHeapLuiOffset,
            0x3C040026,
            "expanded script heap lui");
        BinaryData.WriteUInt32LittleEndian(
            expanded,
            RgoProfile.ScriptHeapAddiuOffset,
            0x24848000,
            "expanded script heap addiu");
        RgoVwfPatchInspection expandedInspection = RgoVwfPatchProfile.Inspect(expanded);
        Require(expandedInspection.CompatibleKnownLineage
            && expandedInspection.ScriptHeapModified
            && expandedInspection.ScriptHeapConsistent
            && expandedInspection.ScriptHeapBytes == expandedHeapBytes
            && expandedInspection.RequiredScriptHeapBytes == expandedHeapBytes,
            expandedInspection.Message);

        RgoVwfPatchResult expandedPatched = RgoVwfPatchProfile.Apply(expanded);
        Require(BinaryData.ReadUInt32LittleEndian(
                expandedPatched.Output,
                RgoProfile.ScriptHeapLuiOffset,
                "expanded patched heap lui") == 0x3C040026
            && BinaryData.ReadUInt32LittleEndian(
                expandedPatched.Output,
                RgoProfile.ScriptHeapAddiuOffset,
                "expanded patched heap addiu") == 0x24848000,
            "VWF application changed a valid expanded script heap.");
        RgoExecutableCheck expandedGenericCheck = RgoProfile.VerifyExecutable(expandedPatched.Output);
        Require(expandedGenericCheck.Compatibility == ExecutableCompatibility.RecognizedVwfPatchLineage
            && expandedGenericCheck.ScriptHeapModified
            && expandedGenericCheck.ScriptHeapConsistent
            && expandedGenericCheck.ScriptHeapBytes == expandedHeapBytes,
            $"Generic verifier rejected a valid expanded script heap: {expandedGenericCheck.Message}");

        byte[] invalidHeapPair = expanded.ToArray();
        BinaryData.WriteUInt32LittleEndian(
            invalidHeapPair,
            RgoProfile.ScriptHeapLuiOffset,
            0x3C050026,
            "invalid script heap lui");
        RgoVwfPatchInspection invalidHeapInspection = RgoVwfPatchProfile.Inspect(invalidHeapPair);
        Require(!invalidHeapInspection.ScriptHeapConsistent
            && !invalidHeapInspection.CompatibleKnownLineage,
            "An invalid script-heap register pair was incorrectly accepted.");
        RequireThrows<ToolkitException>(() => RgoVwfPatchProfile.Apply(invalidHeapPair));

        byte[] unrelatedTamper = elf.ToArray();
        unrelatedTamper[0x500] ^= 0x01;
        RgoVwfPatchInspection unrelatedInspection = RgoVwfPatchProfile.Inspect(unrelatedTamper);
        Require(!unrelatedInspection.CompatibleKnownLineage,
            "Unrelated EBOOT tampering was incorrectly accepted as known lineage.");
        RequireThrows<ToolkitException>(() => RgoVwfPatchProfile.Apply(unrelatedTamper));

        byte[] patchWordTamper = elf.ToArray();
        RgoEbootPatchDefinition first = RgoVwfPatchProfile.Patches[0];
        BinaryData.WriteUInt32LittleEndian(
            patchWordTamper,
            first.Offset,
            0xDEADBEEF,
            "tampered VWF word");
        RgoVwfPatchInspection wordInspection = RgoVwfPatchProfile.Inspect(patchWordTamper);
        Require(wordInspection.MismatchWordCount == 1
            && !wordInspection.CompatibleKnownLineage,
            "Unexpected VWF patch word was not rejected.");
        RequireThrows<ToolkitException>(() => RgoVwfPatchProfile.Apply(patchWordTamper));
    }

    /// <summary>
    /// Reads customer EBOOT while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="archivePath">The archive path value.</param>
    /// <returns>The resulting binary or typed sequence.</returns>
    private static byte[] ReadCustomerEboot(string archivePath)
    {
        InspectionResult inspection = InputInspector.Inspect(archivePath);
        return inspection.Files.Single(file =>
            file.LogicalPath.Equals("EBOOT.BIN", StringComparison.Ordinal)).Data.ToArray();
    }

    /// <summary>
    /// Parses customer archive while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="args">The command-line arguments to parse and execute.</param>
    /// <returns>The resulting text, path, identifier, or hexadecimal digest.</returns>
    /// <remarks>Malformed input or violated preconditions are rejected before a persistent output is committed.</remarks>
    private static string? ParseCustomerArchive(string[] args)
    {
        if (args.Length == 0)
        {
            return null;
        }
        if (args.Length != 2 || args[0] != "--customer-archive")
        {
            throw new ArgumentException(
                "Test usage: LuckyStarPspToolkit.SelfTests [--customer-archive <archive.zip>]");
        }
        return Path.GetFullPath(args[1]);
    }

    /// <summary>
    /// Parses and executes a command-line invocation and returns a stable process exit code.
    /// </summary>
    /// <param name="name">The logical name used for lookup or diagnostics.</param>
    /// <param name="test">The test value.</param>
    private static void Run(string name, Action test)
    {
        try
        {
            test();
            Console.WriteLine($"PASS {name}");
        }
        catch (Exception exception)
        {
            _failures++;
            Console.Error.WriteLine($"FAIL {name}: {exception.Message}");
        }
    }

    /// <summary>
    /// Returns a required value or throws a stable validation error when it is absent.
    /// </summary>
    /// <param name="condition">The condition that must be true.</param>
    /// <param name="message">The diagnostic message used when validation fails.</param>
    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    /// <summary>
    /// Requires throws while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <typeparam name="TException">The t exception type used by the operation.</typeparam>
    /// <param name="action">The action value.</param>
    private static void RequireThrows<TException>(Action action) where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }
        throw new InvalidOperationException($"Expected {typeof(TException).Name} was not thrown.");
    }

    /// <summary>
    /// Runs a test in a unique temporary directory and removes its files in a finally block.
    /// </summary>
    /// <param name="action">The action value.</param>
    private static void WithTemporaryDirectory(Action<string> action)
    {
        string path = Path.Combine(Path.GetTempPath(), $"lsptool-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        try
        {
            action(path);
        }
        finally
        {
            try
            {
                Directory.Delete(path, recursive: true);
            }
            catch
            {
                // Do not hide the test result because cleanup failed.
            }
        }
    }
}
