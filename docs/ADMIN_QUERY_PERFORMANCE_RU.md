# Измерение owner-страниц · 0.18.0 против 0.17.0

Настоящее выполнение одного C# harness против двух отдельно скомпилированных версий.
Среда: PowerShell/Roslyn .NET 11.0.0-preview.6.26359.118, diagnostic Release compilation.
Это НЕ штатный .NET9/MSBuild и не измерение настоящего VPS/HTTPS/браузерного транспорта.

Синтетическая база: 2000 лицензий, 4000 записей установок, несколько выдач в одну секунду.
Пути отдают разные wire-модели намеренно: прежний полный список с устройствами и новая
страница 25 summary-строк. Выбранные 25 UUID совпали с независимым полным sort/take в
обоих прогонах. Это не побайтовая идентичность двух JSON. Статусы тестовых лицензий
активные; прочие статусы покрыты unit tests.

| Показатель | 0.17.0 | 0.18.0 |
|---|---:|---:|
| UTF-8 JSON body одного ответа | 1684891 байт | 5016 байт |
| Медиана managed allocations за 20 чтений | 53398400 байт | 273280 байт |
| Видимые UUID | Тот же набор из 25 | Тот же набор из 25 |

3 прогревочные серии, 7 измеряемых серий по 20 операций. GC.GetAllocatedBytesForCurrentThread
снимается вокруг чтения authority и JSON serialization. Фиксированное тестовое время;
стоимость создания базы/фикстуры, initial full reference, network headers, gzip/TLS,
парсинг/рендеринг React и реальная запись clock-checkpoint в этот диапазон не включены.
Reflection invocation нового метода входит в замер. Первая страница повторяется,
поэтому cursor-продолжения и поздние страницы в этом замере не измеряются.

Body сократилось примерно в 335.9 раза, измеренные
выделения — в 195.4 раза. Это эффект удаления полной выдачи/копирования устройств,
НЕ такое же ускорение всех функций. Это не RAM процесса, native crypto, живой heap
или throughput. Wall-time samples приложены без универсального коэффициента ускорения.

```powershell
pwsh -NoProfile -File scripts/benchmark_owner_queries.ps1 -Root . -ReferenceDirectory <old-assemblies> -Out artifacts/page-old
pwsh -NoProfile -File scripts/benchmark_owner_queries.ps1 -Root . -ReferenceDirectory <new-assemblies> -Out artifacts/page-new
```

Один OwnerQueryComparisonFixture.cs компилируется в обоих случаях. Harness обнаруживает
новый метод reflection и не подменяет реализацию старой версии. Не используйте
неподтверждённые DLL. Диагностические assemblies/runtime не входят в поставку.
Сырые результаты и хэши: reports/owner-query-comparison/. Ограничения: ADMIN_QUERIES_RU.md.
