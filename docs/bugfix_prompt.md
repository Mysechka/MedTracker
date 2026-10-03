# 🛠 MedTracker v1.0.1 — Промт для исправления всех дефектов аудита

## Контекст проекта

**MedTracker** — кроссплатформенное приложение для учёта приёма лекарств и ведения медкарты.

### Стек
- **.NET 10**, C# 13, **Avalonia UI 12.1.1** (macOS + Android)
- **CommunityToolkit.Mvvm 8.4.2** (MVVM, ObservableProperty, RelayCommand)
- **Supabase** (PostgreSQL 17, Auth, Storage, Realtime, Edge Functions / Deno)
- **Discord Bot** (Node.js / TypeScript)
- **xUnit v3 + FluentAssertions** (тесты)

### Архитектура: Clean Architecture
```
Med.Domain          → Чистый C#, доменные сущности, ноль зависимостей
Med.Application     → Интерфейсы, Use Cases, порты
Med.Infrastructure  → Supabase SDK, уведомления, маппинг, SQLite, логирование
Med.Presentation    → ViewModels, навигация, мессенджер
Med.Ui              → Avalonia Views, контролы, конвертеры
Med.Desktop         → Head-проект macOS
Med.Android         → Head-проект Android
```

---

## ЗАДАНИЕ

Исправить **все 19 дефектов**, выявленных в ходе аудита проекта MedTracker (`docs/Bug_report.md`). Работа ведётся **строго по фазам** — каждая фаза завершается прогоном `dotnet build` и `dotnet test`. Код модифицируется **минимально и точечно** — не рефакторить то, что не связано с дефектом.

---

## Фаза 1: 🔴 CRITICAL — Безопасность и потеря данных

> [!CAUTION]
> Эта фаза **блокирует релиз**. Все 5 дефектов должны быть исправлены до перехода к следующей фазе.

---

### FIX C-001: Shell Command Injection (RCE) в `LocalNotificationService`

**Файл**: `src/Med.Infrastructure/Notifications/LocalNotificationService.cs` (строки ~L103–L162)

**Проблема**: Пользовательские данные (`title`, `body` — название лекарства, дозировка) интерполируются напрямую в строки команд `osascript`, `powershell`, `notify-send`. Экранирование через `Replace("\"", "\\\"")` не защищает от одинарных кавычек в `osascript -e '...'`. Инъекция вида `'; do shell script "rm -rf /"; #` приводит к RCE.

**Что сделать**:

1. **Полностью заменить** передачу аргументов через единую строку на `ProcessStartInfo.ArgumentList` (массив аргументов, который ОС передаёт без интерпретации shell):

2. **macOS** (`osascript`):
   ```csharp
   private static void ShowMacNotification(string title, string body)
   {
       var escapedTitle = title.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("'", "'\\''");
       var escapedBody = body.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("'", "'\\''");
       var script = $"display notification \"{escapedBody}\" with title \"{escapedTitle}\"";
       
       var psi = new ProcessStartInfo("osascript");
       psi.ArgumentList.Add("-e");
       psi.ArgumentList.Add(script);  // ArgumentList НЕ проходит через shell
       psi.UseShellExecute = false;
       psi.CreateNoWindow = true;
       Process.Start(psi);
   }
   ```

3. **Windows** (`powershell`): Использовать параметризованный блок `& { param($t, $b) ... }` с передачей `$t` и `$b` через `ArgumentList`:
   ```csharp
   var psi = new ProcessStartInfo("powershell");
   psi.ArgumentList.Add("-NoProfile");
   psi.ArgumentList.Add("-Command");
   psi.ArgumentList.Add("& { param($t,$b) ... }");  // шаблон без интерполяции
   psi.ArgumentList.Add(title);
   psi.ArgumentList.Add(body);
   ```

4. **Linux** (`notify-send`):
   ```csharp
   var psi = new ProcessStartInfo("notify-send");
   psi.ArgumentList.Add(title);
   psi.ArgumentList.Add(body);
   ```

5. **Все пустые `catch { }`** в этом файле — заменить на `catch (Exception ex) { _logger.LogWarning(ex, "Failed to show notification"); }`. Добавить `ILogger` через конструктор если отсутствует.

**Критерий приёмки**: Название лекарства `O'Connor's "Special" Drops; rm -rf /` корректно отображается в уведомлении без выполнения shell-команд.

---

### FIX C-002: Потеря последующих доз дня в `DoseEventMaterializer.RematerializeFuture`

