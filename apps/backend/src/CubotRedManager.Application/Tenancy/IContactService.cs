namespace CubotRedManager.Application.Tenancy;

/// <summary>
/// Directorio de contactos consumidores: CRUD del Contacto, historial de ordenes, perfil dinamico
/// (campos configurables por la agencia) y etiquetas. El tenant activo se resuelve del contexto;
/// el HasQueryFilter global garantiza aislamiento. Las metricas derivadas (LTV, cadencia, proxima
/// compra) se recalculan al mutar ordenes; el ciclo de vida y la afinidad de producto se calculan
/// al leer.
/// </summary>
public interface IContactService
{
    /// <summary>Lista contactos. Filtros opcionales: texto (nombre/telefono/email), etapa del
    /// ciclo de vida, etiqueta. <paramref name="take"/> entre 1 y 500.</summary>
    Task<IReadOnlyList<ContactDto>> ListAsync(string? query = null, bool includeArchived = false, int take = 100,
        string? lifecycle = null, string? tag = null, CancellationToken cancellationToken = default);

    Task<ContactDetailDto?> GetAsync(Guid contactId, CancellationToken cancellationToken = default);

    /// <summary>Crea un contacto. Devuelve null si ya existe el telefono en el tenant.</summary>
    Task<ContactDto?> CreateAsync(CreateContactRequest request, Guid actorUserId, CancellationToken cancellationToken = default);

    /// <summary>Upsert idempotente por telefono: si existe solo rellena campos vacios.</summary>
    Task<ContactDto> UpsertByPhoneAsync(CreateContactRequest request, Guid actorUserId, CancellationToken cancellationToken = default);

    Task<ContactDto?> UpdateAsync(Guid contactId, UpdateContactRequest request, Guid actorUserId, CancellationToken cancellationToken = default);
    Task<ContactDto?> UpdateProfileAsync(Guid contactId, UpdateContactProfileRequest request, Guid actorUserId, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(Guid contactId, Guid actorUserId, CancellationToken cancellationToken = default);

    /// <summary>Todas las etiquetas usadas en el tenant (para autocompletar y filtrar).</summary>
    Task<IReadOnlyList<string>> ListTagsAsync(CancellationToken cancellationToken = default);

    /// <summary>Crea o enriquece el Contacto de un Lead del pipeline Productos (idempotente por
    /// telefono). No hace nada si el lead no tiene telefono o esta en otro pipeline. Recibe el
    /// tenant explicito porque se invoca desde el agente IA en segundo plano.</summary>
    Task<ContactSyncResult> SyncFromLeadAsync(Guid tenantId, Guid leadId, CancellationToken cancellationToken = default);

    /// <summary>Backfill: sincroniza todos los leads activos del pipeline Productos del tenant actual.</summary>
    Task<ContactBackfillResult> SyncAllFromLeadsAsync(CancellationToken cancellationToken = default);

    // === Ordenes ===

    Task<IReadOnlyList<ContactOrderDto>> ListOrdersAsync(Guid contactId, CancellationToken cancellationToken = default);
    Task<ContactOrderDto?> AddOrderAsync(Guid contactId, CreateContactOrderRequest request, Guid actorUserId, CancellationToken cancellationToken = default);
    Task<bool> DeleteOrderAsync(Guid contactId, Guid orderId, Guid actorUserId, CancellationToken cancellationToken = default);

    // === Campos del perfil ===

    /// <summary>Lista los campos del perfil del tenant. Si el tenant no tiene ninguno, siembra
    /// primero los presets de marketing.</summary>
    Task<IReadOnlyList<ContactFieldDto>> ListFieldsAsync(bool includeHidden = true, CancellationToken cancellationToken = default);
    Task<ContactFieldDto?> CreateFieldAsync(SaveContactFieldRequest request, Guid actorUserId, CancellationToken cancellationToken = default);
    Task<ContactFieldDto?> UpdateFieldAsync(Guid fieldId, SaveContactFieldRequest request, Guid actorUserId, CancellationToken cancellationToken = default);
    /// <summary>Borra un campo propio. Los presets (IsSystem) no se borran: se ocultan.</summary>
    Task<bool> DeleteFieldAsync(Guid fieldId, Guid actorUserId, CancellationToken cancellationToken = default);
    /// <summary>Mueve el campo una posicion arriba (-1) o abajo (+1) dentro de su seccion.</summary>
    Task<bool> MoveFieldAsync(Guid fieldId, int direction, Guid actorUserId, CancellationToken cancellationToken = default);

    /// <summary>Telefonos (digitos) que ya existen en el directorio del tenant actual, incluidos
    /// archivados. Sirve para marcar "ya en el directorio" antes de importar.</summary>
    Task<IReadOnlySet<string>> ListPhonesAsync(CancellationToken cancellationToken = default);

    /// <summary>Importacion masiva por telefono (agenda del telefono, archivo, etc.). Crea los que no
    /// existen con la etiqueta dada; a los existentes solo les agrega la etiqueta y completa el nombre
    /// si estaba vacio o era el mismo numero. No marca consentimiento de marketing (Ley 1581).</summary>
    Task<ContactImportResult> ImportAsync(IReadOnlyList<ContactImportItem> items, string? tag, Guid actorUserId, CancellationToken cancellationToken = default);
}
