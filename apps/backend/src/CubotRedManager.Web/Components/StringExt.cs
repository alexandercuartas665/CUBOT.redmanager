namespace CubotRedManager.Web.Components;

/// <summary>Utilidades de string usadas por las paginas Blazor.</summary>
internal static class StringExt
{
    /// <summary>Devuelve null si el string es null/blanco; en otro caso lo trimea.</summary>
    public static string? NullIfBlank(this string? s)
        => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