**Файл**: `src/Med.Domain/Scheduling/DoseEventMaterializer.cs` (строки ~L105–L113)

**Проблема**: Фильтрация `!takenLocalDates.Contains(e.LocalDate)` удаляет ВСЕ дозы на дату, если хотя бы одна доза в этот день принята. При 3-разовом приёме (08:00, 14:00, 20:00) после утреннего приёма дневная и вечерняя дозы безвозвратно теряются.

**Что сделать**:

1. **Удалить** блок вычисления `takenLocalDates`:
   ```csharp
   // УДАЛИТЬ ЭТИ СТРОКИ:
   var takenLocalDates = existingEvents
       .Where(e => e.Status == DoseEventStatus.Taken)
       .Select(e => e.LocalDate)
       .ToHashSet();
   ```

2. **Удалить** фильтрацию по `takenLocalDates` из генерации `freshFutureEvents`:
   ```csharp
   // БЫЛО:
   var freshFutureEvents = MaterializeRange(...)
       .Where(e => !takenLocalDates.Contains(e.LocalDate));
   
   // СТАЛО:
   var freshFutureEvents = MaterializeRange(...);
   ```

3. **Оставить** существующую дедупликацию по `DedupeKey` (`$"{scheduleId}:{scheduledAtUtc:O}"`) — она корректно предотвращает дубли без потери доз.

4. **Написать тест** в `tests/Med.Domain.Tests/Scheduling/DoseEventMaterializerTests.cs`:
   ```csharp
   [Fact]
   public void RematerializeFuture_WithMultipleDailyDoses_PreservesRemainingDoses()
   {
       // Arrange: 3 дозы в день (08:00, 14:00, 20:00), утренняя принята
       // Act: RematerializeFuture
       // Assert: дневная (14:00) и вечерняя (20:00) дозы сохранены
   }
   ```

**Критерий приёмки**: При 3-разовом приёме и принятой утренней дозе вызов `RematerializeFuture` сохраняет дневную и вечернюю дозы.

---

### FIX C-003: Утечка секретов в `.env`

**Файл**: `.env`, `.gitignore`

**Проблема**: Файл `.env` с боевыми `DISCORD_BOT_TOKEN`, `SUPABASE_SERVICE_ROLE_KEY` отслеживается Git и попал в историю коммитов.

**Что сделать**:

1. **Добавить** `.env` в `.gitignore` (если ещё не добавлен):
   ```gitignore
   # Secrets
   .env
   .env.local
   .env.production
   ```

2. **Убрать `.env` из отслеживания** Git (без удаления файла):
   ```bash
   git rm --cached .env
   ```

3. **Проверить** что `.env.example` НЕ содержит реальных значений — только плейсхолдеры:
   ```env
   DISCORD_BOT_TOKEN=your_token_here
   SUPABASE_SERVICE_ROLE_KEY=your_key_here
   ```

4. **Сообщить пользователю** (в комментарии к коммиту): необходимо ротировать `DISCORD_BOT_TOKEN` в Discord Developer Portal и `SUPABASE_SERVICE_ROLE_KEY` в Supabase Dashboard, а также выполнить `git filter-repo` для очистки истории.

**Критерий приёмки**: `git ls-files .env` возвращает пустой результат. `.gitignore` содержит `.env`.

---

### FIX C-004: Потеря уведомлений при закрытии приложения (Android)

**Файлы**:
- `src/Med.Android/AndroidNotificationService.cs` (строки ~L95–L125)
- `src/Med.Android/MainActivity.cs`
- `src/Med.Android/Properties/AndroidManifest.xml`

**Проблема**: Уведомления планируются через in-memory `Task.Delay`. При выгрузке Android-процесса (Doze, закрытие пользователем) все `Task.Delay` уничтожаются → пользователь не получает напоминания.

**Что сделать**:

1. **Создать** `src/Med.Android/NotificationReceiver.cs`:
   ```csharp
   [BroadcastReceiver(Enabled = true, Exported = false)]
   public class NotificationReceiver : BroadcastReceiver
   {
       public override void OnReceive(Context? context, Intent? intent)
       {
           if (context is null || intent is null) return;
           var id = intent.GetIntExtra("notification_id", 0);
           var title = intent.GetStringExtra("title") ?? "";
           var body = intent.GetStringExtra("body") ?? "";
           // Показать уведомление через NotificationManager
       }
   }
   ```

