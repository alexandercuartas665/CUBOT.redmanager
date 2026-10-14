using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CubotRedManager.Application.Abstractions;
using CubotRedManager.Domain.Entities;
using CubotRedManager.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace CubotRedManager.Application.Tenancy;

public sealed class WhatsAppTemplateService : IWhatsAppTemplateService
{
    private static readonly Regex TokenRx = new(@"\{\{\s*([a-zA-Z0-9_]+)\s*\}\}", RegexOptions.Compiled);

    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly ISecretProtector _secretProtector;
    private readonly IYCloudApiClient _ycloud;
    private readonly IWhatsAppConnectorService _connector;
    private readonly IAuditWriter _audit;
    private readonly TimeProvider _timeProvider;

    public WhatsAppTemplateService(IApplicationDbContext db, ITenantContext tenantContext, ISecretProtector secretProtector,
        IYCloudApiClient ycloud, IWhatsAppConnectorService connector, IAuditWriter audit, TimeProvider timeProvider)
    {
        _db = db;
        _tenantContext = tenantContext;
        _secretProtector = secretProtector;
        _ycloud = ycloud;
        _connector = connector;
        _audit = audit;
        _timeProvider = timeProvider;
    }

    public IReadOnlyList<TemplateVariableDef> Catalog() => TemplateVariableCatalog.All;

    public async Task<IReadOnlyList<WhatsAppTemplateDto>> ListAsync(CancellationToken cancellationToken = default)
    {
        var rows = await _db.WhatsAppTemplates.AsNoTracking()
            .OrderByDescending(t => t.UpdatedAt).ThenByDescending(t => t.CreatedAt)
            .ToListAsync(cancellationToken);
        return rows.Select(Map).ToList();
    }

