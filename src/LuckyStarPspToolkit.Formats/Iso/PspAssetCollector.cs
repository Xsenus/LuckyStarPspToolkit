using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using LuckyStarPspToolkit.Formats.Common;

namespace LuckyStarPspToolkit.Formats.Iso;

/// <summary>
/// Represents immutable PSP asset collection options data exchanged by the toolkit.
/// </summary>
/// <param name="IncludeOptional">The include optional value used by this model or operation.</param>
/// <param name="IncludeFileListing">The include file listing value used by this model or operation.</param>
/// <param name="ComputeSourceSha256">The compute source sha256 value used by this model or operation.</param>
public sealed record PspAssetCollectionOptions(
    bool IncludeOptional = false,
    bool IncludeFileListing = true,
    bool ComputeSourceSha256 = true);

/// <summary>
/// Represents the toolkit's PSP asset file report model or service.
/// </summary>
public sealed class PspAssetFileReport
{
    /// <summary>The path value used by this model or operation.</summary>
    public required string Path { get; init; }
    /// <summary>The required value used by this model or operation.</summary>
    public required bool Required { get; init; }
    /// <summary>The present value used by this model or operation.</summary>
    public required bool Present { get; init; }
    /// <summary>The included value used by this model or operation.</summary>
    public required bool Included { get; init; }
    /// <summary>The size value used by this model or operation.</summary>
    public long? Size { get; init; }
    /// <summary>The sha256 value used by this model or operation.</summary>
    public string? Sha256 { get; set; }
    /// <summary>The extent count value used by this model or operation.</summary>
    public int? ExtentCount { get; init; }
}

/// <summary>
/// Represents the toolkit's PSP asset bundle report model or service.
/// </summary>
public sealed class PspAssetBundleReport
{
    /// <summary>The schema value used by this model or operation.</summary>
    public string Schema { get; init; } = "lucky-star-psp.asset-bundle.v1";
    /// <summary>The toolkit version that created this record.</summary>
    public string ToolVersion { get; init; } = ToolkitBuildInfo.Version;
    /// <summary>The UTC creation time of the manifest; it does not prove runtime compatibility.</summary>
    public DateTimeOffset CreatedUtc { get; init; } = DateTimeOffset.UtcNow;
    /// <summary>The status value used by this model or operation.</summary>
    public required string Status { get; set; }
    /// <summary>The source value used by this model or operation.</summary>
    public required string Source { get; init; }
    /// <summary>The source size value used by this model or operation.</summary>
    public required long SourceSize { get; init; }
    /// <summary>The hexadecimal SHA-256 of the exact input snapshot used by this object.</summary>
    public string? SourceSha256 { get; set; }
    /// <summary>The volume identifier value used by this model or operation.</summary>
    public required string VolumeIdentifier { get; init; }
    /// <summary>The iso entry count value used by this model or operation.</summary>
    public required int IsoEntryCount { get; init; }
    /// <summary>The optional assets requested value used by this model or operation.</summary>
    public required bool OptionalAssetsRequested { get; init; }
    /// <summary>The file listing included value used by this model or operation.</summary>
    public required bool FileListingIncluded { get; init; }
    /// <summary>The included bytes value used by this model or operation.</summary>
    public required long IncludedBytes { get; set; }
    /// <summary>The files value used by this model or operation.</summary>
    public required IReadOnlyList<PspAssetFileReport> Files { get; init; }
    /// <summary>The missing required value used by this model or operation.</summary>
    public required IReadOnlyList<string> MissingRequired { get; set; }
    /// <summary>The missing optional value used by this model or operation.</summary>
    public required IReadOnlyList<string> MissingOptional { get; set; }
    /// <summary>The output value used by this model or operation.</summary>
    public required string Output { get; init; }
    /// <summary>The output size value used by this model or operation.</summary>
    public long? OutputSize { get; set; }
    /// <summary>The output sha256 value used by this model or operation.</summary>
    public string? OutputSha256 { get; set; }

