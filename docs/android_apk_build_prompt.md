# 📱 MedTracker — Промт для сборки рабочего Android APK на основе обновлённого macOS-артефакта

> **Назначение**: Скопируй и отправь этот промт агенту (Antigravity / Claude / GPT). Агент изучит текущее состояние обновлённого macOS Desktop-артефакта, адаптирует все изменения для Android-платформы, соберёт рабочий APK, прогонит тесты и верифицирует результат.

---

## 📋 Системная роль и контекст

Ты — **Senior Cross-Platform Build Engineer** проекта **MedTracker**. Твоя задача — проанализировать текущее обновлённое состояние Desktop/macOS-артефакта (собранного `.app`/`.dmg` или рабочего дерева `src/Med.Desktop/`), выявить все изменения в shared-слоях (Domain, Application, Infrastructure, Presentation, Ui), убедиться что они корректно работают на Android, адаптировать платформенно-зависимый код и собрать рабочий `.apk` файл.

---

## 🏗 Описание проекта

**MedTracker** — персональное кроссплатформенное приложение для приёма лекарств и ведения медкарты с нативными уведомлениями ОС. Open-source, **НЕ в продакшене**.

### Стек технологий
- **.NET 10** (SDK `10.0.400`), **C# 14** (latest LangVersion)
- **Avalonia UI 12.1.1** — кроссплатформенный UI-фреймворк (macOS + Android)
- **CommunityToolkit.Mvvm 8.4.2** — MVVM (`ObservableProperty`, `RelayCommand`, `WeakReferenceMessenger`)
- **Supabase 1.6.0** — PostgreSQL, Auth, Storage, Realtime, Edge Functions (Deno)
- **Microsoft.Data.Sqlite 10.0.11** — локальная offline-first БД
- **SkiaSharp 3.119.4** — графика (кадрирование аватаров)
- **xUnit v3 3.2.2 + FluentAssertions 7.2.2 + BenchmarkDotNet 0.14.0** — тесты
- **Clean Architecture** (5 слоёв + 2 head-проекта)

### Архитектура решения (Clean Architecture)

```
src/Med.Domain/          → Чистый C#, доменные сущности, правила, ноль зависимостей
src/Med.Application/     → Интерфейсы (IAuthService, INotificationService, репозитории), Use Cases, DTO
src/Med.Infrastructure/  → Supabase SDK, SQLite LocalStorage, нативные уведомления, маппинг
src/Med.Presentation/    → ViewModels (MVVM), навигация (ShellViewModel), мессенджер
src/Med.Ui/              → Avalonia Views (.axaml), контролы, конвертеры, темы, ViewLocator
src/Med.Desktop/         → Head-проект macOS/Windows/Linux (Program.cs, MacDockIcon.cs)
src/Med.Android/         → Head-проект Android (MainActivity, MedAndroidApplication, AlarmManager)
tests/                   → 5 тестовых проектов (270+ тестов)
```

**Правило зависимостей (строго внутрь):**
- `Med.Domain` → 0 внешних зависимостей
- `Med.Application` → зависит только от `Med.Domain`
- `Med.Infrastructure` → зависит от `Med.Application`
- `Med.Presentation` → зависит от `Med.Application` (НЕ от `Med.Infrastructure`)
- `Med.Ui` → зависит от `Med.Presentation`, `Avalonia`, `SkiaSharp`
- `Med.Desktop` / `Med.Android` → зависят от `Med.Infrastructure` + `Med.Ui`

### Навигация приложения (4 раздела, СТРОГО)

> [!CAUTION]
> В приложении ровно **4 основных раздела** навигации. Никогда **НЕ добавлять** отдельную кнопку/вкладку **«Курсы»**!

1. **«Сегодня»** (`Today`) — текущий статус приёмов на день
2. **«Лекарства»** (`Medications`) — каталог медикаментов с курсами и дозировками
3. **«Медкарта»** (`MedicalCard`) — анализы, документы, медфайлы
4. **«Настройки»** (`Settings`) — профиль, часовой пояс, диагностика

Курсы приёма настраиваются **внутри карточки лекарства**, а не как отдельный раздел.

---

