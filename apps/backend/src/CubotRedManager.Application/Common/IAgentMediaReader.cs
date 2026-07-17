namespace CubotRedManager.Application.Common;

/// <summary>Contenido binario de un recurso del agente, listo para enviar via SendMediaAsync.</summary>
public sealed record AgentMediaContent(string Base64, string? MimeType, string? FileName);

/// <summary>
/// Lee archivos persistidos como recursos del agente desde su FileUrl logico (ej.
/// /uploads/agents/agent-xxxxx.png) y devuelve los bytes en base64. Portado desde CUBOT.travels.
/// </summary>
public interface IAgentMediaReader
{
    Task<AgentMediaContent?> TryReadAsync(string? fileUrl, CancellationToken cancellationToken = default);
}

/// <summary>Implementacion por defecto que NO lee nada. El host puede reemplazarla por una real.</summary>
public sealed class NoOpAgentMediaReader : IAgentMediaReader
{
    public Task<AgentMediaContent?> TryReadAsync(string? fileUrl, CancellationToken cancellationToken = default)
        => Task.FromResult<AgentMediaContent?>(null);
}
