using System.Linq.Expressions;
using CubotRedManager.Application.Abstractions;
// IApplicationDbContext vive en Application.Abstractions (incluido arriba).
using CubotRedManager.Domain.Common;
using CubotRedManager.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CubotRedManager.Infrastructure.Persistence;

/// <summary>
/// DbContext principal. Aplica:
/// - snake_case (via UseSnakeCaseNamingConvention en el registro de DI).
/// - filtro global por tenant en toda entidad ITenantScoped.
/// - auditoria automatica de CreatedAt/UpdatedAt.
/// </summary>
public class CubotRedManagerDbContext : DbContext, IApplicationDbContext
{
    private readonly ITenantProvider _tenantProvider;

    public CubotRedManagerDbContext(
        DbContextOptions<CubotRedManagerDbContext> options,
        ITenantProvider tenantProvider)
        : base(options)
    {
        _tenantProvider = tenantProvider;
    }

    // Globales
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<PlatformUser> PlatformUsers => Set<PlatformUser>();
    public DbSet<AiProviderConfig> AiProviderConfigs => Set<AiProviderConfig>();
    public DbSet<EvolutionMasterConfig> EvolutionMasterConfigs => Set<EvolutionMasterConfig>();
    public DbSet<SaasPlan> SaasPlans => Set<SaasPlan>();
    public DbSet<SaasPlanLimit> SaasPlanLimits => Set<SaasPlanLimit>();
    public DbSet<WompiMasterConfig> WompiMasterConfigs => Set<WompiMasterConfig>();
    public DbSet<WompiWebhookEvent> WompiWebhookEvents => Set<WompiWebhookEvent>();
    public DbSet<TenantSubscription> TenantSubscriptions => Set<TenantSubscription>();
    public DbSet<TenantPayment> TenantPayments => Set<TenantPayment>();
    public DbSet<SuperAdminAuditLog> SuperAdminAuditLogs => Set<SuperAdminAuditLog>();
    public DbSet<SocialNetwork> SocialNetworks => Set<SocialNetwork>();

    // Tenant-scoped
    public DbSet<TenantUser> TenantUsers => Set<TenantUser>();
    public DbSet<Client> Clients => Set<Client>();
    public DbSet<UserClientLink> UserClientLinks => Set<UserClientLink>();
    public DbSet<SocialAccount> SocialAccounts => Set<SocialAccount>();

    // IA (capa 3)
    public DbSet<AiAgent> AiAgents => Set<AiAgent>();
    public DbSet<AiAgentResource> AiAgentResources => Set<AiAgentResource>();
    public DbSet<AiAgentPrompt> AiAgentPrompts => Set<AiAgentPrompt>();
    public DbSet<AiAgentCacheField> AiAgentCacheFields => Set<AiAgentCacheField>();
    public DbSet<AiAgentCacheValue> AiAgentCacheValues => Set<AiAgentCacheValue>();
    public DbSet<AiUsageLog> AiUsageLogs => Set<AiUsageLog>();

    // WhatsApp / Evolution
    public DbSet<WhatsAppLine> WhatsAppLines => Set<WhatsAppLine>();
    public DbSet<TenantEvolutionConfig> TenantEvolutionConfigs => Set<TenantEvolutionConfig>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Enums como texto (no int).
        modelBuilder.Entity<Tenant>().Property(x => x.Status).HasConversion<string>();
        modelBuilder.Entity<Tenant>().Property(x => x.Kind).HasConversion<string>();
        modelBuilder.Entity<PlatformUser>().Property(x => x.PlatformRole).HasConversion<string>();
        modelBuilder.Entity<PlatformUser>().Property(x => x.AuthProvider).HasConversion<string>();
        modelBuilder.Entity<TenantUser>().Property(x => x.TenantRole).HasConversion<string>();
        modelBuilder.Entity<TenantUser>().Property(x => x.Status).HasConversion<string>();

        // Enums como texto (IA / WhatsApp / Evolution).
        modelBuilder.Entity<AiProviderConfig>().Property(x => x.Provider).HasConversion<string>();
        modelBuilder.Entity<AiAgent>().Property(x => x.Provider).HasConversion<string>();
        modelBuilder.Entity<AiAgentResource>().Property(x => x.ResourceType).HasConversion<string>();
        modelBuilder.Entity<AiUsageLog>().Property(x => x.Provider).HasConversion<string>();
        modelBuilder.Entity<WhatsAppLine>().Property(x => x.Status).HasConversion<string>();
        modelBuilder.Entity<EvolutionMasterConfig>().Property(x => x.Status).HasConversion<string>();
        modelBuilder.Entity<SaasPlanLimit>().Property(x => x.EnforcementMode).HasConversion<string>();
        modelBuilder.Entity<SaasPlan>().HasMany(p => p.Limits).WithOne(l => l.Plan!).HasForeignKey(l => l.PlanId).OnDelete(DeleteBehavior.Cascade);

