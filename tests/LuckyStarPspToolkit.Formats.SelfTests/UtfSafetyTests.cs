using System.Buffers.Binary;
using LuckyStarPspToolkit.Formats.Common;
using LuckyStarPspToolkit.Formats.Cri;

/// <summary>Checks allocation limits before malformed UTF tables can materialize excessive cells or repeated payloads.</summary>
internal static partial class SelfTestRunner
{
    /// <summary>Confirms constant-blob clones and implicit-zero cells both consume their configured limits.</summary>
    private static void TestUtfAllocationLimits()
    {
        CriUtfTable table = new() { Name = "budget-test" };
        table.Columns.Add(new CriUtfColumn { Name = "Blob", Type = CriUtfType.Data,
            Storage = CriUtfStorage.Constant, ConstantValue = CriUtfValue.FromData(new byte[64]) });
        for (int i = 0; i < 20; i++)
        {
            CriUtfRow row = new();
            row.Values.Add(table.Columns[0].ConstantValue.Clone());
            table.Rows.Add(row);
        }
        byte[] encoded = CriUtfCodec.BuildTable(table);
        Equal(20, CriUtfCodec.ParseTable(encoded).Rows.Count);
        Throws("UTF_CELL_LIMIT", () => CriUtfCodec.ParseTable(encoded, new FileLimits(MaximumUtfCells: 19)));
        Throws("UTF_ALLOCATION_LIMIT", () => CriUtfCodec.ParseTable(encoded, new FileLimits(MaximumUtfDecodedBytes: 256)));
        Equal(20, CriUtfCodec.ParseTable(encoded, new FileLimits(MaximumUtfDecodedBytes: 2048)).Rows.Count);
        byte[] malformed = encoded.ToArray();
        BinaryPrimitives.WriteUInt32BigEndian(malformed.AsSpan(4), uint.MaxValue);
        Throws("UTF_TABLE_SIZE", () => CriUtfCodec.ParseTable(malformed));
        malformed = encoded.ToArray();
        BinaryPrimitives.WriteUInt32BigEndian(malformed.AsSpan(8), uint.MaxValue);
        Throws("UTF_OFFSET", () => CriUtfCodec.ParseTable(malformed));
    }

    /// <summary>Mutates 512 table headers under low memory limits; accepts only successful parses or explicit format errors.</summary>
    private static void TestUtfMalformedFuzz()
    {
        CriUtfTable table = new() { Name = "mutation-test" };
        table.Columns.Add(new CriUtfColumn { Name = "ID", Type = CriUtfType.UInt16, Storage = CriUtfStorage.PerRow });
        for (ushort i = 0; i < 3; i++)
        {
            CriUtfRow row = new();
            row.Values.Add(CriUtfValue.FromUnsigned(i));
            table.Rows.Add(row);
        }
        byte[] original = CriUtfCodec.BuildTable(table);
        Random random = new(1100);
        FileLimits limits = new(MaximumUtfRows: 64, MaximumUtfColumns: 32,
            MaximumUtfCells: 256, MaximumUtfDecodedBytes: 4096);
        int rejected = 0;
        for (int iteration = 0; iteration < 512; iteration++)
        {
            byte[] bytes = original.ToArray();
            int offset = random.Next(4, Math.Min(bytes.Length, 44));
            bytes[offset] ^= (byte)random.Next(1, 256);
            try { _ = CriUtfCodec.ParseTable(bytes, limits); }
            catch (ToolkitException) { rejected++; }
        }
        True(rejected > 300, "Malformed header fixture did not exercise rejection paths.");
        Console.WriteLine($"UTF header mutations: 512; explicit format rejections: {rejected}");
    }
}
