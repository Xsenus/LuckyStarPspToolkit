using System.Buffers.Binary;
using System.Text;
using LuckyStarPspToolkit.Formats.Common;

namespace LuckyStarPspToolkit.Formats.Cri;

/// <summary>
/// Provides CRI UTF codec operations with strict bounds and format validation.
/// </summary>
public static class CriUtfCodec
{
    /// <summary>The magic value used by this model or operation.</summary>
    private static ReadOnlySpan<byte> Magic => "@UTF"u8;

    /// <summary>
    /// Parses packet while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="packetBytes">The packet bytes value.</param>
    /// <param name="limits">Optional conservative safety limits; defaults are used when omitted.</param>
    /// <returns>The validated operation result.</returns>
    /// <remarks>Malformed input or violated preconditions are rejected before a persistent output is committed.</remarks>
    public static CriUtfPacket ParsePacket(ReadOnlySpan<byte> packetBytes, FileLimits? limits = null)
    {
        limits ??= FileLimits.Default;
        if (packetBytes.Length < 12)
        {
            throw new ToolkitException("UTF_PACKET_SHORT", "CRI UTF packet is shorter than 12 bytes.");
        }

        BinarySpanReader reader = new(packetBytes);
        uint unknown = reader.ReadUInt32LittleEndian();
        ulong size64 = reader.ReadUInt64LittleEndian();
        int size = Guard.CheckedInt((long)size64, "UTF_PACKET_SIZE", "CRI UTF packet size");
        if (size > reader.Remaining)
        {
            throw new ToolkitException("UTF_PACKET_TRUNCATED", $"CRI UTF packet declares {size} bytes, only {reader.Remaining} remain.");
        }

        byte[] tableBytes = reader.ReadBytes(size).ToArray();
        bool encrypted = !tableBytes.AsSpan().StartsWith(Magic);
        if (encrypted)
        {
            TransformPacket(tableBytes);
        }

        if (!tableBytes.AsSpan().StartsWith(Magic))
        {
            throw new ToolkitException("UTF_MAGIC", "Decoded CRI UTF packet does not start with @UTF.");
        }

        CriUtfTable table = ParseTable(tableBytes, limits);
        return new CriUtfPacket(unknown, encrypted, table, checked(12 + size));
    }

    /// <summary>
    /// Builds packet while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="packet">The packet value.</param>
    /// <param name="encrypt">The encrypt value.</param>
    /// <returns>The resulting binary or typed sequence.</returns>
    /// <remarks>Malformed input or violated preconditions are rejected before a persistent output is committed.</remarks>
    public static byte[] BuildPacket(CriUtfPacket packet, bool? encrypt = null)
    {
        ArgumentNullException.ThrowIfNull(packet);
        byte[] table = BuildTable(packet.Table);
        bool encrypted = encrypt ?? packet.WasEncrypted;
        if (encrypted)
        {
            TransformPacket(table);
        }

        BinaryBufferWriter writer = new(checked(12 + table.Length));
        writer.WriteUInt32LittleEndian(packet.Unknown);
        writer.WriteUInt64LittleEndian((ulong)table.Length);
        writer.WriteBytes(table);
        return writer.Buffer;
    }

