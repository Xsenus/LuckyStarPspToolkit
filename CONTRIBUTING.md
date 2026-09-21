# Contributing

Thank you for improving Lucky Star PSP Translation Toolkit. The repository is intentionally conservative: it manipulates opaque binary formats and must fail closed when a revision, checksum, offset, or size is not proven.

## Before opening a change

1. Read `AGENTS.md`, `docs/ARCHITECTURE.md`, `docs/FORMATS.md`, and `SECURITY.md`.
2. Create a branch from the latest tagged release.
3. Do not commit copyrighted game data, firmware, commercial fonts, translations, save files, screenshots, or decrypted executables.
4. Keep runtime code dependency-free unless a proposal explains why the .NET base class library is insufficient.
5. Add or update an independent fixture/oracle for every binary-format change.

## Development setup

Required:

- .NET 9 SDK;
- Python 3.11 or newer;
- Git;
- Bash on Linux/macOS or PowerShell 7 on Windows.

```bash
python3 -m pip install -r validation/requirements.txt
python3 scripts/reference_oracle.py
python3 validation/static_validate.py
dotnet restore LuckyStarPspToolkit.sln
dotnet build LuckyStarPspToolkit.sln -c Release --no-restore
dotnet run --project tests/LuckyStarPspToolkit.SelfTests -c Release --no-build
dotnet run --project tests/LuckyStarPspToolkit.Formats.SelfTests -c Release --no-build
```

## Code rules

- Validate every offset, length, count, alignment, and arithmetic conversion.
- Prefer `checked` arithmetic for sizes and offsets.
- Do not write output until all preconditions are validated.
- User-visible output must be staged and committed atomically.
- Reject symbolic links/reparse points when a path participates in a protected operation.
- Preserve unknown bytes and commands unless their meaning is proven.
- Every named C# type, method, constructor, and local function must have XML documentation. Run:

```bash
python3 scripts/document_csharp.py --check --include-tests
python3 validation/validate_csharp_docs.py
```

- Comments explain binary invariants, ownership, and failure behavior; they should not merely repeat syntax.

## Tests expected for a pull request

A pull request should include the smallest relevant set of:

- positive parse/build/parse round trips;
- negative boundary, overflow, checksum, duplicate, and tamper tests;
- deterministic-output assertions;
- exact SHA-256 oracle data;
- customer-file tests only in a private environment, never committed.

Run the complete local gate with:

```bash
python3 scripts/release_validate.py
```

Pass `--customer-archive /private/archive.zip` only on a trusted machine.

## Commit and pull-request format

Use a focused subject, for example:

```text
CPK: preserve constant UTF columns during rebuild
ISO: reject directory records outside declared volume
Docs: document EBOOT VWF lineage checks
```

A pull request must describe:

- the user-visible change;
- format assumptions and invariants;
- tests actually run;
- checks not run and why;
- compatibility and migration impact.

## Licensing

Contributions are accepted under the repository's MIT License. Do not copy source from repositories that lack an explicit compatible license. Format facts may be independently reimplemented, but provenance must be recorded in `THIRD_PARTY_NOTICES.md` when relevant.
