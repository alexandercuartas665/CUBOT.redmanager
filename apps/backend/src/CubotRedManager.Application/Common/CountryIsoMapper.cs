namespace CubotRedManager.Application.Common;

/// <summary>
/// Mapea nombres de pais en espanol/portugues (como estan en el DataContainer o en la config del
/// agente) al codigo ISO2 que exige la API de FUXION (country=bo, co, pe, ...). Compartido entre
/// PaymentLinkProcessor y PriceSyncService para que ambos hablen el mismo idioma con FUXION.
/// </summary>
public static class CountryIsoMapper
{
    private static readonly Dictionary<string, string> NameToIso2 = new(StringComparer.OrdinalIgnoreCase)
    {
        ["argentina"] = "ar", ["austria"] = "at", ["belgica"] = "be", ["bélgica"] = "be",
        ["bolivia"] = "bo", ["brasil"] = "br", ["chile"] = "cl", ["colombia"] = "co",
        ["costa rica"] = "cr", ["alemania"] = "de", ["ecuador"] = "ec",
        ["espana"] = "es", ["españa"] = "es", ["francia"] = "fr", ["guatemala"] = "gt",
        ["honduras"] = "hn", ["croacia"] = "hr", ["irlanda"] = "ie", ["italia"] = "it",
        ["luxemburgo"] = "lu", ["mexico"] = "mx", ["méxico"] = "mx",
        ["netherlands"] = "nl", ["holanda"] = "nl", ["paises bajos"] = "nl", ["países bajos"] = "nl",
        ["panama"] = "pa", ["panamá"] = "pa", ["peru"] = "pe", ["perú"] = "pe",
        ["portugal"] = "pt", ["europa - portugal"] = "pt",
        ["eslovenia"] = "si", ["eslovaquia"] = "sk",
        ["estados unidos"] = "us", ["usa"] = "us", ["uruguay"] = "uy",
    };

    /// <summary>Devuelve el ISO2 en minusculas si conoce el pais, si viene ya con 2 chars lo
    /// devuelve tal cual, si no reconoce lo devuelve en minusculas (para no perder info y que
    /// el caller decida si loguear/skipear). Cadena vacia => "".</summary>
    public static string ToIso2(string? raw)
    {
        var trimmed = (raw ?? "").Trim();
        if (trimmed.Length == 0) { return ""; }
        if (trimmed.Length == 2) { return trimmed.ToLowerInvariant(); }
        return NameToIso2.TryGetValue(trimmed, out var iso) ? iso : trimmed.ToLowerInvariant();
    }
}
