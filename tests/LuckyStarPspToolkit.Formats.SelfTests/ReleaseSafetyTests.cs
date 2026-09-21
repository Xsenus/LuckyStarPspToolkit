using System.Text.Json;
using LuckyStarPspToolkit.Formats.Common;
using LuckyStarPspToolkit.Formats.Scripts;
using LuckyStarPspToolkit.Formats.Text;
using LuckyStarPspToolkit.Formats.Workspace;

/// <summary>Regression coverage for manifest ambiguity, path containment, and snapshot consistency.</summary>
internal static partial class SelfTestRunner
{
    /// <summary>Rejects repeated and escaped JSON keys while allowing equal keys in separate object scopes.</summary>
    private static void TestStrictJson()
    {
        var options = new JsonSerializerOptions();
        Throws("JSON_DUPLICATE_PROPERTY", () => StrictJson.Deserialize<JsonElement>(
            "{\"value\":1,\"value\":2}"u8, options, "JSON_TEST"));
        Throws("JSON_DUPLICATE_PROPERTY", () => StrictJson.Deserialize<JsonElement>(
            "{\"value\":1,\"\\u0076alue\":2}"u8, options, "JSON_TEST"));
        Throws("JSON_DUPLICATE_PROPERTY", () => StrictJson.Deserialize<JsonElement>(
            "{\"Value\":1,\"value\":2}"u8,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, "JSON_TEST"));
        JsonElement valid = StrictJson.Deserialize<JsonElement>(
            "[{\"value\":1},{\"value\":2}]"u8, options, "JSON_TEST");
        Equal(2, valid.GetArrayLength());
        Equal(1, StrictJson.Deserialize<JsonElement>("\uFEFF{\"value\":1}"u8, options, "JSON_TEST").GetProperty("value").GetInt32());
        Throws("JSON_TEST", () => StrictJson.Deserialize<JsonElement>("{\"x\":1,}"u8, options, "JSON_TEST"));
        Throws("JSON_TEST", () => StrictJson.Deserialize<TranslationWorkspaceManifest>("null"u8, options, "JSON_TEST"));
    }

    /// <summary>Checks filesystem-root containment and rejects Windows-style escapes on every host platform.</summary>
    private static void TestRootPathContainment()
    {
        string root = Path.GetPathRoot(Path.GetFullPath(Path.GetTempPath()))!;
        string child = Path.Combine(root, "lsptool-test-does-not-exist");
        Equal(true, PathUtilities.IsWithinOrSame(child, root));
        Equal(PathUtilities.Normalize(child), PathUtilities.ResolveContainedPath(root, "lsptool-test-does-not-exist", "PATH_TEST"));
        Equal(false, PathUtilities.IsWithinOrSame(Path.Combine(root, "foo-other"), Path.Combine(root, "foo")));
        Throws("PATH_TEST", () => PathUtilities.ResolveContainedPath(root, "..\\escape", "PATH_TEST"));
        Throws("PATH_TEST", () => PathUtilities.ResolveContainedPath(root, "C:\\escape", "PATH_TEST"));
    }

    /// <summary>Confirms that a parsed map does not change when its source byte array changes afterwards.</summary>
    private static void TestGlyphSnapshot()
    {
        byte[] bytes = BinaryUtilities.Utf8("А\nБ\n");
        string hash = BinaryUtilities.Sha256Hex(bytes);
        GlyphMap map = GlyphMap.FromUtf8(bytes);
        Throws("GLYPH_MAP_LIMIT", () => GlyphMap.FromUtf8("А\nБ\n"u8, new FileLimits(MaximumGlyphMapEntries: 1)));
        Throws("GLYPH_MAP_LIMIT", () => GlyphMap.FromUtf8("ABCDE\n"u8, new FileLimits(MaximumGlyphEntryCharacters: 4)));
        Throws("GLYPH_MAP_LIMIT", () => GlyphMap.FromUtf8("A\n"u8, new FileLimits(MaximumGlyphMapCharacters: 1)));
        Throws("GLYPH_MAP_LIMIT", () => GlyphMap.FromLines(["ABCDE"], new FileLimits(MaximumGlyphEntryCharacters: 4)));
        Throws("GLYPH_MAP_LINE", () => GlyphMap.FromLines(["A\nB"]));
        Array.Clear(bytes);
        Equal(hash, map.SourceSha256);
        Equal("А", map.GetText(0));
        Throws("GLYPH_MAP_LIMIT", () => GlyphMap.FromUtf8("А\n"u8, new FileLimits(MaximumTextBytes: 1)));
        Throws("GLYPH_MAP_UTF8", () => GlyphMap.FromUtf8(new byte[] { 0xFF, 0xFE }));
    }

    /// <summary>Rejects build outputs inside a translator's workspace before reading or writing any source files.</summary>
    private static void TestWorkspaceOutputIsolation()
    {
        string workspace = Path.Combine(Path.GetTempPath(), "lsp-isolation-" + Guid.NewGuid().ToString("N"));
        string source = workspace + ".cpk";
        Throws("WORKSPACE_OUTPUT_INSIDE", () => TranslationWorkspaceService.Build(
            workspace, source, Path.Combine(workspace, "workspace.json")));
        Throws("WORKSPACE_OUTPUT_INSIDE", () => TranslationWorkspaceService.Build(
            workspace, source, workspace + ".patched.cpk", Path.Combine(workspace, "glyph-map.txt")));
        Equal(false, Directory.Exists(workspace));
    }
}
