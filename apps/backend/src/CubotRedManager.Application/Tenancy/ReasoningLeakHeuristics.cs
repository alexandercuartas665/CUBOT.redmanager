namespace CubotRedManager.Application.Tenancy;

/// <summary>
/// Heuristica para detectar cuando el LLM filtra su razonamiento interno como si fuera la respuesta
/// al cliente. Portado 1:1 desde CUBOT.travels.
/// </summary>
public static class ReasoningLeakHeuristics
{
    private static readonly string[] HighSignalPhrases =
    {
        "from the system instructions", "the system instructions", "the system prompt",
        "the prompt says", "the prompt instructs", "pipeline marker", "final assembly",
        "final answer:", "let me assemble", "let's assemble", "this matches the instructions",
        "matches the instructions perfectly", "add the pipeline marker", "escalated to a human",
        "i need to emit", "i should emit", "i will emit", "i must emit", "the marker [[",
        "according to the instructions", "based on the rules", "as per the prompt",
        "step 1:", "step 2:", "step 3:", "step 4:", "step 5:"
    };

    public static bool LooksLikeLeak(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) { return false; }
        var lower = text.ToLowerInvariant();
        foreach (var p in HighSignalPhrases)
        {
            if (lower.Contains(p, StringComparison.Ordinal)) { return true; }
        }
        if (lower.Contains("```", StringComparison.Ordinal)) { return true; }
        return false;
    }

    public static string Explain(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) { return "(texto vacio)"; }
        var lower = text.ToLowerInvariant();
        var hits = HighSignalPhrases.Where(p => lower.Contains(p, StringComparison.Ordinal)).ToList();
        if (lower.Contains("```", StringComparison.Ordinal)) { hits.Add("``` (bloque de codigo)"); }
        return hits.Count > 0
            ? $"Senales de razonamiento detectadas: {string.Join(" | ", hits)}"
            : "Heuristica de razonamiento (sin senal especifica)";
    }
}
