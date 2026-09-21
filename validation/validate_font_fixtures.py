#!/usr/bin/env python3
from __future__ import annotations

import argparse
import binascii
import hashlib
import json
import struct
import zlib
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
FIXTURES = ROOT / "tests/LuckyStarPspToolkit.Formats.SelfTests/Fixtures"
RUSSIAN = "АБВГДЕЁЖЗИЙКЛМНОПРСТУФХЦЧШЩЪЫЬЭЮЯабвгдеёжзийклмнопрстуфхцчшщъыьэюя"


def sha256(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def verify_rgo_checksum(data: bytes) -> bool:
    if len(data) < 16 or len(data) % 16:
        return False
    low = high = 0x1111111111111111
    for offset in range(0, len(data) - 16, 16):
        low = (low + int.from_bytes(data[offset:offset + 8], "little")) & 0xFFFFFFFFFFFFFFFF
        high = (high + int.from_bytes(data[offset + 8:offset + 16], "little")) & 0xFFFFFFFFFFFFFFFF
    return data[-16:-8] == low.to_bytes(8, "little") and data[-8:] == high.to_bytes(8, "little")


def parse_glyph(data: bytes, index: int) -> tuple[list[int], int, int, list[int]]:
    record = data[index * 92:(index + 1) * 92]
    if len(record) != 92:
        raise ValueError(f"glyph {index} is truncated")
    levels: list[int] = []
    for y in range(18):
        for x in range(18):
            levels.append((record[y * 5 + x // 4] >> ((x % 4) * 2)) & 3)
    unused = [record[y * 5 + 4] & 0xF0 for y in range(18)]
    return levels, record[90], record[91], unused


def render_atlas(data: bytes, count: int, columns: int = 16, scale: int = 2, gutter: int = 1) -> tuple[int, int, bytes]:
    rows = (count + columns - 1) // columns
    width = columns * (18 * scale + gutter) + gutter
    height = rows * (18 * scale + gutter) + gutter
    rgba = bytearray(width * height * 4)
    for pixel in range(width * height):
        rgba[pixel * 4 + 3] = 255
    for index in range(count):
        levels, _, _, _ = parse_glyph(data, index)
        origin_x = gutter + (index % columns) * (18 * scale + gutter)
        origin_y = gutter + (index // columns) * (18 * scale + gutter)
        for y in range(18):
            for x in range(18):
                intensity = levels[y * 18 + x] * 85
                for sy in range(scale):
                    for sx in range(scale):
                        tx = origin_x + x * scale + sx
                        ty = origin_y + y * scale + sy
                        offset = (ty * width + tx) * 4
                        rgba[offset:offset + 4] = bytes((intensity, intensity, intensity, 255))
    return width, height, bytes(rgba)


def png_chunk(kind: bytes, payload: bytes) -> bytes:
    return struct.pack(">I", len(payload)) + kind + payload + struct.pack(">I", binascii.crc32(kind + payload) & 0xFFFFFFFF)


def encode_png(width: int, height: int, rgba: bytes) -> bytes:
    stride = width * 4
    scanlines = b"".join(b"\0" + rgba[y * stride:(y + 1) * stride] for y in range(height))
    header = struct.pack(">IIBBBBB", width, height, 8, 6, 0, 0, 0)
    return b"\x89PNG\r\n\x1a\n" + png_chunk(b"IHDR", header) + png_chunk(b"IDAT", zlib.compress(scanlines, 9)) + png_chunk(b"IEND", b"")


def validate(preview_path: Path | None = None) -> dict[str, object]:
    oracle = json.loads((FIXTURES / "oracle.json").read_text(encoding="utf-8"))
    source = (FIXTURES / "reference-lt.bin").read_bytes()
    patched = (FIXTURES / "reference-lt-russian.bin").read_bytes()
    atlas_fixture = (FIXTURES / "reference-lt-russian-atlas.rgba").read_bytes()
    glyph_map = (FIXTURES / "glyph-map.txt").read_text(encoding="utf-8").splitlines()
    bdf = (FIXTURES / "reference-font.bdf").read_text(encoding="ascii")

    checks: list[str] = []
    assert sha256(source) == oracle["fontSha256"]
    assert sha256(patched) == oracle["patchedFontSha256"]
    assert verify_rgo_checksum(source) and verify_rgo_checksum(patched)
    assert len(source) == len(patched) == oracle["fontLength"]
    assert len(glyph_map) == oracle["fontGlyphCount"]
    checks.append("font hashes, sizes, and RGO checksums")

    assert set(RUSSIAN).issubset(set(glyph_map))
    bdf_encodings = {int(line.split()[1]) for line in bdf.splitlines() if line.startswith("ENCODING ") and int(line.split()[1]) >= 0}
    assert bdf.count("STARTCHAR ") == oracle["bdfGlyphCount"]
    assert {ord(ch) for ch in RUSSIAN} == bdf_encodings
    checks.append("glyph map and BDF cover the complete Russian alphabet")

    for character in RUSSIAN:
        index = glyph_map.index(character)
        source_levels, source_width, source_reserved, source_unused = parse_glyph(source, index)
        patched_levels, patched_width, patched_reserved, patched_unused = parse_glyph(patched, index)
        assert not any(source_levels)
        assert any(patched_levels)
        assert source_width == patched_width == 9
        assert patched_reserved == source_reserved
        assert source_unused == patched_unused
    assert parse_glyph(source, glyph_map.index("А"))[2] == 0x5A
    assert parse_glyph(patched, glyph_map.index("А"))[2] == 0x5A
    assert parse_glyph(source, glyph_map.index("А"))[3][1] == 0x50
    checks.append("all 66 Russian glyphs change from blank to renderable without losing metadata or unused row bits")

    # Non-target data must remain stable: Latin bitmaps, reserved byte, row padding, and file padding.
    for index in (0, 1, 2, 7, 8):
        assert source[index * 92:(index + 1) * 92] == patched[index * 92:(index + 1) * 92]
    records_end = len(glyph_map) * 92
    assert source[records_end:-16] == patched[records_end:-16]
    assert parse_glyph(source, 1)[3][0] == 0xA0
    assert parse_glyph(source, 2)[2] == 0x7B
    assert source[records_end] == 0xCC
    checks.append("non-target glyph records and non-zero padding are preserved")

    width, height, rgba = render_atlas(patched, len(glyph_map))
    assert width == oracle["atlasWidth"] and height == oracle["atlasHeight"]
    assert rgba == atlas_fixture
    assert sha256(rgba) == oracle["atlasRgbaSha256"]
    preview = encode_png(width, height, rgba)
    if preview_path is not None:
        preview_path.parent.mkdir(parents=True, exist_ok=True)
        preview_path.write_bytes(preview)
    checks.append("independent RGBA atlas and PNG encoding verified")

    return {
        "schema": "lucky-star-psp.font-fixture-validation.v1",
        "passed": True,
        "checks": checks,
        "fontGlyphCount": len(glyph_map),
        "russianGlyphCount": len(RUSSIAN),
        "sourceFontSha256": sha256(source),
        "patchedFontSha256": sha256(patched),
        "atlas": {
            "width": width,
            "height": height,
            "rgbaSha256": sha256(rgba),
            "pngSha256": sha256(preview),
            "pngPath": (
                str(preview_path.resolve().relative_to(ROOT.resolve()))
                if preview_path is not None and preview_path.resolve().is_relative_to(ROOT.resolve())
                else (str(preview_path.resolve()) if preview_path is not None else None)
            ),
        },
    }


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--output", type=Path, default=ROOT / "validation/font-fixture-validation.json")
    parser.add_argument("--preview", type=Path, help="optional path for the generated PNG preview")
    args = parser.parse_args()
    try:
        report = validate(args.preview)
    except Exception as exc:
        report = {
            "schema": "lucky-star-psp.font-fixture-validation.v1",
            "passed": False,
            "error": f"{type(exc).__name__}: {exc}",
        }
        args.output.write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
        print(report["error"])
        return 1
    args.output.write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    for check in report["checks"]:
        print(f"PASS {check}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