2. **Переписать** `ScheduleAsync` в `AndroidNotificationService.cs`:
   - Заменить `Task.Delay` + `Task.Run` на `AlarmManager.SetExactAndAllowWhileIdle()`:
   ```csharp
   public Task ScheduleAsync(string id, string title, string body, DateTimeOffset scheduledAt)
   {
       var alarmManager = (AlarmManager)_context.GetSystemService(Context.AlarmService)!;
       var intent = new Intent(_context, typeof(NotificationReceiver));
       intent.PutExtra("notification_id", id.GetHashCode());
       intent.PutExtra("title", title);
       intent.PutExtra("body", body);
       
       var pendingIntent = PendingIntent.GetBroadcast(
           _context, id.GetHashCode(), intent,
           PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);
       
       var triggerMs = scheduledAt.ToUnixTimeMilliseconds();
       alarmManager.SetExactAndAllowWhileIdle(AlarmType.RtcWakeup, triggerMs, pendingIntent);
       return Task.CompletedTask;
   }
   ```

3. **Переписать** `CancelAsync` — отменять через `AlarmManager.Cancel(pendingIntent)`.

4. **Добавить** в `AndroidManifest.xml` разрешения:
   ```xml
   <uses-permission android:name="android.permission.SCHEDULE_EXACT_ALARM" />
   <uses-permission android:name="android.permission.USE_EXACT_ALARM" />
   ```

5. **Добавить runtime-запрос** `POST_NOTIFICATIONS` в `MainActivity.cs` (решает также H-006):
   ```csharp
   protected override void OnCreate(Bundle? savedInstanceState)
   {
       base.OnCreate(savedInstanceState);
       if (OperatingSystem.IsAndroidVersionAtLeast(33))
       {
           if (CheckSelfPermission(Android.Manifest.Permission.PostNotifications) 
               != Permission.Granted)
           {
               RequestPermissions(
                   new[] { Android.Manifest.Permission.PostNotifications }, 1001);
           }
       }
   }
   ```

**Критерий приёмки**: Уведомление, запланированное на +5 минут, срабатывает после закрытия приложения и при Doze mode.

---

### FIX C-005: Path Traversal в `SupabaseFileStorage` / `Document.BuildStoragePath`

**Файлы**:
- `src/Med.Domain/Entities/Document.cs` (строки ~L50–L54)
- `src/Med.Infrastructure/Supabase/Storage/SupabaseFileStorage.cs` (~L32)

**Проблема**: `BuildStoragePath` формирует путь через `$"{userId}/{category}/{fileName}"` без санитизации. Имя файла `../../avatars/target.png` позволяет перезаписать чужие файлы.

**Что сделать**:

1. **Модифицировать** `Document.BuildStoragePath` в `Document.cs`:
   ```csharp
   public static string BuildStoragePath(Guid userId, string category, string fileName)
   {
       var sanitized = Path.GetFileName(fileName.Trim());
       
       if (string.IsNullOrWhiteSpace(sanitized))
           throw new ArgumentException("File name cannot be empty.", nameof(fileName));
       
       if (sanitized.Contains("..") || sanitized.Contains('/') || sanitized.Contains('\\'))
           throw new ArgumentException(
               $"File name contains invalid characters: '{sanitized}'", nameof(fileName));
       
       return $"{userId}/{category}/{sanitized}";
   }
   ```

2. **Написать тесты** в `tests/Med.Domain.Tests/Entities/DocumentTests.cs`:
   ```csharp
   [Theory]
   [InlineData("../../etc/passwd")]
   [InlineData("../avatars/target.png")]
   [InlineData("..\\..\\secret.pdf")]
   public void BuildStoragePath_WithTraversalAttempt_Throws(string maliciousName)
   {
       Assert.Throws<ArgumentException>(
           () => Document.BuildStoragePath(Guid.NewGuid(), "docs", maliciousName));
   }
   
   [Fact]
   public void BuildStoragePath_WithValidName_ReturnsCorrectPath()
   {
       var path = Document.BuildStoragePath(
           Guid.Parse("..."), "docs", "report.pdf");
       Assert.Equal("...guid.../docs/report.pdf", path);
   }
   ```

**Критерий приёмки**: `BuildStoragePath(id, "docs", "../../etc/passwd")` выбрасывает `ArgumentException`.

---

**После Фазы 1**: Выполнить `dotnet build MedTracker.slnx` и `dotnet test MedTracker.slnx`. Все тесты должны пройти. Закоммитить:
```bash
git -c user.name="Mysechka" -c user.email="egorkostin71@gmail.com" commit -m "fix(security): resolve 5 CRITICAL vulnerabilities — RCE, data loss, path traversal, secret leak, notification loss"
```

