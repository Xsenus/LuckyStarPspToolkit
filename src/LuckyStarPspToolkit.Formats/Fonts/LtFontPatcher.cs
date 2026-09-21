using System.Buffers;
using System.Text;
using LuckyStarPspToolkit.Formats.Common;
using LuckyStarPspToolkit.Formats.Text;

namespace LuckyStarPspToolkit.Formats.Fonts;

/// <summary>
/// Defines the supported lt font patch selection values.
/// </summary>
public enum LtFontPatchSelection
{
    /// <summary>The russian value used by this model or operation.</summary>
    Russian,
    /// <summary>The cyrillic value used by this model or operation.</summary>
    Cyrillic,
    /// <summary>The all single rune value used by this model or operation.</summary>
    AllSingleRune
}

/// <summary>
/// Represents immutable lt font patch options data exchanged by the toolkit.
/// </summary>
/// <param name="Selection">The selection value used by this model or operation.</param>
/// <param name="Baseline">The baseline value used by this model or operation.</param>
/// <param name="Intensity">The intensity value used by this model or operation.</param>
/// <param name="XShift">The xshift value used by this model or operation.</param>
/// <param name="YShift">The yshift value used by this model or operation.</param>
/// <param name="ReplaceExisting">The replace existing value used by this model or operation.</param>
/// <param name="AllowClipping">The allow clipping value used by this model or operation.</param>
public sealed record LtFontPatchOptions(
    LtFontPatchSelection Selection = LtFontPatchSelection.Russian,
    int Baseline = 15,
    int Intensity = 3,
    int XShift = 0,
    int YShift = 0,
    bool ReplaceExisting = false,
    bool AllowClipping = false);

/// <summary>
/// Represents immutable lt font patch item data exchanged by the toolkit.
/// </summary>
/// <param name="Index">The zero-based position in the corresponding source collection.</param>
/// <param name="Text">The decoded string value associated with this record.</param>
/// <param name="CodePoint">The code point value used by this model or operation.</param>
/// <param name="Status">The status value used by this model or operation.</param>
/// <param name="AdvanceWidth">The advance width value used by this model or operation.</param>
/// <param name="ClippedPixels">The clipped pixels value used by this model or operation.</param>
public sealed record LtFontPatchItem(
    int Index,
    string Text,
    int CodePoint,
    string Status,
    int AdvanceWidth,
    int ClippedPixels);

/// <summary>
/// Represents immutable lt font patch result data exchanged by the toolkit.
/// </summary>
/// <param name="Font">The font value used by this model or operation.</param>
/// <param name="SelectedEntryCount">The selected entry count value used by this model or operation.</param>
/// <param name="PatchedGlyphCount">The patched glyph count value used by this model or operation.</param>
/// <param name="ExistingGlyphCount">The existing glyph count value used by this model or operation.</param>
/// <param name="MissingMapCount">The missing map count value used by this model or operation.</param>
/// <param name="MissingBdfCount">The missing bdf count value used by this model or operation.</param>
/// <param name="ClippedGlyphCount">The clipped glyph count value used by this model or operation.</param>
/// <param name="Complete">The complete value used by this model or operation.</param>
/// <param name="MissingCharacters">The missing characters value used by this model or operation.</param>
/// <param name="Items">The items value used by this model or operation.</param>
/// <param name="Warnings">The warnings value used by this model or operation.</param>
public sealed record LtFontPatchResult(
    LtFont Font,
    int SelectedEntryCount,
    int PatchedGlyphCount,
    int ExistingGlyphCount,
    int MissingMapCount,
    int MissingBdfCount,
    int ClippedGlyphCount,
    bool Complete,
    IReadOnlyList<string> MissingCharacters,
    IReadOnlyList<LtFontPatchItem> Items,
    IReadOnlyList<string> Warnings);

