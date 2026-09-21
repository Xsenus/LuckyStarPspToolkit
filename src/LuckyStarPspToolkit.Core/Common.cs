using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LuckyStarPspToolkit;

/// <summary>Controlled exception for malformed, unsupported, or unsafe input.</summary>
public sealed class ToolkitException : Exception
{
    /// <summary>
    /// Initializes a new instance with validated constructor state.
    /// </summary>
    /// <param name="message">The diagnostic message used when validation fails.</param>
    public ToolkitException(string message) : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance with validated constructor state.
    /// </summary>
    /// <param name="message">The diagnostic message used when validation fails.</param>
    /// <param name="innerException">The exception that caused the current failure.</param>
    public ToolkitException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

/// <summary>
/// Represents the toolkit's guard model or service.
/// </summary>
public static class Guard
{
    /// <summary>
    /// Returns a required value or throws a stable validation error when it is absent.
    /// </summary>
    /// <param name="condition">The condition that must be true.</param>
    /// <param name="message">The diagnostic message used when validation fails.</param>
    public static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new ToolkitException(message);
        }
    }

    /// <summary>
    /// Requires range while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="dataLength">The data length value.</param>
    /// <param name="offset">The zero-based byte or element offset.</param>
    /// <param name="length">The number of bytes or elements to process.</param>
    /// <param name="context">A diagnostic label included in validation errors.</param>
    public static void RequireRange(int dataLength, int offset, int length, string context)
    {
        if (offset < 0 || length < 0 || offset > dataLength || length > dataLength - offset)
        {
            throw new ToolkitException(
                $"{context}: range [0x{offset:X}, 0x{(long)offset + length:X}) exceeds data size 0x{dataLength:X}.");
        }
    }
}

/// <summary>
/// Represents the toolkit's binary data model or service.
/// </summary>
public static class BinaryData
{
    /// <summary>
    /// Reads u int 16 little endian while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="data">The binary data to process.</param>
    /// <param name="offset">The zero-based byte or element offset.</param>
    /// <param name="context">A diagnostic label included in validation errors.</param>
    /// <returns>The validated operation result.</returns>
    public static ushort ReadUInt16LittleEndian(ReadOnlySpan<byte> data, int offset, string context)
    {
        Guard.RequireRange(data.Length, offset, sizeof(ushort), context);
        return BinaryPrimitives.ReadUInt16LittleEndian(data[offset..]);
    }

    /// <summary>
    /// Reads u int 32 little endian while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="data">The binary data to process.</param>
    /// <param name="offset">The zero-based byte or element offset.</param>
    /// <param name="context">A diagnostic label included in validation errors.</param>
    /// <returns>The validated operation result.</returns>
    public static uint ReadUInt32LittleEndian(ReadOnlySpan<byte> data, int offset, string context)
    {
        Guard.RequireRange(data.Length, offset, sizeof(uint), context);
        return BinaryPrimitives.ReadUInt32LittleEndian(data[offset..]);
    }

    /// <summary>
    /// Reads int 32 little endian while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="data">The binary data to process.</param>
    /// <param name="offset">The zero-based byte or element offset.</param>
    /// <param name="context">A diagnostic label included in validation errors.</param>
    /// <returns>The validated operation result.</returns>
    public static int ReadInt32LittleEndian(ReadOnlySpan<byte> data, int offset, string context)
    {
        Guard.RequireRange(data.Length, offset, sizeof(int), context);
        return BinaryPrimitives.ReadInt32LittleEndian(data[offset..]);
    }

    /// <summary>
    /// Reads u int 64 little endian while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="data">The binary data to process.</param>
    /// <param name="offset">The zero-based byte or element offset.</param>
    /// <param name="context">A diagnostic label included in validation errors.</param>
    /// <returns>The validated operation result.</returns>
    public static ulong ReadUInt64LittleEndian(ReadOnlySpan<byte> data, int offset, string context)
    {
        Guard.RequireRange(data.Length, offset, sizeof(ulong), context);
        return BinaryPrimitives.ReadUInt64LittleEndian(data[offset..]);
    }

