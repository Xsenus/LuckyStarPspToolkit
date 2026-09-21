using System.Buffers.Binary;
using System.Text.Json.Serialization;

namespace LuckyStarPspToolkit;

/// <summary>
/// Defines the supported RGO EBOOT patch word state values.
/// </summary>
public enum RgoEbootPatchWordState
{
    /// <summary>The original value used by this model or operation.</summary>
    Original,
    /// <summary>The patched value used by this model or operation.</summary>
    Patched,
    /// <summary>The mismatch value used by this model or operation.</summary>
    Mismatch
}

/// <summary>
/// Represents immutable RGO EBOOT patch definition data exchanged by the toolkit.
/// </summary>
/// <param name="Group">The group value used by this model or operation.</param>
/// <param name="Name">The name value used by this model or operation.</param>
/// <param name="Offset">The offset value used by this model or operation.</param>
/// <param name="Expected">The expected value used by this model or operation.</param>
/// <param name="Replacement">The replacement value used by this model or operation.</param>
public sealed record RgoEbootPatchDefinition(
    string Group,
    string Name,
    int Offset,
    uint Expected,
    uint Replacement);

/// <summary>
/// Represents the toolkit's RGO EBOOT patch word status model or service.
/// </summary>
public sealed class RgoEbootPatchWordStatus
{
    /// <summary>The group value used by this model or operation.</summary>
    public required string Group { get; init; }
    /// <summary>The name value used by this model or operation.</summary>
    public required string Name { get; init; }
    /// <summary>The offset value used by this model or operation.</summary>
    public required int Offset { get; init; }
    /// <summary>The offset hex value used by this model or operation.</summary>
    public required string OffsetHex { get; init; }
    /// <summary>The expected hex value used by this model or operation.</summary>
    public required string ExpectedHex { get; init; }
    /// <summary>The replacement hex value used by this model or operation.</summary>
    public required string ReplacementHex { get; init; }
    /// <summary>The actual hex value used by this model or operation.</summary>
    public required string ActualHex { get; init; }
    /// <summary>The state value used by this model or operation.</summary>
    public required RgoEbootPatchWordState State { get; init; }
}

/// <summary>
/// Represents immutable RGO EBOOT patch group info data exchanged by the toolkit.
/// </summary>
/// <param name="Id">The resource identifier preserved across extraction and rebuilding.</param>
/// <param name="Description">The description value used by this model or operation.</param>
/// <param name="PatchCount">The patch count value used by this model or operation.</param>
public sealed record RgoEbootPatchGroupInfo(
    string Id,
    string Description,
    int PatchCount);

/// <summary>
/// Represents the toolkit's RGO VWF patch inspection model or service.
/// </summary>
public sealed class RgoVwfPatchInspection
{
    /// <summary>The schema value used by this model or operation.</summary>
    public string Schema { get; init; } = "lucky-star-psp.rgo-vwf-inspection.v1";
    /// <summary>The profile id value used by this model or operation.</summary>
    public string ProfileId { get; init; } = RgoVwfPatchProfile.ProfileId;
    /// <summary>The source was encrypted value used by this model or operation.</summary>
    public bool SourceWasEncrypted { get; init; }
    /// <summary>The hexadecimal SHA-256 of the exact input snapshot used by this object.</summary>
    public required string SourceSha256 { get; init; }
    /// <summary>The decrypted sha256 value used by this model or operation.</summary>
    public required string DecryptedSha256 { get; init; }
    /// <summary>The canonical patch only sha256 value used by this model or operation.</summary>
    public required string CanonicalPatchOnlySha256 { get; init; }
    /// <summary>The canonical original sha256 value used by this model or operation.</summary>
    public required string CanonicalOriginalSha256 { get; init; }
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
    /// <summary>The exact known original value used by this model or operation.</summary>
    public bool ExactKnownOriginal { get; init; }
    /// <summary>The exact known full patch value used by this model or operation.</summary>
    public bool ExactKnownFullPatch { get; init; }
    /// <summary>The compatible known lineage value used by this model or operation.</summary>
    public bool CompatibleKnownLineage { get; init; }
    /// <summary>The can apply value used by this model or operation.</summary>
    public bool CanApply { get; init; }
    /// <summary>The already applied value used by this model or operation.</summary>
    public bool AlreadyApplied { get; init; }
    /// <summary>The selected patch count value used by this model or operation.</summary>
    public int SelectedPatchCount { get; init; }
    /// <summary>The original word count value used by this model or operation.</summary>
    public int OriginalWordCount { get; init; }
    /// <summary>The patched word count value used by this model or operation.</summary>
    public int PatchedWordCount { get; init; }
    /// <summary>The mismatch word count value used by this model or operation.</summary>
    public int MismatchWordCount { get; init; }
    /// <summary>The selected groups value used by this model or operation.</summary>
    public required IReadOnlyList<string> SelectedGroups { get; init; }
    /// <summary>The patch points value used by this model or operation.</summary>
    public required IReadOnlyList<RgoEbootPatchWordStatus> PatchPoints { get; init; }
    /// <summary>The message value used by this model or operation.</summary>
    public required string Message { get; init; }

    /// <summary>The decrypted elf value used by this model or operation.</summary>
    [JsonIgnore]
    public required byte[] DecryptedElf { get; init; }
}

