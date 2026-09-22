# CLI reference

Run `lsptool --help` for the canonical command surface. Commands return stable exit codes and accept `--json <path>` where documented.

## Exit codes

| Code | Meaning |
|---:|---|
| 0 | Operation completed successfully |
| 2 | Validation or compatibility failure |
| 3 | Required resource is missing |
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
lsptool eboot-vwf-apply <EBOOT.BIN|ELF> <output.ELF> [--groups russian-text] [--json report.json]
lsptool eboot-build <EBOOT.BIN|ELF> <output.ELF> [--groups russian-text] [--size-plan plan.json] [--json report.json]
```

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

## Font commands

```text
lsptool font-inspect <lt.bin> <glyph-map.txt> [--preview atlas.png] [--json report.json]
lsptool font-import-bdf <lt.bin> <glyph-map.txt> <font.bdf> <output-lt.bin> --mode russian [--preview atlas.png] [--json report.json]
```

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
