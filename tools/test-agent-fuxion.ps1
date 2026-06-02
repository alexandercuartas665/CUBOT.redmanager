# ============================================================
#  CUBOT.redmanager - Prueba del agente FUXION via MCP DataContainer
#
#  Hace 6 llamadas reales al endpoint dev POST /dev/test-agent-mcp
#  con el agente FUXION. Imprime pregunta, respuesta truncada y
#  tiempo de respuesta. Requiere:
#    - El stack Docker arriba.
#    - La Web arriba en http://localhost:5036
#    - Una API key de Gemini configurada en ai_provider_configs.
#
#  Uso:  pwsh -File tools\test-agent-fuxion.ps1
# ============================================================

$ErrorActionPreference = "Stop"
$baseUrl = "http://localhost:5036"
$agent = "FUXION"

$questions = @(
    "Hola, que productos tienes para el cansancio?",
    "Cual es el precio de VIVA en Colombia?",
    "Que producto recomiendas para el insomnio?",
    "Tienes algun producto para fortalecer la inmunidad?",
    "Cuanto cuesta FORZA en Mexico?",
    "Que tienes en la linea de Belleza?"
)

Write-Output "============================================"
Write-Output " Prueba MCP -> Agente FUXION (6 preguntas)"
Write-Output "============================================"
Write-Output ""

# Sanity check: la Web debe responder.
try {
    $ping = Invoke-WebRequest -Uri "$baseUrl/login" -UseBasicParsing -TimeoutSec 5
    if ($ping.StatusCode -ne 200) { throw "Web respondio $($ping.StatusCode)" }
} catch {
    Write-Error "No se pudo contactar la Web en $baseUrl. Arrancala con 'pwsh -File tools\run.ps1 -NoBuild' antes de correr esta prueba."
    exit 1
}

$idx = 1
foreach ($q in $questions) {
    Write-Output "----- Pregunta $idx / 6 -----"
    Write-Output "P: $q"

    $bodyObj = @{ agent = $agent; message = $q }
    $bodyJson = ConvertTo-Json $bodyObj -Depth 3

    $start = Get-Date
    try {
        $resp = Invoke-RestMethod -Uri "$baseUrl/dev/test-agent-mcp" -Method Post `
            -ContentType "application/json; charset=utf-8" `
            -Body ([System.Text.Encoding]::UTF8.GetBytes($bodyJson)) `
            -TimeoutSec 120
    } catch {
        $elapsed = ((Get-Date) - $start).TotalMilliseconds
        Write-Output "R: [ERROR HTTP] $($_.Exception.Message)"
        Write-Output "T: $([int]$elapsed) ms"
        Write-Output ""
        $idx++
        continue
    }
    $elapsed = ((Get-Date) - $start).TotalMilliseconds

    if ($resp.ok) {
        $text = $resp.text
        if (-not $text) { $text = "(respuesta vacia)" }
        $truncated = if ($text.Length -gt 300) { $text.Substring(0,300) + "..." } else { $text }
        Write-Output "R: $truncated"
        Write-Output "Tokens: in=$($resp.inputTokens) out=$($resp.outputTokens)"
    } else {
        Write-Output "R: [SIN OK] $($resp.error)"
    }
    Write-Output "T: $([int]$elapsed) ms"
    Write-Output ""
    $idx++
}

Write-Output "============================================"
Write-Output " Pruebas terminadas."
Write-Output "============================================"
