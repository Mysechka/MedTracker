# ✅ MedTracker — Промт для сквозной верификации в Antigravity

> **Назначение**: Скопируй и отправь этот промт агенту в Antigravity для проведения полной технической, архитектурной и UI/UX верификации проекта **MedTracker** (включая проверку обновлённого карточного дизайна экрана «Аккаунт», русской локализации календаря курса, ограничений длины ввода в 20 и 50 символов, прогон всех тестов и сборку с `-warnaserror`).

---

## 📋 Текст промта (для вставки в Antigravity)

Ты — **Principal QA & Verification Engineer** проекта **MedTracker** (.NET 10, C# 14, Avalonia UI 12.1.1, Clean Architecture, SQLite + Supabase).

Твоя задача — выполнить **полную многоуровневую верификацию** текущего состояния репозитория (включая все текущие модификации в рабочем дереве `git status`), проверить соблюдение строгих правил из `AGENTS.md`, прогнать сборку и тесты, выявить возможные регрессии или нарушения контрактов и, если будут обнаружены ошибки компиляции или падения тестов, устранить их.

Выполняй верификацию строго по следующим 6 блокам:

---

### Блок 1. Проверка жёстких правил `AGENTS.md` (Architecture & UI Invariants)

1. **Навигация (КРИТИЧНО)**:
   - Убедись, что в приложении ровно **4 основных раздела навигации**: `Сегодня`, `Лекарства`, `Медкарта`, `Настройки`.
   - Проверь `MainView.axaml`, `ShellViewModel.cs`, `ShellNav.cs`: ни в боковом меню (`SideNavItem`), ни в нижней мобильной панели (`BottomNavItem`) **не должно быть** отдельной кнопки/вкладки «Курсы» (`Courses`).
   - Настройка курса приёма должна работать исключительно внутри карточки создания/редактирования лекарства (`MedicationsView.axaml` / `MedicationsViewModel.cs`).

2. **Адаптивность и Android-требования**:
   - В `MainView.axaml` у нижней панели навигации (`Grid.Row="2"`) должен сохраняться безопасный нижний отступ под жест навигации Android (`Padding="4,6,4,18"`).
   - Текстовые элементы с длинными подписями должны иметь `TextWrapping="Wrap"`, а списки тегов/чипов использовать адаптивные контейнеры (`WrapPanel`).
   - В `src/Med.Android/MedAndroidApplication.cs` обязательно должен присутствовать метод `public override void OnCreate() { base.OnCreate(); }`.

3. **Чистота Clean Architecture**:
   - Проверь `.csproj` файлы всех слоёв:
     - `Med.Domain` — 0 внешних NuGet-пакетов и проектных зависимостей.
     - `Med.Application` — зависит только от `Med.Domain`.
     - `Med.Presentation` — зависит только от `Med.Application` и `Med.Domain` (никаких ссылок на `Med.Infrastructure`).

---

### Блок 2. Верификация текущих изменений Presentation и UI (Material Design 3 / PDF-прототип)

Проверь корректность, потокобезопасность и согласованность последних изменений в `src/Med.Presentation` и `src/Med.Ui`:

1. **Обновлённый карточный дизайн экрана «Аккаунт» (`AccountView.axaml` & `AccountViewModel.cs`)**:
   - Проверь локальные стили в `<UserControl.Styles>` внутри `AccountView.axaml`: `Border.MedAccountCard`, `Button.MedPillDangerOutline`, `Button.MedAvatarEditBadge`, `Border.MedBotStatusChip` не должны конфликтовать с глобальными стилями `MedStyles.axaml`.
   - **Верхняя карточка профиля (Hero Card)**:
     - Круглый аватар (`88×88`) с наложенной в правом нижнем углу круглой кнопкой-карандашом (`MedAvatarEditBadge`, иконка `MedIcon_Pencil`), привязанной к `ChangeAvatarCommand`.
     - Поле «Имя пользователя» (`Username`, `MaxLength="20"`) с бейджем режима (`Локальный режим` при `IsLocalOnly` / `Облачный режим` при `!IsLocalOnly`) и кнопкой «Сохранить» (`SaveCommand`, видима при `HasChanges`).
     - Контурная кнопка «Выйти из аккаунта» (`MedPillDangerOutline`, `SignOutCommand`) в правой части карточки.
   - **Нижняя двухколоночная сетка карточек (`Grid ColumnDefinitions="*,*"`)**:
     - Левая карточка корректно переключается между секцией «Привязка аккаунта» (`IsVisible="{Binding IsLocalOnly}"`, поля `LinkEmail`, `LinkPassword`, ошибка `LinkErrorMessage`, кнопка `LinkAccountCommand`) и секцией «Параметры входа» (`IsVisible="{Binding !IsLocalOnly}"`, поля `Email`, `NewPassword`, кнопка `SaveCommand`), а поля ввода растягиваются по ширине карточки (`Width="NaN" HorizontalAlignment="Stretch"`).
     - Правая карточка «Облако и боты» отображает 3 преимущества облачной синхронизации с иконками `MedIcon_Check`, разделительную линию и нижние статусные чипы `MedBotStatusChip` для Telegram (`IsTelegramLinked` / `!IsTelegramLinked`) и Discord (`IsDiscordLinked` / `!IsDiscordLinked`).
   - **Модальный оверлей кадрирования аватара (`IsCropping`)**: убедись, что все обработчики мыши (`OnCropPointerPressed`, `OnCropPointerMoved`, `OnCropPointerReleased`, `OnCropPointerCaptureLost`, `OnCropPointerWheelChanged`) и привязки (`CropSourcePath`, `CropZoom`, `CropPanX`, `CropPanY`, `CancelCropCommand`, `ApplyCropCommand`, `ResetCropPositionCommand`) полностью сохранены и работают без ошибок.

2. **Русская локализация и обновлённый дизайн календаря курса (`MedicationsViewModel` & `MedicationsView.axaml`)**:
   - Убедись, что карточка выбора даты окончания курса (появляющаяся при включении тогла «Приём лекарства курсом») полностью переведена на русский язык:
     - Заголовок шапки: «Дата окончания курса» (никакого английского `Select date`).
     - Формат выбранной даты (`CourseEndsOnHeaderText`): на русском языке в родительном падеже (например, `Пт, 6 ноября`).
     - Заголовок месяца и года (`CalendarMonthYearTitle`): на русском языке в именительном падеже (например, `Ноябрь 2026`).
     - Дни недели начинаются с понедельника (`Пн Вт Ср Чт Пт Сб Вс`), а смещение первого дня месяца в `RebuildCalendarDays()` вычисляется по формуле `((int)firstOfMonth.DayOfWeek + 6) % 7`.
     - Отображается бейдж длительности курса (`CourseDurationSummaryText`, например `Длительность курса: 15 дн.`).
   - Проверь визуальное оформление `Button.MedCalendarDayBtn` в `MedStyles.axaml`: кнопки дней должны быть строго круглыми (`Width="34" Height="34" MinWidth="34" MinHeight="34" CornerRadius="17"`), без вертикального растяжения в овальную «капсулу».
   - Проверь работу со временем (`IClock` / `TodayDate`): убедись, что даты в `MedicationsViewModel` вычисляются через `TodayDate` (`_clock`), чтобы не нарушать детерминированность unit-тестов.

3. **Ограничения длины ввода (20 и 50 символов)**:
   - **Имя пользователя (`Username`) — строго до 20 символов**:
     - В `AuthView.axaml` и `AccountView.axaml` на `TextBox` для `Username` установлен атрибут `MaxLength="20"`.
     - В `AuthViewModel.cs` и `AccountViewModel.cs` (`MaxUsernameLength = 20`, `OnUsernameChanged`) значения длиннее 20 символов автоматически усекаются.
   - **Название лекарства (`Name`) и Описание лекарства (`Notes`) — строго до 50 символов**:
     - В `MedicationsView.axaml` на полях «Название лекарства» и «Описание лекарства» установлен атрибут `MaxLength="50"`.
     - В `MedicationsViewModel.cs` (`MaxNameLength = 50`, `MaxNotesLength = 50`, `OnNameChanged`, `OnNotesChanged`) значения длиннее 50 символов автоматически усекаются.

4. **Масштабирование интерфейса (`UiScale`)**:
   - Проверь `UiScaleChangedMessage.cs`, `SettingsViewModel.cs` (`UiScale`, `UiScalePercentText`, `SetUiScaleCommand`) и `ShellViewModel.cs` (`Receive(UiScaleChangedMessage)`).
   - Убедись, что `LayoutTransformControl` в `MainView.axaml` корректно привязан к `ScaleX="{Binding UiScale}"` и `ScaleY="{Binding UiScale}"`, а граничные значения масштаба (`Math.Clamp(..., 0.75, 1.35)`) не ломают вёрстку.

5. **Теги времени и карточки лекарств (`MedicationCardViewModel` & `TagChip.axaml`)**:
   - Проверь формирование `DisplayTags` («Утром», «Днем», «Вечером») и сохранение обратной совместимости со свойством `Tags` и методом `NormalizeTagName`, чтобы не сломать существующие тесты сериализации тегов в `Barcode`.

6. **Экран авторизации (`AuthViewModel` & `AuthView.axaml`)**:
   - Проверь команды `SubmitRegistrationCommand` и `SubmitLoginCommand`: убедись, что они корректно обрабатывают как локальный старт (`_auth.IsLocalOnly` / ввод только `Username`), так и облачный вход/регистрацию (`Email` + `Password`), не нарушая работу `StartLocalCommand`, `SignInCommand` и `SignUpCommand`.

7. **Проверка XAML и ресурсов темы (`MedIcons.axaml`, `MedPurpleTheme.axaml`, `MedStyles.axaml`)**:
   - Убедись, что все ключи `DynamicResource` и `StaticResource` существуют в `MedIcons.axaml` и `MedPurpleTheme.axaml`, а типы скомпилированных привязок `x:DataType` во всех `.axaml` файлах валидны.

---

### Блок 3. Сборка с нулевой терпимостью к предупреждениям

Запусти полную сборку решения в режиме `Release` с флагом `-warnaserror`:

```bash
dotnet build MedTracker.slnx -c Release -warnaserror
```

- **Критерий успеха**: `0 Error(s)`, `0 Warning(s)`.
- Если компилятор или анализатор выдаёт предупреждения или ошибки — исправь их в коде.

---

### Блок 4. Полный прогон всех тестовых наборов

Запусти все тестовые проекты решения:

```bash
dotnet test tests/Med.Domain.Tests/Med.Domain.Tests.csproj -c Release
dotnet test tests/Med.Application.Tests/Med.Application.Tests.csproj -c Release
dotnet test tests/Med.Infrastructure.Tests/Med.Infrastructure.Tests.csproj -c Release
dotnet test tests/Med.Presentation.Tests/Med.Presentation.Tests.csproj -c Release
```

- **Критерий успеха**: **100% Passed (`Failed: 0`)** во всех тестовых проектах (270+ тестов).
- Особое внимание удели `Med.Presentation.Tests`: убедись, что все тесты `MedicationsViewModel`, `AuthViewModel`, `AccountViewModel`, `ShellViewModel` и `ViewModelMemoryLeakTests` проходят успешно.

---

### Блок 5. Проверка безопасности и гигиены репозитория

1. Проверь `git status` и убедись, что секреты (`.env`), временные файлы (`local.db`, `.DS_Store`, `bin/`, `obj/`) не попадают в индекс Git.
2. Проверь отсутствие отладочных хвостов (`Console.WriteLine`, `TODO`, `FIXME`, пустых `catch {}` без логирования) в изменённых файлах:
   ```bash
   git grep -nE "TODO|FIXME|HACK|Console\.WriteLine" -- "src/"
   ```

---

### Блок 6. Формат итогового отчёта верификации

По завершении проверок выведи структурированный отчёт:

1. **Сводная таблица статусов по 5 категориям**:
   - Соблюдение `AGENTS.md` (Навигация, Android, Clean Architecture)
   - Корректность новых фич UI/Presentation (Новый карточный дизайн `AccountView.axaml`, русский календарь курса, лимиты 20/50 символов, `UiScale`, `AuthView`, `TodayView`)
   - Статус сборки (`dotnet build -c Release -warnaserror`)
   - Результаты тестов по каждому тестовому проекту (`Passed / Failed / Skipped`)
   - Гигиена репозитория и безопасность
2. **Список обнаруженных и исправленных проблем** (с указанием файла, строк и сути фикса).
3. **Итоговый вердикт**: `✅ VERIFIED & READY` или `🚫 NEEDS ATTENTION`.

> **Важное правило для Git**: Если потребуется сделать коммит по итогам верификации, строго используй авторство из `AGENTS.md` без изменения глобального конфига:
> `git -c user.name="Mysechka" -c user.email="egorkostin71@gmail.com" commit -m "<message>"`
