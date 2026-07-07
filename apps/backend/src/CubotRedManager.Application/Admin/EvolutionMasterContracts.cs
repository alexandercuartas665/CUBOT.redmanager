using CubotRedManager.Domain.Enums;

namespace CubotRedManager.Application.Admin;

public sealed record EvolutionMasterDto(
    string? BaseUrl,
    string? ApiKeyMasked,
    bool HasApiKey,
    EvolutionIntegrationStatus Status,
    DateTimeOffset? LastValidatedAt);

public sealed record SaveEvolutionMasterRequest(string? BaseUrl, string? ApiKey);

public sealed record EvolutionValidationResult(bool Ok, string Message);

/// <summary>Resultado de comprobar la conexion contra un servidor Evolution API.</summary>
public sealed record EvolutionPingResult(bool Reachable, bool Authenticated, int? StatusCode, string? Detail);

/// <summary>Resultado de operaciones sobre una instancia (crear/conectar). QrBase64 es el codigo QR a escanear.</summary>
public sealed record EvolutionInstanceResult(bool Ok, string? QrBase64, string? State, string? PhoneNumber, string? Error);

public sealed record EvolutionSendResult(bool Ok, string? Error);

public sealed record EvolutionGroupInfo(string Jid, string Name, int? ParticipantCount);

public sealed record EvolutionGroupsResult(bool Ok, IReadOnlyList<EvolutionGroupInfo> Groups, string? Error);

/// <summary>Cliente HTTP del servidor Evolution API. Implementacion en Infrastructure.</summary>
public interface IEvolutionApiClient
{
    Task<EvolutionPingResult> CheckAsync(string baseUrl, string apiKey, CancellationToken cancellationToken = default);
    Task<EvolutionInstanceResult> CreateInstanceAsync(string baseUrl, string apiKey, string instanceName, CancellationToken cancellationToken = default);
    Task<EvolutionInstanceResult> ConnectAsync(string baseUrl, string apiKey, string instanceName, CancellationToken cancellationToken = default);
    Task<EvolutionInstanceResult> GetStateAsync(string baseUrl, string apiKey, string instanceName, CancellationToken cancellationToken = default);
    Task<bool> DeleteInstanceAsync(string baseUrl, string apiKey, string instanceName, CancellationToken cancellationToken = default);
    Task<EvolutionSendResult> SendTextAsync(string baseUrl, string apiKey, string instanceName, string phone, string text, CancellationToken cancellationToken = default);
    Task<EvolutionSendResult> SendMediaAsync(string baseUrl, string apiKey, string instanceName, string phone, string mediatype, string base64, string? mimeType, string? fileName, string? caption, CancellationToken cancellationToken = default);
    Task<EvolutionSendResult> SendAudioAsync(string baseUrl, string apiKey, string instanceName, string phone, string base64, CancellationToken cancellationToken = default);
    Task<EvolutionSendResult> SendLocationAsync(string baseUrl, string apiKey, string instanceName, string phone, double latitude, double longitude, string? name, string? address, CancellationToken cancellationToken = default);
    Task<EvolutionSendResult> SetWebhookAsync(string baseUrl, string apiKey, string instanceName, string webhookUrl, string token, CancellationToken cancellationToken = default);

    /// <summary>Lista los grupos de WhatsApp de la instancia (dropdown de destinatarios de resumen).</summary>
    Task<EvolutionGroupsResult> FetchGroupsAsync(string baseUrl, string apiKey, string instanceName, CancellationToken cancellationToken = default);
}

public interface IEvolutionMasterConfigService
{
    Task<EvolutionMasterDto?> GetAsync(CancellationToken cancellationToken = default);
    Task<EvolutionMasterDto> SaveAsync(SaveEvolutionMasterRequest request, Guid actorUserId, CancellationToken cancellationToken = default);

    /// <summary>Valida contra el servidor real (reachability + API key). Null si no hay config.</summary>
    Task<EvolutionValidationResult?> ValidateAsync(Guid actorUserId, CancellationToken cancellationToken = default);
}
