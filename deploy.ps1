#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Локальный CI/CD для TicketMonitor.
    Сборка → тесты → публикация → бэкап → создание/остановка служб → деплой → старт → health-check → автооткат.

.EXAMPLE
    .\deploy.ps1
    .\deploy.ps1 -SkipTests
    .\deploy.ps1 -HealthTimeoutSec 120
#>

[CmdletBinding()]
param(
    [switch]$SkipTests,
    [int]$HealthTimeoutSec = 60
)

$ErrorActionPreference = "Stop"
$ProgressPreference    = "SilentlyContinue"

# ---------- Пути и константы ----------
$RepoRoot    = $PSScriptRoot
$PublishRoot = Join-Path $RepoRoot "publish"
$ApiPublish  = Join-Path $PublishRoot "Api"
$RmPublish   = Join-Path $PublishRoot "Redmine"
$WdPublish   = Join-Path $PublishRoot "Watchdog"

$ApiTarget   = "C:\TicketMonitor"
$RmTarget    = "C:\TicketMonitor-Redmine"
$WdTarget    = "C:\TicketMonitor-Watchdog"

$BackupRoot  = Join-Path $RepoRoot "deploy-backups"
$BackupDir   = Join-Path $BackupRoot (Get-Date -Format "yyyyMMdd_HHmmss")

$ApiHealthUrl = "http://localhost:8080/api/health"
$RmHealthUrl  = "http://localhost:8082/health"
$WdHealthUrl  = "http://localhost:8083/api/watchdog/health"

$ServiceNames = @("TicketMonitor", "TicketMonitor-Redmine", "TicketMonitor-Watchdog")

# Карта: имя службы → порт (для правил файрвола)
$ServicePorts = @{
    "TicketMonitor"          = 8080
    "TicketMonitor-Redmine"  = 8082
    "TicketMonitor-Watchdog" = 8083
}

$ScriptStart = Get-Date

# ---------- Хелперы вывода ----------
function Write-Step($m) { Write-Host "`n=== $m ===" -ForegroundColor Cyan }
function Write-Ok($m)   { Write-Host "[OK] $m"    -ForegroundColor Green }
function Write-Warn2($m){ Write-Host "[!] $m"     -ForegroundColor Yellow }
function Write-Err($m)  { Write-Host "[X] $m"     -ForegroundColor Red }

# ---------- Обёртка для внешних команд ----------
function Invoke-External {
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][scriptblock]$Action
    )
    Write-Step $Name
    & $Action
    if ($null -ne $LASTEXITCODE -and $LASTEXITCODE -ne 0) {
        throw "$Name завершилось с кодом $LASTEXITCODE"
    }
}

# ---------- Ожидание статуса службы ----------
function Wait-ServiceStatus {
    param(
        [string]$Name,
        [string]$Status,          # "Running" | "Stopped"
        [int]$TimeoutSec = 30
    )
    $svc = Get-Service -Name $Name -ErrorAction SilentlyContinue
    if ($null -eq $svc) { throw "Служба '$Name' не найдена" }

    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    while ((Get-Date) -lt $deadline) {
        $svc.Refresh()
        if ($svc.Status -eq $Status) { return }
        Start-Sleep -Milliseconds 500
    }
    throw "Служба '$Name' не достигла статуса '$Status' за $TimeoutSec сек (текущий: $($svc.Status))"
}

# ---------- Ожидание HTTP-endpoint ----------
function Wait-Endpoint {
    param(
        [string]$Url,
        [int]$TimeoutSec = 60
    )
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    while ((Get-Date) -lt $deadline) {
        try {
            return Invoke-RestMethod -Uri $Url -TimeoutSec 5
        } catch {
            Start-Sleep -Seconds 2
        }
    }
    return $null
}

# ============================================================
# 1. СБОРКА
# ============================================================
Invoke-External -Name "Сборка решения (Release)" -Action {
    dotnet build "$RepoRoot\TicketMonitor.slnx" -c Release --nologo
}
Write-Ok "Сборка успешна"

