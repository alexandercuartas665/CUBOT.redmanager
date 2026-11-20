using System.Text;
using System.Text.Json;
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

    // ===================================================================================
    // Contactos
    // ===================================================================================

    public async Task<IReadOnlyList<ContactDto>> ListAsync(string? query = null, bool includeArchived = false, int take = 100,
        string? lifecycle = null, string? tag = null, CancellationToken cancellationToken = default)
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
        if (!string.IsNullOrWhiteSpace(tag))
        {
            var t = NormalizeTag(tag);
            q = q.Where(c => c.Tags != null && ("," + c.Tags + ",").Contains("," + t + ","));
        }
        var rows = await q.OrderByDescending(c => c.LastOrderAt ?? c.CreatedAt)
            .Take(Math.Clamp(take, 1, 500))
            .ToListAsync(cancellationToken);
        var dtos = rows.Select(ToDto);
        // Ciclo de vida depende de la fecha actual: se filtra en memoria tras calcularlo.
        if (!string.IsNullOrWhiteSpace(lifecycle))
        {
            dtos = dtos.Where(d => string.Equals(d.Lifecycle, lifecycle, StringComparison.OrdinalIgnoreCase));
        }
        return dtos.ToList();
    }

    public async Task<ContactDetailDto?> GetAsync(Guid contactId, CancellationToken cancellationToken = default)
    {
        var contact = await _db.Contacts.AsNoTracking().FirstOrDefaultAsync(c => c.Id == contactId, cancellationToken);
        if (contact is null) { return null; }
        var orders = await _db.ContactOrders.AsNoTracking()
            .Where(o => o.ContactId == contactId)
            .OrderByDescending(o => o.OrderedAt)
            .ToListAsync(cancellationToken);
        var products = ContactInsights.ProductAffinity(orders.Select(o => new ContactInsights.OrderItems(o.OrderedAt, o.ItemsJson)));
        return new ContactDetailDto(ToDto(contact), orders.Select(ToDto).ToList(), products);
    }

    public async Task<ContactDto?> CreateAsync(CreateContactRequest request, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId) { return null; }
        var phone = NormalizePhone(request.Phone);
        if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(phone)) { return null; }
        if (await _db.Contacts.AnyAsync(c => c.Phone == phone, cancellationToken)) { return null; }

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
            bool changed = false;
            if (string.IsNullOrWhiteSpace(existing.Email) && !string.IsNullOrWhiteSpace(request.Email)) { existing.Email = request.Email!.Trim(); changed = true; }
            if (string.IsNullOrWhiteSpace(existing.Country) && !string.IsNullOrWhiteSpace(request.Country)) { existing.Country = request.Country!.Trim(); changed = true; }
            if (string.IsNullOrWhiteSpace(existing.City) && !string.IsNullOrWhiteSpace(request.City)) { existing.City = request.City!.Trim(); changed = true; }
            if (existing.LeadId is null && request.LeadId is not null) { existing.LeadId = request.LeadId; changed = true; }
            if (existing.ConversationId is null && request.ConversationId is not null) { existing.ConversationId = request.ConversationId; changed = true; }
            if (changed) { await _db.SaveChangesAsync(cancellationToken); }
            return ToDto(existing);
        }
        return (await CreateAsync(request, actorUserId, cancellationToken))!;
    }

    public async Task<ContactDto?> UpdateAsync(Guid contactId, UpdateContactRequest request, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var contact = await _db.Contacts.FirstOrDefaultAsync(c => c.Id == contactId, cancellationToken);
        if (contact is null) { return null; }
        if (!string.IsNullOrWhiteSpace(request.Name)) { contact.Name = request.Name.Trim(); }
        if (request.Email is not null) { contact.Email = Trim(request.Email); }
        if (request.Country is not null) { contact.Country = Trim(request.Country); }
        if (request.City is not null) { contact.City = Trim(request.City); }
        if (request.BirthDate is not null) { contact.BirthDate = request.BirthDate; }
        if (request.Notes is not null) { contact.Notes = Trim(request.Notes); }
        if (request.Archived is bool arch) { contact.ArchivedAt = arch ? _time.GetUtcNow() : null; }
        _audit.Write(actorUserId, "contact.update", nameof(Contact), contact.Id,
            previousValue: null, newValue: new { contact.Name, Archived = contact.ArchivedAt is not null }, tenantId: contact.TenantId);
        await _db.SaveChangesAsync(cancellationToken);
        return ToDto(contact);
    }

    public async Task<ContactDto?> UpdateProfileAsync(Guid contactId, UpdateContactProfileRequest request, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var contact = await _db.Contacts.FirstOrDefaultAsync(c => c.Id == contactId, cancellationToken);
        if (contact is null) { return null; }

        if (request.FieldValues is not null)
        {
            var current = ParseValues(contact.FieldValuesJson);
            foreach (var (key, value) in request.FieldValues)
            {
                if (string.IsNullOrWhiteSpace(key)) { continue; }
                if (string.IsNullOrWhiteSpace(value)) { current.Remove(key); }
                else { current[key] = value.Trim(); }
            }
            contact.FieldValuesJson = current.Count > 0 ? JsonSerializer.Serialize(current) : null;
        }
        if (request.Tags is not null)
        {
            var tags = request.Tags.Select(NormalizeTag).Where(t => t.Length > 0).Distinct().ToList();
            contact.Tags = tags.Count > 0 ? string.Join(",", tags) : null;
        }
        // Sin valores en el audit: el perfil puede tener datos de salud (sensibles, Ley 1581).
        _audit.Write(actorUserId, "contact.profile.update", nameof(Contact), contact.Id,
            previousValue: null, newValue: new { fields = request.FieldValues?.Keys.ToList(), tagsUpdated = request.Tags is not null },
            tenantId: contact.TenantId);
        await _db.SaveChangesAsync(cancellationToken);
        return ToDto(contact);
    }

    public async Task<bool> DeleteAsync(Guid contactId, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var contact = await _db.Contacts.FirstOrDefaultAsync(c => c.Id == contactId, cancellationToken);
        if (contact is null) { return false; }
        var orders = await _db.ContactOrders.Where(o => o.ContactId == contactId).ToListAsync(cancellationToken);
        foreach (var o in orders) { _db.ContactOrders.Remove(o); }
        _db.Contacts.Remove(contact);
        _audit.Write(actorUserId, "contact.delete", nameof(Contact), contactId,
            previousValue: new { contact.Name, contact.Phone, ordersBorradas = orders.Count }, newValue: null, tenantId: contact.TenantId);
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<IReadOnlyList<string>> ListTagsAsync(CancellationToken cancellationToken = default)
    {
        var raw = await _db.Contacts.AsNoTracking()
            .Where(c => c.Tags != null)
            .Select(c => c.Tags!)
            .ToListAsync(cancellationToken);
        return raw.SelectMany(t => t.Split(',', StringSplitOptions.RemoveEmptyEntries))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(t => t)
            .ToList();
    }

    // ===================================================================================
    // Ordenes
    // ===================================================================================

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
        if (!string.IsNullOrWhiteSpace(request.ItemsJson))
        {
            try { using var _ = JsonDocument.Parse(request.ItemsJson); }
            catch (JsonException) { return null; }
        }
        var order = new ContactOrder
        {
            TenantId = contact.TenantId,
            ContactId = contactId,
            OrderedAt = request.OrderedAt,
            TotalAmount = request.TotalAmount,
            Currency = Trim(request.Currency)?.ToUpperInvariant(),
            ItemsJson = Trim(request.ItemsJson),
            Channel = string.IsNullOrWhiteSpace(request.Channel) ? "Manual" : request.Channel.Trim(),
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

        // LTV solo si todas las ordenes con monto comparten moneda (no sumar COP + USD).
        var withAmount = orders.Where(o => o.TotalAmount is not null).ToList();
        var currencies = withAmount.Select(o => o.Currency).Where(c => !string.IsNullOrEmpty(c)).Distinct().ToList();
        if (withAmount.Count > 0 && currencies.Count <= 1)
        {
            contact.LifetimeValue = withAmount.Sum(o => o.TotalAmount ?? 0);
            contact.LifetimeValueCurrency = currencies.FirstOrDefault();
        }
        else
        {
            contact.LifetimeValue = null;
            contact.LifetimeValueCurrency = null;
        }

        if (orders.Count >= 2)
        {
            double total = 0;
            for (int i = 1; i < orders.Count; i++) { total += (orders[i].OrderedAt - orders[i - 1].OrderedAt).TotalDays; }
            contact.CadenceDays = (int)Math.Round(total / (orders.Count - 1));
            contact.NextExpectedAt = contact.CadenceDays > 0 && contact.LastOrderAt is DateTimeOffset last
                ? last.AddDays(contact.CadenceDays.Value)
                : null;
        }
        else
        {
            contact.CadenceDays = null;
            contact.NextExpectedAt = null;
        }
        await _db.SaveChangesAsync(ct);
    }

    // ===================================================================================
    // Campos del perfil
    // ===================================================================================

    public async Task<IReadOnlyList<ContactFieldDto>> ListFieldsAsync(bool includeHidden = true, CancellationToken cancellationToken = default)
    {
        await EnsureDefaultFieldsAsync(cancellationToken);
        var q = _db.ContactFieldDefinitions.AsNoTracking();
        if (!includeHidden) { q = q.Where(f => !f.IsHidden); }
        var rows = await q.ToListAsync(cancellationToken);
        return rows
            .OrderBy(f => SectionRank(f.Section)).ThenBy(f => f.Section).ThenBy(f => f.SortOrder)
            .Select(ToDto)
            .ToList();
    }

    public async Task<ContactFieldDto?> CreateFieldAsync(SaveContactFieldRequest request, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId) { return null; }
        if (string.IsNullOrWhiteSpace(request.Label) || string.IsNullOrWhiteSpace(request.Section)) { return null; }
        var existingKeys = await _db.ContactFieldDefinitions.Select(f => f.FieldKey).ToListAsync(cancellationToken);
        var key = UniqueKey(Slugify(request.Label), existingKeys);
        var section = request.Section.Trim();
        var nextOrder = (await _db.ContactFieldDefinitions.Where(f => f.Section == section)
            .Select(f => (int?)f.SortOrder).MaxAsync(cancellationToken) ?? -1) + 1;

        var field = new ContactFieldDefinition
        {
            TenantId = tenantId,
            Section = section,
            FieldKey = key,
            Label = request.Label.Trim(),
            FieldType = request.FieldType,
            Options = NormalizeOptions(request.Options),
            Description = Trim(request.Description),
            Column = request.Column >= 2 ? 2 : 1,
            SortOrder = nextOrder,
            ShowInList = request.ShowInList,
            ShowInFilter = request.ShowInFilter,
            IsHidden = request.IsHidden
        };
        _db.ContactFieldDefinitions.Add(field);
        _audit.Write(actorUserId, "contact.field.create", nameof(ContactFieldDefinition), field.Id,
            previousValue: null, newValue: new { field.Section, field.FieldKey, field.FieldType }, tenantId: tenantId);
        await _db.SaveChangesAsync(cancellationToken);
        return ToDto(field);
    }

    public async Task<ContactFieldDto?> UpdateFieldAsync(Guid fieldId, SaveContactFieldRequest request, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var field = await _db.ContactFieldDefinitions.FirstOrDefaultAsync(f => f.Id == fieldId, cancellationToken);
        if (field is null || string.IsNullOrWhiteSpace(request.Label)) { return null; }
        var newSection = string.IsNullOrWhiteSpace(request.Section) ? field.Section : request.Section.Trim();
        if (newSection != field.Section)
        {
            field.SortOrder = (await _db.ContactFieldDefinitions.Where(f => f.Section == newSection)
                .Select(f => (int?)f.SortOrder).MaxAsync(cancellationToken) ?? -1) + 1;
            field.Section = newSection;
        }
        field.Label = request.Label.Trim();
        field.FieldType = request.FieldType;
        field.Options = NormalizeOptions(request.Options);
        field.Description = Trim(request.Description);
        field.Column = request.Column >= 2 ? 2 : 1;
        field.ShowInList = request.ShowInList;
        field.ShowInFilter = request.ShowInFilter;
        field.IsHidden = request.IsHidden;
        _audit.Write(actorUserId, "contact.field.update", nameof(ContactFieldDefinition), field.Id,
            previousValue: null, newValue: new { field.Section, field.FieldKey, field.FieldType, field.IsHidden }, tenantId: field.TenantId);
        await _db.SaveChangesAsync(cancellationToken);
        return ToDto(field);
    }

    public async Task<bool> DeleteFieldAsync(Guid fieldId, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var field = await _db.ContactFieldDefinitions.FirstOrDefaultAsync(f => f.Id == fieldId, cancellationToken);
        if (field is null) { return false; }
        if (field.IsSystem)
        {
            // Los presets se ocultan: integraciones (recordatorios, IA) dependen de su FieldKey.
            field.IsHidden = true;
        }
        else
        {
            _db.ContactFieldDefinitions.Remove(field);
        }
        _audit.Write(actorUserId, field.IsSystem ? "contact.field.hide" : "contact.field.delete", nameof(ContactFieldDefinition), field.Id,
            previousValue: new { field.FieldKey }, newValue: null, tenantId: field.TenantId);
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> MoveFieldAsync(Guid fieldId, int direction, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var field = await _db.ContactFieldDefinitions.FirstOrDefaultAsync(f => f.Id == fieldId, cancellationToken);
        if (field is null || direction == 0) { return false; }
        var siblings = await _db.ContactFieldDefinitions.Where(f => f.Section == field.Section)
            .OrderBy(f => f.SortOrder).ToListAsync(cancellationToken);
        var idx = siblings.FindIndex(f => f.Id == fieldId);
        var target = idx + Math.Sign(direction);
        if (target < 0 || target >= siblings.Count) { return false; }
        (siblings[idx], siblings[target]) = (siblings[target], siblings[idx]);
        for (int i = 0; i < siblings.Count; i++) { siblings[i].SortOrder = i; }
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <summary>Siembra los presets de marketing la primera vez que el tenant usa el perfil.</summary>
    private async Task EnsureDefaultFieldsAsync(CancellationToken ct)
    {
        if (_tenantContext.TenantId is not Guid tenantId) { return; }
        if (await _db.ContactFieldDefinitions.AnyAsync(ct)) { return; }
        var orderBySection = new Dictionary<string, int>();
        foreach (var p in ContactProfilePresets.All)
        {
            orderBySection.TryGetValue(p.Section, out var order);
            _db.ContactFieldDefinitions.Add(new ContactFieldDefinition
            {
                TenantId = tenantId,
                Section = p.Section,
                FieldKey = p.Key,
                Label = p.Label,
                FieldType = p.Type,
                Options = p.Options,
                Description = p.Description,
                Column = p.Column,
                SortOrder = order,
                ShowInList = p.ShowInList,
                ShowInFilter = p.ShowInFilter,
                IsSystem = true
            });
            orderBySection[p.Section] = order + 1;
        }
        await _db.SaveChangesAsync(ct);
    }

    // ===================================================================================
    // Helpers
    // ===================================================================================

    private static int SectionRank(string section)
    {
        for (int i = 0; i < ContactProfilePresets.SectionOrder.Count; i++)
        {
            if (string.Equals(ContactProfilePresets.SectionOrder[i], section, StringComparison.OrdinalIgnoreCase)) { return i; }
        }
        return ContactProfilePresets.SectionOrder.Count;
    }

    private static Dictionary<string, string?> ParseValues(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) { return new Dictionary<string, string?>(); }
        try { return JsonSerializer.Deserialize<Dictionary<string, string?>>(json) ?? new Dictionary<string, string?>(); }
        catch (JsonException) { return new Dictionary<string, string?>(); }
    }

    private static string NormalizePhone(string phone)
    {
        if (string.IsNullOrWhiteSpace(phone)) { return string.Empty; }
        var trimmed = phone.Trim();
        return trimmed.StartsWith('+') ? trimmed[1..] : trimmed;
    }

    private static string NormalizeTag(string tag) => (tag ?? string.Empty).Trim().ToLowerInvariant().Replace(",", " ");

    private static string? NormalizeOptions(string? options)
    {
        if (string.IsNullOrWhiteSpace(options)) { return null; }
        var lines = options.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase);
        var joined = string.Join("\n", lines);
        return joined.Length > 0 ? joined : null;
    }

    private static string Slugify(string label)
    {
        var normalized = label.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var ch in normalized)
        {
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch) == System.Globalization.UnicodeCategory.NonSpacingMark) { continue; }
            if (ch is >= 'a' and <= 'z' or >= '0' and <= '9') { sb.Append(ch); }
            else if (sb.Length > 0 && sb[^1] != '_') { sb.Append('_'); }
        }
        var s = sb.ToString().Trim('_');
        return s.Length == 0 ? "campo" : (s.Length > 60 ? s[..60] : s);
    }

    private static string UniqueKey(string baseKey, IReadOnlyCollection<string> existing)
    {
        var set = new HashSet<string>(existing, StringComparer.OrdinalIgnoreCase);
        if (!set.Contains(baseKey)) { return baseKey; }
        for (int i = 2; ; i++)
        {
            var k = $"{baseKey}_{i}";
            if (!set.Contains(k)) { return k; }
        }
    }

    private static string? Trim(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private ContactDto ToDto(Contact c) => new(
        c.Id, c.Name, c.Phone, c.Email, c.Country, c.City, c.BirthDate,
        c.LeadId, c.ConversationId,
        c.FirstOrderAt, c.LastOrderAt, c.OrderCount, c.LifetimeValue, c.LifetimeValueCurrency,
        c.CadenceDays, c.NextExpectedAt, c.Notes, c.ArchivedAt is not null,
        ContactInsights.Lifecycle(c.OrderCount, c.LastOrderAt, c.CadenceDays, _time.GetUtcNow()),
        string.IsNullOrWhiteSpace(c.Tags) ? Array.Empty<string>() : c.Tags.Split(',', StringSplitOptions.RemoveEmptyEntries),
        ParseValues(c.FieldValuesJson));

    private static ContactOrderDto ToDto(ContactOrder o) => new(
        o.Id, o.ContactId, o.OrderedAt, o.TotalAmount, o.Currency, o.ItemsJson,
        o.Channel, o.ExternalRef, o.Notes, o.CreatedAt);

    private static ContactFieldDto ToDto(ContactFieldDefinition f) => new(
        f.Id, f.Section, f.FieldKey, f.Label, f.FieldType, f.Options, f.Description,
        f.Column, f.SortOrder, f.ShowInList, f.ShowInFilter, f.IsSystem, f.IsHidden);
}
