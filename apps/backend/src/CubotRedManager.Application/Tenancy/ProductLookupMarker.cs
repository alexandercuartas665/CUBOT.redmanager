using System.Text.RegularExpressions;

namespace CubotRedManager.Application.Tenancy;

/// <summary>
/// Extrae los marcadores <c>[[buscar_producto: NOMBRE]]</c> o <c>[[buscar_producto: NOMBRE @pais]]</c>
/// del texto que emite el LLM. El dispatcher los ejecuta y reinvoca al LLM con los resultados.
/// </summary>
public static class ProductLookupMarker
{
    // Grupo query: todo lo que va despues del ':' hasta el ']]'. Grupo iso: opcional '@xx' al final
    // del query (2 chars ISO2, o nombre que CountryIsoMapper luego resuelve).
    // Ejemplos que matchean:
    //   [[buscar_producto: PRUNEX]]
    //   [[buscar_producto: PRUNEX @co]]
    //   [[buscar_producto:  Kit basico Prunex @ colombia ]]
    public static readonly Regex Regex = new(
        @"\[\[\s*buscar_producto\s*:\s*(?<query>[^\]]+?)\s*\]\]",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public sealed record Query(string Text, string? CountryIso2, string RawMarker);

    public static List<Query> Extract(string? agentText)
    {
        var list = new List<Query>();
        if (string.IsNullOrEmpty(agentText)) { return list; }
        foreach (Match m in Regex.Matches(agentText))
        {
            var raw = m.Value;
            var body = m.Groups["query"].Value.Trim();
            string q = body;
            string? iso = null;
            var atIdx = body.LastIndexOf('@');
            if (atIdx > 0 && atIdx < body.Length - 1)
            {
                q = body.Substring(0, atIdx).Trim();
                iso = body.Substring(atIdx + 1).Trim();
            }
            if (q.Length > 0) { list.Add(new Query(q, iso, raw)); }
        }
        return list;
    }
}
