# Диагностика 0.10.0

| Симптом | Что делать |
|---|---|
| `.NET 9 SDK not found` | Установить именно SDK x64 и открыть новый терминал. `dotnet --list-sdks` должен видеть 9.x. В этой поставке отсутствие SDK не означает успешную сборку. |
| Выбран SDK 10 / SDK not compatible | global.json требует 9; установите 9 рядом. Не меняйте TargetFramework лишь ради обхода проверки. |
| `ModuleNotFoundError: pygments/cryptography` | Установить validation/requirements.txt в тот же Python, которым запускается build_release.py. |
| NU1301, runtime pack отсутствует | Проверить сеть/прокси/официальный NuGet source. У self-contained publish свой RID restore; не добавлять `--no-restore` вслепую. |
| C# compile или Roslyn audit FAIL | Открыть лог первого неуспешного шага; исправить причину. Релиз не выпускать до зелёных тестов. |
| JSON_DUPLICATE_PROPERTY | Удалить неоднозначное повторение поля; даже экранированные формы одного имени считаются повтором. |
| WORKSPACE_OUTPUT_INSIDE | Поместить новый CPK/план рядом с папкой перевода, не внутрь неё. |
| Source hash/expected MIPS mismatch | Проверить ревизию/чистый оригинал; не отключать хэш-защиту. |
| Неполный архив ресурсов / exit 2 | Нужны sc.cpk и lt.bin из папки DATA игры. Файлы обновления PSP не являются игровыми сценариями. |
| demo exit 3 на служебном токене | В отрицательном тесте это ожидаемый отказ. В обычной сборке — исправить ввод. |
| Не запускается double click EXE | CLI запускается из терминала с аргументами; выполните `version`, `--help`. |
| Windows считает download неизвестным | Подпись Authenticode не выпускалась. Проверьте SHA-256 и происхождение; не отключайте защиту системы ради неизвестного файла. |
| Повторный тег уже существует | Не передвигайте старый публичный тег. Исправить код, увеличить версию и выпустить новый тег; не затирать релиз вручную без понимания последствий. |

Не открывайте пользовательские приватные данные публично в Issues. Для сообщения об ошибке достаточно версии, команды, кода завершения, обезличенного лога и хэшей входов; сами образы игр не нужны в публичной задаче.


## Scenario errors added in 0.12.0

`SCRIPT_GLYPH_EOF` means a field reaches the next record or checksum without its
structural delimiter. A checksum word is not a legal substitute.
`SCRIPT_TABLE_RANGE`, `SCRIPT_TABLE_OVERLAP`, `SCRIPT_DUPLICATE_TEXT_OFFSET` and
`SCRIPT_OFFSET` indicate unsafe metadata or targets, not a missing font.
`SCRIPT_INPUT_LIMIT`, `SCRIPT_OUTPUT_LIMIT`, `SCRIPT_FIELD_LIMIT` and
`SCRIPT_GLYPH_LIMIT` indicate exhausted explicit budgets; check the file/profile
before increasing limits. `SCRIPT_JUMP_INSIDE_TEXT` is still intentional when the
target is strictly inside a genuinely changed record; unchanged records no longer
trigger that error on a no-op rebuild.

## CPK errors added in 0.13.0

`CPK_METADATA_SCHEMA` means unknown fields cannot be transferred losslessly; do not
drop fields to bypass it. `CPK_FILE_COUNT` flags inconsistent counts. `CPK_METADATA_LIMIT`
is a metadata byte budget, not an out-of-memory error. `CPK_UNSUPPORTED_INDEX` rejects
extra indices whose references this ITOC-only rebuilder cannot relocate.
See [CPK_CODEC_RU.md](CPK_CODEC_RU.md) for all new preconditions.
