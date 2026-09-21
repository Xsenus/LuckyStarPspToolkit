using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using LuckyStarPspToolkit.Formats.Common;
using LuckyStarPspToolkit.Formats.Workspace;

namespace LuckyStarPspToolkit.Formats.Audit;

/// <summary>
/// Represents the toolkit's customer audit report model or service.
/// </summary>
public sealed class CustomerAuditReport
{
    /// <summary>The schema revision; readers reject unsupported revisions.</summary>
    public int SchemaVersion { get; set; } = 1;
    /// <summary>The toolkit version that created this record.</summary>
    public string ToolVersion { get; set; } = ToolkitBuildInfo.Version;
    /// <summary>The source value used by this model or operation.</summary>
    public string Source { get; set; } = string.Empty;
    /// <summary>The audited utc value used by this model or operation.</summary>
    public DateTimeOffset AuditedUtc { get; set; } = DateTimeOffset.UtcNow;
    /// <summary>The files value used by this model or operation.</summary>
    public List<CustomerAuditFile> Files { get; set; } = [];
    /// <summary>The required missing value used by this model or operation.</summary>
    public List<string> RequiredMissing { get; set; } = [];
    /// <summary>The warnings value used by this model or operation.</summary>
    public List<string> Warnings { get; set; } = [];
    /// <summary>The has known rgo eboot value used by this model or operation.</summary>
    public bool HasKnownRgoEboot { get; set; }
}

/// <summary>
/// Represents the toolkit's customer audit file model or service.
/// </summary>
public sealed class CustomerAuditFile
{
    /// <summary>The path value used by this model or operation.</summary>
    public string Path { get; set; } = string.Empty;
    /// <summary>The size value used by this model or operation.</summary>
    public long Size { get; set; }
    /// <summary>The compressed size value used by this model or operation.</summary>
    public long CompressedSize { get; set; }
    /// <summary>The sha256 value used by this model or operation.</summary>
    public string Sha256 { get; set; } = string.Empty;
    /// <summary>The kind value used by this model or operation.</summary>
    public string Kind { get; set; } = string.Empty;
    /// <summary>The is all zero value used by this model or operation.</summary>
    public bool IsAllZero { get; set; }
}

/// <summary>
/// Provides the toolkit's customer archive auditor workflow.
/// </summary>
public static class CustomerArchiveAuditor
{
    /// <summary>The fixed known rgo eboot sha256 value used by this format or revision.</summary>
    public const string KnownRgoEbootSha256 = "4a22c50c0a6ad6249dd1d9ab015d559b19b4acd79daf892a6f29a58648b834f0";
    /// <summary>The required value used by this model or operation.</summary>
    private static readonly string[] Required = ["sc.cpk", "lt.bin"];
    /// <summary>The optional value used by this model or operation.</summary>
    private static readonly string[] Optional = ["union.cpk", "pr.bin"];

    /// <summary>
    /// Audits the supplied input and returns a structured safety and compatibility report.
    /// </summary>
    /// <param name="source">The source binary data or object.</param>
    /// <param name="limits">Optional conservative safety limits; defaults are used when omitted.</param>
    /// <returns>The validated operation result.</returns>
    public static CustomerAuditReport Audit(string source, FileLimits? limits = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        limits ??= FileLimits.Default;
        string full = Path.GetFullPath(source);
        CustomerAuditReport report = new() { Source = full };
        if (Directory.Exists(full))
        {
            AuditDirectory(full, report, limits);
        }
        else if (File.Exists(full) && string.Equals(Path.GetExtension(full), ".zip", StringComparison.OrdinalIgnoreCase))
        {
            AuditZip(full, report, limits);
        }
        else if (File.Exists(full))
        {
            using FileStream stream = File.OpenRead(full);
            report.Files.Add(AuditStream(Path.GetFileName(full), stream, stream.Length, stream.Length, limits));
        }
        else
        {
            throw new ToolkitException("AUDIT_SOURCE", $"Audit source does not exist: {full}");
        }
        FinalizeReport(report);
        return report;
    }