    /// <summary>The complete value used by this model or operation.</summary>
    [JsonIgnore]
    public bool Complete => MissingRequired.Count == 0;
}

/// <summary>
/// Provides the toolkit's PSP asset collector workflow.
/// </summary>
public static class PspAssetCollector
{
    /// <summary>The fixed manifest name value used by this format or revision.</summary>
    public const string ManifestName = "lsptool-asset-manifest.json";
    /// <summary>The fixed listing name value used by this format or revision.</summary>
    public const string ListingName = "lsptool-iso-file-listing.json";
    /// <summary>The fixed instructions name value used by this format or revision.</summary>
    public const string InstructionsName = "README-RU.txt";

    /// <summary>The definitions value used by this model or operation.</summary>
    private static readonly AssetDefinition[] Definitions =
    [
        new("PSP_GAME/PARAM.SFO", true),
        new("PSP_GAME/SYSDIR/EBOOT.BIN", true),
        new("PSP_GAME/USRDIR/DATA/sc.cpk", true),
        new("PSP_GAME/USRDIR/DATA/lt.bin", true),
        new("PSP_GAME/USRDIR/DATA/union.cpk", false),
        new("PSP_GAME/USRDIR/DATA/pr.bin", false),
        new("PSP_GAME/SYSDIR/BOOT.BIN", false),
        new("PSP_GAME/USRDIR/DATA/titlein.pmf", false)
    ];

