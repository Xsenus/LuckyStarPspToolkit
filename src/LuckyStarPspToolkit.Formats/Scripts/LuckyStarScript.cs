using System.Buffers.Binary;
using LuckyStarPspToolkit.Formats.Common;

namespace LuckyStarPspToolkit.Formats.Scripts;

/// <summary>
/// Represents the toolkit's lucky star script model or service.
/// </summary>
public sealed class LuckyStarScript
{
    /// <summary>The fixed metadata base value used by this format or revision.</summary>
    private const int MetadataBase = 0x80;
    /// <summary>The fixed dialog start value used by this format or revision.</summary>
    private const ushort DialogStart = 0xFFF0;
    /// <summary>The fixed speaker end value used by this format or revision.</summary>
    private const ushort SpeakerEnd = 0xFFFF;
    /// <summary>The fixed choice end value used by this format or revision.</summary>
    private const ushort ChoiceEnd = 0xFFFF;
    /// <summary>The message ends value used by this model or operation.</summary>
    private static readonly HashSet<ushort> MessageEnds = [0xFFFB, 0xFFFD, 0xFFFF];

    /// <summary>Stores the original state owned by this instance or type.</summary>
    private readonly byte[] _original;
    /// <summary>Stores the dialog table absolute state owned by this instance or type.</summary>
    private readonly int _dialogTableAbsolute;
    /// <summary>Stores the choice table absolute state owned by this instance or type.</summary>
    private readonly int _choiceTableAbsolute;
    /// <summary>Stores the content start state owned by this instance or type.</summary>
    private readonly int _contentStart;
    /// <summary>Stores the logical end state owned by this instance or type.</summary>
    private readonly int _logicalEnd;
    /// <summary>Stores the jumps state owned by this instance or type.</summary>
    private readonly uint[] _jumps;

    /// <summary>
    /// Initializes a new instance with validated constructor state.
    /// </summary>
    /// <param name="original">The original value.</param>
    /// <param name="profile">The revision-specific format or patch profile.</param>
    /// <param name="magic">The magic value.</param>
    /// <param name="dialogTableAbsolute">The dialog table absolute value.</param>
    /// <param name="choiceTableAbsolute">The choice table absolute value.</param>
    /// <param name="contentStart">The content start value.</param>
    /// <param name="logicalEnd">The logical end value.</param>
    /// <param name="jumps">The jumps value.</param>
    /// <param name="dialogs">The dialogs value.</param>
    /// <param name="choiceGroups">The choice groups value.</param>
    private LuckyStarScript(
        byte[] original,
        ScriptProfile profile,
        ushort magic,
        int dialogTableAbsolute,
        int choiceTableAbsolute,
        int contentStart,
        int logicalEnd,
        uint[] jumps,
        IReadOnlyList<ScriptDialog> dialogs,
        IReadOnlyList<ScriptChoiceGroup> choiceGroups)
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

    /// <summary>The profile value used by this model or operation.</summary>
    public ScriptProfile Profile { get; }
    /// <summary>The magic value used by this model or operation.</summary>
    public ushort Magic { get; }
    /// <summary>The dialogs value used by this model or operation.</summary>
    public IReadOnlyList<ScriptDialog> Dialogs { get; }
    /// <summary>The choice groups value used by this model or operation.</summary>
    public IReadOnlyList<ScriptChoiceGroup> ChoiceGroups { get; }
    /// <summary>The jumps value used by this model or operation.</summary>
    public IReadOnlyList<uint> Jumps => _jumps;
    /// <summary>The checksum valid value used by this model or operation.</summary>
    public bool ChecksumValid => RgoChecksum.Verify(_original);
    /// <summary>The sha256 value used by this model or operation.</summary>
    public string Sha256 => BinaryUtilities.Sha256Hex(_original);

