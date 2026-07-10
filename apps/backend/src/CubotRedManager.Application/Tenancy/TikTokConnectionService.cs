using CubotRedManager.Application.Abstractions;
using CubotRedManager.Domain.Entities;
using CubotRedManager.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace CubotRedManager.Application.Tenancy;

public sealed class TikTokConnectionService : ITikTokConnectionService
{
    private const string TikTokNetwork = "tiktok";
    // Redirect nuevo (Modulo 2.2 - callback automatico): TikTok redirige aqui con ?code=X&state=Y
    // y nuestro endpoint /oauth/tiktok/callback hace el exchange sin que el operador copie codigos.
    // El usuario debe registrar EXACTAMENTE esta URL en developers.tiktok.com > Redirect URIs.
    public const string DefaultRedirect = "https://red.cubot.com.co/oauth/tiktok/callback";
    // URL del sistema anterior (VB.NET). Auto-detectada para forzar el redirect nuevo aunque la DB
    // no se haya actualizado. Evita que un operador que solo actualiza developers.tiktok.com se
    // encuentre con "redirect_uri mismatch" hasta que abra la pantalla de config.
    private const string LegacyRedirectMarker = "bitcode.com.co";
    private const string DefaultScope = "user.info.basic,biz.creator.info,biz.creator.insights,video.list";

    private static string EffectiveRedirect(string? stored)
    {
        if (string.IsNullOrWhiteSpace(stored)) { return DefaultRedirect; }
        if (stored.Contains(LegacyRedirectMarker, StringComparison.OrdinalIgnoreCase)) { return DefaultRedirect; }
        return stored;
    }

    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly ISecretProtector _protector;
    private readonly IAuditWriter _audit;
    private readonly TimeProvider _time;
    private readonly ISocialOAuthProvider _tiktok;
    private readonly ITikTokApiClient _api;

    public TikTokConnectionService(
        IApplicationDbContext db,
        ITenantContext tenantContext,
        ISecretProtector protector,
        IAuditWriter audit,
        TimeProvider time,
        IEnumerable<ISocialOAuthProvider> providers,
        ITikTokApiClient api)
    {
        _db = db;
        _tenantContext = tenantContext;
        _protector = protector;
        _audit = audit;
        _time = time;
        _tiktok = providers.First(p => p.NetworkCode == TikTokNetwork);
        _api = api;
    }

    public async Task<TikTokAppConfigDto?> GetAppConfigAsync(CancellationToken cancellationToken = default)
    {
        var cfg = await _db.TikTokAppConfigs.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        if (cfg is null) { return null; }
        return new TikTokAppConfigDto(cfg.ClientKey, !string.IsNullOrEmpty(cfg.ClientSecretEncrypted), cfg.RedirectUri, cfg.Scope);
    }

    public async Task<TikTokAppConfigDto> SaveAppConfigAsync(SaveTikTokAppConfigRequest request, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        // Config de plataforma: NO requiere tenant activo. La guarda el Super Admin.
        var cfg = await _db.TikTokAppConfigs.FirstOrDefaultAsync(cancellationToken);
        if (cfg is null)
        {
            cfg = new TikTokAppConfig();
            _db.TikTokAppConfigs.Add(cfg);
        }

        cfg.ClientKey = request.ClientKey.Trim();
        cfg.RedirectUri = string.IsNullOrWhiteSpace(request.RedirectUri) ? DefaultRedirect : request.RedirectUri.Trim();
        cfg.Scope = string.IsNullOrWhiteSpace(request.Scope) ? DefaultScope : request.Scope.Trim();
        // Secret vacio = conservar el actual (igual que el control VB.NET).
        if (!string.IsNullOrWhiteSpace(request.ClientSecret))
        {
            cfg.ClientSecretEncrypted = _protector.Protect(request.ClientSecret.Trim());
        }

        // Auditoria a nivel plataforma (sin tenant): actor es el Super Admin.
        _audit.Write(actorUserId, "tiktok.app-config.save", nameof(TikTokAppConfig), cfg.Id,
            previousValue: null, newValue: new { cfg.ClientKey, cfg.RedirectUri, cfg.Scope }, tenantId: null);
        await _db.SaveChangesAsync(cancellationToken);

        return new TikTokAppConfigDto(cfg.ClientKey, !string.IsNullOrEmpty(cfg.ClientSecretEncrypted), cfg.RedirectUri, cfg.Scope);
    }

