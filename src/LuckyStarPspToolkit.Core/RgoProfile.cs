using System.Text;

namespace LuckyStarPspToolkit;

/// <summary>
/// Defines the supported executable compatibility values.
/// </summary>
public enum ExecutableCompatibility
{
    /// <summary>The invalid value used by this model or operation.</summary>
    Invalid,
    /// <summary>The unsupported value used by this model or operation.</summary>
    Unsupported,
    /// <summary>The representative patch points match value used by this model or operation.</summary>
    RepresentativePatchPointsMatch,
    /// <summary>The exact verified revision value used by this model or operation.</summary>
    ExactVerifiedRevision,
    /// <summary>The recognized vwf patch lineage value used by this model or operation.</summary>
    RecognizedVwfPatchLineage,
    /// <summary>The exact verified vwf revision value used by this model or operation.</summary>
    ExactVerifiedVwfRevision
}

/// <summary>
/// Represents the toolkit's patch point check model or service.
/// </summary>
public sealed class PatchPointCheck
{
    /// <summary>The name value used by this model or operation.</summary>
    public required string Name { get; init; }
    /// <summary>The offset value used by this model or operation.</summary>
    public required int Offset { get; init; }
    /// <summary>The offset hex value used by this model or operation.</summary>
    public required string OffsetHex { get; init; }
    /// <summary>The expected value used by this model or operation.</summary>
    public required uint Expected { get; init; }
    /// <summary>The actual value used by this model or operation.</summary>
    public required uint Actual { get; init; }
    /// <summary>The matches value used by this model or operation.</summary>
    public required bool Matches { get; init; }
}

/// <summary>
/// Represents the toolkit's RGO script slot model or service.
/// </summary>
public sealed class RgoScriptSlot
{
    /// <summary>The resource identifier preserved across extraction and rebuilding.</summary>
    public ushort Id { get; init; }
    /// <summary>The blocks2 k value used by this model or operation.</summary>
    public ushort Blocks2K { get; init; }
    /// <summary>The cumulative blocks2 k value used by this model or operation.</summary>
    public ushort CumulativeBlocks2K { get; init; }
    /// <summary>The size bytes value used by this model or operation.</summary>
    public uint SizeBytes { get; init; }
}

