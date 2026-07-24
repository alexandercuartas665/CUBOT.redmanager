using System.Globalization;
using System.Text.RegularExpressions;
using CubotRedManager.Application.Abstractions;
using CubotRedManager.Application.Common;
using CubotRedManager.Domain.Entities;
using CubotRedManager.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CubotRedManager.Application.Tenancy;

/// <summary>
/// Implementacion del despachador. Portado desde CUBOT.travels con adaptaciones para redmanager:
/// - Lead+Pipeline NO existen aun: se salta el "candado del asesor" y el LeadMarker es stub.
/// - El auto-pedido al cerrar (que dependia de LeadsCreated > 0) queda inactivo hasta portar Lead.
/// - El resto del flujo (inferencia, prompt, markers, envio, bitacora, reacciones) va 1:1.
/// </summary>
public sealed class AgentDispatcher : IAgentDispatcher
{
    private const int MaxTurns = 12;
    private const int InterAttachmentDelayMs = 3000;

    private readonly IApplicationDbContext _db;
    private readonly IAiInferenceService _inference;
    private readonly IWhatsAppConnectorService _connector;
    private readonly IChatBroadcaster _broadcaster;
    private readonly ILeadMarkerProcessor _leadMarker;
    private readonly IPedidoMarkerProcessor _pedidoMarker;
    private readonly IPaymentLinkProcessor _paymentLinker;
    private readonly IAgentMediaReader _mediaReader;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<AgentDispatcher> _logger;

