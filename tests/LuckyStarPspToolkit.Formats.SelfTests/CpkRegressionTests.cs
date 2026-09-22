using System.Buffers.Binary;
using LuckyStarPspToolkit.Formats.Common;
using LuckyStarPspToolkit.Formats.Cri;

/// <summary>Regression tests for CPK ownership, allocation preflight, metadata migration and corruption handling.</summary>
internal static partial class SelfTestRunner
{
    /// <summary>Requires a distinct byte-identical output, even with nonzero inter-file padding and a trailer.</summary>
    private static void TestCpkNoOpPreservation()
    {
        byte[] original = CpkScaleFixture.Create();
        CriCpkArchive archive = CriCpkArchive.Parse(original);
        byte[] output = archive.Build().Data;
        SequenceEqual(original, output);
        output[0] ^= 1;
        SequenceEqual(original, archive.Build().Data);
        byte[] file = archive.GetEntry(0).PackedData.ToArray();
        archive.ReplaceEntry(0, file);
        SequenceEqual(original, archive.Build().Data);
        archive.GetEntry(0).PackedData[0] ^= 3;
        byte[] changed = archive.Build().Data;
        Equal((byte)(file[0] ^ 3), CriCpkArchive.Parse(changed).GetEntry(0).PackedData[0]);
        archive.GetEntry(0).PackedData[0] ^= 3;
        SequenceEqual(original, archive.Build().Data);
    }

    /// <summary>Preserves string constants and binary fields across both size classes, regardless of column order.</summary>
    private static void TestCpkAuxiliaryMigration()
    {
        CriCpkArchive lowToHigh = CriCpkArchive.Parse(CpkScaleFixture.Create(auxiliary: true));
        lowToHigh.ReplaceEntry(0, new byte[70000]);
        byte[] grown = lowToHigh.Build().Data;
        CriUtfTable high = CpkScaleFixture.Metadata(grown, true);
        Equal(2, high.Rows.Count);
        Equal("low-tag", high.GetText(0, "Tag"));
        Equal("high-tag", high.GetText(1, "Tag"));
        SequenceEqual(new byte[] { 0, 0xA9, 0, 0xF1 }, high.GetData(0, "Blob"));
        Equal(CriUtfStorage.PerRow, high.Columns[high.GetColumnIndex("Tag")].Storage);
        CriCpkArchive highToLow = CriCpkArchive.Parse(CpkScaleFixture.Create(auxiliary: true));
        highToLow.ReplaceEntry(1, new byte[1024]);
        byte[] shrunk = highToLow.Build().Data;
        CriUtfTable low = CpkScaleFixture.Metadata(shrunk, false);
        Equal("high-tag", low.GetText(1, "Tag"));
        SequenceEqual(new byte[] { 1, 0xA9, 0, 0xF1 }, low.GetData(1, "Blob"));
        SequenceEqual(grown, CriCpkArchive.Parse(grown).Build().Data);
    }

    /// <summary>Fails closed instead of losing an unknown field absent from the other size-class schema.</summary>
    private static void TestCpkIncompatibleMetadata()
    {
        byte[] source = CpkScaleFixture.Create(auxiliary: true);
        byte[] incompatible = EditCpkTables(source, (low, high) =>
        {
            int col = high.GetColumnIndex("Tag");
            high.Columns.RemoveAt(col);
            foreach (CriUtfRow row in high.Rows) row.Values.RemoveAt(col);
        });
        CriCpkArchive archive = CriCpkArchive.Parse(incompatible);
        SequenceEqual(incompatible, archive.Build().Data);
        archive.ReplaceEntry(0, new byte[70000]);
        Throws("CPK_METADATA_SCHEMA", () => archive.Build());
    }

