using System.Text;
using LuckyStarPspToolkit.Formats.Common;
using LuckyStarPspToolkit.Formats.Scripts;
using LuckyStarPspToolkit.Formats.Text;

namespace LuckyStarPspToolkit.Formats.Fonts;

/// <summary>
/// Represents immutable glyph ink bounds data exchanged by the toolkit.
/// </summary>
/// <param name="X">The x value used by this model or operation.</param>
/// <param name="Y">The y value used by this model or operation.</param>
/// <param name="Width">The width value used by this model or operation.</param>
/// <param name="Height">The height value used by this model or operation.</param>
public sealed record GlyphInkBounds(int X, int Y, int Width, int Height);

/// <summary>
/// Represents the toolkit's lt glyph model or service.
/// </summary>
public sealed class LtGlyph
{
    /// <summary>The zero-based position in the corresponding source collection.</summary>
    public required int Index { get; init; }
    /// <summary>The levels value used by this model or operation.</summary>
    public required byte[] Levels { get; set; }
    /// <summary>The unused pixel bits value used by this model or operation.</summary>
    public required byte[] UnusedPixelBits { get; init; }
    /// <summary>The advance width value used by this model or operation.</summary>
    public required byte AdvanceWidth { get; set; }
    /// <summary>The reserved value used by this model or operation.</summary>
    public byte Reserved { get; set; }

    /// <summary>The is blank value used by this model or operation.</summary>
    public bool IsBlank => !Levels.Any(static level => level != 0);

    /// <summary>
    /// Gets ink bounds while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <returns>The validated operation result.</returns>
    public GlyphInkBounds? GetInkBounds()
    {
        int minimumX = LtFont.GlyphWidth;
        int minimumY = LtFont.GlyphHeight;
        int maximumX = -1;
        int maximumY = -1;
        for (int y = 0; y < LtFont.GlyphHeight; y++)
        {
            for (int x = 0; x < LtFont.GlyphWidth; x++)
            {
                if (Levels[y * LtFont.GlyphWidth + x] == 0)
                {
                    continue;
                }
                minimumX = Math.Min(minimumX, x);
                minimumY = Math.Min(minimumY, y);
                maximumX = Math.Max(maximumX, x);
                maximumY = Math.Max(maximumY, y);
            }
        }
        return maximumX < 0
            ? null
            : new GlyphInkBounds(minimumX, minimumY, maximumX - minimumX + 1, maximumY - minimumY + 1);
    }

    /// <summary>
    /// Creates a deep copy whose mutable buffers are independent of the source instance.
    /// </summary>
    /// <returns>The validated operation result.</returns>
    public LtGlyph Clone() => new()
    {
        Index = Index,
        Levels = Levels.ToArray(),
        UnusedPixelBits = UnusedPixelBits.ToArray(),
        AdvanceWidth = AdvanceWidth,
        Reserved = Reserved
    };
}

/// <summary>
/// Represents immutable lt font russian glyph status data exchanged by the toolkit.
/// </summary>
/// <param name="Character">The character value used by this model or operation.</param>
/// <param name="CodePoint">The code point value used by this model or operation.</param>
/// <param name="Index">The zero-based position in the corresponding source collection.</param>
/// <param name="Mapped">The mapped value used by this model or operation.</param>
/// <param name="NonBlank">The non blank value used by this model or operation.</param>
/// <param name="AdvanceWidth">The advance width value used by this model or operation.</param>
public sealed record LtFontRussianGlyphStatus(
    string Character,
    int CodePoint,
    int? Index,
    bool Mapped,
    bool NonBlank,
    byte? AdvanceWidth);

