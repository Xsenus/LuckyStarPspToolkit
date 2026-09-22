# CPK/ITOC в 0.13.0: чтение, сохранность и пределы

## API и владение памятью

`CriCpkArchive.Parse(ReadOnlySpan<byte>, FileLimits?)` сначала выполняет общий
metadata preflight, затем создаёт независимый исходный снимок и изменяемые записи.
Он предназначен для редактирования, а не для максимально дешёвого списка файлов.

`CriCpkArchive.Inspect(ReadOnlySpan<byte>, FileLimits?)` возвращает
`CriCpkInspection` и read-only список `CriCpkFileInfo`: ID, абсолютный offset,
packed/extracted размеры, признак CRILAYLA. Payload-массивов в результате нет.

`CriCpkArchive.Extract(ReadOnlySpan<byte>, ushort id, bool packed=false, FileLimits?)`
проверяет тот же каталог и копирует/декодирует только выбранный файл.
Массив результата принадлежит вызывающему. Проверка заголовка CRILAYLA при Inspect
не равна полной проверке битового потока; она выполняется при Extract/Decompress.

Входной span нельзя параллельно менять во время операции. `Inspect` не удерживает
span: его descriptors применимы к тем же неизменённым исходным байтам. Это read-only
по данным API, но не файловая транзакция или защита от внешнего конкурентного писателя.

## Пример C#

```csharp
byte[] input = BinaryUtilities.ReadAllBytesBounded("sc.cpk");
CriCpkInspection catalog = CriCpkArchive.Inspect(input);
foreach (CriCpkFileInfo file in catalog.Entries)
    Console.WriteLine($"{file.Id}: offset={file.Offset}, bytes={file.PackedSize}");
byte[] scenario = CriCpkArchive.Extract(input, 0);

CriCpkArchive editable = CriCpkArchive.Parse(input);
editable.ReplaceEntry(0, scenario); // same bytes => exact no-op
byte[] rebuilt = editable.Build().Data;
```

Пространства имён: `LuckyStarPspToolkit.Formats.Common` и `.Cri`.
Для транзакционного сохранения используйте `AtomicFile`, а не запись поверх оригинала.

## Инварианты

Поддержан ITOC-only профиль с непрерывными ID от 0, DataL (16-битные размеры)
и DataH (расширенные размеры), выравниванием power-of-two до 1 МиБ.
Проверяются один header row, один ITOC row, таблицы классов, число Files,
отсутствие duplicate ID и выходов за пределы. Полные payload-копии создаются
только после проверки всех ranges. Unsupported дополнительные активные индексы
TOC/ETOC/GTOC отклоняются, а не игнорируются при rebuild.

Новые коды:

| Код | Причина / действие |
|---|---|
| CPK_INPUT_LIMIT | Вход больше MaximumInputBytes; не повышайте лимит без оценки памяти |
| CPK_METADATA_LIMIT | Header+ITOC превышают общий бюджет metadata |
| CPK_PACKET_RANGE | Объявленная длина пакета вне входного массива |
| CPK_HEADER_OVERLAP | Header перекрывает ITOC |
| CPK_FILE_COUNT | Files/FilesL/FilesH не согласованы с дескрипторами |
| CPK_METADATA_SCHEMA | Неизвестные поля классов не совместимы; нужен отдельный анализ схемы |
| CPK_ENTRY_MUTATION | Прямое изменение записей нарушило ID/число/положительный размер |
| CPK_UNSUPPORTED_INDEX | Обнаружен дополнительный индекс, который нельзя безопасно переразместить |

## No-op и реальные замены

No-op — отдельная копия всех исходных байтов, не тот же mutable массив.
Прямые изменения PackedData/ExtractSize перепроверяются; нет cached-флага, способного
пропустить замену. При no-op также действуют текущие FileLimits, даже если архив
был первоначально разобран с более мягкими ограничениями.

При настоящей замене unknown fields переносятся по имени, а не по позиции.
Перестановка столбцов в DataH не меняет смысл DataL-значений. Потенциальная потеря
поля останавливает Build. Требования различающихся типов не угадываются и не
обходятся преобразованием «на всякий случай». Shared constants с разными значениями
переводятся в per-row; исходные строки сохраняются.

Verification разбирает layout нового CPK и сравнивает payload spans и ExtractSize
с ожидаемыми записями. Это не отменяет отдельную проверку сценария или игры.

## Пределы текущей реализации

CLI по-прежнему читает полный CPK в byte[]. Нет потоковой обработки CPK > 2 ГиБ;
ISO-модуль — отдельный потоковый код. Нет CRILAYLA-компрессора и поддержки всех
вариантов CRI CPK. Оригинальный sc.cpk заказчика в этой итерации недоступен.
