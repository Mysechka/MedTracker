#!/usr/bin/env bash
# Сборка Android APK MedTracker из head-проекта Med.Android.
#
# Скрипт собирает APK и копирует его в artifacts/android/.
# Поддерживает как обычную Release-сборку (ad-hoc / debug-signed), так и подпись
# пользовательским keystore через переменные окружения.
#
# Использование:
#   build/android/make-apk.sh                    # сборка Release APK
#   CONFIGURATION=Debug build/android/make-apk.sh
#
# Для релизной подписи задайте переменные:
#   export MEDTRACKER_KEYSTORE=~/keys/medtracker-release.keystore
#   export MEDTRACKER_KEYSTORE_PASS=...
#   export MEDTRACKER_KEY_ALIAS=medtracker
#   export MEDTRACKER_KEY_PASS=...
#   build/android/make-apk.sh

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
PROJECT="$REPO_ROOT/src/Med.Android/Med.Android.csproj"
CONFIGURATION="${CONFIGURATION:-Release}"

OUT_DIR="$REPO_ROOT/artifacts/android"
mkdir -p "$OUT_DIR"

echo "==> Сборка Android APK ($CONFIGURATION)"

SIGN_ARGS=()
if [[ -n "${MEDTRACKER_KEYSTORE:-}" && -f "${MEDTRACKER_KEYSTORE}" ]]; then
  echo "==> Использование пользовательского хранилища ключей: $MEDTRACKER_KEYSTORE"
  SIGN_ARGS=(
    "-p:AndroidKeyStore=true"
    "-p:AndroidSigningKeyStore=${MEDTRACKER_KEYSTORE}"
    "-p:AndroidSigningStorePass=${MEDTRACKER_KEYSTORE_PASS:-}"
    "-p:AndroidSigningKeyAlias=${MEDTRACKER_KEY_ALIAS:-medtracker}"
    "-p:AndroidSigningKeyPass=${MEDTRACKER_KEY_PASS:-}"
  )
fi

dotnet publish "$PROJECT" \
  --configuration "$CONFIGURATION" \
  ${SIGN_ARGS[@]+"${SIGN_ARGS[@]}"}

# Ищем полученный APK (приоритет -Signed.apk, затем обычный .apk)
PUBLISH_DIR="$REPO_ROOT/src/Med.Android/bin/$CONFIGURATION/net10.0-android/publish"
BIN_DIR="$REPO_ROOT/src/Med.Android/bin/$CONFIGURATION/net10.0-android"

APK_FILE=""
if compgen -G "$PUBLISH_DIR/*-Signed.apk" > /dev/null; then
  APK_FILE="$(ls -t "$PUBLISH_DIR"/*-Signed.apk | head -n 1)"
elif compgen -G "$PUBLISH_DIR/*.apk" > /dev/null; then
  APK_FILE="$(ls -t "$PUBLISH_DIR"/*.apk | head -n 1)"
elif compgen -G "$BIN_DIR/*-Signed.apk" > /dev/null; then
  APK_FILE="$(ls -t "$BIN_DIR"/*-Signed.apk | head -n 1)"
elif compgen -G "$BIN_DIR/*.apk" > /dev/null; then
  APK_FILE="$(ls -t "$BIN_DIR"/*.apk | head -n 1)"
fi

if [[ -z "$APK_FILE" || ! -f "$APK_FILE" ]]; then
  echo "Ошибка: APK-файл не найден после публикации." >&2
  exit 1
fi

DEST_APK="$OUT_DIR/$(basename "$APK_FILE")"
cp "$APK_FILE" "$DEST_APK"

SIZE="$(du -sh "$DEST_APK" | cut -f1)"
echo "==> Готово: $DEST_APK ($SIZE)"
echo "    Установка на устройство: adb install -r \"$DEST_APK\""
