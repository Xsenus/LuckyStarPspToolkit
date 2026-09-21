using System.Collections.ObjectModel;
using System.Text;
using System.Text.Json.Serialization;

namespace LuckyStarPspToolkit;

/// <summary>
/// Defines the supported SFO value kind values.
/// </summary>
public enum SfoValueKind
{
    /// <summary>The utf8 string value used by this model or operation.</summary>
    Utf8String,
    /// <summary>The uint32 value used by this model or operation.</summary>
    UInt32,
    /// <summary>The bytes value used by this model or operation.</summary>
    Bytes
}

/// <summary>
/// Represents the toolkit's SFO entry model or service.
/// </summary>
public sealed class SfoEntry
{
    /// <summary>The key value used by this model or operation.</summary>
    public required string Key { get; init; }
    /// <summary>The format value used by this model or operation.</summary>
    public required ushort Format { get; init; }
    /// <summary>The length value used by this model or operation.</summary>
    public required uint Length { get; init; }
    /// <summary>The upper safety limit for length; input above it is rejected.</summary>
    public required uint MaximumLength { get; init; }
    /// <summary>The kind value used by this model or operation.</summary>
    public required SfoValueKind Kind { get; init; }
    /// <summary>The string value value used by this model or operation.</summary>
    public string? StringValue { get; init; }
    /// <summary>The uint32 value value used by this model or operation.</summary>
    public uint? UInt32Value { get; init; }
    /// <summary>The hex value value used by this model or operation.</summary>
    public string? HexValue { get; init; }

    [JsonIgnore]
    /// <summary>The display value value used by this model or operation.</summary>
    public string DisplayValue => Kind switch
    {
        SfoValueKind.Utf8String => StringValue ?? string.Empty,
        SfoValueKind.UInt32 => UInt32Value?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
        _ => HexValue ?? string.Empty
    };
}

/// <summary>
/// Represents the toolkit's SFO file model or service.
/// </summary>
public sealed class SfoFile
{
    /// <summary>Stores the by key state owned by this instance or type.</summary>
    private readonly IReadOnlyDictionary<string, SfoEntry> _byKey;

    /// <summary>
    /// Initializes a new instance with validated constructor state.
    /// </summary>
    /// <param name="version">The version value.</param>
    /// <param name="entries">The entries value.</param>
    public SfoFile(uint version, IReadOnlyList<SfoEntry> entries)
    {
        Version = version;
        Entries = entries;
        _byKey = new ReadOnlyDictionary<string, SfoEntry>(
            entries.ToDictionary(entry => entry.Key, StringComparer.Ordinal));
    }

    /// <summary>The version used in CLI reports and release metadata.</summary>
    public uint Version { get; }
    /// <summary>The entries value used by this model or operation.</summary>
    public IReadOnlyList<SfoEntry> Entries { get; }

    /// <summary>
    /// Looks up an exact PARAM.SFO key and returns null when the key is absent.
    /// </summary>
    /// <param name="key">The key value.</param>
    /// <returns>The validated operation result.</returns>
    public SfoEntry? Find(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return _byKey.GetValueOrDefault(key);
    }

    /// <summary>
    /// Gets string while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="key">The key value.</param>
    /// <returns>The resulting text, path, identifier, or hexadecimal digest.</returns>
    public string? GetString(string key) => Find(key)?.StringValue;

    /// <summary>
    /// Gets u int 32 while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="key">The key value.</param>
    /// <returns>The validated operation result.</returns>
    public uint? GetUInt32(string key) => Find(key)?.UInt32Value;
}

/// <summary>
/// Provides SFO reader operations with strict bounds and format validation.
/// </summary>
public static class SfoReader
{
    /// <summary>The fixed magic value used by this format or revision.</summary>
    private const uint Magic = 0x46535000;
    /// <summary>The fixed header size value used by this format or revision.</summary>
    private const int HeaderSize = 20;
    /// <summary>The fixed index entry size value used by this format or revision.</summary>
    private const int IndexEntrySize = 16;
    /// <summary>The fixed utf8 format value used by this format or revision.</summary>
    private const ushort Utf8Format = 0x0204;
    /// <summary>The fixed uint32 format value used by this format or revision.</summary>
    private const ushort UInt32Format = 0x0404;
    /// <summary>The upper safety limit for entry count; input above it is rejected.</summary>
    private const int MaximumEntryCount = 4096;

