using System.ComponentModel.DataAnnotations;

namespace APILFBI.Worker.Trabajos;

public sealed class WorkerOptions
{
    public const string Seccion = "Worker";

    /// <summary>Zona horaria de las horas programadas (Windows o IANA).</summary>
    [Required] public string ZonaHoraria { get; set; } = "Central America Standard Time";

    /// <summary>Hora local del job de vencimiento (HH:mm).</summary>
    [Required] public string HoraVencimiento { get; set; } = "01:00";

    /// <summary>Hora local de la limpieza de llaves de idempotencia (HH:mm).</summary>
    [Required] public string HoraLimpieza { get; set; } = "02:00";

    /// <summary>Cada cuántos segundos se reintentan las actualizaciones pendientes hacia Laserfiche.</summary>
    [Range(5, 3600)] public int IntervaloSincronizacionSegundos { get; set; } = 30;

    /// <summary>Ejecuta el vencimiento al arrancar, además de a la hora programada (útil si el servidor estuvo apagado).</summary>
    public bool EjecutarVencimientoAlIniciar { get; set; }

    public TimeZoneInfo Zona() => TimeZoneInfo.FindSystemTimeZoneById(ZonaHoraria);
}