/// <summary>
/// Represents immutable lt font analysis data exchanged by the toolkit.
/// </summary>
/// <param name="FileSize">The file size value used by this model or operation.</param>
/// <param name="SourceSha256">The hexadecimal SHA-256 of the exact input snapshot used by this object.</param>
/// <param name="GlyphCount">The glyph count value used by this model or operation.</param>
/// <param name="BlankGlyphCount">The blank glyph count value used by this model or operation.</param>
/// <param name="NonBlankGlyphCount">The non blank glyph count value used by this model or operation.</param>
/// <param name="ZeroWidthGlyphCount">The zero width glyph count value used by this model or operation.</param>
/// <param name="OversizedWidthGlyphCount">The oversized width glyph count value used by this model or operation.</param>
/// <param name="NonZeroReservedByteCount">The non zero reserved byte count value used by this model or operation.</param>
/// <param name="NonZeroUnusedPixelNibbleCount">The non zero unused pixel nibble count value used by this model or operation.</param>
/// <param name="PaddingByteCount">The padding byte count value used by this model or operation.</param>
/// <param name="NonZeroPaddingByteCount">The non zero padding byte count value used by this model or operation.</param>
/// <param name="RequiredRussianCharacterCount">The required russian character count value used by this model or operation.</param>
/// <param name="MappedRussianCharacterCount">The mapped russian character count value used by this model or operation.</param>
/// <param name="RenderableRussianCharacterCount">The renderable russian character count value used by this model or operation.</param>
/// <param name="RussianReady">The russian ready value used by this model or operation.</param>
/// <param name="RussianGlyphs">The russian glyphs value used by this model or operation.</param>
/// <param name="Warnings">The warnings value used by this model or operation.</param>
public sealed record LtFontAnalysis(
    int FileSize,
    string SourceSha256,
    int GlyphCount,
    int BlankGlyphCount,
    int NonBlankGlyphCount,
    int ZeroWidthGlyphCount,
    int OversizedWidthGlyphCount,
    int NonZeroReservedByteCount,
    int NonZeroUnusedPixelNibbleCount,
    int PaddingByteCount,
    int NonZeroPaddingByteCount,
    int RequiredRussianCharacterCount,
    int MappedRussianCharacterCount,
    int RenderableRussianCharacterCount,
    bool RussianReady,
    IReadOnlyList<LtFontRussianGlyphStatus> RussianGlyphs,
    IReadOnlyList<string> Warnings);

/// <summary>
/// Represents the toolkit's lt font model or service.
/// </summary>
public sealed class LtFont
{
    /// <summary>The fixed glyph width value used by this format or revision.</summary>
    public const int GlyphWidth = 18;
    /// <summary>The fixed glyph height value used by this format or revision.</summary>
    public const int GlyphHeight = 18;
    /// <summary>The fixed bits per pixel value used by this format or revision.</summary>
    public const int BitsPerPixel = 2;
    /// <summary>The fixed pixels per byte value used by this format or revision.</summary>
    public const int PixelsPerByte = 4;
    /// <summary>The fixed bytes per row value used by this format or revision.</summary>
    public const int BytesPerRow = 5;
    /// <summary>The fixed bitmap bytes value used by this format or revision.</summary>
    public const int BitmapBytes = 90;
    /// <summary>The fixed glyph record size value used by this format or revision.</summary>
    public const int GlyphRecordSize = 92;
    /// <summary>The fixed advance width offset value used by this format or revision.</summary>
    public const int AdvanceWidthOffset = 90;
    /// <summary>The fixed checksum size value used by this format or revision.</summary>
    public const int ChecksumSize = 16;
    /// <summary>The fixed file alignment value used by this format or revision.</summary>
    public const int FileAlignment = 2048;

    /// <summary>The fixed required russian characters value used by this format or revision.</summary>
    public const string RequiredRussianCharacters =
        "АБВГДЕЁЖЗИЙКЛМНОПРСТУФХЦЧШЩЪЫЬЭЮЯ" +
        "абвгдеёжзийклмнопрстуфхцчшщъыьэюя";

