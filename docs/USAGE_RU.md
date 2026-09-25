> Рабочие команды требуют действующую лицензию. Обычно проверка идёт онлайн; заранее выданный резерв допускает ограниченную работу без связи. Сначала [инструкция владельца](https://github.com/Xsenus/LuckyStarPspToolkit/blob/main/docs/LICENSE_OWNER_RU.md) и [активация](LICENSE_CUSTOMER_RU.md). Сборка без `--license-trust` остаётся заблокированной. Старое standalone demo требует уже активированный CLI; `scripts/license_integration.py` проверяет полный сценарий на отдельном тестовом издателе.

# Запуск и сборка 0.19.1

## 1. Что можно обещать заказчику

Можно показать исходный проект, структуру инструментов и документированные ограничения. Исполняемую утилиту показывайте только после успешного `build.cmd` или нативного CI. Синтетическое демо подтверждает лишь операции на тестовых данных. Готовый русификатор требует исходных `sc.cpk`, `lt.bin`, чистого ISO, проверки отображения текста, сохранений и игровых веток.

В исходном комплекте 0.19.0 EXE не поставлялся; его прежние диагностические отчёты сохранены как история. Текущие проверки штатной сборки 0.19.1 приведены в [матрице приёмки](https://github.com/Xsenus/LuckyStarPspToolkit/blob/main/docs/CUSTOMER_ACCEPTANCE_RU.md). Для настоящей игры нужны отдельные разрешённые оригиналы заказчика.

## 2. Подготовить Windows

Клонируйте репозиторий или распакуйте исходники в отдельную пустую папку. Работайте в корне checkout, где находятся `LuckyStarPspToolkit.sln`, `VERSION` и `build.cmd`; в исходном owner ZIP это папка `project`. Не распаковывайте поверх другого проекта.

Установите .NET 9 SDK x64, Python 3.11+ и Node.js 22+ из официальных дистрибутивов. Node нужен для проверки/сборки React-панели, сервер работает как .NET-процесс. Именно SDK, а не только Runtime. Откройте новое окно PowerShell. Проверьте:

```powershell
dotnet --list-sdks
python --version
node --version
```

В списке SDK должен быть 9.x. `global.json` выбирает стабильный SDK 9; автоматически переходить на 10 эта ветка не настроена.

```powershell
python -m pip install -r validation/requirements.txt
node web/license-admin/test.mjs
node web/license-admin/build.mjs
.\build.cmd --rids win-x64
if ($LASTEXITCODE -ne 0) { throw 'Сборка не прошла; пакет показывать как готовый нельзя.' }
```

Если `python` недоступен, но работает `py`, используйте `py -3 -m pip ...`. `build.cmd` сначала ищет `py`. При нескольких Python-интерпретаторах устанавливайте зависимости в тот же интерпретатор, которым запускаете драйвер:

```powershell
py -3 -m pip install -r validation/requirements.txt
py -3 scripts/build_release.py --rids win-x64
```

Visual Studio необязательна. Обычный `dotnet build LuckyStarPspToolkit.sln` компилирует solution, но не заменяет проверенную упаковку релизов. Вход для полной сборки — `build.cmd`, `scripts/build.ps1` или `dotnet msbuild Build.proj -t:Release`.

Команда без `--license-trust` создаёт заблокированный preview; это ожидаемое поведение.
Для рабочего клиента сначала настройте издателя по [инструкции владельца](https://github.com/Xsenus/LuckyStarPspToolkit/blob/main/docs/LICENSE_OWNER_RU.md), затем:

```powershell
.\build.cmd --rids win-x64 --license-trust C:\LspOwner\client-trust.json
```

## 3. Где результат

После успешной команды:

```text
artifacts/releases/0.19.1/
  win-x64/lsptool.exe
  win-x64/BUILD-STATUS.json
  win-x64/docs/
  win-x64/profiles/
  LuckyStarPspToolkit-0.19.1-win-x64.zip
  LuckyStarPspToolkit-0.19.1-win-x64.zip.sha256
  build-report.json
```

Для заказчика предназначен ZIP соответствующей платформы, целиком. Self-contained пакет не требует установленного .NET Runtime. Python нужен разработчику для проверки/демо, но не самому опубликованному CLI.

Проверьте из корня исходного репозитория. Для self-test и дальнейших рабочих команд нужна клиентская сборка с издателем и активация:

```powershell
$exe = '.\artifacts\releases\0.19.1\win-x64\lsptool.exe'
& $exe version
& $exe --help
& $exe license build-info
& $exe license activate
& $exe license status
& $exe self-test
if ($LASTEXITCODE -ne 0) { throw 'Self-test failed' }
```

Не путайте `dotnet`-компиляцию под Linux с запуском Windows EXE. При сборке двух RID на Windows только win-x64 исполняется локально; `BUILD-STATUS.json` это отражает. Оба нативных запуска обеспечивает CI-матрица Windows/Linux.

В `license activate` вставьте полученный от владельца ключ в скрытый ввод. Он не должен попадать в командную строку или Git. Для preview без издателя проверяйте `version`, `--help` и `license build-info`; активация не настроенного preview не сработает.

## 4. Демонстрация на безопасных данных

```powershell
python scripts/demo_customer.py --cli $exe
if ($LASTEXITCODE -ne 0) { throw 'Демонстрация не прошла' }
```

Скрипт использует только самостоятельно созданные синтетические данные. Он создаёт отдельный каталог `artifacts/demo/<run-id>/`. В `DEMO-REPORT.json` фиксируются реальные exit codes и `dataKind: SYNTHETIC`. `gameRuntimeVerified` остаётся false даже при полном успехе.

Для демонстрации не нужно отправлять/скачивать образ игры. Детальная последовательность: [CUSTOMER_DEMO_RU.md](https://github.com/Xsenus/LuckyStarPspToolkit/blob/main/docs/CUSTOMER_DEMO_RU.md).

## 5. Получить недостающие оригинальные ресурсы

На компьютере владельца чистого образа, после сборки утилиты:

```powershell
New-Item -ItemType Directory -Force .\private | Out-Null
& $exe collect-assets 'D:\Games\LuckyStar.iso' '.\private\lucky-star-assets.zip' --include-optional
```

Нужны `PSP_GAME/USRDIR/DATA/sc.cpk` и `lt.bin`, а также правильные `EBOOT.BIN` и `PARAM.SFO`. Для полной ISO-проверки нужен сам образ на компьютере тестирования. `union.cpk`/`pr.bin` нужны для графических надписей. Папка `private` игнорируется Git, но перед push всё равно проверяйте `git status` и `git diff --cached`.

Если нет обязательных ресурсов, `collect-assets` сообщает неполный комплект. Exit code 2 здесь не означает аварийное повреждение, а означает неполноту. Exit code 3 — некорректный/неподдержанный вход, 64 — ошибка команды, 70 — внутренняя ошибка, 74 — ввод/вывод. Подробности смотрите в JSON/логе.

## 6. Рабочий процесс перевода после получения данных

Для **RGO ULJM05752** исходную индексную карту можно получить из
[`rgo_font_strings.txt` в RyououGakuenToolkit](https://github.com/TimepieceMaster/RyououGakuenToolkit/blob/e734977fc55b39e30a5fbf2b3e52e12443b737b2/apps/RGOScriptExtractor/resources/rgo_font_strings.txt).
Прямой файл в исходном UTF-8/LF содержит 3543 строки; номер строки, начиная с
нуля, соответствует индексу глифа. Не сортируйте строки и не удаляйте пустые.
Если карта отсутствует, сохраните её в отдельную рабочую папку:

```powershell
$mapUrl = 'https://raw.githubusercontent.com/TimepieceMaster/RyououGakuenToolkit/e734977fc55b39e30a5fbf2b3e52e12443b737b2/apps/RGOScriptExtractor/resources/rgo_font_strings.txt'
Invoke-WebRequest -Uri $mapUrl -OutFile '.\private\verified-glyph-map.txt'
(Get-FileHash '.\private\verified-glyph-map.txt' -Algorithm SHA256).Hash
& $exe glyph-map-validate '.\private\verified-glyph-map.txt'
```

Ожидаемый SHA-256 скачанного файла с LF: `D6BBD07170DCE78F34EE363E7D910D7A912886E83AB4851CB78F54CF66513D89`;
`glyph-map-validate` должен показать 3543 записи. Копия с переводами строк
CRLF имеет другой хэш, хотя индексы те же. Карта проверена на исходных RGO
`lt.bin` (3543 глифа) и `sc.cpk`: экспорт сценария 0 и его неизменённая
валидация прошли. Карта **не подходит для NIM** и **не содержит кириллицы**.
`font-inspect` с исходным RGO-шрифтом поэтому сообщает `0/66` русских букв и
код 2; это не ошибка чтения карты. Для русского перевода нужна отдельная
согласованная карта с проверенно свободными индексами и изменённый `lt.bin`.
Если изменить карту после экспорта, создайте workspace заново. Происхождение
таблицы указано в [источниках](SOURCES.md); явная лицензия на её
распространение в исходном репозитории не обнаружена.

Сначала `cpk-verify`, затем проверка карты. Не используйте
`examples/glyph-map.example.txt` или карту синтетического теста для реальной
игры: это другие индексы. Выгрузка:

```powershell
& $exe workspace-export '.\private\sc.cpk' '.\private\verified-glyph-map.txt' '.\private\translation' --game rgo
```

Редактируются только поля `translationSpeaker`, `translationMessage`, `translationText` в `script-XXXX.json`. `null` сохраняет оригинал, пустая строка очищает текст. Нельзя менять исходные поля, ID, терминаторы и ссылки переходов.

```powershell
& $exe workspace-validate '.\private\translation' '.\private\sc.cpk'
& $exe workspace-build '.\private\translation' '.\private\sc.cpk' '.\private\sc-patched.cpk' --plan '.\private\eboot-size-plan.json'
```

Результаты должны находиться ВНЕ каталога `translation`. Далее отдельно проверяются шрифт, EBOOT и ISO. Строго соблюдайте [WORKFLOW_RU.md](https://github.com/Xsenus/LuckyStarPspToolkit/blob/main/docs/WORKFLOW_RU.md) и [EBOOT_VWF_PATCH_RU.md](https://github.com/Xsenus/LuckyStarPspToolkit/blob/main/docs/EBOOT_VWF_PATCH_RU.md). VWF-патч сейчас исследовательский: в PPSSPP с ним пропадают символы клавиатуры имени; для клиентского ISO оставляйте исходный EBOOT. Выход `eboot-build` — расшифрованный ELF, а не повторно подписанный retail PRX. Оригиналы храните отдельно; работа на реальной PSP подтверждается отдельным запуском.

## 7. Когда остановиться

Любой FAIL в build-report, C#-тестах, повторном разборе CPK, checksum, EBOOT-preconditions или ISO проверке — причина не передавать продукт как рабочий. Не обходите ошибку удалением хэшей и отключением защитных проверок. Сохраните папку логов и исправьте причину.

## Проверка CPK 0.13.0

Команды остались совместимыми:

```powershell
& $exe cpk-list sc.cpk
& $exe cpk-extract sc.cpk 0 script-0.bin
& $exe cpk-verify sc.cpk --rebuilt sc-copy.cpk
```

Сначала no-op проверка на копии: `byte-identical: yes`. Для изменённого контейнера
совпадение общего SHA не ожидается. `cpk-list` не копирует вложенные payloads,
но исходный CPK всё ещё читается целиком в ограниченный byte[].