---

## Фаза 2: 🟠 HIGH — Бизнес-логика и UX

---

### FIX H-001: Исчезающие карточки и сломанный прогресс-бар в `TodayViewModel`

**Файлы**:
- `src/Med.Presentation/Today/TodayViewModel.cs` (~L148–L152, L189–L193, L304–L315, L350–L387)
- `src/Med.Presentation/Today/DoseRowViewModel.cs`
- `src/Med.Ui/Views/Screens/TodayView.axaml`

**Проблема**: Принятые/пропущенные дозы физически удаляются из `Items` через `Task.Delay(3000)` / `Task.Delay(30000)`. Из-за уменьшения `Items.Count` прогресс-бар показывает абсурдные данные: «0 из 0».

**Что сделать**:

1. **Удалить** из `TodayViewModel`:
   - Метод `ScheduleRowRemovalAsync` (или аналогичный метод отложенного удаления)
   - Все `Task.Delay(3000); Items.Remove(item);` и `Task.Delay(30000); Items.Remove(item);`
   - Связанные `CancellationTokenSource` для удаления

2. **Добавить** в `DoseRowViewModel`:
   ```csharp
   [ObservableProperty]
   private bool _isCompleted;  // true когда доза принята или пропущена
   ```

3. **Изменить** логику подтверждения/пропуска — вместо удаления из коллекции:
   ```csharp
   // БЫЛО:
   await Task.Delay(3000);
   Items.Remove(item);
   
   // СТАЛО:
   item.IsCompleted = true;
   UpdateProgress();
   ```

4. **Зафиксировать** общее число доз за день для корректного прогресса:
   ```csharp
   private int _totalDosesToday;
   
   private void UpdateProgress()
   {
       int taken = Items.Count(x => x.IsTaken);
       ProgressText = $"Принято {taken} из {_totalDosesToday}";
       ProgressValue = _totalDosesToday > 0 ? (double)taken / _totalDosesToday : 0;
   }
   ```
   `_totalDosesToday` устанавливается в `LoadItemsAsync` при загрузке дня: `_totalDosesToday = Items.Count;`

5. **Добавить стиль** в `TodayView.axaml` для завершённых карточек:
   ```xml
   <Style Selector="Border.dose-card.completed">
       <Setter Property="Opacity" Value="0.5"/>
   </Style>
   ```
   И привязать класс к `IsCompleted` на карточке.

**Критерий приёмки**: При 3 дозах и 2 принятых → прогресс-бар показывает «Принято 2 из 3». Все 3 карточки видны, принятые отображаются приглушённо.

---

### FIX H-002: Навигация на CoursesViewModel и нерабочие кнопки

**Файлы**:
- `src/Med.Presentation/Shell/ShellViewModel.cs` (~L30–L80)
- `src/Med.Presentation/Shell/ShellNav.cs`
- `src/Med.Presentation/MedicalCard/MedicalCardViewModel.cs`
- `src/Med.Presentation/Medications/MedicationCardViewModel.cs`
- `src/Med.Ui/Views/MainView.axaml`
- `src/Med.Ui/Views/Screens/MedicalCardView.axaml` (~L34–L39)
- `src/Med.Ui/Views/Screens/MedicationsView.axaml` (~L100)

**Проблема**: `CoursesViewModel` зарегистрирован в DI, но нет команды навигации. Кнопка «На главную» в `MedicalCardView` и кнопка ≡ в `MedicationsView` не привязаны к командам.

**Что сделать**:

1. **Добавить** `Courses` в enum `ShellNav.cs` (если отсутствует):
   ```csharp
   public enum ShellNav { Today, Medications, Courses, MedicalCard, Settings, Account, Auth }
   ```

2. **Добавить** в `ShellViewModel.cs`:
   ```csharp
   [ObservableProperty] private bool _isCoursesSelected;
   
   [RelayCommand]
   private void GoCourses() => NavigateTo(ShellNav.Courses);
   ```
   И в switch `NavigateTo`:
   ```csharp
   ShellNav.Courses => _courses,
   ```

3. **Добавить кнопку** «Курсы» в `MainView.axaml` в боковое меню и мобильную навигацию.

