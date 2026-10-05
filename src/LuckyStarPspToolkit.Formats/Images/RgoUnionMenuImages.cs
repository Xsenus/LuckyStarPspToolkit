using System.Text.Json;
using LuckyStarPspToolkit.Formats.Common;
using LuckyStarPspToolkit.Formats.Cri;
using LuckyStarPspToolkit.Formats.Fonts;
using LuckyStarPspToolkit.Formats.Scripts;

namespace LuckyStarPspToolkit.Formats.Images;

/// <summary>Describes one authenticated menu resource and its immutable image allocations.</summary>
/// <param name="Id">The original CPK file identifier.</param>
/// <param name="SourceSha256">The original resource digest.</param>
/// <param name="Size">The fixed resource length.</param>
/// <param name="Images">The verified texture layouts.</param>
public sealed record UnionMenuResourceProfile(ushort Id, string SourceSha256, int Size, IReadOnlyList<IndexedTextureLayout> Images);

/// <summary>Binds menu PNGs to the exact original RGO union archive.</summary>
public sealed class UnionMenuImageManifest
{
    /// <summary>The supported schema revision.</summary>
    public int SchemaVersion { get; set; } = 1;
    /// <summary>The supported game revision.</summary>
    public string Game { get; set; } = "rgo-uljm05752";
    /// <summary>The original archive digest.</summary>
    public string SourceSha256 { get; set; } = string.Empty;
    /// <summary>The fixed original resource profiles.</summary>
    public List<UnionMenuResourceProfile> Files { get; set; } = [];
}

/// <summary>Reports fixed-allocation menu changes without claiming gameplay acceptance.</summary>
/// <param name="OutputPath">The new archive path.</param>
/// <param name="OutputSha256">The rebuilt archive digest.</param>
/// <param name="ChangedImages">The file/image identifiers with changed pixels.</param>
/// <param name="ByteIdentical">Whether the output is identical to the original.</param>
public sealed record UnionMenuImageBuildResult(string OutputPath, string OutputSha256, IReadOnlyList<string> ChangedImages, bool ByteIdentical)
{
    /// <summary>Resource checks do not substitute for viewing the changed screens in the game.</summary>
    public bool GameRuntimeVerified => false;
}

/// <summary>Edits authenticated RGO menu textures without moving CPK entries or modifying EBOOT.</summary>
public static class RgoUnionMenuImages
{
    /// <summary>The exact original RGO union archive fingerprint.</summary>
    public const string SourceSha256 = "1f4e558bceec1d3286619295d690e6ecff7bbb0d29f8a46cda47afe778c03508";
    /// <summary>The bounded source archive budget, checked before materializing its contents.</summary>
    private static readonly FileLimits Limits = FileLimits.Default with { MaximumInputBytes = 256 * 1024 * 1024 };
    /// <summary>The private-file-verified, fixed menu image layouts.</summary>
    private static readonly UnionMenuResourceProfile[] Profiles =
    [
        new(2529, "685ffba5e91fbeae81bf2c7d25ef97cd7ebdb760a1da1ae41881f6f69440a04e", 28672,
        [
            new(0, 2048, 22528, 0, 256, 512, 272, true),
            new(1, 24576, 2048, 1024, 256, 128, 32, true),
        ]),
        new(2530, "94f9f25dfc5a34b130aaa3e6d87fa467f5dbb99a328c6291e3b5bed870617f05", 51200,
        [
            new(0, 3072, 26624, 0, 256, 512, 272, true),
            new(1, 29696, 20480, 1024, 256, 256, 256, true),
        ]),
        new(2531, "0806f0698b8279bc4af5a8421eac8383425f5ed9f1693732cd2749d569ca4256", 124928,
        [
            new(0, 3072, 18432, 0, 256, 512, 272, true),
            new(1, 21504, 12288, 1024, 256, 256, 128, true),
            new(2, 33792, 90112, 2048, 256, 512, 512, true),
        ]),
        new(2533, "b9ef2bd9648bad1ede0ee98b63c8d170d716e6015e03595efb4cc1cd4a474ae6", 636928,
        [
            new(0, 16128, 65536, 0, 256, 512, 272, true),
            new(1, 81664, 16384, 1024, 256, 256, 224, true),
            new(2, 98048, 38912, 2048, 256, 512, 272, true),
            new(3, 136960, 32768, 3072, 256, 512, 272, true),
            new(4, 169728, 4096, 4096, 256, 256, 96, true),
            new(5, 173824, 16384, 5120, 16, 512, 288, true),
            new(6, 190208, 26624, 5376, 256, 512, 272, true),
            new(7, 216832, 61440, 6400, 256, 512, 256, true),
            new(8, 278272, 36864, 7424, 256, 512, 288, true),
            new(9, 315136, 45056, 8448, 16, 512, 288, true),
            new(10, 360192, 49152, 8704, 16, 512, 352, true),
            new(11, 409344, 36864, 8960, 256, 512, 272, true),
            new(12, 446208, 40960, 9984, 256, 512, 168, true),
            new(13, 487168, 45056, 11008, 256, 512, 168, true),
            new(14, 532224, 40960, 12032, 256, 512, 272, true),
            new(15, 573184, 26624, 13056, 256, 512, 176, true),
            new(16, 599808, 26624, 14080, 256, 512, 176, true),
            new(17, 626432, 10240, 15104, 256, 512, 176, true),
        ]),
    ];

