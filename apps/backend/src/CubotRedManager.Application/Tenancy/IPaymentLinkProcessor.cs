namespace CubotRedManager.Application.Tenancy;

/// <summary>
/// Procesa markers <c>[[link_pago: NOMBRE:cantidad, NOMBRE:cantidad, ...]]</c> en la respuesta
/// del LLM. Para cada ocurrencia: resuelve nombres a productIds usando el DataContainer del
/// agente, llama al API de FUXION, y sustituye el marker por la URL del sales-link. Si algo
/// falla, sustituye por un fallback amable y notifica al operador (via TenantAlertService).
///
/// El procesador NO decide CUANDO emitir el marker (eso es responsabilidad del prompt), solo
/// procesa los que ya vienen en el texto.
/// </summary>
public interface IPaymentLinkProcessor
{
    Task<PaymentLinkResult> ProcessAsync(Guid tenantId, Guid agentId, string agentText, CancellationToken cancellationToken = default);
}

public sealed record PaymentLinkResult(
    string ProcessedText,        // texto con los markers sustituidos por URL (o fallback)
    int MarkersFound,
    int LinksGenerated,
    int LinksFailed,
    IReadOnlyList<string> Errors); // razones humanas legibles para bitacora
