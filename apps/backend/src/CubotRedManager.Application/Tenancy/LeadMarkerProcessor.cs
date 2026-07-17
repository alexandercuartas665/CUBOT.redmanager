using System.Text.RegularExpressions;

namespace CubotRedManager.Application.Tenancy;

/// <summary>
/// Resultado del procesamiento de markers [[crear_lead_pipeline]] / [[crear_lead_pipeline:{...}]].
/// LeadsCreated queda vacio en redmanager porque Lead+Pipeline aun no se portaron desde travels;
/// el marker solo se retira del texto para que el cliente no lo vea.
/// </summary>
public sealed record LeadMarkerResult(string CleanText, IReadOnlyList<Guid> LeadsCreated, int? AttentionDurationSeconds = null);

public interface ILeadMarkerProcessor
{
    Task<LeadMarkerResult> ProcessAsync(Guid tenantId, Guid agentId, Guid conversationId, string rawText, CancellationToken cancellationToken = default);
}

/// <summary>
/// STUB portado desde CUBOT.travels. En travels crea/actualiza Leads en el pipeline. En redmanager
/// aun no se portaron Lead + PipelineStage + PipelineFieldDefinition, asi que este stub SOLO retira
/// los markers del texto. Cuando se porte el pipeline, reemplazar por la implementacion completa
/// de travels (~495 lineas).
/// </summary>
public sealed class LeadMarkerProcessor : ILeadMarkerProcessor
{
    private static readonly Regex MarkerRegex = new(
        @"\[\[\s*crear_lead_pipeline\s*(?::\s*(?<json>.+?))?\s*\]\]",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    public Task<LeadMarkerResult> ProcessAsync(Guid tenantId, Guid agentId, Guid conversationId, string rawText, CancellationToken cancellationToken = default)
    {
        _ = tenantId; _ = agentId; _ = conversationId; _ = cancellationToken;
        if (string.IsNullOrWhiteSpace(rawText))
        {
            return Task.FromResult(new LeadMarkerResult(rawText, Array.Empty<Guid>()));
        }
        return Task.FromResult(new LeadMarkerResult(StripMarkers(rawText), Array.Empty<Guid>()));
    }

    private static string StripMarkers(string raw)
    {
        var clean = MarkerRegex.Replace(raw, string.Empty);
        clean = Regex.Replace(clean, @"[ \t]+\n", "\n");
        return clean.Trim();
    }
}
