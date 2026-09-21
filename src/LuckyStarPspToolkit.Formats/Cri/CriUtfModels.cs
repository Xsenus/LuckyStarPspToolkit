using LuckyStarPspToolkit.Formats.Common;

namespace LuckyStarPspToolkit.Formats.Cri;

/// <summary>
/// Defines the supported CRI UTF storage values.
/// </summary>
public enum CriUtfStorage : byte
{
    /// <summary>A value represented by implicit zero storage.</summary>
    Zero = 0x10,
    /// <summary>A value encoded once for all rows of a UTF column.</summary>
    Constant = 0x30,
    /// <summary>A value encoded separately in every UTF table row.</summary>
    PerRow = 0x50
}

/// <summary>
/// Defines the supported CRI UTF type values.
/// </summary>
public enum CriUtfType : byte
{
    /// <summary>The uint8 value used by this model or operation.</summary>
    UInt8 = 0x0,
    /// <summary>The int8 value used by this model or operation.</summary>
    Int8 = 0x1,
    /// <summary>The uint16 value used by this model or operation.</summary>
    UInt16 = 0x2,
    /// <summary>The int16 value used by this model or operation.</summary>
    Int16 = 0x3,
    /// <summary>The uint32 value used by this model or operation.</summary>
    UInt32 = 0x4,
    /// <summary>The int32 value used by this model or operation.</summary>
    Int32 = 0x5,
    /// <summary>The uint64 value used by this model or operation.</summary>
    UInt64 = 0x6,
    /// <summary>The int64 value used by this model or operation.</summary>
    Int64 = 0x7,
    /// <summary>The single-precision floating-point value of a UTF field.</summary>
    Single = 0x8,
    /// <summary>The string value used by this model or operation.</summary>
    String = 0xA,
    /// <summary>The byte payload associated with this record.</summary>
    Data = 0xB
}

/// <summary>
/// Represents the toolkit's CRI UTF value model or service.
/// </summary>
public sealed class CriUtfValue
{
    /// <summary>The unsigned representation of an encoded integer field.</summary>
    public ulong Unsigned { get; set; }
    /// <summary>The single-precision floating-point value of a UTF field.</summary>
    public float Single { get; set; }
    /// <summary>The decoded string value associated with this record.</summary>
    public string Text { get; set; } = string.Empty;
    /// <summary>The byte payload associated with this record.</summary>
    public byte[] Data { get; set; } = [];

    /// <summary>
    /// Creates a deep copy whose mutable buffers are independent of the source instance.
    /// </summary>
    /// <returns>The validated operation result.</returns>
    public CriUtfValue Clone() => new()
    {
        Unsigned = Unsigned,
        Single = Single,
        Text = Text,
        Data = Data.ToArray()
    };

    /// <summary>
    /// Creates unsigned while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="value">The value to process.</param>
    /// <returns>The validated operation result.</returns>
    public static CriUtfValue FromUnsigned(ulong value) => new() { Unsigned = value };
    /// <summary>
    /// Creates single while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="value">The value to process.</param>
    /// <returns>The validated operation result.</returns>
    public static CriUtfValue FromSingle(float value) => new() { Single = value };
    /// <summary>
    /// Creates text while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="value">The value to process.</param>
    /// <returns>The validated operation result.</returns>
    public static CriUtfValue FromText(string value) => new() { Text = value ?? throw new ArgumentNullException(nameof(value)) };
    /// <summary>
    /// Creates data while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="value">The value to process.</param>
    /// <returns>The validated operation result.</returns>
    public static CriUtfValue FromData(byte[] value) => new() { Data = value?.ToArray() ?? throw new ArgumentNullException(nameof(value)) };
}