    /// <summary>
    /// Parses validated input into the current binary-format model.
    /// </summary>
    /// <param name="data">The binary data to process.</param>
    /// <param name="profile">The revision-specific format or patch profile.</param>
    /// <param name="limits">Optional conservative safety limits; defaults are used when omitted.</param>
    /// <param name="requireChecksum">The require checksum value.</param>
    /// <returns>The validated operation result.</returns>
    /// <remarks>Malformed input or violated preconditions are rejected before a persistent output is committed.</remarks>
    public static LuckyStarScript Parse(ReadOnlySpan<byte> data, ScriptProfile profile, FileLimits? limits = null, bool requireChecksum = true)
    {
        ArgumentNullException.ThrowIfNull(profile);
        limits ??= FileLimits.Default;
        if (data.Length < profile.JumpTableOffset + 4 + 16 || (data.Length & 0x0F) != 0)
        {
            throw new ToolkitException("SCRIPT_SIZE", $"Script length {data.Length} is invalid for jump table 0x{profile.JumpTableOffset:X}.");
        }
        if (requireChecksum && !RgoChecksum.Verify(data))
        {
            throw new ToolkitException("SCRIPT_CHECKSUM", "Script checksum is invalid.");
        }

        byte[] original = data.ToArray();
        ushort magic = BinaryPrimitives.ReadUInt16LittleEndian(data);
        uint dialogTableRelative = ReadUInt32(data, MetadataBase, "dialog table offset");
        uint dialogCount32 = ReadUInt32(data, MetadataBase + 4, "dialog count");
        uint choiceTableRelative = ReadUInt32(data, MetadataBase + 8, "choice table offset");
        uint choiceCount32 = ReadUInt32(data, MetadataBase + 12, "choice group count");
        if (dialogCount32 > limits.MaximumScriptDialogs)
        {
            throw new ToolkitException("SCRIPT_DIALOG_LIMIT", $"Dialog count {dialogCount32} exceeds limit {limits.MaximumScriptDialogs}.");
        }
        if (choiceCount32 > limits.MaximumScriptChoiceGroups)
        {
            throw new ToolkitException("SCRIPT_CHOICE_LIMIT", $"Choice group count {choiceCount32} exceeds limit {limits.MaximumScriptChoiceGroups}.");
        }
        int dialogCount = checked((int)dialogCount32);
        int choiceCount = checked((int)choiceCount32);
        int dialogTableAbsolute = checked(MetadataBase + (int)dialogTableRelative);
        int choiceTableAbsolute = checked(MetadataBase + (int)choiceTableRelative);
        EnsureRange(data, dialogTableAbsolute, checked(dialogCount * 4), "dialog offset table");
        EnsureRange(data, choiceTableAbsolute, checked(choiceCount * 32), "choice group table");
        if (dialogTableAbsolute < MetadataBase + 16 || choiceTableAbsolute < MetadataBase + 16)
        {
            throw new ToolkitException("SCRIPT_TABLE_OFFSET", "Script metadata tables overlap the header.");
        }

        uint firstJump = ReadUInt32(data, profile.JumpTableOffset, "first jump");
        if (firstJump < 4 || (firstJump & 3) != 0)
        {
            throw new ToolkitException("SCRIPT_JUMP_HEADER", $"First jump 0x{firstJump:X} is not a valid jump table byte length.");
        }
        int jumpCount = checked((int)(firstJump / 4));
        if (jumpCount > limits.MaximumScriptJumps)
        {
            throw new ToolkitException("SCRIPT_JUMP_LIMIT", $"Jump count {jumpCount} exceeds limit {limits.MaximumScriptJumps}.");
        }
        int jumpBytes = checked(jumpCount * 4);
        EnsureRange(data, profile.JumpTableOffset, jumpBytes, "jump table");
        uint[] jumps = new uint[jumpCount];
        for (int i = 0; i < jumpCount; i++)
        {
            jumps[i] = ReadUInt32(data, checked(profile.JumpTableOffset + i * 4), $"jump {i}");
        }
        if (jumps.Length == 0 || jumps[0] != firstJump)
        {
            throw new ToolkitException("SCRIPT_JUMP_ZERO", "Jump zero does not encode the jump table length.");
        }
        int contentStart = checked(profile.JumpTableOffset + jumpBytes);
        int checksumStart = data.Length - 16;

        List<ScriptDialog> dialogs = new(dialogCount);
        List<Interval> intervals = [];
        for (int i = 0; i < dialogCount; i++)
        {
            uint relative = ReadUInt32(data, checked(dialogTableAbsolute + i * 4), $"dialog {i} offset");
            int start = RelativeToAbsolute(relative, profile.JumpTableOffset, checksumStart, $"dialog {i}");
            int position = start;
            ushort marker = ReadUInt16(data, ref position, $"dialog {i} marker");
            if (marker != DialogStart)
            {
                throw new ToolkitException("SCRIPT_DIALOG_MARKER", $"Dialog {i} at 0x{start:X} starts with 0x{marker:X4}, expected 0x{DialogStart:X4}.");
            }
            ushort id = ReadUInt16(data, ref position, $"dialog {i} id");
            ushort[] speaker = ReadGlyphsUntil(data, ref position, SpeakerEnd, checksumStart, $"dialog {i} speaker");
            List<ushort> message = [];
            ushort terminator;
            while (true)
            {
                terminator = ReadUInt16(data, ref position, $"dialog {i} message");
                if (MessageEnds.Contains(terminator))
                {
                    break;
                }
                message.Add(terminator);
                if (message.Count > limits.MaximumInputBytes / 2)
                {
                    throw new ToolkitException("SCRIPT_MESSAGE_LIMIT", $"Dialog {i} message is unreasonably long.");
                }
            }
            ScriptDialog dialog = new()
            {
                Index = i,
                RelativeOffset = relative,
                Id = id,
                SpeakerGlyphs = speaker,
                MessageGlyphs = message.ToArray(),
                MessageTerminator = terminator,
                AbsoluteStart = start,
                AbsoluteEnd = position - 2
            };
            dialogs.Add(dialog);
            intervals.Add(new Interval(start, position - 2, IntervalKind.Dialog, i, -1));
        }

        List<ScriptChoiceGroup> groups = new(choiceCount);
        for (int groupIndex = 0; groupIndex < choiceCount; groupIndex++)
        {
            int row = checked(choiceTableAbsolute + groupIndex * 32);
            uint jumpId = ReadUInt32(data, row, $"choice group {groupIndex} jump id");
            if (jumpId >= jumps.Length)
            {
                throw new ToolkitException("SCRIPT_CHOICE_JUMP", $"Choice group {groupIndex} references jump {jumpId}, only {jumps.Length} exist.");
            }
            ScriptChoiceGroup group = new() { Index = groupIndex, JumpId = jumpId };
            bool gapSeen = false;
            for (int choiceIndex = 0; choiceIndex < 7; choiceIndex++)
            {
                uint relative = ReadUInt32(data, checked(row + 4 + choiceIndex * 4), $"choice {groupIndex}:{choiceIndex} offset");
                if (relative == 0)
                {
                    gapSeen = true;
                    continue;
                }
                if (gapSeen)
                {
                    throw new ToolkitException("SCRIPT_CHOICE_GAP", $"Choice group {groupIndex} contains an offset after an empty slot.");
                }
                int start = RelativeToAbsolute(relative, profile.JumpTableOffset, checksumStart, $"choice {groupIndex}:{choiceIndex}");
                int position = start;
                ushort[] glyphs = ReadGlyphsUntil(data, ref position, ChoiceEnd, checksumStart, $"choice {groupIndex}:{choiceIndex}");
                ScriptChoice choice = new()
                {
                    GroupIndex = groupIndex,
                    ChoiceIndex = choiceIndex,
                    RelativeOffset = relative,
                    Glyphs = glyphs,
                    AbsoluteStart = start,
                    AbsoluteEnd = position - 2
                };
                group.Choices.Add(choice);
                intervals.Add(new Interval(start, position - 2, IntervalKind.Choice, groupIndex, choiceIndex));
            }
            groups.Add(group);
        }

        intervals.Sort(static (left, right) => left.Start.CompareTo(right.Start));
        int previousEnd = contentStart;
        foreach (Interval interval in intervals)
        {
            if (interval.Start < contentStart || interval.Start < previousEnd || interval.End > checksumStart)
            {
                throw new ToolkitException("SCRIPT_INTERVAL", $"Script semantic interval [0x{interval.Start:X},0x{interval.End:X}) overlaps or lies outside content.");
            }
            previousEnd = interval.End;
        }

        int minimumLogicalEnd = Math.Max(contentStart, intervals.Count == 0 ? contentStart : intervals[^1].End);
        foreach (uint jump in jumps)
        {
            if (jump == 0)
            {
                continue;
            }
            int absolute = RelativeToAbsolute(jump, profile.JumpTableOffset, checksumStart, "jump target");
            if (absolute < contentStart)
            {
                throw new ToolkitException("SCRIPT_JUMP_TARGET", $"Jump target 0x{jump:X} points inside the jump table.");
            }
            minimumLogicalEnd = Math.Max(minimumLogicalEnd, absolute);
        }
        int logicalEnd = checksumStart;
        while (logicalEnd > minimumLogicalEnd && data[logicalEnd - 1] == 0)
        {
            logicalEnd--;
        }
        logicalEnd = (logicalEnd + 1) & ~1;
        logicalEnd = Math.Max(logicalEnd, minimumLogicalEnd);
        if (logicalEnd > checksumStart)
        {
            throw new ToolkitException("SCRIPT_LOGICAL_END", "Calculated script logical end overlaps checksum.");
        }

        return new LuckyStarScript(original, profile, magic, dialogTableAbsolute, choiceTableAbsolute, contentStart, logicalEnd, jumps, dialogs, groups);
    }

