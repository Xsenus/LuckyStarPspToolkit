#!/usr/bin/env python3
"""Create one SOURCE handoff ZIP, with a clean publication Git snapshot and explicit test evidence.

Never package historical Git blobs, fonts, game files or prebuilt executables.
The publication bundle starts a NEW snapshot history; original development
commit IDs and a text-only diff are retained separately, not misrepresented.
"""
from __future__ import annotations
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import tempfile
import zipfile
import sys

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'scripts'))
from build_release import package_directory, digest, ensure_regular_tree, write_json  # noqa: E402

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


def snapshot_bundle(project: Path, bundle: Path, version: str, development_commit: str) -> str:
    """Bundle a clean source snapshot without importing past binary/font blobs from development history."""
    git('init', '-b', 'main', cwd=project)
    environment = {**os.environ, 'GIT_AUTHOR_NAME': 'LuckyStar Toolkit', 'GIT_AUTHOR_EMAIL': 'maintainer@example.invalid',
                   'GIT_COMMITTER_NAME': 'LuckyStar Toolkit', 'GIT_COMMITTER_EMAIL': 'maintainer@example.invalid',
                   'GIT_AUTHOR_DATE': '2026-09-21T00:00:00Z', 'GIT_COMMITTER_DATE': '2026-09-21T00:00:00Z'}
    git('add', '.', cwd=project)
    for script in sorted(project.rglob('*.sh')):
        git('update-index', '--chmod=+x', '--', script.relative_to(project).as_posix(), cwd=project)
    git('commit', '-m', f'Source snapshot {version}; development origin {development_commit}', cwd=project, environment=environment)
    commit = git('rev-parse', 'HEAD', cwd=project).decode().strip()
    git('bundle', 'create', str(bundle), '--all', cwd=project)
    git('bundle', 'verify', str(bundle), cwd=project)
    # Leave the source folder clean and usable without Git; the optional snapshot is separate.
    import shutil
    shutil.rmtree(project / '.git')
    return commit


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
    if report['status'] == 'failed':
        raise RuntimeError('Validation contains failures; source cannot be released.')
    if report['status'] != 'passed' and not allow_partial:
        raise RuntimeError('Use --allow-partial explicitly for an uncompiled source preview.')
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
        (reports / 'CHANGES_FROM_0.9.0.patch').write_bytes(git('diff', '--no-ext-diff', previous_tag, 'HEAD', '--', 'src', 'tests', 'scripts', 'tools', 'Directory.Build.props', 'NuGet.Config', '.github'))
        history = root / 'git'; history.mkdir()
        bundle = history / f'LuckyStarPspToolkit-{version}-publication-snapshot.bundle'
        snapshot = snapshot_bundle(project, bundle, version, development)
        (history / 'README_RU.txt').write_text(
            'Это новый чистый Git-снимок для публикации, ветка main. Не полная старая история.\n'
            'Старые бинарные/шрифтовые эталоны в bundle не переносятся.\n'
            'Исходный development commit: ' + development + '\n'
            'Snapshot commit: ' + snapshot + '\n'
            'Восстановление: git clone <этот.bundle> LuckyStarPspToolkit\n'
            'Удалите локальный bundle origin и добавьте свой URL. Тег создайте после зелёного CI.\n', encoding='utf-8')
        manifest = {'schema': 'lsptool.source-handoff.v2', 'version': version,
                    'developmentCommit': development, 'publicationSnapshotCommit': snapshot,
                    'publicationBundleIsFullDevelopmentHistory': False,
                    'validationStatus': report['status'], 'compiledBinariesIncluded': False,
                    'fontFilesIncluded': False, 'customerFilesIncluded': False,
                    'sourceFiles': files, 'snapshotBundleSha256': digest(bundle)}
        write_json(root / 'MANIFEST.json', manifest)
        (root / 'START_HERE_RU.md').write_text(
            '# Полный исходный комплект 0.10.0\n\n'
            'Начните с project/START_HERE_RU.md и project/docs/USAGE_RU.md.\n'
            'C# в среде подготовки НЕ скомпилирован; готового EXE в архиве нет.\n'
            'Проверки, реально выполненные здесь: reports/validation-summary.json.\n'
            'Сборка на Windows: из project выполнить build.cmd --rids win-x64.\n'
            'После успешной сборки доступны ZIP и EXE в project/artifacts/releases/0.10.0.\n'
            'Сценарий показа: project/docs/CUSTOMER_DEMO_RU.md.\n'
            'GitHub: project/docs/GITHUB_PUBLISH_RU.md. Правовой обзор: THIRD_PARTY_NOTICES.md.\n'
            'Git bundle в git/ — очищенный снимок, а не полная старая история.\n', encoding='utf-8')
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
    parser.add_argument('--previous-tag', default='v0.9.0')
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
