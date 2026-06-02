using System.Text;
using System.Text.Json;
using CubotRedManager.Application.Abstractions;
using CubotRedManager.Domain.Entities;
using CubotRedManager.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace CubotRedManager.Application.Tenancy;

public sealed class PublicationExecutorService : IPublicationExecutorService
{
    private const string Network = "tiktok";
    private static readonly HashSet<string> VideoExts = new(StringComparer.OrdinalIgnoreCase)
    { ".mp4", ".mov", ".webm", ".m4v" };
    // En modo sandbox de TikTok solo se permite publicar SELF_ONLY (privado).
    // Una vez auditada la app se podra exponer "PUBLIC_TO_EVERYONE" como opcion.
    private const string PrivacyLevel = "SELF_ONLY";
    private const int MaxStatusPolls = 30;
    private static readonly TimeSpan PollDelay = TimeSpan.FromSeconds(2);

    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly ITikTokApiClient _tiktokApi;
    private readonly ITikTokConnectionService _tiktokConnection;
    private readonly ISecretProtector _protector;
    private readonly IAuditWriter _audit;
    private readonly IUploadPathResolver _uploads;
    private readonly TimeProvider _time;

    public PublicationExecutorService(
        IApplicationDbContext db,
        ITenantContext tenantContext,
        ITikTokApiClient tiktokApi,
        ITikTokConnectionService tiktokConnection,
        ISecretProtector protector,
        IAuditWriter audit,
        IUploadPathResolver uploads,
        TimeProvider time)
    {
        _db = db;
        _tenantContext = tenantContext;
        _tiktokApi = tiktokApi;
        _tiktokConnection = tiktokConnection;
        _protector = protector;
        _audit = audit;
        _uploads = uploads;
        _time = time;
    }

    public async Task<PublicationExecResult> ExecuteAsync(Guid publicationId, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var trace = new StringBuilder();
        var targets = new List<PublicationExecTargetResult>();

        if (_tenantContext.TenantId is not Guid tenantId)
        {
            return new PublicationExecResult(false, PublicationStatus.Failed, "Sin tenant activo.", targets);
        }

        var pub = await _db.Publications
            .Include(p => p.Targets)
            .FirstOrDefaultAsync(p => p.Id == publicationId, cancellationToken);
        if (pub is null)
        {
            return new PublicationExecResult(false, PublicationStatus.Failed, "Publicacion no encontrada.", targets);
        }

        if (pub.Status == PublicationStatus.Published)
        {
            return new PublicationExecResult(true, pub.Status, "Ya estaba publicada.", targets);
        }
        if (pub.Targets.Count == 0)
        {
            pub.Status = PublicationStatus.Failed;
            pub.FailureReason = "Publicacion sin cuentas destino.";
            await _db.SaveChangesAsync(cancellationToken);
            return new PublicationExecResult(false, pub.Status, "[ERROR] Sin targets configurados.", targets);
        }

        // Resolver media URLs locales -> rutas absolutas en disco.
        var urls = DeserializeUrls(pub.MediaUrlsJson);
        if (urls.Count == 0)
        {
            pub.Status = PublicationStatus.Failed;
            pub.FailureReason = "Sin media adjunta.";
            await _db.SaveChangesAsync(cancellationToken);
            return new PublicationExecResult(false, pub.Status, "[ERROR] Sin media adjunta (TikTok exige video).", targets);
        }
        var videoUrl = urls.FirstOrDefault(u => VideoExts.Contains(Path.GetExtension(u)));
        if (videoUrl is null)
        {
            pub.Status = PublicationStatus.Failed;
            pub.FailureReason = "No hay archivo de video en la media adjunta.";
            await _db.SaveChangesAsync(cancellationToken);
            return new PublicationExecResult(false, pub.Status, $"[ERROR] TikTok solo acepta video. La media adjunta es: {string.Join(", ", urls.Select(Path.GetExtension))}.", targets);
        }
        var localPath = _uploads.ResolveFromUrl(videoUrl);
        if (localPath is null || !File.Exists(localPath))
        {
            pub.Status = PublicationStatus.Failed;
            pub.FailureReason = "Archivo de video no encontrado en disco.";
            await _db.SaveChangesAsync(cancellationToken);
            return new PublicationExecResult(false, pub.Status, $"[ERROR] No se encontro {videoUrl} en disco.", targets);
        }
        var videoSize = new FileInfo(localPath).Length;
        trace.AppendLine($"[INFO] Publicando '{Path.GetFileName(localPath)}' ({videoSize / 1024 / 1024.0:F1} MB) en {pub.Targets.Count} cuenta(s).");

        var anySuccess = false;
        var anyFail = false;

        foreach (var target in pub.Targets)
        {
            if (cancellationToken.IsCancellationRequested) { break; }
            var account = await _db.SocialAccounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == target.SocialAccountId, cancellationToken);
            if (account is null)
            {
                targets.Add(new PublicationExecTargetResult(target.Id, target.SocialAccountId, "?", null, false, null, "Cuenta no encontrada."));
                target.Status = PublicationTargetStatus.Failed;
                target.FailureReason = "Cuenta no encontrada.";
                anyFail = true;
                continue;
            }

            if (!string.Equals(account.NetworkCode, Network, StringComparison.OrdinalIgnoreCase))
            {
                targets.Add(new PublicationExecTargetResult(target.Id, account.Id, account.NetworkCode, account.Handle, false, null, "Red no soportada todavia."));
                target.Status = PublicationTargetStatus.Failed;
                target.FailureReason = $"Red {account.NetworkCode} no implementada (solo TikTok por ahora).";
                anyFail = true;
                trace.AppendLine($"[SKIP] @{account.Handle} ({account.NetworkCode}): red no soportada.");
                continue;
            }

            trace.AppendLine($"[TARGET] @{account.Handle}");
            var (ok, publishId, error) = await PublishToTikTokAsync(account, localPath, videoSize, pub.Caption, actorUserId, trace, cancellationToken);
            targets.Add(new PublicationExecTargetResult(target.Id, account.Id, account.NetworkCode, account.Handle, ok, publishId, error));
            if (ok)
            {
                target.Status = PublicationTargetStatus.Published;
                target.ExternalId = publishId;
                target.FailureReason = null;
                anySuccess = true;
            }
            else
            {
                target.Status = PublicationTargetStatus.Failed;
                target.FailureReason = error;
                anyFail = true;
            }
        }