    public AgentDispatcher(
        IApplicationDbContext db,
        IAiInferenceService inference,
        IWhatsAppConnectorService connector,
        IChatBroadcaster broadcaster,
        ILeadMarkerProcessor leadMarker,
        IPedidoMarkerProcessor pedidoMarker,
        IPaymentLinkProcessor paymentLinker,
        IAgentMediaReader mediaReader,
        TimeProvider timeProvider,
        ILogger<AgentDispatcher> logger)
    {
        _db = db;
        _inference = inference;
        _connector = connector;
        _broadcaster = broadcaster;
        _leadMarker = leadMarker;
        _pedidoMarker = pedidoMarker;
        _paymentLinker = paymentLinker;
        _mediaReader = mediaReader;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task DispatchAsync(Guid tenantId, Guid conversationId, Guid? whatsAppLineId, string inboundBody, CancellationToken cancellationToken = default)
    {
        if (whatsAppLineId is null)
        {
            _logger.LogInformation("Dispatcher: skip conv {ConvId} (sin whatsAppLineId)", conversationId);
            return;
        }

        // 1) Binding activo.
        var binding = await _db.AiAgentLineBindings
            .IgnoreQueryFilters()
            .Where(b => b.TenantId == tenantId && b.WhatsAppLineId == whatsAppLineId && b.IsConnected)
            .Select(b => new { b.AgentId, b.AutoConfirm })
            .FirstOrDefaultAsync(cancellationToken);
        if (binding is null)
        {
            _logger.LogInformation("Dispatcher: skip conv {ConvId} linea {LineId} (sin binding activo)", conversationId, whatsAppLineId);
            return;
        }

        // Agente activo + config de reacciones. Reacciones automaticas por emoji sin pasar por
        // el LLM (0 tokens). El path se ejecuta antes de la inferencia para acknowledge rapido.
        var agentCfg = await _db.AiAgents
            .IgnoreQueryFilters()
            .Where(a => a.Id == binding.AgentId)
            .Select(a => new { a.IsActive, a.ReactionsEnabled, a.ReactionRatioN, a.ReactionRatioM, a.ReactionEmojis })
            .FirstOrDefaultAsync(cancellationToken);
        if (agentCfg is null || !agentCfg.IsActive)
        {
            _logger.LogInformation("Dispatcher: skip conv {ConvId} (agente {AgentId} apagado)", conversationId, binding.AgentId);
            return;
        }

        // 2) Conversacion.
        var conv = await _db.Conversations
            .IgnoreQueryFilters()
            .Where(c => c.Id == conversationId)
            .Select(c => new { c.ContactPhone, c.LeadId, c.AgentContextResetAt })
            .FirstOrDefaultAsync(cancellationToken);
        if (conv is null || string.IsNullOrWhiteSpace(conv.ContactPhone))
        {
            _logger.LogInformation("Dispatcher: skip conv {ConvId} (sin telefono)", conversationId);
            return;
        }

        // Lista negra global del tenant.
        var blockedPhones = await _db.TenantBlockedNumbers.IgnoreQueryFilters()
            .Where(b => b.TenantId == tenantId).Select(b => b.Phone).ToListAsync(cancellationToken);
        if (IsBlockedNumber(conv.ContactPhone, blockedPhones))
        {
            _logger.LogInformation("Dispatcher: skip conv {ConvId} (numero {Phone} en lista negra)", conversationId, conv.ContactPhone);
            await LogRunAsync(tenantId, conversationId, binding.AgentId, AiAgentRunLogKind.Info,
                "Skip: numero en lista negra",
                $"El numero {conv.ContactPhone} esta en la lista negra de la agencia; no se responde.",
                null, cancellationToken);
            return;
        }

        // NOTA(redmanager): en travels aqui hay un "candado del asesor" que consulta Lead. Como
        // Lead+Pipeline aun no se portaron, se omite. Sin este candado, el bot NO se calla
        // aunque haya un asesor asignado -- comportamiento aceptable hasta que se porte el pipeline.

        // BITACORA: mensaje recibido.
        await LogRunAsync(tenantId, conversationId, binding.AgentId, AiAgentRunLogKind.Inbound,
            "Mensaje recibido", inboundBody, null, cancellationToken);

        // 2b) Reaccion emoji (sin LLM, 0 tokens). Se hace incluso si el binding no autoConfirma
        // porque es un acknowledge visual que ayuda al cliente a saber que su mensaje llego.
        // Log de diagnostico: sin esto no habia como saber en bitacora POR QUE no se llama a
        // MaybeReactAsync (ej. ReactionsEnabled=false en runtime, aunque en la BD/UI se vea true).
        await LogRunAsync(tenantId, conversationId, binding.AgentId, AiAgentRunLogKind.Info,
            $"Reactions check: enabled={agentCfg.ReactionsEnabled} lineId={(whatsAppLineId is null ? "null" : "ok")} ratio={agentCfg.ReactionRatioN}/{agentCfg.ReactionRatioM} emojis={(string.IsNullOrEmpty(agentCfg.ReactionEmojis) ? "empty" : "ok")}",
            null, null, cancellationToken);
        if (agentCfg.ReactionsEnabled && whatsAppLineId is not null)
        {
            await MaybeReactAsync(tenantId, conversationId, whatsAppLineId.Value, conv.ContactPhone,
                agentCfg.ReactionRatioN, agentCfg.ReactionRatioM, agentCfg.ReactionEmojis,
                binding.AgentId, cancellationToken);
        }

        // 3) Historial (con filtro de reset de contexto).
        var resetAt = conv.AgentContextResetAt;
        var recentMessages = await _db.Messages
            .IgnoreQueryFilters()
            .Where(m => m.TenantId == tenantId && m.ConversationId == conversationId
                     && !(m.SentByName != null && m.SentByName.StartsWith("IA (ai_failed"))
                     && m.Body != "(solo adjuntos)"
                     && (resetAt == null || m.SentAt > resetAt))
            .OrderByDescending(m => m.SentAt)
            .Take(MaxTurns)
            .OrderBy(m => m.SentAt)
            .Select(m => new { m.Direction, m.Body })
            .ToListAsync(cancellationToken);
        var rawTurns = recentMessages
            .Select(m => new AiChatTurn(m.Direction == MessageDirection.Inbound ? "user" : "model", m.Body))
            .ToList();

        // Colapsa turnos consecutivos del mismo rol (Gemini exige alternancia).
        var turns = new List<AiChatTurn>();
        foreach (var t in rawTurns)
        {
            if (turns.Count > 0 && turns[^1].Role == t.Role)
            {
                turns[^1] = turns[^1] with { Text = $"{turns[^1].Text}\n{t.Text}" };
            }
            else
            {
                turns.Add(t);
            }
        }
        if (turns.Count == 0) { turns.Add(new AiChatTurn("user", inboundBody)); }

        // 4) Inferencia.
        var totalChars = turns.Sum(t => t.Text?.Length ?? 0);
        var turnsSummary = string.Join('\n', turns.Select((t, i) =>
            $"[{i + 1}] role={t.Role} chars={t.Text?.Length ?? 0} body=\"{Preview(t.Text, 120)}\""));
        await LogRunAsync(tenantId, conversationId, binding.AgentId, AiAgentRunLogKind.Info,
            $"Contexto enviado al LLM ({turns.Count} turnos, {totalChars} chars)",
            turnsSummary, null, cancellationToken);

        _logger.LogInformation("Dispatcher: ejecutando inferencia agente {AgentId} conv {ConvId} ({TurnCount} turnos, {TotalChars} chars)",
            binding.AgentId, conversationId, turns.Count, totalChars);
        var result = await _inference.TestChatAsync(binding.AgentId, turns, systemPromptOverride: null, cancellationToken: cancellationToken);

        // Prompts / sub-llamadas en bitacora.
        if (result.DebugPrompts is not null)
        {
            foreach (var d in result.DebugPrompts)
            {
                await LogRunAsync(tenantId, conversationId, binding.AgentId,
                    AiAgentRunLogKind.Prompt, d.Title, d.Content, d.Response, cancellationToken);
            }
        }

        if (!result.Ok)
        {
            _logger.LogWarning("Dispatcher: inferencia FALLO agente {AgentId} conv {ConvId}: {Error}",
                binding.AgentId, conversationId, result.Error ?? "(sin detalle)");
            await LogRunAsync(tenantId, conversationId, binding.AgentId, AiAgentRunLogKind.Error,
                "Inferencia fallo", result.Error, null, cancellationToken);
            return;
        }
        var hasAttachments = result.Attachments is { Count: > 0 };
        if (string.IsNullOrWhiteSpace(result.Text) && !hasAttachments)
        {
            _logger.LogWarning("Dispatcher: LLM devolvio texto vacio Y sin attachments agente {AgentId} conv {ConvId}: {Detail}",
                binding.AgentId, conversationId, result.Error ?? "(sin detalle)");
            await LogRunAsync(tenantId, conversationId, binding.AgentId, AiAgentRunLogKind.Error,
                "Texto vacio del LLM",
                "El modelo devolvio una respuesta vacia. Probables causas: contexto demasiado largo, safety filter, prompt mal armado.",
                result.Error ?? "(sin detalle adicional)",
                cancellationToken);
            return;
        }
        _logger.LogInformation("Dispatcher: inferencia OK agente {AgentId} conv {ConvId} ({InTok}+{OutTok} tokens, {AttachCount} adjuntos)",
            binding.AgentId, conversationId, result.InputTokens, result.OutputTokens, result.Attachments?.Count ?? 0);

        // 4b) Postproceso markers en cadena.
        var pedidoCacheRows = await FetchPedidoCacheRowsAsync(tenantId, binding.AgentId, conversationId, cancellationToken);
        var leadResult = await _leadMarker.ProcessAsync(tenantId, binding.AgentId, conversationId, result.Text!, cancellationToken);

        PedidoMarkerResult pedidoResult;
        if (leadResult.LeadsCreated.Count > 0)
        {
            pedidoResult = await _pedidoMarker.ProcessAsync(tenantId, binding.AgentId, leadResult.CleanText, PedidoFooter(leadResult), cancellationToken);
        }
        else
        {
            pedidoResult = new PedidoMarkerResult(_pedidoMarker.StripMarkers(leadResult.CleanText), 0, 0);
        }
        var textToSend = pedidoResult.CleanText ?? string.Empty;

        // 4c) Markers de pago FUXION [[link_pago: NOMBRE:qty, ...]] -> URL de sales-link.
        // Se ejecuta despues de lead/pedido porque puede consumir contenido cache/lead. Si algo
        // falla (token expirado, contrato roto, red), el processor sustituye por un mensaje amable
        // y devuelve errores en el result para loguear en bitacora sin romper la respuesta.
        if (textToSend.Contains("[[link_pago", StringComparison.OrdinalIgnoreCase))
        {
            var payResult = await _paymentLinker.ProcessAsync(tenantId, binding.AgentId, textToSend, cancellationToken);
            textToSend = payResult.ProcessedText;
            if (payResult.MarkersFound > 0)
            {
                var summary = $"markers={payResult.MarkersFound} ok={payResult.LinksGenerated} fallidos={payResult.LinksFailed}";
                var kind = payResult.LinksFailed > 0 ? AiAgentRunLogKind.Error : AiAgentRunLogKind.Tool;
                var body = payResult.Errors.Count > 0
                    ? string.Join(" | ", payResult.Errors)
                    : $"Sales-link(s) generado(s) correctamente para {payResult.LinksGenerated} marker(s).";
                await LogRunAsync(tenantId, conversationId, binding.AgentId, kind,
                    "Payment link processor: " + summary, body, null, cancellationToken);
            }
        }

        // Red de seguridad para markers residuales / mal formados.
        if (textToSend.Contains("[[", StringComparison.Ordinal))
        {
            var scrubbed = AgentMarkerCleaner.ScrubResidualMarkers(textToSend);
            await LogRunAsync(tenantId, conversationId, binding.AgentId, AiAgentRunLogKind.Error,
                "Marcador mal formado suprimido (no enviado al cliente)",
                "El LLM dejo un marcador residual o sin cerrar. Se quito del texto para que el cliente NO lo reciba.",
                textToSend, cancellationToken);
            textToSend = scrubbed;
        }

        // Anti-duplicado con attachments.
        if (AgentMarkerCleaner.TextDuplicatesAttachment(textToSend, result.Attachments))
        {
            await LogRunAsync(tenantId, conversationId, binding.AgentId, AiAgentRunLogKind.Info,
                "Texto suprimido: duplicaba el caption del adjunto",
                textToSend, null, cancellationToken);
            textToSend = string.Empty;
        }

        // Failsafe de cierre tacito.
        var closeScanText = textToSend;
        if (result.Attachments is { Count: > 0 })
        {
            closeScanText += "\n" + string.Join("\n", result.Attachments.Select(a => a.EffectiveCaption ?? a.Name ?? string.Empty));
        }
        var tacitClose = await DetectTacitCloseAsync(tenantId, binding.AgentId, conversationId, closeScanText, leadResult, cancellationToken);
        if (tacitClose.shouldCreateLead)
        {
            leadResult = await _leadMarker.ProcessAsync(tenantId, binding.AgentId, conversationId, "[[crear_lead_pipeline]]", cancellationToken);
        }

        // Pedido automatico al cerrar. En redmanager LeadsCreated siempre es 0 (stub), asi que este
        // bloque no se ejecuta hasta portar Lead+Pipeline.
        int autoPedidoSent = 0, autoPedidoFailed = 0;
        string autoPedidoSummary = string.Empty;
        if (leadResult.LeadsCreated.Count > 0 && pedidoResult.NotificationsSent == 0 && pedidoResult.NotificationsFailed == 0)
        {
            autoPedidoSummary = await BuildPedidoSummaryAsync(tenantId, binding.AgentId, conversationId,
                leadResult.LeadsCreated, pedidoCacheRows, cancellationToken);
            if (!string.IsNullOrWhiteSpace(autoPedidoSummary))
            {
                var notifyResult = await _pedidoMarker.NotifyTargetsAsync(tenantId, binding.AgentId, autoPedidoSummary, PedidoFooter(leadResult), cancellationToken);
                autoPedidoSent = notifyResult.Sent; autoPedidoFailed = notifyResult.Failed;
            }
        }

        // Bitacora efectos.
        if (tacitClose.shouldCreateLead)
        {
            await LogRunAsync(tenantId, conversationId, binding.AgentId, AiAgentRunLogKind.Info,
                "Cierre TACITO detectado",
                "El LLM cerro sin emitir [[crear_lead_pipeline]] pero el cache tiene datos minimos.",
                tacitClose.reason, cancellationToken);
        }
        if (leadResult.LeadsCreated.Count > 0)
        {
            await LogRunAsync(tenantId, conversationId, binding.AgentId, AiAgentRunLogKind.Tool,
                "Lead creado en pipeline",
                $"El agente cerro atencion y se crearon {leadResult.LeadsCreated.Count} lead(s).",
                string.Join(", ", leadResult.LeadsCreated), cancellationToken);
        }
        if (pedidoResult.NotificationsSent > 0 || pedidoResult.NotificationsFailed > 0)
        {
            await LogRunAsync(tenantId, conversationId, binding.AgentId, AiAgentRunLogKind.Tool,
                "Pedido notificado",
                $"Enviado a {pedidoResult.NotificationsSent} destino(s), fallaron {pedidoResult.NotificationsFailed}.",
                null, cancellationToken);
        }
        if (autoPedidoSent > 0 || autoPedidoFailed > 0)
        {
            await LogRunAsync(tenantId, conversationId, binding.AgentId, AiAgentRunLogKind.Tool,
                "Pedido automatico al cerrar lead",
                $"Enviado a {autoPedidoSent} destino(s), fallaron {autoPedidoFailed}.",
                autoPedidoSummary, cancellationToken);
        }

        // 5) autoConfirm=false -> borrador y sale.
        if (!binding.AutoConfirm)
        {
            await PersistOutboundAsync(tenantId, conversationId, textToSend, "ai_draft", null, cancellationToken);
            return;
        }

        // Guardrail anti-fuga de razonamiento.
        if (!string.IsNullOrWhiteSpace(textToSend) && ReasoningLeakHeuristics.LooksLikeLeak(textToSend))
        {
            await LogRunAsync(tenantId, conversationId, binding.AgentId, AiAgentRunLogKind.Error,
                "Texto suprimido: parece razonamiento del modelo (no enviado al cliente)",
                textToSend, ReasoningLeakHeuristics.Explain(textToSend), cancellationToken);
            textToSend = string.Empty;
        }

        // 6) Envio texto principal.
        var textWasSent = false;
        var sourceBase = "ai_sent";
        if (!string.IsNullOrWhiteSpace(textToSend))
        {
            var sendResult = await _connector.SendTestAsync(whatsAppLineId.Value, conv.ContactPhone, textToSend, Guid.Empty, cancellationToken);
            textWasSent = sendResult.Ok;
            sourceBase = sendResult.Ok ? "ai_sent" : "ai_failed";
            await PersistOutboundAsync(tenantId, conversationId, textToSend, sourceBase, sendResult.MessageId, cancellationToken);
        }

        // 6b) Attachments.
        var attachmentsSent = 0;
        var attachmentsFailed = 0;
        var atts = result.Attachments ?? (IReadOnlyList<AiChatAttachment>)Array.Empty<AiChatAttachment>();
        for (var ai = 0; ai < atts.Count; ai++)
        {
            if (ai > 0)
            {
                try { await Task.Delay(InterAttachmentDelayMs, cancellationToken); }
                catch (OperationCanceledException) { break; }
            }
            var a = atts[ai];
            try
            {
                var ok = await SendAttachmentAsync(whatsAppLineId.Value, conv.ContactPhone, tenantId, conversationId, binding.AgentId, a, cancellationToken);
                if (ok) { attachmentsSent++; } else { attachmentsFailed++; }
            }
            catch { attachmentsFailed++; }
        }

        // 7) Etiqueta consolidada.
        var tags = new List<string> { sourceBase };
        if (attachmentsSent > 0) { tags.Add($"adj({attachmentsSent})"); }
        if (attachmentsFailed > 0) { tags.Add($"adj_err({attachmentsFailed})"); }
        if (leadResult.LeadsCreated.Count > 0) { tags.Add("lead"); }
        if (pedidoResult.NotificationsSent > 0) { tags.Add($"pedido({pedidoResult.NotificationsSent})"); }
        if (autoPedidoSent > 0) { tags.Add($"pedido_auto({autoPedidoSent})"); }
        var source = string.Join("+", tags);
        if (!textWasSent && (attachmentsSent > 0 || attachmentsFailed > 0))
        {
            await PersistOutboundAsync(tenantId, conversationId, "(solo adjuntos)", source, null, cancellationToken);
        }

        var replyKind = (textWasSent || attachmentsSent > 0) ? AiAgentRunLogKind.Reply : AiAgentRunLogKind.Error;
        await LogRunAsync(tenantId, conversationId, binding.AgentId, replyKind,
            (textWasSent || attachmentsSent > 0) ? "Respuesta enviada" : "Envio fallo",
            string.IsNullOrWhiteSpace(textToSend) ? "(solo adjuntos)" : textToSend,
            $"texto={(textWasSent ? "OK" : "FAIL")} adj_sent={attachmentsSent} adj_fail={attachmentsFailed} tags={source}",
            cancellationToken);
    }

    public async Task<AgentDispatchResult> DispatchForTestAsync(Guid tenantId, Guid conversationId, Guid? whatsAppLineId, string inboundBody, CancellationToken cancellationToken = default)
    {
        AgentDispatchResult Skip(string reason) => new(false, reason, null, null, false,
            Array.Empty<Guid>(), Array.Empty<string>(), 0, 0, 0, 0, true);

        if (whatsAppLineId is null) { return Skip("Sin whatsAppLineId."); }

        var binding = await _db.AiAgentLineBindings.IgnoreQueryFilters()
            .Where(b => b.TenantId == tenantId && b.WhatsAppLineId == whatsAppLineId && b.IsConnected)
            .Select(b => new { b.AgentId })
            .FirstOrDefaultAsync(cancellationToken);
        if (binding is null) { return Skip("La linea no tiene un agente vinculado y conectado."); }

        var agentActive = await _db.AiAgents.IgnoreQueryFilters()
            .Where(a => a.Id == binding.AgentId).Select(a => a.IsActive)
            .FirstOrDefaultAsync(cancellationToken);
        if (!agentActive) { return Skip("El agente esta apagado."); }

        var conv = await _db.Conversations.IgnoreQueryFilters()
            .Where(c => c.Id == conversationId)
            .Select(c => new { c.ContactPhone, c.LeadId, c.AgentContextResetAt })
            .FirstOrDefaultAsync(cancellationToken);
        if (conv is null || string.IsNullOrWhiteSpace(conv.ContactPhone)) { return Skip("Conversacion sin telefono."); }

        var blockedPhones = await _db.TenantBlockedNumbers.IgnoreQueryFilters()
            .Where(b => b.TenantId == tenantId).Select(b => b.Phone).ToListAsync(cancellationToken);
        if (IsBlockedNumber(conv.ContactPhone, blockedPhones))
        {
            return Skip($"El numero {conv.ContactPhone} esta en la lista negra de la agencia; el bot no responde.");
        }

        // NOTA(redmanager): candado del asesor omitido (sin Lead+Pipeline).

        var resetAt = conv.AgentContextResetAt;
        var recentMessages = await _db.Messages.IgnoreQueryFilters()
            .Where(m => m.TenantId == tenantId && m.ConversationId == conversationId
                     && !(m.SentByName != null && m.SentByName.StartsWith("IA (ai_failed"))
                     && m.Body != "(solo adjuntos)"
                     && (resetAt == null || m.SentAt > resetAt))
            .OrderByDescending(m => m.SentAt).Take(MaxTurns).OrderBy(m => m.SentAt)
            .Select(m => new { m.Direction, m.Body }).ToListAsync(cancellationToken);
        var turns = new List<AiChatTurn>();
        foreach (var m in recentMessages)
        {
            var role = m.Direction == MessageDirection.Inbound ? "user" : "model";
            if (turns.Count > 0 && turns[^1].Role == role) { turns[^1] = turns[^1] with { Text = $"{turns[^1].Text}\n{m.Body}" }; }
            else { turns.Add(new AiChatTurn(role, m.Body)); }
        }
        if (turns.Count == 0) { turns.Add(new AiChatTurn("user", inboundBody)); }

        var result = await _inference.TestChatAsync(binding.AgentId, turns, systemPromptOverride: null, cancellationToken: cancellationToken);
        if (!result.Ok) { return Skip($"La inferencia fallo: {result.Error ?? "(sin detalle)"}"); }

        var leadResult = await _leadMarker.ProcessAsync(tenantId, binding.AgentId, conversationId, result.Text ?? string.Empty, cancellationToken);
        var pedidoClean = _pedidoMarker.StripMarkers(leadResult.CleanText);
        var atts = result.Attachments ?? (IReadOnlyList<AiChatAttachment>)Array.Empty<AiChatAttachment>();
        var closeScan = pedidoClean + (atts.Count > 0 ? "\n" + string.Join("\n", atts.Select(a => a.EffectiveCaption ?? a.Name ?? string.Empty)) : string.Empty);
        var tacit = await DetectTacitCloseAsync(tenantId, binding.AgentId, conversationId, closeScan, leadResult, cancellationToken);
        if (tacit.shouldCreateLead)
        {
            leadResult = await _leadMarker.ProcessAsync(tenantId, binding.AgentId, conversationId, "[[crear_lead_pipeline]]", cancellationToken);
            pedidoClean = _pedidoMarker.StripMarkers(leadResult.CleanText);
        }

        // NOTA(redmanager): sin Lead+Pipeline no hay etapas para reportar.
        var stages = new List<string>();

        var markersLeaked = pedidoClean.Contains("[[", StringComparison.Ordinal);

        var simBody = !string.IsNullOrWhiteSpace(pedidoClean)
            ? pedidoClean
            : (atts.Count > 0 ? "[Recursos enviados: " + string.Join(", ", atts.Select(a => a.Name ?? "recurso")) + "]" : string.Empty);
        if (!string.IsNullOrWhiteSpace(simBody))
        {
            await PersistOutboundAsync(tenantId, conversationId, simBody, "ai_sim", null, cancellationToken);
        }

        return new AgentDispatchResult(
            Ok: true, SkipReason: null, ReplyText: pedidoClean, RawLlmResponse: result.Text,
            MarkersLeaked: markersLeaked, LeadsCreated: leadResult.LeadsCreated, LeadStages: stages,
            AttachmentCount: atts.Count, ContextTurns: turns.Count,
            InputTokens: result.InputTokens, OutputTokens: result.OutputTokens, Simulated: true);
    }

    private async Task MaybeReactAsync(Guid tenantId, Guid conversationId, Guid lineId, string phone,
        int ratioN, int ratioM, string? emojiCsv, Guid agentId, CancellationToken ct)
    {
        try
        {
            // Log de entrada para diagnostico: sin esto no sabiamos si el metodo se llamaba
            // realmente cuando la config estaba activa pero la reaccion nunca llegaba al cliente.
            await LogRunAsync(tenantId, conversationId, agentId, AiAgentRunLogKind.Info,
                "MaybeReactAsync: iniciando", $"phone={phone} lineId={lineId} ratio={ratioN}/{ratioM}", null, ct);

            var emojis = (emojiCsv ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (emojis.Length == 0)
            {
                // Loggeamos a bitacora para que el operador vea POR QUE no reaccionamos.
                // Sin esto, el toggle "activo" + stack vacio era silencioso y no habia forma
                // de saber que faltaba llenar el stack de emojis en /agentes.
                await LogRunAsync(tenantId, conversationId, agentId, AiAgentRunLogKind.Info,
                    "Reacciones: stack de emojis vacio",
                    "Reacciones estan activadas pero no hay emojis en el stack. Agrega al menos uno en /agentes -> Reacciones -> Stack de emojis.",
                    null, ct);
                return;
            }
            if (ratioM <= 0 || ratioN <= 0)
            {
                await LogRunAsync(tenantId, conversationId, agentId, AiAgentRunLogKind.Info,
                    "Reacciones: ratio invalido",
                    $"Ratio N/M actual = {ratioN}/{ratioM}. Debe ser N>=1 y M>=1. Ajusta la frecuencia en /agentes -> Reacciones.",
                    null, ct);
                return;
            }
            if (ratioN < ratioM)
            {
                var roll = Random.Shared.Next(ratioM);
                if (roll >= ratioN)
                {
                    await LogRunAsync(tenantId, conversationId, agentId, AiAgentRunLogKind.Info,
                        "Reacciones: skip por ratio", $"roll={roll} de {ratioM}, umbral<{ratioN}. Este mensaje no reacciona por diseno de la frecuencia.",
                        null, ct);
                    return;
                }
            }

            var lastInbound = await _db.Messages
                .IgnoreQueryFilters()
                .Where(m => m.TenantId == tenantId && m.ConversationId == conversationId
                         && m.Direction == MessageDirection.Inbound
                         && m.ExternalId != null && m.Reaction == null)
                .OrderByDescending(m => m.SentAt)
                .FirstOrDefaultAsync(ct);
            if (lastInbound is null || string.IsNullOrWhiteSpace(lastInbound.ExternalId))
            {
                // Este caso pasa cuando el mensaje entrante aun no se persistio en Messages
                // (rare, race con ChatIngestService) o cuando el ExternalId de Evolution vino
                // vacio. Loggeamos solo si Reactions esta activo pero no llegamos ni al POST.
                await LogRunAsync(tenantId, conversationId, agentId, AiAgentRunLogKind.Info,
                    "Reacciones: sin mensaje inbound al que reaccionar",
                    "No se encontro un mensaje entrante con ExternalId sin reaccion. Puede ser normal si el mensaje del cliente vino por otra via.",
                    null, ct);
                return;
            }

            var emoji = emojis[Random.Shared.Next(emojis.Length)];
            await LogRunAsync(tenantId, conversationId, agentId, AiAgentRunLogKind.Info,
                "Reacciones: llamando al connector",
                $"emoji={emoji} externalId={lastInbound.ExternalId} phone={phone}",
                null, ct);
            var res = await _connector.SendReactionAsync(lineId, phone, lastInbound.ExternalId!, emoji, ct);
            if (res.Ok)
            {
                lastInbound.Reaction = emoji;
                await _db.SaveChangesAsync(ct);
                await LogRunAsync(tenantId, conversationId, agentId, AiAgentRunLogKind.Tool,
                    "Reaccion enviada", $"Emoji {emoji} al mensaje del cliente.", null, ct);
            }
            else
            {
                // Antes iba solo a ILogger, invisible en la bitacora del agente. Ahora queda
                // en run-logs para poder diagnosticar por que Evolution rechaza la reaccion
                // (formato del JID, permisos de la instancia, mensaje demasiado viejo, etc).
                await LogRunAsync(tenantId, conversationId, agentId, AiAgentRunLogKind.Error,
                    "Reaccion no enviada por el connector",
                    $"emoji={emoji} externalId={lastInbound.ExternalId} phone={phone} error={res.Error ?? "(sin detalle)"}",
                    null, ct);
            }
        }
        catch (Exception ex)
        {
            // Idem: la excepcion silenciosa era el otro punto ciego. Ahora si algo revienta
            // (deserializacion del ExternalId, HTTP timeout, etc), queda en la bitacora.
            await LogRunAsync(tenantId, conversationId, agentId, AiAgentRunLogKind.Error,
                "Reacciones: excepcion en MaybeReactAsync",
                $"{ex.GetType().Name}: {ex.Message}",
                null, ct);
            _logger.LogWarning(ex, "Dispatcher: fallo al reaccionar conv {ConvId}", conversationId);
        }
    }

    private async Task<(bool shouldCreateLead, string reason)> DetectTacitCloseAsync(
        Guid tenantId, Guid agentId, Guid conversationId, string textToSend,
        LeadMarkerResult markerResult, CancellationToken ct)
    {
        _ = tenantId;
        if (markerResult.LeadsCreated.Count > 0) { return (false, "ya hay lead via marker"); }
        if (!TacitCloseHeuristics.HasClosePhrase(textToSend)) { return (false, "sin frase de cierre"); }

        var present = await _db.AiAgentCacheValues.IgnoreQueryFilters()
            .Where(v => v.AgentId == agentId && v.SessionId == conversationId
                        && v.Value != null && v.Value != string.Empty)
            .Select(v => v.FieldKey)
            .Distinct()
            .ToListAsync(ct);

        var missing = TacitCloseHeuristics.MissingFields(present);
        if (missing.Count > 0)
        {
            return (false, $"faltan campos: {string.Join(", ", missing)}");
        }
        return (true, "cierre tacito detectado");
    }

    private async Task<string> BuildPedidoSummaryAsync(Guid tenantId, Guid agentId, Guid conversationId,
        IReadOnlyList<Guid> leadIds, IReadOnlyList<(string Label, string Value)> capturedRows, CancellationToken ct)
    {
        _ = tenantId; _ = agentId;
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("Nuevo pedido cerrado por el agente IA");
        sb.AppendLine();

        var contact = await _db.Conversations
            .IgnoreQueryFilters()
            .Where(c => c.Id == conversationId)
            .Select(c => new { c.ContactName, c.ContactPhone })
            .FirstOrDefaultAsync(ct);
        if (contact is not null)
        {
            if (!string.IsNullOrWhiteSpace(contact.ContactName)) { sb.AppendLine($"Cliente: {contact.ContactName}"); }
            if (!string.IsNullOrWhiteSpace(contact.ContactPhone)) { sb.AppendLine($"Telefono: {contact.ContactPhone}"); }
        }

        if (capturedRows.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Datos capturados:");
            foreach (var r in capturedRows) { sb.AppendLine($"- {r.Label}: {r.Value}"); }
        }

        if (leadIds.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"Lead(s) creado(s): {string.Join(", ", leadIds)}");
        }

        return sb.ToString().TrimEnd();
    }

    private async Task<IReadOnlyList<(string Label, string Value)>> FetchPedidoCacheRowsAsync(
        Guid tenantId, Guid agentId, Guid conversationId, CancellationToken ct)
    {
        var values = await _db.AiAgentCacheValues.IgnoreQueryFilters()
            .Where(v => v.TenantId == tenantId && v.AgentId == agentId && v.SessionId == conversationId
                        && v.Value != null && v.Value != string.Empty)
            .Select(v => new { v.FieldKey, v.Value })
            .ToListAsync(ct);
        if (values.Count == 0) { return Array.Empty<(string, string)>(); }

        var fields = await _db.AiAgentCacheFields.IgnoreQueryFilters()
            .Where(f => f.TenantId == tenantId && f.AgentId == agentId)
            .OrderBy(f => f.SortOrder).ThenBy(f => f.Label)
            .Select(f => new { f.FieldKey, f.Label })
            .ToListAsync(ct);
        var valuesByKey = values.ToDictionary(v => v.FieldKey, v => v.Value!);
        return fields
            .Where(f => valuesByKey.ContainsKey(f.FieldKey))
            .Select(f => (f.Label, valuesByKey[f.FieldKey]))
            .ToList();
    }

    private static bool IsBlockedNumber(string? contactPhone, IEnumerable<string> blockedPhones)
    {
        if (string.IsNullOrWhiteSpace(contactPhone)) { return false; }
        var contact = new string(contactPhone.Where(char.IsDigit).ToArray());
        if (contact.Length == 0) { return false; }
        foreach (var raw in blockedPhones)
        {
            var blocked = new string((raw ?? string.Empty).Where(char.IsDigit).ToArray());
            if (blocked.Length < 7) { continue; }
            if (contact == blocked
                || contact.EndsWith(blocked, StringComparison.Ordinal)
                || blocked.EndsWith(contact, StringComparison.Ordinal))
            {
                return true;
            }
        }
        return false;
    }

    private static string? PedidoFooter(LeadMarkerResult r) =>
        r.AttentionDurationSeconds is int s
            ? $"Tiempo de atencion: {FormatAttentionDuration(s)} (desde el primer mensaje del cliente)"
            : null;

    private static string FormatAttentionDuration(int totalSeconds)
    {
        if (totalSeconds < 60) { return $"{totalSeconds}s"; }
        var ts = TimeSpan.FromSeconds(totalSeconds);
        if (ts.TotalHours < 1) { return $"{(int)ts.TotalMinutes}m"; }
        if (ts.TotalDays < 1) { return ts.Minutes > 0 ? $"{(int)ts.TotalHours}h {ts.Minutes}m" : $"{(int)ts.TotalHours}h"; }
        return ts.Hours > 0 ? $"{(int)ts.TotalDays}d {ts.Hours}h" : $"{(int)ts.TotalDays}d";
    }

    private static string Preview(string? s, int max)
    {
        if (string.IsNullOrEmpty(s)) { return string.Empty; }
        var clean = s.Replace('\r', ' ').Replace('\n', ' ');
        return clean.Length <= max ? clean : clean[..max] + "...";
    }

    private async Task LogRunAsync(Guid tenantId, Guid conversationId, Guid agentId,
        AiAgentRunLogKind kind, string title, string? content, string? response, CancellationToken ct)
    {
        try
        {
            _db.AiAgentRunLogs.Add(new AiAgentRunLog
            {
                TenantId = tenantId,
                ConversationId = conversationId,
                AgentId = agentId,
                Kind = kind,
                OccurredAt = _timeProvider.GetUtcNow(),
                Title = title.Length > 200 ? title[..200] : title,
                Content = content,
                Response = response
            });
            await _db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "No se pudo escribir entrada en la bitacora del agente conv {ConvId}", conversationId);
        }
    }

