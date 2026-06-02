namespace CubotRedManager.Application.Tenancy;

/// <summary>
/// Expone los DataContainers del tenant al agente de IA como contexto de prompt. Resuelve
/// placeholders del prompt antes del envio al LLM (patron MCP simplificado, sin servidor HTTP).
///
/// Placeholders soportados:
/// - {{LIST.CONTAINERS}} -> markdown con nombre, descripcion y conteo de columnas/filas.
/// - {{CONTAINER:nombre}} -> tabla markdown con las filas del container indicado.
/// </summary>
public interface IDataContainerMcpService
{
    /// <summary>Lista textual (markdown) de containers del tenant. Para {{LIST.CONTAINERS}}.</summary>
    Task<string> ListContainersAsync(CancellationToken ct = default);

    /// <summary>Tabla textual (markdown) con las filas del container indicado por nombre. Para {{CONTAINER:nombre}}.</summary>
    Task<string> QueryContainerAsync(string containerName, int limit = 100, CancellationToken ct = default);

    /// <summary>
    /// Resuelve todos los placeholders MCP en un texto. Idempotente: si no hay placeholders devuelve igual.
    /// Si <paramref name="mcpEnabled"/> es false y el texto tiene placeholders, los reemplaza por una nota
    /// informativa para que el LLM sepa que el acceso a contenedores esta deshabilitado.
    /// </summary>
    Task<string> ResolvePlaceholdersAsync(string text, bool mcpEnabled, CancellationToken ct = default);
}