        // Wompi / suscripciones / pagos.
        modelBuilder.Entity<WompiMasterConfig>().Property(x => x.Environment).HasConversion<string>();
        modelBuilder.Entity<WompiMasterConfig>().Property(x => x.Status).HasConversion<string>();
        modelBuilder.Entity<WompiWebhookEvent>().Property(x => x.ProcessingStatus).HasConversion<string>();
        modelBuilder.Entity<WompiWebhookEvent>().HasIndex(x => x.ProviderEventId).IsUnique();
        modelBuilder.Entity<TenantSubscription>().Property(x => x.Status).HasConversion<string>();
        modelBuilder.Entity<TenantSubscription>().Property(x => x.BillingFrequency).HasConversion<string>();
        modelBuilder.Entity<TenantPayment>().Property(x => x.Status).HasConversion<string>();
        modelBuilder.Entity<TenantPayment>().Property(x => x.Amount).HasColumnType("numeric(14,2)");

        // Redes y cuentas sociales (Modulo 2.2/2.3).
        modelBuilder.Entity<SocialNetwork>().HasIndex(x => x.Code).IsUnique();
        modelBuilder.Entity<SocialAccount>().Property(x => x.Status).HasConversion<string>();
        modelBuilder.Entity<SocialAccount>()
            .HasIndex(x => new { x.TenantId, x.ClientId, x.NetworkCode, x.ExternalId }).IsUnique();

        // Auditoria global.
        modelBuilder.Entity<SuperAdminAuditLog>().Property(x => x.ActorType).HasConversion<string>();
        modelBuilder.Entity<SuperAdminAuditLog>().Property(x => x.PreviousValue).HasColumnType("jsonb");
        modelBuilder.Entity<SuperAdminAuditLog>().Property(x => x.NewValue).HasColumnType("jsonb");
        modelBuilder.Entity<SuperAdminAuditLog>().HasIndex(x => x.CreatedAt);

        // IA: indices y unicidad.
        modelBuilder.Entity<AiProviderConfig>().HasIndex(x => x.Provider).IsUnique();
        modelBuilder.Entity<AiAgent>().HasIndex(x => new { x.TenantId, x.SortOrder });
        modelBuilder.Entity<AiAgentCacheField>().HasIndex(x => new { x.AgentId, x.FieldKey }).IsUnique();
        modelBuilder.Entity<AiAgentCacheValue>().HasIndex(x => new { x.AgentId, x.SessionId, x.FieldKey }).IsUnique();

        // WhatsApp: una instancia por agencia.
        modelBuilder.Entity<WhatsAppLine>().HasIndex(x => new { x.TenantId, x.InstanceName }).IsUnique();
        modelBuilder.Entity<TenantEvolutionConfig>().HasIndex(x => x.TenantId).IsUnique();

        // Unicidad / constraints clave.
        modelBuilder.Entity<PlatformUser>().HasIndex(x => x.Email).IsUnique();
        modelBuilder.Entity<TenantUser>()
            .HasIndex(x => new { x.TenantId, x.PlatformUserId }).IsUnique();
        modelBuilder.Entity<UserClientLink>()
            .HasIndex(x => new { x.TenantUserId, x.ClientId }).IsUnique();

        // Filtro global por tenant en toda entidad ITenantScoped.
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (typeof(ITenantScoped).IsAssignableFrom(entityType.ClrType))
            {
                ApplyTenantFilter(modelBuilder, entityType.ClrType);
            }
        }
    }

    private void ApplyTenantFilter(ModelBuilder modelBuilder, Type clrType)
    {
        // e => e.TenantId == _tenantProvider.CurrentTenantId
        var parameter = Expression.Parameter(clrType, "e");
        var tenantIdProp = Expression.Property(parameter, nameof(ITenantScoped.TenantId));
        var currentTenant = Expression.Property(
            Expression.Constant(this), nameof(CurrentTenantIdForFilter));
        var body = Expression.Equal(
            Expression.Convert(tenantIdProp, typeof(Guid?)), currentTenant);
        var lambda = Expression.Lambda(body, parameter);
        modelBuilder.Entity(clrType).HasQueryFilter(lambda);
    }

    /// <summary>Expuesto para el filtro de consulta; se evalua por peticion.</summary>
    public Guid? CurrentTenantIdForFilter => _tenantProvider.CurrentTenantId;

    public override int SaveChanges()
    {
        ApplyAuditInfo();
        return base.SaveChanges();
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        ApplyAuditInfo();
        return base.SaveChangesAsync(cancellationToken);
    }

    private void ApplyAuditInfo()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var entry in ChangeTracker.Entries<BaseEntity>())
        {
            if (entry.State == EntityState.Added)
            {
                if (entry.Entity.CreatedAt == default)
                {
                    entry.Entity.CreatedAt = now;
                }
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAt = now;
            }
        }
    }
}
