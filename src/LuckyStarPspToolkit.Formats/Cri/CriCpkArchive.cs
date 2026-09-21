using System.Buffers.Binary;
using LuckyStarPspToolkit.Formats.Common;

namespace LuckyStarPspToolkit.Formats.Cri;

/// <summary>
/// Represents the toolkit's CRI CPK entry model or service.
/// </summary>
public sealed class CriCpkEntry
{
    /// <summary>The resource identifier preserved across extraction and rebuilding.</summary>
    public required ushort Id { get; init; }
    /// <summary>The packed payload bytes; callers must preserve or explicitly replace the contents.</summary>
    public required byte[] PackedData { get; set; }
    /// <summary>The expected decoded payload length, in bytes.</summary>
    public required int ExtractSize { get; set; }
    /// <summary>Whether the packed payload starts with a CRILAYLA frame signature.</summary>
    public bool IsCrilayla => CrilaylaCodec.IsFrame(PackedData);

    /// <summary>
    /// Gets extracted data while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="limits">Optional conservative safety limits; defaults are used when omitted.</param>
    /// <returns>The resulting binary or typed sequence.</returns>
    public byte[] GetExtractedData(FileLimits? limits = null)
        => IsCrilayla ? CrilaylaCodec.Decompress(PackedData, limits) : PackedData.ToArray();

    /// <summary>
    /// Creates a deep copy whose mutable buffers are independent of the source instance.
    /// </summary>
    /// <returns>The validated operation result.</returns>
    public CriCpkEntry Clone() => new()
    {
        Id = Id,
        PackedData = PackedData.ToArray(),
        ExtractSize = ExtractSize
    };
}

/// <summary>
/// Represents immutable CRI CPK build result data exchanged by the toolkit.
/// </summary>
/// <param name="Data">The byte payload associated with this record.</param>
/// <param name="OldPackedSizes">The old packed sizes value used by this model or operation.</param>
/// <param name="NewPackedSizes">The new packed sizes value used by this model or operation.</param>
public sealed record CriCpkBuildResult(byte[] Data, IReadOnlyDictionary<ushort, int> OldPackedSizes, IReadOnlyDictionary<ushort, int> NewPackedSizes);

/// <summary>
/// Represents the toolkit's CRI CPK archive model or service.
/// </summary>
public sealed class CriCpkArchive
{
    /// <summary>The fixed data lmaximum value used by this format or revision.</summary>
    private const int DataLMaximum = ushort.MaxValue;
    /// <summary>Stores the original bytes state owned by this instance or type.</summary>
    private readonly byte[] _originalBytes;
    /// <summary>Stores the header packet state owned by this instance or type.</summary>
    private readonly CriUtfPacket _headerPacket;
    /// <summary>Stores the itoc packet state owned by this instance or type.</summary>
    private readonly CriUtfPacket _itocPacket;
    /// <summary>Stores the data ltemplate state owned by this instance or type.</summary>
    private readonly CriUtfTable _dataLTemplate;
    /// <summary>Stores the data htemplate state owned by this instance or type.</summary>
    private readonly CriUtfTable _dataHTemplate;
    /// <summary>Stores the original low rows state owned by this instance or type.</summary>
    private readonly Dictionary<ushort, CriUtfRow> _originalLowRows;
    /// <summary>Stores the original high rows state owned by this instance or type.</summary>
    private readonly Dictionary<ushort, CriUtfRow> _originalHighRows;
    /// <summary>Stores the itoc size bias state owned by this instance or type.</summary>
    private readonly long _itocSizeBias;
    /// <summary>Stores the entries state owned by this instance or type.</summary>
    private readonly SortedDictionary<ushort, CriCpkEntry> _entries;
    /// <summary>Stores the original packed sizes state owned by this instance or type.</summary>
    private readonly SortedDictionary<ushort, int> _originalPackedSizes;

