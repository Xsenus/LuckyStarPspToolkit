using System.Buffers;
using System.Security.Cryptography;

namespace LuckyStarPspToolkit.Formats.Iso;

/// <summary>
/// Represents the toolkit's seekable data source model or service.
/// </summary>
internal sealed class SeekableDataSource : IDisposable
{
    /// <summary>Stores the stream state owned by this instance or type.</summary>
    private readonly Stream _stream;
    /// <summary>Stores the leave open state owned by this instance or type.</summary>
    private readonly bool _leaveOpen;
    /// <summary>Stores the gate state owned by this instance or type.</summary>
    private readonly object _gate = new();

    /// <summary>
    /// Initializes a new instance with validated constructor state.
    /// </summary>
    /// <param name="stream">The readable and seekable stream to process.</param>
    /// <param name="leaveOpen">Whether ownership of the supplied stream remains with the caller.</param>
    private SeekableDataSource(Stream stream, bool leaveOpen)
    {
        _stream = stream;
        _leaveOpen = leaveOpen;
    }

    /// <summary>The length value used by this model or operation.</summary>
    public long Length => _stream.Length;

    /// <summary>
    /// Opens file while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="path">The file-system path to process.</param>
    /// <returns>The validated operation result.</returns>
    public static SeekableDataSource OpenFile(string path)
    {
        FileStream stream = new(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            1024 * 1024,
            FileOptions.RandomAccess);
        return new SeekableDataSource(stream, false);
    }

    /// <summary>
    /// Creates memory while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="data">The binary data to process.</param>
    /// <returns>The validated operation result.</returns>
    public static SeekableDataSource FromMemory(ReadOnlyMemory<byte> data)
        => new(new MemoryStream(data.ToArray(), writable: false), false);

    /// <summary>
    /// Creates stream while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="stream">The readable and seekable stream to process.</param>
    /// <param name="leaveOpen">Whether ownership of the supplied stream remains with the caller.</param>
    /// <returns>The validated operation result.</returns>
    public static SeekableDataSource FromStream(Stream stream, bool leaveOpen)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanRead || !stream.CanSeek)
        {
            throw new Common.ToolkitException(
                "ISO_STREAM_CAPABILITIES",
                "ISO stream must support reading and seeking.");
        }
        return new SeekableDataSource(stream, leaveOpen);
    }

    /// <summary>
    /// Reads exactly while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="offset">The zero-based byte or element offset.</param>
    /// <param name="destination">The destination stream or buffer.</param>
    /// <param name="context">A diagnostic label included in validation errors.</param>
    public void ReadExactly(long offset, Span<byte> destination, string context)
    {
        if (offset < 0 || offset > Length || destination.Length > Length - offset)
        {
            throw new Common.ToolkitException(
                "ISO_RANGE",
                $"{context} range [0x{offset:X},0x{offset + destination.Length:X}) exceeds source length 0x{Length:X}.");
        }

        lock (_gate)
        {
            _stream.Position = offset;
            try
            {
                _stream.ReadExactly(destination);
            }
            catch (EndOfStreamException ex)
            {
                throw new Common.ToolkitException("ISO_TRUNCATED", $"Unexpected end of source while reading {context}.", ex);
            }
        }
    }

    /// <summary>
    /// Copies range to while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="offset">The zero-based byte or element offset.</param>
    /// <param name="count">The number of items to process.</param>
    /// <param name="destination">The destination stream or buffer.</param>
    /// <param name="hash">An optional incremental hash updated with copied bytes.</param>
    public void CopyRangeTo(
        long offset,
        long count,
        Stream destination,
        IncrementalHash? hash = null)
    {
        ArgumentNullException.ThrowIfNull(destination);
        if (offset < 0 || count < 0 || offset > Length || count > Length - offset)
        {
            throw new Common.ToolkitException(
                "ISO_RANGE",
                $"Copy range [0x{offset:X},0x{offset + count:X}) exceeds source length 0x{Length:X}.");
        }

        byte[] buffer = ArrayPool<byte>.Shared.Rent(1024 * 1024);
        try
        {
            lock (_gate)
            {
                _stream.Position = offset;
                long remaining = count;
                while (remaining > 0)
                {
                    int requested = (int)Math.Min(buffer.Length, remaining);
                    int read = _stream.Read(buffer, 0, requested);
                    if (read <= 0)
                    {
                        throw new Common.ToolkitException("ISO_TRUNCATED", "Unexpected end of source while copying an ISO extent.");
                    }
                    destination.Write(buffer, 0, read);
                    hash?.AppendData(buffer, 0, read);
                    remaining -= read;
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
        }
    }

    /// <summary>
    /// Computes SHA 256 while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <returns>The resulting text, path, identifier, or hexadecimal digest.</returns>
    public string ComputeSha256()
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        CopyRangeTo(0, Length, Stream.Null, hash);
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    /// <summary>
    /// Releases owned streams and other disposable resources.
    /// </summary>
    public void Dispose()
    {
        if (!_leaveOpen)
        {
            _stream.Dispose();
        }
    }
}
