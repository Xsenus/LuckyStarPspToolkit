> Обновление 0.10.0: актуальные команды сборки/релизов и условия показа — USAGE_RU.md, BUILD_AND_RELEASE_RU.md и CUSTOMER_DEMO_RU.md. Игровая приёмка не выполнена.

# Полный рабочий процесс перевода

## 1. Собрать нужные файлы из ISO

Для первой игры минимально нужны:

```text
PSP_GAME/PARAM.SFO
PSP_GAME/SYSDIR/EBOOT.BIN
PSP_GAME/USRDIR/DATA/sc.cpk
PSP_GAME/USRDIR/DATA/lt.bin
```

Для интерфейса желательно добавить `union.cpk` и `pr.bin`. Если есть собственный чистый ISO, вручную извлекать всю папку не требуется:

```powershell
lsptool collect-assets game.iso lucky-star-assets.zip `
  --include-optional `
  --json collection.json
```

Готовый PowerShell-скрипт:

```powershell
.\scripts\collect-game-assets.ps1 `
  -IsoPath C:\Games\LuckyStar.iso `
  -OutputZip .\lucky-star-assets.zip `
  -IncludeOptional
```

Linux:

```bash
./scripts/collect-game-assets.sh /games/LuckyStar.iso ./lucky-star-assets.zip --include-optional
```

Полученный ZIP содержит оригинальные файлы игры. Его нельзя публиковать; передавать следует только по закрытому каналу.

## 2. Проверить структуру ISO вручную

```powershell
lsptool iso-list game.iso --json iso-list.json
lsptool iso-extract game.iso PSP_GAME/USRDIR/DATA/sc.cpk sc.cpk
```

`iso-list` выполняет полный безопасный разбор каталогов. `iso-extract` поддерживает файл, состоящий из нескольких экстентов, и записывает результат атомарно.

## 3. Проверить комплект

```powershell
lsptool audit-rgo lucky-star-assets.zip --json audit.json
```

Код `0` означает наличие подтверждённого EBOOT, `sc.cpk` и `lt.bin`. Код `2` означает корректный, но неполный набор.

## 4. Проверить EBOOT и профиль кириллицы

```powershell
lsptool verify-eboot EBOOT.BIN --json eboot-check.json
lsptool eboot-vwf-inspect EBOOT.BIN --json eboot-vwf-check.json
```

Для ручного анализа можно отдельно получить исходный расшифрованный ELF:

```powershell
lsptool decrypt-eboot EBOOT.BIN EBOOT.DEC.BIN --json eboot-decrypt.json
```

Расшифровка и VWF-патч разрешены только для подтверждённой ревизии `ULJM05752`. Полный профиль содержит 133 проверяемые MIPS-замены. Подробнее: `docs/EBOOT_VWF_PATCH_RU.md`.

## 5. Подготовить карту глифов

Карта — UTF-8-файл, где номер строки равен индексу глифа. Она должна соответствовать конкретному `lt.bin`.

```powershell
lsptool glyph-map-validate glyph-map.txt --json glyph-map.json
```

Нельзя удалять пустые строки внутри карты: они являются реальными индексами.

## 6. Проверить игровой шрифт

```powershell
lsptool font-inspect lt.bin glyph-map.txt `
  --preview font-original.png `
  --columns 64 `
  --scale 2 `
  --json font-original.json
```

Код `2` здесь допустим: файл структурно корректен, но русский набор пока неполный. В отчёте видны пустые глифы, ширины, padding и состояние всех 66 русских букв.

## 7. Создать кириллический `lt.bin`

Нужен BDF-шрифт, лицензию которого разрешено использовать и распространять в составе патча. Сам toolkit не содержит сторонних шрифтов.

```powershell
lsptool font-import-bdf `
  lt.bin `
  glyph-map.txt `
  my-font.bdf `
  lt-russian.bin `
  --mode russian `
  --baseline 15 `
  --intensity 3 `
  --preview font-russian.png `
  --json font-russian.json
```

По умолчанию операция блокируется, если отсутствует хотя бы одна буква или обрезается хотя бы один пиксель. `--allow-clipping` и `--allow-missing` предназначены только для исследования; такой результат возвращает код `2`.

## 8. Проверить `sc.cpk`

```powershell
lsptool cpk-verify sc.cpk --json cpk-check.json
lsptool cpk-list sc.cpk
```

Для RGO ожидаются сценарные ID `0..10`. Их размеры необходимо сопоставить с таблицей EBOOT.

## 9. Экспортировать текст

```powershell
lsptool workspace-export `
  sc.cpk `
  glyph-map.txt `
  translation `
  --game rgo `
  --json export.json
