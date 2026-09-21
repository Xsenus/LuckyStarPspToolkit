using System.Buffers.Binary;
using System.Text;

namespace LuckyStarPspToolkit.Formats.Common;

/// <summary>
/// Provides binary span reader operations with strict bounds and format validation.
/// </summary>
internal ref struct BinarySpanReader
{
    /// <summary>Stores the data state owned by this instance or type.</summary>
    private readonly ReadOnlySpan<byte> _data;

    /// <summary>
    /// Initializes a new instance with validated constructor state.
    /// </summary>
    /// <param name="data">The binary data to process.</param>
    /// <param name="position">The position value.</param>
    public BinarySpanReader(ReadOnlySpan<byte> data, int position = 0)
    {
        _data = data;
        Position = position;
        Ensure(0);
    }

    /// <summary>The position value used by this model or operation.</summary>
    public int Position { get; set; }
    /// <summary>The remaining value used by this model or operation.</summary>
    public int Remaining => _data.Length - Position;
    /// <summary>The length value used by this model or operation.</summary>
    public int Length => _data.Length;

    /// <summary>
    /// Reads byte while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <returns>The validated operation result.</returns>
    public byte ReadByte()
    {
        Ensure(1);
        return _data[Position++];
    }

    /// <summary>
    /// Reads u int 16 little endian while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <returns>The validated operation result.</returns>
    public ushort ReadUInt16LittleEndian()
    {
        Ensure(2);
        ushort value = BinaryPrimitives.ReadUInt16LittleEndian(_data[Position..]);
        Position += 2;
        return value;
    }

    /// <summary>
    /// Reads u int 16 big endian while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <returns>The validated operation result.</returns>
    public ushort ReadUInt16BigEndian()
    {
        Ensure(2);
        ushort value = BinaryPrimitives.ReadUInt16BigEndian(_data[Position..]);
        Position += 2;
        return value;
    }

    /// <summary>
    /// Reads u int 32 little endian while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <returns>The validated operation result.</returns>
    public uint ReadUInt32LittleEndian()
    {
        Ensure(4);
        uint value = BinaryPrimitives.ReadUInt32LittleEndian(_data[Position..]);
        Position += 4;
        return value;
    }

    /// <summary>
    /// Reads u int 32 big endian while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <returns>The validated operation result.</returns>
    public uint ReadUInt32BigEndian()
    {
        Ensure(4);
        uint value = BinaryPrimitives.ReadUInt32BigEndian(_data[Position..]);
        Position += 4;
        return value;
    }

    /// <summary>
    /// Reads u int 64 little endian while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <returns>The validated operation result.</returns>
    public ulong ReadUInt64LittleEndian()
    {
        Ensure(8);
        ulong value = BinaryPrimitives.ReadUInt64LittleEndian(_data[Position..]);
        Position += 8;
        return value;
    }

    /// <summary>
    /// Reads u int 64 big endian while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <returns>The validated operation result.</returns>
    public ulong ReadUInt64BigEndian()
    {
        Ensure(8);
        ulong value = BinaryPrimitives.ReadUInt64BigEndian(_data[Position..]);
        Position += 8;
        return value;
    }

    /// <summary>
    /// Reads bytes while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="length">The number of bytes or elements to process.</param>
    /// <returns>The resulting binary or typed sequence.</returns>
    public ReadOnlySpan<byte> ReadBytes(int length)
    {
        Ensure(length);
        ReadOnlySpan<byte> value = _data.Slice(Position, length);
        Position += length;
        return value;
    }

    /// <summary>
    /// Returns a bounds-checked view over the requested range.
    /// </summary>
    /// <param name="offset">The zero-based byte or element offset.</param>
    /// <param name="length">The number of bytes or elements to process.</param>
    /// <returns>The resulting binary or typed sequence.</returns>
    public ReadOnlySpan<byte> Slice(int offset, int length)
    {
        if (offset < 0 || length < 0 || offset > _data.Length - length)
        {
            throw new ToolkitException("BINARY_RANGE", $"Binary slice [{offset}, {offset + length}) exceeds {_data.Length} bytes.");
        }

        return _data.Slice(offset, length);
    }

    /// <summary>
    /// Reads null terminated UTF 8 at while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="offset">The zero-based byte or element offset.</param>
    /// <param name="maximumBytes">The maximum bytes value.</param>
    /// <returns>The resulting text, path, identifier, or hexadecimal digest.</returns>
    public string ReadNullTerminatedUtf8At(int offset, int maximumBytes)
    {
        if (offset < 0 || offset >= _data.Length)
        {
            throw new ToolkitException("STRING_OFFSET", $"String offset {offset} is outside {_data.Length} bytes.");
        }

        int limit = Math.Min(_data.Length, checked(offset + maximumBytes));
        int end = offset;
        while (end < limit && _data[end] != 0)
        {
            end++;
        }

        if (end == limit)
        {
            throw new ToolkitException("STRING_TERMINATOR", $"String at offset {offset} is not NUL terminated within {maximumBytes} bytes.");
        }

        try
        {
            return new UTF8Encoding(false, true).GetString(_data[offset..end]);
        }
        catch (DecoderFallbackException ex)
        {
            throw new ToolkitException("STRING_UTF8", $"String at offset {offset} is not valid UTF-8.", ex);
        }
    }

    /// <summary>
    /// Checks a required invariant and throws a stable validation error when it fails.
    /// </summary>
    /// <param name="length">The number of bytes or elements to process.</param>
    private void Ensure(int length)
    {
        if (length < 0 || Position < 0 || Position > _data.Length - length)
        {
            throw new ToolkitException("BINARY_EOF", $"Need {length} bytes at offset {Position}, input length is {_data.Length}.");
        }
    }
}
