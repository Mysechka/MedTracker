# MedTracker v2 — Полный промт для реализации обновления

## Контекст проекта

MedTracker — персональное кроссплатформенное приложение для приёма лекарств и ведения медкарты. Это open-source проект, **НЕ в продакшене**, серверной инфраструктуры нет. Целевые платформы: macOS (.app) и Android (.apk).

### Стек технологий
- **.NET 10**, C# 14
- **Avalonia UI 12.1** (кроссплатформенный UI)
- **CommunityToolkit.Mvvm** (MVVM, ObservableProperty, RelayCommand)
- **Supabase** (PostgreSQL, Auth, Storage, Realtime, Edge Functions)
- **xUnit v3 + FluentAssertions + BenchmarkDotNet** (тесты)
- Clean Architecture (Domain → Application → Infrastructure → Presentation → UI)

### Структура проекта

```
src/Med.Domain/          — Чистый C#, доменные сущности, правила расписания, ноль зависимостей
src/Med.Application/     — Интерфейсы сервисов, Use Cases, Abstractions
src/Med.Infrastructure/  — Supabase SDK, локальные уведомления, маппинг
src/Med.Presentation/    — ViewModels (CommunityToolkit.Mvvm), навигация, стейт
src/Med.Ui/              — Avalonia Views, ViewLocator
src/Med.Desktop/         — Head-проект macOS
src/Med.Android/         — Head-проект Android
tests/Med.Domain.Tests/
tests/Med.Application.Tests/
tests/Med.Infrastructure.Tests/
tests/Med.Presentation.Tests/
tests/Med.Performance.Tests/
supabase/migrations/     — PostgreSQL DDL, RLS
supabase/functions/      — Edge Functions (Deno)
discord-bot/             — Discord-бот (TypeScript/Node.js)
```

---

## Карта ключевых файлов (текущее состояние)

### Domain (src/Med.Domain/)

**Entities:**
- `Profile.cs` — record(Guid UserId, string Username, string TimeZoneId, MealWindows Meals, TimeSpan ConfirmationWindow). Привязан к auth.uid(). Методы: Create, WithUsername, WithTimeZoneId, WithMeals, WithConfirmationWindow, ResolveTimeZone.
- `Medication.cs` — record с Name, Form, Dosage, Unit, Barcode?, Notes?. Все строки валидируются через ThrowIfNullOrWhiteSpace. Barcode используется для хранения метаданных (slots, tags).
- `Course.cs` — record(Guid Id, Guid UserId, Guid MedicationId, DateOnly StartsOn, DateOnly? EndsOn, int? DurationDays, bool IsActive, Guid? DiagnosisId). EffectiveEndsOn возвращает DateOnly.MaxValue для бессрочных. ContainsDate проверяет вхождение.
- `Schedule.cs` — record с типами FixedTimes/Interval/MealRelative/AsNeeded. Привязан к CourseId.
- `DoseEvent.cs` — record с state machine (Scheduled→Notified→Taken/Skipped/Missed/Cancelled). DedupeKey для идемпотентности.
- `Inventory.cs`, `InventoryTransaction.cs` — складской учёт, append-only журнал.
- `MessengerLink.cs` — record(Id, UserId, ChannelType[Telegram|Discord], ChatId?, ChannelId?, IsConfirmed, LinkCode?).
- `Diagnosis.cs`, `Document.cs`, `NotificationDelivery.cs`.

**Enums:** DocumentType, DoseEventSource, DoseEventState, InventoryTransactionKind, MealKind, MealRelation, MessengerChannelType, NotificationChannelType, NotificationDeliveryStatus, ScheduleType, WeekDays (flags).

**ValueObjects:** DedupeKey, MealWindows, MoscowOffset, WeekDaysExtensions.

**Logic:** InventoryRules, DoseEventTransitions (state machine), DoseEventMaterializer, LocalTimeConverter.

### Application (src/Med.Application/)

