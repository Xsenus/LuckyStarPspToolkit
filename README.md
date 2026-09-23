# Lucky Star PSP Translation Toolkit

Версия **0.19.1** — C#/.NET 9, инженерная предварительная версия инструмента
для анализа и подготовки переводов PSP Lucky Star.

В проекте есть **React-панель владельца** с password/TOTP/recovery и отдельный,
явно выдаваемый установке **резервный допуск на 1–168 часов**. По умолчанию обычные лицензии онлайн.
Глобальное выключение/отзыв не могут мгновенно дойти до отключённого ПК; срок ограничен подписью.
Исходный код открыт. Рабочие ключи подписи, база владельца, пароли и данные MFA
хранятся отдельно. Клиенту для обычной работы выдаются его ZIP и отдельный ключ.

[Вся документация](docs/README.md) · [Требования и приёмка](docs/CUSTOMER_ACCEPTANCE_RU.md) ·
[Первый запуск](docs/USAGE_RU.md) · [Разработка](docs/DEVELOPMENT.md)

[React-админка и VPS](docs/ADMIN_WEB_RU.md) · [Резерв](docs/RESERVE_ACCESS_RU.md) ·
[Архитектура безопасности](docs/ADMIN_ARCHITECTURE_RU.md) · [Владелец](docs/LICENSE_OWNER_RU.md) ·
[Клиент](docs/LICENSE_CUSTOMER_RU.md) · [Миграция](docs/LICENSE_MIGRATION_RU.md)

