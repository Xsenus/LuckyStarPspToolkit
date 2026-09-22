#!/usr/bin/env python3
"""Run an isolated real server/owner/customer licensing lifecycle with fresh throwaway keys.

No bypass exists in the customer executable. A second test executable embeds an
explicit loopback-only test issuer, obtains an actual signed online lease, then
runs the original synthetic game-tool demonstration. Private test files are
removed in finally; only redacted process logs and explicit acceptance metadata
are retained. HTTPS deployment and real games are NOT attested by this test.
"""
from __future__ import annotations
import argparse
import json
import os
from pathlib import Path
import secrets
import shutil
import signal
import socket
import subprocess
import sys
import time
import urllib.request
import uuid

ROOT = Path(__file__).resolve().parents[1]


def port() -> int:
    """Reserve a currently free loopback test port, then release it before starting the service."""
    with socket.socket() as sock:
        sock.bind(('127.0.0.1', 0))
        return sock.getsockname()[1]


def main() -> int:
    """Execute actual processes, retain only redacted evidence, and return failure on any unexpected access decision."""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--assemblies', type=Path)
    parser.add_argument('--pwsh', type=Path)
    parser.add_argument('--dotnet', default='dotnet')
    parser.add_argument('--output', type=Path)
    args = parser.parse_args()
    output = (args.output or ROOT / 'artifacts/licensing' / uuid.uuid4().hex).resolve()
    if not output.is_relative_to((ROOT / 'artifacts').resolve()):
        raise ValueError('Integration output must be within repository artifacts.')
    output.mkdir(parents=True, exist_ok=False)
    private = output / 'private'
    private.mkdir(mode=0o700)
    password_file = private / 'master.pass'
    password_file.write_text(secrets.token_urlsafe(32), encoding='utf-8')
    password_file.chmod(0o600)
    state = private / 'authority'
    client_output = output / 'test-client'
    steps: list[dict[str, object]] = []
    report = {'schema': 'lsptool.license-process-integration.v1', 'status': 'running',
              'version': (ROOT / 'VERSION').read_text().strip(), 'steps': steps,
              'loopbackHttp': True, 'publicHttpsDeploymentTested': False,
              'nativeWindowsCngTested': os.name == 'nt' and args.pwsh is None,
              'standardNet9Build': args.pwsh is None, 'realGameTested': False}
    environment = dict(os.environ)
    if os.name != 'nt':
        (private / 'local-data').mkdir(mode=0o700)
        environment['XDG_DATA_HOME'] = str(private / 'local-data')
        # Do not change HOME: PowerShell and .NET host dependency resolution must remain reproducible.
    server: subprocess.Popen[str] | None = None
    server_log = None
    known_keys: list[str] = []

    def command(role: str, *, test_client: bool = False) -> list[str]:
        """Resolve an owner/server/test client assembly without using a global install."""
        if test_client:
            assembly = client_output / 'lsptool.dll'
        elif args.assemblies:
            assembly = args.assemblies.resolve() / (role + '.dll')
        else:
            project = {'lsptool': 'LuckyStarPspToolkit.Cli', 'lsp-license-admin': 'LuckyStarPspToolkit.LicenseAdmin',
                       'lsp-license-server': 'LuckyStarPspToolkit.LicenseServer'}[role]
            assembly = ROOT / 'src' / project / 'bin/Release/net9.0' / (role + '.dll')
        return ([str(args.pwsh), '-NoProfile', '-NoLogo', '-File', str(ROOT / 'scripts/run_managed.ps1'), str(assembly)]
                if args.pwsh else [args.dotnet, str(assembly)])

    def run(name: str, cmd: list[str], expected: int = 0, timeout: int = 180) -> str:
        """Capture one process's real exit code without recording raw credential values."""
        print('[' + name + ']', flush=True)
        result = subprocess.run(cmd, cwd=ROOT, env=environment, stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
                                text=True, encoding='utf-8', errors='replace', timeout=timeout)
        log = f'{len(steps) + 1:02d}-{name}.log'
        text = result.stdout
        for key in known_keys:
            if key in text:
                raise RuntimeError('A subprocess leaked an activation key.')
        (output / log).write_text(text, encoding='utf-8')
        steps.append({'name': name, 'exitCode': result.returncode, 'expectedExitCode': expected,
                      'passed': result.returncode == expected, 'log': log})
        if result.returncode != expected:
            raise RuntimeError(f'{name}: expected {expected}, got {result.returncode}: {text[-1500:]}')
        return text

    def start_server(public_port: int, admin_port: int) -> subprocess.Popen[str]:
        """Start a fresh server process and wait for its real loopback health endpoint."""
        nonlocal server_log
        server_log = (output / ('server-' + uuid.uuid4().hex[:8] + '.log')).open('w', encoding='utf-8')
        process = subprocess.Popen([*command('lsp-license-server'), '--data', str(state), '--password-file', str(password_file),
            '--public-port', str(public_port), '--admin-port', str(admin_port)], cwd=ROOT, env=environment,
            stdout=server_log, stderr=subprocess.STDOUT, text=True)
        deadline = time.monotonic() + 20
        while time.monotonic() < deadline:
            if process.poll() is not None:
                raise RuntimeError('License server did not remain running.')
            try:
                with urllib.request.urlopen(f'http://127.0.0.1:{public_port}/health', timeout=1) as response:
                    if response.status == 200:
                        return process
            except (OSError, ValueError):
                time.sleep(0.1)
        process.terminate()
        process.wait(timeout=10)
        raise RuntimeError('License server failed its startup health check.')

    def stop_server() -> None:
        """Stop only the child started by this regression, with bounded cleanup."""
        nonlocal server, server_log
        if server is not None and server.poll() is None:
            server.terminate()
            try:
                server.wait(timeout=15)
            except subprocess.TimeoutExpired:
                server.kill()
                server.wait(timeout=10)
        server = None
        if server_log:
            server_log.close()
            server_log = None

    try:
        public_port, admin_port = port(), port()
        while public_port == admin_port:
            admin_port = port()
        run('unconfigured-block', [*command('lsptool'), 'cpk-list', 'missing-input.cpk'], 77)
        run('owner-initialize', [*command('lsp-license-admin'), 'init', '--data', str(state), '--url',
            f'http://127.0.0.1:{public_port}/', '--password-file', str(password_file), '--development-loopback', '--lease-seconds', '15'])
        connection = state / 'owner-connection.json'
        config = json.loads(connection.read_text())
        config['adminUrl'] = f'http://127.0.0.1:{admin_port}/'
        connection.write_text(json.dumps(config), encoding='utf-8')
        server = start_server(public_port, admin_port)
        trust = state / 'client-trust.json'
        if args.pwsh:
            run('compile-licensed-test-client', [str(args.pwsh), '-NoProfile', '-NoLogo', '-File',
                'scripts/compile_with_roslyn.ps1', '-Root', str(ROOT), '-Out', str(client_output), '-LicenseTrustFile', str(trust)])
        else:
            run('compile-licensed-test-client', [args.dotnet, 'build', 'src/LuckyStarPspToolkit.Cli', '-c', 'Release',
                '--nologo', '-p:LicenseTrustFile=' + str(trust), '-o', str(client_output)])
        cli = command('lsptool', test_client=True)
        run('no-activation-block', [*cli, 'self-test'], 77)
        bad = private / 'invalid-key.txt'
        bad.write_text('LSP-' + secrets.token_urlsafe(32), encoding='ascii')
        run('invalid-key-block', [*cli, 'license', 'activate', '--key-file', str(bad)], 77)
        trial = private / 'trial-key.txt'
        owner = command('lsp-license-admin')
        run('issue-hour-trial', [*owner, 'issue', '--connection', str(connection), '--hours', '1', '--starts', 'activation',
                                '--devices', '1', '--label', 'synthetic-demo', '--out', str(trial)])
        known_keys.append(trial.read_text().strip())
        receipt = json.loads(Path(str(trial) + '.receipt.json').read_text())
        license_id = receipt['id']
        run('retry-issuance', [*owner, 'retry-issue', '--connection', str(connection), '--request',
                             str(trial) + '.issue-request.json', '--out', str(trial)])
        run('activate-customer-cli', [*cli, 'license', 'activate', '--key-file', str(trial)])
        status = json.loads(run('online-status', [*cli, 'license', 'status']).strip())
        if status['licenseId'] != license_id or status['permanent'] is not False:
            raise RuntimeError('Wrong activated license state.')
        run('licensed-self-test', [*cli, 'self-test'])
        demo = [sys.executable, 'scripts/demo_customer.py', '--cli', str(client_output / 'lsptool.dll'), '--output', str(output / 'demo')]
        if args.pwsh:
            demo += ['--powershell-host', str(args.pwsh)]
        run('licensed-original-synthetic-demo', demo)
        report['demo'] = json.loads((output / 'demo/DEMO-REPORT.json').read_text())
        run('owner-suspend', [*owner, 'suspend', '--connection', str(connection), '--id', license_id])
        run('suspended-cli-block', [*cli, 'self-test'], 77)
        run('owner-resume', [*owner, 'resume', '--connection', str(connection), '--id', license_id])
        run('resumed-cli-works', [*cli, 'self-test'])
        run('owner-extend', [*owner, 'extend', '--connection', str(connection), '--id', license_id, '--days', '2'])
        run('owner-revoke', [*owner, 'revoke', '--connection', str(connection), '--id', license_id])
        run('revoked-cli-block', [*cli, 'self-test'], 77)
        permanent = private / 'permanent-key.txt'
        run('issue-permanent', [*owner, 'issue', '--connection', str(connection), '--permanent', '--out', str(permanent)])
        known_keys.append(permanent.read_text().strip())
        run('activate-permanent', [*cli, 'license', 'activate', '--key-file', str(permanent)])
        status = json.loads(run('permanent-online-status', [*cli, 'license', 'status']).strip())
        if status['permanent'] is not True or status['expiresUtc'] is not None:
            raise RuntimeError('Perpetual entitlement has an unexpected expiry.')
        stop_server()
        run('server-offline-block', [*cli, 'self-test'], 77)
        server = start_server(public_port, admin_port)
        run('restart-retains-activation', [*cli, 'self-test'])
        run('owner-audit-export', [*owner, 'audit', '--connection', str(connection), '--out', str(private / 'audit.json')])
        audit = (private / 'audit.json').read_text()
        if any(key in audit for key in known_keys):
            raise RuntimeError('Audit leaked a raw activation credential.')
        report['status'] = 'passed'
        print('PASS: real owner/server/customer lifecycle, offline refusal and original synthetic demo.')
        return 0
    except (OSError, ValueError, RuntimeError, subprocess.TimeoutExpired) as exc:
        report['status'] = 'failed'
        report['error'] = str(exc)
        print('LICENSING INTEGRATION FAILED: ' + str(exc), file=sys.stderr)
        return 1
    finally:
        stop_server()
        # Per-run keys are never included in release evidence, even on a failed run.
        shutil.rmtree(private, ignore_errors=True)
        report['privateTestCredentialsRemoved'] = not private.exists()
        (output / 'licensing-integration-report.json').write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')


if __name__ == '__main__':
    raise SystemExit(main())