    /// <summary>
    /// Serializes the current validated model into its binary representation.
    /// </summary>
    /// <param name="mutation">The requested text or binary mutations.</param>
    /// <param name="limits">Optional conservative safety limits; defaults are used when omitted.</param>
    /// <returns>The validated operation result.</returns>
    /// <remarks>Malformed input or violated preconditions are rejected before a persistent output is committed.</remarks>
    public ScriptBuildResult Build(ScriptMutation mutation, FileLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(mutation);
        limits ??= FileLimits.Default;
        ValidateMutationKeys(mutation);

        List<Replacement> replacements = [];
        foreach (ScriptDialog dialog in Dialogs)
        {
            ushort[] speaker = mutation.SpeakerGlyphs.GetValueOrDefault(dialog.Index) ?? dialog.SpeakerGlyphs;
            ushort[] message = mutation.MessageGlyphs.GetValueOrDefault(dialog.Index) ?? dialog.MessageGlyphs;
            byte[] bytes = BuildDialog(dialog.Id, speaker, message, dialog.MessageTerminator);
            replacements.Add(new Replacement(dialog.AbsoluteStart, dialog.AbsoluteEnd, bytes, $"dialog {dialog.Index}"));
        }
        foreach (ScriptChoiceGroup group in ChoiceGroups)
        {
            foreach (ScriptChoice choice in group.Choices)
            {
                ushort[] glyphs = mutation.ChoiceGlyphs.GetValueOrDefault((group.Index, choice.ChoiceIndex)) ?? choice.Glyphs;
                byte[] bytes = BuildChoice(glyphs);
                replacements.Add(new Replacement(choice.AbsoluteStart, choice.AbsoluteEnd, bytes, $"choice {group.Index}:{choice.ChoiceIndex}"));
            }
        }
        replacements.Sort(static (left, right) => left.OldStart.CompareTo(right.OldStart));

        List<byte> logical = new(_logicalEnd + replacements.Sum(static replacement => replacement.NewBytes.Length - (replacement.OldEnd - replacement.OldStart)));
        logical.AddRange(_original.AsSpan(0, _contentStart).ToArray());
        List<MapSegment> map = [];
        int oldCursor = _contentStart;
        int newCursor = _contentStart;
        foreach (Replacement replacement in replacements)
        {
            if (replacement.OldStart < oldCursor)
            {
                throw new ToolkitException("SCRIPT_REPLACEMENT_OVERLAP", $"Replacement {replacement.Name} overlaps a previous range.");
            }
            if (replacement.OldStart > oldCursor)
            {
                int rawLength = replacement.OldStart - oldCursor;
                logical.AddRange(_original.AsSpan(oldCursor, rawLength).ToArray());
                map.Add(new MapSegment(oldCursor, replacement.OldStart, newCursor, checked(newCursor + rawLength), false));
                oldCursor = replacement.OldStart;
                newCursor += rawLength;
            }
            int replacementNewStart = newCursor;
            logical.AddRange(replacement.NewBytes);
            newCursor = checked(newCursor + replacement.NewBytes.Length);
            map.Add(new MapSegment(replacement.OldStart, replacement.OldEnd, replacementNewStart, newCursor, true));
            oldCursor = replacement.OldEnd;
        }
        if (oldCursor < _logicalEnd)
        {
            int rawLength = _logicalEnd - oldCursor;
            logical.AddRange(_original.AsSpan(oldCursor, rawLength).ToArray());
            map.Add(new MapSegment(oldCursor, _logicalEnd, newCursor, checked(newCursor + rawLength), false));
            newCursor += rawLength;
        }

        int required = checked(logical.Count + 16);
        int newLength = Math.Max(_original.Length, BinaryUtilities.Align(required, Profile.FileAlignment));
        if (newLength > limits.MaximumInputBytes || newLength > int.MaxValue)
        {
            throw new ToolkitException("SCRIPT_OUTPUT_LIMIT", $"Rebuilt script size {newLength} exceeds configured limit.");
        }
        byte[] output = new byte[newLength];
        logical.CopyTo(output, 0);

        for (int i = 0; i < Dialogs.Count; i++)
        {
            uint relative = checked((uint)(MapBoundary(Dialogs[i].AbsoluteStart, map) - Profile.JumpTableOffset));
            BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(checked(_dialogTableAbsolute + i * 4), 4), relative);
        }
        foreach (ScriptChoiceGroup group in ChoiceGroups)
        {
            int row = checked(_choiceTableAbsolute + group.Index * 32);
            foreach (ScriptChoice choice in group.Choices)
            {
                uint relative = checked((uint)(MapBoundary(choice.AbsoluteStart, map) - Profile.JumpTableOffset));
                BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(checked(row + 4 + choice.ChoiceIndex * 4), 4), relative);
            }
        }
        for (int i = 0; i < _jumps.Length; i++)
        {
            uint oldRelative = _jumps[i];
            uint newRelative = oldRelative == 0
                ? 0
                : checked((uint)(MapOffset(checked(Profile.JumpTableOffset + (int)oldRelative), map) - Profile.JumpTableOffset));
            BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(checked(Profile.JumpTableOffset + i * 4), 4), newRelative);
        }
        RgoChecksum.Apply(output);

        LuckyStarScript verification = Parse(output, Profile, limits, true);
        if (verification.Dialogs.Count != Dialogs.Count || verification.ChoiceGroups.Count != ChoiceGroups.Count)
        {
            throw new ToolkitException("SCRIPT_VERIFY_COUNT", "Rebuilt script semantic count verification failed.");
        }
        foreach (ScriptDialog expected in Dialogs)
        {
            ScriptDialog actual = verification.Dialogs[expected.Index];
            ushort[] expectedSpeaker = mutation.SpeakerGlyphs.GetValueOrDefault(expected.Index) ?? expected.SpeakerGlyphs;
            ushort[] expectedMessage = mutation.MessageGlyphs.GetValueOrDefault(expected.Index) ?? expected.MessageGlyphs;
            if (!actual.SpeakerGlyphs.AsSpan().SequenceEqual(expectedSpeaker)
                || !actual.MessageGlyphs.AsSpan().SequenceEqual(expectedMessage)
                || actual.MessageTerminator != expected.MessageTerminator)
            {
                throw new ToolkitException("SCRIPT_VERIFY_DIALOG", $"Rebuilt dialog {expected.Index} failed glyph verification.");
            }
        }
        foreach (ScriptChoiceGroup group in ChoiceGroups)
        {
            foreach (ScriptChoice expected in group.Choices)
            {
                ScriptChoice actual = verification.ChoiceGroups[group.Index].Choices.Single(choice => choice.ChoiceIndex == expected.ChoiceIndex);
                ushort[] expectedGlyphs = mutation.ChoiceGlyphs.GetValueOrDefault((group.Index, expected.ChoiceIndex)) ?? expected.Glyphs;
                if (!actual.Glyphs.AsSpan().SequenceEqual(expectedGlyphs))
                {
                    throw new ToolkitException("SCRIPT_VERIFY_CHOICE", $"Rebuilt choice {group.Index}:{expected.ChoiceIndex} failed glyph verification.");
                }
            }
        }

        return new ScriptBuildResult(output, _original.Length, output.Length,
            Blocks2K(_original.Length), Blocks2K(output.Length));
    }

    /// <summary>
    /// Validates mutation keys while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="mutation">The requested text or binary mutations.</param>
    /// <remarks>Malformed input or violated preconditions are rejected before a persistent output is committed.</remarks>
    private void ValidateMutationKeys(ScriptMutation mutation)
    {
        foreach (int index in mutation.SpeakerGlyphs.Keys.Concat(mutation.MessageGlyphs.Keys))
        {
            if ((uint)index >= (uint)Dialogs.Count)
            {
                throw new ToolkitException("SCRIPT_MUTATION_DIALOG", $"Mutation references missing dialog {index}.");
            }
        }
        foreach ((int group, int choice) in mutation.ChoiceGlyphs.Keys)
        {
            if ((uint)group >= (uint)ChoiceGroups.Count || ChoiceGroups[group].Choices.All(item => item.ChoiceIndex != choice))
            {
                throw new ToolkitException("SCRIPT_MUTATION_CHOICE", $"Mutation references missing choice {group}:{choice}.");
            }
        }
    }

    /// <summary>
    /// Builds dialog while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="id">The numeric resource identifier.</param>
    /// <param name="speaker">The speaker value.</param>
    /// <param name="message">The diagnostic message used when validation fails.</param>
    /// <param name="terminator">The terminator value.</param>
    /// <returns>The resulting binary or typed sequence.</returns>
    /// <remarks>Malformed input or violated preconditions are rejected before a persistent output is committed.</remarks>
    private static byte[] BuildDialog(ushort id, IReadOnlyList<ushort> speaker, IReadOnlyList<ushort> message, ushort terminator)
    {
        if (!MessageEnds.Contains(terminator))
        {
            throw new ToolkitException("SCRIPT_MESSAGE_END", $"Unsupported message terminator 0x{terminator:X4}.");
        }
        byte[] bytes = new byte[checked((3 + speaker.Count + message.Count) * 2)];
        int position = 0;
        WriteUInt16(bytes, ref position, DialogStart);
        WriteUInt16(bytes, ref position, id);
        foreach (ushort glyph in speaker)
        {
            WriteUInt16(bytes, ref position, glyph);
        }
        WriteUInt16(bytes, ref position, SpeakerEnd);
        foreach (ushort glyph in message)
        {
            WriteUInt16(bytes, ref position, glyph);
        }
        return bytes;
    }

    /// <summary>
    /// Builds choice while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="glyphs">The glyphs value.</param>
    /// <returns>The resulting binary or typed sequence.</returns>
    /// <remarks>Malformed input or violated preconditions are rejected before a persistent output is committed.</remarks>
    private static byte[] BuildChoice(IReadOnlyList<ushort> glyphs)
    {
        byte[] bytes = new byte[checked(glyphs.Count * 2)];
        int position = 0;
        foreach (ushort glyph in glyphs)
        {
            WriteUInt16(bytes, ref position, glyph);
        }
        return bytes;
    }

    /// <summary>
    /// Relocates a script segment boundary while preserving whether it lies before or after a text replacement.
    /// </summary>
    /// <param name="oldOffset">The old offset value.</param>
    /// <param name="map">The glyph map used for text conversion.</param>
    /// <returns>The validated operation result.</returns>
    private static int MapBoundary(int oldOffset, IReadOnlyList<MapSegment> map)
    {
        foreach (MapSegment segment in map)
        {
            if (oldOffset == segment.OldStart)
            {
                return segment.NewStart;
            }
            if (oldOffset == segment.OldEnd)
            {
                return segment.NewEnd;
            }
        }
        return MapOffset(oldOffset, map);
    }

    /// <summary>
    /// Relocates an original script offset and rejects ambiguous references inside changed text.
    /// </summary>
    /// <param name="oldOffset">The old offset value.</param>
    /// <param name="map">The glyph map used for text conversion.</param>
    /// <returns>The validated operation result.</returns>
    private static int MapOffset(int oldOffset, IReadOnlyList<MapSegment> map)
    {
        foreach (MapSegment segment in map)
        {
            if (oldOffset < segment.OldStart || oldOffset > segment.OldEnd)
            {
                continue;
            }
            if (oldOffset == segment.OldStart)
            {
                return segment.NewStart;
            }
            if (oldOffset == segment.OldEnd)
            {
                return segment.NewEnd;
            }
            if (segment.Mutable)
            {
                throw new ToolkitException("SCRIPT_JUMP_INSIDE_TEXT", $"A jump target at 0x{oldOffset:X} lies inside translated text and cannot be relocated safely.");
            }
            return checked(segment.NewStart + oldOffset - segment.OldStart);
        }
        throw new ToolkitException("SCRIPT_OFFSET_MAP", $"Cannot map old script offset 0x{oldOffset:X}.");
    }

    /// <summary>
    /// Reads glyphs until while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="data">The binary data to process.</param>
    /// <param name="position">The mutable position value updated by the operation.</param>
    /// <param name="terminator">The terminator value.</param>
    /// <param name="limit">The limit value.</param>
    /// <param name="context">A diagnostic label included in validation errors.</param>
    /// <returns>The resulting binary or typed sequence.</returns>
    private static ushort[] ReadGlyphsUntil(ReadOnlySpan<byte> data, ref int position, ushort terminator, int limit, string context)
    {
        List<ushort> values = [];
        while (true)
        {
            if (position > limit - 2)
            {
                throw new ToolkitException("SCRIPT_GLYPH_EOF", $"{context} reaches checksum without terminator 0x{terminator:X4}.");
            }
            ushort value = ReadUInt16(data, ref position, context);
            if (value == terminator)
            {
                return values.ToArray();
            }
            values.Add(value);
        }
    }

    /// <summary>
    /// Reads u int 16 while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="data">The binary data to process.</param>
    /// <param name="position">The mutable position value updated by the operation.</param>
    /// <param name="context">A diagnostic label included in validation errors.</param>
    /// <returns>The validated operation result.</returns>
    private static ushort ReadUInt16(ReadOnlySpan<byte> data, ref int position, string context)
    {
        if (position < 0 || position > data.Length - 2)
        {
            throw new ToolkitException("SCRIPT_EOF", $"Unexpected end while reading {context} at 0x{position:X}.");
        }
        ushort value = BinaryPrimitives.ReadUInt16LittleEndian(data[position..]);
        position += 2;
        return value;
    }

    /// <summary>
    /// Reads u int 32 while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="data">The binary data to process.</param>
    /// <param name="offset">The zero-based byte or element offset.</param>
    /// <param name="context">A diagnostic label included in validation errors.</param>
    /// <returns>The validated operation result.</returns>
    private static uint ReadUInt32(ReadOnlySpan<byte> data, int offset, string context)
    {
        EnsureRange(data, offset, 4, context);
        return BinaryPrimitives.ReadUInt32LittleEndian(data[offset..]);
    }

    /// <summary>
    /// Writes u int 16 while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="data">The binary data to process.</param>
    /// <param name="position">The mutable position value updated by the operation.</param>
    /// <param name="value">The value to process.</param>
    private static void WriteUInt16(Span<byte> data, ref int position, ushort value)
    {
        BinaryPrimitives.WriteUInt16LittleEndian(data[position..], value);
        position += 2;
    }

    /// <summary>
    /// Adds the scenario jump-table base to a relative offset using checked arithmetic.
    /// </summary>
    /// <param name="relative">The relative value.</param>
    /// <param name="jumpTableOffset">The jump table offset value.</param>
    /// <param name="limit">The limit value.</param>
    /// <param name="context">A diagnostic label included in validation errors.</param>
    /// <returns>The validated operation result.</returns>
    private static int RelativeToAbsolute(uint relative, int jumpTableOffset, int limit, string context)
    {
        long absolute = (long)jumpTableOffset + relative;
        if (absolute < 0 || absolute > limit - 2)
        {
            throw new ToolkitException("SCRIPT_OFFSET", $"{context} relative offset 0x{relative:X} points outside script data.");
        }
        return (int)absolute;
    }

    /// <summary>
    /// Ensures range while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="data">The binary data to process.</param>
    /// <param name="offset">The zero-based byte or element offset.</param>
    /// <param name="length">The number of bytes or elements to process.</param>
    /// <param name="context">A diagnostic label included in validation errors.</param>
    private static void EnsureRange(ReadOnlySpan<byte> data, int offset, int length, string context)
    {
        if (offset < 0 || length < 0 || offset > data.Length - length)
        {
            throw new ToolkitException("SCRIPT_RANGE", $"{context} range [0x{offset:X},0x{offset + length:X}) exceeds script length 0x{data.Length:X}.");
        }
    }

    /// <summary>
    /// Converts a scenario byte count into the required number of 2048-byte allocation blocks.
    /// </summary>
    /// <param name="length">The number of bytes or elements to process.</param>
    /// <returns>The validated operation result.</returns>
    private static int Blocks2K(int length) => checked((length + 2047) / 2048);

    /// <summary>
    /// Defines the supported interval kind values.
    /// </summary>
    private enum IntervalKind
    {
        /// <summary>A dialogue interval containing speaker, message and their structural delimiters.</summary>
        Dialog,
        /// <summary>A choice-text interval ending in its original choice delimiter.</summary>
        Choice
    }
    /// <summary>
    /// Represents immutable interval data exchanged by the toolkit.
    /// </summary>
    /// <param name="Start">The start value used by this model or operation.</param>
    /// <param name="End">The end value used by this model or operation.</param>
    /// <param name="Kind">The kind value used by this model or operation.</param>
    /// <param name="PrimaryIndex">The primary index value used by this model or operation.</param>
    /// <param name="SecondaryIndex">The secondary index value used by this model or operation.</param>
    private sealed record Interval(int Start, int End, IntervalKind Kind, int PrimaryIndex, int SecondaryIndex);
    /// <summary>
    /// Represents immutable replacement data exchanged by the toolkit.
    /// </summary>
    /// <param name="OldStart">The old start value used by this model or operation.</param>
    /// <param name="OldEnd">The old end value used by this model or operation.</param>
    /// <param name="NewBytes">The new bytes value used by this model or operation.</param>
    /// <param name="Name">The name value used by this model or operation.</param>
    private sealed record Replacement(int OldStart, int OldEnd, byte[] NewBytes, string Name);
    /// <summary>
    /// Represents immutable map segment data exchanged by the toolkit.
    /// </summary>
    /// <param name="OldStart">The old start value used by this model or operation.</param>
    /// <param name="OldEnd">The old end value used by this model or operation.</param>
    /// <param name="NewStart">The new start value used by this model or operation.</param>
    /// <param name="NewEnd">The new end value used by this model or operation.</param>
    /// <param name="Mutable">The mutable value used by this model or operation.</param>
    private sealed record MapSegment(int OldStart, int OldEnd, int NewStart, int NewEnd, bool Mutable);
}
