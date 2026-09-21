namespace LuckyStarPspToolkit.Formats.Scripts;

/// <summary>
/// Defines the supported lucky star game values.
/// </summary>
public enum LuckyStarGame
{
    /// <summary>The ryouou gakuen outousai portable value used by this model or operation.</summary>
    RyououGakuenOutousaiPortable,
    /// <summary>The net idol meister value used by this model or operation.</summary>
    NetIdolMeister
}

/// <summary>
/// Represents immutable script profile data exchanged by the toolkit.
/// </summary>
/// <param name="Game">The exact game profile used for interpreting scenario structures.</param>
/// <param name="JumpTableOffset">The jump table offset value used by this model or operation.</param>
/// <param name="FileAlignment">The file alignment value used by this model or operation.</param>
public sealed record ScriptProfile(LuckyStarGame Game, int JumpTableOffset, int FileAlignment)
{
    /// <summary>The rgo value used by this model or operation.</summary>
    public static ScriptProfile Rgo { get; } = new(LuckyStarGame.RyououGakuenOutousaiPortable, 0x1E080, 0x1000);
    /// <summary>The nim value used by this model or operation.</summary>
    public static ScriptProfile Nim { get; } = new(LuckyStarGame.NetIdolMeister, 0x800, 0x1000);

    /// <summary>
    /// Parses validated input into the current binary-format model.
    /// </summary>
    /// <param name="value">The value to process.</param>
    /// <returns>The validated operation result.</returns>
    /// <remarks>Malformed input or violated preconditions are rejected before a persistent output is committed.</remarks>
    public static ScriptProfile Parse(string value) => value.Trim().ToLowerInvariant() switch
    {
        "rgo" or "ryouou" or "uljm05752" => Rgo,
        "nim" or "net-idol" or "netidol" => Nim,
        _ => throw new ArgumentException($"Unknown game profile '{value}'. Use rgo or nim.", nameof(value))
    };
}

/// <summary>
/// Represents the toolkit's script dialog model or service.
/// </summary>
public sealed class ScriptDialog
{
    /// <summary>The zero-based position in the corresponding source collection.</summary>
    public required int Index { get; init; }
    /// <summary>The relative offset value used by this model or operation.</summary>
    public required uint RelativeOffset { get; init; }
    /// <summary>The resource identifier preserved across extraction and rebuilding.</summary>
    public required ushort Id { get; init; }
    /// <summary>The speaker glyphs value used by this model or operation.</summary>
    public required ushort[] SpeakerGlyphs { get; init; }
    /// <summary>The message glyphs value used by this model or operation.</summary>
    public required ushort[] MessageGlyphs { get; init; }
    /// <summary>The original scenario terminator; translators must not change this structural code.</summary>
    public required ushort MessageTerminator { get; init; }
    /// <summary>The absolute start value used by this model or operation.</summary>
    internal int AbsoluteStart { get; init; } = -1;
    /// <summary>The absolute end value used by this model or operation.</summary>
    internal int AbsoluteEnd { get; init; } = -1;
}

/// <summary>
/// Represents the toolkit's script choice model or service.
/// </summary>
public sealed class ScriptChoice
{
    /// <summary>The group index value used by this model or operation.</summary>
    public required int GroupIndex { get; init; }
    /// <summary>The choice index value used by this model or operation.</summary>
    public required int ChoiceIndex { get; init; }
    /// <summary>The relative offset value used by this model or operation.</summary>
    public required uint RelativeOffset { get; init; }
    /// <summary>The glyphs value used by this model or operation.</summary>
    public required ushort[] Glyphs { get; init; }
    /// <summary>The absolute start value used by this model or operation.</summary>
    internal int AbsoluteStart { get; init; } = -1;
    /// <summary>The absolute end value used by this model or operation.</summary>
    internal int AbsoluteEnd { get; init; } = -1;
}

/// <summary>
/// Represents the toolkit's script choice group model or service.
/// </summary>
public sealed class ScriptChoiceGroup
{
    /// <summary>The zero-based position in the corresponding source collection.</summary>
    public required int Index { get; init; }
    /// <summary>The scenario jump-table ID associated with this choice group.</summary>
    public required uint JumpId { get; init; }
    /// <summary>The choices value used by this model or operation.</summary>
    public List<ScriptChoice> Choices { get; } = [];
}

/// <summary>
/// Represents the toolkit's script mutation model or service.
/// </summary>
public sealed class ScriptMutation
{
    /// <summary>The speaker glyphs value used by this model or operation.</summary>
    public Dictionary<int, ushort[]> SpeakerGlyphs { get; } = [];
    /// <summary>The message glyphs value used by this model or operation.</summary>
    public Dictionary<int, ushort[]> MessageGlyphs { get; } = [];
    /// <summary>Replacement glyph indices keyed by the original group and choice positions; absent keys preserve source text.</summary>
    public Dictionary<(int Group, int Choice), ushort[]> ChoiceGlyphs { get; } = [];
}

/// <summary>
/// Represents immutable script build result data exchanged by the toolkit.
/// </summary>
/// <param name="Data">The byte payload associated with this record.</param>
/// <param name="OldLength">The old length value used by this model or operation.</param>
/// <param name="NewLength">The new length value used by this model or operation.</param>
/// <param name="OldBlocks2K">The old blocks2 k value used by this model or operation.</param>
/// <param name="NewBlocks2K">The new blocks2 k value used by this model or operation.</param>
public sealed record ScriptBuildResult(byte[] Data, int OldLength, int NewLength, int OldBlocks2K, int NewBlocks2K);
