using LuckyStarPspToolkit;

namespace LuckyStarPspToolkit.SelfTests;

/// <summary>Exercises public EBOOT mutation entry points without distributing any original game bytes.</summary>
internal static partial class Program
{
    /// <summary>Rejects a same-size synthetic PSP ELF even when every published patch word and table invariant matches.</summary>
    private static void TestUnverifiedScriptTableUpdate()
    {
        byte[] elf = new byte[RgoProfile.KnownDecryptedSize];
        new byte[] { 0x7F, (byte)'E', (byte)'L', (byte)'F', 1, 1, 1 }.CopyTo(elf, 0);
        BinaryData.WriteUInt16LittleEndian(elf, 18, 8, "test machine");
        BinaryData.WriteUInt32LittleEndian(elf, 20, 1, "test version");
        BinaryData.WriteUInt16LittleEndian(elf, 40, 52, "test header size");
        foreach (RgoEbootPatchDefinition patch in RgoVwfPatchProfile.Patches)
            BinaryData.WriteUInt32LittleEndian(elf, patch.Offset, patch.Expected, "synthetic patch word");
        for (int index = 0; index <= RgoProfile.ScriptLastId - RgoProfile.ScriptFirstId; index++)
        {
            int offset = RgoProfile.ScriptSizeTableOffset + index * 4;
            BinaryData.WriteUInt16LittleEndian(elf, offset, 1, "synthetic script size");
            BinaryData.WriteUInt16LittleEndian(elf, offset + 2, checked((ushort)(index + 1)), "synthetic cumulative size");
        }
        BinaryData.WriteUInt32LittleEndian(elf, RgoProfile.ScriptHeapLuiOffset,
            RgoProfile.KnownScriptHeapLuiInstruction, "synthetic heap high word");
        BinaryData.WriteUInt32LittleEndian(elf, RgoProfile.ScriptHeapAddiuOffset,
            RgoProfile.KnownScriptHeapAddiuInstruction, "synthetic heap low word");
        RgoVwfPatchInspection inspection = RgoVwfPatchProfile.Inspect(elf);
        Require(inspection.MismatchWordCount == 0 && inspection.ScriptSizeTableConsistent
            && inspection.ScriptHeapConsistent && !inspection.CompatibleKnownLineage,
            "The synthetic negative fixture must satisfy structural checks but fail the full executable hash.");
        byte[] original = elf.ToArray();
        RequireThrows<ToolkitException>(() => RgoScriptSizeTable.Update(elf, 1, 4096));
        Require(original.SequenceEqual(elf), "Rejected EBOOT mutation changed its input.");
    }
}
