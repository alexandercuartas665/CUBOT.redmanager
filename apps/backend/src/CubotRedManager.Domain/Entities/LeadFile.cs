using CubotRedManager.Domain.Common;

namespace CubotRedManager.Domain.Entities;

/// <summary>Documento adjunto a un lead. Tenant-scoped. El binario vive como bytea en la BD
/// (patron Railway-safe heredado de PublicationMedia / AiAgentResource — filesystem efimero).</summary>
public class LeadFile : TenantEntity
{
    public Guid LeadId { get; set; }
    public Lead? Lead { get; set; }

    /// <summary>Nombre original del archivo subido.</summary>
    public string FileName { get; set; } = null!;

    public string ContentType { get; set; } = "application/octet-stream";
    public long SizeBytes { get; set; }

    /// <summary>Contenido binario del archivo. Servido via GET /api/leads/files/{id}.</summary>
    public byte[] Content { get; set; } = Array.Empty<byte>();
}
