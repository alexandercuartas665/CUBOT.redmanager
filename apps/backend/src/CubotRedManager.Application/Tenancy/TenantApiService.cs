namespace CubotRedManager.Application.Tenancy;

/// <summary>Info de un campo del embudo para generar el ejemplo de curl dinamicamente.</summary>
public sealed record ApiFieldInfo(string FieldKey, string Label, bool IsArray, string Sample);

/// <summary>Config de la API de ingestion del tenant para Mi cuenta (incluye la key en claro y los campos del embudo).</summary>
public sealed record TenantApiConfigDto(Guid TenantId, string? ApiKey, bool IsEnabled, bool HasKey, DateTimeOffset? LastUsedAt, IReadOnlyList<ApiFieldInfo> Fields);

public interface ITenantApiService
{
    Task<TenantApiConfigDto?> GetAsync(Guid tenantId, CancellationToken cancellationToken = default);
    Task<TenantApiConfigDto> RegenerateAsync(Guid tenantId, Guid actorUserId, CancellationToken cancellationToken = default);
    Task<TenantApiConfigDto?> SetEnabledAsync(Guid tenantId, bool enabled, Guid actorUserId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Stub de la API publica de ingestion (heredada de travels: ingreso de leads al embudo CRM).
/// En redmanager, ese concepto (leads/PipelineFieldDefinition/PipelineStage) no existe todavia,
/// asi que el panel de "API de integracion" en Mi cuenta queda visualmente correcto pero no
/// genera claves reales. Devuelve siempre HasKey=false; al pulsar "Generar API key" devuelve una
/// llave de demostracion en memoria (no se persiste, no se valida en ningun endpoint).
///
/// TODO redmanager: cuando aterrice el modulo de embudo / leads externos, portar el servicio
/// completo desde CubotTravels.Application.Tenancy.TenantApiService y crear las entidades
/// TenantApiConfig + PipelineFieldDefinition + Lead + PipelineStage + LeadActivity, y el
/// endpoint POST /api/public/leads.
/// </summary>
public sealed class TenantApiService : ITenantApiService
{
    public Task<TenantApiConfigDto?> GetAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        // Siempre sin clave: la UI muestra el boton "Generar API key".
        var empty = new TenantApiConfigDto(tenantId, null, false, false, null, Array.Empty<ApiFieldInfo>());
        return Task.FromResult<TenantApiConfigDto?>(empty);
    }

    public Task<TenantApiConfigDto> RegenerateAsync(Guid tenantId, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        // Llave de demostracion: visible una vez en memoria, pero no se persiste ni habilita
        // ningun endpoint publico. Sirve para que el usuario vea como luciria el flujo.
        var demoKey = "cbt_demo_" + Guid.NewGuid().ToString("N");
        var dto = new TenantApiConfigDto(tenantId, demoKey, true, true, DateTimeOffset.UtcNow, Array.Empty<ApiFieldInfo>());
        return Task.FromResult(dto);
    }

    public Task<TenantApiConfigDto?> SetEnabledAsync(Guid tenantId, bool enabled, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var dto = new TenantApiConfigDto(tenantId, null, enabled, false, null, Array.Empty<ApiFieldInfo>());
        return Task.FromResult<TenantApiConfigDto?>(dto);
    }
}
