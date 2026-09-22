using System.Security.Cryptography;

namespace LuckyStarPspToolkit;

/// <summary>
/// Represents the toolkit's binary patch model or service.
/// </summary>
public sealed class BinaryPatch
{
    /// <summary>The name value used by this model or operation.</summary>
    public required string Name { get; init; }
    /// <summary>The offset value used by this model or operation.</summary>
    public required int Offset { get; init; }
    /// <summary>The expected hex value used by this model or operation.</summary>
    public required string ExpectedHex { get; init; }
    /// <summary>The replacement hex value used by this model or operation.</summary>
    public required string ReplacementHex { get; init; }
}

/// <summary>
/// Represents the toolkit's applied binary patch model or service.
/// </summary>
public sealed class AppliedBinaryPatch
{
    /// <summary>The name value used by this model or operation.</summary>
    public required string Name { get; init; }
    /// <summary>The offset hex value used by this model or operation.</summary>
    public required string OffsetHex { get; init; }
    /// <summary>The length value used by this model or operation.</summary>
    public required int Length { get; init; }
}

/// <summary>
/// Represents the toolkit's binary patch result model or service.
/// </summary>
public sealed class BinaryPatchResult
{
    /// <summary>The input sha256 value used by this model or operation.</summary>
    public required string InputSha256 { get; init; }
    /// <summary>The output sha256 value used by this model or operation.</summary>
    public required string OutputSha256 { get; init; }
    /// <summary>The applied patches value used by this model or operation.</summary>
    public required IReadOnlyList<AppliedBinaryPatch> AppliedPatches { get; init; }

    /// <summary>The output value used by this model or operation.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public required byte[] Output { get; init; }
}

/// <summary>
/// Provides the toolkit's binary patch engine workflow.
/// </summary>
public static class BinaryPatchEngine
{
    /// <summary>
    /// Applies the requested transformation after validating all preconditions.
    /// </summary>
    /// <param name="input">The input binary data or object.</param>
    /// <param name="patches">The patches value.</param>
    /// <param name="expectedInputSha256">The expected input SHA 256 value.</param>
    /// <returns>The validated operation result.</returns>
    /// <remarks>Malformed input or violated preconditions are rejected before a persistent output is committed.</remarks>
    public static BinaryPatchResult Apply(
        ReadOnlySpan<byte> input,
        IEnumerable<BinaryPatch> patches,
        string? expectedInputSha256 = null)
    {
        ArgumentNullException.ThrowIfNull(patches);
        string inputHash = CryptoUtilities.Sha256Hex(input);
        if (!string.IsNullOrWhiteSpace(expectedInputSha256))
        {
            byte[] expectedHash = HexUtilities.Parse(expectedInputSha256);
            Guard.Require(expectedHash.Length == 32, "Expected input SHA-256 must contain 32 bytes.");
            byte[] actualHash = CryptoUtilities.Sha256(input);
            Guard.Require(CryptographicOperations.FixedTimeEquals(actualHash, expectedHash),
                $"Input SHA-256 mismatch: expected {expectedInputSha256}, got {inputHash}.");
        }

        var prepared = patches.Select(Prepare).OrderBy(item => item.Patch.Offset).ToList();
        Guard.Require(prepared.Count > 0, "At least one binary patch is required.");
        int previousEnd = -1;
        foreach (PreparedPatch item in prepared)
        {
            Guard.Require(item.Patch.Offset >= previousEnd,
                $"Binary patch '{item.Patch.Name}' overlaps a preceding patch.");
            Guard.RequireRange(input.Length, item.Patch.Offset, item.Expected.Length,
                $"Binary patch '{item.Patch.Name}'");
            previousEnd = checked(item.Patch.Offset + item.Expected.Length);
            ReadOnlySpan<byte> actual = input.Slice(item.Patch.Offset, item.Expected.Length);
            Guard.Require(actual.SequenceEqual(item.Expected),
                $"Binary patch '{item.Patch.Name}' precondition failed at " +
                $"{HexUtilities.Offset(item.Patch.Offset)}: expected {item.Patch.ExpectedHex}, " +
                $"got {HexUtilities.ToHex(actual)}.");
        }

        byte[] output = input.ToArray();
        var applied = new List<AppliedBinaryPatch>(prepared.Count);
        foreach (PreparedPatch item in prepared)
        {
            item.Replacement.CopyTo(output, item.Patch.Offset);
            applied.Add(new AppliedBinaryPatch
            {
                Name = item.Patch.Name,
                OffsetHex = HexUtilities.Offset(item.Patch.Offset),
                Length = item.Replacement.Length
            });
        }

        return new BinaryPatchResult
        {
            InputSha256 = inputHash,
            OutputSha256 = CryptoUtilities.Sha256Hex(output),
            AppliedPatches = applied.AsReadOnly(),
            Output = output
        };
    }

