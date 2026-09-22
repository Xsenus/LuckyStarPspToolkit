#!/usr/bin/env python3
"""Create one source handoff ZIP with audited Git lineage and explicit execution evidence.

Every reachable Git tree is checked before bundling: deleted font/game/binary
assets are forbidden too. History starts at the supplied clean 0.10.0 snapshot;
no earlier private development history is reconstructed or invented.
"""
from __future__ import annotations
import argparse
import hashlib
import json
from pathlib import Path
import re
import subprocess
import tempfile
import zipfile
import sys

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'scripts'))
from build_release import package_directory, digest, ensure_regular_tree, write_json  # noqa: E402
from managed_evidence import execution_input_hashes, validate_evidence  # noqa: E402

FORBIDDEN = {'.exe', '.dll', '.pdb', '.so', '.dylib', '.bin', '.cpk', '.iso', '.cso',
             '.pmf', '.sfo', '.prx', '.elf', '.zip', '.bundle', '.7z', '.rar', '.ttf', '.otf', '.bdf', '.woff', '.woff2', '.rgba'}


def git(*arguments: str, cwd: Path = ROOT, environment: dict[str, str] | None = None) -> bytes:
    """Run one checked Git command and return its byte output without shell interpolation."""
    process = subprocess.run(['git', *arguments], cwd=cwd, env=environment,
                             stdout=subprocess.PIPE, stderr=subprocess.PIPE, check=False)
    if process.returncode:
        raise RuntimeError(process.stderr.decode('utf-8', 'replace'))
    return process.stdout


def safe_relative(name: str) -> Path:
    """Reject absolute, Windows-drive, traversal, empty-component and link-like archive paths."""
    parts = name.replace('\\', '/').split('/')
    if not parts or any(part in {'', '.', '..'} or ':' in part for part in parts):
        raise ValueError(f'Unsafe archive path: {name}')
    return Path(*parts)


def check_source_name(name: str) -> Path:
    """Permit source/documentation only; even generated font or game-like binary fixtures are excluded."""
    relative = safe_relative(name)
    if relative.suffix.lower() in FORBIDDEN or any(part in {'.git', 'bin', 'obj', 'private', 'assets', 'artifacts'} for part in relative.parts):
        raise ValueError(f'Not permitted in a source handoff: {name}')
    return relative


def export_source(destination: Path) -> dict[str, str]:
    """Export the exact committed source tree, rejecting symlinks and forbidden asset types."""
    files: dict[str, str] = {}
    entries = git('ls-tree', '-r', '-z', 'HEAD').split(b'\0')
    for entry in entries:
        if not entry:
            continue
        descriptor, raw_name = entry.split(b'\t', 1)
        mode, kind, blob = descriptor.decode('ascii').split()
        name = raw_name.decode('utf-8')
        relative = check_source_name(name)
        if kind != 'blob' or mode not in {'100644', '100755'}:
            raise ValueError(f'Non-regular Git entry: {name}')
        data = git('cat-file', 'blob', blob)
        path = destination / relative
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(data)
        path.chmod(0o755 if mode == '100755' else 0o644)
        files[name] = hashlib.sha256(data).hexdigest()
    return files


def audit_history(repository: Path) -> list[str]:
    """Reject forbidden or linked entries in every reachable commit, including files deleted later."""
    commits = git('rev-list', '--reverse', '--all', cwd=repository).decode('ascii').splitlines()
    if not commits:
        raise ValueError('Cannot package empty Git history.')
    for commit in commits:
        for entry in git('ls-tree', '-r', '-z', commit, cwd=repository).split(b'\0'):
            if not entry:
                continue
            descriptor, name = entry.split(b'\t', 1)
            mode, kind, _ = descriptor.decode('ascii').split()
            check_source_name(name.decode('utf-8'))
            if kind != 'blob' or mode not in {'100644', '100755'}:
                raise ValueError(f'Non-regular historical entry in {commit}: {name!r}')
    return commits


