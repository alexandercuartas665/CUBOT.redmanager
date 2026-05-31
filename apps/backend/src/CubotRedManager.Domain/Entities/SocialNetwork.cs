using CubotRedManager.Domain.Common;

namespace CubotRedManager.Domain.Entities;

/// <summary>
/// Catalogo de redes sociales soportadas (global, Modulo 2.2). El Super Admin habilita/deshabilita
/// redes. Codigos: meta_facebook, meta_instagram, tiktok, youtube.
/// </summary>
public class SocialNetwork : BaseEntity
{
    public string Code { get; set; } = null!;
    public string DisplayName { get; set; } = null!;
    public string? OAuthAuthorizeUrl { get; set; }
    public string? OAuthTokenUrl { get; set; }
    public string? Scopes { get; set; }
    public bool IsEnabled { get; set; } = true;
    public string? LogoUrl { get; set; }
    public string ColorHex { get; set; } = "#A03DC9";
}
