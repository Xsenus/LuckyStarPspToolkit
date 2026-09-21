#!/usr/bin/env python3
"""Independent oracle for the ULJM05752 Russian variable-width-font EBOOT patch.

The validator deliberately does not execute the C# implementation. It decrypts the
customer-supplied PRX with the existing independent Python crypto oracle, loads the
published declarative patch profile, applies every 32-bit word itself, and validates
lineage, idempotence, partial completion, size-table coexistence, and tamper rejection.
It writes metadata only; patched customer binaries are never emitted.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import re
import struct
import zipfile
from collections import Counter
from pathlib import Path
from typing import Any, Iterable

from validate_customer_data import decrypt_prx, sha256

PROFILE_SCHEMA = "lucky-star-psp.rgo-eboot-vwf-profile.v1"
REPORT_SCHEMA = "lucky-star-psp.rgo-eboot-vwf-validation.v1"
UINT32_MAX = 0xFFFFFFFF
ROOT = Path(__file__).resolve().parents[1]


def portable_path(path: Path) -> str:
    """Return a reproducible report path without leaking the local workspace root."""
    resolved = path.resolve()
    try:
        return resolved.relative_to(ROOT).as_posix()
    except ValueError:
        return path.name


class ValidationError(ValueError):
    """Expected fail-closed validation error."""


def read_u16(data: bytes | bytearray, offset: int) -> int:
    return struct.unpack_from("<H", data, offset)[0]


def read_u32(data: bytes | bytearray, offset: int) -> int:
    return struct.unpack_from("<I", data, offset)[0]


def write_u16(data: bytearray, offset: int, value: int) -> None:
    struct.pack_into("<H", data, offset, value)


def write_u32(data: bytearray, offset: int, value: int) -> None:
    struct.pack_into("<I", data, offset, value)


def load_profile(path: Path) -> dict[str, Any]:
    profile = json.loads(path.read_text(encoding="utf-8"))
    if not isinstance(profile, dict):
        raise ValidationError("profile root is not an object")
    expected_root = {
        "schema", "profileId", "game", "discId", "input", "fullOutput",
        "scriptSizeTable", "scriptHeap", "groups", "patches",
    }
    if set(profile) != expected_root:
        raise ValidationError(f"profile root fields mismatch: {sorted(set(profile) ^ expected_root)}")
    if profile["schema"] != PROFILE_SCHEMA:
        raise ValidationError(f"unsupported profile schema {profile['schema']!r}")
    if profile["game"] != "RyououGakuenOutousaiPortable" or profile["discId"] != "ULJM05752":
        raise ValidationError("profile identifies the wrong game revision")

    input_info = profile["input"]
    output_info = profile["fullOutput"]
    if input_info.get("kind") != "decrypted-elf32-mips":
        raise ValidationError("profile input kind mismatch")
    for label, info in (("input", input_info), ("fullOutput", output_info)):
        if not isinstance(info.get("size"), int) or info["size"] <= 0:
            raise ValidationError(f"{label} size is invalid")
        digest = info.get("sha256")
        if not isinstance(digest, str) or re.fullmatch(r"[0-9a-f]{64}", digest) is None:
            raise ValidationError(f"{label} SHA-256 is invalid")
    if input_info["size"] != output_info["size"]:
        raise ValidationError("full patch must not change ELF length")

    table = profile["scriptSizeTable"]
    required_table = {"offset", "offsetHex", "firstId", "lastIdInclusive", "canonicalBytesHex"}
    if not isinstance(table, dict) or set(table) != required_table:
        raise ValidationError("script-size-table fields mismatch")
    if table["offsetHex"] != f"0x{table['offset']:08X}":
        raise ValidationError("script-size-table hexadecimal offset is inconsistent")
    if table["firstId"] != 0 or table["lastIdInclusive"] != 10:
        raise ValidationError("script-size-table ID range mismatch")
    canonical_table = bytes.fromhex(table["canonicalBytesHex"])
    expected_table_length = (table["lastIdInclusive"] - table["firstId"] + 1) * 4
    if len(canonical_table) != expected_table_length:
        raise ValidationError("canonical script-size-table length mismatch")

    heap = profile["scriptHeap"]
    required_heap = {
        "luiOffset", "luiOffsetHex", "addiuOffset", "addiuOffsetHex",
        "originalCapacityBytes", "originalCapacityHex",
        "originalLuiInstruction", "originalLuiInstructionHex",
        "originalAddiuInstruction", "originalAddiuInstructionHex", "policy",
    }
    if not isinstance(heap, dict) or set(heap) != required_heap:
        raise ValidationError("script-heap fields mismatch")
    if heap["luiOffsetHex"] != f"0x{heap['luiOffset']:08X}":
        raise ValidationError("script-heap lui offset hex mismatch")
    if heap["addiuOffsetHex"] != f"0x{heap['addiuOffset']:08X}":
        raise ValidationError("script-heap addiu offset hex mismatch")
    if heap["originalCapacityHex"] != f"0x{heap['originalCapacityBytes']:08X}":
        raise ValidationError("script-heap capacity hex mismatch")
    if heap["originalLuiInstructionHex"] != f"0x{heap['originalLuiInstruction']:08X}":
        raise ValidationError("script-heap lui instruction hex mismatch")
    if heap["originalAddiuInstructionHex"] != f"0x{heap['originalAddiuInstruction']:08X}":
        raise ValidationError("script-heap addiu instruction hex mismatch")
    if heap["policy"] != "max-original-or-largest-script":
        raise ValidationError("script-heap policy mismatch")
    if heap["luiOffset"] % 4 or heap["addiuOffset"] % 4:
        raise ValidationError("script-heap instruction offsets must be four-byte aligned")
    if heap["luiOffset"] + 4 > input_info["size"] or heap["addiuOffset"] + 4 > input_info["size"]:
        raise ValidationError("script-heap instruction is outside the input")
    encoded_original = encode_heap_capacity(heap["originalCapacityBytes"])
    if encoded_original != (heap["originalLuiInstruction"], heap["originalAddiuInstruction"]):
        raise ValidationError("script-heap original instructions do not encode the declared capacity")

    groups = profile["groups"]
    patches = profile["patches"]
    if not isinstance(groups, list) or not groups:
        raise ValidationError("profile groups are empty")
    if not isinstance(patches, list) or not patches:
        raise ValidationError("profile patches are empty")
    group_ids: list[str] = []
    for group in groups:
        if not isinstance(group, dict) or set(group) != {"id", "description", "patchCount"}:
            raise ValidationError("patch group fields mismatch")
        group_id = group["id"]
        if not isinstance(group_id, str) or not re.fullmatch(r"[a-z0-9-]+", group_id):
            raise ValidationError("patch group ID is invalid")
        if group_id in group_ids:
            raise ValidationError(f"duplicate patch group {group_id}")
        if not isinstance(group["description"], str) or not group["description"]:
            raise ValidationError(f"patch group {group_id} has no description")
        if not isinstance(group["patchCount"], int) or group["patchCount"] <= 0:
            raise ValidationError(f"patch group {group_id} count is invalid")
        group_ids.append(group_id)

    names: set[str] = set()
    offsets: set[int] = set()
    counts: Counter[str] = Counter()
    expected_patch_fields = {
        "group", "name", "offset", "offsetHex", "expected", "expectedHex",
        "replacement", "replacementHex",
    }
    for patch in patches:
        if not isinstance(patch, dict) or set(patch) != expected_patch_fields:
            raise ValidationError("patch entry fields mismatch")
        group = patch["group"]
        name = patch["name"]
        offset = patch["offset"]
        expected = patch["expected"]
        replacement = patch["replacement"]
        if group not in group_ids:
            raise ValidationError(f"patch {name!r} references unknown group {group!r}")
        if not isinstance(name, str) or not name or name in names:
            raise ValidationError(f"invalid or duplicate patch name {name!r}")
        if not isinstance(offset, int) or offset < 0 or offset % 4 or offset in offsets:
            raise ValidationError(f"invalid or duplicate patch offset {offset!r}")
        if offset + 4 > input_info["size"]:
            raise ValidationError(f"patch {name!r} is outside the declared input")
        for value_name, value in (("expected", expected), ("replacement", replacement)):
            if not isinstance(value, int) or not 0 <= value <= UINT32_MAX:
                raise ValidationError(f"patch {name!r} {value_name} is not UInt32")
        if expected == replacement:
            raise ValidationError(f"patch {name!r} is a no-op")
        if patch["offsetHex"] != f"0x{offset:08X}":
            raise ValidationError(f"patch {name!r} offset hex mismatch")
        if patch["expectedHex"] != f"0x{expected:08X}":
            raise ValidationError(f"patch {name!r} expected hex mismatch")
        if patch["replacementHex"] != f"0x{replacement:08X}":
            raise ValidationError(f"patch {name!r} replacement hex mismatch")
        names.add(name)
        offsets.add(offset)
        counts[group] += 1

    declared_counts = {item["id"]: item["patchCount"] for item in groups}
    if dict(counts) != declared_counts:
        raise ValidationError(f"group counts mismatch: declared={declared_counts}, actual={dict(counts)}")
    return profile


def compare_csharp_profile(profile: dict[str, Any], csharp_path: Path) -> None:
    source = csharp_path.read_text(encoding="utf-8")
    pattern = re.compile(
        r'new\("(?P<group>[^"]+)",\s*"(?P<name>[^"]+)",\s*'
        r'0x(?P<offset>[0-9A-Fa-f]+),\s*0x(?P<expected>[0-9A-Fa-f]+),\s*'
        r'0x(?P<replacement>[0-9A-Fa-f]+)\),'
    )
    parsed = [
        {
            "group": match.group("group"),
            "name": match.group("name"),
            "offset": int(match.group("offset"), 16),
            "expected": int(match.group("expected"), 16),
            "replacement": int(match.group("replacement"), 16),
        }
        for match in pattern.finditer(source)
    ]
    expected = [
        {key: patch[key] for key in ("group", "name", "offset", "expected", "replacement")}
        for patch in profile["patches"]
    ]
    if parsed != expected:
        if len(parsed) != len(expected):
            raise ValidationError(
                f"C# patch definition count mismatch: source={len(parsed)}, profile={len(expected)}"
            )
        for index, (actual, wanted) in enumerate(zip(parsed, expected)):
            if actual != wanted:
                raise ValidationError(
                    f"C# patch definition mismatch at index {index}: {actual!r} != {wanted!r}"
                )
        raise ValidationError("C# patch definitions do not match the declarative profile")

    if profile["profileId"] not in source:
        raise ValidationError("C# source does not contain profile ID")
    if profile["input"]["sha256"] not in source:
        # The pristine hash lives in RgoProfile.cs, so this check is completed by static_validate.py.
        pass
    if profile["fullOutput"]["sha256"] not in source:
        raise ValidationError("C# source does not contain the full-patch SHA-256")


def selected_patches(profile: dict[str, Any], groups: Iterable[str] | None) -> list[dict[str, Any]]:
    if groups is None:
        return list(profile["patches"])
    group_set = set(groups)
    known = {item["id"] for item in profile["groups"]}
    unknown = group_set - known
    if unknown:
        raise ValidationError(f"unknown groups: {sorted(unknown)}")
    return [patch for patch in profile["patches"] if patch["group"] in group_set]


def apply_patch(
    source: bytes,
    profile: dict[str, Any],
    groups: Iterable[str] | None = None,
) -> tuple[bytes, int, int]:
    output = bytearray(source)
    applied = 0
    already = 0
    for patch in selected_patches(profile, groups):
        actual = read_u32(output, patch["offset"])
        if actual == patch["replacement"]:
            already += 1
            continue
        if actual != patch["expected"]:
            raise ValidationError(
                f"patch word mismatch at {patch['offsetHex']}: "
                f"expected {patch['expectedHex']} or {patch['replacementHex']}, got 0x{actual:08X}"
            )
        write_u32(output, patch["offset"], patch["replacement"])
        applied += 1
    return bytes(output), applied, already


def script_table_consistent(data: bytes, profile: dict[str, Any]) -> bool:
    table = profile["scriptSizeTable"]
    running = 0
    for script_id in range(table["firstId"], table["lastIdInclusive"] + 1):
        offset = table["offset"] + (script_id - table["firstId"]) * 4
        blocks = read_u16(data, offset)
        cumulative = read_u16(data, offset + 2)
        if blocks == 0:
            return False
        running += blocks
        if running > 0xFFFF or cumulative != running:
            return False
    return True


def required_heap_capacity(data: bytes, profile: dict[str, Any]) -> int:
    table = profile["scriptSizeTable"]
    largest_blocks = max(
        read_u16(data, table["offset"] + (script_id - table["firstId"]) * 4)
        for script_id in range(table["firstId"], table["lastIdInclusive"] + 1)
    )
    return max(profile["scriptHeap"]["originalCapacityBytes"], largest_blocks * 2048)


def encode_heap_capacity(capacity: int) -> tuple[int, int]:
    if capacity <= 0 or capacity % 2048:
        raise ValidationError("script-heap capacity must be a positive 2048-byte multiple")
    high = (capacity + 0x8000) >> 16
    if high > 0xFFFF:
        raise ValidationError("script-heap capacity does not fit verified lui/addiu encoding")
    lui = 0x3C040000 | high
    addiu = 0x24840000 | (capacity & 0xFFFF)
    if decode_heap_instructions(lui, addiu) != capacity:
        raise ValidationError("script-heap encoding did not round-trip")
    return lui, addiu


def decode_heap_instructions(lui: int, addiu: int) -> int:
    if lui & 0xFFFF0000 != 0x3C040000 or addiu & 0xFFFF0000 != 0x24840000:
        raise ValidationError("script-heap instructions are not lui a0 / addiu a0,a0")
    low = addiu & 0xFFFF
    if low & 0x8000:
        low -= 0x10000
    capacity = ((lui & 0xFFFF) << 16) + low
    if capacity <= 0:
        raise ValidationError("script-heap capacity is not positive")
    return capacity


def inspect_heap(data: bytes, profile: dict[str, Any]) -> tuple[int, int]:
    heap = profile["scriptHeap"]
    actual = decode_heap_instructions(
        read_u32(data, heap["luiOffset"]),
        read_u32(data, heap["addiuOffset"]),
    )
    required = required_heap_capacity(data, profile)
    if actual != required:
        raise ValidationError(f"script heap is {actual} bytes, current table requires {required}")
    return actual, required


def write_heap_capacity(data: bytearray, profile: dict[str, Any], capacity: int) -> None:
    heap = profile["scriptHeap"]
    lui, addiu = encode_heap_capacity(capacity)
    write_u32(data, heap["luiOffset"], lui)
    write_u32(data, heap["addiuOffset"], addiu)


def canonical_lineage_hash(data: bytes, profile: dict[str, Any]) -> tuple[str, bool]:
    canonical = bytearray(data)
    for patch in profile["patches"]:
        actual = read_u32(canonical, patch["offset"])
        if actual == patch["replacement"]:
            write_u32(canonical, patch["offset"], patch["expected"])
        elif actual != patch["expected"]:
            return sha256(bytes(canonical)), False
    if not script_table_consistent(bytes(canonical), profile):
        return sha256(bytes(canonical)), False
    try:
        inspect_heap(bytes(canonical), profile)
    except ValidationError:
        return sha256(bytes(canonical)), False
    table = profile["scriptSizeTable"]
    canonical_table = bytes.fromhex(table["canonicalBytesHex"])
    canonical[table["offset"]:table["offset"] + len(canonical_table)] = canonical_table
    heap = profile["scriptHeap"]
    write_u32(canonical, heap["luiOffset"], heap["originalLuiInstruction"])
    write_u32(canonical, heap["addiuOffset"], heap["originalAddiuInstruction"])
    digest = sha256(bytes(canonical))
    return digest, digest == profile["input"]["sha256"]


def read_customer_eboot(archive_path: Path) -> bytes:
    with zipfile.ZipFile(archive_path) as archive:
        matches = [
            info for info in archive.infolist()
            if not info.is_dir() and Path(info.filename).name == "EBOOT.BIN"
        ]
        if len(matches) != 1:
            raise ValidationError(f"expected exactly one EBOOT.BIN, got {len(matches)}")
        return archive.read(matches[0])


def changed_offsets(before: bytes, after: bytes) -> list[int]:
    if len(before) != len(after):
        raise ValidationError("comparison inputs have different lengths")
    return [index for index, (left, right) in enumerate(zip(before, after)) if left != right]


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("archive", type=Path)
    parser.add_argument(
        "--profile",
        type=Path,
        default=ROOT / "profiles/rgo-uljm05752-vwf-profile.json",
    )
    parser.add_argument(
        "--csharp-source",
        type=Path,
        default=ROOT / "src/LuckyStarPspToolkit.Core/RgoVwfPatchProfile.cs",
    )
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()

    profile = load_profile(args.profile)
    compare_csharp_profile(profile, args.csharp_source)
    encrypted = read_customer_eboot(args.archive)
    decrypted, crypto = decrypt_prx(encrypted)
    if len(decrypted) != profile["input"]["size"]:
        raise ValidationError("decrypted EBOOT size mismatch")
    if sha256(decrypted) != profile["input"]["sha256"]:
        raise ValidationError("decrypted EBOOT hash mismatch")
    if not script_table_consistent(decrypted, profile):
        raise ValidationError("original script-size table is inconsistent")
    original_heap, original_required_heap = inspect_heap(decrypted, profile)
    if original_heap != profile["scriptHeap"]["originalCapacityBytes"]:
        raise ValidationError("original script heap does not match the profile")

    for patch in profile["patches"]:
        actual = read_u32(decrypted, patch["offset"])
        if actual != patch["expected"]:
            raise ValidationError(
                f"customer EBOOT precondition mismatch at {patch['offsetHex']}: 0x{actual:08X}"
            )

    full, full_applied, full_already = apply_patch(decrypted, profile)
    if full_applied != len(profile["patches"]) or full_already:
        raise ValidationError("full patch count mismatch")
    if len(full) != profile["fullOutput"]["size"]:
        raise ValidationError("full patch changed EBOOT size")
    if sha256(full) != profile["fullOutput"]["sha256"]:
        raise ValidationError("full patch output SHA-256 mismatch")

    permitted_bytes = {
        patch["offset"] + byte_index
        for patch in profile["patches"]
        for byte_index in range(4)
    }
    actual_changed = changed_offsets(decrypted, full)
    unexpected_changed = sorted(set(actual_changed) - permitted_bytes)
    if unexpected_changed:
        raise ValidationError(f"full patch changed unrelated bytes: {unexpected_changed[:16]}")

    idempotent, idempotent_applied, idempotent_already = apply_patch(full, profile)
    if idempotent != full or idempotent_applied or idempotent_already != len(profile["patches"]):
        raise ValidationError("full patch is not idempotent")

    core, core_applied, core_already = apply_patch(decrypted, profile, ["vwf-core"])
    if core_applied != 66 or core_already:
        raise ValidationError("core-only patch count mismatch")
    core_hash, core_lineage = canonical_lineage_hash(core, profile)
    if not core_lineage:
        raise ValidationError(f"core-only output left known lineage: {core_hash}")
    completed, completion_applied, completion_already = apply_patch(core, profile)
    if completion_applied != 67 or completion_already != 66 or completed != full:
        raise ValidationError("partial patch did not complete to canonical output")

    adjusted = bytearray(decrypted)
    table = profile["scriptSizeTable"]
    id_one_offset = table["offset"] + 4
    original_id_one = read_u16(adjusted, id_one_offset)
    write_u16(adjusted, id_one_offset, original_id_one + 2)
    running = 0
    for script_id in range(table["firstId"], table["lastIdInclusive"] + 1):
        offset = table["offset"] + (script_id - table["firstId"]) * 4
        running += read_u16(adjusted, offset)
        write_u16(adjusted, offset + 2, running)
    adjusted_bytes = bytes(adjusted)
    adjusted_hash, adjusted_lineage = canonical_lineage_hash(adjusted_bytes, profile)
    if not adjusted_lineage:
        raise ValidationError(f"valid adjusted script table left known lineage: {adjusted_hash}")
    adjusted_patched, adjusted_applied, adjusted_already = apply_patch(adjusted_bytes, profile)
    if adjusted_applied != len(profile["patches"]) or adjusted_already:
        raise ValidationError("adjusted-table full patch count mismatch")
    table_length = len(bytes.fromhex(table["canonicalBytesHex"]))
    if adjusted_patched[table["offset"]:table["offset"] + table_length] != adjusted_bytes[
        table["offset"]:table["offset"] + table_length
    ]:
        raise ValidationError("VWF patch modified the adjusted script-size table")
    adjusted_patched_hash, adjusted_patched_lineage = canonical_lineage_hash(adjusted_patched, profile)
    if not adjusted_patched_lineage:
        raise ValidationError(f"combined adjusted/VWF output left known lineage: {adjusted_patched_hash}")
    if sha256(adjusted_patched) == profile["fullOutput"]["sha256"]:
        raise ValidationError("adjusted-table output unexpectedly has canonical full-patch hash")

    large = bytearray(decrypted)
    large_id_one_blocks = 1200
    write_u16(large, id_one_offset, large_id_one_blocks)
    running = 0
    for script_id in range(table["firstId"], table["lastIdInclusive"] + 1):
        offset = table["offset"] + (script_id - table["firstId"]) * 4
        running += read_u16(large, offset)
        write_u16(large, offset + 2, running)
    large_required_heap = required_heap_capacity(bytes(large), profile)
    if large_required_heap <= profile["scriptHeap"]["originalCapacityBytes"]:
        raise ValidationError("large-script fixture did not exceed the original heap")
    missing_heap_hash, missing_heap_lineage = canonical_lineage_hash(bytes(large), profile)
    if missing_heap_lineage:
        raise ValidationError("large script table without a matching heap was accepted")
    write_heap_capacity(large, profile, large_required_heap)
    large_bytes = bytes(large)
    large_heap, large_required = inspect_heap(large_bytes, profile)
    if large_heap != large_required or large_heap != large_required_heap:
        raise ValidationError("large-script heap encoding mismatch")
    large_hash, large_lineage = canonical_lineage_hash(large_bytes, profile)
    if not large_lineage:
        raise ValidationError(f"large table + heap left known lineage: {large_hash}")
    large_patched, large_applied, large_already = apply_patch(large_bytes, profile)
    if large_applied != len(profile["patches"]) or large_already:
        raise ValidationError("large-table VWF patch count mismatch")
    large_patched_hash, large_patched_lineage = canonical_lineage_hash(large_patched, profile)
    if not large_patched_lineage:
        raise ValidationError(f"large table + heap + VWF left known lineage: {large_patched_hash}")
    if inspect_heap(large_patched, profile)[0] != large_required_heap:
        raise ValidationError("VWF patch changed the expanded script heap")

    first = profile["patches"][0]
    word_tamper = bytearray(decrypted)
    write_u32(word_tamper, first["offset"], 0xDEADBEEF)
    word_tamper_error = None
    try:
        apply_patch(bytes(word_tamper), profile)
    except ValidationError as exc:
        word_tamper_error = str(exc)
    if word_tamper_error is None:
        raise ValidationError("tampered patch word was accepted")

    unrelated_tamper = bytearray(decrypted)
    unrelated_tamper[0x500] ^= 1
    unrelated_hash, unrelated_lineage = canonical_lineage_hash(bytes(unrelated_tamper), profile)
    if unrelated_lineage:
        raise ValidationError("unrelated tamper was accepted as known lineage")

    report = {
        "schema": REPORT_SCHEMA,
        "profileId": profile["profileId"],
        "customerArchive": args.archive.name + " (customer-supplied; not packaged)",
        "customerEncryptedSha256": sha256(encrypted),
        "decryptedInput": {
            "size": len(decrypted),
            "sha256": sha256(decrypted),
            "crypto": crypto,
        },
        "profile": {
            "path": portable_path(args.profile),
            "sha256": sha256(args.profile.read_bytes()),
            "csharpSource": portable_path(args.csharp_source),
            "csharpSourceSha256": sha256(args.csharp_source.read_bytes()),
            "csharpProfileMatches": True,
            "patchCount": len(profile["patches"]),
            "groupCounts": {
                item["id"]: item["patchCount"] for item in profile["groups"]
            },
        },
        "fullPatch": {
            "size": len(full),
            "sha256": sha256(full),
            "appliedWords": full_applied,
            "alreadyAppliedWords": full_already,
            "changedByteCount": len(actual_changed),
            "unexpectedChangedBytes": len(unexpected_changed),
            "idempotent": True,
        },
        "partialPatch": {
            "coreOnlySha256": sha256(core),
            "coreAppliedWords": core_applied,
            "canonicalLineage": core_lineage,
            "completionAppliedWords": completion_applied,
            "completionAlreadyAppliedWords": completion_already,
            "completionMatchesFullPatch": completed == full,
        },
        "adjustedScriptTable": {
            "inputSha256": sha256(adjusted_bytes),
            "outputSha256": sha256(adjusted_patched),
            "tableConsistent": script_table_consistent(adjusted_bytes, profile),
            "tablePreserved": True,
            "canonicalLineageBefore": adjusted_lineage,
            "canonicalLineageAfter": adjusted_patched_lineage,
        },
        "expandedScriptHeap": {
            "originalCapacityBytes": original_heap,
            "originalRequiredBytes": original_required_heap,
            "largeScriptBlocks2K": large_id_one_blocks,
            "requiredCapacityBytes": large_required_heap,
            "inputSha256": sha256(large_bytes),
            "outputSha256": sha256(large_patched),
            "canonicalLineageBefore": large_lineage,
            "canonicalLineageAfter": large_patched_lineage,
            "missingHeapRejected": not missing_heap_lineage,
            "missingHeapCanonicalSha256": missing_heap_hash,
        },
        "negativeTests": {
            "patchWordTamperRejected": True,
            "patchWordTamperError": word_tamper_error,
            "unrelatedTamperRejectedByLineage": not unrelated_lineage,
            "unrelatedTamperCanonicalSha256": unrelated_hash,
        },
        "passed": True,
    }

    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(f"validated profile: {profile['profileId']}")
    print(f"patch words: {len(profile['patches'])}")
    print(f"full output SHA-256: {sha256(full)}")
    print("idempotence: passed")
    print("partial completion: passed")
    print("adjusted script table: passed")
    print(f"expanded script heap: passed ({large_required_heap} bytes)")
    print("tamper rejection: passed")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
