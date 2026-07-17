namespace CubotRedManager.Domain.Enums;

/// <summary>Estado del tenant (agencia) en el SaaS.</summary>
public enum TenantStatus
{
    Trial,
    Active,
    PastDue,
    Suspended,
    Blocked,
    Cancelled,
    Archived
}

/// <summary>Rol global de plataforma. Null en PlatformUser = usuario de agencia.</summary>
public enum PlatformRole
{
    SuperAdmin,
    PlatformOperator
}

/// <summary>Rol del usuario dentro de una agencia (tenant).</summary>
public enum TenantRole
{
    Owner,
    Admin,
    Operator
}

/// <summary>Estado de la membresia / usuario.</summary>
public enum PlatformUserStatus
{
    Invited,
    Active,
    Disabled
}

/// <summary>Proveedor de identidad del usuario.</summary>
public enum AuthProvider
{
    Local,
    Google
}

/// <summary>Proveedor de IA configurado para un agente.</summary>
public enum AiProvider
{
    Claude = 0,
    Gemini,
    ChatGpt,
    DeepSeek
}

/// <summary>Tipo de recurso que un agente puede usar para entregar informacion.</summary>
public enum AgentResourceType
{
    Image = 0,
    Video,
    Audio,
    Pdf,
    Location,
    Text
}

/// <summary>Estado operativo de una linea WhatsApp del tenant (agencia).</summary>
public enum WhatsAppLineStatus
{
    Created,
    Connecting,
    Connected,
    Disconnected,
    Failed,
    Disabled
}

/// <summary>
/// Proveedor de la linea WhatsApp.
/// <list type="bullet">
///   <item><b>Evolution</b>: conexion no oficial via QR (Evolution API). Riesgo de bloqueos por Meta.</item>
///   <item><b>Cloud</b>: WhatsApp Cloud API oficial de Meta (graph.facebook.com). Numeros verificados.</item>
///   <item><b>YCloud</b>: BSP oficial de WhatsApp (api.ycloud.com) con soporte de coexistencia
///   (usar la WhatsApp Business App y la API sobre el mismo numero). Onboarding asistido via Embedded Signup.</item>
/// </list>
/// </summary>
public enum WhatsAppProvider
{
    Evolution = 0,
    Cloud = 1,
    YCloud = 2
}

/// <summary>Estado de la integracion con el servidor Evolution API maestro (Super Admin SaaS).</summary>
public enum EvolutionIntegrationStatus
{
    NotConfigured,
    Configured,
    Validated,
    Error
}

/// <summary>Estrategia de aplicacion de un limite de plan: duro bloquea, blando tolera.</summary>
public enum LimitEnforcementMode
{
    Hard,
    Soft
}

/// <summary>Tipo de cuenta del tenant (agencia), separado del estado.</summary>
public enum TenantKind
{
    Standard,
    Demo,
    Internal,
    Test
}

/// <summary>Ambiente de operacion de la pasarela Wompi maestra.</summary>
public enum WompiEnvironment { Sandbox, Production }

/// <summary>Estado de la integracion con la pasarela Wompi maestra.</summary>
public enum WompiIntegrationStatus { NotConfigured, Configured, Validated, Error }

/// <summary>Resultado del procesamiento de un evento de webhook de Wompi.</summary>
public enum WebhookProcessingStatus { Received, Processed, NoMatchingPayment, InvalidSignature, Duplicate, Error }

/// <summary>Frecuencia de cobro de una suscripcion.</summary>
public enum BillingFrequency { Monthly, Yearly }

/// <summary>Estado de la suscripcion de un tenant.</summary>
public enum SubscriptionStatus { Trialing, Active, PendingPayment, PastDue, GracePeriod, Suspended, Cancelled, AdminException }

/// <summary>Estado de un pago de suscripcion (alineado con Wompi + revision interna).</summary>
public enum PaymentStatus { Pending, Approved, Declined, Voided, Error, NeedsReview }

/// <summary>Distingue acciones humanas de acciones automaticas del sistema (auditoria).</summary>
public enum AuditActorType { Human, System }

/// <summary>Estado de una cuenta social conectada (Modulo 2.3).</summary>
public enum SocialAccountStatus { Connected, Expired, Revoked, Disconnected }

