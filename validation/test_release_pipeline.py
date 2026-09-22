"""Regression tests for release orchestration; these tests DO NOT compile or execute C#."""
from __future__ import annotations
import importlib.util
import json
from pathlib import Path
import os
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch
import zipfile

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'scripts'))
import build_release as release  # noqa: E402
from managed_evidence import execution_input_hashes, validate_evidence, EXPECTED_STEPS  # noqa: E402
from package_release import audit_history  # noqa: E402
from document_members import has_summary, member_lines  # noqa: E402


class ReleasePipelineTests(unittest.TestCase):
    """Verify fail-fast execution, preservation of old artifacts and honest platform status."""

    def test_api_catalog_uses_same_ordinal_order_on_every_host(self) -> None:
        """Mixed-case source names must not make the checked-in API stale only on Windows."""
        import generate_api_reference as api
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            (root / 'src').mkdir()
            for name in ('CrilaylaCodec', 'CriUtfCodec'):
                (root / 'src' / (name + '.cs')).write_text(
                    '/// <summary>Fixture.</summary>\npublic class ' + name + ' {}\n', encoding='utf-8')
            with patch.object(api, 'ROOT', root):
                rendered, _, _, _ = api.render_reference()
            self.assertLess(rendered.index('src/CriUtfCodec.cs'), rendered.index('src/CrilaylaCodec.cs'))

    def test_owner_cross_publish_keeps_linux_execute_permissions(self) -> None:
        """Linux owner executables built on Windows must launch after ZIP extraction."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            folder = root / 'input'
            folder.mkdir()
            for name in ('lsp-license-server', 'lsp-license-admin'):
                (folder / name).write_bytes(b'\x7fELF-test')
            release.package_directory(folder, root / 'owner.zip')
            with zipfile.ZipFile(root / 'owner.zip') as archive:
                for name in ('lsp-license-server', 'lsp-license-admin'):
                    self.assertEqual(0o100755, archive.getinfo(name).external_attr >> 16)

    def test_source_package_rejects_assets_and_traversal(self) -> None:
        """No executable, game binary, font file or unsafe path can enter the source snapshot."""
        from package_release import check_source_name
        for name in ('font.ttf', 'test.BDF', 'assets/lt.bin', 'fixture.iso', 'tool.exe',
                     '../escape', '/absolute', 'C:\\escape', 'a//b', 'old.bundle'):
            with self.assertRaises(ValueError, msg=name):
                check_source_name(name)
        self.assertEqual(Path('src/Tool.cs'), check_source_name('src/Tool.cs'))

    def test_demo_expects_invalid_input_code(self) -> None:
        """The scripted negative scenario uses the CLI's actual invalid-input code, not incomplete-assets code."""
        text = (ROOT / 'scripts/demo_customer.py').read_text()
        self.assertIn("rejected, expected=3", text)
        cli = (ROOT / 'src/LuckyStarPspToolkit.Cli/CommandApplication.cs').read_text()
        self.assertIn('ExitInvalidInput = 3;', cli)

    def test_demo_expects_incomplete_font_code(self) -> None:
        """A blank Russian fixture is valid but incomplete and must not fail the release demonstration."""
        text = (ROOT / 'scripts/demo_customer.py').read_text()
        self.assertIn("output / 'font.json', expected=2", text)

    def test_summary_after_attribute_is_not_attached(self) -> None:
        """Match the compiler: documentation placed after an attribute cannot document the member."""
        self.assertFalse(has_summary(['[JsonIgnore]', '/// <summary>Too late</summary>', 'public int Value { get; }'], 2))
        self.assertTrue(has_summary(['/// <summary>Correct</summary>', '[JsonIgnore]', 'public int Value { get; }'], 2))

    def test_windows_and_linux_use_shared_driver(self) -> None:
        """Workflows cannot bypass the checked native release driver with a masked command list."""
        for name in ('ci.yml', 'release.yml'):
            text = (ROOT / '.github/workflows' / name).read_text()
            self.assertIn('python scripts/build_release.py --rids', text)
            self.assertNotIn('dotnet publish', text)
        release_workflow = (ROOT / '.github/workflows/release.yml').read_text()
        self.assertIn('--repo "$GITHUB_REPOSITORY"', release_workflow)
        self.assertIn('--draft --prerelease', release_workflow)

    def test_missing_sdk_real_driver_fails(self) -> None:
        """A real driver invocation with an intentionally empty PATH must not announce a built release."""
        result = subprocess.run([sys.executable, str(ROOT / 'scripts/build_release.py'), '--rids', 'win-x64'],
                                cwd=ROOT, env={**os.environ, 'PATH': ''}, capture_output=True, text=True, check=False)
        self.assertEqual(2, result.returncode)
        self.assertIn('No binary release was created', result.stderr)

    def test_source_shell_script_stays_executable_in_zip(self) -> None:
        """Source handoffs preserve shell-script launchability even when packaged on a Windows host."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp); folder = root / 'source'; folder.mkdir()
            (folder / 'build.sh').write_text('#!/bin/sh\nexit 0\n')
            release.package_directory(folder, root / 'source.zip')
            with zipfile.ZipFile(root / 'source.zip') as archive:
                self.assertEqual(0o100755, archive.getinfo('build.sh').external_attr >> 16)

    def test_publish_performs_rid_restore(self) -> None:
        """Self-contained publish must not reuse a platform-neutral restore with --no-restore."""
        cmd = release.publish_command('dotnet', 'win-x64', Path('out'))
        self.assertNotIn('--no-restore', cmd)
        self.assertIn('win-x64', cmd)
        self.assertIn('-p:PublishTrimmed=false', cmd)

    def test_reject_unknown_runtime(self) -> None:
        """Unexpected RIDs cannot become arbitrary directory names or command arguments."""
        for value in ('', '../bad', 'win-x64,../bad', 'linux-x64;bad'):
            with self.assertRaises(Exception):
                release.parse_rids(value)
        self.assertEqual(['win-x64'], release.parse_rids('win-x64,win-x64'))

    def test_real_command_failure(self) -> None:
        """A real child-process exit failure raises immediately and is retained in a log."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            runner = release.CommandRunner(root, root / 'logs')
            with self.assertRaises(release.BuildError):
                runner.run('failure', [sys.executable, '-c', 'print("before failure");raise SystemExit(7)'])
            self.assertEqual(7, runner.steps[0]['exitCode'])
            self.assertFalse(runner.steps[0]['passed'])
            self.assertIn('before failure', (root / 'logs/01-failure.log').read_text())

    def test_real_command_success(self) -> None:
        """Successful child-process output is recorded rather than inferred from an expected file."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            runner = release.CommandRunner(root, root / 'logs')
            self.assertEqual('ok', runner.run('success', [sys.executable, '-c', 'print("ok")']).strip())
            self.assertTrue(runner.steps[0]['passed'])

    def test_missing_executable(self) -> None:
        """A missing tool cannot be reported as a successful skipped step."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            runner = release.CommandRunner(root, root / 'logs')
            with self.assertRaises(release.BuildError):
                runner.run('missing', [str(root / 'no-such-program')])
            self.assertEqual(-1, runner.steps[0]['exitCode'])

    def test_zip_preserves_executable_bit(self) -> None:
        """Linux executable permissions survive packaging and archived names stay relative."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp); folder = root / 'input'; folder.mkdir()
            (folder / 'lsptool').write_bytes(b'\x7fELF-test')
            (folder / 'README.md').write_text('test')
            release.package_directory(folder, root / 'a.zip')
            release.package_directory(folder, root / 'b.zip')
            self.assertEqual(release.digest(root / 'a.zip'), release.digest(root / 'b.zip'))
            with zipfile.ZipFile(root / 'a.zip') as archive:
                self.assertEqual(0o100755, archive.getinfo('lsptool').external_attr >> 16)
                self.assertEqual(['README.md', 'lsptool'], archive.namelist())

    def test_promotion_replaces_only_after_staging(self) -> None:
        """A complete staged directory replaces the prior release as a unit."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp); old = root / 'release'; stage = root / 'stage'
            old.mkdir(); stage.mkdir()
            (old / 'old.txt').write_text('old'); (stage / 'new.txt').write_text('new')
            release.promote_directory(stage, old)
            self.assertFalse((old / 'old.txt').exists())
            self.assertEqual('new', (old / 'new.txt').read_text())
            self.assertFalse(stage.exists())

    def test_promotion_rollback(self) -> None:
        """A failed second rename restores the entire previous release directory."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp); old = root / 'release'; stage = root / 'stage'
            old.mkdir(); stage.mkdir(); (old / 'sentinel').write_text('keep')
            original = Path.rename
            def rename(path: Path, target: Path) -> Path:
                """Inject failure only when the staged directory is promoted."""
                if path == stage:
                    raise OSError('injected rename failure')
                return original(path, target)
            with patch.object(Path, 'rename', rename), self.assertRaises(OSError):
                release.promote_directory(stage, old)
            self.assertEqual('keep', (old / 'sentinel').read_text())

    def test_links_rejected(self) -> None:
        """Artifact writes do not traverse symbolic output links on platforms permitting their creation."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp); target = root / 'real'; target.mkdir(); link = root / 'link'
            try:
                link.symlink_to(target, target_is_directory=True)
            except OSError:
                self.skipTest('Symlink creation is not permitted for this Windows account.')
            with self.assertRaises(release.BuildError):
                release.ensure_regular_tree(link / 'out.zip')

    def test_compile_failure_keeps_previous_release(self) -> None:
        """A mocked compiler failure exercises orchestration only; publish must never start afterwards."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp); (root / 'VERSION').write_text('0.10.0')
            old = root / 'artifacts/releases/0.10.0'; old.mkdir(parents=True)
            (old / 'sentinel').write_text('keep')
            calls = []
            def fake_run(runner: release.CommandRunner, name: str, command: list[str]) -> str:
                """Simulate build-step outcomes without pretending to execute a C# compiler."""
                calls.append(name)
                if name == 'compile':
                    raise release.BuildError('injected compile failure')
                return '9.0.100' if name == 'sdk' else ''
            with patch.object(release.CommandRunner, 'run', fake_run), self.assertRaises(release.BuildError):
                release.build_release(root, ['win-x64'], 'dotnet')
            self.assertFalse(any(name.startswith('publish-') for name in calls))
            self.assertEqual('keep', (old / 'sentinel').read_text())
            report = json.loads(next((root / 'artifacts/build-logs').rglob('build-report.json')).read_text())
            self.assertEqual('failed', report['status'])
            self.assertFalse(report['compiled'])

    def test_model_properties_are_audited(self) -> None:
        """The offline documentation check includes properties that the old method-only check omitted."""
        text = 'public class Item\n{\n    public int Id { get; set; }\n}'
        self.assertIn((2, 'Id'), member_lines(text))
        self.assertFalse(has_summary(text.splitlines(), 2))


    def test_execution_fingerprint_tracks_scripts_and_source_only(self) -> None:
        """Evidence tracks code and runner changes, not generated artifacts, reports or private customer data."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            for name in ('VERSION', 'src/Thing.cs', 'scripts/demo_customer.py', 'artifacts/copy.cs', 'private/code.cs'):
                path = root / name
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_text('0.11.0')
            hashes = execution_input_hashes(root)
            self.assertEqual({'VERSION', 'src/Thing.cs', 'scripts/demo_customer.py'}, set(hashes))
            (root / 'src/Thing.cs').write_text('changed')
            self.assertNotEqual(hashes, execution_input_hashes(root))

    def test_managed_evidence_rejects_stale_source_or_missing_logs(self) -> None:
        """An old green status cannot certify new source; altering a subprocess log invalidates evidence."""
        import hashlib
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp); (root / 'VERSION').write_text('0.11.0')
            logs = root / 'artifacts/logs'; logs.mkdir(parents=True)
            steps = []; log_hashes = {}
            for index, name in enumerate(sorted(EXPECTED_STEPS)):
                log = logs / f'{index}.log'; log.write_text('PASS')
                log_hashes[log.name] = hashlib.sha256(log.read_bytes()).hexdigest()
                steps.append({'name': name, 'exitCode': 0, 'passed': True, 'log': log.name})
            report = {'version': '0.11.0', 'status': 'passed-experimental-managed-host',
                      'inputHashes': execution_input_hashes(root), 'logHashes': log_hashes, 'steps': steps,
                      'standardNet9Validation': False, 'nativeWindowsVerified': False,
                      'gameRuntimeVerified': False, 'binaryReleaseProduced': False,
                      'compilation': {'success': True, 'assemblies': [{'success': True}] * 11,
                                      'standardDotnetBuild': False, 'standardNet9Validation': False}}
            path = logs / 'report.json'; path.write_text(json.dumps(report))
            self.assertEqual('0.11.0', validate_evidence(root, path)['version'])
            (logs / '0.log').write_text('altered')
            with self.assertRaisesRegex(ValueError, 'missing or modified'):
                validate_evidence(root, path)
            (root / 'extra.props').write_text('<Project/>')
            with self.assertRaisesRegex(ValueError, 'stale'):
                validate_evidence(root, path)

    def test_history_audit_rejects_deleted_font(self) -> None:
        """Source packaging checks historical trees, not only HEAD, so deleted font files cannot leak in bundles."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            def git(*args: str) -> None:
                """Create a local isolated test repository without contacting a remote service."""
                subprocess.run(['git', *args], cwd=root, check=True, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
            git('init', '-b', 'main'); git('config', 'user.name', 'Test')
            git('config', 'user.email', 'test@example.invalid')
            (root / 'README.md').write_text('safe'); git('add', '.')
            git('commit', '-m', 'safe baseline')
            self.assertEqual(1, len(audit_history(root)))
            # Arbitrary test bytes, not an actual font, exercise the extension policy.
            (root / 'deleted.bdf').write_text('not a font'); git('add', '.')
            git('commit', '-m', 'forbidden historical extension')
            git('rm', 'deleted.bdf'); git('commit', '-m', 'remove forbidden file')
            with self.assertRaisesRegex(ValueError, 'source handoff'):
                audit_history(root)

    def test_json_writer_is_utf8(self) -> None:
        """Reports preserve Russian text and leave no temporary file after atomic replacement."""
        with tempfile.TemporaryDirectory() as temp:
            path = Path(temp) / 'report.json'
            release.write_json(path, {'text': 'Проверка'})
            self.assertEqual({'text': 'Проверка'}, json.loads(path.read_text(encoding='utf-8')))
            self.assertEqual([path], list(Path(temp).iterdir()))


from test_license_build import LicenseBuildTests  # Production issuer packaging is part of the release gate.


if __name__ == '__main__':
    unittest.main(verbosity=2)
