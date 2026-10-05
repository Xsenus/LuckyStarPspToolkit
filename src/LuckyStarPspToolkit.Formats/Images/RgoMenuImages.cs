using System.Text.Json;
using System.Buffers.Binary;
using LuckyStarPspToolkit.Formats.Common;
using LuckyStarPspToolkit.Formats.Fonts;
using LuckyStarPspToolkit.Formats.Scripts;

namespace LuckyStarPspToolkit.Formats.Images;

/// <summary>Binds an exported RGO interface-image workspace to the exact original PR resource.</summary>
public sealed class MenuImageManifest
{
    /// <summary>The supported manifest schema revision.</summary>
    public int SchemaVersion { get; set; } = 1;
    /// <summary>The verified game identifier.</summary>
    public string Game { get; set; } = "rgo-uljm05752";
    /// <summary>The fingerprint of the original PR resource.</summary>
    public string SourceSha256 { get; set; } = string.Empty;
    /// <summary>The immutable original layouts and image identifiers.</summary>
    public List<IndexedTextureLayout> Images { get; set; } = [];
}

/// <summary>Reports which interface images changed without claiming game runtime acceptance.</summary>
/// <param name="OutputPath">The new resource path.</param>
/// <param name="SourceSha256">The original resource fingerprint.</param>
/// <param name="OutputSha256">The rebuilt resource fingerprint.</param>
/// <param name="ChangedImages">The identifiers with changed pixel data.</param>
/// <param name="ByteIdentical">Whether no-op rebuilding preserved every original byte.</param>
/// <param name="Quantized">Whether nearest-palette color matching was explicitly permitted.</param>
public sealed record MenuImageBuildResult(string OutputPath, string SourceSha256, string OutputSha256, IReadOnlyList<int> ChangedImages, bool ByteIdentical, bool Quantized)
{
    /// <summary>Resource verification never substitutes for launching the rebuilt game.</summary>
    public bool GameRuntimeVerified => false;
}

