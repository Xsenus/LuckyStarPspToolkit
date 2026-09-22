using System.Buffers.Binary;
using System.Diagnostics;
using System.Text.Json;
using LuckyStarPspToolkit.Formats.Common;
using LuckyStarPspToolkit.Formats.Scripts;

/// <summary>Creates synthetic scenarios and measures the same public Parse/Build API across revisions; no game resources are used.</summary>
internal static class ScriptScaleFixture
{
    /// <summary>Builds a deterministic scenario using fixed offsets, independent of LuckyStarScript.Build.</summary>
    /// <param name="dialogCount">Number of dialogues; their tables must fit the selected metadata prefix.</param>
    /// <param name="profile">Known game layout; defaults to RGO.</param>
    /// <returns>A valid checksummed file with start/end jump targets and opaque per-dialogue commands.</returns>
    internal static byte[] Create(int dialogCount, ScriptProfile? profile = null)
    {
        profile ??= ScriptProfile.Rgo;
        int dialogueTableEnd = 0x90 + checked(dialogCount * 4);
        if (dialogCount < 0 || dialogueTableEnd > profile.JumpTableOffset)
            throw new ArgumentOutOfRangeException(nameof(dialogCount));
        int jumpCount = dialogCount * 2 + 1;
        int content = profile.JumpTableOffset + jumpCount * 4;
        int length = BinaryUtilities.Align(content + dialogCount * 28 + 4 + 16, profile.FileAlignment);
        byte[] data = new byte[length];
        Write32(data, 0x80, dialogCount == 0 ? 0U : 0x10U);
        Write32(data, 0x84, (uint)dialogCount);
        Write32(data, 0x88, (uint)(dialogueTableEnd - 0x80));
        Write32(data, profile.JumpTableOffset, (uint)(jumpCount * 4));
        BinaryPrimitives.WriteUInt16LittleEndian(data, 0x584D);
        int p = content;
        for (int i = 0; i < dialogCount; i++)
        {
            int start = p;
            Write32(data, 0x90 + i * 4, (uint)(start - profile.JumpTableOffset));
            Write32(data, profile.JumpTableOffset + (i * 2 + 1) * 4, (uint)(start - profile.JumpTableOffset));
            Write16(data, ref p, 0xFFF0);
            Write16(data, ref p, (ushort)i);
            Write16(data, ref p, 1);
            Write16(data, ref p, 0xFFFF);
            for (int j = 0; j < 8; j++) Write16(data, ref p, (ushort)(3 + j));
            Write32(data, profile.JumpTableOffset + (i * 2 + 2) * 4, (uint)(p - profile.JumpTableOffset));
            Write16(data, ref p, 0xFFFB);
            Write16(data, ref p, (ushort)(0x9000 + i % 1024));
        }
        Write16(data, ref p, 0xC001);
        Write16(data, ref p, 0xC002);
        RgoChecksum.Apply(data);
        return data;
    }

    /// <summary>Changes every other message, leaving speakers and all other records untouched.</summary>
    /// <param name="dialogCount">Number of source dialogues.</param>
    /// <returns>A deterministic edit set that grows each selected dialogue by four bytes.</returns>
    internal static ScriptMutation Mutations(int dialogCount)
    {
        ScriptMutation mutation = new();
        for (int i = 0; i < dialogCount; i += 2)
            mutation.MessageGlyphs.Add(i, [10, 9, 8, 7, 6, 5, 4, 3, 12, 13]);
        return mutation;
    }

    /// <summary>Measures warmed in-process rebuild latency and thread-local managed allocation; emits machine-readable, non-game results.</summary>
    /// <param name="args">Optional --probe to reproduce historical defects instead of running timing samples.</param>
    /// <returns>Zero after reporting; an exception produces a failing host process.</returns>
    internal static int Run(string[] args)
    {
        if (args.Contains("--probe", StringComparer.Ordinal)) return Probe();
        List<object> samples = [];
        foreach (int count in new[] { 1024, 4096, 8192 })
        {
            byte[] source = Create(count);
            LuckyStarScript script = LuckyStarScript.Parse(source, ScriptProfile.Rgo);
            ScriptMutation mutation = Mutations(count);
            script.Build(mutation); // Exclude JIT and first-use initialization from the five timed samples.
            double[] timings = new double[5];
            long[] allocations = new long[5];
            string resultHash = string.Empty;
            for (int i = 0; i < timings.Length; i++)
            {
                GC.Collect();
                long allocated = GC.GetAllocatedBytesForCurrentThread();
                long started = Stopwatch.GetTimestamp();
                ScriptBuildResult result = script.Build(mutation);
                timings[i] = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                allocations[i] = GC.GetAllocatedBytesForCurrentThread() - allocated;
                resultHash = BinaryUtilities.Sha256Hex(result.Data);
            }
            Array.Sort(timings);
            Array.Sort(allocations);
            samples.Add(new { dialogs = count, jumps = count * 2 + 1, replacements = (count + 1) / 2,
                sourceSha256 = BinaryUtilities.Sha256Hex(source), outputSha256 = resultHash,
                medianMilliseconds = timings[2], medianManagedAllocatedBytes = allocations[2], samplesMilliseconds = timings, allocationSamples = allocations });
        }
        Console.WriteLine(JsonSerializer.Serialize(new { schema = "lsptool.synthetic-script-benchmark.v1",
            version = typeof(LuckyStarScript).Assembly.GetName().Version?.ToString(),
            framework = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            tieredCompilation = Environment.GetEnvironmentVariable("DOTNET_TieredCompilation"),
            samples, gameData = false, nativeNet9Benchmark = false }, new JsonSerializerOptions { WriteIndented = true }));
        return 0;
    }

