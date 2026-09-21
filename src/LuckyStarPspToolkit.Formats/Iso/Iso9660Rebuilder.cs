using System.Buffers;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using LuckyStarPspToolkit.Formats.Common;

namespace LuckyStarPspToolkit.Formats.Iso;

/// <summary>
/// Represents the toolkit's ISO 9660 patch manifest model or service.
/// </summary>
public sealed class Iso9660PatchManifest
{
    /// <summary>The schema value used by this model or operation.</summary>
    public required string Schema { get; init; }
    /// <summary>The expected source sha256 value used by this model or operation.</summary>
    public string? ExpectedSourceSha256 { get; init; }
    /// <summary>The replacements value used by this model or operation.</summary>
    public required List<Iso9660PatchManifestEntry> Replacements { get; init; }
}

/// <summary>
/// Represents the toolkit's ISO 9660 patch manifest entry model or service.
/// </summary>
public sealed class Iso9660PatchManifestEntry
{
    /// <summary>The iso path value used by this model or operation.</summary>
    public required string IsoPath { get; init; }
    /// <summary>The source file value used by this model or operation.</summary>
    public required string SourceFile { get; init; }
    /// <summary>The expected original size value used by this model or operation.</summary>
    public long? ExpectedOriginalSize { get; init; }
    /// <summary>The expected original sha256 value used by this model or operation.</summary>
    public string? ExpectedOriginalSha256 { get; init; }
    /// <summary>The expected replacement size value used by this model or operation.</summary>
    public long? ExpectedReplacementSize { get; init; }
    /// <summary>The expected replacement sha256 value used by this model or operation.</summary>
    public string? ExpectedReplacementSha256 { get; init; }
}

/// <summary>
/// Represents immutable ISO 9660 replacement request data exchanged by the toolkit.
/// </summary>
/// <param name="IsoPath">The iso path value used by this model or operation.</param>
/// <param name="ReplacementFile">The replacement file value used by this model or operation.</param>
/// <param name="ExpectedOriginalSize">The expected original size value used by this model or operation.</param>
/// <param name="ExpectedOriginalSha256">The expected original sha256 value used by this model or operation.</param>
/// <param name="ExpectedReplacementSize">The expected replacement size value used by this model or operation.</param>
/// <param name="ExpectedReplacementSha256">The expected replacement sha256 value used by this model or operation.</param>
public sealed record Iso9660ReplacementRequest(
    string IsoPath,
    string ReplacementFile,
    long? ExpectedOriginalSize = null,
    string? ExpectedOriginalSha256 = null,
    long? ExpectedReplacementSize = null,
    string? ExpectedReplacementSha256 = null);

/// <summary>
/// Represents the toolkit's ISO 9660 prepared patch model or service.
/// </summary>
public sealed class Iso9660PreparedPatch
{
    /// <summary>
    /// Initializes a new instance with validated constructor state.
    /// </summary>
    /// <param name="manifestPath">The manifest path value.</param>
    /// <param name="manifestSha256">The manifest SHA 256 value.</param>
    /// <param name="expectedSourceSha256">The expected source SHA 256 value.</param>
    /// <param name="replacements">The replacements value.</param>
    internal Iso9660PreparedPatch(
        string manifestPath,
        string manifestSha256,
        string? expectedSourceSha256,
        IReadOnlyList<Iso9660ReplacementRequest> replacements)
    {
        ManifestPath = manifestPath;
        ManifestSha256 = manifestSha256;
        ExpectedSourceSha256 = expectedSourceSha256;
        Iso9660ReplacementRequest[] replacementSnapshot = replacements.ToArray();
        Replacements = Array.AsReadOnly(replacementSnapshot);
        string[] replacementFiles = replacementSnapshot
            .Select(static replacement => replacement.ReplacementFile)
            .ToArray();
        ReplacementFiles = Array.AsReadOnly(replacementFiles);
    }

    /// <summary>The manifest path value used by this model or operation.</summary>
    public string ManifestPath { get; }
    /// <summary>The manifest sha256 value used by this model or operation.</summary>
    public string ManifestSha256 { get; }
    /// <summary>The expected source sha256 value used by this model or operation.</summary>
    public string? ExpectedSourceSha256 { get; }
    /// <summary>The replacements value used by this model or operation.</summary>
    public IReadOnlyList<Iso9660ReplacementRequest> Replacements { get; }
    /// <summary>The replacement files value used by this model or operation.</summary>
    public IReadOnlyList<string> ReplacementFiles { get; }
}

/// <summary>
/// Represents the toolkit's ISO 9660 replacement report model or service.
/// </summary>
public sealed class Iso9660ReplacementReport
{
    /// <summary>The iso path value used by this model or operation.</summary>
    public required string IsoPath { get; init; }
    /// <summary>The replacement file value used by this model or operation.</summary>
    public required string ReplacementFile { get; init; }
    /// <summary>The original size value used by this model or operation.</summary>
    public required long OriginalSize { get; init; }
    /// <summary>The original sha256 value used by this model or operation.</summary>
    public required string OriginalSha256 { get; init; }
    /// <summary>The replacement size value used by this model or operation.</summary>
    public required long ReplacementSize { get; init; }
    /// <summary>The replacement sha256 value used by this model or operation.</summary>
    public required string ReplacementSha256 { get; init; }
    /// <summary>The original logical block value used by this model or operation.</summary>
    public required uint OriginalLogicalBlock { get; init; }
    /// <summary>The replacement logical block value used by this model or operation.</summary>
    public required uint ReplacementLogicalBlock { get; init; }
    /// <summary>The directory record offset value used by this model or operation.</summary>
    public required long DirectoryRecordOffset { get; init; }
}

/// <summary>
/// Represents the toolkit's ISO 9660 rebuild report model or service.
/// </summary>
public sealed class Iso9660RebuildReport
{
    /// <summary>The schema value used by this model or operation.</summary>
    public string Schema { get; init; } = "lucky-star-psp.iso9660-rebuild.v1";
    /// <summary>The toolkit version that created this record.</summary>
    public string ToolVersion { get; init; } = ToolkitBuildInfo.Version;
    /// <summary>The UTC creation time of the manifest; it does not prove runtime compatibility.</summary>
    public DateTimeOffset CreatedUtc { get; init; } = DateTimeOffset.UtcNow;
    /// <summary>The source value used by this model or operation.</summary>
    public required string Source { get; init; }
    /// <summary>The manifest value used by this model or operation.</summary>
    public string? Manifest { get; init; }
    /// <summary>The manifest sha256 value used by this model or operation.</summary>
    public string? ManifestSha256 { get; init; }
    /// <summary>The output value used by this model or operation.</summary>
    public required string Output { get; init; }
    /// <summary>The volume identifier value used by this model or operation.</summary>
    public required string VolumeIdentifier { get; init; }
    /// <summary>The hexadecimal SHA-256 of the exact input snapshot used by this object.</summary>
    public required string SourceSha256 { get; init; }
    /// <summary>The output sha256 value used by this model or operation.</summary>
    public required string OutputSha256 { get; init; }
    /// <summary>The source size value used by this model or operation.</summary>
    public required long SourceSize { get; init; }
    /// <summary>The output size value used by this model or operation.</summary>
    public required long OutputSize { get; init; }
    /// <summary>The original volume block count value used by this model or operation.</summary>
    public required uint OriginalVolumeBlockCount { get; init; }
    /// <summary>The output volume block count value used by this model or operation.</summary>
    public required uint OutputVolumeBlockCount { get; init; }
    /// <summary>The logical block size value used by this model or operation.</summary>
    public required int LogicalBlockSize { get; init; }
    /// <summary>The replacements value used by this model or operation.</summary>
    public required IReadOnlyList<Iso9660ReplacementReport> Replacements { get; init; }
}

