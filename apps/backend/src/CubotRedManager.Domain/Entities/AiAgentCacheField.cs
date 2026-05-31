using CubotRedManager.Domain.Common;

namespace CubotRedManager.Domain.Entities;

/// <summary>
/// Definicion de un dato que el agente debe capturar durante la conversacion (capa 3, datos cache).
/// Entidad TENANT-SCOPED. Ej.: "marca", "producto", "ciudad". El motor de inferencia intenta llenarlo
/// a partir de los mensajes.
/// </summary>
public class AiAgentCacheField : TenantEntity
{
    public Guid AgentId { get; set; }
    public AiAgent? Agent { get; set; }

    /// <summary>Clave del dato (slug derivado del nombre). Unica por agente.</summary>
    public string FieldKey { get; set; } = null!;

    /// <summary>Nombre visible del dato (ej. "Marca").</summary>
    public string Label { get; set; } = null!;

    /// <summary>De que trata el dato; le sirve al motor para extraerlo del texto.</summary>
    public string? Description { get; set; }

    public int SortOrder { get; set; }

    /// <summary>
    /// Si esta en true, el motor de inferencia puede sobrescribir el valor durante la conversacion.
    /// Si esta en false, una vez capturado queda fijo y no se actualiza.
    /// </summary>
    public bool IsUpdatable { get; set; } = true;
}
