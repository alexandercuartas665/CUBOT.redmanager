# Genera los stubs de paginas del menu (Primera tarea funcional - Modulo 1.5).
# Solo ASCII. Cada pagina renderiza el componente StubPage.

$ErrorActionPreference = "Stop"
$pagesDir = Join-Path $PSScriptRoot "..\apps\backend\src\CubotRedManager.Web\Components\Pages"

# file | route | title | phase | icon | description
$stubs = @(
    @("Dashboard",       "/dashboard",        "Dashboard",            "Fase 2",  "bi-speedometer2",     "KPIs de la agencia: cuentas conectadas, publicaciones del dia, mensajes sin responder, salud de sync."),
    @("Calendario",      "/calendario",       "Calendario editorial", "Fase 2",  "bi-calendar-week",    "Vista mensual y semanal de publicaciones por cliente."),
    @("Bandeja",         "/bandeja",          "Bandeja unificada",    "Fase 2",  "bi-inbox",            "DMs, comentarios y menciones de todas las redes en una sola bandeja."),
    @("Tableros",        "/tableros",         "Tableros de tareas",   "Fase 3",  "bi-kanban",           "Kanban del equipo por cliente o campania (origen del prototipo)."),
    @("Conversaciones",  "/conversaciones",   "Conversaciones",       "Fase 3",  "bi-chat-dots",        "Chats internos y WhatsApp del equipo de la agencia."),
    @("Clientes",        "/clientes",         "Clientes (Marcas)",    "Fase 2",  "bi-people",           "Cartera de marcas que la agencia gestiona."),
    @("CuentasSociales", "/cuentas-sociales", "Cuentas sociales",     "Fase 2",  "bi-link-45deg",       "Cuentas conectadas, estado y proximas expiraciones."),
    @("Conexiones",      "/conexiones",       "OAuth y conexiones",   "Fase 2",  "bi-shield-check",     "Conexion guiada: elegir cliente, red y autorizar."),
    @("Reportes",        "/reportes",         "Reportes ejecutivos",  "Fase 3",  "bi-file-earmark-bar-graph", "Reportes mensuales por cliente exportables a PDF con marca blanca."),
    @("Metricas",        "/metricas",         "Metricas",             "Fase 2",  "bi-graph-up",         "Crecimiento, engagement, mejores horarios y top publicaciones."),
    @("LineasWhatsapp",  "/lineas-whatsapp",  "Lineas WhatsApp",      "Fase 3",  "bi-whatsapp",         "Lineas del equipo interno de la agencia."),
    @("Agentes",         "/agentes",          "Agentes de IA",        "Fase 4",  "bi-robot",            "Copywriter, Bandeja IA, Resumen, Analista, Detector de crisis."),
    @("Autorespuesta",   "/autorespuesta",    "Autorespuesta de comentarios", "Fase 4", "bi-reply-all", "Motor de respuestas automaticas por cuenta (plantillas + IA + horario + resumen WhatsApp)."),
    @("Automatizaciones","/automatizaciones", "Automatizaciones",     "Fase 3",  "bi-gear-wide-connected", "Reglas: alerta de token por expirar, recordatorios, asignacion de DMs."),
    @("Operadores",      "/operadores",       "Asesores y operadores","Fase 1",  "bi-person-badge",     "Equipo interno de la agencia (Admin/Operator)."),
    @("Plantillas",      "/plantillas",       "Plantillas",           "Fase 2",  "bi-card-text",        "Mensajes pregrabados para respuestas en bandeja."),
    @("Cuenta",          "/cuenta",           "Mi cuenta",            "Fase 1",  "bi-person-circle",    "Plan activo, limites, consumo, facturas y marca de la agencia.")
)

foreach ($s in $stubs) {
    $file = $s[0]; $route = $s[1]; $title = $s[2]; $phase = $s[3]; $icon = $s[4]; $desc = $s[5]
    $path = Join-Path $pagesDir ("{0}.razor" -f $file)
    $content = @"
@page "$route"

<StubPage Title="$title" Phase="$phase" Icon="$icon"
          Description="$desc" />
"@
    Set-Content -Path $path -Value $content -Encoding utf8
    Write-Output ("[OK] {0}.razor -> {1}" -f $file, $route)
}

Write-Output ("Generadas {0} paginas stub." -f $stubs.Count)