    /// <summary>
    /// Validates a patch name and range and decodes its original and replacement hexadecimal bytes.
    /// </summary>
    /// <param name="patch">The patch value.</param>
    /// <returns>The validated operation result.</returns>
    private static PreparedPatch Prepare(BinaryPatch patch)
    {
        ArgumentNullException.ThrowIfNull(patch);
        Guard.Require(!string.IsNullOrWhiteSpace(patch.Name), "Binary patch name cannot be empty.");
        Guard.Require(patch.Offset >= 0, $"Binary patch '{patch.Name}' has a negative offset.");
        byte[] expected = HexUtilities.Parse(patch.ExpectedHex);
        byte[] replacement = HexUtilities.Parse(patch.ReplacementHex);
        Guard.Require(expected.Length > 0, $"Binary patch '{patch.Name}' cannot be empty.");
        Guard.Require(expected.Length == replacement.Length,
            $"Binary patch '{patch.Name}' must preserve the binary size.");
        return new PreparedPatch(patch, expected, replacement);
    }

    /// <summary>
    /// Represents immutable prepared patch data exchanged by the toolkit.
    /// </summary>
    /// <param name="Patch">The patch value used by this model or operation.</param>
    /// <param name="Expected">The expected value used by this model or operation.</param>
    /// <param name="Replacement">The replacement value used by this model or operation.</param>
    private sealed record PreparedPatch(BinaryPatch Patch, byte[] Expected, byte[] Replacement);
}

/// <summary>
/// Represents the toolkit's RGO script size table model or service.
/// </summary>
public static class RgoScriptSizeTable
{
    /// <summary>
    /// Verifies the complete known RGO executable lineage, then updates one size and its following cumulative entries in a copy.
    /// </summary>
    /// <param name="decryptedElf">The decrypted ELF value.</param>
    /// <param name="scriptId">The numeric scenario identifier.</param>
    /// <param name="newSizeBytes">The new size bytes value.</param>
    /// <returns>The resulting binary or typed sequence.</returns>
    /// <remarks>This low-level operation only changes the table. Larger scripts may also require the heap adjustment provided by the workspace EBOOT patch plan.</remarks>
    public static byte[] Update(ReadOnlySpan<byte> decryptedElf, ushort scriptId, int newSizeBytes)
    {
        Guard.Require(scriptId is >= RgoProfile.ScriptFirstId and <= RgoProfile.ScriptLastId,
            $"Script ID {scriptId} is outside the RGO table.");
        Guard.Require(newSizeBytes > 0, "New script size must be positive.");
        Guard.Require(newSizeBytes % (int)RgoProfile.ArchiveBlockSize == 0,
            $"New script size must be aligned to {RgoProfile.ArchiveBlockSize} bytes.");
        int newBlocks = checked(newSizeBytes / (int)RgoProfile.ArchiveBlockSize);
        Guard.Require(newBlocks <= ushort.MaxValue, "New script size exceeds the 16-bit block field.");

        int count = RgoProfile.ScriptLastId - RgoProfile.ScriptFirstId + 1;
        Guard.RequireRange(decryptedElf.Length, RgoProfile.ScriptSizeTableOffset, checked(count * 4),
            "RGO script size table");
        RgoVwfPatchInspection inspection = RgoVwfPatchProfile.Inspect(decryptedElf);
        Guard.Require(!inspection.SourceWasEncrypted && inspection.CompatibleKnownLineage,
            "Refusing to update a script table without a decrypted EBOOT from the verified RGO executable lineage.");
        byte[] output = decryptedElf.ToArray();
        int index = scriptId - RgoProfile.ScriptFirstId;
        int offset = checked(RgoProfile.ScriptSizeTableOffset + index * 4);
        ushort oldBlocks = BinaryData.ReadUInt16LittleEndian(output, offset, "RGO old script size");
        int delta = newBlocks - oldBlocks;

        BinaryData.WriteUInt16LittleEndian(output, offset, (ushort)newBlocks, "RGO new script size");
        for (int current = index; current < count; current++)
        {
            int cumulativeOffset = checked(RgoProfile.ScriptSizeTableOffset + current * 4 + 2);
            ushort oldCumulative = BinaryData.ReadUInt16LittleEndian(
                output, cumulativeOffset, "RGO cumulative script size");
            int newCumulative = checked(oldCumulative + delta);
            Guard.Require(newCumulative is >= 0 and <= ushort.MaxValue,
                "Updated cumulative script size exceeds the 16-bit field.");
            BinaryData.WriteUInt16LittleEndian(
                output, cumulativeOffset, (ushort)newCumulative, "RGO cumulative script size");
        }

        return output;
    }
}
