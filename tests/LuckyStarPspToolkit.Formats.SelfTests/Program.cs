using System.Buffers.Binary;
using System.IO.Compression;
using System.Text.Json;
using LuckyStarPspToolkit.Formats.Audit;
using LuckyStarPspToolkit.Formats.Common;
using LuckyStarPspToolkit.Formats.Cri;
using LuckyStarPspToolkit.Formats.Fonts;
using LuckyStarPspToolkit.Formats.Iso;
using LuckyStarPspToolkit.Formats.Scripts;
using LuckyStarPspToolkit.Formats.Text;
using LuckyStarPspToolkit.Formats.Workspace;
using LuckyStarPspToolkit.Cli;
using ExecutableCompatibility = LuckyStarPspToolkit.ExecutableCompatibility;
using RgoExecutableCheck = LuckyStarPspToolkit.RgoExecutableCheck;
using RgoProfile = LuckyStarPspToolkit.RgoProfile;
using RgoVwfPatchInspection = LuckyStarPspToolkit.RgoVwfPatchInspection;
using RgoVwfPatchProfile = LuckyStarPspToolkit.RgoVwfPatchProfile;

return SelfTestRunner.Run(args);

/// <summary>
/// Represents the toolkit's self test runner model or service.
/// </summary>
internal static partial class SelfTestRunner
{
    /// <summary>Stores the passed state owned by this instance or type.</summary>
    private static int _passed;
    /// <summary>The fixtures value used by this model or operation.</summary>
    private static string Fixtures => Path.Combine(AppContext.BaseDirectory, "Fixtures");

