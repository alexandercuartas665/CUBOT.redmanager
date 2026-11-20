using System.Text.Json;

namespace CubotRedManager.Application.Tenancy;

/// <summary>
/// Calculos de marketing sobre el historial de un contacto. Puros (sin BD) para poder probarlos.
/// </summary>
public static class ContactInsights
{
    public const string Prospecto = "Prospecto";
    public const string Nuevo = "Nuevo";
    public const string Activo = "Activo";
    public const string Fiel = "Fiel";
    public const string EnRiesgo = "En riesgo";
    public const string Dormido = "Dormido";
    public const string Perdido = "Perdido";

    public static readonly IReadOnlyList<string> Stages = new[] { Prospecto, Nuevo, Activo, Fiel, EnRiesgo, Dormido, Perdido };

    /// <summary>Cadencia asumida cuando el cliente solo tiene una compra: los productos FUXION
    /// vienen en presentaciones de ~30 dias.</summary>
    public const int DefaultCadenceDays = 30;

    /// <summary>
    /// Etapa del ciclo de vida segun recencia relativa a la cadencia propia del cliente (no un
    /// umbral fijo: un cliente que compra cada 60 dias no esta "en riesgo" a los 40).
    /// </summary>
    public static string Lifecycle(int orderCount, DateTimeOffset? lastOrderAt, int? cadenceDays, DateTimeOffset now)
    {
        if (orderCount == 0 || lastOrderAt is not DateTimeOffset last) { return Prospecto; }
        var daysSince = (now - last).TotalDays;
        var cadence = cadenceDays is int c && c > 0 ? c : DefaultCadenceDays;

        if (daysSince > 180) { return Perdido; }
        if (daysSince > cadence * 2.0) { return Dormido; }
        if (daysSince > cadence * 1.25) { return EnRiesgo; }
        if (orderCount == 1) { return Nuevo; }
        return orderCount >= 3 ? Fiel : Activo;
    }

    public sealed record OrderItems(DateTimeOffset OrderedAt, string? ItemsJson);

    /// <summary>
    /// Afinidad de producto: agrupa los items de todas las ordenes por nombre (case-insensitive)
    /// y calcula cuantas veces lo compro, cantidad total, y su cadencia propia. Items no parseables
    /// se ignoran (la orden puede tener texto libre).
    /// </summary>
    public static IReadOnlyList<ContactProductAffinityDto> ProductAffinity(IEnumerable<OrderItems> orders)
    {
        var byProduct = new Dictionary<string, (string Name, List<DateTimeOffset> Dates, decimal Qty)>(StringComparer.OrdinalIgnoreCase);
        foreach (var o in orders)
        {
            foreach (var (name, qty) in ParseItems(o.ItemsJson))
            {
                if (!byProduct.TryGetValue(name, out var acc)) { acc = (name, new List<DateTimeOffset>(), 0m); }
                acc.Dates.Add(o.OrderedAt);
                acc.Qty += qty;
                byProduct[name] = acc;
            }
        }

        var result = new List<ContactProductAffinityDto>();
        foreach (var (_, acc) in byProduct)
        {
            var dates = acc.Dates.Distinct().OrderBy(d => d).ToList();
            int? avg = null;
            DateTimeOffset? next = null;
            if (dates.Count >= 2)
            {
                var total = 0.0;
                for (var i = 1; i < dates.Count; i++) { total += (dates[i] - dates[i - 1]).TotalDays; }
                avg = (int)Math.Round(total / (dates.Count - 1));
                if (avg > 0) { next = dates[^1].AddDays(avg.Value); }
            }
            result.Add(new ContactProductAffinityDto(acc.Name, dates.Count, acc.Qty, dates[0], dates[^1], avg, next));
        }
        return result.OrderByDescending(p => p.TimesBought).ThenByDescending(p => p.LastBoughtAt).ToList();
    }

    public static IEnumerable<(string Name, decimal Qty)> ParseItems(string? itemsJson)
    {
        if (string.IsNullOrWhiteSpace(itemsJson)) { yield break; }
        JsonDocument doc;
        try { doc = JsonDocument.Parse(itemsJson); }
        catch (JsonException) { yield break; }
        using (doc)
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Array) { yield break; }
            foreach (var it in doc.RootElement.EnumerateArray())
            {
                if (it.ValueKind != JsonValueKind.Object) { continue; }
                var name = it.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString()?.Trim() : null;
                if (string.IsNullOrWhiteSpace(name)) { continue; }
                decimal qty = 1;
                if (it.TryGetProperty("qty", out var q) && q.ValueKind == JsonValueKind.Number && q.TryGetDecimal(out var dq) && dq > 0) { qty = dq; }
                yield return (name!, qty);
            }
        }
    }
}