    /// <summary>Rejects input/metadata budgets and unsigned offsets without raw overflow exceptions.</summary>
    private static void TestCpkPreflight()
    {
        byte[] source = CpkScaleFixture.Create();
        Throws("CPK_INPUT_LIMIT", () => CriCpkArchive.Parse(source, new FileLimits(MaximumInputBytes: source.Length - 1)));
        Throws("CPK_METADATA_LIMIT", () => CriCpkArchive.Parse(source, new FileLimits(MaximumCpkMetadataBytes: 31)));
        CriCpkArchive archive = CriCpkArchive.Parse(source);
        Throws("CPK_INPUT_LIMIT", () => archive.Build(new FileLimits(MaximumInputBytes: 10)));
        foreach (string field in new[] { "ItocOffset", "ContentOffset", "ItocSize" })
        {
            byte[] bad = source.ToArray();
            CpkScaleFixture.RewriteHeader(bad, field, ulong.MaxValue);
            ThrowsToolkit(() => CriCpkArchive.Parse(bad));
        }
        byte[] badCount = source.ToArray();
        CpkScaleFixture.RewriteHeader(badCount, "Files", 999);
        Throws("CPK_FILE_COUNT", () => CriCpkArchive.Parse(badCount));
        byte[] badPacket = source.ToArray();
        BinaryPrimitives.WriteUInt64LittleEndian(badPacket.AsSpan(8, 8), ulong.MaxValue);
        Throws("CPK_PACKET_RANGE", () => CriCpkArchive.Parse(badPacket));
        byte[] cutoff = source[..(source.Length - 50)];
        Throws("CPK_CONTENT_RANGE", () => CriCpkArchive.Parse(cutoff));
    }

    /// <summary>Rejects unsupported additional indices and applies the sum of metadata packets, not separate limits.</summary>
    private static void TestCpkExtraIndex()
    {
        byte[] source = CpkScaleFixture.Create();
        CriUtfPacket header = CriUtfCodec.ParsePacket(source.AsSpan(4));
        CpkScaleFixture.Add(header.Table, "TocOffset", CriUtfType.UInt64, CriUtfValue.FromUnsigned(1));
        byte[] changed = source.ToArray();
        CriUtfCodec.BuildPacket(header).CopyTo(changed, 4);
        Throws("CPK_UNSUPPORTED_INDEX", () => CriCpkArchive.Inspect(changed));
        header.Table.SetUnsigned(0, "TocOffset", ulong.MaxValue);
        CriUtfCodec.BuildPacket(header).CopyTo(changed, 4);
        Equal(2, CriCpkArchive.Inspect(changed).Entries.Count);
        CriUtfPacket itoc = CriUtfCodec.ParsePacket(source.AsSpan(2052));
        long sum = 8L + CriUtfCodec.ParsePacket(source.AsSpan(4)).OriginalPacketLength + itoc.OriginalPacketLength;
        _ = CriCpkArchive.Inspect(source, new FileLimits(MaximumCpkMetadataBytes: sum));
        Throws("CPK_METADATA_LIMIT", () => CriCpkArchive.Inspect(source, new FileLimits(MaximumCpkMetadataBytes: sum - 1)));
    }

    /// <summary>Rejects duplicate/out-of-range IDs and mutated entry lengths in decoded ITOC tables.</summary>
    private static void TestCpkDescriptorErrors()
    {
        byte[] source = CpkScaleFixture.Create();
        byte[] duplicate = EditCpkTables(source, (low, high) => high.SetUnsigned(0, "ID", 0));
        Throws("CPK_DUPLICATE_ID", () => CriCpkArchive.Inspect(duplicate));
        byte[] gap = EditCpkTables(source, (low, high) => high.SetUnsigned(0, "ID", 3));
        Throws("CPK_NONCONTIGUOUS_IDS", () => CriCpkArchive.Inspect(gap));
        byte[] size = EditCpkTables(source, (low, high) => high.SetUnsigned(0, "FileSize", uint.MaxValue));
        Throws("CPK_FILE_SIZE", () => CriCpkArchive.Inspect(size));
        byte[] extracted = EditCpkTables(source, (low, high) => high.SetUnsigned(0, "ExtractSize", 999));
        Throws("CPK_COMPRESSION", () => CriCpkArchive.Inspect(extracted));
        byte[] empty = EditCpkTables(source, (low, high) => low.SetUnsigned(0, "FileSize", 0));
        Throws("CPK_FILE_SIZE", () => CriCpkArchive.Inspect(empty));
        byte[] invalidId = EditCpkTables(source, (low, high) =>
        {
            int column = high.GetColumnIndex("ID");
            high.Columns[column].Type = CriUtfType.UInt64;
            high.Rows[0].Values[column].Unsigned = ulong.MaxValue;
        });
        Throws("CPK_ID", () => CriCpkArchive.Inspect(invalidId));
    }