    /// <summary>Reports how a supplied toolkit revision handles three independent regression inputs, without assuming success.</summary>
    /// <returns>Zero when every probe has an observed outcome, not a guarantee that the old implementation is correct.</returns>
    private static int Probe()
    {
        List<object> results = [];
        byte[] source = Create(1);
        int jump = ScriptProfile.Rgo.JumpTableOffset;
        uint dialogue = BinaryPrimitives.ReadUInt32LittleEndian(source.AsSpan(0x90, 4));
        Write32(source, jump + 8, dialogue + 8);
        RgoChecksum.Apply(source);
        Observe(results, "no-op with jump inside unchanged message", () =>
        {
            byte[] rebuilt = LuckyStarScript.Parse(source, ScriptProfile.Rgo).Build(new ScriptMutation()).Data;
            return rebuilt.AsSpan().SequenceEqual(source) ? "byte-identical" : "bytes-changed";
        });
        byte[] high = Create(1);
        Write32(high, 0x80, uint.MaxValue);
        RgoChecksum.Apply(high);
        Observe(results, "uint32 metadata offset overflow", () => { LuckyStarScript.Parse(high, ScriptProfile.Rgo); return "accepted"; });
        byte[] atChecksum = MessageIntoChecksum();
        Observe(results, "valid checksum used as message terminator", () => { LuckyStarScript.Parse(atChecksum, ScriptProfile.Rgo); return "accepted"; });
        Console.WriteLine(JsonSerializer.Serialize(new { schema = "lsptool.script-regression-probe.v1",
            version = typeof(LuckyStarScript).Assembly.GetName().Version?.ToString(), results, gameData = false },
            new JsonSerializerOptions { WriteIndented = true }));
        return 0;
    }

    /// <summary>Creates a valid-checksum file whose missing message terminator is forged only in checksum bytes.</summary>
    /// <returns>A deliberately malformed text record that must not borrow its delimiter from the checksum.</returns>
    internal static byte[] MessageIntoChecksum()
    {
        byte[] data = Create(1);
        int message = ScriptProfile.Rgo.JumpTableOffset
            + (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(0x90, 4)) + 8;
        for (int i = message; i < data.Length - 16; i += 2)
            BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(i, 2), 0x1001);
        RgoChecksum.Apply(data);
        // Change an uninterpreted header word, not text, to make the checksum's first word 0xFFFB.
        ushort low = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(data.Length - 16, 2));
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(0x20, 2), unchecked((ushort)(0xFFFB - low)));
        RgoChecksum.Apply(data);
        return data;
    }

    /// <summary>Records a probe's actual return value or typed error without losing an unhandled exception's identity.</summary>
    /// <param name="results">Destination outcome list.</param>
    /// <param name="name">Probe label.</param>
    /// <param name="operation">Operation to observe.</param>
    private static void Observe(List<object> results, string name, Func<string> operation)
    {
        try { results.Add(new { name, outcome = operation() }); }
        catch (ToolkitException ex) { results.Add(new { name, outcome = ex.Code }); }
        catch (Exception ex) { results.Add(new { name, outcome = ex.GetType().Name }); }
    }

    /// <summary>Writes one little-endian fixture word and advances its construction cursor.</summary>
    /// <param name="data">Fixture destination.</param>
    /// <param name="position">Cursor advanced by two bytes.</param>
    /// <param name="word">The word to write.</param>
    private static void Write16(byte[] data, ref int position, ushort word)
    {
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(position, 2), word);
        position += 2;
    }

    /// <summary>Writes a fixed-offset little-endian fixture metadata field.</summary>
    /// <param name="data">Fixture destination.</param>
    /// <param name="position">Absolute field position.</param>
    /// <param name="word">The unsigned value to write.</param>
    private static void Write32(byte[] data, int position, uint word) =>
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(position, 4), word);
}
