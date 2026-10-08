using CubotRedManager.Domain.Common;
using CubotRedManager.Domain.Enums;

namespace CubotRedManager.Domain.Entities;

/// <summary>
/// Definicion de un campo configurable de una etapa. Cada tenant puede agregar/quitar campos y
/// cambiarles el tipo; los valores por lead se guardan en Lead.FieldValuesJson indexados por FieldKey.
///
/// Portado 1:1 de CUBOT.travels (fields configurables de embudo son identicos entre marketing
/// y agencia de viajes). Soporta: single/multi/multi-con-detalle, campo Total (suma otros campos),
/// y "repeat with field" (p.ej. "edades" se repite tantas veces como diga "hijos").
/// </summary>
public class PipelineFieldDefinition : TenantEntity
{
    public Guid StageId { get; set; }
    public PipelineStage? Stage { get; set; }

    /// <summary>Clave estable del campo (no cambia tras creado), p.ej. "producto_interes".</summary>
    public string FieldKey { get; set; } = null!;

    public string Label { get; set; } = null!;
    public PipelineFieldType FieldType { get; set; } = PipelineFieldType.Text;

    /// <summary>Si es true, el campo aparece como filtro (chips por valor) en el panel Filtros.</summary>
    public bool ShowInFilter { get; set; }

    /// <summary>Columna del layout en el modal (1 = angosta, 2 = ancha/full).</summary>
    public int Column { get; set; } = 1;
    public int SortOrder { get; set; }

    /// <summary>Opciones para FieldType=Select, separadas por salto de linea.</summary>
    public string? Options { get; set; }

    /// <summary>Descripcion/contexto: para que sirve el campo. Ayuda al asesor y queda disponible
    /// para que el agente IA entienda y llene el campo cuando aplique.</summary>
    public string? Description { get; set; }

    /// <summary>Permite capturar varios valores (p.ej. multiples telefonos). Arreglo JSON.</summary>
    public bool AllowMultiple { get; set; }

    /// <summary>Solo para AllowMultiple: cada valor lleva ademas un texto de detalle. Arreglo JSON
    /// de objetos {"d":detalle,"v":valor}.</summary>
    public bool MultiWithDetail { get; set; }

    /// <summary>Solo para FieldType=Total: FieldKeys (coma-separado) de los campos numericos
    /// de la misma etapa que se suman. Los campos multiples suman todos sus registros.</summary>
    public string? TotalSourceKeys { get; set; }

    /// <summary>Si se indica el FieldKey de un campo numerico de la misma etapa, este campo se
    /// repite N veces segun el valor de ese campo. Valores como arreglo JSON.</summary>
    public string? RepeatWithFieldKey { get; set; }
}
