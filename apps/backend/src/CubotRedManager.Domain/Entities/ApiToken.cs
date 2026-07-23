using CubotRedManager.Domain.Common;

namespace CubotRedManager.Domain.Entities;

/// <summary>
/// Token de API opaco emitido por un usuario del tenant para uso programatico. Solo se guarda
/// el HASH (SHA256) del token, nunca el texto plano. El texto plano se muestra UNA sola vez en
/// la UI al crearse y despues ya no se puede recuperar.
///
/// Uso: el cliente envia el token en el header X-Api-Token; los endpoints /api/* validan contra
/// esta tabla y setean el ambient tenant scope segun TenantId + UserId de la fila.
///
/// Revocacion: RevokedAt no-null invalida el token. Expiracion via ExpiresAt.
/// </summary>
public class ApiToken : TenantEntity
{
    /// <summary>Usuario que emitio el token. Sus permisos aplican a las llamadas hechas con el.</summary>
    public Guid UserId { get; set; }

    /// <summary>Nombre humano para identificar el token en la UI (ej. "Actualizar catalogo FUXION").</summary>
    public string Label { get; set; } = null!;

    /// <summary>SHA256 hex del token en plain. Nunca se guarda el token plano.</summary>
    public string TokenHash { get; set; } = null!;

    /// <summary>Primeros 6 caracteres del token en plain para identificarlo en la UI (ej. "vk7A2b...").
    /// No permite reconstruir el token; solo ayudar al operador a distinguir cual es cual.</summary>
    public string TokenPrefix { get; set; } = null!;

    public DateTimeOffset ExpiresAt { get; set; }

    public DateTimeOffset? LastUsedAt { get; set; }

    public DateTimeOffset? RevokedAt { get; set; }
}
