using System.Globalization;
using System.Text;
using LuckyStarPspToolkit.Formats.Common;

namespace LuckyStarPspToolkit.Formats.Text;

/// <summary>
/// Represents immutable glyph duplicate data exchanged by the toolkit.
/// </summary>
/// <param name="Text">The decoded string value associated with this record.</param>
/// <param name="Indices">The indices value used by this model or operation.</param>
public sealed record GlyphDuplicate(string Text, IReadOnlyList<ushort> Indices);

/// <summary>
/// Represents immutable glyph map analysis data exchanged by the toolkit.
/// </summary>
/// <param name="EntryCount">The entry count value used by this model or operation.</param>
/// <param name="EncodableEntryCount">The encodable entry count value used by this model or operation.</param>
/// <param name="EmptyEntryCount">The empty entry count value used by this model or operation.</param>
/// <param name="ReservedIndexEntryCount">The reserved index entry count value used by this model or operation.</param>
/// <param name="Duplicates">The duplicates value used by this model or operation.</param>
/// <param name="Warnings">The warnings value used by this model or operation.</param>
public sealed record GlyphMapAnalysis(
    int EntryCount,
    int EncodableEntryCount,
    int EmptyEntryCount,
    int ReservedIndexEntryCount,
    IReadOnlyList<GlyphDuplicate> Duplicates,
    IReadOnlyList<string> Warnings);

/// <summary>
/// Represents the toolkit's glyph map model or service.
/// </summary>
public sealed class GlyphMap
{
    /// <summary>The fixed new line value used by this format or revision.</summary>
    private const ushort NewLine = 0xFFFE;
    /// <summary>The fixed first name value used by this format or revision.</summary>
    private const ushort FirstName = 0xFFE7;
    /// <summary>The fixed last name value used by this format or revision.</summary>
    private const ushort LastName = 0xFFE6;
    /// <summary>Stores the glyphs state owned by this instance or type.</summary>
    private readonly string[] _glyphs;
    /// <summary>Stores the lowest indices state owned by this instance or type.</summary>
    private readonly Dictionary<string, ushort> _lowestIndices;
    /// <summary>Stores the root state owned by this instance or type.</summary>
    private readonly TrieNode _root = new();

    /// <summary>
    /// Initializes a new instance with validated constructor state.
    /// </summary>
    /// <param name="glyphs">The glyphs value.</param>
    /// <param name="sourceSha256">The source SHA 256 value.</param>
    private GlyphMap(string[] glyphs, string sourceSha256)
    {
        _glyphs = glyphs;
        SourceSha256 = sourceSha256;
        _lowestIndices = new Dictionary<string, ushort>(StringComparer.Ordinal);
        for (int i = 0; i < glyphs.Length; i++)
        {
            string glyph = glyphs[i];
            if (glyph.Length == 0 || i >= 0xFF00)
            {
                continue;
            }
            ushort index = checked((ushort)i);
            if (!_lowestIndices.TryAdd(glyph, index))
            {
                continue;
            }
            // Input order already visits indices from low to high; no sorted second pass is needed.
            TrieNode node = _root;
            foreach (char character in glyph)
            {
                if (!node.Children.TryGetValue(character, out TrieNode? child))
                {
                    child = new TrieNode();
                    node.Children.Add(character, child);
                }
                node = child;
            }
            node.Index ??= index;
        }
    }

    /// <summary>The number of logical entries in this collection.</summary>
    public int Count => _glyphs.Length;
    /// <summary>The hexadecimal SHA-256 of the exact input snapshot used by this object.</summary>
    public string SourceSha256 { get; }

    /// <summary>
    /// Gets text while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="index">The index value.</param>
    /// <returns>The resulting text, path, identifier, or hexadecimal digest.</returns>
    public string GetText(int index)
    {
        if ((uint)index >= (uint)_glyphs.Length)
        {
            throw new ToolkitException("GLYPH_INDEX", $"Glyph index {index} is outside map with {_glyphs.Length} entries.");
        }
        return _glyphs[index];
    }

    /// <summary>
    /// Attempts to get lowest index.
    /// </summary>
    /// <param name="text">The text to process.</param>
    /// <param name="index">Receives index value when the operation succeeds.</param>
    /// <returns><see langword="true"/> when the requested value was produced; otherwise <see langword="false"/>.</returns>
    public bool TryGetLowestIndex(string text, out ushort index)
    {
        ArgumentNullException.ThrowIfNull(text);
        return _lowestIndices.TryGetValue(text, out index);
    }

