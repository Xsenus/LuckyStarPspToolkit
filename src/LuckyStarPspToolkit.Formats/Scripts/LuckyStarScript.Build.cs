using System.Buffers.Binary;
using LuckyStarPspToolkit.Formats.Common;

namespace LuckyStarPspToolkit.Formats.Scripts;

/// <summary>Preflights scenario changes, writes one result buffer and verifies its decoded semantics.</summary>
public sealed partial class LuckyStarScript
{
    /// <summary>
    /// Rebuilds only genuinely changed text records. Unchanged text remains opaque and may
    /// contain jump targets. All size/glyph budgets are checked before result allocation;
    /// metadata and jumps use a binary-search relocation index, and output is reparsed.
    /// </summary>
    /// <param name="mutation">Explicit replacement glyph arrays keyed by original table indices; do not mutate concurrently.</param>
    /// <param name="limits">Output and decoded-text limits, also applied to verification.</param>
    /// <returns>A new byte buffer and old/new 2048-byte block counts; the input snapshot is never modified.</returns>
    /// <exception cref="ToolkitException">A mutation is invalid, a size limit is exceeded or a target inside changed text is ambiguous.</exception>
    public ScriptBuildResult Build(ScriptMutation mutation, FileLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(mutation);
        limits ??= FileLimits.Default;
        ValidateConfiguration(Profile, limits);
        ValidateMutationKeys(mutation);
        if (Dialogs.Count > limits.MaximumScriptDialogs || ChoiceGroups.Count > limits.MaximumScriptChoiceGroups
            || _jumps.Length > limits.MaximumScriptJumps)
            throw new ToolkitException("SCRIPT_BUILD_COUNT_LIMIT", "The retained scenario exceeds the requested build count budgets.");

        List<Replacement> replacements = [];
        long remainingGlyphs = limits.MaximumScriptGlyphs;
        long delta = 0;
        foreach (ScriptDialog dialog in Dialogs)
        {
            ushort[] speaker = mutation.SpeakerGlyphs.GetValueOrDefault(dialog.Index) ?? dialog.SpeakerGlyphs;
            ushort[] message = mutation.MessageGlyphs.GetValueOrDefault(dialog.Index) ?? dialog.MessageGlyphs;
            ValidateField(speaker, false, limits, ref remainingGlyphs);
            ValidateField(message, true, limits, ref remainingGlyphs);
            long length = (3L + speaker.Length + message.Length) * 2;
            if (length != dialog.AbsoluteEnd - dialog.AbsoluteStart
                || !OriginalWordsEqual(dialog.AbsoluteStart + 4, speaker)
                || !OriginalWordsEqual(dialog.AbsoluteStart + 6 + speaker.Length * 2, message))
            {
                int byteLength = Guard.CheckedInt(length, "SCRIPT_OUTPUT_LIMIT", "dialogue length");
                replacements.Add(new Replacement(dialog.AbsoluteStart, dialog.AbsoluteEnd, dialog.Id,
                    speaker, message, byteLength, $"dialogue {dialog.Index}"));
                delta += length - (dialog.AbsoluteEnd - dialog.AbsoluteStart);
            }
        }
        foreach (ScriptChoiceGroup group in ChoiceGroups)
        {
            foreach (ScriptChoice choice in group.Choices)
            {
                ushort[] glyphs = mutation.ChoiceGlyphs.GetValueOrDefault((group.Index, choice.ChoiceIndex)) ?? choice.Glyphs;
                ValidateField(glyphs, false, limits, ref remainingGlyphs);
                long length = (long)glyphs.Length * 2;
                if (length != choice.AbsoluteEnd - choice.AbsoluteStart || !OriginalWordsEqual(choice.AbsoluteStart, glyphs))
                {
                    int byteLength = Guard.CheckedInt(length, "SCRIPT_OUTPUT_LIMIT", "choice length");
                    replacements.Add(new Replacement(choice.AbsoluteStart, choice.AbsoluteEnd, 0,
                        null, glyphs, byteLength, $"choice {group.Index}:{choice.ChoiceIndex}"));
                    delta += length - (choice.AbsoluteEnd - choice.AbsoluteStart);
                }
            }
        }

        long required = (long)_logicalEnd + delta + 16;
        long aligned = BinaryUtilities.Align(required, Profile.FileAlignment);
        long outputLength = replacements.Count == 0 ? _original.Length : Math.Max(_original.Length, aligned);
        if (outputLength > limits.MaximumScriptBytes || outputLength > limits.MaximumInputBytes || outputLength > int.MaxValue)
            throw new ToolkitException("SCRIPT_OUTPUT_LIMIT", $"Rebuilt scenario size {outputLength} exceeds the configured output budget.");
        if (replacements.Count == 0)
        {
            // A lossless no-op must not reinterpret text-internal jumps or discard padding.
            byte[] unchanged = _original.ToArray();
            RgoChecksum.Apply(unchanged);
            return CreateResult(unchanged);
        }

        replacements.Sort(static (a, b) => a.OldStart.CompareTo(b.OldStart));
        byte[] output = new byte[(int)outputLength];
        _original.AsSpan(0, _contentStart).CopyTo(output);
        List<ScriptOffsetSegment> segments = new(replacements.Count * 2 + 1);
        int oldCursor = _contentStart;
        int newCursor = _contentStart;
        foreach (Replacement replacement in replacements)
        {
            if (replacement.OldStart < oldCursor || replacement.OldEnd > _logicalEnd)
                throw new ToolkitException("SCRIPT_REPLACEMENT_OVERLAP", $"Invalid interval for {replacement.Name}.");
            if (replacement.OldStart != oldCursor)
            {
                int rawLength = replacement.OldStart - oldCursor;
                _original.AsSpan(oldCursor, rawLength).CopyTo(output.AsSpan(newCursor));
                segments.Add(new ScriptOffsetSegment(oldCursor, replacement.OldStart, newCursor, newCursor + rawLength, false));
                newCursor += rawLength;
            }
            WriteReplacement(output.AsSpan(newCursor, replacement.ByteLength), replacement);
            segments.Add(new ScriptOffsetSegment(replacement.OldStart, replacement.OldEnd,
                newCursor, newCursor + replacement.ByteLength, true));
            newCursor += replacement.ByteLength;
            oldCursor = replacement.OldEnd;
        }
        if (oldCursor < _logicalEnd)
        {
            int rawLength = _logicalEnd - oldCursor;
            _original.AsSpan(oldCursor, rawLength).CopyTo(output.AsSpan(newCursor));
            segments.Add(new ScriptOffsetSegment(oldCursor, _logicalEnd, newCursor, newCursor + rawLength, false));
            newCursor += rawLength;
        }
        if (newCursor != required - 16)
            throw new ToolkitException("SCRIPT_OUTPUT_SIZE", "Emitted content does not match its preflight byte count.");
        ScriptOffsetMap map = new(segments);
        for (int i = 0; i < Dialogs.Count; i++)
        {
            uint relative = (uint)(map.Translate(Dialogs[i].AbsoluteStart) - Profile.JumpTableOffset);
            BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(_dialogTableAbsolute + i * 4, 4), relative);
        }
        foreach (ScriptChoiceGroup group in ChoiceGroups)
        {
            int row = _choiceTableAbsolute + group.Index * 32;
            foreach (ScriptChoice choice in group.Choices)
            {
                uint relative = (uint)(map.Translate(choice.AbsoluteStart) - Profile.JumpTableOffset);
                BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(row + 4 + choice.ChoiceIndex * 4, 4), relative);
            }
        }
        for (int i = 0; i < _jumps.Length; i++)
        {
            uint relative = _jumps[i] == 0 ? 0 : (uint)(map.Translate(Profile.JumpTableOffset + (int)_jumps[i]) - Profile.JumpTableOffset);
            BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(Profile.JumpTableOffset + i * 4, 4), relative);
        }
        RgoChecksum.Apply(output);
        VerifyRebuild(output, mutation, limits);
        return CreateResult(output);
    }

    /// <summary>Checks reconstructed IDs, glyphs, terminators and choice counts after a full independent parse of the result.</summary>
    /// <param name="output">Candidate rebuilt scenario, with fresh checksum.</param>
    /// <param name="mutation">Expected explicit text changes.</param>
    /// <param name="limits">Limits for the verification parser.</param>
    /// <exception cref="ToolkitException">Reparsing fails or an expected semantic value changed.</exception>
    private void VerifyRebuild(byte[] output, ScriptMutation mutation, FileLimits limits)
    {
        LuckyStarScript verification = Parse(output, Profile, limits);
        if (verification.Dialogs.Count != Dialogs.Count || verification.ChoiceGroups.Count != ChoiceGroups.Count)
            throw new ToolkitException("SCRIPT_VERIFY_COUNT", "Rebuilt scenario counts differ from the input.");
        foreach (ScriptDialog expected in Dialogs)
        {
            ScriptDialog actual = verification.Dialogs[expected.Index];
            ushort[] speaker = mutation.SpeakerGlyphs.GetValueOrDefault(expected.Index) ?? expected.SpeakerGlyphs;
            ushort[] message = mutation.MessageGlyphs.GetValueOrDefault(expected.Index) ?? expected.MessageGlyphs;
            if (actual.Id != expected.Id || actual.MessageTerminator != expected.MessageTerminator
                || !actual.SpeakerGlyphs.AsSpan().SequenceEqual(speaker) || !actual.MessageGlyphs.AsSpan().SequenceEqual(message))
                throw new ToolkitException("SCRIPT_VERIFY_DIALOG", $"Rebuilt dialogue {expected.Index} failed semantic verification.");
        }
        foreach (ScriptChoiceGroup group in ChoiceGroups)
        {
            ScriptChoiceGroup actualGroup = verification.ChoiceGroups[group.Index];
            if (actualGroup.JumpId != group.JumpId || actualGroup.Choices.Count != group.Choices.Count)
                throw new ToolkitException("SCRIPT_VERIFY_CHOICE", $"Rebuilt choice group {group.Index} changed structure.");
            foreach (ScriptChoice expected in group.Choices)
            {
                ScriptChoice actual = actualGroup.Choices[expected.ChoiceIndex];
                ushort[] glyphs = mutation.ChoiceGlyphs.GetValueOrDefault((group.Index, expected.ChoiceIndex)) ?? expected.Glyphs;
                if (!actual.Glyphs.AsSpan().SequenceEqual(glyphs))
                    throw new ToolkitException("SCRIPT_VERIFY_CHOICE", $"Rebuilt choice {group.Index}:{expected.ChoiceIndex} changed text.");
            }
        }
    }

    /// <summary>Rejects missing indices and null array values before planning any output.</summary>
    /// <param name="mutation">The caller's explicit edits; absent keys mean preserve source.</param>
    /// <exception cref="ToolkitException">A key does not exist or its value is null.</exception>
    private void ValidateMutationKeys(ScriptMutation mutation)
    {
        foreach (int index in mutation.SpeakerGlyphs.Keys.Concat(mutation.MessageGlyphs.Keys))
            if ((uint)index >= (uint)Dialogs.Count)
                throw new ToolkitException("SCRIPT_MUTATION_DIALOG", $"No dialogue exists at index {index}.");
        foreach ((int group, int choice) in mutation.ChoiceGlyphs.Keys)
            if ((uint)group >= (uint)ChoiceGroups.Count || choice < 0 || choice >= ChoiceGroups[group].Choices.Count)
                throw new ToolkitException("SCRIPT_MUTATION_CHOICE", $"No choice exists at {group}:{choice}.");
        if (mutation.SpeakerGlyphs.Values.Any(static v => v is null)
            || mutation.MessageGlyphs.Values.Any(static v => v is null)
            || mutation.ChoiceGlyphs.Values.Any(static v => v is null))
            throw new ToolkitException("SCRIPT_MUTATION_NULL", "Use an absent key to preserve a field, not a null glyph array.");
    }

    /// <summary>Accounts for one output field and blocks terminators injected as text.</summary>
    /// <param name="glyphs">Requested or retained glyph indices.</param>
    /// <param name="message">Whether message-ending codes must also be rejected.</param>
    /// <param name="limits">Per-field glyph count bound.</param>
    /// <param name="remainingGlyphs">Shared total budget, consumed without allocating a serialized copy.</param>
    /// <exception cref="ToolkitException">A budget is exhausted or structural text contains a delimiter.</exception>
    private static void ValidateField(ushort[] glyphs, bool message, FileLimits limits, ref long remainingGlyphs)
    {
        if (glyphs.Length > limits.MaximumScriptFieldGlyphs)
            throw new ToolkitException("SCRIPT_FIELD_LIMIT", "A replacement field exceeds its glyph budget.");
        if (glyphs.Length > remainingGlyphs)
            throw new ToolkitException("SCRIPT_GLYPH_LIMIT", "Rebuilt text exceeds the scenario's total glyph budget.");
        remainingGlyphs -= glyphs.Length;
        foreach (ushort glyph in glyphs)
            if (message ? IsMessageEnd(glyph) : glyph == SpeakerEnd)
                throw new ToolkitException("SCRIPT_MUTATION_TERMINATOR", $"Structural delimiter 0x{glyph:X4} cannot be used as text.");
    }

    /// <summary>Compares requested glyph words directly with the private source snapshot, without serializing an intermediate array.</summary>
    /// <param name="offset">Absolute first original word position.</param>
    /// <param name="words">Expected glyph words.</param>
    /// <returns>True when every original little-endian word is equal and the entire range exists.</returns>
    private bool OriginalWordsEqual(int offset, ushort[] words)
    {
        if (offset < 0 || (long)offset + (long)words.Length * 2 > _original.Length) return false;
        for (int i = 0; i < words.Length; i++)
            if (BinaryPrimitives.ReadUInt16LittleEndian(_original.AsSpan(offset + i * 2, 2)) != words[i]) return false;
        return true;
    }

    /// <summary>Writes one planned interval directly into its final output slice; the original final delimiter stays in the following raw segment.</summary>
    /// <param name="destination">Exactly the planned byte range.</param>
    /// <param name="replacement">Validated replacement arrays and optional dialogue speaker.</param>
    private static void WriteReplacement(Span<byte> destination, Replacement replacement)
    {
        int position = 0;
        if (replacement.Speaker is not null)
        {
            WriteWord(destination, ref position, DialogStart);
            WriteWord(destination, ref position, replacement.DialogId);
            foreach (ushort glyph in replacement.Speaker) WriteWord(destination, ref position, glyph);
            WriteWord(destination, ref position, SpeakerEnd);
        }
        foreach (ushort glyph in replacement.Glyphs) WriteWord(destination, ref position, glyph);
    }

    /// <summary>Writes one little-endian word to a preflight-sized output slice.</summary>
    /// <param name="destination">Validated output slice.</param>
    /// <param name="position">Cursor advanced by two bytes.</param>
    /// <param name="value">Word to serialize.</param>
    private static void WriteWord(Span<byte> destination, ref int position, ushort value)
    {
        BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(position, 2), value);
        position += 2;
    }

    /// <summary>Describes a completed in-memory rebuild using overflow-safe block rounding.</summary>
    /// <param name="output">Owned output array to return to the caller.</param>
    /// <returns>Output bytes and old/new lengths in bytes and 2048-byte blocks.</returns>
    private ScriptBuildResult CreateResult(byte[] output) => new(output, _original.Length, output.Length,
        (int)(((long)_original.Length + 2047) / 2048), (int)(((long)output.Length + 2047) / 2048));

    /// <summary>A lightweight replacement plan; text is not serialized until the full output size is checked.</summary>
    /// <param name="OldStart">First replaced byte.</param>
    /// <param name="OldEnd">Exclusive old end, before the original terminal word.</param>
    /// <param name="DialogId">Original dialogue ID; unused for choices.</param>
    /// <param name="Speaker">Speaker glyphs for a dialogue; null denotes a choice.</param>
    /// <param name="Glyphs">Message or choice glyphs.</param>
    /// <param name="ByteLength">Checked serialized length, without the retained terminal word.</param>
    /// <param name="Name">Diagnostic name of the replacement.</param>
    private sealed record Replacement(int OldStart, int OldEnd, ushort DialogId, ushort[]? Speaker,
        ushort[] Glyphs, int ByteLength, string Name);
}