**Abstractions (интерфейсы):**
- `IAuthService` — CurrentSession, CurrentUserId, AuthStateChanged, SignUpWithPasswordAsync(email, password, username?), SignInWithPasswordAsync, SendMagicLinkAsync, UpdatePasswordAsync, SignOutAsync.
- `AuthSession` — record(Guid UserId, string Email, string AccessToken, string RefreshToken, DateTimeOffset ExpiresAt, string? Username).
- `ICourseRepository` — ListAsync, GetAsync, UpsertAsync, DeleteAsync.
- `IMedicationRepository`, `IScheduleRepository`, `IDoseEventRepository`, `IInventoryRepository`, `IProfileRepository`, `IMessengerLinkRepository`, `INotificationDeliveryRepository`, `IDocumentRepository`, `IFileStorage`, `INotificationService`, `IInventoryCommandService`, `IDoseTransitionService`, `IDoseEventMaterializer`, `IDoseEventRealtime`, `IEntityRealtimeSync`, `ITickInvoker`.

**UseCases:** ConfirmDoseUseCase, SkipDoseUseCase, UndoConfirmDoseUseCase, GetDayAgendaUseCase, MaterializeUpcomingDosesUseCase, RestockInventoryUseCase, UpdateProfileUseCase, UploadDocumentUseCase.

### Presentation (src/Med.Presentation/)

- `AuthViewModel.cs` — Вход/регистрация. Поля: Email, Password, Username, IsRegistrationMode. Команды: SignInAsync, SignUpAsync. Валидация: email обязателен для обоих режимов. CanSignUp/CanSignIn проверяют email содержит @ и точку.
- `ShellViewModel.cs` — Каркас навигации. Свойства: Current (ViewModelBase), ActiveNav (enum ShellNav), IsAuthenticated, AccountName. Все разделы кроме Today и Auth заблокированы для неаутентифицированных (метод EnsureAuthenticated). PromptLoginIfNeeded показывает уведомление.
- `MedicationsViewModel.cs` — Список лекарств. Имеет IsChronicCourse (bool), CourseDurationDays (int, default 14). При сохранении нового лекарства вызывает CreateInitialCourseAndSchedulesAsync, где IsChronicCourse=true → EndsOn=null, DurationDays=null; false → создаёт курс с конкретными датами. LoadItemsAsync загружает лекарства, их курсы, расписания, и формирует MedicationCardViewModel.
- `MedicationCardViewModel.cs` — Карточка лекарства с чекбоксами (1-6 слотов). ParseSlots/ParseTags извлекают из Barcode.
- `CoursesViewModel.cs` — Полный CRUD курсов и расписаний. Отдельная секция навигации.
- `AccountViewModel.cs` — Профиль: Username, Codeword (пароль), аватар с кропом. SignOut.
- `SettingsViewModel.cs`, `TodayViewModel.cs`, `MedicalCardViewModel.cs`, `DiagnosticsViewModel.cs`.
- Messaging: EntitySavedMessage<T>, EntityDeletedMessage<T>, ScheduleUpdatedMessage, DoseEventStatusChangedMessage, ProfileUpdatedMessage, NavigateToSectionMessage, ChangeSource (enum: Local/Realtime).

### Database (supabase/migrations/)

Таблицы: profiles, diagnoses, medications, courses, schedules, dose_events, inventory, inventory_transactions, documents, messenger_links, notification_deliveries.

Ключевые constraints на courses:
```sql
constraint courses_has_end check (ends_on is not null or duration_days is not null),
constraint courses_duration_positive check (duration_days is null or duration_days > 0),
constraint courses_ends_on_order check (ends_on is null or ends_on >= starts_on),
constraint courses_ends_matches_duration check (
  ends_on is null or duration_days is null
  or ends_on = (starts_on + (duration_days - 1))
)
```

Триггер handle_new_user() при регистрации создаёт профиль с username из meta_data или email.

RLS: все таблицы фильтруют по auth.uid() = user_id.

---

## ЗАДАНИЕ: Что нужно сделать

### Фаза 1: Двухэтапная регистрация с локальным хранением

