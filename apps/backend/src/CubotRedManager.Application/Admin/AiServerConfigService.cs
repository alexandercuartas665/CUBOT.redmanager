using CubotRedManager.Application.Abstractions;
using CubotRedManager.Domain.Entities;
using CubotRedManager.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace CubotRedManager.Application.Admin;

/// <summary>
/// Cuentas maestras de IA de la plataforma (Super Admin). Un registro por proveedor; la API key se
/// cifra con ISecretProtector y nunca se devuelve en claro ni se loggea.
/// </summary>
public sealed class AiServerConfigService : IAiServerConfigService
{
    private static readonly AiProvider[] AllProviders =
        { AiProvider.Claude, AiProvider.Gemini, AiProvider.ChatGpt, AiProvider.DeepSeek };

    private readonly IApplicationDbContext _db;
    private readonly ISecretProtector _secretProtector;
    private readonly IAuditWriter _audit;

    public AiServerConfigService(IApplicationDbContext db, ISecretProtector secretProtector, IAuditWriter audit)
    {
        _db = db;
        _secretProtector = secretProtector;
        _audit = audit;
    }

    public async Task<IReadOnlyList<AiProviderDto>> ListAsync(CancellationToken cancellationToken = default)
    {
        var stored = await _db.AiProviderConfigs.AsNoTracking().ToListAsync(cancellationToken);
        return AllProviders.Select(p => Map(p, stored.FirstOrDefault(c => c.Provider == p))).ToList();
    }

    public async Task<AiProviderDto> SaveAsync(SaveAiProviderRequest request, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var config = await _db.AiProviderConfigs.FirstOrDefaultAsync(c => c.Provider == request.Provider, cancellationToken);
        var isNew = config is null;
        if (config is null)
        {
            config = new AiProviderConfig { Provider = request.Provider };
            _db.AiProviderConfigs.Add(config);
        }

        // La API key solo se re-cifra si llega un valor nuevo; si viene vacia se conserva la actual.
        if (!string.IsNullOrWhiteSpace(request.ApiKey))
        {
            config.ApiKeyEncrypted = _secretProtector.Protect(request.ApiKey.Trim());
        }
        config.Model = string.IsNullOrWhiteSpace(request.Model) ? null : request.Model.Trim();
        config.BaseUrl = string.IsNullOrWhiteSpace(request.BaseUrl) ? null : request.BaseUrl.Trim();
        config.IsEnabled = request.IsEnabled && config.ApiKeyEncrypted is not null;

        // Auditoria SIN la API key.
        _audit.Write(actorUserId, isNew ? "ai.provider.create" : "ai.provider.update",
            nameof(AiProviderConfig), config.Id,
            previousValue: null, newValue: new { config.Provider, config.Model, config.IsEnabled });

        await _db.SaveChangesAsync(cancellationToken);
        return Map(config.Provider, config);
    }

    public async Task<IReadOnlyList<AiProviderOptionDto>> ListEnabledAsync(CancellationToken cancellationToken = default)
    {
        var enabled = await _db.AiProviderConfigs.AsNoTracking()
            .Where(c => c.IsEnabled && c.ApiKeyEncrypted != null)
            .Select(c => new { c.Provider, c.Model })
            .ToListAsync(cancellationToken);

        return enabled.Select(c =>
        {
            var meta = AiProviderCatalog.For(c.Provider);
            // El modelo lo define el Super Admin; si no fijo uno, se usa el por defecto del catalogo.
            var model = string.IsNullOrWhiteSpace(c.Model) ? meta.DefaultModel : c.Model!;
            return new AiProviderOptionDto(c.Provider, meta.DisplayName, model);
        }).ToList();
    }

    private AiProviderDto Map(AiProvider provider, AiProviderConfig? c)
    {
        var meta = AiProviderCatalog.For(provider);
        return new AiProviderDto(
            provider,
            meta.DisplayName,
            c?.Model,
            c?.BaseUrl,
            c?.ApiKeyEncrypted is null ? null : Mask(c.ApiKeyEncrypted),
            c?.ApiKeyEncrypted is not null,
            c?.IsEnabled ?? false,
            meta.DefaultModel,
            meta.Models);
    }

    private string Mask(string encrypted)
    {
        string value;
        try { value = _secretProtector.Unprotect(encrypted); }
        catch { return "(re-ingresar)"; }
        return value.Length <= 4 ? "****" : $"{new string('*', Math.Min(value.Length - 4, 8))}{value[^4..]}";
    }
}