/// <summary>
/// Represents the toolkit's RGO executable check model or service.
/// </summary>
public sealed class RgoExecutableCheck
{
    /// <summary>The source was encrypted value used by this model or operation.</summary>
    public bool SourceWasEncrypted { get; init; }
    /// <summary>The decryption succeeded value used by this model or operation.</summary>
    public bool DecryptionSucceeded { get; init; }
    /// <summary>The is elf value used by this model or operation.</summary>
    public bool IsElf { get; init; }
    /// <summary>The is psp mips value used by this model or operation.</summary>
    public bool IsPspMips { get; init; }
    /// <summary>The exact encrypted revision value used by this model or operation.</summary>
    public bool ExactEncryptedRevision { get; init; }
    /// <summary>The exact decrypted revision value used by this model or operation.</summary>
    public bool ExactDecryptedRevision { get; init; }
    /// <summary>The exact full vwf patch value used by this model or operation.</summary>
    public bool ExactFullVwfPatch { get; init; }
    /// <summary>The patch points compatible value used by this model or operation.</summary>
    public bool PatchPointsCompatible { get; init; }
    /// <summary>The vwf patch lineage compatible value used by this model or operation.</summary>
    public bool VwfPatchLineageCompatible { get; init; }
    /// <summary>The script size table modified value used by this model or operation.</summary>
    public bool ScriptSizeTableModified { get; init; }
    /// <summary>The script size table consistent value used by this model or operation.</summary>
    public bool ScriptSizeTableConsistent { get; init; }
    /// <summary>The script heap modified value used by this model or operation.</summary>
    public bool ScriptHeapModified { get; init; }
    /// <summary>The script heap consistent value used by this model or operation.</summary>
    public bool ScriptHeapConsistent { get; init; }
    /// <summary>The script heap bytes value used by this model or operation.</summary>
    public int ScriptHeapBytes { get; init; }
    /// <summary>The required script heap bytes value used by this model or operation.</summary>
    public int RequiredScriptHeapBytes { get; init; }
    /// <summary>The vwf original word count value used by this model or operation.</summary>
    public int VwfOriginalWordCount { get; init; }
    /// <summary>The vwf patched word count value used by this model or operation.</summary>
    public int VwfPatchedWordCount { get; init; }
    /// <summary>The vwf mismatch word count value used by this model or operation.</summary>
    public int VwfMismatchWordCount { get; init; }
    /// <summary>The compatibility value used by this model or operation.</summary>
    public ExecutableCompatibility Compatibility { get; init; }
    /// <summary>The encrypted sha256 value used by this model or operation.</summary>
    public string? EncryptedSha256 { get; init; }
    /// <summary>The decrypted sha256 value used by this model or operation.</summary>
    public string? DecryptedSha256 { get; init; }
    /// <summary>The failure value used by this model or operation.</summary>
    public string? Failure { get; init; }
    /// <summary>The message value used by this model or operation.</summary>
    public required string Message { get; init; }
    /// <summary>The psp header value used by this model or operation.</summary>
    public PspModuleHeader? PspHeader { get; init; }
    /// <summary>The elf header value used by this model or operation.</summary>
    public Elf32Info? ElfHeader { get; init; }
    /// <summary>The referenced assets value used by this model or operation.</summary>
    public required IReadOnlyList<string> ReferencedAssets { get; init; }
    /// <summary>The script slots value used by this model or operation.</summary>
    public required IReadOnlyList<RgoScriptSlot> ScriptSlots { get; init; }
    /// <summary>The patch points value used by this model or operation.</summary>
    public required IReadOnlyList<PatchPointCheck> PatchPoints { get; init; }

    [System.Text.Json.Serialization.JsonIgnore]
    /// <summary>The decrypted elf value used by this model or operation.</summary>
    public byte[]? DecryptedElf { get; init; }
}

/// <summary>
/// Represents the toolkit's RGO resources model or service.
/// </summary>
public sealed class RgoResources
{
    /// <summary>The sc cpk value used by this model or operation.</summary>
    public bool ScCpk { get; init; }
    /// <summary>The lt bin value used by this model or operation.</summary>
    public bool LtBin { get; init; }
    /// <summary>The union cpk value used by this model or operation.</summary>
    public bool UnionCpk { get; init; }
    /// <summary>The pr bin value used by this model or operation.</summary>
    public bool PrBin { get; init; }
}

/// <summary>
/// Represents the toolkit's RGO audit model or service.
/// </summary>
public sealed class RgoAudit
{
    /// <summary>The schema value used by this model or operation.</summary>
    public string Schema { get; init; } = "lucky-star-psp.rgo-audit.v2";
    /// <summary>The input value used by this model or operation.</summary>
    public required string Input { get; init; }
    /// <summary>The status value used by this model or operation.</summary>
    public required string Status { get; init; }
    /// <summary>The game metadata found value used by this model or operation.</summary>
    public bool GameMetadataFound { get; init; }
    /// <summary>The game executable found value used by this model or operation.</summary>
    public bool GameExecutableFound { get; init; }
    /// <summary>The zero filled boot found value used by this model or operation.</summary>
    public bool ZeroFilledBootFound { get; init; }
    /// <summary>The firmware update files found value used by this model or operation.</summary>
    public bool FirmwareUpdateFilesFound { get; init; }
    /// <summary>The mixed content detected value used by this model or operation.</summary>
    public bool MixedContentDetected { get; init; }
    /// <summary>The resources value used by this model or operation.</summary>
    public required RgoResources Resources { get; init; }
    /// <summary>The executable value used by this model or operation.</summary>
    public RgoExecutableCheck? Executable { get; init; }
    /// <summary>The required next files value used by this model or operation.</summary>
    public required IReadOnlyList<string> RequiredNextFiles { get; init; }
    /// <summary>The warnings value used by this model or operation.</summary>
    public required IReadOnlyList<string> Warnings { get; init; }
}

