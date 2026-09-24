using System.Globalization;
using System.Text;
using System.Text.Json;
using LuckyStarPspToolkit;
using LuckyStarPspToolkit.Formats.Audit;
using LuckyStarPspToolkit.Formats.Cri;
using LuckyStarPspToolkit.Formats.Diagnostics;
using LuckyStarPspToolkit.Formats.Fonts;
using LuckyStarPspToolkit.Formats.Iso;
using LuckyStarPspToolkit.Formats.Scripts;
using LuckyStarPspToolkit.Formats.Text;
using LuckyStarPspToolkit.Formats.Workspace;
using CoreAtomicFile = LuckyStarPspToolkit.AtomicFile;
using CoreToolkitException = LuckyStarPspToolkit.ToolkitException;
using FormatAtomicFile = LuckyStarPspToolkit.Formats.Common.AtomicFile;
using FormatBinaryUtilities = LuckyStarPspToolkit.Formats.Common.BinaryUtilities;
using FormatFileLimits = LuckyStarPspToolkit.Formats.Common.FileLimits;
using FormatPathUtilities = LuckyStarPspToolkit.Formats.Common.PathUtilities;
using FormatToolkitException = LuckyStarPspToolkit.Formats.Common.ToolkitException;
using ToolkitBuildInfo = LuckyStarPspToolkit.Formats.Common.ToolkitBuildInfo;

namespace LuckyStarPspToolkit.Cli;

/// <summary>
/// Represents the toolkit's command application model or service.
/// </summary>
public static class CommandApplication
{
    /// <summary>The fixed exit success value used by this format or revision.</summary>
    private const int ExitSuccess = 0;
    /// <summary>The fixed exit incomplete value used by this format or revision.</summary>
    private const int ExitIncomplete = 2;
    /// <summary>The fixed exit invalid input value used by this format or revision.</summary>
    private const int ExitInvalidInput = 3;
    /// <summary>The fixed exit usage value used by this format or revision.</summary>
    private const int ExitUsage = 64;
    /// <summary>The fixed exit software value used by this format or revision.</summary>
    private const int ExitSoftware = 70;
    /// <summary>The fixed exit io value used by this format or revision.</summary>
    private const int ExitIo = 74;

    /// <summary>
    /// Parses and executes a command-line invocation and returns a stable process exit code.
    /// </summary>
    /// <param name="args">The command-line arguments to parse and execute.</param>
    /// <returns>A stable process exit code.</returns>
    public static int Run(string[] args) => LicensedCommandEntry.Run(args);