/// <summary>
/// Provides ISO 9660 rebuilder operations with strict bounds and format validation.
/// </summary>
public static class Iso9660Rebuilder
{
    /// <summary>The fixed manifest schema value used by this format or revision.</summary>
    public const string ManifestSchema = "lucky-star-psp.iso-patch-manifest.v1";

    /// <summary>The json options value used by this model or operation.</summary>
    public static JsonSerializerOptions JsonOptions { get; } = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = false,
        AllowTrailingCommas = false,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    /// <summary>
    /// Loads a strict ISO patch manifest and rebuilds a new verified ISO image.
    /// </summary>
    /// <param name="sourceIsoPath">The source ISO path value.</param>
    /// <param name="manifestPath">The manifest path value.</param>
    /// <param name="outputIsoPath">The output ISO path value.</param>
    /// <param name="limits">Optional conservative safety limits; defaults are used when omitted.</param>
    /// <returns>The validated operation result.</returns>
    /// <remarks>Malformed input or violated preconditions are rejected before a persistent output is committed.</remarks>
    public static Iso9660RebuildReport ApplyManifest(
        string sourceIsoPath,
        string manifestPath,
        string outputIsoPath,
        FileLimits? limits = null)
    {
        limits ??= FileLimits.Default;
        Iso9660PreparedPatch loaded = LoadPatchManifest(manifestPath, limits);
        return ApplyPreparedPatch(sourceIsoPath, loaded, outputIsoPath, limits);
    }

    /// <summary>
    /// Applies an already validated ISO patch plan to a new output image.
    /// </summary>
    /// <param name="sourceIsoPath">The source ISO path value.</param>
    /// <param name="patch">The patch value.</param>
    /// <param name="outputIsoPath">The output ISO path value.</param>
    /// <param name="limits">Optional conservative safety limits; defaults are used when omitted.</param>
    /// <returns>The validated operation result.</returns>
    /// <remarks>Malformed input or violated preconditions are rejected before a persistent output is committed.</remarks>
    public static Iso9660RebuildReport ApplyPreparedPatch(
        string sourceIsoPath,
        Iso9660PreparedPatch patch,
        string outputIsoPath,
        FileLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(patch);
        limits ??= FileLimits.Default;
        return RebuildCore(
            sourceIsoPath,
            patch.Replacements,
            outputIsoPath,
            patch.ExpectedSourceSha256,
            patch.ManifestPath,
            patch.ManifestSha256,
            limits);
    }

    /// <summary>
    /// Resolves manifest replacement files while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="manifestPath">The manifest path value.</param>
    /// <param name="limits">Optional conservative safety limits; defaults are used when omitted.</param>
    /// <returns>The validated operation result.</returns>
    public static IReadOnlyList<string> ResolveManifestReplacementFiles(
        string manifestPath,
        FileLimits? limits = null)
    {
        limits ??= FileLimits.Default;
        return LoadPatchManifest(manifestPath, limits).ReplacementFiles;
    }

    /// <summary>
    /// Loads, validates, snapshots, and resolves an ISO patch manifest.
    /// </summary>
    /// <param name="manifestPath">The manifest path value.</param>
    /// <param name="limits">Optional conservative safety limits; defaults are used when omitted.</param>
    /// <returns>The validated operation result.</returns>
    public static Iso9660PreparedPatch LoadPatchManifest(
        string manifestPath,
        FileLimits? limits = null)
    {
        limits ??= FileLimits.Default;
        string normalizedManifest = PathUtilities.NormalizeProtectedPath(
            manifestPath,
            "ISO_MANIFEST_REPARSE_POINT");
        if (!File.Exists(normalizedManifest))
        {
            throw new ToolkitException("ISO_MANIFEST_NOT_FOUND", $"ISO patch manifest not found: {normalizedManifest}");
        }

        FileInfo manifestInfo = new(normalizedManifest);
        if (manifestInfo.Length > limits.MaximumTextBytes || manifestInfo.Length > int.MaxValue)
        {
            throw new ToolkitException(
                "ISO_MANIFEST_TOO_LARGE",
                $"ISO patch manifest is {manifestInfo.Length} bytes; configured limit is {limits.MaximumTextBytes} bytes.");
        }

        byte[] manifestBytes = ReadManifestSnapshot(normalizedManifest, limits);
        string manifestSha256 = BinaryUtilities.Sha256Hex(manifestBytes);
        Iso9660PatchManifest manifest;
        try
        {
            manifest = StrictJson.Deserialize<Iso9660PatchManifest>(
                manifestBytes,
                JsonOptions, "ISO_MANIFEST_JSON")
                ?? throw new ToolkitException("ISO_MANIFEST_JSON", "ISO patch manifest JSON is null.");
        }
        catch (JsonException ex)
        {
            throw new ToolkitException("ISO_MANIFEST_JSON", "ISO patch manifest JSON is invalid or contains unknown properties.", ex);
        }

        if (!string.Equals(manifest.Schema, ManifestSchema, StringComparison.Ordinal))
        {
            throw new ToolkitException(
                "ISO_MANIFEST_SCHEMA",
                $"Unsupported ISO patch manifest schema '{manifest.Schema}'. Expected '{ManifestSchema}'.");
        }
        if (manifest.Replacements is null || manifest.Replacements.Count == 0)
        {
            throw new ToolkitException("ISO_MANIFEST_EMPTY", "ISO patch manifest must contain at least one replacement.");
        }
        if (manifest.Replacements.Count > limits.MaximumIsoReplacements)
        {
            throw new ToolkitException(
                "ISO_REPLACEMENT_LIMIT",
                $"ISO patch manifest contains {manifest.Replacements.Count} replacements; configured limit is {limits.MaximumIsoReplacements}.");
        }

        string manifestDirectory = Path.GetDirectoryName(normalizedManifest)
            ?? throw new ToolkitException("ISO_MANIFEST_DIRECTORY", "Cannot determine ISO patch manifest directory.");
        var requests = new List<Iso9660ReplacementRequest>(manifest.Replacements.Count);
        foreach (Iso9660PatchManifestEntry? entry in manifest.Replacements)
        {
            if (entry is null)
            {
                throw new ToolkitException("ISO_MANIFEST_ENTRY", "ISO patch manifest contains a null replacement entry.");
            }
            if (string.IsNullOrWhiteSpace(entry.IsoPath))
            {
                throw new ToolkitException("ISO_MANIFEST_ENTRY", "ISO patch manifest entry has an empty ISO path.");
            }
            if (string.IsNullOrWhiteSpace(entry.SourceFile))
            {
                throw new ToolkitException("ISO_MANIFEST_ENTRY", $"ISO patch manifest entry for {entry.IsoPath} has an empty source file.");
            }
            string replacementPath = PathUtilities.ResolveContainedPath(
                manifestDirectory,
                entry.SourceFile,
                "ISO_MANIFEST_PATH");
            requests.Add(new Iso9660ReplacementRequest(
                entry.IsoPath,
                replacementPath,
                entry.ExpectedOriginalSize,
                entry.ExpectedOriginalSha256,
                entry.ExpectedReplacementSize,
                entry.ExpectedReplacementSha256));
        }

        return new Iso9660PreparedPatch(
            normalizedManifest,
            manifestSha256,
            manifest.ExpectedSourceSha256,
            requests);
    }

