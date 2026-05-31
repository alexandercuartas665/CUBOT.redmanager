using CubotRedManager.Application.Abstractions;

namespace CubotRedManager.Integration.Tests;

/// <summary>Proveedor de tenant mutable para simular cambios de agencia activa en pruebas.</summary>
public sealed class TestTenantProvider : ITenantProvider
{
    public Guid? CurrentTenantId { get; set; }
}
