using CubotRedManager.Domain.Common;
using CubotRedManager.Domain.Enums;

namespace CubotRedManager.Domain.Entities;

/// <summary>
/// Campo configurable del perfil de Contacto (patron de PipelineFieldDefinition de CUBOT.travels,
/// pero agrupado por Section en vez de por etapa: el perfil de un cliente no avanza por etapas).
/// Entidad TENANT-SCOPED: cada agencia arma su propio perfil. Al primer acceso se siembran los
/// campos de marketing por defecto (IsSystem=true), que la agencia puede editar u ocultar.
/// </summary>
public class ContactFieldDefinition : TenantEntity
{
    /// <summary>Seccion de la ficha donde se agrupa (ej. "Salud y objetivos").</summary>
    public string Section { get; set; } = null!;

    /// <summary>Clave estable (no cambia tras creado). Es la llave en Contact.FieldValuesJson.</summary>
    public string FieldKey { get; set; } = null!;

    public string Label { get; set; } = null!;
    public ContactFieldType FieldType { get; set; } = ContactFieldType.Text;

    /// <summary>Opciones para Select/MultiSelect, una por linea.</summary>
    public string? Options { get; set; }

    /// <summary>Para que sirve el campo. Ayuda a quien llena la ficha y sirve de contexto al agente IA.</summary>
    public string? Description { get; set; }

    /// <summary>1 = media columna, 2 = ancho completo.</summary>
    public int Column { get; set; } = 1;

    public int SortOrder { get; set; }

    /// <summary>Si es true, el valor se muestra como chip en la tarjeta del directorio.</summary>
    public bool ShowInList { get; set; }

    /// <summary>Si es true, el campo se ofrece como filtro en el directorio.</summary>
    public bool ShowInFilter { get; set; }

    /// <summary>Campo sembrado por defecto (preset de marketing). Se puede editar y ocultar, pero
    /// su FieldKey se conserva para que integraciones (recordatorios, IA) lo encuentren.</summary>
    public bool IsSystem { get; set; }

    /// <summary>Campo oculto: no se muestra en la ficha pero conserva los valores ya capturados.</summary>
    public bool IsHidden { get; set; }
}
