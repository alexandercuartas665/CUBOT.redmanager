# ============================================================
#  CUBOT.redmanager - Lanzador local de desarrollo
#  Levanta Docker (Postgres/Redis/RabbitMQ/pgAdmin), compila y
#  arranca la consola unificada:
#    - Web (Agencia + /admin Super Admin) -> http://localhost:5036
#  Uso:   pwsh -File tools\run.ps1          (build + run)
#         pwsh -File tools\run.ps1 -NoBuild (solo run)
#         pwsh -File tools\run.ps1 -Stop    (detener SOLO este proyecto)
#
#  IMPORTANTE: Este script SOLO detiene los procesos dotnet del proyecto
#  CUBOT.redmanager (identificados por el puerto 5036 o por command-line
#  que contenga "CubotRedManager"). NO afecta otros dotnet corriendo en
#  la maquina (ej. CUBOT.travels en otros puertos).
# ============================================================
param(
    [switch]$NoBuild,
    [switch]$Stop
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$backend = Join-Path $root "apps\backend"
$webDir = Join-Path $backend "src\CubotRedManager.Web"
$docker = Join-Path $root "deploy\docker"
$pidFile = Join-Path $PSScriptRoot ".pids"
$ProjectPorts = @(5036)

# Asegura dotnet 10 en el PATH de esta sesion.
$env:Path = [System.Environment]::GetEnvironmentVariable("Path","Machine") + ";" + [System.Environment]::GetEnvironmentVariable("Path","User")

# Devuelve los PIDs que escuchan los puertos del proyecto.
function Get-CubotPidsByPort {
    $pids = @()
    foreach ($port in $ProjectPorts) {
        try {
            $conn = Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction Stop
            foreach ($c in $conn) {
                if ($c.OwningProcess -gt 0) { $pids += $c.OwningProcess }
            }
        } catch { }
    }
    return $pids | Sort-Object -Unique
}

# Devuelve los PIDs de procesos dotnet cuya command-line contiene "CubotRedManager".
function Get-CubotPidsByCommandLine {
    $pids = @()
    try {
        Get-CimInstance Win32_Process -Filter "Name='dotnet.exe'" -ErrorAction Stop |
            Where-Object { $_.CommandLine -and $_.CommandLine -match "CubotRedManager" } |
            ForEach-Object { $pids += $_.ProcessId }
    } catch { }
    return $pids | Sort-Object -Unique
}

# Devuelve los PIDs de procesos cuyo Path apunta a un binario del proyecto.
function Get-CubotPidsByPath {
    $pids = @()
    try {
        Get-Process -ErrorAction Stop | Where-Object {
            try { $_.Path -and ($_.Path -like "*CubotRedManager*") } catch { $false }
        } | ForEach-Object { $pids += $_.Id }
    } catch { }
    return $pids | Sort-Object -Unique
}

# Combina las tres estrategias.
function Get-CubotPids {
    $pids = @()
    $pids += Get-CubotPidsByPort
    $pids += Get-CubotPidsByCommandLine
    $pids += Get-CubotPidsByPath
    return $pids | Sort-Object -Unique
}

function Stop-Cubot {
    $pids = Get-CubotPids
    if ($pids.Count -eq 0) {
        Write-Output "[run] No hay procesos del proyecto corriendo."
        if (Test-Path $pidFile) { Remove-Item $pidFile -Force -ErrorAction SilentlyContinue }
        return
    }
    Write-Output "[run] Deteniendo procesos del proyecto (PIDs: $($pids -join ', '))..."
    foreach ($id in $pids) {
        Stop-Process -Id $id -Force -ErrorAction SilentlyContinue
    }
    if (Test-Path $pidFile) { Remove-Item $pidFile -Force -ErrorAction SilentlyContinue }
    Write-Output "[run] Listo. (Docker sigue arriba; usa 'docker compose down' en deploy\docker si quieres bajarlo.)"
    Write-Output "[run] Otros proyectos dotnet en otros puertos NO fueron afectados."
}

if ($Stop) { Stop-Cubot; return }

Write-Output "============================================"
Write-Output " CUBOT.redmanager - arranque local"
Write-Output "============================================"

# 1) Docker (idempotente)
Write-Output "[run] Levantando stack Docker (cubotrm)..."
Push-Location $docker
docker compose up -d | Out-Null
Pop-Location

# 2) Build (salvo -NoBuild)
if (-not $NoBuild) {
    Write-Output "[run] Compilando solucion..."
    dotnet build (Join-Path $backend "CubotRedManager.slnx") -c Debug --nologo | Select-Object -Last 3
}

# 3) Detener instancias PREVIAS de ESTE proyecto.
Stop-Cubot | Out-Null
Start-Sleep -Seconds 2

# 4) Arrancar la consola unificada (aplica migracion al iniciar).
Write-Output "[run] Iniciando Web en http://localhost:5036 ..."
$webProc = Start-Process -FilePath "dotnet" -WorkingDirectory $webDir `
    -ArgumentList "run","--no-build","-c","Debug","--urls","http://localhost:5036" `
    -WindowStyle Hidden -PassThru

"$($webProc.Id)" | Out-File -FilePath $pidFile -Encoding ascii

Start-Sleep -Seconds 12

Write-Output ""
Write-Output "============================================"
Write-Output " LISTO. Consola unificada:"
Write-Output "   Agencia    : http://localhost:5036          (login Admin / Operador)"
Write-Output "   Super Admin: http://localhost:5036/admin    (login Operador de plataforma)"
Write-Output ""
Write-Output " Infra Docker:"
Write-Output "   PostgreSQL : localhost:5436   Redis: 6383"
Write-Output "   RabbitMQ   : 5675 (UI 15675)  pgAdmin: http://localhost:5052"
Write-Output ""
Write-Output " Detener (solo este proyecto): pwsh -File tools\run.ps1 -Stop"
Write-Output "============================================"