    /// <summary>Exports all supported menu textures into a new source-bound directory.</summary>
    /// <param name="sourcePath">The original union.cpk.</param>
    /// <param name="outputDirectory">A fresh PNG workspace directory.</param>
    /// <returns>The immutable-source manifest.</returns>
    public static UnionMenuImageManifest Export(string sourcePath, string outputDirectory)
    {
        (byte[] source, CriCpkInspection inspection) = ReadSource(sourcePath);
        string output = PathUtilities.NormalizeProtectedPath(outputDirectory, "MENU_OUTPUT_PATH");
        if (Directory.Exists(output) || File.Exists(output)) throw new ToolkitException("MENU_OUTPUT_EXISTS", "Menu export directory must not already exist.");
        List<(string Name, byte[] Data)> files = [];
        foreach (UnionMenuResourceProfile profile in Profiles)
        {
            CriCpkFileInfo entry = inspection.Entries.Single(item => item.Id == profile.Id);
            ReadOnlySpan<byte> resource = source.AsSpan(entry.Offset, entry.PackedSize);
            foreach (IndexedTextureLayout layout in profile.Images)
                files.Add((ImageName(profile.Id, layout.Id), PngWriter.Encode(IndexedTexture.Decode(resource, layout))));
        }
        UnionMenuImageManifest manifest = new() { SourceSha256 = SourceSha256, Files = Profiles.Select(profile => profile with { Images = profile.Images.ToList() }).ToList() };
        files.Add(("union-menu-images.json", JsonSerializer.SerializeToUtf8Bytes(manifest, RgoMenuImages.JsonOptions)));
        string staging = output + ".tmp." + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(staging);
        foreach ((string name, byte[] data) in files) AtomicFile.WriteAllBytes(Path.Combine(staging, name), data);
        Directory.Move(staging, output);
        return manifest;
    }

