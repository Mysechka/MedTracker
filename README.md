# MedTracker

[![CI Build](https://github.com/mysechka/MedTracker/actions/workflows/ci.yml/badge.svg)](https://github.com/mysechka/MedTracker/actions/workflows/ci.yml)
[![.NET Version](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![Avalonia UI](https://img.shields.io/badge/Avalonia-12.1-8A2BE2?logo=avalonia)](https://avaloniaui.net/)
[![License: GPL v3 + Commons Clause](https://img.shields.io/badge/License-GPL_v3_%2B_Commons_Clause-blue.svg)](LICENSE.md)

Персональное кроссплатформенное приложение для приёма лекарств и ведения персональной медкарты с нативными локальными уведомлениями ОС.

Целевые платформы: **macOS (.app / .dmg)** и **Android (.apk)**.

---

## 📸 Скриншоты интерфейса

| Экран "Сегодня" | Лекарства и курсы | Медкарта |
| :---: | :---: | :---: |
| ![Экран Сегодня](docs/assets/screenshot-today.png) | ![Лекарства и курсы](docs/assets/screenshot-courses.png) | ![Медкарта](docs/assets/screenshot-records.png) |

---

## 📦 Установка и запуск готовых релизов

### macOS (.DMG)
1. Скачайте актуальный релиз `MedTracker-1.0.1-<arch>.dmg` со страницы [Releases](https://github.com/mysechka/MedTracker/releases).
2. Откройте DMG-образ и перетащите **MedTracker.app** в папку **Applications** (`/Applications`).
3. Запустите приложение через Spotlight или Launchpad.
4. *Примечание*: При первом запуске на macOS без сертификата Apple Developer нажмите `Правый клик → Открыть` (Open).

### Android (.APK)
1. Скачайте установочный файл `MedTracker-1.0.1.apk` со страницы [Releases](https://github.com/mysechka/MedTracker/releases).
2. Разрешите установку из неизвестных источников в настройках безопасности Android.
3. Установите APK вручную или через ADB:
   ```bash
   adb install -r artifacts/android/MedTracker-1.0.1.apk
   ```

---

## 🏗 Архитектура решения

Проект построен строго по канонам **Clean Architecture**:

```
src/Med.Domain/          Чистый C#, доменные сущности, правила расписания, ноль внешних зависимостей
src/Med.Application/     Интерфейсы сервисов (INotificationService, IAuthService), Use Cases, Abstractions
src/Med.Infrastructure/  Supabase SDK, локальная SQLite БД, локальные уведомления ОС, маппинг
src/Med.Presentation/    ViewModels (CommunityToolkit.Mvvm), навигация, стейт
src/Med.Ui/              Avalonia Views, ViewLocator, диспетчер UI
src/Med.Desktop/         Head-проект macOS (.app, DMG, иконки)
src/Med.Android/         Head-проект Android (Activity, нативные уведомления AlarmManager)
tests/                   xUnit v3 + FluentAssertions + BenchmarkDotNet
supabase/migrations/     PostgreSQL DDL, RLS политики, pg_cron
supabase/functions/      Edge Functions на Deno
```

---

## ⏰ Часовой пояс и время

Пользователь не выбирает сложную IANA-зону вручную: он задаёт целое смещение в часах относительно Москвы (`0` — МСК, `+4` — Новосибирск, `-2` — Калининград). 

Смещение приводится к фиксированной зоне (`Europe/Moscow`, `Etc/GMT±N`, `Etc/UTC`) и хранится в `profiles.time_zone_id`. Констрейнты базы данных гарантируют целостность.

---

## 🚀 Быстрый старт для разработчиков

Скрипты автоматической оркестрации проверяют зависимости (.NET 10, Docker, Supabase CLI), поднимают локальный Supabase, накатывают сид-данные и запускают десктопный клиент:

```bash
# macOS / Linux (Bash / Zsh)
./run-macos-linux.sh dev       # или 'make dev'
./run-macos-linux.sh watch     # с горячей перезагрузкой
./run-macos-linux.sh test      # прогон xUnit v3 тестов
./run-macos-linux.sh stop      # остановка локального Supabase
```

```powershell
# Windows (PowerShell)
.\run-windows.ps1 dev          # полный запуск окружения и клиента
.\run-windows.ps1 watch        # запуск в режиме watch
.\run-windows.ps1 test         # прогон тестов
.\run-windows.ps1 stop         # остановка Supabase
```

---

## 🛠 Сборка из исходного кода

### Требования
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- .NET Workload `android` (`dotnet workload install android`)
- Android SDK + JDK 17+ (для Android APK)
- macOS + Xcode Command Line Tools (для сборки macOS бандла и DMG)

### Команды быстрой сборки

```bash
# Сборка всего решения
dotnet build MedTracker.slnx

# Прогон всех unit-тестов
dotnet test MedTracker.slnx

# Сборка macOS DMG инсталлятора
build/macos/make-dmg.sh

# Сборка Android APK
build/android/make-apk.sh

# Запуск тестов производительности (BenchmarkDotNet)
dotnet run -c Release --project tests/Med.Performance.Tests
```

---

## 🔐 Безопасность

- В клиентском приложении (Desktop/Android) разрешён **исключительно Anon Key**.
- Встроен защитный механизм [SupabaseOptionsValidator.cs](src/Med.Infrastructure/Configuration/SupabaseOptionsValidator.cs), аварийно блокирующий запуск клиента с сервисным ключом `service_role`.
- Подробный отчёт аудита безопасности доступен в [docs/SECURITY_AUDIT.md](docs/SECURITY_AUDIT.md).

---

## 📄 Документация и релизы

- 📝 [История релизов и статус фичей (RELEASE_NOTES.md)](RELEASE_NOTES.md)
- 🔒 [Отчёт аудита безопасности (docs/SECURITY_AUDIT.md)](docs/SECURITY_AUDIT.md)
- 🍏 [Сборка macOS (docs/build/macos.md)](docs/build/macos.md)
- 🤖 [Сборка Android (docs/build/android.md)](docs/build/android.md)

---

## 📜 Лицензия

Проект распространяется под лицензией **GNU GPL v3 с дополнительными условиями (Commons Clause)**. Подробные условия и ограничения на коммерческую продажу указаны в файле [LICENSE.md](LICENSE.md).
