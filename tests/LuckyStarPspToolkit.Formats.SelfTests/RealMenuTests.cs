using LuckyStarPspToolkit.Cli;
using LuckyStarPspToolkit.Formats.Fonts;
using LuckyStarPspToolkit.Formats.Images;
using LuckyStarPspToolkit.Formats.Scripts;
using System.Buffers.Binary;

/// <summary>Private real-game integration tests; original PR resources are never fixture or release contents.</summary>
internal static partial class SelfTestRunner
{
    /// <summary>Verifies all ten real RGO menu textures, no-op bytes, changed pixels and protected-output refusals.</summary>
    /// <param name="sourcePath">The private original RGO ULJM05752 pr.bin.</param>
    private static void TestRealMenus(string sourcePath)
    {
        byte[] original = File.ReadAllBytes(sourcePath);
        string root = Path.Combine(Path.GetTempPath(), "lsptool-real-menu-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string images = Path.Combine(root, "images"), output = Path.Combine(root, "pr-patched.bin");
            Equal(0, CommandApplication.RunCore(["menu-export", sourcePath, images]));
            Equal(0, CommandApplication.RunCore(["menu-build", sourcePath, images, output]));
            SequenceEqual(original, File.ReadAllBytes(output));
            Equal(3, CommandApplication.RunCore(["menu-export", sourcePath, images]));
            string imagePath = Path.Combine(images, "pr-0000.png");
            RgbaImage png = PngReader.Decode(File.ReadAllBytes(imagePath));
            byte[] before = png.Pixels.ToArray();
            int pixelOffset = Enumerable.Range(0, png.Pixels.Length / 4).First(index => png.Pixels[index * 4 + 3] != 0) * 4;
            png.Pixels.AsSpan(pixelOffset, 4).Clear();
            File.WriteAllBytes(imagePath, PngWriter.Encode(png));
            Equal(0, CommandApplication.RunCore(["menu-build", sourcePath, images, output]));
            byte[] changed = File.ReadAllBytes(output);
            True(!changed.SequenceEqual(original), "Changing a visible real menu pixel must change its resource.");
            IndexedTextureLayout layout = new(0, 0, 0x10000, 0x39C00, 16, 512, 256, false);
            RgbaImage actual = IndexedTexture.Decode(changed, layout);
            True(actual.Pixels[pixelOffset + 3] == 0, "Edited real menu pixel must become transparent.");
            SequenceEqual(before.AsSpan(0, pixelOffset).ToArray(), actual.Pixels.AsSpan(0, pixelOffset).ToArray());
            SequenceEqual(before.AsSpan(pixelOffset + 4).ToArray(), actual.Pixels.AsSpan(pixelOffset + 4).ToArray());
            SequenceEqual(original.AsSpan(0x10000, original.Length - 0x10000 - 16).ToArray(), changed.AsSpan(0x10000, changed.Length - 0x10000 - 16).ToArray());
            True(RgoChecksum.Verify(changed), "Edited PR resource must retain a valid trailing game checksum.");
            True(!original.AsSpan(original.Length - 16).SequenceEqual(changed.AsSpan(changed.Length - 16)), "Pixel edit must update the stored checksum.");
            Equal(3, CommandApplication.RunCore(["menu-build", sourcePath, images, sourcePath]));
            Equal(3, CommandApplication.RunCore(["menu-build", sourcePath, images, output, "--json", imagePath]));
            File.WriteAllBytes(imagePath, PngWriter.Encode(new RgbaImage(1, 1, new byte[4])));
            Equal(3, CommandApplication.RunCore(["menu-build", sourcePath, images, output]));
            SequenceEqual(changed, File.ReadAllBytes(output));
            SequenceEqual(original, File.ReadAllBytes(sourcePath));
            File.WriteAllBytes(imagePath, PngWriter.Encode(new RgbaImage(png.Width, png.Height, before)));
            foreach (IndexedTextureLayout compressed in RgoMenuImages.Export(sourcePath, Path.Combine(root, "checksum-layouts")).Images.Where(item => item.Compressed))
            {
                string compressedPath = Path.Combine(images, $"pr-{compressed.Id:D4}.png");
                RgbaImage edited = PngReader.Decode(File.ReadAllBytes(compressedPath));
                int paletteIndex = Enumerable.Range(0, compressed.ColorCount).First(index => !original.AsSpan(compressed.PaletteOffset + index * 4, 4).SequenceEqual(edited.Pixels.AsSpan(0, 4)));
                original.AsSpan(compressed.PaletteOffset + paletteIndex * 4, 4).CopyTo(edited.Pixels.AsSpan(0, 4));
                File.WriteAllBytes(compressedPath, PngWriter.Encode(edited));
                Equal(0, CommandApplication.RunCore(["menu-build", sourcePath, images, output]));
                byte[] rebuilt = File.ReadAllBytes(output);
                True(RgoChecksum.Verify(rebuilt), "Compressed edit must update the complete PR checksum.");
                int count = BinaryPrimitives.ReadInt32LittleEndian(rebuilt.AsSpan(compressed.PixelOffset));
                int terminal = BinaryPrimitives.ReadInt32LittleEndian(rebuilt.AsSpan(compressed.PixelOffset + 4 + count * 4));
                int checksumLength = (terminal + 2047) / 2048 * 2048;
                True(RgoChecksum.Verify(rebuilt.AsSpan(compressed.PixelOffset, checksumLength)), "Compressed edit must update its aligned image checksum.");
                SequenceEqual(edited.Pixels.AsSpan(0, 4).ToArray(), IndexedTexture.Decode(rebuilt, compressed).Pixels.AsSpan(0, 4).ToArray());
            }
        }
        finally { Directory.Delete(root, true); }
    }
}
