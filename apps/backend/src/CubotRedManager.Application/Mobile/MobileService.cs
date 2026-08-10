using CubotRedManager.Application.Abstractions;
using CubotRedManager.Application.Common.Auth;
using CubotRedManager.Application.Tenancy;
using CubotRedManager.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CubotRedManager.Application.Mobile;

public sealed class MobileService : IMobileService
{
    // Estados de tenant que permiten operar. Espejado de AuthService.OperableStatuses.
    private static readonly TenantStatus[] OperableStatuses =
    [
        TenantStatus.Trial, TenantStatus.Active, TenantStatus.PastDue
    ];

    // TTL del ApiToken emitido en el login mobile. El operador siempre puede revocarlo desde la
    // UI de tokens en la web (/cuenta/tokens-api). 30d es suficientemente largo para no molestar
    // pero corto para minimizar exposicion si el dispositivo se pierde.
    private static readonly TimeSpan MobileTokenTtl = TimeSpan.FromDays(30);

    private readonly IApplicationDbContext _db;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IApiTokenService _apiTokens;
    private readonly IAmbientTenantOverride _ambient;
    private readonly IAiAgentService _agentService;
    private readonly IPriceSyncService _priceSync;
    private readonly ILogger<MobileService> _logger;

    public MobileService(
        IApplicationDbContext db,
        IPasswordHasher passwordHasher,
        IApiTokenService apiTokens,
        IAmbientTenantOverride ambient,
        IAiAgentService agentService,
        IPriceSyncService priceSync,
        ILogger<MobileService> logger)
    {
        _db = db;
        _passwordHasher = passwordHasher;
        _apiTokens = apiTokens;
        _ambient = ambient;
        _agentService = agentService;
        _priceSync = priceSync;
        _logger = logger;
    }

    public async Task<MobileLoginResponse?> LoginAsync(MobileLoginRequest req, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(req.Email) || string.IsNullOrWhiteSpace(req.Password))
        {
            return null;
        }

