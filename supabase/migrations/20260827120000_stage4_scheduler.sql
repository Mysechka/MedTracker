-- Стадия 4: материализация dose_events в БД, Missed, claim delivery, pg_cron → tick.
-- Существующие миграции не правятся.

begin;

create extension if not exists "pg_cron" with schema pg_catalog;
create extension if not exists "pg_net" with schema extensions;

-- ---------------------------------------------------------------------------
-- local wall time → timestamptz (IANA).
-- Невалидный час (весенний DST) → null.
-- Неоднозначный (осенний) → более ранний UTC-инстант (больший offset / DST),
-- как в Med.Domain.Scheduling.LocalTimeConverter.
-- ---------------------------------------------------------------------------
create or replace function public.local_wall_to_utc(
  p_local_date date,
  p_local_time time,
  p_time_zone_id text
)
returns timestamptz
language plpgsql
immutable
strict
as $$
declare
  v_local timestamp without time zone := p_local_date + p_local_time;
  v_as_utc timestamptz;
  v_roundtrip timestamp without time zone;
  v_alt timestamptz;
begin
  -- timestamp AT TIME ZONE zone → timestamptz
  v_as_utc := v_local at time zone p_time_zone_id;
  v_roundtrip := v_as_utc at time zone p_time_zone_id;

  -- Пропущенный час: round-trip не совпадает с исходным локальным временем.
  if v_roundtrip is distinct from v_local then
    return null;
  end if;

  -- Неоднозначный час: есть второй инстант (~1 час раньше/позже).
  -- Берём более ранний UTC (больший offset), как в C#.
  v_alt := (v_local - interval '1 hour') at time zone p_time_zone_id;
  if (v_alt at time zone p_time_zone_id) = v_local
     and v_alt < v_as_utc
  then
    return v_alt;
  end if;

  v_alt := (v_local + interval '1 hour') at time zone p_time_zone_id;
  if (v_alt at time zone p_time_zone_id) = v_local
     and v_alt < v_as_utc
  then
    return v_alt;
  end if;

  return v_as_utc;
exception
  when others then
    return null;
end;
$$;

create or replace function public.dose_dedupe_key(
  p_schedule_id uuid,
  p_scheduled_at timestamptz
)
returns text
language sql
immutable
strict
as $$
  select replace(p_schedule_id::text, '-', '')
    || '|'
    || to_char(timezone('utc', p_scheduled_at), 'YYYY-MM-DD"T"HH24:MI:SS.US')
    || '0Z';
$$;

create or replace function public.schedule_day_allowed(
  p_schedule public.schedules,
  p_course_starts_on date,
  p_day date
)
returns boolean
language plpgsql
immutable
as $$
declare
  v_dow integer := extract(isodow from p_day)::integer; -- 1=Mon .. 7=Sun
  v_ok boolean;
  v_delta integer;
begin
  v_ok := case v_dow
    when 1 then p_schedule.monday
    when 2 then p_schedule.tuesday
    when 3 then p_schedule.wednesday
    when 4 then p_schedule.thursday
    when 5 then p_schedule.friday
    when 6 then p_schedule.saturday
    when 7 then p_schedule.sunday
    else false
  end;

  if not v_ok then
    return false;
  end if;

  if p_schedule.every_n_days is not null then
    v_delta := p_day - p_course_starts_on;
    if v_delta < 0 or (v_delta % p_schedule.every_n_days) <> 0 then
      return false;
    end if;
  end if;

  return true;
end;
$$;

create or replace function public.course_effective_ends_on(p_course public.courses)
returns date
language sql
immutable
as $$
  select coalesce(
    p_course.ends_on,
    p_course.starts_on + (p_course.duration_days - 1)
  );
$$;

create or replace function public.resolve_meal_offset_minutes(
  p_relation text,
  p_offset_minutes integer
)
returns integer
language sql
immutable
as $$
  select case p_relation
    when 'Before' then -abs(p_offset_minutes)
    when 'After' then abs(p_offset_minutes)
    when 'With' then 0
    else p_offset_minutes
  end;
$$;