    /// <summary>
    /// Parses and executes a command-line invocation and returns a stable process exit code.
    /// </summary>
    /// <param name="args">The command-line arguments to parse and execute.</param>
    /// <returns>A stable process exit code.</returns>
    public static int Run(string[] args)
    {
        try
        {
            if (args.Contains("--cpk-benchmark", StringComparer.Ordinal) || args.Contains("--cpk-probe", StringComparer.Ordinal))
                return CpkScaleFixture.Run(args);
            if (args.Contains("--script-benchmark", StringComparer.Ordinal) || args.Contains("--probe", StringComparer.Ordinal))
                return ScriptScaleFixture.Run(args);
            Test("duplicate and escaped JSON properties", TestStrictJson);
            Test("filesystem root and portable path containment", TestRootPathContainment);
            Test("immutable glyph-map snapshot", TestGlyphSnapshot);
            Test("workspace outputs cannot overwrite translation inputs", TestWorkspaceOutputIsolation);
            Test("glyph map round-trip, analysis, and unsupported glyph", TestGlyphMap);
            Test("lt.bin parse/build and PNG preview", TestLtFont);
            Test("BDF Cyrillic import and Russian readiness", TestBdfFontPatch);
            Test("ISO 9660 parse, lookup, and multi-extent extraction", TestIso9660);
            Test("transactional deterministic ISO 9660 rebuild", TestIsoRebuild);
            Test("PSP asset bundle collection and atomic stream writes", TestAssetCollection);
            Test("RGO checksum validation", TestChecksum);
            Test("CRI UTF semantic round-trip", TestUtfRoundTrip);
            Test("UTF cell and decoded-data allocation limits", TestUtfAllocationLimits);
            Test("malformed UTF header fuzz under bounded memory", TestUtfMalformedFuzz);
            Test("independent script fixture parse", TestScriptParse);
            Test("script mutation and relocation", TestScriptMutation);
            Test("deterministic script mutation fuzz", TestScriptFuzz);
            Test("unchanged text targets survive no-op and unrelated edits", TestScriptUnchangedJump);
            Test("scenario checksum cannot terminate message text", TestScriptChecksumNotText);
            Test("scenario fields stop at the next physical record", TestScriptPhysicalBoundaries);
            Test("scenario metadata ranges, alignment and profile limits", TestScriptMetadataPreflight);
            Test("scenario duplicate and misaligned offset preflight", TestScriptOffsetPreflight);
            Test("scenario input and glyph allocation budgets", TestScriptGlyphBudgets);
            Test("scenario output size preflight before large allocation", TestScriptOutputPreflight);
            Test("scenario direct API mutations reject structural delimiters", TestScriptDirectMutationSafety);
            Test("empty choice anchors and reordered dialogue tables", TestScriptEmptyAndReordered);
            Test("opaque, short-aligned and NIM scenario round-trips", TestScriptOpaqueAndNim);
            Test("indexed relocation against independent linear oracle", TestScriptIndexedMap);
            Test("8192-dialogue scenario and all 16385 jump targets", TestScriptLargeRebuild);
            Test("1024 deterministic scenario header mutations", TestScriptHeaderFuzz);
            Test("independent CPK/ITOC and CRILAYLA fixture", TestCpkParse);
            Test("CPK replacement and verified rebuild", TestCpkRebuild);
            Test("CPK byte-identical no-op and independent output ownership", TestCpkNoOpPreservation);
            Test("CPK auxiliary metadata across DataL and DataH", TestCpkAuxiliaryMigration);
            Test("CPK unknown schema migration fails closed", TestCpkIncompatibleMetadata);
            Test("CPK input, metadata and integer preflight", TestCpkPreflight);
            Test("CPK unsupported supplementary indices and aggregate budget", TestCpkExtraIndex);
            Test("CPK malformed class counts, IDs and descriptor types", TestCpkDescriptorErrors);
            Test("CPK metadata-only inspection and single-file extraction", TestCpkInspection);
            Test("CPK direct entry mutation safety", TestCpkEntryMutation);
            Test("CPK repeated UInt16 size threshold migrations", TestCpkThresholds);
            Test("CPK 512 deterministic header mutations", TestCpkHeaderFuzz);
            Test("CPK allocation scaling excludes duplicate payload copies", TestCpkAllocationScaling);
            Test("workspace validation and JSON end-to-end", TestWorkspace);
            Test("workspace tamper, strict JSON, and control-code rejection", TestWorkspaceSafety);
            Test("transactional multi-file output", TestAtomicWriteSet);
            Test("post-commit backup cleanup failure preserves outputs", TestBackupCleanupFailure);
            Test("overlapping transaction paths fail before filesystem changes", TestAtomicTargetOverlap);
            Test("bounded file reads and deterministic SHA-256", TestBoundedFileIo);
            Test("EBOOT size plan safety", TestEbootPlan);
            Test("ZIP traversal rejection", TestZipSafety);
            Test("negative binary validation", TestNegativeValidation);
            Test("unified CLI command surface and report collision safety", TestUnifiedCli);
            string? customer = ReadOption(args, "--customer-archive");
            if (customer is not null)
            {
                Test("customer archive regression", () => TestCustomerArchive(customer));
                Test("customer EBOOT VWF CLI end-to-end", () => TestCustomerVwfCli(customer));
            }
            Console.WriteLine($"SELF-TEST RESULT: {_passed} passed, 0 failed");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"SELF-TEST FAILED after {_passed} passed tests: {ex}");
            return 1;
        }
    }

    /// <summary>
    /// Verifies  behavior and invariants.
    /// </summary>
    /// <param name="name">The logical name used for lookup or diagnostics.</param>
    /// <param name="action">The action value.</param>
    private static void Test(string name, Action action)
    {
        action();
        _passed++;
        Console.WriteLine($"PASS: {name}");
    }

    /// <summary>
    /// Verifies glyph map behavior and invariants.
    /// </summary>
    private static void TestGlyphMap()
    {
        GlyphMap map = GlyphMap.Load(Path.Combine(Fixtures, "glyph-map.txt"));
        ushort[] encoded = map.Encode("АБа\nб{{FIRST_NAME}}");
        Equal("АБа\nб{{FIRST_NAME}}", map.Decode(encoded));
        Throws("GLYPH_NOT_FOUND", () => map.Encode("Ω"));
        Throws(
            "GLYPH_MAP_LIMIT",
            () => GlyphMap.FromLines([" ", "A"], new FileLimits(MaximumGlyphMapEntries: 1)));
        GlyphMapAnalysis analysis = GlyphMap.FromLines([" ", "А", "А"]).Analyze();
        Equal(1, analysis.Duplicates.Count);
        SequenceEqual(new ushort[] { 1, 2 }, analysis.Duplicates[0].Indices);
    }

    /// <summary>
    /// Verifies lt font behavior and invariants.
    /// </summary>
    private static void TestLtFont()
    {
        GlyphMap map = GlyphMap.Load(Path.Combine(Fixtures, "glyph-map.txt"));
        byte[] source = File.ReadAllBytes(Path.Combine(Fixtures, "reference-lt.bin"));
        LtFont font = LtFont.Parse(source, map.Count);
        Equal(map.Count, font.Glyphs.Count);
        SequenceEqual(source, font.Build());

        LtFontAnalysis analysis = font.Analyze(map);
        Equal(66, analysis.RequiredRussianCharacterCount);
        Equal(66, analysis.MappedRussianCharacterCount);
        Equal(0, analysis.RenderableRussianCharacterCount);
        Equal(2, analysis.NonZeroReservedByteCount);
        Equal(2, analysis.NonZeroUnusedPixelNibbleCount);
        Equal(1, analysis.NonZeroPaddingByteCount);
        True(!analysis.RussianReady, "Blank Russian glyph fixture must not be marked ready.");
        True(font.GetGlyph(1).GetInkBounds() is not null, "Latin fixture glyph must contain ink.");
        True(font.GetGlyph(3).GetInkBounds() is null, "Russian source fixture glyph must be blank.");
        Equal((byte)0x5A, font.GetGlyph(3).Reserved);
        Equal((byte)0x50, font.GetGlyph(3).UnusedPixelBits[1]);

        byte[] damaged = source.ToArray();
        damaged[100] ^= 1;
        Throws("FONT_CHECKSUM", () => LtFont.Parse(damaged, map.Count));
        Throws("FONT_MAP_COUNT", () => font.Analyze(GlyphMap.FromLines([" ", "A"])));
    }

    /// <summary>
    /// Verifies BDF font patch behavior and invariants.
    /// </summary>
    private static void TestBdfFontPatch()
    {
        GlyphMap map = GlyphMap.Load(Path.Combine(Fixtures, "glyph-map.txt"));
        LtFont source = LtFont.Parse(
            File.ReadAllBytes(Path.Combine(Fixtures, "reference-lt.bin")),
            map.Count);
        BdfFont bdf = BdfFont.Load(Path.Combine(Fixtures, "reference-font.bdf"));
        Equal(66, bdf.DeclaredGlyphCount);
        Equal(66, bdf.EncodedGlyphCount);
        Throws(
            "BDF_CHAR_COUNT",
            () => BdfFont.Load(
                Path.Combine(Fixtures, "reference-font.bdf"),
                new FileLimits(MaximumBdfGlyphs: 65)));

        LtFontPatchResult patch = LtFontPatcher.ApplyBdf(source, map, bdf);
        True(patch.Complete, "Complete Russian BDF fixture must produce a complete patch.");
        Equal(66, patch.SelectedEntryCount);
        Equal(66, patch.PatchedGlyphCount);
        Equal(0, patch.ExistingGlyphCount);
        Equal(0, patch.MissingMapCount);
        Equal(0, patch.MissingBdfCount);
        Equal(0, patch.ClippedGlyphCount);

        byte[] actual = patch.Font.Build();
        byte[] expected = File.ReadAllBytes(Path.Combine(Fixtures, "reference-lt-russian.bin"));
        SequenceEqual(expected, actual);
        LtFont verified = LtFont.Parse(actual, map.Count);
        LtFontAnalysis analysis = verified.Analyze(map);
        True(analysis.RussianReady, "Patched font must be Russian-ready.");
        Equal(66, analysis.RenderableRussianCharacterCount);
        Equal((byte)0x5A, verified.GetGlyph(3).Reserved);
        Equal((byte)0x50, verified.GetGlyph(3).UnusedPixelBits[1]);

        RgbaImage atlas = verified.RenderAtlas(columns: 16, scale: 2, gutter: 1);
        Equal(593, atlas.Width);
        Equal(186, atlas.Height);
        SequenceEqual(
            File.ReadAllBytes(Path.Combine(Fixtures, "reference-lt-russian-atlas.rgba")),
            atlas.Pixels);
        ValidatePng(PngWriter.Encode(atlas), atlas);

        LtFontPatchResult secondPass = LtFontPatcher.ApplyBdf(verified, map, bdf);
        True(secondPass.Complete, "A second non-replacing font pass must remain complete.");
        Equal(0, secondPass.PatchedGlyphCount);
        Equal(66, secondPass.ExistingGlyphCount);
        SequenceEqual(actual, secondPass.Font.Build());

        LtFontPatchResult clipped = LtFontPatcher.ApplyBdf(
            source,
            map,
            bdf,
            new LtFontPatchOptions(Baseline: 0));
        True(!clipped.Complete, "Clipping must make a strict font patch incomplete.");
        True(clipped.ClippedGlyphCount > 0, "Clipping test did not detect any clipped glyphs.");
        True(clipped.MissingCharacters.Count > 0, "Blocked clipped glyphs must be reported as missing.");
    }

    /// <summary>
    /// Verifies ISO 9660 behavior and invariants.
    /// </summary>
    private static void TestIso9660()
    {
        string path = Path.Combine(Fixtures, "reference.iso");
        using JsonDocument oracle = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(Fixtures, "oracle.json")));
        JsonElement root = oracle.RootElement;

        using (Iso9660Image image = Iso9660Image.Open(path))
        {
            Equal("LSPTOOL_ORACLE", image.VolumeIdentifier);
            Equal(2048, image.LogicalBlockSize);
            Equal(root.GetProperty("isoLength").GetInt64(), image.SourceSize);
            Equal(root.GetProperty("isoSha256").GetString() ?? string.Empty, image.ComputeSourceSha256());
            Equal(13, image.Entries.Count);
            True(image.EstimateListingBytes() > 1024, "ISO listing estimate must include parsed entries.");

            Iso9660Entry canonicalLt = image.GetEntry("PSP_GAME/USRDIR/DATA/lt.bin");
            Iso9660Entry foreignLt = new()
            {
                Path = canonicalLt.Path,
                Identifier = canonicalLt.Identifier,
                RawIdentifier = canonicalLt.RawIdentifier,
                IsDirectory = false,
                IsHidden = canonicalLt.IsHidden,
                Flags = canonicalLt.Flags,
                Size = canonicalLt.Size,
                Extents = canonicalLt.Extents
            };
            Throws("ISO_ENTRY_FOREIGN", () => image.CopyFileTo(foreignLt, Stream.Null));

            foreach (JsonProperty expectedFile in root.GetProperty("isoFiles").EnumerateObject())
            {
                Iso9660Entry entry = image.GetEntry(expectedFile.Name.ToLowerInvariant() + ";1");
                True(!entry.IsDirectory, $"Oracle ISO path unexpectedly resolved to a directory: {entry.Path}");
                Equal(expectedFile.Value.GetProperty("size").GetInt64(), entry.Size);
                Equal(
                    expectedFile.Value.GetProperty("sha256").GetString() ?? string.Empty,
                    image.ComputeFileSha256(expectedFile.Name));
            }

            Iso9660Entry multi = image.GetEntry("PSP_GAME\\USRDIR\\DATA\\MULTI.BIN;1");
            Equal(2, multi.Extents.Count);
            True(multi.IsMultiExtent, "Synthetic MULTI.BIN must be represented by two ISO extents.");
            byte[] multiBytes = image.ReadFile(multi.Path, 4096);
            Equal(3000, multiBytes.Length);
            Equal(
                root.GetProperty("isoFiles").GetProperty("PSP_GAME/USRDIR/DATA/MULTI.BIN")
                    .GetProperty("sha256").GetString() ?? string.Empty,
                BinaryUtilities.Sha256Hex(multiBytes));

            Iso9660Summary summary = image.CreateSummary();
            Equal(image.Entries.Count, summary.EntryCount);
            Equal(image.VolumeBlockCount, summary.VolumeBlockCount);
            byte[] originalIso = File.ReadAllBytes(path);
            Throws("ISO_OUTPUT_SOURCE", () => image.ExtractFile("PSP_GAME/PARAM.SFO", path));
            SequenceEqual(originalIso, File.ReadAllBytes(path));
        }

        byte[] source = File.ReadAllBytes(path);
        using (Iso9660Image memory = Iso9660Image.Parse(source))
        {
            Equal("LSPTOOL_ORACLE", memory.VolumeIdentifier);
            Equal(3000L, memory.GetEntry("psp_game/usrdir/data/multi.bin").Size);
        }

        byte[] endianMismatch = source.ToArray();
        endianMismatch[(16 * Iso9660Image.DescriptorSectorSize) + 84] ^= 1;
        Throws("ISO_BOTH_ENDIAN", () => Iso9660Image.Parse(endianMismatch).Dispose());

        byte[] multiVolume = source.ToArray();
        int setSizeOffset = (16 * Iso9660Image.DescriptorSectorSize) + 120;
        BinaryPrimitives.WriteUInt16LittleEndian(multiVolume.AsSpan(setSizeOffset, 2), 2);
        BinaryPrimitives.WriteUInt16BigEndian(multiVolume.AsSpan(setSizeOffset + 2, 2), 2);
        Throws("ISO_MULTI_VOLUME", () => Iso9660Image.Parse(multiVolume).Dispose());

        byte[] truncatedVolume = source.ToArray();
        int volumeOffset = (16 * Iso9660Image.DescriptorSectorSize) + 80;
        uint impossibleBlocks = checked((uint)(source.Length / Iso9660Image.DescriptorSectorSize + 10));
        BinaryPrimitives.WriteUInt32LittleEndian(truncatedVolume.AsSpan(volumeOffset, 4), impossibleBlocks);
        BinaryPrimitives.WriteUInt32BigEndian(truncatedVolume.AsSpan(volumeOffset + 4, 4), impossibleBlocks);
        Throws("ISO_VOLUME_TRUNCATED", () => Iso9660Image.Parse(truncatedVolume).Dispose());

        byte[] invalidRootExtent = source.ToArray();
        int rootExtentOffset = (16 * Iso9660Image.DescriptorSectorSize) + 156 + 2;
        uint outOfRangeExtent = checked((uint)(source.Length / Iso9660Image.DescriptorSectorSize + 1));
        BinaryPrimitives.WriteUInt32LittleEndian(invalidRootExtent.AsSpan(rootExtentOffset, 4), outOfRangeExtent);
        BinaryPrimitives.WriteUInt32BigEndian(invalidRootExtent.AsSpan(rootExtentOffset + 4, 4), outOfRangeExtent);
        Throws("ISO_EXTENT_RANGE", () => Iso9660Image.Parse(invalidRootExtent).Dispose());

        byte[] missingTerminator = source[..(18 * Iso9660Image.DescriptorSectorSize)].ToArray();
        missingTerminator[17 * Iso9660Image.DescriptorSectorSize] = 0;
        Throws("ISO_TERMINATOR_MISSING", () => Iso9660Image.Parse(missingTerminator).Dispose());

        byte[] duplicatePvd = source.ToArray();
        source.AsSpan(16 * Iso9660Image.DescriptorSectorSize, Iso9660Image.DescriptorSectorSize)
            .CopyTo(duplicatePvd.AsSpan(17 * Iso9660Image.DescriptorSectorSize));
        Throws("ISO_MULTIPLE_PVD", () => Iso9660Image.Parse(duplicatePvd).Dispose());

        byte[] wrongBlockSize = source.ToArray();
        int blockSizeOffset = (16 * Iso9660Image.DescriptorSectorSize) + 128;
        BinaryPrimitives.WriteUInt16LittleEndian(wrongBlockSize.AsSpan(blockSizeOffset, 2), 1024);
        BinaryPrimitives.WriteUInt16BigEndian(wrongBlockSize.AsSpan(blockSizeOffset + 2, 2), 1024);
        Throws("ISO_BLOCK_SIZE", () => Iso9660Image.Parse(wrongBlockSize).Dispose());

        byte[] interleaved = source.ToArray();
        int scName = interleaved.AsSpan().IndexOf("SC.CPK;1"u8);
        True(scName >= 33, "Synthetic ISO does not contain the expected SC.CPK record.");
        interleaved[scName - 33 + 26] = 1;
        Throws("ISO_INTERLEAVE", () => Iso9660Image.Parse(interleaved).Dispose());

        byte[] unfinishedMultiExtent = source.ToArray();
        int multiFirstName = unfinishedMultiExtent.AsSpan().IndexOf("MULTI.BIN;1"u8);
        True(multiFirstName >= 0, "Synthetic ISO does not contain the first MULTI.BIN record.");
        int multiSecondRelative = unfinishedMultiExtent.AsSpan(multiFirstName + 1).IndexOf("MULTI.BIN;1"u8);
        True(multiSecondRelative >= 0, "Synthetic ISO does not contain the second MULTI.BIN record.");
        int multiSecondName = multiFirstName + 1 + multiSecondRelative;
        unfinishedMultiExtent[multiSecondName - 33 + 25] |= 0x80;
        Throws("ISO_MULTI_EXTENT", () => Iso9660Image.Parse(unfinishedMultiExtent).Dispose());

        byte[] duplicatePath = source.ToArray();
        int prName = duplicatePath.AsSpan().IndexOf("PR.BIN;1"u8);
        True(prName >= 0, "Synthetic ISO does not contain PR.BIN.");
        "LT.BIN;1"u8.CopyTo(duplicatePath.AsSpan(prName));
        Throws("ISO_DUPLICATE_PATH", () => Iso9660Image.Parse(duplicatePath).Dispose());

        byte[] directoryCycle = source.ToArray();
        int dataRecordExtent = (23 * Iso9660Image.DescriptorSectorSize) + 68 + 2;
        BinaryPrimitives.WriteUInt32LittleEndian(directoryCycle.AsSpan(dataRecordExtent, 4), 23);
        BinaryPrimitives.WriteUInt32BigEndian(directoryCycle.AsSpan(dataRecordExtent + 4, 4), 23);
        Throws("ISO_DIRECTORY_CYCLE", () => Iso9660Image.Parse(directoryCycle).Dispose());

        Throws(
            "ISO_ENTRY_LIMIT",
            () => Iso9660Image.Parse(source, new FileLimits(MaximumIsoEntries: 1)).Dispose());
        Throws(
            "ISO_DIRECTORY_DEPTH",
            () => Iso9660Image.Parse(source, new FileLimits(MaximumIsoDirectoryDepth: 1)).Dispose());
        Throws(
            "ISO_EXTENT_LIMIT",
            () => Iso9660Image.Parse(source, new FileLimits(MaximumIsoExtents: 2)).Dispose());
        Throws(
            "ISO_DIRECTORY_TOO_LARGE",
            () => Iso9660Image.Parse(source, new FileLimits(MaximumIsoDirectoryBytes: 100)).Dispose());
        Throws(
            "ISO_DIRECTORY_TOTAL_LIMIT",
            () => Iso9660Image.Parse(source, new FileLimits(MaximumIsoTotalDirectoryBytes: 100)).Dispose());
    }

    /// <summary>
    /// Verifies ISO rebuild behavior and invariants.
    /// </summary>
    private static void TestIsoRebuild()
    {
        string sourcePath = Path.Combine(Fixtures, "reference.iso");
        string manifestPath = Path.Combine(Fixtures, "iso-patch-manifest.json");
        string expectedPath = Path.Combine(Fixtures, "reference-patched.iso");
        string replacementSc = Path.Combine(Fixtures, "iso-replacement-sc.cpk");
        string replacementLt = Path.Combine(Fixtures, "iso-replacement-lt.bin");
        using JsonDocument oracle = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(Fixtures, "oracle.json")));
        JsonElement root = oracle.RootElement;
        byte[] sourceBefore = File.ReadAllBytes(sourcePath);

        string temp = Path.Combine(Path.GetTempPath(), "lsptool-iso-rebuild-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            string outputPath = Path.Combine(temp, "patched.iso");
            Iso9660RebuildReport report = Iso9660Rebuilder.ApplyManifest(
                sourcePath,
                manifestPath,
                outputPath);
            Equal(root.GetProperty("patchedIsoLength").GetInt64(), report.OutputSize);
            Equal(root.GetProperty("patchedIsoVolumeBlocks").GetUInt32(), report.OutputVolumeBlockCount);
            Equal(root.GetProperty("patchedIsoSha256").GetString() ?? string.Empty, report.OutputSha256);
            Equal(2, report.Replacements.Count);
            SequenceEqual(File.ReadAllBytes(expectedPath), File.ReadAllBytes(outputPath));
            SequenceEqual(sourceBefore, File.ReadAllBytes(sourcePath));

            string repeatedOutputPath = Path.Combine(temp, "patched-repeat.iso");
            Iso9660RebuildReport repeatedReport = Iso9660Rebuilder.ApplyManifest(
                sourcePath,
                manifestPath,
                repeatedOutputPath);
            Equal(report.OutputSha256, repeatedReport.OutputSha256);
            SequenceEqual(File.ReadAllBytes(outputPath), File.ReadAllBytes(repeatedOutputPath));

            string replaceExistingOutput = Path.Combine(temp, "replace-existing.iso");
            File.WriteAllText(replaceExistingOutput, "old output must be replaced atomically");
            Iso9660RebuildReport replaceExistingReport = Iso9660Rebuilder.ApplyManifest(
                sourcePath,
                manifestPath,
                replaceExistingOutput);
            Equal(report.OutputSha256, replaceExistingReport.OutputSha256);
            SequenceEqual(File.ReadAllBytes(expectedPath), File.ReadAllBytes(replaceExistingOutput));

            string snapshotDirectory = Path.Combine(temp, "manifest-snapshot");
            Directory.CreateDirectory(snapshotDirectory);
            string snapshotManifest = Path.Combine(snapshotDirectory, "manifest.json");
            File.Copy(manifestPath, snapshotManifest);
            File.Copy(replacementSc, Path.Combine(snapshotDirectory, "iso-replacement-sc.cpk"));
            File.Copy(replacementLt, Path.Combine(snapshotDirectory, "iso-replacement-lt.bin"));
            Iso9660PreparedPatch preparedPatch = Iso9660Rebuilder.LoadPatchManifest(snapshotManifest);
            Equal(2, preparedPatch.Replacements.Count);
            Equal(BinaryUtilities.Sha256HexFile(snapshotManifest), preparedPatch.ManifestSha256);
            File.WriteAllText(
                snapshotManifest,
                "{\"schema\":\"lucky-star-psp.iso-patch-manifest.v1\",\"replacements\":[]}");
            string snapshotOutput = Path.Combine(temp, "manifest-snapshot.iso");
            Iso9660RebuildReport snapshotReport = Iso9660Rebuilder.ApplyPreparedPatch(
                sourcePath,
                preparedPatch,
                snapshotOutput);
            Equal(report.OutputSha256, snapshotReport.OutputSha256);
            Equal(preparedPatch.ManifestSha256, snapshotReport.ManifestSha256 ?? string.Empty);
            SequenceEqual(File.ReadAllBytes(expectedPath), File.ReadAllBytes(snapshotOutput));
            Throws(
                "ISO_MANIFEST_EMPTY",
                () => Iso9660Rebuilder.ApplyManifest(
                    sourcePath,
                    snapshotManifest,
                    Path.Combine(temp, "changed-manifest.iso")));

            using (Iso9660Image rebuilt = Iso9660Image.Open(outputPath))
            {
                Equal(report.OutputVolumeBlockCount, rebuilt.VolumeBlockCount);
                Equal(BinaryUtilities.Sha256HexFile(replacementSc), rebuilt.ComputeFileSha256("PSP_GAME/USRDIR/DATA/sc.cpk"));
                Equal(BinaryUtilities.Sha256HexFile(replacementLt), rebuilt.ComputeFileSha256("PSP_GAME/USRDIR/DATA/lt.bin"));
                Equal(
                    root.GetProperty("isoFiles").GetProperty("PSP_GAME/USRDIR/DATA/MULTI.BIN")
                        .GetProperty("sha256").GetString() ?? string.Empty,
                    rebuilt.ComputeFileSha256("PSP_GAME/USRDIR/DATA/MULTI.BIN"));
            }

            string directOutput = Path.Combine(temp, "direct.iso");
            string originalLtSha = root.GetProperty("isoFiles")
                .GetProperty("PSP_GAME/USRDIR/DATA/lt.bin")
                .GetProperty("sha256").GetString() ?? string.Empty;
            Iso9660RebuildReport direct = Iso9660Rebuilder.Rebuild(
                sourcePath,
                [new Iso9660ReplacementRequest(
                    "psp_game/usrdir/data/lt.bin;1",
                    replacementLt,
                    root.GetProperty("isoFiles").GetProperty("PSP_GAME/USRDIR/DATA/lt.bin").GetProperty("size").GetInt64(),
                    originalLtSha)],
                directOutput,
                root.GetProperty("isoSha256").GetString());
            Equal(1, direct.Replacements.Count);
            using (Iso9660Image rebuilt = Iso9660Image.Open(directOutput))
            {
                Equal(BinaryUtilities.Sha256HexFile(replacementLt), rebuilt.ComputeFileSha256("PSP_GAME/USRDIR/DATA/lt.bin"));
            }

            string emptyReplacement = Path.Combine(temp, "empty.bin");
            File.WriteAllBytes(emptyReplacement, []);
            string emptyOutput = Path.Combine(temp, "empty-replacement.iso");
            Iso9660RebuildReport emptyReport = Iso9660Rebuilder.Rebuild(
                sourcePath,
                [new Iso9660ReplacementRequest("PSP_GAME/USRDIR/DATA/lt.bin", emptyReplacement)],
                emptyOutput);
            Equal((long)sourceBefore.Length, emptyReport.OutputSize);
            using (Iso9660Image rebuilt = Iso9660Image.Open(emptyOutput))
            {
                Equal(0L, rebuilt.GetEntry("PSP_GAME/USRDIR/DATA/lt.bin").Size);
                Equal(BinaryUtilities.Sha256Hex(Array.Empty<byte>()), rebuilt.ComputeFileSha256("PSP_GAME/USRDIR/DATA/lt.bin"));
            }

            Throws(
                "ISO_OUTPUT_SOURCE",
                () => Iso9660Rebuilder.Rebuild(
                    sourcePath,
                    [new Iso9660ReplacementRequest("PSP_GAME/USRDIR/DATA/lt.bin", replacementLt)],
                    sourcePath));
            Throws(
                "ISO_REPLACE_MULTI_EXTENT",
                () => Iso9660Rebuilder.Rebuild(
                    sourcePath,
                    [new Iso9660ReplacementRequest("PSP_GAME/USRDIR/DATA/MULTI.BIN", replacementLt)],
                    Path.Combine(temp, "multi.iso")));
            Throws(
                "ISO_SOURCE_SHA256",
                () => Iso9660Rebuilder.Rebuild(
                    sourcePath,
                    [new Iso9660ReplacementRequest("PSP_GAME/USRDIR/DATA/lt.bin", replacementLt)],
                    Path.Combine(temp, "wrong-source.iso"),
                    new string('0', 64)));
            Throws(
                "ISO_ORIGINAL_SIZE",
                () => Iso9660Rebuilder.Rebuild(
                    sourcePath,
                    [new Iso9660ReplacementRequest("PSP_GAME/USRDIR/DATA/lt.bin", replacementLt, 1)],
                    Path.Combine(temp, "wrong-size.iso")));
            Throws(
                "ISO_ORIGINAL_SHA256",
                () => Iso9660Rebuilder.Rebuild(
                    sourcePath,
                    [new Iso9660ReplacementRequest(
                        "PSP_GAME/USRDIR/DATA/lt.bin",
                        replacementLt,
                        null,
                        new string('0', 64))],
                    Path.Combine(temp, "wrong-hash.iso")));
            Throws(
                "ISO_REPLACEMENT_SIZE",
                () => Iso9660Rebuilder.Rebuild(
                    sourcePath,
                    [new Iso9660ReplacementRequest(
                        "PSP_GAME/USRDIR/DATA/lt.bin",
                        replacementLt,
                        ExpectedReplacementSize: 1)],
                    Path.Combine(temp, "wrong-replacement-size.iso")));
            Throws(
                "ISO_REPLACEMENT_SHA256",
                () => Iso9660Rebuilder.Rebuild(
                    sourcePath,
                    [new Iso9660ReplacementRequest(
                        "PSP_GAME/USRDIR/DATA/lt.bin",
                        replacementLt,
                        ExpectedReplacementSha256: new string('0', 64))],
                    Path.Combine(temp, "wrong-replacement-hash.iso")));
            Throws(
                "ISO_REPLACEMENT_DUPLICATE",
                () => Iso9660Rebuilder.Rebuild(
                    sourcePath,
                    [
                        new Iso9660ReplacementRequest("PSP_GAME/USRDIR/DATA/lt.bin", replacementLt),
                        new Iso9660ReplacementRequest("psp_game/usrdir/data/LT.BIN;1", replacementSc)
                    ],
                    Path.Combine(temp, "duplicate.iso")));
            Throws(
                "ISO_REPLACEMENT_OUTPUT",
                () => Iso9660Rebuilder.Rebuild(
                    sourcePath,
                    [new Iso9660ReplacementRequest("PSP_GAME/USRDIR/DATA/lt.bin", replacementLt)],
                    replacementLt));
            Throws(
                "ISO_REPLACEMENT_LIMIT",
                () => Iso9660Rebuilder.Rebuild(
                    sourcePath,
                    [new Iso9660ReplacementRequest("PSP_GAME/USRDIR/DATA/lt.bin", replacementLt)],
                    Path.Combine(temp, "limit.iso"),
                    limits: new FileLimits(MaximumIsoReplacements: 0)));
            Throws(
                "ISO_OUTPUT_TOO_LARGE",
                () => Iso9660Rebuilder.Rebuild(
                    sourcePath,
                    [new Iso9660ReplacementRequest("PSP_GAME/USRDIR/DATA/lt.bin", replacementLt)],
                    Path.Combine(temp, "too-large.iso"),
                    limits: new FileLimits(MaximumIsoOutputBytes: sourceBefore.Length)));
            Throws(
                "ISO_REPLACEMENT_TOTAL_TOO_LARGE",
                () => Iso9660Rebuilder.Rebuild(
                    sourcePath,
                    [new Iso9660ReplacementRequest("PSP_GAME/USRDIR/DATA/lt.bin", replacementLt)],
                    Path.Combine(temp, "replacement-total-too-large.iso"),
                    limits: new FileLimits(MaximumIsoTotalReplacementBytes: 1)));
            Throws(
                "ISO_REPLACEMENT_PATH",
                () => Iso9660Rebuilder.Rebuild(
                    sourcePath,
                    [new Iso9660ReplacementRequest(" ", replacementLt)],
                    Path.Combine(temp, "empty-iso-path.iso")));
            Throws(
                "ISO_REPLACEMENT_FILE",
                () => Iso9660Rebuilder.Rebuild(
                    sourcePath,
                    [new Iso9660ReplacementRequest("PSP_GAME/USRDIR/DATA/lt.bin", " ")],
                    Path.Combine(temp, "empty-file-path.iso")));

            byte[] extendedAttributeIso = sourceBefore.ToArray();
            int scIdentifier = extendedAttributeIso.AsSpan().IndexOf("SC.CPK;1"u8);
            True(scIdentifier >= 33, "Synthetic ISO does not contain SC.CPK directory record.");
            extendedAttributeIso[scIdentifier - 33 + 1] = 1;
            string extendedAttributePath = Path.Combine(temp, "extended-attribute.iso");
            File.WriteAllBytes(extendedAttributePath, extendedAttributeIso);
            Throws(
                "ISO_REPLACE_EXTENDED_ATTRIBUTES",
                () => Iso9660Rebuilder.Rebuild(
                    extendedAttributePath,
                    [new Iso9660ReplacementRequest("PSP_GAME/USRDIR/DATA/sc.cpk", replacementSc)],
                    Path.Combine(temp, "extended-output.iso")));

            byte[] supplementaryIso = new byte[sourceBefore.Length];
            sourceBefore.CopyTo(supplementaryIso, 0);
            int termOffset = 17 * Iso9660Image.DescriptorSectorSize;
            int newTermOffset = 18 * Iso9660Image.DescriptorSectorSize;
            sourceBefore.AsSpan(termOffset, Iso9660Image.DescriptorSectorSize)
                .CopyTo(supplementaryIso.AsSpan(newTermOffset, Iso9660Image.DescriptorSectorSize));
            sourceBefore.AsSpan(16 * Iso9660Image.DescriptorSectorSize, Iso9660Image.DescriptorSectorSize)
                .CopyTo(supplementaryIso.AsSpan(termOffset, Iso9660Image.DescriptorSectorSize));
            supplementaryIso[termOffset] = 2;
            string supplementaryPath = Path.Combine(temp, "supplementary.iso");
            File.WriteAllBytes(supplementaryPath, supplementaryIso);
            Throws(
                "ISO_REBUILD_SUPPLEMENTARY_DESCRIPTOR",
                () => Iso9660Rebuilder.Rebuild(
                    supplementaryPath,
                    [new Iso9660ReplacementRequest("PSP_GAME/USRDIR/DATA/lt.bin", replacementLt)],
                    Path.Combine(temp, "supplementary-output.iso")));

            byte[] partitionIso = supplementaryIso.ToArray();
            partitionIso[termOffset] = 3;
            string partitionPath = Path.Combine(temp, "partition.iso");
            File.WriteAllBytes(partitionPath, partitionIso);
            Throws(
                "ISO_REBUILD_PARTITION_DESCRIPTOR",
                () => Iso9660Rebuilder.Rebuild(
                    partitionPath,
                    [new Iso9660ReplacementRequest("PSP_GAME/USRDIR/DATA/lt.bin", replacementLt)],
                    Path.Combine(temp, "partition-output.iso")));

            byte[] unsupportedDescriptorIso = supplementaryIso.ToArray();
            unsupportedDescriptorIso[termOffset] = 4;
            string unsupportedDescriptorPath = Path.Combine(temp, "unsupported-descriptor.iso");
            File.WriteAllBytes(unsupportedDescriptorPath, unsupportedDescriptorIso);
            Throws(
                "ISO_REBUILD_UNKNOWN_DESCRIPTOR",
                () => Iso9660Rebuilder.Rebuild(
                    unsupportedDescriptorPath,
                    [new Iso9660ReplacementRequest("PSP_GAME/USRDIR/DATA/lt.bin", replacementLt)],
                    Path.Combine(temp, "unsupported-descriptor-output.iso")));

            string malformedManifest = Path.Combine(temp, "malformed-manifest.json");
            File.WriteAllText(
                malformedManifest,
                "{\"schema\":\"lucky-star-psp.iso-patch-manifest.v1\",\"replacements\":[],\"unknown\":1}");
            Throws(
                "ISO_MANIFEST_JSON",
                () => Iso9660Rebuilder.ApplyManifest(
                    sourcePath,
                    malformedManifest,
                    Path.Combine(temp, "malformed-output.iso")));

            string missingSchemaManifest = Path.Combine(temp, "missing-schema-manifest.json");
            File.WriteAllText(
                missingSchemaManifest,
                "{\"replacements\":[{\"isoPath\":\"PSP_GAME/USRDIR/DATA/lt.bin\",\"sourceFile\":\"missing.bin\"}]}");
            Throws(
                "ISO_MANIFEST_JSON",
                () => Iso9660Rebuilder.ApplyManifest(
                    sourcePath,
                    missingSchemaManifest,
                    Path.Combine(temp, "missing-schema-output.iso")));

            string missingReplacementsManifest = Path.Combine(temp, "missing-replacements-manifest.json");
            File.WriteAllText(
                missingReplacementsManifest,
                "{\"schema\":\"lucky-star-psp.iso-patch-manifest.v1\"}");
            Throws(
                "ISO_MANIFEST_JSON",
                () => Iso9660Rebuilder.ApplyManifest(
                    sourcePath,
                    missingReplacementsManifest,
                    Path.Combine(temp, "missing-replacements-output.iso")));

            string wrongSchemaManifest = Path.Combine(temp, "wrong-schema-manifest.json");
            File.WriteAllText(
                wrongSchemaManifest,
                "{\"schema\":\"wrong\",\"replacements\":[{" +
                "\"isoPath\":\"PSP_GAME/USRDIR/DATA/lt.bin\",\"sourceFile\":\"missing.bin\"}]}");
            Throws(
                "ISO_MANIFEST_SCHEMA",
                () => Iso9660Rebuilder.ApplyManifest(
                    sourcePath,
                    wrongSchemaManifest,
                    Path.Combine(temp, "wrong-schema-output.iso")));

            string emptyManifest = Path.Combine(temp, "empty-manifest.json");
            File.WriteAllText(
                emptyManifest,
                "{\"schema\":\"lucky-star-psp.iso-patch-manifest.v1\",\"replacements\":[]}");
            Throws(
                "ISO_MANIFEST_EMPTY",
                () => Iso9660Rebuilder.ApplyManifest(
                    sourcePath,
                    emptyManifest,
                    Path.Combine(temp, "empty-manifest-output.iso")));

            string escapingManifest = Path.Combine(temp, "escaping-manifest.json");
            File.WriteAllText(
                escapingManifest,
                "{\"schema\":\"lucky-star-psp.iso-patch-manifest.v1\",\"replacements\":[{" +
                "\"isoPath\":\"PSP_GAME/USRDIR/DATA/lt.bin\",\"sourceFile\":\"../outside.bin\"}]}");
            Throws(
                "ISO_MANIFEST_PATH",
                () => Iso9660Rebuilder.ApplyManifest(
                    sourcePath,
                    escapingManifest,
                    Path.Combine(temp, "escaping-output.iso")));

            string absoluteManifest = Path.Combine(temp, "absolute-manifest.json");
            File.WriteAllText(
                absoluteManifest,
                JsonSerializer.Serialize(new
                {
                    schema = Iso9660Rebuilder.ManifestSchema,
                    replacements = new[]
                    {
                        new
                        {
                            isoPath = "PSP_GAME/USRDIR/DATA/lt.bin",
                            sourceFile = replacementLt
                        }
                    }
                }));
            Throws(
                "ISO_MANIFEST_PATH",
                () => Iso9660Rebuilder.ApplyManifest(
                    sourcePath,
                    absoluteManifest,
                    Path.Combine(temp, "absolute-output.iso")));
        }
        finally
        {
            if (Directory.Exists(temp)) Directory.Delete(temp, true);
        }
    }

    /// <summary>
    /// Verifies asset collection behavior and invariants.
    /// </summary>
    private static void TestAssetCollection()
    {
        string isoPath = Path.Combine(Fixtures, "reference.iso");
        string temp = Path.Combine(Path.GetTempPath(), "lsptool-assets-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            string minimalZip = Path.Combine(temp, "minimal.zip");
            PspAssetBundleReport minimal = PspAssetCollector.CollectFromIso(
                isoPath,
                minimalZip,
                new PspAssetCollectionOptions(
                    IncludeOptional: false,
                    IncludeFileListing: true,
                    ComputeSourceSha256: true));
            True(minimal.Complete, "Synthetic ISO must contain all required translation assets.");
            Equal(4, minimal.Files.Count(static file => file.Included));
            Equal(0, minimal.MissingRequired.Count);
            True(File.Exists(minimalZip), "Minimal asset bundle was not written.");
            Equal(BinaryUtilities.Sha256HexFile(isoPath), minimal.SourceSha256 ?? string.Empty);
            Equal(BinaryUtilities.Sha256HexFile(minimalZip), minimal.OutputSha256 ?? string.Empty);

            using (ZipArchive zip = ZipFile.OpenRead(minimalZip))
            {
                string[] names = zip.Entries.Select(static entry => entry.FullName).ToArray();
                foreach (string required in new[]
                {
                    "PSP_GAME/PARAM.SFO",
                    "PSP_GAME/SYSDIR/EBOOT.BIN",
                    "PSP_GAME/USRDIR/DATA/sc.cpk",
                    "PSP_GAME/USRDIR/DATA/lt.bin",
                    PspAssetCollector.ManifestName,
                    PspAssetCollector.ListingName,
                    PspAssetCollector.InstructionsName
                })
                {
                    True(names.Contains(required, StringComparer.Ordinal), $"Asset bundle misses {required}.");
                }
                True(!names.Contains("PSP_GAME/USRDIR/DATA/union.cpk", StringComparer.Ordinal),
                    "Optional asset was included in a minimal bundle.");
                foreach (ZipArchiveEntry entry in zip.Entries)
                {
                    Equal(new DateTime(2000, 1, 1, 0, 0, 0), entry.LastWriteTime.DateTime);
                }
                using Stream manifestStream = zip.GetEntry(PspAssetCollector.ManifestName)!.Open();
                using JsonDocument manifest = JsonDocument.Parse(manifestStream);
                Equal("complete", manifest.RootElement.GetProperty("status").GetString() ?? string.Empty);
                Equal("reference.iso", manifest.RootElement.GetProperty("source").GetString() ?? string.Empty);
                Equal("asset-bundle.zip", manifest.RootElement.GetProperty("output").GetString() ?? string.Empty);
                Equal(
                    new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero),
                    manifest.RootElement.GetProperty("createdUtc").GetDateTimeOffset());
                Equal(4, manifest.RootElement.GetProperty("files").EnumerateArray().Count(
                    static item => item.GetProperty("included").GetBoolean()));
                True(
                    !manifest.RootElement.GetRawText().Contains(Path.GetDirectoryName(isoPath)!, StringComparison.Ordinal),
                    "Embedded manifest leaked the absolute source directory.");
                using Iso9660Image sourceImage = Iso9660Image.Open(isoPath);
                foreach (JsonElement file in manifest.RootElement.GetProperty("files").EnumerateArray())
                {
                    if (!file.GetProperty("included").GetBoolean())
                    {
                        continue;
                    }
                    string assetPath = file.GetProperty("path").GetString()
                        ?? throw new InvalidOperationException("Asset manifest path is null.");
                    using Stream asset = zip.GetEntry(assetPath)!.Open();
                    using MemoryStream copied = new();
                    asset.CopyTo(copied);
                    byte[] expected = sourceImage.ReadFile(assetPath);
                    SequenceEqual(expected, copied.ToArray());
                    Equal(
                        BinaryUtilities.Sha256Hex(expected),
                        file.GetProperty("sha256").GetString() ?? string.Empty);
                }
            }

            string repeatZip = Path.Combine(temp, "minimal-repeat.zip");
            PspAssetBundleReport repeated = PspAssetCollector.CollectFromIso(
                isoPath,
                repeatZip,
                new PspAssetCollectionOptions(
                    IncludeOptional: false,
                    IncludeFileListing: true,
                    ComputeSourceSha256: true));
            Equal(minimal.OutputSha256 ?? string.Empty, repeated.OutputSha256 ?? string.Empty);
            SequenceEqual(File.ReadAllBytes(minimalZip), File.ReadAllBytes(repeatZip));

            string optionalZip = Path.Combine(temp, "optional.zip");
            PspAssetBundleReport optional = PspAssetCollector.CollectFromIso(
                isoPath,
                optionalZip,
                new PspAssetCollectionOptions(
                    IncludeOptional: true,
                    IncludeFileListing: false,
                    ComputeSourceSha256: false));
            True(optional.Complete, "Optional asset collection must keep the complete status.");
            Equal(8, optional.Files.Count(static file => file.Included));
            True(optional.SourceSha256 is null, "Source hash must be skipped when requested.");
            using (ZipArchive zip = ZipFile.OpenRead(optionalZip))
            {
                True(zip.GetEntry("PSP_GAME/USRDIR/DATA/union.cpk") is not null,
                    "Optional union.cpk was not collected.");
                True(zip.GetEntry(PspAssetCollector.ListingName) is null,
                    "File listing must be omitted with IncludeFileListing=false.");
            }

            byte[] incompleteBytes = File.ReadAllBytes(isoPath);
            int scriptName = incompleteBytes.AsSpan().IndexOf("SC.CPK;1"u8);
            True(scriptName >= 0, "Synthetic ISO does not contain the expected SC.CPK directory record.");
            incompleteBytes[scriptName + 1] = (byte)'X';
            string incompleteIso = Path.Combine(temp, "incomplete.iso");
            File.WriteAllBytes(incompleteIso, incompleteBytes);
            string incompleteZip = Path.Combine(temp, "incomplete.zip");
            PspAssetBundleReport incomplete = PspAssetCollector.CollectFromIso(incompleteIso, incompleteZip);
            True(!incomplete.Complete, "ISO without sc.cpk must produce an incomplete asset bundle.");
            True(incomplete.MissingRequired.Contains("PSP_GAME/USRDIR/DATA/sc.cpk", StringComparer.Ordinal),
                "Missing sc.cpk was not reported.");
            True(File.Exists(incompleteZip), "Incomplete asset collection must still emit its diagnostic bundle.");

            Throws(
                "ASSET_OUTPUT_COLLISION",
                () => PspAssetCollector.CollectFromIso(isoPath, isoPath));
            Throws(
                "ASSET_SIZE_LIMIT",
                () => PspAssetCollector.CollectFromIso(
                    isoPath,
                    Path.Combine(temp, "too-small.zip"),
                    limits: new FileLimits(MaximumCollectedAssetBytes: 1)));
            Throws(
                "ASSET_LISTING_TOO_LARGE",
                () => PspAssetCollector.CollectFromIso(
                    isoPath,
                    Path.Combine(temp, "listing-too-large.zip"),
                    limits: new FileLimits(MaximumTextBytes: 100)));

            string streamTarget = Path.Combine(temp, "atomic-stream.bin");
            File.WriteAllText(streamTarget, "original");
            bool failed = false;
            try
            {
                AtomicFile.WriteStream(streamTarget, stream =>
                {
                    stream.Write(new byte[] { 1, 2, 3, 4 });
                    throw new InvalidOperationException("synthetic stream failure");
                });
            }
            catch (InvalidOperationException)
            {
                failed = true;
            }
            True(failed, "Synthetic stream failure did not escape AtomicFile.WriteStream.");
            Equal("original", File.ReadAllText(streamTarget));
            True(!Directory.EnumerateFiles(temp, ".*.tmp").Any(), "Atomic stream write leaked a .tmp file.");
            True(!Directory.EnumerateFiles(temp, ".*.bak").Any(), "Atomic stream write leaked a .bak file.");
        }
        finally
        {
            if (Directory.Exists(temp)) Directory.Delete(temp, true);
        }
    }

    /// <summary>
    /// Validates PNG while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="png">The png value.</param>
    /// <param name="expected">The expected value.</param>
    /// <remarks>Malformed input or violated preconditions are rejected before a persistent output is committed.</remarks>
    private static void ValidatePng(byte[] png, RgbaImage expected)
    {
        ReadOnlySpan<byte> signature = [137, 80, 78, 71, 13, 10, 26, 10];
        True(png.AsSpan(0, 8).SequenceEqual(signature), "PNG signature is invalid.");
        int position = 8;
        int width = 0;
        int height = 0;
        using MemoryStream compressed = new();
        bool sawIend = false;
        while (position < png.Length)
        {
            True(position + 12 <= png.Length, "PNG chunk header exceeds the file.");
            int length = checked((int)BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(position, 4)));
            position += 4;
            ReadOnlySpan<byte> typeBytes = png.AsSpan(position, 4);
            string type = System.Text.Encoding.ASCII.GetString(typeBytes);
            position += 4;
            True(length >= 0 && position + length + 4 <= png.Length, "PNG chunk exceeds the file.");
            ReadOnlySpan<byte> payload = png.AsSpan(position, length);
            position += length;
            uint storedCrc = BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(position, 4));
            byte[] crcInput = new byte[checked(4 + length)];
            typeBytes.CopyTo(crcInput);
            payload.CopyTo(crcInput.AsSpan(4));
            Equal(ComputePngCrc(crcInput), storedCrc);
            position += 4;
            if (type == "IHDR")
            {
                Equal(13, length);
                width = checked((int)BinaryPrimitives.ReadUInt32BigEndian(payload[..4]));
                height = checked((int)BinaryPrimitives.ReadUInt32BigEndian(payload.Slice(4, 4)));
                Equal((byte)8, payload[8]);
                Equal((byte)6, payload[9]);
            }
            else if (type == "IDAT")
            {
                compressed.Write(payload);
            }
            else if (type == "IEND")
            {
                sawIend = true;
                break;
            }
        }
        Equal(expected.Width, width);
        Equal(expected.Height, height);
        True(sawIend, "PNG IEND chunk is missing.");

        compressed.Position = 0;
        using ZLibStream zlib = new(compressed, CompressionMode.Decompress);
        using MemoryStream raw = new();
        zlib.CopyTo(raw);
        byte[] scanlines = raw.ToArray();
        Equal(checked(expected.Height * (expected.Width * 4 + 1)), scanlines.Length);
        int source = 0;
        int pixel = 0;
        for (int row = 0; row < expected.Height; row++)
        {
            Equal((byte)0, scanlines[source++]);
            SequenceEqual(
                expected.Pixels.AsSpan(pixel, expected.Width * 4).ToArray(),
                scanlines.AsSpan(source, expected.Width * 4).ToArray());
            source += expected.Width * 4;
            pixel += expected.Width * 4;
        }
    }

    /// <summary>
    /// Computes PNG CRC while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="data">The binary data to process.</param>
    /// <returns>The validated operation result.</returns>
    private static uint ComputePngCrc(ReadOnlySpan<byte> data)
    {
        uint crc = 0xFFFFFFFF;
        foreach (byte value in data)
        {
            crc ^= value;
            for (int bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) != 0 ? 0xEDB88320U ^ (crc >> 1) : crc >> 1;
            }
        }
        return crc ^ 0xFFFFFFFF;
    }

    /// <summary>
    /// Verifies checksum behavior and invariants.
    /// </summary>
    private static void TestChecksum()
    {
        byte[] data = File.ReadAllBytes(Path.Combine(Fixtures, "reference-script.bin"));
        True(RgoChecksum.Verify(data), "Reference script checksum must be valid.");
        data[0x200] ^= 0x40;
        True(!RgoChecksum.Verify(data), "Tampered script checksum must fail.");
    }

    /// <summary>
    /// Verifies UTF round trip behavior and invariants.
    /// </summary>
    private static void TestUtfRoundTrip()
    {
        CriUtfTable table = new() { Name = "SelfTest" };
        CriUtfColumn constant = new() { Name = "Constant", Type = CriUtfType.UInt32, Storage = CriUtfStorage.Constant, ConstantValue = CriUtfValue.FromUnsigned(123456) };
        CriUtfColumn text = new() { Name = "Name", Type = CriUtfType.String, Storage = CriUtfStorage.PerRow };
        CriUtfColumn blob = new() { Name = "Blob", Type = CriUtfType.Data, Storage = CriUtfStorage.PerRow };
        CriUtfColumn zero = new() { Name = "Zero", Type = CriUtfType.UInt16, Storage = CriUtfStorage.Zero };
        table.Columns.AddRange([constant, text, blob, zero]);
        for (int i = 0; i < 3; i++)
        {
            CriUtfRow row = new();
            row.Values.Add(CriUtfValue.FromUnsigned(123456));
            row.Values.Add(CriUtfValue.FromText("строка-" + i));
            row.Values.Add(CriUtfValue.FromData(Enumerable.Range(0, i + 2).Select(value => (byte)(value + i)).ToArray()));
            row.Values.Add(CriUtfValue.FromUnsigned(0));
            table.Rows.Add(row);
        }
        byte[] built = CriUtfCodec.BuildTable(table);
        CriUtfTable parsed = CriUtfCodec.ParseTable(built);
        Equal("SelfTest", parsed.Name);
        Equal((ulong)123456, parsed.GetUnsigned(2, "Constant"));
        Equal("строка-1", parsed.GetText(1, "Name"));
        SequenceEqual(new byte[] { 2, 3, 4, 5 }, parsed.GetData(2, "Blob"));
        CriUtfPacket packet = new(7, true, table, 0);
        CriUtfPacket reparsedPacket = CriUtfCodec.ParsePacket(CriUtfCodec.BuildPacket(packet));
        Equal((uint)7, reparsedPacket.Unknown);
        Equal("строка-2", reparsedPacket.Table.GetText(2, "Name"));
    }

    /// <summary>
    /// Verifies script parse behavior and invariants.
    /// </summary>
    private static void TestScriptParse()
    {
        byte[] data = File.ReadAllBytes(Path.Combine(Fixtures, "reference-script.bin"));
        LuckyStarScript script = LuckyStarScript.Parse(data, ScriptProfile.Rgo);
        Equal(2, script.Dialogs.Count);
        Equal(1, script.ChoiceGroups.Count);
        Equal(1, script.ChoiceGroups[0].Choices.Count);
        Equal((ushort)0xFFFB, script.Dialogs[0].MessageTerminator);
        Equal((ushort)0xFFFD, script.Dialogs[1].MessageTerminator);
        Equal(3, script.Jumps.Count);
    }

    /// <summary>
    /// Verifies script mutation behavior and invariants.
    /// </summary>
    private static void TestScriptMutation()
    {
        byte[] data = File.ReadAllBytes(Path.Combine(Fixtures, "reference-script.bin"));
        LuckyStarScript script = LuckyStarScript.Parse(data, ScriptProfile.Rgo);
        ScriptMutation mutation = new();
        mutation.MessageGlyphs[0] = Enumerable.Repeat((ushort)3, 5_000).ToArray();
        mutation.SpeakerGlyphs[1] = [3, 4, 5, 6];
        mutation.ChoiceGlyphs[(0, 0)] = [3, 4, 5, 6, 7];
        ScriptBuildResult built = script.Build(mutation);
        True(built.NewLength > built.OldLength, "Long translation must grow the script.");
        True(built.NewBlocks2K > built.OldBlocks2K, "Long translation must cross a 2 KiB boundary.");
        LuckyStarScript reparsed = LuckyStarScript.Parse(built.Data, ScriptProfile.Rgo);
        Equal(5_000, reparsed.Dialogs[0].MessageGlyphs.Length);
        SequenceEqual(mutation.SpeakerGlyphs[1], reparsed.Dialogs[1].SpeakerGlyphs);
        SequenceEqual(mutation.ChoiceGlyphs[(0, 0)], reparsed.ChoiceGroups[0].Choices[0].Glyphs);
    }

    /// <summary>
    /// Verifies script fuzz behavior and invariants.
    /// </summary>
    private static void TestScriptFuzz()
    {
        byte[] source = File.ReadAllBytes(Path.Combine(Fixtures, "reference-script.bin"));
        Random random = new(0x5A17);
        for (int iteration = 0; iteration < 40; iteration++)
        {
            LuckyStarScript script = LuckyStarScript.Parse(source, ScriptProfile.Rgo);
            ScriptMutation mutation = new();
            int length = random.Next(0, 7_500);
            mutation.MessageGlyphs[iteration % 2] = Enumerable.Range(0, length).Select(index => (ushort)(3 + index % 4)).ToArray();
            mutation.SpeakerGlyphs[(iteration + 1) % 2] = Enumerable.Range(0, random.Next(0, 20)).Select(index => (ushort)(1 + index % 6)).ToArray();
            ScriptBuildResult built = script.Build(mutation);
            LuckyStarScript parsed = LuckyStarScript.Parse(built.Data, ScriptProfile.Rgo);
            SequenceEqual(mutation.MessageGlyphs[iteration % 2], parsed.Dialogs[iteration % 2].MessageGlyphs);
            SequenceEqual(mutation.SpeakerGlyphs[(iteration + 1) % 2], parsed.Dialogs[(iteration + 1) % 2].SpeakerGlyphs);
            source = built.Data;
        }
    }

    /// <summary>
    /// Verifies CPK parse behavior and invariants.
    /// </summary>
    private static void TestCpkParse()
    {
        byte[] cpk = File.ReadAllBytes(Path.Combine(Fixtures, "reference.cpk"));
        CriCpkArchive archive = CriCpkArchive.Parse(cpk);
        Equal(2, archive.Entries.Count);
        Equal(0x800, archive.Alignment);
        True(!archive.GetEntry(0).IsCrilayla, "Script entry must be uncompressed.");
        True(archive.GetEntry(1).IsCrilayla, "Fixture entry must use CRILAYLA.");
        byte[] expected = File.ReadAllBytes(Path.Combine(Fixtures, "crilayla-extracted.bin"));
        SequenceEqual(expected, archive.GetEntry(1).GetExtractedData());
    }

    /// <summary>
    /// Verifies CPK rebuild behavior and invariants.
    /// </summary>
    private static void TestCpkRebuild()
    {
        byte[] cpk = File.ReadAllBytes(Path.Combine(Fixtures, "reference.cpk"));
        CriCpkArchive archive = CriCpkArchive.Parse(cpk);
        LuckyStarScript script = LuckyStarScript.Parse(archive.GetEntry(0).PackedData, ScriptProfile.Rgo);
        ScriptMutation mutation = new();
        mutation.MessageGlyphs[0] = [3, 4, 5, 6, 3, 4];
        byte[] replacement = script.Build(mutation).Data;
        archive.ReplaceEntry(0, replacement);
        CriCpkBuildResult built = archive.Build();
        CriCpkArchive reparsed = CriCpkArchive.Parse(built.Data);
        SequenceEqual(replacement, reparsed.GetEntry(0).PackedData);
        SequenceEqual(archive.GetEntry(1).PackedData, reparsed.GetEntry(1).PackedData);
    }

    /// <summary>
    /// Verifies workspace behavior and invariants.
    /// </summary>
    private static void TestWorkspace()
    {
        string temp = Path.Combine(Path.GetTempPath(), "lsptool-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            string source = Path.Combine(temp, "sc.cpk");
            string map = Path.Combine(temp, "map.txt");
            File.Copy(Path.Combine(Fixtures, "reference.cpk"), source);
            File.Copy(Path.Combine(Fixtures, "glyph-map.txt"), map);
            string workspace = Path.Combine(temp, "workspace");
            WorkspaceExportResult exported = TranslationWorkspaceService.Export(source, map, workspace, ScriptProfile.Rgo, [0]);
            Equal(1, exported.ScriptCount);
            string scriptFile = Path.Combine(workspace, "script-0000.json");
            TranslationScriptFile translation = JsonSerializer.Deserialize<TranslationScriptFile>(File.ReadAllText(scriptFile), EbootSizePatchPlan.JsonOptions)
                ?? throw new InvalidOperationException("Workspace script JSON is null.");
            translation.Dialogs[0].TranslationSpeaker = "АБ";
            translation.Dialogs[0].TranslationMessage = new string('А', 5_000) + "!";
            translation.ChoiceGroups[0].Choices[0].TranslationText = "Баба?";
            AtomicFile.WriteAllText(scriptFile, JsonSerializer.Serialize(translation, EbootSizePatchPlan.JsonOptions));
            WorkspaceValidationResult validation = TranslationWorkspaceService.Validate(workspace, source);
            Equal(1, validation.ScriptCount);
            Equal(1, validation.ChangedScriptCount);
            Equal(1, validation.TranslatedSpeakers);
            Equal(1, validation.TranslatedMessages);
            Equal(1, validation.TranslatedChoices);
            Equal(1, validation.EbootPlanEntryCount);

            string output = Path.Combine(temp, "sc-patched.cpk");
            string planPath = Path.Combine(temp, "plan.json");
            WorkspaceBuildResult result = TranslationWorkspaceService.Build(workspace, source, output, planPath);
            True(result.ChangedScriptCount == 1, "Workspace must report one changed script.");
            EbootSizePatchPlan plan = EbootSizePatchPlan.Load(planPath);
            True(plan.Entries.Count == 1 && plan.Entries[0].Id == 0, "Workspace must generate a size plan for script 0.");
            Equal(validation.SourceCpkSha256, plan.SourceCpkSha256 ?? string.Empty);
            Equal(validation.RebuiltCpkSha256, plan.RebuiltCpkSha256 ?? string.Empty);
            CriCpkArchive rebuilt = CriCpkArchive.Parse(File.ReadAllBytes(output));
            LuckyStarScript rebuiltScript = LuckyStarScript.Parse(rebuilt.GetEntry(0).PackedData, ScriptProfile.Rgo);
            GlyphMap glyphMap = GlyphMap.Load(map);
            Equal(new string('А', 5_000) + "!", glyphMap.Decode(rebuiltScript.Dialogs[0].MessageGlyphs));
            Equal("Баба?", glyphMap.Decode(rebuiltScript.ChoiceGroups[0].Choices[0].Glyphs));
        }
        finally
        {
            if (Directory.Exists(temp)) Directory.Delete(temp, true);
        }
    }

    /// <summary>
    /// Verifies workspace safety behavior and invariants.
    /// </summary>
    private static void TestWorkspaceSafety()
    {
        string temp = Path.Combine(Path.GetTempPath(), "lsptool-safety-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            string source = Path.Combine(temp, "sc.cpk");
            string map = Path.Combine(temp, "map.txt");
            File.Copy(Path.Combine(Fixtures, "reference.cpk"), source);
            File.Copy(Path.Combine(Fixtures, "glyph-map.txt"), map);
            string workspace = Path.Combine(temp, "workspace");
            _ = TranslationWorkspaceService.Export(source, map, workspace, ScriptProfile.Rgo, new ushort[] { 0 });
            string scriptPath = Path.Combine(workspace, "script-0000.json");
            TranslationScriptFile translation = JsonSerializer.Deserialize<TranslationScriptFile>(File.ReadAllText(scriptPath), EbootSizePatchPlan.JsonOptions)
                ?? throw new InvalidOperationException("Workspace script JSON is null.");
            translation.Dialogs[0].SourceMessage += "tampered";
            AtomicFile.WriteAllText(scriptPath, JsonSerializer.Serialize(translation, EbootSizePatchPlan.JsonOptions));
            Throws("WORKSPACE_SOURCE_TEXT", () => TranslationWorkspaceService.Build(workspace, source, Path.Combine(temp, "bad1.cpk")));

            translation.Dialogs[0].SourceMessage = "Аа";
            translation.Dialogs[0].TranslationMessage = "{{GLYPH:FFFB}}";
            AtomicFile.WriteAllText(scriptPath, JsonSerializer.Serialize(translation, EbootSizePatchPlan.JsonOptions));
            Throws("WORKSPACE_CONTROL_GLYPH", () => TranslationWorkspaceService.Build(workspace, source, Path.Combine(temp, "bad2.cpk")));

            translation.Dialogs[0].TranslationMessage = null;
            string strictJson = JsonSerializer.Serialize(translation, EbootSizePatchPlan.JsonOptions);
            strictJson = strictJson.TrimEnd();
            strictJson = strictJson[..^1] + ",\"unexpectedProperty\":true}";
            AtomicFile.WriteAllText(scriptPath, strictJson);
            Throws("WORKSPACE_JSON", () => TranslationWorkspaceService.Validate(workspace, source));

            AtomicFile.WriteAllText(scriptPath, JsonSerializer.Serialize(translation, EbootSizePatchPlan.JsonOptions));
            Throws("WORKSPACE_OUTPUT_SOURCE", () => TranslationWorkspaceService.Build(workspace, source, source));
        }
        finally
        {
            if (Directory.Exists(temp)) Directory.Delete(temp, true);
        }
    }

    /// <summary>
    /// Verifies bounded file io behavior and invariants.
    /// </summary>
    private static void TestBoundedFileIo()
    {
        WithTemporaryDirectory(root =>
        {
            string path = Path.Combine(root, "bounded.bin");
            byte[] expected = Enumerable.Range(0, 4096).Select(static value => (byte)value).ToArray();
            File.WriteAllBytes(path, expected);

            SequenceEqual(expected, BinaryUtilities.ReadAllBytesBounded(path));
            Equal(BinaryUtilities.Sha256Hex(expected), BinaryUtilities.Sha256HexFile(path));
            Throws(
                "FILE_TOO_LARGE",
                () => BinaryUtilities.ReadAllBytesBounded(
                    path,
                    new FileLimits(MaximumInputBytes: expected.Length - 1)));
        });
    }

    /// <summary>
    /// Verifies atomic write set behavior and invariants.
    /// </summary>
    private static void TestAtomicWriteSet()
    {
        string temp = Path.Combine(Path.GetTempPath(), "lsptool-atomic-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            string target = Path.Combine(temp, "target.bin");
            File.WriteAllBytes(target, [1, 2, 3]);
            Throws(
                "ATOMIC_DUPLICATE_TARGET",
                () => AtomicFile.WriteAll(
                    new AtomicWriteRequest(target, new byte[] { 4 }),
                    new AtomicWriteRequest(target, new byte[] { 5 })));
            SequenceEqual(new byte[] { 1, 2, 3 }, File.ReadAllBytes(target));
            True(!Directory.EnumerateFiles(temp, "*.tmp").Any(), "Atomic write left a staging file behind.");
        }
        finally
        {
            if (Directory.Exists(temp)) Directory.Delete(temp, true);
        }
    }

    /// <summary>
    /// Verifies ZIP safety behavior and invariants.
    /// </summary>
    private static void TestZipSafety()
    {
        string temp = Path.Combine(Path.GetTempPath(), "lsptool-zip-" + Guid.NewGuid().ToString("N") + ".zip");
        try
        {
            using (FileStream stream = File.Create(temp))
            using (System.IO.Compression.ZipArchive zip = new(stream, System.IO.Compression.ZipArchiveMode.Create))
            {
                System.IO.Compression.ZipArchiveEntry entry = zip.CreateEntry("../escape.bin");
                using Stream output = entry.Open();
                output.WriteByte(1);
            }
            Throws("ZIP_PATH", () => CustomerArchiveAuditor.Audit(temp));
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    /// <summary>
    /// Verifies EBOOT plan behavior and invariants.
    /// </summary>
    private static void TestEbootPlan()
    {
        byte[] eboot = new byte[128];
        int table = 16;
        ushort[] sizes = [1, 2, 3, 4];
        ushort[] cumulative = [1, 3, 6, 10];
        for (int i = 0; i < sizes.Length; i++)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(eboot.AsSpan(table + i * 4), sizes[i]);
            BinaryPrimitives.WriteUInt16LittleEndian(eboot.AsSpan(table + i * 4 + 2), cumulative[i]);
        }

        List<EbootSizePatchEntry> changes =
        [
            new EbootSizePatchEntry { Id = 1, OldBlocks2K = 2, NewBlocks2K = 5 }
        ];
        byte[] patched = EbootSizeTablePatcher.Apply(eboot, table, 0, 3, changes);
        Equal((ushort)5, BinaryPrimitives.ReadUInt16LittleEndian(patched.AsSpan(table + 4, 2)));
        Equal((ushort)6, BinaryPrimitives.ReadUInt16LittleEndian(patched.AsSpan(table + 6, 2)));
        Equal((ushort)9, BinaryPrimitives.ReadUInt16LittleEndian(patched.AsSpan(table + 10, 2)));
        Equal((ushort)13, BinaryPrimitives.ReadUInt16LittleEndian(patched.AsSpan(table + 14, 2)));

        byte[] inconsistent = eboot.ToArray();
        BinaryPrimitives.WriteUInt16LittleEndian(inconsistent.AsSpan(table + 10, 2), 99);
        Throws(
            "EBOOT_CUMULATIVE_PRECONDITION",
            () => EbootSizeTablePatcher.Apply(inconsistent, table, 0, 3, changes));

        changes[0].OldBlocks2K = 7;
        Throws("EBOOT_OLD_SIZE", () => EbootSizeTablePatcher.Apply(eboot, table, 0, 3, changes));

        EbootSizePatchPlan canonical = EbootSizePatchPlan.Create(
            ScriptProfile.Rgo,
            [new EbootSizePatchEntry { Id = 1, OldBlocks2K = 110, NewBlocks2K = 112 }],
            new string('a', 64),
            new string('b', 64));
        Equal(0x10319E, canonical.TableOffset);
        Equal(EbootSizePatchPlan.KnownRgoDecryptedEbootSha256, canonical.ExpectedDecryptedEbootSha256 ?? string.Empty);

        canonical.TableOffset = 16;
        Throws("EBOOT_PLAN_LAYOUT", canonical.Validate);

        const int expandedCapacity = 1200 * 2048;
        (uint expandedLui, uint expandedAddiu) = RgoScriptHeapPatcher.EncodeCapacity(expandedCapacity);
        Equal(0x3C040026U, expandedLui);
        Equal(0x24848000U, expandedAddiu);
        Equal(expandedCapacity, RgoScriptHeapPatcher.DecodeCapacity(expandedLui, expandedAddiu));
        Throws(
            "EBOOT_HEAP_INSTRUCTION",
            () => RgoScriptHeapPatcher.DecodeCapacity(0x3C050026U, expandedAddiu));
        Throws(
            "EBOOT_HEAP_SIZE",
            () => RgoScriptHeapPatcher.EncodeCapacity(expandedCapacity + 1));

        byte[] heapFixture = new byte[RgoScriptHeapPatcher.AddiuOffset + sizeof(uint)];
        BinaryPrimitives.WriteUInt32LittleEndian(
            heapFixture.AsSpan(RgoScriptHeapPatcher.LuiOffset, sizeof(uint)),
            RgoScriptHeapPatcher.OriginalLuiInstruction);
        BinaryPrimitives.WriteUInt32LittleEndian(
            heapFixture.AsSpan(RgoScriptHeapPatcher.AddiuOffset, sizeof(uint)),
            RgoScriptHeapPatcher.OriginalAddiuInstruction);
        BinaryPrimitives.WriteUInt16LittleEndian(heapFixture.AsSpan(table, 2), 1200);
        BinaryPrimitives.WriteUInt16LittleEndian(heapFixture.AsSpan(table + 2, 2), 1200);
        BinaryPrimitives.WriteUInt16LittleEndian(heapFixture.AsSpan(table + 4, 2), 2);
        BinaryPrimitives.WriteUInt16LittleEndian(heapFixture.AsSpan(table + 6, 2), 1202);
        RgoScriptHeapPatcher.EnsureCapacity(heapFixture, table, 0, 1);
        Equal(
            expandedCapacity,
            RgoScriptHeapPatcher.DecodeCapacity(
                BinaryPrimitives.ReadUInt32LittleEndian(
                    heapFixture.AsSpan(RgoScriptHeapPatcher.LuiOffset, sizeof(uint))),
                BinaryPrimitives.ReadUInt32LittleEndian(
                    heapFixture.AsSpan(RgoScriptHeapPatcher.AddiuOffset, sizeof(uint)))));

        byte[] idempotentHeap = heapFixture.ToArray();
        RgoScriptHeapPatcher.EnsureCapacity(idempotentHeap, table, 0, 1);
        SequenceEqual(heapFixture, idempotentHeap);

        byte[] unexpectedHeap = heapFixture.ToArray();
        (uint wrongLui, uint wrongAddiu) = RgoScriptHeapPatcher.EncodeCapacity(0x240000);
        BinaryPrimitives.WriteUInt32LittleEndian(
            unexpectedHeap.AsSpan(RgoScriptHeapPatcher.LuiOffset, sizeof(uint)),
            wrongLui);
        BinaryPrimitives.WriteUInt32LittleEndian(
            unexpectedHeap.AsSpan(RgoScriptHeapPatcher.AddiuOffset, sizeof(uint)),
            wrongAddiu);
        Throws(
            "EBOOT_HEAP_PRECONDITION",
            () => RgoScriptHeapPatcher.EnsureCapacity(unexpectedHeap, table, 0, 1));
    }

    /// <summary>
    /// Verifies negative validation behavior and invariants.
    /// </summary>
    private static void TestNegativeValidation()
    {
        byte[] cpk = File.ReadAllBytes(Path.Combine(Fixtures, "reference.cpk"));
        cpk[0] = (byte)'X';
        Throws("CPK_MAGIC", () => CriCpkArchive.Parse(cpk));
        byte[] script = File.ReadAllBytes(Path.Combine(Fixtures, "reference-script.bin"));
        script[^1] ^= 1;
        Throws("SCRIPT_CHECKSUM", () => LuckyStarScript.Parse(script, ScriptProfile.Rgo));
        byte[] frame = File.ReadAllBytes(Path.Combine(Fixtures, "reference.cpk"));
        Throws("CRILAYLA_MAGIC", () => CrilaylaCodec.Inspect(frame));
    }


    /// <summary>
    /// Verifies unified CLI behavior and invariants.
    /// </summary>
    private static void TestUnifiedCli()
    {
        Equal(0, CommandApplication.RunCore(["version"]));
        Equal(0, CommandApplication.RunCore(["formats-self-test"]));
        Equal(64, CommandApplication.RunCore(["definitely-not-a-command"]));

        string isoFixture = Path.Combine(Fixtures, "reference.iso");
        string fixture = Path.Combine(Fixtures, "reference.cpk");
        string glyphMap = Path.Combine(Fixtures, "glyph-map.txt");
        string temp = Path.Combine(Path.GetTempPath(), "lsptool-cli-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            string isoReport = Path.Combine(temp, "iso-list.json");
            Equal(0, CommandApplication.RunCore(["iso-list", isoFixture, "--json", isoReport]));
            using (JsonDocument document = JsonDocument.Parse(File.ReadAllText(isoReport)))
            {
                Equal("lucky-star-psp.iso9660-summary.v1", document.RootElement.GetProperty("schema").GetString() ?? string.Empty);
                Equal("LSPTOOL_ORACLE", document.RootElement.GetProperty("volumeIdentifier").GetString() ?? string.Empty);
            }

            string multiOutput = Path.Combine(temp, "multi.bin");
            string multiReport = Path.Combine(temp, "multi.json");
            Equal(0, CommandApplication.RunCore([
                "iso-extract", isoFixture, "psp_game/usrdir/data/multi.bin;1", multiOutput,
                "--json", multiReport]));
            Equal(3000L, new FileInfo(multiOutput).Length);
            using (JsonDocument document = JsonDocument.Parse(File.ReadAllText(multiReport)))
            {
                Equal(2, document.RootElement.GetProperty("extentCount").GetInt32());
                Equal(BinaryUtilities.Sha256HexFile(multiOutput), document.RootElement.GetProperty("sha256").GetString() ?? string.Empty);
            }

            string assetsZip = Path.Combine(temp, "assets.zip");
            string assetsReport = Path.Combine(temp, "assets.json");
            Equal(0, CommandApplication.RunCore([
                "collect-assets", isoFixture, assetsZip, "--include-optional", "--json", assetsReport]));
            True(File.Exists(assetsZip), "collect-assets did not create its output ZIP.");
            using (JsonDocument document = JsonDocument.Parse(File.ReadAllText(assetsReport)))
            {
                Equal("complete", document.RootElement.GetProperty("status").GetString() ?? string.Empty);
                True(document.RootElement.GetProperty("optionalAssetsRequested").GetBoolean(),
                    "collect-assets did not record the optional asset request.");
            }
            string isoPatchOutput = Path.Combine(temp, "patched.iso");
            string isoPatchReport = Path.Combine(temp, "patched.json");
            Equal(0, CommandApplication.RunCore([
                "iso-apply-manifest",
                isoFixture,
                Path.Combine(Fixtures, "iso-patch-manifest.json"),
                isoPatchOutput,
                "--json", isoPatchReport]));
            SequenceEqual(
                File.ReadAllBytes(Path.Combine(Fixtures, "reference-patched.iso")),
                File.ReadAllBytes(isoPatchOutput));
            using (JsonDocument document = JsonDocument.Parse(File.ReadAllText(isoPatchReport)))
            {
                Equal("lucky-star-psp.iso9660-rebuild.v1", document.RootElement.GetProperty("schema").GetString() ?? string.Empty);
                Equal(2, document.RootElement.GetProperty("replacements").GetArrayLength());
                Equal(
                    BinaryUtilities.Sha256HexFile(Path.Combine(Fixtures, "iso-patch-manifest.json")),
                    document.RootElement.GetProperty("manifestSha256").GetString() ?? string.Empty);
            }

            string directReplacement = Path.Combine(temp, "direct-lt.bin");
            File.Copy(Path.Combine(Fixtures, "iso-replacement-lt.bin"), directReplacement);
            byte[] directReplacementBefore = File.ReadAllBytes(directReplacement);
            string directIsoOutput = Path.Combine(temp, "direct-patched.iso");
            Equal(0, CommandApplication.RunCore([
                "iso-replace", isoFixture, "PSP_GAME/USRDIR/DATA/lt.bin", directReplacement, directIsoOutput,
                "--expect-source-sha256", BinaryUtilities.Sha256HexFile(isoFixture),
                "--expect-original-sha256", BinaryUtilities.Sha256HexFile(Path.Combine(Fixtures, "reference-lt.bin")),
                "--expect-original-size", new FileInfo(Path.Combine(Fixtures, "reference-lt.bin")).Length.ToString(),
                "--expect-replacement-sha256", BinaryUtilities.Sha256HexFile(directReplacement),
                "--expect-replacement-size", new FileInfo(directReplacement).Length.ToString()]));
            using (Iso9660Image directIso = Iso9660Image.Open(directIsoOutput))
            {
                Equal(BinaryUtilities.Sha256HexFile(directReplacement), directIso.ComputeFileSha256("PSP_GAME/USRDIR/DATA/lt.bin"));
            }
            Equal(3, CommandApplication.RunCore([
                "iso-replace", isoFixture, "PSP_GAME/USRDIR/DATA/lt.bin", directReplacement, Path.Combine(temp, "rejected.iso"),
                "--json", directReplacement]));
            SequenceEqual(directReplacementBefore, File.ReadAllBytes(directReplacement));

            string manifestDirectory = Path.Combine(temp, "manifest");
            Directory.CreateDirectory(manifestDirectory);
            File.Copy(Path.Combine(Fixtures, "iso-replacement-lt.bin"), Path.Combine(manifestDirectory, "iso-replacement-lt.bin"));
            File.Copy(Path.Combine(Fixtures, "iso-replacement-sc.cpk"), Path.Combine(manifestDirectory, "iso-replacement-sc.cpk"));
            string localManifest = Path.Combine(manifestDirectory, "manifest.json");
            File.Copy(Path.Combine(Fixtures, "iso-patch-manifest.json"), localManifest);
            string protectedManifestReplacement = Path.Combine(manifestDirectory, "iso-replacement-lt.bin");
            byte[] protectedManifestReplacementBefore = File.ReadAllBytes(protectedManifestReplacement);
            Equal(3, CommandApplication.RunCore([
                "iso-apply-manifest", isoFixture, localManifest, Path.Combine(temp, "manifest-rejected.iso"),
                "--json", protectedManifestReplacement]));
            SequenceEqual(protectedManifestReplacementBefore, File.ReadAllBytes(protectedManifestReplacement));

            byte[] originalIso = File.ReadAllBytes(isoFixture);
            Equal(3, CommandApplication.RunCore(["iso-extract", isoFixture, "PSP_GAME/PARAM.SFO", isoFixture]));
            SequenceEqual(originalIso, File.ReadAllBytes(isoFixture));
            Equal(3, CommandApplication.RunCore(["collect-assets", isoFixture, isoFixture]));
            SequenceEqual(originalIso, File.ReadAllBytes(isoFixture));

            string protectedCpk = Path.Combine(temp, "protected.cpk");
            File.Copy(fixture, protectedCpk);
            byte[] originalCpk = File.ReadAllBytes(protectedCpk);

            string report = Path.Combine(temp, "cpk-verify.json");
            Equal(0, CommandApplication.RunCore(["cpk-verify", protectedCpk, "--json", report]));
            True(File.Exists(report), "Unified CLI did not write its requested CPK verification report.");
            using (JsonDocument document = JsonDocument.Parse(File.ReadAllText(report)))
            {
                Equal("lucky-star-psp.cpk-verify.v1", document.RootElement.GetProperty("schema").GetString() ?? string.Empty);
                True(document.RootElement.GetProperty("semanticVerification").GetBoolean(), "CLI CPK verification report was not marked successful.");
            }

            Equal(3, CommandApplication.RunCore(["cpk-list", protectedCpk, "--json", protectedCpk]));
            SequenceEqual(originalCpk, File.ReadAllBytes(protectedCpk));

            string sourceFont = Path.Combine(temp, "lt.bin");
            string bdf = Path.Combine(temp, "font.bdf");
            File.Copy(Path.Combine(Fixtures, "reference-lt.bin"), sourceFont);
            File.Copy(Path.Combine(Fixtures, "reference-font.bdf"), bdf);
            string fontPreviewBefore = Path.Combine(temp, "font-before.png");
            string fontInspectReport = Path.Combine(temp, "font-inspect.json");
            Equal(2, CommandApplication.RunCore([
                "font-inspect", sourceFont, glyphMap,
                "--preview", fontPreviewBefore,
                "--columns", "16", "--scale", "2",
                "--json", fontInspectReport]));
            True(File.Exists(fontPreviewBefore), "Font inspection did not create a PNG preview.");
            True(File.Exists(fontInspectReport), "Font inspection did not create a JSON report.");

            string patchedFont = Path.Combine(temp, "lt-russian.bin");
            string fontPreviewAfter = Path.Combine(temp, "font-after.png");
            string fontPatchReport = Path.Combine(temp, "font-patch.json");
            Equal(0, CommandApplication.RunCore([
                "font-import-bdf", sourceFont, glyphMap, bdf, patchedFont,
                "--preview", fontPreviewAfter,
                "--columns", "16", "--scale", "2",
                "--json", fontPatchReport]));
            SequenceEqual(
                File.ReadAllBytes(Path.Combine(Fixtures, "reference-lt-russian.bin")),
                File.ReadAllBytes(patchedFont));
            True(File.Exists(fontPreviewAfter), "Font patch did not create a PNG preview.");
            Equal(0, CommandApplication.RunCore(["font-inspect", patchedFont, glyphMap]));

            byte[] originalFont = File.ReadAllBytes(sourceFont);
            Equal(3, CommandApplication.RunCore([
                "font-import-bdf", sourceFont, glyphMap, bdf, sourceFont]));
            SequenceEqual(originalFont, File.ReadAllBytes(sourceFont));

            string workspace = Path.Combine(temp, "workspace");
            _ = TranslationWorkspaceService.Export(protectedCpk, glyphMap, workspace, ScriptProfile.Rgo, [0]);
            string manifest = Path.Combine(workspace, "workspace.json");
            byte[] originalManifest = File.ReadAllBytes(manifest);
            Equal(3, CommandApplication.RunCore(["workspace-validate", workspace, protectedCpk, "--json", manifest]));
            SequenceEqual(originalManifest, File.ReadAllBytes(manifest));

            string outputCpk = Path.Combine(temp, "translated.cpk");
            string defaultPlan = Path.ChangeExtension(outputCpk, ".eboot-size-plan.json");
            Equal(
                3,
                CommandApplication.RunCore(
                    ["workspace-build", workspace, protectedCpk, outputCpk, "--json", defaultPlan]));
            True(!File.Exists(outputCpk), "Rejected workspace build created a CPK output.");
            True(!File.Exists(defaultPlan), "Rejected workspace build created or replaced its EBOOT plan/report path.");

            string ebootSource = Path.Combine(temp, "eboot.bin");
            string planPath = Path.Combine(temp, "plan.json");
            File.WriteAllBytes(ebootSource, new byte[128]);
            File.WriteAllText(planPath, "preserve-me");
            Equal(3, CommandApplication.RunCore(["apply-eboot-plan", ebootSource, planPath, planPath]));
            Equal("preserve-me", File.ReadAllText(planPath));

            True(PathUtilities.IsWithinOrSame(manifest, workspace), "Workspace manifest must be detected inside workspace.");
            True(!PathUtilities.IsWithinOrSame(Path.Combine(temp, "workspace-sibling", "x"), workspace), "Sibling path must not be treated as a workspace child.");
        }
        finally
        {
            if (Directory.Exists(temp)) Directory.Delete(temp, true);
        }
    }

    /// <summary>
    /// Verifies customer archive behavior and invariants.
    /// </summary>
    /// <param name="path">The file-system path to process.</param>
    private static void TestCustomerArchive(string path)
    {
        CustomerAuditReport report = CustomerArchiveAuditor.Audit(path);
        True(report.HasKnownRgoEboot, "Known ULJM05752 EBOOT must be found in customer archive.");
        True(report.RequiredMissing.Contains("sc.cpk", StringComparer.OrdinalIgnoreCase), "Customer archive must still lack sc.cpk.");
        True(report.RequiredMissing.Contains("lt.bin", StringComparer.OrdinalIgnoreCase), "Customer archive must still lack lt.bin.");
        True(report.Files.Any(static file => file.IsAllZero), "Customer archive must contain the all-zero BOOT placeholder.");
    }

    /// <summary>
    /// Verifies customer VWF CLI behavior and invariants.
    /// </summary>
    /// <param name="archivePath">The archive path value.</param>
    private static void TestCustomerVwfCli(string archivePath)
    {
        string temp = Path.Combine(Path.GetTempPath(), "lsptool-vwf-cli-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            string sourcePath = Path.Combine(temp, "EBOOT.BIN");
            using (ZipArchive archive = ZipFile.OpenRead(archivePath))
            {
                ZipArchiveEntry[] matches = archive.Entries
                    .Where(static entry => !string.IsNullOrEmpty(entry.Name)
                        && entry.Name.Equals("EBOOT.BIN", StringComparison.Ordinal))
                    .ToArray();
                Equal(1, matches.Length);
                using Stream input = matches[0].Open();
                using FileStream output = new(sourcePath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                input.CopyTo(output);
            }
            byte[] sourceBefore = File.ReadAllBytes(sourcePath);

            string groupsReport = Path.Combine(temp, "groups.json");
            Equal(0, CommandApplication.RunCore(["eboot-vwf-groups", "--json", groupsReport]));
            using (JsonDocument document = JsonDocument.Parse(File.ReadAllText(groupsReport)))
            {
                Equal(133, document.RootElement.GetProperty("totalPatchCount").GetInt32());
                Equal(5, document.RootElement.GetProperty("groups").GetArrayLength());
            }

            string inspectReport = Path.Combine(temp, "inspect.json");
            Equal(0, CommandApplication.RunCore([
                "eboot-vwf-inspect", sourcePath, "--json", inspectReport]));
            using (JsonDocument document = JsonDocument.Parse(File.ReadAllText(inspectReport)))
            {
                JsonElement root = document.RootElement;
                True(root.GetProperty("compatibleKnownLineage").GetBoolean(),
                    "CLI VWF inspection did not recognize the customer EBOOT lineage.");
                Equal(133, root.GetProperty("originalWordCount").GetInt32());
                Equal(0, root.GetProperty("patchedWordCount").GetInt32());
            }

            string fullPath = Path.Combine(temp, "EBOOT.VWF.ELF");
            string fullReport = Path.Combine(temp, "apply.json");
            Equal(0, CommandApplication.RunCore([
                "eboot-vwf-apply", sourcePath, fullPath, "--json", fullReport]));
            Equal(RgoVwfPatchProfile.KnownFullPatchedSha256, BinaryUtilities.Sha256HexFile(fullPath));

            string verifyFullReport = Path.Combine(temp, "verify-full.json");
            Equal(0, CommandApplication.RunCore([
                "verify-eboot", fullPath, "--json", verifyFullReport]));
            using (JsonDocument document = JsonDocument.Parse(File.ReadAllText(verifyFullReport)))
            {
                Equal(
                    "exact-verified-vwf-revision",
                    document.RootElement.GetProperty("compatibility").GetString() ?? string.Empty);
            }

            string secondPath = Path.Combine(temp, "EBOOT.VWF.SECOND.ELF");
            Equal(0, CommandApplication.RunCore(["eboot-vwf-apply", fullPath, secondPath]));
            SequenceEqual(File.ReadAllBytes(fullPath), File.ReadAllBytes(secondPath));

            string corePath = Path.Combine(temp, "EBOOT.CORE.ELF");
            Equal(0, CommandApplication.RunCore([
                "eboot-vwf-apply", sourcePath, corePath, "--groups", "core-only"]));
            Equal(2, CommandApplication.RunCore(["verify-eboot", corePath]));
            RgoExecutableCheck coreCheck = RgoProfile.VerifyExecutable(File.ReadAllBytes(corePath));
            Equal(ExecutableCompatibility.RecognizedVwfPatchLineage, coreCheck.Compatibility);
            Equal(66, coreCheck.VwfPatchedWordCount);

            EbootSizePatchPlan plan = EbootSizePatchPlan.Create(
                ScriptProfile.Rgo,
                [new EbootSizePatchEntry { Id = 1, OldBlocks2K = 110, NewBlocks2K = 112 }]);
            string planPath = Path.Combine(temp, "size-plan.json");
            plan.Save(planPath);
            string combinedPath = Path.Combine(temp, "EBOOT.VWF.SIZE.ELF");
            Equal(0, CommandApplication.RunCore([
                "eboot-vwf-apply", sourcePath, combinedPath, "--size-plan", planPath]));
            RgoExecutableCheck combinedCheck = RgoProfile.VerifyExecutable(File.ReadAllBytes(combinedPath));
            Equal(ExecutableCompatibility.RecognizedVwfPatchLineage, combinedCheck.Compatibility);
            True(combinedCheck.ScriptSizeTableModified,
                "Combined CLI build did not report the adjusted script-size table.");
            Equal((ushort)112, combinedCheck.ScriptSlots.Single(slot => slot.Id == 1).Blocks2K);
            Equal(RgoProfile.KnownScriptHeapBytes, combinedCheck.ScriptHeapBytes);
            True(combinedCheck.ScriptHeapConsistent,
                "Small size-plan build did not preserve the verified script heap.");
            Equal(0, CommandApplication.RunCore(["verify-eboot", combinedPath]));

            EbootSizePatchPlan largePlan = EbootSizePatchPlan.Create(
                ScriptProfile.Rgo,
                [new EbootSizePatchEntry { Id = 1, OldBlocks2K = 110, NewBlocks2K = 1200 }]);
            string largePlanPath = Path.Combine(temp, "large-size-plan.json");
            largePlan.Save(largePlanPath);
            string largeCombinedPath = Path.Combine(temp, "EBOOT.VWF.LARGE.ELF");
            string largeCombinedReport = Path.Combine(temp, "large-apply.json");
            Equal(0, CommandApplication.RunCore([
                "eboot-build", sourcePath, largeCombinedPath,
                "--size-plan", largePlanPath,
                "--json", largeCombinedReport]));
            RgoExecutableCheck largeCombinedCheck = RgoProfile.VerifyExecutable(
                File.ReadAllBytes(largeCombinedPath));
            Equal(ExecutableCompatibility.RecognizedVwfPatchLineage, largeCombinedCheck.Compatibility);
            Equal((ushort)1200, largeCombinedCheck.ScriptSlots.Single(slot => slot.Id == 1).Blocks2K);
            Equal(1200 * 2048, largeCombinedCheck.ScriptHeapBytes);
            Equal(1200 * 2048, largeCombinedCheck.RequiredScriptHeapBytes);
            True(largeCombinedCheck.ScriptHeapModified && largeCombinedCheck.ScriptHeapConsistent,
                "Large size-plan build did not expand the script heap safely.");
            Equal(0, CommandApplication.RunCore(["verify-eboot", largeCombinedPath]));
            using (JsonDocument document = JsonDocument.Parse(File.ReadAllText(largeCombinedReport)))
            {
                Equal(1200 * 2048, document.RootElement.GetProperty("finalScriptHeapBytes").GetInt32());
                True(document.RootElement.GetProperty("finalKnownLineage").GetBoolean(),
                    "Large size-plan JSON report lost EBOOT lineage provenance.");
            }

            string pristineElfPath = Path.Combine(temp, "EBOOT.PRISTINE.ELF");
            Equal(0, CommandApplication.RunCore(["decrypt-eboot", sourcePath, pristineElfPath]));
            string planOnlyPath = Path.Combine(temp, "EBOOT.LARGE.PLAN.ONLY.ELF");
            string planOnlyReport = Path.Combine(temp, "large-plan-only.json");
            Equal(0, CommandApplication.RunCore([
                "apply-eboot-plan", pristineElfPath, largePlanPath, planOnlyPath,
                "--json", planOnlyReport]));
            RgoVwfPatchInspection planOnlyInspection = RgoVwfPatchProfile.Inspect(
                File.ReadAllBytes(planOnlyPath));
            True(planOnlyInspection.CompatibleKnownLineage
                && planOnlyInspection.OriginalWordCount == 133
                && planOnlyInspection.ScriptSizeTableModified
                && planOnlyInspection.ScriptHeapModified
                && planOnlyInspection.ScriptHeapConsistent
                && planOnlyInspection.ScriptHeapBytes == 1200 * 2048,
                "Standalone EBOOT size-plan path did not preserve verified RGO lineage.");
            using (JsonDocument document = JsonDocument.Parse(File.ReadAllText(planOnlyReport)))
            {
                True(document.RootElement.GetProperty("rgoKnownLineage").GetBoolean(),
                    "Standalone size-plan JSON report did not record verified RGO lineage.");
                Equal(1200 * 2048, document.RootElement.GetProperty("scriptHeapBytes").GetInt32());
            }

            string rejectedCombined = Path.Combine(temp, "rejected-partial-plan.elf");
            Equal(3, CommandApplication.RunCore([
                "eboot-vwf-apply", corePath, rejectedCombined, "--size-plan", planPath]));
            True(!File.Exists(rejectedCombined),
                "Rejected partial-patch + size-plan operation created an output file.");

            Equal(3, CommandApplication.RunCore(["eboot-vwf-apply", sourcePath, sourcePath]));
            SequenceEqual(sourceBefore, File.ReadAllBytes(sourcePath));
        }
        finally
        {
            if (Directory.Exists(temp)) Directory.Delete(temp, true);
        }
    }

    /// <summary>
    /// Reads option while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="args">The command-line arguments to parse and execute.</param>
    /// <param name="name">The logical name used for lookup or diagnostics.</param>
    /// <returns>The resulting text, path, identifier, or hexadecimal digest.</returns>
    private static string? ReadOption(string[] args, string name)
    {
        for (int i = 0; i + 1 < args.Length; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
        }
        return null;
    }

    /// <summary>
    /// Asserts that a test action throws the expected exception or toolkit error code.
    /// </summary>
    /// <param name="code">The code value.</param>
    /// <param name="action">The action value.</param>
    private static void Throws(string code, Action action)
    {
        try
        {
            action();
        }
        catch (ToolkitException ex) when (ex.Code == code)
        {
            return;
        }
        throw new InvalidOperationException($"Expected ToolkitException with code {code}.");
    }

    /// <summary>
    /// Fails a test when the supplied condition is false, retaining its diagnostic message.
    /// </summary>
    /// <param name="value">The value to process.</param>
    /// <param name="message">The diagnostic message used when validation fails.</param>
    private static void True(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    /// <summary>
    /// Fails a test when the actual scalar value differs from the expected value.
    /// </summary>
    /// <typeparam name="T">The t type used by the operation.</typeparam>
    /// <param name="expected">The expected value.</param>
    /// <param name="actual">The actual value.</param>
    private static void Equal<T>(T expected, T actual) where T : notnull
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"Expected '{expected}', got '{actual}'.");
    }

    /// <summary>
    /// Fails a test when the actual sequence differs in element order, values or length.
    /// </summary>
    /// <typeparam name="T">The t type used by the operation.</typeparam>
    /// <param name="expected">The expected value.</param>
    /// <param name="actual">The actual value.</param>
    private static void SequenceEqual<T>(IEnumerable<T> expected, IEnumerable<T> actual)
    {
        if (!expected.SequenceEqual(actual)) throw new InvalidOperationException("Sequences differ.");
    }
    /// <summary>Runs a filesystem test in a unique owned directory and removes it even if the assertion fails.</summary>
    /// <param name="action">The test receiving the new directory's absolute path.</param>
    private static void WithTemporaryDirectory(Action<string> action)
    {
        string path = Path.Combine(Path.GetTempPath(), "lsptool-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        try
        {
            action(path);
        }
        finally
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
    }

}
