-- Стадия 2: начальная схема MedTracker.
-- Существующие миграции не правятся — только новые файлы.
-- Решения: profiles.id = auth.uid(); text+CHECK вместо ENUM;
-- дни недели — отдельные boolean; fixed_times — time[];
-- inventory_transactions append-only; signed URL TTL 5 мин (клиент);
-- Storage path {user_id}/{document_id}/{filename}; RPC confirm_dose.

begin;

-- ---------------------------------------------------------------------------
-- Расширения
-- ---------------------------------------------------------------------------
create extension if not exists "pgcrypto";

-- ---------------------------------------------------------------------------
-- profiles (1:1 с auth.users, id = auth.uid())
-- ---------------------------------------------------------------------------
create table public.profiles (
  id uuid primary key references auth.users (id) on delete cascade,
  username text not null,
  time_zone_id text not null default 'UTC',
  breakfast_time time without time zone not null default time '08:00',
  lunch_time time without time zone not null default time '13:00',
  dinner_time time without time zone not null default time '19:00',
  confirmation_window_minutes integer not null default 180
    check (confirmation_window_minutes > 0),
  created_at timestamptz not null default now(),
  updated_at timestamptz not null default now(),
  constraint profiles_username_not_blank check (length(trim(username)) > 0),
  constraint profiles_time_zone_not_blank check (length(trim(time_zone_id)) > 0)
);

create index profiles_username_idx on public.profiles (lower(username));

-- ---------------------------------------------------------------------------
-- diagnoses
-- ---------------------------------------------------------------------------
create table public.diagnoses (
  id uuid primary key default gen_random_uuid(),
  user_id uuid not null references public.profiles (id) on delete cascade,
  title text not null,
  doctor text,
  diagnosed_on date,
  notes text,
  created_at timestamptz not null default now(),
  updated_at timestamptz not null default now(),
  constraint diagnoses_title_not_blank check (length(trim(title)) > 0)
);

create index diagnoses_user_id_idx on public.diagnoses (user_id);

-- ---------------------------------------------------------------------------
-- medications
-- ---------------------------------------------------------------------------
create table public.medications (
  id uuid primary key default gen_random_uuid(),
  user_id uuid not null references public.profiles (id) on delete cascade,
  name text not null,
  form text not null,
  dosage text not null,
  unit text not null,
  barcode text,
  notes text,
  created_at timestamptz not null default now(),
  updated_at timestamptz not null default now(),
  constraint medications_name_not_blank check (length(trim(name)) > 0),
  constraint medications_form_not_blank check (length(trim(form)) > 0),
  constraint medications_dosage_not_blank check (length(trim(dosage)) > 0),
  constraint medications_unit_not_blank check (length(trim(unit)) > 0)
);

create index medications_user_id_idx on public.medications (user_id);

-- ---------------------------------------------------------------------------
-- courses
-- ---------------------------------------------------------------------------
create table public.courses (
  id uuid primary key default gen_random_uuid(),
  user_id uuid not null references public.profiles (id) on delete cascade,
  medication_id uuid not null references public.medications (id) on delete restrict,
  starts_on date not null,
  ends_on date,
  duration_days integer,
  is_active boolean not null default true,
  diagnosis_id uuid references public.diagnoses (id) on delete set null,
  created_at timestamptz not null default now(),
  updated_at timestamptz not null default now(),
  constraint courses_has_end check (ends_on is not null or duration_days is not null),
  constraint courses_duration_positive check (duration_days is null or duration_days > 0),
  constraint courses_ends_on_order check (ends_on is null or ends_on >= starts_on),
  constraint courses_ends_matches_duration check (
    ends_on is null
    or duration_days is null
    or ends_on = (starts_on + (duration_days - 1))
  )
);

create index courses_user_id_idx on public.courses (user_id);
create index courses_medication_id_idx on public.courses (medication_id);

