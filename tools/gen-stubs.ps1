# Genera los stubs de paginas del menu (Primera tarea funcional - Modulo 1.5).
# Solo ASCII. Cada pagina renderiza el componente ModulePlaceholder (patron familia CUBOT).

$ErrorActionPreference = "Stop"
$pagesDir = Join-Path $PSScriptRoot "..\apps\backend\src\CubotRedManager.Web\Components\Pages"

# file | route | title | phase | eyebrow (seccion) | description
$stubs = @(
    @("Dashboard",       "/dashboard",        "Dashboard",            "Fase 2",  "Operacion diaria",   "KPIs de la agencia: cuentas conectadas, publicaciones del dia, mensajes sin responder, salud de sync."),
    @("Calendario",      "/calendario",       "Calendario editorial", "Fase 2",  "Operacion diaria",   "Vista mensual y semanal de publicaciones por cliente."),
    @("Bandeja",         "/bandeja",          "Bandeja unificada",    "Fase 2",  "Operacion diaria",   "DMs, comentarios y menciones de todas las redes en una sola bandeja."),
    @("Tableros",        "/tableros",         "Tableros de tareas",   "Fase 3",  "Operacion diaria",   "Kanban del equipo por cliente o campania (origen del prototipo)."),
    @("Conversaciones",  "/conversaciones",   "Conversaciones",       "Fase 3",  "Operacion diaria",   "Chats internos y WhatsApp del equipo de la agencia."),
    @("Clientes",        "/clientes",         "Clientes (Marcas)",    "Fase 2",  "Clientes y cuentas", "Cartera de marcas que la agencia gestiona."),
    @("CuentasSociales", "/cuentas-sociales", "Cuentas sociales",     "Fase 2",  "Clientes y cuentas", "Cuentas conectadas, estado y proximas expiraciones."),
    @("Conexiones",      "/conexiones",       "OAuth y conexiones",   "Fase 2",  "Clientes y cuentas", "Conexion guiada: elegir cliente, red y autorizar."),
    @("Reportes",        "/reportes",         "Reportes ejecutivos",  "Fase 3",  "Reportes",           "Reportes mensuales por cliente exportables a PDF con marca blanca."),
    @("Metricas",        "/metricas",         "Metricas",             "Fase 2",  "Reportes",           "Crecimiento, engagement, mejores horarios y top publicaciones."),
    @("LineasWhatsapp",  "/lineas-whatsapp",  "Lineas WhatsApp",      "Fase 3",  "Comunicacion e IA",  "Lineas del equipo interno de la agencia."),
    @("Agentes",         "/agentes",          "Agentes de IA",        "Fase 4",  "Comunicacion e IA",  "Copywriter, Bandeja IA, Resumen, Analista, Detector de crisis."),
    @("Autorespuesta",   "/autorespuesta",    "Autorespuesta de comentarios", "Fase 4", "Comunicacion e IA", "Motor de respuestas automaticas por cuenta (plantillas + IA + horario + resumen WhatsApp)."),
    @("Automatizaciones","/automatizaciones", "Automatizaciones",     "Fase 3",  "Comunicacion e IA",  "Reglas: alerta de token por expirar, recordatorios, asignacion de DMs."),
    @("Operadores",      "/operadores",       "Asesores y operadores","Fase 1",  "Configuracion",      "Equipo interno de la agencia (Admin/Operator)."),
    @("Plantillas",      "/plantillas",       "Plantillas",           "Fase 2",  "Configuracion",      "Mensajes pregrabados para respuestas en bandeja."),
    @("Cuenta",          "/cuenta",           "Mi cuenta",            "Fase 1",  "Configuracion",      "Plan activo, limites, consumo, facturas y marca de la agencia.")
)

foreach ($s in $stubs) {
    $file = $s[0]; $route = $s[1]; $title = $s[2]; $phase = $s[3]; $eyebrow = $s[4]; $desc = $s[5]
    $path = Join-Path $pagesDir ("{0}.razor" -f $file)
    $content = @"
@page "$route"
@attribute [Microsoft.AspNetCore.Authorization.Authorize(Policy = AppPolicies.TenantMember)]

<ModulePlaceholder Eyebrow="$eyebrow" Title="$title" Phase="$phase"
                   Description="$desc" />
"@
    Set-Content -Path $path -Value $content -Encoding utf8
    Write-Output ("[OK] {0}.razor -> {1}" -f $file, $route)
}

Write-Output ("Generadas {0} paginas stub." -f $stubs.Count)
