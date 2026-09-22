using System.Security.Cryptography;
using System.Text;
using LuckyStarPspToolkit.Formats.Common;

namespace LuckyStarPspToolkit.Formats.Iso;

/// <summary>
/// Represents the toolkit's ISO 9660 image model or service.
/// </summary>
public sealed class Iso9660Image : IDisposable
{
    /// <summary>The fixed descriptor sector size value used by this format or revision.</summary>
    public const int DescriptorSectorSize = 2048;
    /// <summary>The fixed first volume descriptor sector value used by this format or revision.</summary>
    private const int FirstVolumeDescriptorSector = 16;
    /// <summary>The upper safety limit for volume descriptors; input above it is rejected.</summary>
    private const int MaximumVolumeDescriptors = 256;

    /// <summary>Stores the source state owned by this instance or type.</summary>
    private readonly SeekableDataSource _source;
    /// <summary>Stores the by path state owned by this instance or type.</summary>
    private readonly Dictionary<string, Iso9660Entry> _byPath;
    /// <summary>Stores the limits state owned by this instance or type.</summary>
    private readonly FileLimits _limits;
    /// <summary>Stores the disposed state owned by this instance or type.</summary>
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance with validated constructor state.
    /// </summary>
    /// <param name="source">The source binary data or object.</param>
    /// <param name="limits">Optional conservative safety limits; defaults are used when omitted.</param>
    /// <param name="sourceName">The source name value.</param>
    /// <param name="volumeIdentifier">The volume identifier value.</param>
    /// <param name="volumeBlockCount">The volume block count value.</param>
    /// <param name="logicalBlockSize">The logical block size value.</param>
    /// <param name="primaryVolumeDescriptorOffset">The primary volume descriptor offset value.</param>
    /// <param name="hasSupplementaryVolumeDescriptor">The has supplementary volume descriptor value.</param>
    /// <param name="hasPartitionDescriptor">The has partition descriptor value.</param>
    /// <param name="hasUnsupportedVolumeDescriptor">The has unsupported volume descriptor value.</param>
    /// <param name="root">The root value.</param>
    /// <param name="byPath">The by path value.</param>
    private Iso9660Image(
        SeekableDataSource source,
        FileLimits limits,
        string sourceName,
        string volumeIdentifier,
        uint volumeBlockCount,
        int logicalBlockSize,
        long primaryVolumeDescriptorOffset,
        bool hasSupplementaryVolumeDescriptor,
        bool hasPartitionDescriptor,
        bool hasUnsupportedVolumeDescriptor,
        Iso9660Entry root,
        Dictionary<string, Iso9660Entry> byPath)
    {
        _source = source;
        _limits = limits;
        SourceName = sourceName;
        VolumeIdentifier = volumeIdentifier;
        VolumeBlockCount = volumeBlockCount;
        LogicalBlockSize = logicalBlockSize;
        PrimaryVolumeDescriptorOffset = primaryVolumeDescriptorOffset;
        HasSupplementaryVolumeDescriptor = hasSupplementaryVolumeDescriptor;
        HasPartitionDescriptor = hasPartitionDescriptor;
        HasUnsupportedVolumeDescriptor = hasUnsupportedVolumeDescriptor;
        Root = root;
        _byPath = byPath;
        Entries = byPath.Values
            .OrderBy(static entry => entry.Path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>The source name value used by this model or operation.</summary>
    public string SourceName { get; }
    /// <summary>The source size value used by this model or operation.</summary>
    public long SourceSize => _source.Length;
    /// <summary>The volume identifier value used by this model or operation.</summary>
    public string VolumeIdentifier { get; }
    /// <summary>The volume block count value used by this model or operation.</summary>
    public uint VolumeBlockCount { get; }
    /// <summary>The volume size value used by this model or operation.</summary>
    public long VolumeSize => checked((long)VolumeBlockCount * LogicalBlockSize);
    /// <summary>The logical block size value used by this model or operation.</summary>
    public int LogicalBlockSize { get; }
    /// <summary>The root value used by this model or operation.</summary>
    public Iso9660Entry Root { get; }
    /// <summary>The entries value used by this model or operation.</summary>
    public IReadOnlyList<Iso9660Entry> Entries { get; }

    /// <summary>The primary volume descriptor offset value used by this model or operation.</summary>
    internal long PrimaryVolumeDescriptorOffset { get; }
    /// <summary>The has supplementary volume descriptor value used by this model or operation.</summary>
    internal bool HasSupplementaryVolumeDescriptor { get; }
    /// <summary>The has partition descriptor value used by this model or operation.</summary>
    internal bool HasPartitionDescriptor { get; }
    /// <summary>The has unsupported volume descriptor value used by this model or operation.</summary>
    internal bool HasUnsupportedVolumeDescriptor { get; }
    /// <summary>The limits value used by this model or operation.</summary>
    internal FileLimits Limits => _limits;

    /// <summary>
    /// Opens a protected ISO path and validates its descriptors and bounded directory tree.
    /// </summary>
    /// <param name="path">The file-system path to process.</param>
    /// <param name="limits">Optional conservative safety limits; defaults are used when omitted.</param>
    /// <returns>The validated operation result.</returns>
    public static Iso9660Image Open(string path, FileLimits? limits = null)
    {
        limits ??= FileLimits.Default;
        string normalized = PathUtilities.NormalizeProtectedPath(path, "ISO_REPARSE_POINT");
        if (!File.Exists(normalized))
        {
            throw new ToolkitException("ISO_NOT_FOUND", $"ISO image not found: {normalized}");
        }
        FileInfo info = new(normalized);
        if (info.Length > limits.MaximumInputBytes)
        {
            throw new ToolkitException(
                "ISO_TOO_LARGE",
                $"ISO image is {info.Length} bytes; configured limit is {limits.MaximumInputBytes} bytes.");
        }

        SeekableDataSource source = SeekableDataSource.OpenFile(normalized);
        try
        {
            return ParseCore(source, limits, normalized);
        }
        catch
        {
            source.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Parses stream while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="stream">The readable and seekable stream to process.</param>
    /// <param name="sourceName">The source name value.</param>
    /// <param name="limits">Optional conservative safety limits; defaults are used when omitted.</param>
    /// <param name="leaveOpen">Whether ownership of the supplied stream remains with the caller.</param>
    /// <returns>The validated operation result.</returns>
    /// <remarks>Malformed input or violated preconditions are rejected before a persistent output is committed.</remarks>
    internal static Iso9660Image ParseStream(
        Stream stream,
        string sourceName,
        FileLimits? limits = null,
        bool leaveOpen = true)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceName);
        limits ??= FileLimits.Default;
        if (stream.Length > limits.MaximumInputBytes)
        {
            throw new ToolkitException(
                "ISO_TOO_LARGE",
                $"ISO image is {stream.Length} bytes; configured limit is {limits.MaximumInputBytes} bytes.");
        }

        SeekableDataSource source = SeekableDataSource.FromStream(stream, leaveOpen);
        try
        {
            return ParseCore(source, limits, sourceName);
        }
        catch
        {
            source.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Parses validated input into the current binary-format model.
    /// </summary>
    /// <param name="data">The binary data to process.</param>
    /// <param name="limits">Optional conservative safety limits; defaults are used when omitted.</param>
    /// <returns>The validated operation result.</returns>
    /// <remarks>Malformed input or violated preconditions are rejected before a persistent output is committed.</remarks>
    public static Iso9660Image Parse(ReadOnlyMemory<byte> data, FileLimits? limits = null)
    {
        limits ??= FileLimits.Default;
        if (data.Length > limits.MaximumInputBytes)
        {
            throw new ToolkitException(
                "ISO_TOO_LARGE",
                $"ISO image is {data.Length} bytes; configured limit is {limits.MaximumInputBytes} bytes.");
        }
        SeekableDataSource source = SeekableDataSource.FromMemory(data);
        try
        {
            return ParseCore(source, limits, "<memory>");
        }
        catch
        {
            source.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Creates summary while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <returns>The validated operation result.</returns>
    public Iso9660Summary CreateSummary()
    {
        ThrowIfDisposed();
        return new Iso9660Summary
        {
            Source = SourceName,
            SourceSize = SourceSize,
            VolumeIdentifier = VolumeIdentifier,
            VolumeBlockCount = VolumeBlockCount,
            LogicalBlockSize = LogicalBlockSize,
            EntryCount = Entries.Count,
            Entries = Entries
        };
    }

    /// <summary>
    /// Estimates a conservative UTF-8 JSON listing size before allocating a complete report.
    /// </summary>
    /// <returns>The validated operation result.</returns>
    public long EstimateListingBytes()
    {
        ThrowIfDisposed();
        long estimate = 2048;
        foreach (Iso9660Entry entry in Entries)
        {
            long pathBytes = Encoding.UTF8.GetByteCount(entry.Path);
            // Summary/listing JSON repeats the path component in several properties.
            // Keep this estimate intentionally conservative so the preflight happens
            // before allocating a potentially huge StringBuilder or JSON string.
            estimate = checked(estimate + 1024 + (pathBytes * 6));
            estimate = checked(estimate + (entry.Extents.Count * 128L));
        }
        return estimate;
    }

    /// <summary>
    /// Attempts to get entry.
    /// </summary>
    /// <param name="path">The file-system path to process.</param>
    /// <param name="entry">Receives validated archive or ISO entry to process when the operation succeeds.</param>
    /// <returns><see langword="true"/> when the requested value was produced; otherwise <see langword="false"/>.</returns>
    public bool TryGetEntry(string path, out Iso9660Entry? entry)
    {
        ThrowIfDisposed();
        return _byPath.TryGetValue(NormalizeLookupPath(path), out entry);
    }

    /// <summary>
    /// Gets entry while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="path">The file-system path to process.</param>
    /// <returns>The validated operation result.</returns>
    public Iso9660Entry GetEntry(string path)
    {
        if (!TryGetEntry(path, out Iso9660Entry? entry) || entry is null)
        {
            throw new ToolkitException("ISO_ENTRY_NOT_FOUND", $"ISO entry not found: {path}");
        }
        return entry;
    }

    /// <summary>
    /// Reads file while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="path">The file-system path to process.</param>
    /// <param name="maximumBytes">The maximum bytes value.</param>
    /// <returns>The resulting binary or typed sequence.</returns>
    public byte[] ReadFile(string path, long? maximumBytes = null)
    {
        Iso9660Entry entry = GetEntry(path);
        if (entry.IsDirectory)
        {
            throw new ToolkitException("ISO_ENTRY_DIRECTORY", $"ISO entry is a directory: {entry.Path}");
        }
        long limit = maximumBytes ?? _limits.MaximumInputBytes;
        if (entry.Size > limit || entry.Size > int.MaxValue)
        {
            throw new ToolkitException(
                "ISO_ENTRY_TOO_LARGE",
                $"ISO entry {entry.Path} is {entry.Size} bytes; in-memory limit is {Math.Min(limit, int.MaxValue)} bytes.");
        }
        using MemoryStream output = new(checked((int)entry.Size));
        CopyFileTo(entry, output);
        return output.ToArray();
    }

    /// <summary>
    /// Extracts file while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="path">The file-system path to process.</param>
    /// <param name="outputPath">The destination file path.</param>
    public void ExtractFile(string path, string outputPath)
    {
        Iso9660Entry entry = GetEntry(path);
        if (entry.IsDirectory)
        {
            throw new ToolkitException("ISO_ENTRY_DIRECTORY", $"ISO entry is a directory: {entry.Path}");
        }
        if (!string.Equals(SourceName, "<memory>", StringComparison.Ordinal))
        {
            PathUtilities.RequireDifferent(
                SourceName,
                outputPath,
                "ISO_OUTPUT_SOURCE",
                "ISO extraction output cannot overwrite the source image.");
        }
        AtomicFile.WriteStream(outputPath, output => CopyFileTo(entry, output));
    }

    /// <summary>
    /// Copies file to while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="path">The file-system path to process.</param>
    /// <param name="destination">The destination stream or buffer.</param>
    /// <param name="hash">An optional incremental hash updated with copied bytes.</param>
    public void CopyFileTo(string path, Stream destination, IncrementalHash? hash = null)
        => CopyFileTo(GetEntry(path), destination, hash);

    /// <summary>
    /// Copies file to while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="entry">The validated archive or ISO entry to process.</param>
    /// <param name="destination">The destination stream or buffer.</param>
    /// <param name="hash">An optional incremental hash updated with copied bytes.</param>
    public void CopyFileTo(Iso9660Entry entry, Stream destination, IncrementalHash? hash = null)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(destination);
        if (entry.IsDirectory)
        {
            throw new ToolkitException("ISO_ENTRY_DIRECTORY", $"ISO entry is a directory: {entry.Path}");
        }
        if (!_byPath.TryGetValue(entry.Path, out Iso9660Entry? canonical)
            || !ReferenceEquals(canonical, entry))
        {
            throw new ToolkitException("ISO_ENTRY_FOREIGN", "ISO entry does not belong to this parsed image instance.");
        }

        long copied = 0;
        foreach (Iso9660Extent extent in entry.Extents)
        {
            long offset = extent.ByteOffset(LogicalBlockSize);
            _source.CopyRangeTo(offset, extent.ByteLength, destination, hash);
            copied = checked(copied + extent.ByteLength);
        }
        if (copied != entry.Size)
        {
            throw new ToolkitException(
                "ISO_EXTENT_SIZE",
                $"ISO entry {entry.Path} declared {entry.Size} bytes but its extents contain {copied} bytes.");
        }
    }

    /// <summary>
    /// Computes SHA-256 over exactly one ISO file payload, including all of its extents in order.
    /// </summary>
    /// <param name="path">The file-system path to process.</param>
    /// <returns>The resulting text, path, identifier, or hexadecimal digest.</returns>
    public string ComputeFileSha256(string path)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        CopyFileTo(path, Stream.Null, hash);
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    /// <summary>
    /// Computes SHA-256 over the complete source image.
    /// </summary>
    /// <returns>The resulting text, path, identifier, or hexadecimal digest.</returns>
    public string ComputeSourceSha256()
    {
        ThrowIfDisposed();
        return _source.ComputeSha256();
    }

    /// <summary>
    /// Copies source to while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="destination">The destination stream or buffer.</param>
    /// <param name="hash">An optional incremental hash updated with copied bytes.</param>
    internal void CopySourceTo(Stream destination, IncrementalHash? hash = null)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(destination);
        _source.CopyRangeTo(0, _source.Length, destination, hash);
    }

    /// <summary>
    /// Reads source exactly while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="offset">The zero-based byte or element offset.</param>
    /// <param name="destination">The destination stream or buffer.</param>
    /// <param name="context">A diagnostic label included in validation errors.</param>
    internal void ReadSourceExactly(long offset, Span<byte> destination, string context)
    {
        ThrowIfDisposed();
        _source.ReadExactly(offset, destination, context);
    }

    /// <summary>
    /// Releases owned streams and other disposable resources.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _source.Dispose();
    }

    /// <summary>
    /// Parses core while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="source">The source binary data or object.</param>
    /// <param name="limits">Optional conservative safety limits; defaults are used when omitted.</param>
    /// <param name="sourceName">The source name value.</param>
    /// <returns>The validated operation result.</returns>
    /// <remarks>Malformed input or violated preconditions are rejected before a persistent output is committed.</remarks>
    private static Iso9660Image ParseCore(
        SeekableDataSource source,
        FileLimits limits,
        string sourceName)
    {
        long descriptorOffset = checked((long)FirstVolumeDescriptorSector * DescriptorSectorSize);
        if (source.Length < descriptorOffset + DescriptorSectorSize)
        {
            throw new ToolkitException("ISO_TOO_SMALL", "Input is too small to contain an ISO 9660 primary volume descriptor.");
        }

        byte[] descriptor = new byte[DescriptorSectorSize];
        byte[]? primary = null;
        long primaryOffset = -1;
        bool hasSupplementary = false;
        bool hasPartition = false;
        bool hasUnsupportedDescriptor = false;
        bool terminatorFound = false;
        for (int index = 0; index < MaximumVolumeDescriptors; index++)
        {
            long offset = checked((long)(FirstVolumeDescriptorSector + index) * DescriptorSectorSize);
            if (offset > source.Length - DescriptorSectorSize)
            {
                break;
            }
            source.ReadExactly(offset, descriptor, $"ISO volume descriptor {index}");
            if (!descriptor.AsSpan(1, 5).SequenceEqual("CD001"u8) || descriptor[6] != 1)
            {
                throw new ToolkitException(
                    "ISO_DESCRIPTOR",
                    $"Invalid ISO 9660 volume descriptor at sector {FirstVolumeDescriptorSector + index}.");
            }

            byte type = descriptor[0];
            if (type == 1)
            {
                if (primary is not null)
                {
                    throw new ToolkitException("ISO_MULTIPLE_PVD", "ISO contains more than one primary volume descriptor.");
                }
                primary = descriptor.ToArray();
                primaryOffset = offset;
            }
            else if (type == 2)
            {
                hasSupplementary = true;
            }
            else if (type == 3)
            {
                hasPartition = true;
            }
            else if (type == 255)
            {
                terminatorFound = true;
                break;
            }
            else if (type != 0)
            {
                hasUnsupportedDescriptor = true;
            }
        }

        if (primary is null)
        {
            throw new ToolkitException("ISO_PVD_MISSING", "ISO 9660 primary volume descriptor was not found.");
        }
        if (!terminatorFound)
        {
            throw new ToolkitException("ISO_TERMINATOR_MISSING", "ISO 9660 volume descriptor terminator was not found.");
        }

        ushort logicalBlockSize = ReadBothEndianUInt16(primary, 128, "logical block size");
        if (logicalBlockSize != DescriptorSectorSize)
        {
            throw new ToolkitException(
                "ISO_BLOCK_SIZE",
                $"Unsupported ISO logical block size {logicalBlockSize}; PSP images are expected to use {DescriptorSectorSize} bytes.");
        }
        ushort volumeSetSize = ReadBothEndianUInt16(primary, 120, "volume set size");
        ushort volumeSequence = ReadBothEndianUInt16(primary, 124, "volume sequence number");
        if (volumeSetSize != 1 || volumeSequence != 1)
        {
            throw new ToolkitException(
                "ISO_MULTI_VOLUME",
                $"Multi-volume ISO sets are not supported (set size {volumeSetSize}, sequence {volumeSequence}).");
        }
        if (primary[881] != 1)
        {
            throw new ToolkitException("ISO_FILE_STRUCTURE_VERSION", $"Unsupported ISO file structure version {primary[881]}.");
        }

        uint volumeBlockCount = ReadBothEndianUInt32(primary, 80, "volume block count");
        if (volumeBlockCount == 0)
        {
            throw new ToolkitException("ISO_VOLUME_SIZE", "ISO volume block count is zero.");
        }
        long volumeSize = checked((long)volumeBlockCount * logicalBlockSize);
        if (volumeSize > source.Length)
        {
            throw new ToolkitException(
                "ISO_VOLUME_TRUNCATED",
                $"ISO volume declares {volumeSize} bytes but the source contains {source.Length} bytes.");
        }

        string volumeIdentifier = DecodePaddedAscii(primary.AsSpan(40, 32));
        int rootLength = primary[156];
        if (rootLength < 34 || 156 + rootLength > primary.Length)
        {
            throw new ToolkitException("ISO_ROOT_RECORD", "ISO root directory record is invalid.");
        }
        ParsedDirectoryRecord rootRecord = ParseDirectoryRecord(
            primary.AsSpan(156, rootLength),
            logicalBlockSize,
            source.Length,
            volumeSize,
            "root directory");
        if (!rootRecord.IsDirectory || rootRecord.SpecialIdentifier != 0)
        {
            throw new ToolkitException("ISO_ROOT_RECORD", "ISO primary volume descriptor does not contain a valid root directory record.");
        }

        Iso9660Entry root = new()
        {
            Path = string.Empty,
            Identifier = string.Empty,
            RawIdentifier = "<root>",
            IsDirectory = true,
            IsHidden = (rootRecord.Flags & 0x01) != 0,
            Flags = rootRecord.Flags,
            Size = rootRecord.DataLength,
            Extents = [new Iso9660Extent(rootRecord.LogicalBlock, rootRecord.DataLength)]
        };

        Dictionary<string, Iso9660Entry> byPath = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> visitedDirectories = new(StringComparer.Ordinal);
        int extentCount = 1;
        long totalDirectoryBytes = 0;
        ParseDirectoryRecursive(
            source,
            root,
            string.Empty,
            logicalBlockSize,
            volumeSize,
            limits,
            byPath,
            visitedDirectories,
            ref extentCount,
            ref totalDirectoryBytes,
            0);

        return new Iso9660Image(
            source,
            limits,
            sourceName,
            volumeIdentifier,
            volumeBlockCount,
            logicalBlockSize,
            primaryOffset,
            hasSupplementary,
            hasPartition,
            hasUnsupportedDescriptor,
            root,
            byPath);
    }

    /// <summary>
    /// Parses directory recursive while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="source">The source binary data or object.</param>
    /// <param name="directory">The directory value.</param>
    /// <param name="parentPath">The parent path value.</param>
    /// <param name="blockSize">The block size value.</param>
    /// <param name="volumeSize">The volume size value.</param>
    /// <param name="limits">Optional conservative safety limits; defaults are used when omitted.</param>
    /// <param name="byPath">The by path value.</param>
    /// <param name="visitedDirectories">The visited directories value.</param>
    /// <param name="extentCount">The mutable extent count value updated by the operation.</param>
    /// <param name="totalDirectoryBytes">The mutable total directory bytes value updated by the operation.</param>
    /// <param name="depth">The depth value.</param>
    /// <remarks>Malformed input or violated preconditions are rejected before a persistent output is committed.</remarks>
    private static void ParseDirectoryRecursive(
        SeekableDataSource source,
        Iso9660Entry directory,
        string parentPath,
        int blockSize,
        long volumeSize,
        FileLimits limits,
        Dictionary<string, Iso9660Entry> byPath,
        HashSet<string> visitedDirectories,
        ref int extentCount,
        ref long totalDirectoryBytes,
        int depth)
    {
        if (depth > limits.MaximumIsoDirectoryDepth)
        {
            throw new ToolkitException(
                "ISO_DIRECTORY_DEPTH",
                $"ISO directory depth exceeds configured limit {limits.MaximumIsoDirectoryDepth} at {directory.Path}.");
        }
        if (directory.Size > limits.MaximumIsoDirectoryBytes || directory.Size > int.MaxValue)
        {
            throw new ToolkitException(
                "ISO_DIRECTORY_TOO_LARGE",
                $"ISO directory {directory.Path} is {directory.Size} bytes; configured limit is {limits.MaximumIsoDirectoryBytes} bytes.");
        }
        totalDirectoryBytes = checked(totalDirectoryBytes + directory.Size);
        if (totalDirectoryBytes > limits.MaximumIsoTotalDirectoryBytes)
        {
            throw new ToolkitException(
                "ISO_DIRECTORY_TOTAL_LIMIT",
                $"ISO directory data exceeds configured total limit {limits.MaximumIsoTotalDirectoryBytes} bytes.");
        }

        string directoryKey = string.Join(
            ",",
            directory.Extents.Select(static extent => $"{extent.LogicalBlock:X8}:{extent.ByteLength:X8}"));
        if (!visitedDirectories.Add(directoryKey))
        {
            throw new ToolkitException(
                "ISO_DIRECTORY_CYCLE",
                $"ISO directory extent is referenced more than once: {directory.Path}.");
        }

        byte[] data = ReadEntryBytes(source, directory, blockSize, checked((int)directory.Size));
        var childDirectories = new List<Iso9660Entry>();
        PendingEntry? pending = null;
        int position = 0;
        while (position < data.Length)
        {
            int recordLength = data[position];
            if (recordLength == 0)
            {
                int next = checked((int)BinaryUtilities.Align(position + 1L, blockSize));
                if (next <= position)
                {
                    throw new ToolkitException("ISO_DIRECTORY_RECORD", "ISO directory parser made no progress.");
                }
                position = Math.Min(next, data.Length);
                continue;
            }

            int sectorRemaining = blockSize - (position % blockSize);
            if (recordLength > sectorRemaining || recordLength < 34 || recordLength > data.Length - position)
            {
                throw new ToolkitException(
                    "ISO_DIRECTORY_RECORD",
                    $"Invalid directory record at offset 0x{position:X} in {directory.Path}.");
            }

            int recordPosition = position;
            long physicalRecordOffset = MapDirectoryDataOffsetToSource(
                directory, blockSize, recordPosition, recordLength);
            ParsedDirectoryRecord record = ParseDirectoryRecord(
                data.AsSpan(position, recordLength),
                blockSize,
                source.Length,
                volumeSize,
                $"directory {directory.Path}");
            position += recordLength;

            if (record.SpecialIdentifier is 0 or 1)
            {
                if (pending is not null)
                {
                    throw new ToolkitException("ISO_MULTI_EXTENT", "Special directory entry interrupted a multi-extent file.");
                }
                continue;
            }

            string identifier = NormalizeIdentifier(record.RawIdentifier);
            ValidatePathComponent(identifier);
            string childPath = string.IsNullOrEmpty(parentPath)
                ? identifier
                : parentPath + "/" + identifier;
            if (childPath.Length > 4096)
            {
                throw new ToolkitException("ISO_PATH_LENGTH", $"ISO path exceeds 4096 characters: {childPath}");
            }

            if (pending is not null)
            {
                if (!string.Equals(pending.Path, childPath, StringComparison.OrdinalIgnoreCase)
                    || pending.IsDirectory != record.IsDirectory
                    || (pending.Flags & 0x7F) != (record.Flags & 0x7F))
                {
                    throw new ToolkitException(
                        "ISO_MULTI_EXTENT",
                        $"Multi-extent entry {pending.Path} is not followed by a compatible extent.");
                }
                pending.Extents.Add(new Iso9660Extent(record.LogicalBlock, record.DataLength));
                pending.DirectoryRecords.Add(CreateRecordLocation(record, physicalRecordOffset, recordLength));
                pending.Size = checked(pending.Size + record.DataLength);
                extentCount = checked(extentCount + 1);
                RequireExtentLimit(extentCount, limits);
                if (!record.MoreExtents)
                {
                    FinalizeEntry(pending, byPath, childDirectories, limits);
                    pending = null;
                }
                continue;
            }

            PendingEntry nextEntry = new(
                childPath,
                identifier,
                record.RawIdentifier,
                record.IsDirectory,
                (record.Flags & 0x01) != 0,
                record.Flags,
                record.DataLength,
                [new Iso9660Extent(record.LogicalBlock, record.DataLength)],
                [CreateRecordLocation(record, physicalRecordOffset, recordLength)]);
            extentCount = checked(extentCount + 1);
            RequireExtentLimit(extentCount, limits);
            if (record.MoreExtents)
            {
                pending = nextEntry;
            }
            else
            {
                FinalizeEntry(nextEntry, byPath, childDirectories, limits);
            }
        }

        if (pending is not null)
        {
            throw new ToolkitException("ISO_MULTI_EXTENT", $"Multi-extent entry {pending.Path} has no final extent.");
        }

        foreach (Iso9660Entry child in childDirectories)
        {
            ParseDirectoryRecursive(
                source,
                child,
                child.Path,
                blockSize,
                volumeSize,
                limits,
                byPath,
                visitedDirectories,
                ref extentCount,
                ref totalDirectoryBytes,
                checked(depth + 1));
        }
    }

    /// <summary>
    /// Creates record location while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="record">The record value.</param>
    /// <param name="physicalOffset">The physical offset value.</param>
    /// <param name="recordLength">The record length value.</param>
    /// <returns>The validated operation result.</returns>
    private static Iso9660DirectoryRecordLocation CreateRecordLocation(
        ParsedDirectoryRecord record,
        long physicalOffset,
        int recordLength)
        => new(
            physicalOffset,
            checked((byte)recordLength),
            record.ExtendedAttributeBlocks,
            record.RecordedExtent,
            record.DataLength,
            record.Flags);

    /// <summary>
    /// Maps a directory-relative record range into one physical source extent, rejecting cross-extent records.
    /// </summary>
    /// <param name="directory">The directory value.</param>
    /// <param name="blockSize">The block size value.</param>
    /// <param name="logicalOffset">The logical offset value.</param>
    /// <param name="byteLength">The byte length value.</param>
    /// <returns>The validated operation result.</returns>
    private static long MapDirectoryDataOffsetToSource(
        Iso9660Entry directory,
        int blockSize,
        int logicalOffset,
        int byteLength)
    {
        long cursor = 0;
        foreach (Iso9660Extent extent in directory.Extents)
        {
            long extentLength = extent.ByteLength;
            if (logicalOffset >= cursor
                && logicalOffset <= cursor + extentLength
                && byteLength <= cursor + extentLength - logicalOffset)
            {
                return checked(extent.ByteOffset(blockSize) + logicalOffset - cursor);
            }
            cursor = checked(cursor + extentLength);
        }

        throw new ToolkitException(
            "ISO_DIRECTORY_RECORD_SPAN",
            $"Directory record at logical offset 0x{logicalOffset:X} in {directory.Path} crosses or falls outside its extents.");
    }

    /// <summary>
    /// Finalizes entry while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="pending">The pending value.</param>
    /// <param name="byPath">The by path value.</param>
    /// <param name="childDirectories">The child directories value.</param>
    /// <param name="limits">Optional conservative safety limits; defaults are used when omitted.</param>
    private static void FinalizeEntry(
        PendingEntry pending,
        Dictionary<string, Iso9660Entry> byPath,
        List<Iso9660Entry> childDirectories,
        FileLimits limits)
    {
        if (byPath.Count >= limits.MaximumIsoEntries)
        {
            throw new ToolkitException(
                "ISO_ENTRY_LIMIT",
                $"ISO entry count exceeds configured limit {limits.MaximumIsoEntries}.");
        }
        Iso9660Entry entry = new()
        {
            Path = pending.Path,
            Identifier = pending.Identifier,
            RawIdentifier = pending.RawIdentifier,
            IsDirectory = pending.IsDirectory,
            IsHidden = pending.IsHidden,
            Flags = pending.Flags,
            Size = pending.Size,
            Extents = pending.Extents.ToArray(),
            DirectoryRecords = pending.DirectoryRecords.ToArray()
        };
        if (!byPath.TryAdd(entry.Path, entry))
        {
            throw new ToolkitException("ISO_DUPLICATE_PATH", $"ISO contains a duplicate path: {entry.Path}");
        }
        if (entry.IsDirectory)
        {
            childDirectories.Add(entry);
        }
    }

    /// <summary>
    /// Reads entry bytes while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="source">The source binary data or object.</param>
    /// <param name="entry">The validated archive or ISO entry to process.</param>
    /// <param name="blockSize">The block size value.</param>
    /// <param name="expectedLength">The expected length value.</param>
    /// <returns>The resulting binary or typed sequence.</returns>
    private static byte[] ReadEntryBytes(
        SeekableDataSource source,
        Iso9660Entry entry,
        int blockSize,
        int expectedLength)
    {
        byte[] result = new byte[expectedLength];
        int position = 0;
        foreach (Iso9660Extent extent in entry.Extents)
        {
            if (extent.ByteLength > result.Length - position)
            {
                throw new ToolkitException("ISO_EXTENT_SIZE", $"ISO extents exceed declared size for {entry.Path}.");
            }
            source.ReadExactly(
                extent.ByteOffset(blockSize),
                result.AsSpan(position, checked((int)extent.ByteLength)),
                $"ISO entry {entry.Path}");
            position = checked(position + (int)extent.ByteLength);
        }
        if (position != expectedLength)
        {
            throw new ToolkitException(
                "ISO_EXTENT_SIZE",
                $"ISO extents contain {position} bytes but {entry.Path} declares {expectedLength} bytes.");
        }
        return result;
    }

    /// <summary>
    /// Parses directory record while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="record">The record value.</param>
    /// <param name="blockSize">The block size value.</param>
    /// <param name="sourceLength">The source length value.</param>
    /// <param name="volumeSize">The volume size value.</param>
    /// <param name="context">A diagnostic label included in validation errors.</param>
    /// <returns>The validated operation result.</returns>
    /// <remarks>Malformed input or violated preconditions are rejected before a persistent output is committed.</remarks>
    private static ParsedDirectoryRecord ParseDirectoryRecord(
        ReadOnlySpan<byte> record,
        int blockSize,
        long sourceLength,
        long volumeSize,
        string context)
    {
        if (record.Length < 34 || record[0] != record.Length)
        {
            throw new ToolkitException("ISO_DIRECTORY_RECORD", $"Invalid ISO directory record for {context}.");
        }
        byte extendedAttributeBlocks = record[1];
        uint extent = ReadBothEndianUInt32(record, 2, $"{context} extent");
        uint dataLength = ReadBothEndianUInt32(record, 10, $"{context} data length");
        byte flags = record[25];
        if (record[26] != 0 || record[27] != 0)
        {
            throw new ToolkitException("ISO_INTERLEAVE", $"Interleaved ISO files are not supported: {context}.");
        }
        ushort volumeSequence = ReadBothEndianUInt16(record, 28, $"{context} volume sequence");
        if (volumeSequence != 1)
        {
            throw new ToolkitException(
                "ISO_MULTI_VOLUME",
                $"Unsupported volume sequence number {volumeSequence} in {context}.");
        }
        int identifierLength = record[32];
        if (identifierLength <= 0 || 33 + identifierLength > record.Length)
        {
            throw new ToolkitException("ISO_IDENTIFIER", $"Invalid ISO identifier length in {context}.");
        }

        ReadOnlySpan<byte> identifierBytes = record.Slice(33, identifierLength);
        int? special = null;
        string rawIdentifier;
        if (identifierLength == 1 && identifierBytes[0] is 0 or 1)
        {
            special = identifierBytes[0];
            rawIdentifier = special == 0 ? "." : "..";
        }
        else
        {
            if (ContainsUnsupportedIdentifierByte(identifierBytes))
            {
                throw new ToolkitException("ISO_IDENTIFIER", $"ISO identifier contains unsupported bytes in {context}.");
            }
            rawIdentifier = Encoding.ASCII.GetString(identifierBytes);
        }

        uint dataLogicalBlock;
        try
        {
            dataLogicalBlock = checked(extent + extendedAttributeBlocks);
        }
        catch (OverflowException ex)
        {
            throw new ToolkitException(
                "ISO_EXTENT_RANGE",
                $"ISO extended-attribute extent overflows for {context}.",
                ex);
        }
        long byteOffset = checked((long)dataLogicalBlock * blockSize);
        if (byteOffset > volumeSize || dataLength > volumeSize - byteOffset
            || byteOffset > sourceLength || dataLength > sourceLength - byteOffset)
        {
            throw new ToolkitException(
                "ISO_EXTENT_RANGE",
                $"ISO extent for {context} exceeds the declared volume or source length.");
        }

        return new ParsedDirectoryRecord(
            dataLogicalBlock,
            extent,
            extendedAttributeBlocks,
            dataLength,
            flags,
            (flags & 0x02) != 0,
            (flags & 0x80) != 0,
            special,
            rawIdentifier);
    }

    /// <summary>
    /// Reads both endian u int 16 while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="data">The binary data to process.</param>
    /// <param name="offset">The zero-based byte or element offset.</param>
    /// <param name="field">The field value.</param>
    /// <returns>The validated operation result.</returns>
    private static ushort ReadBothEndianUInt16(ReadOnlySpan<byte> data, int offset, string field)
    {
        if (offset < 0 || offset > data.Length - 4)
        {
            throw new ToolkitException("ISO_FIELD_RANGE", $"ISO {field} is outside its structure.");
        }
        ushort little = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(offset, 2));
        ushort big = System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(data.Slice(offset + 2, 2));
        if (little != big)
        {
            throw new ToolkitException("ISO_BOTH_ENDIAN", $"ISO {field} little- and big-endian values differ.");
        }
        return little;
    }

    /// <summary>
    /// Reads both endian u int 32 while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="data">The binary data to process.</param>
    /// <param name="offset">The zero-based byte or element offset.</param>
    /// <param name="field">The field value.</param>
    /// <returns>The validated operation result.</returns>
    private static uint ReadBothEndianUInt32(ReadOnlySpan<byte> data, int offset, string field)
    {
        if (offset < 0 || offset > data.Length - 8)
        {
            throw new ToolkitException("ISO_FIELD_RANGE", $"ISO {field} is outside its structure.");
        }
        uint little = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4));
        uint big = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(data.Slice(offset + 4, 4));
        if (little != big)
        {
            throw new ToolkitException("ISO_BOTH_ENDIAN", $"ISO {field} little- and big-endian values differ.");
        }
        return little;
    }