def history_bundle(repository: Path, bundle: Path, version: str, commit: str) -> list[str]:
    """Preserve verified supplied lineage instead of resetting history for each new release."""
    commits = audit_history(repository)
    if git('rev-parse', 'main', cwd=repository).decode().strip() != commit:
        raise ValueError('main must point to the reviewed release commit.')
    if git('rev-parse', f'v{version}^{{commit}}', cwd=repository).decode().strip() != commit:
        raise ValueError('Release tag must point to the reviewed release commit.')
    git('bundle', 'create', str(bundle), '--all', cwd=repository)
    git('bundle', 'verify', str(bundle), cwd=repository)
    return commits


def package(output: Path, previous_tag: str, allow_partial: bool) -> Path:
    """Package source plus truthful validation reports; refuse failures or unacknowledged missing checks."""
    version = (ROOT / 'VERSION').read_text().strip()
    if not re.fullmatch(r'\d+\.\d+\.\d+', version):
        raise ValueError('Invalid VERSION')
    if git('status', '--porcelain').strip():
        raise RuntimeError('Commit reviewed changes before packaging.')
    development = git('rev-parse', 'HEAD').decode().strip()
    report_path = ROOT / 'artifacts/validation/validation-summary.json'
    report = json.loads(report_path.read_text(encoding='utf-8'))
    if report.get('version') != version or report.get('inputHashes') != execution_input_hashes(ROOT):
        raise RuntimeError('Validation does not match this source/version; run the gate again.')
    if report['status'] == 'failed':
        raise RuntimeError('Validation contains failures; source cannot be released.')
    if report['status'] != 'passed' and not allow_partial:
        raise RuntimeError('Use --allow-partial explicitly for a source preview with incomplete native/game validation.')
    output = output.resolve()
    ensure_regular_tree(output)
    output.mkdir(parents=True, exist_ok=True)
    name = f'LuckyStarPspToolkit_CSharp_{version}-ALL_IN_ONE.zip'
    with tempfile.TemporaryDirectory(prefix='lsp-source-') as temp:
        root = Path(temp)
        project = root / 'project'
        project.mkdir()
        files = export_source(project)
        reports = root / 'reports'; reports.mkdir()
        for path in sorted((ROOT / 'artifacts/validation').rglob('*')):
            if not path.is_file() or path.suffix.lower() not in {'.json', '.txt', '.log', '.md'}:
                continue
            relative = path.relative_to(ROOT / 'artifacts/validation')
            target = reports / relative; target.parent.mkdir(parents=True, exist_ok=True)
            target.write_bytes(path.read_bytes())
        (reports / 'DEVELOPMENT_HISTORY.txt').write_bytes(git('log', '--format=%H %s', '--reverse', 'HEAD'))
        previous_version = git('show', f'{previous_tag}:VERSION').decode().strip()
        if not re.fullmatch(r'\d+\.\d+\.\d+', previous_version):
            raise ValueError('Invalid previous version')
        (reports / f'CHANGES_FROM_{previous_version}.patch').write_bytes(git('diff', '--no-ext-diff', previous_tag, 'HEAD', '--', 'src', 'tests', 'scripts', 'tools', 'Directory.Build.props', 'NuGet.Config', '.github'))
        history = root / 'git'; history.mkdir()
        bundle = history / f'LuckyStarPspToolkit-{version}.git.bundle'
        commits = history_bundle(ROOT, bundle, version, development)
        (history / 'README_RU.txt').write_text(
            'История продолжена от переданного чистого Git-снимка 0.10.0, без сброса.\n'
            'Это не восстановление прежней приватной истории 0.1–0.9.\n'
            'Все исторические деревья проверены на запрещённые шрифтовые/игровые/бинарные файлы.\n'
            'Commit: ' + development + '\n'
            'Восстановление: git clone <этот.git.bundle> LuckyStarPspToolkit\n'
            'Ветка main и тег v' + version + ' указывают на эту поставку.\n'
            'Сначала push main и проверка Windows/Linux CI; затем push существующего тега.\n', encoding='utf-8')
        manifest = {'schema': 'lsptool.source-handoff.v3', 'version': version,
                    'developmentCommit': development, 'publicationCommit': development,
                    'historyScope': 'Supplied clean 0.10.0 snapshot and its actual descendants only',
                    'publicationBundleIsFullDevelopmentHistory': False, 'preservesSuppliedHistory': True,
                    'historyCommits': commits,
                    'validationStatus': report['status'], 'compiledBinariesIncluded': False,
                    'fontFilesIncluded': False, 'customerFilesIncluded': False,
                    'ownerOnly': True, 'licensePolicy': 'strict-online', 'signingCredentialsIncluded': False,
                    'sourceFiles': files, 'gitBundleSha256': digest(bundle)}
        write_json(root / 'MANIFEST.json', manifest)
        managed_path = ROOT / 'artifacts/validation/managed-fallback/managed-fallback-report.json'
        managed_line = 'Дополнительный прогон C# в альтернативной среде в этой поставке не зафиксирован.\n'
        if managed_path.is_file():
            validate_evidence(ROOT, managed_path)
            managed_line = ('Реальный C# скомпилирован через Roslyn и протестирован на альтернативном .NET-хосте.\n'
                            'Это НЕ .NET 9 SDK build и НЕ проверка Windows/PSP.\n')
        (root / 'START_HERE_RU.md').write_text(
            f'# Полный исходный комплект {version}\n\n'
            'КОМПЛЕКТ ВЛАДЕЛЬЦА: не отправлять исходники, Git bundle и инструменты выдачи ключей клиенту.\n'
            'Начните с project/START_HERE_RU.md и project/docs/LICENSE_OWNER_RU.md.\n'
            + managed_line +
            'Готовых EXE/DLL, чужого runtime/компилятора, игровых и шрифтовых файлов нет.\n'
            'Проверки: reports/validation-summary.json и reports/managed-fallback/.\n'
            'Клиентская сборка: build.cmd --rids win-x64 --license-trust <публичный client-trust.json>.\n'
            'Без публичного издателя программа остаётся заблокированной. Секреты создаются владельцем отдельно.\n'
            f'После УСПЕШНОЙ native-сборки ZIP и EXE: project/artifacts/releases/{version}.\n'
            'Показ заказчику: project/docs/CUSTOMER_DEMO_RU.md.\n'
            'GitHub: project/docs/GITHUB_PUBLISH_RU.md. Происхождение: project/THIRD_PARTY_NOTICES.md.\n'
            'Git bundle сохраняет переданную историю от чистого снимка 0.10.0 и новые коммиты.\n', encoding='utf-8')
        checksum_lines = [f'{digest(p)}  {p.relative_to(root).as_posix()}\n' for p in sorted(root.rglob('*')) if p.is_file()]
        (root / 'CHECKSUMS.sha256').write_text(''.join(checksum_lines), encoding='utf-8')
        package_directory(root, output / name)
    archive = output / name
    archive.with_suffix('.zip.sha256').write_text(f'{digest(archive)}  {archive.name}\n', encoding='ascii')
    print(archive)
    return archive


def main() -> int:
    """Parse source-handoff options; report errors without creating a fake binary release."""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output-dir', required=True, type=Path)
    parser.add_argument('--previous-tag', default='5d4a4f0')
    parser.add_argument('--allow-partial', action='store_true')
    args = parser.parse_args()
    try:
        package(args.output_dir, args.previous_tag, args.allow_partial)
        return 0
    except Exception as exc:
        print(f'SOURCE PACKAGE FAILED: {exc}', file=sys.stderr)
        return 1


if __name__ == '__main__':
    raise SystemExit(main())