    /// <summary>Ensures metadata-only inspection and one-file extraction match the full parser and own no source payload.</summary>
    private static void TestCpkInspection()
    {
        byte[] source = CpkScaleFixture.Create(auxiliary: true);
        CriCpkInspection info = CriCpkArchive.Inspect(source);
        CriCpkArchive full = CriCpkArchive.Parse(source);
        Equal(full.Alignment, info.Alignment);
        Equal(full.ContentOffset, (long)info.ContentOffset);
        foreach (CriCpkFileInfo entry in info.Entries)
        {
            SequenceEqual(source.AsSpan(entry.Offset, entry.PackedSize).ToArray(), full.GetEntry(entry.Id).PackedData);
            byte[] extracted = CriCpkArchive.Extract(source, entry.Id);
            SequenceEqual(full.GetEntry(entry.Id).GetExtractedData(), extracted);
            extracted[0] ^= 1;
            SequenceEqual(full.GetEntry(entry.Id).PackedData, CriCpkArchive.Extract(source, entry.Id, true));
        }
        Throws("CPK_ENTRY_NOT_FOUND", () => CriCpkArchive.Extract(source, 7));
        byte[] reference = File.ReadAllBytes(Path.Combine(Fixtures, "reference.cpk"));
        foreach (var entry in CriCpkArchive.Parse(reference).Entries.Values)
            SequenceEqual(entry.GetExtractedData(), CriCpkArchive.Extract(reference, entry.Id));
    }

    /// <summary>Checks public mutable entry properties before either rebuilding or decoding an entry.</summary>
    private static void TestCpkEntryMutation()
    {
        CriCpkArchive archive = CriCpkArchive.Parse(CpkScaleFixture.Create());
        archive.GetEntry(0).ExtractSize++;
        Throws("CPK_EXTRACT_SIZE", () => archive.Build());
        Throws("CPK_EXTRACT_SIZE", () => archive.GetEntry(0).GetExtractedData());
        archive.GetEntry(0).ExtractSize--;
        archive.GetEntry(0).PackedData = [];
        Throws("CPK_FILE_SIZE", () => archive.Build());
    }

    /// <summary>Exercises descriptor thresholds, repeated migrations and unmodified file identity.</summary>
    private static void TestCpkThresholds()
    {
        byte[] source = CpkScaleFixture.Create(auxiliary: true);
        CriCpkArchive archive = CriCpkArchive.Parse(source);
        byte[] untouched = archive.GetEntry(1).PackedData.ToArray();
        foreach (int size in new[] { 1, 2047, 2048, 2049, 65534, 65535, 65536, 70000, 1024 })
        {
            byte[] replacement = new byte[size]; replacement.AsSpan().Fill((byte)size);
            archive.ReplaceEntry(0, replacement);
            byte[] built = archive.Build().Data;
            archive = CriCpkArchive.Parse(built);
            SequenceEqual(replacement, archive.GetEntry(0).PackedData);
            SequenceEqual(untouched, archive.GetEntry(1).PackedData);
            CriUtfTable row = CpkScaleFixture.Metadata(built, size > ushort.MaxValue);
            int index = row.Rows.FindIndex(r => r.Values[row.GetColumnIndex("ID")].Unsigned == 0);
            Equal("low-tag", row.GetText(index, "Tag"));
            SequenceEqual(built, archive.Build().Data);
        }
    }

