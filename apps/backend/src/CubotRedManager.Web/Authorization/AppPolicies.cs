namespace CubotRedManager.Web.Authorization;

/// <summary>
/// Nombres de politicas de autorizacion de la consola de agencia. La consola Super Admin
/// usa su propia politica (PlatformOperator) en el proyecto CubotRedManager.SuperAdmin.
/// </summary>
public static class AppPolicies
{
    /// <summary>Miembro de una agencia (Owner/Admin/Operator). Acceso a la consola de agencia.</summary>
    public const string TenantMember = "TenantMember";

    /// <summary>Administrador o dueno de la agencia (Owner/Admin). Configuracion sensible.</summary>
    public const string TenantAdmin = "TenantAdmin";

    /// <summary>Operador de plataforma / Super Admin. NO se mezcla con la consola de agencia.</summary>
    public const string PlatformOperator = "PlatformOperator";
}