    /// <summary>
    /// Stages a new ISO with authenticated replacements and verifies unchanged data before committing it.
    /// </summary>
    /// <param name="sourceIsoPath">The source ISO path value.</param>
    /// <param name="replacements">The replacements value.</param>
    /// <param name="outputIsoPath">The output ISO path value.</param>
    /// <param name="expectedSourceSha256">The expected source SHA 256 value.</param>
    /// <param name="limits">Optional conservative safety limits; defaults are used when omitted.</param>
    /// <returns>The validated operation result.</returns>
    /// <remarks>Malformed input or violated preconditions are rejected before a persistent output is committed.</remarks>
    public static Iso9660RebuildReport Rebuild(
        string sourceIsoPath,
        IEnumerable<Iso9660ReplacementRequest> replacements,
        string outputIsoPath,
        string? expectedSourceSha256 = null,
        FileLimits? limits = null)
        => RebuildCore(
            sourceIsoPath,
            replacements,
            outputIsoPath,
            expectedSourceSha256,
            manifestPath: null,
            manifestSha256: null,
            limits: limits);

    /// <summary>
    /// Rebuilds core while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="sourceIsoPath">The source ISO path value.</param>
    /// <param name="replacements">The replacements value.</param>
    /// <param name="outputIsoPath">The output ISO path value.</param>
    /// <param name="expectedSourceSha256">The expected source SHA 256 value.</param>
    /// <param name="manifestPath">The manifest path value.</param>
    /// <param name="manifestSha256">The manifest SHA 256 value.</param>
    /// <param name="limits">Optional conservative safety limits; defaults are used when omitted.</param>
    /// <returns>The validated operation result.</returns>
    /// <remarks>Malformed input or violated preconditions are rejected before a persistent output is committed.</remarks>
    private static Iso9660RebuildReport RebuildCore(
        string sourceIsoPath,
        IEnumerable<Iso9660ReplacementRequest> replacements,
        string outputIsoPath,
        string? expectedSourceSha256,
        string? manifestPath,
        string? manifestSha256,
        FileLimits? limits)
    {
        ArgumentNullException.ThrowIfNull(replacements);
        limits ??= FileLimits.Default;

        string sourcePath = PathUtilities.NormalizeProtectedPath(sourceIsoPath, "ISO_SOURCE_REPARSE_POINT");
        string outputPath = PathUtilities.NormalizeProtectedPath(outputIsoPath, "ISO_OUTPUT_REPARSE_POINT");
        PathUtilities.RequireDifferent(
            sourcePath,
            outputPath,
            "ISO_OUTPUT_SOURCE",
            "ISO rebuild output cannot overwrite the source image.");
        if (Directory.Exists(outputPath))
        {
            throw new ToolkitException("ISO_OUTPUT_DIRECTORY", $"ISO rebuild output is a directory: {outputPath}");
        }

        string? normalizedManifest = null;
        string? normalizedManifestSha256 = null;
        if (!string.IsNullOrWhiteSpace(manifestPath))
        {
            normalizedManifest = PathUtilities.NormalizeProtectedPath(manifestPath, "ISO_MANIFEST_REPARSE_POINT");
            PathUtilities.RequireDifferent(
                sourcePath,
                normalizedManifest,
                "ISO_MANIFEST_SOURCE",
                "ISO patch manifest cannot be the source ISO.");
            PathUtilities.RequireDifferent(
                outputPath,
                normalizedManifest,
                "ISO_MANIFEST_OUTPUT",
                "ISO patch manifest cannot be the output ISO.");
            normalizedManifestSha256 = NormalizeOptionalSha256(
                manifestSha256,
                "manifest SHA-256");
        }

        List<Iso9660ReplacementRequest> requestList = replacements.ToList();
        if (requestList.Count == 0)
        {
            throw new ToolkitException("ISO_REPLACEMENT_EMPTY", "At least one ISO file replacement is required.");
        }
        if (requestList.Count > limits.MaximumIsoReplacements)
        {
            throw new ToolkitException(
                "ISO_REPLACEMENT_LIMIT",
                $"ISO rebuild contains {requestList.Count} replacements; configured limit is {limits.MaximumIsoReplacements}.");
        }

        using Iso9660Image image = Iso9660Image.Open(sourcePath, limits);
        EnsureRebuildableDescriptorSet(image);
        string sourceSha256 = image.ComputeSourceSha256();
        string? normalizedExpectedSource = NormalizeOptionalSha256(expectedSourceSha256, "expected source SHA-256");
        if (normalizedExpectedSource is not null
            && !string.Equals(sourceSha256, normalizedExpectedSource, StringComparison.Ordinal))
        {
            throw new ToolkitException(
                "ISO_SOURCE_SHA256",
                $"Source ISO SHA-256 mismatch. Expected {normalizedExpectedSource}, actual {sourceSha256}.");
        }

        PreparedReplacement[] prepared = PrepareReplacements(
            image,
            requestList,
            sourcePath,
            outputPath,
            normalizedManifest,
            limits);
        AssignOutputLocations(image, prepared, limits, out long outputSize, out uint outputBlocks);

        string stagedOutputSha256 = string.Empty;
        AtomicFile.WriteStream(outputPath, output =>
        {
            using IncrementalHash copiedSourceHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            image.CopySourceTo(output, copiedSourceHash);
            string copiedSha256 = Convert.ToHexStringLower(copiedSourceHash.GetHashAndReset());
            if (!string.Equals(copiedSha256, sourceSha256, StringComparison.Ordinal))
            {
                throw new ToolkitException(
                    "ISO_SOURCE_CHANGED",
                    "Source ISO changed while it was being copied; output was not committed.");
            }

            PadTo(output, image.LogicalBlockSize);
            foreach (PreparedReplacement replacement in prepared)
            {
                if (output.Position != replacement.OutputByteOffset)
                {
                    throw new ToolkitException(
                        "ISO_OUTPUT_LAYOUT",
                        $"Unexpected output cursor 0x{output.Position:X}; expected 0x{replacement.OutputByteOffset:X} for {replacement.Entry.Path}.");
                }
                CopyReplacementToOutput(replacement, output);
                PadTo(output, image.LogicalBlockSize);
            }
            if (output.Length != outputSize)
            {
                throw new ToolkitException(
                    "ISO_OUTPUT_SIZE",
                    $"Rebuilt ISO is {output.Length} bytes; expected {outputSize} bytes.");
            }

            foreach (PreparedReplacement replacement in prepared)
            {
                PatchDirectoryRecord(output, replacement);
            }
            WriteBothEndianUInt32(
                output,
                checked(image.PrimaryVolumeDescriptorOffset + 80),
                outputBlocks,
                "primary volume block count");

            output.Flush();
            VerifySourcePrefixUnchanged(image, output, prepared);
            using (Iso9660Image staged = Iso9660Image.ParseStream(
                       output,
                       "<staged-iso-rebuild>",
                       limits,
                       leaveOpen: true))
            {
                VerifyStagedImage(image, staged, prepared, outputSize, outputBlocks);
            }
            stagedOutputSha256 = ComputeStreamSha256(output);
        });

        if (string.IsNullOrEmpty(stagedOutputSha256))
        {
            throw new ToolkitException("ISO_OUTPUT_HASH", "Rebuilt ISO hash was not produced.");
        }

        Iso9660ReplacementReport[] reports = prepared.Select(static replacement => new Iso9660ReplacementReport
        {
            IsoPath = replacement.Entry.Path,
            ReplacementFile = replacement.ReplacementPath,
            OriginalSize = replacement.Entry.Size,
            OriginalSha256 = replacement.OriginalSha256,
            ReplacementSize = replacement.ReplacementSize,
            ReplacementSha256 = replacement.ReplacementSha256,
            OriginalLogicalBlock = replacement.Entry.Extents[0].LogicalBlock,
            ReplacementLogicalBlock = replacement.OutputLogicalBlock,
            DirectoryRecordOffset = replacement.Record.PhysicalOffset
        }).ToArray();

        return new Iso9660RebuildReport
        {
            Source = sourcePath,
            Manifest = normalizedManifest,
            ManifestSha256 = normalizedManifestSha256,
            Output = outputPath,
            VolumeIdentifier = image.VolumeIdentifier,
            SourceSha256 = sourceSha256,
            OutputSha256 = stagedOutputSha256,
            SourceSize = image.SourceSize,
            OutputSize = outputSize,
            OriginalVolumeBlockCount = image.VolumeBlockCount,
            OutputVolumeBlockCount = outputBlocks,
            LogicalBlockSize = image.LogicalBlockSize,
            Replacements = reports
        };
    }

