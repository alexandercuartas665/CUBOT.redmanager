namespace CubotRedManager.Web.BackgroundJobs;

/// <summary>
/// Opciones del AutoReplyWorker. Bind desde appsettings:AutoReply. Permite afinar cadencia y
/// activar dry-run (no toca TikTok real, util en dev/sandbox donde el API no permite replies).
/// </summary>
public sealed class AutoReplyOptions
{
    /// <summary>Cada cuanto despierta el worker para escanear cuentas activas. Default 60s.</summary>
    public TimeSpan CheckInterval { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Si true, NO llama a TikTok reply API: deja todo en estado de simulacion pero igual
    /// escribe el log con la traza completa (lo que HABRIA respondido). Util para verificar
    /// la logica del worker sin que TikTok rebote por sandbox.
    /// </summary>
    public bool DryRun { get; set; } = true;

    /// <summary>
    /// Zona horaria del tenant para evaluar ActiveHoursMask / ActiveDaysOfWeekMask.
    /// Default "America/Bogota". Si la zona no existe, fallback a Local.
    /// </summary>
    public string TenantTimeZone { get; set; } = "America/Bogota";

    /// <summary>
    /// Cuantos comentarios sin responder lee el worker por video al evaluar la cola. Tope duro
    /// independiente del MaxRepliesPerRun de la config (eso filtra cuantos efectivamente envia).
    /// </summary>
    public int MaxPendingFetchPerAccount { get; set; } = 100;

    /// <summary>
    /// Retencion en dias de AutoReplyJobLogs. Cada ciclo del worker borra logs mas viejos que
    /// este valor. Default 5. Pon 0 para deshabilitar la purga automatica.
    /// </summary>
    public int RetentionDays { get; set; } = 5;
}