/// <summary>
/// Represents the toolkit's CRI UTF column model or service.
/// </summary>
public sealed class CriUtfColumn
{
    /// <summary>The name value used by this model or operation.</summary>
    public required string Name { get; set; }
    /// <summary>The type value used by this model or operation.</summary>
    public required CriUtfType Type { get; set; }
    /// <summary>The storage value used by this model or operation.</summary>
    public required CriUtfStorage Storage { get; set; }
    /// <summary>The value stored once when a UTF column uses constant storage.</summary>
    public CriUtfValue ConstantValue { get; set; } = new();

    /// <summary>The encoded flags combining storage mode and value type.</summary>
    public byte Flags => (byte)((byte)Storage | (byte)Type);

    /// <summary>
    /// Creates a deep copy whose mutable buffers are independent of the source instance.
    /// </summary>
    /// <returns>The validated operation result.</returns>
    public CriUtfColumn Clone() => new()
    {
        Name = Name,
        Type = Type,
        Storage = Storage,
        ConstantValue = ConstantValue.Clone()
    };
}

/// <summary>
/// Represents the toolkit's CRI UTF row model or service.
/// </summary>
public sealed class CriUtfRow
{
    /// <summary>The values value used by this model or operation.</summary>
    public List<CriUtfValue> Values { get; } = [];

    /// <summary>
    /// Creates a deep copy whose mutable buffers are independent of the source instance.
    /// </summary>
    /// <returns>The validated operation result.</returns>
    public CriUtfRow Clone()
    {
        CriUtfRow row = new();
        row.Values.AddRange(Values.Select(static value => value.Clone()));
        return row;
    }
}

/// <summary>
/// Represents the toolkit's CRI UTF table model or service.
/// </summary>
public sealed class CriUtfTable
{
    /// <summary>The name value used by this model or operation.</summary>
    public required string Name { get; set; }
    /// <summary>The columns value used by this model or operation.</summary>
    public List<CriUtfColumn> Columns { get; } = [];
    /// <summary>The rows value used by this model or operation.</summary>
    public List<CriUtfRow> Rows { get; } = [];
    /// <summary>The original declared length value used by this model or operation.</summary>
    public int OriginalDeclaredLength { get; init; }

    /// <summary>
    /// Gets column index while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="name">The logical name used for lookup or diagnostics.</param>
    /// <returns>The validated operation result.</returns>
    public int GetColumnIndex(string name)
    {
        for (int i = 0; i < Columns.Count; i++)
        {
            if (string.Equals(Columns[i].Name, name, StringComparison.Ordinal))
            {
                return i;
            }
        }

        throw new ToolkitException("UTF_COLUMN_NOT_FOUND", $"CRI UTF column '{name}' was not found in table '{Name}'.");
    }

    /// <summary>
    /// Attempts to get column index.
    /// </summary>
    /// <param name="name">The logical name used for lookup or diagnostics.</param>
    /// <param name="index">Receives index value when the operation succeeds.</param>
    /// <returns><see langword="true"/> when the requested value was produced; otherwise <see langword="false"/>.</returns>
    public bool TryGetColumnIndex(string name, out int index)
    {
        for (int i = 0; i < Columns.Count; i++)
        {
            if (string.Equals(Columns[i].Name, name, StringComparison.Ordinal))
            {
                index = i;
                return true;
            }
        }

        index = -1;
        return false;
    }

    /// <summary>
    /// Gets unsigned while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="rowIndex">The row index value.</param>
    /// <param name="columnName">The column name value.</param>
    /// <returns>The validated operation result.</returns>
    public ulong GetUnsigned(int rowIndex, string columnName)
    {
        int columnIndex = GetColumnIndex(columnName);
        ValidateRow(rowIndex);
        EnsureNumeric(Columns[columnIndex].Type, columnName);
        return Rows[rowIndex].Values[columnIndex].Unsigned;
    }