    /// <summary>
    /// Determines whether unsupported identifier byte.
    /// </summary>
    /// <param name="data">The binary data to process.</param>
    /// <returns><see langword="true"/> when the condition is satisfied; otherwise <see langword="false"/>.</returns>
    private static bool ContainsUnsupportedIdentifierByte(ReadOnlySpan<byte> data)
    {
        foreach (byte value in data)
        {
            if (value < 0x20 || value > 0x7E
                || value == (byte)'/' || value == (byte)'\\')
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Determines whether only ASCII digits.
    /// </summary>
    /// <param name="data">The binary data to process.</param>
    /// <returns><see langword="true"/> when the condition is satisfied; otherwise <see langword="false"/>.</returns>
    private static bool ContainsOnlyAsciiDigits(ReadOnlySpan<char> data)
    {
        if (data.IsEmpty)
        {
            return false;
        }
        foreach (char value in data)
        {
            if (value is < '0' or > '9')
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>
    /// Determines whether non ASCII.
    /// </summary>
    /// <param name="data">The binary data to process.</param>
    /// <returns><see langword="true"/> when the condition is satisfied; otherwise <see langword="false"/>.</returns>
    private static bool ContainsNonAscii(ReadOnlySpan<byte> data)
    {
        foreach (byte value in data)
        {
            if (value > 0x7F)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Decodes padded ASCII while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="data">The binary data to process.</param>
    /// <returns>The resulting text, path, identifier, or hexadecimal digest.</returns>
    private static string DecodePaddedAscii(ReadOnlySpan<byte> data)
    {
        if (ContainsNonAscii(data))
        {
            throw new ToolkitException("ISO_TEXT", "ISO primary volume descriptor contains non-ASCII text.");
        }
        return Encoding.ASCII.GetString(data).TrimEnd(' ', '\0');
    }

    /// <summary>
    /// Normalizes identifier while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="raw">The raw value.</param>
    /// <returns>The resulting text, path, identifier, or hexadecimal digest.</returns>
    private static string NormalizeIdentifier(string raw)
    {
        string value = raw;
        int semicolon = value.LastIndexOf(';');
        if (semicolon > 0
            && semicolon < value.Length - 1
            && ContainsOnlyAsciiDigits(value.AsSpan(semicolon + 1)))
        {
            value = value[..semicolon];
        }
        if (value.EndsWith('.'))
        {
            value = value[..^1];
        }
        return value;
    }

    /// <summary>
    /// Validates path component while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="component">The component value.</param>
    /// <remarks>Malformed input or violated preconditions are rejected before a persistent output is committed.</remarks>
    private static void ValidatePathComponent(string component)
    {
        if (string.IsNullOrWhiteSpace(component)
            || component is "." or ".."
            || component.Contains('/')
            || component.Contains('\\')
            || component.Contains('\0'))
        {
            throw new ToolkitException("ISO_PATH", $"Invalid ISO path component: {component}");
        }
    }

    /// <summary>
    /// Normalizes lookup path while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="path">The file-system path to process.</param>
    /// <returns>The resulting text, path, identifier, or hexadecimal digest.</returns>
    private static string NormalizeLookupPath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string normalized = path.Replace('\\', '/').Trim('/');
        string[] components = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (components.Length == 0)
        {
            throw new ToolkitException("ISO_PATH", "ISO path cannot refer to the root directory for this operation.");
        }
        for (int index = 0; index < components.Length; index++)
        {
            components[index] = NormalizeIdentifier(components[index]);
            ValidatePathComponent(components[index]);
        }
        return string.Join('/', components);
    }

    /// <summary>
    /// Requires extent limit while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="extentCount">The extent count value.</param>
    /// <param name="limits">Optional conservative safety limits; defaults are used when omitted.</param>
    private static void RequireExtentLimit(int extentCount, FileLimits limits)
    {
        if (extentCount > limits.MaximumIsoExtents)
        {
            throw new ToolkitException(
                "ISO_EXTENT_LIMIT",
                $"ISO extent count exceeds configured limit {limits.MaximumIsoExtents}.");
        }
    }

    /// <summary>
    /// Throws when disposed.
    /// </summary>
    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    /// <summary>
    /// Represents immutable parsed directory record data exchanged by the toolkit.
    /// </summary>
    /// <param name="LogicalBlock">The logical block value used by this model or operation.</param>
    /// <param name="RecordedExtent">The recorded extent value used by this model or operation.</param>
    /// <param name="ExtendedAttributeBlocks">The extended attribute blocks value used by this model or operation.</param>
    /// <param name="DataLength">The data length value used by this model or operation.</param>
    /// <param name="Flags">The encoded flags combining storage mode and value type.</param>
    /// <param name="IsDirectory">The is directory value used by this model or operation.</param>
    /// <param name="MoreExtents">The more extents value used by this model or operation.</param>
    /// <param name="SpecialIdentifier">The special identifier value used by this model or operation.</param>
    /// <param name="RawIdentifier">The raw identifier value used by this model or operation.</param>
    private sealed record ParsedDirectoryRecord(
        uint LogicalBlock,
        uint RecordedExtent,
        byte ExtendedAttributeBlocks,
        uint DataLength,
        byte Flags,
        bool IsDirectory,
        bool MoreExtents,
        int? SpecialIdentifier,
        string RawIdentifier);

    /// <summary>
    /// Represents the toolkit's pending entry model or service.
    /// </summary>
    /// <param name="path">The path value used by this model or operation.</param>
    /// <param name="identifier">The identifier value used by this model or operation.</param>
    /// <param name="rawIdentifier">The raw identifier value used by this model or operation.</param>
    /// <param name="isDirectory">The is directory value used by this model or operation.</param>
    /// <param name="isHidden">The is hidden value used by this model or operation.</param>
    /// <param name="flags">The flags value used by this model or operation.</param>
    /// <param name="size">The size value used by this model or operation.</param>
    /// <param name="extents">The extents value used by this model or operation.</param>
    /// <param name="directoryRecords">The directory records value used by this model or operation.</param>
    private sealed class PendingEntry(
        string path,
        string identifier,
        string rawIdentifier,
        bool isDirectory,
        bool isHidden,
        byte flags,
        long size,
        List<Iso9660Extent> extents,
        List<Iso9660DirectoryRecordLocation> directoryRecords)
    {
        /// <summary>The path value used by this model or operation.</summary>
        public string Path { get; } = path;
        /// <summary>The identifier value used by this model or operation.</summary>
        public string Identifier { get; } = identifier;
        /// <summary>The raw identifier value used by this model or operation.</summary>
        public string RawIdentifier { get; } = rawIdentifier;
        /// <summary>The is directory value used by this model or operation.</summary>
        public bool IsDirectory { get; } = isDirectory;
        /// <summary>The is hidden value used by this model or operation.</summary>
        public bool IsHidden { get; } = isHidden;
        /// <summary>The encoded flags combining storage mode and value type.</summary>
        public byte Flags { get; } = flags;
        /// <summary>The size value used by this model or operation.</summary>
        public long Size { get; set; } = size;
        /// <summary>The extents value used by this model or operation.</summary>
        public List<Iso9660Extent> Extents { get; } = extents;
        /// <summary>The directory records value used by this model or operation.</summary>
        public List<Iso9660DirectoryRecordLocation> DirectoryRecords { get; } = directoryRecords;
    }
}