    // Detecta placeholders del tipo {nombre_variable} que no fueron sustituidos por la IA en el caption.
    // Excluye {{...}} (marcadores MCP DataContainer) que ya fueron resueltos antes en el pipeline.
    private static readonly Regex UnresolvedPlaceholder = new(
        @"(?<!\{)\{[a-zA-Z_][a-zA-Z0-9_]*\}(?!\})",
        RegexOptions.Compiled);

    private async Task<bool> SendAttachmentAsync(
        Guid lineId, string phone, Guid tenantId, Guid conversationId, Guid agentId,
        AiChatAttachment a, CancellationToken ct)
    {
        var caption = a.EffectiveCaption;
        await WarnIfUnresolvedPlaceholdersAsync(caption, a.Name, tenantId, conversationId, agentId, ct);

        if (a.ResourceType == AgentResourceType.Text)
        {
            if (string.IsNullOrWhiteSpace(caption)) { return false; }
            var r = await _connector.SendTestAsync(lineId, phone, caption!, Guid.Empty, ct);
            if (r.Ok)
            {
                await PersistOutboundAsync(tenantId, conversationId, caption!, "ai_attach_text", r.MessageId, ct);
            }
            return r.Ok;
        }

        if (a.ResourceType == AgentResourceType.Location)
        {
            var parts = (a.Detail ?? "").Split(',');
            if (parts.Length < 2
                || !double.TryParse(parts[0].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var lat)
                || !double.TryParse(parts[1].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var lng))
            {
                return false;
            }
            var r = await _connector.SendLocationAsync(lineId, phone, lat, lng, a.Name, Guid.Empty, ct);
            if (r.Ok)
            {
                await PersistOutboundMediaAsync(tenantId, conversationId,
                    body: a.Name ?? "Ubicacion", mediaType: MessageMediaType.Location,
                    mediaUrl: a.FileUrl, mimeType: null, sourceTag: "ai_attach_location", externalId: r.MessageId, ct: ct);
            }
            return r.Ok;
        }

        var media = await _mediaReader.TryReadAsync(a.FileUrl, ct);
        if (media is null)
        {
            // Log a bitacora para que el operador sepa POR QUE no se envio el adjunto. Sin esto,
            // el user veia el texto llegar y el marker consumido pero la imagen nunca aparecia,
            // sin explicacion visible. Causa mas comun: recurso subido antes del fix de bytea
            // (FileUrl apunta a /uploads/agents/* del filesystem efimero de Railway, que ya no
            // existe). Solucion: re-subir el archivo en /agentes -> Recursos.
            await LogRunAsync(tenantId, conversationId, agentId, AiAgentRunLogKind.Error,
                "Adjunto no enviado: binario no accesible",
                $"Recurso '{a.Name}' (FileUrl='{a.FileUrl ?? "(null)"}'). El binario no se pudo leer. Si el URL empieza con /uploads/, re-sube el archivo en /agentes -> Recursos para que quede guardado en la BD.",
                null, ct);
            return false;
        }

        var mt = a.ResourceType switch
        {
            AgentResourceType.Image => MessageMediaType.Image,
            AgentResourceType.Video => MessageMediaType.Video,
            AgentResourceType.Audio => MessageMediaType.Audio,
            _ => MessageMediaType.Document
        };
        var res = await _connector.SendMediaAsync(lineId, phone, mt, media.Base64,
            media.MimeType, media.FileName ?? a.FileName, caption, Guid.Empty, ct);
        if (res.Ok)
        {
            await PersistOutboundMediaAsync(tenantId, conversationId,
                body: caption ?? a.Name ?? "(adjunto)", mediaType: mt,
                mediaUrl: a.FileUrl, mimeType: media.MimeType, sourceTag: "ai_attach_media", externalId: res.MessageId, ct: ct);
        }
        return res.Ok;
    }

