namespace CubotRedManager.Application.Abstractions;

/// <summary>
/// Cifra/descifra secretos en reposo (API keys de IA, tokens Evolution). La implementacion usa
/// Data Protection. Solo se persisten los valores cifrados; nunca se loggean en claro.
/// </summary>
public interface ISecretProtector
{
    string Protect(string plaintext);
    string Unprotect(string ciphertext);
}