    /// <summary>Stores the glyphs state owned by this instance or type.</summary>
    private readonly List<LtGlyph> _glyphs;
    /// <summary>Stores the padding state owned by this instance or type.</summary>
    private readonly byte[] _padding;
    /// <summary>Stores the file size state owned by this instance or type.</summary>
    private readonly int _fileSize;

    /// <summary>
    /// Initializes a new instance with validated constructor state.
    /// </summary>
    /// <param name="glyphs">The glyphs value.</param>
    /// <param name="padding">The padding value.</param>
    /// <param name="fileSize">The file size value.</param>
    /// <param name="sourceSha256">The source SHA 256 value.</param>
    private LtFont(List<LtGlyph> glyphs, byte[] padding, int fileSize, string sourceSha256)
    {
        _glyphs = glyphs;
        _padding = padding;
        _fileSize = fileSize;
        SourceSha256 = sourceSha256;
    }

    /// <summary>The glyphs value used by this model or operation.</summary>
    public IReadOnlyList<LtGlyph> Glyphs => _glyphs;
    /// <summary>The hexadecimal SHA-256 of the exact input snapshot used by this object.</summary>
    public string SourceSha256 { get; }
    /// <summary>The padding length value used by this model or operation.</summary>
    public int PaddingLength => _padding.Length;
    /// <summary>The file size value used by this model or operation.</summary>
    public int FileSize => _fileSize;

    /// <summary>
    /// Parses validated input into the current binary-format model.
    /// </summary>
    /// <param name="data">The binary data to process.</param>
    /// <param name="glyphCount">The glyph count value.</param>
    /// <returns>The validated operation result.</returns>
    /// <remarks>Malformed input or violated preconditions are rejected before a persistent output is committed.</remarks>
    public static LtFont Parse(ReadOnlySpan<byte> data, int glyphCount)
    {
        if (glyphCount <= 0 || glyphCount >= 0xFF00)
        {
            throw new ToolkitException("FONT_GLYPH_COUNT", $"Glyph count {glyphCount} must be between 1 and 65279.");
        }
        if (data.Length < ChecksumSize || (data.Length & 0x0F) != 0)
        {
            throw new ToolkitException("FONT_SIZE", $"lt.bin length {data.Length} must be a positive multiple of 16.");
        }
        int recordsLength;
        try
        {
            recordsLength = checked(glyphCount * GlyphRecordSize);
        }
        catch (OverflowException ex)
        {
            throw new ToolkitException("FONT_SIZE", "Glyph records exceed supported size.", ex);
        }
        int contentLength = data.Length - ChecksumSize;
        if (recordsLength > contentLength)
        {
            throw new ToolkitException(
                "FONT_SIZE",
                $"lt.bin contains {contentLength} content bytes, but {glyphCount} glyphs require {recordsLength} bytes.");
        }
        if (!RgoChecksum.Verify(data))
        {
            throw new ToolkitException("FONT_CHECKSUM", "lt.bin checksum is invalid.");
        }

        List<LtGlyph> glyphs = new(glyphCount);
        for (int index = 0; index < glyphCount; index++)
        {
            ReadOnlySpan<byte> record = data.Slice(index * GlyphRecordSize, GlyphRecordSize);
            byte[] levels = new byte[GlyphWidth * GlyphHeight];
            byte[] unusedPixelBits = new byte[GlyphHeight];
            for (int y = 0; y < GlyphHeight; y++)
            {
                for (int x = 0; x < GlyphWidth; x++)
                {
                    int source = y * BytesPerRow + x / PixelsPerByte;
                    int shift = (x % PixelsPerByte) * BitsPerPixel;
                    levels[y * GlyphWidth + x] = checked((byte)((record[source] >> shift) & 0x03));
                }
                unusedPixelBits[y] = checked((byte)(record[y * BytesPerRow + BytesPerRow - 1] & 0xF0));
            }
            glyphs.Add(new LtGlyph
            {
                Index = index,
                Levels = levels,
                UnusedPixelBits = unusedPixelBits,
                AdvanceWidth = record[AdvanceWidthOffset],
                Reserved = record[AdvanceWidthOffset + 1]
            });
        }

        byte[] padding = data.Slice(recordsLength, contentLength - recordsLength).ToArray();
        return new LtFont(glyphs, padding, data.Length, BinaryUtilities.Sha256Hex(data));
    }

