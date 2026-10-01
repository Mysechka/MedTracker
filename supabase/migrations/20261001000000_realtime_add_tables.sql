-- Добавление таблиц medications, courses, schedules в публикацию supabase_realtime
-- для поддержки сквозной Realtime-синхронизации клиентских приложений.

do $$
begin
  alter publication supabase_realtime add table public.medications;
  alter publication supabase_realtime add table public.courses;
  alter publication supabase_realtime add table public.schedules;
exception
  when duplicate_object then null;
  when undefined_object then null;
end;
$$;
