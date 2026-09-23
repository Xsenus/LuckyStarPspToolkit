# Публичный GitHub и выпуск пакетов

Владелец проекта выбрал публичную публикацию исходников. В репозитории находятся
код, документация, профили и генераторы синтетических тестов. Рабочее состояние
издателя и ключи хранятся отдельно. Публичность кода позволяет собрать изменённый
клиент, поэтому лицензионная система управляет штатным дистрибутивом и доступом
к вашему серверу, не обещая невозможности изменения локального приложения.

## Перед первым push

Прочитайте [область лицензии](LICENSING.md),
[сторонние notices](../THIRD_PARTY_NOTICES.md) и [аудит истории](PUBLICATION_AUDIT_RU.md).
Проверяйте файлы, которые будут отправлены, включая все коммиты импортированной истории.

```powershell
python validation/audit_repository.py
git diff --check
git status --short
git remote -v
```

Если проект восстанавливается из owner-комплекта, клонируйте bundle в новую
пустую папку. Не распаковывайте поверх другого проекта. Удаление `origin`
допустимо только когда он указывает на локальный bundle и не является нужным
удалённым репозиторием. Затем добавьте фактический URL вашего GitHub-репозитория
и отправьте проверенную ветку `main`.

Не публикуйте owner ZIP целиком: он не является исходным деревом Git.
`authority.json`, `master.pass`, `owner-connection.json`, `web-account.json`,
`web-enrollment.json`, recovery-коды, access keys, база лицензий и игровые файлы
не должны находиться в commits или публичных Actions artifacts.

## Что делает CI

Обычный push и pull request запускают:

1. Windows/Linux: Node-тесты, сборку закреплённых React-модулей, штатную сборку
   .NET 9, C# self-tests, независимые validators, licensing lifecycle и synthetic demo.
2. Linux: отдельный Chromium integration для владельца, MFA и резерва.
3. Упаковку проверенного preview и сохранение отчётов Actions.

По умолчанию preview без издателя заблокирован. Интеграционный тест создаёт
собственного временного loopback-издателя и проверяет реально подписанные
лицензии; это не настройка вашего production-сервера.

`owner-private-packages` имеет условие `github.event.repository.private` и в
публичном репозитории пропускается. Инструменты владельца собирайте локально
через `python scripts/build_license_owner.py --rids linux-x64,win-x64`.
Отсутствие owner ZIP в публичном release ожидаемо.

## Клиентский релиз с вашим издателем

В repository variable **LSP_LICENSE_TRUST_JSON** поместите содержимое только
публичного `client-trust.json`: открытый ключ, issuer и HTTPS URL. Это не secret
издателя. Workflow отклоняет `developmentLoopback` и закрытые поля.

Локально проверьте профиль и соберите клиент:

```powershell
python scripts/license_build_profile.py C:\LspOwner\client-trust.json
python scripts/build_release.py --rids win-x64 --license-trust C:\LspOwner\client-trust.json
```

После успешного CI создайте и отправьте тег `v<VERSION>` для проверенного коммита.
`release.yml` повторит Windows/Linux проверки и опубликует **engineering prerelease** с
клиентскими ZIP и SHA-256. Без корректного production trust profile job
остановится. Не меняйте уже опубликованный тег для исправления ошибки: выпускайте
новую patch-версию.

Перед выдачей клиенту проверьте `license build-info`, активацию на реальном HTTPS
сервере и условия [матрицы приёмки](CUSTOMER_ACCEPTANCE_RU.md). Успешная упаковка
не утверждает готовность перевода или прохождение игры.
