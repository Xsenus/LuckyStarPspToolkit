using System.Buffers.Binary;
using LuckyStarPspToolkit.Formats.Common;

namespace LuckyStarPspToolkit.Formats.Scripts;

/// <summary>
/// Parses and rebuilds Lucky Star scenario files while preserving opaque command bytes.
/// Text records are bounded by the next record, metadata stays before the jump table,
/// and no text reader may consume the final 16-byte checksum.
/// </summary>
public sealed partial class LuckyStarScript
{
    /// <summary>Absolute start of the four-word scenario metadata header.</summary>
    private const int MetadataBase = 0x80;
    /// <summary>Word marking a dialogue record, followed by its dialogue ID.</summary>
    private const ushort DialogStart = 0xFFF0;
    /// <summary>Word separating a speaker name from its message.</summary>
    private const ushort SpeakerEnd = 0xFFFF;
    /// <summary>Word terminating choice text; it is retained outside replacement intervals.</summary>
    private const ushort ChoiceEnd = 0xFFFF;
    /// <summary>Private snapshot, never exposed as a writable caller buffer.</summary>
    private readonly byte[] _original;
    /// <summary>Absolute start of the dialogue-offset table in the unchanged metadata prefix.</summary>
    private readonly int _dialogTableAbsolute;
    /// <summary>Absolute start of the 32-byte choice-group rows.</summary>
    private readonly int _choiceTableAbsolute;
    /// <summary>First command byte after the jump table.</summary>
    private readonly int _contentStart;
    /// <summary>End of retained commands and text, including every referenced zero-valued word.</summary>
    private readonly int _logicalEnd;
    /// <summary>Relative original jump targets, including zero-valued unused slots.</summary>
    private readonly uint[] _jumps;

    /// <summary>Stores an already validated scenario and the offsets needed for reconstruction.</summary>
    /// <param name="original">Owned snapshot of the complete input, including checksum.</param>
    /// <param name="profile">Validated game layout.</param>
    /// <param name="magic">Preserved initial word; no unsupported magic value is inferred.</param>
    /// <param name="dialogTableAbsolute">Absolute dialogue-offset table position.</param>
    /// <param name="choiceTableAbsolute">Absolute choice-group table position.</param>
    /// <param name="contentStart">First byte after the jump table.</param>
    /// <param name="logicalEnd">Last retained byte, exclusive.</param>
    /// <param name="jumps">Validated relative targets.</param>
    /// <param name="dialogs">Dialogue records in table order, not physical order.</param>
    /// <param name="choiceGroups">Choice groups in table order.</param>
    private LuckyStarScript(byte[] original, ScriptProfile profile, ushort magic,
        int dialogTableAbsolute, int choiceTableAbsolute, int contentStart, int logicalEnd,
        uint[] jumps, IReadOnlyList<ScriptDialog> dialogs, IReadOnlyList<ScriptChoiceGroup> choiceGroups)
    {
        _original = original;
        Profile = profile;
        Magic = magic;
        _dialogTableAbsolute = dialogTableAbsolute;
        _choiceTableAbsolute = choiceTableAbsolute;
        _contentStart = contentStart;
        _logicalEnd = logicalEnd;
        _jumps = jumps;
        Dialogs = dialogs;
        ChoiceGroups = choiceGroups;
    }

    /// <summary>Game-specific jump-table position and output alignment.</summary>
    public ScriptProfile Profile { get; }
    /// <summary>Uninterpreted original first word, retained during rebuilding.</summary>
    public ushort Magic { get; }
    /// <summary>Decoded dialogues in original table order; callers must not mutate these records.</summary>
    public IReadOnlyList<ScriptDialog> Dialogs { get; }
    /// <summary>Decoded groups and choices in original table order.</summary>
    public IReadOnlyList<ScriptChoiceGroup> ChoiceGroups { get; }
    /// <summary>Original relative targets; zero means an unused jump slot.</summary>
    public IReadOnlyList<uint> Jumps => _jumps;
    /// <summary>Whether the private input snapshot has its expected additive checksum.</summary>
    public bool ChecksumValid => RgoChecksum.Verify(_original);
    /// <summary>SHA-256 identifying the exact input bytes rather than the decoded text alone.</summary>
    public string Sha256 => BinaryUtilities.Sha256Hex(_original);

