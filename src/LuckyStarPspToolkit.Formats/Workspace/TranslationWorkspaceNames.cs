using System.Text.Json;
using System.Text.Encodings.Web;
using LuckyStarPspToolkit.Formats.Common;
using LuckyStarPspToolkit.Formats.Text;

namespace LuckyStarPspToolkit.Formats.Workspace;

/// <summary>A source-bound catalogue for translating repeated dialogue speaker names once.</summary>
public sealed class WorkspaceNameCatalog
{
    /// <summary>The supported catalogue schema revision.</summary>
    public int SchemaVersion { get; set; } = 1;
    /// <summary>The original CPK fingerprint; catalogues cannot be applied to a different archive.</summary>
    public string SourceCpkSha256 { get; set; } = string.Empty;
    /// <summary>The glyph-map fingerprint associated with the exported source names.</summary>
    public string GlyphMapSha256 { get; set; } = string.Empty;
    /// <summary>The unique source names and optional replacements.</summary>
    public List<WorkspaceNameTranslation> Names { get; set; } = [];
}

/// <summary>One source name and its optional replacement in every matching dialogue.</summary>
public sealed class WorkspaceNameTranslation
{
    /// <summary>The exact immutable name exported from the scenario.</summary>
    public string SourceSpeaker { get; set; } = string.Empty;
    /// <summary>A replacement name; null leaves existing translations alone, and empty clears the speaker.</summary>
    public string? TranslationSpeaker { get; set; }
    /// <summary>The number of matching dialogue entries when the catalogue was exported.</summary>
    public int Occurrences { get; set; }
}

/// <summary>Exports and applies source-bound speaker catalogues to isolated workspace copies.</summary>
public static partial class TranslationWorkspaceService
{
    /// <summary>Exports unique names after validating the original workspace against its CPK.</summary>
    /// <param name="workspaceDirectory">The existing translation workspace.</param>
    /// <param name="sourceCpkPath">The original scenario archive.</param>
    /// <param name="outputPath">A catalogue path outside the workspace.</param>
    /// <returns>The exported catalogue.</returns>
    public static WorkspaceNameCatalog ExportNames(string workspaceDirectory, string sourceCpkPath, string outputPath)
    {
        _ = Validate(workspaceDirectory, sourceCpkPath);
        string workspace = PathUtilities.Normalize(workspaceDirectory);
        string output = PathUtilities.NormalizeProtectedPath(outputPath, "WORKSPACE_NAMES_PATH");
        PathUtilities.RequireDifferent(output, sourceCpkPath, "WORKSPACE_NAMES_PATH", "Names output must differ from source CPK.");
        RequireNamesOutputOutside(workspace, output);
        TranslationWorkspaceManifest manifest = ReadJson<TranslationWorkspaceManifest>(Path.Combine(workspace, ManifestFileName));
        Dictionary<string, int> names = new(StringComparer.Ordinal);
        foreach (TranslationWorkspaceScriptReference reference in manifest.Scripts)
        {
            TranslationScriptFile script = ReadJson<TranslationScriptFile>(PathUtilities.ResolveContainedPath(workspace, reference.File, "WORKSPACE_PATH"));
            foreach (TranslationDialog dialog in script.Dialogs)
            {
                if (dialog.SourceSpeaker.Length > 0)
                {
                    names[dialog.SourceSpeaker] = names.GetValueOrDefault(dialog.SourceSpeaker) + 1;
                }
            }
        }
        WorkspaceNameCatalog catalog = new()
        {
            SourceCpkSha256 = manifest.SourceCpkSha256,
            GlyphMapSha256 = manifest.GlyphMapSha256,
            Names = names.OrderBy(static pair => pair.Key, StringComparer.Ordinal)
                .Select(static pair => new WorkspaceNameTranslation { SourceSpeaker = pair.Key, Occurrences = pair.Value }).ToList()
        };
        AtomicFile.WriteAllBytes(output, SerializeReadableNames(catalog));
        return catalog;
    }

