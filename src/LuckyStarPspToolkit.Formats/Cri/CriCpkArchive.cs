using System.Buffers.Binary;
using LuckyStarPspToolkit.Formats.Common;

namespace LuckyStarPspToolkit.Formats.Cri;

/// <summary>A validated byte range in a CPK; it never owns or exposes mutable payload bytes.</summary>
/// <param name="Id">Contiguous file identifier from the ITOC.</param>
/// <param name="Offset">Absolute byte offset in the archive.</param>
/// <param name="PackedSize">Length of the stored file bytes, excluding alignment padding.</param>
/// <param name="ExtractSize">Expected length after optional CRILAYLA decoding.</param>
/// <param name="IsCrilayla">Whether the stored frame has a validated CRILAYLA header.</param>
public sealed record CriCpkFileInfo(ushort Id, int Offset, int PackedSize, int ExtractSize, bool IsCrilayla);

/// <summary>Metadata-only inspection of an ITOC CPK; payload ownership stays with the caller.</summary>
/// <param name="Alignment">Power-of-two file alignment.</param>
/// <param name="ContentOffset">First content byte offset.</param>
/// <param name="ItocOffset">Absolute ITOC chunk offset.</param>
/// <param name="Entries">Read-only list of all validated payload ranges, in ID order.</param>
public sealed record CriCpkInspection(int Alignment, int ContentOffset, int ItocOffset, IReadOnlyList<CriCpkFileInfo> Entries);

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
    {
        limits ??= FileLimits.Default;
        if (PackedData is null || PackedData.Length == 0 || PackedData.Length > limits.MaximumInputBytes)
            throw new ToolkitException("CPK_FILE_SIZE", "Packed file violates the configured size budget.");
        int actualSize = IsCrilayla ? CrilaylaCodec.Inspect(PackedData, limits).ExtractedSize : PackedData.Length;
        if (ExtractSize != actualSize)
            throw new ToolkitException("CPK_EXTRACT_SIZE", "Extract size differs from the actual payload size.");
        return IsCrilayla ? CrilaylaCodec.Decompress(PackedData, limits) : PackedData.ToArray();
    }

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
/// Owns an editable snapshot of an ITOC-only CPK and rebuilds replacements with bounded validation.
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

    /// <summary>Validates and lists files without copying archive or payload buffers.</summary>
    /// <param name="data">Borrowed CPK bytes, which are not retained in the result.</param>
    /// <param name="limits">Optional input and metadata allocation budgets.</param>
    /// <returns>Immutable descriptors suitable for listing and range hashing.</returns>
    /// <exception cref="ToolkitException">The archive layout or compression headers are invalid.</exception>
    public static CriCpkInspection Inspect(ReadOnlySpan<byte> data, FileLimits? limits = null)
    {
        CriCpkLayout layout = CriCpkLayout.Parse(data, limits);
        return new CriCpkInspection(layout.Alignment, layout.ContentOffset, layout.ItocOffset, Array.AsReadOnly(layout.Slices));
    }

    /// <summary>Validates the archive and copies or decodes exactly one file without materializing other payloads.</summary>
    /// <param name="data">Borrowed source archive bytes.</param>
    /// <param name="id">Contiguous ITOC file identifier.</param>
    /// <param name="packed">Return packed frame bytes instead of decoding CRILAYLA when true.</param>
    /// <param name="limits">Optional input, metadata and decompression limits.</param>
    /// <returns>New owned bytes of the selected file.</returns>
    /// <exception cref="ToolkitException">The ID, archive, frame, or a configured budget is invalid.</exception>
    public static byte[] Extract(ReadOnlySpan<byte> data, ushort id, bool packed = false, FileLimits? limits = null)
    {
        CriCpkLayout layout = CriCpkLayout.Parse(data, limits);
        if (id >= layout.Slices.Length)
            throw new ToolkitException("CPK_ENTRY_NOT_FOUND", $"CPK entry {id} was not found.");
        CriCpkFileInfo info = layout.Slices[id];
        ReadOnlySpan<byte> bytes = data.Slice(info.Offset, info.PackedSize);
        return info.IsCrilayla && !packed ? CrilaylaCodec.Decompress(bytes, limits) : bytes.ToArray();
    }

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
        CriCpkLayout layout = CriCpkLayout.Parse(data, limits);
        // Materialize payloads only after every descriptor/header has passed validation.
        byte[] original = data.ToArray();
        SortedDictionary<ushort, CriCpkEntry> entries = new();
        foreach (CriCpkFileInfo slice in layout.Slices)
        {
            entries.Add(slice.Id, new CriCpkEntry
            {
                Id = slice.Id,
                PackedData = data.Slice(slice.Offset, slice.PackedSize).ToArray(),
                ExtractSize = slice.ExtractSize
            });
        }
        return new CriCpkArchive(original, layout.HeaderPacket, layout.ItocPacket,
            layout.DataL, layout.DataH, entries, layout.LowRows, layout.HighRows, layout.ItocSizeBias);
    }

    /// <summary>
    /// Looks up a mutable file entry by ID; Build revalidates any direct payload or size changes.
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
    /// Copies an uncompressed replacement into the existing file ID without touching the original archive snapshot.
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
    /// Returns an exact copy for unchanged content; otherwise rebuilds ITOC metadata and verifies every file range.
    /// </summary>
    /// <param name="limits">Optional conservative safety limits; defaults are used when omitted.</param>
    /// <returns>The validated operation result.</returns>
    /// <remarks>Malformed input or violated preconditions are rejected before a persistent output is committed.</remarks>
    public CriCpkBuildResult Build(FileLimits? limits = null)
    {
        limits ??= FileLimits.Default;
        CriCpkLayout originalLayout = CriCpkLayout.Parse(_originalBytes, limits);
        if (_entries.Count != originalLayout.Slices.Length)
            throw new ToolkitException("CPK_ENTRY_MUTATION", "Adding/removing CPK entries is not supported.");
        bool unchanged = true;
        foreach (CriCpkFileInfo slice in originalLayout.Slices)
        {
            if (!_entries.TryGetValue(slice.Id, out CriCpkEntry? entry) || entry.Id != slice.Id || entry.PackedData is null || entry.ExtractSize <= 0)
                throw new ToolkitException("CPK_ENTRY_MUTATION", "CPK entry identity or size was modified inconsistently.");
            if (entry.PackedData.Length == 0 || entry.PackedData.Length > limits.MaximumInputBytes)
                throw new ToolkitException("CPK_FILE_SIZE", "CPK replacement violates the nonempty file size budget.");
            bool compressed = CrilaylaCodec.IsFrame(entry.PackedData);
            int actualSize = compressed ? CrilaylaCodec.Inspect(entry.PackedData, limits).ExtractedSize : entry.PackedData.Length;
            if (entry.ExtractSize != actualSize)
                throw new ToolkitException("CPK_EXTRACT_SIZE", "Entry extract size no longer matches its payload.");
            unchanged &= slice.ExtractSize == entry.ExtractSize && _originalBytes.AsSpan(slice.Offset, slice.PackedSize).SequenceEqual(entry.PackedData);
        }
        if (unchanged)
            return new CriCpkBuildResult(_originalBytes.ToArray(), new SortedDictionary<ushort, int>(_originalPackedSizes), new SortedDictionary<ushort, int>(_originalPackedSizes));
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
        CriUtfTable dataL = BuildDataTable(_dataLTemplate, low);
        CriUtfTable dataH = BuildDataTable(_dataHTemplate, high);
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
        long position = contentOffset;
        foreach (CriCpkEntry entry in _entries.Values)
        {
            entry.PackedData.CopyTo(output.AsSpan((int)position));
            position = BinaryUtilities.Align(position + entry.PackedData.Length, alignment);
        }

        CriCpkLayout verification = CriCpkLayout.Parse(output, limits);
        if (verification.Slices.Length != _entries.Count)
            throw new ToolkitException("CPK_VERIFY_COUNT", "Rebuilt CPK entry count verification failed.");
        foreach (CriCpkFileInfo slice in verification.Slices)
        {
            CriCpkEntry expected = _entries[slice.Id];
            if (slice.ExtractSize != expected.ExtractSize || !output.AsSpan(slice.Offset, slice.PackedSize).SequenceEqual(expected.PackedData))
                throw new ToolkitException("CPK_VERIFY_ENTRY", $"Rebuilt CPK entry {slice.Id} failed byte-for-byte verification.");
        }

        return new CriCpkBuildResult(
            output,
            new SortedDictionary<ushort, int>(_originalPackedSizes),
            new SortedDictionary<ushort, int>(_entries.ToDictionary(static pair => pair.Key, static pair => pair.Value.PackedData.Length)));
    }

    /// <summary>Rebuilds rows by field name, preserving auxiliary values when a file crosses the DataL/DataH threshold.</summary>
    /// <param name="template">Destination schema; columns are cloned without copying unused source rows.</param>
    /// <param name="entries">Ordered entries assigned to the destination size class.</param>
    /// <returns>A new table whose values are independent of both original templates.</returns>
    /// <exception cref="ToolkitException">An auxiliary source field has no compatible destination field.</exception>
    private CriUtfTable BuildDataTable(CriUtfTable template, IReadOnlyList<CriCpkEntry> entries)
    {
        CriUtfTable table = new() { Name = template.Name };
        table.Columns.AddRange(template.Columns.Select(static column => column.Clone()));
        foreach (string name in new[] { "ID", "FileSize", "ExtractSize" })
            table.Columns[table.GetColumnIndex(name)].Storage = CriUtfStorage.PerRow;
        int[]? lowMapping = null;
        int[]? highMapping = null;
        foreach (CriCpkEntry entry in entries)
        {
            bool wasLow = _originalLowRows.TryGetValue(entry.Id, out CriUtfRow? sourceRow);
            sourceRow ??= _originalHighRows[entry.Id];
            CriUtfTable source = wasLow ? _dataLTemplate : _dataHTemplate;
            int[] mapping = wasLow
                ? lowMapping ??= BuildAuxiliaryMapping(source, template)
                : highMapping ??= BuildAuxiliaryMapping(source, template);
            CriUtfRow row = table.CreateEmptyRow();
            for (int column = 0; column < table.Columns.Count; column++)
            {
                string name = table.Columns[column].Name;
                if (name is not ("ID" or "FileSize" or "ExtractSize"))
                    row.Values[column] = sourceRow.Values[mapping[column]].Clone();
            }
            table.Rows.Add(row);
            int rowIndex = table.Rows.Count - 1;
            table.SetUnsigned(rowIndex, "ID", entry.Id);
            table.SetUnsigned(rowIndex, "FileSize", (ulong)entry.PackedData.Length);
            table.SetUnsigned(rowIndex, "ExtractSize", (ulong)entry.ExtractSize);
        }
        // A source value may differ from the destination's shared constant. Promote
        // the whole column rather than silently overwrite other rows or drop metadata.
        for (int column = 0; column < table.Columns.Count; column++)
        {
            CriUtfColumn definition = table.Columns[column];
            if (definition.Storage == CriUtfStorage.PerRow) continue;
            CriUtfValue shared = definition.Storage == CriUtfStorage.Constant ? definition.ConstantValue : new CriUtfValue();
            if (table.Rows.Any(row => !ValuesEqual(definition.Type, row.Values[column], shared)))
                definition.Storage = CriUtfStorage.PerRow;
        }
        return table;
    }

    /// <summary>Computes a field-name mapping once per source schema and rejects lossy migrations.</summary>
    /// <param name="source">Schema in which the original file row was encoded.</param>
    /// <param name="target">Schema selected by the replacement's packed/extracted size.</param>
    /// <returns>Source column indices for each target auxiliary column; required size fields use -1.</returns>
    /// <exception cref="ToolkitException">Auxiliary names or exact field types differ between the size classes.</exception>
    private static int[] BuildAuxiliaryMapping(CriUtfTable source, CriUtfTable target)
    {
        var fields = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < source.Columns.Count; i++)
            if (source.Columns[i].Name is not ("ID" or "FileSize" or "ExtractSize"))
                fields.Add(source.Columns[i].Name, i);
        int[] mapping = new int[target.Columns.Count];
        Array.Fill(mapping, -1);
        int matched = 0;
        for (int i = 0; i < target.Columns.Count; i++)
        {
            CriUtfColumn column = target.Columns[i];
            if (column.Name is "ID" or "FileSize" or "ExtractSize") continue;
            if (!fields.TryGetValue(column.Name, out int sourceIndex) || source.Columns[sourceIndex].Type != column.Type)
                throw new ToolkitException("CPK_METADATA_SCHEMA", $"Cannot migrate unknown field '{column.Name}' without changing its meaning.");
            mapping[i] = sourceIndex;
            matched++;
        }
        if (matched != fields.Count)
            throw new ToolkitException("CPK_METADATA_SCHEMA", "Destination size class is missing an original auxiliary field.");
        return mapping;
    }

    /// <summary>Compares a UTF field without conflating floating-point bit patterns or binary contents.</summary>
    /// <param name="type">Encoded field type.</param>
    /// <param name="left">First row/constant value.</param>
    /// <param name="right">Second row/constant value.</param>
    /// <returns>Whether serialization of the values has the same meaning.</returns>
    private static bool ValuesEqual(CriUtfType type, CriUtfValue left, CriUtfValue right) => type switch
    {
        CriUtfType.String => string.Equals(left.Text, right.Text, StringComparison.Ordinal),
        CriUtfType.Data => left.Data.AsSpan().SequenceEqual(right.Data),
        CriUtfType.Single => BitConverter.SingleToInt32Bits(left.Single) == BitConverter.SingleToInt32Bits(right.Single),
        _ => left.Unsigned == right.Unsigned
    };

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

}
