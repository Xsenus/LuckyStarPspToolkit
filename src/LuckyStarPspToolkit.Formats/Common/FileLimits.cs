namespace LuckyStarPspToolkit.Formats.Common;

/// <summary>
/// Represents immutable file limits data exchanged by the toolkit.
/// </summary>
/// <param name="MaximumInputBytes">The upper safety limit for input bytes; input above it is rejected.</param>
/// <param name="MaximumUtfRows">The upper safety limit for utf rows; input above it is rejected.</param>
/// <param name="MaximumUtfColumns">The upper safety limit for utf columns; input above it is rejected.</param>
/// <param name="MaximumCpkEntries">The upper safety limit for cpk entries; input above it is rejected.</param>
/// <param name="MaximumScriptDialogs">The upper safety limit for script dialogs; input above it is rejected.</param>
/// <param name="MaximumScriptChoiceGroups">The upper safety limit for script choice groups; input above it is rejected.</param>
/// <param name="MaximumScriptJumps">The upper safety limit for script jumps; input above it is rejected.</param>
/// <param name="MaximumCrilaylaOutputBytes">The upper safety limit for crilayla output bytes; input above it is rejected.</param>
/// <param name="MaximumImageDimension">The upper safety limit for image dimension; input above it is rejected.</param>
/// <param name="MaximumImagePixels">The upper safety limit for image pixels; input above it is rejected.</param>
/// <param name="MaximumTextBytes">The upper safety limit for text bytes; input above it is rejected.</param>
/// <param name="MaximumGlyphMapEntries">The upper safety limit for glyph map entries; input above it is rejected.</param>
/// <param name="MaximumBdfGlyphs">The upper safety limit for bdf glyphs; input above it is rejected.</param>
/// <param name="MaximumBdfBitmapPixels">The upper safety limit for bdf bitmap pixels; input above it is rejected.</param>
/// <param name="MaximumIsoEntries">The upper safety limit for iso entries; input above it is rejected.</param>
/// <param name="MaximumIsoDirectoryDepth">The upper safety limit for iso directory depth; input above it is rejected.</param>
/// <param name="MaximumIsoExtents">The upper safety limit for iso extents; input above it is rejected.</param>
/// <param name="MaximumIsoDirectoryBytes">The upper safety limit for iso directory bytes; input above it is rejected.</param>
/// <param name="MaximumIsoTotalDirectoryBytes">The upper safety limit for iso total directory bytes; input above it is rejected.</param>
/// <param name="MaximumCollectedAssetBytes">The upper safety limit for collected asset bytes; input above it is rejected.</param>
/// <param name="MaximumIsoReplacements">The upper safety limit for iso replacements; input above it is rejected.</param>
/// <param name="MaximumIsoReplacementBytes">The upper safety limit for iso replacement bytes; input above it is rejected.</param>
/// <param name="MaximumIsoTotalReplacementBytes">The upper safety limit for iso total replacement bytes; input above it is rejected.</param>
/// <param name="MaximumIsoOutputBytes">The upper safety limit for iso output bytes; input above it is rejected.</param>
/// <param name="MaximumGlyphMapCharacters">Maximum UTF-16 units in a decoded glyph map, including separators; bounds trie construction.</param>
/// <param name="MaximumGlyphEntryCharacters">Maximum UTF-16 units in one glyph label or ligature.</param>
/// <param name="MaximumUtfCells">Maximum materialized row/column cells, including implicit-zero values.</param>
/// <param name="MaximumUtfDecodedBytes">Aggregate budget for copied UTF binary payloads and decoded strings in one table.</param>
public sealed record FileLimits(
    long MaximumInputBytes = 4L * 1024 * 1024 * 1024,
    int MaximumUtfRows = 1_000_000,
    int MaximumUtfColumns = 512,
    int MaximumCpkEntries = 100_000,
    int MaximumScriptDialogs = 1_000_000,
    int MaximumScriptChoiceGroups = 100_000,
    int MaximumScriptJumps = 2_000_000,
    int MaximumCrilaylaOutputBytes = 512 * 1024 * 1024,
    int MaximumImageDimension = 16_384,
    long MaximumImagePixels = 64L * 1024 * 1024,
    long MaximumTextBytes = 64L * 1024 * 1024,
    int MaximumGlyphMapEntries = 65_279,
    int MaximumBdfGlyphs = 65_279,
    long MaximumBdfBitmapPixels = 64L * 1024 * 1024,
    int MaximumIsoEntries = 250_000,
    int MaximumIsoDirectoryDepth = 64,
    int MaximumIsoExtents = 1_000_000,
    long MaximumIsoDirectoryBytes = 64L * 1024 * 1024,
    long MaximumIsoTotalDirectoryBytes = 512L * 1024 * 1024,
    long MaximumCollectedAssetBytes = 2L * 1024 * 1024 * 1024,
    int MaximumIsoReplacements = 128,
    long MaximumIsoReplacementBytes = 2L * 1024 * 1024 * 1024,
    long MaximumIsoTotalReplacementBytes = 2L * 1024 * 1024 * 1024,
    long MaximumIsoOutputBytes = 4L * 1024 * 1024 * 1024,
    int MaximumGlyphMapCharacters = 1_000_000,
    int MaximumGlyphEntryCharacters = 128,
    long MaximumUtfCells = 2_000_000,
    long MaximumUtfDecodedBytes = 128L * 1024 * 1024)
{
    /// <summary>The default conservative limits for parsing caller-supplied data.</summary>
    public static FileLimits Default { get; } = new();
}
