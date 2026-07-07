using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using CubotRedManager.Application.Abstractions;
using CubotRedManager.Application.Tenancy;
using CubotRedManager.Domain.Entities;
using CubotRedManager.Domain.Enums;
using CubotRedManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CubotRedManager.Web.BackgroundJobs;

/// <summary>
/// Worker del Modulo 2.11 (Autorespuesta de Comentarios TikTok).
/// Cada CheckInterval recorre AutoReplyConfig.IsActive=true, respeta la VENTANA DE PROGRAMACION
/// configurada por el operador (Frequency vs ultimo log + ActiveHoursMask + ActiveDaysOfWeekMask),
/// procesa comentarios pendientes de la bandeja (InboxMessage de tipo Comment sin reply), aplica
/// blacklist, genera respuesta segun Mode (Template/Ai/Mixed) y la publica via ITikTokApiClient.
///
/// Escribe AutoReplyJobLog inmutable por ejecucion de cada cuenta con Status + contadores + Trace
/// (lineas de [INFO]/[WARN]/[ERROR]/[REPLY]/[SKIP]/[BLACKLIST]). Esa traza es lo que la pagina
/// "Autorespuesta - Logs" muestra al operador.
///
/// Soporta DryRun (no toca TikTok pero igual escribe el log) para verificar pipeline sin sandbox.
/// </summary>
public sealed class AutoReplyWorker : BackgroundService
{
    private static readonly Guid SystemUserId = Guid.Empty;
    private const string Network = "tiktok";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AutoReplyWorker> _logger;
    private readonly AutoReplyOptions _options;
    private readonly TimeZoneInfo _tz;