/// <summary>
/// Provides lt font patcher operations with strict bounds and format validation.
/// </summary>
public static class LtFontPatcher
{
    /// <summary>
    /// Rasterizes selected BDF glyphs into a cloned LT font while preserving untouched binary metadata.
    /// </summary>
    /// <param name="source">The source binary data or object.</param>
    /// <param name="map">The glyph map used for text conversion.</param>
    /// <param name="bdf">The bdf value.</param>
    /// <param name="options">Optional operation settings.</param>
    /// <returns>The validated operation result.</returns>
    /// <remarks>Malformed input or violated preconditions are rejected before a persistent output is committed.</remarks>
    public static LtFontPatchResult ApplyBdf(
        LtFont source,
        GlyphMap map,
        BdfFont bdf,
        LtFontPatchOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(bdf);
        options ??= new LtFontPatchOptions();
        if (map.Count != source.Glyphs.Count)
        {
            throw new ToolkitException(
                "FONT_MAP_COUNT",
                $"Glyph map has {map.Count} entries, but font has {source.Glyphs.Count} glyphs.");
        }

        LtFont font = source.Clone();
        List<LtFontPatchItem> items = [];
        HashSet<string> mappedSelected = new(StringComparer.Ordinal);
        HashSet<string> missing = new(StringComparer.Ordinal);
        int patched = 0;
        int existing = 0;
        int missingBdf = 0;
        int clippedGlyphs = 0;

        foreach ((ushort index, string text) in map.EnumerateEncodableEntries())
        {
            if (!TrySingleRune(text, out Rune rune) || !IsSelected(rune, options.Selection))
            {
                continue;
            }
            mappedSelected.Add(text);
            LtGlyph glyph = font.GetGlyph(index);
            bool usableExisting = !glyph.IsBlank && glyph.AdvanceWidth is >= 1 and <= LtFont.GlyphWidth;
            if (usableExisting && !options.ReplaceExisting)
            {
                existing++;
                items.Add(new LtFontPatchItem(index, text, rune.Value, "existing", glyph.AdvanceWidth, 0));
                continue;
            }
            if (!bdf.TryGetGlyph(rune, out _))
            {
                missingBdf++;
                missing.Add(text);
                items.Add(new LtFontPatchItem(index, text, rune.Value, "missing-bdf", glyph.AdvanceWidth, 0));
                continue;
            }

            BdfRasterizedGlyph rasterized = bdf.Rasterize(
                rune,
                LtFont.GlyphWidth,
                LtFont.GlyphHeight,
                options.Baseline,
                options.Intensity,
                options.XShift,
                options.YShift);
            if (rasterized.ClippedPixelCount > 0)
            {
                clippedGlyphs++;
                if (!options.AllowClipping)
                {
                    missing.Add(text);
                    items.Add(new LtFontPatchItem(
                        index,
                        text,
                        rune.Value,
                        "clipped-blocked",
                        rasterized.AdvanceWidth,
                        rasterized.ClippedPixelCount));
                    continue;
                }
            }
            font.ReplaceGlyph(index, rasterized.Levels, rasterized.AdvanceWidth);
            patched++;
            items.Add(new LtFontPatchItem(
                index,
                text,
                rune.Value,
                rasterized.ClippedPixelCount > 0 ? "patched-clipped" : "patched",
                rasterized.AdvanceWidth,
                rasterized.ClippedPixelCount));
        }

        int missingMap = 0;
        int unrenderableFinal = 0;
        if (options.Selection == LtFontPatchSelection.Russian)
        {
            foreach (Rune rune in LtFont.RequiredRussianCharacters.EnumerateRunes())
            {
                string character = rune.ToString();
                if (!mappedSelected.Contains(character) || !map.TryGetLowestIndex(character, out ushort index))
                {
                    missingMap++;
                    missing.Add(character);
                    continue;
                }

                LtGlyph finalGlyph = font.GetGlyph(index);
                if (finalGlyph.IsBlank || finalGlyph.AdvanceWidth is 0 or > LtFont.GlyphWidth)
                {
                    unrenderableFinal++;
                    missing.Add(character);
                }
            }
        }

        List<string> warnings = [];
        if (clippedGlyphs > 0)
        {
            warnings.Add(options.AllowClipping
                ? $"{clippedGlyphs} glyph(s) were clipped while rasterizing."
                : $"{clippedGlyphs} glyph(s) were blocked because rasterization would clip pixels.");
        }
        if (missingBdf > 0)
        {
            warnings.Add($"{missingBdf} selected mapped glyph(s) are missing from the BDF font.");
        }
        if (missingMap > 0)
        {
            warnings.Add($"{missingMap} required Russian character(s) are absent from the glyph map.");
        }
        if (unrenderableFinal > 0)
        {
            warnings.Add($"{unrenderableFinal} required Russian glyph(s) remain blank or have an invalid advance width after patching.");
        }

        return new LtFontPatchResult(
            font,
            items.Count,
            patched,
            existing,
            missingMap,
            missingBdf,
            clippedGlyphs,
            missing.Count == 0,
            missing.OrderBy(static value => value, StringComparer.Ordinal).ToArray(),
            items.OrderBy(static item => item.Index).ToArray(),
            warnings);
    }

    /// <summary>
    /// Parses selection while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="value">The value to process.</param>
    /// <returns>The validated operation result.</returns>
    /// <remarks>Malformed input or violated preconditions are rejected before a persistent output is committed.</remarks>
    public static LtFontPatchSelection ParseSelection(string value) => value.Trim().ToLowerInvariant() switch
    {
        "russian" or "ru" => LtFontPatchSelection.Russian,
        "cyrillic" or "cyr" => LtFontPatchSelection.Cyrillic,
        "all" or "all-single" => LtFontPatchSelection.AllSingleRune,
        _ => throw new ToolkitException("FONT_PATCH_SELECTION", $"Unknown font patch selection '{value}'. Use russian, cyrillic, or all.")
    };

    /// <summary>
    /// Attempts to single rune.
    /// </summary>
    /// <param name="value">The value to process.</param>
    /// <param name="rune">Receives rune value when the operation succeeds.</param>
    /// <returns><see langword="true"/> when the requested value was produced; otherwise <see langword="false"/>.</returns>
    private static bool TrySingleRune(string value, out Rune rune)
    {
        rune = default;
        ReadOnlySpan<char> span = value.AsSpan();
        OperationStatus status = Rune.DecodeFromUtf16(span, out Rune decoded, out int consumed);
        if (status != OperationStatus.Done || consumed != span.Length)
        {
            return false;
        }
        rune = decoded;
        return true;
    }

    /// <summary>
    /// Determines whether selected.
    /// </summary>
    /// <param name="rune">The rune value.</param>
    /// <param name="selection">The selection value.</param>
    /// <returns><see langword="true"/> when the condition is satisfied; otherwise <see langword="false"/>.</returns>
    private static bool IsSelected(Rune rune, LtFontPatchSelection selection)
    {
        if (selection == LtFontPatchSelection.AllSingleRune)
        {
            return true;
        }
        if (selection == LtFontPatchSelection.Russian)
        {
            return LtFont.RequiredRussianCharacters.Contains(rune.ToString(), StringComparison.Ordinal);
        }
        int value = rune.Value;
        return value is >= 0x0400 and <= 0x052F
            or >= 0x2DE0 and <= 0x2DFF
            or >= 0xA640 and <= 0xA69F
            or >= 0x1C80 and <= 0x1C8F;
    }
}
