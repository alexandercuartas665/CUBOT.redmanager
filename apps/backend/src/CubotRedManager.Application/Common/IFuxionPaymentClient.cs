namespace CubotRedManager.Application.Common;

/// <summary>
/// Cliente HTTP para el API de app-aware.fuxion.com. Genera "power-links" (sales-links) que
/// un cliente puede abrir para comprar productos FUXION con el distribuidor del token.
///
/// IMPORTANTE: esto es integracion NO-OFICIAL. FUXION no publica esta API; se descubrio
/// inspeccionando la SPA de app-aware.fuxion.com. Todo es configurable via override en
/// AiAgent.PaymentApiBaseUrl / PathTemplate / ResponseUrlPath para tolerar cambios sin
/// redeploy.
/// </summary>
public interface IFuxionPaymentClient
{
    /// <summary>Llama POST /api/products/user/{userId}/generate-power-link y devuelve la URL
    /// del sales-link generado, o un error tipificado. Nunca lanza excepciones al caller.</summary>
    Task<FuxionGenerateLinkResult> GenerateSalesLinkAsync(FuxionGenerateLinkRequest request, CancellationToken cancellationToken = default);

    /// <summary>Llama POST /api/auth/user/verify-session con el Bearer token y devuelve si el
    /// token esta vivo (2xx) o rechazado (401/403). Usado por el worker de vigilancia para
    /// detectar tokens caducados antes de que un cliente intente pagar. No lanza excepciones.</summary>
    Task<FuxionVerifySessionResult> VerifySessionAsync(string baseUrl, string bearerToken, CancellationToken cancellationToken = default);

    /// <summary>Llama GET /api/products?country=XX&amp;language=es y devuelve el diccionario
    /// itemCode -> price del catalogo actual de FUXION para ese pais. Usado por el "Sincronizar
    /// precios" del agente para mantener el DataContainer alineado con el portal. No lanza excepciones.</summary>
    Task<FuxionCatalogResult> GetProductsByCountryAsync(string baseUrl, string bearerToken, string countryIso2, CancellationToken cancellationToken = default);
}

public sealed record FuxionCatalogResult(
    bool Ok,
    IReadOnlyDictionary<string, decimal> Prices, // itemCode -> price
    string? ErrorDetail,
    int? HttpStatus)
{
    public static FuxionCatalogResult Success(IReadOnlyDictionary<string, decimal> prices) =>
        new(true, prices, null, null);
    public static FuxionCatalogResult Failure(string detail, int? status = null) =>
        new(false, new Dictionary<string, decimal>(), detail, status);
}

public sealed record FuxionSalesLinkItem(string ItemId, int Amount);

public sealed record FuxionGenerateLinkRequest(
    string BaseUrl,           // ej. https://api-aware.fuxion.com (con override del agente)
    string PathTemplate,      // ej. /api/products/user/{userId}/generate-power-link
    string ResponseUrlPath,   // ej. data.url (dot-separated JSON path a la URL)
    string UserId,            // reemplaza {userId} en PathTemplate
    string BearerToken,       // xcorptoken descifrado (NUNCA loggear)
    string Country,           // pe / co / mx
    string Description,       // nombre del link (visible en app-aware)
    IReadOnlyList<FuxionSalesLinkItem> Items);

public enum FuxionGenerateLinkErrorKind
{
    None = 0,
    TokenExpired,  // 401
    BadRequest,    // 400 (schema roto, producto inexistente, etc.)
    ServerError,   // 5xx / timeout / network
    UnexpectedResponse // 200 pero sin URL en el JSON esperado (contrato cambio)
}

public sealed record FuxionGenerateLinkResult(
    bool Ok,
    string? Url,
    FuxionGenerateLinkErrorKind ErrorKind,
    string? ErrorDetail,   // mensaje humano legible (sin token ni info sensible)
    int? HttpStatus)
{
    public static FuxionGenerateLinkResult Success(string url) =>
        new(true, url, FuxionGenerateLinkErrorKind.None, null, null);
    public static FuxionGenerateLinkResult Failure(FuxionGenerateLinkErrorKind kind, string? detail, int? status = null) =>
        new(false, null, kind, detail, status);
}

public enum FuxionVerifySessionOutcome
{
    Valid = 0,       // 2xx - token vivo
    Rejected = 1,    // 401/403 - token invalido o expirado
    Unreachable = 2  // red, timeout, 5xx - no se pudo determinar
}

public sealed record FuxionVerifySessionResult(
    FuxionVerifySessionOutcome Outcome,
    int? HttpStatus,
    string? ErrorDetail);