/// <summary>
/// Provides the toolkit's RGO profile workflow.
/// </summary>
public static class RgoProfile
{
    /// <summary>The fixed disc id value used by this format or revision.</summary>
    public const string DiscId = "ULJM05752";
    /// <summary>The fixed known encrypted sha256 value used by this format or revision.</summary>
    public const string KnownEncryptedSha256 =
        "4a22c50c0a6ad6249dd1d9ab015d559b19b4acd79daf892a6f29a58648b834f0";
    /// <summary>The fixed known decrypted sha256 value used by this format or revision.</summary>
    public const string KnownDecryptedSha256 =
        "3e6c1c2f7136a69cecda4f39835c3454b739e23e89e862fe17462cdf915581d8";
    /// <summary>The fixed known decrypted size value used by this format or revision.</summary>
    public const int KnownDecryptedSize = 1_470_173;
    /// <summary>The fixed script size table offset value used by this format or revision.</summary>
    public const int ScriptSizeTableOffset = 0x10319E;
    /// <summary>The fixed script first id value used by this format or revision.</summary>
    public const ushort ScriptFirstId = 0;
    /// <summary>The fixed script last id value used by this format or revision.</summary>
    public const ushort ScriptLastId = 10;
    /// <summary>The fixed archive block size value used by this format or revision.</summary>
    public const uint ArchiveBlockSize = 2048;
    /// <summary>The fixed script heap lui offset value used by this format or revision.</summary>
    public const int ScriptHeapLuiOffset = 0x15B54;
    /// <summary>The fixed script heap addiu offset value used by this format or revision.</summary>
    public const int ScriptHeapAddiuOffset = 0x15B58;
    /// <summary>The fixed known script heap bytes value used by this format or revision.</summary>
    public const int KnownScriptHeapBytes = 0x236000;
    /// <summary>The fixed known script heap lui instruction value used by this format or revision.</summary>
    public const uint KnownScriptHeapLuiInstruction = 0x3C040023;
    /// <summary>The fixed known script heap addiu instruction value used by this format or revision.</summary>
    public const uint KnownScriptHeapAddiuInstruction = 0x24846000;

    private static readonly (string Name, int Offset, uint Expected)[] PatchPreconditions =
    [
        ("speaker-line-spacing", 0x003D24, 0x24840010),
        ("textbox-position", 0x004D98, 0x24A5007D),
        ("choice-width-1", 0x007868, 0x34040012),
        ("choice-width-2", 0x00786C, 0x00932023),
        ("choice-width-3", 0x007940, 0x10800014),
        ("message-overlap", 0x007A9C, 0x2508FFFE),
        ("message-size-overlap", 0x007B4C, 0x2665FFFE),
        ("message-log-overlap", 0x007C38, 0x2508FFFE),
        ("variable-width-font-hook", 0x025CD8, 0x00A04825),
        ("name-spacing-hook", 0x025DE0, 0x3128FFFF),
        ("file-select-name-hook", 0x0362F4, 0x00A03025),
        ("file-select-overlap", 0x0366E4, 0x26240010),
        ("speaker-centering-hook", 0x03A648, 0x00E04025),
        ("speaker-limit-stack", 0x03A93C, 0x27BDFFD0),
        ("automatic-newline", 0x03AC84, 0x11400012)
    ];

