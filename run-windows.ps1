<#
.SYNOPSIS
    MedTracker Developer Runner for Windows PowerShell.
.EXAMPLE
    .\run-windows.ps1 dev
    .\run-windows.ps1 watch
    .\run-windows.ps1 test
    .\run-windows.ps1 stop
#>
param (
    [ValidateSet("dev", "watch", "test", "stop")]
    [string]$Command = "dev"
)

$ErrorActionPreference = "Stop"

function Write-Step {
    param([string]$Message)
    Write-Host "`n==> $Message" -ForegroundColor Cyan
}

function Test-CommandExists {
    param([string]$Name)
    return [bool](Get-Command $Name -ErrorAction SilentlyContinue)
}

function Invoke-Preflight {
    Write-Step "Шаг 1: Проверка установленного окружения (Pre-flight)..."

    # .NET SDK
    if (-not (Test-CommandExists "dotnet")) {
        Write-Host "❌ .NET SDK не найден в PATH." -ForegroundColor Red
        Write-Host "   Установите .NET SDK 10: https://dotnet.microsoft.com/download/dotnet/10.0"
        exit 1
    }
    $dotnetVer = & dotnet --version
    $major = [int]($dotnetVer.Split('.')[0])
    if ($major -lt 10) {
        Write-Host "❌ Требуется .NET SDK 10.0+, обнаружена версия: $dotnetVer" -ForegroundColor Red
        exit 1
    }
    Write-Host "  ✓ .NET SDK: $dotnetVer" -ForegroundColor Green

    # Docker
    if (-not (Test-CommandExists "docker")) {
        Write-Host "❌ Docker не найден в PATH. Установите Docker Desktop." -ForegroundColor Red
        exit 1
    }
    & docker info *>$null
    if ($LASTEXITCODE -ne 0) {
        Write-Host "❌ Демон Docker не запущен. Пожалуйста, запустите Docker Desktop." -ForegroundColor Red
        exit 1
    }
    Write-Host "  ✓ Docker daemon: готов к работе" -ForegroundColor Green

    # Supabase CLI
    if (-not (Test-CommandExists "supabase")) {
        Write-Host "❌ Supabase CLI не найден в PATH." -ForegroundColor Red
        Write-Host "   Установите через Scoop: scoop bucket add supabase https://github.com/supabase/scoop-bucket.git; scoop install supabase"
        exit 1
    }
    $supaVer = (& supabase --version | Select-Object -First 1)
    Write-Host "  ✓ Supabase CLI: $supaVer" -ForegroundColor Green
}

function Start-SupabaseStack {
    Write-Step "Шаг 2: Запуск локального Supabase и применение seed-данных..."
    & supabase start
    Write-Host "  Сброс БД и применение сид-данных (supabase db reset)..."
    & supabase db reset
}

function Generate-Config {
    Write-Step "Шаг 3: Автогенерация конфигурации подключения к Supabase..."
    $statusRaw = (& supabase status -o json) | Out-String
    $statusObj = $statusRaw | ConvertFrom-Json

    $apiUrl = $statusObj.API_URL
    $anonKey = $statusObj.ANON_KEY

    if ([string]::IsNullOrWhiteSpace($apiUrl) -or [string]::IsNullOrWhiteSpace($anonKey)) {
        Write-Host "❌ Не удалось получить API_URL или ANON_KEY от supabase status." -ForegroundColor Red
        exit 1
    }

    $configObj = @{
        Supabase = @{
            Url = $apiUrl
            AnonKey = $anonKey
            SignedUrlTtlSeconds = 300
        }
    }

    $configJson = $configObj | ConvertTo-Json -Depth 4
    $devConfigPath = "src/Med.Desktop/appsettings.Development.json"
    $localConfigPath = "src/Med.Desktop/appsettings.Local.json"

    Set-Content -Path $devConfigPath -Value $configJson -Encoding UTF8
    Set-Content -Path $localConfigPath -Value $configJson -Encoding UTF8

    Write-Host "  ✓ Конфиг сформирован: $devConfigPath" -ForegroundColor Green
    Write-Host "  ✓ Supabase Studio:    http://127.0.0.1:54323" -ForegroundColor Yellow
    Write-Host "  ✓ Inbucket (Email):   http://127.0.0.1:54324" -ForegroundColor Yellow
    Write-Host "  ✓ Тестовый логин:     dev@medtracker.local / пароль: password123" -ForegroundColor Green
}

function Run-DesktopClient {
    param([string]$Mode)
    Write-Step "Шаг 4: Запуск Desktop приложения (Avalonia UI)..."
    if ($Mode -eq "watch") {
        & dotnet watch --project src/Med.Desktop
    } else {
        & dotnet run --project src/Med.Desktop
    }
}

switch ($Command) {
    "dev" {
        Invoke-Preflight
        Start-SupabaseStack
        Generate-Config
        Run-DesktopClient -Mode "run"
    }
    "watch" {
        Invoke-Preflight
        Start-SupabaseStack
        Generate-Config
        Run-DesktopClient -Mode "watch"
    }
    "test" {
        Invoke-Preflight
        Write-Step "Запуск тестов решения MedTracker.slnx..."
        & dotnet test MedTracker.slnx --logger "console;verbosity=normal"
    }
    "stop" {
        Write-Host "Остановка локального Supabase..." -ForegroundColor Yellow
        & supabase stop
    }
}