    public AutoReplyWorker(IServiceScopeFactory scopeFactory, ILogger<AutoReplyWorker> logger, AutoReplyOptions options)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _options = options;
        _tz = ResolveTimeZone(options.TenantTimeZone);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(TimeSpan.FromSeconds(25), stoppingToken); }
        catch (OperationCanceledException) { return; }

        _logger.LogInformation("AutoReplyWorker arrancado. CheckInterval={ci} DryRun={dr} TZ={tz}",
            _options.CheckInterval, _options.DryRun, _tz.Id);

        while (!stoppingToken.IsCancellationRequested)
        {
            try { await RunCycleAsync(stoppingToken); }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { _logger.LogError(ex, "Ciclo AutoReplyWorker fallo."); }

            try { await Task.Delay(_options.CheckInterval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task RunCycleAsync(CancellationToken ct)
    {
        // 0) Retencion: borrar logs mas viejos que RetentionDays. Operacion idempotente y barata
        //    (un solo DELETE indexado por StartedAt). El operador puede usar el boton "Eliminar
        //    logs" de la UI para limpieza manual.
        if (_options.RetentionDays > 0)
        {
            try
            {
                using var pscope = _scopeFactory.CreateScope();
                var pdb = pscope.ServiceProvider.GetRequiredService<CubotRedManagerDbContext>();
                var cutoffUtc = DateTimeOffset.UtcNow.AddDays(-_options.RetentionDays);
                var purged = await pdb.AutoReplyJobLogs.IgnoreQueryFilters()
                    .Where(l => l.StartedAt < cutoffUtc)
                    .ExecuteDeleteAsync(ct);
                if (purged > 0)
                {
                    _logger.LogInformation("AutoReplyWorker purga retencion: {n} logs antiguos eliminados (>{d} dias)", purged, _options.RetentionDays);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AutoReplyWorker fallo la purga por retencion");
            }
        }

        // 1) Listar configs activas (ignorando filtro tenant: somos worker de sistema).
        List<AutoReplyTarget> targets;
        using (var scope = _scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CubotRedManagerDbContext>();
            // Hacemos los filtros sobre las tablas crudas (EF los traduce a SQL) y mapeamos
            // al record en memoria al final con AsEnumerable().
            var rows = await db.AutoReplyConfigs.IgnoreQueryFilters().AsNoTracking()
                .Where(c => c.IsActive)
                .Join(db.SocialAccounts.IgnoreQueryFilters().AsNoTracking()
                        .Where(a => a.NetworkCode == Network && a.Status == SocialAccountStatus.Connected),
                    c => c.SocialAccountId, a => a.Id,
                    (c, a) => new
                    {
                        c.Id, c.TenantId, a.NetworkCode, a.Status, c.SocialAccountId,
                        c.Frequency, c.CronCustom, c.ActiveHoursMask, c.ActiveDaysOfWeekMask,
                        c.Mode, c.MaxRepliesPerRun, c.DelayMinSeconds, c.DelayMaxSeconds,
                        c.BlacklistKeywords, c.DefaultTemplate, AccountId = a.Id,
                        AccountExternalId = a.ExternalId, AccountHandle = a.Handle,
                        c.AiAgentId,
                        c.SummaryEnabled, c.SummaryLineId, c.SummaryTargetType, c.SummaryTarget, c.SummaryTemplate
                    })
                .ToListAsync(ct);
            targets = rows.Select(r => new AutoReplyTarget(
                    r.Id, r.TenantId, r.AccountId, r.SocialAccountId, r.NetworkCode, r.Status,
                    r.Frequency, r.CronCustom, r.ActiveHoursMask, r.ActiveDaysOfWeekMask,
                    r.Mode, r.MaxRepliesPerRun, r.DelayMinSeconds, r.DelayMaxSeconds,
                    r.BlacklistKeywords, r.DefaultTemplate,
                    r.AccountExternalId, r.AccountHandle, r.AiAgentId,
                    r.SummaryEnabled, r.SummaryLineId, r.SummaryTargetType, r.SummaryTarget, r.SummaryTemplate))
                .ToList();
        }

        if (targets.Count == 0) { return; }

        var nowLocal = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, _tz);
        foreach (var t in targets)
        {
            if (ct.IsCancellationRequested) { break; }
            try { await ProcessAccountAsync(t, nowLocal, ct); }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Fallo procesando AutoReply de cuenta {acc} tenant {ten}", t.SocialAccountId, t.TenantId);
            }
        }
    }

    private async Task ProcessAccountAsync(AutoReplyTarget t, DateTimeOffset nowLocal, CancellationToken ct)
    {
        // 2) Ventana de programacion: hora local, dia local y "ya toca?".
        var startedAt = DateTimeOffset.UtcNow;
        var trace = new StringBuilder();
        trace.AppendLine($"[INFO] Ciclo AutoReply - cuenta {t.SocialAccountId} tenant {t.TenantId}");
        trace.AppendLine($"[INFO] Hora local ({_tz.Id}): {nowLocal:yyyy-MM-dd HH:mm:ss}");
        trace.AppendLine($"[INFO] Config: mode={t.Mode} frequency={t.Frequency} maxPerRun={t.MaxRepliesPerRun} delay={t.DelayMinSeconds}-{t.DelayMaxSeconds}s dryRun={_options.DryRun}");

        var hourBit = 1 << nowLocal.Hour;
        // Levantamos el ultimo log de esta cuenta UNA sola vez: lo necesitamos tanto para
        // throttle por Frequency como para evitar spamear logs de "fuera de horario".
        DateTimeOffset? lastStartedUtc;
        using (var scope = _scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CubotRedManagerDbContext>();
            lastStartedUtc = await db.AutoReplyJobLogs.IgnoreQueryFilters().AsNoTracking()
                .Where(l => l.SocialAccountId == t.SocialAccountId)
                .OrderByDescending(l => l.StartedAt)
                .Select(l => (DateTimeOffset?)l.StartedAt)
                .FirstOrDefaultAsync(ct);
        }

        // Si esta fuera de la ventana horaria, escribimos UN log heartbeat por hora maximo (asi el
        // operador ve que el sistema esta vivo aunque sea de noche, sin saturar la BD).
        if ((t.ActiveHoursMask & hourBit) == 0)
        {
            if (lastStartedUtc is null || (DateTimeOffset.UtcNow - lastStartedUtc.Value) >= TimeSpan.FromHours(1))
            {
                using var s = _scopeFactory.CreateScope();
                var db = s.ServiceProvider.GetRequiredService<CubotRedManagerDbContext>();
                trace.AppendLine($"[INFO] Fuera de ventana horaria. Hora local: {nowLocal:HH:mm}. ActiveHoursMask=0x{t.ActiveHoursMask:X}. No se procesa.");
                trace.AppendLine("[INFO] El sistema esta corriendo. Volvera a evaluar en el proximo tick (1 minuto).");
                await WriteLogAsync(db, t, AutoReplyJobStatus.Ok, startedAt, 0, 0, 0, 0, trace, ct);
            }
            return;
        }

        // Lun=bit0 .. Dom=bit6. DayOfWeek de .NET: Sun=0, Mon=1..., Sat=6 -> mapeo a Lun=0..Dom=6.
        var dayBit = 1 << (((int)nowLocal.DayOfWeek + 6) % 7);
        if ((t.ActiveDaysOfWeekMask & dayBit) == 0)
        {
            if (lastStartedUtc is null || (DateTimeOffset.UtcNow - lastStartedUtc.Value) >= TimeSpan.FromHours(1))
            {
                using var s = _scopeFactory.CreateScope();
                var db = s.ServiceProvider.GetRequiredService<CubotRedManagerDbContext>();
                trace.AppendLine($"[INFO] Dia no activo segun configuracion (DayOfWeek={nowLocal.DayOfWeek}, mask=0x{t.ActiveDaysOfWeekMask:X}).");
                await WriteLogAsync(db, t, AutoReplyJobStatus.Ok, startedAt, 0, 0, 0, 0, trace, ct);
            }
            return;
        }

        // Frequency vs ultimo log: si el ultimo arrancho hace menos de X, aun no toca.
        var minGap = MinGapForFrequency(t.Frequency, t.CronCustom);
        if (lastStartedUtc is not null && (DateTimeOffset.UtcNow - lastStartedUtc.Value) < minGap)
        {
            return;
        }

        // 3) Adentro del scope tenant: leer comentarios pendientes y procesar.
        using var scopeTenant = _scopeFactory.CreateScope();
        var ambient = scopeTenant.ServiceProvider.GetRequiredService<IAmbientTenantOverride>();
        ambient.Set(t.TenantId, SystemUserId);
        // Bitacora de esta corrida para el resumen WhatsApp (autor / comentario / respuesta).
        var summaryEntries = new List<(string author, string comment, string reply)>();
        try
        {
            var db = scopeTenant.ServiceProvider.GetRequiredService<CubotRedManagerDbContext>();
            var apiClient = scopeTenant.ServiceProvider.GetRequiredService<ITikTokApiClient>();
            var protector = scopeTenant.ServiceProvider.GetRequiredService<ISecretProtector>();
            var inference = scopeTenant.ServiceProvider.GetRequiredService<IAiInferenceService>();
            var connection = scopeTenant.ServiceProvider.GetRequiredService<ITikTokConnectionService>();

            // Acc + token
            var account = await db.SocialAccounts.FirstOrDefaultAsync(a => a.Id == t.SocialAccountId, ct);
            if (account is null || string.IsNullOrEmpty(account.AccessTokenEncrypted) || string.IsNullOrEmpty(account.ExternalId))
            {
                trace.AppendLine("[ERROR] Cuenta sin token o sin business_id. Skip.");
                await WriteLogAsync(db, t, AutoReplyJobStatus.Error, startedAt, 0, 0, 1, 0, trace, ct);
                return;
            }

            // Refresh proactivo: si el access_token caduca en < 10 min, lo renovamos AHORA. Asi
            // evitamos que TikTok rebote con code=40105 a mitad del bucle (lo cual dejaria las
            // respuestas perdidas hasta el siguiente ciclo). Si el refresh falla, abortamos.
            var refreshBuffer = TimeSpan.FromMinutes(10);
            if (account.ExpiresAt is { } expUtc && expUtc - DateTimeOffset.UtcNow < refreshBuffer)
            {
                trace.AppendLine($"[INFO] Token expira en {(expUtc - DateTimeOffset.UtcNow).TotalSeconds:F0}s (umbral 10min). Refrescando antes de procesar...");
                var refresh = await connection.RefreshAccountAsync(account.Id, SystemUserId, ct);
                if (!refresh.Success)
                {
                    trace.AppendLine($"[ERROR] Refresh fallo: {refresh.Error}");
                    trace.AppendLine(refresh.Trace?.TrimEnd() ?? "");
                    trace.AppendLine("[INFO] No se procesan comentarios este ciclo. El operador debe reconectar la cuenta manualmente desde /cuentas-sociales si el refresh_token tambien caduco.");
                    await WriteLogAsync(db, t, AutoReplyJobStatus.Error, startedAt, 0, 0, 1, 0, trace, ct);
                    return;
                }
                // Releer la cuenta con el token nuevo recien guardado.
                account = await db.SocialAccounts.FirstOrDefaultAsync(a => a.Id == t.SocialAccountId, ct);
                if (account is null || string.IsNullOrEmpty(account.AccessTokenEncrypted))
                {
                    trace.AppendLine("[ERROR] Tras refresh, la cuenta quedo sin token. Skip.");
                    await WriteLogAsync(db, t, AutoReplyJobStatus.Error, startedAt, 0, 0, 1, 0, trace, ct);
                    return;
                }
                trace.AppendLine($"[OK] Token refrescado. Nueva expiracion: {account.ExpiresAt:yyyy-MM-dd HH:mm:ss} UTC.");
            }

            string token;
            try { token = protector.Unprotect(account.AccessTokenEncrypted); }
            catch
            {
                trace.AppendLine("[ERROR] Access token cifrado con version anterior. Reconecta la cuenta.");
                // Auto-correccion: el badge "Conectada" mentia hasta ahora. Lo bajamos a
                // Disconnected y dejamos LastSyncError visible para que la UI muestre la verdad.
                account.Status = SocialAccountStatus.Disconnected;
                account.LastSyncError = "Token no descifrable (llave de DataProtection perdida). Reconecta la cuenta para regenerar el token.";
                await db.SaveChangesAsync(ct);
                trace.AppendLine("[INFO] SocialAccount.Status -> Disconnected. La UI de Cuentas sociales mostrara la cuenta como desconectada.");
                await WriteLogAsync(db, t, AutoReplyJobStatus.Error, startedAt, 0, 0, 1, 0, trace, ct);
                return;
            }

            // Templates
            var templates = await db.AutoReplyTemplates.AsNoTracking()
                .Where(tp => tp.ConfigId == t.ConfigId)
                .OrderBy(tp => tp.SortOrder)
                .Select(tp => new TemplateRow(tp.Keywords, tp.Body))
                .ToListAsync(ct);
            trace.AppendLine($"[INFO] {templates.Count} plantillas cargadas, defaultTemplate={(string.IsNullOrEmpty(t.DefaultTemplate) ? "(ninguno)" : "configurado")}");

            // Comentarios pendientes a responder.
            // Reglas:
            //  1) Es de esta cuenta TikTok y es Comment Unread.
            //  2) NO tiene ya un InboxReply (humano o bot anterior).
            //  3) NO es del propio dueño de la cuenta conectada (sus respuestas humanas en hilos
            //     y las del propio bot quedan excluidas). Comparamos por external_id Y por handle
            //     porque TikTok a veces devuelve uno u otro vacio en los comentarios del dueño.
            //
            // Esta regla permite responder tanto a comentarios top-level como a sub-replies
            // mientras el autor sea OTRO usuario (cliente real).
            var ownerExt = t.AccountExternalId ?? "";
            var ownerHandle = t.AccountHandle ?? "";
            var pending = await (
                from m in db.InboxMessages.AsNoTracking()
                where m.SocialAccountId == t.SocialAccountId
                      && m.NetworkCode == Network
                      && m.Type == InboxMessageType.Comment
                      && m.Status == InboxStatus.Unread
                      && !db.InboxReplies.Any(r => r.InboxMessageId == m.Id)
                      // Excluir comentarios del propio dueño:
                      && !(ownerExt != "" && m.AuthorExternalId == ownerExt)
                      && !(ownerHandle != "" && m.AuthorName == ownerHandle)
                orderby m.ReceivedAt
                select m
            ).Take(_options.MaxPendingFetchPerAccount).ToListAsync(ct);

            trace.AppendLine($"[INFO] {pending.Count} comentarios pendientes en la bandeja");

            if (pending.Count == 0)
            {
                trace.AppendLine("[INFO] Nada por procesar. Cierre OK.");
                await WriteLogAsync(db, t, AutoReplyJobStatus.Ok, startedAt, 0, 0, 0, 0, trace, ct);
                return;
            }

            var blacklist = ParseBlacklist(t.BlacklistKeywords);
            int processed = 0, replied = 0, errors = 0, omitted = 0;
            var rng = new Random();

            // Elegir agente IA si el modo lo necesita.
            //  1) Si AutoReplyConfig.AiAgentId esta configurado, usarlo (decision explicita del operador).
            //  2) Si no, fallback a heuristica: agente cuyo nombre coincida con el cliente, o el primero activo.
            Guid? agentId = null;
            if (t.Mode != AutoReplyMode.Template)
            {
                if (t.AiAgentId is not null)
                {
                    var exists = await db.AiAgents.AsNoTracking()
                        .AnyAsync(a => a.Id == t.AiAgentId.Value && a.IsActive, ct);
                    if (exists) { agentId = t.AiAgentId; }
                    else { trace.AppendLine($"[WARN] El agente IA configurado ({t.AiAgentId}) no esta activo. Cayendo a auto-seleccion."); }
                }
                if (agentId is null)
                {
                    agentId = await PickAgentIdAsync(db, account.ClientId, ct);
                }
                if (agentId is null)
                {
                    trace.AppendLine("[WARN] Mode requiere IA pero no hay agente IA activo en el tenant. Cae a Template-only.");
                }
                else
                {
                    var agentName = await db.AiAgents.AsNoTracking()
                        .Where(a => a.Id == agentId.Value).Select(a => a.Name).FirstOrDefaultAsync(ct);
                    trace.AppendLine($"[INFO] Agente IA: \"{agentName}\" ({agentId})");
                }
            }

            foreach (var msg in pending)
            {
                if (ct.IsCancellationRequested) { break; }
                if (replied >= t.MaxRepliesPerRun)
                {
                    trace.AppendLine($"[INFO] MaxRepliesPerRun ({t.MaxRepliesPerRun}) alcanzado. Corte.");
                    break;
                }
                processed++;

                // Blacklist
                if (MatchesBlacklist(msg.Body, blacklist, out var hit))
                {
                    trace.AppendLine($"[BLACKLIST] Comentario {msg.ExternalId} matchea \"{hit}\" -> NO se responde, queda para humano");
                    omitted++;
                    continue;
                }

                // Decidir respuesta segun modo. Reglas:
                //  - Mode=Template (puro): devuelve la plantilla literal. Sin variacion. Determinista.
                //  - Mode=Ai (puro): pasa el comentario al agente IA, sin plantillas. Variado.
                //  - Mode=Mixed (DEFAULT): si una plantilla matchea Y hay agente IA, le PASAMOS la
                //    plantilla al agente como sugerencia para que genere una variacion que respete
                //    el sentido. Si no hay agente IA, cae a la plantilla literal. Si nada matchea
                //    y hay agente, IA pura. Como ultimo recurso, DefaultTemplate.
                string? replyText = null;
                string source = "(none)";

                string? templateMatch = MatchTemplate(msg.Body, templates);

                if (t.Mode == AutoReplyMode.Template)
                {
                    // Determinista: plantilla literal o default. No usa IA.
                    if (templateMatch is not null) { replyText = templateMatch; source = "template"; }
                    else if (!string.IsNullOrWhiteSpace(t.DefaultTemplate)) { replyText = t.DefaultTemplate; source = "default-template"; }
                }
                else if (t.Mode == AutoReplyMode.Ai && agentId is not null)
                {
                    // IA pura: solo el comentario, sin contexto de plantilla.
                    replyText = await GenerateAiReplyAsync(inference, agentId.Value, msg.Body, suggestion: null, trace, ct);
                    if (replyText is not null) { source = "ia"; }
                }
                else if (t.Mode == AutoReplyMode.Mixed)
                {
                    if (agentId is not null)
                    {
                        // Pasa la plantilla matcheada (si la hay) al agente como sugerencia.
                        // Resultado: variacion personalizada, no copia literal.
                        replyText = await GenerateAiReplyAsync(inference, agentId.Value, msg.Body, suggestion: templateMatch ?? t.DefaultTemplate, trace, ct);
                        if (replyText is not null) { source = templateMatch is not null ? "ia+template" : "ia"; }
                    }
                    if (replyText is null)
                    {
                        // Fallback sin agente: comportamiento legacy (plantilla literal).
                        if (templateMatch is not null) { replyText = templateMatch; source = "template (fallback sin IA)"; }
                        else if (!string.IsNullOrWhiteSpace(t.DefaultTemplate)) { replyText = t.DefaultTemplate; source = "default-template (fallback sin IA)"; }
                    }
                }

                if (string.IsNullOrWhiteSpace(replyText))
                {
                    trace.AppendLine($"[SKIP] Comentario {msg.ExternalId}: sin match de plantilla, sin IA disponible, sin default. Omitido.");
                    omitted++;
                    continue;
                }

                replyText = TruncateForTikTok(replyText);

                // Delay anti-bot (skippable en dry-run para mantener test rapido)
                if (!_options.DryRun && replied > 0)
                {
                    var min = Math.Max(2, t.DelayMinSeconds);
                    var max = Math.Max(min, t.DelayMaxSeconds);
                    var waitSec = rng.Next(min, max + 1);
                    trace.AppendLine($"[INFO] Espera anti-bot {waitSec}s antes de responder");
                    try { await Task.Delay(TimeSpan.FromSeconds(waitSec), ct); }
                    catch (OperationCanceledException) { break; }
                }

                // Publicar (o simular en DryRun)
                string? externalRefId = null;
                bool sentOk;
                if (_options.DryRun)
                {
                    trace.AppendLine($"[DRY-RUN] {source} -> comentario {msg.ExternalId} respuesta: \"{Truncate(replyText, 80)}\"");
                    sentOk = true;
                }
                else
                {
                    try
                    {
                        var resp = await apiClient.PostCommentReplyAsync(
                            token, account.ExternalId, msg.RelatedVideoExternalId ?? "", msg.ExternalId, replyText, ct);
                        sentOk = resp.Code == 0;
                        // En ambos casos (ok o error) loggeamos la respuesta cruda de TikTok para
                        // que el operador pueda inspeccionar exactamente que devolvio el endpoint.
                        var rawPreview = string.IsNullOrEmpty(resp.RawJson)
                            ? "(sin body)"
                            : Truncate(resp.RawJson, 240);
                        if (!sentOk)
                        {
                            trace.AppendLine($"[ERROR] TikTok rechazo reply al comentario {msg.ExternalId}: code={resp.Code} msg={resp.Message}");
                            trace.AppendLine($"[TIKTOK] raw: {rawPreview}");
                        }
                        else
                        {
                            externalRefId = msg.ExternalId; // TikTok no devuelve un id propio del reply en este endpoint
                            trace.AppendLine($"[REPLY] {source} -> comentario {msg.ExternalId} respondido: \"{Truncate(replyText, 80)}\"");
                            trace.AppendLine($"[TIKTOK] code={resp.Code} msg={resp.Message ?? "OK"} raw: {rawPreview}");
                        }
                    }
                    catch (Exception apiEx)
                    {
                        sentOk = false;
                        trace.AppendLine($"[ERROR] Excepcion al publicar reply a {msg.ExternalId}: {apiEx.Message}");
                    }
                }

                if (!sentOk) { errors++; continue; }

                // Persistir InboxReply + marcar el mensaje como Read (operador puede cerrarlo despues)
                db.InboxReplies.Add(new InboxReply
                {
                    TenantId = t.TenantId,
                    InboxMessageId = msg.Id,
                    Body = replyText,
                    SentByTenantUserId = SystemUserId,
                    SentAt = DateTimeOffset.UtcNow,
                    ExternalRefId = externalRefId,
                    Status = _options.DryRun ? "SentDryRun" : "Sent"
                });
                var tracked = await db.InboxMessages.FirstOrDefaultAsync(m => m.Id == msg.Id, ct);
                if (tracked is not null)
                {
                    // El bot acaba de responder este comentario, marcamos Replied. Asi:
                    //  - El contador "Pendientes" del Dashboard TikTok (que cuenta Status != Replied)
                    //    deja de listarlo.
                    //  - La UI Bandeja unificada lo muestra correctamente como respondido.
                    tracked.Status = InboxStatus.Replied;
                }
                await db.SaveChangesAsync(ct);
                summaryEntries.Add((msg.AuthorName ?? "(anon)", msg.Body ?? "", replyText));
                replied++;
            }

            var finalStatus = errors == 0
                ? AutoReplyJobStatus.Ok
                : (replied > 0 ? AutoReplyJobStatus.Warning : AutoReplyJobStatus.Error);
            trace.AppendLine($"[INFO] Cierre: processed={processed} replied={replied} omitted={omitted} errors={errors}");
            await WriteLogAsync(db, t, finalStatus, startedAt, processed, replied, errors, omitted, trace, ct);

            // 4) Resumen por WhatsApp (Evolution) — siempre que este habilitado. Best effort:
            // errores aqui no propagan a la corrida principal, solo se logean en trace.
            if (t.SummaryEnabled)
            {
                try { await SendSummaryAsync(scopeTenant.ServiceProvider, t, processed, replied, errors, summaryEntries, ct); }
                catch (Exception summaryEx) { _logger.LogWarning(summaryEx, "Fallo enviando resumen WA de cuenta {acc}", t.SocialAccountId); }
            }
        }
        finally { ambient.Set(null, null); }
    }

    private async Task SendSummaryAsync(IServiceProvider sp, AutoReplyTarget t,
        int processed, int replied, int errors,
        List<(string author, string comment, string reply)> entries, CancellationToken ct)
    {
        if (t.SummaryLineId is null || string.IsNullOrWhiteSpace(t.SummaryTarget))
        {
            _logger.LogInformation("Resumen WA de cuenta {acc}: config incompleta (linea o destinatario vacios).", t.SocialAccountId);
            return;
        }
        var connector = sp.GetRequiredService<CubotRedManager.Application.Tenancy.IWhatsAppConnectorService>();
        var nowLocal = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, _tz);
        var tpl = t.SummaryTemplate;
        if (string.IsNullOrWhiteSpace(tpl))
        {
            tpl = "Resumen autorespuesta cuenta {{cuenta}}\nEjecutado: {{ejecutado_a}}\nRevisados: {{revisados}} - Respondidos: {{respondidos}} - Fallidos: {{fallidos}}\n\n{{comentarios}}";
        }

        var comentariosBlock = entries.Count == 0
            ? "(ninguna respuesta enviada)"
            : string.Join("\n", entries.Select(e => $"- {e.author}: {Truncate(e.comment, 80)} -> {Truncate(e.reply, 80)}"));

        var msg = tpl
            .Replace("{{cuenta}}", t.AccountHandle ?? t.AccountExternalId ?? "(cuenta)")
            .Replace("{{ejecutado_a}}", nowLocal.ToString("yyyy-MM-dd HH:mm"))
            .Replace("{{revisados}}", processed.ToString())
            .Replace("{{respondidos}}", replied.ToString())
            .Replace("{{fallidos}}", errors.ToString())
            .Replace("{{comentarios}}", comentariosBlock);

        // Para Phone: numero E.164 sin '+'. Para Group: el JID xxx@g.us (Evolution acepta el JID
        // directamente como destino en el mismo endpoint sendText).
        var target = t.SummaryTarget!.Trim();
        var res = await connector.SendTestAsync(t.SummaryLineId.Value, target, msg, SystemUserId, ct);
        if (!res.Ok)
        {
            _logger.LogWarning("Resumen WA de cuenta {acc}: envio fallo - {err}", t.SocialAccountId, res.Error);
        }
    }


    // ----------------- helpers -----------------

    private static TimeSpan MinGapForFrequency(AutoReplyFrequency f, string? cronCustom)
    {
        return f switch
        {
            AutoReplyFrequency.Every15m => TimeSpan.FromMinutes(15),
            AutoReplyFrequency.Every30m => TimeSpan.FromMinutes(30),
            AutoReplyFrequency.Every1h => TimeSpan.FromHours(1),
            AutoReplyFrequency.Every2h => TimeSpan.FromHours(2),
            AutoReplyFrequency.Every4h => TimeSpan.FromHours(4),
            AutoReplyFrequency.Daily => TimeSpan.FromHours(24),
            AutoReplyFrequency.Custom => InferGapFromCron(cronCustom),
            _ => TimeSpan.FromMinutes(30)
        };
    }

    /// <summary>
    /// Lectura conservadora del cron */N * * * * para sacar el paso en minutos. Cron mas complejos
    /// caen a 30 min como fallback hasta que tengamos una libreria de parseo.
    /// </summary>
    private static TimeSpan InferGapFromCron(string? cron)
    {
        if (string.IsNullOrWhiteSpace(cron)) { return TimeSpan.FromMinutes(30); }
        var m = Regex.Match(cron.Trim(), @"^\*/(\d+)\s+\*\s+\*\s+\*\s+\*$");
        if (m.Success && int.TryParse(m.Groups[1].Value, out var n) && n > 0) { return TimeSpan.FromMinutes(n); }
        m = Regex.Match(cron.Trim(), @"^\d+\s+\*/(\d+)\s+\*\s+\*\s+\*$");
        if (m.Success && int.TryParse(m.Groups[1].Value, out var h) && h > 0) { return TimeSpan.FromHours(h); }
        return TimeSpan.FromMinutes(30);
    }

    private static IReadOnlyList<string> ParseBlacklist(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) { return Array.Empty<string>(); }
        return raw
            .Split(new[] { ',', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(NormalizeForMatch)
            .Where(s => s.Length > 0)
            .Distinct()
            .ToList();
    }

    private static bool MatchesBlacklist(string body, IReadOnlyList<string> blacklist, out string hit)
    {
        hit = "";
        if (blacklist.Count == 0) { return false; }
        var norm = NormalizeForMatch(body);
        foreach (var word in blacklist)
        {
            if (Regex.IsMatch(norm, $@"\b{Regex.Escape(word)}\b"))
            {
                hit = word;
                return true;
            }
        }
        return false;
    }

    private static string? MatchTemplate(string body, IReadOnlyList<TemplateRow> templates)
    {
        if (templates.Count == 0) { return null; }
        var norm = NormalizeForMatch(body);
        foreach (var t in templates)
        {
            var keywords = t.Keywords
                .Split(new[] { ',', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(NormalizeForMatch)
                .Where(s => s.Length > 0);
            foreach (var k in keywords)
            {
                if (Regex.IsMatch(norm, $@"\b{Regex.Escape(k)}\b"))
                {
                    return t.Body;
                }
            }
        }
        return null;
    }

    /// <summary>
    /// Llama al agente IA con el comentario del cliente y, opcionalmente, una sugerencia de
    /// respuesta (plantilla que matcheo). El system prompt del agente decide el tono y el
    /// formato; nosotros le pasamos la sugerencia como contexto para que la respuesta no sea
    /// copia literal y varie entre llamadas. Devuelve null si el agente no respondio util.
    /// </summary>
    private static async Task<string?> GenerateAiReplyAsync(
        IAiInferenceService inference, Guid agentId, string commentBody, string? suggestion,
        StringBuilder trace, CancellationToken ct)
    {
        try
        {
            var sb = new StringBuilder();
            sb.AppendLine("Un usuario comento en uno de nuestros videos. Genera UNA respuesta breve (max 150 caracteres) y natural, sin repetir literalmente el comentario, en el tono de tu prompt base.");
            sb.AppendLine();
            sb.AppendLine($"Comentario del usuario: \"{commentBody.Trim()}\"");
            if (!string.IsNullOrWhiteSpace(suggestion))
            {
                sb.AppendLine();
                sb.AppendLine("Respuesta SUGERIDA (usala como inspiracion, NO la copies literal, varia el texto manteniendo el mismo sentido):");
                sb.AppendLine($"\"{suggestion.Trim()}\"");
            }
            sb.AppendLine();
            sb.AppendLine("Responde directamente con el texto a publicar, sin comillas ni prefijos.");

            var ai = await inference.TestChatAsync(
                agentId,
                new[] { new AiChatTurn("user", sb.ToString()) },
                systemPromptOverride: null,
                cancellationToken: ct);
            if (!ai.Ok || string.IsNullOrWhiteSpace(ai.Text))
            {
                trace.AppendLine($"[WARN] IA respondio sin texto util: {ai.Error}");
                return null;
            }
            return ai.Text.Trim();
        }
        catch (Exception aiEx)
        {
            trace.AppendLine($"[ERROR] IA fallo: {aiEx.Message}");
            return null;
        }
    }

    /// <summary>Normaliza: minusculas + sin acentos. Para comparar keywords y blacklist.</summary>
    private static string NormalizeForMatch(string s)
    {
        if (string.IsNullOrEmpty(s)) { return ""; }
        var lower = s.Trim().ToLowerInvariant();
        var formD = lower.Normalize(System.Text.NormalizationForm.FormD);
        var sb = new StringBuilder(formD.Length);
        foreach (var ch in formD)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
            {
                sb.Append(ch);
            }
        }
        return sb.ToString();
    }

    /// <summary>TikTok limita la longitud de comment-reply; truncamos a 150 con elipsis.</summary>
    private static string TruncateForTikTok(string s)
    {
        const int max = 150;
        if (s.Length <= max) { return s; }
        return s[..(max - 3)] + "...";
    }

    private static string Truncate(string s, int n) => string.IsNullOrEmpty(s) ? "" : (s.Length <= n ? s : s[..n] + "...");

    /// <summary>
    /// Elige el agente IA para Mode=Ai/Mixed. Prioridad:
    ///  1) Agente del tenant activo cuyo nombre coincida (case-insensitive) con el Cliente de la cuenta.
    ///  2) Primer agente activo del tenant.
    ///  3) null si no hay ninguno.
    /// </summary>
    private async Task<Guid?> PickAgentIdAsync(CubotRedManagerDbContext db, Guid clientId, CancellationToken ct)
    {
        var clientName = await db.Clients.AsNoTracking()
            .Where(c => c.Id == clientId)
            .Select(c => c.Name)
            .FirstOrDefaultAsync(ct);

        var agents = await db.AiAgents.AsNoTracking()
            .Where(a => a.IsActive)
            .Select(a => new { a.Id, a.Name })
            .ToListAsync(ct);

        if (agents.Count == 0) { return null; }
        if (!string.IsNullOrWhiteSpace(clientName))
        {
            var match = agents.FirstOrDefault(a => string.Equals(a.Name, clientName, StringComparison.OrdinalIgnoreCase));
            if (match is not null) { return match.Id; }
        }
        return agents[0].Id;
    }

    private static async Task WriteLogAsync(
        CubotRedManagerDbContext db, AutoReplyTarget t, AutoReplyJobStatus status,
        DateTimeOffset startedAt, int processed, int replied, int errors, int omitted,
        StringBuilder trace, CancellationToken ct)
    {
        var ended = DateTimeOffset.UtcNow;
        db.AutoReplyJobLogs.Add(new AutoReplyJobLog
        {
            TenantId = t.TenantId,
            SocialAccountId = t.SocialAccountId,
            Status = status,
            StartedAt = startedAt,
            DurationMs = (int)Math.Clamp((ended - startedAt).TotalMilliseconds, 0, int.MaxValue),
            Processed = processed,
            Replied = replied,
            Errors = errors,
            Omitted = omitted,
            Trace = trace.ToString().TrimEnd()
        });
        await db.SaveChangesAsync(ct);
    }

    private static TimeZoneInfo ResolveTimeZone(string id)
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
        catch { return TimeZoneInfo.Local; }
    }

    // ----------------- helper records -----------------

    private sealed record AutoReplyTarget(
        Guid ConfigId, Guid TenantId, Guid AccountId, Guid SocialAccountId, string NetworkCode,
        SocialAccountStatus AccountStatus, AutoReplyFrequency Frequency, string? CronCustom,
        int ActiveHoursMask, byte ActiveDaysOfWeekMask, AutoReplyMode Mode, int MaxRepliesPerRun,
        int DelayMinSeconds, int DelayMaxSeconds, string? BlacklistKeywords, string? DefaultTemplate,
        string? AccountExternalId, string? AccountHandle, Guid? AiAgentId,
        bool SummaryEnabled, Guid? SummaryLineId, AutoReplySummaryTargetType SummaryTargetType,
        string? SummaryTarget, string? SummaryTemplate);

    private sealed record TemplateRow(string Keywords, string Body);
}
