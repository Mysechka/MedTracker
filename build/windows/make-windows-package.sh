#!/usr/bin/env bash
# Сборка и упаковка релизного self-contained пакета MedTracker для Windows (win-x64).
#
# Скрипт может запускаться как на macOS/Linux (кросс-публикация через .NET SDK), так и на Windows.
# Результат: artifacts/windows/MedTracker-<version>-<rid>.zip
#
# Использование:
#   build/windows/make-windows-package.sh
#   RID=win-arm64 build/windows/make-windows-package.sh
#   CONFIGURATION=Debug build/windows/make-windows-package.sh

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
PROJECT="$REPO_ROOT/src/Med.Desktop/Med.Desktop.csproj"

CONFIGURATION="${CONFIGURATION:-Release}"
RID="${RID:-win-x64}"
APP_NAME="MedTracker"
VERSION="${VERSION:-1.0.1}"

OUT_DIR="$REPO_ROOT/artifacts/windows/$RID"
PACKAGE_DIR="$OUT_DIR/$APP_NAME"
ZIP_NAME="${APP_NAME}-${VERSION}-${RID}.zip"
ZIP_PATH="$REPO_ROOT/artifacts/windows/$ZIP_NAME"

echo "==> Публикация MedTracker Windows ($CONFIGURATION / $RID)..."
rm -rf "$PACKAGE_DIR" "$ZIP_PATH"
mkdir -p "$PACKAGE_DIR"

dotnet publish "$PROJECT" \
  --configuration "$CONFIGURATION" \
  --runtime "$RID" \
  --self-contained true \
  --output "$PACKAGE_DIR" \
  -p:PublishTrimmed=false \
  -p:PublishSingleFile=false \
  -p:DebugType=none \
  -p:DebugSymbols=false \
  -p:UseSystemResourceKeys=true \
  -p:EventSourceSupport=false \
  -p:HttpActivityPropagationSupport=false \
  -p:MetadataUpdaterSupport=false

if [[ ! -f "$PACKAGE_DIR/$APP_NAME.exe" ]]; then
  echo "❌ Ошибка: Не найден исполняемый файл $PACKAGE_DIR/$APP_NAME.exe" >&2
  exit 1
fi

if [[ ! -f "$PACKAGE_DIR/Med.Ui.dll" ]]; then
  echo "❌ Ошибка: Не найдена сборка пользовательского интерфейса (фронтенд) $PACKAGE_DIR/Med.Ui.dll" >&2
  exit 1
fi

echo "==> Очистка отладочных артефактов и локальных конфигов..."
rm -f "$PACKAGE_DIR/appsettings.Local.json" \
      "$PACKAGE_DIR/createdump.exe" \
      "$PACKAGE_DIR/"*.pdb \
      "$PACKAGE_DIR/"*.xml

echo "==> Упаковка в ZIP-архив: $ZIP_PATH..."
(cd "$OUT_DIR" && zip -r -9 "$ZIP_PATH" "$APP_NAME")

# Копируем в общую папку дистрибутивов, если она существует
if [[ -d "$REPO_ROOT/artifacts/dist" ]]; then
  cp "$ZIP_PATH" "$REPO_ROOT/artifacts/dist/"
fi

SIZE="$(du -sh "$ZIP_PATH" | cut -f1)"
echo "==> Готово: $ZIP_PATH ($SIZE)"
