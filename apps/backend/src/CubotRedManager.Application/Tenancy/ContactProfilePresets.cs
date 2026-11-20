using CubotRedManager.Domain.Enums;

namespace CubotRedManager.Application.Tenancy;

/// <summary>
/// Campos por defecto del perfil de Contacto, pensados para venta recurrente de productos de
/// bienestar (FUXION) por WhatsApp. Se siembran la primera vez que el tenant abre el directorio.
/// Las FieldKeys son estables: los recordatorios (Fase 3) y el agente IA las usan por nombre
/// (ej. acepta_whatsapp, canal_preferido, meta_principal).
/// </summary>
public static class ContactProfilePresets
{
    public sealed record Preset(string Section, string Key, string Label, ContactFieldType Type,
        string? Options = null, string? Description = null, int Column = 1, bool ShowInList = false, bool ShowInFilter = false);

    public const string SecPerfil = "Perfil personal";
    public const string SecSalud = "Salud y objetivos";
    public const string SecConsumo = "Preferencias de consumo";
    public const string SecRelacion = "Relacion y fidelizacion";
    public const string SecConsent = "Consentimiento y origen";

    private const string Metas =
        "Control de peso\nEnergia y vitalidad\nDigestion\nEstres y sueno\nRendimiento deportivo\nDefensas e inmunidad\nBelleza y antiedad\nControl de azucar";

    public static readonly IReadOnlyList<Preset> All = new[]
    {
        // Perfil personal: para personalizar el mensaje y el momento del contacto.
        new Preset(SecPerfil, "genero", "Genero", ContactFieldType.Select,
            "Femenino\nMasculino\nOtro\nPrefiere no decir"),
        new Preset(SecPerfil, "ocupacion", "Ocupacion", ContactFieldType.Text,
            Description: "Ayuda a ajustar el tono y los horarios (ej. docente, enfermera, independiente)."),
        new Preset(SecPerfil, "hogar", "Hogar", ContactFieldType.Select,
            "Vive solo/a\nEn pareja\nCon hijos\nFamilia extendida",
            "Quien mas en casa podria consumir: oportunidad de venta cruzada familiar."),
        new Preset(SecPerfil, "canal_preferido", "Canal preferido", ContactFieldType.Select,
            "WhatsApp\nLlamada\nEmail\nInstagram", "Por donde prefiere que le escriban.", ShowInFilter: true),
        new Preset(SecPerfil, "horario_contacto", "Mejor horario", ContactFieldType.Select,
            "Manana\nMedio dia\nTarde\nNoche\nFin de semana", "Cuando suele responder. Los recordatorios salen en esa franja."),

        // Salud y objetivos: el "por que" compra. Define que producto recomendar.
        new Preset(SecSalud, "meta_principal", "Meta principal", ContactFieldType.Select, Metas,
            "El resultado que mas le importa. Es el angulo de cada mensaje.", ShowInList: true, ShowInFilter: true),
        new Preset(SecSalud, "metas_secundarias", "Otras metas", ContactFieldType.MultiSelect, Metas,
            "Intereses adicionales: base para recomendar un segundo producto (venta cruzada).", Column: 2),
        new Preset(SecSalud, "condiciones_salud", "Condiciones y medicamentos", ContactFieldType.TextArea,
            Description: "Dato sensible (Ley 1581). Solo lo necesario para recomendar con responsabilidad: diabetes, hipertension, embarazo, medicamentos.",
            Column: 2),
        new Preset(SecSalud, "alergias", "Alergias o intolerancias", ContactFieldType.Text),
        new Preset(SecSalud, "progreso", "Avances reportados", ContactFieldType.TextArea,
            Description: "Lo que el cliente cuenta que ha mejorado. Material para testimonios (con su permiso).", Column: 2),

        // Preferencias de consumo: que le gusta y como compra.
        new Preset(SecConsumo, "sabores", "Sabores preferidos", ContactFieldType.MultiSelect,
            "Frutos rojos\nCitricos\nTropical\nChocolate\nCafe\nVainilla\nNeutro", Column: 2),
        new Preset(SecConsumo, "frecuencia_declarada", "Frecuencia que dice tener", ContactFieldType.Select,
            "Diaria\nSemanal\nQuincenal\nMensual\nBimestral\nOcasional",
            "Lo que el cliente dice. Comparalo con la cadencia real calculada del historial."),
        new Preset(SecConsumo, "sensibilidad_precio", "Sensibilidad al precio", ContactFieldType.Select,
            "Baja (prioriza resultado)\nMedia\nAlta (busca promociones)",
            "Define si ofrecerle kit completo o empezar por un producto.", ShowInFilter: true),
        new Preset(SecConsumo, "pago_preferido", "Medio de pago preferido", ContactFieldType.Select,
            "Link de pago\nTransferencia\nTarjeta\nEfectivo"),
        new Preset(SecConsumo, "motivador_compra", "Que lo motiva a comprar", ContactFieldType.Select,
            "Resultados que siente\nRecomendacion de alguien\nPrecio o promocion\nAsesoria personalizada\nComunidad o retos"),

        // Relacion y fidelizacion: que tan cerca esta y cuanto vale para la red.
        new Preset(SecRelacion, "satisfaccion", "Satisfaccion", ContactFieldType.Rating,
            Description: "1 a 5 estrellas segun su ultima experiencia.", ShowInList: true),
        new Preset(SecRelacion, "nps", "Te recomendaria (0-10)", ContactFieldType.Number,
            Description: "Pregunta NPS: de 0 a 10, que tan probable es que recomiende el producto. 9-10 promotor, 0-6 detractor."),
        new Preset(SecRelacion, "referidos", "Referidos que ha traido", ContactFieldType.Number),
        new Preset(SecRelacion, "potencial_red", "Potencial para la red", ContactFieldType.Select,
            "Si, ya mostro interes\nTal vez\nNo",
            "Clientes satisfechos y recurrentes son los mejores candidatos a distribuidor (pipeline Negocios).", ShowInFilter: true),
        new Preset(SecRelacion, "fechas_especiales", "Fechas especiales", ContactFieldType.TextArea,
            Description: "Aniversarios, cumpleanos de familiares, fechas para un detalle o una promo personal.", Column: 2),

        // Consentimiento y origen: requisito legal (Ley 1581 de 2012) y atribucion de campanas.
        new Preset(SecConsent, "acepta_whatsapp", "Autoriza mensajes por WhatsApp", ContactFieldType.Boolean,
            Description: "Autorizacion de tratamiento de datos (Ley 1581). Sin esto no se envian recordatorios por WhatsApp.",
            ShowInFilter: true),
        new Preset(SecConsent, "acepta_email", "Autoriza mensajes por email", ContactFieldType.Boolean,
            Description: "Sin esto no se envian recordatorios por correo.", ShowInFilter: true),
        new Preset(SecConsent, "fecha_autorizacion", "Fecha de autorizacion", ContactFieldType.Date),
        new Preset(SecConsent, "fuente", "Como llego", ContactFieldType.Select,
            "Instagram\nTikTok\nFacebook\nWhatsApp\nReferido\nEvento\nOtro",
            "Canal de adquisicion: mide que redes traen clientes que si recompran.", ShowInFilter: true),
        new Preset(SecConsent, "referido_por", "Referido por", ContactFieldType.Text,
            Description: "Nombre de quien lo recomendo, para agradecerle."),
    };

    /// <summary>Orden de las secciones en la ficha. Secciones creadas por la agencia van al final.</summary>
    public static readonly IReadOnlyList<string> SectionOrder = new[] { SecPerfil, SecSalud, SecConsumo, SecRelacion, SecConsent };
}
