using CubotRedManager.Domain.Enums;

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
    bool Archived,
    /// <summary>Etapa del ciclo de vida calculada al vuelo (depende de la fecha actual):
    /// Prospecto, Nuevo, Activo, Fiel, En riesgo, Dormido, Perdido.</summary>
    string Lifecycle,
    IReadOnlyList<string> Tags,
    IReadOnlyDictionary<string, string?> FieldValues);

/// <summary>Afinidad de producto calculada desde el historial de ordenes: que consume el
/// cliente, cuantas veces, y cada cuanto (por producto). Base de los recordatorios segmentados.</summary>
public sealed record ContactProductAffinityDto(
    string Product,
    int TimesBought,
    decimal TotalQty,
    DateTimeOffset FirstBoughtAt,
    DateTimeOffset LastBoughtAt,
    int? AvgDaysBetween,
    DateTimeOffset? NextExpectedAt);

/// <summary>Ficha detalle: contacto + historial de ordenes (desc) + afinidad por producto.</summary>
public sealed record ContactDetailDto(
    ContactDto Contact,
    IReadOnlyList<ContactOrderDto> Orders,
    IReadOnlyList<ContactProductAffinityDto> Products);

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

public sealed record ContactFieldDto(
    Guid Id,
    string Section,
    string FieldKey,
    string Label,
    ContactFieldType FieldType,
    string? Options,
    string? Description,
    int Column,
    int SortOrder,
    bool ShowInList,
    bool ShowInFilter,
    bool IsSystem,
    bool IsHidden);

/// <summary>Resultado de sincronizar un Lead con el directorio. ContactId null = no aplica
/// (sin telefono o pipeline distinto de Productos).</summary>
public sealed record ContactSyncResult(Guid? ContactId, bool Created);

public sealed record ContactBackfillResult(int LeadsReviewed, int Created, int Linked, int Skipped);

/// <summary>Fila a importar (ej. agenda del telefono conectado por Evolution). Phone con indicativo.</summary>
public sealed record ContactImportItem(string Phone, string? Name);

/// <summary>Resultado de una importacion masiva. Updated = ya existia y se le agrego la etiqueta
/// y/o el nombre (si solo tenia el numero). Invalid = numero no utilizable.</summary>
public sealed record ContactImportResult(int Received, int Created, int Updated, int Unchanged, int Invalid);

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

/// <summary>Actualiza el perfil dinamico. <paramref name="FieldValues"/> se FUSIONA con lo
/// existente (llave con valor null o "" la borra). <paramref name="Tags"/> null = no tocar;
/// lista (aunque vacia) = reemplazar.</summary>
public sealed record UpdateContactProfileRequest(
    Dictionary<string, string?>? FieldValues = null,
    IReadOnlyList<string>? Tags = null);

public sealed record CreateContactOrderRequest(
    DateTimeOffset OrderedAt,
    decimal? TotalAmount = null,
    string? Currency = null,
    /// <summary>JSON array de {name, qty, unitPrice?}. Se valida que parsee.</summary>
    string? ItemsJson = null,
    string Channel = "Manual",
    string? ExternalRef = null,
    string? Notes = null);

public sealed record SaveContactFieldRequest(
    string Section,
    string Label,
    ContactFieldType FieldType,
    string? Options = null,
    string? Description = null,
    int Column = 1,
    bool ShowInList = false,
    bool ShowInFilter = false,
    bool IsHidden = false);
