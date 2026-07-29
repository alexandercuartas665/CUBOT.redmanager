using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CubotRedManager.Application.Abstractions;
using CubotRedManager.Application.Admin;
using CubotRedManager.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace CubotRedManager.Application.Tenancy;

public sealed class AiInferenceService : IAiInferenceService
{
    private readonly IApplicationDbContext _db;
    private readonly ISecretProtector _secretProtector;
    private readonly IAiProviderClient _client;
    private readonly IAiUsageService _usage;
    private readonly IAiAgentCacheService _cache;
    private readonly IDataContainerMcpService _mcp;

    public AiInferenceService(
        IApplicationDbContext db,
        ISecretProtector secretProtector,
        IAiProviderClient client,
        IAiUsageService usage,
        IAiAgentCacheService cache,
        IDataContainerMcpService mcp)
    {
        _db = db;
        _secretProtector = secretProtector;
        _client = client;
        _usage = usage;
        _cache = cache;
        _mcp = mcp;
    }

    public async Task<AiChatResult> TestChatAsync(Guid agentId, IReadOnlyList<AiChatTurn> turns, string? systemPromptOverride = null, CancellationToken cancellationToken = default)
    {
        var agent = await _db.AiAgents.AsNoTracking().FirstOrDefaultAsync(a => a.Id == agentId, cancellationToken);
        if (agent is null) { return new AiChatResult(false, null, "El agente no existe."); }

        var providerCfg = await _db.AiProviderConfigs.AsNoTracking().FirstOrDefaultAsync(c => c.Provider == agent.Provider, cancellationToken);
        if (providerCfg is null || !providerCfg.IsEnabled || string.IsNullOrWhiteSpace(providerCfg.ApiKeyEncrypted))
        {
            return new AiChatResult(false, null, $"El proveedor {agent.Provider} no esta habilitado en la plataforma.");
        }

        string apiKey;
        try { apiKey = _secretProtector.Unprotect(providerCfg.ApiKeyEncrypted); }
        catch { return new AiChatResult(false, null, "La API key del proveedor esta cifrada con una version anterior. Vuelve a guardarla en Servidores de IA."); }

        var meta = AiProviderCatalog.For(agent.Provider);
        var model = !string.IsNullOrWhiteSpace(agent.Model) ? agent.Model!
            : !string.IsNullOrWhiteSpace(providerCfg.Model) ? providerCfg.Model!
            : meta.DefaultModel;

        if (turns.Count == 0) { return new AiChatResult(false, null, "Escribe un mensaje para probar el agente."); }

        var quota = await _usage.GetQuotaAsync(cancellationToken);
        if (quota.Exceeded && quota.Hard)
        {
            return new AiChatResult(false, null, $"Alcanzaste el limite de tokens de IA de tu plan este mes ({quota.MonthlyLimitTokens:N0}). Actualiza tu plan para seguir usando los agentes.");
        }

        var resources = await _db.AiAgentResources.AsNoTracking()
            .Where(r => r.AgentId == agentId)
            .OrderBy(r => r.SortOrder)
            .Select(r => new AiChatAttachment(r.Name, r.ResourceType, r.FileUrl, r.FileName, r.Detail))
            .ToListAsync(cancellationToken);

        var sessionId = agentId;

        var cacheFields = await _db.AiAgentCacheFields.AsNoTracking()
            .Where(f => f.AgentId == agentId)
            .OrderBy(f => f.SortOrder).ThenBy(f => f.Label)
            .Select(f => new CacheFieldInfo(f.FieldKey, f.Label, f.Description, f.IsUpdatable))
            .ToListAsync(cancellationToken);

        var cacheValues = await _db.AiAgentCacheValues.AsNoTracking()
            .Where(v => v.AgentId == agentId && v.SessionId == sessionId)
            .ToDictionaryAsync(v => v.FieldKey, v => v.Value, cancellationToken);

        var systemPrompt = await BuildSystemPrompt(agentId, systemPromptOverride ?? agent.SystemPrompt, resources, cacheFields, cacheValues, turns, agent.EnableDataContainerMcp, cancellationToken);

        var debugPrompts = new List<AiDebugPrompt>
        {
            new("Prompt principal del agente (enrutador + recursos + estado de cache)", DateTimeOffset.UtcNow, systemPrompt)
        };

        var result = await _client.CompleteAsync(agent.Provider, apiKey, providerCfg.BaseUrl, model, systemPrompt, turns, cancellationToken);

        if (result.Ok)
        {
            await _usage.RecordAsync(agent.Id, agent.Provider, model, result.InputTokens, result.OutputTokens, "test", true, cancellationToken);
        }

        if (result.Ok && cacheFields.Count > 0 && !string.IsNullOrWhiteSpace(result.Text))
        {
            try
            {
                await ExtractAndStoreCacheUpdatesAsync(
                    agentId, sessionId, agent.Provider, apiKey, providerCfg.BaseUrl, model,
                    cacheFields, cacheValues, turns, result.Text!, resources, debugPrompts, cancellationToken);
            }
            catch
            {
                // La extraccion no debe romper la respuesta al cliente.
            }
        }

        if (result.Ok && !string.IsNullOrEmpty(result.Text))
        {
            var (cleanText, attachments) = ExtractAttachments(result.Text!, resources);
            return result with { Text = cleanText, Attachments = attachments, DebugPrompts = debugPrompts };
        }

        return result with { DebugPrompts = debugPrompts };
    }

