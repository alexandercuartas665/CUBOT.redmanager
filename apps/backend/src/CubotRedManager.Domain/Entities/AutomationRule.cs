using CubotRedManager.Domain.Common;
using CubotRedManager.Domain.Enums;

namespace CubotRedManager.Domain.Entities;

/// <summary>
/// Regla de automatizacion (Modulo Automatizaciones). Tenant-scoped. El motor evalua los
/// triggers cada N minutos y dispara la accion si la condicion se cumple.
/// </summary>
public class AutomationRule : TenantEntity
{
    public string Name { get; set; } = "";
    public AutomationTrigger Trigger { get; set; }
    public AutomationAction Action { get; set; }
    public bool IsActive { get; set; }
    /// <summary>Minutos sin respuesta para gatillar NoReply.</summary>
    public int? NoReplyMinutes { get; set; }
    /// <summary>Parametros adicionales JSON (free-form para no requerir migraciones por cada nuevo parametro).</summary>
    public string? ParametersJson { get; set; }
    public int ExecutionCount { get; set; }
    public DateTimeOffset? LastExecutedAt { get; set; }
}
