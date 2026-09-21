# Testing and acceptance — 0.11.0

## Distinct layers, distinct evidence

1. Offline source/documentation checks: Python lexical/XML checks, Bash syntax, project and workflow inspection. These do NOT compile C#.
2. Independent binary reference implementations: generated CPK/script/CRILAYLA/LT/BDF/ISO bytes and Python validators. These do NOT execute the C# codecs.
3. Python unit tests: actually execute fail-fast/packaging/rollback routines; the explicit mocked compiler-failure case checks orchestration, not compilation.
4. C# compilation + both self-test projects + Roslyn named-declaration audit. Mandatory on a real .NET 9 SDK host.
5. Native published executable and `demo_customer.py`: mandatory on Windows and Linux CI jobs, testing synthetic round-trip text and ISO output.
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
