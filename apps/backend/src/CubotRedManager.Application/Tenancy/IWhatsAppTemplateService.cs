namespace CubotRedManager.Application.Tenancy;

/// <summary>
/// Servicio del modulo de plantillas HSM de WhatsApp (tenant-scoped). Crea borradores, los somete
/// a Meta a traves del proveedor (YCloud/Cloud) y sincroniza su estado de aprobacion.
/// </summary>
public interface IWhatsAppTemplateService
{
    Task<IReadOnlyList<WhatsAppTemplateDto>> ListAsync(CancellationToken cancellationToken = default);
    Task<WhatsAppTemplateDto?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Crea un borrador (Status = Draft). No contacta al proveedor.</summary>
    Task<WhatsAppTemplateDto?> CreateDraftAsync(SaveWhatsAppTemplateRequest request, Guid actorUserId, CancellationToken cancellationToken = default);

    /// <summary>Actualiza un borrador. Solo se permite editar mientras esta en Draft o Rejected.</summary>
    Task<WhatsAppTemplateDto?> UpdateAsync(Guid id, SaveWhatsAppTemplateRequest request, Guid actorUserId, CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(Guid id, Guid actorUserId, CancellationToken cancellationToken = default);

    /// <summary>Compila el cuerpo (tokens -> {{1}}..) y somete la plantilla al proveedor para
    /// revision de Meta. Pasa a Status = Submitted (o Approved si el proveedor la aprueba en el acto).</summary>
    Task<TemplateSubmitResult> SubmitAsync(Guid id, Guid actorUserId, CancellationToken cancellationToken = default);

    /// <summary>Consulta al proveedor el estado de las plantillas ya sometidas y actualiza las que
    /// cambiaron. Devuelve cuantas se actualizaron.</summary>
    Task<int> SyncStatusAsync(CancellationToken cancellationToken = default);

    /// <summary>Soft-delete: marca la plantilla como Disabled (si active=false) o la rehabilita a
    /// su estado anterior (Draft/Approved) si active=true. No la borra fisicamente, asi que las
    /// metricas y mensajes historicos siguen referenciandola. Devuelve el nuevo Status.</summary>
    Task<string?> SetActiveAsync(Guid id, bool active, Guid actorUserId, CancellationToken cancellationToken = default);

    /// <summary>Envia la plantilla a un numero de prueba via una linea YCloud (o Emulator). Util
    /// desde el editor para validar que se vea como se espera antes de usarla con clientes reales.
    /// <paramref name="values"/> son los valores de las variables EN ORDEN; faltantes = usar ejemplo.
    /// <paramref name="headerMediaUrl"/> opcional cuando la plantilla usa header de media.</summary>
    Task<TemplateTestSendResult> TestSendAsync(Guid id, Guid whatsAppLineId, string phone,
        IReadOnlyList<string>? values, string? headerMediaUrl, Guid actorUserId,
        CancellationToken cancellationToken = default);

    /// <summary>Importa (upsert) las plantillas que YCloud ya tiene aprobadas/pendientes para la
    /// WABA de la linea indicada. Util para cargar el catalogo cuando se agrega una linea nueva
    /// sin tener que recrear las plantillas desde la UI.</summary>
    Task<TemplateImportReport> ImportFromYCloudAsync(Guid whatsAppLineId, Guid actorUserId,
        CancellationToken cancellationToken = default);

    /// <summary>Catalogo de variables de sesion que el editor puede insertar.</summary>
    IReadOnlyList<TemplateVariableDef> Catalog();
}

public sealed record TemplateTestSendResult(bool Ok, string? Error, string? MessageId);

public sealed record TemplateImportReport(bool Ok, int Imported, int Updated, int Skipped, string? Error);
