# 0.16.0 — React owner console, MFA и ограниченный резерв

Последовательное продолжение 0.15.0, без пересоздания issuer и сброса истории.
Добавлены WebAdminAuthentication, LicenseWebServer, ReserveAuthority, ReserveCrypto и ReserveCache.
Третий loopback listener отделён от owner bearer API. Клиентский протокол расширен `/v1/reserve`.
Серверные grants максимум 168 часов, epoch retirement, отдельный отзыв, parent/device binding.
Клиентское состояние подписано установкой; явные онлайн-отказы сохраняются и не маскируются сетью.
Команды `license reserve-enable --id` и `license reserve-disable` доступны рядом с прежней активацией.

React panel: выдача с idempotent receipt, продление/отзыв/установки, поиск/страницы, reserve policy,
установки и аудит. Password+TOTP/recovery, CSRF, exact Origin, HttpOnly cookie, idle/absolute/fresh MFA.
Добавлены C# regression tests для reserve и MFA; фактический результат — в TEST_REPORT_RU.md.
Новые типы и методы покрыты XML/JSDoc-комментариями.

Owner build теперь собирает web/ и включает его в owner ZIP. Node 22+ нужен только при сборке.
Native browser CI и отдельный private-only owner package workflow подготовлены.
Схема БД 2→3 автоматически; старый server0.15 не понимаетновую схему. Не переинициализировать authority.
Стандартный .NET9 publish/Windows/production HTTPS и реальная игра не выданы за пройденные проверки.
