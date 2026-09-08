# MedTracker Messenger Bots (Telegram & Discord REST API)

REST API сервер и боты для Telegram и Discord в системе MedTracker. Предоставляет эндпоинты для вебхуков Telegram и Discord Interactions API, обработку команд привязки (`/link <код>`), просмотр расписания (`/today`) и интерактивные кнопки подтверждения/пропуска/откладывания приёма лекарств.

## Возможности

### 1. Telegram Bot (REST Webhook `/api/telegram/webhook`)
- Валидация заголовка секретного токена `X-Telegram-Bot-Api-Secret-Token`.
- Команда `/link <код>` — привязка Telegram-чата к профилю MedTracker по 6-значному коду (`confirm_messenger_link`).
- Команда `/today` — вывод списка сегодняшних лекарств с их статусами и суточным прогрессом.
- Интерактивные кнопки под напоминаниями:
  - `take:<dose_id>` / `dose:taken:<id>` — отметка о приёме (`confirm_dose`), списание остатка в inventory, обновление текста на «Принято в HH:mm ✅».
  - `skip:<dose_id>` / `dose:skip:<id>` — отметка о пропуске (`skip_dose`), обновление текста на «Пропущено ❌».
  - `snooze:<dose_id>:15` / `dose:snooze:<id>` — перенос напоминания на 15 минут (`snooze_notification_delivery`).

### 2. Discord Bot (Interactions REST API `/api/discord/interactions` & Gateway Bot)
- Криптографическая валидация Ed25519 подписи (`X-Signature-Ed25519` и `X-Signature-Timestamp`) через публичный ключ `DISCORD_PUBLIC_KEY`.
- PING (Type 1) -> PONG (Type 1).
- Слэш-команды (`/link <code>`, `/today`, `/ping`).
- Обработка кнопок Message Component (Принял / Пропустить / Отложить) с атомарным ответом `UPDATE_MESSAGE` (Type 7).

## Конфигурация и запуск

1. Переменные окружения (`.env`):
   ```env
   # Discord
   DISCORD_BOT_TOKEN=...
   DISCORD_APPLICATION_ID=...
   DISCORD_PUBLIC_KEY=...
   DISCORD_WEBHOOK_URL=...

   # Telegram
   TELEGRAM_BOT_TOKEN=...
   TELEGRAM_WEBHOOK_SECRET=...

   # Supabase
   SUPABASE_URL=http://127.0.0.1:54321
   SUPABASE_SERVICE_ROLE_KEY=...

   PORT=3000
   ```

2. Установка зависимостей:
   ```bash
   npm install
   ```

3. Запуск тестов:
   ```bash
   npm test
   ```

4. Сборка и запуск:
   ```bash
   npm run build
   npm start
   ```