    /// <summary>The strict utf8 value used by this model or operation.</summary>
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    /// <summary>
    /// Determines whether the supplied data matches the expected format signature.
    /// </summary>
    /// <param name="data">The binary data to process.</param>
    /// <returns><see langword="true"/> when the condition is satisfied; otherwise <see langword="false"/>.</returns>
    public static bool LooksLike(ReadOnlySpan<byte> data)
    {
        return data.Length >= sizeof(uint)
            && BinaryData.ReadUInt32LittleEndian(data, 0, "SFO magic") == Magic;
    }

    /// <summary>
    /// Parses validated input into the current binary-format model.
    /// </summary>
    /// <param name="data">The binary data to process.</param>
    /// <returns>The validated operation result.</returns>
    /// <remarks>Malformed input or violated preconditions are rejected before a persistent output is committed.</remarks>
    public static SfoFile Parse(ReadOnlySpan<byte> data)
    {
        Guard.RequireRange(data.Length, 0, HeaderSize, "SFO header");
        Guard.Require(BinaryData.ReadUInt32LittleEndian(data, 0, "SFO magic") == Magic,
            "Not a PARAM.SFO file: invalid magic.");

        uint version = BinaryData.ReadUInt32LittleEndian(data, 4, "SFO version");
        int keyTableOffset = ToInt32(
            BinaryData.ReadUInt32LittleEndian(data, 8, "SFO key-table offset"),
            "SFO key-table offset");
        int dataTableOffset = ToInt32(
            BinaryData.ReadUInt32LittleEndian(data, 12, "SFO data-table offset"),
            "SFO data-table offset");
        int entryCount = ToInt32(
            BinaryData.ReadUInt32LittleEndian(data, 16, "SFO entry count"),
            "SFO entry count");

        Guard.Require(entryCount <= MaximumEntryCount,
            $"SFO declares an unreasonable number of entries ({entryCount}).");
        int indexEnd = checked(HeaderSize + entryCount * IndexEntrySize);
        Guard.Require(indexEnd <= data.Length, "SFO index table exceeds the input file.");
        Guard.Require(keyTableOffset >= indexEnd && keyTableOffset <= data.Length,
            "SFO key-table offset is invalid.");
        Guard.Require(dataTableOffset >= keyTableOffset && dataTableOffset <= data.Length,
            "SFO data-table offset is invalid.");

        var entries = new List<SfoEntry>(entryCount);
        var keys = new HashSet<string>(StringComparer.Ordinal);
        for (int index = 0; index < entryCount; index++)
        {
            int offset = checked(HeaderSize + index * IndexEntrySize);
            int keyRelative = BinaryData.ReadUInt16LittleEndian(data, offset, "SFO key offset");
            ushort format = BinaryData.ReadUInt16LittleEndian(data, offset + 2, "SFO value format");
            uint length = BinaryData.ReadUInt32LittleEndian(data, offset + 4, "SFO value length");
            uint maximumLength = BinaryData.ReadUInt32LittleEndian(data, offset + 8, "SFO maximum value length");
            int dataRelative = ToInt32(
                BinaryData.ReadUInt32LittleEndian(data, offset + 12, "SFO data offset"),
                "SFO data offset");

            Guard.Require(length <= maximumLength,
                $"SFO entry {index} value length exceeds its declared maximum.");
            Guard.Require(keyRelative <= dataTableOffset - keyTableOffset,
                $"SFO entry {index} key offset exceeds the key table.");
            Guard.Require(dataRelative <= data.Length - dataTableOffset,
                $"SFO entry {index} data offset exceeds the file.");

            int keyOffset = checked(keyTableOffset + keyRelative);
            string key = ReadKey(data, keyOffset, dataTableOffset);
            Guard.Require(keys.Add(key), $"SFO contains duplicate key '{key}'.");

            int valueLength = ToInt32(length, $"SFO value length for '{key}'");
            int valueOffset = checked(dataTableOffset + dataRelative);
            Guard.RequireRange(data.Length, valueOffset, valueLength, $"SFO value '{key}'");
            ReadOnlySpan<byte> raw = data.Slice(valueOffset, valueLength);

            entries.Add(ParseEntry(key, format, length, maximumLength, raw));
        }

        return new SfoFile(version, entries.AsReadOnly());
    }

