#!/usr/bin/env bash
set -eo pipefail

# Цвета для вывода в терминал
RED='\033[0;31m'
GREEN='\033[0;32m'
BLUE='\033[0;34m'
YELLOW='\033[1;33m'
BOLD='\033[1m'
NC='\033[0m' # No Color

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

command_exists() {
  command -v "$1" >/dev/null 2>&1
}

# -----------------------------------------------------------------------------
# 1. Pre-flight checks
# -----------------------------------------------------------------------------
preflight_checks() {
  echo -e "${BLUE}${BOLD}==> Шаг 1: Проверка установленного окружения (Pre-flight)...${NC}"

  # Проверка .NET SDK
  if ! command_exists dotnet; then
    echo -e "${RED}❌ .NET SDK не найден в PATH.${NC}"
    echo -e "   Установите .NET SDK 10.0+: https://dotnet.microsoft.com/download/dotnet/10.0"
    exit 1
  fi

  local dotnet_ver
  dotnet_ver="$(dotnet --version 2>/dev/null || echo "0")"
  local dotnet_major
  dotnet_major="$(echo "$dotnet_ver" | cut -d'.' -f1)"

  if [ "$dotnet_major" -lt 10 ]; then
    echo -e "${RED}❌ Требуется .NET SDK 10.0+, обнаружена версия: ${dotnet_ver}.${NC}"
    echo -e "   Установите актуальный .NET SDK 10: https://dotnet.microsoft.com/download/dotnet/10.0"
    exit 1
  fi
  echo -e "  ✓ .NET SDK: ${GREEN}${dotnet_ver}${NC}"

  # Проверка Docker
  if ! command_exists docker; then
    echo -e "${RED}❌ Docker не установлен или не найден в PATH.${NC}"
    echo -e "   Установите Docker Desktop: https://www.docker.com/products/docker-desktop"
    exit 1
  fi

  # Проверка запущенного Docker daemon
  if ! docker info >/dev/null 2>&1; then
    echo -e "  ${YELLOW}⚠️  Docker daemon не отвечает. Пробую запустить Docker Desktop...${NC}"
    if [[ "$OSTYPE" == "darwin"* ]]; then
      open -a Docker >/dev/null 2>&1 || true
    elif command_exists systemctl; then
      sudo systemctl start docker >/dev/null 2>&1 || true
    fi

    # Ожидание готовности демона до 30 секунд
    local ready=false
    for i in {1..30}; do
      if docker info >/dev/null 2>&1; then
        ready=true
        break
      fi
      sleep 1
    done

    if [ "$ready" != "true" ]; then
      echo -e "${RED}❌ Демон Docker не запущен.${NC}"
      echo -e "   Запустите Docker Desktop вручную и повторите запуск."
      exit 1
    fi
  fi
  echo -e "  ✓ Docker daemon: ${GREEN}готов к работе${NC}"

  # Проверка Supabase CLI
  if ! command_exists supabase; then
    echo -e "${RED}❌ Supabase CLI не найден в PATH.${NC}"
    echo -e "   Установите Supabase CLI:"
    echo -e "     macOS/Linux: brew install supabase/tap/supabase"
    echo -e "     Windows: scoop bucket add supabase https://github.com/supabase/scoop-bucket.git && scoop install supabase"
    echo -e "     Подробнее: https://supabase.com/docs/guides/local-development/cli/getting-started"
    exit 1
  fi
  local supabase_ver
  supabase_ver="$(supabase --version 2>/dev/null | head -n 1 || echo "unknown")"
  echo -e "  ✓ Supabase CLI: ${GREEN}${supabase_ver}${NC}"
}

# -----------------------------------------------------------------------------
# 2. Запуск Supabase и применение миграций / seed-данных
# -----------------------------------------------------------------------------
start_supabase() {
  echo -e "\n${BLUE}${BOLD}==> Шаг 2: Запуск локального Supabase и применение seed-данных...${NC}"

  # Проверяем, запущен ли стек
  if ! supabase status >/dev/null 2>&1; then
    echo -e "  Запуск контейнеров Supabase..."
    supabase start
  else
    echo -e "  Контейнеры Supabase уже запущены."
  fi

  # Накат свежих миграций и seed.sql
  echo -e "  Сброс БД и применение сид-данных (supabase db reset)..."
  supabase db reset
}

