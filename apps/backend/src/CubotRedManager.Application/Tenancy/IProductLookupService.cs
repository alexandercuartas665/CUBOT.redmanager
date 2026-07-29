namespace CubotRedManager.Application.Tenancy;

/// <summary>
/// Herramienta que expone al LLM para buscar productos por nombre (parcial) en el contenedor
/// configurado en Pagos FUXION del agente. Reemplaza el patron "pasar el contenedor entero como
/// contexto" (que se trunca a 100 filas y fuerza a la IA a inventar cuando faltan datos) por un
/// "tool call": el LLM emite <c>[[buscar_producto: nombre]]</c> o
/// <c>[[buscar_producto: nombre @pais]]</c>, el dispatcher ejecuta esta busqueda y le devuelve
/// solo las filas matcheantes en un segundo turno.
/// </summary>
public interface IProductLookupService
{
    /// <summary>Busca filas cuyo campo Producto contenga <paramref name="query"/>
    /// (case-insensitive, ignoreando acentos). Si <paramref name="countryIso2"/> es no-vacio,
    /// filtra tambien por esa columna Pais. Devuelve texto formateado (tabla markdown) listo
    /// para pasar al LLM. Nunca lanza excepciones al caller.</summary>
    Task<ProductLookupResult> LookupAsync(Guid agentId, string query, string? countryIso2, CancellationToken cancellationToken = default);
}

public sealed record ProductLookupResult(
    bool Ok,
    string FormattedText,       // tabla markdown lista para el prompt
    int RowsMatched,
    string? ErrorDetail);