    /// <summary>
    /// Verifies executable while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="source">The source binary data or object.</param>
    /// <returns>The validated operation result.</returns>
    /// <remarks>Malformed input or violated preconditions are rejected before a persistent output is committed.</remarks>
    public static RgoExecutableCheck VerifyExecutable(ReadOnlySpan<byte> source)
    {
        bool encrypted = PspPrxReader.LooksLike(source);
        string? encryptedHash = encrypted ? CryptoUtilities.Sha256Hex(source) : null;
        bool exactEncrypted = string.Equals(
            encryptedHash, KnownEncryptedSha256, StringComparison.OrdinalIgnoreCase);
        byte[] plaintext;
        PspModuleHeader? pspHeader = null;

        try
        {
            if (encrypted)
            {
                pspHeader = PspPrxReader.ParseHeader(source);
                PspDecryptionResult result = PspPrxReader.DecryptVerified(source);
                plaintext = result.Elf;
            }
            else
            {
                plaintext = source.ToArray();
            }
        }
        catch (ToolkitException exception)
        {
            return FailureResult(encrypted, encryptedHash, exactEncrypted, pspHeader, exception.Message);
        }

        if (!ElfReader.LooksLike(plaintext))
        {
            return FailureResult(encrypted, encryptedHash, exactEncrypted, pspHeader,
                "Input is neither a supported encrypted game EBOOT nor a decrypted ELF.");
        }

        Elf32Info elf;
        try
        {
            elf = ElfReader.Parse32LittleEndian(plaintext);
        }
        catch (ToolkitException exception)
        {
            return FailureResult(encrypted, encryptedHash, exactEncrypted, pspHeader, exception.Message);
        }

        string decryptedHash = CryptoUtilities.Sha256Hex(plaintext);
        bool exactDecrypted = plaintext.Length == KnownDecryptedSize
            && string.Equals(decryptedHash, KnownDecryptedSha256, StringComparison.OrdinalIgnoreCase);

        var assets = new List<string>();
        foreach (string asset in new[] { "sc.cpk", "lt.bin", "union.cpk", "pr.bin" })
        {
            if (BinaryData.ContainsAscii(plaintext, asset))
            {
                assets.Add(asset);
            }
        }

        IReadOnlyList<RgoScriptSlot> slots = ReadScriptSlots(plaintext, out bool tableConsistent);
        IReadOnlyList<PatchPointCheck> patchPoints = VerifyPatchPoints(plaintext);
        bool patchCompatible = patchPoints.Count == PatchPreconditions.Length
            && patchPoints.All(point => point.Matches);

        RgoVwfPatchInspection? vwfInspection = null;
        try
        {
            vwfInspection = RgoVwfPatchProfile.Inspect(plaintext);
        }
        catch (ToolkitException)
        {
            // The generic verifier must continue to classify unrelated revisions without
            // leaking patch-profile implementation details through an exception.
        }

        bool vwfCompatible = vwfInspection?.CompatibleKnownLineage == true
            && vwfInspection.MismatchWordCount == 0;
        bool exactFullVwf = vwfInspection?.ExactKnownFullPatch == true;

        ExecutableCompatibility compatibility;
        string message;
        if (!elf.IsPspMips)
        {
            compatibility = ExecutableCompatibility.Unsupported;
            message = "Decrypted executable is not a PSP MIPS ELF.";
        }
        else if (exactDecrypted && patchCompatible && tableConsistent)
        {
            compatibility = ExecutableCompatibility.ExactVerifiedRevision;
            message = "Exact verified ULJM05752 executable revision.";
        }
        else if (exactFullVwf && vwfCompatible && tableConsistent)
        {
            compatibility = ExecutableCompatibility.ExactVerifiedVwfRevision;
            message = "Exact verified ULJM05752 executable with the complete Russian VWF patch.";
        }
        else if (vwfCompatible && tableConsistent)
        {
            compatibility = ExecutableCompatibility.RecognizedVwfPatchLineage;
            message = vwfInspection!.AlreadyApplied
                ? "Verified ULJM05752 VWF-patched executable lineage with an adjusted script-size table."
                : "Verified ULJM05752 executable lineage with a recognized partial VWF patch state.";
        }
        else if (patchCompatible && tableConsistent)
        {
            compatibility = ExecutableCompatibility.RepresentativePatchPointsMatch;
            message = "Unknown hash, but all patch preconditions and the script table are consistent.";
        }
        else
        {
            compatibility = ExecutableCompatibility.Unsupported;
            message = "Executable revision does not safely match the known RGO patch layout.";
        }

        return new RgoExecutableCheck
        {
            SourceWasEncrypted = encrypted,
            DecryptionSucceeded = true,
            IsElf = true,
            IsPspMips = elf.IsPspMips,
            ExactEncryptedRevision = exactEncrypted,
            ExactDecryptedRevision = exactDecrypted,
            ExactFullVwfPatch = exactFullVwf,
            PatchPointsCompatible = patchCompatible,
            VwfPatchLineageCompatible = vwfCompatible,
            ScriptSizeTableModified = vwfInspection?.ScriptSizeTableModified == true,
            ScriptSizeTableConsistent = tableConsistent,
            ScriptHeapModified = vwfInspection?.ScriptHeapModified == true,
            ScriptHeapConsistent = vwfInspection?.ScriptHeapConsistent == true,
            ScriptHeapBytes = vwfInspection?.ScriptHeapBytes ?? 0,
            RequiredScriptHeapBytes = vwfInspection?.RequiredScriptHeapBytes ?? 0,
            VwfOriginalWordCount = vwfInspection?.OriginalWordCount ?? 0,
            VwfPatchedWordCount = vwfInspection?.PatchedWordCount ?? 0,
            VwfMismatchWordCount = vwfInspection?.MismatchWordCount ?? 0,
            Compatibility = compatibility,
            EncryptedSha256 = encryptedHash,
            DecryptedSha256 = decryptedHash,
            Message = message,
            PspHeader = pspHeader,
            ElfHeader = elf,
            ReferencedAssets = assets.AsReadOnly(),
            ScriptSlots = slots,
            PatchPoints = patchPoints,
            DecryptedElf = plaintext
        };
    }