#### Концепция
1. Пользователь скачивает приложение → вводит только никнейм → начинает пользоваться. Все данные хранятся **локально в SQLite** на устройстве. Это первичная регистрация.
2. Пользователю понравилось → он решает привязать Discord/Telegram-бота для уведомлений → проходит вторичную регистрацию: ввод email, создание пароля, после чего данные мигрируются из SQLite в Supabase → далее привязка мессенджеров.

#### Что создать

1. **Локальный слой хранения (SQLite)**
   - Создать `src/Med.Infrastructure/LocalStorage/` — новый модуль для работы с локальной SQLite БД.
   - Использовать `Microsoft.Data.Sqlite` или `SQLitePCLRaw` (совместимо с Avalonia на macOS и Android).
   - Создать `LocalDatabase.cs` — инициализация БД, создание таблиц (повторяющих структуру Supabase: medications, courses, schedules, dose_events, inventory, profiles). Схема должна быть максимально идентичной PostgreSQL-схеме (но без RLS, без auth.uid — всё принадлежит единственному локальному пользователю).
   - Реализовать локальные реализации репозиториев: `LocalMedicationRepository`, `LocalCourseRepository`, `LocalScheduleRepository`, `LocalDoseEventRepository`, `LocalInventoryRepository`, `LocalProfileRepository` — все имплементируют существующие интерфейсы из `Med.Application/Abstractions/`.
   - Файл БД хранить в `Environment.SpecialFolder.ApplicationData/MedTracker/local.db`.

2. **Локальная аутентификация**
   - Создать `LocalAuthService : IAuthService` — реализация без обращения к Supabase:
     - `CurrentSession` возвращает синтетическую сессию с `UserId = детерминированный GUID из никнейма` (или фиксированный GUID, хранимый в SQLite).
     - `CurrentUserId` возвращает этот GUID.
     - `SignUpWithPasswordAsync` → для первичной регистрации: просто сохранить username в локальный профиль, создать сессию.
     - `SignInWithPasswordAsync` → бросать NotSupportedException (для локального режима не нужен).
     - Новый метод (добавить в IAuthService): `bool IsLocalOnly { get; }` — true если пользователь не прошёл вторичную регистрацию.
   - Добавить в `IAuthService`:
     - `bool IsLocalOnly { get; }` — работает ли пользователь в локальном режиме
     - `Task MigrateToCloudAsync(string email, string password, CancellationToken)` — вторичная регистрация: создать Supabase-аккаунт, выгрузить данные из SQLite в облако, переключить все репозитории на Supabase.

3. **Миграция данных SQLite → Supabase**
   - Создать `DataMigrationService` — при вторичной регистрации:
     - Создать Supabase-аккаунт через `SignUpWithPasswordAsync`
     - Прочитать все данные из SQLite (medications, courses, schedules, dose_events, inventory)
     - Записать их в Supabase через существующие Supabase-репозитории (с новым userId)
     - Пометить миграцию как завершённую
     - Переключить DI-контейнер на Supabase-реализации репозиториев

4. **Переключатель режима (DI)**
   - Создать `RepositoryMode` enum: `Local`, `Cloud`.
   - Создать `RepositoryModeProvider` — синглтон, хранит текущий режим. По умолчанию `Local`. После вторичной регистрации переключается на `Cloud`.
   - В DI-регистрации: использовать фабрику, которая на основе текущего режима выдаёт локальные или облачные реализации репозиториев.

5. **Переработка AuthViewModel**
   - Заменить текущий экран (email+password+username) на:
     - **Первичная регистрация**: одно поле «Никнейм» + кнопка «Начать». Никакого email, никакого пароля.
     - Команда `StartLocalAsync` — создаёт локальный профиль, записывает в SQLite, создаёт синтетическую сессию.
   - В AccountViewModel добавить секцию «Привязка аккаунта»:
     - Видна только если `IsLocalOnly == true`
     - Поля: Email, Password
     - Кнопка «Привязать аккаунт» → вызывает `MigrateToCloudAsync`
     - После успеха — показать секцию привязки мессенджеров

