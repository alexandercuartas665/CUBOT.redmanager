using System.Globalization;
using System.Text;

namespace CubotRedManager.Application.Tenancy;

/// <summary>
/// Heuristicas para detectar el "cierre tacito": el LLM cierra la atencion conversacionalmente sin
/// emitir el marcador tecnico [[crear_lead_pipeline]]. Portado 1:1 desde CUBOT.travels.
/// </summary>
public static class TacitCloseHeuristics
{
    public static readonly string[] RequiredFields =
    {
        "destino", "ciudad_aeropuerto_salida", "cantidad_de_adultos",
        "fecha_ida", "tipo_plan"
    };

    public static readonly string[] ClosePhrases =
    {
        "uno de nuestros agentes",
        "uno de nuestros asesores",
        "te enviare la informacion",
        "te enviare las opciones",
        "te enviaremos la informacion",
        "tomara tu solicitud",
        "te contactara",
        "gestionara tu solicitud",
        "presentacion del destino"
    };

    public static bool HasClosePhrase(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) { return false; }
        var normalized = StripDiacritics(text.ToLowerInvariant());
        return ClosePhrases.Any(p => normalized.Contains(p));
    }

    public static IReadOnlyList<string> MissingFields(IEnumerable<string> presentFieldKeys)
    {
        var present = new HashSet<string>(presentFieldKeys, StringComparer.OrdinalIgnoreCase);
        return RequiredFields.Where(f => !present.Contains(f)).ToList();
    }

    private static string StripDiacritics(string s)
    {
        var normalized = s.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);
        foreach (var c in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) { sb.Append(c); }
        }
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }
}
