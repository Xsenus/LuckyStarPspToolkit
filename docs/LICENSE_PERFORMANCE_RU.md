# Замеры LicenseStore 0.14.0 → 0.15.0

Одинаковый `tests/LuckyStarPspToolkit.Licensing.SelfTests/LicenseScaleFixture.cs` скомпилирован
против реально собранных библиотек двух версий. SHA-256 harness:
`d2ffadcba6781edf705088302886ca910eeb047518458923c741acafe7f82dee`.

Среда: `.NET 11.0.0-preview.6.26359.118`, Linux, Release-оптимизация Roslyn/C#13.
Это не MSBuild/.NET 9 SDK, не тест Windows и не production TLS. Исходные журналы:
`reports/license-comparison/`; метаданные и SHA-256 библиотек — `comparison.json`.

## Набор данных и граница замера

2000 синтетических лицензий, 1000 записей установок, 3 прогрева и 7 измеряемых операций.
Каждая операция изменяет label и выполняет весь LicenseStore.Change: копии, проверки,
JSON-сериализацию, HMAC, приватный временный файл, flush и замену снимка.
Начальная генерация/загрузка не измеряются. Использована schema 1 для одинакового состояния
обеих версий; схема хранилища сама по себе не меняет сериализацию/копирование коллекций.
Новые данные ключей, подписывание grant, network/TLS, checkpoint и heartbeat не измеряются.

| Метрика | 0.14.0 | 0.15.0 |
|---|---:|---:|
| Медиана GC allocated bytes | 15,008,792 | 8,641,224 |
| Семантический payload, байт | 1,289,129 | 1,289,129 |
| Медиана времени, мс, локальный запуск | 18.8887 | 11.4071 |

Новые управляемые выделения уменьшились на **42.43%**.
Итоговое семантическое содержимое совпадает:
`cf66eae06065ed05418677760da88c93a80e1a14fde438da4a84b7ccc49437c9`.

GC.GetAllocatedBytesForCurrentThread измеряет выделения текущего потока, не RSS, не
пиковую живую кучу и не нативную память. Уже созданные строковые значения могут разделяться
неизменяемыми снимками. Время включает файловый flush, подвержено шуму прогрева/JIT/диска;
семь наблюдений не обосновывают гарантию ускорения сервера или количества клиентов в секунду.
Повторные преобразования JSON удалены, но запись entitlement всё ещё O(N) по размеру базы.

## Повторение разработчиком

Нужен доверенный локальный PowerShell/Roslyn host и отдельно собранные библиотеки обеих
версий. Скрипт ничего не скачивает, выходной каталог обязан быть новым внутри artifacts.

```powershell
pwsh -NoProfile -File scripts/benchmark_licensing.ps1 -Root . -ReferenceDirectory C:\Builds\014 -Out artifacts/license-old
pwsh -NoProfile -File scripts/benchmark_licensing.ps1 -Root . -ReferenceDirectory C:\Builds\015 -Out artifacts/license-new
pwsh -NoProfile -File scripts/benchmark_licensing.ps1 -Root . -ReferenceDirectory C:\Builds\015 -Out artifacts/license-probe -Probe
```

Для сопоставимости используйте один и тот же fixture, host и настройки в обеих командах.
Не исполняйте сторонние DLL, которым не доверяете. Старые DLL в поставку не включены;
их можно собрать из сохранённого Git-тега. Штатная приёмка net9 выполняется другими build-скриптами.
Первичная документация счётчика: https://learn.microsoft.com/en-us/dotnet/api/system.gc.getallocatedbytesforcurrentthread
