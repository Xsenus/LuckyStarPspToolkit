using System.Buffers.Binary;

namespace LuckyStarPspToolkit.Formats.Common;

/// <summary>
/// Provides binary buffer writer operations with strict bounds and format validation.
/// </summary>
internal sealed class BinaryBufferWriter
{
    /// <summary>Stores the buffer state owned by this instance or type.</summary>
    private readonly byte[] _buffer;

    /// <summary>
    /// Initializes a new instance with validated constructor state.
    /// </summary>
    /// <param name="length">The number of bytes or elements to process.</param>
    public BinaryBufferWriter(int length)
    {
        if (length < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(length));
        }

        _buffer = new byte[length];
    }

    /// <summary>The position value used by this model or operation.</summary>
    public int Position { get; set; }
    /// <summary>The length value used by this model or operation.</summary>
    public int Length => _buffer.Length;
    /// <summary>The buffer value used by this model or operation.</summary>
    public byte[] Buffer => _buffer;

    /// <summary>
    /// Writes byte while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="value">The value to process.</param>
    public void WriteByte(byte value)
    {
        Ensure(Position, 1);
        _buffer[Position++] = value;
    }

    /// <summary>
    /// Writes u int 16 little endian while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="value">The value to process.</param>
    public void WriteUInt16LittleEndian(ushort value)
    {
        Ensure(Position, 2);
        BinaryPrimitives.WriteUInt16LittleEndian(_buffer.AsSpan(Position), value);
        Position += 2;
    }

    /// <summary>
    /// Writes u int 16 big endian while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="value">The value to process.</param>
    public void WriteUInt16BigEndian(ushort value)
    {
        Ensure(Position, 2);
        BinaryPrimitives.WriteUInt16BigEndian(_buffer.AsSpan(Position), value);
        Position += 2;
    }

    /// <summary>
    /// Writes u int 32 little endian while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="value">The value to process.</param>
    public void WriteUInt32LittleEndian(uint value)
    {
        Ensure(Position, 4);
        BinaryPrimitives.WriteUInt32LittleEndian(_buffer.AsSpan(Position), value);
        Position += 4;
    }

    /// <summary>
    /// Writes u int 32 big endian while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="value">The value to process.</param>
    public void WriteUInt32BigEndian(uint value)
    {
        Ensure(Position, 4);
        BinaryPrimitives.WriteUInt32BigEndian(_buffer.AsSpan(Position), value);
        Position += 4;
    }

    /// <summary>
    /// Writes u int 64 little endian while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="value">The value to process.</param>
    public void WriteUInt64LittleEndian(ulong value)
    {
        Ensure(Position, 8);
        BinaryPrimitives.WriteUInt64LittleEndian(_buffer.AsSpan(Position), value);
        Position += 8;
    }

    /// <summary>
    /// Writes u int 64 big endian while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="value">The value to process.</param>
    public void WriteUInt64BigEndian(ulong value)
    {
        Ensure(Position, 8);
        BinaryPrimitives.WriteUInt64BigEndian(_buffer.AsSpan(Position), value);
        Position += 8;
    }

    /// <summary>
    /// Writes bytes while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="data">The binary data to process.</param>
    public void WriteBytes(ReadOnlySpan<byte> data)
    {
        WriteBytesAt(Position, data);
        Position += data.Length;
    }

    /// <summary>
    /// Writes bytes at while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="offset">The zero-based byte or element offset.</param>
    /// <param name="data">The binary data to process.</param>
    public void WriteBytesAt(int offset, ReadOnlySpan<byte> data)
    {
        Ensure(offset, data.Length);
        data.CopyTo(_buffer.AsSpan(offset, data.Length));
    }

    /// <summary>
    /// Checks a required invariant and throws a stable validation error when it fails.
    /// </summary>
    /// <param name="offset">The zero-based byte or element offset.</param>
    /// <param name="length">The number of bytes or elements to process.</param>
    private void Ensure(int offset, int length)
    {
        if (offset < 0 || length < 0 || offset > _buffer.Length - length)
        {
            throw new ToolkitException("WRITE_RANGE", $"Binary write [{offset}, {offset + length}) exceeds {_buffer.Length} bytes.");
        }
    }
}