## 🎯 ЗАДАНИЕ: Пошаговый план действий

### Фаза 0: Аудит текущего состояния и выявление изменений

1. **Проверь рабочее дерево на наличие незакоммиченных изменений:**
   ```bash
   git status -s
   git diff --stat HEAD
   ```

2. **Изучи обновлённый macOS артефакт:**
   - Если есть свежесобранный `artifacts/MedTracker.dmg` или `artifacts/macos/` — зафиксируй версию и дату.
   - Если изменения только в рабочем дереве — проанализируй `git diff` по каждому shared-слою:
     ```bash
     git diff HEAD -- src/Med.Domain/
     git diff HEAD -- src/Med.Application/
     git diff HEAD -- src/Med.Infrastructure/
     git diff HEAD -- src/Med.Presentation/
     git diff HEAD -- src/Med.Ui/
     ```
   - Составь список **всех изменённых файлов** в shared-слоях, которые влияют на Android.

3. **Проверь, что Desktop-версия собирается и тесты проходят:**
   ```bash
   dotnet build src/Med.Desktop/Med.Desktop.csproj -c Release
   dotnet test MedTracker.slnx -c Release --verbosity normal
   ```
   Если есть ошибки — исправь их **ДО начала работы** с Android.

---

### Фаза 1: Адаптация Android Head-проекта

> [!IMPORTANT]
> Ключевой принцип: 95% кода приложения живёт в shared-слоях (`Domain`, `Application`, `Infrastructure`, `Presentation`, `Ui`). Android head-проект (`src/Med.Android/`) содержит только **платформенно-зависимый клей**. Если shared-слои корректно обновлены — Android в основном «наследует» изменения автоматически.

#### 1.1 Проверь и обнови `src/Med.Android/Med.Android.csproj`

**Текущая конфигурация:**
```xml
<TargetFramework>net10.0-android</TargetFramework>
<SupportedOSPlatformVersion>26</SupportedOSPlatformVersion>
<ApplicationId>com.mysechka.medtracker</ApplicationId>
<ApplicationVersion>2</ApplicationVersion>
<ApplicationDisplayVersion>1.0.1</ApplicationDisplayVersion>
<AndroidPackageFormat>apk</AndroidPackageFormat>
<PublishTrimmed>false</PublishTrimmed>
<RunAOTCompilation>false</RunAOTCompilation>
<AndroidLinkMode>None</AndroidLinkMode>
<EmbedAssembliesIntoApk>true</EmbedAssembliesIntoApk>
```

**Что проверить:**
- `ApplicationVersion` — если релиз обновился, **инкрементируй** `versionCode` (например, `2` → `3`).
- `ApplicationDisplayVersion` — синхронизируй с `Directory.Build.props` → `<Version>`.
- Зависимости от `Med.Infrastructure` и `Med.Ui` на месте.
- **Тримминг и AOT отключены** (Supabase и Avalonia используют рефлексию и ломаются при тримминге).

#### 1.2 Синхронизируй `AndroidManifest.xml`

**Файл:** `src/Med.Android/Properties/AndroidManifest.xml`

**Обязательные разрешения (проверить наличие):**
```xml
<uses-permission android:name="android.permission.INTERNET" />
<uses-permission android:name="android.permission.POST_NOTIFICATIONS" />
<uses-permission android:name="android.permission.SCHEDULE_EXACT_ALARM" />
<uses-permission android:name="android.permission.USE_EXACT_ALARM" />
<uses-permission android:name="android.permission.VIBRATE" />
<uses-permission android:name="android.permission.RECEIVE_BOOT_COMPLETED" />
<uses-permission android:name="android.permission.READ_MEDIA_IMAGES" />
<uses-permission android:name="android.permission.READ_EXTERNAL_STORAGE" android:maxSdkVersion="32" />
```

**Что проверить:**
- `android:versionCode` синхронизирован с `<ApplicationVersion>` в `.csproj`.
- `android:versionName` синхронизирован с `<ApplicationDisplayVersion>`.
- Если добавлялись новые фичи (например, камера, геолокация, Bluetooth) — добавь соответствующие permissions.

#### 1.3 Проверь `MedAndroidApplication.cs`

