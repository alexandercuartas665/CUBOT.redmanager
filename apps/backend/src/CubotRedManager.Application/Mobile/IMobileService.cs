namespace CubotRedManager.Application.Mobile;

/// <summary>
/// Fachada de todo lo que expone el backend a la app Android. La app se autentica UNA vez con
/// email + password del usuario de la plataforma web (mismo login que red.cubot.com.co) y recibe
/// un ApiToken opaco (TTL 30 dias) que despues manda como X-Api-Token en todas las llamadas
/// siguientes. El servicio delega en los servicios existentes (AuthService, ApiTokenService,
/// PriceSyncService, AiAgentService) para no duplicar logica.
/// </summary>
public interface IMobileService
{
    /// <summary>Valida email+password contra PlatformUsers y emite un ApiToken de larga vida.
    /// Si el usuario pertenece a varios tenants: si <paramref name="req.TenantId"/> viene,
    /// intenta usar ese; si viene vacio y hay uno solo, lo usa; si hay varios sin especificar,
    /// devuelve TenantSelectionRequired=true y la lista de opciones (la app pide al usuario que
    /// elija y llama de nuevo con el tenantId).</summary>
    Task<MobileLoginResponse?> LoginAsync(MobileLoginRequest req, CancellationToken cancellationToken = default);

    /// <summary>KPIs del tenant activo para pintar la home de la app.</summary>
    Task<MobileDashboardDto> GetDashboardAsync(CancellationToken cancellationToken = default);

    /// <summary>Ultimas conversaciones (WhatsApp) ordenadas por LastMessageAt desc.</summary>
    Task<IReadOnlyList<MobileConversationDto>> ListConversationsAsync(int take, CancellationToken cancellationToken = default);

    /// <summary>Mensajes de una conversacion (orden cronologico) para pintar el detalle.</summary>
    Task<IReadOnlyList<MobileMessageDto>> ListMessagesAsync(Guid conversationId, int take, CancellationToken cancellationToken = default);

    /// <summary>Agentes del tenant con su estado de Pagos FUXION (token presente, expiracion,
    /// ultimo sync de precios). Es el listado que la app usa para elegir a cual renovarle el token.</summary>
    Task<IReadOnlyList<MobileAgentDto>> ListAgentsAsync(CancellationToken cancellationToken = default);

    /// <summary>Atajo: guarda solo el JWT nuevo de FUXION en un agente (delega en AiAgentService.SetPaymentConfig
    /// preservando los otros campos). Devuelve el estado actualizado del agente.</summary>
    Task<MobileAgentDto?> UpdateFuxionTokenAsync(Guid agentId, string jwt, Guid actorUserId, CancellationToken cancellationToken = default);

    /// <summary>Dispara el sync de precios del agente. Es el mismo que el boton de /agentes en la web.</summary>
    Task<MobileSyncPricesResult> SyncPricesAsync(Guid agentId, Guid actorUserId, CancellationToken cancellationToken = default);
}

// ---------- DTOs ----------

public sealed record MobileLoginRequest(string Email, string Password, Guid? TenantId, string? DeviceLabel);

public sealed record MobileLoginResponse(
    string? ApiToken,               // null si TenantSelectionRequired=true (la app tiene que re-loggearse con tenantId elegido)
    DateTimeOffset? ExpiresAt,
    MobileUserDto? User,
    MobileTenantDto? Tenant,
    bool TenantSelectionRequired,
    IReadOnlyList<MobileTenantDto> AvailableTenants);

public sealed record MobileUserDto(Guid Id, string Email, string DisplayName);
public sealed record MobileTenantDto(Guid Id, string Name);

public sealed record MobileDashboardDto(
    int ConversationsActive,          // conversaciones con actividad ultimos 7 dias
    int MessagesLast7Days,
    int InboundLast7Days,
    int OutboundLast7Days,
    int AgentsConfigured,
    int AgentsWithFuxion,
    int TokensExpiringSoon,           // <= 24h de expirar
    int VideosSynced,
    int PendingComments,
    DateTimeOffset? LastTikTokSyncAt);

public sealed record MobileConversationDto(
    Guid Id,
    string ContactName,
    string ContactPhone,
    string? LineLabel,                // p.ej. "WhatsApp Evolution +57..."
    DateTimeOffset? LastMessageAt,
    string? LastMessagePreview,
    string LastMessageDirection);     // "inbound" | "outbound"

public sealed record MobileMessageDto(
    Guid Id,
    string Direction,                 // "inbound" | "outbound"
    string? Body,
    DateTimeOffset SentAt,
    string? MediaType,
    string? MediaUrl,
    string? SentByName);

public sealed record MobileAgentDto(
    Guid Id,
    string Name,
    string? Role,
    bool IsActive,
    bool PaymentEnabled,
    bool PaymentTokenPresent,
    DateTimeOffset? PaymentTokenExpiresAt,
    DateTimeOffset? PaymentLastPriceSyncAt);

public sealed record MobileSyncPricesResult(
    bool Ok,
    int RowsChecked,
    int RowsUpdated,
    int RowsAlreadyOk,
    int RowsSkipped,
    IReadOnlyList<string> Errors,
    DateTimeOffset? SyncedAt);