    /// <summary>
    /// Gets text while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="rowIndex">The row index value.</param>
    /// <param name="columnName">The column name value.</param>
    /// <returns>The resulting text, path, identifier, or hexadecimal digest.</returns>
    public string GetText(int rowIndex, string columnName)
    {
        int columnIndex = GetColumnIndex(columnName);
        ValidateRow(rowIndex);
        if (Columns[columnIndex].Type != CriUtfType.String)
        {
            throw new ToolkitException("UTF_COLUMN_TYPE", $"Column '{columnName}' is not a string.");
        }

        return Rows[rowIndex].Values[columnIndex].Text;
    }

    /// <summary>
    /// Gets data while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="rowIndex">The row index value.</param>
    /// <param name="columnName">The column name value.</param>
    /// <returns>The resulting binary or typed sequence.</returns>
    public byte[] GetData(int rowIndex, string columnName)
    {
        int columnIndex = GetColumnIndex(columnName);
        ValidateRow(rowIndex);
        if (Columns[columnIndex].Type != CriUtfType.Data)
        {
            throw new ToolkitException("UTF_COLUMN_TYPE", $"Column '{columnName}' is not a data blob.");
        }

        return Rows[rowIndex].Values[columnIndex].Data.ToArray();
    }

    /// <summary>
    /// Sets unsigned while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="rowIndex">The row index value.</param>
    /// <param name="columnName">The column name value.</param>
    /// <param name="value">The value to process.</param>
    public void SetUnsigned(int rowIndex, string columnName, ulong value)
    {
        int columnIndex = GetColumnIndex(columnName);
        ValidateRow(rowIndex);
        CriUtfColumn column = Columns[columnIndex];
        EnsureNumeric(column.Type, columnName);
        ValidateUnsignedFits(column.Type, value, columnName);
        PromoteIfNecessary(column, CriUtfValue.FromUnsigned(value));
        if (column.Storage == CriUtfStorage.Constant)
        {
            column.ConstantValue.Unsigned = value;
            foreach (CriUtfRow row in Rows)
            {
                row.Values[columnIndex].Unsigned = value;
            }
        }
        else
        {
            Rows[rowIndex].Values[columnIndex].Unsigned = value;
        }
    }

    /// <summary>
    /// Sets data while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="rowIndex">The row index value.</param>
    /// <param name="columnName">The column name value.</param>
    /// <param name="value">The value to process.</param>
    public void SetData(int rowIndex, string columnName, byte[] value)
    {
        ArgumentNullException.ThrowIfNull(value);
        int columnIndex = GetColumnIndex(columnName);
        ValidateRow(rowIndex);
        CriUtfColumn column = Columns[columnIndex];
        if (column.Type != CriUtfType.Data)
        {
            throw new ToolkitException("UTF_COLUMN_TYPE", $"Column '{columnName}' is not a data blob.");
        }

        PromoteIfNecessary(column, CriUtfValue.FromData(value));
        if (column.Storage == CriUtfStorage.Constant)
        {
            column.ConstantValue.Data = value.ToArray();
            foreach (CriUtfRow row in Rows)
            {
                row.Values[columnIndex].Data = value.ToArray();
            }
        }
        else
        {
            Rows[rowIndex].Values[columnIndex].Data = value.ToArray();
        }
    }

    /// <summary>
    /// Sets text while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="rowIndex">The row index value.</param>
    /// <param name="columnName">The column name value.</param>
    /// <param name="value">The value to process.</param>
    public void SetText(int rowIndex, string columnName, string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        int columnIndex = GetColumnIndex(columnName);
        ValidateRow(rowIndex);
        CriUtfColumn column = Columns[columnIndex];
        if (column.Type != CriUtfType.String)
        {
            throw new ToolkitException("UTF_COLUMN_TYPE", $"Column '{columnName}' is not a string.");
        }

        PromoteIfNecessary(column, CriUtfValue.FromText(value));
        if (column.Storage == CriUtfStorage.Constant)
        {
            column.ConstantValue.Text = value;
            foreach (CriUtfRow row in Rows)
            {
                row.Values[columnIndex].Text = value;
            }
        }
        else
        {
            Rows[rowIndex].Values[columnIndex].Text = value;
        }
    }

