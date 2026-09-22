# CPK: сравнение C# 0.12.0 и 0.13.0

22 сентября 2026 проведён один синтетический микротест в одной среде:
PowerShell 7.7.0-preview.4, .NET 11.0.0-preview.6.26359.118, Roslyn/C#13 Release.
`DOTNET_TieredCompilation=0`, 3 прогрева, 7 замеров на каждую DLL, отдельные процессы.
Это не штатная .NET 9 SDK сборка, не Windows и не настоящий архив игры.

Один и тот же `CpkScaleFixture.cs` скомпилирован против двух Formats DLL.
Вход содержит два файла: 513 байт и 8 МиБ. Первый заменяется 8192 нулями.
Создание входа, Parse и подготовка замены не входят в измерение Build.
До серии измерений выполнен прогрев; перед каждым измерением GC.Collect и
WaitForPendingFinalizers. Stopwatch-объект входит в счётчик выделений у обеих версий.
SHA-256 вычисляется после фиксации времени/счётчика. Приведены медианы.

| Показатель | 0.12.0 | 0.13.0 |
|---|---:|---:|
| Новые управляемые выделения, байт | 25,235,088 | 8,448,776 |
| Build, мс | 3.2966 | 1.7185 |
| Размер результата, байт | 8,400,896 | 8,400,896 |

Выделения снизились в 2.9868 раза (на 66.52%). Это только разница
`GC.GetAllocatedBytesForCurrentThread`: не RSS, не размер живой кучи и не native allocations.
Время — наблюдение одной короткой серии; его нельзя обещать как ускорение всей программы.
Метод счётчика: https://learn.microsoft.com/en-us/dotnet/api/system.gc.getallocatedbytesforcurrentthread?view=net-9.0

Входы и результаты совпали по SHA-256 во всех 14 замерах.

```text
input:  0d0336d88d1ec5b7353b8f4de12e233755b283395b62187ddbcc1be680132e1c
output: 81f6f48b594598ef81cca283a9c900a4715392b8930b29437e66a664ed06b8be
```

В полном комплекте: `reports/cpk-comparison/baseline.json`, `updated.json`,
`methodology.json` с хэшами DLL и тестового кода. DLL не распространяются.
Исходный baseline: commit 0f5fb516c5dbb9e8e8eff2fa325b4bf08fe2aa1f.

## Подтверждённые ошибки прежней версии

| Проверка тем же кодом | 0.12.0 | 0.13.0 |
|---|---|---|
| No-op с nonzero padding/trailer | Байты менялись | Полное совпадение |
| Перенос unknown Tag low→high | high-tag вместо low-tag | low-tag сохранён |
| Перенос binary Blob low→high | Пустое значение | 00A900F1 сохранено |
| ItocOffset = UInt64.MaxValue | OverflowException | ToolkitException:CPK_ITOC_OFFSET |
| Header Files = 999 при двух записях | Принят | ToolkitException:CPK_FILE_COUNT |

Два случая потери Tag/Blob относятся к одному дефекту migration.

## Воспроизведение

После успешной штатной сборки:

```powershell
$env:DOTNET_TieredCompilation = '0'
dotnet run --project tests/LuckyStarPspToolkit.Formats.SelfTests -c Release --no-build -- --cpk-benchmark
Remove-Item Env:DOTNET_TieredCompilation
```

Диагностическое сравнение старой и новой заранее скомпилированных DLL:

```powershell
$env:DOTNET_TieredCompilation = '0'
./scripts/benchmark_cpk.ps1 -Root . -ReferenceDirectory <папка-с-Formats.dll> -Out artifacts/cpk-bench-unique
# -Probe запускает только наблюдение четырёх исторических дефектов.
Remove-Item Env:DOTNET_TieredCompilation
```

Runner использует только уже установленный доверенный PowerShell/Roslyn, ничего
не скачивает. Каждый Out должен быть новым каталогом в artifacts.
