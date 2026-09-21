namespace LuckyStarPspToolkit.Formats.Iso;

/// <summary>
/// Represents immutable ISO 9660 extent data exchanged by the toolkit.
/// </summary>
/// <param name="LogicalBlock">The logical block value used by this model or operation.</param>
/// <param name="ByteLength">The byte length value used by this model or operation.</param>
public sealed record Iso9660Extent(uint LogicalBlock, uint ByteLength)
{
    /// <summary>
    /// Converts an ISO logical block number to a checked absolute byte offset.
    /// </summary>
    /// <param name="logicalBlockSize">The logical block size value.</param>
    /// <returns>The validated operation result.</returns>
    public long ByteOffset(int logicalBlockSize)
        => checked((long)LogicalBlock * logicalBlockSize);
}

/// <summary>
/// Represents immutable ISO 9660 directory record location data exchanged by the toolkit.
/// </summary>
/// <param name="PhysicalOffset">The physical offset value used by this model or operation.</param>
/// <param name="RecordLength">The record length value used by this model or operation.</param>
/// <param name="ExtendedAttributeBlocks">The extended attribute blocks value used by this model or operation.</param>
/// <param name="RecordedExtent">The recorded extent value used by this model or operation.</param>
/// <param name="DataLength">The data length value used by this model or operation.</param>
/// <param name="Flags">The encoded flags combining storage mode and value type.</param>
internal sealed record Iso9660DirectoryRecordLocation(
    long PhysicalOffset,
    byte RecordLength,
    byte ExtendedAttributeBlocks,
    uint RecordedExtent,
    uint DataLength,
    byte Flags);

/// <summary>
/// Represents the toolkit's ISO 9660 entry model or service.
/// </summary>
public sealed class Iso9660Entry
{
    /// <summary>The path value used by this model or operation.</summary>
    public required string Path { get; init; }
    /// <summary>The identifier value used by this model or operation.</summary>
    public required string Identifier { get; init; }
    /// <summary>The raw identifier value used by this model or operation.</summary>
    public required string RawIdentifier { get; init; }
    /// <summary>The is directory value used by this model or operation.</summary>
    public required bool IsDirectory { get; init; }
    /// <summary>The is hidden value used by this model or operation.</summary>
    public required bool IsHidden { get; init; }
    /// <summary>The encoded flags combining storage mode and value type.</summary>
    public required byte Flags { get; init; }
    /// <summary>The size value used by this model or operation.</summary>
    public required long Size { get; init; }
    /// <summary>The extents value used by this model or operation.</summary>
    public required IReadOnlyList<Iso9660Extent> Extents { get; init; }

    /// <summary>The directory records value used by this model or operation.</summary>
    internal IReadOnlyList<Iso9660DirectoryRecordLocation> DirectoryRecords { get; init; } = [];

    /// <summary>The name value used by this model or operation.</summary>
    public string Name
    {
        get
        {
            int separator = Path.LastIndexOf('/');
            return separator < 0 ? Path : Path[(separator + 1)..];
        }
    }

    /// <summary>The is multi extent value used by this model or operation.</summary>
    public bool IsMultiExtent => Extents.Count > 1;
}

/// <summary>
/// Represents the toolkit's ISO 9660 summary model or service.
/// </summary>
public sealed class Iso9660Summary
{
    /// <summary>The schema value used by this model or operation.</summary>
    public string Schema { get; init; } = "lucky-star-psp.iso9660-summary.v1";
    /// <summary>The source value used by this model or operation.</summary>
    public required string Source { get; init; }
    /// <summary>The source size value used by this model or operation.</summary>
    public required long SourceSize { get; init; }
    /// <summary>The volume identifier value used by this model or operation.</summary>
    public required string VolumeIdentifier { get; init; }
    /// <summary>The volume block count value used by this model or operation.</summary>
    public required uint VolumeBlockCount { get; init; }
    /// <summary>The logical block size value used by this model or operation.</summary>
    public required int LogicalBlockSize { get; init; }
    /// <summary>The entry count value used by this model or operation.</summary>
    public required int EntryCount { get; init; }
    /// <summary>The entries value used by this model or operation.</summary>
    public required IReadOnlyList<Iso9660Entry> Entries { get; init; }
}