    public async Task<WhatsAppTemplateDto?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var t = await _db.WhatsAppTemplates.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        return t is null ? null : Map(t);
    }

    public async Task<WhatsAppTemplateDto?> CreateDraftAsync(SaveWhatsAppTemplateRequest request, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId) { return null; }

        var t = new WhatsAppTemplate { TenantId = tenantId, Status = "Draft" };
        await ApplyAsync(t, request, cancellationToken);
        if (await _db.WhatsAppTemplates.AnyAsync(x => x.Name == t.Name && x.Language == t.Language, cancellationToken))
        {
            return null;
        }
        _db.WhatsAppTemplates.Add(t);
        _audit.Write(actorUserId, "wa-template.create", nameof(WhatsAppTemplate), t.Id,
            previousValue: null, newValue: new { t.Name, t.Language, t.Category }, tenantId: tenantId);
        await _db.SaveChangesAsync(cancellationToken);
        return Map(t);
    }

    public async Task<WhatsAppTemplateDto?> UpdateAsync(Guid id, SaveWhatsAppTemplateRequest request, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var t = await _db.WhatsAppTemplates.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (t is null) { return null; }
        if (t.Status is not ("Draft" or "Rejected"))
        {
            return Map(t);
        }
        var targetName = NormalizeName(request.Name);
        var targetLang = string.IsNullOrWhiteSpace(request.Language) ? "es" : request.Language.Trim();
        if (await _db.WhatsAppTemplates.AnyAsync(x => x.Id != t.Id && x.Name == targetName && x.Language == targetLang, cancellationToken))
        {
            return null;
        }
        await ApplyAsync(t, request, cancellationToken);
        if (t.Status == "Rejected") { t.Status = "Draft"; t.RejectionReason = null; }
        _audit.Write(actorUserId, "wa-template.update", nameof(WhatsAppTemplate), t.Id,
            previousValue: null, newValue: new { t.Name, t.Language, t.Category }, tenantId: t.TenantId);
        await _db.SaveChangesAsync(cancellationToken);
        return Map(t);
    }

    public async Task<bool> DeleteAsync(Guid id, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var t = await _db.WhatsAppTemplates.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (t is null) { return false; }
        _audit.Write(actorUserId, "wa-template.delete", nameof(WhatsAppTemplate), t.Id,
            previousValue: new { t.Name, t.Status }, newValue: null, tenantId: t.TenantId);
        _db.WhatsAppTemplates.Remove(t);
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<TemplateSubmitResult> SubmitAsync(Guid id, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var t = await _db.WhatsAppTemplates.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (t is null) { return new TemplateSubmitResult(false, "La plantilla no existe.", null); }
        if (string.IsNullOrWhiteSpace(t.BodyText)) { return new TemplateSubmitResult(false, "El cuerpo es obligatorio.", null); }
        if (string.Equals(t.Category, "AUTHENTICATION", StringComparison.OrdinalIgnoreCase))
        {
            return new TemplateSubmitResult(false, "Las plantillas de autenticacion (OTP) aun no estan soportadas en este corte.", null);
        }
        if (t.WhatsAppLineId is not Guid lineId) { return new TemplateSubmitResult(false, "Elige la linea por la que se somete (define la WABA).", null); }

        var line = await _db.WhatsAppLines.FirstOrDefaultAsync(l => l.Id == lineId, cancellationToken);
        if (line is null) { return new TemplateSubmitResult(false, "La linea elegida no existe.", null); }
        if (line.Provider != WhatsAppProvider.YCloud)
        {
            return new TemplateSubmitResult(false, "Por ahora solo se pueden someter plantillas por lineas YCloud.", null);
        }
        var wabaId = !string.IsNullOrWhiteSpace(line.YCloudWabaId) ? line.YCloudWabaId : t.WabaId;
        if (string.IsNullOrWhiteSpace(line.YCloudApiKeyEncrypted) || string.IsNullOrWhiteSpace(wabaId))
        {
            return new TemplateSubmitResult(false, "La linea YCloud no tiene API key o WABA id.", null);
        }

        var (compiledBody, examples) = Compile(t.BodyText);
        var components = BuildComponents(t.HeaderType, t.HeaderText, compiledBody, examples, t.FooterText);
        var apiKey = _secretProtector.Unprotect(line.YCloudApiKeyEncrypted);

        var res = await _ycloud.CreateTemplateAsync(apiKey, wabaId!, t.Name, t.Language, t.Category, components, null, cancellationToken);
        if (!res.IsSuccess)
        {
            return new TemplateSubmitResult(false, res.Error ?? "El proveedor rechazo la solicitud.", null);
        }

        t.ProviderTemplateId = res.Id;
        t.WabaId = wabaId;
        t.SubmittedAt = _timeProvider.GetUtcNow();
        t.Status = MapProviderStatus(res.Status) ?? "Submitted";
        if (t.Status == "Approved") { t.ReviewedAt = _timeProvider.GetUtcNow(); }
        _audit.Write(actorUserId, "wa-template.submit", nameof(WhatsAppTemplate), t.Id,
            previousValue: null, newValue: new { t.Name, t.Status, t.ProviderTemplateId }, tenantId: t.TenantId);
        await _db.SaveChangesAsync(cancellationToken);
        return new TemplateSubmitResult(true, null, t.Status);
    }

    public async Task<int> SyncStatusAsync(CancellationToken cancellationToken = default)
    {
        var pending = await _db.WhatsAppTemplates
            .Where(t => t.Status == "Submitted")
            .ToListAsync(cancellationToken);
        if (pending.Count == 0) { return 0; }

        var byLine = pending.Where(t => t.WhatsAppLineId is not null).GroupBy(t => t.WhatsAppLineId!.Value);
        var updated = 0;
        foreach (var grp in byLine)
        {
            var line = await _db.WhatsAppLines.FirstOrDefaultAsync(l => l.Id == grp.Key, cancellationToken);
            if (line is null || line.Provider != WhatsAppProvider.YCloud
                || string.IsNullOrWhiteSpace(line.YCloudApiKeyEncrypted) || string.IsNullOrWhiteSpace(line.YCloudWabaId))
            {
                continue;
            }
            var apiKey = _secretProtector.Unprotect(line.YCloudApiKeyEncrypted);
            var list = await _ycloud.ListTemplatesAsync(apiKey, line.YCloudWabaId!, cancellationToken);
            if (!list.IsSuccess) { continue; }
            foreach (var t in grp)
            {
                var match = list.Items.FirstOrDefault(i => string.Equals(i.Name, t.Name, StringComparison.OrdinalIgnoreCase)
                    && (i.Language is null || string.Equals(i.Language, t.Language, StringComparison.OrdinalIgnoreCase)));
                if (match is null) { continue; }
                var newStatus = MapProviderStatus(match.Status);
                if (newStatus is not null && newStatus != t.Status)
                {
                    t.Status = newStatus;
                    t.RejectionReason = newStatus == "Rejected" ? match.RejectedReason : null;
                    t.ReviewedAt = _timeProvider.GetUtcNow();
                    updated++;
                }
            }
        }
        if (updated > 0) { await _db.SaveChangesAsync(cancellationToken); }
        return updated;
    }

    // ===== Helpers ============================================================

    private async Task ApplyAsync(WhatsAppTemplate t, SaveWhatsAppTemplateRequest request, CancellationToken ct)
    {
        t.Name = NormalizeName(request.Name);
        t.Language = string.IsNullOrWhiteSpace(request.Language) ? "es" : request.Language.Trim();
        t.Category = string.IsNullOrWhiteSpace(request.Category) ? "UTILITY" : request.Category.Trim().ToUpperInvariant();
        t.HeaderType = string.IsNullOrWhiteSpace(request.HeaderText) ? null : "TEXT";
        t.HeaderText = string.IsNullOrWhiteSpace(request.HeaderText) ? null : request.HeaderText.Trim();
        t.BodyText = request.BodyText?.Trim() ?? string.Empty;
        t.FooterText = string.IsNullOrWhiteSpace(request.FooterText) ? null : request.FooterText.Trim();
        t.VariablesJson = JsonSerializer.Serialize(request.Variables ?? Array.Empty<WhatsAppTemplateVariable>());
        t.WhatsAppLineId = request.WhatsAppLineId;
        if (request.WhatsAppLineId is Guid lineId)
        {
            var line = await _db.WhatsAppLines.AsNoTracking().FirstOrDefaultAsync(l => l.Id == lineId, ct);
            if (line is not null)
            {
                t.Provider = line.Provider;
                t.WabaId = line.Provider == WhatsAppProvider.YCloud ? line.YCloudWabaId : line.CloudBusinessAccountId;
            }
        }
    }

    /// <summary>Compila el cuerpo editable con tokens {{token}} a placeholders posicionales de Meta
    /// {{1}}..{{n}} (en orden de aparicion, sin repetir) y devuelve la lista de ejemplos en ese orden.</summary>
    private static (string Body, List<string> Examples) Compile(string editableBody)
    {
        var vars = ParseVariables(editableBody);
        var order = new List<string>();
        var sb = new StringBuilder();
        var last = 0;
        foreach (Match m in TokenRx.Matches(editableBody))
        {
            var token = m.Groups[1].Value;
            var idx = order.IndexOf(token);
            if (idx < 0) { order.Add(token); idx = order.Count - 1; }
            sb.Append(editableBody, last, m.Index - last);
            sb.Append("{{").Append(idx + 1).Append("}}");
            last = m.Index + m.Length;
        }
        sb.Append(editableBody, last, editableBody.Length - last);
        var examples = order.Select(tok =>
            vars.FirstOrDefault(v => string.Equals(v.Token, tok, StringComparison.OrdinalIgnoreCase))?.Example
            ?? TemplateVariableCatalog.Find(tok)?.DefaultExample
            ?? tok).ToList();
        return (sb.ToString(), examples);
    }

    /// <summary>Construye el arreglo de componentes (HEADER texto opcional, BODY con example.body_text,
    /// FOOTER opcional) para enviar a YCloud/Meta.</summary>
    private static List<object> BuildComponents(string? headerType, string? headerText, string compiledBody, List<string> examples, string? footerText)
    {
        var components = new List<object>();
        if (string.Equals(headerType, "TEXT", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(headerText))
        {
            components.Add(new Dictionary<string, object?> { ["type"] = "HEADER", ["format"] = "TEXT", ["text"] = headerText });
        }
        var body = new Dictionary<string, object?> { ["type"] = "BODY", ["text"] = compiledBody };
        if (examples.Count > 0)
        {
            body["example"] = new Dictionary<string, object?> { ["body_text"] = new[] { examples.ToArray() } };
        }
        components.Add(body);
        if (!string.IsNullOrWhiteSpace(footerText))
        {
            components.Add(new Dictionary<string, object?> { ["type"] = "FOOTER", ["text"] = footerText });
        }
        return components;
    }

    private static List<WhatsAppTemplateVariable> ParseVariables(string body)
    {
        var tokens = new List<WhatsAppTemplateVariable>();
        foreach (Match m in TokenRx.Matches(body))
        {
            var tok = m.Groups[1].Value;
            if (!tokens.Any(v => string.Equals(v.Token, tok, StringComparison.OrdinalIgnoreCase)))
            {
                tokens.Add(new WhatsAppTemplateVariable(tok, TemplateVariableCatalog.Find(tok)?.DefaultExample ?? tok));
            }
        }
        return tokens;
    }

    private static string NormalizeName(string raw)
    {
        var lower = (raw ?? string.Empty).Trim().ToLowerInvariant();
        var sb = new StringBuilder();
        foreach (var c in lower)
        {
            if (c is >= 'a' and <= 'z' or >= '0' and <= '9') { sb.Append(c); }
            else if (c is ' ' or '-' or '_') { sb.Append('_'); }
        }
        var name = sb.ToString().Trim('_');
        while (name.Contains("__")) { name = name.Replace("__", "_"); }
        return string.IsNullOrWhiteSpace(name) ? "plantilla" : name;
    }

    private static string? MapProviderStatus(string? providerStatus) => providerStatus?.ToUpperInvariant() switch
    {
        "APPROVED" => "Approved",
        "REJECTED" => "Rejected",
        "PENDING" or "PENDING_REVIEW" or "IN_APPEAL" => "Submitted",
        "PAUSED" => "Paused",
        "DISABLED" => "Disabled",
        null or "" => null,
        _ => null
    };

    private static WhatsAppTemplateDto Map(WhatsAppTemplate t)
    {
        IReadOnlyList<WhatsAppTemplateVariable> vars;
        try { vars = JsonSerializer.Deserialize<List<WhatsAppTemplateVariable>>(t.VariablesJson) ?? new(); }
        catch { vars = new List<WhatsAppTemplateVariable>(); }
        return new WhatsAppTemplateDto(
            t.Id, t.Name, t.Language, t.Category, t.HeaderType, t.HeaderText, t.BodyText, t.FooterText,
            vars, t.Provider, t.WhatsAppLineId, t.WabaId, t.Status, t.ProviderTemplateId, t.RejectionReason,
            t.SubmittedAt, t.ReviewedAt);
    }

    // =========================================================================
    // PR E — TestSend + Import + SetActive
    // =========================================================================

    public async Task<string?> SetActiveAsync(Guid id, bool active, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var t = await _db.WhatsAppTemplates.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (t is null) { return null; }
        var previous = t.Status;
        // Si se desactiva, marca Disabled; si se reactiva, vuelve a Draft si no estaba Approved/Submitted.
        if (!active)
        {
            if (t.Status == "Disabled") { return t.Status; }
            t.Status = "Disabled";
        }
        else
        {
            if (t.Status != "Disabled") { return t.Status; }
            t.Status = string.IsNullOrWhiteSpace(t.ProviderTemplateId) ? "Draft" : "Approved";
        }
        t.UpdatedAt = _timeProvider.GetUtcNow();
        _audit.Write(actorUserId, "wa-template.set-active", nameof(WhatsAppTemplate), t.Id,
            previousValue: new { status = previous }, newValue: new { status = t.Status, active },
            tenantId: t.TenantId);
        await _db.SaveChangesAsync(cancellationToken);
        return t.Status;
    }

    public async Task<TemplateTestSendResult> TestSendAsync(Guid id, Guid whatsAppLineId, string phone,
        IReadOnlyList<string>? values, string? headerMediaUrl, Guid actorUserId,
        CancellationToken cancellationToken = default)
    {
        var t = await _db.WhatsAppTemplates.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (t is null) { return new TemplateTestSendResult(false, "Plantilla no encontrada.", null); }
        if (string.IsNullOrWhiteSpace(phone)) { return new TemplateTestSendResult(false, "Indica el numero destino.", null); }

        // Si no vinieron valores, usar los ejemplos del VariablesJson (en orden).
        List<string> bodyParams = (values ?? Array.Empty<string>()).ToList();
        if (bodyParams.Count == 0)
        {
            try
            {
                var vars = JsonSerializer.Deserialize<List<WhatsAppTemplateVariable>>(t.VariablesJson) ?? new();
                bodyParams = vars.Select(v => v.Example ?? v.Token).ToList();
            }
            catch { /* lista vacia */ }
        }

        // Header de media: toma el tipo del HeaderType (IMAGE/VIDEO/DOCUMENT) si matchea.
        string? headerMediaType = null;
        if (!string.IsNullOrWhiteSpace(headerMediaUrl) && !string.IsNullOrWhiteSpace(t.HeaderType))
        {
            var h = t.HeaderType!.Trim().ToUpperInvariant();
            headerMediaType = h switch { "IMAGE" => "image", "VIDEO" => "video", "DOCUMENT" => "document", _ => null };
        }

        var sendResult = await _connector.SendTemplateAsync(whatsAppLineId, phone, t.Name, t.Language,
            bodyParams, actorUserId, headerMediaType, headerMediaUrl, null, null, cancellationToken);
        return new TemplateTestSendResult(sendResult.Ok, sendResult.Error, sendResult.MessageId);
    }

    public async Task<TemplateImportReport> ImportFromYCloudAsync(Guid whatsAppLineId, Guid actorUserId,
        CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId)
        {
            return new TemplateImportReport(false, 0, 0, 0, "Sin tenant activo.");
        }
        var line = await _db.WhatsAppLines.AsNoTracking().FirstOrDefaultAsync(l => l.Id == whatsAppLineId, cancellationToken);
        if (line is null) { return new TemplateImportReport(false, 0, 0, 0, "Linea no encontrada."); }
        if (line.Provider != WhatsAppProvider.YCloud)
        {
            return new TemplateImportReport(false, 0, 0, 0, "Solo lineas YCloud en este corte.");
        }
        if (string.IsNullOrWhiteSpace(line.YCloudApiKeyEncrypted) || string.IsNullOrWhiteSpace(line.YCloudWabaId))
        {
            return new TemplateImportReport(false, 0, 0, 0, "Falta API key o WABA Id en la linea.");
        }
        string apiKey;
        try { apiKey = _secretProtector.Unprotect(line.YCloudApiKeyEncrypted!); }
        catch { return new TemplateImportReport(false, 0, 0, 0, "No se pudo descifrar la API key."); }

        var listRes = await _ycloud.ListTemplatesAsync(apiKey, line.YCloudWabaId!, cancellationToken);
        if (!listRes.IsSuccess) { return new TemplateImportReport(false, 0, 0, 0, listRes.Error ?? "Error listando en YCloud."); }

        int imported = 0, updated = 0, skipped = 0;
        var now = _timeProvider.GetUtcNow();
        foreach (var item in listRes.Items)
        {
            var name = item.Name;
            var lang = item.Language ?? "es";
            var local = await _db.WhatsAppTemplates.FirstOrDefaultAsync(x => x.Name == name && x.Language == lang, cancellationToken);
            var mappedStatus = MapProviderStatus(item.Status) ?? "Approved";
            // Variables del body: {{1}}..{{N}} con sus ejemplos del parser. Se serializa como [{token,example}].
            var variablesJson = "[]";
            if (item.VariableExamples is { Count: > 0 } vex)
            {
                var vars = vex.Select((v, i) => new WhatsAppTemplateVariable($"var{i + 1}", v)).ToList();
                variablesJson = JsonSerializer.Serialize(vars);
            }
            if (local is null)
            {
                _db.WhatsAppTemplates.Add(new WhatsAppTemplate
                {
                    TenantId = tenantId,
                    Name = name,
                    Language = lang,
                    Category = string.IsNullOrWhiteSpace(item.Category) ? "UTILITY" : item.Category!,
                    HeaderType = item.HeaderFormat,
                    HeaderText = item.HeaderText,
                    BodyText = item.BodyText ?? "(importada desde YCloud; edita en la UI para ver el cuerpo real)",
                    FooterText = item.FooterText,
                    VariablesJson = variablesJson,
                    Provider = WhatsAppProvider.YCloud,
                    WhatsAppLineId = line.Id,
                    WabaId = line.YCloudWabaId,
                    Status = mappedStatus,
                    ProviderTemplateId = item.Id,
                    RejectionReason = item.RejectedReason,
                    ReviewedAt = now
                });
                imported++;
            }
            else
            {
                var changed = false;
                if (local.Status != mappedStatus) { local.Status = mappedStatus; changed = true; }
                if (local.ProviderTemplateId != item.Id) { local.ProviderTemplateId = item.Id; changed = true; }
                if (local.RejectionReason != item.RejectedReason) { local.RejectionReason = item.RejectedReason; changed = true; }
                // Si YCloud trae el cuerpo real y el local es el placeholder, lo hidratamos.
                if (!string.IsNullOrWhiteSpace(item.BodyText) &&
                    (string.IsNullOrWhiteSpace(local.BodyText) || local.BodyText.StartsWith("(importada desde YCloud")))
                {
                    local.BodyText = item.BodyText!;
                    local.HeaderType = item.HeaderFormat;
                    local.HeaderText = item.HeaderText;
                    local.FooterText = item.FooterText;
                    if (variablesJson != "[]") { local.VariablesJson = variablesJson; }
                    if (!string.IsNullOrWhiteSpace(item.Category)) { local.Category = item.Category!; }
                    changed = true;
                }
                if (changed) { local.UpdatedAt = now; local.ReviewedAt = now; updated++; }
                else { skipped++; }
            }
        }
        if (imported + updated > 0)
        {
            _audit.Write(actorUserId, "wa-template.import-ycloud", nameof(WhatsAppLine), line.Id,
                previousValue: null, newValue: new { imported, updated, skipped },
                tenantId: tenantId);
            await _db.SaveChangesAsync(cancellationToken);
        }
        return new TemplateImportReport(true, imported, updated, skipped, null);
    }
}
