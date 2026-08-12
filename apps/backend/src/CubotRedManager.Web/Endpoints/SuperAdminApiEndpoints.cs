using System.Security.Claims;
using CubotRedManager.Application.Abstractions;
using CubotRedManager.Application.SuperAdminApi;
using CubotRedManager.Web.Authorization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CubotRedManager.Web.Endpoints;

/// <summary>
/// Rutas de la Admin Agent API (brief "Capa 6 / Admin Agent API"). Todo bajo /api/admin/*, todo
/// protegido por policy SuperAdminApi (Bearer JWT + claim is_super_admin=true).
///
/// Convenciones del brief:
///   - tenantId viaja en la RUTA (super admin no lo trae en el JWT).
///   - Mutaciones (POST/PUT) escriben SuperAdminAuditLog inmutable via IAuditWriter. La entrada
///     se agrega al ChangeTracker antes del SaveChanges del service (mismo transaction scope).
///   - Enums viajan como NUMERO (System.Text.Json default), no strings.
/// </summary>
public static class SuperAdminApiEndpoints
{
    public static void MapSuperAdminApi(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints
            .MapGroup("/api/admin")
            .RequireAuthorization(AppPolicies.SuperAdminApi);

        // ---- Tenants (helper global — la ruta base del cross-tenant browser) ---------------
        group.MapGet("/tenants", async (IApplicationDbContext db, CancellationToken ct) =>
        {
            var tenants = await db.Tenants.AsNoTracking()
                .OrderBy(t => t.Name)
                .Select(t => new { id = t.Id, name = t.Name, status = t.Status, kind = t.Kind })
                .ToListAsync(ct);
            return Results.Ok(tenants);
        });

        // ---- Agents ------------------------------------------------------------------------

        group.MapGet("/tenants/{tenantId:guid}/agents", async (
            Guid tenantId,
            IAdminAgentService svc,
            CancellationToken ct) => Results.Ok(await svc.ListAgentsAsync(tenantId, ct)));

        group.MapGet("/tenants/{tenantId:guid}/agents/mcp-tools", () =>
            Results.Ok(new { toolKeys = AdminAgentTools.All }));

        group.MapGet("/tenants/{tenantId:guid}/agents/{agentId:guid}", async (
            Guid tenantId,
            Guid agentId,
            IAdminAgentService svc,
            CancellationToken ct) =>
        {
            var detail = await svc.GetAgentAsync(tenantId, agentId, ct);
            return detail is null ? Results.NotFound() : Results.Ok(detail);
        });

        group.MapPost("/tenants/{tenantId:guid}/agents", async (
            Guid tenantId,
            [FromBody] CreateAiAgentRequest req,
            IAdminAgentService svc,
            IAuditWriter audit,
            IApplicationDbContext db,
            HttpContext http,
            CancellationToken ct) =>
        {
            if (req is null || string.IsNullOrWhiteSpace(req.Name))
            {
                return Results.BadRequest(new { error = "name required" });
            }
            var created = await svc.CreateAgentAsync(tenantId, req, ct);
            // La entrada de auditoria va a super_admin_audit_logs (tabla global, no tenant-scoped)
            // y se persiste con un SaveChanges adicional — el service ya cerro su transaccion.
            audit.Write(Actor(http), "AI_AGENT_ADMIN_CREATE", nameof(Domain.Entities.AiAgent),
                created.Id, previousValue: null, newValue: created, tenantId: tenantId);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/admin/tenants/{tenantId}/agents/{created.Id}", created);
        });

        group.MapPut("/tenants/{tenantId:guid}/agents/{agentId:guid}", async (
            Guid tenantId,
            Guid agentId,
            [FromBody] UpdateAiAgentRequest req,
            IAdminAgentService svc,
            IAuditWriter audit,
            IApplicationDbContext db,
            HttpContext http,
            CancellationToken ct) =>
        {
            if (req is null || string.IsNullOrWhiteSpace(req.Name))
            {
                return Results.BadRequest(new { error = "name required" });
            }
            // Snapshot antes del cambio para tener previousValue en la auditoria.
            var before = await svc.GetAgentAsync(tenantId, agentId, ct);
            if (before is null) { return Results.NotFound(); }
            var updated = await svc.UpdateAgentAsync(tenantId, agentId, req, ct);
            audit.Write(Actor(http), "AI_AGENT_ADMIN_UPDATE", nameof(Domain.Entities.AiAgent),
                agentId, previousValue: before.Agent, newValue: updated, tenantId: tenantId);
            await db.SaveChangesAsync(ct);
            return Results.Ok(updated);
        });

        group.MapPut("/tenants/{tenantId:guid}/agents/{agentId:guid}/tools", async (
            Guid tenantId,
            Guid agentId,
            [FromBody] UpdateAgentToolsRequest req,
            IAdminAgentService svc,
            IAuditWriter audit,
            IApplicationDbContext db,
            HttpContext http,
            CancellationToken ct) =>
        {
            var keys = req?.ToolKeys ?? Array.Empty<string>();
            // Validacion contra el catalogo cerrado: 400 si alguna key no esta en el catalogo.
            var invalid = keys.Where(k => !AdminAgentTools.All.Contains(k, StringComparer.OrdinalIgnoreCase)).ToList();
            if (invalid.Count > 0)
            {
                return Results.BadRequest(new { error = "invalid_tool_keys", invalid });
            }
            var before = await svc.GetAgentAsync(tenantId, agentId, ct);
            if (before is null) { return Results.NotFound(); }
            var updated = await svc.SetAgentToolsAsync(tenantId, agentId, keys, ct);
            audit.Write(Actor(http), "AI_AGENT_ADMIN_TOOLS", nameof(Domain.Entities.AiAgent),
                agentId,
                previousValue: before.Agent.ToolKeys,
                newValue: updated?.Agent.ToolKeys,
                tenantId: tenantId);
            await db.SaveChangesAsync(ct);
            return Results.Ok(updated);
        });

        // ---- Agent Run Logs ----------------------------------------------------------------

        group.MapGet("/tenants/{tenantId:guid}/agent-logs", async (
            Guid tenantId,
            IAdminAgentService svc,
            [FromQuery] int? take,
            CancellationToken ct) =>
                Results.Ok(await svc.ListRunLogConversationsAsync(tenantId, take ?? 100, ct)));

        group.MapGet("/tenants/{tenantId:guid}/agent-logs/{conversationId:guid}", async (
            Guid tenantId,
            Guid conversationId,
            IAdminAgentService svc,
            CancellationToken ct) =>
        {
            var entries = await svc.GetRunLogEntriesAsync(tenantId, conversationId, ct);
            return entries.Count == 0 ? Results.NotFound() : Results.Ok(entries);
        });

        // ---- Lines (WhatsApp) + line-binding (PR3) ----------------------------------------

        group.MapGet("/tenants/{tenantId:guid}/lines", async (
            Guid tenantId,
            IAdminAgentService svc,
            CancellationToken ct) => Results.Ok(await svc.ListLinesAsync(tenantId, ct)));

        group.MapPost("/tenants/{tenantId:guid}/agents/{agentId:guid}/line-binding", async (
            Guid tenantId,
            Guid agentId,
            [FromBody] BindLineRequest req,
            IAdminAgentService svc,
            IAuditWriter audit,
            IApplicationDbContext db,
            HttpContext http,
            CancellationToken ct) =>
        {
            if (req is null || req.WhatsAppLineId == Guid.Empty)
            {
                return Results.BadRequest(new { error = "whatsAppLineId required" });
            }
            var result = await svc.BindLineAsync(tenantId, agentId, req.WhatsAppLineId, ct);
            if (result.Ok)
            {
                audit.Write(Actor(http), "AI_AGENT_ADMIN_BIND", nameof(Domain.Entities.AiAgentLineBinding),
                    entityId: null, previousValue: null, newValue: req, tenantId: tenantId);
                await db.SaveChangesAsync(ct);
                return Results.Ok(new { ok = true });
            }
            // Diferenciamos 404 (agent/line inexistente) de 409 (conflicto de reasignacion).
            var status = result.Error switch
            {
                "agent_not_found" or "line_not_found" => 404,
                "line_already_bound" => 409,
                _ => 400
            };
            return Results.Json(new { ok = false, error = result.Error, currentAgentId = result.CurrentAgentId }, statusCode: status);
        });

        group.MapDelete("/tenants/{tenantId:guid}/agents/{agentId:guid}/line-binding/{lineId:guid}", async (
            Guid tenantId,
            Guid agentId,
            Guid lineId,
            IAdminAgentService svc,
            IAuditWriter audit,
            IApplicationDbContext db,
            HttpContext http,
            CancellationToken ct) =>
        {
            var removed = await svc.UnbindLineAsync(tenantId, agentId, lineId, ct);
            if (!removed) { return Results.NotFound(); }
            audit.Write(Actor(http), "AI_AGENT_ADMIN_UNBIND", nameof(Domain.Entities.AiAgentLineBinding),
                entityId: null,
                previousValue: new { agentId, lineId },
                newValue: null,
                tenantId: tenantId);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });
    }

    /// <summary>Actor del JWT (sub) para la fila de auditoria.</summary>
    private static Guid Actor(HttpContext http)
    {
        var raw = http.User.FindFirstValue("sub");
        return Guid.TryParse(raw, out var id) ? id : Guid.Empty;
    }
}