    public async Task<TikTokValidationResult> ValidateConfigAsync(CancellationToken cancellationToken = default)
    {
        var checks = new List<TikTokConfigCheck>();
        var cfg = await _db.TikTokAppConfigs.AsNoTracking().FirstOrDefaultAsync(cancellationToken);

        // 1. Config existe
        if (cfg is null)
        {
            checks.Add(new("config", "Configuracion guardada", false, "No hay configuracion guardada. Completa el formulario y pulsa Guardar."));
            return new TikTokValidationResult(false, checks);
        }

        // 2. Campos completos
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(cfg.ClientKey)) { missing.Add("App Key"); }
        if (string.IsNullOrEmpty(cfg.ClientSecretEncrypted)) { missing.Add("App Secret"); }
        if (string.IsNullOrWhiteSpace(cfg.RedirectUri)) { missing.Add("Redirect URI"); }
        if (string.IsNullOrWhiteSpace(cfg.Scope)) { missing.Add("Scopes"); }
        if (missing.Count > 0)
        {
            checks.Add(new("fields", "Campos completos", false, "Falta: " + string.Join(", ", missing)));
            return new TikTokValidationResult(false, checks);
        }
        checks.Add(new("fields", "Campos completos", true, "App Key, Secret, Redirect URI y Scopes presentes."));

        // 3. Redirect URI valida HTTPS absoluta
        var redirectOk = Uri.TryCreate(cfg.RedirectUri, UriKind.Absolute, out var uri) &&
                         string.Equals(uri.Scheme, "https", StringComparison.OrdinalIgnoreCase);
        checks.Add(new("redirect", "Redirect URI valida (HTTPS)", redirectOk,
            redirectOk ? cfg.RedirectUri : "TikTok exige una URL HTTPS absoluta registrada en el portal."));

        // 4. Scopes en el catalogo conocido
        var requested = cfg.Scope.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        var unknown = requested.Where(s => !_tiktok.KnownScopes.Contains(s)).ToList();
        checks.Add(new("scopes", $"Scopes reconocidos ({requested.Count})", unknown.Count == 0,
            unknown.Count == 0
                ? "Todos los scopes pertenecen al catalogo de TikTok: " + string.Join(", ", requested)
                : "Scopes desconocidos (revisa typos o si tu app tiene permiso): " + string.Join(", ", unknown)));

        // 5. Conectividad
        var reachable = await _tiktok.CheckReachabilityAsync(cancellationToken);
        checks.Add(new("network", "Conectividad TikTok API", reachable,
            reachable ? "business-api.tiktok.com responde." : "No se pudo conectar al endpoint de TikTok."));

        // 6. Credenciales (sondeo via refresh dummy)
        if (reachable)
        {
            try
            {
                var secret = _protector.Unprotect(cfg.ClientSecretEncrypted!);
                var probe = await _tiktok.ProbeCredentialsAsync(cfg.ClientKey, secret, cancellationToken);
                checks.Add(new("credentials", "App Key + Secret aceptados por TikTok", probe.CredentialsOk, probe.Detail));
            }
            catch
            {
                checks.Add(new("credentials", "App Key + Secret aceptados por TikTok", false,
                    "El App Secret guardado no se puede descifrar (llave perdida). Re-introduce el App Secret y guarda."));
            }
        }
        else
        {
            checks.Add(new("credentials", "App Key + Secret aceptados por TikTok", false,
                "Omitido: sin conectividad no se puede sondear credenciales."));
        }