        var email = req.Email.Trim().ToLowerInvariant();
        // IgnoreQueryFilters: en este punto no hay tenant activo (login publico).
        var user = await _db.PlatformUsers.IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Email == email, ct);
        if (user is null || string.IsNullOrEmpty(user.PasswordHash)
            || !_passwordHasher.Verify(user.PasswordHash, req.Password))
        {
            // Log discreto: no exponemos si es email invalido vs password invalido (evita enumeracion).
            _logger.LogInformation("Mobile login rechazado para {Email}", email);
            return null;
        }

        // Resolver memberships operables (mismo criterio que AuthService).
        var memberships = await _db.TenantUsers.IgnoreQueryFilters()
            .Where(tu => tu.PlatformUserId == user.Id && tu.Status == PlatformUserStatus.Active)
            .Join(_db.Tenants.IgnoreQueryFilters().Where(t => OperableStatuses.Contains(t.Status)),
                tu => tu.TenantId, t => t.Id,
                (tu, t) => new MobileTenantDto(t.Id, t.Name))
            .ToListAsync(ct);

        if (memberships.Count == 0)
        {
            return new MobileLoginResponse(null, null, null, null, false, Array.Empty<MobileTenantDto>());
        }

        // Elegir tenant activo: si vino explicito en el request, verificarlo; si hay uno solo, usarlo;
        // si hay varios y no se especifico, devolver TenantSelectionRequired=true.
        MobileTenantDto? chosen = null;
        if (req.TenantId is Guid rid)
        {
            chosen = memberships.FirstOrDefault(m => m.Id == rid);
            if (chosen is null) { return null; } // pidio uno al que no pertenece: rechazamos
        }
        else if (memberships.Count == 1)
        {
            chosen = memberships[0];
        }
        else
        {
            return new MobileLoginResponse(
                null, null,
                new MobileUserDto(user.Id, user.Email, user.DisplayName),
                null, true, memberships);
        }

        // Emitir ApiToken. CreateAsync requiere ambient tenant seteado + actorUserId; el tenant
        // se lee de _tenantContext (que a su vez lee el ambient override).
        var label = string.IsNullOrWhiteSpace(req.DeviceLabel) ? "mobile" : $"mobile-{req.DeviceLabel.Trim()}";
        _ambient.Set(chosen.Id, user.Id);
        try
        {
            var created = await _apiTokens.CreateAsync(label, MobileTokenTtl, user.Id, ct);
            if (created is null)
            {
                _logger.LogWarning("Mobile login: ApiTokenService.CreateAsync devolvio null para user {UserId} tenant {TenantId}", user.Id, chosen.Id);
                return null;
            }
            return new MobileLoginResponse(
                created.PlainToken,
                created.Token.ExpiresAt,
                new MobileUserDto(user.Id, user.Email, user.DisplayName),
                chosen,
                false,
                memberships);
        }
        finally
        {
            _ambient.Set(null, null);
        }
    }

    public async Task<MobileDashboardDto> GetDashboardAsync(CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var since = now.AddDays(-7);

        // Todas estas queries corren tenant-scoped (HasQueryFilter esta activo porque el ambient
        // ya lo setearon los endpoints via AuthenticateApiTokenAsync).
        var messagesLast7 = await _db.Messages.Where(m => m.SentAt >= since).ToListAsync(ct);
        var conversationsActive = messagesLast7.Select(m => m.ConversationId).Distinct().Count();
        var inboundCount = messagesLast7.Count(m => m.Direction == MessageDirection.Inbound);
        var outboundCount = messagesLast7.Count(m => m.Direction == MessageDirection.Outbound);

        var agents = await _db.AiAgents.Select(a => new
        {
            a.PaymentEnabled, a.PaymentTokenExpiresAt
        }).ToListAsync(ct);
        var soon = now.AddHours(24);
        var tokensExpiringSoon = agents.Count(a => a.PaymentTokenExpiresAt is DateTimeOffset e && e <= soon && e > now);
        var agentsWithFuxion = agents.Count(a => a.PaymentEnabled);

        var videosSynced = await _db.TikTokVideos.CountAsync(ct);
        // "Pendientes" = conversaciones cuyo ultimo mensaje es inbound (esperando respuesta).
        // El modelo Message aca no tiene ParentMessageId; medimos pendientes a nivel conversacion.
        var pendingConversations = await _db.Conversations
            .Where(c => c.ArchivedAt == null && _db.Messages
                .Where(m => m.ConversationId == c.Id)
                .OrderByDescending(m => m.SentAt)
                .Select(m => m.Direction)
                .FirstOrDefault() == MessageDirection.Inbound)
            .CountAsync(ct);
        var lastTiktokSync = await _db.SocialAccounts
            .Where(s => s.NetworkCode == "tiktok")
            .MaxAsync(s => (DateTimeOffset?)s.LastSyncAt, ct);

        return new MobileDashboardDto(
            ConversationsActive: conversationsActive,
            MessagesLast7Days: messagesLast7.Count,
            InboundLast7Days: inboundCount,
            OutboundLast7Days: outboundCount,
            AgentsConfigured: agents.Count,
            AgentsWithFuxion: agentsWithFuxion,
            TokensExpiringSoon: tokensExpiringSoon,
            VideosSynced: videosSynced,
            PendingComments: pendingConversations,
            LastTikTokSyncAt: lastTiktokSync);
    }

    public async Task<IReadOnlyList<MobileConversationDto>> ListConversationsAsync(int take, CancellationToken ct = default)
    {
        var n = Math.Clamp(take, 1, 100);
        var rows = await _db.Conversations
            .Where(c => c.ArchivedAt == null)
            .OrderByDescending(c => c.LastMessageAt)
            .Take(n)
            .Select(c => new
            {
                c.Id,
                c.ContactName,
                c.ContactPhone,
                c.LastMessageAt,
                LineLabel = _db.WhatsAppLines.Where(l => l.Id == c.WhatsAppLineId).Select(l => (string?)(l.PhoneNumber ?? l.InstanceName)).FirstOrDefault(),
                LastMessage = _db.Messages
                    .Where(m => m.ConversationId == c.Id)
                    .OrderByDescending(m => m.SentAt)
                    .Select(m => new { m.Body, m.Direction })
                    .FirstOrDefault()
            })
            .ToListAsync(ct);

        return rows.Select(r => new MobileConversationDto(
            r.Id,
            r.ContactName ?? r.ContactPhone,
            r.ContactPhone,
            r.LineLabel,
            r.LastMessageAt,
            r.LastMessage?.Body is string b && b.Length > 120 ? b[..120] + "..." : r.LastMessage?.Body,
            r.LastMessage?.Direction.ToString().ToLowerInvariant() ?? ""
        )).ToList();
    }

    public async Task<IReadOnlyList<MobileMessageDto>> ListMessagesAsync(Guid conversationId, int take, CancellationToken ct = default)
    {
        var n = Math.Clamp(take, 1, 200);
        // Ultimos N por SentAt desc, despues re-invierto para orden cronologico ascendente en la UI.
        var rows = await _db.Messages
            .Where(m => m.ConversationId == conversationId)
            .OrderByDescending(m => m.SentAt)
            .Take(n)
            .Select(m => new MobileMessageDto(
                m.Id,
                m.Direction.ToString().ToLowerInvariant(),
                m.Body,
                m.SentAt,
                m.MediaType == MessageMediaType.None ? null : m.MediaType.ToString(),
                m.MediaUrl,
                m.SentByName))
            .ToListAsync(ct);
        return rows.OrderBy(m => m.SentAt).ToList();
    }

    public async Task<IReadOnlyList<MobileAgentDto>> ListAgentsAsync(CancellationToken ct = default)
    {
        return await _db.AiAgents
            .OrderBy(a => a.Name)
            .Select(a => new MobileAgentDto(
                a.Id, a.Name, a.Role, a.IsActive,
                a.PaymentEnabled,
                !string.IsNullOrEmpty(a.PaymentTokenEncrypted),
                a.PaymentTokenExpiresAt,
                a.PaymentLastPriceSyncAt))
            .ToListAsync(ct);
    }

    public async Task<MobileAgentDto?> UpdateFuxionTokenAsync(Guid agentId, string jwt, Guid actorUserId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(jwt)) { return null; }
        // Traer la config actual para pasar los otros campos intactos (SetPaymentConfig es replace).
        var current = await _db.AiAgents.AsNoTracking()
            .Where(a => a.Id == agentId)
            .Select(a => new
            {
                a.PaymentEnabled, a.PaymentUserId, a.PaymentCountry,
                a.PaymentCatalogContainerName, a.PaymentCatalogNameColumn,
                a.PaymentCatalogProductIdColumn, a.PaymentCatalogCountryColumn,
                a.PaymentApiBaseUrl, a.PaymentApiPathTemplate, a.PaymentResponseUrlPath
            })
            .FirstOrDefaultAsync(ct);
        if (current is null) { return null; }
        var req = new SetAgentPaymentConfigRequest(
            Enabled: current.PaymentEnabled || true, // habilitamos si venia apagado; el user pidio renovar token
            UserId: current.PaymentUserId,
            Country: current.PaymentCountry,
            NewToken: jwt.Trim(),
            CatalogContainerName: current.PaymentCatalogContainerName,
            CatalogNameColumn: current.PaymentCatalogNameColumn,
            CatalogProductIdColumn: current.PaymentCatalogProductIdColumn,
            CatalogCountryColumn: current.PaymentCatalogCountryColumn,
            ApiBaseUrl: current.PaymentApiBaseUrl,
            ApiPathTemplate: current.PaymentApiPathTemplate,
            ResponseUrlPath: current.PaymentResponseUrlPath);
        var updated = await _agentService.SetPaymentConfigAsync(agentId, req, actorUserId, ct);
        if (updated is null) { return null; }

        // Devolver el DTO refrescado.
        var a = await _db.AiAgents.AsNoTracking().FirstOrDefaultAsync(x => x.Id == agentId, ct);
        return a is null ? null : new MobileAgentDto(
            a.Id, a.Name, a.Role, a.IsActive,
            a.PaymentEnabled,
            !string.IsNullOrEmpty(a.PaymentTokenEncrypted),
            a.PaymentTokenExpiresAt,
            a.PaymentLastPriceSyncAt);
    }

    public async Task<MobileSyncPricesResult> SyncPricesAsync(Guid agentId, Guid actorUserId, CancellationToken ct = default)
    {
        var r = await _priceSync.SyncPricesAsync(agentId, actorUserId, ct);
        return new MobileSyncPricesResult(r.Ok, r.RowsChecked, r.RowsUpdated, r.RowsAlreadyOk, r.RowsSkipped, r.Errors, r.SyncedAt);
    }
}