4. **Добавить команду** `GoBackCommand` в `MedicalCardViewModel.cs`:
   ```csharp
   [RelayCommand]
   private void GoBack()
       => WeakReferenceMessenger.Default.Send(
           new NavigateToSectionMessage(ShellNav.Today));
   ```

5. **Привязать** кнопку «На главную» в `MedicalCardView.axaml`:
   ```xml
   <Button Command="{Binding GoBackCommand}" Content="На главную"/>
   ```

6. **Привязать кнопку ≡** в `MedicationsView.axaml` к `MenuFlyout` с действиями «Редактировать», «Удалить» (добавить `DeleteCardCommand` в `MedicationCardViewModel`).

**Критерий приёмки**: Пользователь может перейти в раздел «Курсы» из навигации, вернуться из медкарты на главную, и открыть меню карточки лекарства.

---

### FIX H-004: Захардкоженный 14-дневный курс

**Файлы**:
- `src/Med.Presentation/Medications/MedicationsViewModel.cs` (~L352–L362)
- `src/Med.Domain/Entities/Course.cs`
- `src/Med.Ui/Views/Screens/MedicationsView.axaml`

**Проблема**: `startsOn.AddDays(13), durationDays: 14` — все новые лекарства получают курс 14 дней. Хронические препараты (инсулин, гипотензивные) перестают генерировать дозы через 2 недели.

**Что сделать**:

1. **Добавить свойства** в `MedicationsViewModel`:
   ```csharp
   [ObservableProperty] private bool _isChronicCourse = true;  // по умолчанию бессрочный
   [ObservableProperty] private int _courseDurationDays = 14;
   ```

2. **Изменить** `CreateInitialCourseAndSchedulesAsync`:
   ```csharp
   // БЫЛО:
   endsOn: startsOn.AddDays(13),
   durationDays: 14
   
   // СТАЛО:
   endsOn: IsChronicCourse ? null : startsOn.AddDays(CourseDurationDays - 1),
   durationDays: IsChronicCourse ? null : CourseDurationDays
   ```

3. **Ослабить валидацию** в `Course.Create` (если есть проверка на обязательность `endsOn` или `durationDays`): разрешить оба `null` для бессрочного курса. `EffectiveEndsOn` уже возвращает `DateOnly.MaxValue` в этом случае — проверить что не сломано.

4. **Добавить UI** в `MedicationsView.axaml`:
   ```xml
   <CheckBox Content="Бессрочный приём" IsChecked="{Binding IsChronicCourse}"/>
   <NumericUpDown Value="{Binding CourseDurationDays}" Minimum="1" Maximum="365"
                  IsVisible="{Binding !IsChronicCourse}"/>
   ```

**Критерий приёмки**: При `IsChronicCourse = true` курс создаётся с `EndsOn = null`, `DurationDays = null`. `EffectiveEndsOn` возвращает `DateOnly.MaxValue`.

---

### FIX H-005: Синтетические Email `@medtracker.local`

**Файлы**:
- `src/Med.Presentation/Shell/AuthViewModel.cs` (~L114–L128)
- `src/Med.Ui/Views/Screens/AuthView.axaml`
- `tests/Med.Presentation.Tests/Shell/AuthViewModelTests.cs`

**Проблема**: `SynthesizeEmail` генерирует фиктивные email `user@medtracker.local`, что делает невозможным восстановление пароля и подтверждение аккаунта.

**Что сделать**:

1. **Удалить** метод `SynthesizeEmail` полностью.

2. **Добавить** обязательное поле `Email` с валидацией:
   ```csharp
   [ObservableProperty] private string _email = "";
   
   private bool CanSignUp => !string.IsNullOrWhiteSpace(Email) 
       && Email.Contains('@') && Email.Contains('.')
       && !string.IsNullOrWhiteSpace(Password)
       && !string.IsNullOrWhiteSpace(Username);
   ```

3. **Добавить поле** Email в `AuthView.axaml`:
   ```xml
   <TextBox Watermark="Email (обязательно)" Text="{Binding Email}"/>
   ```

4. **Обновить тесты** в `AuthViewModelTests.cs`: убрать кейсы с `SynthesizeEmail`, добавить кейсы валидации Email.

**Критерий приёмки**: Регистрация без реального email невозможна. `SynthesizeEmail` удалён. Тесты проходят.

---

### FIX H-006: Разрешение POST_NOTIFICATIONS на Android 13+

**Решается в рамках FIX C-004** (пункт 5). Если C-004 уже выполнен, проверить что runtime-запрос `POST_NOTIFICATIONS` присутствует в `MainActivity.cs`.