-- ---------------------------------------------------------------------------
-- schedules
-- ---------------------------------------------------------------------------
create table public.schedules (
  id uuid primary key default gen_random_uuid(),
  user_id uuid not null references public.profiles (id) on delete cascade,
  course_id uuid not null references public.courses (id) on delete cascade,
  type text not null,
  monday boolean not null default false,
  tuesday boolean not null default false,
  wednesday boolean not null default false,
  thursday boolean not null default false,
  friday boolean not null default false,
  saturday boolean not null default false,
  sunday boolean not null default false,
  dose_amount numeric(12, 4) not null,
  fixed_times time without time zone[] not null default '{}',
  interval_hours integer,
  interval_anchor_time time without time zone,
  every_n_days integer,
  meal_kind text,
  meal_relation text not null default 'Independent',
  offset_minutes integer not null default 0,
  created_at timestamptz not null default now(),
  updated_at timestamptz not null default now(),
  constraint schedules_type_check check (
    type in ('FixedTimes', 'Interval', 'MealRelative', 'AsNeeded')
  ),
  constraint schedules_dose_positive check (dose_amount > 0),
  constraint schedules_interval_hours_positive check (
    interval_hours is null or interval_hours > 0
  ),
  constraint schedules_every_n_days_positive check (
    every_n_days is null or every_n_days > 0
  ),
  constraint schedules_meal_kind_check check (
    meal_kind is null or meal_kind in ('Breakfast', 'Lunch', 'Dinner')
  ),
  constraint schedules_meal_relation_check check (
    meal_relation in ('Before', 'With', 'After', 'Independent')
  ),
  constraint schedules_days_or_as_needed check (
    type = 'AsNeeded'
    or monday or tuesday or wednesday or thursday or friday or saturday or sunday
  ),
  constraint schedules_fixed_times_shape check (
    (type = 'FixedTimes' and cardinality(fixed_times) >= 1)
    or (type <> 'FixedTimes' and cardinality(fixed_times) = 0)
  ),
  constraint schedules_interval_shape check (
    (type = 'Interval'
      and interval_hours is not null
      and interval_anchor_time is not null)
    or (type <> 'Interval'
      and interval_hours is null
      and interval_anchor_time is null)
  ),
  constraint schedules_meal_shape check (
    (type = 'MealRelative' and meal_kind is not null)
    or (type <> 'MealRelative' and meal_kind is null)
  )
);

create index schedules_user_id_idx on public.schedules (user_id);
create index schedules_course_id_idx on public.schedules (course_id);

-- ---------------------------------------------------------------------------
-- dose_events
-- ---------------------------------------------------------------------------
create table public.dose_events (
  id uuid primary key default gen_random_uuid(),
  user_id uuid not null references public.profiles (id) on delete cascade,
  course_id uuid not null references public.courses (id) on delete cascade,
  schedule_id uuid not null references public.schedules (id) on delete cascade,
  scheduled_at timestamptz not null,
  local_date date not null,
  state text not null default 'Scheduled',
  taken_at timestamptz,
  source text,
  dedupe_key text not null,
  created_at timestamptz not null default now(),
  updated_at timestamptz not null default now(),
  constraint dose_events_state_check check (
    state in ('Scheduled', 'Notified', 'Taken', 'Skipped', 'Missed', 'Cancelled')
  ),
  constraint dose_events_source_check check (
    source is null or source in ('App', 'Telegram', 'Discord', 'System')
  ),
  constraint dose_events_dedupe_key_unique unique (dedupe_key),
  constraint dose_events_taken_requires_timestamp check (
    (state = 'Taken' and taken_at is not null)
    or (state <> 'Taken')
  )
);

create index dose_events_user_id_idx on public.dose_events (user_id);
create index dose_events_user_local_date_idx on public.dose_events (user_id, local_date);
create index dose_events_schedule_id_idx on public.dose_events (schedule_id);
create index dose_events_state_scheduled_at_idx on public.dose_events (state, scheduled_at);

-- ---------------------------------------------------------------------------
-- inventory
-- ---------------------------------------------------------------------------
create table public.inventory (
  id uuid primary key default gen_random_uuid(),
  user_id uuid not null references public.profiles (id) on delete cascade,
  medication_id uuid not null references public.medications (id) on delete cascade,
  quantity_on_hand numeric(12, 4) not null default 0,
  low_stock_threshold numeric(12, 4) not null default 0,
  created_at timestamptz not null default now(),
  updated_at timestamptz not null default now(),
  constraint inventory_quantity_non_negative check (quantity_on_hand >= 0),
  constraint inventory_threshold_non_negative check (low_stock_threshold >= 0),
  constraint inventory_medication_unique unique (user_id, medication_id)
);

create index inventory_user_id_idx on public.inventory (user_id);

