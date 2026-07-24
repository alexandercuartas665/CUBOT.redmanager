using CubotRedManager.Application.Abstractions;
using CubotRedManager.Application.Admin;
using CubotRedManager.Domain.Entities;
using CubotRedManager.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace CubotRedManager.Application.Tenancy;

public sealed class WhatsAppConnectorService : IWhatsAppConnectorService
{
    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly ISecretProtector _secretProtector;
    private readonly IEvolutionApiClient _client;
    private readonly IAuditWriter _audit;
    private readonly TimeProvider _timeProvider;

    public WhatsAppConnectorService(
        IApplicationDbContext db,
        ITenantContext tenantContext,
        ISecretProtector secretProtector,
        IEvolutionApiClient client,
        IAuditWriter audit,
        TimeProvider timeProvider)
    {
        _db = db;
        _tenantContext = tenantContext;
        _secretProtector = secretProtector;
        _client = client;
        _audit = audit;
        _timeProvider = timeProvider;
    }

    public async Task<EvolutionServerSettingDto> GetServerAsync(CancellationToken cancellationToken = default)
    {
        var cfg = await _db.TenantEvolutionConfigs.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        var master = await _db.EvolutionMasterConfigs.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        var masterReady = master is not null && !string.IsNullOrWhiteSpace(master.BaseUrl) && !string.IsNullOrWhiteSpace(master.ApiKeyEncrypted);

        return new EvolutionServerSettingDto(
            UseMasterServer: cfg?.UseMasterServer ?? true,
            MasterReady: masterReady,
            MasterBaseUrl: master?.BaseUrl,
            OwnBaseUrl: cfg?.BaseUrl,
            OwnTokenMasked: cfg?.ApiTokenEncrypted is { } enc ? Mask(enc) : null,
            HasOwnToken: cfg?.ApiTokenEncrypted is not null);
    }

    public async Task<EvolutionServerSettingDto?> SetServerAsync(SetEvolutionServerRequest request, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId)
        {
            return null;
        }

        var cfg = await _db.TenantEvolutionConfigs.FirstOrDefaultAsync(cancellationToken);
        if (cfg is null)
        {
            cfg = new TenantEvolutionConfig { TenantId = tenantId };
            _db.TenantEvolutionConfigs.Add(cfg);
        }

        cfg.UseMasterServer = request.UseMasterServer;
        if (!request.UseMasterServer)
        {
            cfg.BaseUrl = NormalizeBaseUrl(request.OwnBaseUrl);
            if (!string.IsNullOrWhiteSpace(request.OwnApiToken))
            {
                cfg.ApiTokenEncrypted = _secretProtector.Protect(request.OwnApiToken.Trim());
            }
        }
        cfg.IsActive = true;

        _audit.Write(actorUserId, "evolution.server.set", nameof(TenantEvolutionConfig), cfg.Id,
            previousValue: null, newValue: new { cfg.UseMasterServer, cfg.BaseUrl }, tenantId: tenantId);

