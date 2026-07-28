using CubotRedManager.Domain.Common;
using CubotRedManager.Domain.Enums;

namespace CubotRedManager.Domain.Entities;

/// <summary>
/// Agente de IA configurable de la agencia (capa 3). Entidad TENANT-SCOPED. Define proveedor,
/// modelo, prompt de sistema y si esta en produccion. Los recursos (AiAgentResource) son los
/// archivos/datos que el agente puede usar para responder.
/// </summary>
public class AiAgent : TenantEntity
{
    public string Name { get; set; } = null!;

    /// <summary>Rol/tipo descriptivo (copywriter, bandeja, analista, etc.). Libre.</summary>
    public string? Role { get; set; }

    public AiProvider Provider { get; set; } = AiProvider.Claude;

    /// <summary>Modelo concreto del proveedor (opcional; si vacio se usa el por defecto).</summary>
    public string? Model { get; set; }

    public string SystemPrompt { get; set; } = "";

    /// <summary>En produccion (encendido) o apagado.</summary>
    public bool IsActive { get; set; }

    /// <summary>
    /// MCP de Contenedores de Datos: si esta habilitado, el agente puede leer los DataContainers
    /// del tenant via placeholders {{LIST.CONTAINERS}} y {{CONTAINER:nombre}} en su prompt. Por
    /// defecto FALSO (opt-in explicito por agente para no exponer datos sin querer).
    /// </summary>
    public bool EnableDataContainerMcp { get; set; }

    /// <summary>
    /// Reacciones automaticas (emoji) a los mensajes del cliente, SIN pasar por el LLM (cero
    /// tokens). Si esta activo, el dispatcher reacciona a ~N de cada M mensajes entrantes con
    /// un emoji elegido al azar de ReactionEmojis. Un mensaje que ya tiene reaccion no recibe
    /// otra. Portado desde CUBOT.travels.
    /// </summary>
    public bool ReactionsEnabled { get; set; }

    /// <summary>Numerador de la frecuencia (ej. 3 de cada 4 -> N=3). Se aplica como probabilidad.</summary>
    public int ReactionRatioN { get; set; } = 3;

    /// <summary>Denominador de la frecuencia (ej. 3 de cada 4 -> M=4).</summary>
    public int ReactionRatioM { get; set; } = 4;

    /// <summary>Emojis para reaccionar al azar, separados por coma. Configurable por UI.</summary>
    public string? ReactionEmojis { get; set; }

    // ===== Integracion con FUXION AWARE (generacion de sales-links / power-links) =====
    // El agente puede generar un link de pago llamando al API interno de app-aware.fuxion.com
    // cuando el LLM emite el marker [[link_pago: NOMBRE:qty, ...]]. Ver ADR pendiente sobre
    // integracion no-oficial: FUXION no expone API publica, se usa el endpoint interno del SPA.

    /// <summary>Feature flag: habilita la generacion de sales-links en respuestas del agente.</summary>
    public bool PaymentEnabled { get; set; }

    /// <summary>ID del distribuidor en FUXION AWARE (aparece en la URL de la API, ej. 3238).</summary>
    public string? PaymentUserId { get; set; }

    /// <summary>Codigo ISO2 lowercase del pais del catalogo (pe, co, mx, ...).</summary>
    public string? PaymentCountry { get; set; }

    /// <summary>Token Bearer JWT cifrado con DataProtection (string ciphertext). NUNCA loggear,
    /// NUNCA mostrar en UI. Misma convencion que SocialAccount.AccessTokenEncrypted.</summary>
    public string? PaymentTokenEncrypted { get; set; }

    /// <summary>Fecha de expiracion del JWT (parseado de la claim exp al guardar). UI muestra "expira en X".</summary>
    public DateTimeOffset? PaymentTokenExpiresAt { get; set; }

    /// <summary>Ultima verify-session exitosa. Null si nunca se verifico o si el ultimo intento fallo.</summary>
    public DateTimeOffset? PaymentTokenLastVerifiedAt { get; set; }

    /// <summary>Ultima vez que se notifico al operador (via TenantAlertConfig) que el token esta
    /// por expirar o fue rechazado. Sirve para no spammear: el worker no re-notifica si esto es
    /// dentro de las ultimas 24h. Se resetea automaticamente al guardar un token nuevo.</summary>
    public DateTimeOffset? PaymentTokenExpiryNotifiedAt { get; set; }

    /// <summary>Ultima vez que se ejecuto exitosamente el "Sincronizar precios" (manual desde UI o
    /// via API). Se muestra en la UI del agente para que el operador sepa si el catalogo esta al
    /// dia respecto al portal FUXION.</summary>
    public DateTimeOffset? PaymentLastPriceSyncAt { get; set; }

    /// <summary>Nombre del DataContainer del tenant que contiene el catalogo de productos.</summary>
    public string? PaymentCatalogContainerName { get; set; }

    /// <summary>Columna del contenedor con el nombre del producto (default "nombre").</summary>
    public string? PaymentCatalogNameColumn { get; set; }

    /// <summary>Columna del contenedor con el productId de FUXION (default "productId").</summary>
    public string? PaymentCatalogProductIdColumn { get; set; }

    /// <summary>Columna del contenedor con el pais ISO2 del producto (opcional). Si existe, el
    /// processor usa el pais por fila (permite un mismo agente vender en varios paises). Si es
    /// null o la columna no existe, se cae al PaymentCountry del agente.</summary>
    public string? PaymentCatalogCountryColumn { get; set; }

    // Overrides opcionales para tolerar cambios de FUXION sin redeploy:

    /// <summary>Base URL de la API (default https://api-aware.fuxion.com).</summary>
    public string? PaymentApiBaseUrl { get; set; }

    /// <summary>Path template del endpoint (default /api/products/user/{userId}/generate-power-link).</summary>
    public string? PaymentApiPathTemplate { get; set; }

    /// <summary>JSON path para extraer la URL de la respuesta (default data.url).</summary>
    public string? PaymentResponseUrlPath { get; set; }

    public int SortOrder { get; set; }
}
