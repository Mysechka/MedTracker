# MedTracker — Инструкции и правила для ИИ-ассистентов (.NET 10 / Avalonia 12 / Supabase)

Репозиторий кроссплатформенного медицинского трекера на стеке **.NET 10, Avalonia UI 12, Supabase BaaS**.

## 1. Архитектурные границы (Clean Architecture)

- `src/Med.Domain`: чистая модель, ноль сторонних зависимостей. Домен не знает о Supabase, Avalonia, HTTP, БД или системном времени.
- `src/Med.Application`: интерфейсы, DTO, use-cases. Зависит только от `Med.Domain` и `Microsoft.Extensions.*` abstractions.
- `src/Med.Infrastructure`: реализация репозиториев (Supabase SDK 1.6.0), PostgREST DTO (`*Row`), HttpClient. Модели PostgREST не должны покидать этот слой.
- `src/Med.Presentation`: ViewModels (`CommunityToolkit.Mvvm`), навигация, абстракция `IUiDispatcher`. Слой не зависит от Avalonia.
- `src/Med.Ui`: Avalonia XAML и Views. Зависит от `Med.Presentation` и Avalonia. **Никакой связи с `Med.Infrastructure`**. В code-behind — только `InitializeComponent`.
- `src/Med.Desktop` и `src/Med.Android`: head-проекты, сборка DI-контейнера.

## 2. Стандарты кода .NET 10 и C#

- `Nullable: enable`, `TreatWarningsAsErrors: true`.
- **Запрет сокрытия варнингов:** Не использовать `#pragma warning disable` для подавления предупреждений компилятора или анализаторов.
- Запрещены блокирующие синхронные ожидания: никаких `.Result`, `.Wait()`, `.GetAwaiter().GetResult()`.
- Запрещён `async void` (кроме обработчиков событий Avalonia UI).
- Все операции со временем опираются на `ISystemClock`. Запрещены прямые обращения к `DateTime.UtcNow`.
- Часовой пояс задаётся явным смещением «Москва ± N часов» и приводится к фиксированной IANA-зоне.

## 3. Avalonia UI 12.1 + CommunityToolkit.Mvvm

- Использовать `[ObservableProperty]` и `[RelayCommand]`.
- Включены скомпилированные привязки: каждый `<DataTemplate>` обязан иметь `x:DataType="vm:TargetViewModel"`.
- Обновления UI из фоновых потоков / Realtime выполняются строго через `IUiDispatcher.Post()`.
- **Предотвращение утечек памяти:** Любой ViewModel, подписывающийся на события (`IAuthService.AuthStateChanged`, `IDoseEventRealtime.Changed`), обязан реализовывать `IDisposable` и отписываться от событий.
- В property changed callbacks запрещены неперехваченные fire-and-forget таски (`_ = Method()`). Ошибки должны перехватываться (`try/catch`).

## 4. Платформенные особенности (Android net10.0-android)

- Supabase Postgrest и Avalonia опираются на reflection. В `Med.Android.csproj` зафиксированы `<PublishTrimmed>false</PublishTrimmed>` и `<AndroidLinkMode>None</AndroidLinkMode>`. Не включать тримминг без полного конфига preserved assemblies.
- Конфигурация на Android читается из `Assets/appsettings.json`. В APK допустим только `anon key`.

## 5. Безопасность и Supabase BaaS

- **Секреты:** Anon key — единственный ключ в клиенте. `service_role` и токены ботов Telegram/Discord живут только в Edge Functions (Deno / TypeScript).
- **RLS:** Включён на всех таблицах PostgreSQL с проверкой `auth.uid()`.
- **Edge Functions:**
  - `tick` обязан валидировать заголовок `Authorization: Bearer <token>`.
  - `telegram-webhook` проверяет `X-Telegram-Bot-Api-Secret-Token`.
  - `discord-interactions` проверяет подписи Ed25519 через WebCrypto: `{ name: "Ed25519" }` (без параметра `namedCurve`).
- Миграции БД только append-only (новые файлы, старые не редактируются).

## 6. Тестирование (xUnit v3 + FluentAssertions)

- Во всех асинхронных тестах использовать `TestContext.Current.CancellationToken`.
- **Никаких Flaky тестов:** Запрещено использовать `await Task.Yield()` или `Task.Delay()` для ожидания фоновых операций. Использовать детерминированные сигналы (Task).
- Тесты обязаны проверять реальные инварианты домена:
  - Идемпотентность материализации по `dedupe_key`.
  - Корректность перемотки курсора интервальных расписаний для курсов в прошлом.
  - Первое терминальное состояние (`Taken`/`Skipped`) побеждает; повторное подтверждение — `NoOp`.
  - Списание остатка ровно один раз через `inventory_transactions`.

## 7. Правила Git, коммитов и контрибуций на GitHub

- **Авторство коммитов (строго обязательно):**
  Все коммиты (`git commit`) и отправки (`git push`) в репозиторий должны выполняться **исключительно** под аккаунтом GitHub [Mysechka](https://github.com/Mysechka) со следующими реквизитами:
  - **Email:** `egorkostin71@gmail.com`
  - **Name:** `Mysechka`
  - **Профиль GitHub:** `https://github.com/Mysechka`
- **Подсчёт контрибуций:** Запрещено использовать любые другие email-адреса (локальные адреса рабочей станции, `noreply@github.com` и сторонние почты), чтобы все коммиты гарантированно привязывались к профилю и учитывались в графике контрибуций (GitHub contribution graph).
- **Команды для агентов (без изменения глобального git config):**
  ```bash
  git -c user.email="egorkostin71@gmail.com" -c user.name="Mysechka" commit -m "..."
  ```
  Либо задание переменных окружения перед выполнением команд:
  ```bash
  export GIT_AUTHOR_EMAIL="egorkostin71@gmail.com"
  export GIT_AUTHOR_NAME="Mysechka"
  export GIT_COMMITTER_EMAIL="egorkostin71@gmail.com"
  export GIT_COMMITTER_NAME="Mysechka"
  ```
- Коммиты и пуши выполняются только по прямой просьбе пользователя. Запрещён force push в ветку `main`.

