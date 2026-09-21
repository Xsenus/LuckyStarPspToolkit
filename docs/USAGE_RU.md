# Запуск и сборка 0.10.0

## 1. Что можно обещать заказчику

Можно показать исходный проект, структуру инструментов и документированные ограничения. Исполняемую утилиту показывайте только после успешного `build.cmd` или нативного CI. Синтетическое демо подтверждает лишь операции на тестовых данных. Готовый русификатор требует исходных `sc.cpk`, `lt.bin`, чистого ISO, проверки отображения текста, сохранений и игровых веток.

В переданном комплекте нет заранее собранного EXE: .NET SDK в среде подготовки отсутствовал. Архив заказчика `archive.zip` в текущей среде также отсутствовал; старые отчёты сохранены как исторические, а не новый прогон.

## 2. Подготовить Windows

Распакуйте весь архив. Работайте в папке `project`, где находятся `LuckyStarPspToolkit.sln`, `VERSION` и `build.cmd`. Не запускайте скрипты прямо из окна ZIP.

Установите .NET 9 SDK x64 и Python 3.11 или новее из официальных дистрибутивов. Именно SDK, а не только Runtime. Откройте новое окно PowerShell. Проверьте:

```powershell
dotnet --list-sdks
python --version
```

В списке SDK должен быть 9.x. `global.json` выбирает стабильный SDK 9; автоматически переходить на 10 эта ветка не настроена.

```powershell
python -m pip install -r validation/requirements.txt
.\build.cmd --rids win-x64
if ($LASTEXITCODE -ne 0) { throw 'Сборка не прошла; пакет показывать как готовый нельзя.' }
```

Если `python` недоступен, но работает `py`, используйте `py -3 -m pip ...`. `build.cmd` сначала ищет `py`. При нескольких Python-интерпретаторах устанавливайте зависимости в тот же интерпретатор, которым запускаете драйвер:

```powershell
py -3 -m pip install -r validation/requirements.txt
py -3 scripts/build_release.py --rids win-x64
```

Visual Studio необязательна. Обычный `dotnet build LuckyStarPspToolkit.sln` компилирует solution, но не заменяет проверенную упаковку релизов. Вход для полной сборки — `build.cmd`, `scripts/build.ps1` или `dotnet msbuild Build.proj -t:Release`.

## 3. Где результат

После успешной команды:

```text
artifacts/releases/0.10.0/
  win-x64/lsptool.exe
  win-x64/BUILD-STATUS.json
  win-x64/docs/
  win-x64/profiles/
  LuckyStarPspToolkit-0.10.0-win-x64.zip
  LuckyStarPspToolkit-0.10.0-win-x64.zip.sha256
  build-report.json
```

Для заказчика предназначен ZIP соответствующей платформы, целиком. Self-contained пакет не требует установленного .NET Runtime. Python нужен разработчику для проверки/демо, но не самому опубликованному CLI.

Проверьте из папки `project`:

```powershell
$exe = '.\artifacts\releases\0.10.0\win-x64\lsptool.exe'
& $exe version
& $exe --help
& $exe self-test
if ($LASTEXITCODE -ne 0) { throw 'Self-test failed' }
```

Не путайте `dotnet`-компиляцию под Linux с запуском Windows EXE. При сборке двух RID на Windows только win-x64 исполняется локально; `BUILD-STATUS.json` это отражает. Оба нативных запуска обеспечивает CI-матрица Windows/Linux.

## 4. Демонстрация на безопасных данных

```powershell
python scripts/demo_customer.py --cli $exe
if ($LASTEXITCODE -ne 0) { throw 'Демонстрация не прошла' }
```

Скрипт использует только самостоятельно созданные синтетические данные. Он создаёт отдельный каталог `artifacts/demo/<run-id>/`. В `DEMO-REPORT.json` фиксируются реальные exit codes и `dataKind: SYNTHETIC`. `gameRuntimeVerified` остаётся false даже при полном успехе.

Для демонстрации не нужно отправлять/скачивать образ игры. Детальная последовательность: [CUSTOMER_DEMO_RU.md](CUSTOMER_DEMO_RU.md).

## 5. Получить недостающие оригинальные ресурсы

На компьютере владельца чистого образа, после сборки утилиты:

```powershell
New-Item -ItemType Directory -Force .\private | Out-Null
& $exe collect-assets 'D:\Games\LuckyStar.iso' '.\private\lucky-star-assets.zip' --include-optional
```

Нужны `PSP_GAME/USRDIR/DATA/sc.cpk` и `lt.bin`, а также правильные `EBOOT.BIN` и `PARAM.SFO`. Для полной ISO-проверки нужен сам образ на компьютере тестирования. `union.cpk`/`pr.bin` нужны для графических надписей. Папка `private` игнорируется Git, но перед push всё равно проверяйте `git status` и `git diff --cached`.

Если нет обязательных ресурсов, `collect-assets` сообщает неполный комплект. Exit code 2 здесь не означает аварийное повреждение, а означает неполноту. Exit code 3 — некорректный/неподдержанный вход, 64 — ошибка команды, 70 — внутренняя ошибка, 74 — ввод/вывод. Подробности смотрите в JSON/логе.

## 6. Рабочий процесс перевода после получения данных

Сначала `cpk-verify`, затем `font-inspect` с проверенной картой глифов. Не используйте `examples/glyph-map.example.txt` или карту синтетического теста для реальной игры: это другие индексы. Выгрузка:

```powershell
& $exe workspace-export '.\private\sc.cpk' '.\private\verified-glyph-map.txt' '.\private\translation' --game rgo
```

Редактируются только поля `translationSpeaker`, `translationMessage`, `translationText` в `script-XXXX.json`. `null` сохраняет оригинал, пустая строка очищает текст. Нельзя менять исходные поля, ID, терминаторы и ссылки переходов.

```powershell
& $exe workspace-validate '.\private\translation' '.\private\sc.cpk'
& $exe workspace-build '.\private\translation' '.\private\sc.cpk' '.\private\sc-patched.cpk' --plan '.\private\eboot-size-plan.json'
```

Результаты должны находиться ВНЕ каталога `translation`. Далее отдельно проверяются шрифт, EBOOT и ISO. Строго соблюдайте [WORKFLOW_RU.md](WORKFLOW_RU.md) и [EBOOT_VWF_PATCH_RU.md](EBOOT_VWF_PATCH_RU.md). Выход `eboot-build` — расшифрованный ELF, а не повторно подписанный retail PRX. Оригиналы храните отдельно; работа на реальной PSP подтверждается отдельным запуском.

## 7. Когда остановиться

Любой FAIL в build-report, C#-тестах, повторном разборе CPK, checksum, EBOOT-preconditions или ISO проверке — причина не передавать продукт как рабочий. Не обходите ошибку удалением хэшей и отключением защитных проверок. Сохраните папку логов и исправьте причину.