    /// <summary>
    /// Initializes a new instance with validated constructor state.
    /// </summary>
    /// <param name="originalBytes">The original bytes value.</param>
    /// <param name="headerPacket">The header packet value.</param>
    /// <param name="itocPacket">The itoc packet value.</param>
    /// <param name="dataL">The data l value.</param>
    /// <param name="dataH">The data h value.</param>
    /// <param name="entries">The entries value.</param>
    /// <param name="originalLowRows">The original low rows value.</param>
    /// <param name="originalHighRows">The original high rows value.</param>
    /// <param name="itocSizeBias">The itoc size bias value.</param>
    private CriCpkArchive(
        byte[] originalBytes,
        CriUtfPacket headerPacket,
        CriUtfPacket itocPacket,
        CriUtfTable dataL,
        CriUtfTable dataH,
        SortedDictionary<ushort, CriCpkEntry> entries,
        Dictionary<ushort, CriUtfRow> originalLowRows,
        Dictionary<ushort, CriUtfRow> originalHighRows,
        long itocSizeBias)
    {
        _originalBytes = originalBytes;
        _headerPacket = headerPacket;
        _itocPacket = itocPacket;
        _dataLTemplate = dataL;
        _dataHTemplate = dataH;
        _entries = entries;
        _originalLowRows = originalLowRows;
        _originalHighRows = originalHighRows;
        _itocSizeBias = itocSizeBias;
        _originalPackedSizes = new SortedDictionary<ushort, int>(entries.ToDictionary(static pair => pair.Key, static pair => pair.Value.PackedData.Length));
    }

    /// <summary>The entries value used by this model or operation.</summary>
    public IReadOnlyDictionary<ushort, CriCpkEntry> Entries => _entries;
    /// <summary>The alignment value used by this model or operation.</summary>
    public int Alignment => Guard.CheckedInt((long)_headerPacket.Table.GetUnsigned(0, "Align"), "CPK_ALIGN", "CPK alignment");
    /// <summary>The content offset value used by this model or operation.</summary>
    public long ContentOffset => checked((long)_headerPacket.Table.GetUnsigned(0, "ContentOffset"));
    /// <summary>The itoc offset value used by this model or operation.</summary>
    public long ItocOffset => checked((long)_headerPacket.Table.GetUnsigned(0, "ItocOffset"));