-- ---------------------------------------------------------------------------
-- Материализация на горизонт. Идемпотентна через dedupe_key.
-- p_user_id null + service_role → все активные курсы;
-- authenticated без p_user_id → только свой профиль.
-- ---------------------------------------------------------------------------
create or replace function public.materialize_upcoming_doses(
  p_horizon_days integer default 14,
  p_user_id uuid default null,
  p_now timestamptz default timezone('utc', now())
)
returns integer
language plpgsql
security definer
set search_path = public
as $$
declare
  v_uid uuid := auth.uid();
  v_is_service boolean := (auth.role() = 'service_role');
  v_target uuid;
  v_inserted integer := 0;
  v_course public.courses%rowtype;
  v_schedule public.schedules%rowtype;
  v_profile public.profiles%rowtype;
  v_horizon_end timestamptz;
  v_range_start date;
  v_range_end date;
  v_course_end date;
  v_day date;
  v_local_date date;
  v_time time;
  v_utc timestamptz;
  v_meal time;
  v_offset integer;
  v_total_minutes bigint;
  v_day_shift integer;
  v_minutes_in_day integer;
  v_cursor timestamptz;
  v_step interval;
  v_iter integer;
  v_rowcount integer;
  v_anchor time;
  v_key text;
begin
  if p_horizon_days is null or p_horizon_days <= 0 then
    raise exception 'horizon_days must be > 0';
  end if;

  if not v_is_service and v_uid is null then
    raise exception 'not authenticated';
  end if;

  if p_user_id is not null then
    if not v_is_service and p_user_id <> v_uid then
      raise exception 'forbidden';
    end if;
    v_target := p_user_id;
  elsif v_is_service then
    v_target := null;
  else
    v_target := v_uid;
  end if;

  v_horizon_end := p_now + make_interval(days => p_horizon_days);

  for v_course in
    select *
    from public.courses c
    where c.is_active
      and (v_target is null or c.user_id = v_target)
  loop
    select * into v_profile from public.profiles where id = v_course.user_id;
    if not found then
      continue;
    end if;

    v_course_end := public.course_effective_ends_on(v_course);
    v_range_start := (p_now at time zone v_profile.time_zone_id)::date;
    if v_range_start < v_course.starts_on then
      v_range_start := v_course.starts_on;
    end if;
    v_range_end := (v_horizon_end at time zone v_profile.time_zone_id)::date;
    if v_range_end > v_course_end then
      v_range_end := v_course_end;
    end if;

    if v_range_end < v_range_start then
      continue;
    end if;

    for v_schedule in
      select *
      from public.schedules s
      where s.course_id = v_course.id
        and s.type <> 'AsNeeded'
    loop
      if v_schedule.type = 'FixedTimes' then
        v_day := v_range_start;
        while v_day <= v_range_end loop
          if public.schedule_day_allowed(v_schedule, v_course.starts_on, v_day) then
            foreach v_time in array v_schedule.fixed_times loop
              v_utc := public.local_wall_to_utc(v_day, v_time, v_profile.time_zone_id);
              if v_utc is not null
                 and v_utc >= p_now
                 and v_utc <= v_horizon_end
                 and v_day >= v_course.starts_on
                 and v_day <= v_course_end
              then
                v_key := public.dose_dedupe_key(v_schedule.id, v_utc);
                insert into public.dose_events (
                  id, user_id, course_id, schedule_id, scheduled_at, local_date, state, dedupe_key
                ) values (
                  gen_random_uuid(), v_course.user_id, v_course.id, v_schedule.id,
                  v_utc, v_day, 'Scheduled', v_key
                )
                on conflict (dedupe_key) do nothing;
                get diagnostics v_rowcount = row_count;
                v_inserted := v_inserted + v_rowcount;
              end if;
            end loop;
          end if;
          v_day := v_day + 1;
        end loop;

      elsif v_schedule.type = 'MealRelative' then
        v_meal := case v_schedule.meal_kind
          when 'Breakfast' then v_profile.breakfast_time
          when 'Lunch' then v_profile.lunch_time
          when 'Dinner' then v_profile.dinner_time
          else null
        end;
        if v_meal is null then
          continue;
        end if;

        v_offset := public.resolve_meal_offset_minutes(
          v_schedule.meal_relation, v_schedule.offset_minutes);
        v_total_minutes :=
          (extract(hour from v_meal)::bigint * 60)
          + extract(minute from v_meal)::bigint
          + v_offset;
        v_day_shift := floor(v_total_minutes / (24.0 * 60))::integer;
        v_minutes_in_day := (v_total_minutes - v_day_shift * 24 * 60)::integer;
        v_time := make_time(v_minutes_in_day / 60, v_minutes_in_day % 60, 0);

        v_day := v_range_start;
        while v_day <= v_range_end loop
          if public.schedule_day_allowed(v_schedule, v_course.starts_on, v_day)
             and v_day >= v_course.starts_on
             and v_day <= v_course_end
          then
            v_local_date := v_day + v_day_shift;
            v_utc := public.local_wall_to_utc(
              v_local_date, v_time, v_profile.time_zone_id);
            if v_utc is not null
               and v_utc >= p_now
               and v_utc <= v_horizon_end
            then
              v_key := public.dose_dedupe_key(v_schedule.id, v_utc);
              insert into public.dose_events (
                id, user_id, course_id, schedule_id, scheduled_at, local_date, state, dedupe_key
              ) values (
                gen_random_uuid(), v_course.user_id, v_course.id, v_schedule.id,
                v_utc, v_local_date, 'Scheduled', v_key
              )
              on conflict (dedupe_key) do nothing;
              get diagnostics v_rowcount = row_count;
              v_inserted := v_inserted + v_rowcount;
            end if;
          end if;
          v_day := v_day + 1;
        end loop;

      elsif v_schedule.type = 'Interval' then
        v_anchor := v_schedule.interval_anchor_time;
        v_cursor := public.local_wall_to_utc(
          v_course.starts_on, v_anchor, v_profile.time_zone_id);
        -- Пропущенный час якоря — сдвиг на минуту вперёд в пределах суток.
        if v_cursor is null then
          for v_iter in 1 .. (24 * 60 - 1) loop
            v_cursor := public.local_wall_to_utc(
              v_course.starts_on,
              (v_anchor + make_interval(mins => v_iter))::time,
              v_profile.time_zone_id);
            exit when v_cursor is not null;
          end loop;
        end if;

        if v_cursor is null then
          continue;
        end if;

        v_step := make_interval(hours => v_schedule.interval_hours);
        for v_iter in 1 .. (14 * 24 + 8) loop
          exit when v_cursor > v_horizon_end;
          v_local_date := (v_cursor at time zone v_profile.time_zone_id)::date;
          exit when v_local_date > v_course_end;

          if v_local_date >= v_course.starts_on
             and public.schedule_day_allowed(v_schedule, v_course.starts_on, v_local_date)
             and v_cursor >= p_now
             and v_cursor <= v_horizon_end
          then
            v_key := public.dose_dedupe_key(v_schedule.id, v_cursor);
            insert into public.dose_events (
              id, user_id, course_id, schedule_id, scheduled_at, local_date, state, dedupe_key
            ) values (
              gen_random_uuid(), v_course.user_id, v_course.id, v_schedule.id,
              v_cursor, v_local_date, 'Scheduled', v_key
            )
            on conflict (dedupe_key) do nothing;
            get diagnostics v_rowcount = row_count;
            v_inserted := v_inserted + v_rowcount;
          end if;

          v_cursor := v_cursor + v_step;
        end loop;
      end if;
    end loop;
  end loop;

  return v_inserted;