6. **Переработка ShellViewModel**
   - Убрать блокировку навигации: анонимные (локальные) пользователи имеют доступ ко ВСЕМ разделам.
   - `EnsureAuthenticated` должен проверять наличие локальной сессии (никнейм введён), а не Supabase Auth.
   - `PromptLoginIfNeeded` → `PromptOnboardingIfNeeded` — показывать экран первичной регистрации (ввод никнейма) только если нет ни локальной, ни облачной сессии.
   - Индикатор в хедере: если `IsLocalOnly` — показывать «Локальный аккаунт» вместо username (или username + иконку).

7. **Добавить AuthSession.IsLocalOnly**
   - Добавить поле `bool IsLocalOnly = false` в record `AuthSession`.

#### Что изменить

- `IAuthService` — добавить `IsLocalOnly`, `MigrateToCloudAsync`
- `AuthSession` — добавить `IsLocalOnly`
- `AuthViewModel` — полная переработка (описано выше)
- `ShellViewModel` — убрать блокировку навигации, изменить `EnsureAuthenticated`
- `AccountViewModel` — добавить секцию привязки аккаунта
- DI-регистрация (в `PresentationServiceCollectionExtensions.cs` и аналогичные) — добавить переключатель локальных/облачных реализаций
- Avalonia Views: `AuthView.axaml` — новый UI первичной регистрации; `AccountView.axaml` — секция привязки аккаунта

---

### Фаза 2: Упрощение логики курсов

#### Концепция
- В карточке добавления/редактирования лекарства есть тогл (слайдер) «Приём курсом».
- **Тогл OFF (по умолчанию)**: лекарство на постоянном приёме, никогда не исчезает из списка. Курс создаётся с `EndsOn = null, DurationDays = null`.
- **Тогл ON**: появляется DatePicker для выбора даты окончания курса. После этой даты лекарство автоматически исчезает (скрывается) из списка лекарств.

#### Что создать

1. **Доменное правило видимости**
   - Создать `src/Med.Domain/MedicationVisibility.cs` — статический класс с методом:
     ```csharp
     public static bool IsVisibleOnDate(IReadOnlyList<Course> coursesForMedication, DateOnly today)
     ```
     - Если список курсов пуст → true (всегда видно)
     - Если хотя бы один курс бессрочный (EffectiveEndsOn == DateOnly.MaxValue) → true
     - Если хотя бы один курс содержит today (ContainsDate) → true
     - Если все курсы завершились → false (скрыть)

2. **Supabase-миграция**
   - Создать `supabase/migrations/20261002_allow_permanent_courses.sql`:
     ```sql
     ALTER TABLE public.courses DROP CONSTRAINT courses_has_end;
     -- Бессрочный курс: и ends_on, и duration_days могут быть NULL
     -- Остальные constraints (duration_positive, ends_on_order, ends_matches_duration) остаются
     ```

3. **Обновить SQLite-схему** (из Фазы 1) — таблица courses без constraint `courses_has_end`.

#### Что изменить

1. **Course.cs**
   - В `Course.Create`: убрать неявное требование `EndsOn != null || DurationDays != null`. Разрешить оба null (бессрочный курс).
   - `EffectiveEndsOn` уже корректно возвращает `DateOnly.MaxValue` — менять не нужно.

2. **MedicationsViewModel.cs**
   - Переименовать `IsChronicCourse` → `IsCourseMode` и **инвертировать логику**:
     - `IsCourseMode = false` (дефолт) → бессрочный приём
     - `IsCourseMode = true` → показать DatePicker, создать курс с конкретной EndsOn
   - Добавить свойство `DateOnly? CourseEndsOn` — дата окончания (из DatePicker).
   - В `CreateInitialCourseAndSchedulesAsync`:
     - `IsCourseMode == false` → `Course.Create(..., endsOn: null, durationDays: null)`
     - `IsCourseMode == true` → `Course.Create(..., endsOn: CourseEndsOn, durationDays: вычислить из разницы дат)`
   - В `LoadItemsAsync`: после загрузки лекарств — фильтровать через `MedicationVisibility.IsVisibleOnDate`, скрывая лекарства с истёкшими курсами.
   - Убрать `CourseDurationDays` (заменяется на DatePicker).

