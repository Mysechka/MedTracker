# MedTracker

Персональное приложение для приёма лекарств и хранения медкарты.
Целевые устройства с GUI — Android и macOS. На Windows и Linux GUI нет:
там уведомления читаются в десктопных клиентах Telegram и Discord.

Спецификация разработки: [`docs/spec/02-промт-разработка.md`](docs/spec/02-промт-разработка.md).

## Текущее состояние

Стадия 7 — сборка и запуск. macOS-бандл собирается скриптом
`build/macos/make-app-bundle.sh` и проверен запуском; шаги Android описаны, но
APK пока не собирался.

Стадия 6 — технический каркас UI закрыт: Avalonia Views для всех экранов
(Auth, Сегодня, Лекарства, Курсы, Медкарта, Настройки, Диагностика), навигация
через `ViewLocator`, обе головы собирают контейнер сами.

Ранее: стадия 5 — ViewModels, стадия 4 — Edge Functions и SQL-материализация.

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

## Сборка и запуск

```bash
dotnet build MedTracker.slnx
dotnet test MedTracker.slnx

# Desktop-голова. Целевая платформа — macOS; на Linux запускается только
# как инструмент разработчика для проверки логики.
dotnet run --project src/Med.Desktop

# macOS-бандл: artifacts/macos/<rid>/MedTracker.app
build/macos/make-app-bundle.sh

# Android: подключить устройство с включённой отладкой по USB
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