end;
$$;

-- ---------------------------------------------------------------------------
-- Scheduled/Notified → Missed после confirmation_window от scheduled_at
-- ---------------------------------------------------------------------------
create or replace function public.mark_missed_doses(
  p_now timestamptz default timezone('utc', now())
)
returns integer
language plpgsql
security definer
set search_path = public
as $$
declare
  v_is_service boolean := (auth.role() = 'service_role');
  v_count integer;
begin
  if not v_is_service then
    raise exception 'service_role required';
  end if;

  with updated as (
    update public.dose_events de
    set
      state = 'Missed',
      source = 'System'
    from public.profiles p
    where de.user_id = p.id
      and de.state in ('Scheduled', 'Notified')
      and p_now >= de.scheduled_at
        + make_interval(mins => p.confirmation_window_minutes)
    returning de.id
  )
  select count(*)::integer into v_count from updated;

  return coalesce(v_count, 0);
end;
$$;

-- ---------------------------------------------------------------------------
-- Scheduled → Notified (после успешной отправки хотя бы в один канал)
-- ---------------------------------------------------------------------------
create or replace function public.mark_dose_notified(
  p_dose_event_id uuid,
  p_source text default 'System'
)
returns jsonb
language plpgsql
security definer
set search_path = public
as $$
declare
  v_is_service boolean := (auth.role() = 'service_role');
  v_event public.dose_events%rowtype;
  v_updated public.dose_events%rowtype;
