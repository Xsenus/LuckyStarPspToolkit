using System.Globalization;
using System.Text;
using LuckyStarPspToolkit.Formats.Common;

namespace LuckyStarPspToolkit.Formats.Fonts;

/// <summary>
/// Represents immutable BDF bounding box data exchanged by the toolkit.
/// </summary>
/// <param name="Width">The width value used by this model or operation.</param>
/// <param name="Height">The height value used by this model or operation.</param>
/// <param name="XOffset">The xoffset value used by this model or operation.</param>
/// <param name="YOffset">The yoffset value used by this model or operation.</param>
public sealed record BdfBoundingBox(int Width, int Height, int XOffset, int YOffset);

/// <summary>
/// Represents the toolkit's BDF glyph model or service.
/// </summary>
public sealed class BdfGlyph
{
    /// <summary>The name value used by this model or operation.</summary>
    public required string Name { get; init; }
    /// <summary>The code point value used by this model or operation.</summary>
    public required int CodePoint { get; init; }
    /// <summary>The advance width value used by this model or operation.</summary>
    public required int AdvanceWidth { get; init; }
    /// <summary>The box value used by this model or operation.</summary>
    public required BdfBoundingBox Box { get; init; }
    /// <summary>The pixels value used by this model or operation.</summary>
    public required bool[] Pixels { get; init; }
}

/// <summary>
/// Represents immutable BDF rasterized glyph data exchanged by the toolkit.
/// </summary>
/// <param name="Levels">The levels value used by this model or operation.</param>
/// <param name="AdvanceWidth">The advance width value used by this model or operation.</param>
/// <param name="ClippedPixelCount">The clipped pixel count value used by this model or operation.</param>
public sealed record BdfRasterizedGlyph(byte[] Levels, byte AdvanceWidth, int ClippedPixelCount);

/// <summary>
/// Represents the toolkit's BDF font model or service.
/// </summary>
public sealed class BdfFont
{
    /// <summary>Stores the glyphs state owned by this instance or type.</summary>
    private readonly Dictionary<int, BdfGlyph> _glyphs;

    /// <summary>
    /// Initializes a new instance with validated constructor state.
    /// </summary>
    /// <param name="glyphs">The glyphs value.</param>
    /// <param name="ascent">The ascent value.</param>
    /// <param name="descent">The descent value.</param>
    /// <param name="declaredGlyphCount">The declared glyph count value.</param>
    /// <param name="sourceSha256">The source SHA 256 value.</param>
    private BdfFont(Dictionary<int, BdfGlyph> glyphs, int ascent, int descent, int declaredGlyphCount, string sourceSha256)
    {
        _glyphs = glyphs;
        Ascent = ascent;
        Descent = descent;
        DeclaredGlyphCount = declaredGlyphCount;
        SourceSha256 = sourceSha256;
    }

    /// <summary>The ascent value used by this model or operation.</summary>
    public int Ascent { get; }
    /// <summary>The descent value used by this model or operation.</summary>
    public int Descent { get; }
    /// <summary>The declared glyph count value used by this model or operation.</summary>
    public int DeclaredGlyphCount { get; }
    /// <summary>The encoded glyph count value used by this model or operation.</summary>
    public int EncodedGlyphCount => _glyphs.Count;
    /// <summary>The hexadecimal SHA-256 of the exact input snapshot used by this object.</summary>
    public string SourceSha256 { get; }

    /// <summary>
    /// Loads and validates the requested data from its source.
    /// </summary>
    /// <param name="path">The file-system path to process.</param>
    /// <param name="limits">Optional conservative safety limits; defaults are used when omitted.</param>
    /// <returns>The validated operation result.</returns>
    public static BdfFont Load(string path, FileLimits? limits = null)
    {
        limits ??= FileLimits.Default;
        byte[] data = ReadTextFile(path, limits.MaximumTextBytes);
        string text;
        try
        {
            text = new UTF8Encoding(false, true).GetString(data);
        }
        catch (DecoderFallbackException ex)
        {
            throw new ToolkitException("BDF_UTF8", "BDF file is not valid UTF-8/ASCII.", ex);
        }
        if (text.IndexOf('\0') >= 0)
        {
            throw new ToolkitException("BDF_NUL", "BDF file contains NUL.");
        }
        string[] lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
        return Parse(lines, BinaryUtilities.Sha256Hex(data), limits);
    }