    /// <summary>
    /// Enumerates nonempty glyph-map entries below the reserved control-code range, in index order.
    /// </summary>
    /// <returns>The validated operation result.</returns>
    public IEnumerable<(ushort Index, string Text)> EnumerateEncodableEntries()
    {
        for (int index = 0; index < _glyphs.Length && index < 0xFF00; index++)
        {
            if (_glyphs[index].Length > 0)
            {
                yield return (checked((ushort)index), _glyphs[index]);
            }
        }
    }

    /// <summary>
    /// Analyzes the supplied model and returns deterministic diagnostics.
    /// </summary>
    /// <returns>The validated operation result.</returns>
    public GlyphMapAnalysis Analyze()
    {
        var indicesByText = new Dictionary<string, List<ushort>>(StringComparer.Ordinal);
        int encodable = 0;
        int empty = 0;
        int reserved = 0;
        for (int index = 0; index < _glyphs.Length; index++)
        {
            string glyph = _glyphs[index];
            if (glyph.Length == 0)
            {
                empty++;
                continue;
            }
            if (index >= 0xFF00)
            {
                reserved++;
                continue;
            }

            encodable++;
            if (!indicesByText.TryGetValue(glyph, out List<ushort>? indices))
            {
                indices = [];
                indicesByText.Add(glyph, indices);
            }
            indices.Add(checked((ushort)index));
        }

        List<GlyphDuplicate> duplicates = indicesByText
            .Where(static pair => pair.Value.Count > 1)
            .OrderBy(static pair => pair.Value[0])
            .Select(static pair => new GlyphDuplicate(pair.Key, pair.Value.ToArray()))
            .ToList();
        List<string> warnings = [];
        if (duplicates.Count > 0)
        {
            warnings.Add($"{duplicates.Count} duplicate glyph value(s) use the lowest index during encoding.");
        }
        if (reserved > 0)
        {
            warnings.Add($"{reserved} non-empty entries are at reserved indices 0xFF00 or above and are decode-only.");
        }
        string[] tokenConflicts = ["{{FIRST_NAME}}", "{{LAST_NAME}}"];
        foreach (string token in tokenConflicts)
        {
            if (_lowestIndices.ContainsKey(token))
            {
                warnings.Add($"Glyph text '{token}' is shadowed by a control token during encoding.");
            }
        }
        return new GlyphMapAnalysis(
            _glyphs.Length,
            encodable,
            empty,
            reserved,
            duplicates,
            warnings);
    }

    /// <summary>
    /// Loads and validates the requested data from its source.
    /// </summary>
    /// <param name="path">The file-system path to process.</param>
    /// <param name="limits">Optional conservative safety limits; defaults are used when omitted.</param>
    /// <returns>The validated operation result.</returns>
    public static GlyphMap Load(string path, FileLimits? limits = null)
    {
        limits ??= FileLimits.Default;
        byte[] data = BinaryUtilities.ReadAllBytesBounded(
            path,
            limits with { MaximumInputBytes = limits.MaximumTextBytes });
        return FromUtf8(data, limits);
    }

    /// <summary>Parses one immutable UTF-8 snapshot and hashes the same bytes used for decoding.</summary>
    /// <param name="data">A UTF-8 line-indexed glyph map; an initial BOM is accepted.</param>
    /// <param name="limits">Limits on encoded size and number of glyph records.</param>
    /// <returns>A map whose source hash refers to exactly <paramref name="data"/>.</returns>
    /// <exception cref="ToolkitException">The snapshot exceeds limits or is malformed UTF-8.</exception>
    public static GlyphMap FromUtf8(ReadOnlySpan<byte> data, FileLimits? limits = null)
    {
        limits ??= FileLimits.Default;
        if (data.Length > limits.MaximumTextBytes)
        {
            throw new ToolkitException("GLYPH_MAP_LIMIT", "Glyph map exceeds the text byte limit.");
        }
        string text;
        try
        {
            var encoding = new UTF8Encoding(false, true);
            if (encoding.GetCharCount(data) > limits.MaximumGlyphMapCharacters)
            {
                throw new ToolkitException("GLYPH_MAP_LIMIT", "Decoded glyph map exceeds the character budget.");
            }
            text = encoding.GetString(data);
        }
        catch (DecoderFallbackException ex)
        {
            throw new ToolkitException("GLYPH_MAP_UTF8", "Glyph map is not valid UTF-8.", ex);
        }
        text = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        ValidateTextBudget(text, limits);
        string[] lines = text.Split('\n');
        if (lines.Length > 0 && lines[^1].Length == 0)
        {
            Array.Resize(ref lines, lines.Length - 1);
        }
        if (lines.Length == 0)
        {
            throw new ToolkitException("GLYPH_MAP_EMPTY", "Glyph map contains no entries.");
        }
        if (lines.Length > limits.MaximumGlyphMapEntries)
        {
            throw new ToolkitException(
                "GLYPH_MAP_LIMIT",
                $"Glyph map contains {lines.Length} entries; limit is {limits.MaximumGlyphMapEntries}.");
        }
        if (lines[0].Length > 0 && lines[0][0] == '\uFEFF')
        {
            lines[0] = lines[0][1..];
        }
        for (int i = 0; i < lines.Length; i++)
        {
            if (lines[i].IndexOf('\0') >= 0)
            {
                throw new ToolkitException("GLYPH_MAP_NUL", $"Glyph map entry {i} contains NUL.");
            }
        }
        return new GlyphMap(lines, BinaryUtilities.Sha256Hex(data));
    }

