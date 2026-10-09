# Проверки и сохранённые результаты

Текущая версия — 0.21.0. Штатный конвейер: `python scripts/build_release.py --compile-only`.
Для клиентской Windows-сборки: `python scripts/build_release.py --rids win-x64 --license-trust /private/client-trust.json`.
Без публичного профиля издателя клиент остаётся заблокированным.

В Git сохраняются скрипты проверок, синтетические эталоны и компактные
исторические отчёты. Старые консольные `.log` удалены из текущего дерева;
их пути внутри исторических JSON обозначают файлы прежних запусков,
а не файлы, доступные после очистки. История Git сохраняет прежние версии.
Новые журналы и бинарные результаты генерируются локально и не публикуются.

`python scripts/release_validate.py` сохраняет реальные результаты в `artifacts/validation/`. Коды: 0 — всё запрошенное пройдено; 2 — частичные данные / есть NOT RUN; 1 — есть ошибка. Не объявляйте exit 2 успешной проверкой программы. `--require-dotnet` превращает отсутствие обязательной .NET-проверки в ошибку.

Все старые versioned reports в этой папке — исторические. Проверка preserved baseline не исполняет код на оригинальном EBOOT. Python oracle не является C#-тестом.

## Исторические сведения

# Validation materials

## Полный release gate

```bash
python scripts/release_validate.py --customer-archive /private/archive.zip
```

Строгий режим, требующий установленный .NET SDK:

```bash
python scripts/release_validate.py --customer-archive /private/archive.zip --require-dotnet
```

Команда создаёт:

- `release-validation-<version>.json` — passed/failed/skipped и точные команды;
- `STATUS_RU.txt` — компактный фактический статус;
- versioned logs для документации, source, форматов, репозитория и customer checks.

Пропущенная проверка не считается пройденной.

## Документация C#

```bash
python scripts/document_csharp.py --check --include-tests
python validation/validate_csharp_docs.py
python scripts/generate_api_reference.py --check
```

Проверяются все именованные типы, методы, конструкторы и локальные функции, а также `<param>`, `<typeparam>` и `<returns>`.

## Репозиторный аудит

```bash
python validation/audit_repository.py
```

Проверяет Git-дерево на секреты, неподтверждённые binaries, игровые данные вне synthetic fixtures и совпадения SHA-256 с файлами заказчика.

```bash
python validation/validate_customer_baseline.py
```

Проверяет согласованность сохранённых результатов реальной проверки customer EBOOT с текущими профилями. Это не новый прогон приватного ZIP; отчёт содержит `rawCustomerArchiveReprocessed: false`.

## Независимые бинарные проверки

- `static_validate.py` — UTF-8/LF, JSON/XML, project graph, token-aware C# scan, version/profile/contracts и публикационная готовность.
- `validate_customer_data.py` — независимый PSP PRX/SFO/customer archive oracle.
- `validate_vwf_patch.py` — независимая расшифровка customer EBOOT, 133 VWF-слова, точный output hash, script table/heap и отрицательные случаи.
- `validate_font_fixtures.py` — `lt.bin`, checksum, BDF, 66 русских букв, metadata/padding и RGBA/PNG.
- `validate_iso_fixture.py` — независимый ISO9660 parser, multi-extent и повреждённые варианты.
- `validate_iso_rebuild.py` — точный rebuilt ISO, both-endian metadata, padding и неизменность нецелевых данных.

Сначала создаются детерминированные fixtures:

```bash
python scripts/reference_oracle.py
python validation/validate_font_fixtures.py
python validation/validate_iso_fixture.py
python validation/validate_iso_rebuild.py
```

Проверка приватного архива:

```bash
python validation/validate_customer_data.py /private/archive.zip \
  --output validation/customer-validation-<version>.json

python validation/validate_vwf_patch.py /private/archive.zip \
  --output validation/vwf-patch-validation-<version>.json
```

Не коммитить и не упаковывать приватный архив, расшифрованный ELF, оригинальный ISO, CPK, PMF, SFO или игровой шрифт.
