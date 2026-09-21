# Architecture

## Goals

Lucky Star PSP Translation Toolkit is a fail-closed, dependency-free .NET command-line application for preparing translation resources from caller-owned PSP game files. Its design prioritizes reproducibility, revision safety, preservation of unknown data, bounded resource use, and atomic output.

## Solution layout

| Project | Responsibility |
|---|---|
| `LuckyStarPspToolkit.Core` | PSP PRX authentication/decryption, ELF inspection, SFO, revision profiles, EBOOT VWF/size/heap patching, generic binary patching |
| `LuckyStarPspToolkit.Formats` | CRI CPK/@UTF/ITOC, CRILAYLA, scripts, glyph maps, LT/BDF/PNG, ISO9660, workspaces, archive auditing |
| `LuckyStarPspToolkit.Cli` | Argument parsing, command orchestration, stable exit codes, human and JSON reports |
| `LuckyStarPspToolkit.SelfTests` | Core cryptographic, profile, SFO, ZIP, patch, and customer-file regression tests |
| `LuckyStarPspToolkit.Formats.SelfTests` | Independent-fixture round trips and negative tests for every supported format |

## Dependency direction

```text
CLI ───────────────► Core
 │                   ▲
 └────► Formats ─────┘

SelfTests ─────────► Core / Formats / CLI
```

Core never depends on Formats. Formats may use Core only for revision-aware EBOOT workflows. Runtime projects use the .NET base class library only.

## Trust boundaries

Every file, archive entry, table row, offset, and manifest field is untrusted. The following boundaries are validated before persistent output:

1. File-system paths are normalized and checked for reparse points.
2. Files are bounded before in-memory reads.
3. Integer arithmetic for offsets and sizes is checked.
4. Duplicate endian fields and duplicated normalized names must agree.
5. Revision-specific EBOOT writes require canonical SHA-256 lineage and exact instruction preconditions.
6. CPK/script/font/ISO outputs are reparsed and compared before commit.
7. Output is staged and atomically replaced; the source is never modified in place.

## Transaction model

Operations that create one file use `AtomicFile`. Operations that create several related files use a staged write set. Rebuilders first produce and verify an in-memory or temporary result. Only after every postcondition succeeds are destination files committed. Failed rollback leaves an explicit backup rather than silently discarding the previous output.

## Format preservation model

The toolkit distinguishes known semantics from opaque data:

- known fields are validated and regenerated;
- unknown UTF columns, script commands, reserved font bytes, padding, and untouched ISO sectors are preserved;
- a transformation is rejected when relocation cannot be proven safe;
- no heuristic patch is applied to an unknown executable revision.

## Extension points

A new game revision requires:

1. disc metadata and encrypted/decrypted SHA-256 values;
2. executable structure and exact patch preconditions;
3. script jump-table and size-table metadata;
4. independent synthetic fixtures;
5. private integration validation on caller-owned files;
6. a documented profile that remains blocked until all required checks pass.
