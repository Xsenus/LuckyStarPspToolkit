# Testing and acceptance

## Distinct layers, distinct evidence

1. Offline source/documentation checks: Python lexical/XML checks, Bash syntax, project and workflow inspection. These do NOT compile C#.
2. Independent binary reference implementations: generated CPK/script/CRILAYLA/LT/BDF/ISO bytes and Python validators. These do NOT execute the C# codecs.
3. Python unit tests: actually execute fail-fast/packaging/rollback routines; the explicit mocked compiler-failure case checks orchestration, not compilation.
4. C# compilation + all three self-test projects + Roslyn named-declaration audit. Mandatory on a real .NET 9 SDK host.
5. Native published executable plus the process licensing lifecycle: mandatory on Windows and Linux CI jobs. `license_integration.py` activates a throwaway test client and runs `demo_customer.py` for synthetic text/ISO round-trips. The normal unconfigured preview remains locked.
6. Real customer archive + game resources + PPSSPP/hardware acceptance: separate and currently incomplete.

The first three layers can pass while the fourth still finds errors. Reports must say which layers were executed. No "100% working" assertion follows from an oracle hash alone.

## Run commands

```bash
python -m unittest discover -s validation -p test_release_pipeline.py -v
python scripts/document_csharp.py --check --include-tests
python scripts/document_members.py --check
python validation/validate_csharp_docs.py
python scripts/generate_api_reference.py --check
python scripts/reference_oracle.py
python validation/validate_font_fixtures.py
python validation/validate_iso_fixture.py
python validation/validate_iso_rebuild.py
python scripts/build_release.py --compile-only
```

The last command must fail if the SDK is missing. It runs the actual Roslyn gate after compilation. It must never fabricate a binary or successful status file.

## New runtime regressions

`ReleaseSafetyTests.cs` covers duplicate/escaped JSON property names, UTF-8 BOM, filesystem-root containment, Windows-style traversal on all hosts, an immutable glyph-byte snapshot, and rejection of build output inside translator workspace. Existing tests retain CPK, CRILAYLA, script, font, EBOOT and ISO checks.

## Documentation gates

All explicitly named types, methods, constructors, local functions, fields, properties, indexers, events and enum members require XML summary. The compiled Roslyn tool also checks attached parameter docs including primary constructors. Lambdas, compiler-generated members and property accessor bodies are not separately named user declarations; documenting the containing property/algorithm is sufficient. The offline scanner has a narrower recognised grammar and is not described as a complete C# parser.

## Performance

No throughput benchmark on real game archives was run. The new warm-pool AES-CMAC
regression measured 1,392 managed allocated bytes for a 4 MiB message on the recorded
alternate host. That number excludes the existing input, initial pool buffers, native
cryptography and total process RSS; it is not a general RAM or performance guarantee.
The implementation uses 64 KiB chunks instead of multiple message-sized copies.
UTF tables now cap cell count and materialized binary/string data before allocations.
Earlier redundant glyph-map passes remain removed. Strict duplicate-key JSON parsing
retains an additional O(n) validation pass to avoid ambiguous manifests.

### Ограничения карты глифов в 0.11.0

Помимо байтового лимита, входной карте задаются предел 1 000 000 UTF-16 единиц и 128 единиц на одну метку глифа. Количество строк и длина меток проверяются до `Split`/построения trie; это предотвращает непропорциональное выделение памяти для огромной строки или миллионов пустых строк. Пределы настраиваются в `FileLimits`. Это защитные ограничения, а не измеренная гарантия расхода RAM или времени.

## Executed alternate-host checks in 0.11.0

`verify_roslyn.py` now compiles and invokes the real C# source via an already installed
PowerShell/Roslyn environment. It records the actual compiler, runtime/reference version,
source fingerprints and subprocess exit codes. This is separate evidence, NOT a successful
.NET 9 SDK build. Do not mix these results with the native release matrix.

The new regressions cover constant-memory CMAC, 81 independent tags, two-target cleanup
failure, overlapping transaction paths, UTF amplification budgets and 512 malformed headers.
See TEST_REPORT_RU.md for actual observations and unexecuted checks.

## Scenario regressions added in 0.12.0

`ScriptRegressionTests.cs` adds 13 named groups: lossless no-op with a text-internal
jump, next-record and checksum boundaries, wide metadata arithmetic, duplicate
record starts, input/field/aggregate/output budgets, direct API mutations, empty
choices, reordered dialogue tables, zero-dialogue opaque scripts, NIM layout,
binary-search mapping versus a linear reference, scale tests and header mutation tests.

A constructed fixture has a VALID additive checksum whose first word is `0xFFFB`;
it must not be consumed as a missing message terminator. The regression is reproduced
against the old DLL as well, rather than merely assumed from source inspection.

The new suite executes 4084 relocation queries against a linear oracle, checks
8192 dialogues / 16385 targets / 4096 edits, and runs 1024 deterministic mixed header
mutations. It accepts legal cases and requires byte-identical no-op results, while
invalid cases must throw a documented ToolkitException rather than an indexing
or arithmetic exception. Counts and raw output are recorded in TEST_REPORT_RU.md.

`ScriptScaleFixture.cs` is shared by the two-version benchmark and `--probe`; no
game binaries or prepared font files are distributed. Timing scope, JIT settings,
managed-allocation caveats and recorded samples are in PERFORMANCE_RU.md.

## CPK regressions added in 0.13.0

11 groups cover exact no-op/ownership, auxiliary migration both ways, incompatible
schemas, input/metadata budgets, unsigned ranges, extra indices, descriptor errors,
metadata-only inspection/extraction, mutable entries, 65535/65536 transitions, 512
header mutations and allocation scaling. `CpkScaleFixture` is compiled unchanged
against old/new public APIs for recorded defect and allocation comparisons.
