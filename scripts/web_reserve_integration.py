#!/usr/bin/env python3
"""Run the real React console, private owner process and licensed CLI with disposable credentials.

Requires a real built frontend and Chromium. The optional PowerShell host records
experimental managed execution explicitly; it never asserts .NET 9/Windows/TLS
or actual gameplay acceptance. No screenshots include activation keys or MFA data.
"""
from __future__ import annotations
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import secrets
import shutil
import socket
import subprocess
import time
import urllib.request
import urllib.error
import http.cookiejar
import faulthandler
import signal
import uuid

ROOT = Path(__file__).resolve().parents[1]


def source_hashes() -> dict[str, str]:
    """Bind diagnostic evidence to actual C# inputs, local React modules and build outputs."""
    from managed_evidence import execution_input_hashes
    result = execution_input_hashes(ROOT)
    for file in sorted((ROOT / 'web/license-admin').rglob('*')):
        if file.is_file() and 'node_modules' not in file.parts:
            result[file.relative_to(ROOT).as_posix()] = hashlib.sha256(file.read_bytes()).hexdigest()
    return result


def free_port() -> int:
    """Allocate a candidate loopback port; collisions remain a startup failure rather than a silent fallback."""
    with socket.socket() as sock:
        sock.bind(('127.0.0.1', 0))
        return sock.getsockname()[1]