    /// <summary>
    /// Validates metadata and all record starts before allocating text arrays, then decodes
    /// each field within its physical record boundary and a shared glyph budget.
    /// </summary>
    /// <param name="data">Complete scenario bytes, including its 16-byte checksum.</param>
    /// <param name="profile">Layout of the target game; not detected by guessing.</param>
    /// <param name="limits">Bounds for file size, record counts and decoded glyphs.</param>
    /// <param name="requireChecksum">Reject an invalid checksum when true; false is for explicit inspection only.</param>
    /// <returns>A validated scenario owning a separate copy of the input.</returns>
    /// <exception cref="ToolkitException">Metadata, offsets, delimiters or resource limits are invalid.</exception>
    public static LuckyStarScript Parse(ReadOnlySpan<byte> data, ScriptProfile profile,
        FileLimits? limits = null, bool requireChecksum = true)
    {
        ArgumentNullException.ThrowIfNull(profile);
        limits ??= FileLimits.Default;
        ValidateConfiguration(profile, limits);
        if (data.Length > limits.MaximumScriptBytes || data.Length > limits.MaximumInputBytes)
            throw new ToolkitException("SCRIPT_INPUT_LIMIT", $"Scenario length {data.Length} exceeds the configured input budget.");
        if ((data.Length & 15) != 0 || (long)data.Length < (long)profile.JumpTableOffset + 4 + 2 + 16)
            throw new ToolkitException("SCRIPT_SIZE", $"Scenario length {data.Length} is invalid for jump table 0x{profile.JumpTableOffset:X}.");
        if (requireChecksum && !RgoChecksum.Verify(data))
            throw new ToolkitException("SCRIPT_CHECKSUM", "Script checksum is invalid.");

        int checksumStart = data.Length - 16;
        int dialogCount = ReadCount(data, MetadataBase + 4, limits.MaximumScriptDialogs, "SCRIPT_DIALOG_LIMIT");
        int groupCount = ReadCount(data, MetadataBase + 12, limits.MaximumScriptChoiceGroups, "SCRIPT_CHOICE_LIMIT");
        int dialogBytes = Guard.CheckedInt((long)dialogCount * 4, "SCRIPT_TABLE_RANGE", "dialogue table length");
        int choiceBytes = Guard.CheckedInt((long)groupCount * 32, "SCRIPT_TABLE_RANGE", "choice table length");
        int dialogTable = ReadTableOffset(data, MetadataBase, dialogBytes, profile.JumpTableOffset, "dialogue");
        int choiceTable = ReadTableOffset(data, MetadataBase + 8, choiceBytes, profile.JumpTableOffset, "choice");
        if (dialogBytes != 0 && choiceBytes != 0
            && dialogTable < (long)choiceTable + choiceBytes && choiceTable < (long)dialogTable + dialogBytes)
            throw new ToolkitException("SCRIPT_TABLE_OVERLAP", "Dialogue and choice tables overlap.");

        uint firstJump = ReadUInt32(data, profile.JumpTableOffset, "first jump");
        if (firstJump < 4 || (firstJump & 3) != 0)
            throw new ToolkitException("SCRIPT_JUMP_HEADER", "The first jump must encode the jump-table byte length.");
        if (firstJump / 4 > limits.MaximumScriptJumps)
            throw new ToolkitException("SCRIPT_JUMP_LIMIT", "The jump table exceeds its configured entry budget.");
        long contentPosition = (long)profile.JumpTableOffset + firstJump;
        if (contentPosition > checksumStart - 2)
            throw new ToolkitException("SCRIPT_JUMP_RANGE", "Jump-table bytes overlap the checksum or leave no target word.");
        int contentStart = (int)contentPosition;
        uint[] jumps = new uint[(int)(firstJump / 4)];
        int minimumLogicalEnd = contentStart;
        for (int i = 0; i < jumps.Length; i++)
        {
            jumps[i] = ReadUInt32(data, profile.JumpTableOffset + i * 4, "jump target");
            if (jumps[i] != 0)
            {
                int absolute = RelativeToAbsolute(jumps[i], profile.JumpTableOffset, contentStart, checksumStart, "jump target");
                // A target pointing at a zero word still owns those two bytes: it is not padding.
                minimumLogicalEnd = Math.Max(minimumLogicalEnd, absolute + 2);
            }
        }

        List<TextRecordStart> starts = new(dialogCount);
        for (int i = 0; i < dialogCount; i++)
        {
            uint relative = ReadUInt32(data, dialogTable + i * 4, "dialogue offset");
            starts.Add(new TextRecordStart(RelativeToAbsolute(relative, profile.JumpTableOffset,
                contentStart, checksumStart, "dialogue"), true, i, -1));
        }
        ScriptChoiceGroup[] groups = new ScriptChoiceGroup[groupCount];
        for (int g = 0; g < groupCount; g++)
        {
            int row = choiceTable + g * 32;
            uint jumpId = ReadUInt32(data, row, "choice-group jump ID");
            if (jumpId >= jumps.Length)
                throw new ToolkitException("SCRIPT_CHOICE_JUMP", $"Choice group {g} references a missing jump.");
            groups[g] = new ScriptChoiceGroup { Index = g, JumpId = jumpId };
            bool gapSeen = false;
            for (int c = 0; c < 7; c++)
            {
                uint relative = ReadUInt32(data, row + 4 + c * 4, "choice offset");
                if (relative == 0) { gapSeen = true; continue; }
                if (gapSeen)
                    throw new ToolkitException("SCRIPT_CHOICE_GAP", $"Choice group {g} has text after an empty slot.");
                starts.Add(new TextRecordStart(RelativeToAbsolute(relative, profile.JumpTableOffset,
                    contentStart, checksumStart, "choice"), false, g, c));
            }
        }
        starts.Sort(static (a, b) => a.Start.CompareTo(b.Start));
        for (int i = 1; i < starts.Count; i++)
            if (starts[i - 1].Start == starts[i].Start)
                throw new ToolkitException("SCRIPT_DUPLICATE_TEXT_OFFSET", $"Multiple text records reference 0x{starts[i].Start:X}.");

        ScriptDialog[] dialogs = new ScriptDialog[dialogCount];
        long remainingGlyphs = limits.MaximumScriptGlyphs;
        for (int i = 0; i < starts.Count; i++)
        {
            TextRecordStart record = starts[i];
            int boundary = i + 1 == starts.Count ? checksumStart : starts[i + 1].Start;
            int position = record.Start;
            uint relative = (uint)(record.Start - profile.JumpTableOffset);
            if (record.IsDialog)
            {
                if (position > boundary - 4)
                    throw new ToolkitException("SCRIPT_INTERVAL", "A dialogue header overlaps the next record or checksum.");
                ushort marker = ReadUInt16(data, ref position, "dialogue marker");
                if (marker != DialogStart)
                    throw new ToolkitException("SCRIPT_DIALOG_MARKER", $"Dialogue at 0x{record.Start:X} does not start with 0xFFF0.");
                ushort id = ReadUInt16(data, ref position, "dialogue ID");
                ushort[] speaker = ReadField(data, ref position, boundary, false, limits, ref remainingGlyphs, "speaker", out _);
                ushort[] message = ReadField(data, ref position, boundary, true, limits, ref remainingGlyphs, "message", out ushort terminator);
                dialogs[record.PrimaryIndex] = new ScriptDialog
                {
                    Index = record.PrimaryIndex, RelativeOffset = relative, Id = id,
                    SpeakerGlyphs = speaker, MessageGlyphs = message, MessageTerminator = terminator,
                    AbsoluteStart = record.Start, AbsoluteEnd = position - 2
                };
            }
            else
            {
                ushort[] glyphs = ReadField(data, ref position, boundary, false, limits, ref remainingGlyphs, "choice", out _);
                groups[record.PrimaryIndex].Choices.Add(new ScriptChoice
                {
                    GroupIndex = record.PrimaryIndex, ChoiceIndex = record.SecondaryIndex,
                    RelativeOffset = relative, Glyphs = glyphs, AbsoluteStart = record.Start, AbsoluteEnd = position - 2
                });
            }
            minimumLogicalEnd = Math.Max(minimumLogicalEnd, position);
        }
        foreach (ScriptChoiceGroup group in groups)
            group.Choices.Sort(static (a, b) => a.ChoiceIndex.CompareTo(b.ChoiceIndex));

        int logicalEnd = checksumStart;
        while (logicalEnd > minimumLogicalEnd && data[logicalEnd - 1] == 0) logicalEnd--;
        logicalEnd = Math.Max((logicalEnd + 1) & ~1, minimumLogicalEnd);
        return new LuckyStarScript(data.ToArray(), profile, BinaryPrimitives.ReadUInt16LittleEndian(data),
            dialogTable, choiceTable, contentStart, logicalEnd, jumps, dialogs, groups);
    }