    /// <summary>
    /// Parses table while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="input">The input binary data or object.</param>
    /// <param name="limits">Optional conservative safety limits; defaults are used when omitted.</param>
    /// <returns>The validated operation result.</returns>
    /// <remarks>Malformed input or violated preconditions are rejected before a persistent output is committed.</remarks>
    public static CriUtfTable ParseTable(ReadOnlySpan<byte> input, FileLimits? limits = null)
    {
        limits ??= FileLimits.Default;
        if (input.Length < 32 || !input.StartsWith(Magic))
        {
            throw new ToolkitException("UTF_MAGIC", "CRI table does not start with @UTF or is too short.");
        }

        BinarySpanReader header = new(input, 4);
        uint tableSizeField = header.ReadUInt32BigEndian();
        int declaredLength = Guard.CheckedInt((long)tableSizeField + 8, "UTF_TABLE_SIZE", "UTF table length");
        if (declaredLength < 32 || declaredLength > input.Length)
        {
            throw new ToolkitException("UTF_TABLE_SIZE", $"CRI UTF declared length {declaredLength} is invalid for {input.Length} bytes.");
        }

        ReadOnlySpan<byte> data = input[..declaredLength];
        int rowsOffset = Guard.CheckedInt((long)header.ReadUInt32BigEndian() + 8, "UTF_OFFSET", "rows offset");
        int stringsOffset = Guard.CheckedInt((long)header.ReadUInt32BigEndian() + 8, "UTF_OFFSET", "strings offset");
        int dataOffset = Guard.CheckedInt((long)header.ReadUInt32BigEndian() + 8, "UTF_OFFSET", "data offset");
        uint tableNameOffset = header.ReadUInt32BigEndian();
        ushort columnCount = header.ReadUInt16BigEndian();
        ushort rowLength = header.ReadUInt16BigEndian();
        uint rowCount32 = header.ReadUInt32BigEndian();

        if (columnCount > limits.MaximumUtfColumns)
        {
            throw new ToolkitException("UTF_COLUMN_LIMIT", $"CRI UTF column count {columnCount} exceeds limit {limits.MaximumUtfColumns}.");
        }
        if (rowCount32 > limits.MaximumUtfRows)
        {
            throw new ToolkitException("UTF_ROW_LIMIT", $"CRI UTF row count {rowCount32} exceeds limit {limits.MaximumUtfRows}.");
        }

        long cells = (long)Math.Max(1, (int)columnCount) * rowCount32;
        if (limits.MaximumUtfCells < 0 || cells > limits.MaximumUtfCells)
        {
            throw new ToolkitException("UTF_CELL_LIMIT", $"UTF table would materialize {cells} cells; limit is {limits.MaximumUtfCells}.");
        }
        UtfAllocationBudget budget = new(limits.MaximumUtfDecodedBytes);
        int rowCount = Guard.CheckedInt(rowCount32, "UTF_ROW_LIMIT", "row count");
        ValidateOffset(rowsOffset, declaredLength, "rows");
        ValidateOffset(stringsOffset, declaredLength, "strings");
        ValidateOffset(dataOffset, declaredLength, "data");
        if (rowsOffset > stringsOffset || stringsOffset > dataOffset)
        {
            throw new ToolkitException("UTF_SECTION_ORDER", "CRI UTF sections are not ordered as rows, strings, data.");
        }
        long rowsEnd = (long)rowsOffset + (long)rowLength * rowCount;
        if (rowsEnd > stringsOffset)
        {
            throw new ToolkitException("UTF_ROWS_RANGE", $"CRI UTF rows end at {rowsEnd}, strings begin at {stringsOffset}.");
        }

        string tableName = ReadString(data, stringsOffset, dataOffset, tableNameOffset, "table name", budget);
        CriUtfTable table = new()
        {
            Name = tableName,
            OriginalDeclaredLength = declaredLength
        };

        BinarySpanReader columnsReader = new(data, 32);
        for (int i = 0; i < columnCount; i++)
        {
            byte flags = columnsReader.ReadByte();
            byte storageNibble = (byte)(flags & 0xF0);
            CriUtfStorage storage = storageNibble switch
            {
                0x00 or 0x10 => CriUtfStorage.Zero,
                0x30 => CriUtfStorage.Constant,
                0x50 => CriUtfStorage.PerRow,
                _ => throw new ToolkitException("UTF_STORAGE", $"Unsupported CRI UTF storage flag 0x{storageNibble:X2} in column {i}.")
            };
            CriUtfType type = ParseType((byte)(flags & 0x0F), i);
            uint nameOffset = columnsReader.ReadUInt32BigEndian();
            string name = ReadString(data, stringsOffset, dataOffset, nameOffset, $"column {i} name", budget);
            if (name.Length == 0 || name.IndexOf('\0') >= 0)
            {
                throw new ToolkitException("UTF_COLUMN_NAME", $"CRI UTF column {i} has an invalid name.");
            }
            if (table.Columns.Any(column => string.Equals(column.Name, name, StringComparison.Ordinal)))
            {
                throw new ToolkitException("UTF_DUPLICATE_COLUMN", $"Duplicate CRI UTF column name '{name}'.");
            }

            CriUtfColumn column = new()
            {
                Name = name,
                Type = type,
                Storage = storage,
                ConstantValue = new CriUtfValue()
            };
            if (storage == CriUtfStorage.Constant)
            {
                column.ConstantValue = ReadValue(ref columnsReader, data, stringsOffset, dataOffset, type, $"constant '{name}'", budget);
            }
            table.Columns.Add(column);
        }

        if (columnsReader.Position > rowsOffset)
        {
            throw new ToolkitException("UTF_COLUMNS_RANGE", $"CRI UTF column definitions overlap rows at {rowsOffset}.");
        }

        int computedRowLength = table.Columns.Where(static column => column.Storage == CriUtfStorage.PerRow).Sum(static column => ValueSize(column.Type));
        if (computedRowLength > rowLength)
        {
            throw new ToolkitException("UTF_ROW_LENGTH", $"CRI UTF row requires {computedRowLength} bytes but declares {rowLength}.");
        }

        for (int rowIndex = 0; rowIndex < rowCount; rowIndex++)
        {
            int rowOffset = checked(rowsOffset + rowIndex * rowLength);
            BinarySpanReader rowReader = new(data, rowOffset);
            CriUtfRow row = new();
            foreach (CriUtfColumn column in table.Columns)
            {
                CriUtfValue value = column.Storage switch
                {
                    CriUtfStorage.Zero => new CriUtfValue(),
                    CriUtfStorage.Constant => CloneConstant(column.ConstantValue, budget),
                    CriUtfStorage.PerRow => ReadValue(ref rowReader, data, stringsOffset, dataOffset, column.Type, $"row {rowIndex}, column '{column.Name}'", budget),
                    _ => throw new ToolkitException("UTF_STORAGE", "Unsupported CRI UTF storage mode.")
                };
                row.Values.Add(value);
            }
            if (rowReader.Position > rowOffset + rowLength)
            {
                throw new ToolkitException("UTF_ROW_OVERRUN", $"CRI UTF row {rowIndex} exceeds its declared length.");
            }
            table.Rows.Add(row);
        }

        return table;
    }

