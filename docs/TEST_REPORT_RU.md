# Фактическое тестирование 0.16.0

Дата выполнения: 2026-09-22. База: commit 348d90531bac48be3b321ea9f24ab2f788c2f78c (v0.15.0).
Проверки выполнены на исходном C# коде, не по нарисованным status-файлам. Дополнительные ограничения ниже
обязательны при передаче результатов: native .NET9/Windows/TLS/игра не заменены диагностикой.

## Выполнено

| Проверка | Результат |
|---|---|
| Roslyn compilation | 11 сборок успешно скомпилированы |
| C# core | 9 групп passed |
| C# форматы | 53 группы passed |
| C# лицензирование | 97 групп passed (36 новых относительно 0.15.0) |
| Roslyn XML documentation | 1887 именованных объявлений, 0 ошибок |
| Python release tests | 29 passed |
| Node React model tests | 10 passed |
| React static build | Выполнен; проверены vendor SHA-256 и детерминированный BUILD-MANIFEST |
| Старый online owner/server/client lifecycle | 24 шага passed |
| Синтетическое игровое демо под действующей лицензией | 11 шагов passed |
| React + actual loopback backend + CLI reserve lifecycle | 29 шагов passed; diagnostic browser bridge, не native networking |
| Общий release validator | 33 passed, 0 failed, 12 not_run |

Реальная managed-среда: PowerShell 7.7 preview, .NET 11.0.0-preview.6.26359.118, Roslyn.
Target solution всё ещё net9.0. В documentation auditor остаются предупреждения CS1701 о согласовании
версий SDK-библиотек и экспериментального runtime; они сохранены в полных журналах. Компилированные
диагностические DLL, SDK, PowerShell и тестовые issuer/пароли в поставку не входят.

## Новые проверки

LSPR1 signature/domain separation, product/license/device/host/nonce scope, max168h, cap по основной
лицензии, hard exclusive expiry, epoch retirement после disable/re-enable, отдельный revoke,
идемпотентность, одноразовый challenge, persistence, clock rollback, monotonic time, fractional restart,
sticky denial и повреждение local cache. Watchdog переводит короткий online lease на ранее подписанный
резерв, не ожидая завершения сетевого таймаута; неподписанный интервал не создаётся.

Web: TOTP RFC vectors, replay, recovery consumption across restart, password обязателен даже с recovery,
CSRF/session logout, idle15min, absolute8h, fresh5min, rate limit, concurrent recovery (один победитель),
origin validation, запрет повторного init, отказ bootstrap output без создания недоступной учётной записи.

Интерактивный React-тест действительно выдаёт ключ в форме, активирует отдельный CLI, выдаёт grant,
останавливает сервер, проверяет offline command, локальный disable, глобальный disable,
restart/session invalidation, старое поколение, новый grant, индивидуальный revoke и сохранение отказа
после отключения сети. Интерфейс просмотрен при 1366×768 и 390×844; JS page errors не обнаружены.

## Чего НЕ доказывает browser bridge

Chromium в этой среде запрещает навигацию URL системной политикой URLBlocklist. Эта политика не менялась.
При диагностике фактический React рендерится в разрешённом about:blank; fetch передаётся через
ограниченный Python HTTP transport в настоящий локальный LicenseWebServer, а cookie ведёт тестовый
cookie jar. Серверные статусы и Set-Cookie/CSP заголовки действительно проверяются, но это **не**
проверка браузерного соблюдения Origin/cookie/CSP, не HTTPS и не реальная запись native downloads.
Для production эти проверки вынесены в native Playwright CI без --browser-bridge.
Screenshot-артефакты показывают реально отрендеренный интерфейс, но не развёрнутый публичный сайт.

## Не выполнено

.NET9 SDK/MSBuild/publish, нативные Windows CNG/ACL/EXE, внешние DNS/TLS/Nginx/VPS, remote GitHub Actions,
аппаратная аттестация/защита от полного VM snapshot rollback, игровая приёмка PPSSPP/PSP.
Оригинальный customer archive, sc.cpk, lt.bin и ISO не предоставлены в текущем файловом пространстве.
Сохранённые старые хэши не выдаются за новый raw-byte прогон.

## Воспроизводимость

Node: `node --test web/license-admin/tests/model.test.mjs`, затем `node web/license-admin/build.mjs`.
Native: `dotnet build LuckyStarPspToolkit.sln -c Release`, три self-test проекта, затем
`python scripts/web_reserve_integration.py --output artifacts/browser-check` с Playwright Chromium.
В диагностической среде — scripts/verify_roslyn.py с явно указанным доверенным pwsh.
Успешные отчёты привязаны к SHA-256 текущих исходников и журналов; изменения делают evidence устаревшим.
Проверку комплекта после распаковки см. reports/CLEAN_ARCHIVE_VERIFY.json и CHECKSUMS.sha256 в корне.
