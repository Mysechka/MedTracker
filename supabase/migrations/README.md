# SQL-миграции MedTracker

Правило: существующие файлы в этой папке **не правятся**. Любое изменение
схемы — новый файл `YYYYMMDDHHMMSS_описание.sql`.

## Применённые миграции

| Файл | Стадия | Содержание |
|---|---|---|
| `20260825120000_init.sql` | 2 | Таблицы, CHECK, RLS, Storage `medical-files`, RPC confirm/skip/undo/restock, Realtime `dose_events` |
| `20260827120000_stage4_scheduler.sql` | 4 | Материализация `dose_events`, перевод в `Missed`, `notification_deliveries`, `pg_cron` |
| `20260828100000_stage6_moscow_timezone_scheme.sql` | 6 | Дефолт `Europe/Moscow` и CHECK «Москва ± N часов» для `profiles.time_zone_id` |

## Применение

```bash
supabase db push
# или локально:
supabase start
supabase migration up
```

Bucket `medical-files` приватный. Путь объекта: `{user_id}/{document_id}/{filename}`.
Доступ к файлу — только signed URL (TTL 5 минут, см. `.env.example`).