---

**После Фазы 2**: `dotnet build && dotnet test`. Закоммитить:
```bash
git -c user.name="Mysechka" -c user.email="egorkostin71@gmail.com" commit -m "fix(core): resolve 5 HIGH issues — progress bar, navigation, course duration, synthetic emails, Android permissions"
```

---

## Фаза 3: 🟠 HIGH — Realtime и утечки памяти

---

### FIX H-003: Двойная обработка Realtime + утечка в DiagnosticsViewModel

**Файлы**:
- `src/Med.Presentation/Today/TodayViewModel.cs` (~L76, L411)
- `src/Med.Presentation/Diagnostics/DiagnosticsViewModel.cs` (~L25–L29)
- `src/Med.Infrastructure/Supabase/Realtime/SupabaseEntityRealtimeSync.cs`
- `src/Med.Infrastructure/Supabase/Realtime/SupabaseDoseEventRealtime.cs`

**Проблема**: 
- `TodayViewModel` одновременно слушает `_realtime.Changed` И `DoseEventStatusChangedMessage` от `IMessenger` → двойной `ReloadAsync()`
- `DiagnosticsViewModel` подписан на `_realtime.Changed` анонимным методом без `IDisposable` → утечка памяти
- `SupabaseDoseEventRealtime` и `SupabaseEntityRealtimeSync` дублируют подписки на `dose_events`

**Что сделать**:

1. **TodayViewModel**: Удалить прямую подписку `_realtime.Changed += ...`. Оставить только обработку через `WeakReferenceMessenger`:
   ```csharp
   // УДАЛИТЬ:
   _realtime.Changed += OnRealtimeChanged;
   
   // ОСТАВИТЬ ТОЛЬКО:
   Messenger.Register<DoseEventStatusChangedMessage>(this, (_, msg) => ...);
   ```

2. **DiagnosticsViewModel**: Реализовать `IDisposable`:
   ```csharp
   public class DiagnosticsViewModel : ViewModelBase, IDisposable
   {
       private readonly IEntityRealtimeSync _realtime;
       
       public DiagnosticsViewModel(IEntityRealtimeSync realtime)
       {
           _realtime = realtime;
           _realtime.Changed += OnRealtimeChanged;
       }
       
       private void OnRealtimeChanged(object? sender, EventArgs e) { ... }
       
       public void Dispose()
       {
           _realtime.Changed -= OnRealtimeChanged;
       }
   }
   ```

3. **SupabaseDoseEventRealtime**: Преобразовать в лёгкий фасад над `IEntityRealtimeSync`, убрав создание отдельного WebSocket-канала. Делегировать события из общего `IEntityRealtimeSync`.

4. **SupabaseEntityRealtimeSync**: 
   - Проверить что фильтр `user_id=eq.{userId}` есть для **ВСЕХ** таблиц: `dose_events`, `medications`, `courses`, **`schedules`** (для `schedules` фильтр был пропущен — добавить!)
   - Добавить reconnect с экспоненциальным backoff (1→2→4→8→16→30 сек, с jitter):
   ```csharp
   private async Task ScheduleReconnect()
   {
       var delay = Math.Min(30, Math.Pow(2, _retryCount)) + Random.Shared.NextDouble();
       _retryCount++;
       await Task.Delay(TimeSpan.FromSeconds(delay));
       await ConnectAsync();
   }
   ```
   - Реализовать `IDisposable` с отпиской и закрытием канала.

5. **Добавить `DiagnosticsViewModel`** в `ViewModelMemoryLeakTests` (если есть такой тест-класс):
   ```csharp
   [Fact]
   public void DiagnosticsViewModel_IsCollectedAfterDispose()
   {
       // Arrange → Dispose → GC.Collect → Assert WeakReference.IsAlive == false
   }
   ```

**Критерий приёмки**: Каждое Realtime-событие вызывает `ReloadAsync()` ровно 1 раз. `DiagnosticsViewModel` корректно собирается GC после `Dispose()`. Для таблицы `schedules` есть фильтр `user_id`.

---

**После Фазы 3**: `dotnet build && dotnet test`. Закоммитить:
```bash
git -c user.name="Mysechka" -c user.email="egorkostin71@gmail.com" commit -m "fix(realtime): eliminate double events, memory leaks, cross-tenant leakage, add reconnect"
```

---

## Фаза 4: 🟡 MEDIUM — Стабильность и устойчивость

---

### FIX M-001: Утечка Bitmap в `PathToBitmapConverter`

