# Лицензирование 0.19.1: инструкция владельца

## Что вы передаёте клиенту

Только клиентский ZIP из `artifacts/releases/0.19.1/` и отдельный файл ключа, например `client-trial.txt`.
Исходный код, включая server/admin, опубликован в публичном репозитории. Клиентский
ZIP содержит только необходимые клиенту компоненты. Рабочие authority, owner-token,
пароли и MFA/recovery остаются закрытыми и никогда не входят в публичную поставку.

Обычный режим требует постоянно доступный сервер. Владелец может отдельно выдать подписанный
резервный допуск максимум на 7 дней: [RESERVE_ACCESS_RU.md](RESERVE_ACCESS_RU.md).
Бессрочная лицензия означает отсутствие даты окончания, а не бессрочный офлайн-допуск.
Изменённые старые или декомпилированные клиентские сборки не находятся под контролем этого механизма.

**Обновление существующего сервера 0.14.0:** сначала [LICENSE_MIGRATION_RU.md](LICENSE_MIGRATION_RU.md).
Издатель и ключи не пересоздаются.

## 1. Собрать инструменты владельца

В корне checkout (папка `project` исходного owner-архива) нужны .NET 9 SDK, Python 3.11+, Node.js 22+ и зависимости проверки:

```powershell
py -3 -m pip install -r validation/requirements.txt
py -3 scripts/build_license_owner.py --rids linux-x64,win-x64
if ($LASTEXITCODE -ne 0) { throw 'Owner build failed' }
```

Результат: `artifacts/owner-releases/0.19.1/`. В каждой платформе отдельные каталоги `server/`, `admin/` и `web/`. React-вход настраивается по [ADMIN_WEB_RU.md](ADMIN_WEB_RU.md).
Публикация проходит общие тесты, лицензирование и сетевой сценарий на тестовом издателе.
Штатный .NET 9 publish 0.19.1, Windows/Linux CI и production-активация клиентского EXE
выполнены; точные результаты — [VERIFICATION_0.19.1_RU.md](VERIFICATION_0.19.1_RU.md).

## 2. Создать издателя на Ubuntu VPS

На установленном сервере используйте [VPS_DEPLOYMENT_RU.md](VPS_DEPLOYMENT_RU.md):
действующий origin — `https://lspt.blagodaty.online`, issuer и владелец уже созданы, повторный init не нужен.
Следующие команды — пример только для нового независимого сервера. Нужны ваш домен с DNS,
действующий TLS-сертификат и Nginx. Пример предполагает, что Linux-сборки
уже скопированы в `/opt/lsptool-license/server` и `/opt/lsptool-license/admin`. Не используйте example-домен буквально.
Порты 17840/17841 должны оставаться только на loopback, внешние порты — 443 и при необходимости 80.

```bash
sudo useradd --system --home-dir /var/lib/lsptool-license --shell /usr/sbin/nologin lsptool-license
sudo install -d -m 700 -o lsptool-license -g lsptool-license /var/lib/lsptool-license
sudo install -d -m 750 -o root -g lsptool-license /etc/lsptool-license
sudo sh -c 'umask 077; openssl rand -base64 48 > /etc/lsptool-license/master.pass'
sudo chown root:lsptool-license /etc/lsptool-license/master.pass
sudo chmod 640 /etc/lsptool-license/master.pass
sudo chmod 755 /opt/lsptool-license/server/lsp-license-server /opt/lsptool-license/admin/lsp-license-admin
sudo -u lsptool-license /opt/lsptool-license/admin/lsp-license-admin init \
  --data /var/lib/lsptool-license/authority \
  --url https://licenses.YOUR-DOMAIN/ \
  --password-file /etc/lsptool-license/master.pass
```

`authority` не должна существовать до init. Не запускайте init повторно над рабочими данными.
Сначала формируется защищённый временный каталог, затем готовая конфигурация целиком переименовывается.
После аварийного обрыва процесса может остаться закрытый `.authority-init-*`; это секретные данные, а не файл для клиента.

Создаются:

