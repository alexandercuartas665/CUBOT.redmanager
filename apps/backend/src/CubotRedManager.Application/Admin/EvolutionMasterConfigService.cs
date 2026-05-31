using CubotRedManager.Application.Abstractions;
using CubotRedManager.Domain.Entities;
using CubotRedManager.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace CubotRedManager.Application.Admin;

/// <summary>
/// Servidor Evolution API maestro de la plataforma (singleton global). La API key se cifra con
/// ISecretProtector y nunca se devuelve en claro ni se loggea. La validacion consulta el servidor real.
/// </summary>
public sealed class EvolutionMasterConfigService : IEvolutionMasterConfigService
{
    private readonly IApplicationDbContext _db;
    private readonly ISecretProtector _secretProtector;
    private readonly IEvolutionApiClient _client;
    private readonly IAuditWriter _audit;

    public EvolutionMasterConfigService(IApplicationDbContext db, ISecretProtector secretProtector, IEvolutionApiClient client, IAuditWriter audit)
    {
        _db = db;
        _secretProtector = secretProtector;
        _client = client;
        _audit = audit;
    }

    public async Task<EvolutionMasterDto?> GetAsync(CancellationToken cancellationToken = default)
    {
        var config = await _db.EvolutionMasterConfigs.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        return config is null ? null : Map(config);
    }

    public async Task<EvolutionMasterDto> SaveAsync(SaveEvolutionMasterRequest request, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var config = await _db.EvolutionMasterConfigs.FirstOrDefaultAsync(cancellationToken);
        var isNew = config is null;
        if (config is null)
        {
            config = new EvolutionMasterConfig();
            _db.EvolutionMasterConfigs.Add(config);
        }

        config.BaseUrl = NormalizeBaseUrl(request.BaseUrl);
        if (!string.IsNullOrWhiteSpace(request.ApiKey))
        {
            config.ApiKeyEncrypted = _secretProtector.Protect(request.ApiKey.Trim());
        }

        config.Status = HasCredentials(config) ? EvolutionIntegrationStatus.Configured : EvolutionIntegrationStatus.NotConfigured;
        config.LastValidatedAt = null;

        _audit.Write(actorUserId, isNew ? "evolution.master.create" : "evolution.master.update",
            nameof(EvolutionMasterConfig), config.Id,
            previousValue: null, newValue: new { config.BaseUrl });

        await _db.SaveChangesAsync(cancellationToken);
        return Map(config);
    }

    public async Task<EvolutionValidationResult?> ValidateAsync(Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var config = await _db.EvolutionMasterConfigs.FirstOrDefaultAsync(cancellationToken);
        if (config is null)
        {
            return null;
        }
        if (string.IsNullOrWhiteSpace(config.BaseUrl) || string.IsNullOrWhiteSpace(config.ApiKeyEncrypted))
        {
            return await FinishValidation(config, actorUserId, false, "Falta la URL del servidor y/o la API key.", cancellationToken);
        }

        string apiKey;
        try
        {
            apiKey = _secretProtector.Unprotect(config.ApiKeyEncrypted);
        }
        catch
        {
            return await FinishValidation(config, actorUserId, false, "La API key esta cifrada con una version anterior. Vuelve a guardarla.", cancellationToken);
        }

        var ping = await _client.CheckAsync(config.BaseUrl, apiKey, cancellationToken);
        var (ok, message) = ping switch
        {
            { Reachable: false } => (false, $"No se pudo conectar con el servidor ({ping.Detail})."),
            { Authenticated: false } => (false, $"El servidor responde pero la API key no es valida (HTTP {ping.StatusCode})."),
            _ => (true, "Conexion exitosa: el servidor responde y la API key es valida.")
        };
        return await FinishValidation(config, actorUserId, ok, message, cancellationToken);
    }

    private async Task<EvolutionValidationResult> FinishValidation(EvolutionMasterConfig config, Guid actorUserId, bool ok, string message, CancellationToken ct)
    {
        config.Status = ok ? EvolutionIntegrationStatus.Validated : EvolutionIntegrationStatus.Error;
        config.LastValidatedAt = DateTimeOffset.UtcNow;
        _audit.Write(actorUserId, "evolution.master.validate", nameof(EvolutionMasterConfig), config.Id,
            previousValue: null, newValue: new { Result = ok ? "Validated" : "Error", message });
        await _db.SaveChangesAsync(ct);
        return new EvolutionValidationResult(ok, message);
    }

    private static bool HasCredentials(EvolutionMasterConfig c) =>
        !string.IsNullOrWhiteSpace(c.BaseUrl) && !string.IsNullOrWhiteSpace(c.ApiKeyEncrypted);

    private static string? NormalizeBaseUrl(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) { return null; }
        var url = raw.Trim().TrimEnd('/');
        if (url.EndsWith("/manager", StringComparison.OrdinalIgnoreCase))
        {
            url = url[..^"/manager".Length];
        }
        return url.TrimEnd('/');
    }

    private EvolutionMasterDto Map(EvolutionMasterConfig c) =>
        new(c.BaseUrl,
            c.ApiKeyEncrypted is null ? null : Mask(c.ApiKeyEncrypted),
            c.ApiKeyEncrypted is not null,
            c.Status,
            c.LastValidatedAt);

    private string Mask(string encrypted)
    {
        string value;
        try { value = _secretProtector.Unprotect(encrypted); }
        catch { return "(re-ingresar)"; }
        return value.Length <= 4 ? "****" : $"{new string('*', Math.Min(value.Length - 4, 8))}{value[^4..]}";
    }
}