    /// <summary>Mutates encoded header integers while requiring bounded success or a ToolkitException only.</summary>
    private static void TestCpkHeaderFuzz()
    {
        byte[] source = CpkScaleFixture.Create();
        var random = new Random(130013);
        int rejected = 0;
        for (int i = 0; i < 512; i++)
        {
            byte[] bad = source.ToArray();
            string field = new[] { "ItocOffset", "ContentOffset", "ItocSize" }[i % 3];
            ulong value = i % 4 == 0 ? ulong.MaxValue - (uint)i : (ulong)random.NextInt64(0, source.Length * 2L);
            CpkScaleFixture.RewriteHeader(bad, field, value);
            try { _ = CriCpkArchive.Inspect(bad); }
            catch (ToolkitException) { rejected++; }
        }
        if (rejected == 0) throw new InvalidOperationException("Mutation corpus did not exercise any rejection.");
        Console.WriteLine($"CPK HEADER MUTATIONS: 512 cases, {rejected} rejected, {512 - rejected} accepted");
    }

    /// <summary>Compares allocation growth of inspection and changed rebuilds against actual input size.</summary>
    private static void TestCpkAllocationScaling()
    {
        byte[] source = CpkScaleFixture.Create(4 * 1024 * 1024, trailer: false);
        _ = CriCpkArchive.Inspect(source);
        long start = GC.GetAllocatedBytesForCurrentThread();
        _ = CriCpkArchive.Inspect(source);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - start;
        if (allocated >= 512 * 1024) throw new InvalidOperationException($"Inspection allocated {allocated} bytes for a tiny metadata catalog.");
        CriCpkArchive archive = CriCpkArchive.Parse(source);
        archive.ReplaceEntry(0, new byte[8192]);
        _ = archive.Build();
        start = GC.GetAllocatedBytesForCurrentThread();
        byte[] output = archive.Build().Data;
        allocated = GC.GetAllocatedBytesForCurrentThread() - start;
        if (allocated > output.Length + 1024 * 1024) throw new InvalidOperationException($"Rebuild copied payloads more than once: {allocated} bytes.");
        Console.WriteLine($"CPK BUILD ALLOCATION: {allocated} bytes; output {output.Length} bytes");
    }

    /// <summary>Rewrites small metadata test tables in the reserved prefix, preserving payload offsets.</summary>
    /// <param name="source">Synthetic fixture with enough header padding.</param>
    /// <param name="edit">Transformation of the decoded DataL and DataH schemas.</param>
    /// <returns>A new CPK with the same data regions.</returns>
    private static byte[] EditCpkTables(byte[] source, Action<CriUtfTable, CriUtfTable> edit)
    {
        CriUtfPacket header = CriUtfCodec.ParsePacket(source.AsSpan(4));
        int offset = (int)header.Table.GetUnsigned(0, "ItocOffset");
        CriUtfPacket itoc = CriUtfCodec.ParsePacket(source.AsSpan(offset + 4));
        CriUtfTable low = CpkScaleFixture.Metadata(source, false), high = CpkScaleFixture.Metadata(source, true);
        edit(low, high);
        itoc.Table.SetData(0, "DataL", CriUtfCodec.BuildTable(low));
        itoc.Table.SetData(0, "DataH", CriUtfCodec.BuildTable(high));
        byte[] packet = CriUtfCodec.BuildPacket(itoc);
        header.Table.SetUnsigned(0, "ItocSize", (ulong)(packet.Length + 4));
        byte[] result = source.ToArray();
        CriUtfCodec.BuildPacket(header).CopyTo(result, 4);
        if (offset + 4 + packet.Length > (int)header.Table.GetUnsigned(0, "ContentOffset"))
            throw new InvalidOperationException("Test metadata outgrew the prefix.");
        packet.CopyTo(result, offset + 4);
        return result;
    }

    /// <summary>Requires a domain-format error rather than an unchecked framework exception.</summary>
    /// <param name="action">Malformed-input operation.</param>
    private static void ThrowsToolkit(Action action)
    {
        try { action(); }
        catch (ToolkitException) { return; }
        throw new InvalidOperationException("Expected a controlled ToolkitException.");
    }
}
