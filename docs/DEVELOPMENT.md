# Development — 0.10.0

Use a stable .NET 9 SDK selected by global.json, Python 3.11+, and validation/requirements.txt. Runtime code has no third-party NuGet packages. The official NuGet source is still needed to restore Microsoft runtime packs for self-contained cross-RID publication.

Build and test without packaging:

```bash
python -m pip install -r validation/requirements.txt
python scripts/build_release.py --compile-only
```

Build release archives:

```bash
python scripts/build_release.py --rids win-x64,linux-x64
```

The output goes under artifacts, never to a caller-selected directory that is recursively erased. Old publish.sh/publish.ps1 output-directory arguments are intentionally no longer supported. Publish scripts now accept only the runtime and call the checked driver.

Generate documentation after reviewed source edits:

```bash
python scripts/document_csharp.py --check --include-tests
python scripts/document_members.py --check
python scripts/generate_api_reference.py
```

Starter-comment generation is an editing tool, not proof of explanatory quality. Review comments for units, index bases, mutation semantics, exceptions and binary invariants. Do not run automatic repair to silence a substantive doc mismatch.

C# projects compile with nullable and compiler warnings as errors; analysis warnings are still reported and are not all promoted to errors. Do not suppress CS1591 to hide undocumented public API. CS1587 is locally tolerated for XML local-function comments, whose attachment is checked explicitly by the SDK Roslyn auditor.

Keep original input, snapshots and translations in ignored private/. Never weaken revision hashes to make a failing game file pass. Source changes and self-tests must move together; a Python oracle is an independent cross-check, not the production implementation.
