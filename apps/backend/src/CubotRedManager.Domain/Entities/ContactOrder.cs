using CubotRedManager.Domain.Common;

namespace CubotRedManager.Domain.Entities;

/// <summary>
/// Orden de compra de un Contacto. Entidad TENANT-SCOPED. En Fase 1 se registra manualmente
/// desde la UI (la lider anota "Pedro compro OFF x1 el 15 nov por $151.900 COP"). En Fase 2
/// se creara tambien automaticamente al sincronizar con la API de FUXION.
///
/// El detalle de productos va serializado en ItemsJson como un array de objetos
/// {name, qty, unitPrice?}. Se usa string en vez de una tabla aparte porque el catalogo
/// FUXION es dinamico y las ordenes son historicas (no necesitamos FK a Product); simplifica
/// la ingesta desde FUXION cuando llegue (su API retorna los items como JSON).
/// </summary>
public class ContactOrder : TenantEntity
{
    public Guid ContactId { get; set; }

    /// <summary>Fecha efectiva de la compra. La lider la elige al registrar (puede ser retroactiva
    /// si esta metiendo el historial viejo). NO es CreatedAt (ese registra cuando se dio de alta
    /// en nuestro sistema). Importante para calculos de cadencia.</summary>
    public DateTimeOffset OrderedAt { get; set; }

    /// <summary>Monto total de la orden. Puede ser null si la orden es cualitativa (ej. muestra).</summary>
    public decimal? TotalAmount { get; set; }

    /// <summary>ISO 4217 (COP, USD, PEN, etc). Obligatorio si TotalAmount tiene valor.</summary>
    public string? Currency { get; set; }

    /// <summary>Items de la orden como JSON: [{"name":"OFF","qty":1,"unitPrice":151900}, ...].
    /// No es tabla aparte porque el catalogo es externo (FUXION) y las lineas son historicas.
    /// Max 4000 chars cubre ~40 productos en una orden tipica.</summary>
    public string? ItemsJson { get; set; }

    /// <summary>De donde vino la orden: Manual (registrada por la lider), Fuxion (sync API),
    /// LinkPago (creada automatica tras [[link_pago]] exitoso), Other.</summary>
    public string Channel { get; set; } = "Manual";

    /// <summary>Referencia externa opcional: id de la orden en FUXION, numero de factura,
    /// wamid del mensaje donde el cliente confirmo pago, etc. Sirve para idempotencia al
    /// sincronizar con FUXION API (evitar crear la misma orden dos veces).</summary>
    public string? ExternalRef { get; set; }

    /// <summary>Observaciones del lider (envio, dificultades del cliente, feedback del producto).</summary>
    public string? Notes { get; set; }
}
