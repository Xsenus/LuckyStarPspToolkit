#!/usr/bin/env python3
"""Demonstrate a COMPILED CLI on synthetic data, never pretend it is a running Lucky Star translation."""
from __future__ import annotations
import argparse
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import sys
import uuid

ROOT = Path(__file__).resolve().parents[1]
FIXTURES = ROOT / 'tests/LuckyStarPspToolkit.Formats.SelfTests/Fixtures'
sys.path.insert(0, str(ROOT / 'scripts'))
from build_release import ensure_regular_tree, write_json  # noqa: E402


def demonstrate(cli: Path, output: Path, *, powershell_host: Path | None = None) -> dict[str, object]:
    """Run positive and negative CLI scenarios and record actual exit codes and round-trip comparisons."""
    cli = cli.resolve(strict=True)
    ensure_regular_tree(output)
    output.mkdir(parents=True, exist_ok=False)
    if powershell_host is not None:
        if cli.suffix.lower() != '.dll':
            raise ValueError('The diagnostic PowerShell host accepts a locally compiled .dll only.')
        prefix = [str(powershell_host), '-NoLogo', '-NoProfile', '-File', str(ROOT / 'scripts/run_managed.ps1'), str(cli)]
    else:
        prefix = ['dotnet', str(cli)] if cli.suffix.lower() == '.dll' else [str(cli)]
    steps: list[dict[str, object]] = []
    report: dict[str, object] = {'dataKind': 'SYNTHETIC', 'gameRuntimeVerified': False,
        'status': 'running', 'steps': steps,
        'executionKind': 'powershell-hosted-managed' if powershell_host else 'published-cli',
        'standardNet9Validation': False if powershell_host else None}

    def run(name: str, *arguments: str | Path, expected: int = 0) -> None:
        """Execute one CLI command and fail the demo if its actual exit code is unexpected."""
        command = prefix + [str(value) for value in arguments]
        completed = subprocess.run(command, cwd=ROOT, capture_output=True, text=True,
                                   encoding='utf-8', errors='replace', timeout=300, check=False)
        log = output / (name + '.log')
        log.write_text(completed.stdout + completed.stderr, encoding='utf-8')
        steps.append({'name': name, 'arguments': [str(value) for value in arguments],
                      'expectedExitCode': expected, 'exitCode': completed.returncode, 'log': log.name})
        if completed.returncode != expected:
            raise RuntimeError(f'{name}: expected exit {expected}, got {completed.returncode}; see {log}')
        print(f'PASS {name}', flush=True)

    try:
        run('01-version', 'version')
        run('02-self-test', 'self-test')
        cpk = FIXTURES / 'reference.cpk'
        glyphs = FIXTURES / 'glyph-map.txt'
        run('03-cpk-verify', 'cpk-verify', cpk)
        workspace = output / 'translation'
        run('04-export', 'workspace-export', cpk, glyphs, workspace, '--game', 'rgo', '--ids', '0')
        translation = workspace / 'script-0000.json'
        document = json.loads(translation.read_text(encoding='utf-8'))
        document['dialogs'][0]['translationSpeaker'] = 'Коната'
        document['dialogs'][0]['translationMessage'] = 'Привет!'
        document['choiceGroups'][0]['choices'][0]['translationText'] = 'Да!'
        write_json(translation, document)
        run('05-validate', 'workspace-validate', workspace, cpk, '--json', output / 'validation.json')
        patched = output / 'sc-patched.cpk'
        plan = output / 'eboot-size-plan.json'
        run('06-build', 'workspace-build', workspace, cpk, patched, '--plan', plan)
        checked = output / 'reextracted'
        run('07-reextract', 'workspace-export', patched, glyphs, checked, '--game', 'rgo', '--ids', '0')
        result = json.loads((checked / 'script-0000.json').read_text(encoding='utf-8'))
        if (result['dialogs'][0]['sourceMessage'] != 'Привет!'
            or result['dialogs'][0]['sourceSpeaker'] != 'Коната'
            or result['choiceGroups'][0]['choices'][0]['sourceText'] != 'Да!'):
            raise RuntimeError('Re-extracted text did not match the inserted Cyrillic strings.')
        report['textRoundTrip'] = True
        document['dialogs'][0]['translationMessage'] = '{{GLYPH:FFFF}}'
        write_json(translation, document)
        rejected = output / 'MUST_NOT_EXIST.cpk'
        run('08-reject-control', 'workspace-build', workspace, cpk, rejected, expected=3)
        if rejected.exists():
            raise RuntimeError('A rejected input produced an output file.')
        document['dialogs'][0]['translationMessage'] = 'Привет!'
        write_json(translation, document)
        run('09-font-inspect', 'font-inspect', FIXTURES / 'reference-lt.bin', glyphs,
            '--preview', output / 'synthetic-font.png', '--json', output / 'font.json', expected=2)
        run('10-font-import', 'font-import-bdf', FIXTURES / 'reference-lt.bin', glyphs,
            FIXTURES / 'reference-font.bdf', output / 'synthetic-lt.bin', '--mode', 'russian',
            '--json', output / 'font-import.json')
        run('11-iso-patch', 'iso-patch', FIXTURES / 'reference.iso', FIXTURES / 'iso-patch-manifest.json',
            output / 'synthetic-patched.iso', '--json', output / 'iso-patch.json')
        actual = hashlib.sha256((output / 'synthetic-patched.iso').read_bytes()).hexdigest()
        expected = hashlib.sha256((FIXTURES / 'reference-patched.iso').read_bytes()).hexdigest()
        if actual != expected:
            raise RuntimeError('The rebuilt synthetic ISO does not match the independent expected result.')
        report['isoOracleMatch'] = True
        report['negativeTestProducedNoOutput'] = True
        report['status'] = 'passed-synthetic-demo'
        return report
    except Exception as exc:
        report['status'] = 'failed'
        report['error'] = str(exc)
        raise
    finally:
        write_json(output / 'DEMO-REPORT.json', report)


def main() -> int:
    """Locate the compiled executable and create a separate evidence directory for the demonstration."""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--cli', required=True, type=Path)
    parser.add_argument('--output', type=Path)
    parser.add_argument('--powershell-host', type=Path, help='Diagnostic fallback for locally compiled DLLs; NOT .NET 9 acceptance.')
    args = parser.parse_args()
    output = args.output or ROOT / 'artifacts' / 'demo' / uuid.uuid4().hex
    try:
        demonstrate(args.cli, output, powershell_host=args.powershell_host)
        print(f'Synthetic demonstration evidence: {output}')
        print('This does not verify text rendering or gameplay in PPSSPP or on a PSP.')
        return 0
    except Exception as exc:
        print(f'DEMO FAILED: {exc}', file=sys.stderr)
        return 1


if __name__ == '__main__':
    raise SystemExit(main())