    /// <summary>
    /// Persists the validated data to its destination.
    /// </summary>
    /// <param name="report">The report value.</param>
    /// <param name="path">The file-system path to process.</param>
    public static void Save(CustomerAuditReport report, string path)
        => AtomicFile.WriteAllText(path, JsonSerializer.Serialize(report, EbootSizePatchPlan.JsonOptions));

    /// <summary>
    /// Audits directory while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="directory">The directory value.</param>
    /// <param name="report">The report value.</param>
    /// <param name="limits">Optional conservative safety limits; defaults are used when omitted.</param>
    private static void AuditDirectory(string directory, CustomerAuditReport report, FileLimits limits)
    {
        foreach (string file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            FileInfo info = new(file);
            string relative = Path.GetRelativePath(directory, file).Replace('\\', '/');
            using FileStream stream = new(file, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.SequentialScan);
            report.Files.Add(AuditStream(relative, stream, info.Length, info.Length, limits));
        }
    }

    /// <summary>
    /// Audits ZIP while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="path">The file-system path to process.</param>
    /// <param name="report">The report value.</param>
    /// <param name="limits">Optional conservative safety limits; defaults are used when omitted.</param>
    private static void AuditZip(string path, CustomerAuditReport report, FileLimits limits)
    {
        using FileStream input = new(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.SequentialScan);
        using ZipArchive zip = new(input, ZipArchiveMode.Read, false);
        HashSet<string> paths = new(StringComparer.OrdinalIgnoreCase);
        long total = 0;
        foreach (ZipArchiveEntry entry in zip.Entries.OrderBy(static item => item.FullName, StringComparer.Ordinal))
        {
            if (string.IsNullOrEmpty(entry.Name))
            {
                continue;
            }
            string normalized = NormalizeZipPath(entry.FullName);
            if (!paths.Add(normalized))
            {
                throw new ToolkitException("ZIP_DUPLICATE", $"ZIP contains a duplicate path: {normalized}");
            }
            if (entry.Length < 0 || entry.Length > limits.MaximumInputBytes)
            {
                throw new ToolkitException("ZIP_ENTRY_LIMIT", $"ZIP entry {normalized} has invalid size {entry.Length}.");
            }
            total = checked(total + entry.Length);
            if (total > limits.MaximumInputBytes)
            {
                throw new ToolkitException("ZIP_TOTAL_LIMIT", $"ZIP expanded size exceeds {limits.MaximumInputBytes} bytes.");
            }
            if (entry.CompressedLength > 0 && entry.Length > 64L * 1024 * 1024 && entry.Length / Math.Max(1, entry.CompressedLength) > 500)
            {
                throw new ToolkitException("ZIP_RATIO", $"ZIP entry {normalized} has a suspicious compression ratio.");
            }
            using Stream stream = entry.Open();
            report.Files.Add(AuditStream(normalized, stream, entry.Length, entry.CompressedLength, limits));
        }
    }

