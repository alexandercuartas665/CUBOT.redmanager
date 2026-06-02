namespace CubotRedManager.Application.Abstractions;

/// <summary>
/// Convierte una URL relativa de upload (ej. "/uploads/{tenantId}/{file}") a la ruta absoluta en
/// disco para que la capa de Application pueda leer el archivo sin depender de IWebHostEnvironment.
/// La implementacion vive en la capa Web (envuelve IWebHostEnvironment.WebRootPath).
/// </summary>
public interface IUploadPathResolver
{
    /// <summary>Devuelve la ruta absoluta en disco, o null si la URL no es valida o no existe.</summary>
    string? ResolveFromUrl(string url);
}
