using CubotRedManager.Application.Abstractions;
using CubotRedManager.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CubotRedManager.Application.Tenancy;

/// <summary>
/// Comandos de control que un asesor puede enviar en una conversacion para "tomar el control":
/// el comando "Manejo_asesor" agrega el numero del contacto a la lista negra del tenant asi el
/// bot deja de responderle y el humano atiende. Se detecta cuando el mensaje entra por el
/// webhook con fromMe = true (asesor escribiendo desde el WhatsApp del negocio).
/// Portado 1:1 desde CUBOT.travels.
/// </summary>
public static class AgentControlCommands
{
    /// <summary>Comando exacto (case-insensitive) para mandar el numero del contacto a la lista negra.</summary>
    public const string TakeControl = "Manejo_asesor";

    public static bool IsTakeControl(string? body)
        => !string.IsNullOrWhiteSpace(body) && string.Equals(body.Trim(), TakeControl, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Agrega el numero a la lista negra GLOBAL del tenant (si no estaba ya). Usa IgnoreQueryFilters
    /// porque puede llamarse desde el webhook (sin tenant en contexto): el tenant se resuelve desde
    /// la linea. Devuelve true si el numero quedo bloqueado (o ya lo estaba).
    /// </summary>
    public static async Task<bool> BlockNumberForLineAsync(IApplicationDbContext db, Guid lineId, string? phone, CancellationToken ct = default)
    {
        var digits = new string((phone ?? string.Empty).Where(char.IsDigit).ToArray());
        if (lineId == Guid.Empty || digits.Length == 0) { return false; }

        var tenantId = await db.WhatsAppLines.IgnoreQueryFilters()
            .Where(l => l.Id == lineId)
            .Select(l => (Guid?)l.TenantId)
            .FirstOrDefaultAsync(ct);
        if (tenantId is null) { return false; }

        var existing = await db.TenantBlockedNumbers.IgnoreQueryFilters()
            .Where(b => b.TenantId == tenantId.Value)
            .Select(b => b.Phone)
            .ToListAsync(ct);
        // Ya bloqueado (comparacion por digitos, tolera codigo de pais).
        if (existing.Any(e => e == digits || digits.EndsWith(e, StringComparison.Ordinal) || e.EndsWith(digits, StringComparison.Ordinal)))
        {
            return true;
        }

        db.TenantBlockedNumbers.Add(new TenantBlockedNumber
        {
            TenantId = tenantId.Value,
            Phone = digits,
            Note = "Agregado por comando Manejo_asesor"
        });
        await db.SaveChangesAsync(ct);
        return true;
    }
}