/// <summary>
/// "Sabor" del flujo OAuth de TikTok con el que fue emitido el token. Sirve para dirigir el
/// refresh al endpoint correcto (las dos rutas son incompatibles entre si). Detectado al canjear
/// el auth_code, persistido en <see cref="CubotRedManager.Domain.Entities.SocialAccount"/>.
/// </summary>
public enum TikTokOAuthFlavor
{
    /// <summary>TikTok for Business (business-api.tiktok.com). Refresh en /oauth2/refresh_token/ con app_id + secret + refresh_token.</summary>
    BusinessV13 = 0,
    /// <summary>TikTok Open API v2 (open.tiktokapis.com). Refresh en /v2/oauth/token/ (form-encoded) con client_key + client_secret + grant_type=refresh_token.</summary>
    OpenV2 = 1
}

/// <summary>Prioridad de una tarjeta del tablero Kanban (Modulo 2.7).</summary>
public enum TaskPriority { Low, Normal, High, Urgent }

/// <summary>Estado de una publicacion del calendario editorial (Modulo 2.5).</summary>
public enum PublicationStatus { Draft, Approved, Scheduled, Published, Failed }

/// <summary>Estado de una publicacion en una cuenta destino (aislado por red).</summary>
public enum PublicationTargetStatus { Pending, Published, Failed }

/// <summary>Tipo de mensaje entrante en la bandeja unificada (Modulo 2.6).</summary>
public enum InboxMessageType { DirectMessage, Comment, Mention }

/// <summary>Estado de un mensaje de la bandeja unificada.</summary>
public enum InboxStatus { Unread, Read, Replied, Archived, EscalateToHuman }

/// <summary>
/// Etapa de embudo comercial sobre comentarios entrantes (Pipeline TikTok Manager).
/// Funnel: New -> Contacted -> Qualified -> Converted. Aplica solo a comentarios (Type=Comment).
/// </summary>
public enum CommentPipelineStage { New, Contacted, Qualified, Converted }

// MessageMediaType movido a su propio archivo (Enums/MessageMediaType.cs) al portar el modulo
// de chat desde travels. Ahora incluye None (0) y Location; los usos existentes (Image/Video/
// Audio/Document) siguen siendo validos.

/// <summary>Modo de respuesta del motor de autorespuesta (Modulo 2.11).</summary>
public enum AutoReplyMode
{
    /// <summary>Solo plantillas por palabra clave. Si nada matchea no responde (o usa plantilla por defecto).</summary>
    Template,
    /// <summary>Solo IA (Bandeja IA). Mas costoso pero entiende contexto.</summary>
    Ai,
    /// <summary>Plantilla si matchea, IA si no. Recomendado: barato + cobertura.</summary>
    Mixed
}

/// <summary>Frecuencia de ejecucion del job de autorespuesta por cuenta.</summary>
public enum AutoReplyFrequency
{
    Every15m,
    Every30m,
    Every1h,
    Every2h,
    Every4h,
    Daily,
    /// <summary>Usar la expresion cron de CronCustom.</summary>
    Custom
}

/// <summary>Estado de una ejecucion del job de autorespuesta.</summary>
public enum AutoReplyJobStatus
{
    Ok,
    Warning,
    Error
}

/// <summary>Destinatario del resumen WhatsApp que envia el worker de autorespuesta al terminar
/// cada corrida: un telefono directo o un grupo de WhatsApp.</summary>
public enum AutoReplySummaryTargetType
{
    Phone,
    Group
}

/// <summary>Trigger que dispara una regla de automatizacion (Modulo Automatizaciones).</summary>
public enum AutomationTrigger
{
    NoReply,
    CommentReceived,
    PublicationFailed,
    TokenExpired,
    DailySchedule
}

/// <summary>Accion que ejecuta una regla de automatizacion.</summary>
public enum AutomationAction
{
    CreateTask,
    NotifyOperator,
    EscalateToHuman,
    AssignToTeam,
    SendWhatsAppSummary
}

/// <summary>Tipo de dato de una columna en un DataContainer (tablas dinamicas).</summary>
public enum DataColumnType
{
    Text,
    Number,
    Decimal,
    Date,
    Boolean
}
