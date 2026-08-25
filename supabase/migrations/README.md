# SQL-миграции MedTracker

Правило: существующие файлы в этой папке **не правятся**. Любое изменение
схемы — новый файл `YYYYMMDDHHMMSS_описание.sql`.

## Применённые миграции

| Файл | Стадия | Содержание |
|---|---|---|
| `20260825120000_init.sql` | 2 | Таблицы, CHECK, RLS, Storage `medical-files`, RPC confirm/skip/undo/restock, Realtime `dose_events` |

## Применение

```bash
supabase db push
# или локально:
supabase start
supabase migration up
```

Bucket `medical-files` приватный. Путь объекта: `{user_id}/{document_id}/{filename}`.
Доступ к файлу — только signed URL (TTL 5 минут, см. `.env.example`).