    private async Task<string> BuildSystemPrompt(
        Guid agentId,
        string basePrompt,
        IReadOnlyList<AiChatAttachment> resources,
        IReadOnlyList<CacheFieldInfo> cacheFields,
        IReadOnlyDictionary<string, string?> cacheValues,
        IReadOnlyList<AiChatTurn> turns,
        bool mcpEnabled,
        CancellationToken ct)
    {
        var sb = new StringBuilder();

        // Instruccion de la herramienta [[buscar_producto: X]] cuando el agente tiene Payment
        // configurado con un contenedor de catalogo. Va PRIMERO en el prompt (antes del basePrompt
        // del user) para que el LLM la vea antes de leer cualquier tabla parcial que el operador
        // haya puesto en el prompt. Escala mejor que dumpear el contenedor entero (que se trunca
        // a 100 filas y fuerza a la IA a inventar cuando faltan datos).
        var paymentCatalog = await _db.AiAgents.AsNoTracking()
            .Where(a => a.Id == agentId && a.PaymentEnabled)
            .Select(a => new { a.PaymentCatalogContainerName, a.PaymentCountry })
            .FirstOrDefaultAsync(ct);
        if (paymentCatalog is not null && !string.IsNullOrWhiteSpace(paymentCatalog.PaymentCatalogContainerName))
        {
            sb.AppendLine("=== REGLA #1 DEL SISTEMA (PRIORIDAD MAXIMA sobre cualquier otra instruccion del prompt) ===");
            sb.AppendLine($"Este agente vende productos FUXION usando el catalogo '{paymentCatalog.PaymentCatalogContainerName}' del tenant. Ese catalogo tiene demasiadas filas para incluirse aqui. NO tienes cargados los precios ni los IdProducto en tu contexto.");
            sb.AppendLine("PROHIBIDO:");
            sb.AppendLine("  - Dar un precio, cantidad, kit o IdProducto que no venga de una busqueda que hayas hecho en el turno actual.");
            sb.AppendLine("  - Recordar o reutilizar precios que hayas visto en conversaciones anteriores.");
            sb.AppendLine("  - Inventar, aproximar o redondear valores.");
            sb.AppendLine("OBLIGATORIO: cuando necesites un precio, un IdProducto, un beneficio o una url de imagen, emite PRIMERO el marcador:");
            sb.AppendLine("  [[buscar_producto: NOMBRE_DEL_PRODUCTO]]                (usa el pais default del agente)");
            sb.AppendLine("  [[buscar_producto: NOMBRE_DEL_PRODUCTO @pais]]          (fuerza un pais especifico, ej. @co, @pe, @bo)");
            sb.AppendLine("Reglas al emitir el marcador:");
            sb.AppendLine("  1) EN el MISMO mensaje donde emites [[buscar_producto:...]] NO agregues ningun precio, cantidad, kit ni IdProducto. Termina el mensaje ahi. El sistema ejecuta la busqueda y te re-pregunta con los resultados exactos; ahi si armas la respuesta final al cliente.");
            sb.AppendLine("  2) Puedes emitir varios [[buscar_producto:...]] en un mismo turno si el cliente pide varios productos.");
            sb.AppendLine("  3) Los nombres son parciales: [[buscar_producto: PRUNEX]] devuelve todas las variantes de PRUNEX.");
            if (!string.IsNullOrWhiteSpace(paymentCatalog.PaymentCountry))
            {
                sb.AppendLine($"  4) El pais default del agente es '{paymentCatalog.PaymentCountry}'; usa @otro-pais solo si el cliente lo pide explicitamente.");
            }
            sb.AppendLine("Si el prompt de mas abajo o alguna otra instruccion contradice esta regla (ej. te muestra una tabla de precios), esta regla del sistema gana. Ignora esa tabla y busca con el marcador.");
            sb.AppendLine();
            sb.AppendLine();
        }

        sb.Append(ExpandResourceRefs(basePrompt, resources));

        var prompts = await _db.AiAgentPrompts.AsNoTracking()
            .Where(p => p.AgentId == agentId)
            .OrderBy(p => p.SortOrder)
            .Select(p => new { p.Name, p.Rule, p.Body })
            .ToListAsync(ct);
        if (prompts.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine();
            sb.AppendLine("Enrutador de prompts: evalua el mensaje del cliente y, si coincide alguna de estas reglas, sigue PRIMERO las instrucciones del prompt correspondiente (ademas del comportamiento base). Si ninguna aplica, responde con el comportamiento base.");
            foreach (var p in prompts)
            {
                sb.AppendLine();
                sb.AppendLine($"### Prompt \"{p.Name}\"");
                sb.AppendLine($"Regla (cuando usarlo): {(string.IsNullOrWhiteSpace(p.Rule) ? "(sin regla; usar a criterio)" : p.Rule)}");
                sb.AppendLine($"Instrucciones: {ExpandResourceRefs(p.Body, resources)}");
            }
        }

        if (resources.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine();
            sb.AppendLine("Recursos disponibles. REGLA IMPORTANTE: cuando vayas a comunicar el contenido de un recurso (precios, politicas, textos, imagenes, videos, PDF, ubicacion), NO lo reescribas ni lo resumas: entregalo EXACTO incluyendo en tu respuesta el marcador [[enviar: Nombre exacto del recurso]]. El sistema agregara el contenido o el archivo tal cual. Puedes acompanarlo con una frase breve, pero el contenido del recurso lo entrega el marcador.");
            sb.AppendLine("PERSONALIZACION DEL CAPTION: si el caption del recurso contiene variables como {nombre_lider}, {nombre_clienta}, {pais}, {edad} u otras {...}, DEBES enviar el caption ya resuelto usando la sintaxis extendida con pipe: [[enviar: Nombre del recurso | \"Caption con las variables ya reemplazadas por sus valores actuales\"]]. Reemplaza cada {variable} con el valor real (los del prompt base, los que ya capturaste del cliente, o los datos ya conocidos). Solo omite el pipe cuando el caption del recurso NO tiene variables o cuando quieres el texto tal cual.");
            foreach (var r in resources)
            {
                var kind = r.ResourceType == AgentResourceType.Text ? "Texto" : r.ResourceType.ToString();
                var desc = string.IsNullOrWhiteSpace(r.Detail) ? "archivo" : r.Detail;
                sb.AppendLine($"- ({kind}) {r.Name}: {desc}  -> entregar con [[enviar: {r.Name}]]");
            }
        }

        var captured = cacheFields
            .Where(f => cacheValues.TryGetValue(f.FieldKey, out var v) && !string.IsNullOrWhiteSpace(v))
            .ToList();
        if (captured.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine();
            sb.AppendLine("### Datos que ya conocemos del cliente (estado de la cache)");
            sb.AppendLine("Estos datos ya estan capturados por el sistema. Usalos para decidir tu siguiente paso: NO le pidas al cliente algo que ya sabes, y avanza el guion si los datos que ya tienes cumplen lo que pide tu prompt enrutado.");
            foreach (var f in captured)
            {
                sb.AppendLine($"- {f.FieldKey}: {cacheValues[f.FieldKey]}");
            }
        }

        if (turns.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine();
            sb.AppendLine("### Ultimos eventos del chat (lo mas reciente al final)");
            sb.AppendLine("Estos son los ultimos mensajes intercambiados. Usalos como contexto inmediato para tu siguiente respuesta. Recuerda que tu objetivo es avanzar el guion segun lo que el cliente diga, no repetir lo que ya enviaste.");
            var lastN = turns.Count > 5 ? turns.Skip(turns.Count - 5).ToList() : turns.ToList();
            foreach (var t in lastN)
            {
                var who = string.Equals(t.Role, "user", StringComparison.OrdinalIgnoreCase) ? "Cliente" : "Agente";
                sb.AppendLine($"- {who}: {BuildTurnLine(t.Text, t.Attachments)}");
            }
        }

        // MCP DataContainer: resuelve placeholders {{LIST.CONTAINERS}} y {{CONTAINER:nombre}}
        // sobre TODO el prompt ensamblado (base + enrutador + recursos + cache + turnos),
        // antes de enviar al proveedor. Idempotente: si no hay placeholders, no hace I/O.
        // Si el agente NO tiene EnableDataContainerMcp, los placeholders se sustituyen por una
        // nota informativa en vez de leer datos del tenant.
        // Si el agente tiene Payment configurado con un contenedor, ese placeholder puntual se
        // reemplaza por una instruccion que remita al tool call [[buscar_producto:X]] en vez de
        // dumpear las primeras 100 filas (que hacian que el LLM invente precios cuando el pais o
        // el producto del cliente cae fuera de las 100 mostradas).
        var assembled = sb.ToString();
        return await _mcp.ResolvePlaceholdersAsync(assembled, mcpEnabled, paymentCatalog?.PaymentCatalogContainerName, ct);
    }