    /// <summary>
    /// Audits the supplied input and returns a structured safety and compatibility report.
    /// </summary>
    /// <param name="inspection">The inspection value.</param>
    /// <returns>The validated operation result.</returns>
    public static RgoAudit Audit(InspectionResult inspection)
    {
        ArgumentNullException.ThrowIfNull(inspection);
        bool gameMetadata = false;
        bool gameExecutable = false;
        bool zeroBoot = false;
        bool firmware = false;
        bool scCpk = false;
        bool ltBin = false;
        bool unionCpk = false;
        bool prBin = false;
        var warnings = new List<string>(inspection.Warnings);
        var executableCandidates = new List<FileProbe>();

        foreach (FileProbe probe in inspection.Files)
        {
            string baseName = probe.BaseName;
            if (probe.Sfo is not null)
            {
                string discId = probe.Sfo.GetString("DISC_ID") ?? string.Empty;
                string category = probe.Sfo.GetString("CATEGORY") ?? string.Empty;
                gameMetadata |= discId == DiscId;
                firmware |= discId == "MSTKUPDATE" || category == "MG";
            }

            if (probe.PspHeader is not null)
            {
                if (probe.PspHeader.ModuleName == "user_main")
                {
                    gameExecutable = true;
                    executableCandidates.Add(probe);
                }
                else if (probe.PspHeader.ModuleName == "updater")
                {
                    firmware = true;
                }
            }

            zeroBoot |= probe.Kind == FileKind.ZeroFilled
                && baseName.Equals("BOOT.BIN", StringComparison.OrdinalIgnoreCase);
            firmware |= probe.Kind == FileKind.Psar;
            scCpk |= baseName.Equals("sc.cpk", StringComparison.OrdinalIgnoreCase);
            ltBin |= baseName.Equals("lt.bin", StringComparison.OrdinalIgnoreCase);
            unionCpk |= baseName.Equals("union.cpk", StringComparison.OrdinalIgnoreCase);
            prBin |= baseName.Equals("pr.bin", StringComparison.OrdinalIgnoreCase);
        }

        FileProbe? selectedExecutable = executableCandidates
            .OrderByDescending(candidate =>
                candidate.Sha256.Equals(KnownEncryptedSha256, StringComparison.OrdinalIgnoreCase))
            .ThenBy(candidate => candidate.LogicalPath, StringComparer.Ordinal)
            .FirstOrDefault();

        if (executableCandidates.Count > 1)
        {
            warnings.Add(
                $"Found {executableCandidates.Count} user_main executables; selected '{selectedExecutable?.LogicalPath}'.");
        }

        RgoExecutableCheck? executable = selectedExecutable is null
            ? null
            : VerifyExecutable(selectedExecutable.Data);

        if (zeroBoot)
        {
            warnings.Add("BOOT.BIN is zero-filled and cannot be used as a decrypted executable.");
        }
        if (firmware)
        {
            warnings.Add("The input mixes game files with PSP firmware-update files.");
        }

        bool mixedContent = firmware && (gameMetadata || gameExecutable);
        string status;
        if (!gameMetadata || !gameExecutable)
        {
            status = "game-not-identified";
        }
        else if (executable is null
            || !executable.DecryptionSucceeded
            || executable.Compatibility is ExecutableCompatibility.Invalid or ExecutableCompatibility.Unsupported)
        {
            status = "unsupported-or-damaged-executable";
        }
        else if (!scCpk || !ltBin)
        {
            status = "executable-verified-resources-missing";
        }
        else
        {
            status = "ready-for-script-and-font-analysis";
        }

        var nextFiles = new List<string>();
        if (!scCpk)
        {
            nextFiles.Add("PSP_GAME/USRDIR/DATA/sc.cpk");
        }
        if (!ltBin)
        {
            nextFiles.Add("PSP_GAME/USRDIR/DATA/lt.bin");
        }
        if (!unionCpk)
        {
            nextFiles.Add("PSP_GAME/USRDIR/DATA/union.cpk (menus and images)");
        }
        if (!prBin)
        {
            nextFiles.Add("PSP_GAME/USRDIR/DATA/pr.bin (interface images)");
        }
        if (nextFiles.Count > 0)
        {
            nextFiles.Add("complete PSP_GAME file listing with relative paths and sizes");
        }

        return new RgoAudit
        {
            Input = inspection.Input,
            Status = status,
            GameMetadataFound = gameMetadata,
            GameExecutableFound = gameExecutable,
            ZeroFilledBootFound = zeroBoot,
            FirmwareUpdateFilesFound = firmware,
            MixedContentDetected = mixedContent,
            Resources = new RgoResources
            {
                ScCpk = scCpk,
                LtBin = ltBin,
                UnionCpk = unionCpk,
                PrBin = prBin
            },
            Executable = executable,
            RequiredNextFiles = nextFiles.AsReadOnly(),
            Warnings = warnings.Distinct(StringComparer.Ordinal).ToList().AsReadOnly()
        };
    }

