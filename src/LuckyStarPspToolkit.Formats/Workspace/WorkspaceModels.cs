using LuckyStarPspToolkit.Formats.Common;

namespace LuckyStarPspToolkit.Formats.Workspace;

/// <summary>
/// Represents the toolkit's translation workspace manifest model or service.
/// </summary>
public sealed class TranslationWorkspaceManifest
{
    /// <summary>The schema revision; readers reject unsupported revisions.</summary>
    public int SchemaVersion { get; set; } = 1;
    /// <summary>The toolkit version that created this record.</summary>
    public string ToolVersion { get; set; } = ToolkitBuildInfo.Version;
    /// <summary>The exact game profile used for interpreting scenario structures.</summary>
    public string Game { get; set; } = string.Empty;
    /// <summary>The hexadecimal SHA-256 of the original, unmodified scenario archive.</summary>
    public string SourceCpkSha256 { get; set; } = string.Empty;
    /// <summary>The relative glyph-map filename resolved within the workspace directory.</summary>
    public string GlyphMapFile { get; set; } = "glyph-map.txt";
    /// <summary>The SHA-256 fingerprint binding this workspace to its glyph map.</summary>
    public string GlyphMapSha256 { get; set; } = string.Empty;
    /// <summary>The UTC creation time of the manifest; it does not prove runtime compatibility.</summary>
    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
    /// <summary>The scripts value used by this model or operation.</summary>
    public List<TranslationWorkspaceScriptReference> Scripts { get; set; } = [];
}

/// <summary>
/// Represents the toolkit's translation workspace script reference model or service.
/// </summary>
public sealed class TranslationWorkspaceScriptReference
{
    /// <summary>The resource identifier preserved across extraction and rebuilding.</summary>
    public ushort Id { get; set; }
    /// <summary>The file value used by this model or operation.</summary>
    public string File { get; set; } = string.Empty;
    /// <summary>The hexadecimal SHA-256 of the exact input snapshot used by this object.</summary>
    public string SourceSha256 { get; set; } = string.Empty;
    /// <summary>The expected original payload length, in bytes.</summary>
    public int SourceLength { get; set; }
}

/// <summary>
/// Represents the toolkit's translation script file model or service.
/// </summary>
public sealed class TranslationScriptFile
{
    /// <summary>The schema revision; readers reject unsupported revisions.</summary>
    public int SchemaVersion { get; set; } = 1;
    /// <summary>The numeric scenario ID in the source CPK archive.</summary>
    public ushort ScriptId { get; set; }
    /// <summary>The hexadecimal SHA-256 of the exact input snapshot used by this object.</summary>
    public string SourceSha256 { get; set; } = string.Empty;
    /// <summary>The expected original payload length, in bytes.</summary>
    public int SourceLength { get; set; }
    /// <summary>The dialogs value used by this model or operation.</summary>
    public List<TranslationDialog> Dialogs { get; set; } = [];
    /// <summary>The choice groups value used by this model or operation.</summary>
    public List<TranslationChoiceGroup> ChoiceGroups { get; set; } = [];
}

/// <summary>
/// Represents the toolkit's translation dialog model or service.
/// </summary>
public sealed class TranslationDialog
{
    /// <summary>The zero-based position in the corresponding source collection.</summary>
    public int Index { get; set; }
    /// <summary>The resource identifier preserved across extraction and rebuilding.</summary>
    public ushort Id { get; set; }
    /// <summary>The immutable decoded speaker name used to detect workspace tampering.</summary>
    public string SourceSpeaker { get; set; } = string.Empty;
    /// <summary>The replacement speaker name; null preserves the original and an empty string clears it.</summary>
    public string? TranslationSpeaker { get; set; }
    /// <summary>The immutable decoded dialogue used to detect workspace tampering.</summary>
    public string SourceMessage { get; set; } = string.Empty;
    /// <summary>The replacement dialogue text; null preserves the original and an empty string clears it.</summary>
    public string? TranslationMessage { get; set; }
    /// <summary>The original scenario terminator; translators must not change this structural code.</summary>
    public string MessageTerminator { get; set; } = string.Empty;
}

/// <summary>
/// Represents the toolkit's translation choice group model or service.
/// </summary>
public sealed class TranslationChoiceGroup
{
    /// <summary>The zero-based position in the corresponding source collection.</summary>
    public int Index { get; set; }
    /// <summary>The scenario jump-table ID associated with this choice group.</summary>
    public uint JumpId { get; set; }
    /// <summary>The choices value used by this model or operation.</summary>
    public List<TranslationChoice> Choices { get; set; } = [];
}

/// <summary>
/// Represents the toolkit's translation choice model or service.
/// </summary>
public sealed class TranslationChoice
{
    /// <summary>The zero-based position in the corresponding source collection.</summary>
    public int Index { get; set; }
    /// <summary>The immutable decoded choice text used to detect workspace tampering.</summary>
    public string SourceText { get; set; } = string.Empty;
    /// <summary>The replacement choice text; null preserves the original and an empty string clears it.</summary>
    public string? TranslationText { get; set; }
}

