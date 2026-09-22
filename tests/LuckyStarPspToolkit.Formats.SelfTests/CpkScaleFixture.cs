using System.Buffers.Binary;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using LuckyStarPspToolkit.Formats.Common;
using LuckyStarPspToolkit.Formats.Cri;

/// <summary>Independent CPK assembly and shared old/new-version probe using only the pre-0.13 public APIs.</summary>
internal static class CpkScaleFixture
{
    /// <summary>Creates UTF metadata with the public UTF writer and lays out content independently of the CPK builder.</summary>
    /// <param name="largeSize">Size of file ID 1; ID 0 has 513 bytes.</param>
    /// <param name="auxiliary">Include per-row blobs and different shared string constants in the size classes.</param>
    /// <param name="trailer">Preserve a deliberately nonzero trailer beyond the last file.</param>
    /// <returns>A synthetic, deterministic CPK with contiguous IDs and two size classes.</returns>
    public static byte[] Create(int largeSize = 131072, bool auxiliary = false, bool trailer = true)
    {
        if (largeSize <= ushort.MaxValue) throw new ArgumentOutOfRangeException(nameof(largeSize));
        CriUtfTable low = CreateTable("CpkItocL", 0, 513, false, auxiliary);
        CriUtfTable high = CreateTable("CpkItocH", 1, largeSize, true, auxiliary);
        CriUtfTable itoc = new() { Name = "CpkItocInfo" };
        Add(itoc, "FilesL", CriUtfType.UInt32, CriUtfValue.FromUnsigned(1));
        Add(itoc, "FilesH", CriUtfType.UInt32, CriUtfValue.FromUnsigned(1));
        Add(itoc, "DataL", CriUtfType.Data, CriUtfValue.FromData(CriUtfCodec.BuildTable(low)));
        Add(itoc, "DataH", CriUtfType.Data, CriUtfValue.FromData(CriUtfCodec.BuildTable(high)));
        byte[] itocBytes = CriUtfCodec.BuildPacket(new CriUtfPacket(255, true, itoc, 0));
        int content = (int)BinaryUtilities.Align(2048L + 4 + itocBytes.Length, 2048);
        CriUtfTable header = new() { Name = "CpkHeader" };
        Add(header, "ItocOffset", CriUtfType.UInt64, CriUtfValue.FromUnsigned(2048));
        Add(header, "ContentOffset", CriUtfType.UInt64, CriUtfValue.FromUnsigned((ulong)content));
        Add(header, "ItocSize", CriUtfType.UInt64, CriUtfValue.FromUnsigned((ulong)(itocBytes.Length + 4)));
        Add(header, "Files", CriUtfType.UInt32, CriUtfValue.FromUnsigned(2));
        Add(header, "Align", CriUtfType.UInt16, CriUtfValue.FromUnsigned(2048));
        Add(header, "ContentSize", CriUtfType.UInt64, CriUtfValue.FromUnsigned((ulong)(513 + largeSize)));
        byte[] headerBytes = CriUtfCodec.BuildPacket(new CriUtfPacket(255, false, header, 0));
        byte[] data = new byte[content + 2048 + largeSize + (trailer ? 31 : 0)];
        "CPK "u8.CopyTo(data); headerBytes.CopyTo(data, 4);
        "ITOC"u8.CopyTo(data.AsSpan(2048)); itocBytes.CopyTo(data, 2052);
        for (int i = 0; i < 513; i++) data[content + i] = (byte)(i * 31);
        data.AsSpan(content + 513, 2048 - 513).Fill(0xA5);
        for (int i = 0; i < largeSize; i++) data[content + 2048 + i] = (byte)(i * 17 + 23);
        if (trailer) data.AsSpan(data.Length - 31).Fill(0x7B);
        return data;
    }

    /// <summary>Creates one original size-class schema; high fields have a deliberately different order.</summary>
    /// <param name="name">Table name.</param>
    /// <param name="id">Resource ID encoded in its only row.</param>
    /// <param name="size">Raw payload length.</param>
    /// <param name="high">Use UInt32 sizes instead of UInt16.</param>
    /// <param name="auxiliary">Include typed metadata that must survive migration.</param>
    /// <returns>A new one-row UTF table.</returns>
    private static CriUtfTable CreateTable(string name, ushort id, int size, bool high, bool auxiliary)
    {
        CriUtfTable table = new() { Name = name };
        if (auxiliary && high) Add(table, "Tag", CriUtfType.String, CriUtfValue.FromText("high-tag"), CriUtfStorage.Constant);
        Add(table, "ID", CriUtfType.UInt16, CriUtfValue.FromUnsigned(id));
        Add(table, "FileSize", high ? CriUtfType.UInt32 : CriUtfType.UInt16, CriUtfValue.FromUnsigned((ulong)size));
        Add(table, "ExtractSize", high ? CriUtfType.UInt32 : CriUtfType.UInt16, CriUtfValue.FromUnsigned((ulong)size));
        if (auxiliary)
        {
            Add(table, "Blob", CriUtfType.Data, CriUtfValue.FromData([(byte)id, 0xA9, 0, 0xF1]));
            if (!high) Add(table, "Tag", CriUtfType.String, CriUtfValue.FromText("low-tag"), CriUtfStorage.Constant);
        }
        return table;
    }

