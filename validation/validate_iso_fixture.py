#!/usr/bin/env python3
from __future__ import annotations

import argparse
import hashlib
import json
import struct
import sys
from dataclasses import dataclass, field
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
FIX = ROOT / 'tests/LuckyStarPspToolkit.Formats.SelfTests/Fixtures'
SECTOR = 2048


@dataclass
class Entry:
    path: str
    directory: bool
    size: int = 0
    extents: list[tuple[int, int]] = field(default_factory=list)


def both16(data: bytes, offset: int) -> int:
    little = struct.unpack_from('<H', data, offset)[0]
    big = struct.unpack_from('>H', data, offset + 2)[0]
    assert little == big
    return little


def both32(data: bytes, offset: int) -> int:
    little = struct.unpack_from('<I', data, offset)[0]
    big = struct.unpack_from('>I', data, offset + 4)[0]
    assert little == big
    return little


def parse_record(record: bytes):
    assert len(record) >= 34 and record[0] == len(record)
    lba = both32(record, 2) + record[1]
    length = both32(record, 10)
    flags = record[25]
    assert record[26] == 0 and record[27] == 0
    assert both16(record, 28) == 1
    name_len = record[32]
    name_raw = record[33:33 + name_len]
    if name_len == 1 and name_raw[0] in (0, 1):
        return lba, length, flags, name_raw[0]
    name = name_raw.decode('ascii')
    if ';' in name and name.rsplit(';', 1)[1].isdigit():
        name = name.rsplit(';', 1)[0]
    if name.endswith('.'):
        name = name[:-1]
    return lba, length, flags, name


