using System.Globalization;
using System.Text;
using CubotRedManager.Application.Abstractions;
using CubotRedManager.Domain.Entities;
using CubotRedManager.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace CubotRedManager.Application.Tenancy;

public sealed class PipelineService : IPipelineService
{
    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly IAuditWriter _audit;

    public PipelineService(IApplicationDbContext db, ITenantContext tenantContext, IAuditWriter audit)
    {
        _db = db;
        _tenantContext = tenantContext;
        _audit = audit;
    }

    // Pipelines por defecto que se siembran en el primer uso de un tenant. Pensados para
    // CUBOT.redmanager (SaaS de agencias de marketing): un embudo "Productos" para marcas
    // que venden catalogo (FUXION, SKU, kit) y uno "Negocios" para B2B / servicios.
    // Son IsSystem: no se pueden borrar; se pueden renombrar y editar stages.
    private sealed record SeedField(string Key, string Label, PipelineFieldType Type, int Col = 1, string? Options = null, bool ShowInFilter = false, string? TotalKeys = null);
    private sealed record SeedStage(string Name, int Order, bool Won = false, bool Lost = false, SeedField[]? Fields = null);
    private sealed record SeedPipeline(string Name, int Order, SeedStage[] Stages);

    private static readonly SeedPipeline[] Seeds =
    [
        new("Productos", 0,
        [
            new("Interes", 0, Fields:
            [
                new("producto_interes", "Producto de interes", PipelineFieldType.Text, ShowInFilter: true),
                new("pais", "Pais", PipelineFieldType.Text, ShowInFilter: true),
                new("comentarios", "Comentarios", PipelineFieldType.TextArea, Col: 2)
            ]),
            new("Cotizacion", 1, Fields:
            [
                new("sku", "SKU / referencia", PipelineFieldType.Text),
                new("cantidad", "Cantidad", PipelineFieldType.Number),
                new("precio_unidad", "Precio unidad", PipelineFieldType.Currency),
                new("total_cotizado", "Total cotizado", PipelineFieldType.Total, Col: 1, TotalKeys: "precio_unidad"),
                new("link_pago", "Link de pago enviado", PipelineFieldType.Text, Col: 2)
            ]),
            new("Negociacion", 2, Fields:
            [
                new("objeciones", "Objeciones del cliente", PipelineFieldType.TextArea, Col: 2),
                new("descuento_pct", "Descuento aplicado (%)", PipelineFieldType.Number),
                new("ultima_oferta", "Ultima oferta enviada", PipelineFieldType.Currency)
            ]),
            new("Ganado", 3, Won: true, Fields:
            [
                new("fecha_cierre", "Fecha de cierre", PipelineFieldType.Date),
                new("medio_pago", "Medio de pago", PipelineFieldType.Select, Col: 1, Options: "Tarjeta\nTransferencia\nEfectivo\nOtro"),
                new("notas_cierre", "Notas del cierre", PipelineFieldType.TextArea, Col: 2)
            ]),
            new("Perdido", 4, Lost: true)
        ]),
        new("Negocios", 1,
        [
            new("Prospecto", 0, Fields:
            [
                new("empresa", "Empresa", PipelineFieldType.Text, ShowInFilter: true),
                new("cargo", "Cargo del contacto", PipelineFieldType.Text),
                new("origen", "De donde llego", PipelineFieldType.Select, Options: "Referido\nWeb\nRedes\nEvento\nOtro", ShowInFilter: true)
            ]),
            new("Reunion", 1, Fields:
            [
                new("fecha_reunion", "Fecha de reunion", PipelineFieldType.Date),
                new("resumen_reunion", "Resumen", PipelineFieldType.TextArea, Col: 2)
            ]),
            new("Propuesta", 2, Fields:
            [
                new("alcance", "Alcance de la propuesta", PipelineFieldType.TextArea, Col: 2),
                new("valor_propuesto", "Valor propuesto", PipelineFieldType.Currency),
                new("vigencia", "Vigencia de la oferta", PipelineFieldType.Date)
            ]),
            new("Cerrado ganado", 3, Won: true, Fields:
            [
                new("contrato_firmado", "Contrato firmado", PipelineFieldType.Date),
                new("ticket_cerrado", "Valor cerrado", PipelineFieldType.Currency)
            ]),
            new("Cerrado perdido", 4, Lost: true)
        ])
    ];

