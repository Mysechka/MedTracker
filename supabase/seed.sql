-- ===========================================================================
-- MedTracker Seed Data (Моковые данные для быстрой локальной разработки)
-- Логин: dev@medtracker.local
-- Пароль: password123
-- ===========================================================================

-- 1. Создание тестового пользователя в Supabase Auth
insert into auth.users (
  instance_id,
  id,
  aud,
  role,
  email,
  encrypted_password,
  email_confirmed_at,
  recovery_sent_at,
  last_sign_in_at,
  raw_app_meta_data,
  raw_user_meta_data,
  created_at,
  updated_at,
  confirmation_token,
  email_change,
  email_change_token_new,
  recovery_token
) values (
  '00000000-0000-0000-0000-000000000000',
  '11111111-1111-1111-1111-111111111111',
  'authenticated',
  'authenticated',
  'dev@medtracker.local',
  crypt('password123', gen_salt('bf')),
  now(),
  now(),
  now(),
  '{"provider":"email","providers":["email"]}',
  '{"username":"dev_user"}',
  now(),
  now(),
  '',
  '',
  '',
  ''
) on conflict (id) do nothing;

insert into auth.identities (
  id,
  user_id,
  identity_data,
  provider,
  provider_id,
  last_sign_in_at,
  created_at,
  updated_at
) values (
  '11111111-1111-1111-1111-111111111111',
  '11111111-1111-1111-1111-111111111111',
  format('{"sub":"%s","email":"%s"}', '11111111-1111-1111-1111-111111111111', 'dev@medtracker.local')::jsonb,
  'email',
  '11111111-1111-1111-1111-111111111111',
  now(),
  now(),
  now()
) on conflict (provider, provider_id) do nothing;

-- 2. Обновление профиля пользователя
update public.profiles
set
  time_zone_id = 'Europe/Moscow',
  breakfast_time = '08:30:00',
  lunch_time = '13:30:00',
  dinner_time = '19:30:00',
  confirmation_window_minutes = 120
where id = '11111111-1111-1111-1111-111111111111';

-- 3. Лекарства (Medications)
insert into public.medications (id, user_id, name, form, dosage, unit, barcode, notes)
values
  (
    '22222222-2222-2222-2222-222222222222',
    '11111111-1111-1111-1111-111111111111',
    'Магний B6',
    'таблетка',
    '500',
    'мг',
    'slots:2;tags:Утром,Вечером',
    'Принимать во время еды, запивая стаканом воды'
  ),
  (
    '44444444-4444-4444-4444-444444444444',
    '11111111-1111-1111-1111-111111111111',
    'Витамин D3',
    'капли',
    '2000',
    'ME',
    'slots:1;tags:Утром',
    'Принимать после завтрака'
  ),
  (
    '66666666-6666-6666-6666-666666666666',
    '11111111-1111-1111-1111-111111111111',
    'Омега-3',
    'капсулы',
    '1000',
    'мг',
    'slots:1;tags:Днем',
    'Капсулу не разжевывать'
  )
on conflict (id) do nothing;

-- 4. Остатки на складе (Inventory)
insert into public.inventory (id, user_id, medication_id, quantity_on_hand, low_stock_threshold)
values
  ('22222222-0000-0000-0000-000000000001', '11111111-1111-1111-1111-111111111111', '22222222-2222-2222-2222-222222222222', 60, 10),
  ('44444444-0000-0000-0000-000000000001', '11111111-1111-1111-1111-111111111111', '44444444-4444-4444-4444-444444444444', 45, 5),
  ('66666666-0000-0000-0000-000000000001', '11111111-1111-1111-1111-111111111111', '66666666-6666-6666-6666-666666666666', 30, 8)
on conflict (id) do nothing;

-- 5. Курсы приёма (Courses)
insert into public.courses (id, user_id, medication_id, starts_on, ends_on, duration_days, is_active)
values
  (
    '33333333-3333-3333-3333-333333333333',
    '11111111-1111-1111-1111-111111111111',
    '22222222-2222-2222-2222-222222222222',
    current_date,
    current_date + 29,
    30,
    true
  ),
  (
    '55555555-5555-5555-5555-555555555555',
    '11111111-1111-1111-1111-111111111111',
    '44444444-4444-4444-4444-444444444444',
    current_date,
    current_date + 59,
    60,
    true
  ),
  (
    '77777777-7777-7777-7777-777777777777',
    '11111111-1111-1111-1111-111111111111',
    '66666666-6666-6666-6666-666666666666',
    current_date,
    current_date + 29,
    30,
    true
  )
on conflict (id) do nothing;

-- 6. Расписания приёма (Schedules)
insert into public.schedules (
  id,
  user_id,
  course_id,
  type,
  monday,
  tuesday,
  wednesday,
  thursday,
  friday,
  saturday,
  sunday,
  dose_amount,
  fixed_times,
  meal_kind,
  meal_relation,
  offset_minutes
) values
  (
    '33333333-0000-0000-0000-000000000001',
    '11111111-1111-1111-1111-111111111111',
    '33333333-3333-3333-3333-333333333333',
    'FixedTimes',
    true, true, true, true, true, true, true,
    1,
    array['09:00:00'::time, '21:00:00'::time],
    null,
    'Independent',
    0
  ),
  (
    '55555555-0000-0000-0000-000000000001',
    '11111111-1111-1111-1111-111111111111',
    '55555555-5555-5555-5555-555555555555',
    'MealRelative',
    true, true, true, true, true, true, true,
    1,
    '{}',
    'Breakfast',
    'After',
    15
  ),
  (
    '77777777-0000-0000-0000-000000000001',
    '11111111-1111-1111-1111-111111111111',
    '77777777-7777-7777-7777-777777777777',
    'MealRelative',
    true, true, true, true, true, true, true,
    1,
    '{}',
    'Lunch',
    'Before',
    0
  )
on conflict (id) do nothing;
