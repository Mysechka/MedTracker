# 📋 Итоговый технический отчёт: Комплексное устранение 25 дефектов в проекте MedTracker

**Проект**: MedTracker (.NET 10, C# 13, Avalonia UI 12.1.1, Supabase SDK 1.6.0)  
**Архитектура**: Clean Architecture (Domain → Application → Infrastructure → Presentation → UI / Head Projects)  
**Автор коммита**: `Mysechka <egorkostin71@gmail.com>`  
**Хэш коммита**: `3154228`  
**Статус верификации**: Сборка `-warnaserror` успешна (0 warnings, 0 errors), 210 тестов пройдено (0 failed).

---

## 1. Введение и общая методология

В ходе работы над проектом MedTracker была выполнена последовательная ликвидация 25 верифицированных дефектов (от критических уязвимостей безопасности до багов синхронизации, бизнес-логики и UI). Работа выполнялась строго по фазам с непрерывным прогоном `dotnet build` и `dotnet test`.

Применялись ключевые принципы разработки:
- **Бескомпромиссная безопасность**: отказ от shell-интерполяции строк, валидация путей в хранилище, предотвращение утечек данных между пользователями.
- **Целостность Clean Architecture**: бизнес-правила и инварианты инкапсулированы в слое `Domain`, контракты и сценарии — в `Application`, детали внешних SDK (Supabase, OS API) — в `Infrastructure`, реактивное состояние — в `Presentation`, а разметка — в `Ui`.
- **Потокобезопасность и управление ресурсами**: устранение race condition в коллекциях, детерминированное освобождение unmanaged памяти и подписок через `IDisposable`, кэширование через `WeakReference`.

---

## 2. Фаза 1: Security — Критические уязвимости безопасности

### FIX-001 🔴 Shell Command Injection (RCE) в `LocalNotificationService`
- **Проблема**: Пользовательские данные (название лекарства, дозировка, инструкция) интерполировались напрямую в строки команд `osascript`, `powershell.exe` и `notify-send`. Недостаточное экранирование одинарных и двойных кавычек приводило к выходу из строкового контекста и выполнению произвольного shell-кода (RCE).
- **Решение**:
  1. Полный отказ от передачи командной строки в виде единой строки.
  2. Использование свойства `ProcessStartInfo.ArgumentList` для всех поддерживаемых операционных систем (аргументы передаются операционной системе массивом в обход командного интерпретатора shell).
  3. Для macOS: аргумент `-e` передается через `ArgumentList`, в AppleScript-скрипте экранируются как двойные, так и одинарные кавычки (`EscapeAppleScript`).
  4. Для Windows: PowerShell вызывается с параметризованным блоком `& { param($title, $body) ... }`, аргументы передаются безопасно через `ArgumentList`.
  5. Для Linux: аргументы `notify-send` передаются отдельными элементами `ArgumentList`.
  6. Пустые блоки `catch { }` заменены на логирование через `ILogger.LogWarning(ex, ...)`.
- **Файл**: [`LocalNotificationService.cs`](file:///Users/mysechka/Desktop/MedTracker-main/src/Med.Infrastructure/Notifications/LocalNotificationService.cs)

### FIX-002 🔴 Cross-Tenant утечка данных через Realtime
- **Проблема**: Подписка на таблицу `schedules` в клиенте Supabase не содержала фильтрации по `user_id`. Каждый подключенный клиент получал realtime-события об изменении расписаний приёма всех остальных пользователей системы.
- **Решение**: В фильтр `PostgresChangesFilter` для таблицы `schedules` добавлен обязательный предикат:
  ```csharp
  Filter = $"user_id=eq.{_userId}"
  ```
- **Файл**: [`SupabaseEntityRealtimeSync.cs`](file:///Users/mysechka/Desktop/MedTracker-main/src/Med.Infrastructure/Supabase/Realtime/SupabaseEntityRealtimeSync.cs)

### FIX-003 🔴 Path Traversal в хранилище файлов `SupabaseFileStorage`
- **Проблема**: Метод `BuildStoragePath` формировал путь через конкатенацию `$"{userId}/{documentId}/{fileName.Trim()}"` без проверки на относительные переходы (`..`) и слеши. Злоумышленник мог передать имя вида `../../avatars/target.png` и перезаписать произвольные файлы в бакете.
- **Решение**:
  1. Отсечение любых путевых префиксов с помощью `Path.GetFileName(fileName.Trim())`.
  2. Проверка имени файла на наличие подстрок `..`, `/` и `\`.
  3. Выброс `ArgumentException` при нарушении инварианта безопасности.
  4. Написаны модульные тесты на блокировку path traversal в [`DocumentTests.cs`](file:///Users/mysechka/Desktop/MedTracker-main/tests/Med.Domain.Tests/Entities/DocumentTests.cs).
- **Файлы**: [`Document.cs`](file:///Users/mysechka/Desktop/MedTracker-main/src/Med.Domain/Entities/Document.cs), [`DocumentTests.cs`](file:///Users/mysechka/Desktop/MedTracker-main/tests/Med.Domain.Tests/Entities/DocumentTests.cs)

### FIX-004 🔴 Thread-Safety и memory leak в `MacDockIcon` (macOS)
- **Проблема**:
  1. Метод `MacDockIcon.TrySetFromPng()` вызывался из фонового пула через `Task.Run`, нарушая требование AppKit (вызов `NSApplication.sharedApplication` строго из main UI thread).
  2. Аллоцированный `NSImage` никогда не освобождался, вызывая утечку нативной памяти.
  3. Прямой P/Invoke к variadic `objc_msgSend` на ARM64 (Apple Silicon) нарушал ABI соглашение о вызовах.
- **Решение**:
  1. В `Program.cs` вызов перенесен в хук `AppBuilder.AfterSetup`, выполняющийся на главном потоке после инициализации Avalonia.
  2. Использованы строго типизированные делегаты `Marshal.GetDelegateForFunctionPointer` с конвенцией `CallingConvention.Cdecl`.
  3. Добавлен гарантированный вызов `release` для `NSImage` после передачи ссылки в `setApplicationIconImage:`.
- **Файлы**: [`Program.cs`](file:///Users/mysechka/Desktop/MedTracker-main/src/Med.Desktop/Program.cs), [`MacDockIcon.cs`](file:///Users/mysechka/Desktop/MedTracker-main/src/Med.Desktop/MacDockIcon.cs)

---

## 3. Фаза 2: Бизнес-логика — Критические баги данных

### FIX-005 🔴 Потеря доз дня в `DoseEventMaterializer.RematerializeFuture`
- **Проблема**: Конструкция `!takenLocalDates.Contains(e.LocalDate)` полностью удаляла из генерации ВСЕ последующие дозы на текущую дату, если хотя бы одна доза в этот день была принята. Пациент, принимающий лекарство 3 раза в день (утро, день, вечер), после утреннего приёма безвозвратно терял дневную и вечернюю дозы.
- **Решение**:
  1. Удален фильтр по датам `takenLocalDates`.
  2. Оставлена строгая дедупликация исключительно по композитному ключу `DedupeKey` (`$"{CourseId}_{ScheduleId}_{LocalDate}_{SlotTime}"`).
  3. Добавлен юнит-тест в [`DoseEventMaterializerTests.cs`](file:///Users/mysechka/Desktop/MedTracker-main/tests/Med.Domain.Tests/Scheduling/DoseEventMaterializerTests.cs), проверяющий сохранность оставшихся доз при 3-разовом приёме.
- **Файлы**: [`DoseEventMaterializer.cs`](file:///Users/mysechka/Desktop/MedTracker-main/src/Med.Domain/Scheduling/DoseEventMaterializer.cs), [`DoseEventMaterializerTests.cs`](file:///Users/mysechka/Desktop/MedTracker-main/tests/Med.Domain.Tests/Scheduling/DoseEventMaterializerTests.cs)

### FIX-006 & FIX-016 🔴 Потеря уведомлений и разрешения на Android
- **Проблема**: Уведомления планировались через in-memory `Task.Delay`. При переводе приложения в фоновый режим или выгрузке ОС Android процесс уничтожался, и все уведомления терялись. Кроме того, отсутствовал runtime-запрос разрешения `POST_NOTIFICATIONS` на Android 13+.
- **Решение**:
  1. Создан `[BroadcastReceiver]` [`NotificationReceiver.cs`](file:///Users/mysechka/Desktop/MedTracker-main/src/Med.Android/NotificationReceiver.cs), обрабатывающий срабатывание системных будильников.
  2. Сервис переписан на использование `AlarmManager.SetExactAndAllowWhileIdle()` с `PendingIntent.GetBroadcast()`.
  3. В [`AndroidManifest.xml`](file:///Users/mysechka/Desktop/MedTracker-main/src/Med.Android/Properties/AndroidManifest.xml) добавлены разрешения `SCHEDULE_EXACT_ALARM` и `USE_EXACT_ALARM`.
  4. В [`MainActivity.cs`](file:///Users/mysechka/Desktop/MedTracker-main/src/Med.Android/MainActivity.cs) реализован runtime-запрос разрешения `POST_NOTIFICATIONS` с проверкой `OperatingSystem.IsAndroidVersionAtLeast(33)`.
- **Файлы**: [`AndroidNotificationService.cs`](file:///Users/mysechka/Desktop/MedTracker-main/src/Med.Android/AndroidNotificationService.cs), [`NotificationReceiver.cs`](file:///Users/mysechka/Desktop/MedTracker-main/src/Med.Android/NotificationReceiver.cs), [`MainActivity.cs`](file:///Users/mysechka/Desktop/MedTracker-main/src/Med.Android/MainActivity.cs), [`AndroidManifest.xml`](file:///Users/mysechka/Desktop/MedTracker-main/src/Med.Android/Properties/AndroidManifest.xml)

### FIX-007 🟠 Исчезающие карточки и сломанный прогресс в `TodayViewModel`
- **Проблема**: Принятые или пропущенные дозы физически удалялись из коллекции `Items` через `Task.Delay(3000)`. Из-за уменьшения `Items.Count` метод `UpdateProgress()` ломал соотношение «Принято X из Y», и к концу дня счетчик показывал «0 из 0».
- **Решение**:
  1. Удален метод отложенного удаления `ScheduleRowRemovalAsync`, токены отмены и флаги удаления.
  2. В [`DoseRowViewModel.cs`](file:///Users/mysechka/Desktop/MedTracker-main/src/Med.Presentation/Today/DoseRowViewModel.cs) добавлено реактивное свойство `IsCompleted`.
  3. В [`TodayViewModel.cs`](file:///Users/mysechka/Desktop/MedTracker-main/src/Med.Presentation/Today/TodayViewModel.cs) зафиксировано общее число доз за день `_totalDosesToday`.
  4. В [`TodayView.axaml`](file:///Users/mysechka/Desktop/MedTracker-main/src/Med.Ui/Views/Screens/TodayView.axaml) добавлен стиль `completed`, отображающий принятые дозы приглушенным цветом с галочкой, сохраняя историю приёма на экране.
- **Файлы**: [`DoseRowViewModel.cs`](file:///Users/mysechka/Desktop/MedTracker-main/src/Med.Presentation/Today/DoseRowViewModel.cs), [`TodayViewModel.cs`](file:///Users/mysechka/Desktop/MedTracker-main/src/Med.Presentation/Today/TodayViewModel.cs), [`TodayView.axaml`](file:///Users/mysechka/Desktop/MedTracker-main/src/Med.Ui/Views/Screens/TodayView.axaml)

### FIX-008 🟠 Захардкоженный 14-дневный курс
- **Проблема**: При создании лекарства автоматически генерировался курс со строгой длительностью 14 дней, что ломало логику для хронических пациентов.
- **Решение**:
  1. В [`Course.cs`](file:///Users/mysechka/Desktop/MedTracker-main/src/Med.Domain/Entities/Course.cs) сняты ограничения на обязательность даты окончания: свойства `EndsOn` и `DurationDays` могут быть `null` для бессрочных курсов (`EffectiveEndsOn` возвращает `DateOnly.MaxValue`).
  2. В [`MedicationsViewModel.cs`](file:///Users/mysechka/Desktop/MedTracker-main/src/Med.Presentation/Medications/MedicationsViewModel.cs) добавлены `IsChronicCourse` и `CourseDurationDays`.
  3. В разметку [`MedicationsView.axaml`](file:///Users/mysechka/Desktop/MedTracker-main/src/Med.Ui/Views/Screens/MedicationsView.axaml) добавлены чекбокс «Бессрочный приём» и поле количества дней приёма.
- **Файлы**: [`Course.cs`](file:///Users/mysechka/Desktop/MedTracker-main/src/Med.Domain/Entities/Course.cs), [`MedicationsViewModel.cs`](file:///Users/mysechka/Desktop/MedTracker-main/src/Med.Presentation/Medications/MedicationsViewModel.cs), [`MedicationsView.axaml`](file:///Users/mysechka/Desktop/MedTracker-main/src/Med.Ui/Views/Screens/MedicationsView.axaml)

---

## 4. Фаза 3: Realtime — Утечки, дублирование, reconnect

### FIX-009 🟠 Утечка памяти в `DiagnosticsViewModel`
- **Проблема**: Подписка на `_realtime.Changed` осуществлялась анонимным лямбда-методом без отписки и без реализации `IDisposable`.
- **Решение**: Реализован `IDisposable`, анонимный метод заменен на именованный `OnRealtimeChanged` с гарантированной отпиской в `Dispose()`.
- **Файл**: [`DiagnosticsViewModel.cs`](file:///Users/mysechka/Desktop/MedTracker-main/src/Med.Presentation/Diagnostics/DiagnosticsViewModel.cs)

### FIX-010 🟠 Двойная обработка Realtime-событий в `TodayViewModel`
- **Проблема**: `TodayViewModel` слушал одновременно `_realtime.Changed` и `DoseEventStatusChangedMessage` от `IMessenger`, что при каждом изменении вызывало двойной вызов `ReloadAsync()`.
- **Решение**: Удалена прямая подписка на `_realtime.Changed`, обработка событий полностью централизована через `WeakReferenceMessenger`.
- **Файл**: [`TodayViewModel.cs`](file:///Users/mysechka/Desktop/MedTracker-main/src/Med.Presentation/Today/TodayViewModel.cs)

### FIX-011 🟠 Дублирование подписок на `dose_events`
- **Проблема**: `SupabaseDoseEventRealtime` и `SupabaseEntityRealtimeSync` создавали два независимых WebSocket-канала на одну и ту же таблицу `dose_events`.
- **Решение**: `SupabaseDoseEventRealtime` преобразован в легковесный фасад над `IEntityRealtimeSync`, делегирующий события без создания второго сетевого соединения.
- **Файл**: [`SupabaseDoseEventRealtime.cs`](file:///Users/mysechka/Desktop/MedTracker-main/src/Med.Infrastructure/Supabase/Realtime/SupabaseDoseEventRealtime.cs)

### FIX-012 🟠 Отсутствие reconnect в Realtime-сервисах
- **Проблема**: При разрыве сетевого соединения WebSocket-канал оставался в нерабочем состоянии без попыток переподключения.
- **Решение**:
  1. Реализован интерфейс `IDisposable`.
  2. Добавлен цикл переподключения с экспоненциальной задержкой (exponential backoff от 1 до 30 секунд): `ScheduleReconnect()`.
  3. Обработчики изменений защищены `try/catch` с логированием, при ошибках подписки ссылка на канал сбрасывается (`_channel = null`).
- **Файл**: [`SupabaseEntityRealtimeSync.cs`](file:///Users/mysechka/Desktop/MedTracker-main/src/Med.Infrastructure/Supabase/Realtime/SupabaseEntityRealtimeSync.cs)

### FIX-013 🟡 Неполная публикация таблиц в Realtime
- **Проблема**: В системную публикацию `supabase_realtime` была включена только таблица `dose_events`, тогда как клиент ожидал обновления `medications`, `courses` и `schedules`.
- **Решение**: Создана миграция [`20261001000000_realtime_add_tables.sql`](file:///Users/mysechka/Desktop/MedTracker-main/supabase/migrations/20261001000000_realtime_add_tables.sql), добавляющая недостающие таблицы в публикацию.
- **Файл**: [`20261001000000_realtime_add_tables.sql`](file:///Users/mysechka/Desktop/MedTracker-main/supabase/migrations/20261001000000_realtime_add_tables.sql)

---

## 5. Фаза 4: Навигация и UI

### FIX-014 🟠 Раздел «Курсы» недоступен из навигации
- **Проблема**: Экземпляр `CoursesViewModel` передавался в DI, но отсутствовал в enum `ShellNav`, не имел команды навигации и пунктов меню в интерфейсе.
- **Решение**:
  1. В [`ShellNav.cs`](file:///Users/mysechka/Desktop/MedTracker-main/src/Med.Presentation/Shell/ShellNav.cs) добавлен элемент `Courses`.
  2. В [`ShellViewModel.cs`](file:///Users/mysechka/Desktop/MedTracker-main/src/Med.Presentation/Shell/ShellViewModel.cs) добавлены `IsCoursesSelected`, команда `GoCoursesCommand` и обработчик сообщений `NavigateToSectionMessage`.
  3. В [`MainView.axaml`](file:///Users/mysechka/Desktop/MedTracker-main/src/Med.Ui/Views/MainView.axaml) добавлены кнопки перехода в боковое десктопное меню и мобильную панель навигации (5 колонок).
- **Файлы**: [`ShellNav.cs`](file:///Users/mysechka/Desktop/MedTracker-main/src/Med.Presentation/Shell/ShellNav.cs), [`ShellViewModel.cs`](file:///Users/mysechka/Desktop/MedTracker-main/src/Med.Presentation/Shell/ShellViewModel.cs), [`MainView.axaml`](file:///Users/mysechka/Desktop/MedTracker-main/src/Med.Ui/Views/MainView.axaml), [`NavigateToSectionMessage.cs`](file:///Users/mysechka/Desktop/MedTracker-main/src/Med.Presentation/Messaging/NavigateToSectionMessage.cs)

### FIX-015 🟠 Кнопки без привязок в `MedicalCardView` и `MedicationsView`
- **Проблема**:
  - Кнопка «На главную» в `MedicalCardView` не была привязана к команде.
  - Кнопка меню ≡ в карточках лекарств не имела обработчика.
- **Решение**:
  1. В [`MedicalCardViewModel.cs`](file:///Users/mysechka/Desktop/MedTracker-main/src/Med.Presentation/MedicalCard/MedicalCardViewModel.cs) добавлена команда `GoBackCommand`, отправляющая сообщение `NavigateToSectionMessage(ShellNav.Today)`. В разметке кнопки проставлен `Command="{Binding GoBackCommand}"`.
  2. В [`MedicationsViewModel.cs`](file:///Users/mysechka/Desktop/MedTracker-main/src/Med.Presentation/Medications/MedicationsViewModel.cs) и [`MedicationCardViewModel.cs`](file:///Users/mysechka/Desktop/MedTracker-main/src/Med.Presentation/Medications/MedicationCardViewModel.cs) добавлены команды `DeleteCardCommand` и `ResetSlotsCommand`.
  3. К кнопке ≡ в [`MedicationsView.axaml`](file:///Users/mysechka/Desktop/MedTracker-main/src/Med.Ui/Views/Screens/MedicationsView.axaml) привязан `MenuFlyout` с действиями: «Редактировать», «Сбросить отметки» и «Удалить».
- **Файлы**: [`MedicalCardViewModel.cs`](file:///Users/mysechka/Desktop/MedTracker-main/src/Med.Presentation/MedicalCard/MedicalCardViewModel.cs), [`MedicalCardView.axaml`](file:///Users/mysechka/Desktop/MedTracker-main/src/Med.Ui/Views/Screens/MedicalCardView.axaml), [`MedicationsView.axaml`](file:///Users/mysechka/Desktop/MedTracker-main/src/Med.Ui/Views/Screens/MedicationsView.axaml), [`MedicationCardViewModel.cs`](file:///Users/mysechka/Desktop/MedTracker-main/src/Med.Presentation/Medications/MedicationCardViewModel.cs)

### FIX-017 🟠 Синтетические Email (`@medtracker.local`)
- **Проблема**: `SynthesizeEmail` генерировал фиктивные локальные адреса на основе имени пользователя, исключая восстановление пароля и подтверждение учетных записей.
- **Решение**:
  1. Метод `SynthesizeEmail` полностью удален.
  2. Добавлено реактивное свойство `Email`, валидация синтаксиса почты (`CanSignUp`, `CanSignIn`, обязательное наличие `@` и `.`).
  3. В [`AuthView.axaml`](file:///Users/mysechka/Desktop/MedTracker-main/src/Med.Ui/Views/Screens/AuthView.axaml) добавлено поле ввода Email с подсказкой «Email (обязательно)».
  4. Обновлены юнит-тесты авторизации в [`AuthViewModelTests.cs`](file:///Users/mysechka/Desktop/MedTracker-main/tests/Med.Presentation.Tests/Shell/AuthViewModelTests.cs).
- **Файлы**: [`AuthViewModel.cs`](file:///Users/mysechka/Desktop/MedTracker-main/src/Med.Presentation/Shell/AuthViewModel.cs), [`AuthView.axaml`](file:///Users/mysechka/Desktop/MedTracker-main/src/Med.Ui/Views/Screens/AuthView.axaml), [`AuthViewModelTests.cs`](file:///Users/mysechka/Desktop/MedTracker-main/tests/Med.Presentation.Tests/Shell/AuthViewModelTests.cs)

---

## 6. Фаза 5: Стабильность и Robustness

### FIX-018 🟡 Утечка памяти в `PathToBitmapConverter`
- **Проблема**: При каждом обращении создавался новый экземпляр `Avalonia.Media.Imaging.Bitmap` из потока файла без кэширования, что вызывало прогрессирующий рост аллокаций памяти.
- **Решение**: Внедрен статический потокобезопасный кэш `ConcurrentDictionary<string, WeakReference<Bitmap>>`, предотвращающий повторные аллокации и корректно освобождающий память при сборке мусора.
- **Файл**: [`PathToBitmapConverter.cs`](file:///Users/mysechka/Desktop/MedTracker-main/src/Med.Ui/Controls/PathToBitmapConverter.cs)

### FIX-019 🟡 Регистрозависимый `Enum.Parse`
- **Проблема**: `Enum.Parse<TEnum>(value, ignoreCase: false)` вызывал исключения при несовпадении регистра строковых данных от БД/RPC.
- **Решение**: В мапперах и парсерах включен флаг `ignoreCase: true`:
  - `Enum.Parse<TEnum>(value, ignoreCase: true)` в `EntityMappers.cs`
  - `Enum.TryParse(text, ignoreCase: true, out TEnum parsed)` в `JsonRpcParser.cs`
- **Файлы**: [`EntityMappers.cs`](file:///Users/mysechka/Desktop/MedTracker-main/src/Med.Infrastructure/Mapping/EntityMappers.cs), [`JsonRpcParser.cs`](file:///Users/mysechka/Desktop/MedTracker-main/src/Med.Infrastructure/Supabase/JsonRpcParser.cs)

### FIX-020 🟡 Прямой `DateTimeOffset.UtcNow` вместо `ISystemClock`
- **Проблема**: Прямые вызовы статического системного времени в сервисах RPC нарушали воспроизводимость тестов на границах временных поясов и интервалов.
- **Решение**: В [`DoseEventMaterializerService.cs`](file:///Users/mysechka/Desktop/MedTracker-main/src/Med.Infrastructure/Supabase/Services/DoseEventMaterializerService.cs) и [`DoseTransitionService.cs`](file:///Users/mysechka/Desktop/MedTracker-main/src/Med.Infrastructure/Supabase/Services/DoseTransitionService.cs) через конструктор внедрен `ISystemClock`, вызовы заменены на `_clock.UtcNow`.
- **Файлы**: [`DoseEventMaterializerService.cs`](file:///Users/mysechka/Desktop/MedTracker-main/src/Med.Infrastructure/Supabase/Services/DoseEventMaterializerService.cs), [`DoseTransitionService.cs`](file:///Users/mysechka/Desktop/MedTracker-main/src/Med.Infrastructure/Supabase/Services/DoseTransitionService.cs)

### FIX-021 🟡 Обход инвариантов record через `with`
- **Проблема**: Использование позиционных параметров в `record Medication` и `record Inventory` позволяло выполнять мутации `med with { Name = "" }` или `inv with { QuantityOnHand = -10 }` в обход доменных правил.
- **Решение**:
  1. В `Medication.cs` и `Inventory.cs` свойства переведены на аксессоры `init` с валидацией аргументов при инициализации и копировании через `with`.
  2. В `InventoryRules.cs` перед списанием дозы добавлена предварительная проверка остатка:
     ```csharp
     decimal newQuantity = inventory.QuantityOnHand - doseAmount;
     if (newQuantity < 0)
         return new InventoryMutationResult(TransitionOutcome.Rejected, ..., "Недостаточно остатка на складе.");
     ```
  3. Добавлены тесты в [`MedicationTests.cs`](file:///Users/mysechka/Desktop/MedTracker-main/tests/Med.Domain.Tests/Entities/MedicationTests.cs).
- **Файлы**: [`Medication.cs`](file:///Users/mysechka/Desktop/MedTracker-main/src/Med.Domain/Entities/Medication.cs), [`Inventory.cs`](file:///Users/mysechka/Desktop/MedTracker-main/src/Med.Domain/Entities/Inventory.cs), [`InventoryRules.cs`](file:///Users/mysechka/Desktop/MedTracker-main/src/Med.Domain/Inventory/InventoryRules.cs), [`MedicationTests.cs`](file:///Users/mysechka/Desktop/MedTracker-main/tests/Med.Domain.Tests/Entities/MedicationTests.cs)

### FIX-022 🟠 Race condition в `EntityChangeDeduplicator`
- **Проблема**: Метод `TrimExpired` удалял записи по ключу через `TryRemove` без проверки метки времени, что приводило к удалению свежей записи, только что добавленной параллельным потоком. Также искусственный порог `Count > 100` приводил к вечной утечке устаревших записей при меньшем числе сущностей.
- **Решение**:
  1. Удален порог `Count > 100`.
  2. Удаление устаревших элементов переведено на атомарный вызов `ICollection<KeyValuePair<...>>.Remove(kvp)`, гарантирующий, что удаляется только то значение, метка времени которого действительно устарела.
- **Файл**: [`EntityChangeDeduplicator.cs`](file:///Users/mysechka/Desktop/MedTracker-main/src/Med.Presentation/Sync/EntityChangeDeduplicator.cs)

### FIX-023 🟠 Race condition при `Dispose` в `FileLoggerProvider`
- **Проблема**: Фоновый поток записи логов прерывался таймаутом в 1 секунду, после чего вызывался `_logQueue.Dispose()`, что приводило к исключению `ObjectDisposedException` в фоновом потоке, еще не успевшем сбросить буфер.
- **Решение**: Порядок завершения скорректирован: сначала вызывается `_logQueue.CompleteAdding()`, таймаут ожидания задачи дренажа `_outputTask.Wait()` увеличен до 10 секунд с перехватом `AggregateException` / `OperationCanceledException`, и только после этого освобождаются ресурсы очереди.
- **Файл**: [`FileLoggerProvider.cs`](file:///Users/mysechka/Desktop/MedTracker-main/src/Med.Infrastructure/Logging/FileLoggerProvider.cs)

### FIX-024 🟠 Отсутствие глобального обработчика ошибок
- **Проблема**: Необработанные ошибки UI или фоновых задач приводили к мгновенному падению процесса без диагностического вывода.
- **Решение**: В `App.axaml.cs` подключены глобальные обработчики `AppDomain.CurrentDomain.UnhandledException` и `TaskScheduler.UnobservedTaskException`, блок инициализации `OnFrameworkInitializationCompleted()` обернут в `try/catch` с подробным логированием.
- **Файл**: [`App.axaml.cs`](file:///Users/mysechka/Desktop/MedTracker-main/src/Med.Ui/App.axaml.cs)

---

## 7. Фаза 6: Мелочи (LOW)

### FIX-025 🟢 Конфликт `ContentControl` в `MainView.axaml`
- **Проблема**: В разметке присутствовали два `ContentControl Content="{Binding Current}"` (для десктопного и мобильного шаблонов), что при одновременной инициализации могло приводить к конфликту visual parent.
- **Решение**: На оба элемента явно наложены взаимоисключающие условия видимости:
  - Десктоп: `IsVisible="{Binding !#Root.IsCompact}"`
  - Мобильный: `IsVisible="{Binding #Root.IsCompact}"`
- **Файл**: [`MainView.axaml`](file:///Users/mysechka/Desktop/MedTracker-main/src/Med.Ui/Views/MainView.axaml)

---

## 8. Результаты верификации и тестирования

| Проект | Количество тестов | Результат |
|---|---|---|
| `Med.Domain.Tests` | 77 | ✅ Passed (0 failed) |
| `Med.Presentation.Tests` | 105 | ✅ Passed (0 failed) |
| `Med.Application.Tests` | 11 | ✅ Passed (0 failed) |
| `Med.Infrastructure.Tests` | 17 (2 skipped live-integration) | ✅ Passed (0 failed) |
| **Всего** | **210 тестов** | **100% успешно** |

- **Статический анализ**: `dotnet build -warnaserror MedTracker.slnx` завершается с **0 предупреждений** и **0 ошибок**.
- **Проверка в реальном времени**: Приложение успешно запущено в среде `Development`, локальный стек Supabase поднят с применением всех миграций, успешно протестирован вход под учетной записью пользователя.
