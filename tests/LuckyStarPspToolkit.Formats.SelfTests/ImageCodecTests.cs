using System.Buffers.Binary;
using System.Text.Json;
using LuckyStarPspToolkit.Formats.Common;
using LuckyStarPspToolkit.Formats.Fonts;
using LuckyStarPspToolkit.Formats.Images;

/// <summary>Checks independently generated PNG, PSP swizzle, palette and gzip image vectors.</summary>
internal static class ImageCodecTests
{
    /// <summary>Exercises all PNG filters and supported color/depth/transparency forms against Python-generated pixels.</summary>
    internal static void RunPng()
    {
        using JsonDocument oracle = ReadOracle();
        foreach (JsonElement vector in oracle.RootElement.GetProperty("pngCases").EnumerateArray())
        {
            byte[] png = Convert.FromBase64String(vector.GetProperty("png").GetString()!);
            RgbaImage image = PngReader.Decode(png);
            if (image.Width != vector.GetProperty("width").GetInt32() || image.Height != vector.GetProperty("height").GetInt32() || !image.Pixels.SequenceEqual(Convert.FromBase64String(vector.GetProperty("rgba").GetString()!)))
                throw new InvalidOperationException("Independent PNG pixel vector failed: " + vector.GetProperty("name").GetString());
            if (!image.Pixels.SequenceEqual(PngReader.Decode(PngWriter.Encode(image)).Pixels)) throw new InvalidOperationException("PNG RGBA roundtrip failed.");
            byte[] corrupt = png.ToArray();
            corrupt[16] ^= 1;
            ExpectError("PNG_CRC", () => PngReader.Decode(corrupt));
            ExpectError("PNG_LIMIT", () => PngReader.Decode(png, FileLimits.Default with { MaximumImagePixels = 1 }));
            ExpectError("PNG_END", () => PngReader.Decode(png.Concat(new byte[] { 1 }).ToArray()));
        }
        foreach (JsonElement vector in oracle.RootElement.GetProperty("pngErrors").EnumerateArray())
            ExpectError(vector.GetProperty("code").GetString()!, () => PngReader.Decode(Convert.FromBase64String(vector.GetProperty("png").GetString()!)));
    }