    /// <summary>
    /// Audits to human text while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="audit">The audit value.</param>
    /// <returns>The resulting text, path, identifier, or hexadecimal digest.</returns>
    public static string AuditToHumanText(RgoAudit audit)
    {
        ArgumentNullException.ThrowIfNull(audit);
        var output = new StringBuilder();
        output.AppendLine("Lucky Star RGO audit");
        output.AppendLine($"  status: {audit.Status}");
        output.AppendLine($"  game metadata {DiscId}: {YesNo(audit.GameMetadataFound)}");
        output.AppendLine($"  game EBOOT user_main:    {YesNo(audit.GameExecutableFound)}");
        output.AppendLine($"  zero-filled BOOT.BIN:    {(audit.ZeroFilledBootFound ? "yes (unusable)" : "no")}");
        output.AppendLine($"  firmware update files:   {(audit.FirmwareUpdateFilesFound ? "yes (not translation assets)" : "no")}");
        output.AppendLine($"  sc.cpk:                   {(audit.Resources.ScCpk ? "yes" : "MISSING")}");
        output.AppendLine($"  lt.bin:                   {(audit.Resources.LtBin ? "yes" : "MISSING")}");
        output.AppendLine($"  union.cpk:                {(audit.Resources.UnionCpk ? "yes" : "missing")}");
        output.AppendLine($"  pr.bin:                   {(audit.Resources.PrBin ? "yes" : "missing")}");

        if (audit.Executable is not null)
        {
            output.AppendLine($"  EBOOT result:             {audit.Executable.Message}");
            output.AppendLine($"  encrypted SHA-256:        {audit.Executable.EncryptedSha256 ?? "n/a"}");
            output.AppendLine($"  decrypted SHA-256:        {audit.Executable.DecryptedSha256 ?? "n/a"}");
            output.AppendLine($"  patch preconditions:      {audit.Executable.PatchPoints.Count(point => point.Matches)}/{audit.Executable.PatchPoints.Count}");
            output.AppendLine($"  VWF lineage:              {(audit.Executable.VwfPatchLineageCompatible ? "verified" : "not recognized")}");
            if (audit.Executable.VwfPatchLineageCompatible)
            {
                output.AppendLine(
                    $"  VWF original/patched/bad:  {audit.Executable.VwfOriginalWordCount}/" +
                    $"{audit.Executable.VwfPatchedWordCount}/{audit.Executable.VwfMismatchWordCount}");
            }
            output.AppendLine($"  script size table:        {(audit.Executable.ScriptSizeTableConsistent ? "consistent" : "INVALID")}");
            output.AppendLine(
                $"  script heap:              {(audit.Executable.ScriptHeapConsistent ? "consistent" : "INVALID")}; " +
                $"allocated {audit.Executable.ScriptHeapBytes} bytes, required {audit.Executable.RequiredScriptHeapBytes}");
            if (audit.Executable.ScriptSlots.Count > 0)
            {
                output.AppendLine("  script slots:");
                foreach (RgoScriptSlot slot in audit.Executable.ScriptSlots)
                {
                    output.AppendLine(
                        $"    id {slot.Id,2}: {slot.Blocks2K,4} x 2 KiB = {slot.SizeBytes,8} bytes; cumulative {slot.CumulativeBlocks2K}");
                }
            }
        }

        if (audit.Warnings.Count > 0)
        {
            output.AppendLine("Warnings:");
            foreach (string warning in audit.Warnings)
            {
                output.AppendLine($"  - {warning}");
            }
        }

        if (audit.RequiredNextFiles.Count > 0)
        {
            output.AppendLine("Required next files:");
            foreach (string file in audit.RequiredNextFiles)
            {
                output.AppendLine($"  - {file}");
            }
        }

        return output.ToString();
    }

