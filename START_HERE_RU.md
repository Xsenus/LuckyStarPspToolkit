# Начните здесь — 0.19.1

Это исходный проект Lucky Star PSP Toolkit: игровой CLI, сервер лицензирования
и React-панель владельца. [Оглавление документации](docs/README.md) помогает
выбрать нужную инструкцию; [матрица приёмки](docs/CUSTOMER_ACCEPTANCE_RU.md)
показывает, что выполнено и какие реальные данные ещё нужны.

## Разработчику

Нужны .NET 9 SDK, Python 3.11+ и Node.js 22+ для сборки панели:

```powershell
python -m pip install -r validation/requirements.txt
node web/license-admin/test.mjs
node web/license-admin/build.mjs
python scripts/build_release.py --compile-only
```

Работайте в корне checkout, где находятся `VERSION` и `LuckyStarPspToolkit.sln`.
Полные инструкции: [первый запуск](docs/USAGE_RU.md), [разработка](docs/DEVELOPMENT.md).

## Владельцу сервера

1. [Соберите owner tools и создайте издателя](docs/LICENSE_OWNER_RU.md).
2. [Настройте HTTPS и React-панель](docs/ADMIN_WEB_RU.md), пароль и TOTP/recovery.
3. Встройте только публичный `client-trust.json` в клиент через `--license-trust`.
4. Выдайте клиенту проверенный ZIP и отдельно ключ; [активация](docs/LICENSE_CUSTOMER_RU.md).
5. При необходимости заранее выдайте установке [резерв на 1–168 часов](docs/RESERVE_ACCESS_RU.md).

Без встроенного издателя рабочие команды возвращают `LICENSE_NOT_CONFIGURED`.
Обычный бессрочный доступ также требует сервер. Исходный код открыт, поэтому
клиентская проверка лицензии не является защитой от изменения самого кода.

Рабочие issuer/owner-секреты, пароли, TOTP/recovery, база лицензий и материалы игры
не входят в публичный репозиторий. [Сторонние материалы](THIRD_PARTY_NOTICES.md)
сохраняют свою область прав и не перелицензируются автоматически.
