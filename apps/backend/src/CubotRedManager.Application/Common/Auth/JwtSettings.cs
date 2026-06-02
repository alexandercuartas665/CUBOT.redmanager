namespace CubotRedManager.Application.Common.Auth;

/// <summary>Configuracion del JWT propio de CUBOT.redmanager (seccion "Jwt").</summary>
public sealed class JwtSettings
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = "CubotRedManager";
    public string Audience { get; set; } = "CubotRedManager";
    public string SigningKey { get; set; } = string.Empty;
    public int AccessTokenMinutes { get; set; } = 60;
}
