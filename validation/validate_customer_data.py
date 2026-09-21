#!/usr/bin/env python3
"""Independent validation oracle for the supplied customer archive.

This script intentionally does not execute or import the C# project. It verifies
its binary constants and expected outputs using Python's cryptography package,
then compares the decrypted ELF with the independently built C++ 0.1.0 oracle
when that file is supplied.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import struct
import zipfile
from dataclasses import dataclass, asdict
from pathlib import Path
from typing import Any

from cryptography.hazmat.primitives import cmac
from cryptography.hazmat.primitives.ciphers import Cipher, algorithms, modes

TAG_KEY = bytes.fromhex("ebff40d8b41ae166913b8f64b6fcb712")
KIRK7_KEY = bytes.fromhex("115a5d20d53a8dd39cc5af410f0f186f")
KIRK1_KEY = bytes.fromhex("98c940975c1d10e87fe60ea3fd03a8ba")
SUPPORTED_TAG = 0xD91613F0
KNOWN_ENCRYPTED = "4a22c50c0a6ad6249dd1d9ab015d559b19b4acd79daf892a6f29a58648b834f0"
KNOWN_DECRYPTED = "3e6c1c2f7136a69cecda4f39835c3454b739e23e89e862fe17462cdf915581d8"
PATCH_POINTS = {
    0x003D24: 0x24840010,
    0x004D98: 0x24A5007D,
    0x007868: 0x34040012,
    0x00786C: 0x00932023,
    0x007940: 0x10800014,
    0x007A9C: 0x2508FFFE,
    0x007B4C: 0x2665FFFE,
    0x007C38: 0x2508FFFE,
    0x025CD8: 0x00A04825,
    0x025DE0: 0x3128FFFF,
    0x0362F4: 0x00A03025,
    0x0366E4: 0x26240010,
    0x03A648: 0x00E04025,
    0x03A93C: 0x27BDFFD0,
    0x03AC84: 0x11400012,
}


def aes_cbc_decrypt(data: bytes, key: bytes) -> bytes:
    if len(data) % 16:
        raise ValueError("AES-CBC data must be block aligned")
    decryptor = Cipher(algorithms.AES(key), modes.CBC(bytes(16))).decryptor()
    return decryptor.update(data) + decryptor.finalize()


def aes_cmac(data: bytes, key: bytes) -> bytes:
    mac = cmac.CMAC(algorithms.AES(key))
    mac.update(data)
    return mac.finalize()


def u16(data: bytes, offset: int) -> int:
    return struct.unpack_from("<H", data, offset)[0]


def u32(data: bytes, offset: int) -> int:
    return struct.unpack_from("<I", data, offset)[0]


def i32(data: bytes, offset: int) -> int:
    return struct.unpack_from("<i", data, offset)[0]


def sha256(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def expand_seed() -> bytes:
    encoded = bytearray(0x90)
    for offset in range(0, 0x90, 0x10):
        encoded[offset:offset + 0x10] = TAG_KEY
        encoded[offset] = offset // 0x10
    return aes_cbc_decrypt(bytes(encoded), KIRK7_KEY)


@dataclass
class PspHeader:
    module_name: str
    elf_size: int
    psp_size: int
    entry: int
    module_info_offset: int
    bss_size: int
    devkit_version: str
    decrypt_mode: str
    compressed_size: int
    tag: str
    oe_tag: str


def parse_header(data: bytes) -> PspHeader:
    if len(data) < 0x150 or data[:4] != b"~PSP":
        raise ValueError("not a PSP PRX")
    module = data[0x0A:0x26].split(b"\0", 1)[0].decode("ascii")
    return PspHeader(
        module_name=module,
        elf_size=u32(data, 0x28),
        psp_size=u32(data, 0x2C),
        entry=u32(data, 0x30),
        module_info_offset=u32(data, 0x34),
        bss_size=i32(data, 0x38),
        devkit_version=f"0x{u32(data, 0x78):08X}",
        decrypt_mode=f"0x{u32(data, 0x7C):08X}",
        compressed_size=i32(data, 0xB0),
        tag=f"0x{u32(data, 0xD0):08X}",
        oe_tag=f"0x{u32(data, 0x130):08X}",
    )


def decrypt_prx(data: bytes) -> tuple[bytes, dict[str, Any]]:
    header = parse_header(data)
    if u32(data, 0xD0) != SUPPORTED_TAG:
        raise ValueError(f"unsupported tag {header.tag}")
    if header.psp_size != len(data):
        raise ValueError("PSP size mismatch")
    if any(data[0xD4:0x12C]):
        raise ValueError("reserved Type-2 area is not zero")

    expanded = expand_seed()
    tail = bytearray(
        data[0x140:0x150]
        + data[0x12C:0x140]
        + data[0x080:0x0B0]
        + data[0x0C0:0x0D0]
    )
    tail[:0x60] = aes_cbc_decrypt(bytes(tail[:0x60]), KIRK7_KEY)
    identifier = bytes(tail[0:0x10])
    expected_sha1 = bytes(tail[0x10:0x24])
    encrypted_kirk_header = bytes(tail[0x24:0x64])
    metadata = data[0xB0:0xC0]
    prx_header = data[:0x80]
    sha_material = (
        data[0xD0:0xD4]
        + expanded[:0x10]
        + bytes(0x58)
        + identifier
        + encrypted_kirk_header
        + metadata
        + prx_header
    )
    actual_sha1 = hashlib.sha1(sha_material).digest()
    if actual_sha1 != expected_sha1:
        raise ValueError("Type-2 SHA-1 authentication failed")

    stage = bytes(a ^ b for a, b in zip(encrypted_kirk_header, expanded[0x10:0x50]))
    stage = aes_cbc_decrypt(stage, KIRK7_KEY)
    stage = bytes(a ^ b for a, b in zip(stage, expanded[0x50:0x90]))
    command = bytearray(0x90)
    command[:0x40] = stage
    command[0x70:0x80] = metadata
    struct.pack_into("<I", command, 0x60, 1)

    key_pair = aes_cbc_decrypt(bytes(command[:0x20]), KIRK1_KEY)
    payload_key, cmac_key = key_pair[:16], key_pair[16:]
    data_size = u32(command, 0x70)
    data_offset = u32(command, 0x74)
    if data_size != header.elf_size or data_offset != 0x80:
        raise ValueError("authenticated KIRK size/offset mismatch")

    encrypted_size = (data_size + 15) & ~15
    encrypted_offset = 0x40 + 0x90 + data_offset
    encrypted_payload = data[encrypted_offset:encrypted_offset + encrypted_size]
    if len(encrypted_payload) != encrypted_size:
        raise ValueError("truncated encrypted payload")

    header_cmac = aes_cmac(bytes(command[0x60:0x90]), cmac_key)
    if header_cmac != bytes(command[0x20:0x30]):
        raise ValueError("KIRK header CMAC failed")
    data_cmac = aes_cmac(bytes(command[0x60:0x90]) + prx_header + encrypted_payload, cmac_key)
    if data_cmac != bytes(command[0x30:0x40]):
        raise ValueError("KIRK payload CMAC failed")

    plaintext = aes_cbc_decrypt(encrypted_payload, payload_key)[:data_size]
    if not plaintext.startswith(b"\x7fELF"):
        raise ValueError("decryption result is not ELF")
    details = {
        "type2_sha1": "passed",
        "kirk_header_cmac": "passed",
        "kirk_payload_cmac": "passed",
        "encrypted_payload_offset": encrypted_offset,
        "encrypted_payload_size": encrypted_size,
    }
    return plaintext, details


def parse_sfo(data: bytes) -> dict[str, Any]:
    if len(data) < 20 or u32(data, 0) != 0x46535000:
        raise ValueError("not SFO")
    key_off, data_off, count = u32(data, 8), u32(data, 12), u32(data, 16)
    values: dict[str, Any] = {}
    for idx in range(count):
        pos = 20 + idx * 16
        key_rel, fmt = u16(data, pos), u16(data, pos + 2)
        length, max_len, value_rel = u32(data, pos + 4), u32(data, pos + 8), u32(data, pos + 12)
        if length > max_len:
            raise ValueError("SFO length exceeds maximum")
        key_pos = key_off + key_rel
        key_end = data.index(0, key_pos, data_off)
        key = data[key_pos:key_end].decode("ascii")
        raw = data[data_off + value_rel:data_off + value_rel + length]
        if fmt == 0x0204:
            values[key] = raw.split(b"\0", 1)[0].decode("utf-8")
        elif fmt == 0x0404:
            values[key] = u32(raw, 0)
        else:
            values[key] = raw.hex()
    return values


def classify(data: bytes) -> str:
    if data and not any(data):
        return "zero-filled"
    if data.startswith(b"\x00PSF"):
        return "param-sfo"
    if data.startswith(b"~PSP"):
        return "psp-prx"
    if data.startswith(b"\x7fELF"):
        return "elf"
    if data.startswith(b"PSAR"):
        return "psar"
    if data.startswith(b"PSMF"):
        return "psmf"
    if data.startswith(b"CPK "):
        return "cri-cpk"
    return "unknown"


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("archive", type=Path)
    parser.add_argument("--cpp-elf", type=Path)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--decrypted-output", type=Path)
    args = parser.parse_args()

    files: list[dict[str, Any]] = []
    content: dict[str, bytes] = {}
    with zipfile.ZipFile(args.archive) as archive:
        for info in sorted(archive.infolist(), key=lambda x: x.filename):
            if info.is_dir():
                continue
            data = archive.read(info)
            content[info.filename] = data
            item: dict[str, Any] = {
                "path": info.filename,
                "size": len(data),
                "compressed_size": info.compress_size,
                "sha256": sha256(data),
                "kind": classify(data),
            }
            if item["kind"] == "param-sfo":
                item["sfo"] = parse_sfo(data)
            if item["kind"] == "psp-prx":
                item["psp_header"] = asdict(parse_header(data))
            files.append(item)

    eboot = content["EBOOT.BIN"]
    plaintext, crypto = decrypt_prx(eboot)
    if args.decrypted_output:
        args.decrypted_output.parent.mkdir(parents=True, exist_ok=True)
        args.decrypted_output.write_bytes(plaintext)

    slots = []
    cumulative_expected = 0
    for script_id in range(11):
        off = 0x10319E + script_id * 4
        blocks, cumulative = u16(plaintext, off), u16(plaintext, off + 2)
        cumulative_expected += blocks
        slots.append({
            "id": script_id,
            "blocks_2k": blocks,
            "cumulative_blocks_2k": cumulative,
            "size_bytes": blocks * 2048,
            "cumulative_valid": cumulative == cumulative_expected,
        })

    points = []
    for offset, expected in PATCH_POINTS.items():
        actual = u32(plaintext, offset)
        points.append({
            "offset": f"0x{offset:08X}",
            "expected": f"0x{expected:08X}",
            "actual": f"0x{actual:08X}",
            "matches": actual == expected,
        })

    tampered = bytearray(eboot)
    tampered[0x200] ^= 1
    tamper_error = None
    try:
        decrypt_prx(bytes(tampered))
    except ValueError as exc:
        tamper_error = str(exc)
    if not tamper_error:
        raise AssertionError("tampered EBOOT was accepted")

    cpp_matches = None
    if args.cpp_elf:
        cpp_matches = args.cpp_elf.read_bytes() == plaintext

    game_sfo = next(x.get("sfo") for x in files if x["path"] == "PARAM-2.SFO")
    firmware_sfo = next(x.get("sfo") for x in files if x["path"] == "PARAM.SFO")
    report = {
        "schema": "lucky-star-psp.customer-validation.v2",
        "archive": args.archive.name + " (customer-supplied; not packaged)",
        "files": files,
        "customer_conclusions": {
            "game_disc_id": game_sfo.get("DISC_ID"),
            "game_title": game_sfo.get("TITLE"),
            "firmware_disc_id": firmware_sfo.get("DISC_ID"),
            "firmware_title": firmware_sfo.get("TITLE"),
            "zero_boot_detected": classify(content["BOOT.BIN"]) == "zero-filled",
            "sc_cpk_present": any(Path(name).name.lower() == "sc.cpk" for name in content),
            "lt_bin_present": any(Path(name).name.lower() == "lt.bin" for name in content),
        },
        "eboot": {
            "header": asdict(parse_header(eboot)),
            "encrypted_sha256": sha256(eboot),
            "encrypted_hash_matches_profile": sha256(eboot) == KNOWN_ENCRYPTED,
            "decrypted_size": len(plaintext),
            "decrypted_sha256": sha256(plaintext),
            "decrypted_hash_matches_profile": sha256(plaintext) == KNOWN_DECRYPTED,
            "crypto": crypto,
            "referenced_assets": [name for name in ["sc.cpk", "lt.bin", "union.cpk", "pr.bin"] if name.encode() in plaintext],
            "script_slots": slots,
            "script_table_consistent": all(slot["cumulative_valid"] for slot in slots),
            "patch_points": points,
            "all_patch_points_match": all(point["matches"] for point in points),
            "cpp_oracle_matches_byte_for_byte": cpp_matches,
            "tamper_rejected": True,
            "tamper_error": tamper_error,
        },
    }

    assert report["customer_conclusions"]["game_disc_id"] == "ULJM05752"
    assert report["eboot"]["encrypted_hash_matches_profile"]
    assert report["eboot"]["decrypted_hash_matches_profile"]
    assert report["eboot"]["script_table_consistent"]
    assert report["eboot"]["all_patch_points_match"]
    if cpp_matches is not None:
        assert cpp_matches

    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(f"validated: {args.archive.name}")
    print(f"encrypted SHA-256: {report['eboot']['encrypted_sha256']}")
    print(f"decrypted SHA-256: {report['eboot']['decrypted_sha256']}")
    print(f"C++ oracle match: {cpp_matches}")
    print(f"tamper rejected: {tamper_error}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
