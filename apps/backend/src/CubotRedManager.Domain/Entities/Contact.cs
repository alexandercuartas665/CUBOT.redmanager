using CubotRedManager.Domain.Common;

namespace CubotRedManager.Domain.Entities;

/// <summary>
/// Contacto consumidor del directorio. Entidad TENANT-SCOPED. Representa a un cliente que
/// consume productos FUXION (u otro catalogo del tenant) de forma recurrente. Se diferencia
/// del Lead (que vive en el pipeline y cierra) en que el Contacto es la ficha permanente del
/// cliente: historial de compras, cadencia, proxima compra proyectada, datos personales y
/// base para recordatorios por correo/WhatsApp.
///
/// La creacion puede ser manual o automatica cuando se crea un Lead en el pipeline "Productos"
/// (hook del LeadMarkerProcessor, PR L4). Un mismo telefono NUNCA puede duplicarse dentro del
/// mismo tenant (unique index TenantId, Phone).
///
/// Los campos derivados (FirstOrderAt, LastOrderAt, OrderCount, LifetimeValue, CadenceDays,
/// NextExpectedAt) se recalculan en el service cada vez que se agrega/borra una orden; no se
/// escriben a mano via API para evitar que queden desincronizados del historial real.
/// </summary>
public class Contact : TenantEntity
{
    public string Name { get; set; } = null!;

    /// <summary>Numero internacional sin "+". Es el identificador funcional del contacto.</summary>
    public string Phone { get; set; } = null!;

    public string? Email { get; set; }
    public string? Country { get; set; }
    public string? City { get; set; }
    public DateOnly? BirthDate { get; set; }

    /// <summary>Vinculo opcional al Lead del pipeline Productos que origino este Contacto.
    /// Puede ser null si se creo manualmente o si el Lead se archivo despues.</summary>
    public Guid? LeadId { get; set; }

    /// <summary>Vinculo opcional a la conversacion WhatsApp mas reciente del contacto. Usado
    /// para abrir el chat desde la ficha y para saber si estamos dentro de la ventana de 24h.</summary>
    public Guid? ConversationId { get; set; }

    // === Metricas derivadas (recalculadas en el service al mutar ordenes) =====
    public DateTimeOffset? FirstOrderAt { get; set; }
    public DateTimeOffset? LastOrderAt { get; set; }
    public int OrderCount { get; set; }

    /// <summary>Suma total de todas las ordenes (en la misma moneda). Si el contacto tiene
    /// ordenes en varias monedas distintas, LifetimeValue queda null porque no tiene sentido
    /// sumar peras con manzanas; en ese caso la UI muestra el breakdown por moneda.</summary>
    public decimal? LifetimeValue { get; set; }
    public string? LifetimeValueCurrency { get; set; }

    /// <summary>Promedio de dias entre ordenes consecutivas (ventana completa). Null si hay
    /// menos de 2 ordenes. Se usa para proyectar NextExpectedAt y detectar "clientes dormidos"
    /// (LastOrderAt + CadenceDays * 1.5 ya paso y no han vuelto a comprar).</summary>
    public int? CadenceDays { get; set; }

    /// <summary>Proxima compra proyectada = LastOrderAt + CadenceDays. Null si no se puede
    /// proyectar (menos de 2 ordenes). Base para recordatorios proactivos.</summary>
    public DateTimeOffset? NextExpectedAt { get; set; }

    /// <summary>Notas libres del lider sobre el contacto (alergias, preferencias, meta de salud,
    /// contexto familiar). Visible solo en la ficha.</summary>
    public string? Notes { get; set; }

    /// <summary>Contacto archivado (oculto del directorio activo). Ejemplo: baja definitiva.
    /// No se borra fisicamente para preservar historial de ordenes.</summary>
    public DateTimeOffset? ArchivedAt { get; set; }

    /// <summary>Valores del perfil dinamico (jsonb): { fieldKey: valor }. Las llaves corresponden
    /// a ContactFieldDefinition.FieldKey del tenant.</summary>
    public string? FieldValuesJson { get; set; }

    /// <summary>Etiquetas libres para segmentar campanas (ej. "vip", "cumple-diciembre"),
    /// separadas por coma, normalizadas en minusculas.</summary>
    public string? Tags { get; set; }
}