    /// <summary>
    /// Attempts to get glyph.
    /// </summary>
    /// <param name="rune">The rune value.</param>
    /// <param name="glyph">Receives glyph value when the operation succeeds.</param>
    /// <returns><see langword="true"/> when the requested value was produced; otherwise <see langword="false"/>.</returns>
    public bool TryGetGlyph(Rune rune, out BdfGlyph? glyph)
        => _glyphs.TryGetValue(rune.Value, out glyph);

    /// <summary>
    /// Places a BDF bitmap in the fixed LT cell and reports clipping rather than silently truncating a glyph.
    /// </summary>
    /// <param name="rune">The rune value.</param>
    /// <param name="targetWidth">The target width value.</param>
    /// <param name="targetHeight">The target height value.</param>
    /// <param name="baseline">The baseline value.</param>
    /// <param name="intensity">The intensity value.</param>
    /// <param name="xShift">The x shift value.</param>
    /// <param name="yShift">The y shift value.</param>
    /// <returns>The validated operation result.</returns>
    public BdfRasterizedGlyph Rasterize(
        Rune rune,
        int targetWidth = LtFont.GlyphWidth,
        int targetHeight = LtFont.GlyphHeight,
        int baseline = 15,
        int intensity = 3,
        int xShift = 0,
        int yShift = 0)
    {
        if (!_glyphs.TryGetValue(rune.Value, out BdfGlyph? glyph))
        {
            throw new ToolkitException("BDF_GLYPH_MISSING", $"BDF does not contain U+{rune.Value:X4} '{rune}'.");
        }
        if (targetWidth <= 0 || targetHeight <= 0 || targetWidth > 256 || targetHeight > 256)
        {
            throw new ToolkitException("BDF_TARGET_SIZE", $"Invalid raster target {targetWidth}x{targetHeight}.");
        }
        if (baseline < 0 || baseline > targetHeight)
        {
            throw new ToolkitException("BDF_BASELINE", $"Baseline {baseline} must be between 0 and {targetHeight}.");
        }
        if (intensity is < 1 or > 3)
        {
            throw new ToolkitException("BDF_INTENSITY", $"Pixel intensity {intensity} must be between 1 and 3.");
        }

        byte[] levels = new byte[checked(targetWidth * targetHeight)];
        int advance = glyph.AdvanceWidth > 0 ? glyph.AdvanceWidth : glyph.Box.Width;
        int horizontalOrigin = (targetWidth - advance) / 2 + glyph.Box.XOffset + xShift;
        int clipped = 0;
        for (int sourceY = 0; sourceY < glyph.Box.Height; sourceY++)
        {
            int coordinateY = glyph.Box.YOffset + glyph.Box.Height - 1 - sourceY;
            int targetY = baseline - 1 - coordinateY + yShift;
            for (int sourceX = 0; sourceX < glyph.Box.Width; sourceX++)
            {
                if (!glyph.Pixels[sourceY * glyph.Box.Width + sourceX])
                {
                    continue;
                }
                int targetX = horizontalOrigin + sourceX;
                if ((uint)targetX >= (uint)targetWidth || (uint)targetY >= (uint)targetHeight)
                {
                    clipped++;
                    continue;
                }
                levels[targetY * targetWidth + targetX] = checked((byte)intensity);
            }
        }
        int outputAdvance = Math.Clamp(advance, 1, targetWidth);
        return new BdfRasterizedGlyph(levels, checked((byte)outputAdvance), clipped);
    }