# ============================================================
# 2. ТЕСТЫ
# ============================================================
if (-not $SkipTests) {
    Invoke-External -Name "Прогон тестов" -Action {
        # Прогоняем проекты последовательно, чтобы не исчерпать память
        # на параллельных testhost-процессах.
        $testProjects = @(
            "TicketMonitor.Core.Tests\TicketMonitor.Core.Tests.csproj",
            "TicketMonitor.Redmine.Tests\TicketMonitor.Redmine.Tests.csproj",
            "TicketMonitor.Watchdog.Tests\TicketMonitor.Watchdog.Tests.csproj"
        )

        foreach ($proj in $testProjects) {
            Write-Host "    → $proj" -ForegroundColor DarkGray
            dotnet test "$RepoRoot\$proj" -c Release --no-build --nologo `
                --logger "console;verbosity=minimal"
            if ($LASTEXITCODE -ne 0) {
                throw "Тесты в $proj завершились с кодом $LASTEXITCODE"
            }
        }
    }
    Write-Ok "Все тесты прошли"
}

# ============================================================
# 3. ПУБЛИКАЦИЯ
# ============================================================
Write-Step "Публикация"
Remove-Item -Recurse -Force $PublishRoot -ErrorAction SilentlyContinue

Invoke-External -Name "Публикация TicketMonitor.Api" -Action {
    dotnet publish "$RepoRoot\TicketMonitor.Api\TicketMonitor.Api.csproj" `
        -c Release -o $ApiPublish --nologo
}

Invoke-External -Name "Публикация TicketMonitor.Redmine" -Action {
    dotnet publish "$RepoRoot\TicketMonitor.Redmine\TicketMonitor.Redmine.csproj" `
        -c Release -o $RmPublish --nologo
}

Invoke-External -Name "Публикация TicketMonitor.Watchdog" -Action {
    dotnet publish "$RepoRoot\TicketMonitor.Watchdog\TicketMonitor.Watchdog.csproj" `
        -c Release -o $WdPublish --nologo
}
Write-Ok "Публикация завершена"

# ============================================================
# 4. БЭКАП ТЕКУЩЕЙ ВЕРСИИ
# ============================================================
Write-Step "Бэкап текущей версии в $BackupDir"
New-Item -ItemType Directory -Path $BackupDir -Force | Out-Null

if (Test-Path $ApiTarget) {
    robocopy $ApiTarget (Join-Path $BackupDir "Api") `
        /E /XD Data Logs Backups /R:1 /W:1 /NFL /NDL /NP /NJH /NJS | Out-Null
} else {
    Write-Warn2 "Папка $ApiTarget не существует — бэкап Api пропущен"
}

if (Test-Path $RmTarget) {
    robocopy $RmTarget (Join-Path $BackupDir "Redmine") `
        /E /XD Data Logs /R:1 /W:1 /NFL /NDL /NP /NJH /NJS | Out-Null
} else {
    Write-Warn2 "Папка $RmTarget не существует — бэкап Redmine пропущен"
}

if (Test-Path $WdTarget) {
    robocopy $WdTarget (Join-Path $BackupDir "Watchdog") `
        /E /XD Data Logs /R:1 /W:1 /NFL /NDL /NP /NJH /NJS | Out-Null
} else {
    Write-Warn2 "Папка $WdTarget не существует — бэкап Watchdog пропущен"
}
Write-Ok "Бэкап сохранён"

# ============================================================
# 5. СОЗДАНИЕ СЛУЖБ (если ещё нет) + ОСТАНОВКА
# ============================================================
Write-Step "Проверка и остановка служб"

# Карта: имя службы → путь к exe
$ServiceExeMap = @{
    "TicketMonitor"           = Join-Path $ApiTarget "TicketMonitor.Api.exe"
    "TicketMonitor-Redmine"   = Join-Path $RmTarget  "TicketMonitor.Redmine.exe"
    "TicketMonitor-Watchdog"  = Join-Path $WdTarget  "TicketMonitor.Watchdog.exe"
}

foreach ($svc in $ServiceNames) {
    $s = Get-Service -Name $svc -ErrorAction SilentlyContinue

    # Если службы нет — создаём (exe должен уже лежать после публикации/копирования).
    if ($null -eq $s) {
        $exePath = $ServiceExeMap[$svc]
        if (-not (Test-Path $exePath)) {
            Write-Warn2 "Служба '$svc' не найдена, а exe ещё нет ($exePath) — пропуск. Запусти deploy.ps1 ещё раз"
            continue
        }

        Write-Host "[i] Создаю службу '$svc' → $exePath" -ForegroundColor DarkGray
        sc.exe create $svc binPath= "`"$exePath`"" start= auto | Out-Null
        if ($LASTEXITCODE -ne 0) {
            Write-Err "Не удалось создать службу '$svc' (sc.exe вернул $LASTEXITCODE)"
            continue
        }
        sc.exe description $svc "TicketMonitor background service" | Out-Null

        # Recovery: если служба падает — ОС перезапускает.
        # Счётчик сбрасывается через сутки; интервалы 5с/10с/30с между попытками.
        sc.exe failure $svc reset= 86400 actions= restart/5000/restart/10000/restart/30000 | Out-Null

        Write-Ok "Служба '$svc' создана (auto-restart настроен)"
        $s = Get-Service -Name $svc -ErrorAction SilentlyContinue
    }

    # Останавливаем, если запущена.
    if ($null -ne $s -and $s.Status -ne "Stopped") {
        Stop-Service -Name $svc -Force
        Wait-ServiceStatus -Name $svc -Status "Stopped" -TimeoutSec 30
        Write-Ok "Служба '$svc' остановлена"
    } elseif ($null -ne $s) {
        Write-Ok "Служба '$svc' уже остановлена"
    }
}

# ============================================================
# 5.5. ПРАВИЛА ФАЙРВОЛА
# ============================================================
# Гарантирует, что порты 8080/8082/8083 открыты для входящих TCP-подключений.
# Используем правила по порту (а не по exe), потому что правила по exe
# зависят от профиля сети и пути к приложению, и ломаются при их изменении.
Write-Step "Проверка правил файрвола"

foreach ($svc in $ServiceNames) {
    $port = $ServicePorts[$svc]
    if ($null -eq $port) { continue }

    $ruleName = "TicketMonitor $svc ($port)"

    $existing = Get-NetFirewallRule -DisplayName $ruleName -ErrorAction SilentlyContinue
    if ($null -ne $existing) {
        Write-Ok "Правило '$ruleName' уже существует"
        continue
    }

    # На всякий случай удаляем старые правила, созданные автоматически
    # при первом запуске приложения (привязка к exe, а не к порту).
    $legacyRules = Get-NetFirewallRule -ErrorAction SilentlyContinue |
        Where-Object { $_.DisplayName -like "*ticketmonitor*" -and $_.DisplayName -like "*.exe" }
    foreach ($legacy in $legacyRules) {
        Remove-NetFirewallRule -Name $legacy.Name -ErrorAction SilentlyContinue
        Write-Warn2 "Удалено устаревшее правило '$($legacy.DisplayName)' (привязка к exe)"
    }

    New-NetFirewallRule `
        -DisplayName $ruleName `
        -Direction Inbound `
        -Protocol TCP `
        -LocalPort $port `
        -Action Allow `
        -Profile Any `
        -Enabled True | Out-Null

    Write-Ok "Правило '$ruleName' создано (порт $port, все профили)"
}

# ============================================================
# 6. КОПИРОВАНИЕ ФАЙЛОВ
# ============================================================
Write-Step "Копирование файлов (Api)"
robocopy $ApiPublish $ApiTarget /MIR /XD Data Logs Backups /R:2 /W:2 /NFL /NDL /NP /IS | Out-Null
if ($LASTEXITCODE -ge 8) { throw "robocopy Api вернул код $LASTEXITCODE" }
Write-Ok "Api: файлы скопированы"

Write-Step "Копирование файлов (Redmine)"
robocopy $RmPublish $RmTarget /MIR /XD Data Logs /R:2 /W:2 /NFL /NDL /NP /IS | Out-Null
if ($LASTEXITCODE -ge 8) { throw "robocopy Redmine вернул код $LASTEXITCODE" }
Write-Ok "Redmine: файлы скопированы"

Write-Step "Копирование файлов (Watchdog)"
robocopy $WdPublish $WdTarget /MIR /XD Data Logs /R:2 /W:2 /NFL /NDL /NP /IS | Out-Null
if ($LASTEXITCODE -ge 8) { throw "robocopy Watchdog вернул код $LASTEXITCODE" }
Write-Ok "Watchdog: файлы скопированы"

# ============================================================
# 7. ЗАПУСК СЛУЖБ
# ============================================================
Write-Step "Запуск служб"
foreach ($svc in $ServiceNames) {
    $s = Get-Service -Name $svc -ErrorAction SilentlyContinue
    if ($null -eq $s) { Write-Warn2 "Служба '$svc' не найдена — пропуск"; continue }
    if ($s.Status -ne "Running") {
        Start-Service -Name $svc
        Wait-ServiceStatus -Name $svc -Status "Running" -TimeoutSec 30
        Write-Ok "Служба '$svc' запущена"
    } else {
        Write-Ok "Служба '$svc' уже запущена"
    }
}

# ============================================================
# 8. HEALTH-CHECK
# ============================================================
Write-Step "Health-check"
$apiHealth = Wait-Endpoint -Url $ApiHealthUrl -TimeoutSec $HealthTimeoutSec
$rmHealth  = Wait-Endpoint -Url $RmHealthUrl  -TimeoutSec $HealthTimeoutSec
$wdHealth  = Wait-Endpoint -Url $WdHealthUrl  -TimeoutSec $HealthTimeoutSec

$apiOk = $null -ne $apiHealth
$rmOk  = $null -ne $rmHealth
$wdOk  = $null -ne $wdHealth

# Доп. проверка Api: должен быть хотя бы один монитор в ответе,
# иначе воркер мог упасть до первой итерации, но endpoint всё равно отвечает 200.
if ($apiOk) {
    $monitorCount = 0
    if ($apiHealth.PSObject.Properties.Name -contains "monitors" -and $null -ne $apiHealth.monitors) {
        $monitorCount = @($apiHealth.monitors.PSObject.Properties).Count
    }
    if ($monitorCount -eq 0) {
        Write-Warn2 "Api отвечает 200, но monitors пуст — воркер мог не отработать первую итерацию"
        # Не считаем это провалом: при холодном старте monitors может быть пустым,
        # если первая итерация ещё не завершилась. Только предупреждаем.
    } else {
        Write-Ok "Api: status=$($apiHealth.status), мониторов=$monitorCount"
    }
}

if ($apiOk -and $rmOk -and $wdOk) {
    Write-Ok "Redmine: status=$($rmHealth.status)"
    Write-Ok "Watchdog: status=$($wdHealth.status), сервисов=$($wdHealth.services)"
    Write-Ok "Деплой успешно завершён за $([int]((Get-Date) - $ScriptStart).TotalSeconds) сек"
    Write-Host "Бэкап предыдущей версии: $BackupDir" -ForegroundColor DarkGray
    exit 0
}

Write-Err "Health-check провален: Api=$apiOk, Redmine=$rmOk, Watchdog=$wdOk"

# ============================================================
# 9. ОТКАТ
# ============================================================
Write-Step "ОТКАТ к предыдущей версии"

foreach ($svc in $ServiceNames) {
    $s = Get-Service -Name $svc -ErrorAction SilentlyContinue
    if ($null -ne $s -and $s.Status -ne "Stopped") {
        Stop-Service -Name $svc -Force
        Wait-ServiceStatus -Name $svc -Status "Stopped" -TimeoutSec 30
    }
}

$apiBackup = Join-Path $BackupDir "Api"
$rmBackup  = Join-Path $BackupDir "Redmine"
$wdBackup  = Join-Path $BackupDir "Watchdog"

if (Test-Path $apiBackup) {
    robocopy $apiBackup $ApiTarget /MIR /XD Data Logs Backups /R:2 /W:2 /IS /NJH /NJS | Out-Null
    if ($LASTEXITCODE -ge 8) { Write-Err "robocopy отката Api вернул $LASTEXITCODE" }
    else { Write-Ok "Api откачен из бэкапа" }
} else {
    Write-Err "Бэкап Api не найден ($apiBackup) — ручной разбор!"
}

if (Test-Path $rmBackup) {
    robocopy $rmBackup $RmTarget /MIR /XD Data Logs /R:2 /W:2 /IS /NJH /NJS | Out-Null
    if ($LASTEXITCODE -ge 8) { Write-Err "robocopy отката Redmine вернул $LASTEXITCODE" }
    else { Write-Ok "Redmine откачен из бэкапа" }
} else {
    Write-Warn2 "Бэкап Redmine не найден — пропуск"
}

if (Test-Path $wdBackup) {
    robocopy $wdBackup $WdTarget /MIR /XD Data Logs /R:2 /W:2 /IS /NJH /NJS | Out-Null
    if ($LASTEXITCODE -ge 8) { Write-Err "robocopy отката Watchdog вернул $LASTEXITCODE" }
    else { Write-Ok "Watchdog откачен из бэкапа" }
} else {
    Write-Warn2 "Бэкап Watchdog не найден — пропуск"
}

foreach ($svc in $ServiceNames) {
    $s = Get-Service -Name $svc -ErrorAction SilentlyContinue
    if ($null -ne $s) {
        Start-Service -Name $svc
        Wait-ServiceStatus -Name $svc -Status "Running" -TimeoutSec 30
        Write-Ok "Служба '$svc' перезапущена после отката"
    }
}

Write-Err "Откат завершён. Проверьте логи: $ApiTarget\Logs"
exit 1