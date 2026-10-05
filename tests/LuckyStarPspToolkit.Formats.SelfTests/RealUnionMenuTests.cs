using LuckyStarPspToolkit.Cli;
using LuckyStarPspToolkit.Formats.Common;
using LuckyStarPspToolkit.Formats.Cri;
using LuckyStarPspToolkit.Formats.Fonts;
using LuckyStarPspToolkit.Formats.Images;
using LuckyStarPspToolkit.Formats.Scripts;

/// <summary>Private integration checks for the original RGO union menu resources.</summary>
internal static partial class SelfTestRunner
{
    /// <summary>Checks all 25 menu edits, fixed archive positions, protected bytes and source/output refusal behavior.</summary>
    /// <param name="sourcePath">The private original union archive.</param>
    private static void TestRealUnionMenus(string sourcePath)
    {
        byte[] original = BinaryUtilities.ReadAllBytesBounded(sourcePath, FileLimits.Default with { MaximumInputBytes = 256 * 1024 * 1024 });
        CriCpkInspection inspection = CriCpkArchive.Inspect(original);
        string root = Path.Combine(Path.GetTempPath(), "lsptool-union-menu-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string images = Path.Combine(root, "images"), output = Path.Combine(root, "union-patched.cpk");
            UnionMenuImageManifest manifest = RgoUnionMenuImages.Export(sourcePath, images);
            Equal(25, manifest.Files.Sum(file => file.Images.Count));
            UnionMenuImageBuildResult noOp = RgoUnionMenuImages.Build(sourcePath, images, output);
            True(noOp.ByteIdentical, "Union menu no-op must preserve the complete original archive.");
            SequenceEqual(original, File.ReadAllBytes(output));
            Dictionary<string, byte[]> expected = [];
            foreach (UnionMenuResourceProfile profile in manifest.Files)
            {
                CriCpkFileInfo entry = inspection.Entries.Single(item => item.Id == profile.Id);
                foreach (IndexedTextureLayout layout in profile.Images)
                {
                    string pngPath = Path.Combine(images, $"union-{profile.Id:D4}-{layout.Id:D2}.png");
                    RgbaImage png = PngReader.Decode(File.ReadAllBytes(pngPath));
                    int paletteIndex = Enumerable.Range(0, layout.ColorCount).First(index =>
                        !original.AsSpan(entry.Offset + layout.PaletteOffset + index * 4, 4).SequenceEqual(png.Pixels.AsSpan(0, 4))
                        && (original[entry.Offset + layout.PaletteOffset + index * 4 + 3] != 0 || png.Pixels[3] != 0));
                    original.AsSpan(entry.Offset + layout.PaletteOffset + paletteIndex * 4, 4).CopyTo(png.Pixels.AsSpan(0, 4));
                    expected.Add($"{profile.Id}:{layout.Id}", png.Pixels.AsSpan(0, 4).ToArray());
                    File.WriteAllBytes(pngPath, PngWriter.Encode(png));
                }
            }
            UnionMenuImageBuildResult result = RgoUnionMenuImages.Build(sourcePath, images, output);
            Equal(25, result.ChangedImages.Count);
            True(!result.ByteIdentical && !result.GameRuntimeVerified, "Resource edits must not claim gameplay acceptance.");
            byte[] changed = File.ReadAllBytes(output);
            Equal(original.Length, changed.Length);
            True(inspection.Entries.SequenceEqual(CriCpkArchive.Inspect(changed).Entries), "Union entry positions and sizes must remain unchanged.");
            int cursor = 0;
            foreach (CriCpkFileInfo entry in inspection.Entries.Where(entry => manifest.Files.Any(file => file.Id == entry.Id)).OrderBy(entry => entry.Offset))
            {
                SequenceEqual(original.AsSpan(cursor, entry.Offset - cursor).ToArray(), changed.AsSpan(cursor, entry.Offset - cursor).ToArray());
                UnionMenuResourceProfile profile = manifest.Files.Single(file => file.Id == entry.Id);
                byte[] resource = changed.AsSpan(entry.Offset, entry.PackedSize).ToArray();
                True(RgoChecksum.Verify(resource), "Edited union resource requires a valid checksum.");
                foreach (IndexedTextureLayout layout in profile.Images)
                {
                    SequenceEqual(expected[$"{profile.Id}:{layout.Id}"], IndexedTexture.Decode(resource, layout).Pixels.AsSpan(0, 4).ToArray());
                    SequenceEqual(original.AsSpan(entry.Offset + layout.PaletteOffset, layout.ColorCount * 4).ToArray(), resource.AsSpan(layout.PaletteOffset, layout.ColorCount * 4).ToArray());
                }
                cursor = entry.Offset + entry.PackedSize;
            }
            SequenceEqual(original.AsSpan(cursor).ToArray(), changed.AsSpan(cursor).ToArray());
            Equal(3, CommandApplication.RunCore(["menu-union-build", sourcePath, images, sourcePath]));
            Equal(3, CommandApplication.RunCore(["menu-union-build", sourcePath, images, output, "--json", Path.Combine(images, "report.json")]));
            manifest.Files[0] = manifest.Files[0] with { Images = [] };
            File.WriteAllBytes(Path.Combine(images, "union-menu-images.json"), System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(manifest, RgoMenuImages.JsonOptions));
            Equal(3, CommandApplication.RunCore(["menu-union-build", sourcePath, images, output]));
            SequenceEqual(changed, File.ReadAllBytes(output));
            SequenceEqual(original, File.ReadAllBytes(sourcePath));
        }
        finally { Directory.Delete(root, true); }
    }
}