    /// <summary>
    /// Audits stream while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="path">The file-system path to process.</param>
    /// <param name="stream">The readable and seekable stream to process.</param>
    /// <param name="size">The size value.</param>
    /// <param name="compressedSize">The compressed size value.</param>
    /// <param name="limits">Optional conservative safety limits; defaults are used when omitted.</param>
    /// <returns>The validated operation result.</returns>
    private static CustomerAuditFile AuditStream(string path, Stream stream, long size, long compressedSize, FileLimits limits)
    {
        if (size > limits.MaximumInputBytes)
        {
            throw new ToolkitException("AUDIT_FILE_LIMIT", $"File {path} exceeds configured limit.");
        }
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] prefix = new byte[32];
        int prefixCount = 0;
        bool allZero = true;
        long readTotal = 0;
        byte[] buffer = new byte[1024 * 1024];
        while (true)
        {
            int read = stream.Read(buffer, 0, buffer.Length);
            if (read == 0)
            {
                break;
            }
            readTotal += read;
            if (readTotal > size || readTotal > limits.MaximumInputBytes)
            {
                throw new ToolkitException("AUDIT_STREAM_SIZE", $"Stream {path} exceeds its declared or configured size.");
            }
            hash.AppendData(buffer, 0, read);
            if (prefixCount < prefix.Length)
            {
                int copy = Math.Min(prefix.Length - prefixCount, read);
                Array.Copy(buffer, 0, prefix, prefixCount, copy);
                prefixCount += copy;
            }
            if (allZero)
            {
                for (int i = 0; i < read; i++)
                {
                    if (buffer[i] != 0)
                    {
                        allZero = false;
                        break;
                    }
                }
            }
        }
        if (readTotal != size)
        {
            throw new ToolkitException("AUDIT_STREAM_TRUNCATED", $"Stream {path} yielded {readTotal} bytes, expected {size}.");
        }
        string sha = Convert.ToHexStringLower(hash.GetHashAndReset());
        return new CustomerAuditFile
        {
            Path = path,
            Size = size,
            CompressedSize = compressedSize,
            Sha256 = sha,
            Kind = DetectKind(path, prefix.AsSpan(0, prefixCount), allZero),
            IsAllZero = allZero
        };
    }

    /// <summary>
    /// Detects kind while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="path">The file-system path to process.</param>
    /// <param name="prefix">The prefix value.</param>
    /// <param name="allZero">The all zero value.</param>
    /// <returns>The resulting text, path, identifier, or hexadecimal digest.</returns>
    private static string DetectKind(string path, ReadOnlySpan<byte> prefix, bool allZero)
    {
        if (allZero)
        {
            return "all-zero placeholder";
        }
        if (prefix.StartsWith("CPK "u8)) return "CRI CPK archive";
        if (prefix.StartsWith("PSMF"u8)) return "PSP movie (PSMF)";
        if (prefix.StartsWith("PSAR"u8)) return "PSP update archive (PSAR)";
        if (prefix.Length >= 4 && prefix[0] == 0 && prefix[1] == (byte)'P' && prefix[2] == (byte)'S' && prefix[3] == (byte)'P') return "encrypted PSP PRX";
        if (prefix.Length >= 4 && prefix[0] == 0x7F && prefix[1] == (byte)'E' && prefix[2] == (byte)'L' && prefix[3] == (byte)'F') return "ELF executable";
        if (prefix.Length >= 4 && prefix[0] == 0 && prefix[1] == (byte)'P' && prefix[2] == (byte)'S' && prefix[3] == (byte)'F') return "PARAM.SFO";
        if (string.Equals(Path.GetExtension(path), ".pmf", StringComparison.OrdinalIgnoreCase)) return "PSP movie (PMF)";
        return "binary";
    }

    /// <summary>
    /// Normalizes ZIP path while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="value">The value to process.</param>
    /// <returns>The resulting text, path, identifier, or hexadecimal digest.</returns>
    private static string NormalizeZipPath(string value)
    {
        string normalized = value.Replace('\\', '/');
        if (normalized.StartsWith('/', StringComparison.Ordinal) || normalized.Contains(':', StringComparison.Ordinal))
        {
            throw new ToolkitException("ZIP_PATH", $"ZIP entry has an absolute path: {value}");
        }
        string[] parts = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Any(static part => part is "." or ".."))
        {
            throw new ToolkitException("ZIP_PATH", $"ZIP entry escapes extraction root: {value}");
        }
        return string.Join('/', parts);
    }

    /// <summary>
    /// Finalizes report while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="report">The report value.</param>
    private static void FinalizeReport(CustomerAuditReport report)
    {
        HashSet<string> names = report.Files.Select(file => Path.GetFileName(file.Path)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        report.RequiredMissing = Required.Where(required => !names.Contains(required)).ToList();
        foreach (string optional in Optional.Where(optional => !names.Contains(optional)))
        {
            report.Warnings.Add($"Optional game resource is missing: {optional}");
        }
        report.HasKnownRgoEboot = report.Files.Any(file => string.Equals(file.Sha256, KnownRgoEbootSha256, StringComparison.OrdinalIgnoreCase));
        if (!report.HasKnownRgoEboot)
        {
            report.Warnings.Add("Known ULJM05752 game EBOOT was not found.");
        }
        if (report.Files.Any(static file => file.IsAllZero))
        {
            report.Warnings.Add("One or more files are all-zero placeholders and cannot be used as game executables.");
        }
        if (report.RequiredMissing.Count > 0)
        {
            report.Warnings.Add("Translation extraction cannot start until sc.cpk and lt.bin are supplied.");
        }
    }
}