    /// <summary>
    /// Reads u int 16 big endian while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="data">The binary data to process.</param>
    /// <param name="offset">The zero-based byte or element offset.</param>
    /// <param name="context">A diagnostic label included in validation errors.</param>
    /// <returns>The validated operation result.</returns>
    public static ushort ReadUInt16BigEndian(ReadOnlySpan<byte> data, int offset, string context)
    {
        Guard.RequireRange(data.Length, offset, sizeof(ushort), context);
        return BinaryPrimitives.ReadUInt16BigEndian(data[offset..]);
    }

    /// <summary>
    /// Reads u int 32 big endian while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="data">The binary data to process.</param>
    /// <param name="offset">The zero-based byte or element offset.</param>
    /// <param name="context">A diagnostic label included in validation errors.</param>
    /// <returns>The validated operation result.</returns>
    public static uint ReadUInt32BigEndian(ReadOnlySpan<byte> data, int offset, string context)
    {
        Guard.RequireRange(data.Length, offset, sizeof(uint), context);
        return BinaryPrimitives.ReadUInt32BigEndian(data[offset..]);
    }

    /// <summary>
    /// Reads u int 64 big endian while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="data">The binary data to process.</param>
    /// <param name="offset">The zero-based byte or element offset.</param>
    /// <param name="context">A diagnostic label included in validation errors.</param>
    /// <returns>The validated operation result.</returns>
    public static ulong ReadUInt64BigEndian(ReadOnlySpan<byte> data, int offset, string context)
    {
        Guard.RequireRange(data.Length, offset, sizeof(ulong), context);
        return BinaryPrimitives.ReadUInt64BigEndian(data[offset..]);
    }

    /// <summary>
    /// Writes u int 16 little endian while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="data">The binary data to process.</param>
    /// <param name="offset">The zero-based byte or element offset.</param>
    /// <param name="value">The value to process.</param>
    /// <param name="context">A diagnostic label included in validation errors.</param>
    public static void WriteUInt16LittleEndian(Span<byte> data, int offset, ushort value, string context)
    {
        Guard.RequireRange(data.Length, offset, sizeof(ushort), context);
        BinaryPrimitives.WriteUInt16LittleEndian(data[offset..], value);
    }

    /// <summary>
    /// Writes u int 32 little endian while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="data">The binary data to process.</param>
    /// <param name="offset">The zero-based byte or element offset.</param>
    /// <param name="value">The value to process.</param>
    /// <param name="context">A diagnostic label included in validation errors.</param>
    public static void WriteUInt32LittleEndian(Span<byte> data, int offset, uint value, string context)
    {
        Guard.RequireRange(data.Length, offset, sizeof(uint), context);
        BinaryPrimitives.WriteUInt32LittleEndian(data[offset..], value);
    }

    /// <summary>
    /// Returns a bounds-checked view over the requested range.
    /// </summary>
    /// <param name="data">The binary data to process.</param>
    /// <param name="offset">The zero-based byte or element offset.</param>
    /// <param name="length">The number of bytes or elements to process.</param>
    /// <param name="context">A diagnostic label included in validation errors.</param>
    /// <returns>The resulting binary or typed sequence.</returns>
    public static ReadOnlySpan<byte> Slice(ReadOnlySpan<byte> data, int offset, int length, string context)
    {
        Guard.RequireRange(data.Length, offset, length, context);
        return data.Slice(offset, length);
    }

    /// <summary>
    /// Checks a binary signature without reading past a short input buffer.
    /// </summary>
    /// <param name="data">The binary data to process.</param>
    /// <param name="magic">The magic value.</param>
    /// <returns><see langword="true"/> when the condition is satisfied; otherwise <see langword="false"/>.</returns>
    public static bool StartsWith(ReadOnlySpan<byte> data, ReadOnlySpan<byte> magic)
    {
        return data.Length >= magic.Length && data[..magic.Length].SequenceEqual(magic);
    }

