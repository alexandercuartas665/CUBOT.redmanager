using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using CubotRedManager.Application.Abstractions;
using CubotRedManager.Domain.Entities;
using CubotRedManager.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CubotRedManager.Application.Tenancy;

/// <summary>
/// Resultado de procesar la respuesta cruda del LLM en busca de marcadores
/// <c>[[crear_lead_pipeline]]</c> o <c>[[crear_lead_pipeline:{...json...}]]</c>. CleanText
/// es el texto que se enviara al cliente (con los marcadores eliminados); LeadsCreated lleva
/// los Ids de los leads que se crearon en este turno.
/// </summary>
public sealed record LeadMarkerResult(string CleanText, IReadOnlyList<Guid> LeadsCreated, int? AttentionDurationSeconds = null);

/// <summary>
/// Procesa los marcadores que cierran una atencion creando un Lead en el pipeline. Soporta DOS
/// formatos:
///
/// 1) <c>[[crear_lead_pipeline]]</c> (sin JSON) — lee los valores de datos cache de la sesion,
///    aplica los mapeos configurados del agente (AiAgentCacheLeadMapping) y crea el lead.
///    Despues borra los valores cache de la sesion.
///
/// 2) <c>[[crear_lead_pipeline:{json}]]</c> — formato legacy donde el LLM serializa el JSON
///    inline. Sigue funcionando para no romper agentes previos. NO toca la cache.
///
/// A diferencia de CUBOT.travels, en redmanager:
/// - Hay MULTIPLES pipelines por tenant. El lead se crea en AiAgent.DefaultPipelineId; si no
///   esta configurado, en el primer pipeline del tenant (por SortOrder). Si el agente tiene
///   CreateLeadsInPipeline = false, el marcador se ignora (solo se limpia del texto).
/// - El Lead NO tiene Destination/DepartureCity; usa Topic como campo libre.
///
/// Cualquier excepcion se traga: el flujo de respuesta al cliente NO se bloquea.
/// </summary>
public interface ILeadMarkerProcessor
{
    Task<LeadMarkerResult> ProcessAsync(Guid tenantId, Guid agentId, Guid conversationId, string rawText, CancellationToken cancellationToken = default);
}

