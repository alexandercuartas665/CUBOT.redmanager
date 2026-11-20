using CubotRedManager.Application.Abstractions;
using CubotRedManager.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CubotRedManager.Application.Tenancy;

public sealed class ContactService : IContactService
{
    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly TimeProvider _time;
    private readonly IAuditWriter _audit;

    public ContactService(IApplicationDbContext db, ITenantContext tenantContext, TimeProvider time, IAuditWriter audit)
    {
        _db = db;
        _tenantContext = tenantContext;
        _time = time;
        _audit = audit;
    }

    public async Task<IReadOnlyList<ContactDto>> ListAsync(string? query = null, bool includeArchived = false, int take = 100, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid) { return Array.Empty<ContactDto>(); }
        var q = _db.Contacts.AsNoTracking();
        if (!includeArchived) { q = q.Where(c => c.ArchivedAt == null); }
        if (!string.IsNullOrWhiteSpace(query))
        {
            var needle = query.Trim().ToLower();
            q = q.Where(c => c.Name.ToLower().Contains(needle)
                          || c.Phone.Contains(needle)
                          || (c.Email != null && c.Email.ToLower().Contains(needle)));
        }
        var rows = await q.OrderByDescending(c => c.LastOrderAt ?? c.CreatedAt)
            .Take(Math.Clamp(take, 1, 500))
            .ToListAsync(cancellationToken);
        return rows.Select(ToDto).ToList();
    }

    public async Task<ContactDetailDto?> GetAsync(Guid contactId, CancellationToken cancellationToken = default)
    {
        var contact = await _db.Contacts.AsNoTracking().FirstOrDefaultAsync(c => c.Id == contactId, cancellationToken);
        if (contact is null) { return null; }
        var orders = await _db.ContactOrders.AsNoTracking()
            .Where(o => o.ContactId == contactId)
            .OrderByDescending(o => o.OrderedAt)
            .ToListAsync(cancellationToken);
        return new ContactDetailDto(ToDto(contact), orders.Select(ToDto).ToList());
    }

    public async Task<ContactDto?> CreateAsync(CreateContactRequest request, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId) { return null; }
        var phone = NormalizePhone(request.Phone);
        if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(phone)) { return null; }
        var exists = await _db.Contacts.AnyAsync(c => c.Phone == phone, cancellationToken);
        if (exists) { return null; } // Caller debe usar UpsertByPhoneAsync si quiere upsert.

        var contact = new Contact
        {
            TenantId = tenantId,
            Name = request.Name.Trim(),
            Phone = phone,
            Email = Trim(request.Email),
            Country = Trim(request.Country),
            City = Trim(request.City),
            BirthDate = request.BirthDate,
            LeadId = request.LeadId,
            ConversationId = request.ConversationId,
            Notes = Trim(request.Notes)
        };
        _db.Contacts.Add(contact);
        _audit.Write(actorUserId, "contact.create", nameof(Contact), contact.Id,
            previousValue: null, newValue: new { contact.Name, contact.Phone }, tenantId: tenantId);
        await _db.SaveChangesAsync(cancellationToken);
        return ToDto(contact);
    }

    public async Task<ContactDto> UpsertByPhoneAsync(CreateContactRequest request, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var phone = NormalizePhone(request.Phone);
        var existing = await _db.Contacts.FirstOrDefaultAsync(c => c.Phone == phone, cancellationToken);
        if (existing is not null)
        {
            // Rellena campos faltantes sin pisar los existentes (idempotente para el hook del lead).
            bool changed = false;
            if (string.IsNullOrWhiteSpace(existing.Email) && !string.IsNullOrWhiteSpace(request.Email)) { existing.Email = request.Email!.Trim(); changed = true; }
            if (string.IsNullOrWhiteSpace(existing.Country) && !string.IsNullOrWhiteSpace(request.Country)) { existing.Country = request.Country!.Trim(); changed = true; }
            if (string.IsNullOrWhiteSpace(existing.City) && !string.IsNullOrWhiteSpace(request.City)) { existing.City = request.City!.Trim(); changed = true; }
            if (existing.LeadId is null && request.LeadId is not null) { existing.LeadId = request.LeadId; changed = true; }
            if (existing.ConversationId is null && request.ConversationId is not null) { existing.ConversationId = request.ConversationId; changed = true; }
            if (changed) { await _db.SaveChangesAsync(cancellationToken); }
            return ToDto(existing);
        }
        var created = await CreateAsync(request, actorUserId, cancellationToken);
        return created!; // CreateAsync no es null aqui porque ya confirmamos que no existe.
    }

    public async Task<ContactDto?> UpdateAsync(Guid contactId, UpdateContactRequest request, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var contact = await _db.Contacts.FirstOrDefaultAsync(c => c.Id == contactId, cancellationToken);
        if (contact is null) { return null; }
        if (!string.IsNullOrWhiteSpace(request.Name)) { contact.Name = request.Name.Trim(); }
        if (request.Email is not null) { contact.Email = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim(); }
        if (request.Country is not null) { contact.Country = string.IsNullOrWhiteSpace(request.Country) ? null : request.Country.Trim(); }
        if (request.City is not null) { contact.City = string.IsNullOrWhiteSpace(request.City) ? null : request.City.Trim(); }
        if (request.BirthDate is not null) { contact.BirthDate = request.BirthDate; }
        if (request.Notes is not null) { contact.Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(); }
        if (request.Archived is bool arch) { contact.ArchivedAt = arch ? _time.GetUtcNow() : null; }
        _audit.Write(actorUserId, "contact.update", nameof(Contact), contact.Id,
            previousValue: null, newValue: new { contact.Name, Archived = contact.ArchivedAt is not null }, tenantId: contact.TenantId);
        await _db.SaveChangesAsync(cancellationToken);
        return ToDto(contact);
    }

    public async Task<bool> DeleteAsync(Guid contactId, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var contact = await _db.Contacts.FirstOrDefaultAsync(c => c.Id == contactId, cancellationToken);
        if (contact is null) { return false; }
        // Borramos ordenes dependientes primero (no hay cascade declarado).
        var orders = await _db.ContactOrders.Where(o => o.ContactId == contactId).ToListAsync(cancellationToken);
        foreach (var o in orders) { _db.ContactOrders.Remove(o); }
        _db.Contacts.Remove(contact);
        _audit.Write(actorUserId, "contact.delete", nameof(Contact), contactId,
            previousValue: new { contact.Name, contact.Phone, ordersBorradas = orders.Count }, newValue: null, tenantId: contact.TenantId);
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    // === Ordenes ===

    public async Task<IReadOnlyList<ContactOrderDto>> ListOrdersAsync(Guid contactId, CancellationToken cancellationToken = default)
    {
        var rows = await _db.ContactOrders.AsNoTracking()
            .Where(o => o.ContactId == contactId)
            .OrderByDescending(o => o.OrderedAt)
            .ToListAsync(cancellationToken);
        return rows.Select(ToDto).ToList();
    }

    public async Task<ContactOrderDto?> AddOrderAsync(Guid contactId, CreateContactOrderRequest request, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var contact = await _db.Contacts.FirstOrDefaultAsync(c => c.Id == contactId, cancellationToken);
        if (contact is null) { return null; }
        // Validacion basica de JSON si viene (que parsee; el schema de items no se valida aqui).
        if (!string.IsNullOrWhiteSpace(request.ItemsJson))
        {
            try { using var _ = System.Text.Json.JsonDocument.Parse(request.ItemsJson); }
            catch { return null; }
        }
        var order = new ContactOrder
        {
            TenantId = contact.TenantId,
            ContactId = contactId,
            OrderedAt = request.OrderedAt,
            TotalAmount = request.TotalAmount,
            Currency = Trim(request.Currency)?.ToUpperInvariant(),
            ItemsJson = Trim(request.ItemsJson),
            Channel = string.IsNullOrWhiteSpace(request.Channel) ? "Manual" : request.Channel!.Trim(),
            ExternalRef = Trim(request.ExternalRef),
            Notes = Trim(request.Notes)
        };
        _db.ContactOrders.Add(order);
        await _db.SaveChangesAsync(cancellationToken);
        await RecalculateMetricsAsync(contactId, cancellationToken);
        _audit.Write(actorUserId, "contact.order.add", nameof(ContactOrder), order.Id,
            previousValue: null, newValue: new { contactId, order.TotalAmount, order.Currency, order.Channel }, tenantId: contact.TenantId);
        return ToDto(order);
    }

    public async Task<bool> DeleteOrderAsync(Guid contactId, Guid orderId, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var order = await _db.ContactOrders.FirstOrDefaultAsync(o => o.Id == orderId && o.ContactId == contactId, cancellationToken);
        if (order is null) { return false; }
        _db.ContactOrders.Remove(order);
        await _db.SaveChangesAsync(cancellationToken);
        await RecalculateMetricsAsync(contactId, cancellationToken);
        _audit.Write(actorUserId, "contact.order.delete", nameof(ContactOrder), orderId,
            previousValue: new { order.TotalAmount, order.Currency, order.OrderedAt }, newValue: null, tenantId: order.TenantId);
        return true;
    }

    /// <summary>Recalcula FirstOrderAt, LastOrderAt, OrderCount, LifetimeValue (si hay moneda
    /// unica), CadenceDays (promedio de dias entre ordenes consecutivas) y NextExpectedAt
    /// (= LastOrderAt + CadenceDays). Se invoca tras cada add/delete de orden para mantener los
    /// derivados del Contacto siempre consistentes con el historial real.</summary>
    private async Task RecalculateMetricsAsync(Guid contactId, CancellationToken ct)
    {
        var contact = await _db.Contacts.FirstOrDefaultAsync(c => c.Id == contactId, ct);
        if (contact is null) { return; }
        var orders = await _db.ContactOrders.AsNoTracking()
            .Where(o => o.ContactId == contactId)
            .OrderBy(o => o.OrderedAt)
            .ToListAsync(ct);

        contact.OrderCount = orders.Count;
        contact.FirstOrderAt = orders.Count > 0 ? orders[0].OrderedAt : null;
        contact.LastOrderAt = orders.Count > 0 ? orders[^1].OrderedAt : null;

        // LTV: solo si TODAS las ordenes con monto usan la misma moneda. Si hay mezcla, dejamos
        // LifetimeValue null y la UI muestra breakdown aparte (TODO Fase 2). No sumar peras +
        // manzanas evita que un reporte muestre "cliente vale 420000" con 2 COP + 1 USD.
        var withAmount = orders.Where(o => o.TotalAmount is not null).ToList();
        var currencies = withAmount.Select(o => o.Currency).Where(c => !string.IsNullOrEmpty(c)).Distinct().ToList();
        if (withAmount.Count > 0 && currencies.Count == 1)
        {
            contact.LifetimeValue = withAmount.Sum(o => o.TotalAmount ?? 0);
            contact.LifetimeValueCurrency = currencies[0];
        }
        else
        {
            contact.LifetimeValue = null;
            contact.LifetimeValueCurrency = null;
        }

        // Cadencia: promedio de dias entre ordenes consecutivas (minimo 2 ordenes). Si el
        // cliente compra irregularmente (grande desviacion), igual tomamos el promedio simple —
        // con mas data historica se puede refinar en Fase 2 (ej. median + alerta de outlier).
        if (orders.Count >= 2)
        {
            double total = 0;
            for (int i = 1; i < orders.Count; i++)
            {
                total += (orders[i].OrderedAt - orders[i - 1].OrderedAt).TotalDays;
            }
            var avg = total / (orders.Count - 1);
            contact.CadenceDays = (int)Math.Round(avg);
            if (contact.CadenceDays is int cd and > 0 && contact.LastOrderAt is DateTimeOffset last)
            {
                contact.NextExpectedAt = last.AddDays(cd);
            }
            else { contact.NextExpectedAt = null; }
        }
        else
        {
            contact.CadenceDays = null;
            contact.NextExpectedAt = null;
        }

        await _db.SaveChangesAsync(ct);
    }

    // === Helpers ===

    private static string NormalizePhone(string phone)
    {
        if (string.IsNullOrWhiteSpace(phone)) { return string.Empty; }
        var trimmed = phone.Trim();
        return trimmed.StartsWith("+") ? trimmed.Substring(1) : trimmed;
    }

    private static string? Trim(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static ContactDto ToDto(Contact c) => new(
        c.Id, c.Name, c.Phone, c.Email, c.Country, c.City, c.BirthDate,
        c.LeadId, c.ConversationId,
        c.FirstOrderAt, c.LastOrderAt, c.OrderCount, c.LifetimeValue, c.LifetimeValueCurrency,
        c.CadenceDays, c.NextExpectedAt, c.Notes, c.ArchivedAt is not null);

    private static ContactOrderDto ToDto(ContactOrder o) => new(
        o.Id, o.ContactId, o.OrderedAt, o.TotalAmount, o.Currency, o.ItemsJson,
        o.Channel, o.ExternalRef, o.Notes, o.CreatedAt);
}