    /// <summary>
    /// Parses validated input into the current binary-format model.
    /// </summary>
    /// <param name="lines">The lines value.</param>
    /// <param name="sourceSha256">The source SHA 256 value.</param>
    /// <param name="limits">Optional conservative safety limits; defaults are used when omitted.</param>
    /// <returns>The validated operation result.</returns>
    /// <remarks>Malformed input or violated preconditions are rejected before a persistent output is committed.</remarks>
    private static BdfFont Parse(string[] lines, string sourceSha256, FileLimits limits)
    {
        bool startFont = false;
        bool endFont = false;
        int ascent = -1;
        int descent = -1;
        int declaredChars = -1;
        int glyphBlocks = 0;
        long totalBitmapPixels = 0;
        Dictionary<int, BdfGlyph> glyphs = [];

        for (int index = 0; index < lines.Length; index++)
        {
            if (lines[index].Length > 1_000_000)
            {
                throw new ToolkitException("BDF_LINE_LIMIT", $"BDF line {index + 1} exceeds one million characters.");
            }
            string line = lines[index].Trim();
            if (line.Length == 0 || line.StartsWith("COMMENT", StringComparison.Ordinal))
            {
                continue;
            }
            if (line.StartsWith("STARTFONT ", StringComparison.Ordinal))
            {
                if (startFont || endFont)
                {
                    throw new ToolkitException("BDF_STRUCTURE", "BDF contains more than one STARTFONT record.");
                }
                startFont = true;
                continue;
            }
            if (line == "ENDFONT")
            {
                if (!startFont || endFont)
                {
                    throw new ToolkitException("BDF_STRUCTURE", $"Unexpected ENDFONT at line {index + 1}.");
                }
                endFont = true;
                continue;
            }
            if (endFont)
            {
                throw new ToolkitException("BDF_STRUCTURE", $"Unexpected content after ENDFONT at line {index + 1}.");
            }
            if (line.StartsWith("FONT_ASCENT ", StringComparison.Ordinal))
            {
                if (ascent >= 0)
                {
                    throw new ToolkitException("BDF_METRICS", "BDF contains duplicate FONT_ASCENT properties.");
                }
                ascent = ParseSingleInteger(line, "FONT_ASCENT");
                continue;
            }
            if (line.StartsWith("FONT_DESCENT ", StringComparison.Ordinal))
            {
                if (descent >= 0)
                {
                    throw new ToolkitException("BDF_METRICS", "BDF contains duplicate FONT_DESCENT properties.");
                }
                descent = ParseSingleInteger(line, "FONT_DESCENT");
                continue;
            }
            if (line.StartsWith("CHARS ", StringComparison.Ordinal))
            {
                if (declaredChars >= 0)
                {
                    throw new ToolkitException("BDF_CHAR_COUNT", "BDF contains more than one CHARS record.");
                }
                declaredChars = ParseSingleInteger(line, "CHARS");
                if (declaredChars < 0 || declaredChars > limits.MaximumBdfGlyphs)
                {
                    throw new ToolkitException(
                        "BDF_CHAR_COUNT",
                        $"BDF CHARS value {declaredChars} is outside 0..{limits.MaximumBdfGlyphs}.");
                }
                continue;
            }
            bool isStartChar = line == "STARTCHAR" || line.StartsWith("STARTCHAR ", StringComparison.Ordinal);
            if (!isStartChar)
            {
                continue;
            }
            if (!startFont || declaredChars < 0)
            {
                throw new ToolkitException("BDF_STRUCTURE", $"STARTCHAR appears before STARTFONT/CHARS at line {index + 1}.");
            }

            glyphBlocks++;
            if (glyphBlocks > limits.MaximumBdfGlyphs)
            {
                throw new ToolkitException(
                    "BDF_CHAR_COUNT",
                    $"BDF contains more than {limits.MaximumBdfGlyphs} glyph blocks.");
            }
            string name = line.Length > "STARTCHAR".Length ? line["STARTCHAR".Length..].Trim() : string.Empty;
            int encoding = -1;
            int advance = -1;
            BdfBoundingBox? box = null;
            bool[]? pixels = null;
            bool ended = false;
            bool seenEncoding = false;
            bool seenAdvance = false;
            bool seenBox = false;
            bool seenBitmap = false;

            for (index++; index < lines.Length; index++)
            {
                line = lines[index].Trim();
                if (line.StartsWith("ENCODING ", StringComparison.Ordinal))
                {
                    if (seenEncoding)
                    {
                        throw new ToolkitException("BDF_ENCODING", $"Duplicate ENCODING record at line {index + 1}.");
                    }
                    seenEncoding = true;
                    string[] values = SplitValues(line);
                    if (values.Length is < 2 or > 3)
                    {
                        throw new ToolkitException("BDF_ENCODING", $"Invalid ENCODING record at line {index + 1}.");
                    }
                    encoding = ParseInteger(values[1], "ENCODING", index + 1);
                    if (encoding < 0 && values.Length == 3)
                    {
                        encoding = ParseInteger(values[2], "ENCODING", index + 1);
                    }
                    continue;
                }
                if (line.StartsWith("DWIDTH ", StringComparison.Ordinal))
                {
                    if (seenAdvance)
                    {
                        throw new ToolkitException("BDF_DWIDTH", $"Duplicate DWIDTH record at line {index + 1}.");
                    }
                    seenAdvance = true;
                    string[] values = SplitValues(line);
                    if (values.Length != 3)
                    {
                        throw new ToolkitException("BDF_DWIDTH", $"Invalid DWIDTH record at line {index + 1}.");
                    }
                    advance = ParseInteger(values[1], "DWIDTH", index + 1);
                    _ = ParseInteger(values[2], "DWIDTH", index + 1);
                    if (advance is < 0 or > 256)
                    {
                        throw new ToolkitException("BDF_DWIDTH", $"DWIDTH {advance} at line {index + 1} is outside 0..256.");
                    }
                    continue;
                }
                if (line.StartsWith("BBX ", StringComparison.Ordinal))
                {
                    if (seenBox)
                    {
                        throw new ToolkitException("BDF_BBX", $"Duplicate BBX record at line {index + 1}.");
                    }
                    seenBox = true;
                    string[] values = SplitValues(line);
                    if (values.Length != 5)
                    {
                        throw new ToolkitException("BDF_BBX", $"Invalid BBX record at line {index + 1}.");
                    }
                    box = new BdfBoundingBox(
                        ParseInteger(values[1], "BBX", index + 1),
                        ParseInteger(values[2], "BBX", index + 1),
                        ParseInteger(values[3], "BBX", index + 1),
                        ParseInteger(values[4], "BBX", index + 1));
                    ValidateBox(box, index + 1);
                    continue;
                }
                if (line == "BITMAP")
                {
                    if (seenBitmap)
                    {
                        throw new ToolkitException("BDF_BITMAP", $"Duplicate BITMAP record at line {index + 1}.");
                    }
                    seenBitmap = true;
                    if (box is null)
                    {
                        throw new ToolkitException("BDF_BITMAP", $"BITMAP precedes BBX at line {index + 1}.");
                    }
                    int bitmapPixels = checked(box.Width * box.Height);
                    totalBitmapPixels = checked(totalBitmapPixels + bitmapPixels);
                    if (totalBitmapPixels > limits.MaximumBdfBitmapPixels)
                    {
                        throw new ToolkitException(
                            "BDF_BITMAP_LIMIT",
                            $"BDF bitmap total exceeds {limits.MaximumBdfBitmapPixels} pixels.");
                    }
                    pixels = new bool[bitmapPixels];
                    int bytesPerRow = (box.Width + 7) / 8;
                    for (int row = 0; row < box.Height; row++)
                    {
                        index++;
                        if (index >= lines.Length)
                        {
                            throw new ToolkitException("BDF_EOF", "BDF ended inside a BITMAP block.");
                        }
                        string hex = lines[index].Trim();
                        if (hex.Length != bytesPerRow * 2)
                        {
                            throw new ToolkitException(
                                "BDF_BITMAP_ROW",
                                $"Bitmap row at line {index + 1} has {hex.Length} hex characters; expected {bytesPerRow * 2}.");
                        }
                        byte[] rowData;
                        try
                        {
                            rowData = Convert.FromHexString(hex);
                        }
                        catch (FormatException ex)
                        {
                            throw new ToolkitException("BDF_BITMAP_ROW", $"Bitmap row at line {index + 1} is not hexadecimal.", ex);
                        }
                        for (int x = 0; x < box.Width; x++)
                        {
                            pixels[row * box.Width + x] = (rowData[x / 8] & (0x80 >> (x % 8))) != 0;
                        }
                    }
                    continue;
                }
                if (line == "ENDCHAR")
                {
                    ended = true;
                    break;
                }
                if (line == "STARTCHAR" || line.StartsWith("STARTCHAR ", StringComparison.Ordinal))
                {
                    throw new ToolkitException("BDF_STRUCTURE", $"Nested STARTCHAR at line {index + 1}.");
                }
            }

            if (!ended)
            {
                throw new ToolkitException("BDF_EOF", $"Glyph '{name}' has no ENDCHAR.");
            }
            if (box is null || pixels is null || advance < 0)
            {
                throw new ToolkitException("BDF_GLYPH", $"Glyph '{name}' is missing ENCODING, DWIDTH, BBX, or BITMAP data.");
            }
            if (encoding < 0)
            {
                continue;
            }
            if (!Rune.IsValid(encoding) || encoding is >= 0xD800 and <= 0xDFFF)
            {
                throw new ToolkitException("BDF_ENCODING", $"Glyph '{name}' has invalid Unicode value {encoding}.");
            }
            if (!glyphs.TryAdd(encoding, new BdfGlyph
            {
                Name = name,
                CodePoint = encoding,
                AdvanceWidth = advance,
                Box = box,
                Pixels = pixels
            }))
            {
                throw new ToolkitException("BDF_DUPLICATE", $"BDF contains duplicate encoding U+{encoding:X4}.");
            }
        }

        if (!startFont || !endFont)
        {
            throw new ToolkitException("BDF_STRUCTURE", "BDF STARTFONT/ENDFONT markers are missing.");
        }
        if (declaredChars < 0 || declaredChars != glyphBlocks)
        {
            throw new ToolkitException("BDF_CHAR_COUNT", $"BDF declares {declaredChars} glyphs but contains {glyphBlocks} STARTCHAR blocks.");
        }
        if (ascent < 0 || descent < 0)
        {
            throw new ToolkitException("BDF_METRICS", "BDF FONT_ASCENT and FONT_DESCENT properties are required.");
        }
        return new BdfFont(glyphs, ascent, descent, declaredChars, sourceSha256);
    }