В 0.19.1 исправлены ошибки, найденные при штатной сборке .NET 9 на Windows.
Штатная сборка и три GitHub job Windows/Linux/Chromium прошли; админка развёрнута
на [lspt.blagodaty.online](https://lspt.blagodaty.online) с проверенным HTTPS.
Выпущенный Windows-клиент прошёл активацию, self-test, synthetic demo и блокировку
после отзыва; лицензия сохранилась при перезапуске сервера.
Фактические результаты и оставшиеся проверки: [отчёт 0.19.1](docs/VERIFICATION_0.19.1_RU.md)
и [матрица приёмки](docs/CUSTOMER_ACCEPTANCE_RU.md).
Отчёты исходного комплекта 0.19.0 про Roslyn/.NET 11 preview сохранены как история.
Оригинальные игровые ресурсы и файлы шрифтов в исходники не включены.

[Исторические проверки 0.19.0](docs/TEST_REPORT_RU.md) ·
[Изменения 0.19.1](docs/RELEASE_NOTES_0.19.1_RU.md) ·
[Алгоритм сценариев](docs/SCRIPT_CODEC_RU.md) ·
[CPK и память](docs/CPK_PERFORMANCE_RU.md) · [Сценарии и память](docs/PERFORMANCE_RU.md)

## Начать здесь

Полная инструкция: [docs/USAGE_RU.md](docs/USAGE_RU.md). Сценарий встречи с заказчиком: [docs/CUSTOMER_DEMO_RU.md](docs/CUSTOMER_DEMO_RU.md).

Для сборки нужны .NET **9 SDK**, Python **3.11+** (в CI используется 3.13) и библиотеки инструментов проверки:

```powershell
python -m pip install -r validation/requirements.txt
.\build.cmd --rids win-x64 --license-trust C:\LspOwner\client-trust.json
```

По умолчанию `build.cmd` / `./build.sh` создают оба пакета: `win-x64,linux-x64`. Для Linux:

```bash
python3 -m pip install -r validation/requirements.txt
./build.sh --rids linux-x64 --license-trust /secure/client-trust.json
```

Сборка последовательно проверяет документацию, генерирует синтетические эталоны, запускает статические проверки, компилирует solution, запускает три C# self-test проекта и Roslyn-аудит документации, затем публикует и проверяет нативный CLI. Только после успеха формируются ZIP и SHA-256 в `artifacts/releases/0.19.1/`.

**Если один шаг не прошёл, релиз не считается готовым.** Подробные логи: `artifacts/build-logs/<run-id>/`. Предыдущая успешная папка релиза сохраняется при ошибке до финального продвижения новой папки.

## Что уже есть в коде

Единый CLI объединяет чтение SFO/PRX/ELF, расшифровку поддерживаемого EBOOT, ревизионные патчи VWF/heap, CPK/ITOC/@UTF/CRILAYLA, экспорт/импорт сценариев через JSON, LT/BDF и ISO9660. Неизвестные или неподтверждённые состояния отвергаются. Это описание реализованных модулей, а не утверждение о выполненной приёмке реальных игр.

```text
lsptool version
lsptool --help
lsptool license activate
lsptool license status
lsptool self-test
lsptool collect-assets game.iso lucky-star-assets.zip --include-optional
lsptool cpk-verify sc.cpk
```

Полный перечень: [CLI reference](docs/CLI_REFERENCE.md).

## Сохранённые улучшения CPK из 0.13.0

`CriCpkArchive.Inspect` возвращает только каталог проверенных ranges, `Extract`
копирует/декодирует один ресурс. CLI использует те же операции. No-op Build
сохраняет все исходные байты. При реальной замене данные проверяются spans
без повторного копирования полного архива. Unknown metadata между DataL/DataH
не теряется; несовместимая схема отклоняется.

[Подробно: алгоритм CPK](docs/CPK_CODEC_RU.md).

## Публикация на GitHub

[Инструкция GitHub](docs/GITHUB_PUBLISH_RU.md). Обычный push запускает проверку и сборку пакетов в Windows/Linux. Push тега, совпадающего с `VERSION`, после успешной проверки обеих платформ публикует **engineering prerelease** с ZIP и SHA-256. Перевод не объявляется завершённым автоматически.

Происхождение VWF-профиля и статус разрешений раскрыты в [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) и [docs/LICENSING.md](docs/LICENSING.md). Сторонняя таблица не перелицензируется нашей MIT. Аудит исходной Git-истории описан в [отчёте публикации](docs/PUBLICATION_AUDIT_RU.md).

## Состав

`Core` — криптография, SFO, EBOOT и профиль; `Formats` — архивы, сценарии, карты/шрифты, ISO; `Cli` — команды; `tests` — автономные C#-тесты; `tools/...Documentation` — Roslyn-аудит; `scripts` и `validation` — сборка, независимые эталоны и проверки; `docs` — инструкции и спецификации.

В исходном комплекте нет игровых образов, оригинальных ресурсов, файлов шрифтов, готовых EXE/DLL и заранее выдуманных результатов C#-тестов. Бинарные синтетические эталоны, включая шрифтовые, создаются локально генератором и не коммитятся.

## Документы и правила

[Архитектура](docs/ARCHITECTURE.md) · [Форматы](docs/FORMATS.md) · [API](docs/API_REFERENCE.md) · [Разработка](docs/DEVELOPMENT.md) · [Тестирование](docs/TESTING.md) · [Сборка и релизы](docs/BUILD_AND_RELEASE_RU.md) · [Ошибки](docs/TROUBLESHOOTING.md) · [SECURITY.md](SECURITY.md) · [CONTRIBUTING.md](CONTRIBUTING.md)

### Ограничения карты глифов в 0.19.0

Помимо байтового лимита, входной карте задаются предел 1 000 000 UTF-16 единиц и 128 единиц на одну метку глифа. Количество строк и длина меток проверяются до `Split`/построения trie; это предотвращает непропорциональное выделение памяти для огромной строки или миллионов пустых строк. Пределы настраиваются в `FileLimits`. Это защитные ограничения, а не измеренная гарантия расхода RAM или времени.

## Новое в 0.19.0

Долгая проверка пароля больше не удерживает блокировку активных сессий.
Login и повторное MFA имеют отдельные лимиты и ресурсы. Поздний результат
не может восстановить отозванную сессию; recovery/TOTP расходуются только
после актуальных проверок и записи состояния. Медленные anonymous login uploads
не занимают обработчики действующего владельца. Сила PBKDF2 не уменьшена.

[Алгоритм и обновление](docs/ADMIN_AUTH_CONCURRENCY_RU.md) ·
[Серверные страницы 0.18](docs/ADMIN_QUERIES_RU.md) ·
[Проверки](docs/TEST_REPORT_RU.md).

Изоляция входа и повторного MFA распространяется также на обновлённый Nginx-шаблон.
Сервер, React-ресурсы и proxy-конфигурацию нужно обновлять согласованно; см.
[ADMIN_AUTH_CONCURRENCY_RU.md](docs/ADMIN_AUTH_CONCURRENCY_RU.md).
