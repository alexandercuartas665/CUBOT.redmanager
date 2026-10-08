namespace CubotRedManager.Domain.Enums;

/// <summary>Tipo de un campo configurable del embudo. Define como se captura/renderiza.
/// Portado 1:1 de CUBOT.travels — soporta multi-valor, campos calculados (Total) y repetidos.</summary>
public enum PipelineFieldType
{
    Text,
    Number,
    Currency,
    TextArea,
    Select,
    Date,
    Phone,
    /// <summary>Campo calculado de solo lectura: suma los valores de los FieldKeys origen
    /// indicados en TotalSourceKeys. Campos multiples suman todos sus registros.</summary>
    Total,
    /// <summary>Hora simple (HH:mm).</summary>
    Time,
    /// <summary>Dos horas en un solo campo (salida y llegada), guardadas como "salida - llegada".</summary>
    TimeRange,
    /// <summary>Separador visual (linea divisoria con titulo). No captura valor.</summary>
    Separator
}
