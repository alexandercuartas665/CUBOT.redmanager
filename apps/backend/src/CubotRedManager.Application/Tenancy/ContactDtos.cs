namespace CubotRedManager.Application.Tenancy;

// === Lectura ========================================================================

public sealed record ContactDto(
    Guid Id,
    string Name,
    string Phone,
    string? Email,
    string? Country,
    string? City,
    DateOnly? BirthDate,
    Guid? LeadId,
    Guid? ConversationId,
    DateTimeOffset? FirstOrderAt,
    DateTimeOffset? LastOrderAt,
    int OrderCount,
    decimal? LifetimeValue,
    string? LifetimeValueCurrency,
    int? CadenceDays,
    DateTimeOffset? NextExpectedAt,
    string? Notes,
    bool Archived);

/// <summary>Ficha detalle: el contacto + su historial completo de ordenes en orden cronologico
/// descendente (lo mas reciente primero).</summary>
public sealed record ContactDetailDto(ContactDto Contact, IReadOnlyList<ContactOrderDto> Orders);

public sealed record ContactOrderDto(
    Guid Id,
    Guid ContactId,
    DateTimeOffset OrderedAt,
    decimal? TotalAmount,
    string? Currency,
    string? ItemsJson,
    string Channel,
    string? ExternalRef,
    string? Notes,
    DateTimeOffset CreatedAt);

// === Mutaciones ======================================================================

public sealed record CreateContactRequest(
    string Name,
    string Phone,
    string? Email = null,
    string? Country = null,
    string? City = null,
    DateOnly? BirthDate = null,
    Guid? LeadId = null,
    Guid? ConversationId = null,
    string? Notes = null);

public sealed record UpdateContactRequest(
    string? Name = null,
    string? Email = null,
    string? Country = null,
    string? City = null,
    DateOnly? BirthDate = null,
    string? Notes = null,
    bool? Archived = null);

public sealed record CreateContactOrderRequest(
    DateTimeOffset OrderedAt,
    decimal? TotalAmount = null,
    string? Currency = null,
    /// <summary>JSON array de {name, qty, unitPrice?}. La UI lo arma antes de enviar; aqui se
    /// guarda tal cual. Se valida JSON basico (parsea) pero no el schema de items.</summary>
    string? ItemsJson = null,
    string Channel = "Manual",
    string? ExternalRef = null,
    string? Notes = null);