    /// <summary>
    /// Validates box while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="box">The box value.</param>
    /// <param name="line">The line value.</param>
    /// <remarks>Malformed input or violated preconditions are rejected before a persistent output is committed.</remarks>
    private static void ValidateBox(BdfBoundingBox box, int line)
    {
        if (box.Width <= 0 || box.Height <= 0 || box.Width > 64 || box.Height > 64)
        {
            throw new ToolkitException("BDF_BBX", $"BBX at line {line} has unsupported size {box.Width}x{box.Height}.");
        }
        if (box.XOffset is < -128 or > 128 || box.YOffset is < -128 or > 128)
        {
            throw new ToolkitException("BDF_BBX", $"BBX at line {line} has unsupported offsets {box.XOffset},{box.YOffset}.");
        }
    }

    /// <summary>
    /// Tokenizes a BDF directive on whitespace for strict numeric field parsing.
    /// </summary>
    /// <param name="line">The line value.</param>
    /// <returns>The resulting binary or typed sequence.</returns>
    private static string[] SplitValues(string line)
        => line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

    /// <summary>
    /// Parses single integer while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="line">The line value.</param>
    /// <param name="name">The logical name used for lookup or diagnostics.</param>
    /// <returns>The validated operation result.</returns>
    /// <remarks>Malformed input or violated preconditions are rejected before a persistent output is committed.</remarks>
    private static int ParseSingleInteger(string line, string name)
    {
        string[] values = SplitValues(line);
        if (values.Length != 2)
        {
            throw new ToolkitException("BDF_NUMBER", $"Invalid {name} record.");
        }
        return ParseInteger(values[1], name, 0);
    }

