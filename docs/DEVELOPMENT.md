# Development

Read [AGENTS.md](../AGENTS.md) before editing. The current version is in `VERSION`;
release metadata must agree with it. Native builds use the stable .NET 9 SDK selected
by `global.json`, Python 3.11+ and Node.js 22+ for the owner web UI. The runtime uses
the .NET base class library; the documentation auditor references SDK Roslyn assemblies.

## First checked build

From the repository root, using the same Python interpreter for installation and scripts:

```powershell
dotnet --list-sdks
python --version
node --version
python -m pip install -r validation/requirements.txt
node web/license-admin/test.mjs
node web/license-admin/build.mjs
python scripts/build_release.py --compile-only
```

The driver generates synthetic fixtures, checks documentation and independent oracles,
builds the real solution, runs all three C# self-test projects and the SDK Roslyn audit.
Review `artifacts/build-logs/<run-id>/build-report.json`; a missing SDK or failed stage
must produce a nonzero exit code. A plain IDE build does not replace these checks.

For a blocked preview package, run `python scripts/build_release.py --rids win-x64`
on Windows or `--rids linux-x64` on Linux. A customer package additionally requires
`--license-trust <public-client-trust.json>`. The official NuGet source is needed for
Microsoft runtime packs during first restore/self-contained publication. Only the RID
matching the host can be executed there; the CI matrix executes both operating systems.

## Licensing and browser integration

After the solution and frontend build:

```powershell
python scripts/license_integration.py
python -m pip install -r web/license-admin/requirements-test.txt
python -m playwright install chromium
python scripts/web_reserve_integration.py --output artifacts/browser-local
```

On Linux CI use `python -m playwright install --with-deps chromium` to install the
browser system dependencies as well. These tests use throwaway local issuers and
test keys. Production HTTPS, the real server configuration and actual game resources
need separate acceptance. Do not use the diagnostic `--browser-bridge` mode as
evidence that production browser transport works.

Build owner tools with `python scripts/build_license_owner.py --rids linux-x64,win-x64`.
Configure a real server using [the owner guide](LICENSE_OWNER_RU.md) and
[the web deployment guide](ADMIN_WEB_RU.md). Do not use production keys in tests.

## Focused changes and documentation

Run the tests relevant to a change, then the checked build before release. Documentation
is generated only after reviewing source edits:

```powershell
python scripts/document_csharp.py --check --include-tests
python scripts/document_members.py --check
python scripts/generate_api_reference.py
python scripts/generate_api_reference.py --check
```

Generated starter comments are editing aids. Review units, bounds, mutation, exception
and binary-format invariants; passing a lexical scanner alone does not prove comment
quality. All named declarations require XML documentation under AGENTS.md. The compiled
Roslyn auditor is authoritative for C# syntax. Keep API output deterministic across
Windows and Linux when changing the generator.

C# compiler warnings are errors; analysis warnings are reported separately. Do not
suppress CS1591 to hide missing public documentation. CS1587 is tolerated locally for
XML local-function comments, which the Roslyn auditor checks explicitly.

Keep originals, translations, maps, issuer state and test secrets under ignored private
locations. Never weaken revision hashes to accept a failing game file. Python oracles
must remain independent from production codecs. The current evidence and missing
customer inputs are listed in [the acceptance matrix](CUSTOMER_ACCEPTANCE_RU.md).
