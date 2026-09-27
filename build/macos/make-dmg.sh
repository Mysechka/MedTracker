#!/usr/bin/env bash
# Создание DMG установщика для macOS (MedTracker-<version>-<rid>.dmg).
#
# Скрипт:
# 1. Проверяет/собирает .app бандл с помощью make-app-bundle.sh
# 2. Создаёт временный каталог со структурой DMG (MedTracker.app + симлинк на /Applications)
# 3. Упаковывает DMG с помощью hdiutil create
# 4. Выполняет ad-hoc подпись DMG
# 5. Результат: artifacts/macos/MedTracker-<version>-<rid>.dmg
#
# Использование:
#   build/macos/make-dmg.sh
#   VERSION=1.0.0 build/macos/make-dmg.sh
#   RID=osx-arm64 build/macos/make-dmg.sh

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
APP_NAME="MedTracker"
VERSION="${VERSION:-1.0.0}"

if [[ -z "${RID:-}" ]]; then
  case "$(uname -m)" in
    arm64) RID="osx-arm64" ;;
    x86_64) RID="osx-x64" ;;
    *) echo "Неизвестная архитектура $(uname -m): задайте RID вручную." >&2; exit 1 ;;
  esac
fi

APP_BUNDLE="$REPO_ROOT/artifacts/macos/$RID/$APP_NAME.app"
OUT_DIR="$REPO_ROOT/artifacts/macos"
DMG_NAME="${APP_NAME}-${VERSION}-${RID}.dmg"
DMG_PATH="$OUT_DIR/$DMG_NAME"

echo "==> Подготовка .app бандла..."
"$REPO_ROOT/build/macos/make-app-bundle.sh"


echo "==> Создание структуры DMG..."
TMP_DMG_DIR="$(mktemp -d /tmp/medtracker_dmg.XXXXXX)"
cleanup() {
  rm -rf "$TMP_DMG_DIR"
}
trap cleanup EXIT

cp -R "$APP_BUNDLE" "$TMP_DMG_DIR/"
ln -s /Applications "$TMP_DMG_DIR/Applications"

echo "==> Упаковка DMG: $DMG_PATH"
rm -f "$DMG_PATH"
mkdir -p "$OUT_DIR"

hdiutil create \
  -volname "$APP_NAME $VERSION" \
  -srcfolder "$TMP_DMG_DIR" \
  -ov \
  -format UDZO \
  "$DMG_PATH"

echo "==> Ad-hoc подпись DMG..."
codesign --force --sign - "$DMG_PATH" || true

SIZE="$(du -sh "$DMG_PATH" | cut -f1)"
echo "==> Готово: $DMG_PATH ($SIZE)"
