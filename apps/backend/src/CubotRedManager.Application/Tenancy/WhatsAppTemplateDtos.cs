using CubotRedManager.Domain.Enums;

namespace CubotRedManager.Application.Tenancy;

/// <summary>Una variable usada en la plantilla: token amigable + valor de ejemplo (lo exige Meta
/// para revisar). Al someter, el servicio asigna posiciones {{1}}..{{n}} en orden de aparicion.</summary>
public sealed record WhatsAppTemplateVariable(string Token, string Example);

public sealed record WhatsAppTemplateDto(
    Guid Id,
    string Name,
    string Language,
    string Category,
    string? HeaderType,
    string? HeaderText,
    string BodyText,
    string? FooterText,
    IReadOnlyList<WhatsAppTemplateVariable> Variables,
    WhatsAppProvider Provider,
    Guid? WhatsAppLineId,
    string? WabaId,
    string Status,
    string? ProviderTemplateId,
    string? RejectionReason,
    DateTimeOffset? SubmittedAt,
    DateTimeOffset? ReviewedAt);

public sealed record SaveWhatsAppTemplateRequest(
    string Name,
    string Language,
    string Category,
    string? HeaderType,
    string? HeaderText,
    string BodyText,
    string? FooterText,
    IReadOnlyList<WhatsAppTemplateVariable> Variables,
    Guid? WhatsAppLineId);

public sealed record TemplateSubmitResult(bool Ok, string? Error, string? Status);

/// <summary>Definicion de una variable disponible en el editor (token amigable + ejemplo por
/// defecto + de donde se resuelve al enviar, en F4).</summary>
public sealed record TemplateVariableDef(string Token, string Label, string Description, string DefaultExample);

/// <summary>Catalogo de variables de la sesion que el editor puede insertar. Al ser una agencia de
/// marketing digital, las variables se orientan al contexto marca / campaña / cliente.</summary>
public static class TemplateVariableCatalog
{
    public static readonly IReadOnlyList<TemplateVariableDef> All = new[]
    {
        new TemplateVariableDef("agencia", "Agencia", "Nombre de la agencia (tenant)", "Mi Agencia"),
        new TemplateVariableDef("marca", "Marca / Cliente", "Nombre del cliente/marca gestionada", "Marca Ejemplo"),
        new TemplateVariableDef("contacto", "Contacto", "Nombre del contacto / lead", "Juan Perez"),
        new TemplateVariableDef("red", "Red social", "Red donde se recibio la interaccion", "TikTok"),
        new TemplateVariableDef("campana", "Campaña", "Campaña asociada", "Lanzamiento Q3"),
        new TemplateVariableDef("fecha", "Fecha", "Fecha relevante", "15 de julio"),
    };

    public static TemplateVariableDef? Find(string token)
        => All.FirstOrDefault(v => string.Equals(v.Token, token, StringComparison.OrdinalIgnoreCase));
}
