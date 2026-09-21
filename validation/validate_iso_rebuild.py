#!/usr/bin/env python3
"""Independent verification for the deterministic ISO replacement fixture."""
from __future__ import annotations

import argparse
import hashlib
import json
import struct
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
FIX = ROOT / 'tests/LuckyStarPspToolkit.Formats.SelfTests/Fixtures'
SECTOR = 2048

sys.path.insert(0, str(ROOT / 'validation'))
from validate_iso_fixture import entry_bytes, parse_iso  # noqa: E402


def sha256(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def main(output: Path) -> int:
    oracle = json.loads((FIX / 'oracle.json').read_text(encoding='utf-8'))
    manifest = json.loads((FIX / 'iso-patch-manifest.json').read_text(encoding='utf-8'))
    source = (FIX / 'reference.iso').read_bytes()
    patched = (FIX / 'reference-patched.iso').read_bytes()

    assert manifest['schema'] == 'lucky-star-psp.iso-patch-manifest.v1'
    assert manifest['expectedSourceSha256'] == sha256(source) == oracle['isoSha256']
    assert len(patched) == oracle['patchedIsoLength']
    assert sha256(patched) == oracle['patchedIsoSha256']
    assert len(patched) % SECTOR == 0
    assert struct.unpack_from('<I', patched, 16 * SECTOR + 80)[0] == oracle['patchedIsoVolumeBlocks']
    assert struct.unpack_from('>I', patched, 16 * SECTOR + 84)[0] == oracle['patchedIsoVolumeBlocks']

    source_volume, source_entries = parse_iso(source)
    patched_volume, patched_entries = parse_iso(patched)
    assert source_volume == patched_volume == oracle['isoVolumeIdentifier']
    source_lower = {path.lower(): entry for path, entry in source_entries.items()}
    patched_lower = {path.lower(): entry for path, entry in patched_entries.items()}
    assert set(source_lower) == set(patched_lower)

    replaced = oracle['patchedIsoReplacements']
    replaced_lower = {path.lower(): value for path, value in replaced.items()}
    assert len(manifest['replacements']) == len(replaced)
    for item in manifest['replacements']:
        payload = (FIX / item['sourceFile']).read_bytes()
        expected = replaced[item['isoPath']]
        assert item['expectedReplacementSize'] == len(payload) == expected['size']
        assert item['expectedReplacementSha256'] == sha256(payload) == expected['sha256']

    for path, old_entry in source_entries.items():
        key = path.lower()
        new_entry = patched_lower[key]
        if key in replaced_lower:
            expected = replaced_lower[key]
            payload = entry_bytes(patched, new_entry)
            assert new_entry.size == expected['size']
            assert new_entry.extents == [(expected['logicalBlock'], expected['size'])]
            assert sha256(payload) == expected['sha256']
        else:
            assert old_entry.directory == new_entry.directory
            assert old_entry.size == new_entry.size
            assert old_entry.extents == new_entry.extents
            if not old_entry.directory:
                assert entry_bytes(source, old_entry) == entry_bytes(patched, new_entry)

    # Only the PVD block count and the two target directory records may differ
    # inside the original image prefix.
    allowed = set(range(16 * SECTOR + 80, 16 * SECTOR + 88))
    for expected in replaced.values():
        record = expected['directoryRecordOffset']
        allowed.update(range(record + 2, record + 18))
    changed = [index for index, (left, right) in enumerate(zip(source, patched[:len(source)])) if left != right]
    assert changed
    assert set(changed).issubset(allowed)

    # Appended payload layout must be deterministic: canonical ISO path order,
    # one sector-aligned extent per replacement, zero-filled padding.
    cursor = len(source)
    if cursor % SECTOR:
        cursor += SECTOR - cursor % SECTOR
    for item in sorted(manifest['replacements'], key=lambda row: row['isoPath'].lower()):
        payload = (FIX / item['sourceFile']).read_bytes()
        expected = replaced[item['isoPath']]
        assert cursor // SECTOR == expected['logicalBlock']
        assert patched[cursor:cursor + len(payload)] == payload
        cursor += len(payload)
        aligned = (cursor + SECTOR - 1) // SECTOR * SECTOR
        assert patched[cursor:aligned] == bytes(aligned - cursor)
        cursor = aligned
    assert cursor == len(patched)

    result = {
        'schema': 'lucky-star-psp.iso-rebuild-fixture-validation.v1',
        'passed': True,
        'sourceSha256': sha256(source),
        'outputSha256': sha256(patched),
        'sourceBytes': len(source),
        'outputBytes': len(patched),
        'volumeBlocks': oracle['patchedIsoVolumeBlocks'],
        'replacementCount': len(replaced),
        'changedPrefixByteCount': len(changed),
        'unchangedEntryCount': len(source_entries) - len(replaced),
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
        default=ROOT / 'validation/iso-rebuild-fixture-validation.json')
    return parser.parse_args()


if __name__ == '__main__':
    try:
        args = parse_args()
        raise SystemExit(main(args.output))
    except Exception as exc:
        print(f'ISO rebuild fixture validation failed: {type(exc).__name__}: {exc}', file=sys.stderr)
        raise