/// <summary>
/// Represents immutable workspace export result data exchanged by the toolkit.
/// </summary>
/// <param name="Directory">The directory value used by this model or operation.</param>
/// <param name="ScriptCount">The script count value used by this model or operation.</param>
/// <param name="DialogCount">The dialog count value used by this model or operation.</param>
/// <param name="ChoiceCount">The choice count value used by this model or operation.</param>
public sealed record WorkspaceExportResult(
    string Directory,
    int ScriptCount,
    int DialogCount,
    int ChoiceCount);

/// <summary>
/// Represents immutable workspace script validation result data exchanged by the toolkit.
/// </summary>
/// <param name="Id">The resource identifier preserved across extraction and rebuilding.</param>
/// <param name="SourceBytes">The source bytes value used by this model or operation.</param>
/// <param name="RebuiltBytes">The rebuilt bytes value used by this model or operation.</param>
/// <param name="BytesChanged">The bytes changed value used by this model or operation.</param>
/// <param name="DialogCount">The dialog count value used by this model or operation.</param>
/// <param name="TranslatedSpeakers">The translated speakers value used by this model or operation.</param>
/// <param name="TranslatedMessages">The translated messages value used by this model or operation.</param>
/// <param name="ChoiceCount">The choice count value used by this model or operation.</param>
/// <param name="TranslatedChoices">The translated choices value used by this model or operation.</param>
/// <param name="OldBlocks2K">The old blocks2 k value used by this model or operation.</param>
/// <param name="NewBlocks2K">The new blocks2 k value used by this model or operation.</param>
public sealed record WorkspaceScriptValidationResult(
    ushort Id,
    int SourceBytes,
    int RebuiltBytes,
    bool BytesChanged,
    int DialogCount,
    int TranslatedSpeakers,
    int TranslatedMessages,
    int ChoiceCount,
    int TranslatedChoices,
    int OldBlocks2K,
    int NewBlocks2K);

/// <summary>
/// Represents immutable workspace validation result data exchanged by the toolkit.
/// </summary>
/// <param name="WorkspaceDirectory">The workspace directory value used by this model or operation.</param>
/// <param name="Game">The exact game profile used for interpreting scenario structures.</param>
/// <param name="SourceCpkSha256">The hexadecimal SHA-256 of the original, unmodified scenario archive.</param>
/// <param name="RebuiltCpkSha256">The hexadecimal SHA-256 of the rebuilt archive associated with this plan.</param>
/// <param name="SourceCpkBytes">The source cpk bytes value used by this model or operation.</param>
/// <param name="RebuiltCpkBytes">The rebuilt cpk bytes value used by this model or operation.</param>
/// <param name="ScriptCount">The script count value used by this model or operation.</param>
/// <param name="ChangedScriptCount">The changed script count value used by this model or operation.</param>
/// <param name="DialogCount">The dialog count value used by this model or operation.</param>
/// <param name="TranslatedSpeakers">The translated speakers value used by this model or operation.</param>
/// <param name="TranslatedMessages">The translated messages value used by this model or operation.</param>
/// <param name="ChoiceCount">The choice count value used by this model or operation.</param>
/// <param name="TranslatedChoices">The translated choices value used by this model or operation.</param>
/// <param name="EbootPlanEntryCount">The eboot plan entry count value used by this model or operation.</param>
/// <param name="Scripts">The scripts value used by this model or operation.</param>
/// <param name="Warnings">The warnings value used by this model or operation.</param>
public sealed record WorkspaceValidationResult(
    string WorkspaceDirectory,
    string Game,
    string SourceCpkSha256,
    string RebuiltCpkSha256,
    int SourceCpkBytes,
    int RebuiltCpkBytes,
    int ScriptCount,
    int ChangedScriptCount,
    int DialogCount,
    int TranslatedSpeakers,
    int TranslatedMessages,
    int ChoiceCount,
    int TranslatedChoices,
    int EbootPlanEntryCount,
    IReadOnlyList<WorkspaceScriptValidationResult> Scripts,
    IReadOnlyList<string> Warnings);

/// <summary>
/// Represents immutable workspace build result data exchanged by the toolkit.
/// </summary>
/// <param name="OutputCpk">The output cpk value used by this model or operation.</param>
/// <param name="EbootPlan">The eboot plan value used by this model or operation.</param>
/// <param name="ScriptCount">The script count value used by this model or operation.</param>
/// <param name="ChangedScriptCount">The changed script count value used by this model or operation.</param>
/// <param name="Warnings">The warnings value used by this model or operation.</param>
/// <param name="Validation">The validation value used by this model or operation.</param>
public sealed record WorkspaceBuildResult(
    string OutputCpk,
    string EbootPlan,
    int ScriptCount,
    int ChangedScriptCount,
    IReadOnlyList<string> Warnings,
    WorkspaceValidationResult Validation);
