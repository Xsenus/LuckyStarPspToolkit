using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LuckyStarPspToolkit;

/// <summary>
/// Defines the supported file kind values.
/// </summary>
public enum FileKind
{
    /// <summary>The zero filled value used by this model or operation.</summary>
    ZeroFilled,
    /// <summary>The param sfo value used by this model or operation.</summary>
    ParamSfo,
    /// <summary>The psp prx value used by this model or operation.</summary>
    PspPrx,
    /// <summary>The elf mips value used by this model or operation.</summary>
    ElfMips,
    /// <summary>The psar value used by this model or operation.</summary>
    Psar,
    /// <summary>The psmf value used by this model or operation.</summary>
    Psmf,
    /// <summary>The cri cpk value used by this model or operation.</summary>
    CriCpk,
    /// <summary>The gzip value used by this model or operation.</summary>
    Gzip,
    /// <summary>The iso9660 value used by this model or operation.</summary>
    Iso9660,
    /// <summary>The unknown value used by this model or operation.</summary>
    Unknown
}

/// <summary>
/// Defines the supported input container kind values.
/// </summary>
public enum InputContainerKind
{
    /// <summary>The file value used by this model or operation.</summary>
    File,
    /// <summary>The directory value used by this model or operation.</summary>
    Directory,
    /// <summary>The zip archive value used by this model or operation.</summary>
    ZipArchive
}

/// <summary>
/// Represents the toolkit's file probe model or service.
/// </summary>
public sealed class FileProbe
{
    /// <summary>The logical path value used by this model or operation.</summary>
    public required string LogicalPath { get; init; }
    /// <summary>The size value used by this model or operation.</summary>
    public required long Size { get; init; }
    /// <summary>The compressed size value used by this model or operation.</summary>
    public long? CompressedSize { get; init; }
    /// <summary>The sha256 value used by this model or operation.</summary>
    public required string Sha256 { get; init; }
    /// <summary>The kind value used by this model or operation.</summary>
    public required FileKind Kind { get; init; }
    /// <summary>The role value used by this model or operation.</summary>
    public required string Role { get; init; }
    /// <summary>The parse error value used by this model or operation.</summary>
    public string? ParseError { get; init; }
    /// <summary>The sfo value used by this model or operation.</summary>
    public SfoFile? Sfo { get; init; }
    /// <summary>The psp header value used by this model or operation.</summary>
    public PspModuleHeader? PspHeader { get; init; }
    /// <summary>The elf header value used by this model or operation.</summary>
    public Elf32Info? ElfHeader { get; init; }

    [JsonIgnore]
    /// <summary>The byte payload associated with this record.</summary>
    public required byte[] Data { get; init; }

    [JsonIgnore]
    /// <summary>The base name value used by this model or operation.</summary>
    public string BaseName => Path.GetFileName(LogicalPath.Replace('/', Path.DirectorySeparatorChar));
}

/// <summary>
/// Represents the toolkit's inspection result model or service.
/// </summary>
public sealed class InspectionResult
{
    /// <summary>The schema value used by this model or operation.</summary>
    public string Schema { get; init; } = "lucky-star-psp.file-inspection.v2";
    /// <summary>The input value used by this model or operation.</summary>
    public required string Input { get; init; }
    /// <summary>The container kind value used by this model or operation.</summary>
    public required InputContainerKind ContainerKind { get; init; }
    /// <summary>The total uncompressed size value used by this model or operation.</summary>
    public required long TotalUncompressedSize { get; init; }
    /// <summary>The files value used by this model or operation.</summary>
    public required IReadOnlyList<FileProbe> Files { get; init; }
    /// <summary>The warnings value used by this model or operation.</summary>
    public required IReadOnlyList<string> Warnings { get; init; }
}