    /// <summary>
    /// Creates a deep copy whose mutable buffers are independent of the source instance.
    /// </summary>
    /// <returns>The validated operation result.</returns>
    public LtFont Clone() => new(
        _glyphs.Select(static glyph => glyph.Clone()).ToList(),
        _padding.ToArray(),
        _fileSize,
        SourceSha256);

    /// <summary>
    /// Gets glyph while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="index">The index value.</param>
    /// <returns>The validated operation result.</returns>
    public LtGlyph GetGlyph(int index)
    {
        if ((uint)index >= (uint)_glyphs.Count)
        {
            throw new ToolkitException("FONT_GLYPH_INDEX", $"Glyph index {index} is outside font with {_glyphs.Count} glyphs.");
        }
        return _glyphs[index];
    }

    /// <summary>
    /// Replaces glyph while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="index">The index value.</param>
    /// <param name="levels">The levels value.</param>
    /// <param name="advanceWidth">The advance width value.</param>
    /// <param name="reserved">The reserved value.</param>
    public void ReplaceGlyph(int index, ReadOnlySpan<byte> levels, int advanceWidth, byte? reserved = null)
    {
        if (levels.Length != GlyphWidth * GlyphHeight)
        {
            throw new ToolkitException(
                "FONT_BITMAP_SIZE",
                $"Glyph bitmap has {levels.Length} pixels; expected {GlyphWidth * GlyphHeight}.");
        }
        if (advanceWidth is < 0 or > GlyphWidth)
        {
            throw new ToolkitException("FONT_ADVANCE", $"Glyph advance width {advanceWidth} must be between 0 and {GlyphWidth}.");
        }
        byte[] copy = levels.ToArray();
        for (int pixel = 0; pixel < copy.Length; pixel++)
        {
            if (copy[pixel] > 3)
            {
                throw new ToolkitException("FONT_PIXEL_LEVEL", $"Glyph pixel {pixel} has unsupported level {copy[pixel]}.");
            }
        }
        LtGlyph glyph = GetGlyph(index);
        glyph.Levels = copy;
        glyph.AdvanceWidth = checked((byte)advanceWidth);
        if (reserved is byte explicitReserved)
        {
            glyph.Reserved = explicitReserved;
        }
    }

    /// <summary>
    /// Serializes the current validated model into its binary representation.
    /// </summary>
    /// <returns>The resulting binary or typed sequence.</returns>
    /// <remarks>Malformed input or violated preconditions are rejected before a persistent output is committed.</remarks>
    public byte[] Build()
    {
        int recordsLength = checked(_glyphs.Count * GlyphRecordSize);
        int expectedLength = checked(recordsLength + _padding.Length + ChecksumSize);
        if (expectedLength != _fileSize || (_fileSize & 0x0F) != 0)
        {
            throw new ToolkitException("FONT_BUILD_SIZE", "Internal lt.bin size metadata is inconsistent.");
        }

        byte[] output = new byte[_fileSize];
        for (int index = 0; index < _glyphs.Count; index++)
        {
            LtGlyph glyph = _glyphs[index];
            if (glyph.Levels.Length != GlyphWidth * GlyphHeight)
            {
                throw new ToolkitException("FONT_BITMAP_SIZE", $"Glyph {index} has an invalid bitmap length.");
            }
            if (glyph.UnusedPixelBits.Length != GlyphHeight)
            {
                throw new ToolkitException("FONT_UNUSED_BITS", $"Glyph {index} has an invalid unused-pixel-bits length.");
            }
            Span<byte> record = output.AsSpan(index * GlyphRecordSize, GlyphRecordSize);
            for (int y = 0; y < GlyphHeight; y++)
            {
                byte unused = glyph.UnusedPixelBits[y];
                if ((unused & 0x0F) != 0)
                {
                    throw new ToolkitException("FONT_UNUSED_BITS", $"Glyph {index} row {y} contains active bits in its unused-pixel mask.");
                }
                record[y * BytesPerRow + BytesPerRow - 1] = unused;
                for (int x = 0; x < GlyphWidth; x++)
                {
                    byte level = glyph.Levels[y * GlyphWidth + x];
                    if (level > 3)
                    {
                        throw new ToolkitException("FONT_PIXEL_LEVEL", $"Glyph {index} contains unsupported pixel level {level}.");
                    }
                    int target = y * BytesPerRow + x / PixelsPerByte;
                    int shift = (x % PixelsPerByte) * BitsPerPixel;
                    record[target] |= checked((byte)(level << shift));
                }
            }
            record[AdvanceWidthOffset] = glyph.AdvanceWidth;
            record[AdvanceWidthOffset + 1] = glyph.Reserved;
        }
        _padding.AsSpan().CopyTo(output.AsSpan(recordsLength));
        RgoChecksum.Apply(output);
        return output;
    }