> [!IMPORTANT]
> В `MedAndroidApplication.cs` **обязательно** должен присутствовать метод `public override void OnCreate() { base.OnCreate(); }`. Его удаление вызывает `UnsatisfiedLinkError: n_onCreate` на Android.

**Критически важные элементы DI-композиции:**
```csharp
services.AddMedApplication();
services.AddMedInfrastructure(configuration);
services.AddSingleton<INotificationService>(sp =>
    new AndroidNotificationService(this, sp.GetService<ILogger<AndroidNotificationService>>()));
services.AddMedPresentation();
services.AddMedUi();  // СТРОГО ПОСЛЕ AddMedPresentation
```

**Что проверить:**
- Если в обновлённых shared-слоях добавились **новые DI-регистрации** (новые сервисы, репозитории, ViewModels) — убедись, что они подхватываются через `AddMedApplication()`, `AddMedInfrastructure()`, `AddMedPresentation()`, `AddMedUi()`.
- Если добавился **новый платформенно-зависимый сервис** (например, новый `IFilePickerService`, `ICameraService`) — зарегистрируй его Android-реализацию здесь.
- Порядок регистрации: `AddMedUi()` строго **после** `AddMedPresentation()` (перекрывает `IUiDispatcher`).

#### 1.4 Проверь `MainActivity.cs`

**Текущие функции:**
- Лаунчер приложения (`MainLauncher = true`)
- `WindowSoftInputMode = SoftInput.AdjustResize` — автомасштабирование при клавиатуре
- Runtime-запрос `POST_NOTIFICATIONS` для Android 13+ (API 33)
- Обработка `ConfigChanges`: Orientation, ScreenSize, UiMode

**Что проверить:**
- Если добавились новые runtime permissions — добавь их запрос в `OnCreate`.

#### 1.5 Проверь `AndroidNotificationService.cs` и `NotificationReceiver.cs`

**Текущая реализация:**
- Уведомления через `AlarmManager.SetExactAndAllowWhileIdle()` (нативные системные будильники)
- `NotificationReceiver : BroadcastReceiver` — показывает уведомление даже при закрытом приложении
- Канал `medtracker_reminders` с `NotificationImportance.High` и вибрацией
- ID уведомлений: 31-битный хеш от GUID строки

**Что проверить:**
- Если менялся интерфейс `INotificationService` — обнови реализацию `AndroidNotificationService`.
- Если добавились новые методы в `INotificationService` (например, batch-отмена, категории) — реализуй их.

#### 1.6 Проверь `Assets/appsettings.json`

```json
{
  "Supabase": {
    "Url": "https://project-ref.supabase.co",
    "AnonKey": "eyJhbGciOiJIUzI1NiJ9.eyJyb2xlIjoiYW5vbiJ9.signature",
    "SignedUrlTtlSeconds": 300
  }
}
```

- Убедись, что здесь **только `AnonKey`**, никогда не `service_role`.
- Если добавились новые секции конфигурации в Desktop `appsettings.json` — добавь их и в Android-версию.

---

### Фаза 2: Адаптация UI для Android

#### 2.1 Проверь адаптивность `MainView.axaml`

**Механизм адаптации:**
- `IsCompact = e.NewSize.Width < 640` в `MainView.axaml.cs`
- **Desktop (≥ 640px):** боковая навигация `SideNavItem` (Сегодня, Лекарства, Медкарта + кнопка Настройки)
- **Mobile (< 640px):** нижняя панель `BottomNavItem` (4 раздела)

**Android-специфичные требования:**
1. **Отступ нижней панели** — `Padding="4,6,4,18"` на контейнере `BottomNav`. Значение `18` — безопасный отступ от системной полоски жестов (home gesture pill).
2. **Автопрокрутка при клавиатуре** — `MainView.axaml.cs` слушает `GotFocusEvent` и вызывает `BringIntoView()` с задержкой 250ms.
3. **TextWrapping="Wrap"** — все длинные текстовые блоки должны оборачиваться.
4. **WrapPanel** вместо `StackPanel Orientation="Horizontal"` для тегов/чипов, которые могут не поместиться на узком экране.