    private async Task WarnIfUnresolvedPlaceholdersAsync(string? caption, string resourceName,
        Guid tenantId, Guid conversationId, Guid agentId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(caption)) { return; }
        var matches = UnresolvedPlaceholder.Matches(caption);
        if (matches.Count == 0) { return; }
        var vars = string.Join(", ", matches.Select(m => m.Value).Distinct());
        await LogRunAsync(tenantId, conversationId, agentId, AiAgentRunLogKind.Info,
            $"WARNING: caption con variables sin resolver ({vars})",
            $"Recurso '{resourceName}'. La IA envio el marker sin sustituir los placeholders. Corrige con la sintaxis [[enviar: {resourceName} | \"caption con los valores ya reemplazados\"]] y refuerza esa regla en el prompt base del agente.",
            null, ct);
    }

    private async Task PersistOutboundMediaAsync(Guid tenantId, Guid conversationId,
        string body, MessageMediaType mediaType, string? mediaUrl, string? mimeType,
        string sourceTag, string? externalId, CancellationToken ct)
    {
        var now = _timeProvider.GetUtcNow();
        var message = new Message
        {
            TenantId = tenantId,
            ConversationId = conversationId,
            Direction = MessageDirection.Outbound,
            ExternalId = string.IsNullOrWhiteSpace(externalId) ? $"agent-{Guid.NewGuid():N}" : externalId,
            Body = body,
            MessageType = mediaType == MessageMediaType.Location ? "location" : "media",
            MediaType = mediaType,
            MediaUrl = mediaUrl,
            MediaMimeType = mimeType,
            SentAt = now,
            SentByName = $"IA ({sourceTag})"
        };
        _db.Messages.Add(message);

        var conv = await _db.Conversations.IgnoreQueryFilters().FirstOrDefaultAsync(c => c.Id == conversationId, ct);
        if (conv is not null) { conv.LastMessageAt = now; }

        await _db.SaveChangesAsync(ct);

        var dto = new MessageDto(message.Id, message.ConversationId, message.Direction, message.Body,
            message.MessageType, message.SentAt, message.MediaType, message.MediaUrl, message.MediaMimeType, message.SentByName);
        await _broadcaster.MessageAddedAsync(tenantId, conversationId, dto, ct);
    }

    private async Task PersistOutboundAsync(Guid tenantId, Guid conversationId, string body, string sourceTag, string? externalId, CancellationToken ct)
    {
        var now = _timeProvider.GetUtcNow();
        var message = new Message
        {
            TenantId = tenantId,
            ConversationId = conversationId,
            Direction = MessageDirection.Outbound,
            ExternalId = string.IsNullOrWhiteSpace(externalId) ? $"agent-{Guid.NewGuid():N}" : externalId,
            Body = body,
            MessageType = "text",
            SentAt = now,
            SentByName = $"IA ({sourceTag})"
        };
        _db.Messages.Add(message);

        var conv = await _db.Conversations.IgnoreQueryFilters().FirstOrDefaultAsync(c => c.Id == conversationId, ct);
        if (conv is not null) { conv.LastMessageAt = now; }

        await _db.SaveChangesAsync(ct);

        var dto = new MessageDto(message.Id, message.ConversationId, message.Direction, message.Body,
            message.MessageType, message.SentAt, message.MediaType, message.MediaUrl, message.MediaMimeType, message.SentByName);
        await _broadcaster.MessageAddedAsync(tenantId, conversationId, dto, ct);
    }
}
