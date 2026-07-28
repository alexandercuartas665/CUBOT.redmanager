namespace CubotRedManager.Application.Tenancy;

/// <summary>
/// Sincroniza la columna Precio del DataContainer configurado en Pagos FUXION del agente contra el
/// catalogo real del portal FUXION (endpoint /api/products?country=XX). Se invoca desde el boton
/// "Sincronizar precios ahora" de la pagina /agentes o desde el endpoint API. No lanza excepciones
/// al caller: cualquier fallo (token expirado, catalogo inaccesible, columna faltante) viene en el
/// resultado con detalle humano.
/// </summary>
public interface IPriceSyncService
{
    Task<PriceSyncResult> SyncPricesAsync(Guid agentId, Guid actorUserId, CancellationToken cancellationToken = default);
}

public sealed record PriceSyncResult(
    bool Ok,
    int RowsChecked,        // filas del contenedor con IdProducto + pais reconocido
    int RowsUpdated,        // filas donde el precio cambio y se PATCHeo
    int RowsAlreadyOk,      // filas donde el precio ya coincidia
    int RowsSkipped,        // filas sin IdProducto o sin pais o sin match en el catalogo FUXION
    IReadOnlyList<string> Errors,           // fallos por pais (ej. token rechazado en co)
    DateTimeOffset? SyncedAt);              // timestamp que quedo persistido en el agente
