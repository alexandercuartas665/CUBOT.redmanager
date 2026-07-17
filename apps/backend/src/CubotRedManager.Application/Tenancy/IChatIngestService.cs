namespace CubotRedManager.Application.Tenancy;

public enum ChatIngestResult
{
    Unauthorized,
    Accepted,
    Duplicate
}

/// <summary>
/// Recibe mensajes entrantes de WhatsApp (Evolution/Cloud/YCloud) y los persiste en Conversation + Message.
/// El AgentDispatcher se dispara aparte (Fase 3). Portado desde CUBOT.travels.
/// </summary>
public interface IChatIngestService
{
    /// <summary>Endpoint por-tenant: valida X-Webhook-Token contra TenantEvolutionConfig.ApiTokenEncrypted.</summary>
    Task<ChatIngestResult> IngestAsync(Guid tenantId, string? providedToken, IngestMessageRequest payload, CancellationToken ct = default);

    /// <summary>
    /// Endpoint crudo: ya se valido el token global (EvolutionMasterConfig.WebhookToken). Solo persiste.
    /// Marca <paramref name="enqueueDispatch"/> = true para (en Fase 3) despertar al AgentDispatcher.
    /// </summary>
    Task<ChatIngestResult> IngestTrustedAsync(Guid tenantId, IngestMessageRequest payload, CancellationToken ct = default, bool enqueueDispatch = true);
}