    // Helper hack: SeedField con TotalKeys va por una variante — mantenerlo simple inline.
    // (Nota: el shape de arriba se construye en EnsureDefaultsAsync, no necesita helpers.)

    public async Task EnsureDefaultsAsync(Guid actorUserId, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId) { return; }
        if (await _db.Pipelines.AnyAsync(cancellationToken)) { return; }

        foreach (var seedPipeline in Seeds)
        {
            var pipeline = new Pipeline
            {
                TenantId = tenantId,
                Name = seedPipeline.Name,
                SortOrder = seedPipeline.Order,
                IsSystem = true
            };
            _db.Pipelines.Add(pipeline);

            foreach (var seedStage in seedPipeline.Stages)
            {
                var stage = new PipelineStage
                {
                    TenantId = tenantId,
                    Pipeline = pipeline,
                    Name = seedStage.Name,
                    SortOrder = seedStage.Order,
                    IsClosedWon = seedStage.Won,
                    IsClosedLost = seedStage.Lost
                };
                _db.PipelineStages.Add(stage);

                if (seedStage.Fields is null) { continue; }
                var fo = 0;
                foreach (var seedField in seedStage.Fields)
                {
                    _db.PipelineFieldDefinitions.Add(new PipelineFieldDefinition
                    {
                        TenantId = tenantId,
                        Stage = stage,
                        FieldKey = seedField.Key,
                        Label = seedField.Label,
                        FieldType = seedField.Type,
                        Column = seedField.Col,
                        SortOrder = fo++,
                        Options = seedField.Options,
                        ShowInFilter = seedField.ShowInFilter,
                        TotalSourceKeys = seedField.TotalKeys
                    });
                }
            }
        }