    /// <summary>
    /// Parses entry while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="key">The key value.</param>
    /// <param name="format">The format value.</param>
    /// <param name="length">The number of bytes or elements to process.</param>
    /// <param name="maximumLength">The maximum length value.</param>
    /// <param name="raw">The raw value.</param>
    /// <returns>The validated operation result.</returns>
    /// <remarks>Malformed input or violated preconditions are rejected before a persistent output is committed.</remarks>
    private static SfoEntry ParseEntry(
        string key,
        ushort format,
        uint length,
        uint maximumLength,
        ReadOnlySpan<byte> raw)
    {
        if (format == Utf8Format)
        {
            int zero = raw.IndexOf((byte)0);
            ReadOnlySpan<byte> text = zero >= 0 ? raw[..zero] : raw;
            string value;
            try
            {
                value = StrictUtf8.GetString(text);
            }
            catch (DecoderFallbackException exception)
            {
                throw new ToolkitException($"SFO value '{key}' is not valid UTF-8.", exception);
            }

            return new SfoEntry
            {
                Key = key,
                Format = format,
                Length = length,
                MaximumLength = maximumLength,
                Kind = SfoValueKind.Utf8String,
                StringValue = value
            };
        }

        if (format == UInt32Format)
        {
            Guard.Require(raw.Length >= sizeof(uint),
                $"SFO integer value '{key}' is shorter than four bytes.");
            return new SfoEntry
            {
                Key = key,
                Format = format,
                Length = length,
                MaximumLength = maximumLength,
                Kind = SfoValueKind.UInt32,
                UInt32Value = BinaryData.ReadUInt32LittleEndian(raw, 0, $"SFO integer '{key}'")
            };
        }

        return new SfoEntry
        {
            Key = key,
            Format = format,
            Length = length,
            MaximumLength = maximumLength,
            Kind = SfoValueKind.Bytes,
            HexValue = HexUtilities.ToHex(raw)
        };
    }

    /// <summary>
    /// Reads key while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="data">The binary data to process.</param>
    /// <param name="offset">The zero-based byte or element offset.</param>
    /// <param name="limit">The limit value.</param>
    /// <returns>The resulting text, path, identifier, or hexadecimal digest.</returns>
    private static string ReadKey(ReadOnlySpan<byte> data, int offset, int limit)
    {
        Guard.Require(offset >= 0 && offset < limit && limit <= data.Length,
            "SFO key offset points outside the key table.");
        int length = 0;
        while (offset + length < limit && data[offset + length] != 0)
        {
            length++;
        }

        Guard.Require(offset + length < limit,
            "SFO key is not NUL-terminated inside the key table.");
        ReadOnlySpan<byte> key = data.Slice(offset, length);
        foreach (byte value in key)
        {
            Guard.Require(value is >= 0x20 and <= 0x7E,
                "SFO key contains a non-ASCII character.");
        }

        return Encoding.ASCII.GetString(key);
    }

    /// <summary>
    /// Converts int 32 while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="value">The value to process.</param>
    /// <param name="context">A diagnostic label included in validation errors.</param>
    /// <returns>The validated operation result.</returns>
    private static int ToInt32(uint value, string context)
    {
        Guard.Require(value <= int.MaxValue, $"{context} is too large.");
        return (int)value;
    }
}
