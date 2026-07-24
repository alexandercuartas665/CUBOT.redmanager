using CubotRedManager.Application.Common;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CubotRedManager.Infrastructure.Persistence;

/// <summary>
/// Lee el binario de un recurso del agente. Soporta tres formatos de FileUrl:
///
/// 1. <c>/api/agent-resources/{id}/file</c> - contenido servido desde la columna
///    <c>ai_agent_resources.file_content</c> (bytea). Formato oficial: sobrevive a
///    restarts del host (filesystem efimero en Railway).
///
/// 2. <c>http(s)://...</c> - URL externa. Se descarga con HttpClient y se reenvia como
///    media a WhatsApp. Usado por catalogos externos (ej. fuxionstorage.blob... para
///    imagenes de productos FUXION). Es de confianza porque el URL lo pone el operador
///    en el catalogo de recursos, no lo inventa el LLM. Timeout 20s.
///
/// 3. <c>/uploads/agents/...</c> - fallback historico: intenta leer desde
///    <c>WebRootPath/uploads/agents/</c>. Solo funciona en instalaciones locales
///    donde el archivo no se ha perdido (Railway lo pierde entre restarts).
///
/// El dispatcher (Application) solo conoce la interfaz; esta implementacion vive
/// en Infrastructure para poder tocar el DbContext + HttpClient.
/// </summary>
public sealed class DbAgentMediaReader : IAgentMediaReader
{
    private readonly CubotRedManagerDbContext _db;
    private readonly IWebHostEnvironment? _env;
    private readonly HttpClient _http;
    private readonly ILogger<DbAgentMediaReader>? _logger;

    public DbAgentMediaReader(CubotRedManagerDbContext db, HttpClient http, IWebHostEnvironment? env = null, ILogger<DbAgentMediaReader>? logger = null)
    {
        _db = db;
        _http = http;
        _env = env;
        _logger = logger;
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

        // Origen 2: URL externa http/https. Se descarga con HttpClient y se reenvia. Usado por
        // catalogos externos (ej. imagenes de productos FUXION en fuxionstorage.blob...).
        if (fileUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || fileUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(TimeSpan.FromSeconds(20));
                using var resp = await _http.GetAsync(fileUrl, cts.Token);
                if (!resp.IsSuccessStatusCode)
                {
                    _logger?.LogWarning("DbAgentMediaReader: HTTP {Status} descargando {Url}", (int)resp.StatusCode, fileUrl);
                    return null;
                }
                var bytes = await resp.Content.ReadAsByteArrayAsync(cts.Token);
                var mime = resp.Content.Headers.ContentType?.MediaType;
                if (string.IsNullOrWhiteSpace(mime))
                {
                    var path = Uri.TryCreate(fileUrl, UriKind.Absolute, out var u) ? u.AbsolutePath : fileUrl;
                    mime = GuessMime(System.IO.Path.GetExtension(path));
                }
                var fileName = System.IO.Path.GetFileName(Uri.TryCreate(fileUrl, UriKind.Absolute, out var uri) ? uri.AbsolutePath : fileUrl);
                if (string.IsNullOrWhiteSpace(fileName)) { fileName = "media"; }
                return new AgentMediaContent(Convert.ToBase64String(bytes), mime, fileName);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "DbAgentMediaReader: excepcion descargando {Url}", fileUrl);
                return null;
            }
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