    /// <summary>
    /// Parses validated input into the current binary-format model.
    /// </summary>
    /// <param name="data">The binary data to process.</param>
    /// <param name="limits">Optional conservative safety limits; defaults are used when omitted.</param>
    /// <returns>The validated operation result.</returns>
    /// <remarks>Malformed input or violated preconditions are rejected before a persistent output is committed.</remarks>
    public static CriCpkArchive Parse(ReadOnlySpan<byte> data, FileLimits? limits = null)
    {
        limits ??= FileLimits.Default;
        if (data.Length < 32 || !data.StartsWith("CPK "u8))
        {
            throw new ToolkitException("CPK_MAGIC", "Input is not a CRI CPK archive.");
        }
        byte[] original = data.ToArray();
        CriUtfPacket headerPacket = CriUtfCodec.ParsePacket(data[4..], limits);
        CriUtfTable header = headerPacket.Table;
        if (header.Rows.Count != 1)
        {
            throw new ToolkitException("CPK_HEADER_ROWS", $"CPK header must have one row; found {header.Rows.Count}.");
        }

        long itocOffset64 = checked((long)header.GetUnsigned(0, "ItocOffset"));
        int itocOffset = Guard.CheckedInt(itocOffset64, "CPK_ITOC_OFFSET", "ITOC offset");
        if (itocOffset < 0 || itocOffset > data.Length - 16 || !data.Slice(itocOffset).StartsWith("ITOC"u8))
        {
            throw new ToolkitException("CPK_ITOC", $"ITOC chunk is missing at offset {itocOffset}.");
        }
        CriUtfPacket itocPacket = CriUtfCodec.ParsePacket(data[(itocOffset + 4)..], limits);
        CriUtfTable itoc = itocPacket.Table;
        if (itoc.Rows.Count != 1)
        {
            throw new ToolkitException("CPK_ITOC_ROWS", $"ITOC must have one row; found {itoc.Rows.Count}.");
        }

        byte[] dataLBytes = itoc.GetData(0, "DataL");
        byte[] dataHBytes = itoc.GetData(0, "DataH");
        if (dataLBytes.Length < 32 || dataHBytes.Length < 32)
        {
            throw new ToolkitException("CPK_ITOC_TABLE", "CPK ITOC DataL/DataH table is missing or too short.");
        }
        CriUtfTable dataL = CriUtfCodec.ParseTable(dataLBytes, limits);
        CriUtfTable dataH = CriUtfCodec.ParseTable(dataHBytes, limits);
        int declaredL = Guard.CheckedInt((long)itoc.GetUnsigned(0, "FilesL"), "CPK_FILE_COUNT", "FilesL");
        int declaredH = Guard.CheckedInt((long)itoc.GetUnsigned(0, "FilesH"), "CPK_FILE_COUNT", "FilesH");
        if (declaredL != dataL.Rows.Count || declaredH != dataH.Rows.Count)
        {
            throw new ToolkitException("CPK_FILE_COUNT", $"ITOC count mismatch: FilesL={declaredL}/{dataL.Rows.Count}, FilesH={declaredH}/{dataH.Rows.Count}.");
        }
        if (declaredL + declaredH > limits.MaximumCpkEntries)
        {
            throw new ToolkitException("CPK_ENTRY_LIMIT", $"CPK entry count {declaredL + declaredH} exceeds limit {limits.MaximumCpkEntries}.");
        }

        Dictionary<ushort, EntryDescriptor> descriptors = new();
        Dictionary<ushort, CriUtfRow> lowRows = ReadDescriptors(dataL, true, descriptors);
        Dictionary<ushort, CriUtfRow> highRows = ReadDescriptors(dataH, false, descriptors);
        if (descriptors.Count != declaredL + declaredH)
        {
            throw new ToolkitException("CPK_DUPLICATE_ID", "CPK contains duplicate file IDs across DataL/DataH.");
        }
        ValidateContiguousIds(descriptors.Keys);

        long contentOffset64 = checked((long)header.GetUnsigned(0, "ContentOffset"));
        int contentOffset = Guard.CheckedInt(contentOffset64, "CPK_CONTENT_OFFSET", "CPK content offset");
        int alignment = Guard.CheckedInt((long)header.GetUnsigned(0, "Align"), "CPK_ALIGN", "CPK alignment");
        if (alignment <= 0 || alignment > 1024 * 1024 || (alignment & (alignment - 1)) != 0)
        {
            throw new ToolkitException("CPK_ALIGN", $"Unsupported CPK alignment {alignment}; expected a power of two up to 1 MiB.");
        }
        int itocActualEnd = checked(itocOffset + 4 + itocPacket.OriginalPacketLength);
        if (contentOffset < itocActualEnd || contentOffset > data.Length)
        {
            throw new ToolkitException("CPK_CONTENT_OFFSET", $"CPK content offset {contentOffset} is invalid; ITOC ends at {itocActualEnd}.");
        }

        SortedDictionary<ushort, CriCpkEntry> entries = new();
        int position = contentOffset;
        foreach ((ushort id, EntryDescriptor descriptor) in descriptors.OrderBy(static pair => pair.Key))
        {
            if (descriptor.PackedSize < 0 || position > data.Length - descriptor.PackedSize)
            {
                throw new ToolkitException("CPK_CONTENT_RANGE", $"CPK entry {id} exceeds archive bounds at offset {position}.");
            }
            byte[] packed = data.Slice(position, descriptor.PackedSize).ToArray();
            bool compressed = CrilaylaCodec.IsFrame(packed);
            if (compressed)
            {
                CrilaylaInfo info = CrilaylaCodec.Inspect(packed, limits);
                if (info.ExtractedSize != descriptor.ExtractSize)
                {
                    throw new ToolkitException("CPK_EXTRACT_SIZE", $"CPK entry {id} declares extract size {descriptor.ExtractSize}, CRILAYLA declares {info.ExtractedSize}.");
                }
            }
            else if (descriptor.ExtractSize != descriptor.PackedSize)
            {
                throw new ToolkitException("CPK_COMPRESSION", $"CPK entry {id} has differing packed/extract sizes without recognized CRILAYLA compression.");
            }
            entries.Add(id, new CriCpkEntry { Id = id, PackedData = packed, ExtractSize = descriptor.ExtractSize });
            position = checked((int)BinaryUtilities.Align((long)position + descriptor.PackedSize, alignment));
        }

        ulong headerItocSize = header.GetUnsigned(0, "ItocSize");
        long actualItocChunk = checked(4L + itocPacket.OriginalPacketLength);
        long itocSizeBias = checked((long)headerItocSize - actualItocChunk);
        if (Math.Abs(itocSizeBias) > 0x1000)
        {
            throw new ToolkitException("CPK_ITOC_SIZE", $"Header ITOC size differs from actual chunk by unexpected bias {itocSizeBias}.");
        }

        return new CriCpkArchive(original, headerPacket, itocPacket, dataL, dataH, entries, lowRows, highRows, itocSizeBias);
    }

