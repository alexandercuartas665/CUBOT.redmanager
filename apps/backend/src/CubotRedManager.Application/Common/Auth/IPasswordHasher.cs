namespace CubotRedManager.Application.Common.Auth;

/// <summary>Hash y verificacion de claves para login local.</summary>
public interface IPasswordHasher
{
    string Hash(string password);
    bool Verify(string hash, string password);
}
