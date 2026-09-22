using System.Buffers.Binary;
using LuckyStarPspToolkit.Formats.Common;
using LuckyStarPspToolkit.Formats.Scripts;

/// <summary>Regression tests for bounded scenario parsing, genuine no-op rebuilds and indexed jump relocation.</summary>
internal static partial class SelfTestRunner
{
    /// <summary>Preserves byte-identical no-ops and interior targets in text not changed by another dialogue's mutation.</summary>
    private static void TestScriptUnchangedJump()
    {
        byte[] data = ScriptScaleFixture.Create(2);
        int jumpBase = ScriptProfile.Rgo.JumpTableOffset;
        uint dialogue = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(0x90, 4));
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(jumpBase + 8, 4), dialogue + 8);
        RgoChecksum.Apply(data);
        LuckyStarScript script = LuckyStarScript.Parse(data, ScriptProfile.Rgo);
        SequenceEqual(data, script.Build(new ScriptMutation()).Data);
        ScriptMutation identical = new();
        identical.MessageGlyphs[0] = script.Dialogs[0].MessageGlyphs.ToArray();
        SequenceEqual(data, script.Build(identical).Data);
        ScriptMutation other = new();
        other.MessageGlyphs[1] = [3, 5, 6];
        LuckyStarScript rebuilt = LuckyStarScript.Parse(script.Build(other).Data, ScriptProfile.Rgo);
        Equal(dialogue + 8, rebuilt.Jumps[2]);
        SequenceEqual(script.Dialogs[0].MessageGlyphs, rebuilt.Dialogs[0].MessageGlyphs);
        other.MessageGlyphs[0] = [7, 8];
        Throws("SCRIPT_JUMP_INSIDE_TEXT", () => script.Build(other));
        Equal(BinaryUtilities.Sha256Hex(data), script.Sha256);
    }

    /// <summary>Rejects a message without a delimiter even if the valid checksum happens to begin with a delimiter word.</summary>
    private static void TestScriptChecksumNotText()
    {
        byte[] data = ScriptScaleFixture.MessageIntoChecksum();
        True(RgoChecksum.Verify(data), "The regression must have a valid checksum.");
        Equal((ushort)0xFFFB, BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(data.Length - 16, 2)));
        Throws("SCRIPT_GLYPH_EOF", () => LuckyStarScript.Parse(data, ScriptProfile.Rgo));
        Throws("SCRIPT_GLYPH_EOF", () => LuckyStarScript.Parse(data, ScriptProfile.Rgo, requireChecksum: false));
    }

    /// <summary>Stops scanning before the next record, rather than allocating text from overlapping records.</summary>
    private static void TestScriptPhysicalBoundaries()
    {
        byte[] source = ScriptScaleFixture.Create(2);
        LuckyStarScript parsed = LuckyStarScript.Parse(source, ScriptProfile.Rgo);
        int end = parsed.Dialogs[0].AbsoluteEnd;
        BinaryPrimitives.WriteUInt16LittleEndian(source.AsSpan(end, 2), 0x1234);
        RgoChecksum.Apply(source);
        Throws("SCRIPT_GLYPH_EOF", () => LuckyStarScript.Parse(source, ScriptProfile.Rgo));
    }

    /// <summary>Checks metadata range overflow, overlap, alignment and jump-table boundaries before narrowing offsets.</summary>
    private static void TestScriptMetadataPreflight()
    {
        byte[] source = File.ReadAllBytes(Path.Combine(Fixtures, "reference-script.bin"));
        foreach (uint relative in new[] { uint.MaxValue, 0x11U, (uint)ScriptProfile.Rgo.JumpTableOffset })
        {
            byte[] changed = source.ToArray();
            BinaryPrimitives.WriteUInt32LittleEndian(changed.AsSpan(0x80, 4), relative);
            RgoChecksum.Apply(changed);
            Throws("SCRIPT_TABLE_RANGE", () => LuckyStarScript.Parse(changed, ScriptProfile.Rgo));
        }
        byte[] overlap = source.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(overlap.AsSpan(0x88, 4), 0x10);
        RgoChecksum.Apply(overlap);
        Throws("SCRIPT_TABLE_OVERLAP", () => LuckyStarScript.Parse(overlap, ScriptProfile.Rgo));
        byte[] jumpIntoChecksum = source.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(jumpIntoChecksum.AsSpan(ScriptProfile.Rgo.JumpTableOffset, 4),
            (uint)(jumpIntoChecksum.Length - 16 - ScriptProfile.Rgo.JumpTableOffset));
        RgoChecksum.Apply(jumpIntoChecksum);
        Throws("SCRIPT_JUMP_RANGE", () => LuckyStarScript.Parse(jumpIntoChecksum, ScriptProfile.Rgo));
        Throws("SCRIPT_PROFILE", () => LuckyStarScript.Parse(source, new ScriptProfile(LuckyStarGame.NetIdolMeister, -4, 4096)));
        Throws("SCRIPT_PROFILE", () => LuckyStarScript.Parse(source, ScriptProfile.Rgo with { FileAlignment = 3 }));
        Throws("SCRIPT_LIMITS", () => LuckyStarScript.Parse(source, ScriptProfile.Rgo, new FileLimits(MaximumScriptGlyphs: -1)));
    }

    /// <summary>Rejects duplicate or misaligned text/jump offsets before repeated text materialization is possible.</summary>
    private static void TestScriptOffsetPreflight()
    {
        byte[] source = File.ReadAllBytes(Path.Combine(Fixtures, "reference-script.bin"));
        uint first = BinaryPrimitives.ReadUInt32LittleEndian(source.AsSpan(0x90, 4));
        byte[] duplicate = source.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(duplicate.AsSpan(0x94, 4), first);
        RgoChecksum.Apply(duplicate);
        Throws("SCRIPT_DUPLICATE_TEXT_OFFSET", () => LuckyStarScript.Parse(duplicate, ScriptProfile.Rgo));
        duplicate = source.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(duplicate.AsSpan(0xA4, 4), first);
        RgoChecksum.Apply(duplicate);
        Throws("SCRIPT_DUPLICATE_TEXT_OFFSET", () => LuckyStarScript.Parse(duplicate, ScriptProfile.Rgo));
        foreach (int field in new[] { 0x90, ScriptProfile.Rgo.JumpTableOffset + 8 })
        {
            byte[] unaligned = source.ToArray();
            BinaryPrimitives.WriteUInt32LittleEndian(unaligned.AsSpan(field, 4), first + 1);
            RgoChecksum.Apply(unaligned);
            Throws("SCRIPT_OFFSET", () => LuckyStarScript.Parse(unaligned, ScriptProfile.Rgo));
        }
    }

    /// <summary>Exercises exact limits and one-past limits for input bytes, individual fields and aggregate decoded glyphs.</summary>
    private static void TestScriptGlyphBudgets()
    {
        byte[] data = File.ReadAllBytes(Path.Combine(Fixtures, "reference-script.bin"));
        LuckyStarScript.Parse(data, ScriptProfile.Rgo, new FileLimits(MaximumScriptFieldGlyphs: 2, MaximumScriptGlyphs: 8));
        Throws("SCRIPT_FIELD_LIMIT", () => LuckyStarScript.Parse(data, ScriptProfile.Rgo, new FileLimits(MaximumScriptFieldGlyphs: 1)));
        Throws("SCRIPT_GLYPH_LIMIT", () => LuckyStarScript.Parse(data, ScriptProfile.Rgo, new FileLimits(MaximumScriptGlyphs: 7)));
        Throws("SCRIPT_INPUT_LIMIT", () => LuckyStarScript.Parse(data, ScriptProfile.Rgo, new FileLimits(MaximumScriptBytes: data.Length - 1)));
        Throws("SCRIPT_INPUT_LIMIT", () => LuckyStarScript.Parse(data, ScriptProfile.Rgo, new FileLimits(MaximumInputBytes: data.Length - 1)));
    }

    /// <summary>Proves an oversized rebuild is rejected before allocating a serialized copy of the large replacement.</summary>
    private static void TestScriptOutputPreflight()
    {
        byte[] source = ScriptScaleFixture.Create(2);
        LuckyStarScript script = LuckyStarScript.Parse(source, ScriptProfile.Rgo);
        ScriptMutation mutation = new();
        mutation.MessageGlyphs[0] = Enumerable.Repeat((ushort)3, 200_000).ToArray();
        FileLimits limits = new(MaximumScriptBytes: source.Length);
        Throws("SCRIPT_OUTPUT_LIMIT", () => script.Build(mutation, limits)); // Warm exception path.
        long before = GC.GetAllocatedBytesForCurrentThread();
        Throws("SCRIPT_OUTPUT_LIMIT", () => script.Build(mutation, limits));
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Console.WriteLine($"SCRIPT LIMIT PREFLIGHT: allocated {allocated} managed bytes for a rejected 400000-byte replacement (caller array excluded).");
        True(allocated < 64_000, "Rejection allocated the large serialized replacement before checking the output budget.");
        Throws("SCRIPT_FIELD_LIMIT", () => script.Build(mutation, new FileLimits(MaximumScriptFieldGlyphs: 199_999)));
        Throws("SCRIPT_GLYPH_LIMIT", () => script.Build(new ScriptMutation(), new FileLimits(MaximumScriptGlyphs: 0)));
        Throws("SCRIPT_BUILD_COUNT_LIMIT", () => script.Build(new ScriptMutation(), new FileLimits(MaximumScriptDialogs: 1)));
    }

    /// <summary>Blocks direct API delimiter injection, null mutations and nonexistent indices without relying on workspace validation.</summary>
    private static void TestScriptDirectMutationSafety()
    {
        LuckyStarScript script = LuckyStarScript.Parse(File.ReadAllBytes(Path.Combine(Fixtures, "reference-script.bin")), ScriptProfile.Rgo);
        foreach (ushort delimiter in new ushort[] { 0xFFFF, 0xFFFB, 0xFFFD })
        {
            ScriptMutation mutation = new();
            mutation.MessageGlyphs[0] = [delimiter];
            Throws("SCRIPT_MUTATION_TERMINATOR", () => script.Build(mutation));
        }
        ScriptMutation speaker = new(); speaker.SpeakerGlyphs[0] = [0xFFFF];
        Throws("SCRIPT_MUTATION_TERMINATOR", () => script.Build(speaker));
        ScriptMutation choice = new(); choice.ChoiceGlyphs[(0, 0)] = [0xFFFF];
        Throws("SCRIPT_MUTATION_TERMINATOR", () => script.Build(choice));
        ScriptMutation nil = new(); nil.MessageGlyphs[0] = null!;
        Throws("SCRIPT_MUTATION_NULL", () => script.Build(nil));
        ScriptMutation absent = new(); absent.ChoiceGlyphs[(-1, 0)] = [1];
        Throws("SCRIPT_MUTATION_CHOICE", () => script.Build(absent));
    }

    /// <summary>Preserves empty-choice anchors on insertion and keeps physically reordered dialogues in metadata order.</summary>
    private static void TestScriptEmptyAndReordered()
    {
        byte[] data = File.ReadAllBytes(Path.Combine(Fixtures, "reference-script.bin"));
        int jump = ScriptProfile.Rgo.JumpTableOffset;
        uint choiceOffset = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(0xA4, 4));
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(jump + (int)choiceOffset, 2), 0xFFFF);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(jump + 4, 4), choiceOffset);
        RgoChecksum.Apply(data);
        LuckyStarScript empty = LuckyStarScript.Parse(data, ScriptProfile.Rgo);
        SequenceEqual(data, empty.Build(new ScriptMutation()).Data);
        ScriptMutation insertion = new(); insertion.ChoiceGlyphs[(0, 0)] = [3, 4, 5];
        LuckyStarScript changed = LuckyStarScript.Parse(empty.Build(insertion).Data, ScriptProfile.Rgo);
        SequenceEqual(new ushort[] { 3, 4, 5 }, changed.ChoiceGroups[0].Choices[0].Glyphs);
        Equal(changed.ChoiceGroups[0].Choices[0].RelativeOffset, changed.Jumps[1]);

        byte[] reordered = ScriptScaleFixture.Create(2);
        uint a = BinaryPrimitives.ReadUInt32LittleEndian(reordered.AsSpan(0x90, 4));
        uint b = BinaryPrimitives.ReadUInt32LittleEndian(reordered.AsSpan(0x94, 4));
        BinaryPrimitives.WriteUInt32LittleEndian(reordered.AsSpan(0x90, 4), b);
        BinaryPrimitives.WriteUInt32LittleEndian(reordered.AsSpan(0x94, 4), a);
        RgoChecksum.Apply(reordered);
        LuckyStarScript parsed = LuckyStarScript.Parse(reordered, ScriptProfile.Rgo);
        Equal((ushort)1, parsed.Dialogs[0].Id);
        ScriptMutation edit = new(); edit.MessageGlyphs[0] = [5, 6];
        LuckyStarScript rebuilt = LuckyStarScript.Parse(parsed.Build(edit).Data, ScriptProfile.Rgo);
        Equal((ushort)1, rebuilt.Dialogs[0].Id);
        SequenceEqual(new ushort[] { 5, 6 }, rebuilt.Dialogs[0].MessageGlyphs);
    }

    /// <summary>Covers opaque zero-dialogue scenarios, NIM layout and no-op files that are only 16-byte aligned.</summary>
    private static void TestScriptOpaqueAndNim()
    {
        foreach (ScriptProfile profile in new[] { ScriptProfile.Rgo, ScriptProfile.Nim })
        {
            byte[] source = ScriptScaleFixture.Create(0, profile);
            LuckyStarScript script = LuckyStarScript.Parse(source, profile);
            SequenceEqual(source, script.Build(new ScriptMutation()).Data);
            byte[] shortFile = source.AsSpan(0, profile.JumpTableOffset + 32).ToArray();
            RgoChecksum.Apply(shortFile);
            LuckyStarScript shorter = LuckyStarScript.Parse(shortFile, profile);
            SequenceEqual(shortFile, shorter.Build(new ScriptMutation(), new FileLimits(MaximumScriptBytes: shortFile.Length)).Data);
        }
        byte[] nim = ScriptScaleFixture.Create(16, ScriptProfile.Nim);
        LuckyStarScript rebuilt = LuckyStarScript.Parse(
            LuckyStarScript.Parse(nim, ScriptProfile.Nim).Build(ScriptScaleFixture.Mutations(16)).Data, ScriptProfile.Nim);
        Equal(16, rebuilt.Dialogs.Count);
        Equal(10, rebuilt.Dialogs[0].MessageGlyphs.Length);
    }

    /// <summary>Compares every query against an independent linear map, including zero-length insertions and changed interiors.</summary>
    private static void TestScriptIndexedMap()
    {
        Random random = new(12571);
        List<ScriptOffsetSegment> segments = [];
        int old = 100;
        int next = 200;
        for (int i = 0; i < 512; i++)
        {
            int oldLength = random.Next(0, 17);
            bool changed = random.Next(0, 3) == 0;
            int newLength = changed ? random.Next(0, 21) : oldLength;
            segments.Add(new ScriptOffsetSegment(old, old + oldLength, next, next + newLength, changed));
            old += oldLength; next += newLength;
        }
        ScriptOffsetMap map = new(segments);
        int queries = 0;
        for (int offset = 100; offset <= old; offset++)
        {
            ScriptOffsetSegment expected = segments.First(s => offset >= s.OldStart && offset <= s.OldEnd);
            if (offset == expected.OldStart) Equal(expected.NewStart, map.Translate(offset));
            else if (offset == expected.OldEnd) Equal(expected.NewEnd, map.Translate(offset));
            else if (expected.Changed) Throws("SCRIPT_JUMP_INSIDE_TEXT", () => map.Translate(offset));
            else Equal(expected.NewStart + offset - expected.OldStart, map.Translate(offset));
            queries++;
        }
        Throws("SCRIPT_OFFSET_MAP", () => map.Translate(99));
        Throws("SCRIPT_OFFSET_MAP", () => map.Translate(old + 1));
        Throws("SCRIPT_OFFSET_MAP", () => new ScriptOffsetMap([]));
        Throws("SCRIPT_OFFSET_MAP", () => new ScriptOffsetMap([new(0, 2, 0, 3, false)]));
        Throws("SCRIPT_OFFSET_MAP", () => new ScriptOffsetMap([new(0, 2, 0, 2, false), new(3, 4, 2, 3, false)]));
        Console.WriteLine($"SCRIPT MAP ORACLE: {queries} queries agree with the linear reference.");
    }

    /// <summary>Rebuilds 8192 dialogues and verifies every relocated start/end target plus every opaque command word.</summary>
    private static void TestScriptLargeRebuild()
    {
        const int count = 8192;
        byte[] source = ScriptScaleFixture.Create(count);
        string sourceHash = BinaryUtilities.Sha256Hex(source);
        LuckyStarScript original = LuckyStarScript.Parse(source, ScriptProfile.Rgo);
        byte[] result = original.Build(ScriptScaleFixture.Mutations(count)).Data;
        LuckyStarScript parsed = LuckyStarScript.Parse(result, ScriptProfile.Rgo);
        for (int i = 0; i < count; i++)
        {
            ScriptDialog dialogue = parsed.Dialogs[i];
            Equal((ushort)i, dialogue.Id);
            Equal(i % 2 == 0 ? 10 : 8, dialogue.MessageGlyphs.Length);
            Equal(dialogue.RelativeOffset, parsed.Jumps[i * 2 + 1]);
            Equal((uint)(dialogue.AbsoluteEnd - ScriptProfile.Rgo.JumpTableOffset), parsed.Jumps[i * 2 + 2]);
            Equal((ushort)(0x9000 + i % 1024), BinaryPrimitives.ReadUInt16LittleEndian(result.AsSpan(dialogue.AbsoluteEnd + 2, 2)));
        }
        Equal(sourceHash, original.Sha256);
        Equal(sourceHash, BinaryUtilities.Sha256Hex(source));
        Console.WriteLine($"SCRIPT SCALE: {count} dialogues, {count * 2 + 1} targets, {count / 2} changes verified.");
    }

    /// <summary>Mutates metadata and targets under bounded limits; accepted inputs must rebuild byte-identically and all rejections must be typed.</summary>
    private static void TestScriptHeaderFuzz()
    {
        byte[] source = ScriptScaleFixture.Create(8);
        int[] fields = [0x80, 0x84, 0x88, 0x8C, 0x90, 0x94, ScriptProfile.Rgo.JumpTableOffset, ScriptProfile.Rgo.JumpTableOffset + 8];
        Random random = new(981125);
        int rejected = 0;
        int accepted = 0;
        for (int i = 0; i < 1024; i++)
        {
            byte[] data = source.ToArray();
            int field = fields[i % fields.Length];
            uint value = i % 4 == 0 ? (uint)random.Next(0, 512) : unchecked((uint)random.NextInt64(0, (long)uint.MaxValue + 1));
            // Mix structurally legal count reductions with adversarial offsets, not only guaranteed-invalid bytes.
            if (i % 8 == 1 && i % 3 == 0) value = (uint)random.Next(0, 9);
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(field, 4), value);
            RgoChecksum.Apply(data);
            try
            {
                LuckyStarScript parsed = LuckyStarScript.Parse(data, ScriptProfile.Rgo,
                    new FileLimits(MaximumScriptDialogs: 4096, MaximumScriptChoiceGroups: 512,
                        MaximumScriptJumps: 8192, MaximumScriptFieldGlyphs: 4096, MaximumScriptGlyphs: 65_536));
                SequenceEqual(data, parsed.Build(new ScriptMutation()).Data);
                accepted++;
            }
            catch (ToolkitException) { rejected++; }
        }
        Equal(1024, accepted + rejected);
        Console.WriteLine($"SCRIPT HEADER FUZZ: {accepted} valid, {rejected} typed rejections, 0 unhandled exceptions.");
    }
}