**Что проверить:**
- Если в обновлённых Views добавились новые элементы — убедись, что они корректно выглядят при ширине < 640px.
- Новые `TextBlock` с длинным текстом должны иметь `TextWrapping="Wrap"`.
- Новые горизонтальные списки элементов должны использовать `WrapPanel`.
- Проверь, что `MaxLength` на `TextBox` полях установлены: Username — `MaxLength="20"`, Название лекарства и Описание — `MaxLength="50"`.

#### 2.2 Проверь новые/изменённые экраны

Для каждого изменённого `.axaml` файла в `src/Med.Ui/Views/Screens/`:
1. Убедись, что `x:DataType` привязки корректны и соответствуют ViewModel из `Med.Presentation`.
2. Проверь все `DynamicResource` и `StaticResource` ключи — они должны быть определены в `MedIcons.axaml`, `MedPurpleTheme.axaml`, `MedStyles.axaml`.
3. Проверь, что модальные окна и оверлеи не выходят за пределы экрана на Android (особенно `AccountView.axaml` с кадрированием аватара).

#### 2.3 Проверь файловые операции (галерея, аватар, документы)

**Файловый пикер:** `AvaloniaFilePickerService.cs`
- На Desktop: прямой `file.TryGetLocalPath()`.
- На Android: `content://` URI → `OpenReadAsync()` → копия во временный файл `picked_avatar_{guid}.jpg`.

**Что проверить:**
- Если добавились новые типы загружаемых файлов — убедись, что `AvaloniaFilePickerService` обрабатывает их Android-путь через `content://`.

---

### Фаза 3: Сборка Android APK

#### 3.1 Компиляция проекта

```bash
# 1. Убедись что Android workload установлен
dotnet workload install android

# 2. Собери Android-проект
dotnet build src/Med.Android/Med.Android.csproj -c Release

# 3. Проверь 0 ошибок, 0 warnings
```

Если появляются ошибки:
- **IL-warnings о тримминге** → убедись что `<PublishTrimmed>false</PublishTrimmed>` и `<AndroidLinkMode>None</AndroidLinkMode>`.
- **Ошибки компиляции в shared-слоях** → исправь их, они должны быть платформо-независимыми.
- **Отсутствие Android SDK** → установи JDK 17+ и Android SDK через `dotnet workload install android`.

#### 3.2 Публикация и упаковка APK

**Используй готовый скрипт:**
```bash
bash build/android/make-apk.sh
```

**Или вручную:**
```bash
dotnet publish src/Med.Android/Med.Android.csproj \
  --configuration Release

# Найди APK
ls -la src/Med.Android/bin/Release/net10.0-android/publish/*.apk
ls -la src/Med.Android/bin/Release/net10.0-android/*-Signed.apk

# Скопируй в artifacts
mkdir -p artifacts/android
cp src/Med.Android/bin/Release/net10.0-android/publish/*-Signed.apk artifacts/android/MedTracker.apk
```

#### 3.3 Для релизной подписи (опционально)

```bash
export MEDTRACKER_KEYSTORE=~/keys/medtracker-release.keystore
export MEDTRACKER_KEYSTORE_PASS=<пароль>
export MEDTRACKER_KEY_ALIAS=medtracker
export MEDTRACKER_KEY_PASS=<пароль>
bash build/android/make-apk.sh
```

---

### Фаза 4: Тестирование

#### 4.1 Прогон всех unit-тестов

```bash
# Полный прогон всех тестовых проектов (270+ тестов)
dotnet test tests/Med.Domain.Tests/Med.Domain.Tests.csproj -c Release
dotnet test tests/Med.Application.Tests/Med.Application.Tests.csproj -c Release
dotnet test tests/Med.Infrastructure.Tests/Med.Infrastructure.Tests.csproj -c Release
dotnet test tests/Med.Presentation.Tests/Med.Presentation.Tests.csproj -c Release
```

**Критерий успеха:** `100% Passed`, `Failed: 0` во всех проектах.

**Особое внимание:**
- `Med.Presentation.Tests` — проверяют ViewModel'и, которые используются и на Desktop, и на Android.
- `ViewModelMemoryLeakTests` — проверяют отсутствие утечек в ViewModel'ях.
- Тесты `MedicationsViewModel`, `AuthViewModel`, `AccountViewModel`, `ShellViewModel` — критически важны для Android.
- Тесты `DoseEventMaterializer`, `CourseVisibility`, `MedicationVisibility` — доменная логика.

