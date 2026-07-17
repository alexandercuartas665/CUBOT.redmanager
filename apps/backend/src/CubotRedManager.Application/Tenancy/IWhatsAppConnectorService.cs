namespace CubotRedManager.Application.Tenancy;

/// <summary>
/// Conecta las lineas WhatsApp del tenant activo con el servidor Evolution (maestro de la
/// plataforma o propio de la agencia): crea instancias, entrega el QR, refresca el estado y
/// desconecta. Resuelve el servidor efectivo segun la eleccion de la agencia.
/// </summary>
public interface IWhatsAppConnectorService
{
    Task<EvolutionServerSettingDto> GetServerAsync(CancellationToken cancellationToken = default);

    /// <summary>Define si la agencia usa el servidor maestro o uno propio (URL + API key). Null si no hay tenant.</summary>
    Task<EvolutionServerSettingDto?> SetServerAsync(SetEvolutionServerRequest request, Guid actorUserId, CancellationToken cancellationToken = default);

    /// <summary>Crea/recupera la instancia de la linea en Evolution y devuelve el QR para escanear.</summary>
    Task<LineConnectResult> ConnectLineAsync(Guid lineId, Guid actorUserId, CancellationToken cancellationToken = default);

    /// <summary>Consulta el estado real en Evolution y actualiza el estado de la linea. Null si la linea no existe.</summary>
    Task<WhatsAppLineDto?> RefreshAsync(Guid lineId, Guid actorUserId, CancellationToken cancellationToken = default);

    /// <summary>Cierra sesion y elimina la instancia; deja la linea como desconectada.</summary>
    Task<bool> DisconnectAsync(Guid lineId, Guid actorUserId, CancellationToken cancellationToken = default);

    /// <summary>Elimina la linea por completo: borra la instancia en Evolution y quita la fila del tenant.</summary>
    Task<bool> DeleteLineAsync(Guid lineId, Guid actorUserId, CancellationToken cancellationToken = default);

    /// <summary>(Re)aplica el webhook configurado a todas las lineas conectadas. Devuelve cuantas se actualizaron.</summary>
    Task<int> ApplyWebhookToConnectedLinesAsync(Guid actorUserId, CancellationToken cancellationToken = default);

    /// <summary>Envia un mensaje de prueba desde la linea a un numero (con codigo de pais).</summary>
    Task<LineSendResult> SendTestAsync(Guid lineId, string phone, string text, Guid actorUserId, CancellationToken cancellationToken = default);

    /// <summary>Envia un adjunto (imagen/video/audio/documento) en base64 desde la linea al numero.</summary>
    Task<LineSendResult> SendMediaAsync(Guid lineId, string phone, Domain.Enums.MessageMediaType mediaType, string base64, string? mimeType, string? fileName, string? caption, Guid actorUserId, CancellationToken cancellationToken = default);

    /// <summary>Envia una ubicacion desde la linea al numero.</summary>
    Task<LineSendResult> SendLocationAsync(Guid lineId, string phone, double latitude, double longitude, string? name, Guid actorUserId, CancellationToken cancellationToken = default);

    /// <summary>Lista los grupos de WhatsApp visibles desde la instancia Evolution asociada a la
    /// linea (para poblar dropdowns de destinatario). Devuelve lista vacia + error si aplica.</summary>
    Task<LineGroupsResult> FetchGroupsAsync(Guid lineId, CancellationToken cancellationToken = default);

    /// <summary>Envia una reaccion (emoji) al mensaje entrante identificado por externalId.</summary>
    Task<LineSendResult> SendReactionAsync(Guid lineId, string phone, string externalMessageId, string emoji, CancellationToken cancellationToken = default);

    /// <summary>Descarga el contenido binario (base64) de un mensaje entrante con adjunto, cuando
    /// Evolution no lo incluyo en el webhook. Devuelve Ok=false si no se pudo obtener.</summary>
    Task<InboundMediaResult> FetchInboundMediaAsync(Guid lineId, string externalMessageId, CancellationToken cancellationToken = default);
}

/// <summary>Configuracion de servidor Evolution de la agencia: maestro de la plataforma o propio.</summary>
public sealed record EvolutionServerSettingDto(
    bool UseMasterServer,
    bool MasterReady,
    string? MasterBaseUrl,
    string? OwnBaseUrl,
    string? OwnTokenMasked,
    bool HasOwnToken);

public sealed record SetEvolutionServerRequest(
    bool UseMasterServer,
    string? OwnBaseUrl = null,
    string? OwnApiToken = null);

/// <summary>Resultado de conectar/refrescar una linea: QR a escanear (base64) o error.</summary>
public sealed record LineConnectResult(bool Ok, string? QrBase64, string? Error);

/// <summary>Resultado de un envio de mensaje de prueba. MessageId es el id real de WhatsApp
/// devuelto por el provider (opcional; permite eliminar "para todos" desde la UI).</summary>
public sealed record LineSendResult(bool Ok, string? Error, string? MessageId = null);

/// <summary>Contenido binario descargado de un mensaje entrante con adjunto.</summary>
public sealed record InboundMediaResult(bool Ok, string? Base64, string? MimeType, string? FileName, string? Error);

public sealed record LineGroupDto(string Jid, string Name, int? ParticipantCount);
public sealed record LineGroupsResult(bool Ok, IReadOnlyList<LineGroupDto> Groups, string? Error);