    /// <summary>The stable zip timestamp value used by this model or operation.</summary>
    private static readonly DateTimeOffset StableZipTimestamp =
        new(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>The fixed instructions text value used by this format or revision.</summary>
    private const string InstructionsText =
        "Этот архив создан Lucky Star PSP Translation Toolkit.\n" +
        "Он содержит только выбранные файлы из принадлежащего вам образа PSP.\n" +
        "Передайте архив разработчику перевода по закрытому каналу и не публикуйте его.\n" +
        "Файл lsptool-asset-manifest.json содержит SHA-256 и перечень обязательных файлов.\n";

    /// <summary>
    /// Collects the minimum translation assets from a caller-owned PSP ISO into a deterministic ZIP bundle.
    /// </summary>
    /// <param name="isoPath">The iso path value.</param>
    /// <param name="outputZipPath">The output ZIP path value.</param>
    /// <param name="options">Optional operation settings.</param>
    /// <param name="limits">Optional conservative safety limits; defaults are used when omitted.</param>
    /// <returns>The validated operation result.</returns>
    public static PspAssetBundleReport CollectFromIso(
        string isoPath,
        string outputZipPath,
        PspAssetCollectionOptions? options = null,
        FileLimits? limits = null)
    {
        options ??= new PspAssetCollectionOptions();
        limits ??= FileLimits.Default;
        string sourcePath = PathUtilities.NormalizeProtectedPath(isoPath, "ASSET_SOURCE_REPARSE_POINT");
        string outputPath = PathUtilities.NormalizeProtectedPath(outputZipPath, "ASSET_OUTPUT_REPARSE_POINT");
        PathUtilities.RequireDifferent(
            sourcePath,
            outputPath,
            "ASSET_OUTPUT_COLLISION",
            "Asset bundle output must be different from the source ISO.");
        if (Directory.Exists(outputPath))
        {
            throw new ToolkitException("ASSET_OUTPUT_DIRECTORY", $"Asset bundle output is a directory: {outputPath}");
        }

        using Iso9660Image image = Iso9660Image.Open(sourcePath, limits);
        List<PspAssetFileReport> files = [];
        long totalBytes = 0;
        foreach (AssetDefinition definition in Definitions)
        {
            bool present = image.TryGetEntry(definition.Path, out Iso9660Entry? entry)
                && entry is not null
                && !entry.IsDirectory;
            bool included = present && (definition.Required || options.IncludeOptional);
            if (included)
            {
                totalBytes = checked(totalBytes + entry!.Size);
                if (totalBytes > limits.MaximumCollectedAssetBytes)
                {
                    throw new ToolkitException(
                        "ASSET_SIZE_LIMIT",
                        $"Selected assets total {totalBytes} bytes; configured limit is {limits.MaximumCollectedAssetBytes} bytes.");
                }
            }
            files.Add(new PspAssetFileReport
            {
                Path = definition.Path,
                Required = definition.Required,
                Present = present,
                Included = included,
                Size = present ? entry!.Size : null,
                ExtentCount = present ? entry!.Extents.Count : null
            });
        }

        string[] missingRequired = files
            .Where(static file => file.Required && !file.Present)
            .Select(static file => file.Path)
            .ToArray();
        string[] missingOptional = files
            .Where(static file => !file.Required && !file.Present)
            .Select(static file => file.Path)
            .ToArray();

        PspAssetBundleReport report = new()
        {
            Status = missingRequired.Length == 0 ? "complete" : "incomplete",
            Source = sourcePath,
            SourceSize = image.SourceSize,
            SourceSha256 = options.ComputeSourceSha256 ? image.ComputeSourceSha256() : null,
            VolumeIdentifier = image.VolumeIdentifier,
            IsoEntryCount = image.Entries.Count,
            OptionalAssetsRequested = options.IncludeOptional,
            FileListingIncluded = options.IncludeFileListing,
            IncludedBytes = totalBytes,
            Files = files.AsReadOnly(),
            MissingRequired = missingRequired,
            MissingOptional = missingOptional,
            Output = outputPath
        };

        AtomicFile.WriteStream(outputPath, output => WriteBundle(image, output, report, options, limits));
        FileInfo outputInfo = new(outputPath);
        report.OutputSize = outputInfo.Length;
        report.OutputSha256 = BinaryUtilities.Sha256HexFile(outputPath);
        return report;
    }

    /// <summary>
    /// Formats the current result as human-readable diagnostic text.
    /// </summary>
    /// <param name="report">The report value.</param>
    /// <returns>The resulting text, path, identifier, or hexadecimal digest.</returns>
    public static string ToHumanText(PspAssetBundleReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        var output = new System.Text.StringBuilder();
        output.AppendLine("PSP translation asset bundle");
        output.AppendLine($"  status:       {report.Status}");
        output.AppendLine($"  source:       {report.Source}");
        output.AppendLine($"  volume:       {report.VolumeIdentifier}");
        output.AppendLine($"  ISO entries:  {report.IsoEntryCount}");
        output.AppendLine($"  included:     {report.Files.Count(static file => file.Included)} files, {report.IncludedBytes} bytes");
        output.AppendLine($"  output:       {report.Output}");
        if (!string.IsNullOrWhiteSpace(report.OutputSha256))
        {
            output.AppendLine($"  output SHA-256: {report.OutputSha256}");
        }
        output.AppendLine("Files:");
        foreach (PspAssetFileReport file in report.Files)
        {
            string state = !file.Present ? "MISSING" : file.Included ? "included" : "available (not requested)";
            output.AppendLine($"  {(file.Required ? "required" : "optional"),8}  {state,-25} {file.Path}");
        }
        return output.ToString();
    }

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
    /// Writes bundle while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="image">The image value.</param>
    /// <param name="output">The destination stream, buffer, or model.</param>
    /// <param name="report">The report value.</param>
    /// <param name="options">Optional operation settings.</param>
    /// <param name="limits">Optional conservative safety limits; defaults are used when omitted.</param>
    private static void WriteBundle(
        Iso9660Image image,
        Stream output,
        PspAssetBundleReport report,
        PspAssetCollectionOptions options,
        FileLimits limits)
    {
        using ZipArchive zip = new(output, ZipArchiveMode.Create, leaveOpen: true);
        foreach (PspAssetFileReport file in report.Files.Where(static file => file.Included))
        {
            Iso9660Entry isoEntry = image.GetEntry(file.Path);
            ZipArchiveEntry zipEntry = zip.CreateEntry(file.Path, CompressionLevel.NoCompression);
            zipEntry.LastWriteTime = StableZipTimestamp;
            using Stream destination = zipEntry.Open();
            using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            image.CopyFileTo(isoEntry, destination, hash);
            file.Sha256 = Convert.ToHexStringLower(hash.GetHashAndReset());
        }

        WriteZipEntry(zip, InstructionsName, System.Text.Encoding.UTF8.GetBytes(InstructionsText));

        if (options.IncludeFileListing)
        {
            long estimatedListingBytes = image.EstimateListingBytes();
            if (estimatedListingBytes > limits.MaximumTextBytes)
            {
                throw new ToolkitException(
                    "ASSET_LISTING_TOO_LARGE",
                    $"ISO file listing is estimated at {estimatedListingBytes} bytes; configured text limit is {limits.MaximumTextBytes} bytes.");
            }
            var listing = new
            {
                schema = "lucky-star-psp.iso9660-listing.v1",
                volumeIdentifier = image.VolumeIdentifier,
                logicalBlockSize = image.LogicalBlockSize,
                volumeBlockCount = image.VolumeBlockCount,
                entries = image.Entries.Select(static entry => new
                {
                    path = entry.Path,
                    directory = entry.IsDirectory,
                    hidden = entry.IsHidden,
                    size = entry.Size,
                    flags = $"0x{entry.Flags:X2}",
                    extents = entry.Extents.Select(static extent => new
                    {
                        logicalBlock = extent.LogicalBlock,
                        byteLength = extent.ByteLength
                    }).ToArray()
                }).ToArray()
            };
            byte[] listingJson = JsonSerializer.SerializeToUtf8Bytes(listing, JsonOptions);
            if (listingJson.LongLength > limits.MaximumTextBytes)
            {
                throw new ToolkitException(
                    "ASSET_LISTING_TOO_LARGE",
                    $"ISO file listing is {listingJson.LongLength} bytes; configured text limit is {limits.MaximumTextBytes} bytes.");
            }
            WriteZipEntry(zip, ListingName, listingJson);
        }

        PspAssetBundleReport embeddedReport = new()
        {
            CreatedUtc = StableZipTimestamp,
            Status = report.Status,
            Source = Path.GetFileName(report.Source) ?? report.Source,
            SourceSize = report.SourceSize,
            SourceSha256 = report.SourceSha256,
            VolumeIdentifier = report.VolumeIdentifier,
            IsoEntryCount = report.IsoEntryCount,
            OptionalAssetsRequested = report.OptionalAssetsRequested,
            FileListingIncluded = report.FileListingIncluded,
            IncludedBytes = report.IncludedBytes,
            Files = report.Files,
            MissingRequired = report.MissingRequired,
            MissingOptional = report.MissingOptional,
            Output = "asset-bundle.zip"
        };
        byte[] manifest = JsonSerializer.SerializeToUtf8Bytes(embeddedReport, JsonOptions);
        if (manifest.LongLength > limits.MaximumTextBytes)
        {
            throw new ToolkitException("ASSET_MANIFEST_TOO_LARGE", "Asset bundle manifest exceeds the configured text limit.");
        }
        WriteZipEntry(zip, ManifestName, manifest);
    }

    /// <summary>
    /// Writes ZIP entry while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="zip">The zip value.</param>
    /// <param name="name">The logical name used for lookup or diagnostics.</param>
    /// <param name="data">The binary data to process.</param>
    private static void WriteZipEntry(ZipArchive zip, string name, ReadOnlySpan<byte> data)
    {
        ZipArchiveEntry entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        entry.LastWriteTime = StableZipTimestamp;
        using Stream destination = entry.Open();
        destination.Write(data);
    }

    /// <summary>
    /// Represents immutable asset definition data exchanged by the toolkit.
    /// </summary>
    /// <param name="Path">The path value used by this model or operation.</param>
    /// <param name="Required">The required value used by this model or operation.</param>
    private sealed record AssetDefinition(string Path, bool Required);
}