/// <summary>
/// Represents the toolkit's RGO VWF patch result model or service.
/// </summary>
public sealed class RgoVwfPatchResult
{
    /// <summary>The schema value used by this model or operation.</summary>
    public string Schema { get; init; } = "lucky-star-psp.rgo-vwf-patch-result.v1";
    /// <summary>The profile id value used by this model or operation.</summary>
    public string ProfileId { get; init; } = RgoVwfPatchProfile.ProfileId;
    /// <summary>The source was encrypted value used by this model or operation.</summary>
    public bool SourceWasEncrypted { get; init; }
    /// <summary>The hexadecimal SHA-256 of the exact input snapshot used by this object.</summary>
    public required string SourceSha256 { get; init; }
    /// <summary>The decrypted input sha256 value used by this model or operation.</summary>
    public required string DecryptedInputSha256 { get; init; }
    /// <summary>The canonical patch only sha256 value used by this model or operation.</summary>
    public required string CanonicalPatchOnlySha256 { get; init; }
    /// <summary>The canonical original sha256 value used by this model or operation.</summary>
    public required string CanonicalOriginalSha256 { get; init; }
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
    /// <summary>The output sha256 value used by this model or operation.</summary>
    public required string OutputSha256 { get; init; }
    /// <summary>The output is decrypted elf value used by this model or operation.</summary>
    public bool OutputIsDecryptedElf { get; init; } = true;
    /// <summary>The selected patch count value used by this model or operation.</summary>
    public int SelectedPatchCount { get; init; }
    /// <summary>The newly applied patch count value used by this model or operation.</summary>
    public int NewlyAppliedPatchCount { get; init; }
    /// <summary>The previously applied patch count value used by this model or operation.</summary>
    public int PreviouslyAppliedPatchCount { get; init; }
    /// <summary>The selected groups value used by this model or operation.</summary>
    public required IReadOnlyList<string> SelectedGroups { get; init; }
    /// <summary>The applied patches value used by this model or operation.</summary>
    public required IReadOnlyList<AppliedBinaryPatch> AppliedPatches { get; init; }

    /// <summary>The decrypted input value used by this model or operation.</summary>
    [JsonIgnore]
    public required byte[] DecryptedInput { get; init; }

    /// <summary>The output value used by this model or operation.</summary>
    [JsonIgnore]
    public required byte[] Output { get; init; }
}

/// <summary>
/// Provides the toolkit's RGO VWF patch profile workflow.
/// </summary>
public static class RgoVwfPatchProfile
{
    /// <summary>The fixed profile id value used by this format or revision.</summary>
    public const string ProfileId = "rgo-uljm05752-russian-vwf-v1";
    /// <summary>The fixed known full patched sha256 value used by this format or revision.</summary>
    public const string KnownFullPatchedSha256 =
        "0a2ce9212c3335e17fad28d457b0caf7f25bc1b046707e0da994ee8437f7244f";

    /// <summary>The known script size table value used by this model or operation.</summary>
    private static readonly byte[] KnownScriptSizeTable = Convert.FromHexString(
        "6C046C046E00DA043E0018053E0056053E0094053E00D2053E0010063E004E063E008C063E00CA063E000807");

    /// <summary>The group order value used by this model or operation.</summary>
    private static readonly string[] GroupOrder =
    [
        "vwf-core",
        "message-layout",
        "speaker-layout",
        "choice-layout",
        "name-layout"
    ];

