using CubotRedManager.Domain.Common;
using CubotRedManager.Domain.Enums;

namespace CubotRedManager.Domain.Entities;

/// <summary>
/// Configuracion de autorespuesta por cuenta social (Modulo 2.11). UNICA por SocialAccount.
/// Vive bajo TikTok Manager (futuro: extensible a otras redes). Tenant-scoped.
/// Guardrails duros: blacklist obligatoria, MaxRepliesPerRun, delay anti-bot, horario activo.
/// </summary>
public class AutoReplyConfig : TenantEntity
{
    public Guid SocialAccountId { get; set; }
    public SocialAccount? SocialAccount { get; set; }

    /// <summary>Si esta apagada el worker no procesa esta cuenta.</summary>
    public bool IsActive { get; set; }

    public AutoReplyMode Mode { get; set; } = AutoReplyMode.Mixed;

    /// <summary>Maximo de respuestas que el job puede enviar en UNA ejecucion (anti-spam TikTok).</summary>
    public int MaxRepliesPerRun { get; set; } = 20;

    /// <summary>Espera aleatoria entre respuestas (segundos). Min &gt;= 2 (regla del vault).</summary>
    public int DelayMinSeconds { get; set; } = 3;
    public int DelayMaxSeconds { get; set; } = 10;

    /// <summary>Palabras (separadas por coma o salto de linea) que descartan respuesta automatica y escalan a humano.</summary>
    public string? BlacklistKeywords { get; set; }

    public AutoReplyFrequency Frequency { get; set; } = AutoReplyFrequency.Every30m;

    /// <summary>Expresion cron si Frequency = Custom.</summary>
    public string? CronCustom { get; set; }

    /// <summary>Bitmask 24 bits (1 bit por hora del dia local). 0xFFFFFF = 24/7. Por defecto 8-20 = 0x1FFF00.</summary>
    public int ActiveHoursMask { get; set; } = 0x1FFF00;

    /// <summary>Bitmask 7 bits (lunes=bit0, ... domingo=bit6). 0x7F = todos los dias.</summary>
    public byte ActiveDaysOfWeekMask { get; set; } = 0x7F;

    /// <summary>Plantilla a usar si Mode=Template y ningun keyword matchea.</summary>
    public string? DefaultTemplate { get; set; }

    /// <summary>
    /// Agente IA del tenant que ejecuta las respuestas cuando Mode=Ai/Mixed. Si es null el
    /// worker elige el primer agente activo del tenant (compatibilidad hacia atras). Cuando
    /// Mode=Mixed y una plantilla matchea, la plantilla se pasa AL AGENTE como sugerencia
    /// para que genere una respuesta variada que respete el sentido (no copia literal).
    /// </summary>
    public Guid? AiAgentId { get; set; }
    public AiAgent? AiAgent { get; set; }

    public ICollection<AutoReplyTemplate> Templates { get; set; } = new List<AutoReplyTemplate>();
}