def main() -> int:
    """Execute visible browser interactions and offline access decisions, writing only redacted evidence."""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--assemblies', type=Path)
    parser.add_argument('--pwsh', type=Path)
    parser.add_argument('--dotnet', default='dotnet')
    parser.add_argument('--browser-bridge', action='store_true', help='Diagnostic only: render React locally and route its API through the isolated HTTP test client; does not test native browser network/cookie/CSP enforcement.')
    parser.add_argument('--chromium', default=shutil.which('chromium'))
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    out = args.output.resolve()
    if not out.is_relative_to((ROOT / 'artifacts').resolve()):
        raise ValueError('Evidence must be under repository artifacts.')
    out.mkdir(parents=True, exist_ok=False)
    private = out / 'private'
    private.mkdir(mode=0o700)
    password = secrets.token_urlsafe(32)
    password_file = private / 'owner.pass'
    password_file.write_text(password)
    password_file.chmod(0o600)
    state = private / 'authority'
    enrollment_path = private / 'web-enrollment.json'
    ports: set[int] = set()
    while len(ports) < 3:
        ports.add(free_port())
    public, admin, web = sorted(ports)
    origin = f'http://127.0.0.1:{web}'
    env = dict(os.environ)
    (private / 'app-data').mkdir(mode=0o700)
    if os.name != 'nt':
        env['XDG_DATA_HOME'] = str(private / 'app-data')
    steps: list[dict] = []
    report = dict(schema='lsptool.web-reserve-integration.v1', status='running', steps=steps,
                  version=(ROOT / 'VERSION').read_text().strip(), realBrowser=True,
                  loopbackHttp=True, productionHttpsTested=False, gameRuntimeTested=False,
                  standardNet9Build=args.pwsh is None, browser='Chromium',
                  nativeBrowserTransport=not args.browser_bridge, nativeDownloadTested=not args.browser_bridge,
                  reactProvenance=json.loads((ROOT / 'web/license-admin/vendor-manifest.json').read_text()), inputHashes=source_hashes())
    server = None
    server_log = None
    sensitive = [password]
    client = out / 'licensed-client'

    def record(name: str) -> None:
        """Mark a completed assertion group only after the actual operation succeeds."""
        steps.append(dict(name=name, passed=True))
        print('PASS', name, flush=True)

    def cmd(role: str, licensed: bool = False) -> list[str]:
        """Select locally built assemblies without trusting a global installed product binary."""
        if licensed:
            assembly = client / (role + '.dll')
        elif args.assemblies:
            assembly = args.assemblies.resolve() / (role + '.dll')
        else:
            project = {'lsptool': 'LuckyStarPspToolkit.Cli', 'lsp-license-admin': 'LuckyStarPspToolkit.LicenseAdmin',
                       'lsp-license-server': 'LuckyStarPspToolkit.LicenseServer'}[role]
            assembly = ROOT / 'src' / project / 'bin/Release/net9.0' / (role + '.dll')
        return ([str(args.pwsh), '-NoProfile', '-File', str(ROOT / 'scripts/run_managed.ps1'), str(assembly)]
                if args.pwsh else [args.dotnet, str(assembly)])

    def run(name: str, command: list[str], expected: int = 0, timeout: int = 180) -> str:
        """Capture process output after rejecting accidental exposure of a known plaintext credential."""
        result = subprocess.run(command, cwd=ROOT, env=env, capture_output=True, text=True, timeout=timeout)
        text = result.stdout + result.stderr
        if any(value and value in text for value in sensitive):
            raise RuntimeError('Credential appeared in subprocess output: ' + name)
        (out / (name + '.log')).write_text(text, encoding='utf-8')
        if result.returncode != expected:
            raise RuntimeError(f'{name}: expected exit {expected}, received {result.returncode}; inspect redacted log.')
        record(name)
        return text

    def start_server() -> None:
        """Start only this test's loopback backend; wait for its actual health response."""
        nonlocal server, server_log
        server_log = (out / f'server-{len(steps)}.log').open('w')
        server = subprocess.Popen([*cmd('lsp-license-server'), '--data', str(state), '--password-file', str(password_file),
            '--public-port', str(public), '--admin-port', str(admin), '--web-port', str(web),
            '--web-root', str(ROOT / 'web/license-admin/dist')], cwd=ROOT, env=env, stdout=server_log, stderr=subprocess.STDOUT)
        for _ in range(120):
            if server.poll() is not None:
                raise RuntimeError('Server exited during startup; inspect server log.')
            try:
                with urllib.request.urlopen(f'http://127.0.0.1:{public}/health', timeout=1) as response:
                    if response.status == 200:
                        record('real-server-start')
                        return
            except OSError:
                time.sleep(.1)
        raise RuntimeError('Server startup timeout')

    def stop_server() -> None:
        """Stop the owned child and bound cleanup; never touch another local process."""
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
        run('authority-init', [*cmd('lsp-license-admin'), 'init', '--data', str(state), '--url',
            f'http://127.0.0.1:{public}/', '--password-file', str(password_file), '--development-loopback', '--lease-seconds', '15'])
        run('web-owner-init', [*cmd('lsp-license-admin'), 'web-init', '--data', str(state), '--username', 'owner',
            '--password-file', str(password_file), '--origin', origin, '--out', str(enrollment_path), '--development-loopback'])
        enrollment = json.loads(enrollment_path.read_text())
        sensitive += [enrollment['totpSecret'], *enrollment['recoveryCodes']]
        compile_command = ([str(args.pwsh), '-NoProfile', '-File', 'scripts/compile_with_roslyn.ps1', '-Root', str(ROOT),
             '-Out', str(client), '-LicenseTrustFile', str(state / 'client-trust.json')] if args.pwsh else
             [args.dotnet, 'build', 'src/LuckyStarPspToolkit.Cli', '-c', 'Release', '--nologo',
              '-p:LicenseTrustFile=' + str(state / 'client-trust.json'), '-o', str(client)])
        run('compile-test-client', compile_command)
        start_server()
        from playwright.sync_api import sync_playwright, expect
        with sync_playwright() as playwright:
            browser = playwright.chromium.launch(executable_path=args.chromium, headless=True,
                args=['--no-sandbox', '--disable-dev-shm-usage'])
            context = browser.new_context(viewport={'width': 1366, 'height': 768}, accept_downloads=True)
            page = context.new_page()
            errors: list[str] = []
            page.on('pageerror', lambda error: errors.append(str(error)))
            page.on('dialog', lambda dialog: dialog.accept())
            jar = http.cookiejar.CookieJar()
            opener = urllib.request.build_opener(urllib.request.HTTPCookieProcessor(jar))

            def bridge_api(payload: dict) -> dict:
                """Test-only bounded transport to this fixture's actual backend, never an arbitrary external URL."""
                path = payload['path']
                if not re.fullmatch(r'/api/[a-z/-]+', path):
                    raise ValueError('Unknown test API path')
                options = payload.get('options', {})
                data = options.get('body')
                req = urllib.request.Request(origin + path, method=options.get('method', 'GET'),
                    headers={'Origin': origin, **options.get('headers', {})}, data=data.encode() if data is not None else None)
                try:
                    response = opener.open(req, timeout=15)
                except urllib.error.HTTPError as exc:
                    response = exc
                with response:
                    return dict(status=response.status, headers={k.lower():v for k,v in response.headers.items()}, body=response.read().decode())

            def navigate() -> None:
                """Use native navigation normally, or an explicitly reported local-render transport under browser policy restrictions."""
                if not args.browser_bridge:
                    response = page.goto(origin)
                    assert response is not None and "script-src 'self'" in response.headers['content-security-policy']
                    return
                print('render: blank', flush=True)
                page.goto('about:blank')
                page.set_content('<!doctype html><html lang="ru"><head><meta name="viewport" content="width=device-width, initial-scale=1"></head><body><div id="root"></div></body></html>')
                page.add_style_tag(path=str(ROOT / 'web/license-admin/dist/assets/app.css'))
                page.evaluate("""()=>{window.__downloads=[];window.fetch=async(path,options={})=>{const r=await window.__testApi({path,options});return new Response(r.body,{status:r.status,headers:r.headers});};const real=URL.createObjectURL;const blobs=new Map();URL.createObjectURL=b=>{const u=real(b);blobs.set(u,b);return u;};HTMLAnchorElement.prototype.click=function(){const b=blobs.get(this.href);if(b)b.text().then(text=>window.__downloads.push({name:this.download,text}));};if(!crypto.randomUUID)crypto.randomUUID=()=>{const b=crypto.getRandomValues(new Uint8Array(16));b[6]=(b[6]&15)|64;b[8]=(b[8]&63)|128;const t=Array.from(b,x=>x.toString(16).padStart(2,'0')).join('');return t.slice(0,8)+'-'+t.slice(8,12)+'-'+t.slice(12,16)+'-'+t.slice(16,20)+'-'+t.slice(20);};}""")
                vendor = (ROOT / 'web/license-admin/dist/assets/vendor.js').read_text().replace('export const React=', 'const React=').replace('export const createRoot=', 'const createRoot=')
                print('render: vendor', flush=True)
                page.add_script_tag(content='(function(){' + vendor + ';window.__lspVendor={React,createRoot};})()')
                app = re.sub(r"import\s+\{\s*React,\s*createRoot\s*\}\s+from\s+'\./vendor.js';", 'const {React,createRoot}=window.__lspVendor;', (ROOT / 'web/license-admin/dist/assets/app.js').read_text())
                print('render: app', flush=True)
                page.add_script_tag(content='(function(){' + app + '})()')
                response = context.request.get(origin)
                assert response.status == 200 and "script-src 'self'" in response.headers['content-security-policy']
            if args.browser_bridge:
                page.expose_function('__testApi', bridge_api)
            navigate()
            expect(page.get_by_role('heading', name='Управление лицензиями')).to_be_visible()
            page.screenshot(path=str(out / 'admin-login.png'), full_page=True)
            record('react-login-render-and-csp')

            def login(code: str) -> None:
                """Fill the real login form, never dumping its DOM or screenshot while secrets are present."""
                page.get_by_label('Логин', exact=True).fill('owner')
                page.get_by_label('Пароль', exact=True).fill(password)
                page.get_by_label('Код TOTP или код восстановления').fill(code)
                page.get_by_role('button', name='Войти', exact=True).click()
                expect(page.get_by_role('heading', name='Лицензии', exact=True)).to_be_visible(timeout=15000)

            login(enrollment['recoveryCodes'][0])
            if not args.browser_bridge:
                assert page.evaluate('document.cookie') == ''
            if args.browser_bridge:
                cookies = list(jar)
                assert len(cookies)==1 and cookies[0].has_nonstandard_attr('HttpOnly') and cookies[0].get_nonstandard_attr('SameSite')=='Strict'
            else:
                cookies = context.cookies()
                assert len(cookies) == 1 and cookies[0]['httpOnly'] and cookies[0]['sameSite'] == 'Strict'
            if not args.browser_bridge:
                assert page.evaluate('localStorage.length + sessionStorage.length') == 0
            record('mfa-login-httponly-cookie-no-web-storage')
            status = page.evaluate("""async()=> (await fetch('/api/reserve/policy',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({requestId:crypto.randomUUID(),enabled:true})})).status""")
            assert status == 403
            record('browser-mutation-without-csrf-blocked')
            assert context.request.post(origin + '/api/logout', headers={'Origin': 'https://attacker.invalid', 'Content-Type': 'application/json'}, data='{}').status == 403
            assert context.request.get(origin + '/admin/licenses').status == 404
            assert context.request.get(origin + '/authority.json').status == 404
            record('cross-origin-private-api-and-file-path-blocked')
            # A second real HTTP session is independent of the browser/bridge cookie jar.
            secondary = urllib.request.build_opener(urllib.request.HTTPCookieProcessor(http.cookiejar.CookieJar()))
            second_login = urllib.request.Request(origin + '/api/login', method='POST',
                headers={'Origin': origin, 'Content-Type': 'application/json'},
                data=json.dumps({'username':'owner','password':password,'code':enrollment['recoveryCodes'][7]}).encode())
            with secondary.open(second_login, timeout=15) as answer:
                assert answer.status == 200
            page.get_by_role('button', name='Обновить', exact=True).click()
            page.get_by_role('button', name='Сессии владельца', exact=True).click()
            expect(page.get_by_role('heading', name='Активные сессии', exact=True)).to_be_visible()
            expect(page.get_by_text('Другой браузер', exact=True)).to_be_visible()
            page.get_by_role('button', name='Завершить остальные', exact=True).click()
            expect(page.get_by_text('Другой браузер', exact=True)).to_have_count(0)
            expect(page.get_by_text('Текущий браузер', exact=True)).to_be_visible()
            try:
                secondary.open(origin + '/api/session', timeout=15)
                raise AssertionError('Revoked secondary cookie still authorizes requests')
            except urllib.error.HTTPError as denial:
                assert denial.code == 401
            page.screenshot(path=str(out / 'admin-sessions.png'), full_page=True)
            record('react-revokes-other-real-session-current-survives')
            page.get_by_role('button', name='Выдать ключ', exact=True).click()
            page.get_by_label('Клиент / примечание').fill('Тестовый заказчик')
            page.get_by_label('Срок', exact=True).select_option('permanent')
            if args.browser_bridge:
                page.get_by_role('button', name='Создать лицензию', exact=True).click()
                page.wait_for_function('window.__downloads.length > 0')
                saved = page.evaluate('window.__downloads[0]')
                (private / 'owner-request.json').write_text(saved['text'], encoding='utf-8')
            else:
                with page.expect_download() as download:
                    page.get_by_role('button', name='Создать лицензию', exact=True).click()
                download.value.save_as(str(private / 'owner-request.json'))
            expect(page.get_by_test_id('issued-key')).to_be_visible(timeout=15000)
            key = page.get_by_test_id('issued-key').inner_text().strip()
            assert re.fullmatch(r'LSP-[A-Za-z0-9_-]{43}', key)
            sensitive.append(key)
            receipt = json.loads((private / 'owner-request.json').read_text())
            assert receipt['accessKey'] == key
            key_file = private / 'access.txt'
            key_file.write_text(key)
            key_file.chmod(0o600)
            record('browser-issue-and-owner-retry-receipt')
            run('activate-browser-issued-key', [*cmd('lsptool', True), 'license', 'activate', '--key-file', str(key_file)])
            run('ordinary-online-command', [*cmd('lsptool', True), 'self-test'])
            page.get_by_role('button', name='Лицензии', exact=True).click()
            page.get_by_role('button', name='Обновить', exact=True).click()
            expect(page.locator('.badge.active')).to_be_visible(timeout=15000)
            page.screenshot(path=str(out / 'admin-licenses.png'), full_page=True)
            # This non-mutating path must still require the existing session and CSRF token.
            anonymous = urllib.request.Request(origin + '/api/licenses/query', method='POST', headers={'Origin': origin, 'Content-Type': 'application/json'}, data=b'{}')
            try:
                urllib.request.urlopen(anonymous, timeout=15)
                raise AssertionError('Anonymous owner query was authorized')
            except urllib.error.HTTPError as denial:
                assert denial.code == 401
            no_csrf = page.evaluate("""async()=> (await fetch('/api/licenses/query',{method:'POST',headers:{'Content-Type':'application/json'},body:'{}'})).status""")
            assert no_csrf == 403
            record('owner-page-api-requires-session-and-csrf')
            def issue_batch(count: int, start: int = 0) -> None:
                """Seed disposable owner trials through the actual authenticated API, never by editing its database."""
                values = [{'id':str(uuid.uuid4()),'accessKey':'LSP-'+secrets.token_urlsafe(32),'label':f'Проверка страницы {i:03d}',
                           'unit':'days','amount':7,'starts':'activation','maxDevices':1} for i in range(start, start+count)]
                sensitive.extend(x['accessKey'] for x in values)
                result = page.evaluate("""async(values)=>{const session=await (await fetch('/api/session')).json();for(const value of values){const r=await fetch('/api/issue',{method:'POST',headers:{'Content-Type':'application/json','X-CSRF-Token':session.csrfToken},body:JSON.stringify(value)});if(!r.ok)return r.status;}return 200;}""",values)
                assert result == 200
            issue_batch(55)
            page.get_by_role('button', name='Обновить', exact=True).click()
            expect(page.locator('tbody tr')).to_have_count(25, timeout=15000)
            expect(page.locator('.stat').first.locator('strong')).to_have_text('56')
            page.get_by_role('button', name='Далее', exact=True).click()
            expect(page.get_by_text('Страница 2 · Найдено 56', exact=True)).to_be_visible()
            issue_batch(1, 55)
            page.get_by_role('button', name='Далее', exact=True).click()
            expect(page.get_by_text('Данные изменились или срок страницы истёк. Открыта первая страница.', exact=True)).to_be_visible()
            expect(page.get_by_text('Страница 1 · Найдено 57', exact=True)).to_be_visible()
            page.get_by_label('Поиск по клиенту или ID').fill('страницы 051')
            page.get_by_role('button', name='Применить фильтры', exact=True).click()
            expect(page.locator('tbody tr')).to_have_count(1)
            expect(page.locator('tbody tr')).to_contain_text('051')
            page.get_by_role('button', name='Открыть', exact=True).click()
            expect(page.get_by_role('heading', name='Проверка страницы 051', exact=True)).to_be_visible()
            page.get_by_label('Поиск по клиенту или ID').fill('')
            page.get_by_role('button', name='Применить фильтры', exact=True).click()
            expect(page.locator('tbody tr')).to_have_count(25)
            page.screenshot(path=str(out / 'admin-pagination.png'), full_page=True)
            record('real-owner-pages-global-counts-search-details-and-stale-recovery')
            page.get_by_role('button', name='Резервный доступ', exact=True).click()
            page.get_by_role('button', name='Включить резервную систему', exact=True).click()
            expect(page.get_by_text('Выдача включена', exact=True)).to_be_visible(timeout=15000)
            page.get_by_label('Лицензия (из загруженного списка)').select_option(receipt['id'])
            expect(page.get_by_label('Установка', exact=True).locator('option')).to_have_count(2, timeout=15000)
            device = page.get_by_label('Установка', exact=True).locator('option').nth(1).get_attribute('value')
            assert device is not None
            expect(page.get_by_label('Установка', exact=True).locator('option')).to_have_count(2, timeout=15000)
            page.get_by_label('Установка', exact=True).select_option(device)
            page.get_by_role('button', name='Выдать резервный допуск', exact=True).click()
            expect(page.get_by_test_id('reserve-command')).to_be_visible(timeout=15000)
            command = page.get_by_test_id('reserve-command').inner_text()
            grant_id = command.split()[-1]
            assert str(uuid.UUID(grant_id)) == grant_id
            page.screenshot(path=str(out / 'admin-reserve.png'), full_page=True)
            record('browser-global-reserve-enable-and-device-grant')
            run('client-enable-seven-day-reserve', [*cmd('lsptool', True), 'license', 'reserve-enable', '--id', grant_id])
            stop_server()
            run('offline-reserve-command-works', [*cmd('lsptool', True), 'self-test'])
            run('local-reserve-disable', [*cmd('lsptool', True), 'license', 'reserve-disable'])
            run('offline-without-reserve-denied', [*cmd('lsptool', True), 'self-test'], 77)
            start_server()
            run('reserve-reenabled-online', [*cmd('lsptool', True), 'license', 'reserve-enable', '--id', grant_id])
            navigate()
            expect(page.get_by_role('heading', name='Управление лицензиями')).to_be_visible()
            login(enrollment['recoveryCodes'][1])
            record('restart-invalidates-browser-session-preserves-license')
            page.get_by_role('button', name='Резервный доступ', exact=True).click()
            page.get_by_role('button', name='Отключить резервную систему', exact=True).click()
            expect(page.get_by_text('Выдача отключена', exact=True)).to_be_visible(timeout=15000)
            run('online-license-survives-reserve-disable', [*cmd('lsptool', True), 'self-test'])
            stop_server()
            run('learned-reserve-disable-blocks-offline', [*cmd('lsptool', True), 'self-test'], 77)
            start_server()
            navigate()
            login(enrollment['recoveryCodes'][2])
            page.get_by_role('button', name='Резервный доступ', exact=True).click()
            page.get_by_role('button', name='Включить резервную систему', exact=True).click()
            expect(page.get_by_text('Выдача включена', exact=True)).to_be_visible(timeout=15000)
            expect(page.get_by_text('Поколение отключено', exact=True)).to_be_visible()
            run('old-grant-not-resurrected', [*cmd('lsptool', True), 'license', 'reserve-enable', '--id', grant_id], 77)
            page.get_by_label('Лицензия (из загруженного списка)').select_option(receipt['id'])
            expect(page.get_by_label('Установка', exact=True).locator('option')).to_have_count(2, timeout=15000)
            page.get_by_label('Установка', exact=True).select_option(device)
            page.get_by_label('Окно без обновления, часы').fill('24')
            page.get_by_role('button', name='Выдать резервный допуск', exact=True).click()
            expect(page.get_by_test_id('reserve-command')).to_be_visible(timeout=15000)
            fresh_id = page.get_by_test_id('reserve-command').inner_text().split()[-1]
            run('fresh-reserve-after-epoch-change', [*cmd('lsptool', True), 'license', 'reserve-enable', '--id', fresh_id])
            page.locator('tr').filter(has_text=fresh_id).get_by_role('button', name='Отозвать', exact=True).click()
            expect(page.locator('tr').filter(has_text=fresh_id).get_by_text('Отозван', exact=True)).to_be_visible(timeout=15000)
            run('explicit-revoked-refresh-blocked', [*cmd('lsptool', True), 'license', 'reserve-enable', '--id', fresh_id], 77)
            stop_server()
            run('revoked-refresh-denial-is-sticky-offline', [*cmd('lsptool', True), 'self-test'], 77)
            start_server()
            navigate()
            login(enrollment['recoveryCodes'][3])
            page.get_by_role('button', name='Выйти', exact=True).click()
            expect(page.get_by_role('heading', name='Управление лицензиями')).to_be_visible(timeout=15000)
            page.get_by_label('Логин', exact=True).fill('owner')
            page.get_by_label('Пароль', exact=True).fill(password)
            page.get_by_label('Код TOTP или код восстановления').fill(enrollment['recoveryCodes'][3])
            page.get_by_role('button', name='Войти', exact=True).click()
            expect(page.get_by_role('alert')).to_contain_text('Неверный логин, пароль или одноразовый код', timeout=15000)
            login(enrollment['recoveryCodes'][4])
            page.set_viewport_size({'width':390,'height':844})
            assert page.evaluate('document.documentElement.scrollWidth <= innerWidth')
            page.screenshot(path=str(out / 'admin-mobile.png'), full_page=True)
            record('logout-recovery-replay-and-responsive-console')
            if errors:
                raise RuntimeError('Browser JS errors: ' + '; '.join(errors)[:1000])
            record('no-browser-javascript-errors')
            browser.close()
        if report['inputHashes'] != source_hashes():
            raise RuntimeError('Sources changed during browser integration; rerun it.')
        report['status'] = 'passed'
        return 0
    except Exception as exc:
        text = str(exc)
        for value in sensitive:
            text = text.replace(value, '[REDACTED]')
        report['status'] = 'failed'
        report['error'] = text
        print('WEB INTEGRATION FAILED:', text, flush=True)
        return 1
    finally:
        stop_server()
        shutil.rmtree(private, ignore_errors=True)
        report['privateCredentialsRemoved'] = not private.exists()
        (out / 'web-reserve-integration-report.json').write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')


if __name__ == '__main__':
    if hasattr(signal, 'SIGUSR1'):
        faulthandler.register(signal.SIGUSR1)
    raise SystemExit(main())