    /// <summary>
    /// Gets entry while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="id">The numeric resource identifier.</param>
    /// <returns>The validated operation result.</returns>
    public CriCpkEntry GetEntry(ushort id)
    {
        if (!_entries.TryGetValue(id, out CriCpkEntry? entry))
        {
            throw new ToolkitException("CPK_ENTRY_NOT_FOUND", $"CPK entry {id} was not found.");
        }
        return entry;
    }

    /// <summary>
    /// Replaces entry while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="id">The numeric resource identifier.</param>
    /// <param name="uncompressedData">The uncompressed data value.</param>
    public void ReplaceEntry(ushort id, ReadOnlySpan<byte> uncompressedData)
    {
        if (!_entries.TryGetValue(id, out CriCpkEntry? entry))
        {
            throw new ToolkitException("CPK_ENTRY_NOT_FOUND", $"CPK entry {id} was not found.");
        }
        if (uncompressedData.Length == 0)
        {
            throw new ToolkitException("CPK_EMPTY_REPLACEMENT", $"Refusing to replace entry {id} with an empty file.");
        }
        entry.PackedData = uncompressedData.ToArray();
        entry.ExtractSize = uncompressedData.Length;
    }

    /// <summary>
    /// Serializes the current validated model into its binary representation.
    /// </summary>
    /// <param name="limits">Optional conservative safety limits; defaults are used when omitted.</param>
    /// <returns>The validated operation result.</returns>
    /// <remarks>Malformed input or violated preconditions are rejected before a persistent output is committed.</remarks>
    public CriCpkBuildResult Build(FileLimits? limits = null)
    {
        limits ??= FileLimits.Default;
        ValidateContiguousIds(_entries.Keys);
        var low = new List<CriCpkEntry>(_entries.Count);
        var high = new List<CriCpkEntry>();
        long packedSumSigned = 0;
        long extractSumSigned = 0;
        foreach (CriCpkEntry entry in _entries.Values)
        {
            packedSumSigned = checked(packedSumSigned + entry.PackedData.Length);
            extractSumSigned = checked(extractSumSigned + entry.ExtractSize);
            if (entry.PackedData.Length <= DataLMaximum && entry.ExtractSize <= DataLMaximum)
            {
                low.Add(entry);
            }
            else
            {
                high.Add(entry);
            }
        }
        CriUtfTable dataL = BuildDataTable(_dataLTemplate, low, _originalLowRows);
        CriUtfTable dataH = BuildDataTable(_dataHTemplate, high, _originalHighRows);
        byte[] dataLBytes = CriUtfCodec.BuildTable(dataL);
        byte[] dataHBytes = CriUtfCodec.BuildTable(dataH);

        CriUtfTable itoc = _itocPacket.Table.Clone();
        itoc.SetUnsigned(0, "FilesL", checked((ulong)low.Count));
        itoc.SetUnsigned(0, "FilesH", checked((ulong)high.Count));
        itoc.SetData(0, "DataL", dataLBytes);
        itoc.SetData(0, "DataH", dataHBytes);
        CriUtfPacket newItocPacketModel = _itocPacket with { Table = itoc };
        byte[] itocPacketBytes = CriUtfCodec.BuildPacket(newItocPacketModel);
        int itocChunkLength = checked(4 + itocPacketBytes.Length);

        CriUtfTable header = _headerPacket.Table.Clone();
        long itocOffset = checked((long)header.GetUnsigned(0, "ItocOffset"));
        int alignment = Guard.CheckedInt((long)header.GetUnsigned(0, "Align"), "CPK_ALIGN", "CPK alignment");
        long newItocSizeSigned = checked(itocChunkLength + _itocSizeBias);
        if (newItocSizeSigned < 0)
        {
            throw new ToolkitException("CPK_ITOC_SIZE", "Calculated ITOC size is negative.");
        }
        ulong newItocSize = checked((ulong)newItocSizeSigned);
        long contentOffset64 = BinaryUtilities.Align(checked(itocOffset + (long)newItocSize), alignment);
        if (contentOffset64 < itocOffset + itocChunkLength)
        {
            contentOffset64 = BinaryUtilities.Align(itocOffset + itocChunkLength, alignment);
        }
        ulong packedSum = checked((ulong)packedSumSigned);
        ulong extractSum = checked((ulong)extractSumSigned);
        header.SetUnsigned(0, "ItocSize", newItocSize);
        header.SetUnsigned(0, "ContentOffset", checked((ulong)contentOffset64));
        SetIfPresent(header, "ContentSize", packedSum);
        SetIfPresent(header, "EnabledPackedSize", packedSum);
        SetIfPresent(header, "EnabledDataSize", extractSum);
        SetIfPresent(header, "Files", checked((ulong)_entries.Count));

        byte[] headerPacketBytes = CriUtfCodec.BuildPacket(_headerPacket with { Table = header });
        int headerChunkLength = checked(4 + headerPacketBytes.Length);
        int itocOffsetInt = Guard.CheckedInt(itocOffset, "CPK_ITOC_OFFSET", "ITOC offset");
        int contentOffset = Guard.CheckedInt(contentOffset64, "CPK_CONTENT_OFFSET", "content offset");
        if (headerChunkLength > itocOffsetInt)
        {
            throw new ToolkitException("CPK_HEADER_OVERLAP", $"Rebuilt CPK header ({headerChunkLength} bytes) overlaps ITOC at {itocOffsetInt}.");
        }
        if (itocOffsetInt + itocChunkLength > contentOffset)
        {
            throw new ToolkitException("CPK_ITOC_OVERLAP", "Rebuilt ITOC overlaps CPK content.");
        }

        long outputLength64 = contentOffset;
        int index = 0;
        foreach (CriCpkEntry entry in _entries.Values)
        {
            outputLength64 = checked(outputLength64 + entry.PackedData.Length);
            index++;
            if (index != _entries.Count)
            {
                outputLength64 = BinaryUtilities.Align(outputLength64, alignment);
            }
        }
        int outputLength = Guard.CheckedInt(outputLength64, "CPK_OUTPUT_SIZE", "rebuilt CPK size");
        if (outputLength > limits.MaximumInputBytes)
        {
            throw new ToolkitException("CPK_OUTPUT_LIMIT", $"Rebuilt CPK size {outputLength} exceeds configured limit.");
        }

        byte[] output = new byte[outputLength];
        int preservedPrefix = (int)Math.Min(Math.Min((long)_originalBytes.Length, ContentOffset), contentOffset);
        Array.Copy(_originalBytes, output, preservedPrefix);
        "CPK "u8.CopyTo(output);
        headerPacketBytes.CopyTo(output.AsSpan(4));
        "ITOC"u8.CopyTo(output.AsSpan(itocOffsetInt));
        itocPacketBytes.CopyTo(output.AsSpan(itocOffsetInt + 4));
        int position = contentOffset;
        foreach (CriCpkEntry entry in _entries.Values)
        {
            entry.PackedData.CopyTo(output.AsSpan(position));
            position = checked((int)BinaryUtilities.Align((long)position + entry.PackedData.Length, alignment));
        }

        CriCpkArchive verification = Parse(output, limits);
        if (verification.Entries.Count != _entries.Count)
        {
            throw new ToolkitException("CPK_VERIFY_COUNT", "Rebuilt CPK entry count verification failed.");
        }
        foreach ((ushort id, CriCpkEntry expected) in _entries)
        {
            CriCpkEntry actual = verification.GetEntry(id);
            if (actual.ExtractSize != expected.ExtractSize || !actual.PackedData.AsSpan().SequenceEqual(expected.PackedData))
            {
                throw new ToolkitException("CPK_VERIFY_ENTRY", $"Rebuilt CPK entry {id} failed byte-for-byte verification.");
            }
        }

        return new CriCpkBuildResult(
            output,
            new SortedDictionary<ushort, int>(_originalPackedSizes),
            new SortedDictionary<ushort, int>(_entries.ToDictionary(static pair => pair.Key, static pair => pair.Value.PackedData.Length)));
    }

