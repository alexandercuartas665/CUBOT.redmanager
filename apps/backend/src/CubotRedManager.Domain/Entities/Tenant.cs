using CubotRedManager.Domain.Common;
using CubotRedManager.Domain.Enums;

namespace CubotRedManager.Domain.Entities;

/// <summary>
/// Agencia de marketing = inquilino SaaS. Entidad GLOBAL (no tenant-scoped):
/// la administra el Super Admin sobre todas las agencias.
/// </summary>
public class Tenant : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string? LegalName { get; set; }
    public string? TaxId { get; set; }
    public string? Country { get; set; }
    public string? Currency { get; set; }
    public string? TimeZone { get; set; }
    public string? LogoUrl { get; set; }
    public string? BrandColorsJson { get; set; }
    public TenantStatus Status { get; set; } = TenantStatus.Trial;
    public TenantKind Kind { get; set; } = TenantKind.Standard;

    public ICollection<TenantUser> TenantUsers { get; set; } = new List<TenantUser>();
    public ICollection<Client> Clients { get; set; } = new List<Client>();
}
