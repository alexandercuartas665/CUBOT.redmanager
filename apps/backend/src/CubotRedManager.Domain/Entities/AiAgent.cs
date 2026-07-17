using CubotRedManager.Domain.Common;
using CubotRedManager.Domain.Enums;

namespace CubotRedManager.Domain.Entities;

/// <summary>
/// Agente de IA configurable de la agencia (capa 3). Entidad TENANT-SCOPED. Define proveedor,
/// modelo, prompt de sistema y si esta en produccion. Los recursos (AiAgentResource) son los
/// archivos/datos que el agente puede usar para responder.
/// </summary>
public class AiAgent : TenantEntity
{
    public string Name { get; set; } = null!;

    /// <summary>Rol/tipo descriptivo (copywriter, bandeja, analista, etc.). Libre.</summary>
    public string? Role { get; set; }

    public AiProvider Provider { get; set; } = AiProvider.Claude;

    /// <summary>Modelo concreto del proveedor (opcional; si vacio se usa el por defecto).</summary>
    public string? Model { get; set; }

    public string SystemPrompt { get; set; } = "";

    /// <summary>En produccion (encendido) o apagado.</summary>
    public bool IsActive { get; set; }

    /// <summary>
    /// MCP de Contenedores de Datos: si esta habilitado, el agente puede leer los DataContainers
    /// del tenant via placeholders {{LIST.CONTAINERS}} y {{CONTAINER:nombre}} en su prompt. Por
    /// defecto FALSO (opt-in explicito por agente para no exponer datos sin querer).
    /// </summary>
    public bool EnableDataContainerMcp { get; set; }

    /// <summary>
    /// Reacciones automaticas (emoji) a los mensajes del cliente, SIN pasar por el LLM (cero
    /// tokens). Si esta activo, el dispatcher reacciona a ~N de cada M mensajes entrantes con
    /// un emoji elegido al azar de ReactionEmojis. Un mensaje que ya tiene reaccion no recibe
    /// otra. Portado desde CUBOT.travels.
    /// </summary>
    public bool ReactionsEnabled { get; set; }

    /// <summary>Numerador de la frecuencia (ej. 3 de cada 4 -> N=3). Se aplica como probabilidad.</summary>
    public int ReactionRatioN { get; set; } = 3;

    /// <summary>Denominador de la frecuencia (ej. 3 de cada 4 -> M=4).</summary>
    public int ReactionRatioM { get; set; } = 4;

    /// <summary>Emojis para reaccionar al azar, separados por coma. Configurable por UI.</summary>
    public string? ReactionEmojis { get; set; }

    public int SortOrder { get; set; }
}