    /// <summary>
    /// Analyzes the supplied model and returns deterministic diagnostics.
    /// </summary>
    /// <param name="map">The glyph map used for text conversion.</param>
    /// <returns>The validated operation result.</returns>
    public LtFontAnalysis Analyze(GlyphMap? map = null)
    {
        if (map is not null && map.Count != _glyphs.Count)
        {
            throw new ToolkitException(
                "FONT_MAP_COUNT",
                $"Glyph map has {map.Count} entries, but lt.bin was parsed with {_glyphs.Count} glyphs.");
        }

        int blank = _glyphs.Count(static glyph => glyph.IsBlank);
        int zeroWidth = _glyphs.Count(static glyph => glyph.AdvanceWidth == 0);
        int oversized = _glyphs.Count(static glyph => glyph.AdvanceWidth > GlyphWidth);
        int reserved = _glyphs.Count(static glyph => glyph.Reserved != 0);
        int nonZeroUnusedPixelNibbles = _glyphs.Sum(static glyph => glyph.UnusedPixelBits.Count(static value => value != 0));
        int nonZeroPadding = _padding.Count(static value => value != 0);
        List<string> warnings = [];
        if (oversized > 0)
        {
            warnings.Add($"{oversized} glyph(s) have an advance wider than the {GlyphWidth}-pixel cell.");
        }
        if (reserved > 0)
        {
            warnings.Add($"{reserved} glyph record(s) have a non-zero reserved byte; these bytes are preserved.");
        }
        if (nonZeroUnusedPixelNibbles > 0)
        {
            warnings.Add($"{nonZeroUnusedPixelNibbles} row(s) contain non-zero bits outside the 18-pixel glyph width; these bits are preserved.");
        }
        if (nonZeroPadding > 0)
        {
            warnings.Add($"{nonZeroPadding} non-zero byte(s) were found in font padding; padding is preserved.");
        }

        List<LtFontRussianGlyphStatus> russian = [];
        foreach (Rune rune in RequiredRussianCharacters.EnumerateRunes())
        {
            string character = rune.ToString();
            int? index = null;
            bool mapped = map is not null && map.TryGetLowestIndex(character, out ushort mappedIndex);
            if (mapped)
            {
                index = mappedIndex;
            }
            LtGlyph? glyph = index is int valid && valid < _glyphs.Count ? _glyphs[valid] : null;
            russian.Add(new LtFontRussianGlyphStatus(
                character,
                rune.Value,
                index,
                mapped,
                glyph is not null && !glyph.IsBlank,
                glyph?.AdvanceWidth));
        }
        int mappedCount = russian.Count(static item => item.Mapped);
        int renderableCount = russian.Count(static item =>
            item.Mapped && item.NonBlank && item.AdvanceWidth is > 0 and <= GlyphWidth);
        bool ready = map is not null && renderableCount == russian.Count;
        if (map is not null && !ready)
        {
            warnings.Add(
                $"Russian alphabet readiness is incomplete: {renderableCount}/{russian.Count} required characters have mapped, non-blank glyphs with a valid advance width.");
        }

        return new LtFontAnalysis(
            _fileSize,
            SourceSha256,
            _glyphs.Count,
            blank,
            _glyphs.Count - blank,
            zeroWidth,
            oversized,
            reserved,
            nonZeroUnusedPixelNibbles,
            _padding.Length,
            nonZeroPadding,
            russian.Count,
            mappedCount,
            renderableCount,
            ready,
            russian,
            warnings);
    }