    /// <summary>Validates caller configuration independently of untrusted file fields.</summary>
    /// <param name="profile">Requested fixed layout.</param>
    /// <param name="limits">Nonnegative size and count budgets.</param>
    /// <exception cref="ToolkitException">The profile alignment or a configured limit is invalid.</exception>
    private static void ValidateConfiguration(ScriptProfile profile, FileLimits limits)
    {
        if (profile.JumpTableOffset < MetadataBase + 16 || (profile.JumpTableOffset & 3) != 0
            || profile.FileAlignment < 16 || (profile.FileAlignment & 15) != 0)
            throw new ToolkitException("SCRIPT_PROFILE", "Jump-table offsets must be 4-byte aligned and file alignment a positive multiple of 16.");
        if (limits.MaximumInputBytes < 0 || limits.MaximumScriptBytes < 0 || limits.MaximumScriptDialogs < 0
            || limits.MaximumScriptChoiceGroups < 0 || limits.MaximumScriptJumps < 0
            || limits.MaximumScriptFieldGlyphs < 0 || limits.MaximumScriptGlyphs < 0)
            throw new ToolkitException("SCRIPT_LIMITS", "Script budgets cannot be negative.");
    }

    /// <summary>Reads a count without narrowing an unchecked UInt32 from the input.</summary>
    /// <param name="data">Validated header buffer.</param>
    /// <param name="offset">Absolute count field position.</param>
    /// <param name="maximum">Inclusive permitted count.</param>
    /// <param name="code">Stable error code for exceeding this budget.</param>
    /// <returns>A count safe to use for collection allocation.</returns>
    private static int ReadCount(ReadOnlySpan<byte> data, int offset, int maximum, string code)
    {
        uint count = ReadUInt32(data, offset, "record count");
        if (count > maximum) throw new ToolkitException(code, $"Record count {count} exceeds {maximum}.");
        return (int)count;
    }