begin
  if not v_is_service then
    raise exception 'service_role required';
  end if;

  if p_source is null or p_source not in ('App', 'Telegram', 'Discord', 'System') then
    raise exception 'invalid source';
  end if;

  select * into v_event
  from public.dose_events
  where id = p_dose_event_id
  for update;

  if not found then
    raise exception 'dose_event not found';
  end if;

  if v_event.state = 'Notified' then
    return jsonb_build_object('outcome', 'NoOp', 'dose_event_id', v_event.id, 'state', v_event.state);
  end if;

  if v_event.state <> 'Scheduled' then
    return jsonb_build_object(
      'outcome', 'Rejected',
      'dose_event_id', v_event.id,
      'state', v_event.state,
      'reason', 'not Scheduled'
    );
  end if;

  update public.dose_events
  set state = 'Notified', source = p_source
  where id = v_event.id and state = 'Scheduled'
  returning * into v_updated;

  if not found then
    return jsonb_build_object('outcome', 'Rejected', 'dose_event_id', v_event.id, 'reason', 'lost race');
  end if;

  return jsonb_build_object(
    'outcome', 'Applied',
    'dose_event_id', v_updated.id,
    'state', v_updated.state
  );
end;
$$;

-- ---------------------------------------------------------------------------
-- Идемпотентный claim строки notification_deliveries перед отправкой.
-- Возвращает строку для работы или null, если уже Sent/Edited и не snooze-retry.
-- ---------------------------------------------------------------------------
create or replace function public.claim_notification_delivery(
  p_dose_event_id uuid,
  p_user_id uuid,
  p_channel_type text,
  p_now timestamptz default timezone('utc', now()),
  p_max_attempts integer default 8
)
returns public.notification_deliveries
language plpgsql
security definer
set search_path = public
as $$
declare
  v_is_service boolean := (auth.role() = 'service_role');
  v_row public.notification_deliveries%rowtype;
begin
  if not v_is_service then
    raise exception 'service_role required';
  end if;

  if p_channel_type not in ('telegram', 'discord_app', 'discord_webhook') then
    raise exception 'invalid channel_type';
  end if;

  insert into public.notification_deliveries (
    id, user_id, dose_event_id, channel_type, status, attempts, next_retry_at
  ) values (
    gen_random_uuid(), p_user_id, p_dose_event_id, p_channel_type, 'Pending', 0, p_now
  )
  on conflict (dose_event_id, channel_type) do nothing;

  select * into v_row
  from public.notification_deliveries
  where dose_event_id = p_dose_event_id and channel_type = p_channel_type
  for update;

  if not found then
    return null;
  end if;

  -- Уже доставлено и нет отложенного ретрая (snooze) — не трогаем.
  if v_row.status in ('Sent', 'Edited')
     and (v_row.next_retry_at is null or v_row.next_retry_at > p_now)
  then
    return null;
  end if;

  if v_row.status = 'Failed' and v_row.attempts >= p_max_attempts then
    return null;
  end if;

  if v_row.next_retry_at is not null and v_row.next_retry_at > p_now then
    return null;
  end if;

  update public.notification_deliveries
  set
    status = 'Pending',
    attempts = attempts + 1,
    last_error = null
  where id = v_row.id
  returning * into v_row;

  return v_row;
end;
$$;

