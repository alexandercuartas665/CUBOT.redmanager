using CubotRedManager.Application.Abstractions;
using CubotRedManager.Domain.Entities;
using CubotRedManager.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CubotRedManager.Application.Tenancy;

public sealed class ChatService : IChatService
{
    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IWhatsAppConnectorService _connector;
    private readonly IChatBroadcaster _broadcaster;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ChatService> _logger;

    public ChatService(
        IApplicationDbContext db,
        ITenantContext tenant,
        IWhatsAppConnectorService connector,
        IChatBroadcaster broadcaster,
        TimeProvider timeProvider,
        ILogger<ChatService> logger)
    {
        _db = db;
        _tenant = tenant;
        _connector = connector;
        _broadcaster = broadcaster;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<ChatConversationDto?> GetOrCreateForLeadAsync(Guid leadId, CancellationToken ct = default)
    {
        if (_tenant.TenantId is not Guid tenantId) { return null; }
        var lead = await _db.Leads.AsNoTracking()
            .Where(l => l.Id == leadId)
            .Select(l => new { l.ContactPhone, l.ContactName })
            .FirstOrDefaultAsync(ct);
        if (lead is null || string.IsNullOrWhiteSpace(lead.ContactPhone))
        {
            return null;
        }
        return await GetOrCreateForPhoneAsync(lead.ContactPhone, lead.ContactName, ct);
    }

    public async Task<ChatConversationDto?> GetOrCreateForPhoneAsync(string contactPhone, string? contactName, CancellationToken ct = default)
    {
        if (_tenant.TenantId is not Guid tenantId) { return null; }
        if (string.IsNullOrWhiteSpace(contactPhone)) { return null; }
        var phone = new string(contactPhone.Where(char.IsDigit).ToArray());
        if (string.IsNullOrWhiteSpace(phone)) { return null; }

        var conv = await _db.Conversations
            .FirstOrDefaultAsync(c => c.ContactPhone == phone, ct);
        if (conv is null)
        {
            conv = new Conversation
            {
                TenantId = tenantId,
                ContactPhone = phone,
                ContactName = contactName,
                LastMessageAt = _timeProvider.GetUtcNow()
            };
            _db.Conversations.Add(conv);
            await _db.SaveChangesAsync(ct);
        }
        else if (!string.IsNullOrWhiteSpace(contactName) && string.IsNullOrWhiteSpace(conv.ContactName))
        {
            conv.ContactName = contactName;
            await _db.SaveChangesAsync(ct);
        }
        return new ChatConversationDto(conv.Id, conv.ContactPhone, conv.ContactName, conv.WhatsAppLineId, conv.LastMessageAt);
    }

    public async Task<IReadOnlyList<MessageDto>> ListMessagesAsync(Guid conversationId, int take = 100, CancellationToken ct = default)
    {
        if (take <= 0 || take > 500) { take = 100; }
        var items = await _db.Messages
            .AsNoTracking()
            .Where(m => m.ConversationId == conversationId)
            .OrderByDescending(m => m.SentAt)
            .Take(take)
            .ToListAsync(ct);
        items.Reverse();
        return items.Select(m => new MessageDto(
            m.Id, m.ConversationId, m.Direction, m.Body ?? string.Empty,
            m.MessageType, m.SentAt, m.MediaType, m.MediaUrl, m.MediaMimeType,
            m.SentByName, m.Reaction)).ToList();
    }

    public async Task<ChatSendResult> SendViaLineAsync(Guid conversationId, Guid whatsAppLineId, string body, Guid actorUserId, string? actorDisplayName = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return new ChatSendResult(false, null, "Mensaje vacio.");
        }
        if (_tenant.TenantId is not Guid tenantId)
        {
            return new ChatSendResult(false, null, "Sin tenant activo.");
        }
        var conv = await _db.Conversations.FirstOrDefaultAsync(c => c.Id == conversationId, ct);
        if (conv is null)
        {
            return new ChatSendResult(false, null, "Conversacion no encontrada.");
        }

        // 1) Enviar al provider (Emulator -> no-op; Evolution/Cloud/YCloud -> API real).
        var sendResult = await _connector.SendTestAsync(whatsAppLineId, conv.ContactPhone, body.Trim(), actorUserId, ct);
        if (!sendResult.Ok)
        {
            _logger.LogWarning("ChatService: envio fallo. convId={ConvId} lineId={LineId} error={Error}",
                conversationId, whatsAppLineId, sendResult.Error);
            return new ChatSendResult(false, null, sendResult.Error ?? "Fallo al enviar.");
        }

        // 2) Persistir outbound. El webhook del provider puede traer su propio ExternalId mas tarde;
        //    aqui generamos uno local con prefijo para distinguirlo.
        var now = _timeProvider.GetUtcNow();
        var msg = new Message
        {
            TenantId = tenantId,
            ConversationId = conversationId,
            Direction = MessageDirection.Outbound,
            Body = body.Trim(),
            MessageType = "text",
            SentAt = now,
            ExternalId = sendResult.MessageId ?? $"mgr-{Guid.NewGuid():N}",
            SentByTenantUserId = actorUserId == Guid.Empty ? null : actorUserId,
            SentByName = actorDisplayName
        };
        _db.Messages.Add(msg);

        // Actualizar LastMessageAt y pegar la linea al conversation si estaba vacia.
        if (conv.LastMessageAt is null || now > conv.LastMessageAt) { conv.LastMessageAt = now; }
        if (conv.WhatsAppLineId is null) { conv.WhatsAppLineId = whatsAppLineId; }
        await _db.SaveChangesAsync(ct);

        var dto = new MessageDto(msg.Id, msg.ConversationId, msg.Direction, msg.Body,
            msg.MessageType, msg.SentAt, msg.MediaType, msg.MediaUrl, msg.MediaMimeType,
            msg.SentByName, msg.Reaction);

        // 3) Broadcast (NoOp si no hay SignalR; SignalRChatBroadcaster empuja al hub /hubs/chat).
        try { await _broadcaster.MessageAddedAsync(tenantId, conversationId, dto, ct); } catch { /* best-effort */ }

        return new ChatSendResult(true, dto, null);
    }
}
