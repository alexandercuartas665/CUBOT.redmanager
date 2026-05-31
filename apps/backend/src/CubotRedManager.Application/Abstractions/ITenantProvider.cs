namespace CubotRedManager.Application.Abstractions;

/// <summary>
/// Resuelve el tenant (agencia) activo de la peticion actual. Lo implementa Infrastructure
/// (desde claims JWT / cookie). El DbContext lo consume para el filtro global por tenant.
/// </summary>
public interface ITenantProvider
{
    /// <summary>Tenant activo. Null cuando no hay agencia en contexto (ej. Super Admin o sin login).</summary>
    Guid? CurrentTenantId { get; }
}
