using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace CubotRedManager.Web.Auth;

/// <summary>
/// Firma y verifica el <c>state</c> que viaja por el redirect OAuth de TikTok.
/// TikTok devuelve el <c>state</c> tal cual en la callback URL. Aqui codificamos
/// (tenant, cliente, operador, fecha) cifrado con DataProtection para que:
///   1) Nadie de fuera pueda forjar un callback valido (integridad).
///   2) El callback sepa a que tenant/cliente adjudicar la cuenta sin consultar DB.
///   3) Expire a los 15 min (TTL corto -> no queda como token de larga vida en logs).
///
/// SEGURIDAD: el payload NO contiene tokens. Solo IDs. No se loguea el token cifrado.
/// </summary>
public sealed class TikTokOAuthStateService
{
    private const string Purpose = "cubot.redmanager.oauth.tiktok.state.v1";
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(15);

    private readonly ITimeLimitedDataProtector _protector;

    public TikTokOAuthStateService(IDataProtectionProvider provider)
    {
        _protector = provider.CreateProtector(Purpose).ToTimeLimitedDataProtector();
    }

    public string Generate(Guid tenantId, Guid clientId, Guid actorUserId)
    {
        var payload = new StatePayload(tenantId, clientId, actorUserId, Guid.NewGuid().ToString("N")[..8]);
        var json = JsonSerializer.Serialize(payload);
        return _protector.Protect(json, Ttl);
    }

    public bool TryDecode(string token, out Guid tenantId, out Guid clientId, out Guid actorUserId, out string? error)
    {
        tenantId = clientId = actorUserId = Guid.Empty;
        error = null;
        if (string.IsNullOrWhiteSpace(token)) { error = "state vacio"; return false; }
        try
        {
            var json = _protector.Unprotect(token);
            var p = JsonSerializer.Deserialize<StatePayload>(json);
            if (p is null) { error = "state ilegible"; return false; }
            tenantId = p.T;
            clientId = p.C;
            actorUserId = p.A;
            return true;
        }
        catch (Exception ex)
        {
            // Puede ser: token expirado (>15 min), firma invalida (llave rotada), o payload alterado.
            // No filtramos el mensaje bruto al usuario para no revelar detalles del cifrado.
            error = ex is System.Security.Cryptography.CryptographicException
                ? "state expirado o invalido"
                : "state ilegible";
            return false;
        }
    }

    private sealed record StatePayload(Guid T, Guid C, Guid A, string N);
}
