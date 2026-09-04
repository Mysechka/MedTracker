-- Стадия 7: оптимизация и исправление перемотки курсора для Interval-расписаний.
-- Существующие миграции не правятся — это новый файл.
--
-- Проблема: если курс начался за несколько месяцев до текущего момента (p_now),
-- фиксированный лимит итераций (14 * 24 + 8) исчерпывался в прошлом, и дозы
-- на текущий горизонт не материализовались.
-- Решение: быстрая перемотка v_cursor к p_now перед запуском цикла горизонта.

begin;

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
    v_range_start := greatest(
      v_course.starts_on,
      (p_now at time zone v_profile.time_zone_id)::date
    );
    v_range_end := least(
      v_course_end,
      (v_horizon_end at time zone v_profile.time_zone_id)::date
    );

    if v_range_end < v_range_start then
      continue;
    end if;

    for v_schedule in
      select *
      from public.schedules s
      where s.course_id = v_course.id
    loop
      if v_schedule.type = 'AsNeeded' then
        continue;
      end if;

      if v_schedule.type = 'FixedTimes' then
        v_day := v_range_start;
        while v_day <= v_range_end loop
          if public.schedule_day_allowed(v_schedule, v_course.starts_on, v_day) then
            if v_schedule.fixed_times is not null then
              foreach v_time in array v_schedule.fixed_times loop
                v_utc := public.local_wall_to_utc(v_day, v_time, v_profile.time_zone_id);
                if v_utc is not null
                   and v_utc >= p_now
                   and v_utc <= v_horizon_end
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
          end if;
          v_day := v_day + 1;
        end loop;

      elsif v_schedule.type = 'MealRelative' then
        v_meal := case v_schedule.meal_kind
          when 'Breakfast' then v_profile.breakfast_time
          when 'Lunch'     then v_profile.lunch_time
          when 'Dinner'    then v_profile.dinner_time
          else null
        end;

        if v_meal is null then
          continue;
        end if;

        v_offset := public.resolve_meal_offset_minutes(
          v_schedule.meal_relation, v_schedule.offset_minutes);

        v_total_minutes := (extract(hour from v_meal)::bigint * 60)
          + extract(minute from v_meal)::bigint + v_offset;
        v_day_shift := floor(v_total_minutes::numeric / (24 * 60))::integer;
        v_minutes_in_day := (v_total_minutes - (v_day_shift::bigint * 24 * 60))::integer;
        v_time := make_time(v_minutes_in_day / 60, v_minutes_in_day % 60, 0);

        v_day := v_range_start;
        while v_day <= v_range_end loop
          if public.schedule_day_allowed(v_schedule, v_course.starts_on, v_day) then
            v_local_date := v_day + v_day_shift;
            v_utc := public.local_wall_to_utc(v_local_date, v_time, v_profile.time_zone_id);
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

        -- Перемотка вперёд для курсов, начавшихся в прошлом:
        if v_cursor < p_now then
          v_cursor := v_cursor + v_step * floor(extract(epoch from (p_now - v_cursor)) / extract(epoch from v_step));
          while v_cursor < p_now loop
            v_cursor := v_cursor + v_step;
          end loop;
        end if;

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

commit;