**Файл**: `src/Med.Ui/Controls/PathToBitmapConverter.cs` (~L27)

**Что сделать**: Добавить потокобезопасный кэш с `WeakReference`:
```csharp
private static readonly ConcurrentDictionary<string, WeakReference<Bitmap>> _cache = new();

public object? Convert(object? value, ...)
{
    if (value is not string path || !File.Exists(path)) return null;
    
    if (_cache.TryGetValue(path, out var weakRef) && weakRef.TryGetTarget(out var cached))
        return cached;
    
    var bitmap = new Bitmap(path);
    _cache[path] = new WeakReference<Bitmap>(bitmap);
    return bitmap;
}
```

---

### FIX M-002: Регистрозависимый `Enum.Parse`

**Файлы**:
- `src/Med.Infrastructure/Mapping/EntityMappers.cs` (~L383)
- `src/Med.Infrastructure/Supabase/JsonRpcParser.cs` (~L99)

**Что сделать**: Заменить все вызовы:
```csharp
// БЫЛО:
Enum.Parse<TEnum>(value)

// СТАЛО:
Enum.Parse<TEnum>(value, ignoreCase: true)
```

В `JsonRpcParser`: заменить на безопасный `TryParse`:
```csharp
if (!Enum.TryParse<TEnum>(text, ignoreCase: true, out var result))
    throw new FormatException($"Unknown enum value '{text}' for {typeof(TEnum).Name}");
```

---

### FIX M-003: `DateTimeOffset.UtcNow` → `ISystemClock`

**Файлы**:
- `src/Med.Infrastructure/Supabase/Services/DoseEventMaterializerService.cs` (~L48)
- `src/Med.Infrastructure/Supabase/Services/DoseTransitionService.cs` (~L42)

**Что сделать**:

1. Убедиться что `ISystemClock` (или `IClock`) существует в `Med.Application/Abstractions/`. Если нет — создать:
   ```csharp
   public interface ISystemClock
   {
       DateTimeOffset UtcNow { get; }
   }
   ```
   И реализацию в `Med.Infrastructure`:
   ```csharp
   public class SystemClock : ISystemClock
   {
       public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
   }
   ```

2. Внедрить через конструктор в оба сервиса:
   ```csharp
   private readonly ISystemClock _clock;
   
   // Заменить:
   var now = DateTimeOffset.UtcNow;
   // На:
   var now = _clock.UtcNow;
   ```

3. Зарегистрировать `SystemClock` в DI как Singleton.

---

### FIX M-004: Realtime Publication для таблиц

**Что сделать**: Создать миграцию `supabase/migrations/20261001000000_realtime_add_tables.sql`:
```sql
ALTER PUBLICATION supabase_realtime ADD TABLE public.medications, public.courses, public.schedules;
```

---

### FIX M-005: Обход инвариантов record через `with`

**Файлы**:
- `src/Med.Domain/Entities/Medication.cs`
- `src/Med.Domain/Entities/Inventory.cs`
- `src/Med.Domain/Inventory/InventoryRules.cs` (~L67)

**Что сделать**:

1. Перевести критичные свойства с позиционных параметров на `init` с валидацией:
   ```csharp
   public record Medication
   {
       public required string Name
       {
           get;
           init
           {
               ArgumentException.ThrowIfNullOrWhiteSpace(value, nameof(Name));
               field = value;
           }
       }
       // аналогично для Dosage, Unit...
   }
   ```

2. В `Inventory`: добавить проверку `QuantityOnHand >= 0` в `init`.

3. В `InventoryRules.cs`: перед списанием проверить остаток:
   ```csharp
   decimal newQty = inventory.QuantityOnHand - doseAmount;
   if (newQty < 0)
       return new InventoryMutationResult(TransitionOutcome.Rejected, ..., "Недостаточно остатка");
   ```

4. **Написать тесты** в `tests/Med.Domain.Tests/Entities/MedicationTests.cs`:
   ```csharp
   [Fact]
   public void Medication_WithEmptyName_ThrowsArgumentException()
   {
       var med = Medication.Create(...);
       Assert.Throws<ArgumentException>(() => med with { Name = "" });
   }
   ```

---

**После Фазы 4**: `dotnet build && dotnet test`. Закоммитить:
```bash
git -c user.name="Mysechka" -c user.email="egorkostin71@gmail.com" commit -m "fix(stability): bitmap cache, case-insensitive enum, IClock, realtime tables, record invariants"
```