public sealed class LeadMarkerProcessor : ILeadMarkerProcessor
{
    // Marker unico que cubre ambos formatos: con JSON (grupo "json" capturado) o sin nada.
    // Ej: [[crear_lead_pipeline]]               -> json es null
    //     [[crear_lead_pipeline: {"x":1}]]      -> json = "{\"x\":1}"
    private static readonly Regex MarkerRegex = new(
        @"\[\[\s*crear_lead_pipeline\s*(?::\s*(?<json>.+?))?\s*\]\]",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    private readonly IApplicationDbContext _db;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<LeadMarkerProcessor> _logger;

    public LeadMarkerProcessor(IApplicationDbContext db, TimeProvider timeProvider, ILogger<LeadMarkerProcessor> logger)
    {
        _db = db;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<LeadMarkerResult> ProcessAsync(Guid tenantId, Guid agentId, Guid conversationId, string rawText, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(rawText))
        {
            return new LeadMarkerResult(rawText, Array.Empty<Guid>());
        }

        var matches = MarkerRegex.Matches(rawText);
        if (matches.Count == 0)
        {
            return new LeadMarkerResult(rawText, Array.Empty<Guid>());
        }

        // Config del agente: solo creamos leads si el flag esta activo. Si esta apagado, los
        // marcadores se limpian pero no generan leads (modo "sugerencia que ignoramos").
        var agent = await _db.AiAgents
            .IgnoreQueryFilters()
            .Where(a => a.Id == agentId && a.TenantId == tenantId)
            .Select(a => new { a.CreateLeadsInPipeline, a.DefaultPipelineId })
            .FirstOrDefaultAsync(cancellationToken);
        if (agent is null || !agent.CreateLeadsInPipeline)
        {
            return new LeadMarkerResult(StripMarkers(rawText), Array.Empty<Guid>());
        }

        // Resolver pipeline destino: DefaultPipelineId del agente, o el primero del tenant.
        Guid? pipelineId = agent.DefaultPipelineId;
        if (pipelineId is null || !await _db.Pipelines.IgnoreQueryFilters()
                .AnyAsync(p => p.Id == pipelineId && p.TenantId == tenantId, cancellationToken))
        {
            pipelineId = await _db.Pipelines
                .IgnoreQueryFilters()
                .Where(p => p.TenantId == tenantId)
                .OrderBy(p => p.SortOrder)
                .Select(p => (Guid?)p.Id)
                .FirstOrDefaultAsync(cancellationToken);
        }
        if (pipelineId is null)
        {
            // Tenant sin pipelines: no podemos crear nada. Limpia markers y sigue.
            _logger.LogWarning("Marker [[crear_lead_pipeline]] recibido pero el tenant {TenantId} no tiene pipelines configurados.", tenantId);
            return new LeadMarkerResult(StripMarkers(rawText), Array.Empty<Guid>());
        }

        // Conversacion para defaults de telefono/nombre + reset de contexto (para medir tiempo de atencion).
        var conv = await _db.Conversations
            .IgnoreQueryFilters()
            .Where(c => c.Id == conversationId && c.TenantId == tenantId)
            .Select(c => new { c.ContactPhone, c.ContactName, c.AgentContextResetAt })
            .FirstOrDefaultAsync(cancellationToken);

        // TIEMPO DE ATENCION: segundos desde el primer mensaje del cliente (de este ciclo tras el
        // ultimo reinicio de contexto) hasta ahora.
        var resetAt = conv?.AgentContextResetAt;
        var firstContactAt = await _db.Messages
            .IgnoreQueryFilters()
            .Where(msg => msg.TenantId == tenantId && msg.ConversationId == conversationId
                       && msg.Direction == MessageDirection.Inbound
                       && (resetAt == null || msg.SentAt > resetAt))
            .OrderBy(msg => msg.SentAt)
            .Select(msg => (DateTimeOffset?)msg.SentAt)
            .FirstOrDefaultAsync(cancellationToken);
        int? attentionSeconds = firstContactAt is DateTimeOffset fc
            ? (int)Math.Max(0, (_timeProvider.GetUtcNow() - fc).TotalSeconds)
            : null;

        // Etapas del pipeline destino (solo las del pipeline resuelto).
        var stages = await _db.PipelineStages
            .IgnoreQueryFilters()
            .Where(s => s.TenantId == tenantId && s.PipelineId == pipelineId)
            .OrderBy(s => s.SortOrder)
            .Select(s => new StageRef(s.Id, s.Name))
            .ToListAsync(cancellationToken);
        if (stages.Count == 0)
        {
            _logger.LogWarning("Pipeline {PipelineId} del tenant {TenantId} no tiene etapas; marker ignorado.", pipelineId, tenantId);
            return new LeadMarkerResult(StripMarkers(rawText), Array.Empty<Guid>());
        }

        // Mappings cache -> Lead (1 query).
        var mappings = await _db.AiAgentCacheLeadMappings
            .IgnoreQueryFilters()
            .Where(m => m.TenantId == tenantId && m.AgentId == agentId)
            .Select(m => new { m.CacheFieldKey, m.TargetSelector })
            .ToListAsync(cancellationToken);

        // Metadata de los campos del pipeline destino: serializacion de multi/repetibles.
        var fieldMeta = (await _db.PipelineFieldDefinitions
                .IgnoreQueryFilters()
                .Where(f => f.TenantId == tenantId && f.Stage!.PipelineId == pipelineId)
                .Select(f => new { f.FieldKey, f.AllowMultiple, f.MultiWithDetail, f.RepeatWithFieldKey })
                .ToListAsync(cancellationToken))
            .GroupBy(f => f.FieldKey)
            .ToDictionary(
                g => g.Key,
                g => new FieldMeta(g.First().AllowMultiple, g.First().MultiWithDetail,
                                   !string.IsNullOrEmpty(g.First().RepeatWithFieldKey)));

        // Cache values de la sesion.
        var cacheValues = await _db.AiAgentCacheValues
            .IgnoreQueryFilters()
            .Where(v => v.TenantId == tenantId && v.AgentId == agentId && v.SessionId == conversationId)
            .Select(v => new { v.Id, v.FieldKey, v.Value })
            .ToListAsync(cancellationToken);
        var cacheDict = cacheValues
            .Where(v => !string.IsNullOrWhiteSpace(v.Value))
            .ToDictionary(v => v.FieldKey, v => v.Value!);

        var created = new List<Guid>();
        var updated = new List<Guid>();
        bool anyFromCache = false;
        Guid? activeLeadId = null;

        foreach (Match m in matches)
        {
            Lead? candidate;
            if (m.Groups["json"].Success && !string.IsNullOrWhiteSpace(m.Groups["json"].Value))
            {
                candidate = TryBuildLeadFromJson(m.Groups["json"].Value, tenantId, pipelineId.Value, conv?.ContactPhone, conv?.ContactName, stages);
            }
            else
            {
                candidate = TryBuildLeadFromCache(tenantId, pipelineId.Value,
                    mappings.ToDictionary(x => x.CacheFieldKey, x => x.TargetSelector),
                    cacheDict, conv?.ContactPhone, conv?.ContactName, stages, fieldMeta);
                if (candidate is not null) { anyFromCache = true; }
            }

            if (candidate is null) { continue; }
            candidate.AttentionDurationSeconds = attentionSeconds;

            // Candado anti-duplicado por contacto (igual que travels): si existe un lead abierto
            // del mismo telefono, lo enriquecemos en lugar de duplicarlo. Si ya tiene asesor, no
            // tocamos nada (respetamos su trabajo), pero re-apuntamos la conversacion.
            Lead? existing = null;
            if (!string.IsNullOrWhiteSpace(candidate.ContactPhone))
            {
                existing = await _db.Leads
                    .IgnoreQueryFilters()
                    .Where(l => l.TenantId == tenantId
                             && l.ArchivedAt == null
                             && l.ContactPhone == candidate.ContactPhone)
                    .OrderByDescending(l => l.CreatedAt)
                    .FirstOrDefaultAsync(cancellationToken);
            }

            if (existing is null)
            {
                _db.Leads.Add(candidate);
                created.Add(candidate.Id);
                activeLeadId = candidate.Id;
            }
            else if (existing.AssignedToTenantUserId is null)
            {
                MergeLeadInto(existing, candidate);
                if (!updated.Contains(existing.Id)) { updated.Add(existing.Id); }
                activeLeadId = existing.Id;
            }
            else
            {
                activeLeadId = existing.Id;
            }
        }

        // Limpieza de cache: solo si CREAMOS desde la cache. Formato legacy con JSON no toca cache.
        if (anyFromCache && cacheValues.Count > 0)
        {
            var attached = await _db.AiAgentCacheValues
                .IgnoreQueryFilters()
                .Where(v => v.TenantId == tenantId && v.AgentId == agentId && v.SessionId == conversationId)
                .ToListAsync(cancellationToken);
            foreach (var v in attached) { _db.AiAgentCacheValues.Remove(v); }
        }

        // Enlace conversacion -> lead activo (asi el "candado del asesor" del AgentDispatcher
        // silencia al bot cuando se asigne asesor al lead vigente).
        bool convLinkChanged = false;
        if (activeLeadId is Guid linkLeadId)
        {
            var convEntity = await _db.Conversations
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(c => c.Id == conversationId && c.TenantId == tenantId, cancellationToken);
            if (convEntity is not null && convEntity.LeadId != linkLeadId)
            {
                convEntity.LeadId = linkLeadId;
                convLinkChanged = true;
            }
        }

        if (created.Count > 0 || updated.Count > 0 || anyFromCache || convLinkChanged)
        {
            try
            {
                await _db.SaveChangesAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "LeadMarkerProcessor: fallo al persistir cambios (tenant={TenantId} agent={AgentId} conv={ConversationId})",
                    tenantId, agentId, conversationId);
                created.Clear();
                updated.Clear();
            }
        }

        return new LeadMarkerResult(StripMarkers(rawText), created, created.Count > 0 ? attentionSeconds : null);
    }

