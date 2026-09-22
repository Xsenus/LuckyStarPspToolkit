using System.Buffers.Binary;
using LuckyStarPspToolkit.Formats.Common;

namespace LuckyStarPspToolkit.Formats.Cri;

/// <summary>Preflights all CPK metadata and payload headers without copying archive or file payload buffers.</summary>
/// <param name="HeaderPacket">Parsed CPK header retained for rebuilding unknown columns.</param>
/// <param name="ItocPacket">Parsed ITOC packet, including its original encryption mode.</param>
/// <param name="DataL">Small-entry schema and source row values.</param>
/// <param name="DataH">Large-entry schema and source row values.</param>
/// <param name="LowRows">Source DataL rows keyed by file identifier.</param>
/// <param name="HighRows">Source DataH rows keyed by file identifier.</param>
/// <param name="Slices">Ordered immutable file ranges.</param>
/// <param name="ContentOffset">Start of aligned file content.</param>
/// <param name="ItocOffset">Absolute ITOC chunk offset.</param>
/// <param name="Alignment">Power-of-two file alignment in bytes.</param>
/// <param name="ItocSizeBias">Preserved difference between declared ITOC size and encoded packet length.</param>
internal sealed record CriCpkLayout(CriUtfPacket HeaderPacket, CriUtfPacket ItocPacket,
    CriUtfTable DataL, CriUtfTable DataH, Dictionary<ushort, CriUtfRow> LowRows,
    Dictionary<ushort, CriUtfRow> HighRows, CriCpkFileInfo[] Slices, int ContentOffset,
    int ItocOffset, int Alignment, long ItocSizeBias)
{
    /// <summary>Validates the complete ITOC-only layout before a caller allocates payload copies.</summary>
    /// <param name="data">Borrowed archive bytes; this method does not retain the span.</param>
    /// <param name="limits">Budgets for the input, metadata, tables and decoded frame headers.</param>
    /// <returns>Metadata and checked file ranges; no decoded CRILAYLA body is produced.</returns>
    /// <exception cref="ToolkitException">An offset, count, allocation budget or supported-profile invariant is invalid.</exception>
    internal static CriCpkLayout Parse(ReadOnlySpan<byte> data, FileLimits? limits = null)
    {
        limits ??= FileLimits.Default;
        if (data.Length < 32 || !data.StartsWith("CPK "u8))
        {
            throw new ToolkitException("CPK_MAGIC", "Input is not a CRI CPK archive.");
        }
        if (limits.MaximumInputBytes < 0 || data.Length > limits.MaximumInputBytes)
            throw new ToolkitException("CPK_INPUT_LIMIT", "CPK input exceeds the configured byte budget.");
        long metadataBytes = 0;
        CriUtfPacket headerPacket = ReadPacket(data, 0, limits, ref metadataBytes);
        CriUtfTable header = headerPacket.Table;
        if (header.Rows.Count != 1)
        {
            throw new ToolkitException("CPK_HEADER_ROWS", $"CPK header must have one row; found {header.Rows.Count}.");
        }

        int itocOffset = ReadInt(header, 0, "ItocOffset", "CPK_ITOC_OFFSET");
        if (itocOffset < 0 || itocOffset > data.Length - 16 || !data.Slice(itocOffset).StartsWith("ITOC"u8))
        {
            throw new ToolkitException("CPK_ITOC", $"ITOC chunk is missing at offset {itocOffset}.");
        }
        if (4L + headerPacket.OriginalPacketLength > itocOffset)
            throw new ToolkitException("CPK_HEADER_OVERLAP", "CPK header overlaps the ITOC chunk.");
        foreach (string column in new[] { "TocOffset", "EtocOffset", "GtocOffset" })
            if (header.TryGetColumnIndex(column, out _) && header.GetUnsigned(0, column) is not (0 or ulong.MaxValue))
                throw new ToolkitException("CPK_UNSUPPORTED_INDEX", $"Additional {column} index is not supported by this ITOC-only codec.");
        CriUtfPacket itocPacket = ReadPacket(data, itocOffset, limits, ref metadataBytes);
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
        int declaredL = ReadInt(itoc, 0, "FilesL", "CPK_FILE_COUNT");
        int declaredH = ReadInt(itoc, 0, "FilesH", "CPK_FILE_COUNT");
        if (declaredL != dataL.Rows.Count || declaredH != dataH.Rows.Count)
        {
            throw new ToolkitException("CPK_FILE_COUNT", $"ITOC count mismatch: FilesL={declaredL}/{dataL.Rows.Count}, FilesH={declaredH}/{dataH.Rows.Count}.");
        }
        if ((long)declaredL + declaredH > limits.MaximumCpkEntries)
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
        if (header.TryGetColumnIndex("Files", out _) && header.GetUnsigned(0, "Files") != (ulong)descriptors.Count)
            throw new ToolkitException("CPK_FILE_COUNT", "Header Files disagrees with the ITOC descriptor count.");

        int contentOffset = ReadInt(header, 0, "ContentOffset", "CPK_CONTENT_OFFSET");
        int alignment = ReadInt(header, 0, "Align", "CPK_ALIGN");
        if (alignment <= 0 || alignment > 1024 * 1024 || (alignment & (alignment - 1)) != 0)
        {
            throw new ToolkitException("CPK_ALIGN", $"Unsupported CPK alignment {alignment}; expected a power of two up to 1 MiB.");
        }
        long itocActualEnd = (long)itocOffset + 4 + itocPacket.OriginalPacketLength;
        if (contentOffset < itocActualEnd || contentOffset > data.Length)
        {
            throw new ToolkitException("CPK_CONTENT_OFFSET", $"CPK content offset {contentOffset} is invalid; ITOC ends at {itocActualEnd}.");
        }

        var slices = new List<CriCpkFileInfo>(descriptors.Count);
        long position = contentOffset;
        foreach ((ushort id, EntryDescriptor descriptor) in descriptors.OrderBy(static pair => pair.Key))
        {
            if (descriptor.PackedSize < 0 || position > data.Length - descriptor.PackedSize)
            {
                throw new ToolkitException("CPK_CONTENT_RANGE", $"CPK entry {id} exceeds archive bounds at offset {position}.");
            }
            ReadOnlySpan<byte> packed = data.Slice((int)position, descriptor.PackedSize);
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
            slices.Add(new CriCpkFileInfo(id, (int)position, descriptor.PackedSize, descriptor.ExtractSize, compressed));
            position = BinaryUtilities.Align(position + descriptor.PackedSize, alignment);
        }

        ulong headerItocSize = header.GetUnsigned(0, "ItocSize");
        long actualItocChunk = checked(4L + itocPacket.OriginalPacketLength);
        if (headerItocSize > int.MaxValue)
            throw new ToolkitException("CPK_ITOC_SIZE", "ITOC size exceeds the addressable archive size.");
        long itocSizeBias = (long)headerItocSize - actualItocChunk;
        if (Math.Abs(itocSizeBias) > 0x1000)
        {
            throw new ToolkitException("CPK_ITOC_SIZE", $"Header ITOC size differs from actual chunk by unexpected bias {itocSizeBias}.");
        }

        return new CriCpkLayout(headerPacket, itocPacket, dataL, dataH, lowRows, highRows,
            slices.ToArray(), contentOffset, itocOffset, alignment, itocSizeBias);
    }


    /// <summary>Checks a packet's declared length and aggregate metadata budget before decoding @UTF.</summary>
    /// <param name="data">Complete borrowed CPK bytes.</param>
    /// <param name="offset">Chunk start, including its four-byte magic.</param>
    /// <param name="limits">Maximum total size of the header and ITOC packets.</param>
    /// <param name="metadataBytes">Updated aggregate byte count, including chunk headers.</param>
    /// <returns>The validated packet with its original encoded length.</returns>
    private static CriUtfPacket ReadPacket(ReadOnlySpan<byte> data, int offset, FileLimits limits, ref long metadataBytes)
    {
        if (offset < 0 || offset > data.Length - 16)
            throw new ToolkitException("CPK_PACKET_RANGE", "CPK metadata packet header is truncated.");
        ulong size = BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(offset + 8, 8));
        if (size > (ulong)(data.Length - offset - 16))
            throw new ToolkitException("CPK_PACKET_RANGE", "CPK metadata packet exceeds archive bounds.");
        metadataBytes += 16 + (long)size;
        if (limits.MaximumCpkMetadataBytes < 0 || metadataBytes > limits.MaximumCpkMetadataBytes)
            throw new ToolkitException("CPK_METADATA_LIMIT", "CPK header and ITOC exceed the metadata allocation budget.");
        return CriUtfCodec.ParsePacket(data.Slice(offset + 4, 12 + (int)size), limits);
    }

    /// <summary>Converts unsigned on-disk integers only after testing the requested representable maximum.</summary>
    /// <param name="table">UTF table containing the integer field.</param>
    /// <param name="row">Zero-based row index.</param>
    /// <param name="column">Exact UTF field name.</param>
    /// <param name="code">Stable error code used for malformed values.</param>
    /// <param name="maximum">Largest supported integer; identifiers use UInt16 instead of Int32.</param>
    /// <returns>The checked integer value.</returns>
    private static int ReadInt(CriUtfTable table, int row, string column, string code, int maximum = int.MaxValue)
    {
        ulong value = table.GetUnsigned(row, column);
        if (value > (ulong)maximum)
            throw new ToolkitException(code, $"{column} value {value} exceeds supported maximum {maximum}.");
        return (int)value;
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
            ushort id = checked((ushort)ReadInt(table, i, "ID", "CPK_ID", ushort.MaxValue));
            int packed = ReadInt(table, i, "FileSize", "CPK_FILE_SIZE");
            int extracted = ReadInt(table, i, "ExtractSize", "CPK_EXTRACT_SIZE");
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
            rows.Add(id, table.Rows[i]);
        }
        return rows;
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
