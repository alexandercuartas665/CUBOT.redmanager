namespace CubotRedManager.Domain.Enums;

/// <summary>Estado del tenant (agencia) en el SaaS.</summary>
public enum TenantStatus
{
    Trial,
    Active,
    PastDue,
    Suspended,
    Blocked,
    Cancelled,
    Archived
}

/// <summary>Rol global de plataforma. Null en PlatformUser = usuario de agencia.</summary>
public enum PlatformRole
{
    SuperAdmin,
    PlatformOperator
}

/// <summary>Rol del usuario dentro de una agencia (tenant).</summary>
public enum TenantRole
{
    Owner,
    Admin,
    Operator
}

/// <summary>Estado de la membresia / usuario.</summary>
public enum PlatformUserStatus
{
    Invited,
    Active,
    Disabled
}

/// <summary>Proveedor de identidad del usuario.</summary>
public enum AuthProvider
{
    Local,
    Google
}