    /// <summary>
    /// Reads descriptors while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="table">The table value.</param>
    /// <param name="low">The low value.</param>
    /// <param name="output">The destination stream, buffer, or model.</param>
    /// <returns>The validated operation result.</returns>
    private static Dictionary<ushort, CriUtfRow> ReadDescriptors(CriUtfTable table, bool low, Dictionary<ushort, EntryDescriptor> output)
    {
        Dictionary<ushort, CriUtfRow> rows = new();
        for (int i = 0; i < table.Rows.Count; i++)
        {
            ushort id = Guard.CheckedUInt16((long)table.GetUnsigned(i, "ID"), "CPK_ID", "CPK file ID");
            int packed = Guard.CheckedInt((long)table.GetUnsigned(i, "FileSize"), "CPK_FILE_SIZE", $"CPK entry {id} packed size");
            int extracted = Guard.CheckedInt((long)table.GetUnsigned(i, "ExtractSize"), "CPK_EXTRACT_SIZE", $"CPK entry {id} extract size");
            if (packed <= 0 || extracted <= 0)
            {
                throw new ToolkitException("CPK_FILE_SIZE", $"CPK entry {id} has non-positive sizes {packed}/{extracted}.");
            }
            if (low && (packed > ushort.MaxValue || extracted > ushort.MaxValue))
            {
                throw new ToolkitException("CPK_DATAL_RANGE", $"DataL entry {id} exceeds UInt16 size.");
            }
            if (!output.TryAdd(id, new EntryDescriptor(packed, extracted)))
            {
                throw new ToolkitException("CPK_DUPLICATE_ID", $"Duplicate CPK entry ID {id}.");
            }
            rows.Add(id, table.Rows[i].Clone());
        }
        return rows;
    }

