#!/usr/bin/env python3
"""Record actual source/oracle/build checks without converting unavailable tools into successful tests.

Exit 0 means all requested checks passed, 2 means partial evidence, 1 means a
failed check. The source packager requires explicit --allow-partial for status 2.
"""
from __future__ import annotations
import argparse
import ast
from datetime import datetime, timezone
import importlib.metadata
import json
from pathlib import Path
import platform
import shutil
import subprocess
import sys
from typing import Sequence

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'scripts'))
from build_release import native_rid, write_json, ensure_regular_tree  # noqa: E402


def validate(customer: Path | None, require_dotnet: bool) -> dict[str, object]:
    """Execute available validators and preserve per-command exit codes, logs and explicit missing checks."""
    output = ROOT / 'artifacts/validation'
    ensure_regular_tree(output)
    output.mkdir(parents=True, exist_ok=True)
    checks: list[dict[str, object]] = []

    def run(name: str, command: Sequence[str]) -> bool:
        """Run a synchronous validator with a bounded timeout and capture its actual result."""
        log = f'{len(checks) + 1:02d}-{name}.log'
        try:
            result = subprocess.run(list(command), cwd=ROOT, text=True, encoding='utf-8', errors='replace',
                                    stdout=subprocess.PIPE, stderr=subprocess.STDOUT, timeout=1800, check=False)
            code, text = result.returncode, result.stdout
        except (OSError, subprocess.TimeoutExpired) as exc:
            code, text = -1, str(exc)
        (output / log).write_text(text, encoding='utf-8')
        checks.append({'name': name, 'status': 'passed' if code == 0 else 'failed',
                       'command': list(command), 'exitCode': code, 'log': log})
        print(f'{checks[-1]["status"].upper()} {name}', flush=True)
        return code == 0

    def skip(name: str, reason: str) -> None:
        """Record a check as not run; it must never contribute to the passing count."""
        checks.append({'name': name, 'status': 'not_run', 'reason': reason})
        print(f'NOT RUN {name}: {reason}', flush=True)

    syntax_errors: list[str] = []
    paths = [p for p in ROOT.rglob('*.py') if not {'.git', 'bin', 'obj', 'artifacts', '__pycache__'} & set(p.relative_to(ROOT).parts)]
    for path in paths:
        try:
            ast.parse(path.read_text(encoding='utf-8-sig'), filename=str(path))
        except SyntaxError as exc:
            syntax_errors.append(str(exc))
    checks.append({'name': 'python-syntax', 'status': 'failed' if syntax_errors else 'passed', 'files': len(paths), 'errors': syntax_errors})
    bash = shutil.which('bash')
    if bash:
        for path in sorted(ROOT.rglob('*.sh')):
            if {'.git', 'bin', 'obj', 'artifacts'} & set(path.relative_to(ROOT).parts):
                continue
            run('bash-' + path.stem, [bash, '-n', str(path)])
    else:
        skip('bash-syntax', 'Bash is not installed.')
    for name, args in [
        ('summary-methods', ['scripts/document_csharp.py', '--check', '--include-tests']),
        ('summary-members', ['scripts/document_members.py', '--check']),
        ('summary-xml', ['validation/validate_csharp_docs.py', '--output', str(output / 'csharp-docs.json')]),
        ('api-reference', ['scripts/generate_api_reference.py', '--check']),
        ('synthetic-fixture-generator', ['scripts/reference_oracle.py']),
        ('static-contracts', ['validation/static_validate.py']),
        ('source-audit', ['validation/audit_repository.py', '--output', str(output / 'repository-audit.json')]),
        ('preserved-baseline-only', ['validation/validate_customer_baseline.py']),
        ('python-font-oracle', ['validation/validate_font_fixtures.py', '--output', str(output / 'font-oracle.json')]),
        ('python-iso-oracle', ['validation/validate_iso_fixture.py', '--output', str(output / 'iso-oracle.json')]),
        ('python-iso-rebuild-oracle', ['validation/validate_iso_rebuild.py', '--output', str(output / 'iso-rebuild-oracle.json')]),
        ('python-release-unit-tests', ['-m', 'unittest', 'discover', '-s', 'validation', '-p', 'test_release_pipeline.py', '-v']),
    ]:
        run(name, [sys.executable, *args])
    if (ROOT / '.git').exists():
        run('git-whitespace', ['git', 'diff', '--check', 'HEAD'])
    else:
        skip('git-whitespace', 'This is a source snapshot without .git.')
    if customer is not None:
        for name, script in [('fresh-customer-bytes', 'validation/validate_customer_data.py'),
                             ('fresh-vwf-bytes', 'validation/validate_vwf_patch.py')]:
            run(name, [sys.executable, script, str(customer.resolve()), '--output', str(output / (name + '.json'))])
    else:
        skip('fresh-customer-bytes', 'Original private archive.zip was not supplied to this run.')
        skip('fresh-vwf-bytes', 'Preserved report consistency does not replace original EBOOT bytes.')
    pwsh = shutil.which('pwsh')
    if pwsh:
        command = "$bad=0; Get-ChildItem -Path scripts -Filter '*.ps1' | ForEach-Object { $e=$null; $t=$null; $null=[System.Management.Automation.Language.Parser]::ParseFile($_.FullName,[ref]$t,[ref]$e); if($e){$bad++;$e | Out-String | Write-Output} }; exit $bad"
        run('powershell-syntax', [pwsh, '-NoProfile', '-Command', command])
    else:
        skip('powershell-syntax', 'PowerShell is not installed.')
    dotnet = shutil.which('dotnet')
    if dotnet and native_rid() and not any(c['status'] == 'failed' for c in checks):
        run('actual-native-release-pipeline', [sys.executable, 'scripts/build_release.py', '--rids', native_rid()])
    else:
        for name in ('csharp-compilation', 'csharp-self-tests', 'compiled-roslyn-audit',
                     'native-published-demo', 'self-contained-publish'):
            skip(name, '.NET SDK/native target unavailable or preceding source validation failed.')
        if require_dotnet:
            checks.append({'name': 'required-dotnet-sdk', 'status': 'failed', 'reason': 'Required .NET validation was not executed.'})
    skip('github-hosted-ci', 'Workflow definitions were inspected; remote jobs were not executed here.')
    skip('ppsspp-and-psp-acceptance', 'Original sc.cpk, lt.bin, ISO and game runtime acceptance are not available.')
    counts = {state: sum(c['status'] == state for c in checks) for state in ('passed', 'failed', 'not_run')}
    report = {'schema': 'lsptool.actual-validation.v3', 'version': (ROOT / 'VERSION').read_text().strip(),
              'generatedUtc': datetime.now(timezone.utc).isoformat(),
              'status': 'failed' if counts['failed'] else ('partial' if counts['not_run'] else 'passed'),
              'summary': counts, 'checks': checks,
              'environment': {'python': platform.python_version(), 'platform': platform.platform(),
                              'dotnetFound': dotnet is not None, 'powershellFound': pwsh is not None,
                              'pygments': importlib.metadata.version('Pygments'),
                              'cryptography': importlib.metadata.version('cryptography')},
              'gameRuntimeVerified': False,
              'warning': 'Python oracles and mocked compiler-failure orchestration are NOT execution of C# codecs.'}
    write_json(output / 'validation-summary.json', report)
    return report


def main() -> int:
    """Expose partial evidence with a distinct nonzero code, instead of declaring an incomplete gate successful."""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--customer-archive', type=Path)
    parser.add_argument('--require-dotnet', action='store_true')
    args = parser.parse_args()
    report = validate(args.customer_archive, args.require_dotnet)
    print(json.dumps(report['summary'], ensure_ascii=False))
    return {'passed': 0, 'partial': 2, 'failed': 1}[report['status']]


if __name__ == '__main__':
    raise SystemExit(main())
