> Рабочие команды требуют действующую лицензию. Заранее выданный резерв разрешает ограниченную работу без связи. Сначала [инструкция владельца](https://github.com/Xsenus/LuckyStarPspToolkit/blob/main/docs/LICENSE_OWNER_RU.md) и [активация](LICENSE_CUSTOMER_RU.md). Сборка без `--license-trust` остаётся заблокированной. Старое standalone demo требует уже активированный CLI; `scripts/license_integration.py` проверяет полный сценарий на отдельном тестовом издателе.

# CLI reference

Run `lsptool --help` for the canonical command surface. Commands return stable exit codes and accept `--json <path>` where documented.

## Exit codes

| Code | Meaning |
|---:|---|
| 0 | Operation completed successfully |
| 2 | Incomplete inspection or unsupported compatibility state |
| 3 | Invalid or unsafe input, including an unapproved patch |
| 64 | Invalid command-line usage |
| 70 | Unexpected internal failure |
| 74 | File-system or transactional output failure |

## Inspection and executable commands

```text
lsptool inspect <file|directory|zip> [--json report.json]
lsptool audit-rgo <file|directory|zip> [--json report.json]
lsptool verify-eboot <EBOOT.BIN> [--json report.json]
lsptool decrypt-eboot <EBOOT.BIN> <EBOOT.ELF> [--json report.json]
lsptool sfo <PARAM.SFO> [--json report.json]
lsptool eboot-vwf-groups
lsptool eboot-vwf-inspect <EBOOT.BIN|ELF> [--json report.json]
lsptool eboot-vwf-apply <EBOOT.BIN|ELF> <output.ELF> --experimental-vwf [--groups russian-text] [--json report.json]
lsptool eboot-build <EBOOT.BIN|ELF> <output.ELF> --experimental-vwf [--groups russian-text] [--size-plan plan.json] [--json report.json]
```

VWF is a research path: the full patch made Japanese name-entry keyboard glyphs
disappear in PPSSPP. The explicit flag does not certify gameplay compatibility.
`decrypt-eboot` accepts the exact verified RGO or NIM encrypted revision. For NIM,
the JSON report marks `patchProfileAvailable: false`; this command only reads the
executable and does not make a modified NIM ELF playable.

## CPK and script commands

```text
lsptool cpk-list <archive.cpk> [--json report.json]
lsptool cpk-verify <archive.cpk> [--json report.json]
lsptool cpk-extract <archive.cpk> <id> <output.bin>
lsptool cpk-replace <archive.cpk> <id> <replacement.bin> <output.cpk> [--json report.json]
lsptool script-inspect <script.bin> --game rgo|nim [--json report.json]
```

## Translation workspace

```text
lsptool glyph-map-validate <glyph-map.txt> [--json report.json]
lsptool workspace-export <sc.cpk> <glyph-map.txt> <workspace-dir> --game rgo|nim
lsptool workspace-validate <workspace-dir> <original-sc.cpk> [--json report.json]
lsptool workspace-build <workspace-dir> <original-sc.cpk> <patched-sc.cpk> [--plan eboot-size-plan.json]
lsptool apply-eboot-plan <decrypted-EBOOT.ELF> <plan.json> <patched-EBOOT.ELF>
```

Only `translationSpeaker`, `translationMessage`, and `translationText` are translator-editable. Source fields, IDs, terminators, and origin hashes are integrity-protected.

## Speaker-name catalogues

```text
lsptool workspace-names-export <workspace-dir> <source-sc.cpk> <names.json>
lsptool workspace-names-apply <workspace-dir> <source-sc.cpk> <names.json> <new-workspace-dir>
```

Edit only `translationSpeaker` in `names.json`. Null leaves existing translations
alone; an empty string clears matching names. Non-null translations apply to
every exact source-name match. Keep source names, occurrence counts and hashes.
Applying validates the original workspace and publishes a separate validated
copy; the output directory must not exist. Subsequent validate/build commands
must use that new workspace. This changes dialogue names, not graphical menus.

## Font inspection and import

```text
lsptool font-inspect <lt.bin> <glyph-map.txt> [--spacing fixed|variable] [--preview atlas.png] [--json report.json]
lsptool font-export-bdf <lt.bin> <glyph-map.txt> <output.bdf> [--json report.json]
lsptool font-import-bdf <lt.bin> <glyph-map.txt> <font.bdf> <output-lt.bin> --mode russian [--preview atlas.png] [--json report.json]
```

`font-inspect` defaults to `--spacing fixed`: success means all 66 Russian letters
have mapped bitmap ink. `--spacing variable` additionally requires valid nonzero
VWF advance widths. The report exposes bitmap coverage separately from VWF
readiness; neither mode proves that an edited game renders correctly at runtime.
`font-import-bdf` still validates VWF widths for imported letters.

`font-export-bdf` creates an editable 66-letter Cyrillic BDF template from the
existing mapped bitmaps, including letters with zero VWF advances. It preserves
cell positions, uses baseline 15 and fixed advances of 18. Nonzero LT pixel
levels become monochrome ink: this is an editing template, not a lossless font
backup. Import edited templates with `--baseline 15 --replace-existing` into a
new LT file; never replace the only copy of the original game font.

## RGO interface images

```text
lsptool menu-export <original-rgo-pr.bin> <new-image-dir> [--json report.json]
lsptool menu-build <original-rgo-pr.bin> <image-dir> <new-pr.bin> [--quantize] [--json report.json]
```

These commands support the exact original ULJM05752 PR resource and export ten
PNG atlases plus `menu-images.json`. Edit lettering inside its existing image
regions; preserve dimensions, filenames, positions and the manifest. Keep all
ten PNG files, including unchanged ones. Use the original resource for every
build, and write to a separate output file. Original palettes and allocation
sizes are preserved. Aligned compressed-image checksums and the trailing
PR resource checksum are recalculated automatically. By default edited colors must match the original palette;
`--quantize` explicitly permits nearest-palette matching. Oversized compressed
textures are refused before output is written.

This covers PR interface atlases, including save/load and settings labels. It
does not cover all title-screen or `union.cpk` graphics, other PR revisions or
NIM menus. Resource validation does not certify runtime display: rebuild a copy
of the ISO and inspect each changed screen in the game.

```text
lsptool menu-union-export <original-rgo-union.cpk> <new-image-dir> [--json report.json]
lsptool menu-union-build <original-rgo-union.cpk> <image-dir> <new-union.cpk> [--quantize] [--json report.json]
```

The union commands support 25 authenticated PNG atlases in original RGO
resources 2529 (name entry), 2530 (options), 2531 (save) and 2533 (Extra).
Keep all PNG files, their sizes and positions, and `union-menu-images.json`.
Build from the original archive into a separate output. Entry sizes, CPK
offsets, palettes and unrelated bytes are preserved; image and resource
checksums are recalculated. This fixed-allocation mode requires no EBOOT
size/offset patch. Oversized compressed edits are rejected. Other resources,
revisions and NIM menus are unsupported. Reports retain `gameRuntimeVerified=false`.

## ISO commands

```text
lsptool iso-list <game.iso> [--json listing.json]
lsptool iso-extract <game.iso> <path-inside-iso> <output-file>
lsptool collect-assets <game.iso> <assets.zip> [--include-optional] [--json report.json]
lsptool iso-replace <game.iso> <path-inside-iso> <replacement> <output.iso> [precondition options]
lsptool iso-apply-manifest <game.iso> <manifest.json> <output.iso> [--json report.json]
```

## Diagnostics

```text
lsptool version
lsptool self-test
lsptool formats-self-test
lsptool customer-audit <archive.zip> [--json report.json]
```

The exact options and current aliases are printed by `lsptool --help`; scripts in `scripts/` provide common Windows and Linux workflows.

## CPK implementation note (0.13.0)

`cpk-list` now uses metadata-only inspection and range hashing; `cpk-extract`
materializes only the selected file after layout preflight. The input CPK is
still read into one bounded byte array. `cpk-verify` produces an exact no-op copy
when no payload or extract-size was changed. These optimizations do not skip
validation of descriptors, counts or compression headers.