        await _db.SaveChangesAsync(cancellationToken);
        _audit.Write(actorUserId, "pipeline.seed-defaults", nameof(Pipeline),
            entityId: null, previousValue: null,
            newValue: new { Pipelines = Seeds.Select(p => p.Name) }, tenantId: tenantId);
    }

    // === Pipeline (contenedor) ============================================================

    public async Task<IReadOnlyList<PipelineDto>> ListPipelinesAsync(CancellationToken cancellationToken = default)
    {
        var pipelines = await _db.Pipelines.AsNoTracking()
            .OrderBy(p => p.SortOrder).ThenBy(p => p.Name)
            .ToListAsync(cancellationToken);
        var ids = pipelines.Select(p => p.Id).ToList();
        var stageCounts = await _db.PipelineStages.AsNoTracking()
            .Where(s => ids.Contains(s.PipelineId))
            .GroupBy(s => s.PipelineId).Select(g => new { g.Key, C = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.C, cancellationToken);
        var leadCounts = await _db.Leads.AsNoTracking()
            .Where(l => ids.Contains(l.PipelineId) && l.ArchivedAt == null)
            .GroupBy(l => l.PipelineId).Select(g => new { g.Key, C = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.C, cancellationToken);
        return pipelines.Select(p => new PipelineDto(
            p.Id, p.Name, p.SortOrder, p.IsSystem,
            stageCounts.TryGetValue(p.Id, out var sc) ? sc : 0,
            leadCounts.TryGetValue(p.Id, out var lc) ? lc : 0)).ToList();
    }

    public async Task<PipelineDto?> CreatePipelineAsync(CreatePipelineRequest request, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId) { return null; }
        if (string.IsNullOrWhiteSpace(request.Name)) { return null; }
        var name = request.Name.Trim();
        if (await _db.Pipelines.AnyAsync(p => p.Name == name, cancellationToken)) { return null; }

        var maxOrder = await _db.Pipelines.Select(p => (int?)p.SortOrder).MaxAsync(cancellationToken) ?? -1;
        var pipeline = new Pipeline { TenantId = tenantId, Name = name, SortOrder = maxOrder + 1, IsSystem = false };
        _db.Pipelines.Add(pipeline);
        _audit.Write(actorUserId, "pipeline.create", nameof(Pipeline), pipeline.Id,
            previousValue: null, newValue: new { pipeline.Name }, tenantId: tenantId);
        await _db.SaveChangesAsync(cancellationToken);
        return new PipelineDto(pipeline.Id, pipeline.Name, pipeline.SortOrder, pipeline.IsSystem, 0, 0);
    }

    public async Task<PipelineDto?> UpdatePipelineAsync(Guid pipelineId, UpdatePipelineRequest request, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Name)) { return null; }
        var pipeline = await _db.Pipelines.FirstOrDefaultAsync(p => p.Id == pipelineId, cancellationToken);
        if (pipeline is null) { return null; }
        pipeline.Name = request.Name.Trim();
        _audit.Write(actorUserId, "pipeline.update", nameof(Pipeline), pipeline.Id,
            previousValue: null, newValue: new { pipeline.Name }, tenantId: pipeline.TenantId);
        await _db.SaveChangesAsync(cancellationToken);
        var sc = await _db.PipelineStages.CountAsync(s => s.PipelineId == pipeline.Id, cancellationToken);
        var lc = await _db.Leads.CountAsync(l => l.PipelineId == pipeline.Id && l.ArchivedAt == null, cancellationToken);
        return new PipelineDto(pipeline.Id, pipeline.Name, pipeline.SortOrder, pipeline.IsSystem, sc, lc);
    }

    public async Task ReorderPipelinesAsync(ReorderPipelinesRequest request, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var all = await _db.Pipelines.ToListAsync(cancellationToken);
        var order = 0;
        foreach (var id in request.OrderedPipelineIds)
        {
            var p = all.FirstOrDefault(x => x.Id == id);
            if (p is not null) { p.SortOrder = order++; }
        }
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> DeletePipelineAsync(Guid pipelineId, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var pipeline = await _db.Pipelines.FirstOrDefaultAsync(p => p.Id == pipelineId, cancellationToken);
        if (pipeline is null) { return false; }
        if (pipeline.IsSystem) { return false; }
        if (await _db.Leads.AnyAsync(l => l.PipelineId == pipelineId, cancellationToken)) { return false; }
        _db.Pipelines.Remove(pipeline); // cascade a Stages + Fields
        _audit.Write(actorUserId, "pipeline.delete", nameof(Pipeline), pipeline.Id,
            previousValue: new { pipeline.Name }, newValue: null, tenantId: pipeline.TenantId);
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    // === Stage ============================================================================

    public async Task<IReadOnlyList<PipelineStageDto>> ListStagesAsync(Guid? pipelineId = null, CancellationToken cancellationToken = default)
    {
        var q = _db.PipelineStages.AsNoTracking();
        if (pipelineId is Guid pid) { q = q.Where(s => s.PipelineId == pid); }
        return await q
            .OrderBy(s => s.PipelineId).ThenBy(s => s.SortOrder)
            .Select(s => new PipelineStageDto(s.Id, s.PipelineId, s.Name, s.SortOrder, s.IsClosedWon, s.IsClosedLost))
            .ToListAsync(cancellationToken);
    }

    public async Task<PipelineStageDto?> CreateStageAsync(CreatePipelineStageRequest request, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId) { return null; }
        var pipeline = await _db.Pipelines.FirstOrDefaultAsync(p => p.Id == request.PipelineId, cancellationToken);
        if (pipeline is null) { return null; }

        var maxOrder = await _db.PipelineStages
            .Where(s => s.PipelineId == request.PipelineId)
            .Select(s => (int?)s.SortOrder).MaxAsync(cancellationToken) ?? -1;

        var stage = new PipelineStage
        {
            TenantId = tenantId,
            PipelineId = request.PipelineId,
            Name = request.Name.Trim(),
            SortOrder = request.SortOrder > 0 ? request.SortOrder : maxOrder + 1,
            IsClosedWon = request.IsClosedWon,
            IsClosedLost = request.IsClosedLost
        };
        _db.PipelineStages.Add(stage);
        _audit.Write(actorUserId, "pipeline-stage.create", nameof(PipelineStage), stage.Id,
            previousValue: null, newValue: new { stage.Name, stage.PipelineId }, tenantId: tenantId);
        await _db.SaveChangesAsync(cancellationToken);
        return new PipelineStageDto(stage.Id, stage.PipelineId, stage.Name, stage.SortOrder, stage.IsClosedWon, stage.IsClosedLost);
    }

    public async Task<PipelineStageDto?> UpdateStageAsync(Guid stageId, UpdatePipelineStageRequest request, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var stage = await _db.PipelineStages.FirstOrDefaultAsync(s => s.Id == stageId, cancellationToken);
        if (stage is null) { return null; }
        stage.Name = request.Name.Trim();
        stage.IsClosedWon = request.IsClosedWon;
        stage.IsClosedLost = request.IsClosedLost;
        _audit.Write(actorUserId, "pipeline-stage.update", nameof(PipelineStage), stage.Id,
            previousValue: null, newValue: new { stage.Name, stage.IsClosedWon, stage.IsClosedLost }, tenantId: stage.TenantId);
        await _db.SaveChangesAsync(cancellationToken);
        return new PipelineStageDto(stage.Id, stage.PipelineId, stage.Name, stage.SortOrder, stage.IsClosedWon, stage.IsClosedLost);
    }

    public async Task ReorderStagesAsync(ReorderStagesRequest request, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        // Solo reordena stages del pipeline indicado — se ignoran IDs de otros pipelines.
        var stages = await _db.PipelineStages
            .Where(s => s.PipelineId == request.PipelineId)
            .ToListAsync(cancellationToken);
        var order = 0;
        foreach (var id in request.OrderedStageIds)
        {
            var s = stages.FirstOrDefault(x => x.Id == id);
            if (s is not null) { s.SortOrder = order++; }
        }
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> DeleteStageAsync(Guid stageId, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var stage = await _db.PipelineStages.FirstOrDefaultAsync(s => s.Id == stageId, cancellationToken);
        if (stage is null) { return false; }
        if (await _db.Leads.AnyAsync(l => l.StageId == stageId, cancellationToken)) { return false; }
        _db.PipelineStages.Remove(stage);
        _audit.Write(actorUserId, "pipeline-stage.delete", nameof(PipelineStage), stage.Id,
            previousValue: new { stage.Name }, newValue: null, tenantId: stage.TenantId);
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    // === Fields ===========================================================================

    public async Task<IReadOnlyList<PipelineFieldDto>> ListFieldsAsync(Guid? pipelineId = null, CancellationToken cancellationToken = default)
    {
        IQueryable<PipelineFieldDefinition> q = _db.PipelineFieldDefinitions.AsNoTracking();
        if (pipelineId is Guid pid)
        {
            // Join a Stage para filtrar por PipelineId.
            q = from f in q
                join s in _db.PipelineStages.AsNoTracking() on f.StageId equals s.Id
                where s.PipelineId == pid
                select f;
        }
        return await q
            .OrderBy(f => f.StageId).ThenBy(f => f.SortOrder)
            .Select(f => new PipelineFieldDto(
                f.Id, f.StageId, f.FieldKey, f.Label, f.FieldType, f.Column, f.SortOrder, f.Options,
                f.Description, f.AllowMultiple, f.RepeatWithFieldKey, f.MultiWithDetail, f.TotalSourceKeys, f.ShowInFilter))
            .ToListAsync(cancellationToken);
    }

    public async Task<PipelineFieldDto?> CreateFieldAsync(CreatePipelineFieldRequest request, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId) { return null; }
        if (!await _db.PipelineStages.AnyAsync(s => s.Id == request.StageId, cancellationToken)) { return null; }

        var key = string.IsNullOrWhiteSpace(request.FieldKey) ? Slugify(request.Label) : request.FieldKey.Trim();
        if (ReservedSystemKeys.Contains(key.ToLowerInvariant())) { return null; }
        var existingKeys = await _db.PipelineFieldDefinitions
            .Where(f => f.StageId == request.StageId).Select(f => f.FieldKey).ToListAsync(cancellationToken);
        key = EnsureUniqueKey(key, existingKeys);

        var maxOrder = await _db.PipelineFieldDefinitions
            .Where(f => f.StageId == request.StageId)
            .Select(f => (int?)f.SortOrder).MaxAsync(cancellationToken) ?? -1;
        var field = new PipelineFieldDefinition
        {
            TenantId = tenantId,
            StageId = request.StageId,
            FieldKey = key,
            Label = request.Label.Trim(),
            FieldType = request.FieldType,
            Column = Math.Clamp(request.Column, 1, 3),
            SortOrder = maxOrder + 1,
            Options = string.IsNullOrWhiteSpace(request.Options) ? null : request.Options.Trim(),
            Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            AllowMultiple = request.AllowMultiple,
            RepeatWithFieldKey = string.IsNullOrWhiteSpace(request.RepeatWithFieldKey) ? null : request.RepeatWithFieldKey.Trim(),
            MultiWithDetail = request.AllowMultiple && request.MultiWithDetail,
            TotalSourceKeys = request.FieldType == PipelineFieldType.Total && !string.IsNullOrWhiteSpace(request.TotalSourceKeys)
                ? request.TotalSourceKeys.Trim() : null,
            ShowInFilter = request.ShowInFilter
        };
        _db.PipelineFieldDefinitions.Add(field);
        _audit.Write(actorUserId, "pipeline-field.create", nameof(PipelineFieldDefinition), field.Id,
            previousValue: null, newValue: new { field.StageId, field.FieldKey, field.Label }, tenantId: tenantId);
        await _db.SaveChangesAsync(cancellationToken);
        return Map(field);
    }

    public async Task<PipelineFieldDto?> UpdateFieldAsync(Guid fieldId, UpdatePipelineFieldRequest request, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var field = await _db.PipelineFieldDefinitions.FirstOrDefaultAsync(f => f.Id == fieldId, cancellationToken);
        if (field is null) { return null; }
        field.Label = request.Label.Trim();
        field.FieldType = request.FieldType;
        field.Column = Math.Clamp(request.Column, 1, 3);
        field.Options = string.IsNullOrWhiteSpace(request.Options) ? null : request.Options.Trim();
        field.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        field.AllowMultiple = request.AllowMultiple;
        field.RepeatWithFieldKey = string.IsNullOrWhiteSpace(request.RepeatWithFieldKey) ? null : request.RepeatWithFieldKey.Trim();
        field.MultiWithDetail = request.AllowMultiple && request.MultiWithDetail;
        field.TotalSourceKeys = request.FieldType == PipelineFieldType.Total && !string.IsNullOrWhiteSpace(request.TotalSourceKeys)
            ? request.TotalSourceKeys.Trim() : null;
        field.ShowInFilter = request.ShowInFilter;
        _audit.Write(actorUserId, "pipeline-field.update", nameof(PipelineFieldDefinition), field.Id,
            previousValue: null, newValue: new { field.Label, field.FieldType }, tenantId: field.TenantId);
        await _db.SaveChangesAsync(cancellationToken);
        return Map(field);
    }

    public async Task<PipelineFieldDto?> MoveFieldToStageAsync(Guid fieldId, Guid targetStageId, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var field = await _db.PipelineFieldDefinitions.FirstOrDefaultAsync(f => f.Id == fieldId, cancellationToken);
        if (field is null) { return null; }
        if (field.StageId == targetStageId) { return Map(field); }

        // Validacion de cross-pipeline: la stage destino DEBE pertenecer al mismo pipeline que la actual.
        var sourceStage = await _db.PipelineStages.AsNoTracking().FirstOrDefaultAsync(s => s.Id == field.StageId, cancellationToken);
        var targetStage = await _db.PipelineStages.AsNoTracking().FirstOrDefaultAsync(s => s.Id == targetStageId, cancellationToken);
        if (sourceStage is null || targetStage is null) { return null; }
        if (sourceStage.PipelineId != targetStage.PipelineId) { return null; }

        var maxOrder = await _db.PipelineFieldDefinitions.Where(f => f.StageId == targetStageId)
            .Select(f => (int?)f.SortOrder).MaxAsync(cancellationToken) ?? -1;
        field.StageId = targetStageId;
        field.SortOrder = maxOrder + 1;
        _audit.Write(actorUserId, "pipeline-field.move-stage", nameof(PipelineFieldDefinition), field.Id,
            previousValue: null, newValue: new { field.FieldKey, TargetStageId = targetStageId }, tenantId: field.TenantId);
        await _db.SaveChangesAsync(cancellationToken);
        return Map(field);
    }

    public async Task ReorderFieldsAsync(ReorderFieldsRequest request, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var fields = await _db.PipelineFieldDefinitions.ToListAsync(cancellationToken);
        var order = 0;
        foreach (var id in request.OrderedFieldIds)
        {
            var f = fields.FirstOrDefault(x => x.Id == id);
            if (f is not null) { f.SortOrder = order++; }
        }
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> DeleteFieldAsync(Guid fieldId, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var field = await _db.PipelineFieldDefinitions.FirstOrDefaultAsync(f => f.Id == fieldId, cancellationToken);
        if (field is null) { return false; }
        _db.PipelineFieldDefinitions.Remove(field);
        _audit.Write(actorUserId, "pipeline-field.delete", nameof(PipelineFieldDefinition), field.Id,
            previousValue: new { field.FieldKey, field.Label }, newValue: null, tenantId: field.TenantId);
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    // === Helpers ==========================================================================

    private static PipelineFieldDto Map(PipelineFieldDefinition f) =>
        new(f.Id, f.StageId, f.FieldKey, f.Label, f.FieldType, f.Column, f.SortOrder, f.Options,
            f.Description, f.AllowMultiple, f.RepeatWithFieldKey, f.MultiWithDetail, f.TotalSourceKeys, f.ShowInFilter);

    /// <summary>Claves reservadas que ya existen como campos de sistema (nativos del lead):
    /// nombre, telefono, tema, valor estimado, moneda. No se permite crear fields configurables
    /// con estas claves para evitar duplicados visuales.</summary>
    private static readonly HashSet<string> ReservedSystemKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "nombre", "contactname",
        "telefono", "contactphone",
        "tema", "topic",
        "valor", "valor_estimado", "estimatedvalue",
        "moneda", "currency",
        "pipeline", "stage", "status"
    };

    private static string EnsureUniqueKey(string key, IReadOnlyCollection<string> existing)
    {
        if (!existing.Contains(key)) { return key; }
        var i = 2;
        while (existing.Contains($"{key}{i}")) { i++; }
        return $"{key}{i}";
    }

    private static string Slugify(string label)
    {
        var normalized = label.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var c in normalized)
        {
            var cat = CharUnicodeInfo.GetUnicodeCategory(c);
            if (cat == UnicodeCategory.NonSpacingMark) { continue; }
            if (char.IsLetterOrDigit(c)) { sb.Append(c); }
            else if (sb.Length > 0 && sb[^1] != '_') { sb.Append('_'); }
        }
        var slug = sb.ToString().Trim('_');
        return string.IsNullOrEmpty(slug) ? "campo" : slug;
    }
}