    private async Task ExtractAndStoreCacheUpdatesAsync(
        Guid agentId,
        Guid sessionId,
        AiProvider provider,
        string apiKey,
        string? baseUrl,
        string model,
        IReadOnlyList<CacheFieldInfo> fields,
        IReadOnlyDictionary<string, string?> currentValues,
        IReadOnlyList<AiChatTurn> originalTurns,
        string botResponse,
        IReadOnlyList<AiChatAttachment> resources,
        List<AiDebugPrompt> debugPrompts,
        CancellationToken ct)
    {
        var lastUser = originalTurns.LastOrDefault(t => string.Equals(t.Role, "user", StringComparison.OrdinalIgnoreCase))?.Text ?? "";

        var sysSb = new StringBuilder();
        sysSb.AppendLine("Eres un extractor de datos para una agencia de marketing. NO debes responder al cliente.");
        sysSb.AppendLine("Tu unico trabajo es leer la ultima interaccion cliente+agente y devolver un JSON plano con los campos que puedas inferir CON CERTEZA del mensaje del cliente.");
        sysSb.AppendLine("Reglas:");
        sysSb.AppendLine("- NO inventes datos. Si no esta claro, NO incluyas el campo.");
        sysSb.AppendLine("- Si un campo ya tiene valor y el cliente no lo cambia, NO lo incluyas (no es necesario reescribirlo).");
        sysSb.AppendLine("- NO incluyas el valor literal \"PENDIENTE\".");
        sysSb.AppendLine("- Responde UNICAMENTE el JSON, sin texto antes ni despues, sin markdown.");
        sysSb.AppendLine();
        sysSb.AppendLine("### Campos a capturar");
        foreach (var f in fields)
        {
            sysSb.AppendLine($"- {f.FieldKey}: {(string.IsNullOrWhiteSpace(f.Description) ? f.Label : f.Description)}");
        }
        sysSb.AppendLine();
        sysSb.AppendLine("### Estado actual de la cache");
        var anyKnown = false;
        foreach (var f in fields)
        {
            if (currentValues.TryGetValue(f.FieldKey, out var v) && !string.IsNullOrWhiteSpace(v))
            {
                sysSb.AppendLine($"- {f.FieldKey} = {v}");
                anyKnown = true;
            }
        }
        if (!anyKnown) { sysSb.AppendLine("(vacio)"); }
        sysSb.AppendLine();
        sysSb.AppendLine("### Formato de respuesta");
        sysSb.AppendLine("JSON plano. Ejemplo: {\"tipo_cliente\":\"Interesado\",\"opcion_elegida\":\"Pronto\"}");
        sysSb.AppendLine("Si no hay nada nuevo, responde {}");

        var transcript = new StringBuilder();
        transcript.AppendLine("### Transcripcion completa de la conversacion hasta ahora");
        transcript.AppendLine("Estos son TODOS los mensajes intercambiados, en orden cronologico. El cliente y el agente pueden haber compartido datos en cualquier turno previo, no solo en el ultimo. Cuando el agente envia un recurso, anotamos que recurso entrego y de que trata.");
        transcript.AppendLine();
        foreach (var t in originalTurns)
        {
            var who = string.Equals(t.Role, "user", StringComparison.OrdinalIgnoreCase) ? "Cliente" : "Agente";
            transcript.AppendLine($"{who}: {BuildTurnLine(t.Text, t.Attachments)}");
            transcript.AppendLine();
        }
        transcript.AppendLine($"Agente (respuesta actual): {ExpandMarkersForTranscript(botResponse, resources)}");

        var userTurn = new AiChatTurn("user",
            transcript.ToString() + "\n\n¿Que campos puedes inferir del cliente a partir de TODA esta conversacion? Recuerda: solo agrega campos que puedes inferir CON CERTEZA y no incluyas los que ya tienen valor en el estado actual.");

        var extractorSystemPrompt = sysSb.ToString();
        var extractorEntry = new AiDebugPrompt(
            $"Agente de cache de datos (extractor; ultimo mensaje del cliente: \"{Truncate(lastUser, 60)}\")",
            DateTimeOffset.UtcNow,
            extractorSystemPrompt + "\n\n---\n[Turno del usuario al extractor]\n" + userTurn.Text);
        debugPrompts.Add(extractorEntry);
        var extractorIndex = debugPrompts.Count - 1;

        AiChatResult ext;
        try
        {
            ext = await _client.CompleteAsync(provider, apiKey, baseUrl, model, extractorSystemPrompt, new[] { userTurn }, ct);
        }
        catch (Exception callEx)
        {
            debugPrompts[extractorIndex] = extractorEntry with { Response = $"[Llamada fallida] {callEx.GetType().Name}: {callEx.Message}" };
            return;
        }

        debugPrompts[extractorIndex] = extractorEntry with { Response = ext.Ok ? (ext.Text ?? "(sin texto)") : $"[Sin Ok] {ext.Error}" };
        if (!ext.Ok || string.IsNullOrWhiteSpace(ext.Text)) { return; }

        await _usage.RecordAsync(agentId, provider, model, ext.InputTokens, ext.OutputTokens, "cache", true, ct);

        var json = StripJsonFromMarkdown(ext.Text!);
        Dictionary<string, JsonElement>? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);
        }
        catch
        {
            return;
        }
        if (parsed is null || parsed.Count == 0) { return; }

        var fieldKeys = fields.Select(f => f.FieldKey).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, valEl) in parsed)
        {
            if (!fieldKeys.Contains(key)) { continue; }
            string? v = valEl.ValueKind switch
            {
                JsonValueKind.String => valEl.GetString(),
                JsonValueKind.Number => valEl.GetRawText(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                JsonValueKind.Array => string.Join(", ", valEl.EnumerateArray()
                    .Select(e => e.ValueKind == JsonValueKind.String ? e.GetString() : e.GetRawText())
                    .Where(s => !string.IsNullOrWhiteSpace(s))),
                JsonValueKind.Object => valEl.GetRawText(),
                _ => null
            };
            if (string.IsNullOrWhiteSpace(v)) { continue; }
            if (string.Equals(v.Trim(), "PENDIENTE", StringComparison.OrdinalIgnoreCase)) { continue; }
            try { await _cache.SetValueAsync(new SetAgentCacheValueRequest(agentId, sessionId, key, v.Trim(), "inference"), ct); }
            catch { /* la falla de un campo no debe abortar el resto */ }
        }
    }

    private static string StripJsonFromMarkdown(string text)
    {
        var t = text.Trim();
        t = Regex.Replace(t, @"^```(?:json)?\s*\n?", "", RegexOptions.IgnoreCase);
        t = Regex.Replace(t, @"\n?```\s*$", "");
        var i = t.IndexOf('{');
        var j = t.LastIndexOf('}');
        if (i >= 0 && j > i) { t = t.Substring(i, j - i + 1); }
        return t.Trim();
    }

    private static string ExpandResourceRefs(string text, IReadOnlyList<AiChatAttachment> resources)
    {
        if (string.IsNullOrEmpty(text) || !text.Contains("{{")) { return text; }
        return Regex.Replace(text, @"\{\{\s*([^}]+?)\s*\}\}", m =>
        {
            var res = FindResource(resources, m.Groups[1].Value);
            if (res is null) { return m.Value; }
            return $"el recurso \"{res.Name}\" (entregalo EXACTO incluyendo el marcador [[enviar: {res.Name}]]; el sistema agrega su contenido, no lo reescribas)";
        });
    }

    // Sintaxis del marker de envio de recurso:
    //   [[enviar: NombreRecurso]]                              -> caption = Detail del recurso (comportamiento historico)
    //   [[enviar: NombreRecurso | Caption personalizado]]      -> caption = lo que va despues del pipe
    //   [[enviar: NombreRecurso | "Caption con comillas"]]     -> se aceptan comillas dobles o simples envolventes
    // El pipe permite a la IA resolver placeholders {nombre_lider} etc con los valores capturados.
    private static readonly Regex EnviarMarker = new(
        @"\[\[\s*enviar\s*:\s*([^|\]]+?)\s*(?:\|\s*(.+?)\s*)?\]\]",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static (string, IReadOnlyList<AiChatAttachment>) ExtractAttachments(string text, IReadOnlyList<AiChatAttachment> resources)
    {
        var attachments = new List<AiChatAttachment>();
        var clean = EnviarMarker.Replace(text, m =>
        {
            var res = FindResource(resources, m.Groups[1].Value);
            if (res is null) { return string.Empty; }
            var override_ = m.Groups[2].Success ? StripEnclosingQuotes(m.Groups[2].Value) : null;
            var eff = string.IsNullOrWhiteSpace(override_) ? res : res with { CaptionOverride = override_ };
            // Dedup por (Name + CaptionOverride) para permitir el mismo recurso dos veces con captions distintos.
            if (attachments.All(a => a.Name != eff.Name || (a.CaptionOverride ?? "") != (eff.CaptionOverride ?? "")))
            {
                attachments.Add(eff);
            }
            return string.Empty;
        });

        clean = Regex.Replace(clean, @"[ \t]+\n", "\n").Trim();
        return (clean, attachments);
    }

    private static string? StripEnclosingQuotes(string? s)
    {
        if (string.IsNullOrEmpty(s)) { return s; }
        var t = s.Trim();
        if (t.Length >= 2
            && ((t[0] == '"' && t[^1] == '"') || (t[0] == '\'' && t[^1] == '\'')))
        {
            return t.Substring(1, t.Length - 2);
        }
        return t;
    }

    private static AiChatAttachment? FindResource(IReadOnlyList<AiChatAttachment> resources, string name)
    {
        var key = Normalize(name);
        return resources.FirstOrDefault(r => Normalize(r.Name) == key);
    }

    private static string Normalize(string s)
    {
        var n = s.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var c in n)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) { sb.Append(c); }
        }
        return sb.ToString();
    }

    private sealed record CacheFieldInfo(string FieldKey, string Label, string? Description, bool IsUpdatable);

    private static string Truncate(string s, int n) => string.IsNullOrEmpty(s) ? "" : (s.Length <= n ? s : s[..n] + "...");

    private static string BuildTurnLine(string text, IReadOnlyList<AiChatAttachment>? attachments)
    {
        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(text)) { sb.Append(text.Trim()); }
        if (attachments is { Count: > 0 })
        {
            foreach (var a in attachments)
            {
                if (sb.Length > 0) { sb.AppendLine(); }
                var desc = string.IsNullOrWhiteSpace(a.Detail) ? a.ResourceType.ToString() : a.Detail!.Trim();
                sb.Append($"[envio el recurso \"{a.Name}\" ({a.ResourceType}). Contenido: {desc}]");
            }
        }
        return sb.Length == 0 ? "(turno vacio)" : sb.ToString();
    }

    private static string ExpandMarkersForTranscript(string rawText, IReadOnlyList<AiChatAttachment> resources)
    {
        if (string.IsNullOrEmpty(rawText)) { return "(respuesta vacia)"; }
        var expanded = EnviarMarker.Replace(rawText, m =>
        {
            var name = m.Groups[1].Value.Trim();
            var overrideRaw = m.Groups[2].Success ? StripEnclosingQuotes(m.Groups[2].Value) : null;
            var res = FindResource(resources, name);
            if (res is null) { return $"[envio el recurso \"{name}\"]"; }
            var desc = !string.IsNullOrWhiteSpace(overrideRaw)
                ? overrideRaw!.Trim()
                : (string.IsNullOrWhiteSpace(res.Detail) ? res.ResourceType.ToString() : res.Detail!.Trim());
            return $"[envio el recurso \"{res.Name}\" ({res.ResourceType}). Contenido: {desc}]";
        });
        expanded = Regex.Replace(expanded, @"[ \t]+\n", "\n").Trim();
        return string.IsNullOrEmpty(expanded) ? "(respuesta vacia)" : expanded;
    }
}
