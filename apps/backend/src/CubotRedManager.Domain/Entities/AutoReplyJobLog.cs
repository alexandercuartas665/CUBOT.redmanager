using CubotRedManager.Domain.Common;
using CubotRedManager.Domain.Enums;

namespace CubotRedManager.Domain.Entities;

/// <summary>
/// Log inmutable de una ejecucion del job de autorespuesta (Modulo 2.11). Solo INSERT.
/// Retencion sugerida: 12 meses. Tenant-scoped.
/// </summary>
public class AutoReplyJobLog : TenantEntity
{
    public Guid SocialAccountId { get; set; }
    public SocialAccount? SocialAccount { get; set; }

    public AutoReplyJobStatus Status { get; set; } = AutoReplyJobStatus.Ok;

    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;
    public int DurationMs { get; set; }

    public int Processed { get; set; }
    public int Replied { get; set; }
    public int Errors { get; set; }
    public int Omitted { get; set; }

    /// <summary>Traza linea por linea (concatenada). Util para debugging desde la UI.</summary>
    public string? Trace { get; set; }
}
