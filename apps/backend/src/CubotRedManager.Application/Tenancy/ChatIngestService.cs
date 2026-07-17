using System.Security.Cryptography;
using System.Text;
using CubotRedManager.Application.Abstractions;
using CubotRedManager.Domain.Entities;
using CubotRedManager.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CubotRedManager.Application.Tenancy;

/// <summary>
/// Persiste el mensaje entrante en Conversation + Message. Portado desde CUBOT.travels con dos
/// adaptaciones para redmanager:
/// - Sin IChatBroadcaster: se deja hueco (SignalR llegara en Fase 2 si se necesita UI en vivo).
/// - Sin IAgentDispatchQueue: se deja hueco para Fase 3; hoy solo persistimos y logueamos.
/// </summary>
public sealed class ChatIngestService : IChatIngestService
{
    private readonly IApplicationDbContext _db;
    private readonly ISecretProtector _secretProtector;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ChatIngestService> _logger;

    public ChatIngestService(IApplicationDbContext db, ISecretProtector secretProtector,
        TimeProvider timeProvider, ILogger<ChatIngestService> logger)
    {
        _db = db;
        _secretProtector = secretProtector;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<ChatIngestResult> IngestAsync(Guid tenantId, string? providedToken, IngestMessageRequest payload, CancellationToken ct = default)
    {
        // Valida el token vs TenantEvolutionConfig.ApiTokenEncrypted (comparacion en tiempo constante).
        var cfg = await _db.TenantEvolutionConfigs.IgnoreQueryFilters()
            .Where(c => c.TenantId == tenantId)
            .Select(c => new { c.ApiTokenEncrypted })
            .FirstOrDefaultAsync(ct);
        if (cfg is null || string.IsNullOrWhiteSpace(cfg.ApiTokenEncrypted) || string.IsNullOrWhiteSpace(providedToken))
        {
            return ChatIngestResult.Unauthorized;
        }
        var expected = _secretProtector.Unprotect(cfg.ApiTokenEncrypted!);
        if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(providedToken)))
        {
            return ChatIngestResult.Unauthorized;
        }
        return await IngestTrustedAsync(tenantId, payload, ct);
    }

    public async Task<ChatIngestResult> IngestTrustedAsync(Guid tenantId, IngestMessageRequest payload, CancellationToken ct = default, bool enqueueDispatch = true)
    {
        // Dedupe: si ya persistimos este ExternalMessageId, no re-crear.
        var duplicate = await _db.Messages.IgnoreQueryFilters()
            .AnyAsync(m => m.TenantId == tenantId && m.ExternalId == payload.ExternalMessageId, ct);
        if (duplicate)
        {
            return ChatIngestResult.Duplicate;
        }

        var sentAt = payload.SentAt ?? _timeProvider.GetUtcNow();

        // Conversacion por (TenantId, ContactPhone). Si no existe, la creamos.
        var conversation = await _db.Conversations.IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.TenantId == tenantId && c.ContactPhone == payload.ContactPhone, ct);
        if (conversation is null)
        {
            conversation = new Conversation
            {
                TenantId = tenantId,
                ContactPhone = payload.ContactPhone,
                ContactName = payload.ContactName,
                WhatsAppLineId = payload.WhatsAppLineId,
                LastMessageAt = sentAt
            };
            _db.Conversations.Add(conversation);
        }
        else
        {
            // Mover LastMessageAt solo hacia adelante (guard contra timestamps historicos).
            if (conversation.LastMessageAt is null || sentAt > conversation.LastMessageAt)
            {
                conversation.LastMessageAt = sentAt;
            }
            // Desarchivar por mensaje entrante nuevo.
            conversation.ArchivedAt = null;
            // Rellenar name/line si estaban null.
            if (string.IsNullOrWhiteSpace(conversation.ContactName) && !string.IsNullOrWhiteSpace(payload.ContactName))
            {
                conversation.ContactName = payload.ContactName;
            }
            if (conversation.WhatsAppLineId is null && payload.WhatsAppLineId is not null)
            {
                conversation.WhatsAppLineId = payload.WhatsAppLineId;
            }
        }

        var message = new Message
        {
            TenantId = tenantId,
            ConversationId = conversation.Id,
            Direction = MessageDirection.Inbound,
            ExternalId = payload.ExternalMessageId,
            Body = payload.Body ?? "",
            MessageType = string.IsNullOrWhiteSpace(payload.MessageType) ? "text" : payload.MessageType!,
            SentAt = sentAt,
            MediaType = payload.MediaType,
            MediaUrl = payload.MediaUrl,
            MediaMimeType = payload.MediaMimeType
        };
        _db.Messages.Add(message);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Chat ingest OK: tenant={Tenant} conv={Conv} msg={Msg} phone={Phone}",
            tenantId, conversation.Id, message.Id, payload.ContactPhone);

        // TODO(Fase 3): cuando exista IAgentDispatchQueue, encolar aqui si enqueueDispatch && WhatsAppLineId.
        _ = enqueueDispatch;
        return ChatIngestResult.Accepted;
    }
}