/// <summary>
/// Represents the toolkit's input safety limits model or service.
/// </summary>
public sealed class InputSafetyLimits
{
    /// <summary>The upper safety limit for entries; input above it is rejected.</summary>
    public int MaximumEntries { get; init; } = 10_000;
    /// <summary>The upper safety limit for entry bytes; input above it is rejected.</summary>
    public long MaximumEntryBytes { get; init; } = 256L * 1024 * 1024;
    /// <summary>The upper safety limit for total bytes; input above it is rejected.</summary>
    public long MaximumTotalBytes { get; init; } = 512L * 1024 * 1024;
    /// <summary>The upper safety limit for compression ratio; input above it is rejected.</summary>
    public double MaximumCompressionRatio { get; init; } = 1_000;
    /// <summary>The compression ratio check threshold value used by this model or operation.</summary>
    public long CompressionRatioCheckThreshold { get; init; } = 16L * 1024 * 1024;
}

/// <summary>
/// Provides the toolkit's input inspector workflow.
/// </summary>
public static class InputInspector
{
    /// <summary>The path comparer value used by this model or operation.</summary>
    private static readonly StringComparer PathComparer = StringComparer.OrdinalIgnoreCase;

    /// <summary>
    /// Inspects the supplied input and returns a structured diagnostic result.
    /// </summary>
    /// <param name="path">The file-system path to process.</param>
    /// <param name="limits">Optional conservative safety limits; defaults are used when omitted.</param>
    /// <returns>The validated operation result.</returns>
    public static InspectionResult Inspect(string path, InputSafetyLimits? limits = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        limits ??= new InputSafetyLimits();
        ValidateLimits(limits);

        string fullPath = Path.GetFullPath(path);
        if (File.Exists(fullPath))
        {
            if (string.Equals(Path.GetExtension(fullPath), ".zip", StringComparison.OrdinalIgnoreCase)
                || HasZipMagic(fullPath))
            {
                return InspectZip(fullPath, limits);
            }

            byte[] data = ReadRegularFile(fullPath, limits.MaximumEntryBytes);
            FileProbe probe = Probe(Path.GetFileName(fullPath), data, compressedSize: null);
            return new InspectionResult
            {
                Input = fullPath,
                ContainerKind = InputContainerKind.File,
                TotalUncompressedSize = data.LongLength,
                Files = new[] { probe },
                Warnings = BuildWarnings(new[] { probe })
            };
        }

        if (Directory.Exists(fullPath))
        {
            return InspectDirectory(fullPath, limits);
        }

        throw new ToolkitException($"Input path does not exist: {fullPath}");
    }