    /// <summary>Runs command implementations after authorization; friend regression tests exercise parsers without issuing customer grants.</summary>
    /// <param name="args">Validated or test-supplied command arguments.</param>
    /// <returns>Original command exit code.</returns>
    internal static int RunCore(string[] args)
    {
        Console.InputEncoding = Encoding.UTF8;
        Console.OutputEncoding = new UTF8Encoding(false);

        try
        {
            if (args.Length == 0)
            {
                PrintHelp(Console.Error);
                return ExitUsage;
            }

            string command = args[0].ToLowerInvariant();
            string[] tail = args[1..];
            return command switch
            {
                "help" or "--help" or "-h" => Help(tail),
                "version" or "--version" or "-v" => Version(tail),
                "inspect" => Inspect(tail),
                "audit-rgo" => AuditRgo(tail),
                "verify-eboot" => VerifyEboot(tail),
                "decrypt-eboot" => DecryptEboot(tail),
                "eboot-vwf-groups" => EbootVwfGroups(tail),
                "eboot-vwf-inspect" => EbootVwfInspect(tail),
                "eboot-vwf-apply" or "eboot-build" => EbootVwfApply(tail),
                "sfo" => InspectSfo(tail),
                "iso-list" => IsoList(tail),
                "iso-extract" => IsoExtract(tail),
                "iso-replace" => IsoReplace(tail),
                "iso-apply-manifest" or "iso-patch" => IsoApplyManifest(tail),
                "collect-assets" => CollectAssets(tail),
                "cpk-list" => CpkList(tail),
                "cpk-verify" => CpkVerify(tail),
                "cpk-extract" => CpkExtract(tail),
                "cpk-replace" => CpkReplace(tail),
                "script-inspect" => ScriptInspect(tail),
                "glyph-map-validate" => GlyphMapValidate(tail),
                "font-inspect" => FontInspect(tail),
                "font-import-bdf" => FontImportBdf(tail),
                "workspace-export" => WorkspaceExport(tail),
                "workspace-validate" => WorkspaceValidate(tail),
                "workspace-build" => WorkspaceBuild(tail),
                "apply-eboot-plan" => ApplyEbootPlan(tail),
                "customer-audit" => CustomerAudit(tail),
                "formats-self-test" => FormatsSelfTest(tail),
                "self-test" => SelfTest(tail),
                _ => throw new UsageException($"Unknown command '{args[0]}'.")
            };
        }
        catch (UsageException ex)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            Console.Error.WriteLine();
            PrintHelp(Console.Error);
            return ExitUsage;
        }
        catch (FormatToolkitException ex)
        {
            Console.Error.WriteLine($"error [{ex.Code}]: {ex.Message}");
            return ExitInvalidInput;
        }
        catch (CoreToolkitException ex)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            return ExitInvalidInput;
        }
        catch (UnauthorizedAccessException ex)
        {
            Console.Error.WriteLine($"error: access denied: {ex.Message}");
            return ExitIo;
        }
        catch (IOException ex)
        {
            Console.Error.WriteLine($"error: I/O failure: {ex.Message}");
            return ExitIo;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"fatal: {ex.GetType().Name}: {ex.Message}");
            return ExitSoftware;
        }
    }

    /// <summary>
    /// Executes the <c>help</c> CLI command and returns a stable process exit code.
    /// </summary>
    /// <param name="args">The command-line arguments to parse and execute.</param>
    /// <returns>A stable process exit code.</returns>
    private static int Help(string[] args)
    {
        ParsedArguments.Parse(args, 0);
        PrintHelp(Console.Out);
        return ExitSuccess;
    }

    /// <summary>
    /// Executes the <c>version</c> CLI command and returns a stable process exit code.
    /// </summary>
    /// <param name="args">The command-line arguments to parse and execute.</param>
    /// <returns>A stable process exit code.</returns>
    private static int Version(string[] args)
    {
        ParsedArguments.Parse(args, 0);
        Console.WriteLine(ToolkitBuildInfo.Version);
        return ExitSuccess;
    }

    /// <summary>
    /// Inspects the supplied input and returns a structured diagnostic result.
    /// </summary>
    /// <param name="args">The command-line arguments to parse and execute.</param>
    /// <returns>A stable process exit code.</returns>
    private static int Inspect(string[] args)
    {
        ParsedArguments options = ParsedArguments.Parse(args, 1, ["--json"]);
        ProtectReportFromInput(options.GetOption("--json"), options.Positionals[0]);
        InspectionResult result = InputInspector.Inspect(options.Positionals[0]);
        WriteReport(InputInspector.ToHumanText(result), JsonDefaults.Serialize(result), options.GetOption("--json"));
        return ExitSuccess;
    }

    /// <summary>
    /// Executes the <c>audit-rgo</c> CLI command and returns a stable process exit code.
    /// </summary>
    /// <param name="args">The command-line arguments to parse and execute.</param>
    /// <returns>A stable process exit code.</returns>
    private static int AuditRgo(string[] args)
    {
        ParsedArguments options = ParsedArguments.Parse(args, 1, ["--json"]);
        ProtectReportFromInput(options.GetOption("--json"), options.Positionals[0]);
        InspectionResult inspection = InputInspector.Inspect(options.Positionals[0]);
        RgoAudit audit = RgoProfile.Audit(inspection);
        WriteReport(RgoProfile.AuditToHumanText(audit), JsonDefaults.Serialize(audit), options.GetOption("--json"));
        return audit.Status == "ready-for-script-and-font-analysis" ? ExitSuccess : ExitIncomplete;
    }

    /// <summary>
    /// Verifies EBOOT while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="args">The command-line arguments to parse and execute.</param>
    /// <returns>A stable process exit code.</returns>
    /// <remarks>Malformed input or violated preconditions are rejected before a persistent output is committed.</remarks>
    private static int VerifyEboot(string[] args)
    {
        ParsedArguments options = ParsedArguments.Parse(args, 1, ["--json"]);
        ProtectReportFromInput(options.GetOption("--json"), options.Positionals[0]);
        byte[] source = ReadSingleFile(options.Positionals[0]);
        RgoExecutableCheck check = RgoProfile.VerifyExecutable(source);
        WriteReport(ExecutableCheckToHuman(check), JsonDefaults.Serialize(check), options.GetOption("--json"));
        return check.Compatibility switch
        {
            ExecutableCompatibility.ExactVerifiedRevision
                or ExecutableCompatibility.ExactVerifiedVwfRevision
                or ExecutableCompatibility.RepresentativePatchPointsMatch => ExitSuccess,
            ExecutableCompatibility.RecognizedVwfPatchLineage
                when check.VwfOriginalWordCount == 0
                    && check.VwfMismatchWordCount == 0
                    && check.ScriptSizeTableConsistent
                    && check.ScriptHeapConsistent => ExitSuccess,
            ExecutableCompatibility.RecognizedVwfPatchLineage => ExitIncomplete,
            _ => ExitInvalidInput
        };
    }

    /// <summary>
    /// Executes the <c>decrypt-eboot</c> CLI command and returns a stable process exit code.
    /// </summary>
    /// <param name="args">The command-line arguments to parse and execute.</param>
    /// <returns>A stable process exit code.</returns>
    /// <remarks>Malformed input or violated preconditions are rejected before a persistent output is committed.</remarks>
    private static int DecryptEboot(string[] args)
    {
        ParsedArguments options = ParsedArguments.Parse(args, 2, ["--json"]);
        string inputPath = FormatPathUtilities.Normalize(options.Positionals[0]);
        string outputPath = FormatPathUtilities.Normalize(options.Positionals[1]);
        FormatPathUtilities.RequireDifferent(
            inputPath,
            outputPath,
            "OUTPUT_SOURCE_COLLISION",
            "Refusing to overwrite the encrypted input file.");
        RequireReportDifferent(options.GetOption("--json"), inputPath, outputPath);

        byte[] source = ReadSingleFile(inputPath);
        string sourceHash = CryptoUtilities.Sha256Hex(source);
        byte[] plaintext;
        string decryptedHash;
        string discId;
        if (sourceHash.Equals(NimProfile.KnownEncryptedSha256, StringComparison.OrdinalIgnoreCase))
        {
            PspDecryptionResult result = PspPrxReader.DecryptVerified(source);
            Elf32Info elf = ElfReader.Parse32LittleEndian(result.Elf);
            if (result.Header.Tag != PspPrxReader.SupportedNimTag || !elf.IsPspMips
                || result.Elf.Length != NimProfile.KnownDecryptedSize
                || !result.DecryptedSha256.Equals(NimProfile.KnownDecryptedSha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new CoreToolkitException("NIM executable differs from the verified revision.");
            }
            plaintext = result.Elf;
            decryptedHash = result.DecryptedSha256;
            discId = NimProfile.DiscId;
        }
        else
        {
            RgoExecutableCheck check = RgoProfile.VerifyExecutable(source);
            if (check.Compatibility != ExecutableCompatibility.ExactVerifiedRevision)
            {
                throw new CoreToolkitException($"Refusing to write a verified ELF: {check.Message}");
            }
            plaintext = check.DecryptedElf
                ?? throw new CoreToolkitException("Verified executable did not retain decrypted data.");
            decryptedHash = check.DecryptedSha256
                ?? throw new CoreToolkitException("Verified executable has no decrypted checksum.");
            discId = RgoProfile.DiscId;
        }
        CoreAtomicFile.WriteAllBytes(outputPath, plaintext);

        var report = new
        {
            schema = "lucky-star-psp.decrypt-eboot.v1",
            input = inputPath,
            output = outputPath,
            discId,
            inputSha256 = sourceHash,
            outputSha256 = decryptedHash,
            outputSize = plaintext.Length,
            compatibility = "ExactVerifiedRevision",
            patchProfileAvailable = discId == RgoProfile.DiscId
        };
        string human =
            $"Decrypted ELF written to {outputPath}{Environment.NewLine}" +
            $"SHA-256: {decryptedHash}{Environment.NewLine}" +
            $"Size: {plaintext.Length} bytes{Environment.NewLine}";
        WriteReport(human.ToString(), Serialize(report), options.GetOption("--json"));
        return ExitSuccess;
    }

    /// <summary>
    /// Executes the <c>eboot-vwf-groups</c> CLI command and returns a stable process exit code.
    /// </summary>
    /// <param name="args">The command-line arguments to parse and execute.</param>
    /// <returns>A stable process exit code.</returns>
    private static int EbootVwfGroups(string[] args)
    {
        ParsedArguments options = ParsedArguments.Parse(args, 0, ["--json"]);
        var report = new
        {
            schema = "lucky-star-psp.rgo-vwf-groups.v1",
            profileId = RgoVwfPatchProfile.ProfileId,
            originalDecryptedSha256 = RgoProfile.KnownDecryptedSha256,
            fullPatchedSha256 = RgoVwfPatchProfile.KnownFullPatchedSha256,
            totalPatchCount = RgoVwfPatchProfile.Patches.Count,
            groups = RgoVwfPatchProfile.Groups
        };
        var human = new StringBuilder();
        human.AppendLine($"RGO EBOOT patch profile: {RgoVwfPatchProfile.ProfileId}");
        human.AppendLine($"Total 32-bit patch words: {RgoVwfPatchProfile.Patches.Count}");
        foreach (RgoEbootPatchGroupInfo group in RgoVwfPatchProfile.Groups)
        {
            human.AppendLine($"  {group.Id}: {group.PatchCount} - {group.Description}");
        }
        human.AppendLine("Preset 'russian-text' selects all groups; 'core-only' selects vwf-core.");
        WriteReport(human.ToString(), Serialize(report), options.GetOption("--json"));
        return ExitSuccess;
    }

    /// <summary>
    /// Executes the <c>eboot-vwf-inspect</c> CLI command and returns a stable process exit code.
    /// </summary>
    /// <param name="args">The command-line arguments to parse and execute.</param>
    /// <returns>A stable process exit code.</returns>
    private static int EbootVwfInspect(string[] args)
    {
        ParsedArguments options = ParsedArguments.Parse(args, 1, ["--groups", "--json"]);
        ProtectReportFromInput(options.GetOption("--json"), options.Positionals[0]);
        byte[] source = ReadSingleFile(options.Positionals[0]);
        RgoVwfPatchInspection inspection = RgoVwfPatchProfile.Inspect(
            source,
            ParseVwfGroups(options.GetOption("--groups")));

        var human = new StringBuilder();
        human.AppendLine($"RGO EBOOT VWF inspection: {inspection.Message}");
        human.AppendLine($"  profile:             {inspection.ProfileId}");
        human.AppendLine($"  encrypted input:     {YesNo(inspection.SourceWasEncrypted)}");
        human.AppendLine($"  source SHA-256:       {inspection.SourceSha256}");
        human.AppendLine($"  decrypted SHA-256:    {inspection.DecryptedSha256}");
        human.AppendLine($"  patch-only canonical: {inspection.CanonicalPatchOnlySha256}");
        human.AppendLine($"  canonical SHA-256:    {inspection.CanonicalOriginalSha256}");
        human.AppendLine($"  script table changed: {YesNo(inspection.ScriptSizeTableModified)}");
        human.AppendLine($"  script table valid:   {YesNo(inspection.ScriptSizeTableConsistent)}");
        human.AppendLine($"  script heap changed:  {YesNo(inspection.ScriptHeapModified)}");
        human.AppendLine($"  script heap valid:    {YesNo(inspection.ScriptHeapConsistent)}");
        human.AppendLine($"  script heap bytes:    {inspection.ScriptHeapBytes} (required {inspection.RequiredScriptHeapBytes})");
        human.AppendLine($"  known lineage:        {YesNo(inspection.CompatibleKnownLineage)}");
        human.AppendLine($"  selected groups:      {string.Join(", ", inspection.SelectedGroups)}");
        human.AppendLine($"  original/patched/bad: {inspection.OriginalWordCount}/{inspection.PatchedWordCount}/{inspection.MismatchWordCount}");
        WriteReport(human.ToString(), Serialize(inspection), options.GetOption("--json"));
        return inspection.CompatibleKnownLineage && inspection.MismatchWordCount == 0
            ? ExitSuccess
            : ExitInvalidInput;
    }

    /// <summary>
    /// Executes the <c>eboot-vwf-apply</c> CLI command and returns a stable process exit code.
    /// </summary>
    /// <param name="args">The command-line arguments to parse and execute.</param>
    /// <returns>A stable process exit code.</returns>
    private static int EbootVwfApply(string[] args)
    {
        ParsedArguments options = ParsedArguments.Parse(
            args,
            2,
            ["--groups", "--size-plan", "--json"],
            ["--experimental-vwf"]);
        string sourcePath = FormatPathUtilities.Normalize(options.Positionals[0]);
        string outputPath = FormatPathUtilities.Normalize(options.Positionals[1]);
        string? sizePlanPath = options.GetOption("--size-plan") is { } rawPlan
            ? FormatPathUtilities.Normalize(rawPlan)
            : null;

        FormatPathUtilities.RequireDifferent(
            sourcePath,
            outputPath,
            "EBOOT_VWF_OUTPUT_SOURCE",
            "Refusing to overwrite the source EBOOT.");
        if (sizePlanPath is not null)
        {
            FormatPathUtilities.RequireDifferent(
                sourcePath,
                sizePlanPath,
                "EBOOT_VWF_PLAN_SOURCE",
                "EBOOT source and size plan must be different files.");
            FormatPathUtilities.RequireDifferent(
                outputPath,
                sizePlanPath,
                "EBOOT_VWF_OUTPUT_PLAN",
                "EBOOT output and size plan must be different files.");
        }
        var protectedPaths = new List<string> { sourcePath, outputPath };
        if (sizePlanPath is not null)
        {
            protectedPaths.Add(sizePlanPath);
        }
        RequireReportDifferent(options.GetOption("--json"), protectedPaths.ToArray());

        if (!options.HasFlag("--experimental-vwf"))
        {
            throw new FormatToolkitException(
                "EBOOT_VWF_UNVERIFIED_RUNTIME",
                "The RGO VWF profile is not approved for game output: PPSSPP testing showed missing " +
                "Japanese glyphs on the name-entry keyboard. Use --experimental-vwf only for isolated " +
                "research after reviewing docs/EBOOT_VWF_PATCH_RU.md.");
        }

        byte[] source = ReadSingleFile(sourcePath);
        IReadOnlyList<string>? groups = ParseVwfGroups(options.GetOption("--groups"));
        RgoVwfPatchResult patch = RgoVwfPatchProfile.Apply(source, groups);
        byte[] output = patch.Output.ToArray();
        EbootSizePatchPlan? sizePlan = null;
        string? sizeOnlySha256 = null;
        if (sizePlanPath is not null)
        {
            sizePlan = EbootSizePatchPlan.Load(sizePlanPath);
            if (!string.Equals(
                    sizePlan.Game,
                    LuckyStarGame.RyououGakuenOutousaiPortable.ToString(),
                    StringComparison.Ordinal))
            {
                throw new FormatToolkitException(
                    "EBOOT_VWF_PLAN_GAME",
                    "The RGO VWF profile can only be combined with an RGO EBOOT size plan.");
            }
            if (!string.Equals(
                    patch.DecryptedInputSha256,
                    RgoProfile.KnownDecryptedSha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new FormatToolkitException(
                    "EBOOT_VWF_PLAN_SOURCE_STATE",
                    "Combining a size plan with the VWF patch requires the exact pristine decrypted ULJM05752 EBOOT. " +
                    "Apply both changes in one command instead of reusing a partially patched executable.");
            }

            byte[] sizePatched = sizePlan.Apply(patch.DecryptedInput);
            int tableLength = checked((sizePlan.LastIdInclusive - sizePlan.FirstId + 1) * 4);
            int tableEnd = checked(sizePlan.TableOffset + tableLength);
            int heapLuiEnd = checked(RgoProfile.ScriptHeapLuiOffset + sizeof(uint));
            int heapAddiuEnd = checked(RgoProfile.ScriptHeapAddiuOffset + sizeof(uint));
            foreach (RgoEbootPatchDefinition definition in RgoVwfPatchProfile.Patches)
            {
                int patchEnd = checked(definition.Offset + sizeof(uint));
                bool overlapsTable = RangesOverlap(
                    definition.Offset,
                    patchEnd,
                    sizePlan.TableOffset,
                    tableEnd);
                bool overlapsHeapLui = RangesOverlap(
                    definition.Offset,
                    patchEnd,
                    RgoProfile.ScriptHeapLuiOffset,
                    heapLuiEnd);
                bool overlapsHeapAddiu = RangesOverlap(
                    definition.Offset,
                    patchEnd,
                    RgoProfile.ScriptHeapAddiuOffset,
                    heapAddiuEnd);
                if (overlapsTable || overlapsHeapLui || overlapsHeapAddiu)
                {
                    throw new FormatToolkitException(
                        "EBOOT_VWF_PLAN_OVERLAP",
                        $"VWF patch '{definition.Name}' overlaps dynamic EBOOT script metadata.");
                }
            }

            for (int index = 0; index < sizePatched.Length; index++)
            {
                if (sizePatched[index] == patch.DecryptedInput[index])
                {
                    continue;
                }
                bool allowed = index >= sizePlan.TableOffset && index < tableEnd
                    || index >= RgoProfile.ScriptHeapLuiOffset && index < heapLuiEnd
                    || index >= RgoProfile.ScriptHeapAddiuOffset && index < heapAddiuEnd;
                if (!allowed)
                {
                    throw new FormatToolkitException(
                        "EBOOT_VWF_PLAN_UNEXPECTED_CHANGE",
                        $"Size-plan application changed unexpected EBOOT byte 0x{index:X}.");
                }
            }

            sizePatched.AsSpan(sizePlan.TableOffset, tableLength)
                .CopyTo(output.AsSpan(sizePlan.TableOffset, tableLength));
            sizePatched.AsSpan(RgoProfile.ScriptHeapLuiOffset, sizeof(uint))
                .CopyTo(output.AsSpan(RgoProfile.ScriptHeapLuiOffset, sizeof(uint)));
            sizePatched.AsSpan(RgoProfile.ScriptHeapAddiuOffset, sizeof(uint))
                .CopyTo(output.AsSpan(RgoProfile.ScriptHeapAddiuOffset, sizeof(uint)));
            sizeOnlySha256 = FormatBinaryUtilities.Sha256Hex(sizePatched);

        }

        RgoVwfPatchInspection finalInspection = RgoVwfPatchProfile.Inspect(output, groups);
        if (!finalInspection.CompatibleKnownLineage
            || finalInspection.MismatchWordCount != 0
            || finalInspection.OriginalWordCount != 0)
        {
            throw new FormatToolkitException(
                "EBOOT_VWF_VERIFY",
                $"Final EBOOT verification failed: {finalInspection.Message}");
        }

        string outputSha256 = FormatBinaryUtilities.Sha256Hex(output);
        var report = new
        {
            schema = "lucky-star-psp.rgo-eboot-build.v1",
            profileId = RgoVwfPatchProfile.ProfileId,
            source = sourcePath,
            sourceWasEncrypted = patch.SourceWasEncrypted,
            sourceSha256 = patch.SourceSha256,
            decryptedInputSha256 = patch.DecryptedInputSha256,
            canonicalPatchOnlySha256 = patch.CanonicalPatchOnlySha256,
            canonicalOriginalSha256 = patch.CanonicalOriginalSha256,
            scriptSizeTableModifiedBeforeBuild = patch.ScriptSizeTableModified,
            scriptHeapModifiedBeforeBuild = patch.ScriptHeapModified,
            output = outputPath,
            outputSha256,
            outputIsDecryptedElf = true,
            gameRuntimeVerified = false,
            knownRuntimeRegression = "Japanese name-entry keyboard glyphs disappeared in PPSSPP with the full VWF patch.",
            finalScriptSizeTableModified = finalInspection.ScriptSizeTableModified,
            finalScriptHeapModified = finalInspection.ScriptHeapModified,
            finalScriptHeapBytes = finalInspection.ScriptHeapBytes,
            finalRequiredScriptHeapBytes = finalInspection.RequiredScriptHeapBytes,
            finalKnownLineage = finalInspection.CompatibleKnownLineage,
            selectedGroups = patch.SelectedGroups,
            selectedPatchCount = patch.SelectedPatchCount,
            newlyAppliedPatchCount = patch.NewlyAppliedPatchCount,
            previouslyAppliedPatchCount = patch.PreviouslyAppliedPatchCount,
            sizePlan = sizePlanPath,
            sizePlanEntryCount = sizePlan?.Entries.Count ?? 0,
            sizeOnlySha256
        };
        var human = new StringBuilder();
        human.AppendLine($"Patched decrypted RGO EBOOT written atomically: {outputPath}");
        human.AppendLine($"  selected groups: {string.Join(", ", patch.SelectedGroups)}");
        human.AppendLine($"  patch words: {patch.SelectedPatchCount}; newly applied: {patch.NewlyAppliedPatchCount}; already present: {patch.PreviouslyAppliedPatchCount}");
        human.AppendLine($"  size-plan entries: {sizePlan?.Entries.Count ?? 0}");
        human.AppendLine($"  script heap: {finalInspection.ScriptHeapBytes} bytes (required {finalInspection.RequiredScriptHeapBytes})");
        human.AppendLine($"  output SHA-256: {outputSha256}");
        human.AppendLine("  NOTE: output is a decrypted ELF payload, not a re-encrypted retail PRX.");
        human.AppendLine("  WARNING: experimental VWF profile; name-entry keyboard glyph regression remains unresolved.");

        var writes = new List<LuckyStarPspToolkit.Formats.Common.AtomicWriteRequest>
        {
            new(outputPath, output)
        };
        CommitArtifactsAndWriteReport(
            writes,
            human.ToString(),
            Serialize(report),
            options.GetOption("--json"));
        return ExitSuccess;
    }

    /// <summary>
    /// Inspects SFO while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="args">The command-line arguments to parse and execute.</param>
    /// <returns>A stable process exit code.</returns>
    private static int InspectSfo(string[] args)
    {
        ParsedArguments options = ParsedArguments.Parse(args, 1, ["--json"]);
        string path = FormatPathUtilities.Normalize(options.Positionals[0]);
        RequireReportDifferent(options.GetOption("--json"), path);
        SfoFile sfo = SfoReader.Parse(ReadSingleFile(path));
        var output = new StringBuilder();
        output.AppendLine(path);
        output.AppendLine($"  version: {HexUtilities.UInt32(sfo.Version)}");
        foreach (SfoEntry entry in sfo.Entries)
        {
            output.AppendLine($"  {entry.Key}: {entry.DisplayValue}");
        }
        WriteReport(output.ToString(), JsonDefaults.Serialize(sfo), options.GetOption("--json"));
        return ExitSuccess;
    }

    /// <summary>
    /// Executes the <c>iso-list</c> CLI command and returns a stable process exit code.
    /// </summary>
    /// <param name="args">The command-line arguments to parse and execute.</param>
    /// <returns>A stable process exit code.</returns>
    private static int IsoList(string[] args)
    {
        ParsedArguments options = ParsedArguments.Parse(args, 1, ["--json"]);
        string path = FormatPathUtilities.Normalize(options.Positionals[0]);
        RequireReportDifferent(options.GetOption("--json"), path);
        FormatFileLimits limits = FormatFileLimits.Default;
        using Iso9660Image image = Iso9660Image.Open(path, limits);
        long estimatedListingBytes = image.EstimateListingBytes();
        if (estimatedListingBytes > limits.MaximumTextBytes)
        {
            throw new FormatToolkitException(
                "ISO_LISTING_TOO_LARGE",
                $"ISO listing is estimated at {estimatedListingBytes} bytes; configured report limit is {limits.MaximumTextBytes} bytes.");
        }
        Iso9660Summary summary = image.CreateSummary();

        var human = new StringBuilder();
        human.AppendLine($"ISO 9660: {path}");
        human.AppendLine($"  volume: {summary.VolumeIdentifier}");
        human.AppendLine($"  size: {summary.SourceSize} bytes; blocks: {summary.VolumeBlockCount}; block size: {summary.LogicalBlockSize}");
        human.AppendLine($"  entries: {summary.EntryCount}");
        foreach (Iso9660Entry entry in summary.Entries)
        {
            human.AppendLine(
                $"  {(entry.IsDirectory ? "dir " : "file"),4} {entry.Size,12} " +
                $"{(entry.IsMultiExtent ? $"[{entry.Extents.Count} extents] " : string.Empty)}{entry.Path}");
        }

        string humanText = human.ToString();
        string json = JsonSerializer.Serialize(summary, PspAssetCollector.JsonOptions);
        if (Encoding.UTF8.GetByteCount(humanText) > limits.MaximumTextBytes
            || Encoding.UTF8.GetByteCount(json) > limits.MaximumTextBytes)
        {
            throw new FormatToolkitException(
                "ISO_LISTING_TOO_LARGE",
                $"ISO listing exceeds the configured report limit of {limits.MaximumTextBytes} bytes.");
        }
        WriteReport(humanText, json, options.GetOption("--json"));
        return ExitSuccess;
    }

    /// <summary>
    /// Executes the <c>iso-extract</c> CLI command and returns a stable process exit code.
    /// </summary>
    /// <param name="args">The command-line arguments to parse and execute.</param>
    /// <returns>A stable process exit code.</returns>
    private static int IsoExtract(string[] args)
    {
        ParsedArguments options = ParsedArguments.Parse(args, 3, ["--json"]);
        string sourcePath = FormatPathUtilities.Normalize(options.Positionals[0]);
        string isoPath = options.Positionals[1];
        string outputPath = FormatPathUtilities.Normalize(options.Positionals[2]);
        FormatPathUtilities.RequireDifferent(
            sourcePath,
            outputPath,
            "ISO_OUTPUT_SOURCE",
            "ISO extraction output cannot overwrite the source image.");
        RequireReportDifferent(options.GetOption("--json"), sourcePath, outputPath);

        using Iso9660Image image = Iso9660Image.Open(sourcePath);
        Iso9660Entry entry = image.GetEntry(isoPath);
        if (entry.IsDirectory)
        {
            throw new FormatToolkitException("ISO_ENTRY_DIRECTORY", $"ISO entry is a directory: {entry.Path}");
        }
        image.ExtractFile(entry.Path, outputPath);
        string sha256 = FormatBinaryUtilities.Sha256HexFile(outputPath);
        var report = new
        {
            schema = "lucky-star-psp.iso-extract.v1",
            source = sourcePath,
            volumeIdentifier = image.VolumeIdentifier,
            path = entry.Path,
            extentCount = entry.Extents.Count,
            output = outputPath,
            size = entry.Size,
            sha256
        };
        WriteReport(
            $"Extracted {entry.Path}: {entry.Size} bytes, {entry.Extents.Count} extent(s) -> {outputPath}{Environment.NewLine}" +
            $"SHA-256: {sha256}{Environment.NewLine}",
            JsonSerializer.Serialize(report, PspAssetCollector.JsonOptions),
            options.GetOption("--json"));
        return ExitSuccess;
    }

    /// <summary>
    /// Executes the <c>iso-replace</c> CLI command and returns a stable process exit code.
    /// </summary>
    /// <param name="args">The command-line arguments to parse and execute.</param>
    /// <returns>A stable process exit code.</returns>
    private static int IsoReplace(string[] args)
    {
        ParsedArguments options = ParsedArguments.Parse(
            args,
            4,
            [
                "--expect-source-sha256",
                "--expect-original-sha256",
                "--expect-original-size",
                "--expect-replacement-sha256",
                "--expect-replacement-size",
                "--json"
            ]);
        string sourcePath = FormatPathUtilities.Normalize(options.Positionals[0]);
        string isoPath = options.Positionals[1];
        string replacementPath = FormatPathUtilities.Normalize(options.Positionals[2]);
        string outputPath = FormatPathUtilities.Normalize(options.Positionals[3]);
        RequireReportDifferent(
            options.GetOption("--json"),
            sourcePath,
            replacementPath,
            outputPath);

        long? expectedOriginalSize = ParseNullableLongOption(
            options.GetOption("--expect-original-size"),
            "--expect-original-size",
            0,
            uint.MaxValue);
        long? expectedReplacementSize = ParseNullableLongOption(
            options.GetOption("--expect-replacement-size"),
            "--expect-replacement-size",
            0,
            uint.MaxValue);
        Iso9660RebuildReport report = Iso9660Rebuilder.Rebuild(
            sourcePath,
            [new Iso9660ReplacementRequest(
                isoPath,
                replacementPath,
                expectedOriginalSize,
                options.GetOption("--expect-original-sha256"),
                expectedReplacementSize,
                options.GetOption("--expect-replacement-sha256"))],
            outputPath,
            options.GetOption("--expect-source-sha256"));
        WriteReport(
            Iso9660Rebuilder.ToHumanText(report),
            JsonSerializer.Serialize(report, Iso9660Rebuilder.JsonOptions),
            options.GetOption("--json"));
        return ExitSuccess;
    }

    /// <summary>
    /// Executes the <c>iso-apply-manifest</c> CLI command and returns a stable process exit code.
    /// </summary>
    /// <param name="args">The command-line arguments to parse and execute.</param>
    /// <returns>A stable process exit code.</returns>
    private static int IsoApplyManifest(string[] args)
    {
        ParsedArguments options = ParsedArguments.Parse(args, 3, ["--json"]);
        string sourcePath = FormatPathUtilities.Normalize(options.Positionals[0]);
        string manifestPath = FormatPathUtilities.Normalize(options.Positionals[1]);
        string outputPath = FormatPathUtilities.Normalize(options.Positionals[2]);
        Iso9660PreparedPatch patch = Iso9660Rebuilder.LoadPatchManifest(manifestPath);
        string[] protectedPaths = [sourcePath, manifestPath, outputPath, .. patch.ReplacementFiles];
        RequireReportDifferent(options.GetOption("--json"), protectedPaths);

        Iso9660RebuildReport report = Iso9660Rebuilder.ApplyPreparedPatch(
            sourcePath,
            patch,
            outputPath);
        WriteReport(
            Iso9660Rebuilder.ToHumanText(report),
            JsonSerializer.Serialize(report, Iso9660Rebuilder.JsonOptions),
            options.GetOption("--json"));
        return ExitSuccess;
    }

    /// <summary>
    /// Executes the <c>collect-assets</c> CLI command and returns a stable process exit code.
    /// </summary>
    /// <param name="args">The command-line arguments to parse and execute.</param>
    /// <returns>A stable process exit code.</returns>
    private static int CollectAssets(string[] args)
    {
        ParsedArguments options = ParsedArguments.Parse(
            args,
            2,
            ["--json"],
            ["--include-optional", "--no-listing", "--skip-source-hash"]);
        string sourcePath = FormatPathUtilities.Normalize(options.Positionals[0]);
        string outputPath = FormatPathUtilities.Normalize(options.Positionals[1]);
        RequireReportDifferent(options.GetOption("--json"), sourcePath, outputPath);

        PspAssetCollectionOptions collectionOptions = new(
            IncludeOptional: options.HasFlag("--include-optional"),
            IncludeFileListing: !options.HasFlag("--no-listing"),
            ComputeSourceSha256: !options.HasFlag("--skip-source-hash"));
        PspAssetBundleReport report = PspAssetCollector.CollectFromIso(
            sourcePath,
            outputPath,
            collectionOptions);
        WriteReport(
            PspAssetCollector.ToHumanText(report),
            JsonSerializer.Serialize(report, PspAssetCollector.JsonOptions),
            options.GetOption("--json"));
        return report.Complete ? ExitSuccess : ExitIncomplete;
    }

    /// <summary>
    /// Executes the <c>cpk-list</c> CLI command and returns a stable process exit code.
    /// </summary>
    /// <param name="args">The command-line arguments to parse and execute.</param>
    /// <returns>A stable process exit code.</returns>
    private static int CpkList(string[] args)
    {
        ParsedArguments options = ParsedArguments.Parse(args, 1, ["--json"]);
        string path = FormatPathUtilities.Normalize(options.Positionals[0]);
        RequireReportDifferent(options.GetOption("--json"), path);
        byte[] input = FormatBinaryUtilities.ReadAllBytesBounded(path);
        CriCpkInspection archive = CriCpkArchive.Inspect(input);
        var model = new
        {
            schema = "lucky-star-psp.cpk-list.v1",
            path,
            sha256 = FormatBinaryUtilities.Sha256Hex(input),
            size = input.Length,
            alignment = archive.Alignment,
            contentOffset = archive.ContentOffset,
            itocOffset = archive.ItocOffset,
            entries = archive.Entries.Select(entry => new
            {
                id = entry.Id,
                packedSize = entry.PackedSize,
                extractSize = entry.ExtractSize,
                compressed = entry.IsCrilayla,
                sha256 = FormatBinaryUtilities.Sha256Hex(input.AsSpan(entry.Offset, entry.PackedSize))
            }).ToArray()
        };
        var human = new StringBuilder();
        human.AppendLine($"CPK: {path}");
        human.AppendLine($"  SHA-256: {model.sha256}");
        human.AppendLine($"  entries: {model.entries.Length}; alignment: {archive.Alignment}; content: 0x{archive.ContentOffset:X}");
        foreach (var entry in model.entries)
        {
            human.AppendLine(
                $"  {entry.id,5}: packed {entry.packedSize,9}; extracted {entry.extractSize,9}; " +
                $"{(entry.compressed ? "CRILAYLA" : "raw")}");
        }
        WriteReport(human.ToString(), Serialize(model), options.GetOption("--json"));
        return ExitSuccess;
    }

    /// <summary>
    /// Executes the <c>cpk-verify</c> CLI command and returns a stable process exit code.
    /// </summary>
    /// <param name="args">The command-line arguments to parse and execute.</param>
    /// <returns>A stable process exit code.</returns>
    private static int CpkVerify(string[] args)
    {
        ParsedArguments options = ParsedArguments.Parse(args, 1, ["--json", "--rebuilt"]);
        string path = FormatPathUtilities.Normalize(options.Positionals[0]);
        RequireReportDifferent(options.GetOption("--json"), path);
        byte[] source = FormatBinaryUtilities.ReadAllBytesBounded(path);
        CriCpkArchive archive = CriCpkArchive.Parse(source);
        CriCpkBuildResult rebuilt = archive.Build();
        string? rebuiltPath = options.GetOption("--rebuilt");
        if (rebuiltPath is not null)
        {
            string normalizedOutput = FormatPathUtilities.Normalize(rebuiltPath);
            FormatPathUtilities.RequireDifferent(
                path,
                normalizedOutput,
                "CPK_OUTPUT_SOURCE",
                "Refusing to overwrite the CPK being verified.");
            RequireReportDifferent(options.GetOption("--json"), normalizedOutput);
            FormatAtomicFile.WriteAllBytes(normalizedOutput, rebuilt.Data);
            rebuiltPath = normalizedOutput;
        }

        string sourceHash = FormatBinaryUtilities.Sha256Hex(source);
        string rebuiltHash = FormatBinaryUtilities.Sha256Hex(rebuilt.Data);
        var model = new
        {
            schema = "lucky-star-psp.cpk-verify.v1",
            path,
            sourceSha256 = sourceHash,
            rebuiltSha256 = rebuiltHash,
            sourceSize = source.Length,
            rebuiltSize = rebuilt.Data.Length,
            byteIdentical = source.AsSpan().SequenceEqual(rebuilt.Data),
            entryCount = archive.Entries.Count,
            semanticVerification = true,
            rebuiltPath
        };
        string human =
            $"CPK verification: PASS{Environment.NewLine}" +
            $"  entries: {model.entryCount}{Environment.NewLine}" +
            $"  source:  {model.sourceSize} bytes, {sourceHash}{Environment.NewLine}" +
            $"  rebuilt: {model.rebuiltSize} bytes, {rebuiltHash}{Environment.NewLine}" +
            $"  byte-identical: {(model.byteIdentical ? "yes" : "no; semantically equivalent")}{Environment.NewLine}";
        WriteReport(human, Serialize(model), options.GetOption("--json"));
        return ExitSuccess;
    }

    /// <summary>
    /// Executes the <c>cpk-extract</c> CLI command and returns a stable process exit code.
    /// </summary>
    /// <param name="args">The command-line arguments to parse and execute.</param>
    /// <returns>A stable process exit code.</returns>
    private static int CpkExtract(string[] args)
    {
        ParsedArguments options = ParsedArguments.Parse(args, 3, ["--json"], ["--packed"]);
        string sourcePath = FormatPathUtilities.Normalize(options.Positionals[0]);
        string outputPath = FormatPathUtilities.Normalize(options.Positionals[2]);
        FormatPathUtilities.RequireDifferent(
            sourcePath,
            outputPath,
            "CPK_EXTRACT_SOURCE",
            "Extraction output cannot overwrite the source CPK.");
        RequireReportDifferent(options.GetOption("--json"), sourcePath, outputPath);
        ushort id = ParseUShort(options.Positionals[1], "id");
        byte[] input = FormatBinaryUtilities.ReadAllBytesBounded(sourcePath);
        bool packed = options.HasFlag("--packed");
        byte[] output = CriCpkArchive.Extract(input, id, packed);
        FormatAtomicFile.WriteAllBytes(outputPath, output);
        var report = new
        {
            schema = "lucky-star-psp.cpk-extract.v1",
            source = sourcePath,
            id,
            packed,
            output = outputPath,
            size = output.Length,
            sha256 = FormatBinaryUtilities.Sha256Hex(output)
        };
        WriteReport(
            $"Extracted CPK entry {id}: {output.Length} bytes -> {outputPath}{Environment.NewLine}",
            Serialize(report),
            options.GetOption("--json"));
        return ExitSuccess;
    }

    /// <summary>
    /// Executes the <c>cpk-replace</c> CLI command and returns a stable process exit code.
    /// </summary>
    /// <param name="args">The command-line arguments to parse and execute.</param>
    /// <returns>A stable process exit code.</returns>
    private static int CpkReplace(string[] args)
    {
        ParsedArguments options = ParsedArguments.Parse(args, 4, ["--json"]);
        string sourcePath = FormatPathUtilities.Normalize(options.Positionals[0]);
        string replacementPath = FormatPathUtilities.Normalize(options.Positionals[2]);
        string outputPath = FormatPathUtilities.Normalize(options.Positionals[3]);
        FormatPathUtilities.RequireDifferent(
            sourcePath,
            outputPath,
            "CPK_OUTPUT_SOURCE",
            "Refusing to overwrite the source CPK.");
        FormatPathUtilities.RequireDifferent(
            replacementPath,
            outputPath,
            "CPK_OUTPUT_REPLACEMENT",
            "Refusing to overwrite the replacement input file.");
        RequireReportDifferent(options.GetOption("--json"), sourcePath, replacementPath, outputPath);
        ushort id = ParseUShort(options.Positionals[1], "id");
        byte[] source = FormatBinaryUtilities.ReadAllBytesBounded(sourcePath);
        byte[] replacement = FormatBinaryUtilities.ReadAllBytesBounded(replacementPath);
        CriCpkArchive archive = CriCpkArchive.Parse(source);
        archive.ReplaceEntry(id, replacement);
        CriCpkBuildResult result = archive.Build();
        FormatAtomicFile.WriteAllBytes(outputPath, result.Data);
        var report = new
        {
            schema = "lucky-star-psp.cpk-replace.v1",
            source = sourcePath,
            sourceSha256 = FormatBinaryUtilities.Sha256Hex(source),
            id,
            replacement = replacementPath,
            replacementSha256 = FormatBinaryUtilities.Sha256Hex(replacement),
            output = outputPath,
            outputSha256 = FormatBinaryUtilities.Sha256Hex(result.Data),
            sourceSize = source.Length,
            outputSize = result.Data.Length
        };
        WriteReport(
            $"Rebuilt CPK entry {id}: {source.Length} -> {result.Data.Length} bytes; output {outputPath}{Environment.NewLine}",
            Serialize(report),
            options.GetOption("--json"));
        return ExitSuccess;
    }

    /// <summary>
    /// Executes the <c>script-inspect</c> CLI command and returns a stable process exit code.
    /// </summary>
    /// <param name="args">The command-line arguments to parse and execute.</param>
    /// <returns>A stable process exit code.</returns>
    private static int ScriptInspect(string[] args)
    {
        ParsedArguments options = ParsedArguments.Parse(args, 1, ["--game", "--json"]);
        ScriptProfile profile = ScriptProfile.Parse(options.GetOption("--game") ?? "rgo");
        string path = FormatPathUtilities.Normalize(options.Positionals[0]);
        RequireReportDifferent(options.GetOption("--json"), path);
        byte[] input = FormatBinaryUtilities.ReadAllBytesBounded(path);
        LuckyStarScript script = LuckyStarScript.Parse(input, profile);
        var model = new
        {
            schema = "lucky-star-psp.script-inspect.v1",
            path,
            game = profile.Game.ToString(),
            sha256 = script.Sha256,
            size = input.Length,
            checksumValid = script.ChecksumValid,
            magic = $"0x{script.Magic:X4}",
            jumps = script.Jumps.Count,
            dialogs = script.Dialogs.Count,
            choiceGroups = script.ChoiceGroups.Count,
            choices = script.ChoiceGroups.Sum(static group => group.Choices.Count)
        };
        string human =
            $"Script: {path}{Environment.NewLine}" +
            $"  game: {model.game}; size: {model.size}; SHA-256: {model.sha256}{Environment.NewLine}" +
            $"  magic: {model.magic}; checksum: valid; jumps: {model.jumps}{Environment.NewLine}" +
            $"  dialogs: {model.dialogs}; choice groups: {model.choiceGroups}; choices: {model.choices}{Environment.NewLine}";
        WriteReport(human, Serialize(model), options.GetOption("--json"));
        return ExitSuccess;
    }

    /// <summary>
    /// Executes the <c>glyph-map-validate</c> CLI command and returns a stable process exit code.
    /// </summary>
    /// <param name="args">The command-line arguments to parse and execute.</param>
    /// <returns>A stable process exit code.</returns>
    private static int GlyphMapValidate(string[] args)
    {
        ParsedArguments options = ParsedArguments.Parse(args, 1, ["--json"]);
        string path = FormatPathUtilities.Normalize(options.Positionals[0]);
        RequireReportDifferent(options.GetOption("--json"), path);
        GlyphMap map = GlyphMap.Load(path);
        GlyphMapAnalysis analysis = map.Analyze();
        var model = new
        {
            schema = "lucky-star-psp.glyph-map-validation.v1",
            path,
            sha256 = map.SourceSha256,
            analysis.EntryCount,
            analysis.EncodableEntryCount,
            analysis.EmptyEntryCount,
            analysis.ReservedIndexEntryCount,
            analysis.Duplicates,
            analysis.Warnings
        };
        var human = new StringBuilder();
        human.AppendLine($"Glyph map: {path}");
        human.AppendLine($"  SHA-256: {map.SourceSha256}");
        human.AppendLine(
            $"  entries: {analysis.EntryCount}; encodable: {analysis.EncodableEntryCount}; " +
            $"empty: {analysis.EmptyEntryCount}; reserved: {analysis.ReservedIndexEntryCount}");
        human.AppendLine($"  duplicates: {analysis.Duplicates.Count}");
        foreach (string warning in analysis.Warnings)
        {
            human.AppendLine($"  WARNING: {warning}");
        }
        WriteReport(human.ToString(), Serialize(model), options.GetOption("--json"));
        return ExitSuccess;
    }

    /// <summary>
    /// Executes the <c>font-inspect</c> CLI command and returns a stable process exit code.
    /// </summary>
    /// <param name="args">The command-line arguments to parse and execute.</param>
    /// <returns>A stable process exit code.</returns>
    private static int FontInspect(string[] args)
    {
        ParsedArguments options = ParsedArguments.Parse(
            args,
            2,
            ["--preview", "--columns", "--scale", "--gutter", "--json"]);
        string fontPath = FormatPathUtilities.Normalize(options.Positionals[0]);
        string mapPath = FormatPathUtilities.Normalize(options.Positionals[1]);
        string? previewPath = options.GetOption("--preview");
        if (previewPath is not null)
        {
            previewPath = FormatPathUtilities.Normalize(previewPath);
            FormatPathUtilities.RequireDifferent(
                previewPath,
                fontPath,
                "FONT_PREVIEW_COLLISION",
                "Font preview path must differ from lt.bin.");
            FormatPathUtilities.RequireDifferent(
                previewPath,
                mapPath,
                "FONT_PREVIEW_COLLISION",
                "Font preview path must differ from glyph map.");
        }
        RequireReportDifferent(options.GetOption("--json"), fontPath, mapPath);
        if (previewPath is not null)
        {
            RequireReportDifferent(options.GetOption("--json"), previewPath);
        }

        GlyphMap map = GlyphMap.Load(mapPath);
        byte[] source = FormatBinaryUtilities.ReadAllBytesBounded(fontPath);
        LtFont font = LtFont.Parse(source, map.Count);
        LtFontAnalysis analysis = font.Analyze(map);
        List<LuckyStarPspToolkit.Formats.Common.AtomicWriteRequest> writes = [];
        if (previewPath is not null)
        {
            int columns = ParseIntOption(options.GetOption("--columns"), 64, "--columns", 1, 512);
            int scale = ParseIntOption(options.GetOption("--scale"), 1, "--scale", 1, 16);
            int gutter = ParseIntOption(options.GetOption("--gutter"), 1, "--gutter", 0, 16);
            byte[] png = PngWriter.Encode(font.RenderAtlas(columns, scale, gutter));
            writes.Add(new(previewPath, png));
        }

        var model = new
        {
            schema = "lucky-star-psp.font-inspect.v1",
            path = fontPath,
            glyphMap = mapPath,
            glyphMapSha256 = map.SourceSha256,
            preview = previewPath,
            analysis
        };
        var human = new StringBuilder();
        human.AppendLine($"Font: {fontPath}");
        human.AppendLine($"  SHA-256: {analysis.SourceSha256}");
        human.AppendLine(
            $"  glyphs: {analysis.GlyphCount}; non-blank: {analysis.NonBlankGlyphCount}; " +
            $"blank: {analysis.BlankGlyphCount}; padding: {analysis.PaddingByteCount} bytes");
        human.AppendLine(
            $"  Russian readiness: {analysis.RenderableRussianCharacterCount}/{analysis.RequiredRussianCharacterCount} " +
            $"({(analysis.RussianReady ? "ready" : "incomplete")})");
        if (previewPath is not null)
        {
            human.AppendLine($"  preview: {previewPath}");
        }
        foreach (string warning in analysis.Warnings)
        {
            human.AppendLine($"  WARNING: {warning}");
        }
        CommitArtifactsAndWriteReport(
            writes,
            human.ToString(),
            Serialize(model),
            options.GetOption("--json"));
        return analysis.RussianReady ? ExitSuccess : ExitIncomplete;
    }

    /// <summary>
    /// Executes the <c>font-import-bdf</c> CLI command and returns a stable process exit code.
    /// </summary>
    /// <param name="args">The command-line arguments to parse and execute.</param>
    /// <returns>A stable process exit code.</returns>
    private static int FontImportBdf(string[] args)
    {
        ParsedArguments options = ParsedArguments.Parse(
            args,
            4,
            [
                "--mode", "--baseline", "--intensity", "--x-shift", "--y-shift",
                "--preview", "--columns", "--scale", "--gutter", "--json"
            ],
            ["--replace-existing", "--allow-clipping", "--allow-missing"]);
        string sourcePath = FormatPathUtilities.Normalize(options.Positionals[0]);
        string mapPath = FormatPathUtilities.Normalize(options.Positionals[1]);
        string bdfPath = FormatPathUtilities.Normalize(options.Positionals[2]);
        string outputPath = FormatPathUtilities.Normalize(options.Positionals[3]);
        foreach (string input in new[] { sourcePath, mapPath, bdfPath })
        {
            FormatPathUtilities.RequireDifferent(
                outputPath,
                input,
                "FONT_OUTPUT_COLLISION",
                "Patched lt.bin output must differ from every input file.");
        }

        string? previewPath = options.GetOption("--preview");
        if (previewPath is not null)
        {
            previewPath = FormatPathUtilities.Normalize(previewPath);
            foreach (string protectedPath in new[] { sourcePath, mapPath, bdfPath, outputPath })
            {
                FormatPathUtilities.RequireDifferent(
                    previewPath,
                    protectedPath,
                    "FONT_PREVIEW_COLLISION",
                    "Font preview path must differ from all input and output files.");
            }
        }
        RequireReportDifferent(options.GetOption("--json"), sourcePath, mapPath, bdfPath, outputPath);
        if (previewPath is not null)
        {
            RequireReportDifferent(options.GetOption("--json"), previewPath);
        }

        GlyphMap map = GlyphMap.Load(mapPath);
        byte[] source = FormatBinaryUtilities.ReadAllBytesBounded(sourcePath);
        LtFont font = LtFont.Parse(source, map.Count);
        BdfFont bdf = BdfFont.Load(bdfPath);
        LtFontPatchOptions patchOptions = new(
            LtFontPatcher.ParseSelection(options.GetOption("--mode") ?? "russian"),
            ParseIntOption(options.GetOption("--baseline"), 15, "--baseline", 0, LtFont.GlyphHeight),
            ParseIntOption(options.GetOption("--intensity"), 3, "--intensity", 1, 3),
            ParseIntOption(options.GetOption("--x-shift"), 0, "--x-shift", -64, 64),
            ParseIntOption(options.GetOption("--y-shift"), 0, "--y-shift", -64, 64),
            options.HasFlag("--replace-existing"),
            options.HasFlag("--allow-clipping"));
        LtFontPatchResult patch = LtFontPatcher.ApplyBdf(font, map, bdf, patchOptions);
        if (!patch.Complete && !options.HasFlag("--allow-missing"))
        {
            throw new FormatToolkitException(
                "FONT_PATCH_INCOMPLETE",
                $"Font patch is incomplete; missing or blocked characters: {string.Join("", patch.MissingCharacters)}");
        }

        byte[] output = patch.Font.Build();
        LtFont verified = LtFont.Parse(output, map.Count);
        LtFontAnalysis analysis = verified.Analyze(map);
        bool complete = patch.Complete
            && (patchOptions.Selection != LtFontPatchSelection.Russian || analysis.RussianReady);
        if (!complete && !options.HasFlag("--allow-missing"))
        {
            throw new FormatToolkitException(
                "FONT_PATCH_VERIFY",
                "Patched lt.bin failed the final Russian glyph readiness verification.");
        }

        List<LuckyStarPspToolkit.Formats.Common.AtomicWriteRequest> writes =
        [
            new(outputPath, output)
        ];
        if (previewPath is not null)
        {
            int columns = ParseIntOption(options.GetOption("--columns"), 64, "--columns", 1, 512);
            int scale = ParseIntOption(options.GetOption("--scale"), 1, "--scale", 1, 16);
            int gutter = ParseIntOption(options.GetOption("--gutter"), 1, "--gutter", 0, 16);
            writes.Add(new(previewPath, PngWriter.Encode(verified.RenderAtlas(columns, scale, gutter))));
        }

        var model = new
        {
            schema = "lucky-star-psp.font-import-bdf.v1",
            source = sourcePath,
            sourceSha256 = FormatBinaryUtilities.Sha256Hex(source),
            glyphMap = mapPath,
            glyphMapSha256 = map.SourceSha256,
            bdf = bdfPath,
            bdfSha256 = bdf.SourceSha256,
            output = outputPath,
            outputSha256 = FormatBinaryUtilities.Sha256Hex(output),
            preview = previewPath,
            complete,
            patch = new
            {
                patch.SelectedEntryCount,
                patch.PatchedGlyphCount,
                patch.ExistingGlyphCount,
                patch.MissingMapCount,
                patch.MissingBdfCount,
                patch.ClippedGlyphCount,
                patch.Complete,
                patch.MissingCharacters,
                patch.Items,
                patch.Warnings
            },
            analysis
        };
        var human = new StringBuilder();
        human.AppendLine($"Patched font written: {outputPath}");
        human.AppendLine($"  SHA-256: {model.outputSha256}");
        human.AppendLine(
            $"  selected: {patch.SelectedEntryCount}; patched: {patch.PatchedGlyphCount}; " +
            $"already present: {patch.ExistingGlyphCount}");
        human.AppendLine(
            $"  missing map/BDF: {patch.MissingMapCount}/{patch.MissingBdfCount}; clipped: {patch.ClippedGlyphCount}");
        human.AppendLine(
            $"  Russian readiness: {analysis.RenderableRussianCharacterCount}/{analysis.RequiredRussianCharacterCount}");
        if (previewPath is not null)
        {
            human.AppendLine($"  preview: {previewPath}");
        }
        foreach (string warning in patch.Warnings.Concat(analysis.Warnings).Distinct(StringComparer.Ordinal))
        {
            human.AppendLine($"  WARNING: {warning}");
        }
        CommitArtifactsAndWriteReport(
            writes,
            human.ToString(),
            Serialize(model),
            options.GetOption("--json"));
        return complete ? ExitSuccess : ExitIncomplete;
    }

    /// <summary>
    /// Executes the <c>workspace-export</c> CLI command and returns a stable process exit code.
    /// </summary>
    /// <param name="args">The command-line arguments to parse and execute.</param>
    /// <returns>A stable process exit code.</returns>
    private static int WorkspaceExport(string[] args)
    {
        ParsedArguments options = ParsedArguments.Parse(args, 3, ["--game", "--ids", "--json"]);
        ScriptProfile profile = ScriptProfile.Parse(options.GetOption("--game") ?? "rgo");
        RequireReportDifferent(options.GetOption("--json"), options.Positionals[0], options.Positionals[1]);
        RequireReportOutside(options.GetOption("--json"), options.Positionals[2]);
        IReadOnlyCollection<ushort>? ids = ParseIds(options.GetOption("--ids"));
        WorkspaceExportResult result = TranslationWorkspaceService.Export(
            options.Positionals[0],
            options.Positionals[1],
            options.Positionals[2],
            profile,
            ids);
        string human =
            $"Workspace created: {result.Directory}{Environment.NewLine}" +
            $"Scripts: {result.ScriptCount}; dialogs: {result.DialogCount}; choices: {result.ChoiceCount}{Environment.NewLine}";
        WriteReport(human, Serialize(result), options.GetOption("--json"));
        return ExitSuccess;
    }

    /// <summary>
    /// Executes the <c>workspace-validate</c> CLI command and returns a stable process exit code.
    /// </summary>
    /// <param name="args">The command-line arguments to parse and execute.</param>
    /// <returns>A stable process exit code.</returns>
    private static int WorkspaceValidate(string[] args)
    {
        ParsedArguments options = ParsedArguments.Parse(args, 2, ["--json"]);
        RequireReportDifferent(options.GetOption("--json"), options.Positionals[1]);
        RequireReportOutside(options.GetOption("--json"), options.Positionals[0]);
        WorkspaceValidationResult result = TranslationWorkspaceService.Validate(
            options.Positionals[0],
            options.Positionals[1]);
        WriteReport(WorkspaceValidationToHuman(result), Serialize(result), options.GetOption("--json"));
        return ExitSuccess;
    }

    /// <summary>
    /// Executes the <c>workspace-build</c> CLI command and returns a stable process exit code.
    /// </summary>
    /// <param name="args">The command-line arguments to parse and execute.</param>
    /// <returns>A stable process exit code.</returns>
    private static int WorkspaceBuild(string[] args)
    {
        ParsedArguments options = ParsedArguments.Parse(args, 3, ["--plan", "--json"]);
        string workspacePath = FormatPathUtilities.Normalize(options.Positionals[0]);
        string sourcePath = FormatPathUtilities.Normalize(options.Positionals[1]);
        string outputPath = FormatPathUtilities.Normalize(options.Positionals[2]);
        string planPath = FormatPathUtilities.Normalize(
            options.GetOption("--plan") ?? Path.ChangeExtension(outputPath, ".eboot-size-plan.json"));

        FormatPathUtilities.RequireDifferent(
            sourcePath,
            outputPath,
            "WORKSPACE_OUTPUT_SOURCE",
            "Refusing to overwrite the source sc.cpk.");
        FormatPathUtilities.RequireDifferent(
            sourcePath,
            planPath,
            "WORKSPACE_PLAN_SOURCE",
            "EBOOT plan output cannot overwrite the source sc.cpk.");
        FormatPathUtilities.RequireDifferent(
            outputPath,
            planPath,
            "WORKSPACE_OUTPUT_PLAN",
            "CPK output and EBOOT plan must use different paths.");
        RequireReportDifferent(options.GetOption("--json"), sourcePath, outputPath, planPath);
        RequireReportOutside(options.GetOption("--json"), workspacePath);
        WorkspaceBuildResult result = TranslationWorkspaceService.Build(
            workspacePath,
            sourcePath,
            outputPath,
            planPath);
        var human = new StringBuilder();
        human.AppendLine($"CPK built: {result.OutputCpk}");
        human.AppendLine($"EBOOT plan: {result.EbootPlan}");
        human.Append(WorkspaceValidationToHuman(result.Validation));
        WriteReport(human.ToString(), Serialize(result), options.GetOption("--json"));
        return ExitSuccess;
    }

    /// <summary>
    /// Applies EBOOT plan while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="args">The command-line arguments to parse and execute.</param>
    /// <returns>A stable process exit code.</returns>
    /// <remarks>Malformed input or violated preconditions are rejected before a persistent output is committed.</remarks>
    private static int ApplyEbootPlan(string[] args)
    {
        ParsedArguments options = ParsedArguments.Parse(
            args,
            3,
            ["--json"],
            ["--unsafe-allow-unknown-hash", "--allow-unknown-hash"]);
        string sourcePath = FormatPathUtilities.Normalize(options.Positionals[0]);
        string planPath = FormatPathUtilities.Normalize(options.Positionals[1]);
        string outputPath = FormatPathUtilities.Normalize(options.Positionals[2]);
        FormatPathUtilities.RequireDifferent(
            sourcePath,
            outputPath,
            "EBOOT_OUTPUT_SOURCE",
            "Refusing to overwrite the decrypted EBOOT source.");
        FormatPathUtilities.RequireDifferent(
            planPath,
            outputPath,
            "EBOOT_OUTPUT_PLAN",
            "Refusing to overwrite the EBOOT plan file.");
        FormatPathUtilities.RequireDifferent(
            sourcePath,
            planPath,
            "EBOOT_PLAN_SOURCE",
            "EBOOT source and plan must be different files.");
        RequireReportDifferent(
            options.GetOption("--json"),
            sourcePath,
            planPath,
            outputPath);
        byte[] source = FormatBinaryUtilities.ReadAllBytesBounded(sourcePath);
        EbootSizePatchPlan plan = EbootSizePatchPlan.Load(planPath);
        bool unsafeOverride = options.HasFlag("--unsafe-allow-unknown-hash")
            || options.HasFlag("--allow-unknown-hash");
        byte[] output = plan.Apply(source, unsafeOverride);
        RgoVwfPatchInspection? rgoInspection = null;
        if (string.Equals(
                plan.Game,
                LuckyStarGame.RyououGakuenOutousaiPortable.ToString(),
                StringComparison.Ordinal))
        {
            rgoInspection = RgoVwfPatchProfile.Inspect(output);
            if (!rgoInspection.CompatibleKnownLineage
                || !rgoInspection.ScriptSizeTableConsistent
                || !rgoInspection.ScriptHeapConsistent
                || rgoInspection.MismatchWordCount != 0)
            {
                throw new FormatToolkitException(
                    "EBOOT_PLAN_VERIFY",
                    $"Applied RGO EBOOT plan failed lineage verification: {rgoInspection.Message}");
            }
        }

        FormatAtomicFile.WriteAllBytes(outputPath, output);
        var report = new
        {
            schema = "lucky-star-psp.apply-eboot-plan.v1",
            source = sourcePath,
            sourceSha256 = FormatBinaryUtilities.Sha256Hex(source),
            plan = planPath,
            output = outputPath,
            outputSha256 = FormatBinaryUtilities.Sha256Hex(output),
            changedEntries = plan.Entries.Count,
            unsafeUnknownHashOverride = unsafeOverride,
            rgoKnownLineage = rgoInspection?.CompatibleKnownLineage,
            scriptSizeTableModified = rgoInspection?.ScriptSizeTableModified,
            scriptHeapModified = rgoInspection?.ScriptHeapModified,
            scriptHeapBytes = rgoInspection?.ScriptHeapBytes,
            requiredScriptHeapBytes = rgoInspection?.RequiredScriptHeapBytes
        };
        var human = new StringBuilder();
        human.AppendLine($"EBOOT plan applied atomically: {outputPath}");
        human.AppendLine($"Changed entries: {plan.Entries.Count}; SHA-256: {report.outputSha256}");
        if (rgoInspection is not null)
        {
            human.AppendLine(
                $"Script heap: {rgoInspection.ScriptHeapBytes} bytes " +
                $"(required {rgoInspection.RequiredScriptHeapBytes}); lineage verified.");
        }
        WriteReport(human.ToString(), Serialize(report), options.GetOption("--json"));
        return ExitSuccess;
    }

    /// <summary>
    /// Executes the <c>customer-audit</c> CLI command and returns a stable process exit code.
    /// </summary>
    /// <param name="args">The command-line arguments to parse and execute.</param>
    /// <returns>A stable process exit code.</returns>
    private static int CustomerAudit(string[] args)
    {
        ParsedArguments options = ParsedArguments.Parse(args, 1, ["--json"]);
        ProtectReportFromInput(options.GetOption("--json"), options.Positionals[0]);
        CustomerAuditReport report = CustomerArchiveAuditor.Audit(options.Positionals[0]);
        var human = new StringBuilder();
        human.AppendLine($"Customer data audit: {report.Source}");
        human.AppendLine($"  files: {report.Files.Count}");
        human.AppendLine($"  known ULJM05752 EBOOT: {(report.HasKnownRgoEboot ? "yes" : "no")}");
        human.AppendLine(
            $"  required missing: {(report.RequiredMissing.Count == 0 ? "none" : string.Join(", ", report.RequiredMissing))}");
        foreach (CustomerAuditFile file in report.Files)
        {
            human.AppendLine($"  {file.Path}: {file.Size} bytes; {file.Kind}; {file.Sha256}");
        }
        foreach (string warning in report.Warnings)
        {
            human.AppendLine($"  WARNING: {warning}");
        }
        WriteReport(human.ToString(), Serialize(report), options.GetOption("--json"));
        return report.RequiredMissing.Count == 0 && report.HasKnownRgoEboot
            ? ExitSuccess
            : ExitIncomplete;
    }

    /// <summary>
    /// Formats s self test while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="args">The command-line arguments to parse and execute.</param>
    /// <returns>A stable process exit code.</returns>
    private static int FormatsSelfTest(string[] args)
    {
        ParsedArguments options = ParsedArguments.Parse(args, 0, ["--json"]);
        FormatSelfTestReport report = FormatSelfDiagnostics.Run();
        WriteReport(FormatSelfDiagnostics.ToHumanText(report), Serialize(report), options.GetOption("--json"));
        return report.Passed ? ExitSuccess : ExitSoftware;
    }

    /// <summary>
    /// Executes the <c>self-test</c> CLI command and returns a stable process exit code.
    /// </summary>
    /// <param name="args">The command-line arguments to parse and execute.</param>
    /// <returns>A stable process exit code.</returns>
    private static int SelfTest(string[] args)
    {
        ParsedArguments options = ParsedArguments.Parse(args, 0, ["--json"]);
        RuntimeSelfTestReport core = SelfDiagnostics.Run();
        FormatSelfTestReport formats = FormatSelfDiagnostics.Run();
        var report = new
        {
            schema = "lucky-star-psp.self-test.v2",
            version = ToolkitBuildInfo.Version,
            passed = core.Passed && formats.Passed,
            core,
            formats
        };
        string human =
            SelfDiagnostics.ToHumanText(core) + Environment.NewLine +
            FormatSelfDiagnostics.ToHumanText(formats) + Environment.NewLine +
            $"Overall result: {(report.passed ? "PASS" : "FAIL")}{Environment.NewLine}";
        WriteReport(human, Serialize(report), options.GetOption("--json"));
        return report.passed ? ExitSuccess : ExitSoftware;
    }

    /// <summary>
    /// Reads single file while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="path">The file-system path to process.</param>
    /// <returns>The resulting binary or typed sequence.</returns>
    private static byte[] ReadSingleFile(string path)
    {
        InspectionResult result = InputInspector.Inspect(path);
        if (result.ContainerKind != InputContainerKind.File || result.Files.Count != 1)
        {
            throw new CoreToolkitException("This command requires a regular file, not a directory or ZIP archive.");
        }
        return result.Files[0].Data;
    }

    /// <summary>
    /// Formats the executable revision, cryptographic checks and patch preconditions for a terminal report.
    /// </summary>
    /// <param name="check">The check value.</param>
    /// <returns>The resulting text, path, identifier, or hexadecimal digest.</returns>
    private static string ExecutableCheckToHuman(RgoExecutableCheck check)
    {
        var output = new StringBuilder();
        output.AppendLine("RGO executable verification");
        output.AppendLine($"  result:                 {check.Message}");
        output.AppendLine($"  compatibility:          {check.Compatibility}");
        output.AppendLine($"  encrypted source:       {YesNo(check.SourceWasEncrypted)}");
        output.AppendLine($"  decryption succeeded:   {YesNo(check.DecryptionSucceeded)}");
        output.AppendLine($"  PSP MIPS ELF:           {YesNo(check.IsPspMips)}");
        output.AppendLine($"  exact encrypted hash:   {YesNo(check.ExactEncryptedRevision)}");
        output.AppendLine($"  exact decrypted hash:   {YesNo(check.ExactDecryptedRevision)}");
        output.AppendLine($"  exact full VWF hash:    {YesNo(check.ExactFullVwfPatch)}");
        output.AppendLine(
            $"  patch points:           {check.PatchPoints.Count(static point => point.Matches)}/{check.PatchPoints.Count}");
        output.AppendLine($"  VWF lineage:            {YesNo(check.VwfPatchLineageCompatible)}");
        if (check.VwfPatchLineageCompatible)
        {
            output.AppendLine(
                $"  VWF original/patched/bad:{check.VwfOriginalWordCount}/" +
                $"{check.VwfPatchedWordCount}/{check.VwfMismatchWordCount}");
        }
        output.AppendLine($"  script table modified:  {YesNo(check.ScriptSizeTableModified)}");
        output.AppendLine($"  script size table:      {(check.ScriptSizeTableConsistent ? "consistent" : "invalid")}");
        output.AppendLine($"  script heap modified:   {YesNo(check.ScriptHeapModified)}");
        output.AppendLine(
            $"  script heap:            {(check.ScriptHeapConsistent ? "consistent" : "invalid")}; " +
            $"{check.ScriptHeapBytes} bytes (required {check.RequiredScriptHeapBytes})");
        output.AppendLine($"  encrypted SHA-256:      {check.EncryptedSha256 ?? "n/a"}");
        output.AppendLine($"  decrypted SHA-256:      {check.DecryptedSha256 ?? "n/a"}");
        if (!string.IsNullOrWhiteSpace(check.Failure))
        {
            output.AppendLine($"  failure:                {check.Failure}");
        }
        if (check.PspHeader is not null)
        {
            output.AppendLine($"  module:                 {check.PspHeader.ModuleName}");
            output.AppendLine($"  PRX tag:                {HexUtilities.UInt32(check.PspHeader.Tag)}");
        }
        if (check.ElfHeader is not null)
        {
            output.AppendLine($"  ELF entry:              {HexUtilities.UInt32(check.ElfHeader.EntryPoint)}");
        }
        if (check.ReferencedAssets.Count > 0)
        {
            output.AppendLine($"  referenced assets:      {string.Join(", ", check.ReferencedAssets)}");
        }
        return output.ToString();
    }

    /// <summary>
    /// Formats scenario counts, changed byte sizes and build warnings without writing game resources.
    /// </summary>
    /// <param name="result">The result value.</param>
    /// <returns>The resulting text, path, identifier, or hexadecimal digest.</returns>
    private static string WorkspaceValidationToHuman(WorkspaceValidationResult result)
    {
        var output = new StringBuilder();
        output.AppendLine("Workspace validation: PASS");
        output.AppendLine($"  game: {result.Game}");
        output.AppendLine(
            $"  scripts: {result.ScriptCount}; changed: {result.ChangedScriptCount}; " +
            $"EBOOT table changes: {result.EbootPlanEntryCount}");
        output.AppendLine(
            $"  dialogs: {result.DialogCount}; translated speakers: {result.TranslatedSpeakers}; " +
            $"translated messages: {result.TranslatedMessages}");
        output.AppendLine($"  choices: {result.ChoiceCount}; translated: {result.TranslatedChoices}");
        output.AppendLine(
            $"  CPK: {result.SourceCpkBytes} -> {result.RebuiltCpkBytes} bytes; " +
            $"SHA-256 {result.RebuiltCpkSha256}");
        foreach (WorkspaceScriptValidationResult script in result.Scripts)
        {
            output.AppendLine(
                $"  script {script.Id}: {script.SourceBytes} -> {script.RebuiltBytes} bytes; " +
                $"changed={(script.BytesChanged ? "yes" : "no")}; " +
                $"translations={script.TranslatedSpeakers + script.TranslatedMessages + script.TranslatedChoices}");
        }
        foreach (string warning in result.Warnings)
        {
            output.AppendLine($"  WARNING: {warning}");
        }
        return output.ToString();
    }

    /// <summary>
    /// Parses int option while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="value">The value to process.</param>
    /// <param name="defaultValue">The value used when the option is omitted.</param>
    /// <param name="name">The logical name used for lookup or diagnostics.</param>
    /// <param name="minimum">The inclusive minimum accepted value.</param>
    /// <param name="maximum">The inclusive maximum accepted value.</param>
    /// <returns>A stable process exit code.</returns>
    /// <remarks>Malformed input or violated preconditions are rejected before a persistent output is committed.</remarks>
    private static int ParseIntOption(string? value, int defaultValue, string name, int minimum, int maximum)
    {
        if (value is null)
        {
            return defaultValue;
        }
        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) || parsed < minimum || parsed > maximum)
        {
            throw new UsageException($"Invalid {name} '{value}'; expected an integer from {minimum} to {maximum}.");
        }
        return parsed;
    }

    /// <summary>
    /// Parses nullable long option while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="value">The value to process.</param>
    /// <param name="name">The logical name used for lookup or diagnostics.</param>
    /// <param name="minimum">The inclusive minimum accepted value.</param>
    /// <param name="maximum">The inclusive maximum accepted value.</param>
    /// <returns>The validated operation result.</returns>
    /// <remarks>Malformed input or violated preconditions are rejected before a persistent output is committed.</remarks>
    private static long? ParseNullableLongOption(
        string? value,
        string name,
        long minimum,
        long maximum)
    {
        if (value is null)
        {
            return null;
        }
        if (!long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsed)
            || parsed < minimum
            || parsed > maximum)
        {
            throw new UsageException(
                $"Invalid {name} '{value}'; expected an integer from {minimum} to {maximum}.");
        }
        return parsed;
    }

    /// <summary>
    /// Parses u short while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="value">The value to process.</param>
    /// <param name="name">The logical name used for lookup or diagnostics.</param>
    /// <returns>The validated operation result.</returns>
    /// <remarks>Malformed input or violated preconditions are rejected before a persistent output is committed.</remarks>
    private static ushort ParseUShort(string value, string name)
    {
        NumberStyles style = NumberStyles.Integer;
        string normalized = value;
        if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            normalized = value[2..];
            style = NumberStyles.AllowHexSpecifier;
        }
        if (!ushort.TryParse(normalized, style, CultureInfo.InvariantCulture, out ushort result))
        {
            throw new UsageException($"Invalid {name} '{value}'; expected UInt16 decimal or 0x-prefixed hexadecimal.");
        }
        return result;
    }

    /// <summary>
    /// Parses ids while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="value">The value to process.</param>
    /// <returns>The validated operation result.</returns>
    /// <remarks>Malformed input or violated preconditions are rejected before a persistent output is committed.</remarks>
    private static IReadOnlyCollection<ushort>? ParseIds(string? value)
    {
        if (value is null)
        {
            return null;
        }
        string[] parts = value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            throw new UsageException("--ids requires at least one ID.");
        }
        return parts.Select(part => ParseUShort(part, "script ID")).Distinct().Order().ToArray();
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
    /// Parses VWF groups while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="value">The value to process.</param>
    /// <returns>The validated operation result.</returns>
    /// <remarks>Malformed input or violated preconditions are rejected before a persistent output is committed.</remarks>
    private static IReadOnlyList<string>? ParseVwfGroups(string? value)
    {
        if (value is null)
        {
            return null;
        }
        return RgoVwfPatchProfile.ResolveGroups([value]);
    }

    /// <summary>
    /// Protects report from input while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="reportPath">The optional JSON report path.</param>
    /// <param name="inputPath">The source file path.</param>
    private static void ProtectReportFromInput(string? reportPath, string inputPath)
    {
        if (string.IsNullOrWhiteSpace(reportPath) || reportPath == "-")
        {
            return;
        }
        string normalizedInput = FormatPathUtilities.Normalize(inputPath);
        if (Directory.Exists(normalizedInput))
        {
            RequireReportOutside(reportPath, normalizedInput);
        }
        else
        {
            RequireReportDifferent(reportPath, normalizedInput);
        }
    }

    /// <summary>
    /// Requires report outside while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="reportPath">The optional JSON report path.</param>
    /// <param name="protectedDirectories">Directories inside which the report must not be written.</param>
    private static void RequireReportOutside(string? reportPath, params string[] protectedDirectories)
    {
        if (string.IsNullOrWhiteSpace(reportPath) || reportPath == "-")
        {
            return;
        }
        foreach (string directory in protectedDirectories)
        {
            if (FormatPathUtilities.IsWithinOrSame(reportPath, directory))
            {
                throw new FormatToolkitException(
                    "REPORT_PROTECTED_DIRECTORY",
                    $"JSON report path must be outside protected directory: {FormatPathUtilities.Normalize(directory)}");
            }
        }
    }

    /// <summary>
    /// Requires report different while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="reportPath">The optional JSON report path.</param>
    /// <param name="protectedPaths">Paths that the report must not overwrite.</param>
    private static void RequireReportDifferent(string? reportPath, params string[] protectedPaths)
    {
        if (string.IsNullOrWhiteSpace(reportPath) || reportPath == "-")
        {
            return;
        }
        foreach (string protectedPath in protectedPaths)
        {
            FormatPathUtilities.RequireDifferent(
                reportPath,
                protectedPath,
                "REPORT_OUTPUT_COLLISION",
                "JSON report path must be different from binary output paths.");
        }
    }

    /// <summary>
    /// Commits artifacts and write report while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="artifacts">The artifacts value.</param>
    /// <param name="human">The human value.</param>
    /// <param name="json">The json value.</param>
    /// <param name="jsonPath">The json path value.</param>
    private static void CommitArtifactsAndWriteReport(
        ICollection<LuckyStarPspToolkit.Formats.Common.AtomicWriteRequest> artifacts,
        string human,
        string json,
        string? jsonPath)
    {
        ArgumentNullException.ThrowIfNull(artifacts);
        List<LuckyStarPspToolkit.Formats.Common.AtomicWriteRequest> writes = [.. artifacts];
        string? normalizedReport = null;
        if (!string.IsNullOrWhiteSpace(jsonPath) && jsonPath != "-")
        {
            normalizedReport = FormatPathUtilities.Normalize(jsonPath);
            writes.Add(new(normalizedReport, FormatBinaryUtilities.Utf8(json + "\n")));
        }
        FormatAtomicFile.WriteAll(writes.ToArray());

        if (jsonPath == "-")
        {
            Console.WriteLine(json);
            return;
        }
        Console.Write(human);
        if (normalizedReport is not null)
        {
            Console.WriteLine($"JSON report: {normalizedReport}");
        }
    }

    /// <summary>
    /// Writes report while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="human">The human value.</param>
    /// <param name="json">The json value.</param>
    /// <param name="jsonPath">The json path value.</param>
    private static void WriteReport(string human, string json, string? jsonPath)
    {
        if (jsonPath == "-")
        {
            Console.WriteLine(json);
            return;
        }
        Console.Write(human);
        if (!string.IsNullOrWhiteSpace(jsonPath))
        {
            FormatAtomicFile.WriteAllText(jsonPath, json + Environment.NewLine);
            Console.WriteLine($"JSON report: {Path.GetFullPath(jsonPath)}");
        }
    }

    /// <summary>
    /// Serializes a value to stable UTF-8 JSON for reports or workspaces.
    /// </summary>
    /// <typeparam name="T">The t type used by the operation.</typeparam>
    /// <param name="value">The value to process.</param>
    /// <returns>The resulting text, path, identifier, or hexadecimal digest.</returns>
    private static string Serialize<T>(T value)
        => JsonSerializer.Serialize(value, EbootSizePatchPlan.JsonOptions);

    /// <summary>
    /// Formats a Boolean value as stable human-readable yes/no text.
    /// </summary>
    /// <param name="value">The value to process.</param>
    /// <returns>The resulting text, path, identifier, or hexadecimal digest.</returns>
    private static string YesNo(bool value) => value ? "yes" : "no";

    /// <summary>
    /// Writes help while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="writer">The callback that writes the staged output stream.</param>
    private static void PrintHelp(TextWriter writer)
    {
        writer.WriteLine($"Lucky Star PSP Translation Toolkit {ToolkitBuildInfo.Version}");
        writer.WriteLine();
        writer.WriteLine("Core and customer-data commands:");
        writer.WriteLine("  lsptool inspect <file|directory|archive.zip> [--json <path|->]");
        writer.WriteLine("  lsptool audit-rgo <file|directory|archive.zip> [--json <path|->]");
        writer.WriteLine("  lsptool verify-eboot <EBOOT.BIN|EBOOT.DEC.BIN> [--json <path|->]");
        writer.WriteLine("  lsptool decrypt-eboot <EBOOT.BIN> <EBOOT.DEC.BIN> [--json <path|->]");
        writer.WriteLine("  lsptool eboot-vwf-groups [--json <path|->]");
        writer.WriteLine("  lsptool eboot-vwf-inspect <EBOOT.BIN|EBOOT.DEC.BIN> [--groups LIST] [--json <path|->]");
        writer.WriteLine("  lsptool eboot-vwf-apply <EBOOT.BIN|EBOOT.DEC.BIN> <output-elf> --experimental-vwf [--groups LIST] [--size-plan <path>] [--json <path|->]");
        writer.WriteLine("  lsptool sfo <PARAM.SFO> [--json <path|->]");
        writer.WriteLine("  lsptool iso-list <image.iso> [--json <path|->]");
        writer.WriteLine("  lsptool iso-extract <image.iso> <path-inside-iso> <output> [--json <path|->]");
        writer.WriteLine("  lsptool iso-replace <image.iso> <path-inside-iso> <replacement> <output.iso> [--expect-source-sha256 HEX] [--expect-original-sha256 HEX] [--expect-original-size N] [--expect-replacement-sha256 HEX] [--expect-replacement-size N] [--json <path|->]");
        writer.WriteLine("  lsptool iso-apply-manifest <image.iso> <manifest.json> <output.iso> [--json <path|->]");
        writer.WriteLine("  lsptool collect-assets <image.iso> <output.zip> [--include-optional] [--no-listing] [--skip-source-hash] [--json <path|->]");
        writer.WriteLine("  lsptool customer-audit <archive.zip|directory|file> [--json <path|->]");
        writer.WriteLine();
        writer.WriteLine("CPK, script, and translation commands:");
        writer.WriteLine("  lsptool cpk-list <archive.cpk> [--json <path|->]");
        writer.WriteLine("  lsptool cpk-verify <archive.cpk> [--rebuilt <path>] [--json <path|->]");
        writer.WriteLine("  lsptool cpk-extract <archive.cpk> <id> <output.bin> [--packed] [--json <path|->]");
        writer.WriteLine("  lsptool cpk-replace <archive.cpk> <id> <replacement.bin> <output.cpk> [--json <path|->]");
        writer.WriteLine("  lsptool script-inspect <script.bin> [--game rgo|nim] [--json <path|->]");
        writer.WriteLine("  lsptool glyph-map-validate <glyph-map.txt> [--json <path|->]");
        writer.WriteLine("  lsptool font-inspect <lt.bin> <glyph-map.txt> [--preview <atlas.png>] [--columns N] [--scale N] [--gutter N] [--json <path|->]");
        writer.WriteLine("  lsptool font-import-bdf <lt.bin> <glyph-map.txt> <font.bdf> <output-lt.bin> [--mode russian|cyrillic|all] [--baseline N] [--intensity 1..3] [--x-shift N] [--y-shift N] [--replace-existing] [--allow-clipping] [--allow-missing] [--preview <atlas.png>] [--json <path|->]");
        writer.WriteLine("  lsptool workspace-export <sc.cpk> <glyph-map.txt> <dir> [--game rgo|nim] [--ids 0,1] [--json <path|->]");
        writer.WriteLine("  lsptool workspace-validate <workspace-dir> <source-sc.cpk> [--json <path|->]");
        writer.WriteLine("  lsptool workspace-build <workspace-dir> <source-sc.cpk> <output-sc.cpk> [--plan <path>] [--json <path|->]");
        writer.WriteLine("  lsptool apply-eboot-plan <EBOOT.DEC.BIN> <plan.json> <output.bin> [--json <path|->]");
        writer.WriteLine();
        writer.WriteLine("Diagnostics:");
        writer.WriteLine("  lsptool self-test [--json <path|->]");
        writer.WriteLine("  lsptool formats-self-test [--json <path|->]");
        writer.WriteLine("  lsptool version");
        writer.WriteLine("  lsptool license activate [--key-file <path>]");
        writer.WriteLine("  lsptool license status|device|forget|build-info|help");
        writer.WriteLine("  Operational commands require an online license. Contact the owner for a configured customer build.");
        writer.WriteLine();
        writer.WriteLine("The unsafe --allow-unknown-hash alias is retained for compatibility but should not be used for unverified EBOOT revisions.");
        writer.WriteLine("Exit codes: 0 success, 2 incomplete customer input, 3 invalid/unsafe input, 64 usage, 70 software, 74 I/O.");
    }

    /// <summary>
    /// Represents a validated usage exception failure with a stable diagnostic code.
    /// </summary>
    /// <param name="message">The message value used by this model or operation.</param>
    private sealed class UsageException(string message) : Exception(message);

    /// <summary>
    /// Represents the toolkit's parsed arguments model or service.
    /// </summary>
    private sealed class ParsedArguments
    {
        /// <summary>Stores the options state owned by this instance or type.</summary>
        private readonly Dictionary<string, string> _options;
        /// <summary>Stores the flags state owned by this instance or type.</summary>
        private readonly HashSet<string> _flags;

        /// <summary>
        /// Initializes a new instance with validated constructor state.
        /// </summary>
        /// <param name="positionals">The positionals value.</param>
        /// <param name="options">Optional operation settings.</param>
        /// <param name="flags">The flags value.</param>
        /// <remarks>Malformed input or violated preconditions are rejected before a persistent output is committed.</remarks>
        private ParsedArguments(
            IReadOnlyList<string> positionals,
            Dictionary<string, string> options,
            HashSet<string> flags)
        {
            Positionals = positionals;
            _options = options;
            _flags = flags;
        }

        /// <summary>The positionals value used by this model or operation.</summary>
        public IReadOnlyList<string> Positionals { get; }

        /// <summary>
        /// Gets option while enforcing the relevant format and safety invariants.
        /// </summary>
        /// <param name="name">The logical name used for lookup or diagnostics.</param>
        /// <returns>The resulting text, path, identifier, or hexadecimal digest.</returns>
        public string? GetOption(string name)
            => _options.TryGetValue(name, out string? value) ? value : null;

        /// <summary>
        /// Determines whether flag.
        /// </summary>
        /// <param name="name">The logical name used for lookup or diagnostics.</param>
        /// <returns><see langword="true"/> when the condition is satisfied; otherwise <see langword="false"/>.</returns>
        public bool HasFlag(string name) => _flags.Contains(name);

        /// <summary>
        /// Parses validated input into the current binary-format model.
        /// </summary>
        /// <param name="args">The command-line arguments to parse and execute.</param>
        /// <param name="expectedPositionals">The expected positionals value.</param>
        /// <param name="valueOptions">The value options value.</param>
        /// <param name="flagOptions">The flag options value.</param>
        /// <returns>The validated operation result.</returns>
        /// <remarks>Malformed input or violated preconditions are rejected before a persistent output is committed.</remarks>
        public static ParsedArguments Parse(
            string[] args,
            int expectedPositionals,
            IReadOnlyCollection<string>? valueOptions = null,
            IReadOnlyCollection<string>? flagOptions = null)
        {
            HashSet<string> allowedValues = valueOptions?.ToHashSet(StringComparer.OrdinalIgnoreCase)
                ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> allowedFlags = flagOptions?.ToHashSet(StringComparer.OrdinalIgnoreCase)
                ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, string> options = new(StringComparer.OrdinalIgnoreCase);
            HashSet<string> flags = new(StringComparer.OrdinalIgnoreCase);
            List<string> positionals = [];
            bool positionalOnly = false;

            for (int index = 0; index < args.Length; index++)
            {
                string argument = args[index];
                if (!positionalOnly && argument == "--")
                {
                    positionalOnly = true;
                    continue;
                }
                if (!positionalOnly && argument.StartsWith("-", StringComparison.Ordinal))
                {
                    if (allowedValues.Contains(argument))
                    {
                        if (options.ContainsKey(argument))
                        {
                            throw new UsageException($"Option '{argument}' may only be specified once.");
                        }
                        if (++index >= args.Length)
                        {
                            throw new UsageException($"Option '{argument}' requires a value.");
                        }
                        options.Add(argument, args[index]);
                        continue;
                    }
                    if (allowedFlags.Contains(argument))
                    {
                        if (!flags.Add(argument))
                        {
                            throw new UsageException($"Flag '{argument}' may only be specified once.");
                        }
                        continue;
                    }
                    throw new UsageException($"Unknown option '{argument}'.");
                }
                positionals.Add(argument);
            }

            if (positionals.Count != expectedPositionals)
            {
                throw new UsageException(
                    $"Expected {expectedPositionals} positional argument(s), got {positionals.Count}.");
            }
            return new ParsedArguments(positionals.AsReadOnly(), options, flags);
        }
    }
}
