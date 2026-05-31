# ============================================================
#  CUBOT.redmanager - Lanzador local de desarrollo
#  Levanta Docker (Postgres/Redis/RabbitMQ/pgAdmin), compila y
#  arranca las dos consolas:
#    - Web Agencia    -> http://localhost:5036
#    - Super Admin    -> http://localhost:5037
#  Uso:   pwsh -File tools\run.ps1          (build + run)
#         pwsh -File tools\run.ps1 -NoBuild (solo run)
#         pwsh -File tools\run.ps1 -Stop    (detener todo)
# ============================================================
param(
    [switch]$NoBuild,
    [switch]$Stop
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$backend = Join-Path $root "apps\backend"
$webDir = Join-Path $backend "src\CubotRedManager.Web"
$saDir = Join-Path $backend "src\CubotRedManager.SuperAdmin"
$docker = Join-Path $root "deploy\docker"

# Asegura dotnet 10 en el PATH de esta sesion.
$env:Path = [System.Environment]::GetEnvironmentVariable("Path","Machine") + ";" + [System.Environment]::GetEnvironmentVariable("Path","User")

function Stop-All {
    Write-Output "[run] Deteniendo procesos dotnet..."
    Get-Process dotnet -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    Write-Output "[run] Listo. (Docker sigue arriba; usa 'docker compose down' en deploy\docker si quieres bajarlo.)"
}

if ($Stop) { Stop-All; return }

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

# 3) Detener instancias previas para no chocar puertos
Get-Process dotnet -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 2

# 4) Arrancar las dos consolas (aplican migracion al iniciar)
Write-Output "[run] Iniciando Web Agencia en http://localhost:5036 ..."
Start-Process -FilePath "dotnet" -WorkingDirectory $webDir `
    -ArgumentList "run","--no-build","-c","Debug","--urls","http://localhost:5036" -WindowStyle Hidden

Write-Output "[run] Iniciando Super Admin en http://localhost:5037 ..."
Start-Process -FilePath "dotnet" -WorkingDirectory $saDir `
    -ArgumentList "run","--no-build","-c","Debug","--urls","http://localhost:5037" -WindowStyle Hidden

Start-Sleep -Seconds 12

Write-Output ""
Write-Output "============================================"
Write-Output " LISTO. Consolas disponibles:"
Write-Output "   Agencia    : http://localhost:5036  (Login dev: Admin / Operador)"
Write-Output "   Super Admin: http://localhost:5037  (Login dev: Operador de plataforma)"
Write-Output ""
Write-Output " Infra Docker:"
Write-Output "   PostgreSQL : localhost:5436   Redis: 6383"
Write-Output "   RabbitMQ   : 5675 (UI 15675)  pgAdmin: http://localhost:5052"
Write-Output ""
Write-Output " Detener:  pwsh -File tools\run.ps1 -Stop"
Write-Output "============================================"
