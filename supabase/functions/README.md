# Edge Functions (Deno / TypeScript)

Стадия 4. Секреты — только через `supabase secrets set` / Vault, не в репозитории.

| Function | Назначение |
|---|---|
| `tick` | Материализация 14 дней, Missed, отправка уведомлений, sync edit |
| `telegram-webhook` | Secret-token, привязка по коду, callbacks Принял/Пропуск/Snooze |
| `discord-interactions` | Ed25519, PING, `/link`, кнопки App |

Общий код: `_shared/` — `INotificationChannel` (Telegram / DiscordApp / DiscordWebhook).

## pg_cron

Миграция `20260827120000_stage4_scheduler.sql` ставит job `medtracker-tick` (* * * * *).
Перед работой положить в Vault:

- `medtracker_tick_url` — `https://<ref>.supabase.co/functions/v1/tick`
- `medtracker_service_role_key` — service_role

Пока секретов нет, `cron_invoke_tick()` — no-op.

## Локально (после установки supabase CLI)

```bash
supabase functions serve tick --env-file .env
supabase functions serve telegram-webhook --env-file .env
supabase functions serve discord-interactions --env-file .env
```

Deploy на этой стадии не выполняем — только код в репозитории.
