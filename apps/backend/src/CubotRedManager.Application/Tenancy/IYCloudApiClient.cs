namespace CubotRedManager.Application.Tenancy;

/// <summary>
/// Cliente HTTP de YCloud (BSP oficial de WhatsApp, api.ycloud.com v2). Cubre envio de
/// mensajes y gestion de plantillas HSM. La autenticacion es una sola API key que se entrega
/// por llamada (header X-API-Key) ya descifrada por el caller: el cliente NO conoce la entidad
/// WhatsAppLine ni el ISecretProtector. Los errores HTTP se devuelven como result, sin throw.
///
/// Referencia: https://docs.ycloud.com/reference (send: POST /whatsapp/messages,
/// plantillas: POST/GET /whatsapp/templates). La implementacion es tolerante a variaciones de
/// campos entre versiones del OpenAPI oficial.
/// </summary>
public interface IYCloudApiClient
{
    /// <summary>Valida la API key (y opcionalmente el numero) consultando los phone numbers de
    /// la cuenta. Devuelve el numero/WABA detectado o un error si la key es invalida.</summary>
    Task<YCloudCheckResult> CheckAsync(string apiKey, string? phoneNumber, CancellationToken cancellationToken = default);

    /// <summary>Envia texto. <paramref name="fromPhone"/> es el sender registrado en YCloud y
    /// <paramref name="toPhone"/> el destino, ambos en formato internacional sin "+".</summary>
    Task<YCloudSendResult> SendTextAsync(string apiKey, string fromPhone, string toPhone, string text, CancellationToken cancellationToken = default);

    /// <summary>Envia media (imagen/video/audio/documento) por URL publica.</summary>
    Task<YCloudSendResult> SendMediaAsync(string apiKey, string fromPhone, string toPhone, YCloudMediaKind kind, string mediaUrl, string? caption, string? fileName, CancellationToken cancellationToken = default);

    /// <summary>Envia una plantilla HSM aprobada. <paramref name="bodyParams"/> son los valores de las
    /// variables {{1}}...{{N}} del body EN ORDEN. <paramref name="headerMediaType"/> (image/document/video)
    /// y <paramref name="headerMediaUrl"/> solo cuando la plantilla fue creada con header de media. Los
    /// <paramref name="urlButtons"/> aplican a plantillas con botones URL dinamicos (parametro en el path).</summary>
    Task<YCloudSendResult> SendTemplateAsync(string apiKey, string fromPhone, string toPhone, string templateName, string language, IReadOnlyList<string> bodyParams, string? headerMediaType = null, string? headerMediaUrl = null, string? headerMediaFileName = null, IReadOnlyList<WhatsAppUrlButtonParam>? urlButtons = null, CancellationToken cancellationToken = default);

    /// <summary>Envia una reaccion (emoji) al mensaje identificado por <paramref name="messageId"/> (wamid).
    /// Un emoji vacio quita la reaccion previa (comportamiento nativo de WhatsApp).</summary>
    Task<YCloudSendResult> SendReactionAsync(string apiKey, string fromPhone, string toPhone, string messageId, string emoji, CancellationToken cancellationToken = default);

    // === Plantillas HSM (F2) =================================================
    /// <summary>Crea/somete una plantilla a revision de Meta a traves de YCloud.
    /// <paramref name="components"/> es el arreglo de componentes ya armado por el servicio
    /// (HEADER/BODY/FOOTER/BUTTONS con sus example values), serializable a JSON.</summary>
    Task<YCloudTemplateResult> CreateTemplateAsync(string apiKey, string wabaId, string name, string language, string category, object components, int? ttlSeconds, CancellationToken cancellationToken = default);

    /// <summary>Lista las plantillas de una WABA con su estado actual (para sincronizar).</summary>
    Task<YCloudTemplateListResult> ListTemplatesAsync(string apiKey, string wabaId, CancellationToken cancellationToken = default);
}

public enum YCloudMediaKind { Image, Video, Audio, Document }

public sealed record YCloudCheckResult(bool IsValid, string? VerifiedPhone, string? WabaId, string? Error);

public sealed record YCloudSendResult(bool IsSuccess, string? MessageId, string? Error);

/// <summary>Resultado de crear/someter una plantilla. <paramref name="Status"/> suele venir
/// PENDING al crear (Meta la revisa); APPROVED inmediato solo para AUTHENTICATION.</summary>
public sealed record YCloudTemplateResult(bool IsSuccess, string? Id, string? Status, string? Error);

public sealed record YCloudTemplateStatus(string Name, string? Language, string Status, string? Id, string? RejectedReason);

public sealed record YCloudTemplateListResult(bool IsSuccess, IReadOnlyList<YCloudTemplateStatus> Items, string? Error);

/// <summary>Parametro de un boton URL dinamico de una plantilla HSM. <paramref name="Index"/> es la
/// posicion del boton (1-based) dentro del arreglo de buttons de la plantilla. <paramref name="Text"/>
/// es el sufijo que WhatsApp inyecta en el placeholder {{1}} de la URL del boton.</summary>
public sealed record WhatsAppUrlButtonParam(int Index, string Text);