    private static string StripMarkers(string raw)
    {
        var clean = MarkerRegex.Replace(raw, string.Empty);
        clean = Regex.Replace(clean, @"[ \t]+\n", "\n");
        return clean.Trim();
    }

    private static void MergeLeadInto(Lead existing, Lead candidate)
    {
        if (!string.IsNullOrWhiteSpace(candidate.ContactName)
            && !string.Equals(candidate.ContactName, "Cliente WhatsApp", StringComparison.OrdinalIgnoreCase))
        {
            existing.ContactName = candidate.ContactName;
        }
        if (!string.IsNullOrWhiteSpace(candidate.Topic)) { existing.Topic = candidate.Topic; }
        if (candidate.EstimatedValue is not null) { existing.EstimatedValue = candidate.EstimatedValue; }
        if (!string.IsNullOrWhiteSpace(candidate.Currency)) { existing.Currency = candidate.Currency; }
        existing.FieldValuesJson = MergeFieldValues(existing.FieldValuesJson, candidate.FieldValuesJson);
    }

    private static string? MergeFieldValues(string? existingJson, string? candidateJson)
    {
        if (string.IsNullOrWhiteSpace(candidateJson)) { return existingJson; }
        if (string.IsNullOrWhiteSpace(existingJson)) { return candidateJson; }
        try
        {
            var a = JsonSerializer.Deserialize<Dictionary<string, string?>>(existingJson) ?? new();
            var b = JsonSerializer.Deserialize<Dictionary<string, string?>>(candidateJson) ?? new();
            foreach (var kv in b)
            {
                if (!string.IsNullOrWhiteSpace(kv.Value)) { a[kv.Key] = kv.Value; }
            }
            return a.Count > 0 ? JsonSerializer.Serialize(a) : null;
        }
        catch
        {
            return candidateJson;
        }
    }

