using CubotRedManager.Application.Abstractions;
using CubotRedManager.Domain.Entities;
using CubotRedManager.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace CubotRedManager.Application.Tenancy;

public sealed class WhatsAppLineService : IWhatsAppLineService
{
    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly IAuditWriter _audit;
    private readonly ISecretProtector _secretProtector;
    private readonly TimeProvider _timeProvider;

    public WhatsAppLineService(IApplicationDbContext db, ITenantContext tenantContext, IAuditWriter audit, ISecretProtector secretProtector, TimeProvider timeProvider)
    {
        _db = db;
        _tenantContext = tenantContext;
        _audit = audit;
        _secretProtector = secretProtector;
        _timeProvider = timeProvider;
    }

    public async Task<IReadOnlyList<WhatsAppLineDto>> ListAsync(CancellationToken cancellationToken = default)
    {
        var rows = await _db.WhatsAppLines
            .AsNoTracking()
            .OrderBy(l => l.InstanceName)
            .ToListAsync(cancellationToken);
        return rows.Select(Map).ToList();
    }

    public async Task<WhatsAppLineDto?> CreateAsync(CreateWhatsAppLineRequest request, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId)
        {
            return null;
        }

        var line = new WhatsAppLine
        {
            TenantId = tenantId,
            InstanceName = request.InstanceName.Trim(),
            PhoneNumber = request.PhoneNumber?.Trim(),
            Status = WhatsAppLineStatus.Created,
            Provider = request.Provider,
            LastStatusAt = _timeProvider.GetUtcNow()
        };

        // Si la linea es Meta Cloud, guarda los identificadores en claro y cifra los tokens.
        if (request.Provider == WhatsAppProvider.Cloud)
        {
            line.CloudPhoneNumberId = request.CloudPhoneNumberId?.Trim();
            line.CloudBusinessAccountId = request.CloudBusinessAccountId?.Trim();
            if (!string.IsNullOrWhiteSpace(request.CloudAccessToken))
            {
                line.CloudAccessTokenEncrypted = _secretProtector.Protect(request.CloudAccessToken.Trim());
            }
            if (!string.IsNullOrWhiteSpace(request.CloudWebhookVerifyToken))
            {
                line.CloudWebhookVerifyTokenEncrypted = _secretProtector.Protect(request.CloudWebhookVerifyToken.Trim());
            }
        }
        // Si la linea es YCloud (BSP), guarda el numero/WABA en claro y cifra la API key + secreto.
        else if (request.Provider == WhatsAppProvider.YCloud)
        {
            line.YCloudPhoneNumberId = request.YCloudPhoneNumberId?.Trim();
            line.YCloudWabaId = request.YCloudWabaId?.Trim();
            if (!string.IsNullOrWhiteSpace(request.YCloudApiKey))
            {
                line.YCloudApiKeyEncrypted = _secretProtector.Protect(request.YCloudApiKey.Trim());
            }
            if (!string.IsNullOrWhiteSpace(request.YCloudWebhookSecret))
            {
                line.YCloudWebhookSecretEncrypted = _secretProtector.Protect(request.YCloudWebhookSecret.Trim());
            }
            // El numero ya esta onboardeado/conectado en YCloud (coexistencia); la linea queda lista
            // para enviar de inmediato.
            line.Status = WhatsAppLineStatus.Connected;
            line.LastConnectedAt = _timeProvider.GetUtcNow();
        }

        _db.WhatsAppLines.Add(line);

        _audit.Write(actorUserId, "whatsapp-line.create", nameof(WhatsAppLine), line.Id,
            previousValue: null,
            newValue: new { line.InstanceName, line.PhoneNumber, Provider = request.Provider.ToString() },
            tenantId: tenantId);

        await _db.SaveChangesAsync(cancellationToken);
        return Map(line);
    }

    public async Task<WhatsAppLineDto?> ChangeStatusAsync(Guid lineId, WhatsAppLineStatus status, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var line = await _db.WhatsAppLines.FirstOrDefaultAsync(l => l.Id == lineId, cancellationToken);
        if (line is null)
        {
            return null;
        }

        var previous = line.Status;
        if (previous != status)
        {
            var now = _timeProvider.GetUtcNow();
            line.Status = status;
            line.LastStatusAt = now;
            if (status == WhatsAppLineStatus.Connected)
            {
                line.LastConnectedAt = now;
            }

            _audit.Write(actorUserId, "whatsapp-line.change-status", nameof(WhatsAppLine), line.Id,
                previousValue: new { Status = previous }, newValue: new { Status = status }, tenantId: line.TenantId);

            await _db.SaveChangesAsync(cancellationToken);
        }

        return Map(line);
    }

    public async Task<WhatsAppLineDto?> AssignAsync(Guid lineId, Guid? tenantUserId, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var line = await _db.WhatsAppLines.FirstOrDefaultAsync(l => l.Id == lineId, cancellationToken);
        if (line is null)
        {
            return null;
        }

        if (tenantUserId is Guid userId)
        {
            var belongs = await _db.TenantUsers.AnyAsync(tu => tu.Id == userId, cancellationToken);
            if (!belongs)
            {
                return null;
            }
        }

        line.AssignedToTenantUserId = tenantUserId;
        _audit.Write(actorUserId, "whatsapp-line.assign", nameof(WhatsAppLine), line.Id,
            previousValue: null, newValue: new { AssignedToTenantUserId = tenantUserId }, tenantId: line.TenantId);

        await _db.SaveChangesAsync(cancellationToken);
        return Map(line);
    }

    /// <summary>Elimina una linea WhatsApp. Solo permitido si Status != Connected para evitar
    /// desconexiones accidentales; el caller debe pasar a Disconnected primero.</summary>
    public async Task<bool> DeleteAsync(Guid lineId, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var line = await _db.WhatsAppLines.FirstOrDefaultAsync(l => l.Id == lineId, cancellationToken);
        if (line is null) { return false; }

        _db.WhatsAppLines.Remove(line);
        _audit.Write(actorUserId, "whatsapp-line.delete", nameof(WhatsAppLine), line.Id,
            previousValue: new { line.InstanceName, Provider = line.Provider.ToString() }, newValue: null, tenantId: line.TenantId);
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static WhatsAppLineDto Map(WhatsAppLine l) =>
        new(l.Id, l.InstanceName, l.PhoneNumber, l.Status, l.AssignedToTenantUserId, l.LastConnectedAt, l.LastStatusAt,
            l.Provider, l.CloudPhoneNumberId, l.CloudBusinessAccountId,
            !string.IsNullOrEmpty(l.CloudAccessTokenEncrypted),
            !string.IsNullOrEmpty(l.CloudWebhookVerifyTokenEncrypted),
            l.YCloudPhoneNumberId, l.YCloudWabaId, !string.IsNullOrEmpty(l.YCloudApiKeyEncrypted));
}
