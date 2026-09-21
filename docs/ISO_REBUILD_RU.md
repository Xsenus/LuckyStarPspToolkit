# Безопасная обратная сборка ISO9660

Версия 0.9.0 умеет помещать изменённые ресурсы обратно в **новую копию** PSP ISO без `UMD-replace.exe`, монтирования образа и сторонних библиотек.

Исходный ISO всегда открывается только для чтения. In-place изменение запрещено.

## Быстрая команда

Для одной замены:

```powershell
lsptool iso-replace `
  game.iso `
  PSP_GAME/USRDIR/DATA/sc.cpk `
  sc-patched.cpk `
  game-patched.iso `
  --expect-source-sha256 SOURCE_SHA256 `
  --expect-original-size 2318336 `
  --expect-original-sha256 ORIGINAL_SC_SHA256 `
  --expect-replacement-size 2457600 `
  --expect-replacement-sha256 PATCHED_SC_SHA256 `
  --json iso-rebuild.json
```

Для связанного набора файлов удобнее использовать манифест:

```powershell
lsptool iso-apply-manifest game.iso iso-patch-manifest.json game-patched.iso `
  --json iso-rebuild.json
```

Готовый скрипт:

```powershell
.\scripts\rebuild-game-iso.ps1 `
  -IsoPath C:\Games\LuckyStar.iso `
  -ManifestPath .\iso-patch-manifest.json `
  -OutputIso .\LuckyStar-patched.iso
```

Linux:

```bash
./scripts/rebuild-game-iso.sh \
  /games/LuckyStar.iso \
  ./iso-patch-manifest.json \
  ./LuckyStar-patched.iso
```

## Формат манифеста

Схема: `lucky-star-psp.iso-patch-manifest.v1`. Формальное описание для редакторов и CI находится в `schemas/iso-patch-manifest.schema.json`.

```json
{
  "schema": "lucky-star-psp.iso-patch-manifest.v1",
  "expectedSourceSha256": "64-hex-characters",
  "replacements": [
    {
      "isoPath": "PSP_GAME/SYSDIR/EBOOT.BIN",
      "sourceFile": "patched/EBOOT.BIN",
      "expectedOriginalSize": 1470512,
      "expectedOriginalSha256": "64-hex-characters",
      "expectedReplacementSize": 1470173,
      "expectedReplacementSha256": "64-hex-characters"
    },
    {
      "isoPath": "PSP_GAME/USRDIR/DATA/sc.cpk",
      "sourceFile": "patched/sc.cpk",
      "expectedOriginalSize": 2318336,
      "expectedOriginalSha256": "64-hex-characters",
      "expectedReplacementSize": 2457600,
      "expectedReplacementSha256": "64-hex-characters"
    },
    {
      "isoPath": "PSP_GAME/USRDIR/DATA/lt.bin",
      "sourceFile": "patched/lt.bin",
      "expectedOriginalSha256": "64-hex-characters",
      "expectedReplacementSize": 327680,
      "expectedReplacementSha256": "64-hex-characters"
    }
  ]
}
```

`sourceFile` обязан быть относительным путём внутри каталога манифеста. Абсолютные пути, `..`, symbolic links и reparse points отклоняются.

`expectedSourceSha256`, `expectedOriginalSize`, `expectedOriginalSha256`, `expectedReplacementSize` и `expectedReplacementSha256` являются preconditions. Для реальной поставки их рекомендуется указывать всегда: инструмент откажется применять набор к другой ревизии образа, к уже изменённому исходному файлу или к неверной версии подготовленного результата.

## Как строится новый образ

1. Полностью разбирается исходная ISO9660-структура.
2. Проверяются источник и все исходные целевые файлы.
3. Замены сортируются по каноническому внутреннему пути.
4. Исходный образ потоково копируется во временный файл с повторным SHA-256.
5. Новые файлы размещаются после исходного образа на границах секторов 2048 байт.
6. Для каждой цели обновляются обе endian-копии extent LBA и длины в directory record.
7. В Primary Volume Descriptor обновляется обеими endian-копиями число блоков тома.
8. Временный образ заново разбирается тем же строгим ISO9660-парсером.
9. Побайтно сравнивается весь исходный префикс, кроме точно разрешённых 8-байтовых полей PVD и directory records.
10. Проверяются SHA-256 замен, число записей и неизменность всех остальных файлов и метаданных.
11. Считается SHA-256 всего результата, после чего временный файл атомарно становится целевым.

Старые payload-байты внутри образа не удаляются: directory record начинает ссылаться на новую копию файла. Это намеренно консервативная стратегия — она не перемещает существующие файлы и не перестраивает каталоги или path tables.

## Поддерживаемый безопасный профиль

Разрешена замена обычного файла, который:

- имеет одну directory record;
- состоит из одного extent;
- не использует extended attribute blocks;
- находится в основной ISO9660-иерархии;
- имеет размер результата не более `UInt32.MaxValue` и настроенного лимита;
- укладывается в лимиты одной замены, суммарного объёма замен и итогового образа.

Сборка отклоняется, если образ содержит:

- Supplementary Volume Descriptor;
- Volume Partition Descriptor;
- зарезервированный тип volume descriptor;
- многодисковую структуру;
- нестандартный logical block size;
- interleaved-файл;
- повреждённую или неоднозначную структуру каталогов;
- multi-extent целевой файл.

Читать и извлекать multi-extent файлы инструмент умеет, но их замена пока намеренно заблокирована: корректное обновление всей цепочки directory records требует отдельного проверяемого алгоритма.

## Детерминированность

При одинаковых входном ISO, манифесте и файлах замены итоговые байты ISO совпадают. Порядок записей в JSON-манифесте не влияет на размещение: используется сортировка по внутреннему ISO-пути.

Независимый Python-oracle создаёт эталонный образ с двумя заменами. Ожидаемый SHA-256:

```text
9d6afa89d328df29dfe4d2807990900a8fb0e8c802861b96c316e0fe35cd62c3
```

## Что этот модуль не делает

- не создаёт и не распространяет образ игры;
- не преобразует CSO в ISO;
- не подписывает и не шифрует модифицированный EBOOT;
- не доказывает совместимость патча с другой ревизией игры;
- не запускает игру и не заменяет проверку в PPSSPP/на PSP.

Для распространения перевода следует выпускать patch-only пакет или набор изменённых ресурсов, а не готовый ISO.
