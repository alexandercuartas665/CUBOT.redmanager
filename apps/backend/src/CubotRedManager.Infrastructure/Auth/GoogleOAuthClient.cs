using System.Text.Json;
using CubotRedManager.Application.Auth;

namespace CubotRedManager.Infrastructure.Auth;

/// <summary>
/// Intercambia el authorization code de Google por la identidad del usuario. Hace el POST al token
/// endpoint sobre TLS directo con Google y lee el id_token (JWT) para extraer sub/email/name.
/// No persiste ni loggea el client secret ni los tokens.
/// </summary>
public sealed class GoogleOAuthClient : IGoogleOAuthClient
{
    private const string TokenEndpoint = "https://oauth2.googleapis.com/token";

    private readonly HttpClient _http;

    public GoogleOAuthClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<GoogleIdentity?> ExchangeCodeAsync(string clientId, string clientSecret, string code, string redirectUri, CancellationToken cancellationToken = default)
    {
        try
        {
            var form = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["code"] = code,
                ["client_id"] = clientId,
                ["client_secret"] = clientSecret,
                ["redirect_uri"] = redirectUri,
                ["grant_type"] = "authorization_code"
            });

            using var resp = await _http.PostAsync(TokenEndpoint, form, cancellationToken);
            if (!resp.IsSuccessStatusCode) { return null; }

            await using var stream = await resp.Content.ReadAsStreamAsync(cancellationToken);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            if (!doc.RootElement.TryGetProperty("id_token", out var idTokenEl)) { return null; }

            var idToken = idTokenEl.GetString();
            if (string.IsNullOrEmpty(idToken)) { return null; }

            // El id_token llega directo de Google sobre TLS; leemos su payload (no requiere verificar firma
            // en el flujo server-side con code, segun la guia de Google).
            var payload = DecodeJwtPayload(idToken);
            if (payload is null) { return null; }

            var root = payload.Value;
            var sub = GetString(root, "sub");
            var email = GetString(root, "email");
            if (string.IsNullOrEmpty(sub) || string.IsNullOrEmpty(email)) { return null; }

            var emailVerified = root.TryGetProperty("email_verified", out var ev)
                && (ev.ValueKind == JsonValueKind.True || (ev.ValueKind == JsonValueKind.String && ev.GetString() == "true"));

            return new GoogleIdentity(sub!, email!, emailVerified, GetString(root, "name"), GetString(root, "picture"));
        }
        catch
        {
            return null;
        }
    }

    private static JsonElement? DecodeJwtPayload(string jwt)
    {
        var parts = jwt.Split('.');
        if (parts.Length < 2) { return null; }
        var payload = parts[1];
        // base64url -> base64 con padding
        payload = payload.Replace('-', '+').Replace('_', '/');
        switch (payload.Length % 4) { case 2: payload += "=="; break; case 3: payload += "="; break; }
        try
        {
            var bytes = Convert.FromBase64String(payload);
            using var doc = JsonDocument.Parse(bytes);
            return doc.RootElement.Clone();
        }
        catch { return null; }
    }

    private static string? GetString(JsonElement el, string name)
        => el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