3. **Удалить раздел «Курсы» из навигации**
   - `ShellViewModel.cs` — убрать `GoCourses()`, `_courses`, убрать `ShellNav.Courses` из switch в `Receive(NavigateToSectionMessage)`.
   - `ShellNav.cs` — убрать значение `Courses` из enum (или пометить `[Obsolete]`).
   - `ShellView.axaml` (в Med.Ui) — убрать пункт «Курсы» из бокового меню навигации.
   - `CoursesViewModel.cs` — НЕ удалять файл (курсы как сущность остаются), но убрать из DI-регистрации как навигационный target.
   - `PresentationServiceCollectionExtensions.cs` — убрать регистрацию `CoursesViewModel` как навигационной зависимости ShellViewModel (но оставить если он используется внутренне).

4. **Avalonia Views (Med.Ui)**
   - Форма добавления лекарства (скриншот из задания — "Добавить лекарство"):
     - Тогл «Приём курсом» (Switch/ToggleSwitch) — привязан к `IsCourseMode`
     - При `IsCourseMode == true`: показать `CalendarDatePicker` или `DatePicker` для выбора даты окончания
     - При `IsCourseMode == false`: DatePicker скрыт
     - Убрать поле ввода количества дней (`CourseDurationDays`)

---

### Фаза 3: Тесты

Все тесты пишутся на xUnit v3 + FluentAssertions. Моки через NSubstitute или Moq (смотри что уже используется в проекте).

#### Тесты регистрации

1. `tests/Med.Presentation.Tests/Shell/AuthViewModelLocalRegistrationTests.cs`:
   - `StartLocal_WithValidUsername_CreatesLocalSession` — ввод никнейма → сессия создана, IsLocalOnly == true
   - `StartLocal_WithEmptyUsername_ShowsError` — пустой никнейм → ошибка валидации
   - `StartLocal_WithWhitespaceUsername_ShowsError` — пробельный никнейм → ошибка
   - `StartLocal_CreatesProfileInLocalDb` — после первичной регистрации профиль существует в локальной БД

2. `tests/Med.Presentation.Tests/Shell/ShellNavigationLocalTests.cs`:
   - `LocalUser_CanNavigateToMedications` — локальный пользователь может открыть раздел «Лекарства»
   - `LocalUser_CanNavigateToMedicalCard` — может открыть «Медкарту»
   - `LocalUser_CanNavigateToSettings` — может открыть «Настройки»
   - `LocalUser_CanNavigateToToday` — может открыть «Сегодня»
   - `NoSession_ShowsOnboarding` — без сессии показывается экран первичной регистрации

3. `tests/Med.Application.Tests/Security/DataMigrationTests.cs`:
   - `MigrateToCloud_TransfersAllMedications` — все лекарства из SQLite появляются в Supabase
   - `MigrateToCloud_TransfersAllCourses` — все курсы мигрируют
   - `MigrateToCloud_SwitchesToCloudMode` — после миграции IsLocalOnly == false
   - `MigrateToCloud_PreservesRelationships` — связи medication→course→schedule сохраняются

#### Тесты курсов

4. `tests/Med.Domain.Tests/Entities/CourseVisibilityTests.cs`:
   - `PermanentCourse_EffectiveEndsOn_IsMaxValue` — бессрочный курс (null, null) → DateOnly.MaxValue
   - `ExpiredCourse_ContainsDate_ReturnsFalse` — курс с EndsOn в прошлом → false
   - `ActiveCourse_ContainsDate_ReturnsTrue` — курс с EndsOn в будущем → true
   - `Course_Create_AllowsNullEndsAndDuration` — Course.Create с null/null не бросает исключение

5. `tests/Med.Domain.Tests/MedicationVisibilityTests.cs`:
   - `NoCourses_AlwaysVisible` — лекарство без курсов видно
   - `PermanentCourse_AlwaysVisible` — бессрочный курс → видно
   - `ExpiredCourse_NotVisible` — все курсы истекли → не видно
   - `MixedCourses_VisibleIfAnyActive` — один истёк + один бессрочный → видно
   - `MultiplePermanentCourses_Visible` — несколько бессрочных → видно