    /// <summary>
    /// Decrypts for patching while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="source">The source binary data or object.</param>
    /// <returns>The resulting binary or typed sequence.</returns>
    /// <remarks>Malformed input or violated preconditions are rejected before a persistent output is committed.</remarks>
    public static byte[] DecryptForPatching(ReadOnlySpan<byte> source)
    {
        RgoExecutableCheck check = VerifyExecutable(source);
        Guard.Require(check.Compatibility == ExecutableCompatibility.ExactVerifiedRevision,
            $"Refusing to produce a patchable ELF: {check.Message}");
        return check.DecryptedElf?.ToArray()
            ?? throw new ToolkitException("Verified executable did not retain decrypted bytes.");
    }

    /// <summary>
    /// Reads script slots while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="plaintext">The plaintext value.</param>
    /// <param name="consistent">Receives consistent value when the operation succeeds.</param>
    /// <returns>The validated operation result.</returns>
    private static IReadOnlyList<RgoScriptSlot> ReadScriptSlots(
        ReadOnlySpan<byte> plaintext,
        out bool consistent)
    {
        int count = ScriptLastId - ScriptFirstId + 1;
        int length = checked(count * 4);
        if (ScriptSizeTableOffset < 0 || ScriptSizeTableOffset > plaintext.Length
            || length > plaintext.Length - ScriptSizeTableOffset)
        {
            consistent = false;
            return Array.Empty<RgoScriptSlot>();
        }

        var slots = new List<RgoScriptSlot>(count);
        uint cumulative = 0;
        consistent = true;
        for (ushort id = ScriptFirstId; id <= ScriptLastId; id++)
        {
            int tableIndex = id - ScriptFirstId;
            int offset = checked(ScriptSizeTableOffset + tableIndex * 4);
            ushort blocks = BinaryData.ReadUInt16LittleEndian(plaintext, offset, "RGO script size table");
            ushort cumulativeBlocks = BinaryData.ReadUInt16LittleEndian(
                plaintext, offset + 2, "RGO cumulative script size table");
            cumulative = checked(cumulative + blocks);
            consistent &= blocks > 0 && cumulative <= ushort.MaxValue && cumulativeBlocks == cumulative;
            slots.Add(new RgoScriptSlot
            {
                Id = id,
                Blocks2K = blocks,
                CumulativeBlocks2K = cumulativeBlocks,
                SizeBytes = checked((uint)blocks * ArchiveBlockSize)
            });
        }

        return slots.AsReadOnly();
    }