        // Estado final de la Publication: Published si TODOS los targets fueron OK, Failed si NINGUNO.
        // Si fue mixto: Failed + FailureReason indicando parciales.
        var finalStatus = (anySuccess, anyFail) switch
        {
            (true, false) => PublicationStatus.Published,
            (false, true) => PublicationStatus.Failed,
            (true, true) => PublicationStatus.Failed,   // parcial = Failed (puedes reintentar solo los failed)
            _ => PublicationStatus.Failed
        };
        pub.Status = finalStatus;
        if (finalStatus == PublicationStatus.Published) { pub.PublishedAt = _time.GetUtcNow(); pub.FailureReason = null; }
        else { pub.FailureReason = string.Join(" | ", targets.Where(t => !t.Success).Select(t => $"@{t.Handle}: {t.Error}")); }

        _audit.Write(actorUserId, "publication.execute", nameof(Publication), pub.Id,
            previousValue: null, newValue: new { finalStatus = finalStatus.ToString(), successCount = targets.Count(t => t.Success), failureCount = targets.Count(t => !t.Success) },
            tenantId: tenantId);
        await _db.SaveChangesAsync(cancellationToken);

        trace.AppendLine($"[FIN] Estado final: {finalStatus}");
        return new PublicationExecResult(anySuccess && !anyFail, finalStatus, trace.ToString(), targets);
    }

    private async Task<(bool ok, string? publishId, string? error)> PublishToTikTokAsync(
        SocialAccount account, string localPath, long videoSize, string caption, Guid actorUserId, StringBuilder trace, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(account.AccessTokenEncrypted))
        {
            return (false, null, "Cuenta sin Access Token. Reconecta.");
        }
        var token = _protector.Unprotect(account.AccessTokenEncrypted);

        // --- 1) Init ---
        // Sandbox mode: usamos INBOX UPLOAD (el video llega a la bandeja de borradores del
        // usuario en su app TikTok; el usuario lo publica manualmente). Direct Post requiere
        // app auditada y permiso explicito en TikTok Developer Portal.
        trace.AppendLine("  [1/3] init upload a INBOX (modo sandbox)...");
        var init = await _tiktokApi.PublishVideoInitAsync(token, caption, videoSize, PrivacyLevel, ct);
        // Manejo 40105 con refresh + retry
        if (NeedsRefresh(init))
        {
            trace.AppendLine("  [INFO] Token expirado. Refrescando...");
            var rr = await _tiktokConnection.RefreshAccountAsync(account.Id, actorUserId, ct);
            if (!rr.Success) { return (false, null, "Token expirado y refresh fallido: " + rr.Error); }
            var fresh = await _db.SocialAccounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == account.Id, ct);
            if (fresh is null || string.IsNullOrEmpty(fresh.AccessTokenEncrypted)) { return (false, null, "No se pudo releer el token tras refresh."); }
            token = _protector.Unprotect(fresh.AccessTokenEncrypted);
            init = await _tiktokApi.PublishVideoInitAsync(token, caption, videoSize, PrivacyLevel, ct);
        }
        if (init.Code != 0)
        {
            trace.AppendLine($"  [ERROR] init: {init.Message}");
            return (false, null, init.Message ?? "init fallido");
        }

        // Extraer publish_id + upload_url
        string? publishId = null;
        string? uploadUrl = null;
        try
        {
            using var doc = JsonDocument.Parse(init.RawJson);
            if (doc.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object)
            {
                if (data.TryGetProperty("publish_id", out var pi) && pi.ValueKind == JsonValueKind.String) { publishId = pi.GetString(); }
                if (data.TryGetProperty("upload_url", out var uu) && uu.ValueKind == JsonValueKind.String) { uploadUrl = uu.GetString(); }
            }
        }
        catch (Exception ex) { return (false, null, "init JSON invalido: " + ex.Message); }
        if (string.IsNullOrEmpty(publishId) || string.IsNullOrEmpty(uploadUrl))
        {
            return (false, null, "init no devolvio publish_id/upload_url");
        }
        trace.AppendLine($"  [OK] publish_id={publishId}");

        // --- 2) PUT del archivo al upload_url ---
        trace.AppendLine("  [2/3] subiendo archivo...");
        await using (var fs = File.OpenRead(localPath))
        {
            var ext = Path.GetExtension(localPath).ToLowerInvariant();
            var contentType = ext switch
            {
                ".mp4" or ".m4v" => "video/mp4",
                ".mov" => "video/quicktime",
                ".webm" => "video/webm",
                _ => "video/mp4"
            };
            var (ok, err) = await _tiktokApi.PublishVideoUploadAsync(uploadUrl!, fs, videoSize, contentType, ct);
            if (!ok)
            {
                trace.AppendLine($"  [ERROR] upload: {err}");
                return (false, publishId, "Upload fallido: " + err);
            }
        }
        trace.AppendLine("  [OK] upload completado.");

        // --- 3) Polling de status ---
        trace.AppendLine("  [3/3] polling status...");
        for (var i = 0; i < MaxStatusPolls; i++)
        {
            try { await Task.Delay(PollDelay, ct); } catch (OperationCanceledException) { break; }
            var st = await _tiktokApi.PublishStatusAsync(token, publishId!, ct);
            if (st.Code != 0) { trace.AppendLine($"  [WARN] status code={st.Code}: {st.Message}"); continue; }

            try
            {
                using var doc = JsonDocument.Parse(st.RawJson);
                if (doc.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object
                    && data.TryGetProperty("status", out var s) && s.ValueKind == JsonValueKind.String)
                {
                    var status = s.GetString();
                    trace.AppendLine($"  [POLL {i + 1}] status={status}");
                    // SEND_TO_USER_INBOX = el video llego al inbox de TikTok (esperado en sandbox).
                    // PUBLISH_COMPLETE   = se publico directamente (requiere app auditada).
                    if (status is "PUBLISH_COMPLETE" or "SEND_TO_USER_INBOX")
                    {
                        trace.AppendLine(status == "SEND_TO_USER_INBOX"
                            ? "  [OK] Video enviado al inbox de TikTok. Abre la app y publicalo manualmente."
                            : "  [OK] Publicado en TikTok directamente.");
                        return (true, publishId, null);
                    }
                    if (status == "FAILED")
                    {
                        var failReason = data.TryGetProperty("fail_reason", out var fr) && fr.ValueKind == JsonValueKind.String ? fr.GetString() : "TikTok FAILED";
                        return (false, publishId, failReason);
                    }
                }
            }
            catch { /* tolerante */ }
        }
        return (false, publishId, "Timeout esperando status PUBLISH_COMPLETE.");
    }

    private static bool NeedsRefresh(TikTokApiPage page)
    {
        if (page.Code == 40105) { return true; }
        if (page.Message is { } m && (m.Contains("expired", StringComparison.OrdinalIgnoreCase)
                                       || m.Contains("invalid token", StringComparison.OrdinalIgnoreCase))) { return true; }
        return false;
    }

    private static IReadOnlyList<string> DeserializeUrls(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) { return Array.Empty<string>(); }
        try { return JsonSerializer.Deserialize<List<string>>(json) ?? new(); }
        catch { return Array.Empty<string>(); }
    }
}
