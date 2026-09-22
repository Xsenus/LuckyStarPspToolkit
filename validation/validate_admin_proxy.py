#!/usr/bin/env python3
"""Exercise the admin Nginx template with temporary loopback TLS and a non-authenticating echo upstream.

Never edits system configuration or uses production secrets. TLS trusts only the freshly
created local certificate for this run. This is a proxy isolation check, not real domain,
browser cookie/CSP or authority deployment acceptance. Exit 2 means missing executables.
"""
from __future__ import annotations
import argparse
import hashlib
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import http.client
import json
from pathlib import Path
import shutil
import socket
import ssl
import subprocess
import tempfile
import threading
import time

ROOT = Path(__file__).resolve().parents[1]


class Echo(BaseHTTPRequestHandler):
    """Reply only to bounded synthetic loopback traffic; this is explicitly not the C# authority."""

    def do_POST(self) -> None:
        """Consume the two-byte fixture body and acknowledge that Nginx admitted it."""
        self.rfile.read(int(self.headers.get('Content-Length', '0')))
        self.do_GET()

    def do_GET(self) -> None:
        """Return a fixed response without credentials or licensing behavior."""
        body = b'{"syntheticProxyUpstream":true}'
        self.send_response(200)
        self.send_header('Content-Length', str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def log_message(self, format: str, *args: object) -> None:
        """Avoid retaining request bodies, headers or temporary system paths."""


def free_port() -> int:
    """Reserve a candidate loopback-only port; a concurrent bind failure aborts, never falls back to public binding."""
    with socket.socket() as sock:
        sock.bind(('127.0.0.1', 0))
        return sock.getsockname()[1]


def run_lane(template: str, snippet: str, saturated: str, other: str, nginx: str, openssl: str) -> dict:
    """Run one independent pair of ingress budgets in an isolated foreground Nginx instance."""
    with tempfile.TemporaryDirectory(prefix='lsp-proxy-check-') as folder:
        work = Path(folder)
        backend = ThreadingHTTPServer(('127.0.0.1', 0), Echo)
        thread = threading.Thread(target=backend.serve_forever, daemon=True)
        thread.start()
        process = None
        try:
            port = free_port(); redirect = free_port()
            cert, key = work / 'cert.pem', work / 'key.pem'
            subprocess.run([openssl, 'req', '-x509', '-newkey', 'rsa:2048', '-nodes', '-days', '1',
                            '-subj', '/CN=127.0.0.1', '-addext', 'subjectAltName=IP:127.0.0.1',
                            '-keyout', str(key), '-out', str(cert)], check=True,
                           stdout=subprocess.DEVNULL, stderr=subprocess.PIPE, timeout=15)
            snippet_path = work / 'proxy.conf'
            snippet_path.write_text(snippet.replace('127.0.0.1:17842', f'127.0.0.1:{backend.server_port}'))
            configured = template.replace('listen 80;', f'listen 127.0.0.1:{redirect};')
            configured = configured.replace('listen 443 ssl;', f'listen 127.0.0.1:{port} ssl;')
            configured = configured.replace('127.0.0.1:17842', f'127.0.0.1:{backend.server_port}')
            configured = configured.replace('/etc/letsencrypt/live/admin.example.com/fullchain.pem', str(cert))
            configured = configured.replace('/etc/letsencrypt/live/admin.example.com/privkey.pem', str(key))
            configured = configured.replace('/etc/nginx/snippets/lsp-admin-proxy.conf', str(snippet_path))
            config = work / 'nginx.conf'
            config.write_text(f'worker_processes 1;\npid {work}/nginx.pid;\nerror_log {work}/error.log;\n'
                              'events { worker_connections 64; }\nhttp {\n' + configured + '\n}\n')
            test = subprocess.run([nginx, '-p', str(work) + '/', '-c', str(config), '-t'],
                                  capture_output=True, text=True, timeout=10, check=True)
            process = subprocess.Popen([nginx, '-p', str(work) + '/', '-c', str(config), '-g', 'daemon off;'],
                                       stdout=subprocess.DEVNULL, stderr=subprocess.PIPE)
            deadline = time.monotonic() + 5
            while True:
                if process.poll() is not None:
                    raise RuntimeError('Temporary Nginx exited before accepting loopback connections.')
                try:
                    with socket.create_connection(('127.0.0.1', port), timeout=.1):
                        break
                except OSError:
                    if time.monotonic() > deadline:
                        raise TimeoutError('Temporary proxy did not start.')
                    time.sleep(.03)
            context = ssl.create_default_context(cafile=str(cert))
            connection = http.client.HTTPSConnection('127.0.0.1', port, context=context, timeout=5)
            try:
                def request(path: str, method: str = 'POST') -> int:
                    """Send synthetic JSON over verified local TLS and consume the response before reuse."""
                    connection.request(method, path, b'{}' if method == 'POST' else None,
                                       {'Content-Type': 'application/json'})
                    response = connection.getresponse(); response.read()
                    return response.status
                statuses = [request(saturated) for _ in range(9)]
                counterpart = request(other)
                ordinary = request('/api/session', 'GET')
            finally:
                connection.close()
            if 429 not in statuses or 200 not in statuses or ordinary != 200:
                raise AssertionError(f'Unexpected proxy behavior: {statuses}, {ordinary}')
            return {'saturatedPath': saturated, 'statuses': statuses, 'otherPath': other,
                    'otherStatus': counterpart, 'ordinaryReadStatus': ordinary, 'configurationTestPassed': test.returncode == 0}
        finally:
            if process is not None:
                process.terminate()
                try:
                    process.communicate(timeout=5)
                except subprocess.TimeoutExpired:
                    process.kill(); process.communicate()
            backend.shutdown(); backend.server_close(); thread.join(timeout=2)


def main() -> int:
    """Compare the current or an explicitly supplied historical template and record bounded observations."""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--template', type=Path, default=ROOT / 'deploy/licensing/nginx-admin.conf')
    parser.add_argument('--expect-shared', action='store_true', help='Only for reproducing the previous version.')
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    nginx, openssl = shutil.which('nginx'), shutil.which('openssl')
    if not nginx or not openssl:
        print('NOT RUN: Nginx/OpenSSL unavailable.'); return 2
    raw = args.template.read_bytes()
    snippet = (ROOT / 'deploy/licensing/lsp-admin-proxy.conf').read_text()
    observations = [run_lane(raw.decode(), snippet, a, b, nginx, openssl) for a, b in
                    [('/api/login', '/api/reauth'), ('/api/reauth', '/api/login')]]
    expected = 429 if args.expect_shared else 200
    if any(item['otherStatus'] != expected for item in observations):
        raise AssertionError('Observed proxy budget isolation differs from the requested expectation.')
    report = {'schema': 'lsptool.admin-proxy-isolation.v1', 'status': 'passed',
              'templateSha256': hashlib.sha256(raw).hexdigest(),
              'runnerSha256': hashlib.sha256(Path(__file__).read_bytes()).hexdigest(),
              'nginx': subprocess.run([nginx, '-v'], capture_output=True, text=True, check=True).stderr.strip(),
              'expectSharedBudget': args.expect_shared, 'observations': observations,
              'localTlsVerified': True, 'syntheticEchoUpstream': True,
              'productionDeploymentVerified': False, 'browserCookieCspVerified': False}
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(report, ensure_ascii=False, indent=2) + '\n')
    print(json.dumps(report, ensure_ascii=False, indent=2)); return 0


if __name__ == '__main__':
    raise SystemExit(main())
