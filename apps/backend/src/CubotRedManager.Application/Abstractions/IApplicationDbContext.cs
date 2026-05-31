using CubotRedManager.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CubotRedManager.Application.Abstractions;

/// <summary>
/// Abstraccion del DbContext para los casos de uso de Application, sin acoplar a Infrastructure.
/// Expone solo los conjuntos que la capa necesita (se amplia segun se portan modulos).
/// </summary>
public interface IApplicationDbContext
{
    // Identidad y agencia
    DbSet<PlatformUser> PlatformUsers { get; }
    DbSet<TenantUser> TenantUsers { get; }
    DbSet<Tenant> Tenants { get; }
    DbSet<Client> Clients { get; }
    DbSet<UserClientLink> UserClientLinks { get; }

    // Planes SaaS
    DbSet<SaasPlan> SaasPlans { get; }
    DbSet<SaasPlanLimit> SaasPlanLimits { get; }

    // Wompi / suscripciones / pagos
    DbSet<WompiMasterConfig> WompiMasterConfigs { get; }
    DbSet<WompiWebhookEvent> WompiWebhookEvents { get; }
    DbSet<TenantSubscription> TenantSubscriptions { get; }
    DbSet<TenantPayment> TenantPayments { get; }
    DbSet<SuperAdminAuditLog> SuperAdminAuditLogs { get; }

    // IA
    DbSet<AiProviderConfig> AiProviderConfigs { get; }
    DbSet<AiAgent> AiAgents { get; }
    DbSet<AiAgentResource> AiAgentResources { get; }
    DbSet<AiAgentPrompt> AiAgentPrompts { get; }
    DbSet<AiAgentCacheField> AiAgentCacheFields { get; }
    DbSet<AiAgentCacheValue> AiAgentCacheValues { get; }
    DbSet<AiUsageLog> AiUsageLogs { get; }

    // WhatsApp / Evolution
    DbSet<WhatsAppLine> WhatsAppLines { get; }
    DbSet<TenantEvolutionConfig> TenantEvolutionConfigs { get; }
    DbSet<EvolutionMasterConfig> EvolutionMasterConfigs { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