/// <summary>Exports and rebuilds the ten interface textures of the authenticated original RGO PR resource.</summary>
public static class RgoMenuImages
{
    /// <summary>The SHA-256 of the user-supplied original RGO ULJM05752 pr.bin.</summary>
    public const string SourceSha256 = "3da3f301711d87f88277628b93cacaa8a472b89bc2f60ec14dd2971b369ebb92";
    /// <summary>The layouts verified against the original resource; the game fixes these allocations.</summary>
    private static readonly IndexedTextureLayout[] Layouts =
    [
        new(0, 0x00000, 0x10000, 0x39C00, 16, 512, 256, false),
        new(1, 0x10000, 0x0A000, 0x39D00, 256, 256, 160, false),
        new(2, 0x1A000, 0x01000, 0x3A500, 16, 256, 128, true),
        new(3, 0x1B000, 0x03800, 0x3A900, 256, 256, 256, true),
        new(4, 0x1E800, 0x01800, 0x39800, 256, 256, 64, true),
        new(5, 0x20000, 0x08000, 0x3A100, 256, 256, 128, false),
        new(6, 0x28000, 0x08000, 0x3A500, 16, 256, 256, false),
        new(7, 0x30000, 0x08800, 0x3A500, 16, 256, 272, false),
        new(8, 0x38800, 0x00800, 0x3A700, 16, 128, 128, true),
        new(9, 0x39000, 0x00800, 0x3A800, 16, 128, 128, true)
    ];
    /// <summary>Strict, deterministic JSON settings for image workspace metadata.</summary>
    public static JsonSerializerOptions JsonOptions { get; } = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow
    };

    /// <summary>Decodes all interface images, validates the resource revision and publishes a fresh PNG directory.</summary>
    /// <param name="sourcePath">The unmodified original pr.bin.</param>
    /// <param name="outputDirectory">A new directory for PNG files and menu-images.json.</param>
    /// <returns>The source-bound image workspace manifest.</returns>
    public static MenuImageManifest Export(string sourcePath, string outputDirectory)
    {
        byte[] source = ReadSource(sourcePath);
        string output = PathUtilities.NormalizeProtectedPath(outputDirectory, "MENU_OUTPUT_PATH");
        if (Directory.Exists(output) || File.Exists(output)) throw new ToolkitException("MENU_OUTPUT_EXISTS", "Menu export directory must not already exist.");
        List<(string Name, byte[] Data)> files = [];
        foreach (IndexedTextureLayout layout in Layouts)
            files.Add((ImageName(layout.Id), PngWriter.Encode(IndexedTexture.Decode(source, layout))));
        MenuImageManifest manifest = new() { SourceSha256 = SourceSha256, Images = Layouts.ToList() };
        files.Add(("menu-images.json", JsonSerializer.SerializeToUtf8Bytes(manifest, JsonOptions)));
        string staging = output + ".tmp." + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(staging);
        foreach ((string name, byte[] data) in files) AtomicFile.WriteAllBytes(Path.Combine(staging, name), data);
        Directory.Move(staging, output);
        return manifest;
    }

    /// <summary>Rebuilds edited interface images into a new same-size resource, keeping all palettes and unrelated bytes.</summary>
    /// <param name="sourcePath">The original unmodified pr.bin used for export.</param>
    /// <param name="imageDirectory">The exported image directory; original filenames and manifest are preserved.</param>
    /// <param name="outputPath">A new PR resource file outside the image workspace.</param>
    /// <param name="quantize">Whether edited colors may be mapped to the closest original palette color.</param>
    /// <returns>The verified binary rebuild report.</returns>
    public static MenuImageBuildResult Build(string sourcePath, string imageDirectory, string outputPath, bool quantize = false)
    {
        byte[] source = ReadSource(sourcePath);
        string directory = PathUtilities.NormalizeProtectedPath(imageDirectory, "MENU_IMAGE_PATH");
        string output = PathUtilities.NormalizeProtectedPath(outputPath, "MENU_OUTPUT_PATH");
        PathUtilities.RequireDifferent(sourcePath, output, "MENU_OUTPUT_SOURCE", "Refusing to overwrite original pr.bin.");
        if (PathUtilities.IsWithinOrSame(output, directory)) throw new ToolkitException("MENU_OUTPUT_PATH", "Menu output must be outside the image workspace.");
        byte[] manifestBytes = BinaryUtilities.ReadAllBytesBounded(PathUtilities.ResolveContainedPath(directory, "menu-images.json", "MENU_IMAGE_PATH"), FileLimits.Default with { MaximumInputBytes = 64 * 1024 });
        MenuImageManifest? manifest = StrictJson.Deserialize<MenuImageManifest>(manifestBytes, JsonOptions, "MENU_MANIFEST");
        if (manifest is null || manifest.SchemaVersion != 1 || manifest.Game != "rgo-uljm05752" || manifest.SourceSha256 != SourceSha256 || manifest.Images is null || !manifest.Images.SequenceEqual(Layouts))
            throw new ToolkitException("MENU_MANIFEST", "Image manifest does not match the original RGO PR layouts.");
        byte[] rebuilt = source;
        List<int> changed = [];
        foreach (IndexedTextureLayout layout in Layouts)
        {
            string pngPath = PathUtilities.ResolveContainedPath(directory, ImageName(layout.Id), "MENU_IMAGE_PATH");
            byte[] png = BinaryUtilities.ReadAllBytesBounded(pngPath, FileLimits.Default with { MaximumInputBytes = 64 * 1024 * 1024 });
            RgbaImage image = PngReader.Decode(png);
            byte[] next = IndexedTexture.Replace(rebuilt, layout, image, quantize);
            if (!rebuilt.AsSpan(layout.PixelOffset, layout.Capacity).SequenceEqual(next.AsSpan(layout.PixelOffset, layout.Capacity))) changed.Add(layout.Id);
            if (layout.Compressed) ProcessImageChecksum(next, layout, apply: true);
            rebuilt = next;
        }
        // The complete resource is small; check every protected byte independently of encoder bookkeeping.
        for (int offset = 0; offset < source.Length; offset++)
            if (source[offset] != rebuilt[offset] && !Layouts.Any(layout => offset >= layout.PixelOffset && offset < layout.PixelOffset + layout.Capacity))
                throw new ToolkitException("MENU_PROTECTED_BYTES", "Menu rebuild changed a palette or unrelated byte.");
        foreach (IndexedTextureLayout layout in Layouts) _ = IndexedTexture.Decode(rebuilt, layout);
        // The game validates the trailing sums before accepting the PR resource.
        // Pixel changes therefore require a new checksum even when its size is unchanged.
        RgoChecksum.Apply(rebuilt);
        if (!RgoChecksum.Verify(rebuilt)) throw new ToolkitException("MENU_CHECKSUM", "Rebuilt PR checksum verification failed.");
        MenuImageBuildResult result = new(output, SourceSha256, BinaryUtilities.Sha256Hex(rebuilt), changed, source.AsSpan().SequenceEqual(rebuilt), quantize);
        AtomicFile.WriteAllBytes(output, rebuilt);
        return result;
    }

    /// <summary>Authenticates the exact original PR revision before interpreting any image offsets.</summary>
    /// <param name="path">The original PR file.</param>
    /// <returns>The authenticated snapshot.</returns>
    private static byte[] ReadSource(string path)
    {
        byte[] bytes = BinaryUtilities.ReadAllBytesBounded(path, FileLimits.Default with { MaximumInputBytes = 1024 * 1024 });
        if (bytes.Length != 241664 || BinaryUtilities.Sha256Hex(bytes) != SourceSha256)
            throw new ToolkitException("MENU_REVISION", "Menu image profile requires the original RGO ULJM05752 pr.bin with its verified SHA-256; NIM and modified PR resources need a separate profile.");
        if (!RgoChecksum.Verify(bytes)) throw new ToolkitException("MENU_CHECKSUM", "Original PR checksum verification failed.");
        foreach (IndexedTextureLayout layout in Layouts.Where(layout => layout.Compressed)) ProcessImageChecksum(bytes, layout, apply: false);
        return bytes;
    }

    /// <summary>Checks or updates the RGO checksum of a compressed image's 2048-byte aligned allocation.</summary>
    /// <param name="resource">The authenticated PR snapshot or its rebuilt copy.</param>
    /// <param name="layout">The fixed compressed-image layout.</param>
    /// <param name="apply">Whether to write freshly calculated checksum words.</param>
    internal static void ProcessImageChecksum(byte[] resource, IndexedTextureLayout layout, bool apply)
    {
        Span<byte> block = resource.AsSpan(layout.PixelOffset, layout.Capacity);
        int count = BinaryPrimitives.ReadInt32LittleEndian(block);
        if (count is < 1 or > 256 || (count + 2) * 4 > block.Length)
            throw new ToolkitException("MENU_CHECKSUM", "Invalid compressed-image checksum boundary table.");
        int end = BinaryPrimitives.ReadInt32LittleEndian(block.Slice(4 + count * 4, 4));
        if (end <= 0 || end > block.Length)
            throw new ToolkitException("MENU_CHECKSUM", "Invalid compressed-image checksum extent.");
        int aligned = checked((end + 2047) / 2048 * 2048);
        if (aligned > block.Length || end > aligned - 16)
            throw new ToolkitException("TEXTURE_CAPACITY", "Compressed image leaves no room for its aligned game checksum.");
        Span<byte> checkedBlock = block[..aligned];
        if (apply) RgoChecksum.Apply(checkedBlock);
        if (!RgoChecksum.Verify(checkedBlock))
            throw new ToolkitException("MENU_CHECKSUM", "Compressed-image checksum verification failed.");
    }

    /// <summary>Returns the stable filename used by interface-image workspaces.</summary>
    /// <param name="id">The authenticated image identifier.</param>
    /// <returns>The PNG filename.</returns>
    private static string ImageName(int id) => $"pr-{id:D4}.png";
}