create or replace function public.complete_notification_delivery(
  p_delivery_id uuid,
  p_message_id text,
  p_status text default 'Sent'
)
returns void
language plpgsql
security definer
set search_path = public
as $$
begin
  if auth.role() <> 'service_role' then
    raise exception 'service_role required';
  end if;

  if p_status not in ('Sent', 'Edited', 'Failed', 'Pending') then
    raise exception 'invalid status';
  end if;

  update public.notification_deliveries
  set
    status = p_status,
    message_id = coalesce(p_message_id, message_id),
    next_retry_at = null,
    last_error = null
  where id = p_delivery_id;
end;
$$;

create or replace function public.fail_notification_delivery(
  p_delivery_id uuid,
  p_error text,
  p_next_retry_at timestamptz
)
returns void
language plpgsql
security definer
set search_path = public
as $$
begin
  if auth.role() <> 'service_role' then
    raise exception 'service_role required';
  end if;

  update public.notification_deliveries
  set
    status = 'Failed',
    last_error = left(coalesce(p_error, 'error'), 500),
    next_retry_at = p_next_retry_at
  where id = p_delivery_id;
end;
$$;

-- Snooze: повторная доставка через 15 минут без сдвига scheduled_at.
create or replace function public.snooze_notification_delivery(
  p_dose_event_id uuid,
  p_channel_type text,
  p_minutes integer default 15,
  p_now timestamptz default timezone('utc', now())
)
returns jsonb
language plpgsql
security definer
set search_path = public
as $$
declare
  v_is_service boolean := (auth.role() = 'service_role');
  v_event public.dose_events%rowtype;
begin
  if not v_is_service then
    raise exception 'service_role required';
  end if;

  select * into v_event from public.dose_events where id = p_dose_event_id;
  if not found then
    raise exception 'dose_event not found';
  end if;

  if v_event.state not in ('Scheduled', 'Notified') then
    return jsonb_build_object('outcome', 'Rejected', 'reason', 'terminal state');
  end if;

  update public.notification_deliveries
  set
    status = 'Pending',
    next_retry_at = p_now + make_interval(mins => greatest(p_minutes, 1)),
    last_error = null
  where dose_event_id = p_dose_event_id
    and channel_type = p_channel_type;

  if not found then
    insert into public.notification_deliveries (
      id, user_id, dose_event_id, channel_type, status, attempts, next_retry_at
    ) values (
      gen_random_uuid(), v_event.user_id, p_dose_event_id, p_channel_type,
      'Pending', 0, p_now + make_interval(mins => greatest(p_minutes, 1))
    );
  end if;

  return jsonb_build_object(
    'outcome', 'Applied',
    'dose_event_id', p_dose_event_id,
    'retry_at', p_now + make_interval(mins => greatest(p_minutes, 1))
  );
end;
$$;

-- Привязка мессенджера по one-time коду (вызов из Edge Functions).
create or replace function public.confirm_messenger_link(
  p_link_code text,
  p_channel_type text,
  p_chat_id text default null,
  p_channel_id text default null
)
returns jsonb
language plpgsql
security definer
set search_path = public
as $$
declare
  v_row public.messenger_links%rowtype;
begin
  if auth.role() <> 'service_role' then
    raise exception 'service_role required';
  end if;

  if p_link_code is null or length(trim(p_link_code)) = 0 then
    raise exception 'link_code required';
  end if;

  if p_channel_type not in ('telegram', 'discord') then
    raise exception 'invalid channel_type';
  end if;

  if p_chat_id is null and p_channel_id is null then
    raise exception 'chat_id or channel_id required';
  end if;

  select * into v_row
  from public.messenger_links
  where link_code = trim(p_link_code)
    and channel_type = p_channel_type
  for update;

  if not found then
    return jsonb_build_object('outcome', 'Rejected', 'reason', 'unknown code');
  end if;

  update public.messenger_links
  set
    chat_id = coalesce(p_chat_id, chat_id),
    channel_id = coalesce(p_channel_id, channel_id),
    is_confirmed = true,
    link_code = null
  where id = v_row.id
  returning * into v_row;

  return jsonb_build_object(
    'outcome', 'Applied',
    'user_id', v_row.user_id,
    'channel_type', v_row.channel_type
  );