    /// <summary>
    /// Formats the current result as human-readable diagnostic text.
    /// </summary>
    /// <param name="report">The report value.</param>
    /// <returns>The resulting text, path, identifier, or hexadecimal digest.</returns>
    public static string ToHumanText(Iso9660RebuildReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        var output = new System.Text.StringBuilder();
        output.AppendLine("ISO 9660 rebuild: PASS");
        output.AppendLine($"  source:       {report.Source}");
        if (!string.IsNullOrWhiteSpace(report.Manifest))
        {
            output.AppendLine($"  manifest:     {report.Manifest}");
            if (!string.IsNullOrWhiteSpace(report.ManifestSha256))
            {
                output.AppendLine($"  manifest SHA-256: {report.ManifestSha256}");
            }
        }
        output.AppendLine($"  output:       {report.Output}");
        output.AppendLine($"  volume:       {report.VolumeIdentifier}");
        output.AppendLine($"  source SHA-256: {report.SourceSha256}");
        output.AppendLine($"  output SHA-256: {report.OutputSha256}");
        output.AppendLine(
            $"  size:         {report.SourceSize} -> {report.OutputSize} bytes; " +
            $"blocks: {report.OriginalVolumeBlockCount} -> {report.OutputVolumeBlockCount}");
        output.AppendLine($"  replacements: {report.Replacements.Count}");
        foreach (Iso9660ReplacementReport replacement in report.Replacements)
        {
            output.AppendLine(
                $"    {replacement.IsoPath}: {replacement.OriginalSize} -> {replacement.ReplacementSize} bytes; " +
                $"LBA {replacement.OriginalLogicalBlock} -> {replacement.ReplacementLogicalBlock}");
            output.AppendLine($"      SHA-256: {replacement.ReplacementSha256}");
        }
        return output.ToString();
    }