    /// <summary>
    /// Builds data table while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="template">The template value.</param>
    /// <param name="entries">The entries value.</param>
    /// <param name="originalRows">The original rows value.</param>
    /// <returns>The validated operation result.</returns>
    /// <remarks>Malformed input or violated preconditions are rejected before a persistent output is committed.</remarks>
    private static CriUtfTable BuildDataTable(CriUtfTable template, IReadOnlyList<CriCpkEntry> entries, IReadOnlyDictionary<ushort, CriUtfRow> originalRows)
    {
        CriUtfTable table = template.Clone();
        table.Rows.Clear();
        foreach (CriCpkEntry entry in entries.OrderBy(static item => item.Id))
        {
            CriUtfRow row = originalRows.TryGetValue(entry.Id, out CriUtfRow? original)
                ? original.Clone()
                : table.CreateEmptyRow();
            table.Rows.Add(row);
            int rowIndex = table.Rows.Count - 1;
            table.SetUnsigned(rowIndex, "ID", entry.Id);
            table.SetUnsigned(rowIndex, "FileSize", checked((ulong)entry.PackedData.Length));
            table.SetUnsigned(rowIndex, "ExtractSize", checked((ulong)entry.ExtractSize));
        }
        return table;
    }

    /// <summary>
    /// Sets if present while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="table">The table value.</param>
    /// <param name="column">The column value.</param>
    /// <param name="value">The value to process.</param>
    private static void SetIfPresent(CriUtfTable table, string column, ulong value)
    {
        if (table.TryGetColumnIndex(column, out _))
        {
            table.SetUnsigned(0, column, value);
        }
    }

    /// <summary>
    /// Validates contiguous ids while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="ids">The ids value.</param>
    /// <remarks>Malformed input or violated preconditions are rejected before a persistent output is committed.</remarks>
    private static void ValidateContiguousIds(IEnumerable<ushort> ids)
    {
        ushort expected = 0;
        foreach (ushort id in ids.Order())
        {
            if (id != expected)
            {
                throw new ToolkitException("CPK_NONCONTIGUOUS_IDS", $"CPK file IDs must be contiguous from 0; expected {expected}, found {id}.");
            }
            if (expected == ushort.MaxValue)
            {
                break;
            }
            expected++;
        }
    }

    /// <summary>
    /// Represents immutable entry descriptor data exchanged by the toolkit.
    /// </summary>
    /// <param name="PackedSize">The packed size value used by this model or operation.</param>
    /// <param name="ExtractSize">The expected decoded payload length, in bytes.</param>
    private sealed record EntryDescriptor(int PackedSize, int ExtractSize);
}
