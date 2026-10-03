#!/usr/bin/env bash
# ==============================================================================
# MedTracker macOS — Скрипт автоматической установки в 1 команду
# Снимает блокировку Gatekeeper (com.apple.quarantine) и запускает приложение
#
# Использование:
#   curl -sSL https://raw.githubusercontent.com/mysechka/MedTracker/main/install.sh | bash
#   или
#   ./install.sh
# ==============================================================================

set -euo pipefail

BOLD='\033[1m'
GREEN='\033[0;32m'
BLUE='\033[0;34m'
YELLOW='\033[1;33m'
RED='\033[0;31m'
NC='\033[0m'

echo -e "${BLUE}${BOLD}"
echo "╔═══════════════════════════════════════════════════════════════╗"
echo "║          MedTracker macOS — Автоматическая установка          ║"
echo "╚═══════════════════════════════════════════════════════════════╝"
echo -e "${NC}"

# 1. Проверка операционной системы
if [[ "$(uname -s)" != "Darwin" ]]; then
    echo -e "${RED}[ERROR] Данный установщик предназначен только для macOS!${NC}"
    exit 1
fi

# 2. Определение архитектуры процессора
ARCH="$(uname -m)"
echo -e "${BLUE}▶${NC} Архитектура: ${BOLD}${ARCH}${NC}"

RELEASE_TAG="v1.0.1"
REPO="mysechka/MedTracker"

# 3. Поиск DMG: локально или скачивание с GitHub
DMG_FILE=""

# Проверяем локальные пути (если скрипт запускается из репозитория или рядом)
LOCAL_CANDIDATES=(
    "./artifacts/macos/MedTracker-1.0.1.dmg"
    "./MedTracker-1.0.1.dmg"
    "$HOME/Downloads/MedTracker-1.0.1.dmg"
    "$HOME/Downloads/MedTracker.dmg"
)

for cand in "${LOCAL_CANDIDATES[@]}"; do
    if [[ -f "$cand" ]]; then
        DMG_FILE="$cand"
        echo -e "${GREEN}✓${NC} Найден локальный DMG: ${BOLD}${cand}${NC}"
        break
    fi
done

TEMP_DIR=""
if [[ -z "$DMG_FILE" ]]; then
    TEMP_DIR="$(mktemp -d /tmp/medtracker_install.XXXXXX)"
    DMG_FILE="${TEMP_DIR}/MedTracker-1.0.1.dmg"
    DOWNLOAD_URL="https://github.com/${REPO}/releases/download/${RELEASE_TAG}/MedTracker-1.0.1-osx-arm64.dmg"
    
    echo -e "${BLUE}▶${NC} Загрузка релизного пакета с GitHub: ${DOWNLOAD_URL}..."
    curl -fSL --progress-bar "$DOWNLOAD_URL" -o "$DMG_FILE"
    echo -e "${GREEN}✓${NC} Загрузка успешно завершена."
fi

# 4. Закрытие работающего экземпляра (если запущен)
if pgrep -x "MedTracker" > /dev/null 2>&1; then
    echo -e "${YELLOW}▶${NC} Завершение работающего процесса MedTracker..."
    pkill -x "MedTracker" || true
    sleep 1
fi

# 5. Монтирование DMG
MOUNT_DIR="$(mktemp -d /tmp/medtracker_mount.XXXXXX)"
echo -e "${BLUE}▶${NC} Монтирование образа диска..."
hdiutil attach -nobrowse -readonly "$DMG_FILE" -mountpoint "$MOUNT_DIR" -quiet

cleanup() {
    if [[ -d "$MOUNT_DIR" ]]; then
        hdiutil detach "$MOUNT_DIR" -force -quiet > /dev/null 2>&1 || true
        rm -rf "$MOUNT_DIR" 2>/dev/null || true
    fi
    if [[ -n "$TEMP_DIR" && -d "$TEMP_DIR" ]]; then
        rm -rf "$TEMP_DIR" 2>/dev/null || true
    fi
}
trap cleanup EXIT

# 6. Копирование в /Applications
SOURCE_APP="${MOUNT_DIR}/MedTracker.app"
DEST_APP="/Applications/MedTracker.app"

if [[ ! -d "$SOURCE_APP" ]]; then
    echo -e "${RED}[ERROR] MedTracker.app не найден внутри DMG!${NC}"
    exit 1
fi

echo -e "${BLUE}▶${NC} Копирование в ${DEST_APP}..."
rm -rf "$DEST_APP"
cp -R "$SOURCE_APP" "$DEST_APP"

# 7. Снятие блокировки Gatekeeper (quarantine)
echo -e "${BLUE}▶${NC} Снятие атрибута карантина Gatekeeper (xattr -cr)..."
xattr -cr "$DEST_APP"

# 8. Локальная переподпись ad-hoc
echo -e "${BLUE}▶${NC} Применение локальной ad-hoc подписи..."
codesign --force --deep -s - "$DEST_APP" > /dev/null 2>&1 || true

# 9. Успешный запуск
echo -e "${GREEN}${BOLD}✓ MedTracker успешно установлен в /Applications!${NC}"
echo -e "${BLUE}▶${NC} Запуск приложения..."
open "$DEST_APP"

echo -e "${GREEN}═══════════════════════════════════════════════════════════════${NC}"
echo -e "${GREEN}${BOLD}Готово! Приложение запущено без предупреждений Gatekeeper.${NC}"
echo -e "${GREEN}═══════════════════════════════════════════════════════════════${NC}"
