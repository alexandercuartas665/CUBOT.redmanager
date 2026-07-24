using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace CubotRedManager.Application.Tenancy;

/// <summary>
/// Utilidad para quitar los markers de "dispatcher" del texto que devuelve el LLM. Portado 1:1
/// desde CUBOT.travels.
/// </summary>
public static class AgentMarkerCleaner
{
    private static readonly Regex CrearLeadPipeline = new(@"\[\[\s*crear_lead_pipeline(?:\s*:[^\]]*)?\s*\]\]",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex Pedido = new(@"\[\[\s*pedido\s*:[^\]]*\]\]",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex CollapseBlankLines = new(@"\n{3,}", RegexOptions.Compiled);
    private static readonly Regex TrailingWhitespace = new(@"[ \t]+\n", RegexOptions.Compiled);
    private static readonly Regex AnyClosedMarker = new(@"\[\[[\s\S]*?\]\]", RegexOptions.Compiled);

    public static string ScrubResidualMarkers(string? text)
    {
        if (string.IsNullOrEmpty(text)) { return text ?? string.Empty; }
        var s = AnyClosedMarker.Replace(text, string.Empty);
        var open = s.IndexOf("[[", StringComparison.Ordinal);
        if (open >= 0) { s = s[..open]; }
        s = TrailingWhitespace.Replace(s, "\n");
        s = CollapseBlankLines.Replace(s, "\n\n");
        return s.Trim();
    }

    public static bool TextDuplicatesAttachment(string? text, IReadOnlyList<AiChatAttachment>? attachments)
    {
        if (string.IsNullOrWhiteSpace(text) || attachments is not { Count: > 0 }) { return false; }
        var normText = NormalizeLoose(text);
        if (normText.Length == 0) { return false; }

        foreach (var a in attachments)
        {
            var cap = a.EffectiveCaption;
            if (string.IsNullOrWhiteSpace(cap)) { continue; }
            var normCap = NormalizeLoose(cap);
            if (normCap.Length == 0) { continue; }
            if (normCap.Contains(normText) || normText.Contains(normCap))
            {
                var ratio = (double)Math.Min(normText.Length, normCap.Length) / Math.Max(normText.Length, normCap.Length);
                if (ratio >= 0.6) { return true; }
            }
        }
        return false;
    }

    private static string NormalizeLoose(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var c in s.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) { continue; }
            if (char.IsLetterOrDigit(c)) { sb.Append(char.ToLowerInvariant(c)); }
        }
        return sb.ToString();
    }

    public static (string CleanText, IReadOnlyList<string> DetectedMarkers) Strip(string? text)
    {
        if (string.IsNullOrEmpty(text)) { return (text ?? string.Empty, Array.Empty<string>()); }
        var detected = new List<string>();
        foreach (Match m in CrearLeadPipeline.Matches(text)) { detected.Add(m.Value); }
        foreach (Match m in Pedido.Matches(text)) { detected.Add(m.Value); }
        var s = CrearLeadPipeline.Replace(text, string.Empty);
        s = Pedido.Replace(s, string.Empty);
        s = TrailingWhitespace.Replace(s, "\n");
        s = CollapseBlankLines.Replace(s, "\n\n");
        return (s.Trim(), detected);
    }
}
