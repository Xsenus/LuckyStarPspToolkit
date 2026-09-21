# Изменения 0.10.0

## Статус

Engineering preview; исходная поставка. C# и игра ещё не прошли текущую нативную приёмку. Реальные результаты: TEST_REPORT_RU.md. Никакого заранее собранного EXE в комплекте нет.

## Надёжность и ограничения памяти

Общий снимок glyph-map для hash/decode/copy; прямое построение trie без повторной сортировки. Бюджет decoded map 1 000 000 UTF-16 units и 128 units на метку; число строк проверяется до Split. Повторяющиеся JSON-поля и escaped aliases отвергаются. Обновлён текстовый лимит JSON. Output CPK/plan изолирован от каталога переводов. Исправлена корневая граница путей и переносимая проверка `..`/drive paths.

## Документация

Расширено покрытие summary на свойства, поля, enum и primary constructor parameters. Добавлен проверяющий SDK Roslyn инструмент; его запуск после сборки обязателен. Дополнены инструкции пользователя, сценарий показа, диагностика, сборка/релизы, GitHub, происхождение. API reference обновлён. Старая абсолютная гарантия clean-room заменена явным нерешённым rights-review пунктом.

## Сборка

Новый `build.cmd` / `build.sh` / `Build.proj`: checked build → self-tests → Roslyn docs → self-contained publish → native synthetic demo → ZIP/SHA-256. Ненулевой код останавливает выпуск. RID publish восстанавливает Microsoft runtime packs. CI Windows/Linux использует тот же driver; тегированная сборка создаёт draft prerelease после успеха обеих платформ. Пакеты сохраняют executable bit Linux CLI и shell-скриптов.

## Совместимость

Обычные CLI-команды сохранены. `publish.*` больше не принимают произвольную output-папку для удаления; принимается runtime. Путь пакетов фиксируется `artifacts/releases/<version>`. JSON с повторяющимися ключами теперь отклоняется; выход внутри workspace теперь запрещён. Generated binary fixtures больше не коммитятся и создаются перед сборкой.
