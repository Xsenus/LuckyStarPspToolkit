"""Independent synthetic PNG/swizzle/gzip oracle; uses only Python's standard library.

No original game data or third-party implementation is included. PNG filtering
and chunking follow https://www.w3.org/TR/png-3/ .
"""
from pathlib import Path
import argparse
import base64
import binascii
import gzip
import json
import random
import struct
import zlib

ROOT = Path(__file__).resolve().parents[1]
OUTPUT = ROOT / 'tests/LuckyStarPspToolkit.Formats.SelfTests/Fixtures/image-codec-oracle.json'


def b64(data):
    """Serialize an independent byte vector without platform-specific encodings."""
    return base64.b64encode(data).decode('ascii')


def chunk(name, data):
    """Build a PNG chunk using the platform-independent library CRC."""
    return struct.pack('>I', len(data)) + name + data + struct.pack('>I', binascii.crc32(name + data))


def paeth(a, b, c):
    """Select the neighbor nearest the PNG linear predictor, with prescribed ties."""
    p = a + b - c
    return min(((abs(p-a), 0, a), (abs(p-b), 1, b), (abs(p-c), 2, c)))[2]


def png(width, height, depth, color, rows, bpp, filter_type=0, palette=None, transparency=None):
    """Encode known scanline bytes with one of all five PNG filters."""
    scanlines = bytearray()
    for y, row in enumerate(rows):
        scanlines.append(filter_type)
        for x, value in enumerate(row):
            a = row[x-bpp] if x >= bpp else 0
            b = rows[y-1][x] if y else 0
            c = rows[y-1][x-bpp] if y and x >= bpp else 0
            prediction = (0, a, b, (a+b)//2, paeth(a, b, c))[filter_type]
            scanlines.append((value - prediction) % 256)
    result = b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', struct.pack('>IIBBBBB', width, height, depth, color, 0, 0, 0))
    if palette is not None:
        result += chunk(b'PLTE', palette)
    if transparency is not None:
        result += chunk(b'tRNS', transparency)
    return result + chunk(b'IDAT', zlib.compress(scanlines, 9)) + chunk(b'IEND', b'')


def pack_samples(values, depth):
    """Pack indexed or grayscale samples MSB-first, including partial final bytes."""
    packed = bytearray((len(values) * depth + 7) // 8)
    for x, value in enumerate(values):
        packed[x*depth//8] |= value << (8-depth-x*depth%8)
    return bytes(packed)


def make_vectors():
    """Compute exact PNG pixels and texture bytes independently of the C# codecs."""
    cases = []
    width, height = 19, 9
    for color, channels in ((6, 4), (2, 3), (4, 2)):
        rows = [bytes((x*31+y*47+c*67) % 256 for x in range(width) for c in range(channels)) for y in range(height)]
        pixels = bytearray()
        for row in rows:
            for x in range(width):
                sample = row[x*channels:(x+1)*channels]
                pixels.extend(sample if color == 6 else sample+b'\xff' if color == 2 else bytes((sample[0], sample[0], sample[0], sample[1])))
        for filter_type in range(5):
            cases.append({'name': f'color-{color}-filter-{filter_type}', 'width': width, 'height': height,
                          'png': b64(png(width, height, 8, color, rows, channels, filter_type)), 'rgba': b64(pixels)})
    for depth in (1, 2, 4, 8):
        count = 1 << depth
        colors = [bytes(((index*17) % 256, (index*61) % 256, (index*101) % 256)) for index in range(count)]
        alpha = bytes(index*255//(count-1) for index in range(count))
        samples = [[(x*7+y*3) % count for x in range(width)] for y in range(height)]
        rows = [pack_samples(row, depth) for row in samples]
        rgba = bytes(value for row in samples for index in row for value in colors[index]+alpha[index:index+1])
        cases.append({'name': f'indexed-{depth}', 'width': width, 'height': height,
                      'png': b64(png(width, height, depth, 3, rows, 1, 4, b''.join(colors), alpha)), 'rgba': b64(rgba)})
        gray = bytes(value for row in samples for index in row for value in bytes((index*255//(count-1),)*3+(0 if index == 1 else 255,)))
        cases.append({'name': f'grayscale-{depth}', 'width': width, 'height': height,
                      'png': b64(png(width, height, depth, 0, rows, 1, 3, transparency=struct.pack('>H', 1))), 'rgba': b64(gray)})
    rows = [bytes((x*31+y*47+c*67) % 256 for x in range(width) for c in range(3)) for y in range(height)]
    key = rows[0][:3]
    rgba = bytes(value for row in rows for x in range(width) for value in row[x*3:x*3+3]+bytes((0 if row[x*3:x*3+3] == key else 255,)))
    cases.append({'name': 'rgb-transparency', 'width': width, 'height': height,
                  'png': b64(png(width, height, 8, 2, rows, 3, 2, transparency=b''.join(struct.pack('>H', x) for x in key))), 'rgba': b64(rgba)})
    textures = []
    for colors in (16, 256):
        w, h = 32, 16
        stride = w//2 if colors == 16 else w
        palette = bytearray(value for index in range(colors) for value in ((index*17)%256, (index*61)%256, (index*101)%256, 0 if index == 0 else 255))
        # Duplicate palette colors test no-op index preservation.
        palette[-4:] = palette[7*4:8*4]
        indices = [[(x*7+y*3) % colors for x in range(w)] for y in range(h)]
        changed = [row.copy() for row in indices]
        changed[9][17], changed[10][18] = 14, 3
        packed, edited = bytearray(stride*h), bytearray(stride*h)
        for y in range(h):
            for x in range(w):
                bx = x//2 if colors == 16 else x
                address = (y//8*(stride//16)+bx//16)*128+(y%8)*16+bx%16
                shift = (x%2)*4 if colors == 16 else 0
                packed[address] |= indices[y][x] << shift
                edited[address] |= changed[y][x] << shift
        for compressed in (False, True):
            capacity = 2048 if compressed else len(packed)
            block = bytearray([0xA5] * capacity)
            if compressed:
                struct.pack_into('<I', block, 0, 2)
                cursor = 32
                for frame in range(2):
                    struct.pack_into('<I', block, 4+frame*4, cursor)
                    part = packed[frame*len(packed)//2:(frame+1)*len(packed)//2]
                    encoded = gzip.compress(part, compresslevel=9, mtime=0)
                    struct.pack_into('<I', block, cursor, len(part))
                    block[cursor+4:cursor+16] = bytes(12)
                    block[cursor+16:cursor+16+len(encoded)] = encoded
                    cursor = (cursor+16+len(encoded)+15)//16*16
                struct.pack_into('<I', block, 12, cursor)
            else:
                block[:] = packed
            resource = bytes(block+palette+bytearray([0x5A]*37))
            expected_edit = bytes(edited+palette+bytearray([0x5A]*37)) if not compressed else None
            textures.append({'name': f'indexed-{colors}-gzip-{compressed}',
                             'layout': {'id': 0, 'pixelOffset': 0, 'capacity': capacity, 'paletteOffset': capacity, 'colorCount': colors, 'width': w, 'height': h, 'compressed': compressed},
                             'resource': b64(resource), 'rgba': b64(bytes(value for row in indices for index in row for value in palette[index*4:index*4+4])),
                             'editedRgba': b64(bytes(value for row in changed for index in row for value in palette[index*4:index*4+4])),
                             'editedResource': b64(expected_edit) if expected_edit else None})
    rgba_rows = [bytes((x*31+y*47+c*67) % 256 for x in range(width) for c in range(4)) for y in range(height)]
    valid = png(width, height, 8, 6, rgba_rows, 4)
    signature = b'\x89PNG\r\n\x1a\n'
    errors = []
    for name, depth, interlace, w, code in (('depth-16', 16, 0, width, 'PNG_UNSUPPORTED'), ('interlace', 8, 1, width, 'PNG_UNSUPPORTED'), ('dimension-overflow', 8, 0, 0xffffffff, 'PNG_LIMIT')):
        header = struct.pack('>IIBBBBB', w, height, depth, 6, 0, 0, interlace)
        errors.append({'name': name, 'png': b64(signature + chunk(b'IHDR', header) + valid[33:]), 'code': code})
    errors.append({'name': 'animation', 'png': b64(valid[:33]+chunk(b'acTL', struct.pack('>II', 1, 0))+valid[33:]), 'code': 'PNG_UNSUPPORTED'})
    errors.append({'name': 'missing-ending', 'png': b64(valid[:-12]), 'code': 'PNG_END'})
    raw = b''.join(b'\x00'+row for row in rgba_rows)
    for name, filtered, code in (('expanded-overflow', raw+b'\x00', 'PNG_INFLATE_SIZE'), ('truncated-expansion', raw[:-1], 'PNG_INFLATE'), ('unknown-filter', b'\x05'+raw[1:], 'PNG_FILTER')):
        errors.append({'name': name, 'png': b64(valid[:33]+chunk(b'IDAT', zlib.compress(filtered))+chunk(b'IEND', b'')), 'code': code})
    small_layout = {'id': 0, 'pixelOffset': 0, 'capacity': 128, 'paletteOffset': 128, 'colorCount': 16, 'width': 32, 'height': 8, 'compressed': True}
    small = bytearray(128)
    packed = bytes(128)
    encoded = gzip.compress(packed, compresslevel=9, mtime=0)
    end = (32+len(encoded)+15)//16*16
    struct.pack_into('<III', small, 0, 1, 16, end)
    struct.pack_into('<I', small, 16, len(packed))
    small[32:32+len(encoded)] = encoded
    palette = bytes(value for index in range(16) for value in ((index*17)%256, (index*61)%256, (index*101)%256, 0 if index == 0 else 255))
    rng = random.Random(42)
    noise = bytes(rng.randrange(256) for _ in range(128))
    noisy_rgba = bytearray()
    for y in range(8):
        for x in range(32):
            index = (noise[y*16+x//2] >> (x%2*4)) & 15
            noisy_rgba.extend(palette[index*4:index*4+4])
    return {'pngCases': cases, 'pngErrors': errors, 'textures': textures,
            'capacityTexture': {'layout': small_layout, 'resource': b64(small+palette), 'noisyRgba': b64(noisy_rgba)}}


def main():
    """Create the checked-in oracle once, or verify it against independent formulas during every release build."""
    parser = argparse.ArgumentParser()
    parser.add_argument('--write', action='store_true')
    args = parser.parse_args()
    expected = json.dumps(make_vectors(), indent=2) + '\n'
    if args.write:
        OUTPUT.write_text(expected, encoding='utf-8', newline='\n')
    elif OUTPUT.read_text(encoding='utf-8') != expected:
        raise SystemExit('Image codec oracle differs from independent formulas')
    print('PASS independent image oracle: 24 PNG cases, 8 malformed PNG cases, 4 texture cases and compressed-capacity refusal')


if __name__ == '__main__':
    main()