end;
$$;

-- ---------------------------------------------------------------------------
-- pg_cron → Edge Function tick.
-- URL и service_role берутся из vault (имена medtracker_tick_url,
-- medtracker_service_role_key). Пока секретов нет — no-op без ошибки.
-- ---------------------------------------------------------------------------
create or replace function public.cron_invoke_tick()
returns bigint
language plpgsql
security definer
set search_path = public, extensions, vault
as $$
declare
  v_url text;
  v_key text;
  v_request_id bigint;
begin
  begin
    select decrypted_secret into v_url
    from vault.decrypted_secrets
    where name = 'medtracker_tick_url'
    limit 1;

    select decrypted_secret into v_key
    from vault.decrypted_secrets
    where name = 'medtracker_service_role_key'
    limit 1;
  exception
    when undefined_table then
      return null;
    when undefined_object then
      return null;
  end;

  if v_url is null or length(trim(v_url)) = 0
     or v_key is null or length(trim(v_key)) = 0
  then
    return null;
  end if;

  select net.http_post(
    url := trim(v_url),
    headers := jsonb_build_object(
      'Content-Type', 'application/json',
      'Authorization', 'Bearer ' || trim(v_key)
    ),
    body := jsonb_build_object('source', 'pg_cron')
  ) into v_request_id;

  return v_request_id;
end;
$$;

do $$
begin
  if exists (select 1 from cron.job where jobname = 'medtracker-tick') then
    perform cron.unschedule('medtracker-tick');
  end if;
exception
  when undefined_table then null;
  when undefined_function then null;
  when others then null;
end;
$$;

do $$
begin
  perform cron.schedule(
    'medtracker-tick',
    '* * * * *',
    $cron$select public.cron_invoke_tick();$cron$
  );
exception
  when undefined_function then
    raise notice 'pg_cron unavailable — schedule skipped';
  when undefined_table then
    raise notice 'pg_cron unavailable — schedule skipped';
  when insufficient_privilege then
    raise notice 'pg_cron schedule skipped (privilege)';
end;
$$;

revoke all on function public.materialize_upcoming_doses(integer, uuid, timestamptz) from public;
revoke all on function public.mark_missed_doses(timestamptz) from public;
revoke all on function public.mark_dose_notified(uuid, text) from public;
revoke all on function public.claim_notification_delivery(uuid, uuid, text, timestamptz, integer) from public;
revoke all on function public.complete_notification_delivery(uuid, text, text) from public;
revoke all on function public.fail_notification_delivery(uuid, text, timestamptz) from public;
revoke all on function public.snooze_notification_delivery(uuid, text, integer, timestamptz) from public;
revoke all on function public.confirm_messenger_link(text, text, text, text) from public;
revoke all on function public.cron_invoke_tick() from public;

grant execute on function public.materialize_upcoming_doses(integer, uuid, timestamptz) to authenticated;
grant execute on function public.materialize_upcoming_doses(integer, uuid, timestamptz) to service_role;

grant execute on function public.mark_missed_doses(timestamptz) to service_role;
grant execute on function public.mark_dose_notified(uuid, text) to service_role;
grant execute on function public.claim_notification_delivery(uuid, uuid, text, timestamptz, integer) to service_role;
grant execute on function public.complete_notification_delivery(uuid, text, text) to service_role;
grant execute on function public.fail_notification_delivery(uuid, text, timestamptz) to service_role;
grant execute on function public.snooze_notification_delivery(uuid, text, integer, timestamptz) to service_role;
grant execute on function public.confirm_messenger_link(text, text, text, text) to service_role;
grant execute on function public.cron_invoke_tick() to service_role;

-- service_role обходит RLS; явные grants на таблицы для ясности.
grant select, insert, update on public.dose_events to service_role;
grant select, insert, update on public.notification_deliveries to service_role;
grant select, update on public.messenger_links to service_role;
grant select on public.profiles to service_role;
grant select on public.courses to service_role;
grant select on public.schedules to service_role;
grant select on public.medications to service_role;

commit;
