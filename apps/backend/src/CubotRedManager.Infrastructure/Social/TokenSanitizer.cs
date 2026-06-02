using System.Text.RegularExpressions;

namespace CubotRedManager.Infrastructure.Social;

/// <summary>
/// Enmascara secretos (access/refresh tokens, JWTs) en strings que puedan llegar a logs o trazas.
/// TikTok suele incluir el refresh_token en el cuerpo de sus mensajes de error
/// ("Invalid refresh_token:rft.XXX..."), y debemos enmascararlo antes de propagarlo.
/// Cumple la regla no negociable: jamas loggear access/refresh tokens (CLAUDE.md §5).
/// </summary>
public static class TokenSanitizer
{
    // TikTok: "rft.PUXDS...!4637.s1.YYY", "act.XXX!1234.s1.YYY", "atk.XXX..."
    private static readonly Regex TikTokTokenPattern = new(
        @"(rft|act|atk|act\d*|rft\d*)\.[A-Za-z0-9!._\-]{12,}",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // JWT-like: tres segmentos base64url separados por puntos
    private static readonly Regex JwtPattern = new(
        @"eyJ[A-Za-z0-9_\-]+\.[A-Za-z0-9_\-]+\.[A-Za-z0-9_\-]+",
        RegexOptions.Compiled);

    /// <summary>Devuelve la cadena con los tokens reconocidos enmascarados como "[token]".</summary>
    public static string? Sanitize(string? input)
    {
        if (string.IsNullOrEmpty(input)) { return input; }
        var s = TikTokTokenPattern.Replace(input, "[token]");
        s = JwtPattern.Replace(s, "[jwt]");
        return s;
    }
}