    /// <summary>
    /// Builds table while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="table">The table value.</param>
    /// <returns>The resulting binary or typed sequence.</returns>
    /// <remarks>Malformed input or violated preconditions are rejected before a persistent output is committed.</remarks>
    public static byte[] BuildTable(CriUtfTable table)
    {
        ArgumentNullException.ThrowIfNull(table);
        if (table.Columns.Count > ushort.MaxValue)
        {
            throw new ToolkitException("UTF_COLUMN_LIMIT", "Too many CRI UTF columns.");
        }
        if (table.Rows.Count > int.MaxValue)
        {
            throw new ToolkitException("UTF_ROW_LIMIT", "Too many CRI UTF rows.");
        }
        foreach (CriUtfRow row in table.Rows)
        {
            if (row.Values.Count != table.Columns.Count)
            {
                throw new ToolkitException("UTF_ROW_SHAPE", $"Table '{table.Name}' has a row with {row.Values.Count} values for {table.Columns.Count} columns.");
            }
        }

        StringPool strings = new();
        uint tableNameOffset = strings.Add(table.Name);
        uint[] columnNameOffsets = table.Columns.Select(column => strings.Add(column.Name)).ToArray();

        Dictionary<(int Row, int Column), uint> rowStringOffsets = new();
        Dictionary<int, uint> constantStringOffsets = new();
        for (int columnIndex = 0; columnIndex < table.Columns.Count; columnIndex++)
        {
            CriUtfColumn column = table.Columns[columnIndex];
            ValidateValueForType(column.ConstantValue, column.Type, column.Name);
            if (column.Storage == CriUtfStorage.Constant && column.Type == CriUtfType.String)
            {
                constantStringOffsets[columnIndex] = strings.Add(column.ConstantValue.Text);
            }
            if (column.Storage == CriUtfStorage.PerRow && column.Type == CriUtfType.String)
            {
                for (int rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
                {
                    string value = table.Rows[rowIndex].Values[columnIndex].Text;
                    rowStringOffsets[(rowIndex, columnIndex)] = strings.Add(value);
                }
            }
        }

        DataPool dataPool = new();
        Dictionary<int, uint> constantDataOffsets = new();
        Dictionary<(int Row, int Column), uint> rowDataOffsets = new();
        for (int columnIndex = 0; columnIndex < table.Columns.Count; columnIndex++)
        {
            CriUtfColumn column = table.Columns[columnIndex];
            if (column.Storage == CriUtfStorage.Constant && column.Type == CriUtfType.Data)
            {
                constantDataOffsets[columnIndex] = dataPool.Add(column.ConstantValue.Data);
            }
            if (column.Storage == CriUtfStorage.PerRow && column.Type == CriUtfType.Data)
            {
                for (int rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
                {
                    rowDataOffsets[(rowIndex, columnIndex)] = dataPool.Add(table.Rows[rowIndex].Values[columnIndex].Data);
                }
            }
        }

        int columnDefinitionsLength = 0;
        foreach (CriUtfColumn column in table.Columns)
        {
            columnDefinitionsLength = checked(columnDefinitionsLength + 5);
            if (column.Storage == CriUtfStorage.Constant)
            {
                columnDefinitionsLength = checked(columnDefinitionsLength + ValueSize(column.Type));
            }
        }
        int rowLengthInt = table.Columns.Where(static column => column.Storage == CriUtfStorage.PerRow).Sum(static column => ValueSize(column.Type));
        if (rowLengthInt > ushort.MaxValue)
        {
            throw new ToolkitException("UTF_ROW_LENGTH", $"CRI UTF row length {rowLengthInt} exceeds UInt16.");
        }

        int rowsOffset = BinaryUtilities.Align(checked(32 + columnDefinitionsLength), 8);
        int stringsOffset = BinaryUtilities.Align(checked(rowsOffset + rowLengthInt * table.Rows.Count), 8);
        byte[] stringBytes = strings.ToArray();
        int dataOffset = BinaryUtilities.Align(checked(stringsOffset + stringBytes.Length), 8);
        byte[] dataBytes = dataPool.ToArray();
        int totalLength = BinaryUtilities.Align(checked(dataOffset + dataBytes.Length), 8);
        if (totalLength < 32)
        {
            throw new ToolkitException("UTF_BUILD_SIZE", "Invalid CRI UTF output size.");
        }

        BinaryBufferWriter writer = new(totalLength);
        writer.WriteBytes(Magic);
        writer.WriteUInt32BigEndian(checked((uint)(totalLength - 8)));
        writer.WriteUInt32BigEndian(checked((uint)(rowsOffset - 8)));
        writer.WriteUInt32BigEndian(checked((uint)(stringsOffset - 8)));
        writer.WriteUInt32BigEndian(checked((uint)(dataOffset - 8)));
        writer.WriteUInt32BigEndian(tableNameOffset);
        writer.WriteUInt16BigEndian(checked((ushort)table.Columns.Count));
        writer.WriteUInt16BigEndian(checked((ushort)rowLengthInt));
        writer.WriteUInt32BigEndian(checked((uint)table.Rows.Count));

        for (int columnIndex = 0; columnIndex < table.Columns.Count; columnIndex++)
        {
            CriUtfColumn column = table.Columns[columnIndex];
            writer.WriteByte(column.Flags);
            writer.WriteUInt32BigEndian(columnNameOffsets[columnIndex]);
            if (column.Storage == CriUtfStorage.Constant)
            {
                WriteValue(writer, column.ConstantValue, column.Type,
                    constantStringOffsets.GetValueOrDefault(columnIndex),
                    constantDataOffsets.GetValueOrDefault(columnIndex));
            }
        }

        for (int rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
        {
            writer.Position = checked(rowsOffset + rowIndex * rowLengthInt);
            for (int columnIndex = 0; columnIndex < table.Columns.Count; columnIndex++)
            {
                CriUtfColumn column = table.Columns[columnIndex];
                if (column.Storage != CriUtfStorage.PerRow)
                {
                    continue;
                }
                CriUtfValue value = table.Rows[rowIndex].Values[columnIndex];
                ValidateValueForType(value, column.Type, column.Name);
                WriteValue(writer, value, column.Type,
                    rowStringOffsets.GetValueOrDefault((rowIndex, columnIndex)),
                    rowDataOffsets.GetValueOrDefault((rowIndex, columnIndex)));
            }
        }

        writer.WriteBytesAt(stringsOffset, stringBytes);
        writer.WriteBytesAt(dataOffset, dataBytes);
        return writer.Buffer;
    }

    /// <summary>
    /// Transforms packet while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="data">The binary data to process.</param>
    public static void TransformPacket(Span<byte> data)
    {
        uint key = 0x0000655F;
        const uint multiplier = 0x00004115;
        for (int i = 0; i < data.Length; i++)
        {
            data[i] ^= (byte)key;
            key = unchecked(key * multiplier);
        }
    }

    /// <summary>
    /// Parses type while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="nibble">The nibble value.</param>
    /// <param name="columnIndex">The column index value.</param>
    /// <returns>The validated operation result.</returns>
    /// <remarks>Malformed input or violated preconditions are rejected before a persistent output is committed.</remarks>
    private static CriUtfType ParseType(byte nibble, int columnIndex) => nibble switch
    {
        <= 0x8 => (CriUtfType)nibble,
        0xA => CriUtfType.String,
        0xB => CriUtfType.Data,
        _ => throw new ToolkitException("UTF_TYPE", $"Unsupported CRI UTF type 0x{nibble:X1} in column {columnIndex}.")
    };

    /// <summary>
    /// Reads value while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="reader">The mutable reader value updated by the operation.</param>
    /// <param name="table">The table value.</param>
    /// <param name="stringsOffset">The strings offset value.</param>
    /// <param name="dataOffset">The data offset value.</param>
    /// <param name="type">The type value.</param>
    /// <param name="context">A diagnostic label included in validation errors.</param>
    /// <param name="budget">Shared table-wide allocation allowance, charged before copies are created.</param>
    /// <returns>The validated operation result.</returns>
    private static CriUtfValue ReadValue(ref BinarySpanReader reader, ReadOnlySpan<byte> table, int stringsOffset, int dataOffset, CriUtfType type, string context, UtfAllocationBudget budget)
    {
        return type switch
        {
            CriUtfType.UInt8 or CriUtfType.Int8 => CriUtfValue.FromUnsigned(reader.ReadByte()),
            CriUtfType.UInt16 or CriUtfType.Int16 => CriUtfValue.FromUnsigned(reader.ReadUInt16BigEndian()),
            CriUtfType.UInt32 or CriUtfType.Int32 => CriUtfValue.FromUnsigned(reader.ReadUInt32BigEndian()),
            CriUtfType.UInt64 or CriUtfType.Int64 => CriUtfValue.FromUnsigned(reader.ReadUInt64BigEndian()),
            CriUtfType.Single => CriUtfValue.FromSingle(BitConverter.Int32BitsToSingle(unchecked((int)reader.ReadUInt32BigEndian()))),
            CriUtfType.String => CriUtfValue.FromText(ReadString(table, stringsOffset, dataOffset, reader.ReadUInt32BigEndian(), context, budget)),
            CriUtfType.Data => ReadData(ref reader, table, dataOffset, context, budget),
            _ => throw new ToolkitException("UTF_TYPE", $"Unsupported CRI UTF type in {context}.")
        };
    }

    /// <summary>
    /// Reads data while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="reader">The mutable reader value updated by the operation.</param>
    /// <param name="table">The table value.</param>
    /// <param name="dataOffset">The data offset value.</param>
    /// <param name="context">A diagnostic label included in validation errors.</param>
    /// <param name="budget">Shared table-wide allocation allowance, charged before copies are created.</param>
    /// <returns>The validated operation result.</returns>
    private static CriUtfValue ReadData(ref BinarySpanReader reader, ReadOnlySpan<byte> table, int dataOffset, string context, UtfAllocationBudget budget)
    {
        uint relativeOffset = reader.ReadUInt32BigEndian();
        uint length32 = reader.ReadUInt32BigEndian();
        int length = Guard.CheckedInt(length32, "UTF_DATA_RANGE", "data length");
        long absolute = (long)dataOffset + relativeOffset;
        if (absolute < 0 || absolute > table.Length - length)
        {
            throw new ToolkitException("UTF_DATA_RANGE", $"Data value in {context} points outside the CRI UTF table.");
        }
        budget.Charge(length, context);
        // This is already a fresh owned buffer; FromData would make an unnecessary second copy.
        return new CriUtfValue { Data = table.Slice((int)absolute, length).ToArray() };
    }

    /// <summary>
    /// Reads string while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="table">The table value.</param>
    /// <param name="stringsOffset">The strings offset value.</param>
    /// <param name="dataOffset">The data offset value.</param>
    /// <param name="relativeOffset">The relative offset value.</param>
    /// <param name="context">A diagnostic label included in validation errors.</param>
    /// <param name="budget">Shared table-wide allocation allowance, charged before copies are created.</param>
    /// <returns>The resulting text, path, identifier, or hexadecimal digest.</returns>
    private static string ReadString(ReadOnlySpan<byte> table, int stringsOffset, int dataOffset, uint relativeOffset, string context, UtfAllocationBudget budget)
    {
        long absolute64 = (long)stringsOffset + relativeOffset;
        if (absolute64 < stringsOffset || absolute64 >= dataOffset)
        {
            throw new ToolkitException("UTF_STRING_RANGE", $"String in {context} points outside the string section.");
        }
        int absolute = (int)absolute64;
        int end = absolute;
        while (end < dataOffset && table[end] != 0)
        {
            end++;
        }
        if (end == dataOffset)
        {
            throw new ToolkitException("UTF_STRING_TERMINATOR", $"String in {context} is not NUL terminated.");
        }
        try
        {
            // Two UTF-16 bytes per source byte is a conservative upper bound for valid UTF-8.
            budget.Charge((long)(end - absolute) * 2, context);
            string value = new UTF8Encoding(false, true).GetString(table[absolute..end]);
            if (value.IndexOf('\0') >= 0)
            {
                throw new ToolkitException("UTF_STRING_NUL", $"String in {context} contains an embedded NUL.");
            }
            return value;
        }
        catch (DecoderFallbackException ex)
        {
            throw new ToolkitException("UTF_STRING_ENCODING", $"String in {context} is not valid UTF-8.", ex);
        }
    }

    /// <summary>
    /// Writes value while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="writer">The callback that writes the staged output stream.</param>
    /// <param name="value">The value to process.</param>
    /// <param name="type">The type value.</param>
    /// <param name="stringOffset">The string offset value.</param>
    /// <param name="dataOffset">The data offset value.</param>
    private static void WriteValue(BinaryBufferWriter writer, CriUtfValue value, CriUtfType type, uint stringOffset, uint dataOffset)
    {
        switch (type)
        {
            case CriUtfType.UInt8:
            case CriUtfType.Int8:
                writer.WriteByte(checked((byte)value.Unsigned));
                break;
            case CriUtfType.UInt16:
            case CriUtfType.Int16:
                writer.WriteUInt16BigEndian(checked((ushort)value.Unsigned));
                break;
            case CriUtfType.UInt32:
            case CriUtfType.Int32:
                writer.WriteUInt32BigEndian(checked((uint)value.Unsigned));
                break;
            case CriUtfType.UInt64:
            case CriUtfType.Int64:
                writer.WriteUInt64BigEndian(value.Unsigned);
                break;
            case CriUtfType.Single:
                writer.WriteUInt32BigEndian(unchecked((uint)BitConverter.SingleToInt32Bits(value.Single)));
                break;
            case CriUtfType.String:
                writer.WriteUInt32BigEndian(stringOffset);
                break;
            case CriUtfType.Data:
                writer.WriteUInt32BigEndian(dataOffset);
                writer.WriteUInt32BigEndian(checked((uint)value.Data.Length));
                break;
            default:
                throw new ToolkitException("UTF_TYPE", $"Cannot write CRI UTF type {type}.");
        }
    }

    /// <summary>
    /// Returns the encoded byte width of one supported CRI UTF field type.
    /// </summary>
    /// <param name="type">The type value.</param>
    /// <returns>The validated operation result.</returns>
    private static int ValueSize(CriUtfType type) => type switch
    {
        CriUtfType.UInt8 or CriUtfType.Int8 => 1,
        CriUtfType.UInt16 or CriUtfType.Int16 => 2,
        CriUtfType.UInt32 or CriUtfType.Int32 or CriUtfType.Single or CriUtfType.String => 4,
        CriUtfType.UInt64 or CriUtfType.Int64 or CriUtfType.Data => 8,
        _ => throw new ToolkitException("UTF_TYPE", $"Unsupported CRI UTF type {type}.")
    };

    /// <summary>
    /// Validates value for type while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="value">The value to process.</param>
    /// <param name="type">The type value.</param>
    /// <param name="context">A diagnostic label included in validation errors.</param>
    /// <remarks>Malformed input or violated preconditions are rejected before a persistent output is committed.</remarks>
    private static void ValidateValueForType(CriUtfValue value, CriUtfType type, string context)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (type == CriUtfType.String && value.Text.IndexOf('\0') >= 0)
        {
            throw new ToolkitException("UTF_STRING_NUL", $"String value in '{context}' contains an embedded NUL.");
        }
        if (type == CriUtfType.Data && value.Data is null)
        {
            throw new ToolkitException("UTF_DATA_NULL", $"Data value in '{context}' is null.");
        }
        ulong maximum = type switch
        {
            CriUtfType.UInt8 or CriUtfType.Int8 => byte.MaxValue,
            CriUtfType.UInt16 or CriUtfType.Int16 => ushort.MaxValue,
            CriUtfType.UInt32 or CriUtfType.Int32 => uint.MaxValue,
            _ => ulong.MaxValue
        };
        if (type is not (CriUtfType.String or CriUtfType.Data or CriUtfType.Single) && value.Unsigned > maximum)
        {
            throw new ToolkitException("UTF_VALUE_RANGE", $"Integer value in '{context}' does not fit {type}.");
        }
    }

    /// <summary>
    /// Validates offset while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="offset">The zero-based byte or element offset.</param>
    /// <param name="length">The number of bytes or elements to process.</param>
    /// <param name="name">The logical name used for lookup or diagnostics.</param>
    /// <remarks>Malformed input or violated preconditions are rejected before a persistent output is committed.</remarks>
    private static void ValidateOffset(int offset, int length, string name)
    {
        if (offset < 0 || offset > length)
        {
            throw new ToolkitException("UTF_OFFSET", $"CRI UTF {name} offset {offset} is outside {length} bytes.");
        }
    }

    /// <summary>
    /// Represents the toolkit's string pool model or service.
    /// </summary>
    private sealed class StringPool
    {
        /// <summary>Stores the offsets state owned by this instance or type.</summary>
        private readonly Dictionary<string, uint> _offsets = new(StringComparer.Ordinal);
        /// <summary>Stores the bytes state owned by this instance or type.</summary>
        private readonly List<byte> _bytes = [];

        /// <summary>
        /// Interns a UTF table string or data blob and returns its offset in the corresponding pool.
        /// </summary>
        /// <param name="value">The value to process.</param>
        /// <returns>The validated operation result.</returns>
        public uint Add(string value)
        {
            ArgumentNullException.ThrowIfNull(value);
            if (value.IndexOf('\0') >= 0)
            {
                throw new ToolkitException("UTF_STRING_NUL", "CRI UTF string contains an embedded NUL.");
            }
            if (_offsets.TryGetValue(value, out uint existing))
            {
                return existing;
            }
            uint offset = checked((uint)_bytes.Count);
            byte[] encoded = new UTF8Encoding(false, true).GetBytes(value);
            _bytes.AddRange(encoded);
            _bytes.Add(0);
            _offsets.Add(value, offset);
            return offset;
        }

        /// <summary>
        /// Copies the current validated sequence into a new array.
        /// </summary>
        /// <returns>The resulting binary or typed sequence.</returns>
        public byte[] ToArray() => _bytes.ToArray();
    }

    /// <summary>
    /// Represents the toolkit's data pool model or service.
    /// </summary>
    private sealed class DataPool
    {
        /// <summary>Stores the bytes state owned by this instance or type.</summary>
        private readonly List<byte> _bytes = [];

        /// <summary>
        /// Interns a UTF table string or data blob and returns its offset in the corresponding pool.
        /// </summary>
        /// <param name="data">The binary data to process.</param>
        /// <returns>The validated operation result.</returns>
        public uint Add(byte[] data)
        {
            ArgumentNullException.ThrowIfNull(data);
            while ((_bytes.Count & 3) != 0)
            {
                _bytes.Add(0);
            }
            uint offset = checked((uint)_bytes.Count);
            _bytes.AddRange(data);
            return offset;
        }

        /// <summary>
        /// Copies the current validated sequence into a new array.
        /// </summary>
        /// <returns>The resulting binary or typed sequence.</returns>
        public byte[] ToArray() => _bytes.ToArray();
    }
    /// <summary>Copies mutable constant payloads only after charging every per-row materialization.</summary>
    /// <param name="value">The constant's decoded value; text is immutable, but binary buffers must not alias.</param>
    /// <param name="budget">The aggregate decoded-data allowance for this table.</param>
    /// <returns>An independent mutable value with a separately owned binary buffer.</returns>
    private static CriUtfValue CloneConstant(CriUtfValue value, UtfAllocationBudget budget)
    {
        budget.Charge(value.Data.Length, "constant row clone");
        return value.Clone();
    }

    /// <summary>Prevents short UTF tables from expanding repeated blob references into unbounded memory.</summary>
    private sealed class UtfAllocationBudget
    {
        /// <summary>Remaining permitted bytes of decoded strings and owned binary buffers.</summary>
        private long _remaining;

        /// <summary>Starts a positive allocation allowance before any variable-sized value is copied.</summary>
        /// <param name="maximum">The configured table-wide decoded-byte limit.</param>
        internal UtfAllocationBudget(long maximum)
        {
            if (maximum < 0) throw new ToolkitException("UTF_ALLOCATION_LIMIT", "UTF allocation limit must be non-negative.");
            _remaining = maximum;
        }

        /// <summary>Reserves bytes before allocation and rejects a request that exceeds the remaining budget.</summary>
        /// <param name="bytes">The number of decoded or copied bytes about to be materialized.</param>
        /// <param name="context">The field or operation reported when its allocation is refused.</param>
        internal void Charge(long bytes, string context)
        {
            if (bytes < 0 || bytes > _remaining)
                throw new ToolkitException("UTF_ALLOCATION_LIMIT", $"Decoded-data budget exhausted by {context} ({bytes} bytes requested).");
            _remaining -= bytes;
        }
    }

}
