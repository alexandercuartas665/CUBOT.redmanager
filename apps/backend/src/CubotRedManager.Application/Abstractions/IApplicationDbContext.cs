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
    DbSet<SocialNetwork> SocialNetworks { get; }
    DbSet<SocialAccount> SocialAccounts { get; }
    DbSet<TikTokAppConfig> TikTokAppConfigs { get; }
    DbSet<TikTokVideo> TikTokVideos { get; }
    DbSet<AutoReplyConfig> AutoReplyConfigs { get; }
    DbSet<AutoReplyTemplate> AutoReplyTemplates { get; }
    DbSet<AutoReplyJobLog> AutoReplyJobLogs { get; }
    DbSet<AutomationRule> AutomationRules { get; }
    DbSet<TaskBoard> TaskBoards { get; }
    DbSet<TaskBoardColumn> TaskBoardColumns { get; }
    DbSet<TaskCard> TaskCards { get; }
    DbSet<TaskCardAssignment> TaskCardAssignments { get; }
    DbSet<TaskCardTag> TaskCardTags { get; }
    DbSet<TaskCardTagAssignment> TaskCardTagAssignments { get; }
    DbSet<TaskCardChecklistItem> TaskCardChecklistItems { get; }
    DbSet<TaskCardActivity> TaskCardActivities { get; }
    DbSet<TaskCardAttachment> TaskCardAttachments { get; }
    DbSet<Publication> Publications { get; }
    DbSet<PublicationTarget> PublicationTargets { get; }
    DbSet<InboxMessage> InboxMessages { get; }
    DbSet<InboxReply> InboxReplies { get; }
    DbSet<MessageTemplate> MessageTemplates { get; }

    // Contenedor de Datos (modelos dinamicos EAV)
    DbSet<DataContainer> DataContainers { get; }
    DbSet<DataContainerColumn> DataContainerColumns { get; }
    DbSet<DataContainerRow> DataContainerRows { get; }
    DbSet<DataContainerCell> DataContainerCells { get; }

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
    DbSet<WhatsAppTemplate> WhatsAppTemplates { get; }
    DbSet<TenantEvolutionConfig> TenantEvolutionConfigs { get; }
    DbSet<EvolutionMasterConfig> EvolutionMasterConfigs { get; }

    // Alertas operativas por WhatsApp (tenant-scoped, una por tenant).
    DbSet<TenantAlertConfig> TenantAlertConfigs { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
