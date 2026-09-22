# Документация Lucky Star PSP Toolkit

Этот проект содержит инструменты обработки игровых ресурсов, клиент с проверкой
лицензии и сервер с браузерной панелью владельца. Исходники опубликованы отдельно
от рабочих ключей, данных владельца и оригинальных игровых материалов.

Начните с раздела, соответствующего вашей задаче:

| Задача | Руководство |
|---|---|
| Понять готовность проекта и оставшиеся условия приёмки | [Матрица требований и приёмки](CUSTOMER_ACCEPTANCE_RU.md) |
| Собрать и впервые запустить клиент | [Запуск и сборка](USAGE_RU.md) |
| Запустить сервер и выдать доступ | [Инструкция владельца](LICENSE_OWNER_RU.md), [React-панель](ADMIN_WEB_RU.md), [эксплуатация VPS](VPS_DEPLOYMENT_RU.md) |
| Активировать полученный клиент | [Инструкция клиента](LICENSE_CUSTOMER_RU.md) |
| Работать над переводом | [Рабочий процесс](WORKFLOW_RU.md), [CLI](CLI_REFERENCE.md) |
| Разрабатывать и проверять код | [Разработка](DEVELOPMENT.md), [Тестирование](TESTING.md) |
| Выпустить пакеты или опубликовать изменения | [Сборка и релизы](BUILD_AND_RELEASE_RU.md), [GitHub](GITHUB_PUBLISH_RU.md) |
| Разобраться с ошибкой | [Диагностика](TROUBLESHOOTING.md), [поддержка](../SUPPORT.md) |

## Игровые форматы и алгоритмы

- [Общая архитектура](ARCHITECTURE_RU.md) и [краткая английская версия](ARCHITECTURE.md).
- [Поддерживаемые форматы](FORMATS.md), [CPK/ITOC/@UTF/CRILAYLA](CPK_CODEC_RU.md),
  [сценарии и пересчёт смещений](SCRIPT_CODEC_RU.md).
- [Профиль EBOOT и VWF](EBOOT_VWF_PATCH_RU.md), [перестройка ISO](ISO_REBUILD_RU.md).
- [Измерения CPK](CPK_PERFORMANCE_RU.md) и [измерения сценариев](PERFORMANCE_RU.md).
- [API C#](API_REFERENCE.md), [примеры](../examples/README.md), [схемы JSON](../schemas/).
- [Исторический анализ файлов заказчика](CUSTOMER_DATA_REPORT_RU.md),
  [демонстрация и игровая приёмка](CUSTOMER_DEMO_RU.md).

## Лицензирование и администрирование

- [Архитектура админки](ADMIN_ARCHITECTURE_RU.md),
  [пароль, MFA и конкурентные запросы](ADMIN_AUTH_CONCURRENCY_RU.md).
- [Поиск, страницы и счётчики](ADMIN_QUERIES_RU.md),
  [измерения запросов](ADMIN_QUERY_PERFORMANCE_RU.md).
- [Резерв на 1–168 часов](RESERVE_ACCESS_RU.md),
  [отзыв, сброс устройства и сессии](RESERVE_SESSION_HARDENING_RU.md),
  [измерения резерва](RESERVE_PERFORMANCE_RU.md).
- [API лицензирования](LICENSE_API.md), [миграция](LICENSE_MIGRATION_RU.md),
  [модель защиты](LICENSE_SECURITY_RU.md), [усиление проверок](LICENSE_HARDENING_RU.md),
  [измерения лицензирования](LICENSE_PERFORMANCE_RU.md).
- [Политика безопасности](../SECURITY.md) и [русские пояснения](SECURITY_RU.md).

## Проверки, история и происхождение

- [Матрица текущей приёмки](CUSTOMER_ACCEPTANCE_RU.md) отделяет свежие проверки
  от отсутствующих игровых данных и условий, которые ещё нужно проверить.
- [Отчёт исходного комплекта](TEST_REPORT_RU.md) и [validation](../validation/README.md)
  сохраняют фактические результаты прежних итераций. Даты и версии в них существенны:
  старый PASS не означает повторный запуск на текущем коммите.
- [CHANGELOG](../CHANGELOG.md), [история релизов](RELEASE_NOTES_RU.md),
  [0.19.1](RELEASE_NOTES_0.19.1_RU.md), [0.19.0](RELEASE_NOTES_0.19.0_RU.md), [0.18.0](RELEASE_NOTES_0.18.0_RU.md),
  [0.17.0](RELEASE_NOTES_0.17.0_RU.md), [план развития](ROADMAP_RU.md).
- [Сторонние материалы](../THIRD_PARTY_NOTICES.md), [область лицензии](LICENSING.md),
  [технические источники](SOURCES.md), [аудит публичной истории](PUBLICATION_AUDIT_RU.md).
- [Правила работы с кодом](../AGENTS.md), [участие в разработке](../CONTRIBUTING.md),
  [правила общения](../CODE_OF_CONDUCT.md).

Документы `FINAL_SUMMARY_RU.md`, `MESSAGE_TO_CUSTOMER_RU.txt` и заметки отдельных
версий сохранены как история передачи проекта. Для первого запуска используйте
руководства из первой таблицы. Секреты сервера и данные TOTP в эту документацию
не добавляются.
