# Фактическое тестирование 0.18.0

Дата: 2026-09-22. База: fbf27fd92f4817e756266916161feb8525b95f20 (v0.17.0).
Архив базы SHA-256 1651abdefa8a12aeb95ae1693a980a480f2bd9e70aec89799b46dc41a32670f5
проверен; все 341 исходных Git-файлов совпали с bundle. История продолжается, не сбрасывается.
На исходной сборке выполнены прежние 109 групп лицензирования до начала правок.

## Выполнено на текущей версии

| Проверка | Результат |
|---|---|
| Компиляция C#13 через Roslyn | 11 сборок |
| C# ядро | 9 групп |
| C# игровые форматы | 53 группы |
| C# лицензирование | 121 группа, 12 новых |
| Roslyn XML documentation | 1970 объявлений, 0 ошибок |
| Текстовая документация C# | 90 файлов, обнаруженных пропусков нет |
| Node frontend tests | 28 тестов, 10 новых |
| React build | Vendor SHA-256 проверен, production assets собраны |
| Python release pipeline | 29 тестов |
| Python owner-build | 7 тестов отдельно |
| CLI/owner/server licensing lifecycle | 24 шага |
| Синтетическое игровое демо с лицензией | 11 шагов |
| React + настоящий backend + клиентский процесс | 32 шага, diagnostic HTTP bridge |
| Общий release validator | 33 passed, 0 failed, 12 not_run |

Среда: PowerShell 7.7.0-preview.4, .NET 11.0.0-preview.6.26359.118, Roslyn/C#13.
Целевая платформа — net9.0. Предупреждения CS1701 согласования host/reference libraries
в инструменте документации сохранены в журналах. Основной managed-прогон не является
SDK build, Windows EXE, self-contained publish или приёмкой настоящей игры.

## Новые регрессии

Полный обход 63 лицензий по 1/7/25/100 строк, одинаковые секунды выдачи, независимый
sort-reference, фильтры и global counts, activated-only, пустой результат и неверный
запрос. Подмена MAC, переключение limit/filter/endpoint, база после записи, граница
120 секунд, перезапуск authority, неизменное asOf при истечении между страницами.
Проверено отсутствие полных устройств/pubkey/hostBinding в summary, изоляция возвращённых
массивов, история из 231 резервного допуска (больше прежнего UI-ограничения 200),
правильность признаков родительской лицензии/установки/политики.

Frontend: передача фильтров серверу, next/previous cursors, одна попытка восстановить
устаревший снимок, сохранение старой страницы при transport failure, валидация response,
защита от late-response и отмена unmount. Новый Node runner обнаруживает все *.test.mjs
без зависимости от shell wildcard, чтобы новые тесты не выпадали из Windows CI.

HTTP/browser integration: реальные 57 лицензий и счётчики; страницы по 25; переход на
вторую; изменение базы и заметный reset; поиск одной метки, точная карточка; query без
сессии получает 401 и без CSRF получает 403. Все прежние MFA/recovery/session/revoke и
клиентские offline reserve сценарии продолжают проходить. Screenshots содержат только
синтетические метки/UUID, не активационные ключи или MFA-данные.

## Производительность

Один C# harness выполнен против .17/.18. База 2000 лицензий/4000 установок;
3 прогрева, 7 samples по 20 чтений. Body JSON 1684891 -> 5016 байт;
медиана новых managed allocations за 20 чтений 53398400 -> 273280 байт.
Две wire-модели намеренно разные: полный список против одной summary-страницы.
25 выбранных UUID совпали с независимым эталоном. Это не полная память процесса,
не production HTTPS throughput и не ускорение всех команд. Фильтрация/подсчёт всё
ещё сканируют N записей под lock. См. ADMIN_QUERY_PERFORMANCE_RU.md и сырые reports.

## Браузерная граница

Нативная навигация Chromium реально предпринята и отклонена политикой среды:
ERR_BLOCKED_BY_ADMINISTRATOR для loopback. Политика не менялась. Эта попытка не PASS.
Затем настоящий React отрисован локально, его API передан диагностическим HTTP-мостом
к настоящему backend. MFA/сессии/CSRF проверены на сервере, но enforcement cookie/CSP
самим браузером, TLS и native downloads этим не проверяются. Reports/browser-native
и reports/browser-bridge разделены; обычный Playwright CI без моста не выполнен здесь.

## Не выполнено

.NET9 SDK/MSBuild/self-contained publish, Windows CNG/ACL/EXE, VPS/Nginx/DNS/TLS,
GitHub-hosted Actions, настоящие sc.cpk/lt.bin/ISO и PPSSPP/PSP. Приватного archive.zip
в текущем файловом пространстве нет. Старые baseline hashes не считаются новым чтением.
Нет обещаний невзламываемости клиента, защиты от полного VM rollback или мгновенного
дистанционного отзыва допуска у уже отключённого компьютера.

## Воспроизводимость

.NET9 + Python + Node22: build.cmd --rids win-x64 --license-trust ПУБЛИЧНЫЙ_ПРОФИЛЬ.
Owner: python scripts/build_license_owner.py --rids linux-x64,win-x64.
Frontend: node web/license-admin/test.mjs; node web/license-admin/build.mjs.
Diagnostic: scripts/verify_roslyn.py --pwsh ПУТЬ. Browser integration: scripts/web_reserve_integration.py,
--assemblies/--pwsh и явный --browser-bridge только для диагностической проверки границ среды.
Benchmark: scripts/benchmark_owner_queries.ps1 с папками библиотек двух версий.

Итоговый протокол чистого извлечения, повторного выполнения и сверки исходников находится
в reports/CLEAN_ARCHIVE_VERIFY.json. Диагностические DLL, чужой runtime/компилятор,
рабочие секреты, оригинальные игры и файлы шрифтов в комплект не включаются.
