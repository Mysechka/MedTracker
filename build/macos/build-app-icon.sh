#!/usr/bin/env zsh
# Рендер macOS-стиля app-icon.png и сборка AppIcon.icns из него.
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
ICON_SRC="$REPO_ROOT/src/Med.Ui/Assets/app-icon.png"
ICONSET="$REPO_ROOT/build/macos/AppIcon.iconset"
ICNS="$REPO_ROOT/build/macos/AppIcon.icns"
RENDER="$REPO_ROOT/build/macos/render-app-icon.swift"

echo "==> Рендер app-icon.png (macOS squircle)"
swift "$RENDER"
sips -z 1024 1024 "$ICON_SRC" --out "$ICON_SRC" >/dev/null

if [[ ! -f "$ICON_SRC" ]]; then
  echo "Не найден $ICON_SRC" >&2
  exit 1
fi

echo "==> Генерация AppIcon.icns"
rm -rf "$ICONSET" "$ICNS"
mkdir -p "$ICONSET"
local name size
for name size in \
  icon_16x16.png 16 \
  icon_16x16@2x.png 32 \
  icon_32x32.png 32 \
  icon_32x32@2x.png 64 \
  icon_128x128.png 128 \
  icon_128x128@2x.png 256 \
  icon_256x256.png 256 \
  icon_256x256@2x.png 512 \
  icon_512x512.png 512 \
  icon_512x512@2x.png 1024; do
  sips -s format png -z "$size" "$size" "$ICON_SRC" --out "$ICONSET/$name" >/dev/null
done
iconutil -c icns "$ICONSET" -o "$ICNS"

echo "==> AppIcon.icns → $ICNS"