| Файл | Назначение | Кому передавать |
|---|---|---|
| `authority.json` | Зашифрованный приватный PKCS#8, integrity pepper, хэш токена управления | Никому из клиентов |
| `licenses.json` | Аутентифицированная база лицензий, устройств и аудита | Только ваша резервная копия |
| `server-clock.json` | Checkpoint времени, создаётся при первом запуске authority 0.15; обязателен для schema 2 | Только вместе с полным backup |
| `owner-connection.json` | Токен административного API | Только владельцу через защищённый канал |
| `client-trust.json` | URL, UUID издателя и открытый ключ | Встроить в клиентскую сборку; не секрет |
| `authority.lock` | Блокировка единственного процесса записи | Не переносить как механизм координации |

`master.pass` хранится отдельно от базы, защищает приватный ключ при запуске. Не вводите его в аргументе команды,
не помещайте в Git, логи и публичные переменные CI. Сделайте защищённую резервную копию до выдачи ключей.

## 3. Запустить сервис и HTTPS

Шаблоны: `deploy/licensing/lsptool-license.service`, `deploy/licensing/nginx.conf`.
Измените домен и пути к действительному сертификату в Nginx. Шаблон предназначен для `http`-контекста (`conf.d`).
Он публикует только `/v1/` и `/health`; `/admin/` не проксируется.

```bash
sudo cp deploy/licensing/lsptool-license.service /etc/systemd/system/
sudo systemctl daemon-reload
sudo systemctl enable --now lsptool-license
sudo systemctl status lsptool-license --no-pager
curl --fail http://127.0.0.1:17840/health
# После установки отредактированного nginx.conf и сертификата:
sudo nginx -t
sudo systemctl reload nginx
curl --fail https://licenses.YOUR-DOMAIN/health
```

До демонстрации проверьте HTTPS с клиентского компьютера. Запрос
`https://licenses.YOUR-DOMAIN/admin/licenses` должен возвращать 404, а не список лицензий.
Не отключайте проверку сертификата в клиенте. Для текущего VPS HTTPS, защищённые
маршруты, вход, клиентская активация/отзыв и рестарт подтверждены в
[отчёте проверки](VERIFICATION_0.19.1_RU.md); шаблоны выше относятся к отдельной новой установке.

## 4. Подготовить клиентский EXE

Скопируйте **только публичный** `client-trust.json` на свой ПК, например `C:\LspOwner\client-trust.json`.

```powershell
py -3 scripts/license_build_profile.py C:\LspOwner\client-trust.json
.\build.cmd --rids win-x64 --license-trust C:\LspOwner\client-trust.json
if ($LASTEXITCODE -ne 0) { throw 'Customer publish failed' }
```

Клиентский ZIP появится в `artifacts/releases/0.19.1/`. Публичная конфигурация включена в ресурс сборки,
а не читается из заменяемого рядом JSON. Вариант `--development-loopback` production-публикатор отвергает.
Сборка без `--license-trust` специально остаётся заблокированной с `LICENSE_NOT_CONFIGURED`.

Проверка встроенного издателя, не требующая активации:

```powershell
.\artifacts\releases\0.19.1\win-x64\lsptool.exe license build-info
```

Значения issuer/URL должны соответствовать вашему `client-trust.json`, `developmentLoopback` — false.
Тестовые сборки интеграции и `artifacts/roslyn/*/test-client` клиентам не выдавайте.

## 5. Доступ владельца к управлению

Сохраните `owner-connection.json` только в своём закрытом каталоге. Для удалённого VPS административный адрес
в нём оставьте `http://127.0.0.1:17841/`; подключайтесь через SSH-туннель:

```powershell
ssh -N -L 17841:127.0.0.1:17841 SSH_USER@VPS_HOST
```

Это отдельное окно; пока оно открыто, Windows-инструмент управления обращается к локальному порту туннеля.
Никогда не открывайте 17841 в firewall и не проксируйте его в публичный Nginx.

Во втором окне PowerShell:

```powershell
$admin = 'C:\LspOwner\admin\lsp-license-admin.exe'
$owner = 'C:\LspOwner\private\owner-connection.json'
New-Item -ItemType Directory -Force 'C:\LspOwner\issued' | Out-Null
```

## 6. Выдать тест или постоянный доступ