    /// <summary>
    /// Creates a deep copy whose mutable buffers are independent of the source instance.
    /// </summary>
    /// <returns>The validated operation result.</returns>
    public CriUtfTable Clone()
    {
        CriUtfTable table = new()
        {
            Name = Name,
            OriginalDeclaredLength = OriginalDeclaredLength
        };
        table.Columns.AddRange(Columns.Select(static column => column.Clone()));
        table.Rows.AddRange(Rows.Select(static row => row.Clone()));
        return table;
    }

    /// <summary>
    /// Creates empty row while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <returns>The validated operation result.</returns>
    public CriUtfRow CreateEmptyRow()
    {
        CriUtfRow row = new();
        foreach (CriUtfColumn column in Columns)
        {
            row.Values.Add(column.Storage == CriUtfStorage.Constant ? column.ConstantValue.Clone() : new CriUtfValue());
        }

        return row;
    }

    /// <summary>
    /// Validates row while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="rowIndex">The row index value.</param>
    /// <remarks>Malformed input or violated preconditions are rejected before a persistent output is committed.</remarks>
    private void ValidateRow(int rowIndex)
    {
        if ((uint)rowIndex >= (uint)Rows.Count)
        {
            throw new ToolkitException("UTF_ROW_RANGE", $"Row index {rowIndex} is outside table '{Name}' with {Rows.Count} rows.");
        }
    }

    /// <summary>
    /// Promotes a constant or zero UTF column to per-row storage before writing a different row value.
    /// </summary>
    /// <param name="column">The column value.</param>
    /// <param name="value">The value to process.</param>
    private static void PromoteIfNecessary(CriUtfColumn column, CriUtfValue value)
    {
        if (column.Storage == CriUtfStorage.Zero)
        {
            column.Storage = CriUtfStorage.Constant;
            column.ConstantValue = value.Clone();
        }
    }

    /// <summary>
    /// Ensures numeric while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="type">The type value.</param>
    /// <param name="name">The logical name used for lookup or diagnostics.</param>
    private static void EnsureNumeric(CriUtfType type, string name)
    {
        if (type is CriUtfType.String or CriUtfType.Data or CriUtfType.Single)
        {
            throw new ToolkitException("UTF_COLUMN_TYPE", $"Column '{name}' is not an integer.");
        }
    }

    /// <summary>
    /// Validates unsigned fits while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="type">The type value.</param>
    /// <param name="value">The value to process.</param>
    /// <param name="name">The logical name used for lookup or diagnostics.</param>
    /// <remarks>Malformed input or violated preconditions are rejected before a persistent output is committed.</remarks>
    private static void ValidateUnsignedFits(CriUtfType type, ulong value, string name)
    {
        ulong maximum = type switch
        {
            CriUtfType.UInt8 or CriUtfType.Int8 => byte.MaxValue,
            CriUtfType.UInt16 or CriUtfType.Int16 => ushort.MaxValue,
            CriUtfType.UInt32 or CriUtfType.Int32 => uint.MaxValue,
            CriUtfType.UInt64 or CriUtfType.Int64 => ulong.MaxValue,
            _ => throw new ToolkitException("UTF_COLUMN_TYPE", $"Column '{name}' is not an integer.")
        };
        if (value > maximum)
        {
            throw new ToolkitException("UTF_VALUE_RANGE", $"Value {value} does not fit column '{name}' ({type}).");
        }
    }
}

/// <summary>
/// Represents immutable CRI UTF packet data exchanged by the toolkit.
/// </summary>
/// <param name="Unknown">The unknown value used by this model or operation.</param>
/// <param name="WasEncrypted">The was encrypted value used by this model or operation.</param>
/// <param name="Table">The table value used by this model or operation.</param>
/// <param name="OriginalPacketLength">The original packet length value used by this model or operation.</param>
public sealed record CriUtfPacket(uint Unknown, bool WasEncrypted, CriUtfTable Table, int OriginalPacketLength);
