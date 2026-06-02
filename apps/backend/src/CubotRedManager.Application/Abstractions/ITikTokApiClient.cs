namespace CubotRedManager.Application.Abstractions;

/// <summary>Pagina de respuesta cruda de TikTok Business API (parseada minimamente).</summary>
/// <param name="Code">code field del JSON (0 = exito).</param>
/// <param name="Message">message field del JSON (descripcion del error si Code != 0).</param>
/// <param name="RawJson">JSON completo de la respuesta para parseo posterior.</param>
public sealed record TikTokApiPage(int Code, string? Message, string RawJson);

/// <summary>
/// Cliente HTTP de los endpoints de DATOS de TikTok Business API (videos, comentarios, replies).
/// Separado del OAuth provider porque opera con un Access-Token de cuenta, no con credenciales
/// de app. Devuelve respuestas crudas (RawJson) para que la capa de Application haga el parseo
/// tolerante (TikTok cambia nombres de campos entre versiones).
/// </summary>
public interface ITikTokApiClient
{
    /// <summary>GET /business/video/list/ — cursor-based, max 20 por request.</summary>
    Task<TikTokApiPage> ListVideosAsync(
        string accessToken, string businessId, long cursor, int maxCount, CancellationToken cancellationToken = default);

    /// <summary>GET /business/comment/list/ — page-based, max 20 por request.</summary>
    Task<TikTokApiPage> ListCommentsAsync(
        string accessToken, string businessId, string videoId, int page, int pageSize, CancellationToken cancellationToken = default);

    /// <summary>GET /business/comment/reply/list/ — cursor-based, max 20 por request.</summary>
    Task<TikTokApiPage> ListRepliesAsync(
        string accessToken, string businessId, string videoId, string commentId, long cursor, int count, CancellationToken cancellationToken = default);

    /// <summary>POST /business/comment/reply/create/ — publica una respuesta a un comentario.</summary>
    Task<TikTokApiPage> PostCommentReplyAsync(
        string accessToken, string businessId, string videoId, string commentId, string text, CancellationToken cancellationToken = default);

    // ----- Content Posting API (publicacion directa de videos) -----
    // Host: open.tiktokapis.com (no business-api). Header: Authorization: Bearer (no Access-Token).
    // Requiere scope video.publish; en sandbox solo permite SELF_ONLY hasta que se audite la app.

    /// <summary>POST /v2/post/publish/video/init/ — inicia el upload (single chunk).</summary>
    Task<TikTokApiPage> PublishVideoInitAsync(
        string accessToken, string caption, long videoSize, string privacyLevel, CancellationToken cancellationToken = default);

    /// <summary>PUT al upload_url devuelto por init. Sube el video completo en una sola request.</summary>
    Task<(bool Ok, string? Error)> PublishVideoUploadAsync(
        string uploadUrl, Stream videoStream, long videoSize, string contentType, CancellationToken cancellationToken = default);

    /// <summary>POST /v2/post/publish/status/fetch/ — poll del status del publish_id.</summary>
    Task<TikTokApiPage> PublishStatusAsync(
        string accessToken, string publishId, CancellationToken cancellationToken = default);

    /// <summary>GET /v2/user/info/ — devuelve perfil del usuario (open_id, username, display_name, avatar_url).</summary>
    Task<TikTokApiPage> GetUserInfoAsync(string accessToken, CancellationToken cancellationToken = default);
}
