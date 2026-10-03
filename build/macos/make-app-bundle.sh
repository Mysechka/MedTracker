#!/usr/bin/env bash
# Сборка macOS-бандла MedTracker.app из head-проекта Med.Desktop.
#
# .NET SDK не умеет делать .app сам: он выдаёт каталог с исполняемым файлом.
# Скрипт публикует self-contained сборку и раскладывает её в структуру бандла.
#
# Использование:
#   build/macos/make-app-bundle.sh                # архитектура текущей машины
#   RID=osx-x64 build/macos/make-app-bundle.sh    # сборка под Intel
#   CONFIGURATION=Debug build/macos/make-app-bundle.sh

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
PROJECT="$REPO_ROOT/src/Med.Desktop/Med.Desktop.csproj"

CONFIGURATION="${CONFIGURATION:-Release}"
if [[ -z "${RID:-}" ]]; then
  case "$(uname -m)" in
    arm64) RID="osx-arm64" ;;
    x86_64) RID="osx-x64" ;;
    *) echo "Неизвестная архитектура $(uname -m): задайте RID вручную." >&2; exit 1 ;;
  esac
fi

APP_NAME="MedTracker"
BUNDLE_ID="com.mysechka.medtracker"
# Версии держим здесь же, чтобы Info.plist не расходился с csproj незаметно.
SHORT_VERSION="1.0.1"
BUILD_VERSION="1"
MIN_MACOS="12.0"

OUT_DIR="$REPO_ROOT/artifacts/macos/$RID"
PUBLISH_DIR="$OUT_DIR/publish"
APP_DIR="$OUT_DIR/$APP_NAME.app"
ICON_SRC="$REPO_ROOT/src/Med.Ui/Assets/app-icon.png"
ICNS="$REPO_ROOT/build/macos/AppIcon.icns"

build_macos_icon() {
  "$REPO_ROOT/build/macos/build-app-icon.sh"
}

build_macos_icon

echo "==> Публикация $CONFIGURATION / $RID"
rm -rf "$PUBLISH_DIR" "$APP_DIR"
# self-contained: на чужой машине не нужен установленный .NET.
# Тримминг НЕ включаем: Avalonia и Supabase-клиент используют рефлексию.
dotnet publish "$PROJECT" \
  --configuration "$CONFIGURATION" \
  --runtime "$RID" \
  --self-contained true \
  --output "$PUBLISH_DIR" \
  -p:PublishTrimmed=false \
  -p:PublishSingleFile=false

if [[ ! -x "$PUBLISH_DIR/$APP_NAME" ]]; then
  echo "Не найден исполняемый файл $PUBLISH_DIR/$APP_NAME" >&2
  exit 1
fi

echo "==> Сборка бандла $APP_DIR"
mkdir -p "$APP_DIR/Contents/MacOS" "$APP_DIR/Contents/Resources"
cp -R "$PUBLISH_DIR/." "$APP_DIR/Contents/MacOS/"
cp "$REPO_ROOT/build/macos/AppIcon.icns" "$APP_DIR/Contents/Resources/AppIcon.icns"

# appsettings.Local.json — локальные секреты разработчика, в бандл не кладём.
rm -f "$APP_DIR/Contents/MacOS/appsettings.Local.json"

cat > "$APP_DIR/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleName</key>
  <string>$APP_NAME</string>
  <key>CFBundleDisplayName</key>
  <string>$APP_NAME</string>
  <key>CFBundleIdentifier</key>
  <string>$BUNDLE_ID</string>
  <key>CFBundleExecutable</key>
  <string>$APP_NAME</string>
  <key>CFBundlePackageType</key>
  <string>APPL</string>
  <key>CFBundleShortVersionString</key>
  <string>$SHORT_VERSION</string>
  <key>CFBundleVersion</key>
  <string>$BUILD_VERSION</string>
  <key>LSMinimumSystemVersion</key>
  <string>$MIN_MACOS</string>
  <key>NSHighResolutionCapable</key>
  <true/>
  <key>CFBundleIconFile</key>
  <string>AppIcon</string>
</dict>
</plist>
PLIST

echo "==> Ad-hoc подпись"
# Валидного Developer ID нет, поэтому подпись ad-hoc: приложение запускается
# локально, но не переносится через интернет без нотаризации.
#
# --deep для распространения Apple не рекомендует, но здесь он единственный
# рабочий вариант: apphost от .NET уже подписан ad-hoc с идентификатором
# «apphost», и без --deep подпись бандла не перекрывает вложенные native-библиотеки.
codesign --force --deep --sign - "$APP_DIR"

# Проверяем без --deep: управляемые .dll не Mach-O, и deep-проверка
# объявляет их «неподписанным кодом», хотя подписывать их не нужно.
codesign --verify --strict "$APP_DIR"

SIZE="$(du -sh "$APP_DIR" | cut -f1)"
echo "==> Готово: $APP_DIR ($SIZE)"
echo "    Запуск: open \"$APP_DIR\""
