using CubotRedManager.Domain.Common;

namespace CubotRedManager.Domain.Entities;

/// <summary>
/// Vincula un AiAgent a una WhatsAppLine para que el agente "atienda" automaticamente
/// los mensajes entrantes de esa linea. Tenant-scoped. La restriccion es: una linea solo
/// puede tener UN agente activo a la vez (indice unico filtrado por IsConnected=true).
/// Multiples agentes pueden compartir la misma linea como "pendientes/inactivos" para
/// tener historial de reasignaciones sin borrar el vinculo anterior.
/// Portado 1:1 desde CUBOT.travels (AiAgentLineBinding).
/// </summary>
public class AiAgentLineBinding : TenantEntity
{
    public Guid AgentId { get; set; }
    public AiAgent? Agent { get; set; }

    public Guid WhatsAppLineId { get; set; }
    public WhatsAppLine? WhatsAppLine { get; set; }

    /// <summary>Cuando es true el agente recibe los mensajes entrantes de la linea y los
    /// responde segun su prompt operativo. Falso = vinculo inactivo (queda en la tabla
    /// como historial pero no recibe trafico).</summary>
    public bool IsConnected { get; set; } = true;

    /// <summary>Cuando es true el agente envia la respuesta automaticamente. Cuando es
    /// false la respuesta queda como "sugerencia" para que un asesor humano la apruebe
    /// (no se manda al cliente). Default true segun decision del producto.</summary>
    public bool AutoConfirm { get; set; } = true;
}
