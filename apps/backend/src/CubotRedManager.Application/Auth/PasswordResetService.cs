namespace CubotRedManager.Application.Auth;

public sealed record PasswordResetResult(bool Success, string? Error);

public interface IPasswordResetService
{
    /// <summary>
    /// Solicita un reseteo: en travels genera token, lo persiste hasheado y lo envia por correo.
    /// TODO redmanager: faltan PasswordResetToken (entidad) e IEmailSender (servicio); por ahora
    /// devuelve Success=true sin enviar nada para no romper el flujo de la UI.
    /// </summary>
    Task<PasswordResetResult> RequestAsync(string email, string baseUrl, CancellationToken cancellationToken = default);

    /// <summary>Aplica la nueva contrasena si el token es valido, no usado y no expirado.</summary>
    Task<PasswordResetResult> ResetAsync(string token, string newPassword, CancellationToken cancellationToken = default);
}

/// <summary>
/// Reseteo de contrasena por correo (autogestion).
/// TODO redmanager: requiere portar la entidad PasswordResetToken, IEmailSender y la
/// infraestructura SMTP. Por ahora el servicio es un stub que no falla la UI pero tampoco
/// realiza el reseteo: el flujo completo se incorpora con el modulo de identidad real.
/// </summary>
public sealed class PasswordResetService : IPasswordResetService
{
    public Task<PasswordResetResult> RequestAsync(string email, string baseUrl, CancellationToken cancellationToken = default)
    {
        // No revelar si el correo existe: respuesta uniforme.
        return Task.FromResult(new PasswordResetResult(true, null));
    }

    public Task<PasswordResetResult> ResetAsync(string token, string newPassword, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new PasswordResetResult(false,
            "El reseteo por correo aun no esta habilitado. Pide a tu administrador que actualice tu clave manualmente."));
    }
}
