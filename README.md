# MedTracker

Персональное приложение для приёма лекарств и хранения медкарты.
Целевые устройства с GUI — Android и macOS. На Windows и Linux GUI нет:
там уведомления читаются в десктопных клиентах Telegram и Discord.

Спецификация разработки: [`docs/spec/02-промт-разработка.md`](docs/spec/02-промт-разработка.md).

## Текущее состояние

Стадия 4 — Edge Functions (`tick`, `telegram-webhook`, `discord-interactions`),
материализация dose_events в SQL, pg_cron → tick через Vault.
Med.Presentation пока каркас; экраны ViewModels — стадия 5.

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

## Требования

| Инструмент | Версия | Зачем |
|---|---|---|
| .NET SDK | 10.0.400 | сборка всего решения |
| workload `android` | 36.1.69 | head-проект Android |
| Android SDK + JDK 17+ | — | сборка APK |
| supabase CLI | 2.115+ | миграции и Edge Functions (стадия 2+) |
| Xcode Command Line Tools | — | сборка macOS-бандла, только на Mac |

Локальная установка на этой машине: .NET в `~/.dotnet`, Android SDK в
`~/Android/Sdk`, JDK в `~/Android/jdk`. Переменные `DOTNET_ROOT`, `ANDROID_HOME`
и `JAVA_HOME` прописаны в `~/.bashrc`.

## Сборка и запуск

```bash
dotnet build MedTracker.slnx
dotnet test tests/Med.Domain.Tests
dotnet test tests/Med.Application.Tests

# Desktop-голова. Целевая платформа — macOS; на Linux запускается только
# как инструмент разработчика для проверки логики.
dotnet run --project src/Med.Desktop

# Android: подключить устройство с включённой отладкой по USB
dotnet build src/Med.Android -t:Run
```

## Конфигурация

Клиент читает настройки из `appsettings.json` рядом с бинарником, из
`appsettings.Local.json` (в `.gitignore`) и из переменных окружения с префиксом
`MEDTRACKER_`. На Android — из ассета `Assets/appsettings.json`.

В клиенте допустим только anon key: конфигурация с ключом `service_role`
не проходит валидацию и приложение падает на старте осознанно.

Секреты Telegram, Discord и `service_role` живут только в переменных окружения
Edge Functions. Перечень — в [`.env.example`](.env.example).