    /// <summary>Rebuilds PNG edits into a same-size archive, preserving all entry offsets and palettes.</summary>
    /// <param name="sourcePath">The original union.cpk used for export.</param>
    /// <param name="imageDirectory">The PNG workspace with its unchanged manifest.</param>
    /// <param name="outputPath">A separate archive output.</param>
    /// <param name="quantize">Whether new colors may map to the nearest original palette entry.</param>
    /// <returns>The verified resource-level report.</returns>
    public static UnionMenuImageBuildResult Build(string sourcePath, string imageDirectory, string outputPath, bool quantize = false)
    {
        (byte[] source, CriCpkInspection inspection) = ReadSource(sourcePath);
        string directory = PathUtilities.NormalizeProtectedPath(imageDirectory, "MENU_IMAGE_PATH");
        string output = PathUtilities.NormalizeProtectedPath(outputPath, "MENU_OUTPUT_PATH");
        PathUtilities.RequireDifferent(sourcePath, output, "MENU_OUTPUT_SOURCE", "Refusing to overwrite original union.cpk.");
        if (PathUtilities.IsWithinOrSame(output, directory)) throw new ToolkitException("MENU_OUTPUT_PATH", "Menu output must be outside the PNG workspace.");
        byte[] json = BinaryUtilities.ReadAllBytesBounded(PathUtilities.ResolveContainedPath(directory, "union-menu-images.json", "MENU_IMAGE_PATH"), Limits with { MaximumInputBytes = 64 * 1024 });
        UnionMenuImageManifest? manifest = StrictJson.Deserialize<UnionMenuImageManifest>(json, RgoMenuImages.JsonOptions, "MENU_MANIFEST");
        if (manifest is null || manifest.SchemaVersion != 1 || manifest.Game != "rgo-uljm05752" || manifest.SourceSha256 != SourceSha256 || manifest.Files is null || manifest.Files.Count != Profiles.Length)
            throw new ToolkitException("MENU_MANIFEST", "Union menu manifest does not match the supported original archive.");
        for (int index = 0; index < Profiles.Length; index++)
        {
            UnionMenuResourceProfile expected = Profiles[index], actual = manifest.Files[index];
            if (actual is null || actual.Id != expected.Id || actual.SourceSha256 != expected.SourceSha256 || actual.Size != expected.Size || actual.Images is null || !actual.Images.SequenceEqual(expected.Images))
                throw new ToolkitException("MENU_MANIFEST", "Union menu layouts were changed.");
        }
        byte[] rebuiltArchive = source.ToArray();
        List<string> changed = [];
        foreach (UnionMenuResourceProfile profile in Profiles)
        {
            CriCpkFileInfo entry = inspection.Entries.Single(item => item.Id == profile.Id);
            byte[] original = source.AsSpan(entry.Offset, entry.PackedSize).ToArray(), rebuilt = original;
            foreach (IndexedTextureLayout layout in profile.Images)
            {
                string path = PathUtilities.ResolveContainedPath(directory, ImageName(profile.Id, layout.Id), "MENU_IMAGE_PATH");
                RgbaImage image = PngReader.Decode(BinaryUtilities.ReadAllBytesBounded(path, Limits with { MaximumInputBytes = 16 * 1024 * 1024 }));
                byte[] next = IndexedTexture.Replace(rebuilt, layout, image, quantize);
                if (!rebuilt.AsSpan(layout.PixelOffset, layout.Capacity).SequenceEqual(next.AsSpan(layout.PixelOffset, layout.Capacity))) changed.Add($"{profile.Id}:{layout.Id}");
                RgoMenuImages.ProcessImageChecksum(next, layout, apply: true);
                rebuilt = next;
            }
            for (int offset = 0; offset < original.Length; offset++)
                if (original[offset] != rebuilt[offset] && !profile.Images.Any(layout => offset >= layout.PixelOffset && offset < layout.PixelOffset + layout.Capacity))
                    throw new ToolkitException("MENU_PROTECTED_BYTES", "Union menu rebuild changed a palette or unrelated byte.");
            RgoChecksum.Apply(rebuilt);
            if (!RgoChecksum.Verify(rebuilt)) throw new ToolkitException("MENU_CHECKSUM", "Union resource checksum verification failed.");
            foreach (IndexedTextureLayout layout in profile.Images) _ = IndexedTexture.Decode(rebuilt, layout);
            rebuilt.CopyTo(rebuiltArchive.AsSpan(entry.Offset, entry.PackedSize));
        }
        // Payload lengths are fixed; the original metadata, alignment and every unrelated entry remain byte-identical.
        CriCpkInspection verified = CriCpkArchive.Inspect(rebuiltArchive, Limits);
        if (!inspection.Entries.SequenceEqual(verified.Entries)) throw new ToolkitException("MENU_CPK_LAYOUT", "Union menu entry positions changed.");
        UnionMenuImageBuildResult result = new(output, BinaryUtilities.Sha256Hex(rebuiltArchive), changed, source.AsSpan().SequenceEqual(rebuiltArchive));
        AtomicFile.WriteAllBytes(output, rebuiltArchive);
        return result;
    }

    /// <summary>Authenticates the original archive, menu resources and nested image checksums.</summary>
    /// <param name="path">The original union.cpk path.</param>
    /// <returns>The authenticated bytes and metadata-only inspection.</returns>
    private static (byte[] Source, CriCpkInspection Inspection) ReadSource(string path)
    {
        byte[] bytes = BinaryUtilities.ReadAllBytesBounded(path, Limits);
        if (bytes.Length != 223946752 || BinaryUtilities.Sha256Hex(bytes) != SourceSha256)
            throw new ToolkitException("MENU_REVISION", "Union menu profile requires the exact original RGO ULJM05752 union.cpk; NIM and modified archives need a separate profile.");
        CriCpkInspection inspection = CriCpkArchive.Inspect(bytes, Limits);
        foreach (UnionMenuResourceProfile profile in Profiles)
        {
            CriCpkFileInfo? entry = inspection.Entries.SingleOrDefault(item => item.Id == profile.Id);
            if (entry is null || entry.IsCrilayla || entry.PackedSize != profile.Size || entry.ExtractSize != profile.Size)
                throw new ToolkitException("MENU_REVISION", "Union menu entry does not match its verified profile.");
            byte[] resource = bytes.AsSpan(entry.Offset, entry.PackedSize).ToArray();
            if (BinaryUtilities.Sha256Hex(resource) != profile.SourceSha256 || !RgoChecksum.Verify(resource))
                throw new ToolkitException("MENU_CHECKSUM", "Original union menu resource digest or checksum is invalid.");
            foreach (IndexedTextureLayout layout in profile.Images) RgoMenuImages.ProcessImageChecksum(resource, layout, apply: false);
        }
        return (bytes, inspection);
    }

    /// <summary>Returns a fixed source-bound workspace filename.</summary>
    /// <param name="fileId">The original CPK file identifier.</param>
    /// <param name="imageId">The image index within its resource.</param>
    /// <returns>The PNG filename.</returns>
    private static string ImageName(ushort fileId, int imageId) => $"union-{fileId:D4}-{imageId:D2}.png";
}