    /// <summary>Adds a typed field and its materialized row value to a single-row test table.</summary>
    /// <param name="table">Table being constructed.</param>
    /// <param name="name">Exact column name.</param>
    /// <param name="type">Encoded data type.</param>
    /// <param name="value">Source value to clone.</param>
    /// <param name="storage">Encoded storage kind.</param>
    public static void Add(CriUtfTable table, string name, CriUtfType type, CriUtfValue value, CriUtfStorage storage = CriUtfStorage.PerRow)
    {
        table.Columns.Add(new CriUtfColumn { Name = name, Type = type, Storage = storage, ConstantValue = value.Clone() });
        if (table.Rows.Count == 0) table.Rows.Add(new CriUtfRow());
        table.Rows[0].Values.Add(value.Clone());
    }

    /// <summary>Reads the raw UTF headers of a synthetic archive without calling the CPK parser.</summary>
    /// <param name="data">Archive bytes.</param>
    /// <param name="high">Select DataH, otherwise DataL.</param>
    /// <returns>The selected independently located UTF table.</returns>
    public static CriUtfTable Metadata(byte[] data, bool high)
    {
        CriUtfPacket header = CriUtfCodec.ParsePacket(data.AsSpan(4));
        int offset = (int)header.Table.GetUnsigned(0, "ItocOffset");
        CriUtfPacket itoc = CriUtfCodec.ParsePacket(data.AsSpan(offset + 4));
        return CriUtfCodec.ParseTable(itoc.Table.GetData(0, high ? "DataH" : "DataL"));
    }

    /// <summary>Runs reproducible probes and median allocation/time comparisons against the loaded Formats assembly.</summary>
    /// <param name="args">Accepts --cpk-probe to report historical defects only.</param>
    /// <returns>Zero after printing JSON; failures in operations are reported explicitly by the probe.</returns>
    public static int Run(string[] args)
    {
        byte[] input = Create(auxiliary: true);
        var outcomes = new Dictionary<string, object>();
        outcomes["noOpByteIdentical"] = input.AsSpan().SequenceEqual(CriCpkArchive.Parse(input).Build().Data);
        CriCpkArchive moved = CriCpkArchive.Parse(input);
        moved.ReplaceEntry(0, new byte[70000]);
        byte[] result = moved.Build().Data;
        CriUtfTable rows = Metadata(result, true);
        outcomes["migratedAuxiliaryTag"] = rows.GetText(0, "Tag");
        outcomes["migratedAuxiliaryBlob"] = Convert.ToHexString(rows.GetData(0, "Blob"));
        byte[] bad = Create();
        RewriteHeader(bad, "ItocOffset", ulong.MaxValue);
        outcomes["largeOffsetError"] = ObserveParse(bad);
        bad = Create(); RewriteHeader(bad, "Files", 999);
        outcomes["mismatchedFileCount"] = ObserveParse(bad);
        if (!args.Contains("--cpk-probe", StringComparer.Ordinal))
        {
            byte[] source = Create(8 * 1024 * 1024, trailer: false);
            outcomes["benchmarkInputBytes"] = source.Length;
            outcomes["benchmarkInputSha256"] = Convert.ToHexString(SHA256.HashData(source)).ToLowerInvariant();
            CriCpkArchive archive = CriCpkArchive.Parse(source);
            archive.ReplaceEntry(0, new byte[8192]);
            for (int i = 0; i < 3; i++) _ = archive.Build();
            var samples = new List<object>();
            for (int i = 0; i < 7; i++)
            {
                GC.Collect(); GC.WaitForPendingFinalizers();
                long start = GC.GetAllocatedBytesForCurrentThread();
                var watch = Stopwatch.StartNew();
                byte[] rebuilt = archive.Build().Data;
                watch.Stop();
                long allocated = GC.GetAllocatedBytesForCurrentThread() - start;
                samples.Add(new { allocated, milliseconds = watch.Elapsed.TotalMilliseconds, outputSize = rebuilt.Length,
                    sha256 = Convert.ToHexString(SHA256.HashData(rebuilt)).ToLowerInvariant() });
            }
            outcomes["benchmark"] = samples;
        }
        Console.WriteLine(JsonSerializer.Serialize(new { framework = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            assembly = typeof(CriCpkArchive).Assembly.FullName, outcomes }, new JsonSerializerOptions { WriteIndented = true }));
        return 0;
    }

    /// <summary>Edits one fixed-width header field without rebuilding any content.</summary>
    /// <param name="data">Mutable synthetic CPK.</param>
    /// <param name="name">Header column name.</param>
    /// <param name="value">Test integer, including malformed extremes.</param>
    public static void RewriteHeader(byte[] data, string name, ulong value)
    {
        CriUtfPacket packet = CriUtfCodec.ParsePacket(data.AsSpan(4));
        packet.Table.SetUnsigned(0, name, value);
        byte[] rebuilt = CriUtfCodec.BuildPacket(packet);
        if (rebuilt.Length != packet.OriginalPacketLength) throw new InvalidOperationException("Test header layout changed.");
        rebuilt.CopyTo(data, 4);
    }

    /// <summary>Reports the actual public parser outcome without treating acceptance as a successful rejection.</summary>
    /// <param name="data">Malformed fixture.</param>
    /// <returns>Accepted or the actual exception type and stable code.</returns>
    private static string ObserveParse(byte[] data)
    {
        try { _ = CriCpkArchive.Parse(data); return "ACCEPTED"; }
        catch (ToolkitException ex) { return ex.GetType().Name + ":" + ex.Code; }
        catch (Exception ex) { return ex.GetType().Name; }
    }
}
