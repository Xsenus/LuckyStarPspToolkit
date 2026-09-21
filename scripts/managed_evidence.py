"""Fingerprint actual C# execution inputs; reject stale evidence without promoting it to a .NET 9 release."""
from __future__ import annotations

import hashlib
import json
from pathlib import Path

EXCLUDED = {'.git', 'bin', 'obj', 'artifacts', '__pycache__'}
EXTENSIONS = {'.cs', '.csproj', '.props', '.targets', '.sln', '.py', '.ps1', '.sh', '.cmd'}
ROOT_FILES = {'VERSION', 'global.json', 'NuGet.Config', '.editorconfig'}
EXPECTED_STEPS = {'fixtures', 'roslyn-compilation', 'LuckyStarPspToolkit.SelfTests',
                  'LuckyStarPspToolkit.Formats.SelfTests', 'LuckyStarPspToolkit.Documentation',
                  'managed-cli-self-test', 'managed-synthetic-demo'}


def execution_input_hashes(root: Path) -> dict[str, str]:
    """Hash source, build settings, generators and runners, excluding generated binaries and private material."""
    hashes: dict[str, str] = {}
    for path in sorted(root.rglob('*')):
        relative = path.relative_to(root)
        if EXCLUDED.intersection(relative.parts) or not path.is_file():
            continue
        # Restrict directories; private customer input must never be scanned or exported.
        if len(relative.parts) > 1 and relative.parts[0] not in {'src', 'tests', 'tools', 'scripts', 'validation'}:
            continue
        if path.suffix.lower() not in EXTENSIONS and relative.as_posix() not in ROOT_FILES:
            continue
        if path.is_symlink():
            raise ValueError(f'Execution input must not be a symbolic link: {relative}')
        hashes[relative.as_posix()] = hashlib.sha256(path.read_bytes()).hexdigest()
    return hashes


def validate_evidence(root: Path, report_path: Path) -> dict[str, object]:
    """Accept only successful, complete, current-source reports with intact subprocess logs and explicit limits.

    This verifies consistency and freshness, not a digital signature or remote CI attestation.
    The caller must continue to mark standard SDK compilation and native publication separately.
    """
    report = json.loads(report_path.read_text(encoding='utf-8'))
    if report.get('status') != 'passed-experimental-managed-host':
        raise ValueError('The managed-host verification did not finish successfully.')
    if report.get('version') != (root / 'VERSION').read_text(encoding='utf-8').strip():
        raise ValueError('Managed-host report version does not match VERSION.')
    if report.get('inputHashes') != execution_input_hashes(root):
        raise ValueError('Managed-host report is stale: source or execution scripts changed.')
    for field in ('standardNet9Validation', 'nativeWindowsVerified', 'gameRuntimeVerified', 'binaryReleaseProduced'):
        if report.get(field) is not False:
            raise ValueError(f'The fallback cannot attest {field}.')
    steps = report.get('steps', [])
    if len(steps) != len(EXPECTED_STEPS) or {item.get('name') for item in steps} != EXPECTED_STEPS:
        raise ValueError('Incomplete managed-host step set.')
    for step in steps:
        if step.get('exitCode') != 0 or step.get('passed') is not True:
            raise ValueError(f'Managed-host step did not pass: {step.get("name")}')
        name = step.get('log', '')
        if not name or Path(name).name != name or '/' in name or '\\' in name:
            raise ValueError('Unsafe managed-host log path.')
        log = report_path.parent / name
        if log.is_symlink() or not log.is_file() or hashlib.sha256(log.read_bytes()).hexdigest() != report.get('logHashes', {}).get(name):
            raise ValueError(f'Managed-host log is missing or modified: {name}')
    compilation = report.get('compilation', {})
    assemblies = compilation.get('assemblies', [])
    if compilation.get('success') is not True or len(assemblies) != 6 or any(a.get('success') is not True for a in assemblies):
        raise ValueError('Six successful compilations are required.')
    if compilation.get('standardDotnetBuild') is not False or compilation.get('standardNet9Validation') is not False:
        raise ValueError('Fallback compilation must not masquerade as dotnet build.')
    return report
