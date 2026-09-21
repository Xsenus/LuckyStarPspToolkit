using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using LuckyStarPspToolkit.Formats.Common;
using LuckyStarPspToolkit.Formats.Scripts;

namespace LuckyStarPspToolkit.Formats.Workspace;

/// <summary>
/// Represents the toolkit's EBOOT size patch plan model or service.
/// </summary>
public sealed class EbootSizePatchPlan
{
    /// <summary>The fixed known rgo decrypted eboot sha256 value used by this format or revision.</summary>
    public const string KnownRgoDecryptedEbootSha256 =
        "3e6c1c2f7136a69cecda4f39835c3454b739e23e89e862fe17462cdf915581d8";

    /// <summary>The schema revision; readers reject unsupported revisions.</summary>
    public int SchemaVersion { get; set; } = 1;
    /// <summary>The toolkit version that created this record.</summary>
    public string ToolVersion { get; set; } = ToolkitBuildInfo.Version;
    /// <summary>The UTC creation time of the manifest; it does not prove runtime compatibility.</summary>
    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
    /// <summary>The exact game profile used for interpreting scenario structures.</summary>
    public string Game { get; set; } = string.Empty;
    /// <summary>The expected decrypted eboot sha256 value used by this model or operation.</summary>
    public string? ExpectedDecryptedEbootSha256 { get; set; }
    /// <summary>The hexadecimal SHA-256 of the original, unmodified scenario archive.</summary>
    public string? SourceCpkSha256 { get; set; }
    /// <summary>The hexadecimal SHA-256 of the rebuilt archive associated with this plan.</summary>
    public string? RebuiltCpkSha256 { get; set; }
    /// <summary>The byte offset of the scenario size table within the decrypted executable.</summary>
    public int TableOffset { get; set; }
    /// <summary>The inclusive first scenario ID represented by the executable size table.</summary>
    public ushort FirstId { get; set; }
    /// <summary>The inclusive last scenario ID represented by the executable size table.</summary>
    public ushort LastIdInclusive { get; set; }
    /// <summary>The entries value used by this model or operation.</summary>
    public List<EbootSizePatchEntry> Entries { get; set; } = [];
    /// <summary>The warnings value used by this model or operation.</summary>
    public List<string> Warnings { get; set; } = [];

