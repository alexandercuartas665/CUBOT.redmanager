using CubotRedManager.Domain.Common;

namespace CubotRedManager.Domain.Entities;

/// <summary>Archivo adjunto a una tarjeta del tablero. TENANT-SCOPED.</summary>
public class TaskCardAttachment : TenantEntity
{
    public Guid TaskCardId { get; set; }
    public TaskCard? TaskCard { get; set; }

    public string FileName { get; set; } = null!;
    public string Url { get; set; } = null!;
    public string? MimeType { get; set; }
    public long SizeBytes { get; set; }

    /// <summary>Quien lo subio (PlatformUser id).</summary>
    public Guid? UploadedBy { get; set; }
    public string? UploadedByName { get; set; }
}
