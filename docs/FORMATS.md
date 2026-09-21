# Supported binary formats

## PARAM.SFO

The SFO reader validates the header, key table, data table, entry count, entry bounds, value format, and UTF-8 decoding. It is used to identify the disc revision before any executable patching.

## PSP PRX and ELF

The verified RGO executable is a PSP PRX Type 2 module with tag `0xD91613F0`. The Core project validates the PRX envelope, SHA-1, KIRK CMAC values, declared sizes, and decrypted ELF32 little-endian MIPS header. Patching operates on the verified decrypted ELF. The toolkit does not claim to recreate a Sony-signed retail PRX.

## CRI CPK, @UTF, and ITOC

The CPK implementation supports the ITOC layout used by the games:

- encrypted or plain `@UTF` packets;
- zero, constant, and per-row storage modes;
- `DataL` and `DataH` descriptor tables;
- numeric, floating-point, string, and binary fields;
- deterministic extraction, replacement, rebuild, and parse-after-build verification.

Unknown columns and their values are retained. IDs must remain contiguous for the supported game archives.

## CRILAYLA

CRILAYLA payloads are decompressed with strict header, size, bitstream, back-reference, and output limits. Existing compressed entries can be preserved. New modified scripts are stored uncompressed unless a separately verified encoder is introduced.

## Lucky Star scripts

RGO uses a jump-table offset of `0x1E080`; the provisional NIM profile uses `0x800`. Supported structures include:

- script magic;
- jump offsets;
- command sections retained as opaque 16-bit words;
- speaker and message glyph indices;
- message terminators;
- choice groups and up to seven choices;
- game checksum in the final 16 bytes.

Text growth relocates dialogs, choices, command sections, and jumps through an explicit old-to-new offset map. A jump into an ambiguous modified payload is rejected.

## Glyph maps

A glyph-map line maps its zero-based line number to one Unicode token. Longest-prefix matching supports multi-character glyphs. Reserved control tokens include newline, protagonist first/last name, and explicit raw glyph indices. Structural terminators cannot be inserted as normal text.

## LT font

Each glyph record is 92 bytes:

- 18×18 pixels;
- 2 bits per pixel;
- 5 bytes per row;
- width metadata at offset 90;
- reserved metadata and unused pixel bits preserved from the source record.

The final 16 bytes store the game checksum. Padding after the glyph records is preserved. BDF import supports a caller-selected licensed font and validates coverage, clipping, dimensions, and the full Russian alphabet including Ё/ё.

## ISO9660

The reader supports the PSP primary ISO9660 tree, case-insensitive lookup, standard `;1` versions, and multi-extent reads. It validates duplicated endian values, volume bounds, directory cycles, entry/extents limits, and unsupported layouts.

The rebuilder appends replacement payloads on sector boundaries, updates both endian copies of directory-record LBA/size and PVD volume blocks, reparses the staged image, verifies every replacement, and confirms that unrelated source bytes/files remain unchanged.

## JSON contracts

Workspace and ISO patch manifests use strict deserialization: unknown properties, duplicate logical identifiers, invalid paths, and mismatched source hashes are rejected. Formal schemas live in `schemas/` and examples in `examples/` and `profiles/`.
