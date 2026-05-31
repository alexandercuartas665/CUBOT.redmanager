namespace CubotRedManager.SuperAdmin.Authorization;

/// <summary>Politicas de la consola Super Admin (separadas de la consola de agencia).</summary>
public static class AppPolicies
{
    /// <summary>Operador de plataforma / Super Admin (claim platform_role).</summary>
    public const string PlatformOperator = "PlatformOperator";

    /// <summary>Solo Super Admin (platform_role == SuperAdmin).</summary>
    public const string SuperAdminOnly = "SuperAdminOnly";
}