```powershell
# 6 часов от первой успешной активации, одна установка
& $admin issue --connection $owner --hours 6 --starts activation --devices 1 --label artem-test --out C:\LspOwner\issued\artem-6h.txt

# 7 суток от первой активации (это значение starts по умолчанию)
& $admin issue --connection $owner --days 7 --out C:\LspOwner\issued\artem-7d.txt

# Два календарных года от момента выдачи
& $admin issue --connection $owner --years 2 --starts issue --out C:\LspOwner\issued\artem-2y.txt

# Бессрочно, не более двух установок
& $admin issue --connection $owner --permanent --devices 2 --out C:\LspOwner\issued\artem-permanent.txt
```

Указывайте ровно один срок. Длительности — положительные целые значения; годы считаются в UTC через календарь,
сутки — 24 часа. Для первого запуска можно задать крайний момент
`--activate-before 2027-01-01T00:00:00Z`. Этот предел относится к первой и новым установкам (также после reset-device); уже активированной установке он не сокращает лицензию. Это не продление пробного срока.
Максимум периода — 100 лет. Лимит установок выбирается при выдаче: от 1 до 100.

Команда создаёт `*.txt` с ключом, `*.receipt.json` с ID лицензии и `*.issue-request.json` с запросом безопасного повтора.
**Клиенту отправляйте только txt с ключом.** Запрос выдачи также содержит ключ и остаётся у вас.
Ключ чувствителен к регистру. Исходная строка после активации клиентом на диск не сохраняется.

Сначала запрос сохраняется локально, потом отправляется на сервер. Если связь оборвалась, результат мог быть записан:
повторяйте тот же запрос, а не создавайте новую лицензию:

```powershell
& $admin retry-issue --connection $owner --request C:\LspOwner\issued\artem-7d.txt.issue-request.json --out C:\LspOwner\issued\artem-7d.txt
```

## 7. Продлить, заблокировать или отозвать

```powershell
& $admin list --connection $owner
$licenseId = 'UUID_ИЗ_RECEIPT_ИЛИ_LIST'
& $admin extend --connection $owner --id $licenseId --days 30
& $admin suspend --connection $owner --id $licenseId
& $admin resume --connection $owner --id $licenseId
& $admin permanent --connection $owner --id $licenseId
& $admin revoke --connection $owner --id $licenseId
& $admin audit --connection $owner --out C:\LspOwner\private\audit.json
```

`suspend` обратим, `revoke` необратим. Нельзя снять отзыв продлением или permanent: выдайте новую лицензию.
Продление считается от max(текущий срок, время сервера), не перезапускает пробную лицензию и не применимо к ещё
не активированной activation-start лицензии. `resume` не прибавляет время к сроку.
Перед изменением запрос сохраняется в каталоге `requests` рядом с owner-connection; безопасный повтор:
`retry-change --connection ... --request ПУТЬ_ИЗ_СООБЩЕНИЯ`. Один UUID не продлевает лицензию дважды.

На новом ПК/после переустановки ОС может понадобиться освободить старую установку:

```powershell
& $admin reset-device --connection $owner --id $licenseId --device SHA256_УСТАНОВКИ_ИЗ_LIST
```

Это освобождает слот, но не отменяет сам ключ и не сбрасывает дату первой активации.
Знающий ключ может снова занять свободный слот. Для скомпрометированного ключа используйте revoke.

## 8. Резервное копирование и восстановление

Остановите единственный сервер, скопируйте весь закрытый каталог authority (включая
server-clock.json после первого запуска 0.15) и отдельно passphrase, затем запустите.
Храните копии зашифрованными, вне публичного Git и вне клиентского ZIP. Восстановление проверяйте на изолированном
сервере без второго активного писателя. Переносите издателя с тем же доменом/сертификатом, authority, базой и паролем.

Не заменяйте испорченную/потерянную базу пустой: она содержит первую активацию, отзывы и слоты.
Сервер откажется стартовать при неверной подписи базы, отсутствии обязательного checkpoint
или времени UTC раньше сохранённого наблюдения. Не удаляйте checkpoint для снятия отказа.
Возврат старой полной резервной копии может вернуть старые состояния отзывов; это управляемое владельцем восстановление,
а не предотвращённая программой атака. Настройте NTP, мониторинг /health, диска и срока сертификата.

При потере приватного ключа и passphrase без резервной копии старый издатель не восстанавливается.
При компрометации издателя нужна новая пара ключей, новая клиентская сборка и повторная выдача: автоматической
безостановочной ротации, HSM и HA в 0.19.1 нет.
