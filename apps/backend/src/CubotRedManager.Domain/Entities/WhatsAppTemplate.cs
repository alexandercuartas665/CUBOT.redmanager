using CubotRedManager.Domain.Common;
using CubotRedManager.Domain.Enums;

namespace CubotRedManager.Domain.Entities;

/// <summary>
/// Plantilla HSM de WhatsApp (mensaje plantilla aprobado por Meta). Entidad TENANT-SCOPED.
/// Distinta de <see cref="MessageTemplate"/> (pregrabados internos de texto libre): estas se
/// someten a revision de Meta a traves del proveedor (YCloud/Cloud), tienen categoria, idioma,
/// variables y un ciclo de aprobacion (Draft -> Submitted -> Approved/Rejected).
///
/// El <see cref="BodyText"/> se guarda en formato EDITABLE con tokens amigables (ej.
/// "Hola {{cliente}}, tu cita para {{servicio}}..."). Al someter, el servicio lo compila a los
/// placeholders posicionales de Meta ({{1}}, {{2}}, ...) y manda los valores de ejemplo.
/// </summary>
public class WhatsAppTemplate : TenantEntity
{
    /// <summary>Nombre tecnico de la plantilla en Meta (minusculas, guion_bajo). Unico por tenant+idioma.</summary>
    public string Name { get; set; } = null!;

    /// <summary>Codigo de idioma de Meta (ej. "es", "es_CO", "en_US").</summary>
    public string Language { get; set; } = "es";

    /// <summary>Categoria de Meta: MARKETING, UTILITY o AUTHENTICATION.</summary>
    public string Category { get; set; } = "UTILITY";

    /// <summary>Tipo de header: null/"NONE" o "TEXT" (en v1 solo texto; imagen/video/doc despues).</summary>
    public string? HeaderType { get; set; }
    public string? HeaderText { get; set; }

    /// <summary>Cuerpo editable con tokens amigables {{empresa}}, {{cliente}}, {{servicio}}, etc.</summary>
    public string BodyText { get; set; } = null!;

    public string? FooterText { get; set; }

    /// <summary>JSON con las variables usadas y su ejemplo: [{ "token": "cliente", "example": "Juan" }].</summary>
    public string VariablesJson { get; set; } = "[]";

    // === Proveedor / destino del submit ======================================
    public WhatsAppProvider Provider { get; set; } = WhatsAppProvider.YCloud;
    /// <summary>Linea por la que se somete (define la WABA y las credenciales).</summary>
    public Guid? WhatsAppLineId { get; set; }
    public string? WabaId { get; set; }

    // === Estado de aprobacion ================================================
    /// <summary>Draft / Submitted / Approved / Rejected / Paused / Disabled.</summary>
    public string Status { get; set; } = "Draft";
    /// <summary>Id de la plantilla en el proveedor/Meta (tras someter).</summary>
    public string? ProviderTemplateId { get; set; }
    public string? RejectionReason { get; set; }
    public DateTimeOffset? SubmittedAt { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }
}