-- ---------------------------------------------------------------------------
-- inventory_transactions (append-only)
-- ---------------------------------------------------------------------------
create table public.inventory_transactions (
  id uuid primary key default gen_random_uuid(),
  user_id uuid not null references public.profiles (id) on delete cascade,
  inventory_id uuid not null references public.inventory (id) on delete restrict,
  medication_id uuid not null references public.medications (id) on delete restrict,
  dose_event_id uuid references public.dose_events (id) on delete set null,
  kind text not null,
  amount numeric(12, 4) not null,
  created_at timestamptz not null default now(),
  note text,
  constraint inventory_transactions_kind_check check (
    kind in ('Debit', 'Credit', 'Restock')
  ),
  constraint inventory_transactions_amount_positive check (amount > 0)
);

create index inventory_transactions_user_id_idx on public.inventory_transactions (user_id);
create index inventory_transactions_inventory_id_idx on public.inventory_transactions (inventory_id);
create index inventory_transactions_dose_event_id_idx on public.inventory_transactions (dose_event_id);

-- ---------------------------------------------------------------------------
-- documents
-- ---------------------------------------------------------------------------
create table public.documents (
  id uuid primary key default gen_random_uuid(),
  user_id uuid not null references public.profiles (id) on delete cascade,
  storage_path text not null,
  mime_type text not null,
  size_bytes bigint not null,
  doc_type text not null,
  diagnosis_id uuid references public.diagnoses (id) on delete set null,
  course_id uuid references public.courses (id) on delete set null,
  created_at timestamptz not null default now(),
  constraint documents_storage_path_not_blank check (length(trim(storage_path)) > 0),
  constraint documents_size_non_negative check (size_bytes >= 0),
  constraint documents_doc_type_check check (
    doc_type in ('LabResult', 'Prescription', 'Certificate', 'Discharge')
  ),
  constraint documents_has_link check (diagnosis_id is not null or course_id is not null)
);

create index documents_user_id_idx on public.documents (user_id);

-- ---------------------------------------------------------------------------
-- messenger_links
-- ---------------------------------------------------------------------------
create table public.messenger_links (
  id uuid primary key default gen_random_uuid(),
  user_id uuid not null references public.profiles (id) on delete cascade,
  channel_type text not null,
  chat_id text,
  channel_id text,
  is_confirmed boolean not null default false,
  link_code text,
  created_at timestamptz not null default now(),
  updated_at timestamptz not null default now(),
  constraint messenger_links_channel_type_check check (
    channel_type in ('telegram', 'discord')
  ),
  constraint messenger_links_target_check check (
    chat_id is not null or channel_id is not null or link_code is not null
  )
);

create unique index messenger_links_user_channel_unique
  on public.messenger_links (user_id, channel_type);
create unique index messenger_links_link_code_unique
  on public.messenger_links (link_code)
  where link_code is not null;
create index messenger_links_user_id_idx on public.messenger_links (user_id);

-- ---------------------------------------------------------------------------
-- notification_deliveries
-- ---------------------------------------------------------------------------
create table public.notification_deliveries (
  id uuid primary key default gen_random_uuid(),
  user_id uuid not null references public.profiles (id) on delete cascade,
  dose_event_id uuid not null references public.dose_events (id) on delete cascade,
  channel_type text not null,
  message_id text,
  status text not null default 'Pending',
  attempts integer not null default 0,
  next_retry_at timestamptz,
  last_error text,
  created_at timestamptz not null default now(),
  updated_at timestamptz not null default now(),
  constraint notification_deliveries_channel_type_check check (
    channel_type in ('telegram', 'discord_app', 'discord_webhook')
  ),
  constraint notification_deliveries_status_check check (
    status in ('Pending', 'Sent', 'Failed', 'Edited')
  ),
  constraint notification_deliveries_attempts_non_negative check (attempts >= 0)
);

-- Идемпотентность tick: одна активная доставка на пару событие+канал.
create unique index notification_deliveries_dose_channel_unique
  on public.notification_deliveries (dose_event_id, channel_type);

create index notification_deliveries_user_id_idx on public.notification_deliveries (user_id);
create index notification_deliveries_status_retry_idx
  on public.notification_deliveries (status, next_retry_at);

-- ---------------------------------------------------------------------------
-- updated_at helper
-- ---------------------------------------------------------------------------
create or replace function public.set_updated_at()
returns trigger
language plpgsql
as $$
begin
  new.updated_at = now();
  return new;
end;
$$;