    /// <summary>
    /// Classifies one supplied file by its bytes and attempts the corresponding bounded header parser.
    /// </summary>
    /// <param name="logicalPath">The logical path value.</param>
    /// <param name="data">The binary data to process.</param>
    /// <param name="compressedSize">The compressed size value.</param>
    /// <returns>The validated operation result.</returns>
    public static FileProbe Probe(string logicalPath, byte[] data, long? compressedSize)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logicalPath);
        ArgumentNullException.ThrowIfNull(data);

        FileKind kind = FileKind.Unknown;
        SfoFile? sfo = null;
        PspModuleHeader? psp = null;
        Elf32Info? elf = null;
        string? parseError = null;

        if (data.Length > 0 && BinaryData.IsAllZero(data))
        {
            kind = FileKind.ZeroFilled;
        }
        else if (SfoReader.LooksLike(data))
        {
            kind = FileKind.ParamSfo;
            TryParse(() => sfo = SfoReader.Parse(data), ref parseError);
        }
        else if (PspPrxReader.LooksLike(data))
        {
            kind = FileKind.PspPrx;
            TryParse(() => psp = PspPrxReader.ParseHeader(data), ref parseError);
        }
        else if (ElfReader.LooksLike(data))
        {
            TryParse(() => elf = ElfReader.Parse32LittleEndian(data), ref parseError);
            kind = elf?.IsPspMips == true ? FileKind.ElfMips : FileKind.Unknown;
        }
        else if (BinaryData.StartsWithAscii(data, "PSAR"))
        {
            kind = FileKind.Psar;
        }
        else if (BinaryData.StartsWithAscii(data, "PSMF"))
        {
            kind = FileKind.Psmf;
        }
        else if (BinaryData.StartsWithAscii(data, "CPK "))
        {
            kind = FileKind.CriCpk;
        }
        else if (data.Length >= 2 && data[0] == 0x1F && data[1] == 0x8B)
        {
            kind = FileKind.Gzip;
        }
        else if (LooksLikeIso9660(data))
        {
            kind = FileKind.Iso9660;
        }

        var probe = new FileProbe
        {
            LogicalPath = logicalPath.Replace('\\', '/'),
            Size = data.LongLength,
            CompressedSize = compressedSize,
            Sha256 = CryptoUtilities.Sha256Hex(data),
            Kind = kind,
            Role = string.Empty,
            ParseError = parseError,
            Sfo = sfo,
            PspHeader = psp,
            ElfHeader = elf,
            Data = data
        };

        return new FileProbe
        {
            LogicalPath = probe.LogicalPath,
            Size = probe.Size,
            CompressedSize = probe.CompressedSize,
            Sha256 = probe.Sha256,
            Kind = probe.Kind,
            Role = DetermineRole(probe),
            ParseError = probe.ParseError,
            Sfo = probe.Sfo,
            PspHeader = probe.PspHeader,
            ElfHeader = probe.ElfHeader,
            Data = probe.Data
        };
    }

    /// <summary>
    /// Formats the current result as human-readable diagnostic text.
    /// </summary>
    /// <param name="result">The result value.</param>
    /// <returns>The resulting text, path, identifier, or hexadecimal digest.</returns>
    public static string ToHumanText(InspectionResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var output = new StringBuilder();
        output.AppendLine("Lucky Star PSP input inspection");
        output.AppendLine($"  input: {result.Input}");
        output.AppendLine($"  container: {EnumText(result.ContainerKind)}");
        output.AppendLine($"  files: {result.Files.Count}");
        output.AppendLine($"  total: {result.TotalUncompressedSize} bytes");
        output.AppendLine();

        foreach (FileProbe probe in result.Files)
        {
            output.AppendLine(probe.LogicalPath);
            output.AppendLine($"  kind:   {EnumText(probe.Kind)}");
            output.AppendLine($"  size:   {probe.Size} bytes");
            if (probe.CompressedSize.HasValue)
            {
                output.AppendLine($"  packed: {probe.CompressedSize.Value} bytes");
            }
            output.AppendLine($"  sha256: {probe.Sha256}");
            if (!string.IsNullOrWhiteSpace(probe.Role))
            {
                output.AppendLine($"  role:   {probe.Role}");
            }
            if (!string.IsNullOrWhiteSpace(probe.ParseError))
            {
                output.AppendLine($"  error:  {probe.ParseError}");
            }
            if (probe.Sfo is not null)
            {
                AppendOptional(output, "disc", probe.Sfo.GetString("DISC_ID"));
                AppendOptional(output, "title", probe.Sfo.GetString("TITLE"));
                AppendOptional(output, "category", probe.Sfo.GetString("CATEGORY"));
                AppendOptional(output, "system", probe.Sfo.GetString("PSP_SYSTEM_VER"));
            }
            if (probe.PspHeader is not null)
            {
                output.AppendLine($"  module: {probe.PspHeader.ModuleName}");
                output.AppendLine($"  tag:    {HexUtilities.UInt32(probe.PspHeader.Tag)}");
                output.AppendLine($"  ELF:    {probe.PspHeader.ElfSize} bytes");
            }
            if (probe.ElfHeader is not null)
            {
                output.AppendLine($"  machine: {probe.ElfHeader.Machine} (MIPS={YesNo(probe.ElfHeader.IsPspMips)})");
                output.AppendLine($"  entry:   {HexUtilities.UInt32(probe.ElfHeader.EntryPoint)}");
            }
            output.AppendLine();
        }

        if (result.Warnings.Count > 0)
        {
            output.AppendLine("Warnings:");
            foreach (string warning in result.Warnings)
            {
                output.AppendLine($"  - {warning}");
            }
        }

        return output.ToString();
    }

    /// <summary>
    /// Inspects ZIP while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="fullPath">The full path value.</param>
    /// <param name="limits">Optional conservative safety limits; defaults are used when omitted.</param>
    /// <returns>The validated operation result.</returns>
    private static InspectionResult InspectZip(string fullPath, InputSafetyLimits limits)
    {
        var probes = new List<FileProbe>();
        var seenPaths = new HashSet<string>(PathComparer);
        long total = 0;

        try
        {
            using FileStream stream = new(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read,
                bufferSize: 128 * 1024, options: FileOptions.SequentialScan);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
            Guard.Require(archive.Entries.Count <= limits.MaximumEntries,
                $"ZIP contains too many entries ({archive.Entries.Count}; maximum {limits.MaximumEntries}).");

            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                if (IsDirectoryEntry(entry))
                {
                    continue;
                }

                string logicalPath = NormalizeArchivePath(entry.FullName);
                Guard.Require(seenPaths.Add(logicalPath),
                    $"ZIP contains duplicate logical path '{logicalPath}'.");
                ValidateEntrySize(logicalPath, entry.Length, limits);
                total = checked(total + entry.Length);
                Guard.Require(total <= limits.MaximumTotalBytes,
                    $"ZIP expands beyond the configured total limit ({limits.MaximumTotalBytes} bytes).");
                ValidateCompressionRatio(entry, logicalPath, limits);

                byte[] data = ReadZipEntryExactly(entry);
                probes.Add(Probe(logicalPath, data, entry.CompressedLength));
            }
        }
        catch (ToolkitException)
        {
            throw;
        }
        catch (InvalidDataException exception)
        {
            throw new ToolkitException("Invalid or damaged ZIP archive.", exception);
        }
        catch (IOException exception)
        {
            throw new ToolkitException($"Cannot read ZIP archive '{fullPath}'.", exception);
        }

        probes.Sort((left, right) => StringComparer.Ordinal.Compare(left.LogicalPath, right.LogicalPath));
        return new InspectionResult
        {
            Input = fullPath,
            ContainerKind = InputContainerKind.ZipArchive,
            TotalUncompressedSize = total,
            Files = probes.AsReadOnly(),
            Warnings = BuildWarnings(probes)
        };
    }

    /// <summary>
    /// Inspects directory while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="root">The root value.</param>
    /// <param name="limits">Optional conservative safety limits; defaults are used when omitted.</param>
    /// <returns>The validated operation result.</returns>
    private static InspectionResult InspectDirectory(string root, InputSafetyLimits limits)
    {
        var paths = new List<string>();
        try
        {
            var options = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = false,
                ReturnSpecialDirectories = false,
                AttributesToSkip = FileAttributes.ReparsePoint
            };
            foreach (string path in Directory.EnumerateFiles(root, "*", options))
            {
                paths.Add(path);
                Guard.Require(paths.Count <= limits.MaximumEntries,
                    $"Directory contains more than {limits.MaximumEntries} files.");
            }
        }
        catch (ToolkitException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new ToolkitException($"Cannot enumerate directory '{root}'.", exception);
        }

        paths.Sort(StringComparer.Ordinal);
        var probes = new List<FileProbe>(paths.Count);
        long total = 0;
        foreach (string path in paths)
        {
            var info = new FileInfo(path);
            ValidateEntrySize(path, info.Length, limits);
            total = checked(total + info.Length);
            Guard.Require(total <= limits.MaximumTotalBytes,
                $"Directory data exceeds the configured total limit ({limits.MaximumTotalBytes} bytes).");
            byte[] data = ReadRegularFile(path, limits.MaximumEntryBytes);
            string logicalPath = Path.GetRelativePath(root, path).Replace('\\', '/');
            probes.Add(Probe(logicalPath, data, compressedSize: null));
        }

        return new InspectionResult
        {
            Input = root,
            ContainerKind = InputContainerKind.Directory,
            TotalUncompressedSize = total,
            Files = probes.AsReadOnly(),
            Warnings = BuildWarnings(probes)
        };
    }

    /// <summary>
    /// Reads regular file while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="path">The file-system path to process.</param>
    /// <param name="maximumBytes">The maximum bytes value.</param>
    /// <returns>The resulting binary or typed sequence.</returns>
    private static byte[] ReadRegularFile(string path, long maximumBytes)
    {
        try
        {
            var info = new FileInfo(path);
            Guard.Require(info.Length >= 0 && info.Length <= maximumBytes && info.Length <= int.MaxValue,
                $"File '{path}' exceeds the supported size limit.");
            byte[] data = new byte[(int)info.Length];
            using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                bufferSize: 128 * 1024, options: FileOptions.SequentialScan);
            ReadExactly(stream, data, path);
            Guard.Require(stream.ReadByte() == -1, $"File '{path}' changed while it was being read.");
            return data;
        }
        catch (ToolkitException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new ToolkitException($"Cannot read file '{path}'.", exception);
        }
    }

    /// <summary>
    /// Reads ZIP entry exactly while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="entry">The validated archive or ISO entry to process.</param>
    /// <returns>The resulting binary or typed sequence.</returns>
    private static byte[] ReadZipEntryExactly(ZipArchiveEntry entry)
    {
        Guard.Require(entry.Length <= int.MaxValue,
            $"ZIP entry '{entry.FullName}' is too large for this process.");
        byte[] data = new byte[(int)entry.Length];
        using Stream stream = entry.Open();
        ReadExactly(stream, data, entry.FullName);
        Guard.Require(stream.ReadByte() == -1,
            $"ZIP entry '{entry.FullName}' produced more data than declared.");
        return data;
    }

    /// <summary>
    /// Reads exactly while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="stream">The readable and seekable stream to process.</param>
    /// <param name="destination">The destination stream or buffer.</param>
    /// <param name="context">A diagnostic label included in validation errors.</param>
    private static void ReadExactly(Stream stream, Span<byte> destination, string context)
    {
        int offset = 0;
        while (offset < destination.Length)
        {
            int read = stream.Read(destination[offset..]);
            Guard.Require(read > 0, $"Unexpected end of input while reading '{context}'.");
            offset += read;
        }
    }

    /// <summary>
    /// Validates entry size while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="name">The logical name used for lookup or diagnostics.</param>
    /// <param name="length">The number of bytes or elements to process.</param>
    /// <param name="limits">Optional conservative safety limits; defaults are used when omitted.</param>
    /// <remarks>Malformed input or violated preconditions are rejected before a persistent output is committed.</remarks>
    private static void ValidateEntrySize(string name, long length, InputSafetyLimits limits)
    {
        Guard.Require(length >= 0, $"Entry '{name}' has a negative length.");
        Guard.Require(length <= limits.MaximumEntryBytes,
            $"Entry '{name}' exceeds the per-file limit ({limits.MaximumEntryBytes} bytes).");
        Guard.Require(length <= int.MaxValue,
            $"Entry '{name}' cannot be held safely in memory by this build.");
    }

    /// <summary>
    /// Validates compression ratio while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="entry">The validated archive or ISO entry to process.</param>
    /// <param name="logicalPath">The logical path value.</param>
    /// <param name="limits">Optional conservative safety limits; defaults are used when omitted.</param>
    /// <remarks>Malformed input or violated preconditions are rejected before a persistent output is committed.</remarks>
    private static void ValidateCompressionRatio(
        ZipArchiveEntry entry,
        string logicalPath,
        InputSafetyLimits limits)
    {
        if (entry.Length < limits.CompressionRatioCheckThreshold)
        {
            return;
        }

        Guard.Require(entry.CompressedLength > 0,
            $"ZIP entry '{logicalPath}' has an unsafe zero compressed length.");
        double ratio = entry.Length / (double)entry.CompressedLength;
        Guard.Require(ratio <= limits.MaximumCompressionRatio,
            $"ZIP entry '{logicalPath}' has a suspicious compression ratio ({ratio:F1}:1).");
    }

    /// <summary>
    /// Normalizes archive path while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="path">The file-system path to process.</param>
    /// <returns>The resulting text, path, identifier, or hexadecimal digest.</returns>
    private static string NormalizeArchivePath(string path)
    {
        Guard.Require(!string.IsNullOrWhiteSpace(path), "ZIP contains an empty entry name.");
        Guard.Require(path.IndexOf('\0') < 0, "ZIP entry name contains a NUL character.");
        string normalized = path.Replace('\\', '/');
        Guard.Require(!normalized.StartsWith('/', StringComparison.Ordinal),
            $"ZIP entry uses an absolute path: '{path}'.");
        Guard.Require(!(normalized.Length >= 2 && char.IsLetter(normalized[0]) && normalized[1] == ':'),
            $"ZIP entry uses a drive-qualified path: '{path}'.");

        string[] segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        Guard.Require(segments.Length > 0, $"ZIP entry path is invalid: '{path}'.");
        foreach (string segment in segments)
        {
            Guard.Require(segment is not "." and not "..",
                $"ZIP entry attempts path traversal: '{path}'.");
        }

        return string.Join("/", segments);
    }

    /// <summary>
    /// Determines whether directory entry.
    /// </summary>
    /// <param name="entry">The validated archive or ISO entry to process.</param>
    /// <returns><see langword="true"/> when the condition is satisfied; otherwise <see langword="false"/>.</returns>
    private static bool IsDirectoryEntry(ZipArchiveEntry entry)
    {
        return entry.FullName.EndsWith("/", StringComparison.Ordinal)
            || string.IsNullOrEmpty(entry.Name);
    }

    /// <summary>
    /// Determines whether ZIP magic.
    /// </summary>
    /// <param name="path">The file-system path to process.</param>
    /// <returns><see langword="true"/> when the condition is satisfied; otherwise <see langword="false"/>.</returns>
    private static bool HasZipMagic(string path)
    {
        try
        {
            Span<byte> header = stackalloc byte[4];
            using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                bufferSize: 4, options: FileOptions.SequentialScan);
            int read = stream.Read(header);
            if (read < 4 || header[0] != (byte)'P' || header[1] != (byte)'K')
            {
                return false;
            }
            return (header[2] == 0x03 && header[3] == 0x04)
                || (header[2] == 0x05 && header[3] == 0x06)
                || (header[2] == 0x07 && header[3] == 0x08);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new ToolkitException($"Cannot inspect file signature for '{path}'.", exception);
        }
    }

    /// <summary>
    /// Checks the primary ISO descriptor signature at logical sector 16; this is not full ISO validation.
    /// </summary>
    /// <param name="data">The binary data to process.</param>
    /// <returns><see langword="true"/> when the condition is satisfied; otherwise <see langword="false"/>.</returns>
    private static bool LooksLikeIso9660(ReadOnlySpan<byte> data)
    {
        const int primaryVolumeDescriptor = 16 * 2048;
        return data.Length >= primaryVolumeDescriptor + 6
            && data[primaryVolumeDescriptor] == 1
            && data.Slice(primaryVolumeDescriptor + 1, 5).SequenceEqual("CD001"u8);
    }

    /// <summary>
    /// Determines role while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="probe">The probe value.</param>
    /// <returns>The resulting text, path, identifier, or hexadecimal digest.</returns>
    private static string DetermineRole(FileProbe probe)
    {
        if (probe.Sfo is not null)
        {
            string discId = probe.Sfo.GetString("DISC_ID") ?? string.Empty;
            string category = probe.Sfo.GetString("CATEGORY") ?? string.Empty;
            if (discId == RgoProfile.DiscId)
            {
                return "Lucky Star: Ryouou Gakuen Outousai Portable game metadata";
            }
            if (discId == "MSTKUPDATE" || category == "MG")
            {
                return "PSP firmware updater metadata; not game translation data";
            }
        }

        if (probe.PspHeader is not null)
        {
            if (probe.PspHeader.ModuleName == "user_main")
            {
                return "encrypted game executable; authenticate and decrypt before patching";
            }
            if (probe.PspHeader.ModuleName == "updater")
            {
                return "PSP firmware updater executable; not the game executable";
            }
            return "encrypted PSP module";
        }

        return probe.Kind switch
        {
            FileKind.ZeroFilled => "all bytes are zero; not a usable BOOT/ELF",
            FileKind.Psar => "PSP firmware update archive; not a game script archive",
            FileKind.Psmf => "PSP movie stream; may contain rendered text, not editable dialogue",
            FileKind.CriCpk => "CRI CPK archive; candidate for script or image extraction",
            FileKind.ElfMips => "decrypted MIPS executable suitable for compatibility checks",
            FileKind.Iso9660 => "optical-disc image; inspect its PSP_GAME tree before modification",
            _ => string.Empty
        };
    }

    /// <summary>
    /// Builds warnings while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="probes">The probes value.</param>
    /// <returns>The validated operation result.</returns>
    /// <remarks>Malformed input or violated preconditions are rejected before a persistent output is committed.</remarks>
    private static IReadOnlyList<string> BuildWarnings(IEnumerable<FileProbe> probes)
    {
        var warnings = new List<string>();
        foreach (FileProbe probe in probes)
        {
            if (!string.IsNullOrWhiteSpace(probe.ParseError))
            {
                warnings.Add($"{probe.LogicalPath}: {probe.ParseError}");
            }
        }
        return warnings.AsReadOnly();
    }

    /// <summary>
    /// Validates limits while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="limits">Optional conservative safety limits; defaults are used when omitted.</param>
    /// <remarks>Malformed input or violated preconditions are rejected before a persistent output is committed.</remarks>
    private static void ValidateLimits(InputSafetyLimits limits)
    {
        Guard.Require(limits.MaximumEntries > 0, "MaximumEntries must be positive.");
        Guard.Require(limits.MaximumEntryBytes > 0, "MaximumEntryBytes must be positive.");
        Guard.Require(limits.MaximumTotalBytes >= limits.MaximumEntryBytes,
            "MaximumTotalBytes must be at least MaximumEntryBytes.");
        Guard.Require(limits.MaximumTotalBytes <= int.MaxValue,
            "MaximumTotalBytes cannot exceed the safe in-memory limit for this build.");
        Guard.Require(limits.MaximumCompressionRatio >= 1, "MaximumCompressionRatio must be at least one.");
        Guard.Require(limits.CompressionRatioCheckThreshold >= 0,
            "CompressionRatioCheckThreshold cannot be negative.");
    }

    /// <summary>
    /// Attempts to parse.
    /// </summary>
    /// <param name="action">The action value.</param>
    /// <param name="error">The mutable error value updated by the operation.</param>
    private static void TryParse(Action action, ref string? error)
    {
        try
        {
            action();
        }
        catch (ToolkitException exception)
        {
            error = exception.Message;
        }
    }

    /// <summary>
    /// Appends optional while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="output">The destination stream, buffer, or model.</param>
    /// <param name="label">The label value.</param>
    /// <param name="value">The value to process.</param>
    private static void AppendOptional(StringBuilder output, string label, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            output.AppendLine($"  {label}: {value}");
        }
    }

    /// <summary>
    /// Formats a Boolean value as stable human-readable yes/no text.
    /// </summary>
    /// <param name="value">The value to process.</param>
    /// <returns>The resulting text, path, identifier, or hexadecimal digest.</returns>
    private static string YesNo(bool value) => value ? "yes" : "no";

    /// <summary>
    /// Converts an enum name into the kebab-case spelling used in inspection reports.
    /// </summary>
    /// <typeparam name="T">The t type used by the operation.</typeparam>
    /// <param name="value">The value to process.</param>
    /// <returns>The resulting text, path, identifier, or hexadecimal digest.</returns>
    private static string EnumText<T>(T value) where T : struct, Enum
    {
        return JsonNamingPolicy.KebabCaseLower.ConvertName(value.ToString());
    }
}