        var overall = checks.All(c => c.Ok);
        return new TikTokValidationResult(overall, checks);
    }

    public Task<(string? Url, string State, string? Error)> BuildAuthorizeUrlAsync(CancellationToken cancellationToken = default) =>
        BuildAuthorizeUrlAsync(null, cancellationToken);

    /// <summary>
    /// Genera la URL de autorizacion de TikTok. Si <paramref name="stateOverride"/> viene, se usa tal
    /// cual (lo firma el llamador con DataProtection para el callback automatico). Si no, genera un
    /// state opaco corto (compatibilidad con el flujo antiguo de paste manual).
    /// </summary>
    public async Task<(string? Url, string State, string? Error)> BuildAuthorizeUrlAsync(string? stateOverride, CancellationToken cancellationToken = default)
    {
        var cfg = await _db.TikTokAppConfigs.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        if (cfg is null || string.IsNullOrWhiteSpace(cfg.ClientKey))
        {
            return (null, "", "Configura primero el App Key de TikTok.");
        }
        var state = string.IsNullOrEmpty(stateOverride) ? Guid.CreateVersion7().ToString("N")[..16] : stateOverride;
        var url = _tiktok.BuildAuthorizeUrl(
            cfg.ClientKey,
            EffectiveRedirect(cfg.RedirectUri),
            string.IsNullOrWhiteSpace(cfg.Scope) ? DefaultScope : cfg.Scope,
            state);
        return (url, state, null);
    }

    public async Task<TikTokOpResult> ExchangeCodeAsync(Guid clientId, string authCode, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId) { return new TikTokOpResult(false, "", "Sin tenant activo.", null); }
        if (string.IsNullOrWhiteSpace(authCode)) { return new TikTokOpResult(false, "", "Pega el auth_code antes de canjear.", null); }

        var client = await _db.Clients.FirstOrDefaultAsync(c => c.Id == clientId, cancellationToken);
        if (client is null) { return new TikTokOpResult(false, "", "Cliente no encontrado.", null); }

        var cfg = await _db.TikTokAppConfigs.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        if (cfg is null || string.IsNullOrWhiteSpace(cfg.ClientKey) || string.IsNullOrEmpty(cfg.ClientSecretEncrypted))
        {
            return new TikTokOpResult(false, "", "Configura App Key y App Secret de TikTok primero.", null);
        }

        string secret;
        try { secret = _protector.Unprotect(cfg.ClientSecretEncrypted); }
        catch
        {
            // El App Secret se cifro con una llave de DataProtection que ya no existe (dev efimero,
            // rotacion). Re-introducirlo en la seccion 1 lo cifra con las llaves actuales.
            return new TikTokOpResult(false, "",
                "El App Secret guardado no se puede descifrar (llave perdida). Vuelve a la seccion 1, re-pega el App Secret en 'App Secret (client_secret)' y pulsa 'Guardar configuracion'. Luego vuelve a Canjear Auth Code.",
                null);
        }
        var swExchange = System.Diagnostics.Stopwatch.StartNew();
        var result = await _tiktok.ExchangeCodeAsync(cfg.ClientKey, secret, EffectiveRedirect(cfg.RedirectUri), authCode.Trim(), cancellationToken);
        swExchange.Stop();
        if (!result.Success || string.IsNullOrEmpty(result.AccessToken))
        {
            // Log del intento fallido — accountId NULL porque aun no existe la cuenta. Persistimos
            // asociado al clientId para diagnostico. Skipeamos si el service todavia no tiene tenant.
            await TryWriteExchangeLogAsync(clientId, tenantId, result, swExchange.ElapsedMilliseconds, cancellationToken);
            return new TikTokOpResult(false, result.Trace, result.Error ?? "Canje fallido.", null);
        }

        var externalId = string.IsNullOrWhiteSpace(result.OpenId) ? Guid.CreateVersion7().ToString("N")[..16] : result.OpenId!;
        // Identificamos la cuenta TikTok especifica por su external_id (open_id de TikTok). Asi:
        //  - Si el usuario reconecta la MISMA cuenta TikTok -> encuentra y actualiza tokens.
        //  - Si conecta OTRA cuenta TikTok del mismo cliente -> no encuentra -> crea fila nueva
        //    (UNIQUE constraint UNIQUE(tenant_id, client_id, network_code, external_id) lo permite).
        // Antes el query buscaba solo (ClientId, NetworkCode) y sobrescribia la fila existente,
        // borrando logicamente la primera cuenta conectada al conectar la segunda.
        var account = await _db.SocialAccounts.FirstOrDefaultAsync(
            a => a.ClientId == clientId && a.NetworkCode == TikTokNetwork && a.ExternalId == externalId, cancellationToken);
        if (account is null)
        {
            account = new SocialAccount { TenantId = tenantId, ClientId = clientId, NetworkCode = TikTokNetwork };
            _db.SocialAccounts.Add(account);
        }

        account.ExternalId = externalId;
        account.AccessTokenEncrypted = _protector.Protect(result.AccessToken!);
        if (!string.IsNullOrEmpty(result.RefreshToken)) { account.RefreshTokenEncrypted = _protector.Protect(result.RefreshToken!); }
        account.TokenScope = cfg.Scope;
        account.ExpiresAt = result.ExpiresInSeconds is { } secs ? _time.GetUtcNow().AddSeconds(secs) : _time.GetUtcNow().AddDays(1);
        account.Status = SocialAccountStatus.Connected;
        // Persistimos el flavor OAuth detectado por el proveedor. El refresh futuro golpea SOLO
        // ese endpoint sin cascada (ver TikTokOAuthProvider.RefreshAsync).
        if (result.OAuthFlavor is int flavorInt && Enum.IsDefined(typeof(TikTokOAuthFlavor), flavorInt))
        {
            account.OAuthFlavor = (TikTokOAuthFlavor)flavorInt;
        }
        // Cuenta acaba de reconectar: reseteamos TODO el rastro de fallos previos. Sin esto el
        // badge "Con problema (N)" y el mensaje LastSyncError persisten aunque la cuenta este sana
        // ahora - engañoso al operador que acaba de renovar.
        account.LastRefreshFailureNotifiedAt = null;
        account.LastSyncError = null;
        account.RefreshFailureCount = 0;

        _audit.Write(actorUserId, "tiktok.connect", nameof(SocialAccount), account.Id,
            previousValue: null, newValue: new { account.ClientId, externalId }, tenantId: tenantId);
        await _db.SaveChangesAsync(cancellationToken);

        // Log persistente del exchange exitoso (endpoint golpeado, flavor detectado, duracion).
        _db.TokenRefreshLogs.Add(new TokenRefreshLog
        {
            TenantId = tenantId,
            SocialAccountId = account.Id,
            AttemptedAt = _time.GetUtcNow(),
            Operation = "exchange",
            Endpoint = result.EndpointUsed ?? "unknown",
            Flavor = account.OAuthFlavor.ToString(),
            Success = true,
            HttpStatus = result.HttpStatus,
            ResponseCode = result.ResponseCode,
            ErrorMessage = null,
            DurationMs = (int)swExchange.ElapsedMilliseconds,
            FailureCountAfter = 0
        });
        await _db.SaveChangesAsync(cancellationToken);

        // Tras el canje OAuth, intentamos rellenar el perfil. NO bloqueamos si falla:
        // ya tenemos token+open_id y eso basta para sync/publish; el handle es solo UX.
        try { await FetchAndApplyProfileAsync(account.Id, result.AccessToken!, cancellationToken); }
        catch { /* silencioso — UX only */ }

        var dto = await LoadDtoAsync(account.Id, cancellationToken);
        return new TikTokOpResult(true, result.Trace, null, dto);
    }

    /// <summary>
    /// Registra un intento de exchange que fallo antes de crear cuenta. Como no hay account.Id,
    /// buscamos si ya existe una cuenta para este cliente (cualquiera) y asociamos ahi. Si tampoco
    /// existe, saltamos el log (no hay a que asociarlo). Silencioso: nunca bloquea el flujo.
    /// </summary>
    private async Task TryWriteExchangeLogAsync(Guid clientId, Guid tenantId, OAuthTokenResult result,
        long elapsedMs, CancellationToken ct)
    {
        try
        {
            var probe = await _db.SocialAccounts
                .Where(a => a.ClientId == clientId && a.NetworkCode == TikTokNetwork)
                .Select(a => new { a.Id })
                .FirstOrDefaultAsync(ct);
            if (probe is null) { return; }
            _db.TokenRefreshLogs.Add(new TokenRefreshLog
            {
                TenantId = tenantId,
                SocialAccountId = probe.Id,
                AttemptedAt = _time.GetUtcNow(),
                Operation = "exchange",
                Endpoint = result.EndpointUsed ?? "unknown",
                Flavor = "unknown",
                Success = false,
                HttpStatus = result.HttpStatus,
                ResponseCode = result.ResponseCode,
                ErrorMessage = result.Error,
                DurationMs = (int)elapsedMs,
                FailureCountAfter = 0
            });
            await _db.SaveChangesAsync(ct);
        }
        catch { /* diagnostico — nunca bloquea */ }
    }

    public async Task<TikTokOpResult> RefreshProfileAsync(Guid accountId, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var account = await _db.SocialAccounts.FirstOrDefaultAsync(a => a.Id == accountId && a.NetworkCode == TikTokNetwork, cancellationToken);
        if (account is null) { return new TikTokOpResult(false, "", "Cuenta no encontrada.", null); }
        if (string.IsNullOrEmpty(account.AccessTokenEncrypted)) { return new TikTokOpResult(false, "", "Sin Access Token.", null); }

        var token = _protector.Unprotect(account.AccessTokenEncrypted);
        var (ok, info) = await TryFetchProfileAsync(token, cancellationToken);
        if (!ok || info is null) { return new TikTokOpResult(false, "", "TikTok rechazo la consulta de perfil.", null); }
        ApplyProfileToAccount(account, info);
        _audit.Write(actorUserId, "tiktok.refresh-profile", nameof(SocialAccount), account.Id,
            previousValue: null, newValue: new { account.Handle, account.DisplayName }, tenantId: account.TenantId);
        await _db.SaveChangesAsync(cancellationToken);
        var dto = await LoadDtoAsync(account.Id, cancellationToken);
        return new TikTokOpResult(true, "Perfil actualizado.", null, dto);
    }

    /// <summary>Llama al endpoint user/info y aplica el resultado al SocialAccount (mismo scope context).</summary>
    private async Task FetchAndApplyProfileAsync(Guid accountId, string accessToken, CancellationToken ct)
    {
        var (ok, info) = await TryFetchProfileAsync(accessToken, ct);
        if (!ok || info is null) { return; }
        var account = await _db.SocialAccounts.FirstOrDefaultAsync(a => a.Id == accountId, ct);
        if (account is null) { return; }
        ApplyProfileToAccount(account, info);
        await _db.SaveChangesAsync(ct);
    }

    private async Task<(bool ok, TikTokUserInfo? info)> TryFetchProfileAsync(string accessToken, CancellationToken ct)
    {
        var page = await _api.GetUserInfoAsync(accessToken, ct);
        if (page.Code != 0) { return (false, null); }
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(page.RawJson);
            if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != System.Text.Json.JsonValueKind.Object) { return (false, null); }
            // El user object esta a veces directo en data, a veces en data.user
            var user = data.TryGetProperty("user", out var u) && u.ValueKind == System.Text.Json.JsonValueKind.Object ? u : data;
            return (true, new TikTokUserInfo(
                Username: ReadStr(user, "username"),
                DisplayName: ReadStr(user, "display_name", "nickname"),
                AvatarUrl: ReadStr(user, "avatar_url", "avatar"),
                BioDescription: ReadStr(user, "bio_description", "bio"),
                FollowerCount: ReadLong(user, "follower_count"),
                OpenId: ReadStr(user, "open_id")));
        }
        catch { return (false, null); }
    }

    private static void ApplyProfileToAccount(SocialAccount account, TikTokUserInfo info)
    {
        if (!string.IsNullOrEmpty(info.Username)) { account.Handle = info.Username; }
        if (!string.IsNullOrEmpty(info.DisplayName)) { account.DisplayName = info.DisplayName; }
        if (!string.IsNullOrEmpty(info.AvatarUrl)) { account.AvatarUrl = info.AvatarUrl; }
        if (!string.IsNullOrEmpty(info.BioDescription)) { account.Bio = info.BioDescription; }
        if (info.FollowerCount > 0) { account.FollowersCount = info.FollowerCount; }
    }

    private static string? ReadStr(System.Text.Json.JsonElement el, params string[] names)
    {
        foreach (var n in names)
        {
            if (el.TryGetProperty(n, out var p) && p.ValueKind == System.Text.Json.JsonValueKind.String)
            {
                var s = p.GetString();
                if (!string.IsNullOrWhiteSpace(s)) { return s; }
            }
        }
        return null;
    }
    private static long ReadLong(System.Text.Json.JsonElement el, params string[] names)
    {
        foreach (var n in names)
        {
            if (el.TryGetProperty(n, out var p) && p.ValueKind == System.Text.Json.JsonValueKind.Number && p.TryGetInt64(out var v)) { return v; }
        }
        return 0;
    }

    private sealed record TikTokUserInfo(string? Username, string? DisplayName, string? AvatarUrl, string? BioDescription, long FollowerCount, string? OpenId);

    public async Task<TikTokOpResult> RefreshAccountAsync(Guid accountId, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId) { return new TikTokOpResult(false, "", "Sin tenant activo.", null); }

        var account = await _db.SocialAccounts.FirstOrDefaultAsync(a => a.Id == accountId && a.NetworkCode == TikTokNetwork, cancellationToken);
        if (account is null) { return new TikTokOpResult(false, "", "Cuenta no encontrada.", null); }
        if (string.IsNullOrEmpty(account.RefreshTokenEncrypted)) { return new TikTokOpResult(false, "", "La cuenta no tiene refresh token. Reconecta con OAuth.", null); }

        var cfg = await _db.TikTokAppConfigs.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        if (cfg is null || string.IsNullOrEmpty(cfg.ClientSecretEncrypted)) { return new TikTokOpResult(false, "", "Falta config de app TikTok.", null); }

        string secret, refresh;
        try { secret = _protector.Unprotect(cfg.ClientSecretEncrypted); }
        catch
        {
            return new TikTokOpResult(false, "",
                "El App Secret guardado no se puede descifrar (llave perdida). Re-introduce el App Secret en TikTok manager y guarda.",
                null);
        }
        try { refresh = _protector.Unprotect(account.RefreshTokenEncrypted); }
        catch
        {
            // Refresh token cifrado con llave perdida: la cuenta ya no se puede recuperar via refresh.
            account.Status = SocialAccountStatus.Disconnected;
            account.LastSyncError = "Refresh token no descifrable (llave perdida). Vuelve a hacer OAuth para reconectar.";
            await _db.SaveChangesAsync(cancellationToken);
            return new TikTokOpResult(false, "",
                "El refresh token de esta cuenta no se puede descifrar. Reconecta la cuenta para regenerar credenciales.",
                null);
        }
        var swRefresh = System.Diagnostics.Stopwatch.StartNew();
        var result = await _tiktok.RefreshAsync(cfg.ClientKey, secret, refresh, (int)account.OAuthFlavor, cancellationToken);
        swRefresh.Stop();
        if (!result.Success || string.IsNullOrEmpty(result.AccessToken))
        {
            // Refresh fallo. Decidimos si dejar Status=Connected (dando el beneficio de la duda al
            // access_token vigente) o marcar Expired de inmediato. Tres criterios que MARCAN Expired:
            //   (1) Error terminal reconocido por texto/codigo -> refresh_token muerto en TikTok
            //   (2) Contador de fallos consecutivos >= 3 -> algo cronico, ya no es glitch transient
            //   (3) access_token ya expiro segun ExpiresAt (regla previa)
            // Si no cae en ninguno -> lo dejamos Connected pero anotamos LastSyncError para que el
            // UI muestre "Con problema" y el operador no vea un badge verde enganoso.
            var now = _time.GetUtcNow();
            var accessStillAlive = account.ExpiresAt is { } expiresAt && expiresAt > now;
            var isTerminal = IsTerminalRefreshError(result.Error);
            account.LastSyncError = result.Error;
            account.RefreshFailureCount += 1;

            var shouldExpire = isTerminal || account.RefreshFailureCount >= 3 || !accessStillAlive;
            if (shouldExpire)
            {
                account.Status = SocialAccountStatus.Expired;
            }
            // (si no debe expirar, mantenemos Status Connected pero LastSyncError disparara badge)

            _db.TokenRefreshLogs.Add(new TokenRefreshLog
            {
                TenantId = tenantId,
                SocialAccountId = account.Id,
                AttemptedAt = _time.GetUtcNow(),
                Operation = "refresh",
                Endpoint = result.EndpointUsed ?? "unknown",
                Flavor = account.OAuthFlavor.ToString(),
                Success = false,
                HttpStatus = result.HttpStatus,
                ResponseCode = result.ResponseCode,
                ErrorMessage = result.Error,
                DurationMs = (int)swRefresh.ElapsedMilliseconds,
                FailureCountAfter = account.RefreshFailureCount
            });
            await _db.SaveChangesAsync(cancellationToken);
            return new TikTokOpResult(false, result.Trace, result.Error ?? "Renovacion fallida.", null);
        }

        account.AccessTokenEncrypted = _protector.Protect(result.AccessToken!);
        if (!string.IsNullOrEmpty(result.RefreshToken)) { account.RefreshTokenEncrypted = _protector.Protect(result.RefreshToken!); }
        if (!string.IsNullOrWhiteSpace(result.OpenId)) { account.ExternalId = result.OpenId!; }
        account.ExpiresAt = result.ExpiresInSeconds is { } secs ? _time.GetUtcNow().AddSeconds(secs) : _time.GetUtcNow().AddDays(1);
        account.Status = SocialAccountStatus.Connected;
        account.LastSyncError = null;
        account.RefreshFailureCount = 0;
        // Refresh exitoso -> limpiamos el marcador de alerta previa para que si vuelve a fallar
        // en el futuro se notifique de nuevo (no una alerta cada dia perpetuamente).
        account.LastRefreshFailureNotifiedAt = null;

        _audit.Write(actorUserId, "tiktok.refresh", nameof(SocialAccount), account.Id,
            previousValue: null, newValue: new { account.Id }, tenantId: tenantId);
        _db.TokenRefreshLogs.Add(new TokenRefreshLog
        {
            TenantId = tenantId,
            SocialAccountId = account.Id,
            AttemptedAt = _time.GetUtcNow(),
            Operation = "refresh",
            Endpoint = result.EndpointUsed ?? "unknown",
            Flavor = account.OAuthFlavor.ToString(),
            Success = true,
            HttpStatus = result.HttpStatus,
            ResponseCode = result.ResponseCode,
            ErrorMessage = null,
            DurationMs = (int)swRefresh.ElapsedMilliseconds,
            FailureCountAfter = 0
        });
        await _db.SaveChangesAsync(cancellationToken);

        // Oportunistico: si la cuenta no tenia handle, intentamos llenarlo con el token nuevo.
        if (string.IsNullOrEmpty(account.Handle))
        {
            try { await FetchAndApplyProfileAsync(account.Id, result.AccessToken!, cancellationToken); }
            catch { /* silencioso */ }
        }

        var dto = await LoadDtoAsync(account.Id, cancellationToken);
        return new TikTokOpResult(true, result.Trace, null, dto);
    }

    private async Task<SocialAccountDto?> LoadDtoAsync(Guid id, CancellationToken ct)
    {
        var row = await (from a in _db.SocialAccounts.AsNoTracking()
                         join c in _db.Clients.AsNoTracking() on a.ClientId equals c.Id
                         where a.Id == id
                         select new { a, c.Name }).FirstOrDefaultAsync(ct);
        if (row is null) { return null; }
        return new SocialAccountDto(row.a.Id, row.a.ClientId, row.Name, row.a.NetworkCode, "TikTok", "#010101",
            row.a.Handle, row.a.DisplayName, row.a.Status, row.a.ExpiresAt, row.a.LastSyncAt,
            row.a.FollowersCount, row.a.AvatarUrl, row.a.Bio, row.a.LastSyncError, row.a.RefreshFailureCount);
    }

    /// <summary>
    /// Un error de refresh es TERMINAL cuando TikTok comunica de forma explicita que el
    /// refresh_token quedo invalidado (revocado, expirado del lado de TikTok, o simplemente
    /// desconocido). En esos casos no tiene sentido esperar a que access_token venza para
    /// marcar la cuenta como Expired: ya no hay forma de renovar sin OAuth manual.
    /// Errores por red, HTTP 5xx, o codigos de rate-limit NO son terminales; el contador
    /// consecutivo (>=3) los captura como cronicos si persisten.
    /// </summary>
    private static bool IsTerminalRefreshError(string? error)
    {
        if (string.IsNullOrWhiteSpace(error)) { return false; }
        var lower = error.ToLowerInvariant();
        // Marcadores explicitos de TikTok / OAuth2 estandar
        if (lower.Contains("invalid_grant")) { return true; }
        if (lower.Contains("invalid refresh_token")) { return true; }
        if (lower.Contains("invalid_refresh_token")) { return true; }
        if (lower.Contains("refresh token invalid")) { return true; }
        if (lower.Contains("refresh_token expired")) { return true; }
        if (lower.Contains("token has been revoked")) { return true; }
        if (lower.Contains("token_revoked")) { return true; }
        if (lower.Contains("access_denied")) { return true; }
        // Codigo TikTok Business (parte del mensaje "code=X ..."): 40105 = access_token invalid,
        // 40106 = refresh_token invalid. En OpenV2 son numeros distintos, pero los mensajes
        // ya contienen los textos "invalid refresh_token" o "expired" cubiertos arriba.
        if (lower.Contains("code=40106")) { return true; }
        return false;
    }
}