    private sealed record StageRef(Guid Id, string Name);

    private sealed record FieldMeta(bool AllowMultiple, bool MultiWithDetail, bool IsRepeat);

    private static readonly char[] MultiSeparators = { ',', ';', '/', '\n' };

    private static string SerializeForField(string key, string rawValue, IReadOnlyDictionary<string, FieldMeta> fieldMeta)
    {
        if (!fieldMeta.TryGetValue(key, out var meta) || (!meta.AllowMultiple && !meta.IsRepeat))
        {
            return rawValue;
        }

        var items = SplitMulti(rawValue);
        if (items.Count == 0) { return rawValue; }

        if (meta.AllowMultiple && meta.MultiWithDetail)
        {
            var objs = items.Select(v => new Dictionary<string, string?> { ["d"] = null, ["v"] = v }).ToList();
            return JsonSerializer.Serialize(objs);
        }

        return JsonSerializer.Serialize(items);
    }

    private static List<string> SplitMulti(string raw)
        => raw.Split(MultiSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
              .Where(s => s.Length > 0)
              .ToList();

    private Lead? TryBuildLeadFromCache(
        Guid tenantId,
        Guid pipelineId,
        IReadOnlyDictionary<string, string> mappingsByCacheKey,
        IReadOnlyDictionary<string, string> cache,
        string? defaultPhone,
        string? defaultName,
        IReadOnlyList<StageRef> stages,
        IReadOnlyDictionary<string, FieldMeta> fieldMeta)
    {
        string? name = null, phone = null, topic = null, currency = null;
        decimal? value = null;
        var customFields = new Dictionary<string, string?>();

        foreach (var (cacheKey, selector) in mappingsByCacheKey)
        {
            if (!cache.TryGetValue(cacheKey, out var raw) || string.IsNullOrWhiteSpace(raw)) { continue; }
            var trimmed = raw.Trim();

            if (selector.StartsWith("core:", StringComparison.OrdinalIgnoreCase))
            {
                var field = selector.Substring(5);
                switch (field.ToLowerInvariant())
                {
                    case "contactname":    name = trimmed; break;
                    case "contactphone":   phone = trimmed; break;
                    case "topic":          topic = trimmed; break;
                    case "currency":       currency = trimmed.ToUpperInvariant(); break;
                    case "estimatedvalue":
                        if (decimal.TryParse(trimmed, NumberStyles.Any, CultureInfo.InvariantCulture, out var d)) { value = d; }
                        break;
                }
            }
            else if (selector.StartsWith("field:", StringComparison.OrdinalIgnoreCase))
            {
                var key = selector.Substring(6);
                if (!string.IsNullOrWhiteSpace(key)) { customFields[key] = SerializeForField(key, trimmed, fieldMeta); }
            }
        }

        name ??= defaultName ?? defaultPhone ?? "Cliente WhatsApp";
        phone ??= defaultPhone;

        return new Lead
        {
            TenantId = tenantId,
            ContactName = name.Trim(),
            ContactPhone = phone?.Trim(),
            Topic = string.IsNullOrWhiteSpace(topic) ? null : topic!.Trim(),
            EstimatedValue = value,
            Currency = currency,
            PipelineId = pipelineId,
            StageId = stages[0].Id,
            Status = LeadStatus.Open,
            StageChangedAt = _timeProvider.GetUtcNow(),
            FieldValuesJson = customFields.Count > 0 ? JsonSerializer.Serialize(customFields) : null
        };
    }

    private Lead? TryBuildLeadFromJson(
        string json,
        Guid tenantId,
        Guid pipelineId,
        string? defaultPhone,
        string? defaultName,
        IReadOnlyList<StageRef> stages)
    {
        JsonElement root;
        try
        {
            using var doc = JsonDocument.Parse(json);
            root = doc.RootElement.Clone();
        }
        catch
        {
            return null;
        }

        if (root.ValueKind != JsonValueKind.Object) { return null; }

        var name = ReadString(root, "nombre", "name", "contacto", "contact_name")
                   ?? defaultName
                   ?? defaultPhone
                   ?? "Cliente WhatsApp";
        var phone = ReadString(root, "telefono", "phone", "celular") ?? defaultPhone;
        var topic = ReadString(root, "tema", "topic", "producto", "product", "interes");
        var currency = ReadString(root, "moneda", "currency");
        decimal? value = ReadDecimal(root, "valor", "value", "monto", "amount");
        var stageHint = ReadString(root, "etapa", "stage");

        var stageId = stages[0].Id;
        if (!string.IsNullOrWhiteSpace(stageHint))
        {
            foreach (var s in stages)
            {
                if (string.Equals(s.Name, stageHint, StringComparison.OrdinalIgnoreCase))
                {
                    stageId = s.Id;
                    break;
                }
            }
        }

        return new Lead
        {
            TenantId = tenantId,
            ContactName = name.Trim(),
            ContactPhone = phone?.Trim(),
            Topic = string.IsNullOrWhiteSpace(topic) ? null : topic!.Trim(),
            EstimatedValue = value,
            Currency = string.IsNullOrWhiteSpace(currency) ? null : currency!.Trim().ToUpperInvariant(),
            PipelineId = pipelineId,
            StageId = stageId,
            Status = LeadStatus.Open,
            StageChangedAt = _timeProvider.GetUtcNow()
        };
    }

    private static string? ReadString(JsonElement root, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (root.TryGetProperty(key, out var el))
            {
                if (el.ValueKind == JsonValueKind.String) { return el.GetString(); }
                if (el.ValueKind == JsonValueKind.Number) { return el.GetRawText(); }
            }
        }
        return null;
    }

    private static decimal? ReadDecimal(JsonElement root, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (root.TryGetProperty(key, out var el))
            {
                if (el.ValueKind == JsonValueKind.Number && el.TryGetDecimal(out var d)) { return d; }
                if (el.ValueKind == JsonValueKind.String && decimal.TryParse(el.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var ds)) { return ds; }
            }
        }
        return null;
    }
}
