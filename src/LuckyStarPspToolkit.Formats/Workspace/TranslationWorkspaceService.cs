using System.Text.Json;
using LuckyStarPspToolkit.Formats.Common;
using LuckyStarPspToolkit.Formats.Cri;
using LuckyStarPspToolkit.Formats.Scripts;
using LuckyStarPspToolkit.Formats.Text;

namespace LuckyStarPspToolkit.Formats.Workspace;

/// <summary>
/// Provides the toolkit's translation workspace service workflow.
/// </summary>
public static class TranslationWorkspaceService
{
    /// <summary>The fixed manifest file name value used by this format or revision.</summary>
    private const string ManifestFileName = "workspace.json";

    /// <summary>
    /// Creates an isolated translation workspace from authenticated CPK bytes and a single glyph-map snapshot.
    /// </summary>
    /// <param name="sourceCpkPath">The source CPK path value.</param>
    /// <param name="glyphMapPath">The glyph map path value.</param>
    /// <param name="outputDirectory">The output directory value.</param>
    /// <param name="profile">The revision-specific format or patch profile.</param>
    /// <param name="scriptIds">The script ids value.</param>
    /// <param name="limits">Optional conservative safety limits; defaults are used when omitted.</param>
    /// <returns>The validated operation result.</returns>
    public static WorkspaceExportResult Export(
        string sourceCpkPath,
        string glyphMapPath,
        string outputDirectory,
        ScriptProfile profile,
        IReadOnlyCollection<ushort>? scriptIds = null,
        FileLimits? limits = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceCpkPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(glyphMapPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        ArgumentNullException.ThrowIfNull(profile);
        limits ??= FileLimits.Default;

        byte[] sourceCpk = BinaryUtilities.ReadAllBytesBounded(sourceCpkPath, limits);
        CriCpkArchive archive = CriCpkArchive.Parse(sourceCpk, limits);
        byte[] mapBytes = BinaryUtilities.ReadAllBytesBounded(
            glyphMapPath, limits with { MaximumInputBytes = limits.MaximumTextBytes });
        GlyphMap map = GlyphMap.FromUtf8(mapBytes, limits);
        List<ushort> ids = ResolveScriptIds(archive, profile, scriptIds);
        if (ids.Count == 0)
        {
            throw new ToolkitException("WORKSPACE_NO_SCRIPTS", "No script IDs were selected for export.");
        }

        string fullOutput = PathUtilities.NormalizeProtectedPath(outputDirectory, "WORKSPACE_OUTPUT_REPARSE");
        if (File.Exists(fullOutput))
        {
            throw new ToolkitException("WORKSPACE_OUTPUT_FILE", $"Workspace output path is an existing file: {fullOutput}");
        }
        if (Directory.Exists(fullOutput) && Directory.EnumerateFileSystemEntries(fullOutput).Any())
        {
            throw new ToolkitException("WORKSPACE_NOT_EMPTY", $"Output directory is not empty: {fullOutput}");
        }
        string staging = fullOutput + ".tmp." + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(staging);
        try
        {
            string mapDestination = Path.Combine(staging, "glyph-map.txt");
            AtomicFile.WriteAllBytes(mapDestination, mapBytes);
            TranslationWorkspaceManifest manifest = new()
            {
                Game = profile.Game.ToString(),
                SourceCpkSha256 = BinaryUtilities.Sha256Hex(sourceCpk),
                GlyphMapSha256 = map.SourceSha256
            };
            int dialogs = 0;
            int choices = 0;
            foreach (ushort id in ids)
            {
                CriCpkEntry entry = archive.GetEntry(id);
                if (entry.IsCrilayla)
                {
                    throw new ToolkitException("WORKSPACE_COMPRESSED_SCRIPT", $"Script entry {id} is CRILAYLA-compressed. Safe replacement is not enabled for compressed scripts.");
                }
                byte[] scriptBytes = entry.PackedData;
                LuckyStarScript script = LuckyStarScript.Parse(scriptBytes, profile, limits, true);
                TranslationScriptFile file = ToWorkspaceFile(id, script, map, scriptBytes);
                string fileName = $"script-{id:D4}.json";
                WriteJson(Path.Combine(staging, fileName), file);
                manifest.Scripts.Add(new TranslationWorkspaceScriptReference
                {
                    Id = id,
                    File = fileName,
                    SourceSha256 = BinaryUtilities.Sha256Hex(scriptBytes),
                    SourceLength = scriptBytes.Length
                });
                dialogs += file.Dialogs.Count;
                choices += file.ChoiceGroups.Sum(static group => group.Choices.Count);
            }
            WriteJson(Path.Combine(staging, ManifestFileName), manifest);
            if (Directory.Exists(fullOutput))
            {
                // Do not recursively delete files another process may have added after the initial check.
                Directory.Delete(fullOutput, false);
            }
            Directory.Move(staging, fullOutput);
            return new WorkspaceExportResult(fullOutput, ids.Count, dialogs, choices);
        }
        catch
        {
            if (Directory.Exists(staging))
            {
                Directory.Delete(staging, true);
            }
            throw;
        }
    }

    /// <summary>
    /// Validates the supplied state and rejects violated format invariants.
    /// </summary>
    /// <param name="workspaceDirectory">The workspace directory value.</param>
    /// <param name="sourceCpkPath">The source CPK path value.</param>
    /// <param name="limits">Optional conservative safety limits; defaults are used when omitted.</param>
    /// <returns>The validated operation result.</returns>
    /// <remarks>Malformed input or violated preconditions are rejected before a persistent output is committed.</remarks>
    public static WorkspaceValidationResult Validate(
        string workspaceDirectory,
        string sourceCpkPath,
        FileLimits? limits = null)
        => PrepareBuild(workspaceDirectory, sourceCpkPath, limits).Validation;

    /// <summary>
    /// Serializes the current validated model into its binary representation.
    /// </summary>
    /// <param name="workspaceDirectory">The workspace directory value.</param>
    /// <param name="sourceCpkPath">The source CPK path value.</param>
    /// <param name="outputCpkPath">The output CPK path value.</param>
    /// <param name="outputPlanPath">The output plan path value.</param>
    /// <param name="limits">Optional conservative safety limits; defaults are used when omitted.</param>
    /// <returns>The validated operation result.</returns>
    /// <remarks>Malformed input or violated preconditions are rejected before a persistent output is committed.</remarks>
    public static WorkspaceBuildResult Build(
        string workspaceDirectory,
        string sourceCpkPath,
        string outputCpkPath,
        string? outputPlanPath = null,
        FileLimits? limits = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputCpkPath);
        string sourcePath = PathUtilities.Normalize(sourceCpkPath);
        string outputPath = PathUtilities.Normalize(outputCpkPath);
        string planPath = PathUtilities.Normalize(
            outputPlanPath ?? Path.ChangeExtension(outputPath, ".eboot-size-plan.json"));
        PathUtilities.RequireDifferent(
            sourcePath,
            outputPath,
            "WORKSPACE_OUTPUT_SOURCE",
            "Refusing to overwrite the source sc.cpk.");
        PathUtilities.RequireDifferent(
            sourcePath,
            planPath,
            "WORKSPACE_PLAN_SOURCE",
            "EBOOT plan output cannot overwrite the source sc.cpk.");
        PathUtilities.RequireDifferent(
            outputPath,
            planPath,
            "WORKSPACE_OUTPUT_COLLISION",
            "CPK output and EBOOT plan output must be different files.");

        if (PathUtilities.IsWithinOrSame(outputPath, workspaceDirectory)
            || PathUtilities.IsWithinOrSame(planPath, workspaceDirectory))
        {
            throw new ToolkitException("WORKSPACE_OUTPUT_INSIDE", "Build outputs must be outside the protected translation workspace.");
        }
        PreparedWorkspaceBuild prepared = PrepareBuild(workspaceDirectory, sourcePath, limits);
        byte[] planJson = BinaryUtilities.Utf8(JsonSerializer.Serialize(prepared.Plan, EbootSizePatchPlan.JsonOptions));
        AtomicFile.WriteAll(
            new AtomicWriteRequest(outputPath, prepared.CpkData),
            new AtomicWriteRequest(planPath, planJson));

        return new WorkspaceBuildResult(
            outputPath,
            planPath,
            prepared.Validation.ScriptCount,
            prepared.Validation.ChangedScriptCount,
            prepared.Validation.Warnings,
            prepared.Validation);
    }