    /// <summary>
    /// Encodes an ASCII signature and compares it to the beginning of the input bytes.
    /// </summary>
    /// <param name="data">The binary data to process.</param>
    /// <param name="magic">The magic value.</param>
    /// <returns><see langword="true"/> when the condition is satisfied; otherwise <see langword="false"/>.</returns>
    public static bool StartsWithAscii(ReadOnlySpan<byte> data, string magic)
    {
        return StartsWith(data, Encoding.ASCII.GetBytes(magic));
    }

    /// <summary>
    /// Determines whether all zero.
    /// </summary>
    /// <param name="data">The binary data to process.</param>
    /// <returns><see langword="true"/> when the condition is satisfied; otherwise <see langword="false"/>.</returns>
    public static bool IsAllZero(ReadOnlySpan<byte> data)
    {
        foreach (byte value in data)
        {
            if (value != 0)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Reads null terminated UTF 8 while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="data">The binary data to process.</param>
    /// <returns>The resulting text, path, identifier, or hexadecimal digest.</returns>
    public static string ReadNullTerminatedUtf8(ReadOnlySpan<byte> data)
    {
        int zero = data.IndexOf((byte)0);
        ReadOnlySpan<byte> text = zero >= 0 ? data[..zero] : data;
        try
        {
            return new UTF8Encoding(false, true).GetString(text);
        }
        catch (DecoderFallbackException exception)
        {
            throw new ToolkitException("Input contains invalid UTF-8 text.", exception);
        }
    }

    /// <summary>
    /// Reads null terminated ASCII while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="data">The binary data to process.</param>
    /// <returns>The resulting text, path, identifier, or hexadecimal digest.</returns>
    public static string ReadNullTerminatedAscii(ReadOnlySpan<byte> data)
    {
        int zero = data.IndexOf((byte)0);
        ReadOnlySpan<byte> text = zero >= 0 ? data[..zero] : data;
        return Encoding.ASCII.GetString(text);
    }

    /// <summary>
    /// Rounds a non-negative value up to the requested positive alignment.
    /// </summary>
    /// <param name="value">The value to process.</param>
    /// <param name="alignment">The required positive alignment in bytes.</param>
    /// <returns>The validated operation result.</returns>
    public static int AlignUp(int value, int alignment)
    {
        Guard.Require(value >= 0, "Alignment input cannot be negative.");
        Guard.Require(alignment > 0, "Alignment must be positive.");
        int remainder = value % alignment;
        if (remainder == 0)
        {
            return value;
        }

        return checked(value + alignment - remainder);
    }

    /// <summary>
    /// Determines whether ASCII.
    /// </summary>
    /// <param name="data">The binary data to process.</param>
    /// <param name="text">The text to process.</param>
    /// <returns><see langword="true"/> when the condition is satisfied; otherwise <see langword="false"/>.</returns>
    public static bool ContainsAscii(ReadOnlySpan<byte> data, string text)
    {
        byte[] needle = Encoding.ASCII.GetBytes(text);
        return data.IndexOf(needle) >= 0;
    }
}

/// <summary>
/// Provides the toolkit's crypto utilities workflow.
/// </summary>
public static class CryptoUtilities
{
    /// <summary>
    /// Computes the 20-byte SHA-1 digest required by the PSP PRX envelope format.
    /// </summary>
    /// <param name="data">The binary data to process.</param>
    /// <returns>The resulting binary or typed sequence.</returns>
    public static byte[] Sha1(ReadOnlySpan<byte> data) => SHA1.HashData(data);

    /// <summary>
    /// Computes a 32-byte SHA-256 fingerprint for revision and output integrity checks.
    /// </summary>
    /// <param name="data">The binary data to process.</param>
    /// <returns>The resulting binary or typed sequence.</returns>
    public static byte[] Sha256(ReadOnlySpan<byte> data) => SHA256.HashData(data);

    /// <summary>
    /// Computes the lowercase SHA-256 hexadecimal digest of the supplied bytes.
    /// </summary>
    /// <param name="data">The binary data to process.</param>
    /// <returns>The resulting text, path, identifier, or hexadecimal digest.</returns>
    public static string Sha256Hex(ReadOnlySpan<byte> data) => HexUtilities.ToHex(Sha256(data));

    /// <summary>
    /// Encrypts complete 16-byte blocks with AES-128-CBC, a zero IV and no padding.
    /// </summary>
    /// <param name="plaintext">The plaintext value.</param>
    /// <param name="key">The key value.</param>
    /// <returns>The resulting binary or typed sequence.</returns>
    public static byte[] Aes128CbcEncrypt(ReadOnlySpan<byte> plaintext, ReadOnlySpan<byte> key)
    {
        return Aes128CbcTransform(plaintext, key, encrypt: true);
    }

    /// <summary>
    /// Decrypts complete 16-byte blocks with AES-128-CBC, a zero IV and no padding.
    /// </summary>
    /// <param name="ciphertext">The ciphertext value.</param>
    /// <param name="key">The key value.</param>
    /// <returns>The resulting binary or typed sequence.</returns>
    public static byte[] Aes128CbcDecrypt(ReadOnlySpan<byte> ciphertext, ReadOnlySpan<byte> key)
    {
        return Aes128CbcTransform(ciphertext, key, encrypt: false);
    }

    /// <summary>
    /// Authenticates a byte sequence with AES-CMAC, including empty and partial final blocks.
    /// </summary>
    /// <param name="message">The diagnostic message used when validation fails.</param>
    /// <param name="key">The key value.</param>
    /// <returns>The resulting binary or typed sequence.</returns>
    public static byte[] Aes128Cmac(ReadOnlySpan<byte> message, ReadOnlySpan<byte> key)
    {
        Guard.Require(key.Length == 16, "AES-CMAC requires a 16-byte key.");

        byte[] zero = new byte[16];
        byte[] encryptedZero = Aes128CbcEncrypt(zero, key);
        byte[] k1 = DeriveCmacSubkey(encryptedZero);
        byte[] k2 = DeriveCmacSubkey(k1);

        bool completeLastBlock = message.Length > 0 && message.Length % 16 == 0;
        int blockCount = message.Length == 0 ? 1 : checked((message.Length + 15) / 16);
        byte[] transformed = new byte[checked(blockCount * 16)];
        message.CopyTo(transformed);

        int lastBlockOffset = transformed.Length - 16;
        ReadOnlySpan<byte> subkey = completeLastBlock ? k1 : k2;
        if (!completeLastBlock)
        {
            transformed[message.Length] = 0x80;
        }

        for (int index = 0; index < 16; index++)
        {
            transformed[lastBlockOffset + index] ^= subkey[index];
        }

        byte[] encrypted = Aes128CbcEncrypt(transformed, key);
        return encrypted[^16..];
    }

    /// <summary>
    /// Validates the AES key and block lengths, then performs the requested zero-IV CBC transform.
    /// </summary>
    /// <param name="input">The input binary data or object.</param>
    /// <param name="key">The key value.</param>
    /// <param name="encrypt">The encrypt value.</param>
    /// <returns>The resulting binary or typed sequence.</returns>
    private static byte[] Aes128CbcTransform(ReadOnlySpan<byte> input, ReadOnlySpan<byte> key, bool encrypt)
    {
        Guard.Require(key.Length == 16, "AES-128 requires a 16-byte key.");
        Guard.Require(input.Length % 16 == 0, "AES-128-CBC input length must be a multiple of 16 bytes.");

        using Aes aes = Aes.Create();
        aes.KeySize = 128;
        aes.BlockSize = 128;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.None;
        aes.Key = key.ToArray();
        aes.IV = new byte[16];

        using ICryptoTransform transform = encrypt ? aes.CreateEncryptor() : aes.CreateDecryptor();
        byte[] source = input.ToArray();
        return transform.TransformFinalBlock(source, 0, source.Length);
    }

    /// <summary>
    /// Derives CMAC subkey while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="input">The input binary data or object.</param>
    /// <returns>The resulting binary or typed sequence.</returns>
    private static byte[] DeriveCmacSubkey(ReadOnlySpan<byte> input)
    {
        Guard.Require(input.Length == 16, "AES-CMAC subkey input must be one block.");
        byte[] output = new byte[16];
        byte carry = 0;
        for (int index = 15; index >= 0; index--)
        {
            byte nextCarry = (byte)((input[index] & 0x80) != 0 ? 1 : 0);
            output[index] = (byte)((input[index] << 1) | carry);
            carry = nextCarry;
        }

        if ((input[0] & 0x80) != 0)
        {
            output[15] ^= 0x87;
        }

        return output;
    }
}

/// <summary>
/// Provides the toolkit's hex utilities workflow.
/// </summary>
public static class HexUtilities
{
    /// <summary>
    /// Converts hex while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="data">The binary data to process.</param>
    /// <returns>The resulting text, path, identifier, or hexadecimal digest.</returns>
    public static string ToHex(ReadOnlySpan<byte> data) => Convert.ToHexString(data).ToLowerInvariant();

    /// <summary>
    /// Formats a 32-bit value as a fixed-width uppercase hexadecimal word with a 0x prefix.
    /// </summary>
    /// <param name="value">The value to process.</param>
    /// <returns>The resulting text, path, identifier, or hexadecimal digest.</returns>
    public static string UInt32(uint value) => $"0x{value:X8}";

    /// <summary>
    /// Formats a byte offset as an eight-digit uppercase hexadecimal value with a 0x prefix.
    /// </summary>
    /// <param name="value">The value to process.</param>
    /// <returns>The resulting text, path, identifier, or hexadecimal digest.</returns>
    public static string Offset(int value) => $"0x{value:X8}";

    /// <summary>
    /// Parses validated input into the current binary-format model.
    /// </summary>
    /// <param name="value">The value to process.</param>
    /// <returns>The resulting binary or typed sequence.</returns>
    /// <remarks>Malformed input or violated preconditions are rejected before a persistent output is committed.</remarks>
    public static byte[] Parse(string value)
    {
        try
        {
            return Convert.FromHexString(value);
        }
        catch (FormatException exception)
        {
            throw new ToolkitException($"Invalid hexadecimal string: {value}", exception);
        }
    }
}

/// <summary>
/// Represents the toolkit's atomic file model or service.
/// </summary>
public static class AtomicFile
{
    /// <summary>
    /// Writes all bytes while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="path">The file-system path to process.</param>
    /// <param name="data">The binary data to process.</param>
    public static void WriteAllBytes(string path, ReadOnlySpan<byte> data)
    {
        string fullPath = Path.GetFullPath(path);
        string directory = Path.GetDirectoryName(fullPath)
            ?? throw new ToolkitException($"Cannot determine output directory for {path}.");
        Directory.CreateDirectory(directory);
        string temporaryPath = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");

        try
        {
            using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 64 * 1024,
                options: FileOptions.WriteThrough))
            {
                stream.Write(data);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, fullPath, overwrite: true);
        }
        catch
        {
            TryDelete(temporaryPath);
            throw;
        }
    }

    /// <summary>
    /// Writes all text while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="path">The file-system path to process.</param>
    /// <param name="text">The text to process.</param>
    public static void WriteAllText(string path, string text)
    {
        WriteAllBytes(path, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(text));
    }

    /// <summary>
    /// Attempts to delete.
    /// </summary>
    /// <param name="path">The file-system path to process.</param>
    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Preserve the original exception. A stale temp file is harmless and identifiable.
        }
    }
}

/// <summary>
/// Represents the toolkit's JSON defaults model or service.
/// </summary>
public static class JsonDefaults
{
    /// <summary>
    /// Creates indented while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <returns>The validated operation result.</returns>
    public static JsonSerializerOptions CreateIndented()
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.KebabCaseLower));
        return options;
    }

    /// <summary>
    /// Serializes a value to stable UTF-8 JSON for reports or workspaces.
    /// </summary>
    /// <typeparam name="T">The t type used by the operation.</typeparam>
    /// <param name="value">The value to process.</param>
    /// <returns>The resulting text, path, identifier, or hexadecimal digest.</returns>
    public static string Serialize<T>(T value)
    {
        return JsonSerializer.Serialize(value, CreateIndented()) + Environment.NewLine;
    }
}