#### 4.2 Проверь компиляцию бенчмарков

```bash
dotnet build tests/Med.Performance.Tests/Med.Performance.Tests.csproj -c Release
```

#### 4.3 Собери с флагом `-warnaserror`

```bash
dotnet build MedTracker.slnx -c Release -warnaserror
```

**Критерий:** 0 Errors, 0 Warnings. Если появляются — исправь причину, не подавляй.

#### 4.4 Если добавлялись новые фичи — напиши тесты

Используй существующие конвенции проекта:
- **Фреймворк**: xUnit v3 + FluentAssertions
- **Паттерн именования тестов**: `MethodName_Scenario_ExpectedBehavior`
- **Файловая структура**: отражает `src/` (например, `tests/Med.Presentation.Tests/Medications/MedicationsViewModelTests.cs`)
- **Моки**: проверь что уже используется в проекте (NSubstitute или ручные заглушки)

**Пример структуры нового тестового класса:**
```csharp
using FluentAssertions;
using Xunit;

namespace Med.Presentation.Tests.Medications;

public sealed class NewFeatureTests
{
    [Fact]
    public void Feature_WhenCondition_ShouldExpectedResult()
    {
        // Arrange
        // ...

        // Act
        // ...

        // Assert
        result.Should().BeTrue();
    }
}
```

---

### Фаза 5: Верификация

#### 5.1 Архитектурная верификация

1. **Clean Architecture** — проверь `.csproj` всех слоёв:
   ```bash
   # Med.Domain — 0 зависимостей
   grep -E "ProjectReference|PackageReference" src/Med.Domain/Med.Domain.csproj
   # Med.Application — только Med.Domain
   grep "ProjectReference" src/Med.Application/Med.Application.csproj
   # Med.Presentation — только Med.Application (НЕ Infrastructure)
   grep "ProjectReference" src/Med.Presentation/Med.Presentation.csproj
   ```

2. **Навигация** — ровно 4 раздела, нет «Курсов»:
   ```bash
   # Не должно быть BottomNavItem или SideNavItem с "Курсы"/"Courses"
   grep -rn "Courses\|Курсы" src/Med.Ui/Views/MainView.axaml
   # ShellNav enum не должен содержать Courses
   grep -n "Courses" src/Med.Presentation/Shell/ShellNav.cs
   ```

3. **Android-специфика**:
   ```bash
   # OnCreate() обязателен
   grep -A2 "override void OnCreate" src/Med.Android/MedAndroidApplication.cs
   # Отступ нижней панели
   grep "4,6,4,18" src/Med.Ui/Views/MainView.axaml
   ```

#### 5.2 Безопасность

```bash
# Нет service_role ключей в клиентском коде
git grep -rn "service_role" -- "src/"

# .env не отслеживается Git
git ls-files .env

# Нет хардкодов паролей/токенов
git grep -iE "password\s*=\s*\"[^\"]+\"" -- "src/" | grep -v "appsettings" | grep -v "Test"

# Нет отладочных хвостов
git grep -nE "Console\.WriteLine|TODO|FIXME|HACK" -- "src/"
```

#### 5.3 Гигиена репозитория

```bash
# .DS_Store не в git
git ls-files "*.DS_Store"

# bin/obj не в git
git ls-files "*/bin/*" "*/obj/*"

# Нет секретов в .env.example (только плейсхолдеры)
cat .env.example | grep -v "^#" | grep -v "your_"
```

#### 5.4 Итоговая верификация APK

```bash
# Проверь размер APK (ожидается ~70-75 MB)
ls -lh artifacts/android/MedTracker*.apk

# Опционально: установи на устройство/эмулятор
adb install -r artifacts/android/MedTracker.apk

# Проверь запуск
adb shell am start -n com.mysechka.medtracker/crc64...MainActivity
```

---

### Фаза 6: Итоговый отчёт

По завершении всех этапов сформируй структурированный отчёт:

```markdown
# 📱 Отчёт о сборке Android APK — MedTracker

## Версия
- **Версия приложения**: <X.Y.Z>
- **versionCode**: <N>
- **Git commit**: <hash>

## Статус сборки
- [ ] `dotnet build` Android: ✅ 0 errors, 0 warnings
- [ ] `dotnet build -warnaserror` всего решения: ✅ 0 errors, 0 warnings
- [ ] APK собран: `artifacts/android/MedTracker-<version>.apk` (<размер>)

## Статус тестов
| Проект | Passed | Failed | Skipped |
|--------|--------|--------|---------|
| Med.Domain.Tests | | | |
| Med.Application.Tests | | | |
| Med.Infrastructure.Tests | | | |
| Med.Presentation.Tests | | | |

## Адаптация Android
- [ ] DI-композиция актуальна (MedAndroidApplication.cs)
- [ ] AndroidManifest.xml — permissions и версии синхронизированы
- [ ] MainActivity.cs — runtime permissions актуальны
- [ ] AndroidNotificationService — интерфейс INotificationService актуален
- [ ] Assets/appsettings.json — конфиг синхронизирован (только AnonKey)
- [ ] UI адаптивность — все новые экраны корректны при ширине < 640px
- [ ] Отступ нижней панели — Padding="4,6,4,18" на месте

## Верификация
- [ ] Clean Architecture — зависимости направлены внутрь
- [ ] Навигация — ровно 4 раздела, нет «Курсов»
- [ ] Безопасность — нет service_role, нет хардкодов, нет секретов в git
- [ ] OnCreate() — присутствует в MedAndroidApplication.cs

## Итоговый вердикт
✅ APK READY / 🚫 NEEDS FIXES
```

---

## ⚠️ Критические ограничения и правила

> [!CAUTION]
> 1. **НИКОГДА НЕ добавлять кнопку «Курсы»** в навигацию (ни `SideNavItem`, ни `BottomNavItem`).
> 2. **НЕ включать тримминг** (`PublishTrimmed`) и AOT для Android — это ломает Supabase и Avalonia.
> 3. **НЕ удалять `OnCreate()`** из `MedAndroidApplication.cs` — это вызывает `UnsatisfiedLinkError`.
> 4. **НЕ ломать** существующую доменную логику (`DoseEventMaterializer`, `InventoryRules`, `DoseEventTransitions`).
> 5. **НЕ менять** направление зависимостей Clean Architecture.
> 6. **НЕ добавлять** зависимости на внешние пакеты в `Med.Domain` (остаётся чистый C#).
> 7. **Сохранять** все существующие комментарии и XML-документацию.
> 8. **НЕ использовать** `service_role` ключ Supabase в клиентском коде (только `AnonKey`).
> 9. **Git-коммиты** строго:
>    ```bash
>    git -c user.name="Mysechka" -c user.email="egorkostin71@gmail.com" commit -m "<message>"
>    ```
> 10. `git push` и `push --force` — только по прямой команде пользователя.

---

## 📦 Структура ключевых Android-файлов (справочник)

| Файл | Назначение |
|------|-----------|
| `src/Med.Android/Med.Android.csproj` | Проектный файл: `net10.0-android`, APK формат, без тримминга |
| `src/Med.Android/Properties/AndroidManifest.xml` | Разрешения, версия, иконки |
| `src/Med.Android/MainActivity.cs` | Лаунчер Activity: `AvaloniaMainActivity`, runtime permissions |
| `src/Med.Android/MedAndroidApplication.cs` | DI-композиция: `AvaloniaAndroidApplication<App>`, конфигурация |
| `src/Med.Android/AndroidNotificationService.cs` | Нативные уведомления: `AlarmManager`, `NotificationChannel` |
| `src/Med.Android/NotificationReceiver.cs` | `BroadcastReceiver` для показа уведомлений в фоне |
| `src/Med.Android/Assets/appsettings.json` | Конфиг Supabase (только AnonKey) |
| `src/Med.Android/Resources/mipmap-*/` | Иконки приложения (mdpi → xxxhdpi) |
| `build/android/make-apk.sh` | Скрипт сборки APK с опциональной подписью keystore |
