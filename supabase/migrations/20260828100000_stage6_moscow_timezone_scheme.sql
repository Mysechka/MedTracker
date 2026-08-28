-- Стадия 6: часовой пояс профиля по схеме «Москва ± N часов».
-- Существующие миграции не правятся — это новый файл.
--
-- Пользователь больше не задаёт произвольную IANA-зону: он выбирает целое смещение
-- в часах относительно Москвы, а клиент приводит его к фиксированной зоне.
-- Смещение 0 — это 'Europe/Moscow', остальные — 'Etc/GMT±N' и 'Etc/UTC'.
--
-- Строки со старым значением 'UTC' НЕ переписываются: 'UTC' это Москва−3, то есть
-- уже корректная точка на шкале. Молча сдвигать чужое время на три часа нельзя.

begin;

-- Дефолт для новых профилей — московское время.
alter table public.profiles
  alter column time_zone_id set default 'Europe/Moscow';

-- Запрет произвольной зоны на уровне базы, а не только на уровне клиента.
-- Допустимый диапазон совпадает с доменным: от UTC−9 (Москва−12) до UTC+14 (Москва+11).
alter table public.profiles
  add constraint profiles_time_zone_moscow_scheme check (
    time_zone_id in ('Europe/Moscow', 'Etc/UTC', 'Etc/GMT', 'UTC')
    or time_zone_id ~ '^Etc/GMT\+[1-9]$'
    or time_zone_id ~ '^Etc/GMT-([1-9]|1[0-4])$'
  );

comment on column public.profiles.time_zone_id is
  'Фиксированная зона, полученная из смещения «Москва ± N часов». 0 → Europe/Moscow.';

commit;
