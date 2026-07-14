using CubotRedManager.Domain.Common;

namespace CubotRedManager.Domain.Entities;

/// <summary>
/// Numero de telefono en la lista negra GLOBAL del tenant (agencia): ningun agente de IA le responde.
/// Es compartida por todos los agentes, por eso vive en su propio modulo y no en cada agente. La
/// comparacion en el dispatcher es por digitos (ignora "+", espacios y el codigo de pais sobrante).
/// Portado 1:1 desde CUBOT.travels.
/// </summary>
public class TenantBlockedNumber : TenantEntity
{
    /// <summary>Telefono normalizado a solo digitos.</summary>
    public string Phone { get; set; } = null!;

    /// <summary>Nota opcional: motivo, o como se agrego (ej. comando Manejo_asesor).</summary>
    public string? Note { get; set; }
}