    /// <summary>
    /// Parses integer while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="value">The value to process.</param>
    /// <param name="name">The logical name used for lookup or diagnostics.</param>
    /// <param name="line">The line value.</param>
    /// <returns>The validated operation result.</returns>
    /// <remarks>Malformed input or violated preconditions are rejected before a persistent output is committed.</remarks>
    private static int ParseInteger(string value, string name, int line)
    {
        if (!int.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int parsed))
        {
            string location = line > 0 ? $" at line {line}" : string.Empty;
            throw new ToolkitException("BDF_NUMBER", $"Invalid integer '{value}' in {name}{location}.");
        }
        return parsed;
    }

    /// <summary>
    /// Reads text file while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="path">The file-system path to process.</param>
    /// <param name="maximumBytes">The maximum bytes value.</param>
    /// <returns>The resulting binary or typed sequence.</returns>
    private static byte[] ReadTextFile(string path, long maximumBytes)
    {
        FileInfo info = new(path);
        if (!info.Exists)
        {
            throw new ToolkitException("FILE_NOT_FOUND", $"File not found: {path}");
        }
        if (info.Length < 0 || info.Length > maximumBytes || info.Length > int.MaxValue)
        {
            throw new ToolkitException("BDF_LIMIT", $"BDF file size {info.Length} exceeds {maximumBytes} bytes.");
        }
        return File.ReadAllBytes(path);
    }
}
