# 🔍 MedTracker v1.0.1 — Промт для предрелизного аудита

> **Режим**: Только анализ. Код **НЕ** модифицировать. Найти баги, уязвимости и проблемы → предложить конкретные исправления в виде рекомендаций с указанием файлов и строк.

---

## Контекст проекта

**MedTracker** — кроссплатформенное приложение для учёта приёма лекарств и ведения медкарты.

### Стек
- **.NET 10**, C# 13/14, **Avalonia UI 12.1.1** (macOS + Android)
- **CommunityToolkit.Mvvm 8.4.2** (MVVM, ObservableProperty, RelayCommand)
- **Supabase** (PostgreSQL 17, Auth, Storage, Realtime, Edge Functions / Deno)
- **Discord Bot** (Node.js / TypeScript)
- **xUnit v3 + FluentAssertions + BenchmarkDotNet** (тесты)

### Архитектура: Clean Architecture (5 слоёв)
```
Med.Domain          → Чистый C#, доменные сущности, правила, ноль внешних зависимостей
Med.Application     → Интерфейсы сервисов, Use Cases, порты
Med.Infrastructure  → Supabase SDK, локальные уведомления, маппинг, SQLite, логирование
Med.Presentation    → ViewModels, навигация, стейт, мессенджер (WeakReferenceMessenger)
Med.Ui              → Avalonia Views, контролы, конвертеры, темы
Med.Desktop         → Head-проект macOS
Med.Android         → Head-проект Android
```

### Ключевые сущности
- `Profile` — пользовательский профиль (username, timezone, meal windows)
- `Medication` — лекарство (name, form, dosage, unit, barcode для слотов/тегов)
- `Course` — курс приёма (starts_on, ends_on?, duration_days?, is_active)
- `Schedule` — расписание (FixedTimes / Interval / MealRelative / AsNeeded)
- `DoseEvent` — событие приёма со state machine (Scheduled→Notified→Taken/Skipped/Missed/Cancelled)
- `Inventory` / `InventoryTransaction` — складской учёт препаратов
- `MessengerLink` — привязка Telegram/Discord
- `Document` — медицинские документы в Storage

### Режимы работы
- **Локальный режим** (SQLite) — первичная регистрация по никнейму, без email
- **Облачный режим** (Supabase) — вторичная регистрация с email/паролем, данные мигрируются из SQLite

### Предыдущие исправления (уже применены в v1.0.0 → v1.0.1)
В проекте уже были исправлены 25 дефектов (C-001..C-005, H-001..H-006, M-001..M-005 и другие). Нужно **проверить качество этих исправлений** и **найти регрессии и новые проблемы**.

---

## ЗАДАНИЕ

Выполни **глубокий инспекционный аудит всей кодовой базы** проекта MedTracker перед выпуском версии 1.0.1. Код **НЕ модифицировать**. Для каждого найденного дефекта указать: файл, строки, описание проблемы, уровень критичности и конкретную рекомендацию по исправлению.

Аудит состоит из **трёх разделов**:

---

## Раздел 1: 🛡 Аудит безопасности

Проанализировать **каждый файл** в `src/` на предмет следующих классов уязвимостей:

### 1.1 Инъекции и выполнение кода
- **Shell Command Injection**: Проверить ВСЕ вызовы `Process.Start`, `ProcessStartInfo`, `Runtime.exec` на наличие интерполяции пользовательского ввода. Убедиться, что `LocalNotificationService.cs` использует `ArgumentList` вместо единой строки аргументов. Проверить, что AppleScript, PowerShell и bash-команды не собираются через `$"..."` с данными пользователя.
- **SQL Injection**: Проверить ВСЕ обращения к SQLite в `src/Med.Infrastructure/LocalStorage/` — убедиться что используются параметризованные запросы (`@param`), а не конкатенация строк.
- **Path Traversal**: Проверить `Document.BuildStoragePath`, `SupabaseFileStorage.UploadFileAsync` и ВСЕ операции с файловой системой — убедиться в наличии `Path.GetFileName()`, проверки на `..`, `/`, `\`.

### 1.2 Аутентификация и авторизация
- **Утечка секретов**: Проверить `.env`, `.gitignore` — убедиться что `.env` не отслеживается Git. Проверить наличие API-ключей, токенов, паролей в исходном коде (hardcoded strings).
- **Supabase Service Role Key**: Убедиться что `SUPABASE_SERVICE_ROLE_KEY` **НИКОГДА** не используется в клиентском коде (только `anon` ключ). Проверить `SupabaseOptionsValidator`.
- **JWT / Session Management**: Проверить хранение и обновление токенов. Убедиться что refresh-токены не логируются. Проверить истечение сессий.
- **Анонимная аутентификация**: Проверить `LocalAuthService` — убедиться что синтетический `UserId` не предсказуем и не конфликтует с реальными Supabase UUID.

### 1.3 Защита данных
- **Cross-Tenant Data Leakage**: Проверить ВСЕ Realtime-подписки в `SupabaseEntityRealtimeSync.cs` — убедиться что для **КАЖДОЙ** таблицы (dose_events, medications, courses, schedules) указан фильтр `user_id=eq.{userId}`.
- **RLS (Row Level Security)**: Проверить SQL-миграции в `supabase/migrations/` — убедиться что для ВСЕХ таблиц с пользовательскими данными включен `ENABLE ROW LEVEL SECURITY` и есть политики на `auth.uid() = user_id`.
- **Медицинские данные**: Проверить что пути к документам, названия лекарств, дозировки НЕ логируются в плейнтексте. Проверить `FileLoggerProvider` на предмет записи чувствительных данных.

### 1.4 Платформенная безопасность
- **Android**: Проверить `AndroidManifest.xml` на наличие `POST_NOTIFICATIONS`, `SCHEDULE_EXACT_ALARM`. Убедиться что в `MainActivity.cs` есть runtime-запрос разрешений для Android 13+ (API 33).
- **P/Invoke (macOS)**: Проверить `MacDockIcon.cs` — потокобезопасность вызовов AppKit (только main thread), корректный `release` для NSImage, ABI-совместимость с ARM64.
- **Deserialization**: Проверить что `System.Text.Json` настроен безопасно (нет `TypeNameHandling.All` или аналога).

### 1.5 Миграция данных
- **Data Migration Service**: Проверить что миграция SQLite→Supabase атомарна или идемпотентна. Что происходит при обрыве сети посередине? Сохраняются ли связи (medication→course→schedule→dose_event)? Нет ли дублирования данных при повторной попытке?

---

## Раздел 2: ⚡ Аудит производительности

### 2.1 Утечки памяти и ресурсов
- **IDisposable**: Проверить ВСЕ ViewModels, сервисы, Realtime-подписки — убедиться что классы с подписками на события реализуют `IDisposable` и отписываются в `Dispose()`. Особое внимание:
  - `DiagnosticsViewModel` — подписка на `_realtime.Changed`
  - `TodayViewModel` — подписка на `WeakReferenceMessenger`
  - `SupabaseEntityRealtimeSync` — WebSocket-канал
- **Bitmap / unmanaged**: Проверить `PathToBitmapConverter.cs` — наличие кэша для `Avalonia.Media.Imaging.Bitmap`, использование `WeakReference`, отсутствие утечек нативной памяти Skia.
- **Task.Delay / CancellationTokenSource**: Проверить все `Task.Delay` в проекте — убедиться что они отменяются через `CancellationToken` и не висят в памяти бесконечно при выгрузке ViewModel.
- **Event Handler утечки**: Проверить что ни один ViewModel не подписывается на долгоживущий сервис (Singleton) через `+=` без парного `-=`. Если используется `WeakReferenceMessenger` — убедиться что `UnregisterAll(this)` вызывается в деструкторе/Dispose.

### 2.2 Сетевые запросы и I/O
- **N+1 запросы**: Проверить `TodayViewModel.LoadItemsAsync`, `MedicationsViewModel.LoadItemsAsync` — не делается ли отдельный запрос на каждое лекарство/курс/расписание. Предложить batch/join подход (RPC `get_day_agenda`).
- **Двойные запросы**: Убедиться что Realtime-события не приводят к двойному вызову `ReloadAsync()` (через одновременную подписку на `_realtime.Changed` и `IMessenger`).
- **Экспоненциальный backoff**: Проверить reconnect-логику в `SupabaseEntityRealtimeSync` — корректная ли задержка, есть ли jitter, ограничен ли максимум.

### 2.3 UI-производительность (Avalonia)
- **ObservableCollection**: Проверить что массовые обновления коллекций (загрузка лекарств, доз) выполняются на UI-потоке через `Dispatcher`. Не вызывают ли они множественные перерисовки (N вызовов `Add` вместо одного `Reset` с новой коллекцией).
- **XAML visual tree**: Проверить `MainView.axaml` на наличие дублирующихся `ContentControl`, лишних уровней вложенности. Проверить `CoursesView.axaml` на неиспользуемые `xmlns` импорты.
- **Конвертеры**: Проверить все IValueConverter — нет ли тяжёлых операций (файловый I/O, сетевые вызовы) в методе `Convert()`.

### 2.4 Планирование уведомлений
- **Android**: Убедиться что уведомления используют `AlarmManager.SetExactAndAllowWhileIdle()` + `BroadcastReceiver`, а **НЕ** `Task.Delay` в памяти. Проверить поведение при Doze mode, перезагрузке устройства (BOOT_COMPLETED), закрытии приложения.
- **Desktop**: Убедиться что уведомления не блокируют UI-поток и обрабатывают ошибки платформенных API (отсутствие `osascript`, отключённые уведомления).

### 2.5 Бенчмарки
- Проверить наличие и адекватность бенчмарков в `tests/Med.Performance.Tests/`:
  - `DoseEventMaterializer` — генерация расписания на 7/30/365 дней
  - Фильтрация лекарств по видимости курсов (100/500/1000 записей)
  - `EntityMappers` — маппинг 1000 сущностей

---

## Раздел 3: 🧹 Аудит чистоты и корректности кода

### 3.1 Доменная модель
- **Инварианты**: Проверить ВСЕ `record` в `Med.Domain/Entities/` — можно ли обойти валидацию `Create` через `with { ... }`. Убедиться что `init`-сеттеры содержат guard clauses для критичных свойств (`Name`, `Dosage`, `QuantityOnHand`).
- **State Machine**: Проверить `DoseEventTransitions.cs` — все ли переходы состояний (Scheduled→Taken, Scheduled→Skipped, Taken→Cancelled, Notified→Missed) корректны. Нет ли невозможных переходов. Не может ли `Missed` перетереть `Taken`.
- **Enum парсинг**: Проверить ВСЕ `Enum.Parse<T>` и `Enum.TryParse<T>` — убедиться что используется `ignoreCase: true`. Особенно в `EntityMappers.cs`, `JsonRpcParser.cs`.
- **Время и часовые пояса**: Проверить что НИГДЕ в `Med.Infrastructure` и `Med.Presentation` не используется `DateTime.Now`, `DateTime.UtcNow`, `DateTimeOffset.UtcNow` напрямую — только через абстракцию `IClock` / `ISystemClock`. Проверить `LocalTimeConverter`, `MoscowOffset`.

### 3.2 Архитектура Clean Architecture
- **Нарушение направления зависимостей**: Проверить `.csproj` файлы ВСЕХ проектов — убедиться что:
  - `Med.Domain` НЕ ссылается на ЛЮБОЙ другой проект и не имеет NuGet-зависимостей
  - `Med.Application` зависит ТОЛЬКО от `Med.Domain`
  - `Med.Infrastructure` зависит от `Med.Application` (и транзитивно от `Med.Domain`)
  - `Med.Presentation` зависит от `Med.Application` (НЕ от Infrastructure)
  - `Med.Ui` зависит от `Med.Presentation`
- **DI-регистрация**: Проверить что все зависимости зарегистрированы корректно (Singleton / Scoped / Transient). Не зарегистрированы ли ViewModels как Singleton (утечка состояния)?

### 3.3 Обработка ошибок
- **Пустые catch-блоки**: Найти ВСЕ `catch { }` и `catch (Exception) { }` без логирования — каждый должен хотя бы логировать `ILogger.LogWarning/LogError`.
- **Необработанные исключения**: Проверить наличие глобальных обработчиков в `App.axaml.cs` (`UnhandledException`, `UnobservedTaskException`).
- **null-safety**: Проверить использование `!` (null-forgiving operator) — каждый случай должен быть обоснован. Проверить nullable reference types на предмет потенциальных `NullReferenceException`.

### 3.4 Тестовое покрытие
- **Фиктивные тесты**: Найти тесты с `[Fact(Skip = "...")]` или `Assert.True(true)` — это маскировка отсутствия покрытия.
- **Непокрытые критические пути**:
  - Есть ли тесты для `DataMigrationService` (миграция SQLite→Supabase)?
  - Есть ли тесты для `LocalAuthService`?
  - Есть ли тесты для `LocalNotificationService` / `AndroidNotificationService`?
  - Есть ли тесты для Edge Functions (`supabase/functions/`)?
  - Есть ли тесты для path traversal в `Document`?
  - Есть ли тесты для state machine `DoseEventTransitions`?
- **Тесты утечек памяти**: Проверить `ViewModelMemoryLeakTests` — все ли ViewModels включены (особенно `DiagnosticsViewModel`).
- **Интеграционные тесты**: Проверить сколько тестов помечены как Skip — если Supabase-интеграция вообще не тестируется, отметить это как риск.

### 3.5 Качество кода
- **Мёртвый код**: Найти файлы, классы, методы, поля, которые объявлены, но нигде не используются. Особое внимание: `CoursesViewModel` — если раздел «Курсы» удалён из навигации, зачем он остаётся в DI?
- **Дублирование**: Найти повторяющиеся блоки кода (copy-paste). Особенно в Realtime-подписках, маппинге сущностей, XAML-шаблонах.
- **Нарушения SOLID**:
  - **SRP**: ViewModels с более чем 500 строками — кандидаты на декомпозицию.
  - **DIP**: Прямые обращения к конкретным классам вместо интерфейсов.
- **Code style**: Проверить соответствие `.editorconfig`. Неиспользуемые `using` / `xmlns`. Согласованность именования (русские vs английские комментарии, `IsChronicCourse` vs `IsCourseMode`).
- **TODO / FIXME / HACK**: Найти ВСЕ оставленные маркеры в коде и оценить — закрыты ли они или это незавершённая работа.

### 3.6 Конфигурация и сборка
- **Warnings**: Запустить `dotnet build -warnaserror MedTracker.slnx` и `dotnet test MedTracker.slnx` — отчитаться о результатах.
- **Пакеты**: Проверить `Directory.Packages.props` на наличие устаревших версий пакетов с известными уязвимостями.
- **global.json**: Проверить версию SDK — актуальна ли она.

### 3.7 База данных
- **Миграции**: Проверить порядок и корректность ВСЕХ миграций в `supabase/migrations/`. Нет ли конфликтующих constraints. Убедиться что миграция `allow_permanent_courses` корректно ослабляет `courses_has_end` без потери данных.
- **Realtime Publication**: Проверить миграцию `realtime_add_tables` — добавлены ли ВСЕ нужные таблицы в `supabase_realtime`.
- **Триггеры**: Проверить `handle_new_user()` — корректная ли обработка анонимных пользователей.

---

## Формат отчёта

Для каждого найденного дефекта использовать формат:

```
### [ID]: [Краткое название]
- **Критичность**: 🔴 CRITICAL / 🟠 HIGH / 🟡 MEDIUM / 🟢 LOW
- **Раздел аудита**: Безопасность / Производительность / Качество кода
- **Файл**: `путь/к/файлу.cs:L{строки}`
- **Описание**: Что именно не так и почему это проблема.
- **Воздействие**: Что произойдёт если не исправить (утечка данных, crash, потеря данных, деградация производительности).
- **Рекомендация**: Конкретные шаги для исправления (с примерами кода если нужно).
```

### Итоговая таблица

В конце отчёта предоставить сводную таблицу:

| ID | Критичность | Раздел | Файл | Краткое описание |
|:---:|:---:|:---:|---|---|

### Вердикт

Завершить отчёт вердиктом:
- **✅ READY FOR RELEASE** — критических и высоких проблем не найдено
- **⚠️ RELEASE WITH KNOWN ISSUES** — критических нет, но есть HIGH-проблемы с workaround
- **🚫 NOT READY FOR RELEASE** — есть CRITICAL-проблемы, блокирующие релиз

---

## Важные ограничения

> [!CAUTION]
> - **НЕ МОДИФИЦИРОВАТЬ КОД** — только анализ и рекомендации
> - **НЕ ПРОПУСКАТЬ ФАЙЛЫ** — проверить КАЖДЫЙ `.cs`, `.axaml`, `.csproj`, `.sql`, `.ts`, `.json` файл
> - **НЕ ВЫДУМЫВАТЬ ПРОБЛЕМЫ** — каждый дефект должен быть подтверждён конкретными строками кода
> - **НЕ ИГНОРИРОВАТЬ ИСПРАВЛЕННЫЕ БАГИ** — проверить что ВСЕ 25 ранее найденных дефектов (FIX-001..FIX-025 из `docs/bugfix_report.md`) действительно исправлены корректно и не создали регрессий
