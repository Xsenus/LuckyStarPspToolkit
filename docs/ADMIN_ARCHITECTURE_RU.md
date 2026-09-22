# Архитектура React/MFA/reserve

Браузер → HTTPS admin-origin → Nginx → 127.0.0.1:17842 LicenseWebServer → LicenseAuthority.
Клиент → HTTPS license-origin → Nginx → 127.0.0.1:17840 LicenseHttpServer → та же LicenseAuthority.
Закрытый owner CLI → SSH tunnel → 127.0.0.1:17841 bearer API. Приватный signer находится только у authority.

## Browser API

GET `/api/session`, `/api/licenses`, `/api/audit`, `/api/reserve` требуют непросроченную сессию.
POST `/api/login` требует точного Origin и пароль+второй фактор; cookie выдаётся только после сохранения
расхода одноразового фактора. POST `/api/reauth`, `/api/logout`, `/api/issue`, `/api/change`,
`/api/reserve/policy`, `/api/reserve/issue`, `/api/reserve/revoke` требуют cookie и X-CSRF-Token.
Reserve mutations, бессрочная выдача, permanent/revoke/reset-device требуют свежего MFA.
API не принимает bearer-токен владельца из браузера и не возвращает его в JavaScript.

Request body ограничен 32 КиБ и 15 секундами. В 0.19.0 выделены HTTP-полосы: 2 login, 2 reauth, 8 остальных запросов; PBKDF2 максимум 1+1 без общей блокировки сессий. Cookie/CSRF проверяются до и после чтения тела.
Статические ресурсы выдаются из фиксированного allowlist и читаются один раз при старте.
Не существует catch-all доступа к пути файла, конфигу authority или каталогу deployment.
С 0.18.0 лицензии/резервы читаются серверными страницами по 25, API допускает 1–100.
См. ADMIN_QUERIES_RU.md. Многопользовательский RBAC не реализован.

## Reserve API

POST `/v1/reserve`: `{ grantId, request }`, где request — обычная установка-подпись
проверки `action=check`, с challenge и свежим clientNonce. Authorize и проверка grant выполняются
под общей блокировкой authority: между проверкой права и подписью не может вклиниться отзыв.
Возвращается LeaseResponse с LSPR1. Префикс/подпись отличны от короткого обычного LSP1.

Внутренняя схема license database теперь 3: ReserveEnabled, ReserveEpoch, ReserveGrants.
Рекорды grants неизменяемые; публичные представления копируются. Количество grants ограничено 10000.
Политика и отзывы записываются в существующий журнал без секретов. Выданные токены не кладутся в базу.
Обычное обновление подписи не пересериализует полную БД: сохраняется bounded clock checkpoint.
Клиентское чтение кэша не обновляет authoritative ValidUntil и не подписывает собственный доступ.

## Два типа восстановления

Восстановление MFA и аварийный доступ к ПО — независимые trust domains. Recovery code не работает
в activate. Installation signature не заменяет подпись issuer. UUID grant не подписывает ничего.
Снижение срока родительской лицензии/отключение policy нельзя сообщить отключённому клиенту
без канала связи — верхняя граница автономности и есть предел задержки отзыва.

## Provenance и границы проверки

React browser production modules взяты из official CI oss-stable-semver build, commit
59aff3e18cb5b3a336c280bbfa57ec37999511b9, run 35328185246, artifact 10539334300.
Проверен SHA-256 скачанного artifact и каждого vendor-модуля. Это не утверждение идентичности npm.
В runtime отсутствуют CDN, React Server Components, package-install hooks и eval.
Используется Node build без внешних зависимостей с сохранением MIT notices.

Native Playwright test предусмотрен scripts/web_reserve_integration.py. В текущей среде Chromium
запрещает навигацию URL системной политикой. Поэтому дополнительно существует явно отмеченный
`--browser-bridge`: реальный DOM/React, реальный HTTP backend и процессы клиента, но transport/cookie
для fetch обеспечивает Python HTTP client. Такой прогон НЕ проверяет native browser origin/cookie/CSP
и фактическую запись браузерных скачиваний. В CI этот флаг не применяется.

## Bounded reads 0.18.0

POST /api/licenses/query, /api/reserve/query и /api/license/get используют ту же
owner-session/Origin/CSRF boundary. Токен страницы не авторизует запрос; он проверяется
HMAC до JSON parsing и связывается с запросом/ревизией/исходным временем. Процессная
ротация курсорного ключа не меняет издателя лицензий. Детали — ADMIN_QUERIES_RU.md.

Конкурентная проверка владельца и модель отказов: [ADMIN_AUTH_CONCURRENCY_RU.md](ADMIN_AUTH_CONCURRENCY_RU.md).
