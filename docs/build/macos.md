# Сборка и запуск на macOS

Head-проект — `src/Med.Desktop`. Целевая архитектура — Apple Silicon (`osx-arm64`),
Intel собирается тем же скриптом через `RID=osx-x64`.

## Что нужно установить

| Инструмент | Версия | Проверка |
|---|---|---|
| .NET SDK | 10.0.400 (см. `global.json`) | `dotnet --version` |
| Xcode Command Line Tools | любая актуальная | `xcode-select -p` |

Полный Xcode не нужен: `codesign` и `xcrun` входят в Command Line Tools.
Если `xcode-select -p` ничего не возвращает — `xcode-select --install`.

## Быстрый запуск для разработки

```bash
dotnet run --project src/Med.Desktop
```

Так удобно проверять логику: не нужен ни бандл, ни подпись. Конфигурация читается
из `appsettings.json` рядом с бинарником, из `appsettings.Local.json` (в `.gitignore`)
и из переменных окружения с префиксом `MEDTRACKER_`.

## Сборка бандла

```bash
build/macos/make-app-bundle.sh
```

Результат — `artifacts/macos/<rid>/MedTracker.app` (около 115 МБ, self-contained).
Переменные скрипта:

| Переменная | По умолчанию | Смысл |
|---|---|---|
| `RID` | по `uname -m` | `osx-arm64` или `osx-x64` |
| `CONFIGURATION` | `Release` | конфигурация сборки |

Запуск: `open artifacts/macos/osx-arm64/MedTracker.app`.

Что делает скрипт: публикует self-contained сборку, раскладывает её в
`Contents/MacOS`, генерирует `Info.plist`, ставит ad-hoc подпись и проверяет её.

## Подводные камни

**`.app` не собирается сам.** `dotnet publish` даёт каталог с исполняемым файлом,
а не бандл. Структуру `Contents/MacOS` + `Info.plist` создаёт скрипт. Без
`Info.plist` с корректным `CFBundleExecutable` Finder бандл не запустит.

**Ad-hoc подпись вместо Developer ID.** Валидного сертификата нет
(`security find-identity -v -p codesigning` → 0 identities), поэтому подпись
ad-hoc. Локально приложение запускается нормально. Но если такой `.app` передать
через интернет, macOS поставит на него карантин, и Gatekeeper откажет: нужно снять
карантин (`xattr -dr com.apple.quarantine MedTracker.app`) либо открыть через
контекстное меню «Открыть». Полноценное решение — Apple Developer Program,
Developer ID и нотаризация; это выходит за рамки текущей фазы.

**Подпись только с `--deep`.** apphost от .NET уже подписан ad-hoc с
идентификатором `apphost`. Без `--deep` подпись бандла не перекрывает вложенные
native-библиотеки, и получается бандл со `Sealed Resources=none`, который не
проходит `codesign --verify`. Apple не рекомендует `--deep` для распространения,
но для ad-hoc это единственный работающий вариант.

**Проверять подпись без `--deep`.** `codesign --verify --deep --strict` падает на
управляемых `.dll`: они не Mach-O, и deep-проверка объявляет их «неподписанным
кодом». Проверка бандла — `codesign --verify --strict`.

**Тримминг ломает приложение.** `PublishTrimmed=true` вырезает типы, которые
Avalonia и клиент Supabase достают рефлексией. Скрипт явно выставляет
`PublishTrimmed=false`. То же относится к `PublishSingleFile`: Avalonia работает,
но диагностика становится сложнее, поэтому в скрипте он выключен.

**`InvariantGlobalization` включать нельзя.** Часовой пояс профиля — фиксированная
IANA-зона (`Europe/Moscow`, `Etc/GMT±N`). В invariant-режиме
`TimeZoneInfo.FindSystemTimeZoneById` не находит зоны, и приложение падает на
загрузке профиля. В `Directory.Build.props` стоит `false`, менять не нужно.

**Архитектура должна совпадать.** `osx-arm64`-бандл на Intel-маке не запустится
(Rosetta работает только в обратную сторону). Universal-бандл собирается двумя
публикациями и `lipo` для исполняемого файла и всех `.dylib`; сейчас это не нужно.

**`appsettings.Local.json` не попадает в бандл.** Скрипт удаляет его из
`Contents/MacOS` после копирования: это локальные настройки разработчика.
В бандле остаётся `appsettings.json` с плейсхолдерами — реальный проект Supabase
задаётся переменными `MEDTRACKER_*` или локальным файлом рядом с бинарником.