# -----------------------------------------------------------------------------
# 3. Генерация конфигурации appsettings.Development.json
# -----------------------------------------------------------------------------
generate_config() {
  echo -e "\n${BLUE}${BOLD}==> Шаг 3: Автогенерация конфигурации подключения к Supabase...${NC}"

  local status_json
  status_json="$(supabase status -o json 2>/dev/null)"

  local api_url
  local anon_key

  # Извлекаем ключи через python/node/grep
  if command_exists python3; then
    api_url=$(python3 -c "import sys, json; data=json.loads(sys.stdin.read()); print(data.get('API_URL','http://127.0.0.1:54321'))" <<< "$status_json")
    anon_key=$(python3 -c "import sys, json; data=json.loads(sys.stdin.read()); print(data.get('ANON_KEY',''))" <<< "$status_json")
  elif command_exists node; then
    api_url=$(node -e "const fs=require('fs'); const d=JSON.parse(fs.readFileSync(0,'utf-8')); console.log(d.API_URL||'http://127.0.0.1:54321');" <<< "$status_json")
    anon_key=$(node -e "const fs=require('fs'); const d=JSON.parse(fs.readFileSync(0,'utf-8')); console.log(d.ANON_KEY||'');" <<< "$status_json")
  else
    api_url="$(echo "$status_json" | grep -o '"API_URL": "[^"]*' | cut -d'"' -f4)"
    anon_key="$(echo "$status_json" | grep -o '"ANON_KEY": "[^"]*' | cut -d'"' -f4)"
  fi

  if [ -z "$api_url" ] || [ -z "$anon_key" ]; then
    echo -e "${RED}❌ Не удалось извлечь API_URL или ANON_KEY из supabase status.${NC}"
    exit 1
  fi

  local config_file="src/Med.Desktop/appsettings.Development.json"
  cat > "$config_file" <<EOF
{
  "Supabase": {
    "Url": "${api_url}",
    "AnonKey": "${anon_key}",
    "SignedUrlTtlSeconds": 300
  }
}
EOF

  # Также обновляем appsettings.Local.json для совместимости
  cp "$config_file" "src/Med.Desktop/appsettings.Local.json"

  echo -e "  ✓ Конфиг сформирован: ${GREEN}${config_file}${NC}"
  echo -e "  ✓ Supabase Studio:    ${YELLOW}http://127.0.0.1:54323${NC}"
  echo -e "  ✓ Inbucket (Email):   ${YELLOW}http://127.0.0.1:54324${NC}"
  echo -e "  ✓ Тестовый логин:     ${GREEN}dev@medtracker.local${NC} / пароль: ${GREEN}password123${NC}"
}

# -----------------------------------------------------------------------------
# 4. Запуск десктопного клиента
# -----------------------------------------------------------------------------
run_desktop() {
  local mode="${1:-run}"
  echo -e "\n${BLUE}${BOLD}==> Шаг 4: Запуск Desktop приложения (Avalonia UI)...${NC}"

  if [ "$mode" == "watch" ]; then
    echo -e "  Запуск в режиме Hot-Reload (dotnet watch)..."
    dotnet watch --project src/Med.Desktop
  else
    dotnet run --project src/Med.Desktop
  fi
}

# -----------------------------------------------------------------------------
# Тесты
# -----------------------------------------------------------------------------
run_tests() {
  preflight_checks
  echo -e "\n${BLUE}${BOLD}==> Запуск тестов решения MedTracker.slnx (xUnit v3)...${NC}"
  dotnet test MedTracker.slnx --logger "console;verbosity=normal"
}

# -----------------------------------------------------------------------------
# Остановка
# -----------------------------------------------------------------------------
stop_supabase() {
  echo -e "${YELLOW}Остановка локального Supabase...${NC}"
  supabase stop
}

# -----------------------------------------------------------------------------
# Главный диспетчер аргументов
# -----------------------------------------------------------------------------
case "${1:-dev}" in
  dev)
    preflight_checks
    start_supabase
    generate_config
    run_desktop "run"
    ;;
  watch)
    preflight_checks
    start_supabase
    generate_config
    run_desktop "watch"
    ;;
  test)
    run_tests
    ;;
  stop)
    stop_supabase
    ;;
  *)
    echo "Использование: $0 [dev|watch|test|stop]"
    echo "  dev   - Проверка зависимостей, старт Supabase + seed, генерация конфига и запуск Desktop"
    echo "  watch - То же самое, но запуск через dotnet watch (hot-reload)"
    echo "  test  - Запуск всех юнит-тестов (без GUI)"
    echo "  stop  - Остановка локальных контейнеров Supabase"
    exit 1
    ;;
esac