    /// <summary>
    /// Renders decoded LT glyphs into a bounded RGBA atlas using the requested columns, scale and gutter.
    /// </summary>
    /// <param name="columns">The columns value.</param>
    /// <param name="scale">The scale value.</param>
    /// <param name="gutter">The gutter value.</param>
    /// <param name="limits">Optional conservative safety limits; defaults are used when omitted.</param>
    /// <returns>The validated operation result.</returns>
    public RgbaImage RenderAtlas(int columns = 64, int scale = 1, int gutter = 1, FileLimits? limits = null)
    {
        limits ??= FileLimits.Default;
        if (columns <= 0 || columns > 512)
        {
            throw new ToolkitException("FONT_PREVIEW_COLUMNS", $"Preview columns {columns} must be between 1 and 512.");
        }
        if (scale <= 0 || scale > 16)
        {
            throw new ToolkitException("FONT_PREVIEW_SCALE", $"Preview scale {scale} must be between 1 and 16.");
        }
        if (gutter < 0 || gutter > 16)
        {
            throw new ToolkitException("FONT_PREVIEW_GUTTER", $"Preview gutter {gutter} must be between 0 and 16.");
        }

        int rows = checked((_glyphs.Count + columns - 1) / columns);
        int cellWidth = checked(GlyphWidth * scale + gutter);
        int cellHeight = checked(GlyphHeight * scale + gutter);
        int width = checked(columns * cellWidth + gutter);
        int height = checked(rows * cellHeight + gutter);
        long pixels = checked((long)width * height);
        if (width > limits.MaximumImageDimension || height > limits.MaximumImageDimension || pixels > limits.MaximumImagePixels)
        {
            throw new ToolkitException(
                "FONT_PREVIEW_LIMIT",
                $"Font preview {width}x{height} ({pixels} pixels) exceeds configured limits.");
        }

        byte[] rgba = new byte[checked((int)pixels * 4)];
        for (int pixel = 0; pixel < pixels; pixel++)
        {
            rgba[pixel * 4 + 3] = 255;
        }
        for (int index = 0; index < _glyphs.Count; index++)
        {
            int column = index % columns;
            int row = index / columns;
            int originX = checked(gutter + column * cellWidth);
            int originY = checked(gutter + row * cellHeight);
            LtGlyph glyph = _glyphs[index];
            for (int y = 0; y < GlyphHeight; y++)
            {
                for (int x = 0; x < GlyphWidth; x++)
                {
                    byte intensity = checked((byte)(glyph.Levels[y * GlyphWidth + x] * 85));
                    for (int sy = 0; sy < scale; sy++)
                    {
                        for (int sx = 0; sx < scale; sx++)
                        {
                            int targetX = originX + x * scale + sx;
                            int targetY = originY + y * scale + sy;
                            int target = checked((targetY * width + targetX) * 4);
                            rgba[target] = intensity;
                            rgba[target + 1] = intensity;
                            rgba[target + 2] = intensity;
                            rgba[target + 3] = 255;
                        }
                    }
                }
            }
        }
        return new RgbaImage(width, height, rgba);
    }
}