create trigger profiles_set_updated_at
  before update on public.profiles
  for each row execute function public.set_updated_at();

create trigger diagnoses_set_updated_at
  before update on public.diagnoses
  for each row execute function public.set_updated_at();

create trigger medications_set_updated_at
  before update on public.medications
  for each row execute function public.set_updated_at();

create trigger courses_set_updated_at
  before update on public.courses
  for each row execute function public.set_updated_at();

create trigger schedules_set_updated_at
  before update on public.schedules
  for each row execute function public.set_updated_at();

create trigger dose_events_set_updated_at
  before update on public.dose_events
  for each row execute function public.set_updated_at();

create trigger inventory_set_updated_at
  before update on public.inventory
  for each row execute function public.set_updated_at();

create trigger messenger_links_set_updated_at
  before update on public.messenger_links
  for each row execute function public.set_updated_at();

create trigger notification_deliveries_set_updated_at
  before update on public.notification_deliveries
  for each row execute function public.set_updated_at();

-- ---------------------------------------------------------------------------
-- Автосоздание профиля при регистрации
-- ---------------------------------------------------------------------------
create or replace function public.handle_new_user()
returns trigger
language plpgsql
security definer
set search_path = public
as $$
declare
  chosen_username text;
begin
  chosen_username := nullif(trim(coalesce(new.raw_user_meta_data ->> 'username', '')), '');
  if chosen_username is null then
    chosen_username := split_part(coalesce(new.email, new.id::text), '@', 1);
  end if;

  insert into public.profiles (id, username)
  values (new.id, chosen_username);
  return new;
end;
$$;

create trigger on_auth_user_created
  after insert on auth.users
  for each row execute function public.handle_new_user();

-- ---------------------------------------------------------------------------
-- RPC: первое терминальное состояние побеждает; списание ровно при Taken
-- ---------------------------------------------------------------------------
create or replace function public.confirm_dose(
  p_dose_event_id uuid,
  p_source text default 'App',
  p_taken_at timestamptz default timezone('utc', now())
)
returns jsonb
language plpgsql
security invoker
set search_path = public
as $$
declare
  v_uid uuid := auth.uid();
  v_is_service boolean := (auth.role() = 'service_role');
  v_event public.dose_events%rowtype;
  v_updated public.dose_events%rowtype;
  v_schedule public.schedules%rowtype;
  v_course public.courses%rowtype;
  v_inventory public.inventory%rowtype;
  v_tx_id uuid;
  v_owner uuid;
begin
  if v_uid is null and not v_is_service then
    raise exception 'not authenticated';
  end if;

  if p_source is null or p_source not in ('App', 'Telegram', 'Discord', 'System') then
    raise exception 'invalid source';
  end if;

  select * into v_event
  from public.dose_events
  where id = p_dose_event_id
    and (v_is_service or user_id = v_uid)
  for update;

  if not found then
    raise exception 'dose_event not found';
  end if;

  if v_event.state = 'Taken' then
    return jsonb_build_object(
      'outcome', 'NoOp',
      'dose_event_id', v_event.id,
      'state', v_event.state
    );
  end if;

  if v_event.state not in ('Scheduled', 'Notified') then
    return jsonb_build_object(
      'outcome', 'Rejected',
      'dose_event_id', v_event.id,
      'state', v_event.state,
      'reason', 'terminal state already set'
    );
  end if;

  update public.dose_events
  set
    state = 'Taken',
    taken_at = p_taken_at,
    source = p_source
  where id = v_event.id
    and state in ('Scheduled', 'Notified')
  returning * into v_updated;

  if not found then
    return jsonb_build_object(
      'outcome', 'Rejected',
      'dose_event_id', v_event.id,
      'reason', 'lost race to another terminal transition'
    );
  end if;

  v_owner := v_updated.user_id;

  select * into v_schedule from public.schedules where id = v_updated.schedule_id;
  select * into v_course from public.courses where id = v_updated.course_id;

  select * into v_inventory
  from public.inventory
  where user_id = v_owner and medication_id = v_course.medication_id
  for update;

  if not found then
    raise exception 'inventory row missing for medication';
  end if;

  if v_inventory.quantity_on_hand < v_schedule.dose_amount then
    -- Состояние уже Taken; откатываем транзакцию целиком.
    raise exception 'insufficient inventory';
  end if;

  v_tx_id := gen_random_uuid();

  insert into public.inventory_transactions (
    id, user_id, inventory_id, medication_id, dose_event_id, kind, amount, note
  ) values (
    v_tx_id, v_owner, v_inventory.id, v_inventory.medication_id, v_updated.id,
    'Debit', v_schedule.dose_amount, null
  );

  update public.inventory
  set quantity_on_hand = quantity_on_hand - v_schedule.dose_amount
  where id = v_inventory.id;

  return jsonb_build_object(
    'outcome', 'Applied',
    'dose_event_id', v_updated.id,
    'state', v_updated.state,
    'transaction_id', v_tx_id,
    'quantity_on_hand', v_inventory.quantity_on_hand - v_schedule.dose_amount
  );
