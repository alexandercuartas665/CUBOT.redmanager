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
