namespace CubotRedManager.Application.Tenancy;

/// <summary>
/// Directorio de contactos consumidores (modulo Fase 1). Expone CRUD del Contacto + gestion de
/// su historial de ordenes. El tenant activo se resuelve del contexto; el HasQueryFilter global
/// garantiza aislamiento. Las metricas derivadas (LTV, cadencia, proxima compra) se recalculan
/// automaticamente al mutar ordenes — el caller NO necesita setearlas.
/// </summary>
public interface IContactService
{
    /// <summary>Lista contactos activos del tenant. <paramref name="query"/> filtra por nombre,
    /// telefono o email (contains, case-insensitive). <paramref name="includeArchived"/> true
    /// trae tambien los archivados. <paramref name="take"/> entre 1 y 500, default 100.</summary>
    Task<IReadOnlyList<ContactDto>> ListAsync(string? query = null, bool includeArchived = false, int take = 100, CancellationToken cancellationToken = default);

    Task<ContactDetailDto?> GetAsync(Guid contactId, CancellationToken cancellationToken = default);

    /// <summary>Crea un contacto nuevo. Devuelve null si ya existe un contacto con el mismo
    /// (TenantId, Phone); el caller debe usar GetByPhoneAsync si quiere comportamiento upsert.</summary>
    Task<ContactDto?> CreateAsync(CreateContactRequest request, Guid actorUserId, CancellationToken cancellationToken = default);

    /// <summary>Upsert por telefono: si existe, devuelve el existente sin modificarlo; si no,
    /// lo crea. Pensado para el hook del LeadMarkerProcessor (PR L4) que debe ser idempotente.</summary>
    Task<ContactDto> UpsertByPhoneAsync(CreateContactRequest request, Guid actorUserId, CancellationToken cancellationToken = default);

    Task<ContactDto?> UpdateAsync(Guid contactId, UpdateContactRequest request, Guid actorUserId, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(Guid contactId, Guid actorUserId, CancellationToken cancellationToken = default);

    // === Ordenes ===

    Task<IReadOnlyList<ContactOrderDto>> ListOrdersAsync(Guid contactId, CancellationToken cancellationToken = default);
    Task<ContactOrderDto?> AddOrderAsync(Guid contactId, CreateContactOrderRequest request, Guid actorUserId, CancellationToken cancellationToken = default);
    Task<bool> DeleteOrderAsync(Guid contactId, Guid orderId, Guid actorUserId, CancellationToken cancellationToken = default);
}