    /// <summary>
    /// Creates lines while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="lines">The lines value.</param>
    /// <param name="limits">Optional conservative safety limits; defaults are used when omitted.</param>
    /// <returns>The validated operation result.</returns>
    public static GlyphMap FromLines(IEnumerable<string> lines, FileLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(lines);
        limits ??= FileLimits.Default;
        var collected = new List<string>();
        long characters = 0;
        foreach (string line in lines)
        {
            if (line is null || line.IndexOfAny(['\0', '\r', '\n']) >= 0)
            {
                throw new ToolkitException("GLYPH_MAP_LINE", "Each glyph label must be one non-null line without NUL.");
            }
            characters += (long)line.Length + 1;
            if (collected.Count >= limits.MaximumGlyphMapEntries
                || characters > limits.MaximumGlyphMapCharacters
                || line.Length > limits.MaximumGlyphEntryCharacters)
            {
                throw new ToolkitException("GLYPH_MAP_LIMIT", "Glyph map exceeds the entry or character budget.");
            }
            collected.Add(line);
        }
        if (collected.Count == 0)
        {
            throw new ToolkitException("GLYPH_MAP_EMPTY", "Glyph map contains no entries.");
        }
        string[] array = collected.ToArray();
        string normalized = string.Join('\n', array) + "\n";
        return new GlyphMap(array, BinaryUtilities.Sha256Hex(BinaryUtilities.Utf8(normalized)));
    }