end;
$$;

create or replace function public.skip_dose(
  p_dose_event_id uuid,
  p_source text default 'App',
  p_skipped_at timestamptz default timezone('utc', now())
)
returns jsonb
language plpgsql
security invoker
set search_path = public
as $$
declare
  v_uid uuid := auth.uid();
  v_is_service boolean := (auth.role() = 'service_role');
  v_event public.dose_events%rowtype;
  v_updated public.dose_events%rowtype;
begin
  if v_uid is null and not v_is_service then
    raise exception 'not authenticated';
  end if;

  if p_source is null or p_source not in ('App', 'Telegram', 'Discord', 'System') then
    raise exception 'invalid source';
  end if;

  select * into v_event
  from public.dose_events
  where id = p_dose_event_id
    and (v_is_service or user_id = v_uid)
  for update;

  if not found then
    raise exception 'dose_event not found';
  end if;

  if v_event.state = 'Skipped' then
    return jsonb_build_object('outcome', 'NoOp', 'dose_event_id', v_event.id, 'state', v_event.state);
  end if;

  if v_event.state not in ('Scheduled', 'Notified') then
    return jsonb_build_object(
      'outcome', 'Rejected',
      'dose_event_id', v_event.id,
      'state', v_event.state,
      'reason', 'terminal state already set'
    );
  end if;

  update public.dose_events
  set
    state = 'Skipped',
    taken_at = p_skipped_at,
    source = p_source
  where id = v_event.id
    and state in ('Scheduled', 'Notified')
  returning * into v_updated;

  if not found then
    return jsonb_build_object(
      'outcome', 'Rejected',
      'dose_event_id', v_event.id,
      'reason', 'lost race to another terminal transition'
    );
  end if;

  return jsonb_build_object(
    'outcome', 'Applied',
    'dose_event_id', v_updated.id,
    'state', v_updated.state
  );
end;
$$;

create or replace function public.undo_confirm_dose(
  p_dose_event_id uuid,
  p_undone_at timestamptz default timezone('utc', now())
)
returns jsonb
language plpgsql
security invoker
set search_path = public
as $$
declare
  v_uid uuid := auth.uid();
  v_is_service boolean := (auth.role() = 'service_role');
  v_event public.dose_events%rowtype;
  v_updated public.dose_events%rowtype;
  v_schedule public.schedules%rowtype;
  v_course public.courses%rowtype;
  v_inventory public.inventory%rowtype;
  v_tx_id uuid;
  v_owner uuid;
begin
  if v_uid is null and not v_is_service then
    raise exception 'not authenticated';
  end if;

  select * into v_event
  from public.dose_events
  where id = p_dose_event_id
    and (v_is_service or user_id = v_uid)
  for update;

  if not found then
    raise exception 'dose_event not found';
  end if;

  if v_event.state <> 'Taken' then
    return jsonb_build_object(
      'outcome', 'Rejected',
      'dose_event_id', v_event.id,
      'state', v_event.state,
      'reason', 'undo only from Taken'
    );
  end if;

  update public.dose_events
  set
    state = 'Scheduled',
    taken_at = null,
    source = null
  where id = v_event.id
    and state = 'Taken'
  returning * into v_updated;

  if not found then
    return jsonb_build_object(
      'outcome', 'Rejected',
      'dose_event_id', v_event.id,
      'reason', 'lost race'
    );
  end if;

  v_owner := v_updated.user_id;

  select * into v_schedule from public.schedules where id = v_updated.schedule_id;
  select * into v_course from public.courses where id = v_updated.course_id;

  select * into v_inventory
  from public.inventory
  where user_id = v_owner and medication_id = v_course.medication_id
  for update;

  if not found then
    raise exception 'inventory row missing for medication';
  end if;

  v_tx_id := gen_random_uuid();

  insert into public.inventory_transactions (
    id, user_id, inventory_id, medication_id, dose_event_id, kind, amount, note
  ) values (
    v_tx_id, v_owner, v_inventory.id, v_inventory.medication_id, v_updated.id,
    'Credit', v_schedule.dose_amount, 'undo'
  );

  update public.inventory
  set quantity_on_hand = quantity_on_hand + v_schedule.dose_amount
  where id = v_inventory.id;

  return jsonb_build_object(
    'outcome', 'Applied',
    'dose_event_id', v_updated.id,
    'state', v_updated.state,
    'transaction_id', v_tx_id,
    'quantity_on_hand', v_inventory.quantity_on_hand + v_schedule.dose_amount
  );
