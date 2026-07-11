using CubotRedManager.Domain.Common;

namespace CubotRedManager.Domain.Entities;

/// <summary>
/// Registro de cada intento de refresh de token OAuth (exchange o refresh) por cuenta social.
/// El proposito es diagnostico: cuando una cuenta "se cae" el operador puede ver exactamente
/// que endpoint se golpeo, que respondio TikTok, y con que frecuencia. Retencion corta
/// (7 dias) — se rota via job para no crecer sin limite.
///
/// SEGURIDAD: NUNCA guardamos el access_token ni el refresh_token, ni el authorization code.
/// Solo metadatos: endpoint, HTTP status, codigo de error de TikTok, mensaje sanitizado.
/// </summary>
public class TokenRefreshLog : TenantEntity
{
    /// <summary>Cuenta afectada. FK a SocialAccount (cascade delete al borrar cuenta).</summary>
    public Guid SocialAccountId { get; set; }
    public SocialAccount? SocialAccount { get; set; }

    /// <summary>Instante del intento (UTC).</summary>
    public DateTimeOffset AttemptedAt { get; set; }

    /// <summary>"exchange" (canje del auth_code inicial) o "refresh" (renovacion). Distingue
    /// el primer intento post-OAuth del ciclo periodico del worker.</summary>
    public string Operation { get; set; } = "";

    /// <summary>URL exacta que se golpeo. Ej: "open.tiktokapis.com/v2/oauth/token/".</summary>
    public string Endpoint { get; set; } = "";

    /// <summary>Flavor OAuth usado en el intento. "BusinessV13" o "OpenV2".</summary>
    public string Flavor { get; set; } = "";

    /// <summary>true = TikTok devolvio access_token utilizable.</summary>
    public bool Success { get; set; }

    /// <summary>Codigo de estado HTTP de TikTok. 0 = no llego a haber respuesta (timeout/DNS/etc).</summary>
    public int? HttpStatus { get; set; }

    /// <summary>Codigo de negocio de TikTok (0 = OK; !=0 = error). Ej: "40002", "40105".
    /// Extraido del body de la respuesta.</summary>
    public string? ResponseCode { get; set; }

    /// <summary>Mensaje de error TAL CUAL respondio TikTok, sanitizado (sin tokens). Vacio si Success.</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>Duracion del round-trip HTTP en milisegundos.</summary>
    public int DurationMs { get; set; }

    /// <summary>Contador de fallos consecutivos DESPUES de este intento (snapshot). Facilita
    /// ver la escalada temporal.</summary>
    public int FailureCountAfter { get; set; }

    /// <summary>Cuerpo crudo de la respuesta de TikTok (sanitizado: sin tokens ni auth_codes),
    /// truncado a 2KB. Sirve para diagnosticar campos que el parseo estructurado no captura
    /// (log_id, request_id, sub-errors, etc). Nulo si no se capturo o si el sanitizado lo vacio.</summary>
    public string? RawResponse { get; set; }
}
