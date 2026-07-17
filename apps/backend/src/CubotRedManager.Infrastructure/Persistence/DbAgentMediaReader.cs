using CubotRedManager.Application.Common;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;

namespace CubotRedManager.Infrastructure.Persistence;

/// <summary>
/// Lee el binario de un recurso del agente. Soporta dos formatos de FileUrl:
///
/// 1. <c>/api/agent-resources/{id}/file</c> - contenido servido desde la columna
///    <c>ai_agent_resources.file_content</c> (bytea). Este es el formato oficial
///    porque sobrevive a restarts del host (filesystem efimero en Railway).
///
/// 2. <c>/uploads/agents/...</c> - fallback historico: intenta leer desde
///    <c>WebRootPath/uploads/agents/</c>. Solo funciona en instalaciones locales
///    donde el archivo no se ha perdido.
///
/// El dispatcher (Application) solo conoce la interfaz; esta implementacion vive
/// en Infrastructure para poder tocar el DbContext.
/// </summary>
public sealed class DbAgentMediaReader : IAgentMediaReader
{
    private readonly CubotRedManagerDbContext _db;
    private readonly IWebHostEnvironment? _env;

    public DbAgentMediaReader(CubotRedManagerDbContext db, IWebHostEnvironment? env = null)
    {
        _db = db;
        _env = env;
    }

    public async Task<AgentMediaContent?> TryReadAsync(string? fileUrl, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(fileUrl)) { return null; }

        // Formato oficial: /api/agent-resources/{id}/file
        var match = System.Text.RegularExpressions.Regex.Match(
            fileUrl, @"^/api/agent-resources/(?<id>[0-9a-fA-F-]{36})/file$");
        if (match.Success && Guid.TryParse(match.Groups["id"].Value, out var id))
        {
            var res = await _db.AiAgentResources.AsNoTracking()
                .Where(r => r.Id == id)
                .Select(r => new { r.FileContent, r.FileMimeType, r.FileName })
                .FirstOrDefaultAsync(cancellationToken);
            if (res?.FileContent is { Length: > 0 })
            {
                return new AgentMediaContent(
                    Convert.ToBase64String(res.FileContent),
                    string.IsNullOrWhiteSpace(res.FileMimeType) ? "application/octet-stream" : res.FileMimeType,
                    res.FileName);
            }
            return null;
        }

        // Fallback historico: /uploads/agents/... desde el filesystem local.
        if (_env?.WebRootPath is { Length: > 0 } webRoot
            && fileUrl.StartsWith("/uploads/", StringComparison.OrdinalIgnoreCase))
        {
            var rel = fileUrl.TrimStart('/').Replace('/', System.IO.Path.DirectorySeparatorChar);
            var abs = System.IO.Path.Combine(webRoot, rel);
            if (System.IO.File.Exists(abs))
            {
                var bytes = await System.IO.File.ReadAllBytesAsync(abs, cancellationToken);
                var name = System.IO.Path.GetFileName(abs);
                return new AgentMediaContent(
                    Convert.ToBase64String(bytes),
                    GuessMime(System.IO.Path.GetExtension(name)),
                    name);
            }
        }

        return null;
    }

    private static string GuessMime(string ext) => ext.ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".webp" => "image/webp",
        ".gif" => "image/gif",
        ".mp4" => "video/mp4",
        ".mp3" => "audio/mpeg",
        ".ogg" => "audio/ogg",
        ".pdf" => "application/pdf",
        _ => "application/octet-stream"
    };
}
