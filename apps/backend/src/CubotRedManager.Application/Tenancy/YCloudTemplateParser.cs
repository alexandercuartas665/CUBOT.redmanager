using System.Text.Json;

namespace CubotRedManager.Application.Tenancy;

/// <summary>
/// Parseo PURO (sin red) de un item de plantilla como lo devuelve YCloud en <c>items[]</c> (formato
/// Meta: name/language/status/category + components HEADER/BODY/FOOTER/BUTTONS). Aislado del cliente
/// HTTP para poder testearlo con payloads reales. Las variables del cuerpo son POSICIONALES
/// ({{1}}...{{N}}) y sus ejemplos se leen de <c>example.body_text[0]</c>.
/// Portado desde CUBOT.travels / ECOREX.tareas (completando PR A).
/// </summary>
public static class YCloudTemplateParser
{
    /// <summary>Parsea un item de la lista YCloud. Devuelve null si falta el campo <c>name</c>
    /// (item invalido). El estado y los extras (header/body/footer/buttons) van en el record devuelto.</summary>
    public static YCloudTemplateStatus? ParseItem(JsonElement it)
    {
        var name = Str(it, "name");
        if (string.IsNullOrWhiteSpace(name)) { return null; }

        var (headerFormat, headerText, bodyText, footerText, varExamples, buttons) = ParseComponents(it);
        return new YCloudTemplateStatus(
            name!,
            Str(it, "language"),
            Str(it, "status") ?? "UNKNOWN",
            Str(it, "id"),
            Str(it, "rejectedReason") ?? Str(it, "qualityScore"),
            Category: Str(it, "category"),
            HeaderFormat: headerFormat,
            HeaderText: headerText,
            BodyText: bodyText,
            FooterText: footerText,
            VariableExamples: varExamples,
            Buttons: buttons);
    }

    private static (string? headerFormat, string? headerText, string? bodyText, string? footerText,
        IReadOnlyList<string>? varExamples, IReadOnlyList<WhatsAppTemplateButtonInfo>? buttons)
        ParseComponents(JsonElement it)
    {
        string? headerFormat = null, headerText = null, bodyText = null, footerText = null;
        List<string>? varExamples = null;
        List<WhatsAppTemplateButtonInfo>? buttons = null;
        if (!it.TryGetProperty("components", out var comps) || comps.ValueKind != JsonValueKind.Array)
        {
            return (null, null, null, null, null, null);
        }
        foreach (var c in comps.EnumerateArray())
        {
            switch (Str(c, "type")?.ToUpperInvariant())
            {
                case "HEADER":
                    headerFormat = Str(c, "format")?.ToUpperInvariant();
                    if (string.Equals(headerFormat, "TEXT", StringComparison.Ordinal)) { headerText = Str(c, "text"); }
                    break;
                case "BODY":
                    bodyText = Str(c, "text");
                    if (c.TryGetProperty("example", out var ex) && ex.ValueKind == JsonValueKind.Object
                        && ex.TryGetProperty("body_text", out var bt) && bt.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var group in bt.EnumerateArray())
                        {
                            if (group.ValueKind != JsonValueKind.Array) { continue; }
                            varExamples = new List<string>();
                            foreach (var v in group.EnumerateArray())
                            {
                                varExamples.Add(v.ValueKind == JsonValueKind.String ? (v.GetString() ?? string.Empty) : v.ToString());
                            }
                            break; // solo el primer grupo de ejemplos
                        }
                    }
                    break;
                case "FOOTER":
                    footerText = Str(c, "text");
                    break;
                case "BUTTONS":
                    buttons = ParseButtons(c);
                    break;
            }
        }
        return (headerFormat, headerText, bodyText, footerText, varExamples, buttons);
    }

    /// <summary>Lee el arreglo <c>buttons[]</c> de un componente BUTTONS (formato Meta). Cada boton
    /// trae type (URL/QUICK_REPLY/PHONE_NUMBER/COPY_CODE/...), text y, segun el tipo, url o
    /// phone_number. Un boton URL con <c>{{</c> en la url es DINAMICO (sufijo variable por envio).
    /// Devuelve null si no hay botones validos.</summary>
    private static List<WhatsAppTemplateButtonInfo>? ParseButtons(JsonElement comp)
    {
        if (!comp.TryGetProperty("buttons", out var arr) || arr.ValueKind != JsonValueKind.Array)
        {
            return null;
        }
        var list = new List<WhatsAppTemplateButtonInfo>();
        foreach (var b in arr.EnumerateArray())
        {
            if (b.ValueKind != JsonValueKind.Object) { continue; }
            var type = Str(b, "type")?.ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(type)) { continue; }
            var url = Str(b, "url");
            var hasVar = !string.IsNullOrWhiteSpace(url) && url!.Contains("{{", StringComparison.Ordinal);
            list.Add(new WhatsAppTemplateButtonInfo(type!, Str(b, "text"), url, Str(b, "phone_number"), hasVar));
        }
        return list.Count > 0 ? list : null;
    }

    private static string? Str(JsonElement el, string prop)
        => el.ValueKind == JsonValueKind.Object && el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;
}
