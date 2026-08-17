using CubotRedManager.Domain.Common;

namespace CubotRedManager.Domain.Entities;

/// <summary>
/// Archivo adjunto de una Publicacion, persistido en BD como bytea. Tenant-scoped, con cascade
/// delete desde Publication. Motivacion: Railway monta /app read-only para el proceso .NET —
/// escribir a /app/wwwroot/uploads lanza UnauthorizedAccessException. Mismo patron que
/// AiAgentResource.FileContent para consistencia.
///
/// El endpoint GET /api/publications/media/{id} sirve el binario con Content-Type correcto.
/// El PublicationExecutor lee Content directo de aca para hacer el upload a TikTok — sin disco.
/// </summary>
public class PublicationMedia : TenantEntity
{
    public Guid PublicationId { get; set; }
    public Publication? Publication { get; set; }

    public string FileName { get; set; } = null!;

    /// <summary>MIME resuelto en el upload (video/mp4, video/quicktime, image/png, etc).</summary>
    public string MimeType { get; set; } = null!;

    public long FileSize { get; set; }

    /// <summary>Contenido binario. Se sirve via endpoint /api/publications/media/{id} y se
    /// consume directo desde el executor de TikTok (no hay archivo en disco).</summary>
    public byte[] Content { get; set; } = Array.Empty<byte>();

    public int SortOrder { get; set; }
}