```

Изменять следует только `translationSpeaker`, `translationMessage` и `translationText`. Поля `source*`, ID, terminator, hashes и команды защищены.

## 10. Провести сухую проверку

```powershell
lsptool workspace-validate translation sc.cpk --json validation.json
```

Проверяются происхождение файлов, UTF-8, доступность глифов, управляющие токены, дубли, переходы, checksum, новый CPK и необходимость изменения таблицы размеров.

## 11. Собрать CPK и план EBOOT

```powershell
lsptool workspace-build `
  translation `
  sc.cpk `
  sc-patched.cpk `
  --plan eboot-size-plan.json `
  --json build.json
```

CPK и план записываются одной транзакцией. После сборки:

```powershell
lsptool cpk-verify sc-patched.cpk
```

## 12. Собрать EBOOT с VWF и новым размером сценариев

Рекомендуемый путь применяет VWF и план размера одной проверяемой операцией к чистому исходному EBOOT:

```powershell
lsptool eboot-build `
  EBOOT.BIN `
  EBOOT.TRANSLATED.ELF `
  --groups russian-text `
  --size-plan eboot-size-plan.json `
  --json eboot-build.json
```

Проверяются все 133 MIPS-слова, точный SHA-256 исходной ревизии, таблица блоков 2 КиБ и script heap. Если крупнейший сценарий превышает исходные `0x236000` байт, пара MIPS `lui/addiu` увеличивается до точного требуемого размера с корректной обработкой знакового immediate.

Для диагностического применения только таблицы и heap к уже расшифрованному чистому ELF остаётся команда:

```powershell
lsptool apply-eboot-plan `
  EBOOT.DEC.BIN `
  eboot-size-plan.json `
  EBOOT.SIZE-ONLY.ELF `
  --json size-only.json
```

Она также повторно проверяет линию происхождения RGO. Итог обеих команд — расшифрованный ELF, не заново подписанный retail PRX. Подробности: `docs/EBOOT_VWF_PATCH_RU.md`.

## 13. Собрать новую копию ISO

Создайте `iso-patch-manifest.json`, указав точные SHA-256 и размеры чистого образа, исходных файлов и подготовленных replacement-файлов. Файлы замены должны располагаться внутри каталога манифеста и задаваться относительными путями.

```json
{
  "schema": "lucky-star-psp.iso-patch-manifest.v1",
  "expectedSourceSha256": "SOURCE_SHA256",
  "replacements": [
    {
      "isoPath": "PSP_GAME/SYSDIR/EBOOT.BIN",
      "sourceFile": "patched/EBOOT.TRANSLATED.ELF",
      "expectedOriginalSha256": "ORIGINAL_EBOOT_SHA256",
      "expectedReplacementSize": 1470173,
      "expectedReplacementSha256": "PATCHED_EBOOT_SHA256"
    },
    {
      "isoPath": "PSP_GAME/USRDIR/DATA/sc.cpk",
      "sourceFile": "patched/sc-patched.cpk",
      "expectedOriginalSha256": "ORIGINAL_SC_SHA256",
      "expectedReplacementSize": 2457600,
      "expectedReplacementSha256": "PATCHED_SC_SHA256"
    },
    {
      "isoPath": "PSP_GAME/USRDIR/DATA/lt.bin",
      "sourceFile": "patched/lt-russian.bin",
      "expectedOriginalSha256": "ORIGINAL_LT_SHA256",
      "expectedReplacementSize": 327680,
      "expectedReplacementSha256": "PATCHED_LT_SHA256"
    }
  ]
}
```

Сборка:

```powershell
lsptool iso-apply-manifest `
  game-clean.iso `
  iso-patch-manifest.json `
  game-patched.iso `
  --json iso-rebuild.json
```

Или:

```powershell
.\scripts\rebuild-game-iso.ps1 `
  -IsoPath .\game-clean.iso `
  -ManifestPath .\iso-patch-manifest.json `
  -OutputIso .\game-patched.iso
```

Инструмент добавляет новые payload в конец копии ISO, обновляет directory records и PVD, побайтно проверяет неизменённый префикс, затем повторно разбирает и хэширует временный результат. Исходный образ не изменяется. Multi-extent цели и неоднозначные ISO-профили блокируются. Подробности: `docs/ISO_REBUILD_RU.md`.

## 14. Игровая приёмка

Минимальный набор:

1. `АаБбЁё`, пунктуация и пробелы;
2. короткое и длинное имя говорящего;
3. две и более строки сообщения;
4. строка у правой и нижней границы окна;
5. варианты ответа;
6. журнал диалогов;
7. save/load;
8. переходы между сценами;
9. сборка без перевода и сравнение поведения;
10. PPSSPP, затем настоящая PSP.

Оригинальные игровые файлы и расшифрованный EBOOT нельзя включать в публичный Git или релиз.
