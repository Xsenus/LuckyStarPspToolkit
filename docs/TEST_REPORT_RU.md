# Фактическое тестирование 0.17.0

Дата: 2026-09-22. База: 5f56653e055e3f7f9aaa0e4673841de142b4d77d (v0.16.0).
334 исходных файла совпали с переданным Git bundle; история продолжается, не сбрасывается.

## Выполнено

| Проверка | Результат |
|---|---|
| Компиляция C#13 через Roslyn | 11 сборок |
| C# ядро | 9 групп |
| C# игровые форматы | 53 группы |
| C# лицензирование | 109 групп, из них 12 новых |
| Roslyn XML documentation | 1920 объявлений, 0 ошибок |
| Текстовая документация C# | 86 файлов, недокументированных объявлений не найдено |
| Поля/свойства/enum | 781 явное объявление, пропусков summary не найдено |
| Node React model tests | 18 тестов, из них 8 новых |
| React build | Создан и проверен vendor SHA-256 |
| Python release pipeline | 29 тестов |
| CLI/owner/server licensing lifecycle | 24 шага |
| Синтетическое игровое демо с лицензией | 11 шагов |
| React + реальный backend + клиентский процесс | 30 шагов, diagnostic bridge |
| Общий release validator | 33 passed, 0 failed, 12 not_run |

Среда: PowerShell 7.7.0-preview.4, .NET 11.0.0-preview.6.26359.118, Roslyn.
Целевая платформа проекта остаётся net9.0. Предупреждения CS1701 согласования библиотек
Roslyn в documentation tool сохранены в логах. Это не SDK build, не Windows EXE и не publish.

## Новые регрессии

Одним C# probe против старой/новой сборки воспроизведены три дефекта резерва:
наблюдённое истечение после возврата часов/перезапуска; наблюдённый rollback после восстановления
часов/перезапуска; старый reserve grant после reset-device/re-activation. В 0.16 инварианты false,
в 0.17 true. Добавлены тесты свежего online recovery, отказа дисковой записи, изоляции reset
между лицензиями, реальной проверки подписи и one-use nonce, ограниченного хвоста аудита.

Сессии: отсутствие cookie/CSRF в выдаваемых view, management UUID не является входным ключом,
revoke одного/всех остальных, идемпотентный повтор, отказ self-target, CSRF, fresh MFA и expiry.
Просмотр списка не сохраняет idle-состояние других браузеров. В React-тесте создан второй настоящий
HTTP cookie-сеанс, завершён кнопкой панели; следующий его запрос получил 401, текущий сохранился.

Frontend: single-flight одинаковых запросов; canonical property order; неизменяемый снимок тела;
поздний ответ старой сессии не удаляет новый pending request. Проверены streaming byte limits,
ранняя отмена oversized body, UTF-8 на границе chunks, некорректный UTF-8 и null error response.

## Производительность

Одинаковый cross-version harness: 3 прогрева, 7 измерений по 100 операций.
Reserve renewal allocations: медиана 5 813 280 -> 4 857 800 байт.
Audit tail (50001 событий, последние 500): 40 424 536 -> 416 800 байт на 100 чтений.
Это managed allocations потока, а не RSS/нативная память/HTTP throughput. Время и сырые samples
сохранены, ускорение wall-time на общем хосте не заявляется. См. RESERVE_PERFORMANCE_RU.md.

## Браузер и границы проверки

Нативная навигация Chromium была действительно предпринята и отклонена системной политикой:
ERR_BLOCKED_BY_ADMINISTRATOR для loopback URL. Политика не менялась. Отказ записан отдельно
в reports/browser-native/; это не продуктовый PASS.

Затем настоящий React отрисован в about:blank, API-запросы переданы ограниченным диагностическим
HTTP-мостом к настоящему локальному backend; MFA и cookie state проверены на серверной стороне.
Это НЕ native enforcement браузерных cookies/CSP, НЕ HTTPS и не проверка нативных скачиваний.
Соответствующий CI-сценарий без --browser-bridge подготовлен, но remote GitHub jobs не выполнялись.
Screenshot — реально отрисованный диагностический интерфейс, не публичный сайт.

## Не выполнено

Штатные .NET9 SDK/MSBuild/self-contained publish, Windows CNG/ACL/EXE, VPS/Nginx/DNS/TLS,
remote Actions, тест на реальных sc.cpk/lt.bin/ISO и PPSSPP/PSP. Приватного архива заказчика
в текущем файловом пространстве нет. Старые сохранённые хэши не являются свежим прогоном.
Никаких обещаний невзламываемости EXE, аппаратной аттестации или защиты от полного VM rollback.

## Воспроизводимость

.NET9 + Python + Node22: build.cmd --rids win-x64 --license-trust ПУБЛИЧНЫЙ_ПРОФИЛЬ.
Frontend: node --test web/license-admin/tests/model.test.mjs; node web/license-admin/build.mjs.
Diagnostic host: scripts/verify_roslyn.py --pwsh ПУТЬ; scripts/web_reserve_integration.py с
--assemblies, --pwsh и явным --browser-bridge только при ограничениях среды.
Performance: scripts/benchmark_reserve.ps1 с двумя каталогами скомпилированных версий.

Протокол чистой распаковки и сверки всех файлов/контрольных сумм добавляется в
reports/CLEAN_ARCHIVE_VERIFY.json. Диагностические DLL/PowerShell/ключи/игровые/шрифтовые файлы
не входят в исходную поставку; owner-web содержит только собранные статические файлы React.