    /// <summary>
    /// Verifies patch points while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="plaintext">The plaintext value.</param>
    /// <returns>The validated operation result.</returns>
    /// <remarks>Malformed input or violated preconditions are rejected before a persistent output is committed.</remarks>
    private static IReadOnlyList<PatchPointCheck> VerifyPatchPoints(ReadOnlySpan<byte> plaintext)
    {
        var checks = new List<PatchPointCheck>(PatchPreconditions.Length);
        foreach ((string name, int offset, uint expected) in PatchPreconditions)
        {
            bool inRange = offset >= 0 && offset <= plaintext.Length && sizeof(uint) <= plaintext.Length - offset;
            uint actual = inRange
                ? BinaryData.ReadUInt32LittleEndian(plaintext, offset, $"RGO patch point '{name}'")
                : 0;
            checks.Add(new PatchPointCheck
            {
                Name = name,
                Offset = offset,
                OffsetHex = HexUtilities.Offset(offset),
                Expected = expected,
                Actual = actual,
                Matches = inRange && actual == expected
            });
        }

        return checks.AsReadOnly();
    }

    /// <summary>
    /// Builds a failed executable-check result while retaining available source and PRX header evidence.
    /// </summary>
    /// <param name="encrypted">The encrypted value.</param>
    /// <param name="encryptedHash">The encrypted hash value.</param>
    /// <param name="exactEncrypted">The exact encrypted value.</param>
    /// <param name="pspHeader">The psp header value.</param>
    /// <param name="failure">The failure value.</param>
    /// <returns>The validated operation result.</returns>
    private static RgoExecutableCheck FailureResult(
        bool encrypted,
        string? encryptedHash,
        bool exactEncrypted,
        PspModuleHeader? pspHeader,
        string failure)
    {
        return new RgoExecutableCheck
        {
            SourceWasEncrypted = encrypted,
            DecryptionSucceeded = false,
            IsElf = false,
            IsPspMips = false,
            ExactEncryptedRevision = exactEncrypted,
            ExactDecryptedRevision = false,
            ExactFullVwfPatch = false,
            PatchPointsCompatible = false,
            VwfPatchLineageCompatible = false,
            ScriptSizeTableModified = false,
            ScriptSizeTableConsistent = false,
            ScriptHeapModified = false,
            ScriptHeapConsistent = false,
            ScriptHeapBytes = 0,
            RequiredScriptHeapBytes = 0,
            VwfOriginalWordCount = 0,
            VwfPatchedWordCount = 0,
            VwfMismatchWordCount = 0,
            Compatibility = ExecutableCompatibility.Invalid,
            EncryptedSha256 = encryptedHash,
            Failure = failure,
            Message = failure,
            PspHeader = pspHeader,
            ReferencedAssets = Array.Empty<string>(),
            ScriptSlots = Array.Empty<RgoScriptSlot>(),
            PatchPoints = Array.Empty<PatchPointCheck>()
        };
    }

    /// <summary>
    /// Formats a Boolean value as stable human-readable yes/no text.
    /// </summary>
    /// <param name="value">The value to process.</param>
    /// <returns>The resulting text, path, identifier, or hexadecimal digest.</returns>
    private static string YesNo(bool value) => value ? "yes" : "no";
}