    /// <summary>
    /// Prepares replacements while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="image">The image value.</param>
    /// <param name="requests">The complete set of transactional write requests.</param>
    /// <param name="sourcePath">The normalized source path.</param>
    /// <param name="outputPath">The destination file path.</param>
    /// <param name="manifestPath">The manifest path value.</param>
    /// <param name="limits">Optional conservative safety limits; defaults are used when omitted.</param>
    /// <returns>The resulting binary or typed sequence.</returns>
    private static PreparedReplacement[] PrepareReplacements(
        Iso9660Image image,
        IReadOnlyList<Iso9660ReplacementRequest> requests,
        string sourcePath,
        string outputPath,
        string? manifestPath,
        FileLimits limits)
    {
        var canonicalPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var replacementPaths = new HashSet<string>(PathUtilities.FileSystemComparer);
        var result = new List<PreparedReplacement>(requests.Count);
        long totalReplacementBytes = 0;

        foreach (Iso9660ReplacementRequest? request in requests)
        {
            if (request is null)
            {
                throw new ToolkitException("ISO_REPLACEMENT_NULL", "ISO rebuild contains a null replacement request.");
            }
            if (string.IsNullOrWhiteSpace(request.IsoPath))
            {
                throw new ToolkitException("ISO_REPLACEMENT_PATH", "ISO replacement path cannot be empty.");
            }
            if (string.IsNullOrWhiteSpace(request.ReplacementFile))
            {
                throw new ToolkitException(
                    "ISO_REPLACEMENT_FILE",
                    $"Replacement file path cannot be empty for ISO entry {request.IsoPath}.");
            }

            Iso9660Entry entry = image.GetEntry(request.IsoPath);
            if (entry.IsDirectory)
            {
                throw new ToolkitException("ISO_REPLACE_DIRECTORY", $"Cannot replace an ISO directory: {entry.Path}");
            }
            if (!canonicalPaths.Add(entry.Path))
            {
                throw new ToolkitException("ISO_REPLACEMENT_DUPLICATE", $"ISO path is replaced more than once: {entry.Path}");
            }
            if (entry.IsMultiExtent || entry.DirectoryRecords.Count != 1)
            {
                throw new ToolkitException(
                    "ISO_REPLACE_MULTI_EXTENT",
                    $"Replacing multi-extent ISO files is not supported: {entry.Path}");
            }

            Iso9660DirectoryRecordLocation record = entry.DirectoryRecords[0];
            if (record.ExtendedAttributeBlocks != 0)
            {
                throw new ToolkitException(
                    "ISO_REPLACE_EXTENDED_ATTRIBUTES",
                    $"Replacing files with extended attribute blocks is not supported: {entry.Path}");
            }
            if ((record.Flags & 0x80) != 0)
            {
                throw new ToolkitException(
                    "ISO_REPLACE_MULTI_EXTENT",
                    $"ISO directory record still marks {entry.Path} as multi-extent.");
            }
            if (record.RecordedExtent != entry.Extents[0].LogicalBlock
                || record.DataLength != entry.Extents[0].ByteLength)
            {
                throw new ToolkitException(
                    "ISO_RECORD_PRECONDITION",
                    $"ISO directory record metadata does not match parsed extent for {entry.Path}.");
            }

            string replacementPath = PathUtilities.NormalizeProtectedPath(
                request.ReplacementFile,
                "ISO_REPLACEMENT_REPARSE_POINT");
            if (!File.Exists(replacementPath))
            {
                throw new ToolkitException("ISO_REPLACEMENT_NOT_FOUND", $"Replacement file not found: {replacementPath}");
            }
            if (!replacementPaths.Add(replacementPath))
            {
                throw new ToolkitException(
                    "ISO_REPLACEMENT_FILE_DUPLICATE",
                    $"The same replacement file is used more than once: {replacementPath}");
            }
            PathUtilities.RequireDifferent(
                sourcePath,
                replacementPath,
                "ISO_REPLACEMENT_SOURCE",
                "Replacement file cannot be the source ISO.");
            PathUtilities.RequireDifferent(
                outputPath,
                replacementPath,
                "ISO_REPLACEMENT_OUTPUT",
                "Replacement file cannot be the output ISO.");
            if (manifestPath is not null)
            {
                PathUtilities.RequireDifferent(
                    manifestPath,
                    replacementPath,
                    "ISO_REPLACEMENT_MANIFEST",
                    "Replacement file cannot be the patch manifest.");
            }

            FileInfo replacementInfo = new(replacementPath);
            if (replacementInfo.Length > limits.MaximumIsoReplacementBytes
                || replacementInfo.Length > uint.MaxValue)
            {
                throw new ToolkitException(
                    "ISO_REPLACEMENT_TOO_LARGE",
                    $"Replacement {replacementPath} is {replacementInfo.Length} bytes; configured limit is " +
                    $"{Math.Min(limits.MaximumIsoReplacementBytes, uint.MaxValue)} bytes.");
            }
            totalReplacementBytes = checked(totalReplacementBytes + replacementInfo.Length);
            if (totalReplacementBytes > limits.MaximumIsoTotalReplacementBytes)
            {
                throw new ToolkitException(
                    "ISO_REPLACEMENT_TOTAL_TOO_LARGE",
                    $"ISO replacements contain {totalReplacementBytes} bytes in total; configured limit is " +
                    $"{limits.MaximumIsoTotalReplacementBytes} bytes.");
            }
            if (request.ExpectedReplacementSize is < 0)
            {
                throw new ToolkitException(
                    "ISO_REPLACEMENT_SIZE_FORMAT",
                    $"Expected replacement size cannot be negative for {entry.Path}.");
            }
            if (request.ExpectedReplacementSize is long expectedReplacementSize
                && replacementInfo.Length != expectedReplacementSize)
            {
                throw new ToolkitException(
                    "ISO_REPLACEMENT_SIZE",
                    $"Replacement size mismatch for {entry.Path}. Expected {expectedReplacementSize}, " +
                    $"actual {replacementInfo.Length}.");
            }

            if (request.ExpectedOriginalSize is < 0)
            {
                throw new ToolkitException(
                    "ISO_ORIGINAL_SIZE_FORMAT",
                    $"Expected original size cannot be negative for {entry.Path}.");
            }
            if (request.ExpectedOriginalSize is long expectedSize && entry.Size != expectedSize)
            {
                throw new ToolkitException(
                    "ISO_ORIGINAL_SIZE",
                    $"Original size mismatch for {entry.Path}. Expected {expectedSize}, actual {entry.Size}.");
            }

            string originalSha256 = image.ComputeFileSha256(entry.Path);
            string? expectedOriginalSha256 = NormalizeOptionalSha256(
                request.ExpectedOriginalSha256,
                $"expected original SHA-256 for {entry.Path}");
            if (expectedOriginalSha256 is not null
                && !string.Equals(originalSha256, expectedOriginalSha256, StringComparison.Ordinal))
            {
                throw new ToolkitException(
                    "ISO_ORIGINAL_SHA256",
                    $"Original SHA-256 mismatch for {entry.Path}. Expected {expectedOriginalSha256}, actual {originalSha256}.");
            }

            string replacementSha256 = BinaryUtilities.Sha256HexFile(replacementPath);
            string? expectedReplacementSha256 = NormalizeOptionalSha256(
                request.ExpectedReplacementSha256,
                $"expected replacement SHA-256 for {entry.Path}");
            if (expectedReplacementSha256 is not null
                && !string.Equals(replacementSha256, expectedReplacementSha256, StringComparison.Ordinal))
            {
                throw new ToolkitException(
                    "ISO_REPLACEMENT_SHA256",
                    $"Replacement SHA-256 mismatch for {entry.Path}. Expected {expectedReplacementSha256}, " +
                    $"actual {replacementSha256}.");
            }

            result.Add(new PreparedReplacement(
                entry,
                record,
                replacementPath,
                replacementInfo.Length,
                replacementSha256,
                originalSha256));
        }

        return result
            .OrderBy(static replacement => replacement.Entry.Path, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static replacement => replacement.Entry.Path, StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>
    /// Assigns aligned append-only extents and validates the final volume and replacement size limits.
    /// </summary>
    /// <param name="image">The image value.</param>
    /// <param name="replacements">The replacements value.</param>
    /// <param name="limits">Optional conservative safety limits; defaults are used when omitted.</param>
    /// <param name="outputSize">Receives output size value when the operation succeeds.</param>
    /// <param name="outputBlocks">Receives output blocks value when the operation succeeds.</param>
    private static void AssignOutputLocations(
        Iso9660Image image,
        IReadOnlyList<PreparedReplacement> replacements,
        FileLimits limits,
        out long outputSize,
        out uint outputBlocks)
    {
        long cursor = BinaryUtilities.Align(image.SourceSize, image.LogicalBlockSize);
        foreach (PreparedReplacement replacement in replacements)
        {
            long logicalBlock = cursor / image.LogicalBlockSize;
            if (logicalBlock > uint.MaxValue)
            {
                throw new ToolkitException("ISO_OUTPUT_LBA", $"Replacement LBA exceeds UInt32 for {replacement.Entry.Path}.");
            }
            replacement.OutputByteOffset = cursor;
            replacement.OutputLogicalBlock = checked((uint)logicalBlock);
            cursor = checked(cursor + replacement.ReplacementSize);
            cursor = BinaryUtilities.Align(cursor, image.LogicalBlockSize);
            if (cursor > limits.MaximumIsoOutputBytes)
            {
                throw new ToolkitException(
                    "ISO_OUTPUT_TOO_LARGE",
                    $"Rebuilt ISO would be {cursor} bytes; configured limit is {limits.MaximumIsoOutputBytes} bytes.");
            }
        }

        long blockCount = cursor / image.LogicalBlockSize;
        if (blockCount <= 0 || blockCount > uint.MaxValue)
        {
            throw new ToolkitException("ISO_OUTPUT_VOLUME_SIZE", $"Rebuilt ISO block count {blockCount} is invalid.");
        }
        outputSize = cursor;
        outputBlocks = checked((uint)blockCount);
    }

    /// <summary>
    /// Ensures rebuildable descriptor set while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="image">The image value.</param>
    private static void EnsureRebuildableDescriptorSet(Iso9660Image image)
    {
        if (image.HasSupplementaryVolumeDescriptor)
        {
            throw new ToolkitException(
                "ISO_REBUILD_SUPPLEMENTARY_DESCRIPTOR",
                "ISO contains a supplementary volume descriptor. Rebuilding it could leave an alternate directory tree inconsistent.");
        }
        if (image.HasPartitionDescriptor)
        {
            throw new ToolkitException(
                "ISO_REBUILD_PARTITION_DESCRIPTOR",
                "ISO contains a volume partition descriptor, which is not supported by the safe rebuild path.");
        }
        if (image.HasUnsupportedVolumeDescriptor)
        {
            throw new ToolkitException(
                "ISO_REBUILD_UNKNOWN_DESCRIPTOR",
                "ISO contains a reserved or unsupported volume descriptor type.");
        }
    }

    /// <summary>
    /// Copies replacement to output while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="replacement">The replacement value.</param>
    /// <param name="output">The destination stream, buffer, or model.</param>
    private static void CopyReplacementToOutput(PreparedReplacement replacement, Stream output)
    {
        using FileStream input = new(
            replacement.ReplacementPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            1024 * 1024,
            FileOptions.SequentialScan);
        if (input.Length != replacement.ReplacementSize)
        {
            throw new ToolkitException(
                "ISO_REPLACEMENT_CHANGED",
                $"Replacement size changed while rebuilding: {replacement.ReplacementPath}");
        }

        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer = ArrayPool<byte>.Shared.Rent(1024 * 1024);
        long copied = 0;
        try
        {
            while (true)
            {
                int read = input.Read(buffer, 0, buffer.Length);
                if (read == 0)
                {
                    break;
                }
                output.Write(buffer, 0, read);
                hash.AppendData(buffer, 0, read);
                copied = checked(copied + read);
                if (copied > replacement.ReplacementSize)
                {
                    throw new ToolkitException(
                        "ISO_REPLACEMENT_CHANGED",
                        $"Replacement grew while rebuilding: {replacement.ReplacementPath}");
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
        }

        string copiedSha256 = Convert.ToHexStringLower(hash.GetHashAndReset());
        if (copied != replacement.ReplacementSize
            || !string.Equals(copiedSha256, replacement.ReplacementSha256, StringComparison.Ordinal))
        {
            throw new ToolkitException(
                "ISO_REPLACEMENT_CHANGED",
                $"Replacement changed while rebuilding: {replacement.ReplacementPath}");
        }
    }

    /// <summary>
    /// Updates both endian copies of a replacement file location and length in its ISO directory record.
    /// </summary>
    /// <param name="output">The destination stream, buffer, or model.</param>
    /// <param name="replacement">The replacement value.</param>
    private static void PatchDirectoryRecord(Stream output, PreparedReplacement replacement)
    {
        Iso9660DirectoryRecordLocation record = replacement.Record;
        if (record.RecordLength < 34)
        {
            throw new ToolkitException("ISO_RECORD_PRECONDITION", $"Directory record is too small for {replacement.Entry.Path}.");
        }
        WriteBothEndianUInt32(
            output,
            checked(record.PhysicalOffset + 2),
            replacement.OutputLogicalBlock,
            $"extent for {replacement.Entry.Path}");
        WriteBothEndianUInt32(
            output,
            checked(record.PhysicalOffset + 10),
            checked((uint)replacement.ReplacementSize),
            $"length for {replacement.Entry.Path}");
    }

    /// <summary>
    /// Writes both endian u int 32 while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="output">The destination stream, buffer, or model.</param>
    /// <param name="offset">The zero-based byte or element offset.</param>
    /// <param name="value">The value to process.</param>
    /// <param name="context">A diagnostic label included in validation errors.</param>
    private static void WriteBothEndianUInt32(Stream output, long offset, uint value, string context)
    {
        if (!output.CanSeek || !output.CanWrite)
        {
            throw new ToolkitException("ISO_OUTPUT_CAPABILITIES", "ISO output stream must support writing and seeking.");
        }
        if (offset < 0 || offset > output.Length - 8)
        {
            throw new ToolkitException(
                "ISO_OUTPUT_RANGE",
                $"Metadata write for {context} at 0x{offset:X} exceeds output length 0x{output.Length:X}.");
        }

        Span<byte> encoded = stackalloc byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(encoded[..4], value);
        BinaryPrimitives.WriteUInt32BigEndian(encoded[4..], value);
        output.Position = offset;
        output.Write(encoded);
    }

    /// <summary>
    /// Verifies that ISO bytes outside explicitly mutable metadata ranges remain byte-for-byte unchanged.
    /// </summary>
    /// <param name="original">The original value.</param>
    /// <param name="staged">The staged value.</param>
    /// <param name="replacements">The replacements value.</param>
    /// <remarks>Malformed input or violated preconditions are rejected before a persistent output is committed.</remarks>
    private static void VerifySourcePrefixUnchanged(
        Iso9660Image original,
        Stream staged,
        IReadOnlyList<PreparedReplacement> replacements)
    {
        if (!staged.CanRead || !staged.CanSeek)
        {
            throw new ToolkitException(
                "ISO_OUTPUT_CAPABILITIES",
                "ISO output stream must support reading and seeking for source-prefix verification.");
        }

        var mutableRanges = new List<ByteRange>(1 + (replacements.Count * 2))
        {
            new(checked(original.PrimaryVolumeDescriptorOffset + 80), 8)
        };
        foreach (PreparedReplacement replacement in replacements)
        {
            mutableRanges.Add(new ByteRange(checked(replacement.Record.PhysicalOffset + 2), 8));
            mutableRanges.Add(new ByteRange(checked(replacement.Record.PhysicalOffset + 10), 8));
        }

        ByteRange[] normalized = NormalizeMutableRanges(mutableRanges, original.SourceSize);
        byte[] originalBuffer = ArrayPool<byte>.Shared.Rent(1024 * 1024);
        byte[] stagedBuffer = ArrayPool<byte>.Shared.Rent(1024 * 1024);
        try
        {
            long cursor = 0;
            foreach (ByteRange range in normalized)
            {
                CompareUnchangedRange(
                    original,
                    staged,
                    cursor,
                    checked(range.Offset - cursor),
                    originalBuffer,
                    stagedBuffer);
                cursor = range.End;
            }
            CompareUnchangedRange(
                original,
                staged,
                cursor,
                checked(original.SourceSize - cursor),
                originalBuffer,
                stagedBuffer);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(originalBuffer, clearArray: true);
            ArrayPool<byte>.Shared.Return(stagedBuffer, clearArray: true);
        }
    }

    /// <summary>
    /// Normalizes mutable ranges while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="ranges">The ranges value.</param>
    /// <param name="sourceSize">The source size value.</param>
    /// <returns>The resulting binary or typed sequence.</returns>
    private static ByteRange[] NormalizeMutableRanges(
        IEnumerable<ByteRange> ranges,
        long sourceSize)
    {
        ByteRange[] ordered = ranges
            .OrderBy(static range => range.Offset)
            .ThenBy(static range => range.Length)
            .ToArray();
        var merged = new List<ByteRange>(ordered.Length);
        foreach (ByteRange range in ordered)
        {
            if (range.Offset < 0 || range.Length <= 0 || range.End > sourceSize)
            {
                throw new ToolkitException(
                    "ISO_MUTABLE_RANGE",
                    $"Allowed ISO metadata range [0x{range.Offset:X},0x{range.End:X}) is invalid for source length 0x{sourceSize:X}.");
            }

            if (merged.Count == 0 || range.Offset > merged[^1].End)
            {
                merged.Add(range);
                continue;
            }

            ByteRange previous = merged[^1];
            long end = Math.Max(previous.End, range.End);
            merged[^1] = new ByteRange(previous.Offset, checked(end - previous.Offset));
        }
        return merged.ToArray();
    }

    /// <summary>
    /// Compares an unmodified ISO range through reusable buffers and fails at the first changed byte.
    /// </summary>
    /// <param name="original">The original value.</param>
    /// <param name="staged">The staged value.</param>
    /// <param name="offset">The zero-based byte or element offset.</param>
    /// <param name="length">The number of bytes or elements to process.</param>
    /// <param name="originalBuffer">The original buffer value.</param>
    /// <param name="stagedBuffer">The staged buffer value.</param>
    private static void CompareUnchangedRange(
        Iso9660Image original,
        Stream staged,
        long offset,
        long length,
        byte[] originalBuffer,
        byte[] stagedBuffer)
    {
        if (length <= 0)
        {
            return;
        }

        long position = offset;
        long remaining = length;
        while (remaining > 0)
        {
            int count = checked((int)Math.Min(originalBuffer.Length, remaining));
            original.ReadSourceExactly(
                position,
                originalBuffer.AsSpan(0, count),
                "ISO source-prefix verification");
            staged.Position = position;
            try
            {
                staged.ReadExactly(stagedBuffer.AsSpan(0, count));
            }
            catch (EndOfStreamException ex)
            {
                throw new ToolkitException(
                    "ISO_REBUILD_VERIFY_PREFIX",
                    "Staged ISO ended while verifying unchanged source bytes.",
                    ex);
            }

            ReadOnlySpan<byte> expected = originalBuffer.AsSpan(0, count);
            ReadOnlySpan<byte> actual = stagedBuffer.AsSpan(0, count);
            if (!expected.SequenceEqual(actual))
            {
                int relative = FirstDifference(expected, actual);
                long absolute = checked(position + relative);
                throw new ToolkitException(
                    "ISO_REBUILD_VERIFY_PREFIX",
                    $"Unexpected change in preserved ISO bytes at offset 0x{absolute:X}.");
            }

            position = checked(position + count);
            remaining -= count;
        }
    }

    /// <summary>
    /// Finds the first unequal byte, or the shared-length boundary when only the lengths differ.
    /// </summary>
    /// <param name="left">The left value.</param>
    /// <param name="right">The right value.</param>
    /// <returns>The validated operation result.</returns>
    private static int FirstDifference(ReadOnlySpan<byte> left, ReadOnlySpan<byte> right)
    {
        int count = Math.Min(left.Length, right.Length);
        for (int index = 0; index < count; index++)
        {
            if (left[index] != right[index])
            {
                return index;
            }
        }
        return count;
    }

    /// <summary>
    /// Verifies staged image while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="original">The original value.</param>
    /// <param name="staged">The staged value.</param>
    /// <param name="replacements">The replacements value.</param>
    /// <param name="expectedOutputSize">The expected output size value.</param>
    /// <param name="expectedOutputBlocks">The expected output blocks value.</param>
    /// <remarks>Malformed input or violated preconditions are rejected before a persistent output is committed.</remarks>
    private static void VerifyStagedImage(
        Iso9660Image original,
        Iso9660Image staged,
        IReadOnlyList<PreparedReplacement> replacements,
        long expectedOutputSize,
        uint expectedOutputBlocks)
    {
        if (staged.SourceSize != expectedOutputSize || staged.VolumeBlockCount != expectedOutputBlocks)
        {
            throw new ToolkitException(
                "ISO_REBUILD_VERIFY_VOLUME",
                $"Staged ISO size/block mismatch: {staged.SourceSize}/{staged.VolumeBlockCount}, " +
                $"expected {expectedOutputSize}/{expectedOutputBlocks}.");
        }
        if (!string.Equals(original.VolumeIdentifier, staged.VolumeIdentifier, StringComparison.Ordinal))
        {
            throw new ToolkitException("ISO_REBUILD_VERIFY_VOLUME", "ISO volume identifier changed during rebuild.");
        }

        Dictionary<string, PreparedReplacement> replacementMap = replacements.ToDictionary(
            static replacement => replacement.Entry.Path,
            StringComparer.OrdinalIgnoreCase);
        foreach (Iso9660Entry oldEntry in original.Entries)
        {
            Iso9660Entry newEntry = staged.GetEntry(oldEntry.Path);
            if (replacementMap.TryGetValue(oldEntry.Path, out PreparedReplacement? replacement))
            {
                if (newEntry.IsDirectory
                    || newEntry.IsMultiExtent
                    || newEntry.Identifier != oldEntry.Identifier
                    || newEntry.RawIdentifier != oldEntry.RawIdentifier
                    || newEntry.Flags != oldEntry.Flags
                    || newEntry.IsHidden != oldEntry.IsHidden
                    || newEntry.Size != replacement.ReplacementSize
                    || newEntry.Extents.Count != 1
                    || newEntry.Extents[0].LogicalBlock != replacement.OutputLogicalBlock
                    || newEntry.Extents[0].ByteLength != replacement.ReplacementSize
                    || newEntry.DirectoryRecords.Count != 1
                    || newEntry.DirectoryRecords[0].PhysicalOffset != replacement.Record.PhysicalOffset
                    || newEntry.DirectoryRecords[0].ExtendedAttributeBlocks != 0
                    || newEntry.DirectoryRecords[0].RecordedExtent != replacement.OutputLogicalBlock
                    || newEntry.DirectoryRecords[0].DataLength != replacement.ReplacementSize
                    || newEntry.DirectoryRecords[0].Flags != oldEntry.Flags)
                {
                    throw new ToolkitException(
                        "ISO_REBUILD_VERIFY_TARGET",
                        $"Rebuilt metadata does not match replacement for {oldEntry.Path}.");
                }
                string stagedSha256 = staged.ComputeFileSha256(newEntry.Path);
                if (!string.Equals(stagedSha256, replacement.ReplacementSha256, StringComparison.Ordinal))
                {
                    throw new ToolkitException(
                        "ISO_REBUILD_VERIFY_TARGET",
                        $"Rebuilt data SHA-256 mismatch for {oldEntry.Path}.");
                }
                continue;
            }

            if (oldEntry.Identifier != newEntry.Identifier
                || oldEntry.RawIdentifier != newEntry.RawIdentifier
                || oldEntry.IsDirectory != newEntry.IsDirectory
                || oldEntry.IsHidden != newEntry.IsHidden
                || oldEntry.Flags != newEntry.Flags
                || oldEntry.Size != newEntry.Size
                || !oldEntry.Extents.SequenceEqual(newEntry.Extents)
                || !oldEntry.DirectoryRecords.SequenceEqual(newEntry.DirectoryRecords))
            {
                throw new ToolkitException(
                    "ISO_REBUILD_VERIFY_UNCHANGED",
                    $"Unchanged ISO entry metadata differs after rebuild: {oldEntry.Path}");
            }
        }
        if (original.Entries.Count != staged.Entries.Count)
        {
            throw new ToolkitException("ISO_REBUILD_VERIFY_ENTRIES", "ISO entry count changed during file replacement.");
        }
    }

    /// <summary>
    /// Computes stream SHA 256 while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="stream">The readable and seekable stream to process.</param>
    /// <returns>The resulting text, path, identifier, or hexadecimal digest.</returns>
    private static string ComputeStreamSha256(Stream stream)
    {
        if (!stream.CanRead || !stream.CanSeek)
        {
            throw new ToolkitException("ISO_OUTPUT_CAPABILITIES", "ISO output stream must support reading and seeking for verification.");
        }
        stream.Flush();
        long restore = stream.Position;
        stream.Position = 0;
        try
        {
            return Convert.ToHexStringLower(SHA256.HashData(stream));
        }
        finally
        {
            stream.Position = restore;
        }
    }

    /// <summary>
    /// Appends zero bytes until the output position is a multiple of the requested alignment.
    /// </summary>
    /// <param name="output">The destination stream, buffer, or model.</param>
    /// <param name="alignment">The required positive alignment in bytes.</param>
    private static void PadTo(Stream output, int alignment)
    {
        long aligned = BinaryUtilities.Align(output.Position, alignment);
        long padding = aligned - output.Position;
        if (padding == 0)
        {
            return;
        }
        Span<byte> zeros = stackalloc byte[256];
        while (padding > 0)
        {
            int count = checked((int)Math.Min(zeros.Length, padding));
            output.Write(zeros[..count]);
            padding -= count;
        }
    }

    /// <summary>
    /// Normalizes optional SHA 256 while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="value">The value to process.</param>
    /// <param name="context">A diagnostic label included in validation errors.</param>
    /// <returns>The resulting text, path, identifier, or hexadecimal digest.</returns>
    private static string? NormalizeOptionalSha256(string? value, string context)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }
        string normalized = value.Trim().ToLowerInvariant();
        if (normalized.Length != 64 || normalized.Any(static character => !Uri.IsHexDigit(character)))
        {
            throw new ToolkitException("ISO_SHA256_FORMAT", $"Invalid {context}: expected exactly 64 hexadecimal characters.");
        }
        return normalized;
    }

    /// <summary>
    /// Reads manifest snapshot while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="path">The file-system path to process.</param>
    /// <param name="limits">Optional conservative safety limits; defaults are used when omitted.</param>
    /// <returns>The resulting binary or typed sequence.</returns>
    private static byte[] ReadManifestSnapshot(string path, FileLimits limits)
    {
        using FileStream stream = new(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            64 * 1024,
            FileOptions.SequentialScan);
        long length = stream.Length;
        if (length > limits.MaximumTextBytes || length > int.MaxValue)
        {
            throw new ToolkitException(
                "ISO_MANIFEST_TOO_LARGE",
                $"ISO patch manifest is {length} bytes; configured limit is {limits.MaximumTextBytes} bytes.");
        }

        byte[] data = new byte[checked((int)length)];
        try
        {
            stream.ReadExactly(data);
        }
        catch (EndOfStreamException ex)
        {
            throw new ToolkitException(
                "ISO_MANIFEST_CHANGED",
                "ISO patch manifest changed or was truncated while being read.",
                ex);
        }
        if (stream.ReadByte() != -1)
        {
            throw new ToolkitException(
                "ISO_MANIFEST_CHANGED",
                "ISO patch manifest grew while being read.");
        }
        return data;
    }

    /// <summary>
    /// Represents immutable byte range data exchanged by the toolkit.
    /// </summary>
    /// <param name="Offset">The offset value used by this model or operation.</param>
    /// <param name="Length">The length value used by this model or operation.</param>
    private readonly record struct ByteRange(long Offset, long Length)
    {
        /// <summary>The end value used by this model or operation.</summary>
        public long End => checked(Offset + Length);
    }

    /// <summary>
    /// Represents the toolkit's prepared replacement model or service.
    /// </summary>
    /// <param name="entry">The entry value used by this model or operation.</param>
    /// <param name="record">The record value used by this model or operation.</param>
    /// <param name="replacementPath">The replacement path value used by this model or operation.</param>
    /// <param name="replacementSize">The replacement size value used by this model or operation.</param>
    /// <param name="replacementSha256">The replacement sha256 value used by this model or operation.</param>
    /// <param name="originalSha256">The original sha256 value used by this model or operation.</param>
    private sealed class PreparedReplacement(
        Iso9660Entry entry,
        Iso9660DirectoryRecordLocation record,
        string replacementPath,
        long replacementSize,
        string replacementSha256,
        string originalSha256)
    {
        /// <summary>The entry value used by this model or operation.</summary>
        public Iso9660Entry Entry { get; } = entry;
        public Iso9660DirectoryRecordLocation Record { get; } = record;
        /// <summary>The replacement path value used by this model or operation.</summary>
        public string ReplacementPath { get; } = replacementPath;
        /// <summary>The replacement size value used by this model or operation.</summary>
        public long ReplacementSize { get; } = replacementSize;
        /// <summary>The replacement sha256 value used by this model or operation.</summary>
        public string ReplacementSha256 { get; } = replacementSha256;
        /// <summary>The original sha256 value used by this model or operation.</summary>
        public string OriginalSha256 { get; } = originalSha256;
        /// <summary>The output byte offset value used by this model or operation.</summary>
        public long OutputByteOffset { get; set; }
        /// <summary>The output logical block value used by this model or operation.</summary>
        public uint OutputLogicalBlock { get; set; }
    }
}
