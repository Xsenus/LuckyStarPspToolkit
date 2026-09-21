using System.Security.Cryptography;
using System.Text;

namespace LuckyStarPspToolkit.Formats.Common;

/// <summary>
/// Provides the toolkit's binary utilities workflow.
/// </summary>
public static class BinaryUtilities
{
    /// <summary>
    /// Rounds a non-negative value up to the requested positive alignment.
    /// </summary>
    /// <param name="value">The value to process.</param>
    /// <param name="alignment">The required positive alignment in bytes.</param>
    /// <returns>The validated operation result.</returns>
    public static int Align(int value, int alignment)
    {
        if (value < 0 || alignment <= 0)
        {
            throw new ArgumentOutOfRangeException(value < 0 ? nameof(value) : nameof(alignment));
        }

        int remainder = value % alignment;
        return remainder == 0 ? value : checked(value + alignment - remainder);
    }

    /// <summary>
    /// Rounds a non-negative value up to the requested positive alignment.
    /// </summary>
    /// <param name="value">The value to process.</param>
    /// <param name="alignment">The required positive alignment in bytes.</param>
    /// <returns>The validated operation result.</returns>
    public static long Align(long value, long alignment)
    {
        if (value < 0 || alignment <= 0)
        {
            throw new ArgumentOutOfRangeException(value < 0 ? nameof(value) : nameof(alignment));
        }

        long remainder = value % alignment;
        return remainder == 0 ? value : checked(value + alignment - remainder);
    }

    /// <summary>
    /// Computes the lowercase SHA-256 hexadecimal digest of the supplied bytes.
    /// </summary>
    /// <param name="data">The binary data to process.</param>
    /// <returns>The resulting text, path, identifier, or hexadecimal digest.</returns>
    public static string Sha256Hex(ReadOnlySpan<byte> data) => Convert.ToHexStringLower(SHA256.HashData(data));

    /// <summary>
    /// Streams a protected file through SHA-256 and rejects a length change during hashing.
    /// </summary>
    /// <param name="path">The file-system path to process.</param>
    /// <returns>The resulting text, path, identifier, or hexadecimal digest.</returns>
    public static string Sha256HexFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string normalized = PathUtilities.NormalizeProtectedPath(path, "FILE_REPARSE_POINT");
        using FileStream stream = new(
            normalized,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            1024 * 1024,
            FileOptions.SequentialScan);
        long originalLength = stream.Length;
        string digest = Convert.ToHexStringLower(SHA256.HashData(stream));
        if (stream.Length != originalLength)
        {
            throw new ToolkitException(
                "FILE_CHANGED",
                $"File changed length while SHA-256 was being computed: {normalized}");
        }
        return digest;
    }

    /// <summary>
    /// Parses hex while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="value">The value to process.</param>
    /// <returns>The resulting binary or typed sequence.</returns>
    /// <remarks>Malformed input or violated preconditions are rejected before a persistent output is committed.</remarks>
    public static byte[] ParseHex(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        string normalized = value.Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal);
        try
        {
            return Convert.FromHexString(normalized);
        }
        catch (FormatException ex)
        {
            throw new ToolkitException("HEX_FORMAT", $"Invalid hexadecimal value: {value}.", ex);
        }
    }

    /// <summary>
    /// Reads a regular file once while enforcing configured size and reparse-point protections.
    /// </summary>
    /// <param name="path">The file-system path to process.</param>
    /// <param name="limits">Optional conservative safety limits; defaults are used when omitted.</param>
    /// <returns>The resulting binary or typed sequence.</returns>
    public static byte[] ReadAllBytesBounded(string path, FileLimits? limits = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        limits ??= FileLimits.Default;
        string normalized = PathUtilities.NormalizeProtectedPath(path, "FILE_REPARSE_POINT");
        if (!File.Exists(normalized))
        {
            throw new ToolkitException("FILE_NOT_FOUND", $"File not found: {normalized}");
        }

        using FileStream stream = new(
            normalized,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            1024 * 1024,
            FileOptions.SequentialScan);
        long originalLength = stream.Length;
        if (originalLength > limits.MaximumInputBytes || originalLength > int.MaxValue)
        {
            throw new ToolkitException(
                "FILE_TOO_LARGE",
                $"File is too large for an in-memory operation: {originalLength} bytes.");
        }

        byte[] data = new byte[checked((int)originalLength)];
        try
        {
            stream.ReadExactly(data);
        }
        catch (EndOfStreamException ex)
        {
            throw new ToolkitException(
                "FILE_CHANGED",
                $"File was truncated while it was being read: {normalized}",
                ex);
        }
        if (stream.Length != originalLength)
        {
            throw new ToolkitException(
                "FILE_CHANGED",
                $"File changed length while it was being read: {normalized}");
        }
        return data;
    }

    /// <summary>
    /// Encodes text as strict UTF-8 without a byte-order mark.
    /// </summary>
    /// <param name="value">The value to process.</param>
    /// <returns>The resulting binary or typed sequence.</returns>
    public static byte[] Utf8(string value) => new UTF8Encoding(false, true).GetBytes(value);
}
