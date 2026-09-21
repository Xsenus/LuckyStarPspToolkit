namespace LuckyStarPspToolkit;

/// <summary>
/// Represents the toolkit's ELF 32 info model or service.
/// </summary>
public sealed class Elf32Info
{
    /// <summary>The type value used by this model or operation.</summary>
    public ushort Type { get; init; }
    /// <summary>The machine value used by this model or operation.</summary>
    public ushort Machine { get; init; }
    /// <summary>The version used in CLI reports and release metadata.</summary>
    public uint Version { get; init; }
    /// <summary>The entry point value used by this model or operation.</summary>
    public uint EntryPoint { get; init; }
    /// <summary>The program header offset value used by this model or operation.</summary>
    public uint ProgramHeaderOffset { get; init; }
    /// <summary>The section header offset value used by this model or operation.</summary>
    public uint SectionHeaderOffset { get; init; }
    /// <summary>The encoded flags combining storage mode and value type.</summary>
    public uint Flags { get; init; }
    /// <summary>The header size value used by this model or operation.</summary>
    public ushort HeaderSize { get; init; }
    /// <summary>The program header entry size value used by this model or operation.</summary>
    public ushort ProgramHeaderEntrySize { get; init; }
    /// <summary>The program header count value used by this model or operation.</summary>
    public ushort ProgramHeaderCount { get; init; }
    /// <summary>The section header entry size value used by this model or operation.</summary>
    public ushort SectionHeaderEntrySize { get; init; }
    /// <summary>The section header count value used by this model or operation.</summary>
    public ushort SectionHeaderCount { get; init; }
    /// <summary>The section name index value used by this model or operation.</summary>
    public ushort SectionNameIndex { get; init; }
    /// <summary>The is psp mips value used by this model or operation.</summary>
    public bool IsPspMips { get; init; }
}

/// <summary>
/// Provides ELF reader operations with strict bounds and format validation.
/// </summary>
public static class ElfReader
{
    /// <summary>The magic value used by this model or operation.</summary>
    private static readonly byte[] Magic = [0x7F, (byte)'E', (byte)'L', (byte)'F'];
    /// <summary>The fixed elf32 header size value used by this format or revision.</summary>
    private const int Elf32HeaderSize = 52;
    /// <summary>The fixed machine mips value used by this format or revision.</summary>
    private const ushort MachineMips = 8;

    /// <summary>
    /// Determines whether the supplied data matches the expected format signature.
    /// </summary>
    /// <param name="data">The binary data to process.</param>
    /// <returns><see langword="true"/> when the condition is satisfied; otherwise <see langword="false"/>.</returns>
    public static bool LooksLike(ReadOnlySpan<byte> data) => BinaryData.StartsWith(data, Magic);

    /// <summary>
    /// Parses 32 little endian while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="data">The binary data to process.</param>
    /// <returns>The validated operation result.</returns>
    /// <remarks>Malformed input or violated preconditions are rejected before a persistent output is committed.</remarks>
    public static Elf32Info Parse32LittleEndian(ReadOnlySpan<byte> data)
    {
        Guard.RequireRange(data.Length, 0, Elf32HeaderSize, "ELF32 header");
        Guard.Require(LooksLike(data), "Not an ELF file: invalid magic.");
        Guard.Require(data[4] == 1, "Unsupported ELF class: expected ELF32.");
        Guard.Require(data[5] == 1, "Unsupported ELF byte order: expected little-endian.");
        Guard.Require(data[6] == 1, "Unsupported ELF identification version.");

        var info = new Elf32Info
        {
            Type = BinaryData.ReadUInt16LittleEndian(data, 16, "ELF type"),
            Machine = BinaryData.ReadUInt16LittleEndian(data, 18, "ELF machine"),
            Version = BinaryData.ReadUInt32LittleEndian(data, 20, "ELF version"),
            EntryPoint = BinaryData.ReadUInt32LittleEndian(data, 24, "ELF entry point"),
            ProgramHeaderOffset = BinaryData.ReadUInt32LittleEndian(data, 28, "ELF program-header offset"),
            SectionHeaderOffset = BinaryData.ReadUInt32LittleEndian(data, 32, "ELF section-header offset"),
            Flags = BinaryData.ReadUInt32LittleEndian(data, 36, "ELF flags"),
            HeaderSize = BinaryData.ReadUInt16LittleEndian(data, 40, "ELF header size"),
            ProgramHeaderEntrySize = BinaryData.ReadUInt16LittleEndian(data, 42, "ELF program-header entry size"),
            ProgramHeaderCount = BinaryData.ReadUInt16LittleEndian(data, 44, "ELF program-header count"),
            SectionHeaderEntrySize = BinaryData.ReadUInt16LittleEndian(data, 46, "ELF section-header entry size"),
            SectionHeaderCount = BinaryData.ReadUInt16LittleEndian(data, 48, "ELF section-header count"),
            SectionNameIndex = BinaryData.ReadUInt16LittleEndian(data, 50, "ELF section-name index")
        };

        Guard.Require(info.Version == 1, "Unsupported ELF header version.");
        Guard.Require(info.HeaderSize >= Elf32HeaderSize, "ELF header size is shorter than ELF32.");
        ValidateTable(data.Length, info.ProgramHeaderOffset, info.ProgramHeaderEntrySize,
            info.ProgramHeaderCount, "ELF program-header table");
        ValidateTable(data.Length, info.SectionHeaderOffset, info.SectionHeaderEntrySize,
            info.SectionHeaderCount, "ELF section-header table");

        return new Elf32Info
        {
            Type = info.Type,
            Machine = info.Machine,
            Version = info.Version,
            EntryPoint = info.EntryPoint,
            ProgramHeaderOffset = info.ProgramHeaderOffset,
            SectionHeaderOffset = info.SectionHeaderOffset,
            Flags = info.Flags,
            HeaderSize = info.HeaderSize,
            ProgramHeaderEntrySize = info.ProgramHeaderEntrySize,
            ProgramHeaderCount = info.ProgramHeaderCount,
            SectionHeaderEntrySize = info.SectionHeaderEntrySize,
            SectionHeaderCount = info.SectionHeaderCount,
            SectionNameIndex = info.SectionNameIndex,
            IsPspMips = info.Machine == MachineMips
        };
    }

    /// <summary>
    /// Validates table while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="dataLength">The data length value.</param>
    /// <param name="offset">The zero-based byte or element offset.</param>
    /// <param name="entrySize">The entry size value.</param>
    /// <param name="count">The number of items to process.</param>
    /// <param name="context">A diagnostic label included in validation errors.</param>
    /// <remarks>Malformed input or violated preconditions are rejected before a persistent output is committed.</remarks>
    private static void ValidateTable(
        int dataLength,
        uint offset,
        ushort entrySize,
        ushort count,
        string context)
    {
        if (count == 0)
        {
            return;
        }

        Guard.Require(entrySize != 0, $"{context} entry size is zero.");
        long length = (long)entrySize * count;
        Guard.Require(offset <= int.MaxValue && length <= int.MaxValue,
            $"{context} is too large.");
        Guard.RequireRange(dataLength, (int)offset, (int)length, context);
    }
}
