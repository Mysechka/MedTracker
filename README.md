# MedTracker

Персональное приложение для приёма лекарств и хранения медкарты.
Целевые устройства с GUI — Android и macOS. На Windows и Linux GUI нет:
там уведомления читаются в десктопных клиентах Telegram и Discord.

Спецификация разработки: [`docs/spec/02-промт-разработка.md`](docs/spec/02-промт-разработка.md).

## Текущее состояние

**Стадии 0–7 спецификации ([`docs/spec/02-промт-разработка.md`](docs/spec/02-промт-разработка.md)) завершены в полном объёме:**

- **Стадия 7 (Сборка и запуск)** — закрыта:
  - **macOS-бандл**: скрипт `build/macos/make-app-bundle.sh` собирает самодостаточный `.app` (`artifacts/macos/<rid>/MedTracker.app`), генерирует системную иконку `.icns` из ассета приложения, формирует `Info.plist` и выполняет ad-hoc codesign. Проверен запуском.
  - **Android APK**: скрипт `build/android/make-apk.sh` собирает и подписывает APK (`artifacts/android/com.mysechka.medtracker-Signed.apk`). Устранён конфликт `RunAOTCompilation` при отключённом тримминге (`PublishTrimmed=false`). Поддерживается релизная подпись через переменные окружения `MEDTRACKER_KEYSTORE*`.
  - **CI/CD**: GitHub Actions workflow (`.github/workflows/ci.yml`) выполняет сборку всего решения, прогон тестов всех слоёв и формирует артефакты для обеих платформ (macOS `.app` и Android `.apk`).
- **Стадия 6 (UI и head-проекты)** — закрыта:
  - Avalonia Views реализованы для всех экранов (Авторизация, Сегодня, Лекарства, Курсы и расписания, Медкарта и документы, Настройки профиля и мессенджеров, Диагностика и Realtime).
  - Навигация построена на базе `ViewLocator` и `CurrentScreen`.
  - Обе головы (`Med.Desktop` и `Med.Android`) изолированно конфигурируют DI-контейнер и предоставляют `IUiDispatcher`.
  - Строго соблюдены архитектурные границы Clean Architecture: нулевая бизнес-логика в слое View и code-behind, отсутствие прямых ссылок `Med.Ui` на `Med.Infrastructure`.
- **Ранее закрыты**:
  - Стадия 5: ViewModels (`CommunityToolkit.Mvvm`) с покрытием тестами без UI-рантайма.
  - Стадия 4: Edge Functions Supabase (`tick`, `telegram-webhook`, `discord-interactions`) и идемпотентная SQL-материализация расписаний через `pg_cron`.
  - Стадии 1–3: Доменная модель, правила времени и DST, SQL-миграции, RLS, репозитории на Supabase SDK.

## Структура

```
src/Med.Domain/          чистый C#, ноль внешних зависимостей
src/Med.Application/     интерфейсы сервисов, DTO, use-cases
src/Med.Infrastructure/  Supabase-репозитории, HttpClient-клиенты, маппинг
src/Med.Presentation/    ViewModels (CommunityToolkit.Mvvm), навигация
src/Med.Ui/              Avalonia Views — технический каркас, выбрасываемый
src/Med.Desktop/         head-проект macOS
src/Med.Android/         head-проект Android
tests/                   xUnit + FluentAssertions
supabase/migrations/     *.sql
supabase/functions/      Edge Functions (Deno)
```

Правило слоёв: бизнес-логики нет ни в `Med.Ui`, ни в code-behind. `Med.Ui` не
ссылается на `Med.Infrastructure`, поэтому обратиться к Supabase из View нельзя
физически. Слой UI удаляется целиком без поломки остального решения.

Интерфейс делает отдельный человек позже, поэтому текущие экраны — голые
контролы без стилей и без темизации.