    /// <summary>Checks a metadata table's entire range before the jump table; zero offsets are allowed only for empty tables.</summary>
    /// <param name="data">Scenario header.</param>
    /// <param name="fieldOffset">Position of the relative offset field.</param>
    /// <param name="length">Validated total table byte count.</param>
    /// <param name="jumpOffset">Exclusive end of metadata space.</param>
    /// <param name="context">Table name for diagnostics.</param>
    /// <returns>The absolute table offset without overflowing Int32.</returns>
    private static int ReadTableOffset(ReadOnlySpan<byte> data, int fieldOffset, int length, int jumpOffset, string context)
    {
        uint relative = ReadUInt32(data, fieldOffset, context);
        if (length == 0 && relative == 0) return MetadataBase;
        long absolute = MetadataBase + (long)relative;
        if ((relative & 3) != 0 || absolute < MetadataBase + 16 || absolute > (long)jumpOffset - length)
            throw new ToolkitException("SCRIPT_TABLE_RANGE", $"The {context} table does not fit in metadata space.");
        return (int)absolute;
    }

    /// <summary>Scans a bounded field before allocating its exact glyph array; delimiters never consume the checksum.</summary>
    /// <param name="data">Complete scenario buffer.</param>
    /// <param name="position">Field start on entry; byte after its delimiter on success.</param>
    /// <param name="boundary">Exclusive next-record or checksum position.</param>
    /// <param name="message">Whether all three message-ending codes are delimiters.</param>
    /// <param name="limits">Per-field allocation budget.</param>
    /// <param name="remainingGlyphs">Shared budget, decremented only after the field is validated.</param>
    /// <param name="context">Field label for errors.</param>
    /// <param name="terminator">The exact original delimiter word.</param>
    /// <returns>Little-endian glyph indices, excluding the delimiter.</returns>
    /// <exception cref="ToolkitException">A delimiter is missing or a glyph budget is exhausted.</exception>
    private static ushort[] ReadField(ReadOnlySpan<byte> data, ref int position, int boundary, bool message,
        FileLimits limits, ref long remainingGlyphs, string context, out ushort terminator)
    {
        int start = position;
        int scan = start;
        int count = 0;
        while (true)
        {
            if (scan > boundary - 2)
                throw new ToolkitException("SCRIPT_GLYPH_EOF", $"The {context} field reaches its record boundary without a delimiter.");
            ushort word = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(scan, 2));
            scan += 2;
            if (message ? IsMessageEnd(word) : word == SpeakerEnd) { terminator = word; break; }
            if (count >= limits.MaximumScriptFieldGlyphs)
                throw new ToolkitException("SCRIPT_FIELD_LIMIT", $"The {context} field exceeds its glyph budget.");
            if (count >= remainingGlyphs)
                throw new ToolkitException("SCRIPT_GLYPH_LIMIT", "The scenario exceeds its total decoded-glyph budget.");
            count++;
        }
        remainingGlyphs -= count;
        ushort[] result = new ushort[count];
        for (int i = 0; i < count; i++)
            result[i] = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(start + i * 2, 2));
        position = scan;
        return result;
    }

    /// <summary>Recognizes only the three observed structural message delimiters.</summary>
    /// <param name="word">A scenario word, not a Unicode code point.</param>
    /// <returns>True for clear, keep or alternate message termination.</returns>
    private static bool IsMessageEnd(ushort word) => word is 0xFFFB or 0xFFFD or 0xFFFF;

    /// <summary>Reads one bounded little-endian word and advances its cursor.</summary>
    /// <param name="data">Source bytes.</param>
    /// <param name="position">Absolute cursor, advanced by two.</param>
    /// <param name="context">Diagnostic label.</param>
    /// <returns>The decoded word.</returns>
    private static ushort ReadUInt16(ReadOnlySpan<byte> data, ref int position, string context)
    {
        EnsureRange(data, position, 2, context);
        ushort value = BinaryPrimitives.ReadUInt16LittleEndian(data[position..]);
        position += 2;
        return value;
    }

    /// <summary>Reads one bounded little-endian metadata or jump-table value.</summary>
    /// <param name="data">Source bytes.</param>
    /// <param name="offset">Absolute four-byte field position.</param>
    /// <param name="context">Diagnostic label.</param>
    /// <returns>The unsigned value without a narrowing conversion.</returns>
    private static uint ReadUInt32(ReadOnlySpan<byte> data, int offset, string context)
    {
        EnsureRange(data, offset, 4, context);
        return BinaryPrimitives.ReadUInt32LittleEndian(data[offset..]);
    }

    /// <summary>Validates an aligned target in command/text space using wide offset arithmetic.</summary>
    /// <param name="relative">Target relative to the jump-table base.</param>
    /// <param name="jumpTableOffset">Absolute jump-table base.</param>
    /// <param name="contentStart">First permissible target byte.</param>
    /// <param name="limit">Checksum start, which no target word may overlap.</param>
    /// <param name="context">Diagnostic label.</param>
    /// <returns>An absolute target that fits an entire two-byte word.</returns>
    private static int RelativeToAbsolute(uint relative, int jumpTableOffset, int contentStart, int limit, string context)
    {
        long absolute = (long)jumpTableOffset + relative;
        if ((relative & 1) != 0 || absolute < contentStart || absolute > limit - 2)
            throw new ToolkitException("SCRIPT_OFFSET", $"The {context} offset 0x{relative:X} is unaligned or outside command/text space.");
        return (int)absolute;
    }

    /// <summary>Rejects invalid byte ranges without overflowing offset-plus-length arithmetic.</summary>
    /// <param name="data">Source or destination bounds.</param>
    /// <param name="offset">Nonnegative start.</param>
    /// <param name="length">Nonnegative byte count.</param>
    /// <param name="context">Diagnostic label.</param>
    private static void EnsureRange(ReadOnlySpan<byte> data, int offset, int length, string context)
    {
        if (offset < 0 || length < 0 || offset > data.Length - length)
            throw new ToolkitException("SCRIPT_RANGE", $"The {context} range at 0x{offset:X} does not fit in {data.Length} bytes.");
    }

    /// <summary>A prevalidated physical text start; its end is bounded by the next sorted start.</summary>
    /// <param name="Start">Absolute first text-record byte.</param>
    /// <param name="IsDialog">True for a dialogue, false for a choice.</param>
    /// <param name="PrimaryIndex">Dialogue or group table index.</param>
    /// <param name="SecondaryIndex">Choice index; minus one for a dialogue.</param>
    private sealed record TextRecordStart(int Start, bool IsDialog, int PrimaryIndex, int SecondaryIndex);
}
