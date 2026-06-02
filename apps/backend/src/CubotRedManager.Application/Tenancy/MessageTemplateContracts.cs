namespace CubotRedManager.Application.Tenancy;

public sealed record MessageTemplateDto(
    Guid Id,
    string Name,
    string Body,
    string? Tags,
    DateTimeOffset UpdatedAt,
    DateTimeOffset CreatedAt);

public sealed record SaveMessageTemplateRequest(
    Guid? Id,
    string Name,
    string Body,
    string? Tags);

/// <summary>
/// Banco de plantillas de mensajes reutilizables del tenant. Sirve para acelerar el copy
/// de publicaciones, respuestas de bandeja y autorespuesta (Modulo Plantillas, Configuracion).
/// </summary>
public interface IMessageTemplateService
{
    Task<IReadOnlyList<MessageTemplateDto>> ListAsync(string? search = null, CancellationToken ct = default);
    Task<MessageTemplateDto?> SaveAsync(SaveMessageTemplateRequest request, Guid actorUserId, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid id, Guid actorUserId, CancellationToken ct = default);
}
