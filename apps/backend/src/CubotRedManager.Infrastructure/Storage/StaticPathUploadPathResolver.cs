using CubotRedManager.Application.Abstractions;

namespace CubotRedManager.Infrastructure.Storage;

/// <summary>
/// Resuelve URLs /uploads/{tenantId}/{file} a rutas absolutas dentro de un webRoot pasado por
/// constructor. Valida que la ruta resuelta este DENTRO de {webRoot}/uploads (defensa contra
/// path traversal). Esta implementacion vive en Infrastructure para que pueda registrarse por
/// AddInfrastructure y sirva a ambas consolas (Web y SuperAdmin) sin acoplar Infrastructure a
/// IWebHostEnvironment.
/// </summary>
public sealed class StaticPathUploadPathResolver : IUploadPathResolver
{
    private readonly string _webRoot;
    public StaticPathUploadPathResolver(string webRoot) => _webRoot = webRoot;

    public string? ResolveFromUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url) || !url.StartsWith("/uploads/", StringComparison.OrdinalIgnoreCase)) { return null; }
        var rel = url.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
        var uploadsRoot = Path.GetFullPath(Path.Combine(_webRoot, "uploads"));
        var fullPath = Path.GetFullPath(Path.Combine(_webRoot, rel));
        // Defensa contra path traversal (../).
        if (!fullPath.StartsWith(uploadsRoot, StringComparison.OrdinalIgnoreCase)) { return null; }
        return fullPath;
    }
}