end;
$$;

create or replace function public.restock_inventory(
  p_medication_id uuid,
  p_amount numeric,
  p_note text default null
)
returns jsonb
language plpgsql
security invoker
set search_path = public
as $$
declare
  v_uid uuid := auth.uid();
  v_inventory public.inventory%rowtype;
  v_tx_id uuid;
begin
  if v_uid is null then
    raise exception 'not authenticated';
  end if;

  if p_amount is null or p_amount <= 0 then
    raise exception 'amount must be > 0';
  end if;

  select * into v_inventory
  from public.inventory
  where user_id = v_uid and medication_id = p_medication_id
  for update;

  if not found then
    raise exception 'inventory row missing for medication';
  end if;

  v_tx_id := gen_random_uuid();

  insert into public.inventory_transactions (
    id, user_id, inventory_id, medication_id, dose_event_id, kind, amount, note
  ) values (
    v_tx_id, v_uid, v_inventory.id, v_inventory.medication_id, null,
    'Restock', p_amount, p_note
  );

  update public.inventory
  set quantity_on_hand = quantity_on_hand + p_amount
  where id = v_inventory.id;

  return jsonb_build_object(
    'outcome', 'Applied',
    'transaction_id', v_tx_id,
    'quantity_on_hand', v_inventory.quantity_on_hand + p_amount
  );
end;
$$;

revoke all on function public.confirm_dose(uuid, text, timestamptz) from public;
revoke all on function public.skip_dose(uuid, text, timestamptz) from public;
revoke all on function public.undo_confirm_dose(uuid, timestamptz) from public;
revoke all on function public.restock_inventory(uuid, numeric, text) from public;

grant execute on function public.confirm_dose(uuid, text, timestamptz) to authenticated;
grant execute on function public.skip_dose(uuid, text, timestamptz) to authenticated;
grant execute on function public.undo_confirm_dose(uuid, timestamptz) to authenticated;
grant execute on function public.restock_inventory(uuid, numeric, text) to authenticated;

-- service_role нужен tick/webhook на стадии 4
grant execute on function public.confirm_dose(uuid, text, timestamptz) to service_role;
grant execute on function public.skip_dose(uuid, text, timestamptz) to service_role;
grant execute on function public.undo_confirm_dose(uuid, timestamptz) to service_role;
grant execute on function public.restock_inventory(uuid, numeric, text) to service_role;

-- ---------------------------------------------------------------------------
-- Grants (RLS всё равно фильтрует строки)
-- ---------------------------------------------------------------------------
grant select, update on public.profiles to authenticated;
grant select, insert, update, delete on public.diagnoses to authenticated;
grant select, insert, update, delete on public.medications to authenticated;
grant select, insert, update, delete on public.courses to authenticated;
grant select, insert, update, delete on public.schedules to authenticated;
grant select, insert, update, delete on public.dose_events to authenticated;
grant select, insert, update, delete on public.inventory to authenticated;
grant select, insert on public.inventory_transactions to authenticated;
grant select, insert, update, delete on public.documents to authenticated;
grant select, insert, update, delete on public.messenger_links to authenticated;
grant select, insert, update on public.notification_deliveries to authenticated;

revoke update, delete on public.inventory_transactions from authenticated;

-- ---------------------------------------------------------------------------
-- RLS
-- ---------------------------------------------------------------------------
alter table public.profiles enable row level security;
alter table public.diagnoses enable row level security;
alter table public.medications enable row level security;
alter table public.courses enable row level security;
alter table public.schedules enable row level security;
alter table public.dose_events enable row level security;
alter table public.inventory enable row level security;
alter table public.inventory_transactions enable row level security;
alter table public.documents enable row level security;
alter table public.messenger_links enable row level security;
alter table public.notification_deliveries enable row level security;

