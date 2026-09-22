#!/usr/bin/env python3
"""Execute real C# code using a local PowerShell/Roslyn host; never mark the target .NET 9 release as built.

This optional maintainer fallback downloads nothing and must not replace build.cmd
or the native CI matrix. Compiled diagnostic assemblies stay in artifacts/ and
are not included in source handoffs. Its reference framework is recorded explicitly.
"""
from __future__ import annotations

import argparse
import json
from pathlib import Path
import shutil
import sys
import uuid

from build_release import CommandRunner, BuildError, ensure_regular_tree, write_json, digest
from managed_evidence import execution_input_hashes

ROOT = Path(__file__).resolve().parents[1]


def verify(pwsh: Path) -> int:
    """Compile eleven assemblies, invoke three suites and Roslyn docs, then run the full synthetic CLI demo."""
    base = ROOT / 'artifacts' / 'roslyn' / uuid.uuid4().hex
    ensure_regular_tree(base)
    base.mkdir(parents=True)
    runner = CommandRunner(ROOT, base / 'logs')
    assembly_root = base / 'assemblies'
    report = {'schema': 'lsptool.actual-managed-fallback.v1',
              'version': (ROOT / 'VERSION').read_text().strip(),
              'status': 'running', 'steps': runner.steps,
              'standardNet9Validation': False, 'nativeWindowsVerified': False,
              'gameRuntimeVerified': False, 'binaryReleaseProduced': False,
              'inputHashes': execution_input_hashes(ROOT)}
    code = 1
    try:
        runner.run('fixtures', [sys.executable, 'scripts/reference_oracle.py'])
        runner.run('roslyn-compilation', [str(pwsh), '-NoProfile', '-NoLogo', '-File',
            'scripts/compile_with_roslyn.ps1', '-Root', str(ROOT), '-Out', str(assembly_root)])
        report['compilation'] = json.loads((assembly_root / 'compilation-report.json').read_text())
        shutil.copytree(ROOT / 'tests/LuckyStarPspToolkit.Formats.SelfTests/Fixtures', assembly_root / 'Fixtures')
        for name in ('LuckyStarPspToolkit.SelfTests', 'LuckyStarPspToolkit.Formats.SelfTests',
                     'LuckyStarPspToolkit.Licensing.SelfTests', 'LuckyStarPspToolkit.Documentation'):
            extra = [str(ROOT)] if name.endswith('.Documentation') else []
            runner.run(name, [str(pwsh), '-NoProfile', '-NoLogo', '-File', 'scripts/run_managed.ps1',
                            str(assembly_root / (name + '.dll')), *extra])
        runner.run('managed-cli-self-test', [sys.executable, 'scripts/check_locked_cli.py', '--cli', str(assembly_root / 'lsptool.dll'), '--pwsh', str(pwsh)])
        runner.run('managed-synthetic-demo', [sys.executable, 'scripts/license_integration.py', '--assemblies',
                    str(assembly_root), '--pwsh', str(pwsh), '--output', str(base / 'licensing')])
        report['licensing'] = json.loads((base / 'licensing/licensing-integration-report.json').read_text())
        report['demo'] = report['licensing']['demo']
        if report['inputHashes'] != execution_input_hashes(ROOT):
            raise BuildError('Source changed during managed verification; run it again.')
        report['status'] = 'passed-experimental-managed-host'
        code = 0
    except (BuildError, OSError, ValueError) as exc:
        report['status'] = 'failed'
        report['error'] = str(exc)
        print(f'MANAGED FALLBACK FAILED: {exc}', file=sys.stderr)
    finally:
        report['logHashes'] = {path.name: digest(path) for path in (base / 'logs').glob('*.log')}
        write_json(base / 'managed-fallback-report.json', report)
        evidence = ROOT / 'artifacts/validation/managed-fallback'
        ensure_regular_tree(evidence)
        evidence.mkdir(parents=True, exist_ok=True)
        # Export evidence only, never local runtime/compiler/assemblies or generated fonts.
        for path in (base / 'logs').glob('*.log'):
            shutil.copy2(path, evidence / path.name)
        write_json(evidence / 'managed-fallback-report.json', report)
        if (base / 'licensing/licensing-integration-report.json').is_file():
            clean = ROOT / 'artifacts/validation/licensing-process'
            clean.mkdir(parents=True, exist_ok=True)
            for path in (base / 'licensing').glob('*.log'):
                shutil.copy2(path, clean / path.name)
            shutil.copy2(base / 'licensing/licensing-integration-report.json', clean / 'licensing-integration-report.json')
            if (base / 'licensing/demo/DEMO-REPORT.json').is_file():
                shutil.copy2(base / 'licensing/demo/DEMO-REPORT.json', clean / 'DEMO-REPORT.json')
        print(f'Managed verification: {report["status"]}; .NET 9 SDK/release and gameplay are NOT verified.')
    return code


def main() -> int:
    """Resolve an explicit trusted PowerShell host and stop on any compilation or test failure."""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--pwsh', type=Path, default=Path(shutil.which('pwsh')) if shutil.which('pwsh') else None)
    args = parser.parse_args()
    if args.pwsh is None or not args.pwsh.is_file():
        print('A locally installed PowerShell containing Roslyn and reference assemblies is required.', file=sys.stderr)
        return 2
    return verify(args.pwsh.resolve())


if __name__ == '__main__':
    raise SystemExit(main())