    /// <summary>Checks bit-exact raw edits, unchanged gzip preservation, palette refusals and guarded resource bounds.</summary>
    internal static void RunTextures()
    {
        using JsonDocument oracle = ReadOracle();
        foreach (JsonElement vector in oracle.RootElement.GetProperty("textures").EnumerateArray())
        {
            IndexedTextureLayout layout = JsonSerializer.Deserialize<IndexedTextureLayout>(vector.GetProperty("layout"), RgoMenuImages.JsonOptions)!;
            byte[] original = Convert.FromBase64String(vector.GetProperty("resource").GetString()!);
            byte[] snapshot = original.ToArray();
            RgbaImage image = IndexedTexture.Decode(original, layout);
            if (!image.Pixels.SequenceEqual(Convert.FromBase64String(vector.GetProperty("rgba").GetString()!))) throw new InvalidOperationException("Independent swizzle/palette decoding failed.");
            if (!IndexedTexture.Replace(original, layout, image).SequenceEqual(original)) throw new InvalidOperationException("No-op resource changed original indices or gzip bytes.");
            byte[] editedPixels = Convert.FromBase64String(vector.GetProperty("editedRgba").GetString()!);
            RgbaImage edited = new(layout.Width, layout.Height, editedPixels);
            byte[] replacement = IndexedTexture.Replace(original, layout, edited);
            if (!IndexedTexture.Decode(replacement, layout).Pixels.SequenceEqual(editedPixels)) throw new InvalidOperationException("Edited texture pixel roundtrip failed.");
            if (vector.GetProperty("editedResource").ValueKind == JsonValueKind.String && !replacement.SequenceEqual(Convert.FromBase64String(vector.GetProperty("editedResource").GetString()!))) throw new InvalidOperationException("Raw texture edit differs from independent packed-byte oracle.");
            if (!replacement.AsSpan(layout.Capacity).SequenceEqual(original.AsSpan(layout.Capacity))) throw new InvalidOperationException("Texture replacement changed its palette or unrelated bytes.");
            if (!original.SequenceEqual(snapshot)) throw new InvalidOperationException("Texture replacement mutated its input.");
            byte[] unknownColor = image.Pixels.ToArray();
            original.AsSpan(layout.PaletteOffset + 4 * 4, 4).CopyTo(unknownColor);
            unknownColor[0]++;
            RgbaImage unlisted = new(layout.Width, layout.Height, unknownColor);
            ExpectError("TEXTURE_PALETTE", () => IndexedTexture.Replace(original, layout, unlisted));
            byte[] quantized = IndexedTexture.Replace(original, layout, unlisted, quantize: true);
            if (!IndexedTexture.Decode(quantized, layout).Pixels.AsSpan(0, 4).SequenceEqual(original.AsSpan(layout.PaletteOffset + 4 * 4, 4))) throw new InvalidOperationException("Explicit nearest-palette matching selected the wrong color.");
            ExpectError("TEXTURE_DIMENSIONS", () => IndexedTexture.Replace(original, layout, new RgbaImage(1, 1, new byte[4])));
            ExpectError("TEXTURE_RANGE", () => IndexedTexture.Decode(original, layout with { PaletteOffset = 0 }));
            if (layout.Compressed)
            {
                byte[] wrongLength = original.ToArray();
                int firstFrame = BinaryPrimitives.ReadInt32LittleEndian(wrongLength.AsSpan(4));
                BinaryPrimitives.WriteInt32LittleEndian(wrongLength.AsSpan(firstFrame), int.MaxValue);
                ExpectError("TEXTURE_INFLATE_SIZE", () => IndexedTexture.Decode(wrongLength, layout));
                byte[] badFrames = original.ToArray();
                BinaryPrimitives.WriteInt32LittleEndian(badFrames, 257);
                ExpectError("TEXTURE_FRAMES", () => IndexedTexture.Decode(badFrames, layout));
            }
        }
        JsonElement capacity = oracle.RootElement.GetProperty("capacityTexture");
        IndexedTextureLayout tightLayout = JsonSerializer.Deserialize<IndexedTextureLayout>(capacity.GetProperty("layout"), RgoMenuImages.JsonOptions)!;
        byte[] tightSource = Convert.FromBase64String(capacity.GetProperty("resource").GetString()!);
        byte[] tightSnapshot = tightSource.ToArray();
        RgbaImage noise = new(tightLayout.Width, tightLayout.Height, Convert.FromBase64String(capacity.GetProperty("noisyRgba").GetString()!));
        ExpectError("TEXTURE_CAPACITY", () => IndexedTexture.Replace(tightSource, tightLayout, noise));
        if (!tightSource.SequenceEqual(tightSnapshot)) throw new InvalidOperationException("Compressed capacity refusal modified its input.");
        byte[] invalidRevision = new byte[241664];
        string temp = Path.Combine(Path.GetTempPath(), "lsptool-menu-revision-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            string source = Path.Combine(temp, "pr.bin"), output = Path.Combine(temp, "images");
            File.WriteAllBytes(source, invalidRevision);
            ExpectError("MENU_REVISION", () => RgoMenuImages.Export(source, output));
            if (Directory.Exists(output)) throw new InvalidOperationException("Unknown menu revision created an image workspace.");
        }
        finally { Directory.Delete(temp, true); }
    }

    /// <summary>Loads only the checked-in synthetic oracle, never an original game resource.</summary>
    /// <returns>The JSON oracle snapshot.</returns>
    private static JsonDocument ReadOracle() => JsonDocument.Parse(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "image-codec-oracle.json")));

    /// <summary>Requires an exact format refusal and treats other failures as regressions.</summary>
    /// <param name="code">The expected stable refusal code.</param>
    /// <param name="action">The malformed-input operation.</param>
    private static void ExpectError(string code, Action action)
    {
        try { action(); }
        catch (ToolkitException ex) when (ex.Code == code) { return; }
        throw new InvalidOperationException("Expected image error " + code);
    }
}