    /// <summary>Rejects excessive lines or label lengths before allocating a split array or trie nodes.</summary>
    /// <param name="text">The decoded glyph-map text with newlines normalized to LF.</param>
    /// <param name="limits">The configured maximum entries and per-entry UTF-16 length.</param>
    /// <exception cref="ToolkitException">The map would exceed its entry or character budget.</exception>
    private static void ValidateTextBudget(string text, FileLimits limits)
    {
        int entries = text.Length == 0 ? 0 : 1;
        int lineLength = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '\n')
            {
                if (i + 1 < text.Length)
                {
                    entries++;
                }
                lineLength = 0;
            }
            else if (!(i == 0 && text[i] == '\uFEFF'))
            {
                lineLength++;
            }
            if (entries > limits.MaximumGlyphMapEntries || lineLength > limits.MaximumGlyphEntryCharacters)
            {
                throw new ToolkitException("GLYPH_MAP_LIMIT", "Glyph map exceeds the entry or per-label character budget.");
            }
        }
    }

    /// <summary>
    /// Decodes validated input into the source representation.
    /// </summary>
    /// <param name="indices">The indices value.</param>
    /// <returns>The resulting text, path, identifier, or hexadecimal digest.</returns>
    public string Decode(IEnumerable<ushort> indices)
    {
        ArgumentNullException.ThrowIfNull(indices);
        StringBuilder result = new();
        foreach (ushort index in indices)
        {
            switch (index)
            {
                case NewLine:
                    result.Append('\n');
                    break;
                case FirstName:
                    result.Append("{{FIRST_NAME}}");
                    break;
                case LastName:
                    result.Append("{{LAST_NAME}}");
                    break;
                default:
                    if (index < _glyphs.Length && _glyphs[index].Length > 0)
                    {
                        result.Append(_glyphs[index]);
                    }
                    else
                    {
                        result.Append("{{GLYPH:");
                        result.Append(index.ToString("X4", CultureInfo.InvariantCulture));
                        result.Append("}}");
                    }
                    break;
            }
        }
        return result.ToString();
    }

    /// <summary>
    /// Encodes validated text or binary state into the target representation.
    /// </summary>
    /// <param name="text">The text to process.</param>
    /// <returns>The resulting binary or typed sequence.</returns>
    public ushort[] Encode(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        List<ushort> output = [];
        int position = 0;
        while (position < text.Length)
        {
            if (text[position] == '\r')
            {
                if (position + 1 < text.Length && text[position + 1] == '\n')
                {
                    position++;
                }
                output.Add(NewLine);
                position++;
                continue;
            }
            if (text[position] == '\n')
            {
                output.Add(NewLine);
                position++;
                continue;
            }
            if (TryConsumeToken(text, ref position, output))
            {
                continue;
            }

            if (!TryMatch(text, position, out ushort index, out int consumed))
            {
                Rune rune;
                try
                {
                    rune = Rune.GetRuneAt(text, position);
                }
                catch (ArgumentException ex)
                {
                    throw new ToolkitException("GLYPH_UNICODE", $"Translation contains an invalid UTF-16 sequence at offset {position}.", ex);
                }
                throw new ToolkitException("GLYPH_NOT_FOUND", $"No glyph mapping for '{rune}' (U+{rune.Value:X4}) at UTF-16 offset {position}.");
            }
            output.Add(index);
            position += consumed;
        }
        return output.ToArray();
    }

    /// <summary>
    /// Attempts to match.
    /// </summary>
    /// <param name="text">The text to process.</param>
    /// <param name="position">The position value.</param>
    /// <param name="index">Receives index value when the operation succeeds.</param>
    /// <param name="consumed">Receives consumed value when the operation succeeds.</param>
    /// <returns><see langword="true"/> when the requested value was produced; otherwise <see langword="false"/>.</returns>
    private bool TryMatch(string text, int position, out ushort index, out int consumed)
    {
        TrieNode node = _root;
        ushort? best = null;
        int bestLength = 0;
        for (int i = position; i < text.Length; i++)
        {
            if (!node.Children.TryGetValue(text[i], out TrieNode? child))
            {
                break;
            }
            node = child;
            if (node.Index.HasValue)
            {
                best = node.Index.Value;
                bestLength = i - position + 1;
            }
        }
        index = best.GetValueOrDefault();
        consumed = bestLength;
        return best.HasValue;
    }

    /// <summary>
    /// Attempts to consume token.
    /// </summary>
    /// <param name="text">The text to process.</param>
    /// <param name="position">The mutable position value updated by the operation.</param>
    /// <param name="output">The destination stream, buffer, or model.</param>
    /// <returns><see langword="true"/> when the requested value was produced; otherwise <see langword="false"/>.</returns>
    private static bool TryConsumeToken(string text, ref int position, ICollection<ushort> output)
    {
        ReadOnlySpan<char> remaining = text.AsSpan(position);
        if (remaining.StartsWith("{{FIRST_NAME}}".AsSpan(), StringComparison.Ordinal))
        {
            output.Add(FirstName);
            position += "{{FIRST_NAME}}".Length;
            return true;
        }
        if (remaining.StartsWith("{{LAST_NAME}}".AsSpan(), StringComparison.Ordinal))
        {
            output.Add(LastName);
            position += "{{LAST_NAME}}".Length;
            return true;
        }
        const string prefix = "{{GLYPH:";
        if (!remaining.StartsWith(prefix.AsSpan(), StringComparison.Ordinal))
        {
            return false;
        }
        int closing = text.IndexOf("}}", position + prefix.Length, StringComparison.Ordinal);
        if (closing < 0)
        {
            throw new ToolkitException("GLYPH_TOKEN", $"Unterminated glyph token at offset {position}.");
        }
        string hex = text.Substring(position + prefix.Length, closing - position - prefix.Length);
        if (hex.Length != 4 || !ushort.TryParse(hex, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out ushort value))
        {
            throw new ToolkitException("GLYPH_TOKEN", $"Invalid glyph token '{{{{GLYPH:{hex}}}}}'.");
        }
        output.Add(value);
        position = closing + 2;
        return true;
    }

    /// <summary>
    /// Represents the toolkit's trie node model or service.
    /// </summary>
    private sealed class TrieNode
    {
        /// <summary>The children value used by this model or operation.</summary>
        public Dictionary<char, TrieNode> Children { get; } = [];
        /// <summary>The zero-based position in the corresponding source collection.</summary>
        public ushort? Index { get; set; }
    }
}
