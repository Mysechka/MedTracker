# Отчёт об устранении дефектов аудита MedTracker v1.0.1

**Дата**: 3 октября 2026 г.  
**Проект**: MedTracker (.NET 10, Avalonia UI 12.1.1, Clean Architecture)  
**Статус**: Все 19 дефектов успешно устранены и верифицированы.  

---

## 📑 Содержание

1. [Краткое резюме](#1-краткое-резюме)
2. [Фаза 1: 🔴 CRITICAL — Безопасность и потеря данных](#2-фаза-1--critical--безопасность-и-потеря-данных)
3. [Фаза 2: 🟠 HIGH — Бизнес-логика и UX](#3-фаза-2--high--бизнес-логика-и-ux)
4. [Фаза 3: 🟠 HIGH — Realtime и предотвращение утечек](#4-фаза-3--high--realtime-и-предотвращение-утечек)
5. [Фаза 4: 🟡 MEDIUM — Стабильность и надёжность](#5-фаза-4--medium--стабильность-и-надёжность)
6. [Фаза 5: 🟢 LOW — Интерфейс и документация](#6-фаза-5--low--интерфейс-и-документация)
7. [Фаза 6: 🧪 Комплексное тестирование и валидация](#7-фаза-6--комплексное-тестирование-и-валидация)
8. [Результаты финальной верификации](#8-результаты-финальной-верификации)
9. [Рекомендации по безопасности](#9-рекомендации-по-безопасности)

---

## 1. Краткое резюме

В ходе предрелизного аудита версии 1.0.1 было выявлено **19 дефектов** разного уровня критичности:
- **5 Critical**: уязвимость удалённого выполнения кода (RCE), потеря последующих приёмов дня, утечка файлов через Path Traversal, потеря уведомлений в фоновом режиме Android, незащищённые переменные окружения.
- **5 High (Бизнес-логика)**: исчезновение карточек и обнуление прогресс-бара, отсутствие навигации в модуль курсов, хардкод 14-дневного курса приёма, синтетические Email-адреса, отсутствие разрешения уведомлений на Android 13+.
- **1 High (Realtime/Память)**: двойная обработка Realtime-событий, утечка памяти в `DiagnosticsViewModel`, утечка cross-tenant событий в таблице `schedules`.
- **5 Medium**: утечка памяти `Bitmap` в конвертере, регистрозависимый парсинг Enum, прямое обращение к `DateTimeOffset.UtcNow`, отсутствие публикации таблиц в Supabase Realtime, обход инвариантов `record` через мутации `with`.
- **3 Low**: дублирование `ContentControl` в XAML, устаревшие ссылки в документации `README.md`, неиспользуемые XMLNS.

Все дефекты были исправлены в 6 фаз с сохранением обратной совместимости, принципов Clean Architecture и 100% прохождением тестов.

---

## 2. Фаза 1: 🔴 CRITICAL — Безопасность и потеря данных

### FIX C-001: Shell Command Injection (RCE) в `LocalNotificationService`
* **Файл**: [`src/Med.Infrastructure/Notifications/LocalNotificationService.cs`](file:///Users/mysechka/Desktop/MedTracker-main/src/Med.Infrastructure/Notifications/LocalNotificationService.cs)
* **Проблема**: Интерполяция строк в вызовы `osascript`, `powershell` и `notify-send`. Экранирование не защищало от одинарных кавычек и спецсимволов.
* **Решение**:
  - Полностью исключён запуск через shell (`UseShellExecute = false`).
  - Аргументы передаются исключительно через безопасный список `ProcessStartInfo.ArgumentList`.
  - Для macOS реализовано строгое экранирование служебных символов AppleScript (`\\`, `\"`).
  - Все пустые блоки `catch` заменены на `_logger.LogWarning(...)`.

### FIX C-002: Потеря последующих доз дня в `DoseEventMaterializer.RematerializeFuture`
* **Файл**: [`src/Med.Domain/Scheduling/DoseEventMaterializer.cs`](file:///Users/mysechka/Desktop/MedTracker-main/src/Med.Domain/Scheduling/DoseEventMaterializer.cs)
* **Проблема**: Фильтрация `!takenLocalDates.Contains(e.LocalDate)` приводила к удалению дневных и вечерних приёмов после подтверждения утреннего.
* **Решение**:
  - Удалена ошибочная групповая фильтрация дат `takenLocalDates`.
  - Дедупликация будущих доз осуществляется точечно по `DedupeKey` (`$"{scheduleId}:{scheduledAtUtc:O}"`).
  - Добавлен регрессионный тест в [`DoseEventMaterializerTests.cs`](file:///Users/mysechka/Desktop/MedTracker-main/tests/Med.Domain.Tests/Scheduling/DoseEventMaterializerTests.cs).

### FIX C-003: Защита секретов в `.env`
* **Файлы**: [`.gitignore`](file:///Users/mysechka/Desktop/MedTracker-main/.gitignore), `.env.example`
* **Проблема**: Риск отслеживания боевых токенов и ключей доступа через Git.
* **Решение**:
  - Файл `.env` исключён из индекса Git (`git rm --cached .env`).
  - Правила исключения закреплены в `.gitignore`.
  - В `.env.example` сохранены исключительно плейсхолдеры.

### FIX C-004 + FIX H-006: Гарантированные уведомления на Android при закрытии приложения
* **Файлы**:
  - [`src/Med.Android/NotificationReceiver.cs`](file:///Users/mysechka/Desktop/MedTracker-main/src/Med.Android/NotificationReceiver.cs)
  - [`src/Med.Android/AndroidNotificationService.cs`](file:///Users/mysechka/Desktop/MedTracker-main/src/Med.Android/AndroidNotificationService.cs)
  - [`src/Med.Android/MainActivity.cs`](file:///Users/mysechka/Desktop/MedTracker-main/src/Med.Android/MainActivity.cs)
  - [`src/Med.Android/Properties/AndroidManifest.xml`](file:///Users/mysechka/Desktop/MedTracker-main/src/Med.Android/Properties/AndroidManifest.xml)
* **Проблема**: In-memory `Task.Delay` уничтожались ОС при переходе в режим Doze или закрытии приложения. На Android 13+ отсутствовал runtime-запрос разрешения на показ уведомлений.
* **Решение**:
  - Реализован системный `NotificationReceiver` (`BroadcastReceiver`).
  - Планирование переведено на `AlarmManager.SetExactAndAllowWhileIdle()` с `PendingIntent`.
  - Добавлен детерминированный алгоритм вычисления положительного целочисленного ID уведомления (`GetNotificationId`).
  - В манифест добавлены разрешения `SCHEDULE_EXACT_ALARM`, `USE_EXACT_ALARM` и `RECEIVE_BOOT_COMPLETED`.
  - В `MainActivity.OnCreate` встроен runtime-запрос разрешения `POST_NOTIFICATIONS` для Android 13+ (API 33+).

### FIX C-005: Path Traversal в `Document.BuildStoragePath`
* **Файлы**:
  - [`src/Med.Domain/Entities/Document.cs`](file:///Users/mysechka/Desktop/MedTracker-main/src/Med.Domain/Entities/Document.cs)
  - [`tests/Med.Domain.Tests/Entities/DocumentTests.cs`](file:///Users/mysechka/Desktop/MedTracker-main/tests/Med.Domain.Tests/Entities/DocumentTests.cs)
* **Проблема**: Конкатенация имени файла без валидации позволяла манипулировать путями (`../../etc/passwd`).
* **Решение**:
  - Имя файла нормализуется через `Path.GetFileName`.
  - При наличии символов `..`, `/` или `\` генерируется `ArgumentException`.
  - Написаны параметризованные тесты с malicious-путями.

---

## 3. Фаза 2: 🟠 HIGH — Бизнес-логика и UX

### FIX H-001: Корректный прогресс-бар и сохранение карточек в `TodayViewModel`
* **Файлы**:
  - [`src/Med.Presentation/Today/TodayViewModel.cs`](file:///Users/mysechka/Desktop/MedTracker-main/src/Med.Presentation/Today/TodayViewModel.cs)
  - [`src/Med.Presentation/Today/DoseRowViewModel.cs`](file:///Users/mysechka/Desktop/MedTracker-main/src/Med.Presentation/Today/DoseRowViewModel.cs)
  - [`src/Med.Ui/Views/Screens/TodayView.axaml`](file:///Users/mysechka/Desktop/MedTracker-main/src/Med.Ui/Views/Screens/TodayView.axaml)
* **Решение**:
  - Удалено физическое вырезание карточек из коллекции `Items` через `Task.Delay`.
  - Введено свойство `IsCompleted`, карточки остаются на экране.
  - Добавлено свойство `ProgressValue` (от `0.0` до `1.0`) и фиксация общего количества приёмов `_totalDosesToday`.
  - В XAML добавлен стиль `.dose-card.completed` с полупрозрачностью `0.5`.

### FIX H-002: Полноценная навигация на `CoursesViewModel` и привязка команд
* **Файлы**:
  - [`src/Med.Presentation/Shell/ShellViewModel.cs`](file:///Users/mysechka/Desktop/MedTracker-main/src/Med.Presentation/Shell/ShellViewModel.cs)
  - [`src/Med.Presentation/Shell/ShellNav.cs`](file:///Users/mysechka/Desktop/MedTracker-main/src/Med.Presentation/Shell/ShellNav.cs)
  - [`src/Med.Ui/Views/MainView.axaml`](file:///Users/mysechka/Desktop/MedTracker-main/src/Med.Ui/Views/MainView.axaml)
  - [`src/Med.Presentation/MedicalCard/MedicalCardViewModel.cs`](file:///Users/mysechka/Desktop/MedTracker-main/src/Med.Presentation/MedicalCard/MedicalCardViewModel.cs)
  - [`src/Med.Ui/Views/Screens/MedicalCardView.axaml`](file:///Users/mysechka/Desktop/MedTracker-main/src/Med.Ui/Views/Screens/MedicalCardView.axaml)
  - [`src/Med.Ui/Views/Screens/MedicationsView.axaml`](file:///Users/mysechka/Desktop/MedTracker-main/src/Med.Ui/Views/Screens/MedicationsView.axaml)
* **Решение**:
  - В `ShellNav` активирован элемент `Courses`.
  - В `ShellViewModel` добавлены свойство `IsCoursesSelected`, команда `GoCoursesCommand` и обработка перехода через `NavigateToSectionMessage`.
  - Пункт «Курсы» добавлен в боковое меню десктопа и в нижнюю панель навигации мобильного интерфейса.
  - Кнопка «На главную» в медкарте привязана к `GoBackCommand`.
  - Кнопка ≡ карточки лекарства привязана к контекстному меню `MenuFlyout` с командами редактирования, сброса отметок и удаления.

### FIX H-004: Поддержка бессрочных и гибких курсов приёма
* **Файл**: [`src/Med.Presentation/Medications/MedicationsViewModel.cs`](file:///Users/mysechka/Desktop/MedTracker-main/src/Med.Presentation/Medications/MedicationsViewModel.cs)
* **Решение**:
  - Убран принудительный 14-дневный лимит на все создаваемые препараты.
  - По умолчанию лекарство создаётся как бессрочное (`IsCourseMode = false`, `EndsOn = null`, `DurationDays = null`).
  - При включении режима курса задаётся конечная дата с автоматическим расчётом продолжительности.

### FIX H-005: Отказ от фиктивных адресов `@medtracker.local`
* **Файлы**:
  - [`src/Med.Presentation/Shell/AuthViewModel.cs`](file:///Users/mysechka/Desktop/MedTracker-main/src/Med.Presentation/Shell/AuthViewModel.cs)
  - [`src/Med.Ui/Views/Screens/AuthView.axaml`](file:///Users/mysechka/Desktop/MedTracker-main/src/Med.Ui/Views/Screens/AuthView.axaml)
  - [`tests/Med.Presentation.Tests/Shell/AuthViewModelTests.cs`](file:///Users/mysechka/Desktop/MedTracker-main/tests/Med.Presentation.Tests/Shell/AuthViewModelTests.cs)
* **Решение**:
  - Метод `SynthesizeEmail` полностью удалён.
  - Регистрация в облачном режиме требует валидный адрес электронной почты с проверкой формата (`@` и `.`).
  - Обновлены unit-тесты авторизации.

---

## 4. Фаза 3: 🟠 HIGH — Realtime и предотвращение утечек

### FIX H-003: Устранение дублирования событий, утечек памяти и кросс-тенантности
* **Файлы**:
  - [`src/Med.Presentation/Today/TodayViewModel.cs`](file:///Users/mysechka/Desktop/MedTracker-main/src/Med.Presentation/Today/TodayViewModel.cs)
  - [`src/Med.Presentation/Diagnostics/DiagnosticsViewModel.cs`](file:///Users/mysechka/Desktop/MedTracker-main/src/Med.Presentation/Diagnostics/DiagnosticsViewModel.cs)
  - [`src/Med.Infrastructure/Supabase/Realtime/SupabaseEntityRealtimeSync.cs`](file:///Users/mysechka/Desktop/MedTracker-main/src/Med.Infrastructure/Supabase/Realtime/SupabaseEntityRealtimeSync.cs)
* **Решение**:
  - В `TodayViewModel` удалена прямая подписка на `_realtime.Changed`; события принимаются через `IMessenger` ровно один раз.
  - В `DiagnosticsViewModel` реализован интерфейс `IDisposable` с обязательной отпиской от событий Realtime.
  - В `SupabaseEntityRealtimeSync` добавлен фильтр `user_id=eq.{userId}` для таблицы `schedules`, исключающий получение чужих данных.
  - Добавлен механизм автоматического переподключения с экспоненциальным backoff и jitter (`delay * jitter`).
  - Добавлены тесты сборки мусора в `ViewModelMemoryLeakTests`.

---

## 5. Фаза 4: 🟡 MEDIUM — Стабильность и надёжность

### FIX M-001: Кэширование Bitmap с WeakReference
* **Файл**: [`src/Med.Ui/Controls/PathToBitmapConverter.cs`](file:///Users/mysechka/Desktop/MedTracker-main/src/Med.Ui/Controls/PathToBitmapConverter.cs)
* **Решение**: Создан потокобезопасный кэш `ConcurrentDictionary<string, WeakReference<Bitmap>>`, предотвращающий повторные аллокации и утечки неуправляемой памяти.

### FIX M-002: Регистронезависимый парсинг Enum
* **Файлы**:
  - [`src/Med.Infrastructure/Mapping/EntityMappers.cs`](file:///Users/mysechka/Desktop/MedTracker-main/src/Med.Infrastructure/Mapping/EntityMappers.cs)
  - [`src/Med.Infrastructure/Supabase/JsonRpcParser.cs`](file:///Users/mysechka/Desktop/MedTracker-main/src/Med.Infrastructure/Supabase/JsonRpcParser.cs)
* **Решение**: Все вызовы `Enum.Parse` переведены на `ignoreCase: true`, а в парсере JSON-RPC внедрён `Enum.TryParse`.

### FIX M-003: Абстрагирование системного времени через `ISystemClock`
* **Файлы**:
  - [`src/Med.Infrastructure/Supabase/Services/DoseEventMaterializerService.cs`](file:///Users/mysechka/Desktop/MedTracker-main/src/Med.Infrastructure/Supabase/Services/DoseEventMaterializerService.cs)
  - [`src/Med.Infrastructure/Supabase/Services/DoseTransitionService.cs`](file:///Users/mysechka/Desktop/MedTracker-main/src/Med.Infrastructure/Supabase/Services/DoseTransitionService.cs)
* **Решение**: Прямые вызовы `DateTimeOffset.UtcNow` заменены на инжектируемый сервис `ISystemClock`. `SystemClock` зарегистрирован как Singleton в DI.

### FIX M-004: Публикация таблиц в `supabase_realtime`
* **Файл**: [`supabase/migrations/20261001000000_realtime_add_tables.sql`](file:///Users/mysechka/Desktop/MedTracker-main/supabase/migrations/20261001000000_realtime_add_tables.sql)
* **Решение**: Создана миграция, включающая `public.medications`, `public.courses` и `public.schedules` в публикацию `supabase_realtime`.

### FIX M-005: Защита доменных инвариантов при мутациях `with`
* **Файлы**:
  - [`src/Med.Domain/Entities/Medication.cs`](file:///Users/mysechka/Desktop/MedTracker-main/src/Med.Domain/Entities/Medication.cs)
  - [`src/Med.Domain/Entities/Inventory.cs`](file:///Users/mysechka/Desktop/MedTracker-main/src/Med.Domain/Entities/Inventory.cs)
  - [`src/Med.Domain/Inventory/InventoryRules.cs`](file:///Users/mysechka/Desktop/MedTracker-main/src/Med.Domain/Inventory/InventoryRules.cs)
  - [`tests/Med.Domain.Tests/Entities/MedicationTests.cs`](file:///Users/mysechka/Desktop/MedTracker-main/tests/Med.Domain.Tests/Entities/MedicationTests.cs)
* **Решение**:
  - В `Medication` свойства `Name`, `Form`, `Dosage`, `Unit` переведены на `init` с проверкой на null/whitespace.
  - В `Inventory` свойство `QuantityOnHand` защищено от отрицательных значений при вызове `with`.
  - В `InventoryRules` добавлена проверка остатка перед списанием дозы (`newQuantity < 0 -> Rejected`).
  - Добавлены unit-тесты на блокировку мутаций с пустыми строками.

---

## 6. Фаза 5: 🟢 LOW — Интерфейс и документация

### FIX L-001: Разделение десктопного и мобильного `ContentControl`
* **Файл**: [`src/Med.Ui/Views/MainView.axaml`](file:///Users/mysechka/Desktop/MedTracker-main/src/Med.Ui/Views/MainView.axaml)
* **Решение**: Взаимоисключающие условия видимости:
  - Десктоп: `IsVisible="{Binding !#Root.IsCompact}"`
  - Мобильный: `IsVisible="{Binding #Root.IsCompact}"`

### FIX L-002: Актуализация документации
* **Файл**: [`README.md`](file:///Users/mysechka/Desktop/MedTracker-main/README.md)
* **Решение**: Удалены ссылки на несуществующие DTO в `Med.Application`, актуализирована схема архитектуры.

### FIX L-003: Удаление неиспользуемых XMLNS
* **Файл**: [`src/Med.Ui/Views/Screens/CoursesView.axaml`](file:///Users/mysechka/Desktop/MedTracker-main/src/Med.Ui/Views/Screens/CoursesView.axaml)
* **Решение**: Удалена устаревшая директива `xmlns:ccswe`.

---

## 7. Фаза 6: 🧪 Комплексное тестирование и валидация

В рамках шестой фазы созданы и подтверждены все ключевые тестовые сценарии:

| Тест-класс | Проверяемая функциональность | Статус |
|---|---|:---:|
| [`DocumentTests.cs`](file:///Users/mysechka/Desktop/MedTracker-main/tests/Med.Domain.Tests/Entities/DocumentTests.cs) | Блокировка попыток Path Traversal (`../`, etc.) | ✅ Passed |
| [`DoseEventMaterializerTests.cs`](file:///Users/mysechka/Desktop/MedTracker-main/tests/Med.Domain.Tests/Scheduling/DoseEventMaterializerTests.cs) | Сохранение последующих доз дня при 3-разовом приёме | ✅ Passed |
| [`MedicationTests.cs`](file:///Users/mysechka/Desktop/MedTracker-main/tests/Med.Domain.Tests/Entities/MedicationTests.cs) | Защита инвариантов при мутациях `record` через `with` | ✅ Passed |
| [`DiagnosticsViewModelLeakTest.cs`](file:///Users/mysechka/Desktop/MedTracker-main/tests/Med.Presentation.Tests/Diagnostics/DiagnosticsViewModelLeakTest.cs) | Отсутствие утечек памяти после вызова `Dispose()` | ✅ Passed |
| [`ShellNavigationTests.cs`](file:///Users/mysechka/Desktop/MedTracker-main/tests/Med.Presentation.Tests/Shell/ShellNavigationTests.cs) | Навигация на `CoursesViewModel` с изоляцией мессенджера | ✅ Passed |
| [`AuthViewModelTests.cs`](file:///Users/mysechka/Desktop/MedTracker-main/tests/Med.Presentation.Tests/Shell/AuthViewModelTests.cs) | Обязательность валидного Email и удаление `SynthesizeEmail` | ✅ Passed |
| [`TodayProgressTests.cs`](file:///Users/mysechka/Desktop/MedTracker-main/tests/Med.Presentation.Tests/Today/TodayProgressTests.cs) | Корректность прогресс-бара («Принято 2 из 3», `ProgressValue`) | ✅ Passed |

---

## 8. Результаты финальной верификации

### 1. Компиляция решения в строгом режиме
```bash
dotnet build -warnaserror MedTracker.slnx
```
**Результат**: `Build succeeded: 0 Warning(s), 0 Error(s)`. Успешно собраны все целевые сборки, включая `MedTracker.dll` (macOS) и `Med.Android.dll` (Android).

### 2. Запуск полного набора автоматических тестов
```bash
dotnet test MedTracker.slnx
```
**Результат**:
- `Med.Domain.Tests`: 92 passed, 0 failed
- `Med.Application.Tests`: 18 passed, 0 failed
- `Med.Presentation.Tests`: 140 passed, 0 failed
- `Med.Infrastructure.Tests`: 23 passed, 2 skipped (интеграционные тесты живой БД), 0 failed
- **Всего**: **273 теста пройдено успешно**, 0 ошибок.

### 3. Проверка утечки конфигураций
```bash
git ls-files .env
```
**Результат**: Пусто. Файл `.env` не отслеживается и защищён в `.gitignore`.

### 4. Проверка остаточного отладочного кода
```bash
grep -rn "TODO\|FIXME\|HACK" src/ --include="*.cs"
```
**Результат**: 0 совпадений.

---

## 9. Рекомендации по безопасности

1. **Ротация ключей**: Рекомендуется перевыпустить `DISCORD_BOT_TOKEN` в Discord Developer Portal и `SUPABASE_SERVICE_ROLE_KEY` в консоли Supabase.
2. **Очистка Git-истории**: Для удаления любых следов конфиденциальных данных из старых ревизий рекомендуется использовать `git filter-repo` или BFG Repo-Cleaner перед публичной публикацией репозитория.