    /// <summary>
    /// Creates and validates a game-specific EBOOT size plan with sorted script entries and optional CPK hashes.
    /// </summary>
    /// <param name="profile">The revision-specific format or patch profile.</param>
    /// <param name="entries">The entries value.</param>
    /// <param name="sourceCpkSha256">The source CPK SHA 256 value.</param>
    /// <param name="rebuiltCpkSha256">The rebuilt CPK SHA 256 value.</param>
    /// <returns>The validated operation result.</returns>
    public static EbootSizePatchPlan Create(
        ScriptProfile profile,
        IEnumerable<EbootSizePatchEntry> entries,
        string? sourceCpkSha256 = null,
        string? rebuiltCpkSha256 = null)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(entries);
        (int tableOffset, ushort first, ushort last, string? hash) = profile.Game switch
        {
            LuckyStarGame.RyououGakuenOutousaiPortable =>
                (0x10319E, 0, 10, KnownRgoDecryptedEbootSha256),
            LuckyStarGame.NetIdolMeister =>
                (0x14C466, 0, 594, null),
            _ => throw new ToolkitException("EBOOT_PROFILE", $"Unsupported game profile {profile.Game}.")
        };
        EbootSizePatchPlan plan = new()
        {
            Game = profile.Game.ToString(),
            ExpectedDecryptedEbootSha256 = hash,
            SourceCpkSha256 = sourceCpkSha256,
            RebuiltCpkSha256 = rebuiltCpkSha256,
            TableOffset = tableOffset,
            FirstId = first,
            LastIdInclusive = last,
            Entries = entries.OrderBy(static entry => entry.Id).ToList()
        };
        if (profile.Game == LuckyStarGame.NetIdolMeister)
        {
            plan.Warnings.Add(
                "Net Idol Meister EBOOT hash is not known yet; application remains blocked unless an explicit unsafe override is supplied.");
        }
        plan.Warnings.Add(profile.Game == LuckyStarGame.RyououGakuenOutousaiPortable
            ? "The plan updates the CPK file-size table and increases the verified RGO script heap when a rebuilt script exceeds the original 0x236000-byte capacity. Runtime validation in PPSSPP and on hardware is still required."
            : "The plan updates the CPK file-size table only. A translation exceeding the original script heap must also be validated in PPSSPP and on hardware.");
        plan.Validate();
        return plan;
    }

    /// <summary>
    /// Validates the supplied state and rejects violated format invariants.
    /// </summary>
    /// <remarks>Malformed input or violated preconditions are rejected before a persistent output is committed.</remarks>
    public void Validate()
    {
        if (SchemaVersion != 1)
        {
            throw new ToolkitException("EBOOT_PLAN_SCHEMA", $"Unsupported EBOOT plan schema {SchemaVersion}.");
        }
        if (string.IsNullOrWhiteSpace(ToolVersion))
        {
            throw new ToolkitException("EBOOT_PLAN_VERSION", "EBOOT plan tool version is missing.");
        }
        if (!Enum.TryParse(Game, false, out LuckyStarGame game)
            || !Enum.IsDefined(typeof(LuckyStarGame), game))
        {
            throw new ToolkitException("EBOOT_PLAN_GAME", $"Unsupported EBOOT plan game '{Game}'.");
        }
        if (CreatedUtc == default)
        {
            throw new ToolkitException("EBOOT_PLAN_TIME", "EBOOT plan creation timestamp is missing.");
        }

        ValidateCanonicalLayout(game);
        ValidateOptionalHash(ExpectedDecryptedEbootSha256, "EBOOT_PLAN_EBOOT_HASH", "decrypted EBOOT");
        ValidateOptionalHash(SourceCpkSha256, "EBOOT_PLAN_SOURCE_HASH", "source CPK");
        ValidateOptionalHash(RebuiltCpkSha256, "EBOOT_PLAN_OUTPUT_HASH", "rebuilt CPK");
        if ((SourceCpkSha256 is null) != (RebuiltCpkSha256 is null))
        {
            throw new ToolkitException(
                "EBOOT_PLAN_PROVENANCE",
                "Source and rebuilt CPK hashes must either both be present or both be omitted.");
        }

        if (game == LuckyStarGame.RyououGakuenOutousaiPortable
            && !string.Equals(
                ExpectedDecryptedEbootSha256,
                KnownRgoDecryptedEbootSha256,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ToolkitException(
                "EBOOT_PLAN_EBOOT_HASH",
                "The verified RGO profile must retain its exact decrypted EBOOT SHA-256 precondition.");
        }

        if (Entries is null)
        {
            throw new ToolkitException("EBOOT_PLAN_ENTRIES", "EBOOT plan entries collection is null.");
        }
        Warnings ??= [];

        int tableCount = checked(LastIdInclusive - FirstId + 1);
        if (Entries.Count > tableCount)
        {
            throw new ToolkitException("EBOOT_PLAN_ENTRY", "EBOOT plan contains more entries than its declared table range.");
        }
        HashSet<ushort> ids = [];
        foreach (EbootSizePatchEntry? entry in Entries)
        {
            if (entry is null)
            {
                throw new ToolkitException("EBOOT_PLAN_ENTRY", "EBOOT plan contains a null entry.");
            }
            if (entry.Id < FirstId || entry.Id > LastIdInclusive || !ids.Add(entry.Id))
            {
                throw new ToolkitException("EBOOT_PLAN_ENTRY", $"Invalid or duplicate EBOOT plan entry {entry.Id}.");
            }
            if (entry.OldBlocks2K == 0 || entry.NewBlocks2K == 0)
            {
                throw new ToolkitException(
                    "EBOOT_PLAN_SIZE",
                    $"Invalid block count for script {entry.Id}: {entry.OldBlocks2K} -> {entry.NewBlocks2K}.");
            }
        }
    }

    /// <summary>
    /// Applies the requested transformation after validating all preconditions.
    /// </summary>
    /// <param name="decryptedEboot">The decrypted EBOOT value.</param>
    /// <param name="allowUnknownHash">The allow unknown hash value.</param>
    /// <returns>The resulting binary or typed sequence.</returns>
    /// <remarks>Malformed input or violated preconditions are rejected before a persistent output is committed.</remarks>
    public byte[] Apply(ReadOnlySpan<byte> decryptedEboot, bool allowUnknownHash = false)
    {
        Validate();
        string actualHash = BinaryUtilities.Sha256Hex(decryptedEboot);
        if (!string.IsNullOrWhiteSpace(ExpectedDecryptedEbootSha256))
        {
            if (!string.Equals(actualHash, ExpectedDecryptedEbootSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new ToolkitException(
                    "EBOOT_HASH",
                    $"Decrypted EBOOT SHA-256 mismatch. Expected {ExpectedDecryptedEbootSha256}, got {actualHash}.");
            }
        }
        else if (!allowUnknownHash)
        {
            throw new ToolkitException(
                "EBOOT_HASH_UNKNOWN",
                "This game profile has no verified EBOOT hash. Refusing to apply without an explicit unsafe override.");
        }

        byte[] output = EbootSizeTablePatcher.Apply(
            decryptedEboot,
            TableOffset,
            FirstId,
            LastIdInclusive,
            Entries);
        if (Enum.Parse<LuckyStarGame>(Game, false) == LuckyStarGame.RyououGakuenOutousaiPortable)
        {
            RgoScriptHeapPatcher.EnsureCapacity(
                output,
                TableOffset,
                FirstId,
                LastIdInclusive);
        }
        return output;
    }

    /// <summary>
    /// Loads and validates the requested data from its source.
    /// </summary>
    /// <param name="path">The file-system path to process.</param>
    /// <param name="limits">Optional conservative safety limits; defaults are used when omitted.</param>
    /// <returns>The validated operation result.</returns>
    public static EbootSizePatchPlan Load(string path, FileLimits? limits = null)
    {
        limits ??= FileLimits.Default;
        byte[] data = BinaryUtilities.ReadAllBytesBounded(
            path, limits with { MaximumInputBytes = limits.MaximumTextBytes });
        try
        {
            _ = new UTF8Encoding(false, true).GetCharCount(data);
        }
        catch (DecoderFallbackException ex)
        {
            throw new ToolkitException("EBOOT_PLAN_UTF8", "EBOOT plan is not valid UTF-8.", ex);
        }

        try
        {
            EbootSizePatchPlan? plan = StrictJson.Deserialize<EbootSizePatchPlan>(data, JsonOptions, "EBOOT_PLAN_JSON");
            if (plan is null)
            {
                throw new ToolkitException("EBOOT_PLAN_JSON", "EBOOT plan JSON is empty.");
            }
            plan.Validate();
            return plan;
        }
        catch (JsonException ex)
        {
            throw new ToolkitException("EBOOT_PLAN_JSON", $"EBOOT plan JSON is invalid: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Persists the validated data to its destination.
    /// </summary>
    /// <param name="path">The file-system path to process.</param>
    public void Save(string path)
        => AtomicFile.WriteAllText(path, JsonSerializer.Serialize(this, JsonOptions));

    /// <summary>The json options value used by this model or operation.</summary>
    public static JsonSerializerOptions JsonOptions { get; } = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = false,
        AllowTrailingCommas = false,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    /// <summary>
    /// Validates canonical layout while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="game">The game value.</param>
    /// <remarks>Malformed input or violated preconditions are rejected before a persistent output is committed.</remarks>
    private void ValidateCanonicalLayout(LuckyStarGame game)
    {
        (int expectedOffset, ushort expectedFirst, ushort expectedLast) = game switch
        {
            LuckyStarGame.RyououGakuenOutousaiPortable => (0x10319E, 0, 10),
            LuckyStarGame.NetIdolMeister => (0x14C466, 0, 594),
            _ => throw new ToolkitException("EBOOT_PLAN_GAME", $"Unsupported EBOOT plan game '{Game}'.")
        };
        if (TableOffset != expectedOffset || FirstId != expectedFirst || LastIdInclusive != expectedLast)
        {
            throw new ToolkitException(
                "EBOOT_PLAN_LAYOUT",
                $"EBOOT plan layout does not match {game}: expected table 0x{expectedOffset:X}, IDs {expectedFirst}..{expectedLast}.");
        }
    }

    /// <summary>
    /// Validates optional hash while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="hash">An optional incremental hash updated with copied bytes.</param>
    /// <param name="code">The code value.</param>
    /// <param name="name">The logical name used for lookup or diagnostics.</param>
    /// <remarks>Malformed input or violated preconditions are rejected before a persistent output is committed.</remarks>
    private static void ValidateOptionalHash(string? hash, string code, string name)
    {
        if (hash is null)
        {
            return;
        }
        if (hash.Length != 64 || hash.Any(static character => !Uri.IsHexDigit(character)))
        {
            throw new ToolkitException(code, $"Expected {name} SHA-256 is not a 64-character hexadecimal value.");
        }
    }
}

/// <summary>
/// Provides RGO script heap patcher operations with strict bounds and format validation.
/// </summary>
internal static class RgoScriptHeapPatcher
{
    /// <summary>The fixed lui offset value used by this format or revision.</summary>
    internal const int LuiOffset = 0x15B54;
    /// <summary>The fixed addiu offset value used by this format or revision.</summary>
    internal const int AddiuOffset = 0x15B58;
    /// <summary>The fixed original capacity bytes value used by this format or revision.</summary>
    internal const int OriginalCapacityBytes = 0x236000;
    /// <summary>The fixed original lui instruction value used by this format or revision.</summary>
    internal const uint OriginalLuiInstruction = 0x3C040023;
    /// <summary>The fixed original addiu instruction value used by this format or revision.</summary>
    internal const uint OriginalAddiuInstruction = 0x24846000;

    /// <summary>The fixed lui opcode and register mask value used by this format or revision.</summary>
    private const uint LuiOpcodeAndRegisterMask = 0xFFFF0000;
    /// <summary>The fixed lui a0 instruction value used by this format or revision.</summary>
    private const uint LuiA0Instruction = 0x3C040000;
    /// <summary>The fixed addiu opcode and registers mask value used by this format or revision.</summary>
    private const uint AddiuOpcodeAndRegistersMask = 0xFFFF0000;
    /// <summary>The fixed addiu a0 a0 instruction value used by this format or revision.</summary>
    private const uint AddiuA0A0Instruction = 0x24840000;
    /// <summary>The fixed archive block size value used by this format or revision.</summary>
    private const int ArchiveBlockSize = 2048;

    /// <summary>
    /// Ensures capacity while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="eboot">The eboot value.</param>
    /// <param name="tableOffset">The table offset value.</param>
    /// <param name="firstId">The first id value.</param>
    /// <param name="lastIdInclusive">The last id inclusive value.</param>
    internal static void EnsureCapacity(
        Span<byte> eboot,
        int tableOffset,
        ushort firstId,
        ushort lastIdInclusive)
    {
        if (lastIdInclusive < firstId)
        {
            throw new ToolkitException("EBOOT_HEAP_TABLE", "Invalid RGO script-table ID range.");
        }
        int count = checked(lastIdInclusive - firstId + 1);
        int tableLength = checked(count * 4);
        if (tableOffset < 0 || tableOffset > eboot.Length - tableLength)
        {
            throw new ToolkitException("EBOOT_HEAP_TABLE", "RGO script table is outside the EBOOT.");
        }
        if (LuiOffset > eboot.Length - sizeof(uint) || AddiuOffset > eboot.Length - sizeof(uint))
        {
            throw new ToolkitException("EBOOT_HEAP_RANGE", "RGO script-heap instructions are outside the EBOOT.");
        }

        int largestBlocks = 0;
        for (int index = 0; index < count; index++)
        {
            int rowOffset = checked(tableOffset + index * 4);
            ushort blocks = BinaryPrimitives.ReadUInt16LittleEndian(eboot.Slice(rowOffset, 2));
            if (blocks == 0)
            {
                throw new ToolkitException(
                    "EBOOT_HEAP_TABLE",
                    $"RGO script {index + firstId} has a zero block count.");
            }
            largestBlocks = Math.Max(largestBlocks, blocks);
        }
        int requiredBytes = Math.Max(
            OriginalCapacityBytes,
            checked(largestBlocks * ArchiveBlockSize));

        uint currentLui = BinaryPrimitives.ReadUInt32LittleEndian(eboot.Slice(LuiOffset, sizeof(uint)));
        uint currentAddiu = BinaryPrimitives.ReadUInt32LittleEndian(eboot.Slice(AddiuOffset, sizeof(uint)));
        int currentBytes = DecodeCapacity(currentLui, currentAddiu);
        if (currentBytes != OriginalCapacityBytes && currentBytes != requiredBytes)
        {
            throw new ToolkitException(
                "EBOOT_HEAP_PRECONDITION",
                $"RGO script heap is {currentBytes} bytes; expected the verified original " +
                $"{OriginalCapacityBytes} bytes or the required {requiredBytes} bytes.");
        }

        (uint newLui, uint newAddiu) = EncodeCapacity(requiredBytes);
        BinaryPrimitives.WriteUInt32LittleEndian(eboot.Slice(LuiOffset, sizeof(uint)), newLui);
        BinaryPrimitives.WriteUInt32LittleEndian(eboot.Slice(AddiuOffset, sizeof(uint)), newAddiu);
        int verifiedBytes = DecodeCapacity(
            BinaryPrimitives.ReadUInt32LittleEndian(eboot.Slice(LuiOffset, sizeof(uint))),
            BinaryPrimitives.ReadUInt32LittleEndian(eboot.Slice(AddiuOffset, sizeof(uint))));
        if (verifiedBytes != requiredBytes)
        {
            throw new ToolkitException(
                "EBOOT_HEAP_VERIFY",
                $"RGO script heap verification failed: expected {requiredBytes}, got {verifiedBytes}.");
        }
    }

    /// <summary>
    /// Decodes capacity while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="luiInstruction">The lui instruction value.</param>
    /// <param name="addiuInstruction">The addiu instruction value.</param>
    /// <returns>The validated operation result.</returns>
    internal static int DecodeCapacity(uint luiInstruction, uint addiuInstruction)
    {
        if ((luiInstruction & LuiOpcodeAndRegisterMask) != LuiA0Instruction
            || (addiuInstruction & AddiuOpcodeAndRegistersMask) != AddiuA0A0Instruction)
        {
            throw new ToolkitException(
                "EBOOT_HEAP_INSTRUCTION",
                "RGO script-heap instructions are not the verified 'lui a0' / 'addiu a0,a0' pair.");
        }
        int high = checked((int)(luiInstruction & 0xFFFFU));
        int signedLow = unchecked((short)(addiuInstruction & 0xFFFFU));
        long decoded = ((long)high << 16) + signedLow;
        if (decoded <= 0 || decoded > int.MaxValue)
        {
            throw new ToolkitException(
                "EBOOT_HEAP_INSTRUCTION",
                $"Decoded RGO script-heap capacity is outside the supported range: {decoded}.");
        }
        return (int)decoded;
    }

    /// <summary>
    /// Encodes capacity while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="bytes">The bytes value.</param>
    /// <returns>The validated operation result.</returns>
    internal static (uint Lui, uint Addiu) EncodeCapacity(int bytes)
    {
        if (bytes <= 0 || (bytes & (ArchiveBlockSize - 1)) != 0)
        {
            throw new ToolkitException(
                "EBOOT_HEAP_SIZE",
                $"RGO script-heap capacity must be a positive {ArchiveBlockSize}-byte multiple.");
        }
        long high = ((long)bytes + 0x8000L) >> 16;
        if (high > ushort.MaxValue)
        {
            throw new ToolkitException(
                "EBOOT_HEAP_SIZE",
                $"RGO script-heap capacity {bytes} cannot be encoded by the verified MIPS instruction pair.");
        }
        uint lui = LuiA0Instruction | (uint)high;
        uint addiu = AddiuA0A0Instruction | (ushort)bytes;
        if (DecodeCapacity(lui, addiu) != bytes)
        {
            throw new ToolkitException("EBOOT_HEAP_VERIFY", "Encoded RGO script-heap capacity did not round-trip.");
        }
        return (lui, addiu);
    }
}

/// <summary>
/// Represents the toolkit's EBOOT size patch entry model or service.
/// </summary>
public sealed class EbootSizePatchEntry
{
    /// <summary>The resource identifier preserved across extraction and rebuilding.</summary>
    public ushort Id { get; set; }
    /// <summary>The old blocks2 k value used by this model or operation.</summary>
    public ushort OldBlocks2K { get; set; }
    /// <summary>The new blocks2 k value used by this model or operation.</summary>
    public ushort NewBlocks2K { get; set; }
}

/// <summary>
/// Provides EBOOT size table patcher operations with strict bounds and format validation.
/// </summary>
internal static class EbootSizeTablePatcher
{
    /// <summary>
    /// Applies the requested transformation after validating all preconditions.
    /// </summary>
    /// <param name="source">The source binary data or object.</param>
    /// <param name="tableOffset">The table offset value.</param>
    /// <param name="firstId">The first id value.</param>
    /// <param name="lastIdInclusive">The last id inclusive value.</param>
    /// <param name="entries">The entries value.</param>
    /// <returns>The resulting binary or typed sequence.</returns>
    /// <remarks>Malformed input or violated preconditions are rejected before a persistent output is committed.</remarks>
    internal static byte[] Apply(
        ReadOnlySpan<byte> source,
        int tableOffset,
        ushort firstId,
        ushort lastIdInclusive,
        IReadOnlyCollection<EbootSizePatchEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        if (tableOffset < 0 || (tableOffset & 1) != 0 || lastIdInclusive < firstId)
        {
            throw new ToolkitException("EBOOT_TABLE_LAYOUT", "Invalid EBOOT size table layout.");
        }

        int count = checked(lastIdInclusive - firstId + 1);
        int tableLength = checked(count * 4);
        if (tableOffset > source.Length - tableLength)
        {
            throw new ToolkitException(
                "EBOOT_TABLE_RANGE",
                $"EBOOT size table [0x{tableOffset:X},0x{tableOffset + tableLength:X}) exceeds file length 0x{source.Length:X}.");
        }

        ushort[] originalSizes = new ushort[count];
        ushort[] originalCumulative = new ushort[count];
        for (int index = 0; index < count; index++)
        {
            int rowOffset = checked(tableOffset + index * 4);
            originalSizes[index] = BinaryPrimitives.ReadUInt16LittleEndian(source.Slice(rowOffset, 2));
            originalCumulative[index] = BinaryPrimitives.ReadUInt16LittleEndian(source.Slice(rowOffset + 2, 2));
        }

        int originalRunningTotal = 0;
        for (int index = 0; index < count; index++)
        {
            originalRunningTotal = checked(originalRunningTotal + originalSizes[index]);
            if (originalRunningTotal > ushort.MaxValue
                || originalCumulative[index] != originalRunningTotal)
            {
                throw new ToolkitException(
                    "EBOOT_CUMULATIVE_PRECONDITION",
                    $"EBOOT cumulative value for script {index + firstId} is {originalCumulative[index]}, " +
                    $"but the running size total is {originalRunningTotal}.");
            }
        }

        Dictionary<int, EbootSizePatchEntry> byIndex = [];
        foreach (EbootSizePatchEntry? entry in entries)
        {
            if (entry is null)
            {
                throw new ToolkitException("EBOOT_PLAN_ENTRY", "EBOOT size update contains a null entry.");
            }
            if (entry.Id < firstId || entry.Id > lastIdInclusive)
            {
                throw new ToolkitException("EBOOT_PLAN_ENTRY", $"Script ID {entry.Id} is outside the table range.");
            }
            int index = entry.Id - firstId;
            if (!byIndex.TryAdd(index, entry))
            {
                throw new ToolkitException("EBOOT_PLAN_ENTRY", $"Duplicate EBOOT plan entry {entry.Id}.");
            }
            if (originalSizes[index] != entry.OldBlocks2K)
            {
                throw new ToolkitException(
                    "EBOOT_OLD_SIZE",
                    $"Script {entry.Id} size table value is {originalSizes[index]}, plan expects {entry.OldBlocks2K}.");
            }
        }

        ushort[] expectedSizes = originalSizes.ToArray();
        foreach ((int index, EbootSizePatchEntry entry) in byIndex)
        {
            expectedSizes[index] = entry.NewBlocks2K;
        }

        ushort[] expectedCumulative = new ushort[count];
        int updatedRunningTotal = 0;
        for (int index = 0; index < count; index++)
        {
            updatedRunningTotal = checked(updatedRunningTotal + expectedSizes[index]);
            if (updatedRunningTotal > ushort.MaxValue)
            {
                throw new ToolkitException(
                    "EBOOT_CUMULATIVE_RANGE",
                    $"Cumulative EBOOT table value for script {index + firstId} exceeds the 16-bit field.");
            }
            expectedCumulative[index] = (ushort)updatedRunningTotal;
        }

        byte[] output = source.ToArray();
        for (int index = 0; index < count; index++)
        {
            int rowOffset = checked(tableOffset + index * 4);
            BinaryPrimitives.WriteUInt16LittleEndian(output.AsSpan(rowOffset, 2), expectedSizes[index]);
            BinaryPrimitives.WriteUInt16LittleEndian(output.AsSpan(rowOffset + 2, 2), expectedCumulative[index]);
        }

        for (int index = 0; index < count; index++)
        {
            int rowOffset = checked(tableOffset + index * 4);
            ushort actualSize = BinaryPrimitives.ReadUInt16LittleEndian(output.AsSpan(rowOffset, 2));
            ushort actualCumulative = BinaryPrimitives.ReadUInt16LittleEndian(output.AsSpan(rowOffset + 2, 2));
            if (actualSize != expectedSizes[index] || actualCumulative != expectedCumulative[index])
            {
                throw new ToolkitException(
                    "EBOOT_VERIFY_TABLE",
                    $"EBOOT size table verification failed for script {index + firstId}.");
            }
        }
        return output;
    }
}