    /// <summary>
    /// Prepares build while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="workspaceDirectory">The workspace directory value.</param>
    /// <param name="sourceCpkPath">The source CPK path value.</param>
    /// <param name="limits">Optional conservative safety limits; defaults are used when omitted.</param>
    /// <returns>The validated operation result.</returns>
    private static PreparedWorkspaceBuild PrepareBuild(
        string workspaceDirectory,
        string sourceCpkPath,
        FileLimits? limits)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceCpkPath);
        limits ??= FileLimits.Default;
        string workspace = PathUtilities.NormalizeProtectedPath(workspaceDirectory, "WORKSPACE_REPARSE");
        if (!Directory.Exists(workspace))
        {
            throw new ToolkitException("WORKSPACE_NOT_FOUND", $"Workspace directory was not found: {workspace}");
        }

        string manifestPath = PathUtilities.ResolveContainedPath(workspace, ManifestFileName, "WORKSPACE_PATH");
        TranslationWorkspaceManifest manifest = ReadJson<TranslationWorkspaceManifest>(manifestPath, limits);
        ValidateManifest(manifest);
        if (!Enum.TryParse(manifest.Game, false, out LuckyStarGame game)
            || !Enum.IsDefined(typeof(LuckyStarGame), game))
        {
            throw new ToolkitException("WORKSPACE_GAME", $"Unknown workspace game '{manifest.Game}'.");
        }
        ScriptProfile profile = game == LuckyStarGame.RyououGakuenOutousaiPortable
            ? ScriptProfile.Rgo
            : ScriptProfile.Nim;

        byte[] sourceCpk = BinaryUtilities.ReadAllBytesBounded(sourceCpkPath, limits);
        string sourceHash = BinaryUtilities.Sha256Hex(sourceCpk);
        if (!string.Equals(sourceHash, manifest.SourceCpkSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new ToolkitException(
                "WORKSPACE_SOURCE_HASH",
                $"Source sc.cpk hash mismatch. Expected {manifest.SourceCpkSha256}, got {sourceHash}.");
        }

        string glyphMapPath = PathUtilities.ResolveContainedPath(workspace, manifest.GlyphMapFile, "WORKSPACE_PATH");
        byte[] mapBytes = BinaryUtilities.ReadAllBytesBounded(
            glyphMapPath, limits with { MaximumInputBytes = limits.MaximumTextBytes });
        string mapHash = BinaryUtilities.Sha256Hex(mapBytes);
        if (!string.Equals(mapHash, manifest.GlyphMapSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new ToolkitException(
                "WORKSPACE_MAP_HASH",
                $"Glyph map hash mismatch. Expected {manifest.GlyphMapSha256}, got {mapHash}.");
        }

        GlyphMap map = GlyphMap.FromUtf8(mapBytes, limits);
        CriCpkArchive archive = CriCpkArchive.Parse(sourceCpk, limits);
        List<EbootSizePatchEntry> planEntries = [];
        List<WorkspaceScriptValidationResult> scriptResults = [];
        List<string> warnings = [];
        if (!string.Equals(manifest.ToolVersion, ToolkitBuildInfo.Version, StringComparison.Ordinal))
        {
            warnings.Add(
                $"Workspace was created by toolkit {manifest.ToolVersion}; validation is running with {ToolkitBuildInfo.Version}.");
        }

        HashSet<ushort> seenIds = [];
        HashSet<string> referencedPaths = new(PathUtilities.FileSystemComparer);
        int changed = 0;
        int dialogCount = 0;
        int translatedSpeakers = 0;
        int translatedMessages = 0;
        int choiceCount = 0;
        int translatedChoices = 0;

        foreach (TranslationWorkspaceScriptReference reference in manifest.Scripts.OrderBy(static item => item.Id))
        {
            if (!seenIds.Add(reference.Id))
            {
                throw new ToolkitException("WORKSPACE_DUPLICATE_SCRIPT", $"Duplicate workspace script ID {reference.Id}.");
            }
            string scriptPath = PathUtilities.ResolveContainedPath(workspace, reference.File, "WORKSPACE_PATH");
            if (!referencedPaths.Add(scriptPath))
            {
                throw new ToolkitException("WORKSPACE_DUPLICATE_FILE", $"Workspace file is referenced more than once: {reference.File}");
            }

            TranslationScriptFile translation = ReadJson<TranslationScriptFile>(scriptPath, limits);
            ValidateWorkspaceScript(reference, translation);
            CriCpkEntry sourceEntry = archive.GetEntry(reference.Id);
            if (sourceEntry.IsCrilayla)
            {
                throw new ToolkitException(
                    "WORKSPACE_COMPRESSED_SCRIPT",
                    $"Script entry {reference.Id} is compressed and cannot be replaced safely.");
            }
            string scriptHash = BinaryUtilities.Sha256Hex(sourceEntry.PackedData);
            if (!string.Equals(scriptHash, reference.SourceSha256, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(scriptHash, translation.SourceSha256, StringComparison.OrdinalIgnoreCase)
                || sourceEntry.PackedData.Length != reference.SourceLength
                || sourceEntry.PackedData.Length != translation.SourceLength)
            {
                throw new ToolkitException(
                    "WORKSPACE_SCRIPT_HASH",
                    $"Source script {reference.Id} does not match workspace metadata.");
            }

            LuckyStarScript script = LuckyStarScript.Parse(sourceEntry.PackedData, profile, limits, true);
            ScriptMutation mutation = BuildMutation(script, translation, map);
            ScriptBuildResult rebuilt = script.Build(mutation, limits);
            bool bytesChanged = !rebuilt.Data.AsSpan().SequenceEqual(sourceEntry.PackedData);
            if (bytesChanged)
            {
                changed++;
                archive.ReplaceEntry(reference.Id, rebuilt.Data);
            }
            if (rebuilt.OldBlocks2K != rebuilt.NewBlocks2K)
            {
                planEntries.Add(new EbootSizePatchEntry
                {
                    Id = reference.Id,
                    OldBlocks2K = Guard.CheckedUInt16(rebuilt.OldBlocks2K, "EBOOT_PLAN_SIZE", "old script blocks"),
                    NewBlocks2K = Guard.CheckedUInt16(rebuilt.NewBlocks2K, "EBOOT_PLAN_SIZE", "new script blocks")
                });
            }

            int scriptTranslatedSpeakers = translation.Dialogs.Count(static item => item.TranslationSpeaker is not null);
            int scriptTranslatedMessages = translation.Dialogs.Count(static item => item.TranslationMessage is not null);
            int scriptChoices = translation.ChoiceGroups.Sum(static group => group.Choices.Count);
            int scriptTranslatedChoices = translation.ChoiceGroups.Sum(
                static group => group.Choices.Count(static choice => choice.TranslationText is not null));
            dialogCount += translation.Dialogs.Count;
            translatedSpeakers += scriptTranslatedSpeakers;
            translatedMessages += scriptTranslatedMessages;
            choiceCount += scriptChoices;
            translatedChoices += scriptTranslatedChoices;
            scriptResults.Add(new WorkspaceScriptValidationResult(
                reference.Id,
                sourceEntry.PackedData.Length,
                rebuilt.Data.Length,
                bytesChanged,
                translation.Dialogs.Count,
                scriptTranslatedSpeakers,
                scriptTranslatedMessages,
                scriptChoices,
                scriptTranslatedChoices,
                rebuilt.OldBlocks2K,
                rebuilt.NewBlocks2K));
        }

        HashSet<string> declaredNames = manifest.Scripts
            .Select(static item => item.File)
            .ToHashSet(PathUtilities.FileSystemComparer);
        string[] unreferenced = Directory.EnumerateFiles(workspace, "script-*.json", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .Where(name => name is not null && !declaredNames.Contains(name))
            .Cast<string>()
            .Order(PathUtilities.FileSystemComparer)
            .ToArray();
        if (unreferenced.Length > 0)
        {
            warnings.Add($"Unreferenced script JSON files are ignored: {string.Join(", ", unreferenced)}");
        }

        CriCpkBuildResult cpkBuild = archive.Build(limits);
        string rebuiltCpkHash = BinaryUtilities.Sha256Hex(cpkBuild.Data);
        EbootSizePatchPlan plan = EbootSizePatchPlan.Create(
            profile,
            planEntries,
            sourceHash,
            rebuiltCpkHash);
        warnings.AddRange(plan.Warnings);
        if (planEntries.Count == 0)
        {
            warnings.Add("No script crossed a 2 KiB size boundary; the EBOOT size plan contains no changes.");
        }
        if (translatedSpeakers + translatedMessages + translatedChoices == 0)
        {
            warnings.Add("Workspace contains no translation fields; rebuilt content is semantically unchanged.");
        }

        WorkspaceValidationResult validation = new(
            workspace,
            manifest.Game,
            sourceHash,
            rebuiltCpkHash,
            sourceCpk.Length,
            cpkBuild.Data.Length,
            manifest.Scripts.Count,
            changed,
            dialogCount,
            translatedSpeakers,
            translatedMessages,
            choiceCount,
            translatedChoices,
            planEntries.Count,
            scriptResults,
            warnings);
        return new PreparedWorkspaceBuild(cpkBuild.Data, plan, validation);
    }

    /// <summary>
    /// Validates manifest while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="manifest">The validated operation manifest.</param>
    /// <remarks>Malformed input or violated preconditions are rejected before a persistent output is committed.</remarks>
    private static void ValidateManifest(TranslationWorkspaceManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        if (manifest.SchemaVersion != 1)
        {
            throw new ToolkitException("WORKSPACE_SCHEMA", $"Unsupported workspace schema {manifest.SchemaVersion}.");
        }
        if (string.IsNullOrWhiteSpace(manifest.ToolVersion))
        {
            throw new ToolkitException("WORKSPACE_TOOL_VERSION", "Workspace tool version is missing.");
        }
        if (manifest.CreatedUtc == default)
        {
            throw new ToolkitException("WORKSPACE_CREATED", "Workspace creation timestamp is missing.");
        }
        if (!Enum.TryParse(manifest.Game, false, out LuckyStarGame game)
            || !Enum.IsDefined(typeof(LuckyStarGame), game))
        {
            throw new ToolkitException("WORKSPACE_GAME", $"Unknown workspace game '{manifest.Game}'.");
        }
        ValidateSha256(manifest.SourceCpkSha256, "WORKSPACE_SOURCE_HASH", "source CPK hash");
        ValidateSha256(manifest.GlyphMapSha256, "WORKSPACE_MAP_HASH", "glyph map hash");
        if (string.IsNullOrWhiteSpace(manifest.GlyphMapFile))
        {
            throw new ToolkitException("WORKSPACE_MAP_FILE", "Workspace glyph map file is missing.");
        }
        if (manifest.Scripts is null || manifest.Scripts.Count == 0)
        {
            throw new ToolkitException("WORKSPACE_NO_SCRIPTS", "Workspace manifest contains no scripts.");
        }
        foreach (TranslationWorkspaceScriptReference? reference in manifest.Scripts)
        {
            if (reference is null)
            {
                throw new ToolkitException("WORKSPACE_SCRIPT_REFERENCE", "Workspace manifest contains a null script reference.");
            }
            if (string.IsNullOrWhiteSpace(reference.File) || reference.SourceLength <= 0)
            {
                throw new ToolkitException("WORKSPACE_SCRIPT_REFERENCE", $"Script reference {reference.Id} is incomplete.");
            }
            ValidateSha256(reference.SourceSha256, "WORKSPACE_SCRIPT_HASH", $"script {reference.Id} hash");
        }
    }

    /// <summary>
    /// Validates SHA 256 while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="value">The value to process.</param>
    /// <param name="code">The code value.</param>
    /// <param name="name">The logical name used for lookup or diagnostics.</param>
    /// <remarks>Malformed input or violated preconditions are rejected before a persistent output is committed.</remarks>
    private static void ValidateSha256(string value, string code, string name)
    {
        if (string.IsNullOrEmpty(value) || value.Length != 64 || value.Any(static character => !Uri.IsHexDigit(character)))
        {
            throw new ToolkitException(code, $"Workspace {name} is not a 64-character SHA-256 value.");
        }
    }

    /// <summary>
    /// Converts workspace file while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="scriptId">The numeric scenario identifier.</param>
    /// <param name="script">The script value.</param>
    /// <param name="map">The glyph map used for text conversion.</param>
    /// <param name="source">The source binary data or object.</param>
    /// <returns>The validated operation result.</returns>
    private static TranslationScriptFile ToWorkspaceFile(ushort scriptId, LuckyStarScript script, GlyphMap map, byte[] source)
    {
        TranslationScriptFile file = new()
        {
            ScriptId = scriptId,
            SourceSha256 = BinaryUtilities.Sha256Hex(source),
            SourceLength = source.Length
        };
        foreach (ScriptDialog dialog in script.Dialogs)
        {
            file.Dialogs.Add(new TranslationDialog
            {
                Index = dialog.Index,
                Id = dialog.Id,
                SourceSpeaker = map.Decode(dialog.SpeakerGlyphs),
                SourceMessage = map.Decode(dialog.MessageGlyphs),
                MessageTerminator = $"0x{dialog.MessageTerminator:X4}"
            });
        }
        foreach (ScriptChoiceGroup group in script.ChoiceGroups)
        {
            TranslationChoiceGroup target = new() { Index = group.Index, JumpId = group.JumpId };
            foreach (ScriptChoice choice in group.Choices)
            {
                target.Choices.Add(new TranslationChoice
                {
                    Index = choice.ChoiceIndex,
                    SourceText = map.Decode(choice.Glyphs)
                });
            }
            file.ChoiceGroups.Add(target);
        }
        return file;
    }

    /// <summary>
    /// Builds mutation while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="script">The script value.</param>
    /// <param name="translation">The translation value.</param>
    /// <param name="map">The glyph map used for text conversion.</param>
    /// <returns>The validated operation result.</returns>
    /// <remarks>Malformed input or violated preconditions are rejected before a persistent output is committed.</remarks>
    private static ScriptMutation BuildMutation(LuckyStarScript script, TranslationScriptFile translation, GlyphMap map)
    {
        if (translation.Dialogs.Count != script.Dialogs.Count || translation.ChoiceGroups.Count != script.ChoiceGroups.Count)
        {
            throw new ToolkitException("WORKSPACE_STRUCTURE", $"Script {translation.ScriptId} dialog/choice group counts were changed.");
        }
        ScriptMutation mutation = new();
        foreach (TranslationDialog item in translation.Dialogs)
        {
            if ((uint)item.Index >= (uint)script.Dialogs.Count || script.Dialogs[item.Index].Id != item.Id)
            {
                throw new ToolkitException("WORKSPACE_DIALOG_ID", $"Script {translation.ScriptId} dialog {item.Index} metadata was changed.");
            }
            string expectedTerminator = $"0x{script.Dialogs[item.Index].MessageTerminator:X4}";
            if (!string.Equals(item.MessageTerminator, expectedTerminator, StringComparison.OrdinalIgnoreCase))
            {
                throw new ToolkitException("WORKSPACE_TERMINATOR", $"Script {translation.ScriptId} dialog {item.Index} terminator was changed.");
            }
            string decodedSpeaker = map.Decode(script.Dialogs[item.Index].SpeakerGlyphs);
            string decodedMessage = map.Decode(script.Dialogs[item.Index].MessageGlyphs);
            if (!string.Equals(item.SourceSpeaker, decodedSpeaker, StringComparison.Ordinal)
                || !string.Equals(item.SourceMessage, decodedMessage, StringComparison.Ordinal))
            {
                throw new ToolkitException("WORKSPACE_SOURCE_TEXT", $"Script {translation.ScriptId} dialog {item.Index} source text was changed.");
            }
            if (item.TranslationSpeaker is not null)
            {
                ushort[] encoded = map.Encode(item.TranslationSpeaker);
                ValidateStructuralGlyphs(encoded, [0xFFFF], $"speaker {item.Index}");
                mutation.SpeakerGlyphs[item.Index] = encoded;
            }
            if (item.TranslationMessage is not null)
            {
                ushort[] encoded = map.Encode(item.TranslationMessage);
                ValidateStructuralGlyphs(encoded, [0xFFFB, 0xFFFD, 0xFFFF], $"message {item.Index}");
                mutation.MessageGlyphs[item.Index] = encoded;
            }
        }
        foreach (TranslationChoiceGroup group in translation.ChoiceGroups)
        {
            if ((uint)group.Index >= (uint)script.ChoiceGroups.Count || group.JumpId != script.ChoiceGroups[group.Index].JumpId)
            {
                throw new ToolkitException("WORKSPACE_CHOICE_GROUP", $"Script {translation.ScriptId} choice group {group.Index} metadata was changed.");
            }
            ScriptChoiceGroup sourceGroup = script.ChoiceGroups[group.Index];
            if (group.Choices.Count != sourceGroup.Choices.Count
                || group.Choices.Select(static choice => choice.Index).Distinct().Count() != group.Choices.Count
                || !group.Choices.Select(static choice => choice.Index).Order().SequenceEqual(sourceGroup.Choices.Select(static choice => choice.ChoiceIndex).Order()))
            {
                throw new ToolkitException("WORKSPACE_CHOICE_COUNT", $"Script {translation.ScriptId} choice group {group.Index} structure was changed.");
            }
            foreach (TranslationChoice choice in group.Choices)
            {
                if (sourceGroup.Choices.All(item => item.ChoiceIndex != choice.Index))
                {
                    throw new ToolkitException("WORKSPACE_CHOICE_ID", $"Script {translation.ScriptId} choice {group.Index}:{choice.Index} was added or renumbered.");
                }
                ScriptChoice sourceChoice = sourceGroup.Choices.Single(item => item.ChoiceIndex == choice.Index);
                if (!string.Equals(choice.SourceText, map.Decode(sourceChoice.Glyphs), StringComparison.Ordinal))
                {
                    throw new ToolkitException("WORKSPACE_SOURCE_TEXT", $"Script {translation.ScriptId} choice {group.Index}:{choice.Index} source text was changed.");
                }
                if (choice.TranslationText is not null)
                {
                    ushort[] encoded = map.Encode(choice.TranslationText);
                    ValidateStructuralGlyphs(encoded, [0xFFFF], $"choice {group.Index}:{choice.Index}");
                    mutation.ChoiceGlyphs[(group.Index, choice.Index)] = encoded;
                }
            }
        }
        return mutation;
    }


    /// <summary>
    /// Validates structural glyphs while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="glyphs">The glyphs value.</param>
    /// <param name="forbidden">The forbidden value.</param>
    /// <param name="context">A diagnostic label included in validation errors.</param>
    /// <remarks>Malformed input or violated preconditions are rejected before a persistent output is committed.</remarks>
    private static void ValidateStructuralGlyphs(IEnumerable<ushort> glyphs, ReadOnlySpan<ushort> forbidden, string context)
    {
        foreach (ushort glyph in glyphs)
        {
            if (forbidden.Contains(glyph))
            {
                throw new ToolkitException("WORKSPACE_CONTROL_GLYPH", $"Translation {context} contains structural control glyph 0x{glyph:X4}.");
            }
        }
    }

    /// <summary>
    /// Validates workspace script while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="reference">The reference value.</param>
    /// <param name="script">The script value.</param>
    /// <remarks>Malformed input or violated preconditions are rejected before a persistent output is committed.</remarks>
    private static void ValidateWorkspaceScript(TranslationWorkspaceScriptReference reference, TranslationScriptFile script)
    {
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(script);
        if (script.SchemaVersion != 1 || script.ScriptId != reference.Id)
        {
            throw new ToolkitException("WORKSPACE_SCRIPT_SCHEMA", $"Workspace file '{reference.File}' has an invalid schema or script ID.");
        }
        if (script.SourceLength <= 0)
        {
            throw new ToolkitException("WORKSPACE_SCRIPT_LENGTH", $"Workspace file '{reference.File}' has an invalid source length.");
        }
        ValidateSha256(script.SourceSha256, "WORKSPACE_SCRIPT_HASH", $"script {reference.Id} file hash");
        if (script.Dialogs is null || script.ChoiceGroups is null)
        {
            throw new ToolkitException("WORKSPACE_STRUCTURE", $"Workspace file '{reference.File}' has null structural collections.");
        }
        if (script.Dialogs.Any(static item => item is null)
            || script.ChoiceGroups.Any(static item => item is null))
        {
            throw new ToolkitException("WORKSPACE_STRUCTURE", $"Workspace file '{reference.File}' contains null structural items.");
        }
        if (script.Dialogs.Select(static item => item.Index).Distinct().Count() != script.Dialogs.Count
            || script.ChoiceGroups.Select(static item => item.Index).Distinct().Count() != script.ChoiceGroups.Count)
        {
            throw new ToolkitException("WORKSPACE_DUPLICATE_INDEX", $"Workspace file '{reference.File}' contains duplicate indexes.");
        }
        foreach (TranslationChoiceGroup group in script.ChoiceGroups)
        {
            if (group.Choices is null || group.Choices.Any(static item => item is null))
            {
                throw new ToolkitException("WORKSPACE_STRUCTURE", $"Workspace file '{reference.File}' contains a null choice collection or item.");
            }
            if (group.Choices.Select(static item => item.Index).Distinct().Count() != group.Choices.Count)
            {
                throw new ToolkitException("WORKSPACE_DUPLICATE_INDEX", $"Workspace file '{reference.File}' contains duplicate choice indexes in group {group.Index}.");
            }
        }
    }

    /// <summary>
    /// Resolves script ids while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="archive">The parsed archive to inspect or modify.</param>
    /// <param name="profile">The revision-specific format or patch profile.</param>
    /// <param name="requested">The requested value.</param>
    /// <returns>The validated operation result.</returns>
    private static List<ushort> ResolveScriptIds(CriCpkArchive archive, ScriptProfile profile, IReadOnlyCollection<ushort>? requested)
    {
        if (requested is { Count: > 0 })
        {
            List<ushort> result = requested.Distinct().Order().ToList();
            foreach (ushort id in result)
            {
                _ = archive.GetEntry(id);
            }
            return result;
        }
        if (profile.Game == LuckyStarGame.RyououGakuenOutousaiPortable)
        {
            List<ushort> expected = Enumerable.Range(0, 11).Select(static value => checked((ushort)value)).ToList();
            foreach (ushort id in expected)
            {
                _ = archive.GetEntry(id);
            }
            return expected;
        }
        return archive.Entries.Keys.Order().ToList();
    }

    /// <summary>
    /// Reads JSON while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <typeparam name="T">The t type used by the operation.</typeparam>
    /// <param name="path">The file-system path to process.</param>
    /// <param name="limits">Optional conservative safety limits; defaults are used when omitted.</param>
    /// <returns>The validated operation result.</returns>
    private static T ReadJson<T>(string path, FileLimits? limits = null)
    {
        limits ??= FileLimits.Default;
        byte[] data = BinaryUtilities.ReadAllBytesBounded(
            path, limits with { MaximumInputBytes = limits.MaximumTextBytes });
        try
        {
            _ = new System.Text.UTF8Encoding(false, true).GetCharCount(data);
        }
        catch (System.Text.DecoderFallbackException ex)
        {
            throw new ToolkitException("WORKSPACE_JSON_UTF8", $"JSON file is not valid UTF-8: {path}", ex);
        }

        try
        {
            T? value = StrictJson.Deserialize<T>(data, EbootSizePatchPlan.JsonOptions, "WORKSPACE_JSON");
            return value ?? throw new ToolkitException("WORKSPACE_JSON", $"JSON file is empty: {path}");
        }
        catch (JsonException ex)
        {
            throw new ToolkitException("WORKSPACE_JSON", $"JSON file is invalid: {path}: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Writes JSON while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <typeparam name="T">The t type used by the operation.</typeparam>
    /// <param name="path">The file-system path to process.</param>
    /// <param name="value">The value to process.</param>
    private static void WriteJson<T>(string path, T value)
        => AtomicFile.WriteAllText(path, JsonSerializer.Serialize(value, EbootSizePatchPlan.JsonOptions));

    /// <summary>
    /// Represents immutable prepared workspace build data exchanged by the toolkit.
    /// </summary>
    /// <param name="CpkData">The cpk data value used by this model or operation.</param>
    /// <param name="Plan">The plan value used by this model or operation.</param>
    /// <param name="Validation">The validation value used by this model or operation.</param>
    private sealed record PreparedWorkspaceBuild(
        byte[] CpkData,
        EbootSizePatchPlan Plan,
        WorkspaceValidationResult Validation);
}
