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

    /// <summary>Catalogo de variables de sesion que el editor puede insertar.</summary>
    IReadOnlyList<TemplateVariableDef> Catalog();
}