    /// <summary>Applies names to a fresh copy, validating all edits before publishing its directory.</summary>
    /// <param name="workspaceDirectory">The existing translation workspace, preserved unchanged.</param>
    /// <param name="sourceCpkPath">The original scenario archive.</param>
    /// <param name="catalogPath">The edited exported names catalogue.</param>
    /// <param name="outputDirectory">A new directory outside the original workspace.</param>
    /// <returns>The fully validated output workspace.</returns>
    public static WorkspaceValidationResult ApplyNames(string workspaceDirectory, string sourceCpkPath, string catalogPath, string outputDirectory)
    {
        _ = Validate(workspaceDirectory, sourceCpkPath);
        string workspace = PathUtilities.Normalize(workspaceDirectory);
        string output = PathUtilities.NormalizeProtectedPath(outputDirectory, "WORKSPACE_NAMES_PATH");
        RequireNamesOutputOutside(workspace, output);
        if (Directory.Exists(output) || File.Exists(output))
        {
            throw new ToolkitException("WORKSPACE_NAMES_OUTPUT_EXISTS", "Names output directory must not already exist.");
        }
        TranslationWorkspaceManifest manifest = ReadJson<TranslationWorkspaceManifest>(Path.Combine(workspace, ManifestFileName));
        WorkspaceNameCatalog catalog = ReadJson<WorkspaceNameCatalog>(catalogPath);
        if (catalog.SchemaVersion != 1 || catalog.SourceCpkSha256 != manifest.SourceCpkSha256 || catalog.GlyphMapSha256 != manifest.GlyphMapSha256)
        {
            throw new ToolkitException("WORKSPACE_NAMES_SOURCE", "Names catalogue does not match this workspace's source archive and glyph map.");
        }
        if (catalog.Names is null || catalog.Names.Any(static item => item is null || item.SourceSpeaker is null))
        {
            throw new ToolkitException("WORKSPACE_NAMES_SCHEMA", "Names catalogue contains null entries.");
        }
        Dictionary<string, WorkspaceNameTranslation> replacements = new(StringComparer.Ordinal);
        foreach (WorkspaceNameTranslation item in catalog.Names)
        {
            if (!replacements.TryAdd(item.SourceSpeaker, item))
            {
                throw new ToolkitException("WORKSPACE_NAMES_DUPLICATE", "Names catalogue contains a duplicate source speaker.");
            }
        }
        byte[] mapBytes = BinaryUtilities.ReadAllBytesBounded(PathUtilities.ResolveContainedPath(workspace, manifest.GlyphMapFile, "WORKSPACE_PATH"));
        GlyphMap map = GlyphMap.FromUtf8(mapBytes);
        List<(string File, TranslationScriptFile Script)> scripts = [];
        Dictionary<string, int> occurrences = new(StringComparer.Ordinal);
        foreach (TranslationWorkspaceScriptReference reference in manifest.Scripts)
        {
            TranslationScriptFile script = ReadJson<TranslationScriptFile>(PathUtilities.ResolveContainedPath(workspace, reference.File, "WORKSPACE_PATH"));
            scripts.Add((reference.File, script));
            foreach (TranslationDialog dialog in script.Dialogs)
            {
                occurrences[dialog.SourceSpeaker] = occurrences.GetValueOrDefault(dialog.SourceSpeaker) + 1;
                if (replacements.TryGetValue(dialog.SourceSpeaker, out WorkspaceNameTranslation? name) && name.TranslationSpeaker is not null)
                {
                    _ = map.Encode(name.TranslationSpeaker);
                    dialog.TranslationSpeaker = name.TranslationSpeaker;
                }
            }
        }
        foreach (WorkspaceNameTranslation item in catalog.Names)
        {
            if (!occurrences.TryGetValue(item.SourceSpeaker, out int count) || count != item.Occurrences)
            {
                throw new ToolkitException("WORKSPACE_NAMES_OCCURRENCES", "A catalogue source name or occurrence count differs from the workspace.");
            }
        }
        string staging = output + ".tmp." + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(staging);
        // A failed validation leaves its isolated staging directory for diagnosis; originals are never edited.
        WriteJson(Path.Combine(staging, ManifestFileName), manifest);
        AtomicFile.WriteAllBytes(PathUtilities.ResolveContainedPath(staging, manifest.GlyphMapFile, "WORKSPACE_PATH"), mapBytes);
        foreach ((string file, TranslationScriptFile script) in scripts)
        {
            AtomicFile.WriteAllBytes(PathUtilities.ResolveContainedPath(staging, file, "WORKSPACE_PATH"), SerializeReadableNames(script));
        }
        _ = Validate(staging, sourceCpkPath);
        Directory.Move(staging, output);
        return Validate(output, sourceCpkPath);
    }

    /// <summary>Rejects writes into or above the original workspace tree.</summary>
    /// <param name="workspace">The normalized source workspace directory.</param>
    /// <param name="output">The normalized output file or directory.</param>
    private static void RequireNamesOutputOutside(string workspace, string output)
    {
        if (PathUtilities.IsWithinOrSame(output, workspace) || PathUtilities.IsWithinOrSame(workspace, output))
        {
            throw new ToolkitException("WORKSPACE_NAMES_PATH", "Names output must be outside, and not an ancestor of, the original workspace.");
        }
    }

    /// <summary>Produces readable UTF-8 JSON without changing source fields or literal newline semantics.</summary>
    /// <param name="value">The catalogue or scenario model.</param>
    /// <typeparam name="T">The serializable catalogue or scenario model type.</typeparam>
    /// <returns>The bounded model's serialized bytes.</returns>
    private static byte[] SerializeReadableNames<T>(T value)
        => JsonSerializer.SerializeToUtf8Bytes(value, new JsonSerializerOptions(EbootSizePatchPlan.JsonOptions)
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never
        });
}