def parse_iso(data: bytes):
    pvd = data[16 * SECTOR:17 * SECTOR]
    term = data[17 * SECTOR:18 * SECTOR]
    assert pvd[0] == 1 and pvd[1:6] == b'CD001' and pvd[6] == 1
    assert term[0] == 255 and term[1:6] == b'CD001' and term[6] == 1
    blocks = both32(pvd, 80)
    block_size = both16(pvd, 128)
    assert block_size == SECTOR
    assert both16(pvd, 120) == 1
    assert both16(pvd, 124) == 1
    assert pvd[881] == 1
    assert blocks * block_size <= len(data)
    volume = pvd[40:72].decode('ascii').rstrip(' \0')
    root_len = pvd[156]
    root_lba, root_size, root_flags, root_name = parse_record(pvd[156:156 + root_len])
    assert root_name == 0 and root_flags & 2

    entries: dict[str, Entry] = {}
    visited: set[tuple[int, int]] = set()

    def walk(path: str, lba: int, size: int):
        key = (lba, size)
        assert key not in visited
        visited.add(key)
        directory = data[lba * block_size:lba * block_size + size]
        pos = 0
        pending: Entry | None = None
        child_dirs: list[Entry] = []
        while pos < len(directory):
            length = directory[pos]
            if length == 0:
                pos = ((pos // block_size) + 1) * block_size
                continue
            assert length >= 34 and length <= block_size - (pos % block_size)
            lba2, length2, flags2, name = parse_record(directory[pos:pos + length])
            assert lba2 * block_size + length2 <= blocks * block_size
            assert lba2 * block_size + length2 <= len(data)
            pos += length
            if name in (0, 1):
                assert pending is None
                continue
            child_path = f'{path}/{name}' if path else name
            more = bool(flags2 & 0x80)
            is_dir = bool(flags2 & 0x02)
            if pending is not None:
                assert pending.path.lower() == child_path.lower()
                assert pending.directory == is_dir
                pending.extents.append((lba2, length2))
                pending.size += length2
                if not more:
                    assert child_path.lower() not in (key.lower() for key in entries)
                    entries[child_path] = pending
                    if pending.directory:
                        child_dirs.append(pending)
                    pending = None
                continue
            current = Entry(child_path, is_dir, length2, [(lba2, length2)])
            if more:
                pending = current
            else:
                assert child_path.lower() not in (key.lower() for key in entries)
                entries[child_path] = current
                if current.directory:
                    child_dirs.append(current)
        assert pending is None
        for child in child_dirs:
            assert len(child.extents) == 1
            walk(child.path, child.extents[0][0], child.size)

    walk('', root_lba, root_size)
    return volume, entries


def entry_bytes(data: bytes, entry: Entry) -> bytes:
    out = bytearray()
    for lba, length in entry.extents:
        start = lba * SECTOR
        out += data[start:start + length]
    assert len(out) == entry.size
    return bytes(out)


def expect_rejected(data: bytes, mutation) -> None:
    damaged = bytearray(data)
    mutation(damaged)
    try:
        parse_iso(bytes(damaged))
    except (AssertionError, UnicodeDecodeError, ValueError, struct.error):
        return
    raise AssertionError('damaged ISO fixture was unexpectedly accepted')


def main(output: Path) -> int:
    oracle = json.loads((FIX / 'oracle.json').read_text(encoding='utf-8'))
    data = (FIX / 'reference.iso').read_bytes()
    assert len(data) == oracle['isoLength']
    assert hashlib.sha256(data).hexdigest() == oracle['isoSha256']
    volume, entries = parse_iso(data)
    assert volume == oracle['isoVolumeIdentifier']
    by_lower = {path.lower(): entry for path, entry in entries.items()}
    for path, expected in oracle['isoFiles'].items():
        assert path.lower() in by_lower, path
        entry = by_lower[path.lower()]
        assert not entry.directory
        payload = entry_bytes(data, entry)
        assert len(payload) == expected['size']
        assert hashlib.sha256(payload).hexdigest() == expected['sha256']
    multi = by_lower['psp_game/usrdir/data/multi.bin']
    assert len(multi.extents) == 2
    required = {
        'PSP_GAME/PARAM.SFO',
        'PSP_GAME/SYSDIR/EBOOT.BIN',
        'PSP_GAME/USRDIR/DATA/sc.cpk',
        'PSP_GAME/USRDIR/DATA/lt.bin',
    }
    assert {path.lower() for path in required}.issubset(by_lower)

    pvd_offset = 16 * SECTOR
    expect_rejected(data, lambda damaged: damaged.__setitem__(pvd_offset + 84, damaged[pvd_offset + 84] ^ 1))
    expect_rejected(data, lambda damaged: damaged.__setitem__(17 * SECTOR, 1))

    def make_multivolume(damaged: bytearray) -> None:
        struct.pack_into('<H', damaged, pvd_offset + 120, 2)
        struct.pack_into('>H', damaged, pvd_offset + 122, 2)
    expect_rejected(data, make_multivolume)

    def truncate_volume(damaged: bytearray) -> None:
        blocks_too_large = len(damaged) // SECTOR + 1
        struct.pack_into('<I', damaged, pvd_offset + 80, blocks_too_large)
        struct.pack_into('>I', damaged, pvd_offset + 84, blocks_too_large)
    expect_rejected(data, truncate_volume)

    def duplicate_pvd(damaged: bytearray) -> None:
        damaged[17 * SECTOR:18 * SECTOR] = damaged[16 * SECTOR:17 * SECTOR]
    expect_rejected(data, duplicate_pvd)

    def wrong_block_size(damaged: bytearray) -> None:
        struct.pack_into('<H', damaged, pvd_offset + 128, 1024)
        struct.pack_into('>H', damaged, pvd_offset + 130, 1024)
    expect_rejected(data, wrong_block_size)

    def interleaved_file(damaged: bytearray) -> None:
        name = damaged.find(b'SC.CPK;1')
        assert name >= 33
        damaged[name - 33 + 26] = 1
    expect_rejected(data, interleaved_file)

    def unfinished_multi_extent(damaged: bytearray) -> None:
        first = damaged.find(b'MULTI.BIN;1')
        second = damaged.find(b'MULTI.BIN;1', first + 1)
        assert first >= 33 and second >= 33
        damaged[second - 33 + 25] |= 0x80
    expect_rejected(data, unfinished_multi_extent)

    def duplicate_path(damaged: bytearray) -> None:
        name = damaged.find(b'PR.BIN;1')
        assert name >= 0
        damaged[name:name + 8] = b'LT.BIN;1'
    expect_rejected(data, duplicate_path)

    def directory_cycle(damaged: bytearray) -> None:
        extent_offset = 23 * SECTOR + 68 + 2
        struct.pack_into('<I', damaged, extent_offset, 23)
        struct.pack_into('>I', damaged, extent_offset + 4, 23)
    expect_rejected(data, directory_cycle)

    selected_bytes = sum(by_lower[path.lower()].size for path in required)
    result = {
        'schema': 'lucky-star-psp.iso-fixture-validation.v1',
        'passed': True,
        'isoSha256': oracle['isoSha256'],
        'volumeIdentifier': volume,
        'entryCount': len(entries),
        'fileCount': sum(not entry.directory for entry in entries.values()),
        'directoryCount': sum(entry.directory for entry in entries.values()),
        'multiExtentSha256': oracle['isoFiles']['PSP_GAME/USRDIR/DATA/MULTI.BIN']['sha256'],
        'requiredAssetCount': len(required),
        'requiredAssetBytes': selected_bytes,
        'negativeCases': 10,
    }
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(result, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    print(json.dumps(result, ensure_ascii=False))
    return 0


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument(
        '--output',
        type=Path,
        default=ROOT / 'validation/iso-fixture-validation.json')
    return parser.parse_args()


if __name__ == '__main__':
    try:
        args = parse_args()
        raise SystemExit(main(args.output))
    except Exception as exc:
        print(f'ISO fixture validation failed: {type(exc).__name__}: {exc}', file=sys.stderr)
        raise
