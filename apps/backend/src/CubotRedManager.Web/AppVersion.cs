namespace CubotRedManager.Web;

/// <summary>
/// Metadatos de version de la build en curso. Los valores se obtienen en tiempo de arranque desde
/// variables de entorno que Railway inyecta automaticamente (RAILWAY_GIT_COMMIT_SHA, RAILWAY_DEPLOYMENT_ID)
/// y de la fecha en que se construyo el ensamblado. Permite al operador verificar que el pod que
/// esta viendo es el ultimo deploy y no una version en cache o un contenedor previo.
/// </summary>
public static class AppVersion
{
    /// <summary>Version semantica del producto (MAJOR.MINOR.PATCH). Se incrementa manualmente en commits
    /// que cierran un hito funcional. Es el numero que ve el usuario en login y en el sidebar.</summary>
    public const string SemanticVersion = "0.3.2";

    /// <summary>SHA corto del commit desplegado (7 chars). "dev" si la variable no esta definida (local).</summary>
    public static string ShortSha { get; } = ReadShortSha();

    /// <summary>SHA completo del commit desplegado. Vacio si no aplica.</summary>
    public static string FullSha { get; } = Environment.GetEnvironmentVariable("RAILWAY_GIT_COMMIT_SHA") ?? "";

    /// <summary>ID del deploy en Railway (util para correlacionar con el dashboard). Vacio en local.</summary>
    public static string DeploymentId { get; } = Environment.GetEnvironmentVariable("RAILWAY_DEPLOYMENT_ID") ?? "";

    /// <summary>Timestamp UTC en que arranco este proceso. Aproxima "hora del deploy" en el contenedor.</summary>
    public static DateTimeOffset StartedAtUtc { get; } = DateTimeOffset.UtcNow;

    /// <summary>Etiqueta corta "v0.1.0" para footer de login (sin SHA, solo semver).</summary>
    public static string DisplaySemver => "v" + SemanticVersion;

    /// <summary>Etiqueta completa "v0.1.0 · bb28e92" para footer del sidebar (semver + sha).</summary>
    public static string Display => $"v{SemanticVersion} · {ShortSha}";

    private static string ReadShortSha()
    {
        var sha = Environment.GetEnvironmentVariable("RAILWAY_GIT_COMMIT_SHA");
        if (string.IsNullOrWhiteSpace(sha)) { return "dev"; }
        return sha.Length >= 7 ? sha[..7] : sha;
    }
}
