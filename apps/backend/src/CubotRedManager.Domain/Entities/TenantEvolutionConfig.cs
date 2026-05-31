using CubotRedManager.Domain.Common;

namespace CubotRedManager.Domain.Entities;

/// <summary>
/// Configuracion de Evolution API de una agencia. Entidad TENANT-SCOPED. El token se guarda cifrado
/// (ApiTokenEncrypted) y nunca se expone completo ni se loggea.
/// </summary>
public class TenantEvolutionConfig : TenantEntity
{
    /// <summary>Si true, la agencia usa el servidor Evolution maestro de la plataforma; si false, el suyo propio.</summary>
    public bool UseMasterServer { get; set; } = true;

    /// <summary>URL del servidor propio (solo cuando UseMasterServer = false).</summary>
    public string? BaseUrl { get; set; }
    public string? InstanceName { get; set; }

    /// <summary>API key del servidor propio cifrada (solo cuando UseMasterServer = false).</summary>
    public string? ApiTokenEncrypted { get; set; }
    public string? WebhookUrl { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset? LastValidatedAt { get; set; }
}