    /// <summary>The group descriptions value used by this model or operation.</summary>
    private static readonly IReadOnlyDictionary<string, string> GroupDescriptions =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["vwf-core"] = "Variable-width glyph rendering core.",
            ["message-layout"] = "Message box spacing, overlap, positioning, and automatic-newline adjustments.",
            ["speaker-layout"] = "Speaker centering and expanded speaker-name capacity.",
            ["choice-layout"] = "Expanded and left-aligned choice text.",
            ["name-layout"] = "Player-name spacing and file-select name handling."
        };

    /// <summary>The patch definitions value used by this model or operation.</summary>
    private static readonly RgoEbootPatchDefinition[] PatchDefinitions =
    [
        new("vwf-core", "vwf-core-00", 0x00025CD8, 0x00A04825, 0x3C080980),
        new("vwf-core", "vwf-core-01", 0x00025CDC, 0x00802825, 0x8D0977E0),
        new("vwf-core", "vwf-core-02", 0x00025CE0, 0x00063400, 0xAD0577E0),
        new("vwf-core", "vwf-core-03", 0x00025CE4, 0x00072400, 0x00A94823),
        new("vwf-core", "vwf-core-04", 0x00025CE8, 0x00063C03, 0x2D29000A),
        new("vwf-core", "vwf-core-05", 0x00025CEC, 0x00042403, 0x11200004),
        new("vwf-core", "vwf-core-06", 0x00025CF0, 0x310800FF, 0x00000000),
        new("vwf-core", "vwf-core-07", 0x00025CF4, 0x15000055, 0x8D0577E4),
        new("vwf-core", "vwf-core-08", 0x00025CF8, 0x01203025, 0x10000003),
        new("vwf-core", "vwf-core-09", 0x00025CFC, 0x00E74021, 0x00000000),
        new("vwf-core", "vwf-core-10", 0x00025D00, 0x00E83821, 0xAD0577E4),
        new("vwf-core", "vwf-core-11", 0x00025D04, 0x30E7FFFF, 0xA10077E8),
        new("vwf-core", "vwf-core-12", 0x00025D08, 0x00076900, 0x10C00002),
        new("vwf-core", "vwf-core-13", 0x00025D0C, 0x31ADFFFF, 0x00000000),
        new("vwf-core", "vwf-core-14", 0x00025D10, 0x340C0000, 0x34060003),
        new("vwf-core", "vwf-core-15", 0x00025D14, 0x90A90000, 0x808B005A),
        new("vwf-core", "vwf-core-16", 0x00025D18, 0x34030000, 0x00A06021),
        new("vwf-core", "vwf-core-17", 0x00025D1C, 0x31290003, 0x340D0000),
        new("vwf-core", "vwf-core-18", 0x00025D20, 0x90C20000, 0x340E0000),
        new("vwf-core", "vwf-core-19", 0x00025D24, 0x3128FFFF, 0x910977E8),
        new("vwf-core", "vwf-core-20", 0x00025D28, 0x24CA0001, 0x000E7883),
        new("vwf-core", "vwf-core-21", 0x00025D2C, 0x24AB0001, 0x01E47821),
        new("vwf-core", "vwf-core-22", 0x00025D30, 0x01007025, 0x91EF0000),
        new("vwf-core", "vwf-core-23", 0x00025D34, 0x34080000, 0x31D80003),
        new("vwf-core", "vwf-core-24", 0x00025D38, 0x55C00001, 0x13000005),
        new("vwf-core", "vwf-core-25", 0x00025D3C, 0x01274021, 0x00000000),
        new("vwf-core", "vwf-core-26", 0x00025D40, 0x00484025, 0x000F7883),
        new("vwf-core", "vwf-core-27", 0x00025D44, 0xA0C80000, 0x2718FFFF),
        new("vwf-core", "vwf-core-28", 0x00025D48, 0x90A80000, 0x1000FFFB),
        new("vwf-core", "vwf-core-29", 0x00025D4C, 0x90C20000, 0x00000000),
        new("vwf-core", "vwf-core-30", 0x00025D50, 0x3109000C, 0x31EF0003),
        new("vwf-core", "vwf-core-31", 0x00025D54, 0x00094880, 0x11E00002),
        new("vwf-core", "vwf-core-32", 0x00025D58, 0x34080000, 0x00000000),
        new("vwf-core", "vwf-core-33", 0x00025D5C, 0x3129FFFF, 0x01E67821),
        new("vwf-core", "vwf-core-34", 0x00025D60, 0x55200001, 0x11200002),
        new("vwf-core", "vwf-core-35", 0x00025D64, 0x012D4021, 0x00000000),
        new("vwf-core", "vwf-core-36", 0x00025D68, 0x00484025, 0x000F7900),
        new("vwf-core", "vwf-core-37", 0x00025D6C, 0xA0C80000, 0x91990000),
        new("vwf-core", "vwf-core-38", 0x00025D70, 0x90A80000, 0x01F97825),
        new("vwf-core", "vwf-core-39", 0x00025D74, 0x01403025, 0xA18F0000),
        new("vwf-core", "vwf-core-40", 0x00025D78, 0x31090030, 0x11200002),
        new("vwf-core", "vwf-core-41", 0x00025D7C, 0x00094903, 0x00000000),
        new("vwf-core", "vwf-core-42", 0x00025D80, 0x90C20000, 0x258C0001),
        new("vwf-core", "vwf-core-43", 0x00025D84, 0x34080000, 0x39290001),
        new("vwf-core", "vwf-core-44", 0x00025D88, 0x3129FFFF, 0x25CE0001),
        new("vwf-core", "vwf-core-45", 0x00025D8C, 0x55200001, 0x01CB782B),
        new("vwf-core", "vwf-core-46", 0x00025D90, 0x01274021, 0x15E0FFE5),
        new("vwf-core", "vwf-core-47", 0x00025D94, 0x00484025, 0x00000000),
        new("vwf-core", "vwf-core-48", 0x00025D98, 0xA0C80000, 0x340E0000),
        new("vwf-core", "vwf-core-49", 0x00025D9C, 0x90A50000, 0x24840005),
        new("vwf-core", "vwf-core-50", 0x00025DA0, 0x90C20000, 0x00A72821),
        new("vwf-core", "vwf-core-51", 0x00025DA4, 0x30A800C0, 0x24A50009),
        new("vwf-core", "vwf-core-52", 0x00025DA8, 0x00084083, 0x00A06021),
        new("vwf-core", "vwf-core-53", 0x00025DAC, 0x34050000, 0x25AD0001),
        new("vwf-core", "vwf-core-54", 0x00025DB0, 0x3108FFFF, 0x2DAF0012),
        new("vwf-core", "vwf-core-55", 0x00025DB4, 0x55000001, 0x15E0FFDB),
        new("vwf-core", "vwf-core-56", 0x00025DB8, 0x010D2821, 0x00000000),
        new("vwf-core", "vwf-core-57", 0x00025DBC, 0x00452825, 0x910C77E8),
        new("vwf-core", "vwf-core-58", 0x00025DC0, 0xA0C50000, 0x016C5821),
        new("vwf-core", "vwf-core-59", 0x00025DC4, 0x01602825, 0xA10977E8),
        new("vwf-core", "vwf-core-60", 0x00025DC8, 0x90A90000, 0x000B5843),
        new("vwf-core", "vwf-core-61", 0x00025DCC, 0x24C60001, 0x8D0C77E4),
        new("vwf-core", "vwf-core-62", 0x00025DD0, 0x24630001, 0x016C5821),
        new("vwf-core", "vwf-core-63", 0x00025DD4, 0x31290003, 0xAD0B77E4),
        new("vwf-core", "vwf-core-64", 0x00025DD8, 0x286E0004, 0x03E00008),
        new("vwf-core", "vwf-core-65", 0x00025DDC, 0x90C20000, 0x00000000),
        new("message-layout", "speaker-line-spacing", 0x00003D24, 0x24840010, 0x24840012),
        new("message-layout", "message-overlap", 0x00007A9C, 0x2508FFFE, 0x00000000),
        new("message-layout", "message-size-overlap", 0x00007B4C, 0x2665FFFE, 0x00000000),
        new("message-layout", "message-log-overlap", 0x00007C38, 0x2508FFFE, 0x00000000),
        new("message-layout", "file-select-overlap", 0x000366E4, 0x26240010, 0x26240012),
        new("message-layout", "automatic-newline", 0x0003AC84, 0x11400012, 0x10000012),
        new("message-layout", "textbox-position", 0x00004D98, 0x24A5007D, 0x3405007D),
        new("speaker-layout", "speaker-center-a", 0x00003C50, 0x24A50075, 0x24A50073),
        new("speaker-layout", "speaker-center-b", 0x00003C90, 0x00A82821, 0x00000000),
        new("speaker-layout", "speaker-center-c", 0x00003CB4, 0x24A50075, 0x24A50073),
        new("speaker-layout", "speaker-center-code-00", 0x0003A648, 0x00E04025, 0x00064040),
        new("speaker-layout", "speaker-center-code-01", 0x0003A64C, 0x00C04825, 0x02084023),
        new("speaker-layout", "speaker-center-code-02", 0x0003A650, 0x308700FF, 0x340B0000),
        new("speaker-layout", "speaker-center-code-03", 0x0003A654, 0x30A600FF, 0x95090000),
        new("speaker-layout", "speaker-center-code-04", 0x0003A658, 0x310400FF, 0x340A005C),
        new("speaker-layout", "speaker-center-code-05", 0x0003A65C, 0x14800027, 0x012A0018),
        new("speaker-layout", "speaker-center-code-06", 0x0003A660, 0x312500FF, 0x00004812),
        new("speaker-layout", "speaker-center-code-07", 0x0003A664, 0x00C5202A, 0x3C0A097B),
        new("speaker-layout", "speaker-center-code-08", 0x0003A668, 0x1480002F, 0x254A7800),
        new("speaker-layout", "speaker-center-code-09", 0x0003A66C, 0x00000000, 0x012A4821),
        new("speaker-layout", "speaker-center-code-10", 0x0003A670, 0x10A0002D, 0x9129005A),
        new("speaker-layout", "speaker-center-code-11", 0x0003A674, 0x28E40002, 0x01695821),
        new("speaker-layout", "speaker-center-code-12", 0x0003A678, 0x1080000B, 0x25080002),
        new("speaker-layout", "speaker-center-code-13", 0x0003A67C, 0x28E40003, 0x1510FFF5),
        new("speaker-layout", "speaker-center-code-14", 0x0003A680, 0x04E00029, 0x00000000),
        new("speaker-layout", "speaker-center-code-15", 0x0003A684, 0x00000000, 0x34080076),
        new("speaker-layout", "speaker-center-code-16", 0x0003A688, 0x18E00027, 0x010B502A),
        new("speaker-layout", "speaker-center-code-17", 0x0003A68C, 0x00C52023, 0x15400005),
        new("speaker-layout", "speaker-center-code-18", 0x0003A690, 0x00042900, 0x00000000),
        new("speaker-layout", "speaker-center-code-19", 0x0003A694, 0x00852821, 0x010B4023),
        new("speaker-layout", "speaker-center-code-20", 0x0003A698, 0x00852021, 0x00081043),
        new("speaker-layout", "speaker-center-code-21", 0x0003A69C, 0x00041400, 0x03E00008),
        new("speaker-layout", "speaker-center-code-22", 0x0003A6A0, 0x03E00008, 0x00000000),
        new("speaker-layout", "speaker-center-code-23", 0x0003A6A4, 0x00021403, 0x34020000),
        new("speaker-layout", "speaker-center-code-24", 0x0003A6A8, 0x1480000D, 0x03E00008),
        new("speaker-layout", "speaker-center-code-25", 0x0003A6AC, 0x00C52023, 0x00000000),
        new("speaker-layout", "speaker-limit-00", 0x0003A93C, 0x27BDFFD0, 0x27BDFFB0),
        new("speaker-layout", "speaker-limit-01", 0x0003A944, 0xAFB10018, 0xAFB10048),
        new("speaker-layout", "speaker-limit-02", 0x0003A954, 0xAFB00014, 0xAFB00044),
        new("speaker-layout", "speaker-limit-03", 0x0003A958, 0xAFB2001C, 0xAFB2004C),
        new("speaker-layout", "speaker-limit-04", 0x0003A95C, 0xAFBF0020, 0xAFBF0050),
        new("speaker-layout", "speaker-limit-05", 0x0003A98C, 0x8FB00014, 0x8FB00044),
        new("speaker-layout", "speaker-limit-06", 0x0003A990, 0x8FB10018, 0x8FB10048),
        new("speaker-layout", "speaker-limit-07", 0x0003A994, 0x8FB2001C, 0x8FB2004C),
        new("speaker-layout", "speaker-limit-08", 0x0003A998, 0x8FBF0020, 0x8FBF0050),
        new("speaker-layout", "speaker-limit-09", 0x0003A9A0, 0x27BD0030, 0x27BD0050),
        new("speaker-layout", "speaker-limit-10", 0x0003A9B8, 0x8FB00014, 0x8FB00044),
        new("speaker-layout", "speaker-limit-11", 0x0003A9BC, 0x8FB10018, 0x8FB10048),
        new("speaker-layout", "speaker-limit-12", 0x0003A9C0, 0x8FB2001C, 0x8FB2004C),
        new("speaker-layout", "speaker-limit-13", 0x0003A9C4, 0x8FBF0020, 0x8FBF0050),
        new("speaker-layout", "speaker-limit-14", 0x0003A9CC, 0x27BD0030, 0x27BD0050),
        new("speaker-layout", "speaker-limit-15", 0x0003A9D8, 0x34060007, 0x34060017),
        new("speaker-layout", "speaker-limit-16", 0x0003A9F8, 0x8FB00014, 0x8FB00044),
        new("speaker-layout", "speaker-limit-17", 0x0003A9FC, 0x8FB10018, 0x8FB10048),
        new("speaker-layout", "speaker-limit-18", 0x0003AA00, 0x8FB2001C, 0x8FB2004C),
        new("speaker-layout", "speaker-limit-19", 0x0003AA04, 0x8FBF0020, 0x8FBF0050),
        new("speaker-layout", "speaker-limit-20", 0x0003AA0C, 0x27BD0030, 0x27BD0050),
        new("choice-layout", "choice-width-1", 0x00007868, 0x34040012, 0x00000000),
        new("choice-layout", "choice-width-2", 0x0000786C, 0x00932023, 0x34040000),
        new("choice-layout", "choice-width-3", 0x00007940, 0x10800014, 0x00000000),
        new("name-layout", "name-layout-00", 0x000362F4, 0x00A03025, 0x0A20A748),
        new("name-layout", "name-layout-01", 0x000362F8, 0x34050000, 0xA4800000),
        new("name-layout", "name-layout-02", 0x00025DE0, 0x3128FFFF, 0x00A03021),
        new("name-layout", "name-layout-03", 0x00025DE4, 0x24CA0001, 0x34050000),
        new("name-layout", "name-layout-04", 0x00025DE8, 0x15C0FFD1, 0x24840002),
        new("name-layout", "name-layout-05", 0x00025DEC, 0x24AB0001, 0x0A20E88F),
        new("name-layout", "name-layout-06", 0x00025DF0, 0x01001825, 0x26310002),
    ];

    /// <summary>The patches value used by this model or operation.</summary>
    public static IReadOnlyList<RgoEbootPatchDefinition> Patches { get; } =
        Array.AsReadOnly(PatchDefinitions);

    /// <summary>The groups value used by this model or operation.</summary>
    public static IReadOnlyList<RgoEbootPatchGroupInfo> Groups { get; } =
        Array.AsReadOnly(
            GroupOrder.Select(id => new RgoEbootPatchGroupInfo(
                id,
                GroupDescriptions[id],
                PatchDefinitions.Count(patch => string.Equals(
                    patch.Group,
                    id,
                    StringComparison.OrdinalIgnoreCase)))).ToArray());

    /// <summary>
    /// Resolves groups while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="groups">The groups value.</param>
    /// <returns>The validated operation result.</returns>
    public static IReadOnlyList<string> ResolveGroups(IEnumerable<string>? groups)
    {
        if (groups is null)
        {
            return Array.AsReadOnly(GroupOrder.ToArray());
        }

        HashSet<string> requested = new(StringComparer.OrdinalIgnoreCase);
        bool all = false;
        foreach (string? raw in groups)
        {
            if (raw is null)
            {
                continue;
            }
            foreach (string part in raw.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                string normalized = part.ToLowerInvariant();
                if (normalized is "all" or "russian-text")
                {
                    all = true;
                    continue;
                }
                if (normalized is "core" or "core-only")
                {
                    normalized = "vwf-core";
                }
                if (!GroupDescriptions.ContainsKey(normalized))
                {
                    throw new ToolkitException(
                        $"Unknown RGO EBOOT patch group '{part}'. Known groups: {string.Join(", ", GroupOrder)}.");
                }
                requested.Add(normalized);
            }
        }

        if (all || requested.Count == 0)
        {
            return Array.AsReadOnly(GroupOrder.ToArray());
        }

        return Array.AsReadOnly(GroupOrder.Where(requested.Contains).ToArray());
    }

    /// <summary>
    /// Inspects the supplied input and returns a structured diagnostic result.
    /// </summary>
    /// <param name="source">The source binary data or object.</param>
    /// <param name="groups">The groups value.</param>
    /// <returns>The validated operation result.</returns>
    public static RgoVwfPatchInspection Inspect(
        ReadOnlySpan<byte> source,
        IEnumerable<string>? groups = null)
    {
        IReadOnlyList<string> selectedGroups = ResolveGroups(groups);
        PreparedExecutable prepared = PrepareExecutable(source);
        HashSet<string> selected = selectedGroups.ToHashSet(StringComparer.OrdinalIgnoreCase);

        byte[] canonical = prepared.DecryptedElf.ToArray();
        var allStates = new List<RgoEbootPatchWordStatus>(PatchDefinitions.Length);
        bool recognizedWords = true;
        foreach (RgoEbootPatchDefinition patch in PatchDefinitions)
        {
            Guard.RequireRange(canonical.Length, patch.Offset, sizeof(uint), $"RGO VWF patch '{patch.Name}'");
            uint actual = BinaryPrimitives.ReadUInt32LittleEndian(canonical.AsSpan(patch.Offset, sizeof(uint)));
            RgoEbootPatchWordState state;
            if (actual == patch.Expected)
            {
                state = RgoEbootPatchWordState.Original;
            }
            else if (actual == patch.Replacement)
            {
                state = RgoEbootPatchWordState.Patched;
                BinaryPrimitives.WriteUInt32LittleEndian(
                    canonical.AsSpan(patch.Offset, sizeof(uint)),
                    patch.Expected);
            }
            else
            {
                state = RgoEbootPatchWordState.Mismatch;
                recognizedWords = false;
            }

            if (selected.Contains(patch.Group))
            {
                allStates.Add(new RgoEbootPatchWordStatus
                {
                    Group = patch.Group,
                    Name = patch.Name,
                    Offset = patch.Offset,
                    OffsetHex = HexUtilities.Offset(patch.Offset),
                    ExpectedHex = $"0x{patch.Expected:X8}",
                    ReplacementHex = $"0x{patch.Replacement:X8}",
                    ActualHex = $"0x{actual:X8}",
                    State = state
                });
            }
        }

        string canonicalPatchOnlyHash = CryptoUtilities.Sha256Hex(canonical);
        bool scriptTableConsistent = IsScriptSizeTableConsistent(canonical);
        Guard.RequireRange(
            canonical.Length,
            RgoProfile.ScriptSizeTableOffset,
            KnownScriptSizeTable.Length,
            "RGO known script size table");
        bool scriptTableModified = !canonical.AsSpan(
            RgoProfile.ScriptSizeTableOffset,
            KnownScriptSizeTable.Length).SequenceEqual(KnownScriptSizeTable);
        ScriptHeapState scriptHeap = ReadScriptHeap(canonical);
        int requiredScriptHeapBytes = GetRequiredScriptHeapBytes(canonical);
        bool scriptHeapConsistent = scriptHeap.Valid
            && scriptHeap.Bytes == requiredScriptHeapBytes;
        bool scriptHeapModified = !scriptHeap.Valid
            || scriptHeap.LuiInstruction != RgoProfile.KnownScriptHeapLuiInstruction
            || scriptHeap.AddiuInstruction != RgoProfile.KnownScriptHeapAddiuInstruction;
        byte[] canonicalOriginal = canonical.ToArray();
        KnownScriptSizeTable.CopyTo(canonicalOriginal, RgoProfile.ScriptSizeTableOffset);
        BinaryPrimitives.WriteUInt32LittleEndian(
            canonicalOriginal.AsSpan(RgoProfile.ScriptHeapLuiOffset, sizeof(uint)),
            RgoProfile.KnownScriptHeapLuiInstruction);
        BinaryPrimitives.WriteUInt32LittleEndian(
            canonicalOriginal.AsSpan(RgoProfile.ScriptHeapAddiuOffset, sizeof(uint)),
            RgoProfile.KnownScriptHeapAddiuInstruction);
        string canonicalHash = CryptoUtilities.Sha256Hex(canonicalOriginal);
        string decryptedHash = CryptoUtilities.Sha256Hex(prepared.DecryptedElf);
        bool compatibleLineage = recognizedWords
            && scriptTableConsistent
            && scriptHeapConsistent
            && string.Equals(
                canonicalHash,
                RgoProfile.KnownDecryptedSha256,
                StringComparison.OrdinalIgnoreCase);
        bool exactOriginal = string.Equals(
            decryptedHash,
            RgoProfile.KnownDecryptedSha256,
            StringComparison.OrdinalIgnoreCase);
        bool exactFullPatch = string.Equals(
            decryptedHash,
            KnownFullPatchedSha256,
            StringComparison.OrdinalIgnoreCase);
        int originalCount = allStates.Count(point => point.State == RgoEbootPatchWordState.Original);
        int patchedCount = allStates.Count(point => point.State == RgoEbootPatchWordState.Patched);
        int mismatchCount = allStates.Count(point => point.State == RgoEbootPatchWordState.Mismatch);
        bool alreadyApplied = compatibleLineage && mismatchCount == 0 && originalCount == 0;
        bool canApply = compatibleLineage && mismatchCount == 0;

        string message = BuildInspectionMessage(
            recognizedWords,
            scriptTableConsistent,
            scriptHeap,
            scriptHeapConsistent,
            requiredScriptHeapBytes,
            compatibleLineage,
            alreadyApplied,
            patchedCount);

        return new RgoVwfPatchInspection
        {
            SourceWasEncrypted = prepared.SourceWasEncrypted,
            SourceSha256 = prepared.SourceSha256,
            DecryptedSha256 = decryptedHash,
            CanonicalPatchOnlySha256 = canonicalPatchOnlyHash,
            CanonicalOriginalSha256 = canonicalHash,
            ScriptSizeTableModified = scriptTableModified,
            ScriptSizeTableConsistent = scriptTableConsistent,
            ScriptHeapModified = scriptHeapModified,
            ScriptHeapConsistent = scriptHeapConsistent,
            ScriptHeapBytes = scriptHeap.Bytes,
            RequiredScriptHeapBytes = requiredScriptHeapBytes,
            ExactKnownOriginal = exactOriginal,
            ExactKnownFullPatch = exactFullPatch,
            CompatibleKnownLineage = compatibleLineage,
            CanApply = canApply,
            AlreadyApplied = alreadyApplied,
            SelectedPatchCount = allStates.Count,
            OriginalWordCount = originalCount,
            PatchedWordCount = patchedCount,
            MismatchWordCount = mismatchCount,
            SelectedGroups = selectedGroups,
            PatchPoints = allStates.AsReadOnly(),
            Message = message,
            DecryptedElf = prepared.DecryptedElf
        };
    }

    /// <summary>
    /// Applies the requested transformation after validating all preconditions.
    /// </summary>
    /// <param name="source">The source binary data or object.</param>
    /// <param name="groups">The groups value.</param>
    /// <returns>The validated operation result.</returns>
    /// <remarks>Malformed input or violated preconditions are rejected before a persistent output is committed.</remarks>
    public static RgoVwfPatchResult Apply(
        ReadOnlySpan<byte> source,
        IEnumerable<string>? groups = null)
    {
        RgoVwfPatchInspection inspection = Inspect(source, groups);
        Guard.Require(
            inspection.CompatibleKnownLineage,
            $"Refusing to patch EBOOT: {inspection.Message}");
        Guard.Require(
            inspection.MismatchWordCount == 0,
            "Refusing to patch EBOOT with mismatched patch words.");

        HashSet<string> selected = inspection.SelectedGroups.ToHashSet(StringComparer.OrdinalIgnoreCase);
        List<RgoEbootPatchDefinition> selectedDefinitions = PatchDefinitions
            .Where(patch => selected.Contains(patch.Group))
            .OrderBy(patch => patch.Offset)
            .ToList();
        List<BinaryPatch> pending = [];
        int previouslyApplied = 0;
        foreach (RgoEbootPatchDefinition patch in selectedDefinitions)
        {
            uint actual = BinaryPrimitives.ReadUInt32LittleEndian(
                inspection.DecryptedElf.AsSpan(patch.Offset, sizeof(uint)));
            if (actual == patch.Replacement)
            {
                previouslyApplied++;
                continue;
            }
            Guard.Require(
                actual == patch.Expected,
                $"RGO VWF patch '{patch.Name}' has an unexpected value at {HexUtilities.Offset(patch.Offset)}.");
            pending.Add(new BinaryPatch
            {
                Name = $"{patch.Group}/{patch.Name}",
                Offset = patch.Offset,
                ExpectedHex = ToLittleEndianHex(patch.Expected),
                ReplacementHex = ToLittleEndianHex(patch.Replacement)
            });
        }

        byte[] output;
        IReadOnlyList<AppliedBinaryPatch> applied;
        if (pending.Count == 0)
        {
            output = inspection.DecryptedElf.ToArray();
            applied = Array.Empty<AppliedBinaryPatch>();
        }
        else
        {
            BinaryPatchResult result = BinaryPatchEngine.Apply(inspection.DecryptedElf, pending);
            output = result.Output;
            applied = result.AppliedPatches;
        }

        VerifySelectedWords(output, selectedDefinitions);
        bool allGroupsSelected = inspection.SelectedGroups.Count == GroupOrder.Length
            && GroupOrder.All(group => inspection.SelectedGroups.Contains(group, StringComparer.OrdinalIgnoreCase));
        string outputHash = CryptoUtilities.Sha256Hex(output);
        if (allGroupsSelected
            && !inspection.ScriptSizeTableModified
            && !inspection.ScriptHeapModified)
        {
            Guard.Require(
                string.Equals(outputHash, KnownFullPatchedSha256, StringComparison.OrdinalIgnoreCase),
                $"Full RGO VWF output SHA-256 mismatch: expected {KnownFullPatchedSha256}, got {outputHash}.");
        }

        return new RgoVwfPatchResult
        {
            SourceWasEncrypted = inspection.SourceWasEncrypted,
            SourceSha256 = inspection.SourceSha256,
            DecryptedInputSha256 = inspection.DecryptedSha256,
            CanonicalPatchOnlySha256 = inspection.CanonicalPatchOnlySha256,
            CanonicalOriginalSha256 = inspection.CanonicalOriginalSha256,
            ScriptSizeTableModified = inspection.ScriptSizeTableModified,
            ScriptSizeTableConsistent = inspection.ScriptSizeTableConsistent,
            ScriptHeapModified = inspection.ScriptHeapModified,
            ScriptHeapConsistent = inspection.ScriptHeapConsistent,
            ScriptHeapBytes = inspection.ScriptHeapBytes,
            RequiredScriptHeapBytes = inspection.RequiredScriptHeapBytes,
            OutputSha256 = outputHash,
            SelectedPatchCount = selectedDefinitions.Count,
            NewlyAppliedPatchCount = pending.Count,
            PreviouslyAppliedPatchCount = previouslyApplied,
            SelectedGroups = inspection.SelectedGroups,
            AppliedPatches = applied,
            DecryptedInput = inspection.DecryptedElf.ToArray(),
            Output = output
        };
    }


    /// <summary>
    /// Builds inspection message while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="recognizedWords">The recognized words value.</param>
    /// <param name="scriptTableConsistent">The script table consistent value.</param>
    /// <param name="scriptHeap">The script heap value.</param>
    /// <param name="scriptHeapConsistent">The script heap consistent value.</param>
    /// <param name="requiredScriptHeapBytes">The required script heap bytes value.</param>
    /// <param name="compatibleLineage">The compatible lineage value.</param>
    /// <param name="alreadyApplied">The already applied value.</param>
    /// <param name="patchedCount">The patched count value.</param>
    /// <returns>The resulting text, path, identifier, or hexadecimal digest.</returns>
    /// <remarks>Malformed input or violated preconditions are rejected before a persistent output is committed.</remarks>
    private static string BuildInspectionMessage(
        bool recognizedWords,
        bool scriptTableConsistent,
        ScriptHeapState scriptHeap,
        bool scriptHeapConsistent,
        int requiredScriptHeapBytes,
        bool compatibleLineage,
        bool alreadyApplied,
        int patchedCount)
    {
        if (!recognizedWords)
        {
            return "One or more patch words match neither the verified original nor the known replacement.";
        }
        if (!scriptTableConsistent)
        {
            return "The RGO script-size table is internally inconsistent.";
        }
        if (!scriptHeap.Valid)
        {
            return "The RGO script-heap MIPS instructions do not use the verified lui/addiu register pair.";
        }
        if (!scriptHeapConsistent)
        {
            return $"The RGO script heap is {scriptHeap.Bytes} bytes, but the current script table requires exactly {requiredScriptHeapBytes} bytes.";
        }
        if (!compatibleLineage)
        {
            return "Reverting all recognized patch words and dynamic metadata does not reproduce the verified ULJM05752 EBOOT.";
        }
        if (alreadyApplied)
        {
            return "All selected patch groups are already applied.";
        }
        return patchedCount > 0
            ? "The EBOOT has a recognized partial patch state and can be completed safely."
            : "Verified original ULJM05752 EBOOT is ready for the selected patch groups.";
    }

    /// <summary>
    /// Determines whether script size table consistent.
    /// </summary>
    /// <param name="source">The source binary data or object.</param>
    /// <returns><see langword="true"/> when the condition is satisfied; otherwise <see langword="false"/>.</returns>
    private static bool IsScriptSizeTableConsistent(ReadOnlySpan<byte> source)
    {
        int count = RgoProfile.ScriptLastId - RgoProfile.ScriptFirstId + 1;
        int length = checked(count * 4);
        Guard.RequireRange(source.Length, RgoProfile.ScriptSizeTableOffset, length, "RGO script size table");
        int running = 0;
        for (int index = 0; index < count; index++)
        {
            int offset = checked(RgoProfile.ScriptSizeTableOffset + index * 4);
            ushort blocks = BinaryPrimitives.ReadUInt16LittleEndian(source.Slice(offset, 2));
            ushort cumulative = BinaryPrimitives.ReadUInt16LittleEndian(source.Slice(offset + 2, 2));
            if (blocks == 0)
            {
                return false;
            }
            running = checked(running + blocks);
            if (running > ushort.MaxValue || cumulative != running)
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>
    /// Gets required script heap bytes while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="source">The source binary data or object.</param>
    /// <returns>The validated operation result.</returns>
    private static int GetRequiredScriptHeapBytes(ReadOnlySpan<byte> source)
    {
        int count = RgoProfile.ScriptLastId - RgoProfile.ScriptFirstId + 1;
        int largestBlocks = 0;
        for (int index = 0; index < count; index++)
        {
            int offset = checked(RgoProfile.ScriptSizeTableOffset + index * 4);
            ushort blocks = BinaryPrimitives.ReadUInt16LittleEndian(source.Slice(offset, 2));
            largestBlocks = Math.Max(largestBlocks, blocks);
        }
        int largestScriptBytes = checked(largestBlocks * (int)RgoProfile.ArchiveBlockSize);
        return Math.Max(RgoProfile.KnownScriptHeapBytes, largestScriptBytes);
    }

    /// <summary>
    /// Reads script heap while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="source">The source binary data or object.</param>
    /// <returns>The validated operation result.</returns>
    private static ScriptHeapState ReadScriptHeap(ReadOnlySpan<byte> source)
    {
        Guard.RequireRange(
            source.Length,
            RgoProfile.ScriptHeapLuiOffset,
            sizeof(uint),
            "RGO script heap lui");
        Guard.RequireRange(
            source.Length,
            RgoProfile.ScriptHeapAddiuOffset,
            sizeof(uint),
            "RGO script heap addiu");
        uint lui = BinaryPrimitives.ReadUInt32LittleEndian(
            source.Slice(RgoProfile.ScriptHeapLuiOffset, sizeof(uint)));
        uint addiu = BinaryPrimitives.ReadUInt32LittleEndian(
            source.Slice(RgoProfile.ScriptHeapAddiuOffset, sizeof(uint)));
        bool valid = (lui & 0xFFFF0000U) == 0x3C040000U
            && (addiu & 0xFFFF0000U) == 0x24840000U;
        if (!valid)
        {
            return new ScriptHeapState(false, 0, lui, addiu);
        }

        int high = checked((int)(lui & 0xFFFFU));
        int low = unchecked((short)(addiu & 0xFFFFU));
        long decoded = ((long)high << 16) + low;
        if (decoded <= 0 || decoded > int.MaxValue)
        {
            return new ScriptHeapState(false, 0, lui, addiu);
        }
        return new ScriptHeapState(true, (int)decoded, lui, addiu);
    }

    /// <summary>
    /// Verifies selected words while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="output">The destination stream, buffer, or model.</param>
    /// <param name="selected">The selected value.</param>
    /// <remarks>Malformed input or violated preconditions are rejected before a persistent output is committed.</remarks>
    private static void VerifySelectedWords(
        ReadOnlySpan<byte> output,
        IReadOnlyCollection<RgoEbootPatchDefinition> selected)
    {
        foreach (RgoEbootPatchDefinition patch in selected)
        {
            Guard.RequireRange(output.Length, patch.Offset, sizeof(uint), $"RGO VWF verify '{patch.Name}'");
            uint actual = BinaryPrimitives.ReadUInt32LittleEndian(output.Slice(patch.Offset, sizeof(uint)));
            Guard.Require(
                actual == patch.Replacement,
                $"RGO VWF output verification failed for '{patch.Name}' at {HexUtilities.Offset(patch.Offset)}.");
        }
    }

    /// <summary>
    /// Prepares executable while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="source">The source binary data or object.</param>
    /// <returns>The validated operation result.</returns>
    private static PreparedExecutable PrepareExecutable(ReadOnlySpan<byte> source)
    {
        bool encrypted = PspPrxReader.LooksLike(source);
        string sourceHash = CryptoUtilities.Sha256Hex(source);
        byte[] plaintext;
        if (encrypted)
        {
            Guard.Require(
                string.Equals(sourceHash, RgoProfile.KnownEncryptedSha256, StringComparison.OrdinalIgnoreCase),
                $"Encrypted EBOOT SHA-256 mismatch: expected {RgoProfile.KnownEncryptedSha256}, got {sourceHash}.");
            plaintext = PspPrxReader.DecryptVerified(source).Elf;
        }
        else
        {
            plaintext = source.ToArray();
        }

        Guard.Require(ElfReader.LooksLike(plaintext), "RGO VWF patch input is not an ELF32 executable.");
        Elf32Info elf = ElfReader.Parse32LittleEndian(plaintext);
        Guard.Require(elf.IsPspMips, "RGO VWF patch input is not a PSP MIPS ELF.");
        Guard.Require(
            plaintext.Length == RgoProfile.KnownDecryptedSize,
            $"RGO VWF patch input size mismatch: expected {RgoProfile.KnownDecryptedSize}, got {plaintext.Length}.");
        return new PreparedExecutable(encrypted, sourceHash, plaintext);
    }

    /// <summary>
    /// Converts little endian hex while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="value">The value to process.</param>
    /// <returns>The resulting text, path, identifier, or hexadecimal digest.</returns>
    private static string ToLittleEndianHex(uint value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
        return Convert.ToHexString(bytes);
    }

    /// <summary>
    /// Represents immutable prepared executable data exchanged by the toolkit.
    /// </summary>
    /// <param name="SourceWasEncrypted">The source was encrypted value used by this model or operation.</param>
    /// <param name="SourceSha256">The hexadecimal SHA-256 of the exact input snapshot used by this object.</param>
    /// <param name="DecryptedElf">The decrypted elf value used by this model or operation.</param>
    private sealed record PreparedExecutable(
        bool SourceWasEncrypted,
        string SourceSha256,
        byte[] DecryptedElf);

    /// <summary>
    /// Represents immutable script heap state data exchanged by the toolkit.
    /// </summary>
    /// <param name="Valid">The valid value used by this model or operation.</param>
    /// <param name="Bytes">The bytes value used by this model or operation.</param>
    /// <param name="LuiInstruction">The lui instruction value used by this model or operation.</param>
    /// <param name="AddiuInstruction">The addiu instruction value used by this model or operation.</param>
    private sealed record ScriptHeapState(
        bool Valid,
        int Bytes,
        uint LuiInstruction,
        uint AddiuInstruction);
}
