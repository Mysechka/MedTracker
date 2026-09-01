#!/usr/bin/env zsh
# Быстрый dev-бандл из Debug-сборки: иконка Dock из AppIcon.icns, без self-contained publish.
#
# Использование:
#   build/macos/run-dev.sh

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
PROJECT="$REPO_ROOT/src/Med.Desktop/Med.Desktop.csproj"
APP_NAME="MedTracker"
BUNDLE_ID="com.mysechka.medtracker"
BIN_DIR="$REPO_ROOT/src/Med.Desktop/bin/Debug/net10.0"
APP_DIR="$REPO_ROOT/artifacts/macos/dev/$APP_NAME.app"
ICON_SRC="$REPO_ROOT/src/Med.Ui/Assets/app-icon.png"
ICNS="$REPO_ROOT/build/macos/AppIcon.icns"

echo "==> Сборка Debug"
dotnet build "$PROJECT" --configuration Debug --nologo -v q

if [[ ! -x "$BIN_DIR/$APP_NAME" ]]; then
  echo "Не найден $BIN_DIR/$APP_NAME" >&2
  exit 1
fi

echo "==> Иконка Dock (squircle + AppIcon.icns)"
"$REPO_ROOT/build/macos/build-app-icon.sh"

echo "==> Бандл $APP_DIR"
rm -rf "$APP_DIR"
mkdir -p "$APP_DIR/Contents/MacOS" "$APP_DIR/Contents/Resources"
cp -R "$BIN_DIR/." "$APP_DIR/Contents/MacOS/"
cp "$ICNS" "$APP_DIR/Contents/Resources/AppIcon.icns"
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
  <string>0.1.0</string>
  <key>CFBundleVersion</key>
  <string>1</string>
  <key>LSMinimumSystemVersion</key>
  <string>12.0</string>
  <key>NSHighResolutionCapable</key>
  <true/>
  <key>CFBundleIconFile</key>
  <string>AppIcon</string>
</dict>
</plist>
PLIST

codesign --force --deep --sign - "$APP_DIR" 2>/dev/null || true

echo "==> Запуск"
open "$APP_DIR"
