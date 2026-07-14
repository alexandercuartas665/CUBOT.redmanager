using CubotRedManager.Application.Abstractions;
using CubotRedManager.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CubotRedManager.Application.Tenancy;

/// <summary>Portado 1:1 desde CUBOT.travels (AiAgentLineBindingService).</summary>
public sealed class AiAgentLineBindingService : IAiAgentLineBindingService
{
    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly IAuditWriter _audit;

    public AiAgentLineBindingService(IApplicationDbContext db, ITenantContext tenantContext, IAuditWriter audit)
    {
        _db = db;
        _tenantContext = tenantContext;
        _audit = audit;
    }

    public async Task<IReadOnlyList<AgentLineBindingRowDto>> ListForAgentAsync(Guid agentId, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId) { return Array.Empty<AgentLineBindingRowDto>(); }

        // Listamos todas las lineas del tenant. Para cada una buscamos su binding activo
        // (si lo hay) y resolvemos a que agente pertenece.
        var lines = await _db.WhatsAppLines.AsNoTracking()
            .OrderBy(l => l.InstanceName)
            .Select(l => new { l.Id, l.InstanceName, l.PhoneNumber })
            .ToListAsync(cancellationToken);

        var activeBindings = await _db.AiAgentLineBindings.AsNoTracking()
            .Where(b => b.IsConnected)
            .Select(b => new { b.WhatsAppLineId, b.AgentId, b.AutoConfirm })
            .ToListAsync(cancellationToken);

        var agentNames = await _db.AiAgents.AsNoTracking()
            .Select(a => new { a.Id, a.Name })
            .ToDictionaryAsync(a => a.Id, a => a.Name, cancellationToken);

        var rows = new List<AgentLineBindingRowDto>(lines.Count);
        foreach (var line in lines)
        {
            var binding = activeBindings.FirstOrDefault(b => b.WhatsAppLineId == line.Id);
            var isBoundHere = binding is not null && binding.AgentId == agentId;
            var boundAgentId = binding?.AgentId;
            var boundAgentName = boundAgentId is Guid bid && agentNames.TryGetValue(bid, out var name) ? name : null;
            rows.Add(new AgentLineBindingRowDto(
                line.Id,
                line.InstanceName,
                line.PhoneNumber,
                isBoundHere,
                binding?.AutoConfirm ?? true,
                boundAgentId,
                boundAgentName));
        }
        return rows;
    }

    public async Task<bool> SetAsync(SetAgentLineBindingRequest request, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId) { return false; }

        // Verifica que ambos recursos pertenecen al tenant.
        var agent = await _db.AiAgents.FirstOrDefaultAsync(a => a.Id == request.AgentId, cancellationToken);
        if (agent is null) { return false; }
        var line = await _db.WhatsAppLines.FirstOrDefaultAsync(l => l.Id == request.WhatsAppLineId, cancellationToken);
        if (line is null) { return false; }

        // Busca CUALQUIER binding activo previo de esa linea. IgnoreQueryFilters() evita
        // que un cambio futuro de politica de tenant nos genere sorpresas.
        var existingActive = await _db.AiAgentLineBindings
            .IgnoreQueryFilters()
            .Where(b => b.TenantId == tenantId && b.WhatsAppLineId == request.WhatsAppLineId && b.IsConnected)
            .FirstOrDefaultAsync(cancellationToken);

        if (!request.Connected)
        {
            // Solo desconectamos si el binding activo pertenece al agente solicitado.
            if (existingActive is null || existingActive.AgentId != request.AgentId) { return false; }
            existingActive.IsConnected = false;
            _audit.Write(actorUserId, "ai-agent.unbind-line", nameof(AiAgentLineBinding), existingActive.Id,
                previousValue: new { request.AgentId, request.WhatsAppLineId, IsConnected = true },
                newValue: new { request.AgentId, request.WhatsAppLineId, IsConnected = false },
                tenantId: tenantId);
            await _db.SaveChangesAsync(cancellationToken);
            return true;
        }

        // Connected = true.
        if (existingActive is not null && existingActive.AgentId != request.AgentId)
        {
            // Linea ocupada por otro agente. No reasignamos por accidente.
            return false;
        }

        if (existingActive is not null && existingActive.AgentId == request.AgentId)
        {
            // Ya estaba activa; solo actualizamos AutoConfirm si cambio.
            if (existingActive.AutoConfirm != request.AutoConfirm)
            {
                existingActive.AutoConfirm = request.AutoConfirm;
                await _db.SaveChangesAsync(cancellationToken);
            }
            return true;
        }

        // Reutiliza un binding historico (inactivo) si existe para este agente y linea.
        var historic = await _db.AiAgentLineBindings
            .IgnoreQueryFilters()
            .Where(b => b.TenantId == tenantId
                     && b.WhatsAppLineId == request.WhatsAppLineId
                     && b.AgentId == request.AgentId
                     && !b.IsConnected)
            .FirstOrDefaultAsync(cancellationToken);
        if (historic is not null)
        {
            historic.IsConnected = true;
            historic.AutoConfirm = request.AutoConfirm;
            _audit.Write(actorUserId, "ai-agent.bind-line", nameof(AiAgentLineBinding), historic.Id,
                previousValue: null,
                newValue: new { request.AgentId, request.WhatsAppLineId, Reactivated = true },
                tenantId: tenantId);
            await _db.SaveChangesAsync(cancellationToken);
            return true;
        }

        var binding = new AiAgentLineBinding
        {
            TenantId = tenantId,
            AgentId = request.AgentId,
            WhatsAppLineId = request.WhatsAppLineId,
            IsConnected = true,
            AutoConfirm = request.AutoConfirm
        };
        _db.AiAgentLineBindings.Add(binding);
        await _db.SaveChangesAsync(cancellationToken);
        _audit.Write(actorUserId, "ai-agent.bind-line", nameof(AiAgentLineBinding), binding.Id,
            previousValue: null,
            newValue: new { binding.AgentId, binding.WhatsAppLineId, binding.AutoConfirm },
            tenantId: tenantId);
        return true;
    }
}
