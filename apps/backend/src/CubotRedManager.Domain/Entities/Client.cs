using CubotRedManager.Domain.Common;

namespace CubotRedManager.Domain.Entities;

/// <summary>
/// Marca / empresa cuyas redes gestiona la agencia. NO es el inquilino SaaS.
/// Tenant-scoped: pertenece a una agencia.
/// </summary>
public class Client : TenantEntity
{
    public string Name { get; set; } = string.Empty;
    public string? ContactName { get; set; }
    public string? ContactEmail { get; set; }
    public string? ContactPhone { get; set; }
    public string? Industry { get; set; }
    public string? BrandLogoUrl { get; set; }
    public string? BrandColorsJson { get; set; }

    /// <summary>Manual de marca (markdown). Input principal del agente Copywriter.</summary>
    public string? BrandToneNotes { get; set; }
    public string? TimeZone { get; set; }
    public bool IsActive { get; set; } = true;

    public ICollection<UserClientLink> AssignedOperators { get; set; } = new List<UserClientLink>();
}
