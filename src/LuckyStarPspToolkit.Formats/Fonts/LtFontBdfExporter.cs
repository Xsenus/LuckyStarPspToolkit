using System.Globalization;
using System.Text;
using LuckyStarPspToolkit.Formats.Common;
using LuckyStarPspToolkit.Formats.Text;

namespace LuckyStarPspToolkit.Formats.Fonts;

/// <summary>Creates an editable monochrome BDF template from the mapped Russian LT bitmaps.</summary>
public static class LtFontBdfExporter
{
    /// <summary>Exports 66 existing letters in their original cell positions, using a fixed 18-pixel advance.</summary>
    /// <param name="font">The decoded LT font, left unchanged.</param>
    /// <param name="map">The matching character map, left unchanged.</param>
    /// <returns>A BDF template with baseline 15; nonzero LT intensities become monochrome ink.</returns>
    public static string ExportRussian(LtFont font, GlyphMap map)
    {
        if (!font.Analyze(map).RussianBitmapsComplete)
        {
            throw new ToolkitException("FONT_RUSSIAN_BITMAPS", "A complete set of mapped Russian bitmap letters is required for BDF export.");
        }
        StringBuilder text = new();
        text.AppendLine("STARTFONT 2.1");
        text.AppendLine("COMMENT Generated editable template; nonzero LT pixels become monochrome ink.");
        text.AppendLine("COMMENT Import with baseline 15; this template uses fixed 18-pixel advances.");
        text.AppendLine("FONT -lsptool-template-medium-r-normal--18-180-75-75-c-180-iso10646-1");
        text.AppendLine("SIZE 18 75 75");
        text.AppendLine("FONTBOUNDINGBOX 18 18 0 -3");
        text.AppendLine("STARTPROPERTIES 2");
        text.AppendLine("FONT_ASCENT 15");
        text.AppendLine("FONT_DESCENT 3");
        text.AppendLine("ENDPROPERTIES");
        text.AppendLine("CHARS 66");
        foreach (Rune rune in LtFont.RequiredRussianCharacters.EnumerateRunes())
        {
            _ = map.TryGetLowestIndex(rune.ToString(), out ushort index);
            LtGlyph glyph = font.GetGlyph(index);
            text.AppendLine("STARTCHAR U" + rune.Value.ToString("X4", CultureInfo.InvariantCulture));
            text.AppendLine("ENCODING " + rune.Value.ToString(CultureInfo.InvariantCulture));
            text.AppendLine("SWIDTH 1000 0");
            text.AppendLine("DWIDTH 18 0");
            text.AppendLine("BBX 18 18 0 -3");
            text.AppendLine("BITMAP");
            for (int y = 0; y < LtFont.GlyphHeight; y++)
            {
                uint row = 0;
                for (int x = 0; x < LtFont.GlyphWidth; x++)
                {
                    if (glyph.Levels[y * LtFont.GlyphWidth + x] != 0)
                    {
                        row |= 1u << (23 - x);
                    }
                }
                text.AppendLine(row.ToString("X6", CultureInfo.InvariantCulture));
            }
            text.AppendLine("ENDCHAR");
        }
        text.AppendLine("ENDFONT");
        return text.ToString();
    }
}