---

## Фаза 5: 🟢 LOW — Мелочи

---

### FIX L-001: Дублирование ContentControl в `MainView.axaml`

**Файл**: `src/Med.Ui/Views/MainView.axaml` (~L128, L143)

**Что сделать**: Добавить взаимоисключающие условия видимости:
```xml
<!-- Десктопный layout -->
<ContentControl Content="{Binding Current}" IsVisible="{Binding !#Root.IsCompact}"/>

<!-- Мобильный layout -->
<ContentControl Content="{Binding Current}" IsVisible="{Binding #Root.IsCompact}"/>
```

---

### FIX L-002: Устаревшая документация

Обновить `README.md` — убрать ссылки на удалённые DTO-классы из `Med.Application`. Актуализировать архитектурную схему.

---

### FIX L-003: Неиспользуемый xmlns в `CoursesView.axaml`

**Файл**: `src/Med.Ui/Views/Screens/CoursesView.axaml` (~L5)

**Что сделать**: Удалить неиспользуемую директиву:
```xml
<!-- УДАЛИТЬ: -->
xmlns:ccswe="using:CCSWE.Avalonia.Material"
```

---

**После Фазы 5**: `dotnet build && dotnet test`. Закоммитить:
```bash
git -c user.name="Mysechka" -c user.email="egorkostin71@gmail.com" commit -m "fix(cleanup): remove duplicate ContentControl, stale docs, unused xmlns"
```

---

## Фаза 6: 🧪 Тесты — закрытие пробелов

> [!IMPORTANT]
> Новые тесты пишутся на **xUnit v3 + FluentAssertions**. Моки — через тот фреймворк, который уже используется в проекте (NSubstitute или Moq).

### Новые тесты (если не были созданы в предыдущих фазах):

| Тест-файл | Что проверяет |
|---|---|
| `Med.Domain.Tests/Entities/DocumentTests.cs` | Path Traversal блокируется в `BuildStoragePath` |
| `Med.Domain.Tests/Scheduling/DoseEventMaterializerTests.cs` | Сохранность доз при 3-разовом приёме |
| `Med.Domain.Tests/Entities/MedicationTests.cs` | Обход инвариантов через `with` блокируется |
| `Med.Presentation.Tests/Diagnostics/DiagnosticsViewModelLeakTest.cs` | Утечка памяти после Dispose |
| `Med.Presentation.Tests/Shell/ShellNavigationTests.cs` | Навигация на CoursesViewModel работает |
| `Med.Presentation.Tests/Shell/AuthViewModelTests.cs` | Email обязателен, SynthesizeEmail удалён |
| `Med.Presentation.Tests/Today/TodayProgressTests.cs` | Прогресс-бар корректен при N принятых из M |

### Существующие тесты — проверить:

- `ViewModelMemoryLeakTests` включает `DiagnosticsViewModel`
- `SupabaseIntegrationTests` — оставить `[Fact(Skip)]` но добавить комментарий с объяснением
- `AuthViewModelTests` — обновить под новую логику без `SynthesizeEmail`

---

**После Фазы 6**: `dotnet test MedTracker.slnx`. Закоммитить:
```bash
git -c user.name="Mysechka" -c user.email="egorkostin71@gmail.com" commit -m "test: add coverage for path traversal, dose preservation, memory leaks, navigation, progress bar"
```

---

## Финальная верификация

```bash
# 1. Чистая сборка без warnings
dotnet build -warnaserror MedTracker.slnx

# 2. Прогон всех тестов
dotnet test MedTracker.slnx

# 3. Проверить что .env не отслеживается
git ls-files .env  # должно быть пусто

# 4. Проверить что нет TODO/FIXME от исправлений
grep -rn "TODO\|FIXME\|HACK" src/ --include="*.cs"
```

---

## Важные ограничения

> [!CAUTION]
> - **НЕ ломать** существующие 196+ тестов — они ВСЕ должны проходить после каждой фазы
> - **НЕ нарушать** Clean Architecture: Domain НЕ зависит от внешних пакетов, Presentation НЕ зависит от Infrastructure
> - **НЕ удалять** существующие комментарии и XML-документацию, не связанные с исправлением
> - **НЕ рефакторить** код, не связанный с конкретным дефектом
> - **Коммиты**: `git -c user.name="Mysechka" -c user.email="egorkostin71@gmail.com" commit -m "..."`
> - **Каждая фаза** завершается `dotnet build && dotnet test` перед коммитом
