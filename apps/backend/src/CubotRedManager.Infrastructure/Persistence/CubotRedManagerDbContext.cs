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
    public DbSet<TikTokAppConfig> TikTokAppConfigs => Set<TikTokAppConfig>();
    public DbSet<TikTokVideo> TikTokVideos => Set<TikTokVideo>();
    public DbSet<AutoReplyConfig> AutoReplyConfigs => Set<AutoReplyConfig>();
    public DbSet<AutoReplyTemplate> AutoReplyTemplates => Set<AutoReplyTemplate>();
    public DbSet<AutoReplyJobLog> AutoReplyJobLogs => Set<AutoReplyJobLog>();
    public DbSet<AutomationRule> AutomationRules => Set<AutomationRule>();
    public DbSet<TaskBoard> TaskBoards => Set<TaskBoard>();
    public DbSet<TaskBoardColumn> TaskBoardColumns => Set<TaskBoardColumn>();
    public DbSet<TaskCard> TaskCards => Set<TaskCard>();
    public DbSet<TaskCardAssignment> TaskCardAssignments => Set<TaskCardAssignment>();
    public DbSet<TaskCardTag> TaskCardTags => Set<TaskCardTag>();
    public DbSet<TaskCardTagAssignment> TaskCardTagAssignments => Set<TaskCardTagAssignment>();
    public DbSet<TaskCardChecklistItem> TaskCardChecklistItems => Set<TaskCardChecklistItem>();
    public DbSet<TaskCardActivity> TaskCardActivities => Set<TaskCardActivity>();
    public DbSet<TaskCardAttachment> TaskCardAttachments => Set<TaskCardAttachment>();
    public DbSet<Publication> Publications => Set<Publication>();
    public DbSet<PublicationTarget> PublicationTargets => Set<PublicationTarget>();
    public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();
    public DbSet<InboxReply> InboxReplies => Set<InboxReply>();
    public DbSet<MessageTemplate> MessageTemplates => Set<MessageTemplate>();

    // Contenedor de Datos (modelos dinamicos EAV).
    public DbSet<DataContainer> DataContainers => Set<DataContainer>();
    public DbSet<DataContainerColumn> DataContainerColumns => Set<DataContainerColumn>();
    public DbSet<DataContainerRow> DataContainerRows => Set<DataContainerRow>();
    public DbSet<DataContainerCell> DataContainerCells => Set<DataContainerCell>();

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
        // App de TikTok: una config por tenant (Modulo 2.2).
        modelBuilder.Entity<TikTokAppConfig>().HasIndex(x => x.TenantId).IsUnique();
        // Videos TikTok: idempotencia por cuenta + external_id (Modulo 2.4 Sync).
        modelBuilder.Entity<TikTokVideo>().HasIndex(x => new { x.TenantId, x.SocialAccountId, x.ExternalId }).IsUnique();
        modelBuilder.Entity<TikTokVideo>().HasIndex(x => new { x.SocialAccountId, x.PublishedAt });

        // Autorespuesta (Modulo 2.11): una config por cuenta social.
        modelBuilder.Entity<AutoReplyConfig>().HasIndex(x => x.SocialAccountId).IsUnique();
        modelBuilder.Entity<AutoReplyConfig>().Property(x => x.Mode).HasConversion<string>();
        modelBuilder.Entity<AutoReplyConfig>().Property(x => x.Frequency).HasConversion<string>();
        modelBuilder.Entity<AutoReplyConfig>().HasMany(c => c.Templates).WithOne(t => t.Config!).HasForeignKey(t => t.ConfigId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<AutoReplyTemplate>().HasIndex(x => new { x.ConfigId, x.SortOrder });
        modelBuilder.Entity<AutoReplyJobLog>().Property(x => x.Status).HasConversion<string>();
        modelBuilder.Entity<AutoReplyJobLog>().HasIndex(x => new { x.SocialAccountId, x.StartedAt });

        // Automatizaciones.
        modelBuilder.Entity<AutomationRule>().Property(x => x.Trigger).HasConversion<string>();
        modelBuilder.Entity<AutomationRule>().Property(x => x.Action).HasConversion<string>();
        modelBuilder.Entity<AutomationRule>().HasIndex(x => new { x.TenantId, x.IsActive });

        // ---- Modulo Tableros (Kanban) - portado verbatim desde travels ----

        modelBuilder.Entity<TaskBoard>(b =>
        {
            b.Property(x => x.Name).HasMaxLength(150).IsRequired();
            b.Property(x => x.Description).HasMaxLength(2000);
            b.Property(x => x.Color).HasMaxLength(20);
            b.HasIndex(x => new { x.TenantId, x.SortOrder });
        });

        modelBuilder.Entity<TaskBoardColumn>(b =>
        {
            b.Property(x => x.Name).HasMaxLength(120).IsRequired();
            b.Property(x => x.Color).HasMaxLength(20);
            b.HasOne(x => x.Board).WithMany().HasForeignKey(x => x.BoardId).OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(x => new { x.TenantId, x.BoardId, x.SortOrder });
        });

        modelBuilder.Entity<TaskCard>(b =>
        {
            b.Property(x => x.Title).HasMaxLength(200).IsRequired();
            b.Property(x => x.Description).HasColumnType("text");
            b.HasOne(x => x.Board).WithMany().HasForeignKey(x => x.BoardId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne(x => x.Column).WithMany().HasForeignKey(x => x.ColumnId).OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(x => new { x.TenantId, x.BoardId, x.ColumnId, x.SortOrder });
            b.HasIndex(x => new { x.TenantId, x.IsArchived });
        });

        modelBuilder.Entity<TaskCardAssignment>(b =>
        {
            b.HasOne(x => x.TaskCard).WithMany().HasForeignKey(x => x.TaskCardId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne(x => x.TenantUser).WithMany().HasForeignKey(x => x.TenantUserId).OnDelete(DeleteBehavior.Cascade);
            // Un usuario no se asigna dos veces a la misma tarjeta.
            b.HasIndex(x => new { x.TaskCardId, x.TenantUserId }).IsUnique();
        });

        modelBuilder.Entity<TaskCardTag>(b =>
        {
            b.Property(x => x.Name).HasMaxLength(80).IsRequired();
            b.Property(x => x.Color).HasMaxLength(20);
            b.HasOne(x => x.Board).WithMany().HasForeignKey(x => x.BoardId).OnDelete(DeleteBehavior.Cascade);
            // El nombre de etiqueta es unico por tablero.
            b.HasIndex(x => new { x.BoardId, x.Name }).IsUnique();
        });

        modelBuilder.Entity<TaskCardTagAssignment>(b =>
        {
            b.HasOne(x => x.TaskCard).WithMany().HasForeignKey(x => x.TaskCardId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne(x => x.Tag).WithMany().HasForeignKey(x => x.TagId).OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(x => new { x.TaskCardId, x.TagId }).IsUnique();
        });

        modelBuilder.Entity<TaskCardChecklistItem>(b =>
        {
            b.Property(x => x.Text).HasMaxLength(500).IsRequired();
            b.HasOne(x => x.TaskCard).WithMany().HasForeignKey(x => x.TaskCardId).OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(x => new { x.TaskCardId, x.SortOrder });
        });

        modelBuilder.Entity<TaskCardActivity>(b =>
        {
            b.Property(x => x.ActorName).HasMaxLength(200).IsRequired();
            b.Property(x => x.Text).HasColumnType("text").IsRequired();
            b.HasOne(x => x.TaskCard).WithMany().HasForeignKey(x => x.TaskCardId).OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(x => new { x.TaskCardId, x.CreatedAt });
        });

        modelBuilder.Entity<TaskCardAttachment>(b =>
        {
            b.Property(x => x.FileName).HasMaxLength(255).IsRequired();
            b.Property(x => x.Url).HasMaxLength(500).IsRequired();
            b.Property(x => x.MimeType).HasMaxLength(120);
            b.Property(x => x.UploadedByName).HasMaxLength(200);
            b.HasOne(x => x.TaskCard).WithMany().HasForeignKey(x => x.TaskCardId).OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(x => new { x.TaskCardId, x.CreatedAt });
        });

        // Calendario / publicaciones (Modulo 2.5).
        modelBuilder.Entity<Publication>().Property(x => x.Status).HasConversion<string>();
        modelBuilder.Entity<Publication>().HasMany(p => p.Targets).WithOne(t => t.Publication!).HasForeignKey(t => t.PublicationId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<PublicationTarget>().Property(x => x.Status).HasConversion<string>();
        modelBuilder.Entity<Publication>().HasIndex(x => new { x.TenantId, x.ScheduledAt });

        // Bandeja unificada (Modulo 2.6).
        modelBuilder.Entity<InboxMessage>().Property(x => x.Type).HasConversion<string>();
        modelBuilder.Entity<InboxMessage>().Property(x => x.Status).HasConversion<string>();
        modelBuilder.Entity<InboxMessage>().Property(x => x.PipelineStage).HasConversion<string>();
        modelBuilder.Entity<InboxMessage>().HasIndex(x => new { x.TenantId, x.PipelineStage });
        modelBuilder.Entity<InboxMessage>().HasIndex(x => new { x.NetworkCode, x.ExternalId }).IsUnique();
        modelBuilder.Entity<InboxMessage>().HasMany(m => m.Replies).WithOne(r => r.Message!).HasForeignKey(r => r.InboxMessageId).OnDelete(DeleteBehavior.Cascade);

        // Plantillas de mensajes reutilizables (Configuracion / Plantillas).
        modelBuilder.Entity<MessageTemplate>().HasIndex(x => new { x.TenantId, x.Name });

        // Contenedor de Datos (modelos dinamicos EAV).
        modelBuilder.Entity<DataContainer>().HasIndex(x => new { x.TenantId, x.Name }).IsUnique();
        modelBuilder.Entity<DataContainerColumn>().Property(x => x.Type).HasConversion<string>();
        modelBuilder.Entity<DataContainerColumn>().HasIndex(x => new { x.ContainerId, x.Name }).IsUnique();
        modelBuilder.Entity<DataContainerColumn>()
            .HasOne(c => c.Container)
            .WithMany(c => c.Columns)
            .HasForeignKey(c => c.ContainerId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<DataContainerRow>()
            .HasOne(r => r.Container)
            .WithMany(c => c.Rows)
            .HasForeignKey(r => r.ContainerId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<DataContainerRow>().HasIndex(x => new { x.ContainerId, x.CreatedAt });
        modelBuilder.Entity<DataContainerCell>().HasIndex(x => new { x.RowId, x.ColumnId }).IsUnique();
        modelBuilder.Entity<DataContainerCell>()
            .HasOne(c => c.Row)
            .WithMany(r => r.Cells)
            .HasForeignKey(c => c.RowId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<DataContainerCell>()
            .HasOne(c => c.Column)
            .WithMany()
            .HasForeignKey(c => c.ColumnId)
            .OnDelete(DeleteBehavior.Cascade);

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
