namespace CubotRedManager.Application.Admin;

/// <summary>Vista de la marca de la plataforma para pintar el login y otras superficies publicas.</summary>
public sealed record PlatformBrandingDto(
    string PlatformName,
    string? Tagline,
    string? LoginLogoUrl,
    string? LoginHeadline,
    string? LoginSubtext)
{
    /// <summary>Valores por defecto cuando aun no se ha configurado la marca.</summary>
    public static PlatformBrandingDto Default => new(
        "CUBOT.redmanager",
        "Gestor de redes sociales",
        null,
        "Rapido, facil y seguro",
        "La plataforma SaaS para agencias de marketing digital: gestiona Meta, TikTok y YouTube de tus clientes con agentes de IA.");
}

public sealed record SaveBrandingRequest(
    string PlatformName,
    string? Tagline,
    string? LoginLogoUrl,
    string? LoginHeadline,
    string? LoginSubtext);

public interface IPlatformBrandingService
{
    Task<PlatformBrandingDto> GetAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(SaveBrandingRequest request, Guid actorUserId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Marca de la plataforma. TODO redmanager: travels persiste esto en una tabla PlatformBranding;
/// aqui aun no existe esa entidad, asi que devolvemos siempre los valores por defecto y SaveAsync
/// es no-op. Cuando se agregue la tabla, replicar el servicio de travels (audit + ISecretProtector).
/// </summary>
public sealed class PlatformBrandingService : IPlatformBrandingService
{
    public Task<PlatformBrandingDto> GetAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(PlatformBrandingDto.Default);

    public Task SaveAsync(SaveBrandingRequest request, Guid actorUserId, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