        await _db.SaveChangesAsync(cancellationToken);
        return await GetServerAsync(cancellationToken);
    }

    public async Task<LineConnectResult> ConnectLineAsync(Guid lineId, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var line = await _db.WhatsAppLines.FirstOrDefaultAsync(l => l.Id == lineId, cancellationToken);
        if (line is null)
        {
            return new LineConnectResult(false, null, "La linea no existe.");
        }
        var server = await ResolveServerAsync(cancellationToken);
        if (server is null)
        {
            return new LineConnectResult(false, null, "No hay servidor Evolution configurado (ni maestro ni propio).");
        }

        var (baseUrl, apiKey) = server.Value;
        var result = await _client.CreateInstanceAsync(baseUrl, apiKey, EvoInstance(line), cancellationToken);
        if (!result.Ok)
        {
            line.Status = WhatsAppLineStatus.Failed;
            line.LastStatusAt = _timeProvider.GetUtcNow();
            await _db.SaveChangesAsync(cancellationToken);
            return new LineConnectResult(false, null, result.Error);
        }

        line.Status = WhatsAppLineStatus.Connecting;
        line.LastStatusAt = _timeProvider.GetUtcNow();
        _audit.Write(actorUserId, "whatsapp-line.connect", nameof(WhatsAppLine), line.Id,
            previousValue: null, newValue: new { instance = EvoInstance(line) }, tenantId: line.TenantId);
        await _db.SaveChangesAsync(cancellationToken);

        // Configura el webhook entrante para recibir mensajes en caliente (si esta configurado).
        // El tenantId va en la URL (mismo esquema que ApplyWebhookToConnectedLinesAsync).
        var (webhookBase, webhookToken) = await EffectiveWebhookBaseAsync(cancellationToken);
        if (webhookBase is not null && webhookToken is not null)
        {
            var webhookUrl = $"{webhookBase}/{line.TenantId:D}";
            await _client.SetWebhookAsync(baseUrl, apiKey, EvoInstance(line), webhookUrl, webhookToken, cancellationToken);
        }

        return new LineConnectResult(true, result.QrBase64, null);
    }

    public async Task<WhatsAppLineDto?> RefreshAsync(Guid lineId, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var line = await _db.WhatsAppLines.FirstOrDefaultAsync(l => l.Id == lineId, cancellationToken);
        if (line is null)
        {
            return null;
        }
        var server = await ResolveServerAsync(cancellationToken);
        if (server is null)
        {
            return Map(line);
        }

        var (baseUrl, apiKey) = server.Value;
        var state = await _client.GetStateAsync(baseUrl, apiKey, EvoInstance(line), cancellationToken);
        if (state.Ok)
        {
            var mapped = state.State?.ToLowerInvariant() switch
            {
                "open" => WhatsAppLineStatus.Connected,
                "connecting" => WhatsAppLineStatus.Connecting,
                "close" => WhatsAppLineStatus.Disconnected,
                _ => line.Status
            };
            if (mapped != line.Status)
            {
                var now = _timeProvider.GetUtcNow();
                line.Status = mapped;
                line.LastStatusAt = now;
                if (mapped == WhatsAppLineStatus.Connected) { line.LastConnectedAt = now; }
                await _db.SaveChangesAsync(cancellationToken);
            }
        }
        return Map(line);
    }

    public async Task<bool> DisconnectAsync(Guid lineId, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var line = await _db.WhatsAppLines.FirstOrDefaultAsync(l => l.Id == lineId, cancellationToken);
        if (line is null)
        {
            return false;
        }
        var server = await ResolveServerAsync(cancellationToken);
        if (server is not null)
        {
            var (baseUrl, apiKey) = server.Value;
            await _client.DeleteInstanceAsync(baseUrl, apiKey, EvoInstance(line), cancellationToken);
        }

        line.Status = WhatsAppLineStatus.Disconnected;
        line.LastStatusAt = _timeProvider.GetUtcNow();
        _audit.Write(actorUserId, "whatsapp-line.disconnect", nameof(WhatsAppLine), line.Id,
            previousValue: null, newValue: new { instance = EvoInstance(line) }, tenantId: line.TenantId);
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> DeleteLineAsync(Guid lineId, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var line = await _db.WhatsAppLines.FirstOrDefaultAsync(l => l.Id == lineId, cancellationToken);
        if (line is null)
        {
            return false;
        }

        // Borra la instancia en Evolution (best-effort) antes de quitar la fila.
        var server = await ResolveServerAsync(cancellationToken);
        if (server is not null)
        {
            var (baseUrl, apiKey) = server.Value;
            try { await _client.DeleteInstanceAsync(baseUrl, apiKey, EvoInstance(line), cancellationToken); }
            catch { /* la instancia puede no existir */ }
        }

        _audit.Write(actorUserId, "whatsapp-line.delete", nameof(WhatsAppLine), line.Id,
            previousValue: new { line.InstanceName, line.Status }, newValue: null, tenantId: line.TenantId);

        _db.WhatsAppLines.Remove(line);
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<LineSendResult> SendTestAsync(Guid lineId, string phone, string text, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(phone) || string.IsNullOrWhiteSpace(text))
        {
            return new LineSendResult(false, "Indica el numero y el mensaje.");
        }
        var line = await _db.WhatsAppLines.FirstOrDefaultAsync(l => l.Id == lineId, cancellationToken);
        if (line is null)
        {
            return new LineSendResult(false, "La linea no existe.");
        }
        if (line.Status != WhatsAppLineStatus.Connected)
        {
            return new LineSendResult(false, "La linea no esta conectada.");
        }
        var server = await ResolveServerAsync(cancellationToken);
        if (server is null)
        {
            return new LineSendResult(false, "No hay servidor Evolution configurado.");
        }

        var digits = new string(phone.Where(char.IsDigit).ToArray());
        var (baseUrl, apiKey) = server.Value;
        var result = await _client.SendTextAsync(baseUrl, apiKey, EvoInstance(line), digits, text.Trim(), cancellationToken);

        _audit.Write(actorUserId, "whatsapp-line.test-send", nameof(WhatsAppLine), line.Id,
            previousValue: null, newValue: new { to = digits, ok = result.Ok }, tenantId: line.TenantId);

        return new LineSendResult(result.Ok, result.Error);
    }

    public async Task<LineSendResult> SendMediaAsync(Guid lineId, string phone, MessageMediaType mediaType, string base64, string? mimeType, string? fileName, string? caption, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var ready = await ReadyLineAsync(lineId, phone, cancellationToken);
        if (ready.Error is not null) { return new LineSendResult(false, ready.Error); }
        var (baseUrl, apiKey, instance, digits) = ready.Value;

        var result = mediaType switch
        {
            MessageMediaType.Audio => await _client.SendAudioAsync(baseUrl, apiKey, instance, digits, base64, cancellationToken),
            MessageMediaType.Image => await _client.SendMediaAsync(baseUrl, apiKey, instance, digits, "image", base64, mimeType, fileName, caption, cancellationToken),
            MessageMediaType.Video => await _client.SendMediaAsync(baseUrl, apiKey, instance, digits, "video", base64, mimeType, fileName, caption, cancellationToken),
            MessageMediaType.Document => await _client.SendMediaAsync(baseUrl, apiKey, instance, digits, "document", base64, mimeType, fileName, caption, cancellationToken),
            _ => new EvolutionSendResult(false, "Tipo de adjunto no soportado.")
        };
        return new LineSendResult(result.Ok, result.Error);
    }

    public async Task<LineSendResult> SendLocationAsync(Guid lineId, string phone, double latitude, double longitude, string? name, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var ready = await ReadyLineAsync(lineId, phone, cancellationToken);
        if (ready.Error is not null) { return new LineSendResult(false, ready.Error); }
        var (baseUrl, apiKey, instance, digits) = ready.Value;
        var result = await _client.SendLocationAsync(baseUrl, apiKey, instance, digits, latitude, longitude, name, null, cancellationToken);
        return new LineSendResult(result.Ok, result.Error);
    }

    // Resuelve linea conectada + servidor + numero normalizado. Error no nulo si algo falta.
    private async Task<(string Error, (string baseUrl, string apiKey, string instance, string digits) Value)> ReadyLineAsync(Guid lineId, string phone, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(phone)) { return ("Indica el numero.", default); }
        var line = await _db.WhatsAppLines.FirstOrDefaultAsync(l => l.Id == lineId, ct);
        if (line is null) { return ("La linea no existe.", default); }
        if (line.Status != WhatsAppLineStatus.Connected) { return ("La linea no esta conectada.", default); }
        var server = await ResolveServerAsync(ct);
        if (server is null) { return ("No hay servidor Evolution configurado.", default); }
        var (baseUrl, apiKey) = server.Value;
        var digits = new string(phone.Where(char.IsDigit).ToArray());
        return (null!, (baseUrl, apiKey, EvoInstance(line), digits));
    }

    public async Task<LineGroupsResult> FetchGroupsAsync(Guid lineId, CancellationToken cancellationToken = default)
    {
        var line = await _db.WhatsAppLines.FirstOrDefaultAsync(l => l.Id == lineId, cancellationToken);
        if (line is null) { return new LineGroupsResult(false, Array.Empty<LineGroupDto>(), "La linea no existe."); }
        if (line.Provider != WhatsAppProvider.Evolution)
        {
            return new LineGroupsResult(false, Array.Empty<LineGroupDto>(), "Solo se listan grupos de lineas Evolution.");
        }
        if (line.Status != WhatsAppLineStatus.Connected)
        {
            return new LineGroupsResult(false, Array.Empty<LineGroupDto>(), "La linea no esta conectada.");
        }
        var server = await ResolveServerAsync(cancellationToken);
        if (server is null) { return new LineGroupsResult(false, Array.Empty<LineGroupDto>(), "No hay servidor Evolution configurado."); }
        var (baseUrl, apiKey) = server.Value;
        var res = await _client.FetchGroupsAsync(baseUrl, apiKey, EvoInstance(line), cancellationToken);
        var mapped = res.Groups.Select(g => new LineGroupDto(g.Jid, g.Name, g.ParticipantCount)).ToList();
        return new LineGroupsResult(res.Ok, mapped, res.Error);
    }

    public async Task<LineSendResult> SendReactionAsync(Guid lineId, string phone, string externalMessageId, string emoji, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(externalMessageId) || string.IsNullOrWhiteSpace(emoji))
        {
            return new LineSendResult(false, "Falta el id del mensaje o el emoji.", null);
        }
        // IgnoreQueryFilters: lo llama el dispatcher del agente (webhook entrante, sin tenant context).
        var line = await _db.WhatsAppLines.IgnoreQueryFilters().FirstOrDefaultAsync(l => l.Id == lineId, cancellationToken);
        if (line is null) { return new LineSendResult(false, "La linea no existe.", null); }
        if (line.Provider != WhatsAppProvider.Evolution)
        {
            return new LineSendResult(false, "Las reacciones por id solo aplican a lineas Evolution en este corte.", null);
        }
        if (line.Status != WhatsAppLineStatus.Connected) { return new LineSendResult(false, "La linea no esta conectada.", null); }
        var server = await ResolveServerAsync(cancellationToken);
        if (server is null) { return new LineSendResult(false, "No hay servidor Evolution configurado.", null); }
        var (baseUrl, apiKey) = server.Value;
        var digits = new string(phone.Where(char.IsDigit).ToArray());
        var remoteJid = $"{digits}@s.whatsapp.net";
        var result = await _client.SendReactionAsync(baseUrl, apiKey, EvoInstance(line), remoteJid, externalMessageId, emoji, cancellationToken);
        return new LineSendResult(result.Ok, result.Error, null);
    }

    public Task<InboundMediaResult> FetchInboundMediaAsync(Guid lineId, string externalMessageId, CancellationToken cancellationToken = default)
    {
        // STUB Fase 2: cuando se porte el endpoint /chat/getBase64FromMediaMessage, este metodo
        // descargara el media. Por ahora los mensajes con adjunto quedan persistidos con MediaUrl=null.
        _ = lineId; _ = externalMessageId; _ = cancellationToken;
        return Task.FromResult(new InboundMediaResult(false, null, null, null, "FetchInboundMediaAsync no implementado aun."));
    }

    public async Task<int> ApplyWebhookToConnectedLinesAsync(Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var (baseWebhookUrl, webhookToken) = await EffectiveWebhookBaseAsync(cancellationToken);
        if (baseWebhookUrl is null || webhookToken is null) { return 0; }
        var server = await ResolveServerAsync(cancellationToken);
        if (server is null) { return 0; }
        var (baseUrl, apiKey) = server.Value;

        var lines = await _db.WhatsAppLines.IgnoreQueryFilters()
            .Where(l => l.Status == WhatsAppLineStatus.Connected).ToListAsync(cancellationToken);
        var applied = 0;
        foreach (var line in lines)
        {
            // El tenantId va en la URL para que ASP.NET Core 10 aplique `.DisableAntiforgery()`
            // (rutas sin parametros no lo hacen bien en el receptor de Blazor Server).
            var webhookUrl = $"{baseWebhookUrl}/{line.TenantId:D}";
            var res = await _client.SetWebhookAsync(baseUrl, apiKey, EvoInstance(line), webhookUrl, webhookToken, cancellationToken);
            if (res.Ok) { applied++; }
        }
        return applied;
    }

    // URL base + token efectivos del webhook segun el modo configurado (dev usa la URL activa del
    // tunel). El tenantId se concatena por linea en ApplyWebhookToConnectedLinesAsync.
    private async Task<(string? Url, string? Token)> EffectiveWebhookBaseAsync(CancellationToken ct)
    {
        var master = await _db.EvolutionMasterConfigs.AsNoTracking().FirstOrDefaultAsync(ct);
        if (master is null || string.IsNullOrWhiteSpace(master.WebhookToken)) { return (null, null); }
        var baseUrl = string.Equals(master.WebhookMode, "Production", StringComparison.OrdinalIgnoreCase)
            ? master.WebhookPublicUrl
            : master.WebhookActiveUrl;
        if (string.IsNullOrWhiteSpace(baseUrl)) { return (null, null); }
        return ($"{baseUrl!.TrimEnd('/')}/webhooks/evolution", master.WebhookToken);
    }

    // Servidor efectivo (URL + API key descifrada) segun la eleccion del tenant.
    private async Task<(string baseUrl, string apiKey)?> ResolveServerAsync(CancellationToken ct)
    {
        var cfg = await _db.TenantEvolutionConfigs.AsNoTracking().FirstOrDefaultAsync(ct);
        if (cfg is not null && !cfg.UseMasterServer)
        {
            if (string.IsNullOrWhiteSpace(cfg.BaseUrl) || string.IsNullOrWhiteSpace(cfg.ApiTokenEncrypted)) { return null; }
            return (cfg.BaseUrl!, _secretProtector.Unprotect(cfg.ApiTokenEncrypted!));
        }
        var master = await _db.EvolutionMasterConfigs.AsNoTracking().FirstOrDefaultAsync(ct);
        if (master is null || string.IsNullOrWhiteSpace(master.BaseUrl) || string.IsNullOrWhiteSpace(master.ApiKeyEncrypted)) { return null; }
        return (master.BaseUrl!, _secretProtector.Unprotect(master.ApiKeyEncrypted!));
    }

    // Nombre de instancia unico en el servidor compartido: cubot_<tenant>_<linea>.
    private static string EvoInstance(WhatsAppLine line) => $"cubot_{line.TenantId:N}_{line.Id:N}";

    private static string? NormalizeBaseUrl(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) { return null; }
        var url = raw.Trim().TrimEnd('/');
        if (url.EndsWith("/manager", StringComparison.OrdinalIgnoreCase)) { url = url[..^"/manager".Length]; }
        return url.TrimEnd('/');
    }

    private string Mask(string encrypted)
    {
        string value;
        try { value = _secretProtector.Unprotect(encrypted); }
        catch { return "(re-ingresar)"; }
        return value.Length <= 4 ? "****" : $"{new string('*', Math.Min(value.Length - 4, 8))}{value[^4..]}";
    }

    private static WhatsAppLineDto Map(WhatsAppLine l) =>
        new(l.Id, l.InstanceName, l.PhoneNumber, l.Status, l.AssignedToTenantUserId, l.LastConnectedAt, l.LastStatusAt,
            l.Provider, l.CloudPhoneNumberId, l.CloudBusinessAccountId,
            !string.IsNullOrEmpty(l.CloudAccessTokenEncrypted),
            !string.IsNullOrEmpty(l.CloudWebhookVerifyTokenEncrypted),
            l.YCloudPhoneNumberId, l.YCloudWabaId, !string.IsNullOrEmpty(l.YCloudApiKeyEncrypted));
}
