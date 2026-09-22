# GitHub и лицензирование 0.19.0

Полный архив и Git-история — только владельцу. Публикация исходников позволяет снять клиентский контроль.
Изучите THIRD_PARTY_NOTICES.md перед публичным распространением. Реальная проверка ваших GitHub jobs не выполнялась.

```powershell
git clone .\git\LuckyStarPspToolkit-0.19.0.git.bundle LuckyStarPspToolkit
cd LuckyStarPspToolkit
git remote remove origin
git remote add origin <адрес-вашего-репозитория>
git push -u origin main
```

CI создаёт заблокированный preview и отдельно проверяет лицензирование на случайном временном loopback-издателе.
Для клиентских релизов создайте repository variable **LSP_LICENSE_TRUST_JSON** со всем содержимым вашего
публичного client-trust.json. Это открытый ключ/URL, не authority.json, не master.pass и не owner-connection.json.
Workflow валидирует профиль и отвергает developmentLoopback/private fields.

После успешных Windows/Linux проверок:

```powershell
git push origin v0.19.0
```

Получится draft prerelease с customer-пакетами. Без публичной настройки издателя release job остановится,
а не выдаст незаметно открытый EXE. Исходные и owner-пакеты не добавляются автоматически в публичный релиз.
Встроенный профиль можно проверить `lsptool license build-info`; активация на вашем реальном HTTPS-сервере
и игровая приёмка являются дополнительными проверками перед выдачей клиенту.

## 0.19.0 React/owner packages

Держите репозиторий приватным. Основной CI выполняет Node-тесты и сборку pinned React,
отдельный browser-owner-integration job использует настоящее Chromium-навигационное соединение.
В нём --browser-bridge не используется. Manual owner-private-packages workflow работает
только в private repo и сохраняет owner ZIP как artifact, не прикладывая его к клиентскому релизу.
Не загружайте authority.json, пароли или web-enrollment в Git/Actions.
Адрес admin-origin и web-account настраиваются только на сервере.
