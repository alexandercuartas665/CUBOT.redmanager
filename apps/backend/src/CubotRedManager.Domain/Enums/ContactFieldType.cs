namespace CubotRedManager.Domain.Enums;

/// <summary>
/// Tipos de campo del perfil de un Contacto. Toma la base de PipelineFieldType (travels) y agrega
/// los tipos que un perfil de marketing necesita: seleccion multiple (intereses, sabores), si/no
/// (autorizaciones), calificacion (satisfaccion) y email. Todos los valores se guardan como string
/// en Contact.FieldValuesJson: MultiSelect como arreglo JSON, Boolean como "true"/"false",
/// Rating como "1".."5".
/// </summary>
public enum ContactFieldType
{
    Text,
    TextArea,
    Number,
    Currency,
    Date,
    Phone,
    Email,
    Select,
    MultiSelect,
    Boolean,
    Rating,
    /// <summary>Titulo visual dentro de una seccion. No captura valor.</summary>
    Separator
}