Единственное, что UI-слой отдаёт наверх, — реализация `IUiDispatcher`
(`AddMedUi()` в голове). Realtime и события Auth приходят из фоновых потоков,
а привязанные коллекции менять оттуда нельзя. Без Avalonia работает
`ImmediateUiDispatcher` из `Med.Presentation`, поэтому ViewModels тестируются
без UI-рантайма.

## Часовой пояс

Пользователь не выбирает IANA-зону: он задаёт целое смещение в часах
относительно Москвы. `0` — московское время, `+4` — Москва+4, `-2` — Москва−2.
Допустимый диапазон — от −12 до +11: Москва это UTC+3, а зон восточнее UTC+14
в базе IANA нет.

Смещение приводится к фиксированной зоне (`Europe/Moscow`, `Etc/GMT±N`,
`Etc/UTC`) и в этом виде хранится в `profiles.time_zone_id`. Тот же диапазон
проверяется CHECK-констрейнтом в базе, а не только в клиенте. У фиксированных
зон и у Москвы нет перехода на летнее время, поэтому «пропавших» и «удвоенных»
часов в расписаниях не бывает.

## Требования

| Инструмент | Версия | Зачем |
|---|---|---|
| .NET SDK | 10.0.400 | сборка всего решения |
| workload `android` | 36.1.69 | head-проект Android |
| Android SDK + JDK 17+ | — | сборка APK |
| supabase CLI | 2.115+ | миграции и Edge Functions (стадия 2+) |
| Xcode Command Line Tools | — | сборка macOS-бандла, только на Mac |

Локальная установка на этой машине (macOS): .NET в `~/.dotnet`, Android SDK
в `~/Android/Sdk` (symlink на Homebrew cmdline-tools), JDK 17 в `~/Android/jdk`
(symlink на `openjdk@17`). Переменные `DOTNET_ROOT`, `ANDROID_HOME` и
`JAVA_HOME` прописаны в `~/.zshrc` / `~/.bashrc`.

## Быстрый старт (Quick Start)

Всего 2 команды для полноценного локального запуска и проверки:

```bash
# 1. Проверка окружения, запуск Supabase, накат миграций и seed-данных, запуск Desktop UI:
make dev

# 2. Быстрый прогон всех unit- и integration-тестов (xUnit v3) без запуска GUI:
make test
```

> **Альтернатива без make:**
> - macOS / Linux: `./run.sh dev` и `./run.sh test`
> - Windows: `.\run.ps1 dev` и `.\run.ps1 test`
>
> **Тестовый аккаунт (из seed.sql):**
> - **Логин**: `dev@medtracker.local`
> - **Пароль**: `password123`
> - **Supabase Studio**: `http://127.0.0.1:54323`
> - **Inbucket (почта)**: `http://127.0.0.1:54324`

## Ручная сборка и запуск

```bash
dotnet build MedTracker.slnx
dotnet test MedTracker.slnx

# Desktop-голова. Целевая платформа — macOS; на Linux запускается только
# как инструмент разработчика для проверки логики.
dotnet run --project src/Med.Desktop

# macOS-бандл: artifacts/macos/<rid>/MedTracker.app
build/macos/make-app-bundle.sh

# Android APK: artifacts/android/com.mysechka.medtracker-Signed.apk
build/android/make-apk.sh

# Android запуск на подключенном устройстве с включённой отладкой по USB
dotnet build src/Med.Android -t:Run
```

Подробные шаги и подводные камни:
[`docs/build/macos.md`](docs/build/macos.md),
[`docs/build/android.md`](docs/build/android.md).

## Конфигурация

Клиент читает настройки из `appsettings.json` рядом с бинарником, из
`appsettings.Local.json` (в `.gitignore`) и из переменных окружения с префиксом
`MEDTRACKER_`. На Android — из ассета `Assets/appsettings.json`.

В клиенте допустим только anon key: конфигурация с ключом `service_role`
не проходит валидацию и приложение падает на старте осознанно.

Секреты Telegram, Discord и `service_role` живут только в переменных окружения
Edge Functions. Перечень — в [`.env.example`](.env.example).