-- profiles
create policy profiles_select_own on public.profiles
  for select to authenticated using (id = auth.uid());
create policy profiles_update_own on public.profiles
  for update to authenticated using (id = auth.uid()) with check (id = auth.uid());
-- insert делает trigger security definer; клиентский insert не нужен

-- diagnoses
create policy diagnoses_all_own on public.diagnoses
  for all to authenticated
  using (user_id = auth.uid())
  with check (user_id = auth.uid());

-- medications
create policy medications_all_own on public.medications
  for all to authenticated
  using (user_id = auth.uid())
  with check (user_id = auth.uid());

-- courses
create policy courses_all_own on public.courses
  for all to authenticated
  using (user_id = auth.uid())
  with check (user_id = auth.uid());

-- schedules
create policy schedules_all_own on public.schedules
  for all to authenticated
  using (user_id = auth.uid())
  with check (user_id = auth.uid());

-- dose_events
create policy dose_events_all_own on public.dose_events
  for all to authenticated
  using (user_id = auth.uid())
  with check (user_id = auth.uid());

-- inventory
create policy inventory_all_own on public.inventory
  for all to authenticated
  using (user_id = auth.uid())
  with check (user_id = auth.uid());

-- inventory_transactions: append-only (SELECT + INSERT), без UPDATE/DELETE
create policy inventory_transactions_select_own on public.inventory_transactions
  for select to authenticated
  using (user_id = auth.uid());
create policy inventory_transactions_insert_own on public.inventory_transactions
  for insert to authenticated
  with check (user_id = auth.uid());

-- documents
create policy documents_all_own on public.documents
  for all to authenticated
  using (user_id = auth.uid())
  with check (user_id = auth.uid());

-- messenger_links
create policy messenger_links_all_own on public.messenger_links
  for all to authenticated
  using (user_id = auth.uid())
  with check (user_id = auth.uid());

-- notification_deliveries: клиент читает; пишет в основном service_role (tick)
create policy notification_deliveries_select_own on public.notification_deliveries
  for select to authenticated
  using (user_id = auth.uid());
create policy notification_deliveries_insert_own on public.notification_deliveries
  for insert to authenticated
  with check (user_id = auth.uid());
create policy notification_deliveries_update_own on public.notification_deliveries
  for update to authenticated
  using (user_id = auth.uid())
  with check (user_id = auth.uid());

-- ---------------------------------------------------------------------------
-- Storage: приватный bucket медфайлов
-- Путь: {user_id}/{document_id}/{filename}
-- Signed URL TTL = 5 минут — задаётся клиентом при создании URL.
-- ---------------------------------------------------------------------------
insert into storage.buckets (id, name, public, file_size_limit, allowed_mime_types)
values (
  'medical-files',
  'medical-files',
  false,
  20971520, -- 20 MiB
  array[
    'application/pdf',
    'image/jpeg',
    'image/png',
    'image/webp',
    'image/heic',
    'text/plain'
  ]
)
on conflict (id) do nothing;

create policy medical_files_select_own
  on storage.objects for select to authenticated
  using (
    bucket_id = 'medical-files'
    and (storage.foldername(name))[1] = auth.uid()::text
  );

create policy medical_files_insert_own
  on storage.objects for insert to authenticated
  with check (
    bucket_id = 'medical-files'
    and (storage.foldername(name))[1] = auth.uid()::text
  );

create policy medical_files_update_own
  on storage.objects for update to authenticated
  using (
    bucket_id = 'medical-files'
    and (storage.foldername(name))[1] = auth.uid()::text
  )
  with check (
    bucket_id = 'medical-files'
    and (storage.foldername(name))[1] = auth.uid()::text
  );

create policy medical_files_delete_own
  on storage.objects for delete to authenticated
  using (
    bucket_id = 'medical-files'
    and (storage.foldername(name))[1] = auth.uid()::text
  );

-- ---------------------------------------------------------------------------
-- Realtime: клиенты слушают свои dose_events
-- ---------------------------------------------------------------------------
do $$
begin
  alter publication supabase_realtime add table public.dose_events;
exception
  when duplicate_object then null;
  when undefined_object then
    -- Локально без Supabase publication — пропускаем.
    null;
end;
$$;

commit;