6. `tests/Med.Presentation.Tests/Medications/MedicationsViewModelCourseToggleTests.cs`:
   - `CourseMode_Off_CreatesPermanentCourse` — IsCourseMode=false → курс с EndsOn=null
   - `CourseMode_On_CreatesTimedCourse` — IsCourseMode=true + дата → курс с EndsOn
   - `CourseMode_Toggle_UpdatesFormVisibility` — переключение тогла → DatePicker показывается/скрывается
   - `LoadItems_HidesExpiredMedications` — лекарства с истёкшими курсами не в Items
   - `LoadItems_ShowsPermanentMedications` — бессрочные лекарства в Items

#### Тесты безопасности

7. `tests/Med.Application.Tests/Security/LocalSessionSecurityTests.cs`:
   - `LocalSession_HasDeterministicUserId` — userId стабилен между запусками
   - `LocalData_NotAccessibleAfterMigration` — после миграции локальные данные можно очистить
   - `SupabaseOptionsValidator_StillBlocksServiceRole` — существующий валидатор работает

#### Тесты производительности

8. `tests/Med.Performance.Tests/` — добавить бенчмарк:
   - `MedicationVisibilityBenchmark` — фильтрация 100/500/1000 лекарств с проверкой видимости курсов

---

## Порядок реализации (строго последовательно)

1. **Фаза 2.1**: Domain changes — `Course.Create` ослабление + `MedicationVisibility` + миграция SQL
2. **Фаза 2.2**: `MedicationsViewModel` — IsCourseMode, CourseEndsOn, фильтрация по видимости
3. **Фаза 2.3**: Удаление раздела «Курсы» из навигации (ShellViewModel, ShellNav, Views)
4. **Фаза 2.4**: Avalonia Views — тогл + DatePicker в форме лекарства
5. **Фаза 1.1**: SQLite-слой — LocalDatabase, локальные репозитории
6. **Фаза 1.2**: LocalAuthService + IAuthService расширения + AuthSession.IsLocalOnly
7. **Фаза 1.3**: AuthViewModel переработка (только никнейм)
8. **Фаза 1.4**: ShellViewModel — убрать блокировку навигации
9. **Фаза 1.5**: AccountViewModel — секция привязки аккаунта
10. **Фаза 1.6**: DataMigrationService + DI-переключатель режимов
11. **Фаза 1.7**: Avalonia Views — AuthView, AccountView обновления
12. **Фаза 3**: Все тесты (писать параллельно с каждой фазой, но финальный прогон в конце)

## Критерии приёмки

- [ ] Первый запуск → экран с вводом никнейма (без email/пароля) → все разделы доступны
- [ ] Лекарства, курсы, расписания сохраняются в локальной SQLite
- [ ] Тогл «Приём курсом» OFF → лекарство всегда в списке
- [ ] Тогл «Приём курсом» ON + дата → лекарство исчезает после даты
- [ ] Раздел «Курсы» убран из навигации
- [ ] В настройках/аккаунте есть кнопка «Привязать аккаунт» → email + пароль → данные мигрируют в Supabase
- [ ] После привязки — секция привязки Discord/Telegram (UI готов, бот-логика не реализуется)
- [ ] `dotnet test MedTracker.slnx` — все тесты проходят
- [ ] `dotnet build MedTracker.slnx` — сборка без ошибок и warnings

## Важные ограничения

- **НЕ ломать** существующую доменную логику (DoseEventMaterializer, InventoryRules, DoseEventTransitions)
- **НЕ менять** структуру Clean Architecture (направление зависимостей Domain ← Application ← Infrastructure ← Presentation)
- **НЕ добавлять** зависимости из Domain на внешние пакеты (Domain остаётся чистым C#)
- **Сохранять** все существующие комментарии и XML-документацию
- Для git-коммитов использовать: `git -c user.name="Mysechka" -c user.email="egorkostin71@gmail.com" commit -m "..."`
- Telegram-бот: реализовать только UI для привязки (поле ввода кода, кнопка), саму бот-логику НЕ писать
