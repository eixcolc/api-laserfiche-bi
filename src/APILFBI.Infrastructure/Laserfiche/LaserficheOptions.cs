using System.ComponentModel.DataAnnotations;

namespace APILFBI.Infrastructure.Laserfiche;

/// <summary>
/// Configuración de Laserfiche 11 (Repository API self-hosted). Las rutas son plantillas con
/// {repositorio} y {entryId}, para ajustarlas a la versión instalada sin cambiar código.
/// Usuario y Contrasena (cuenta de servicio) van en User Secrets o Key Vault, nunca en appsettings.
/// </summary>
public sealed class LaserficheOptions
{
    public const string Seccion = "Laserfiche";

    /// <summary>true = no se conecta a Laserfiche; usa documentos simulados.</summary>
    public bool Simulado { get; set; } = true;

    public string UrlBase { get; set; } = string.Empty;
    public string RepositorioId { get; set; } = string.Empty;
    public string Usuario { get; set; } = string.Empty;
    public string Contrasena { get; set; } = string.Empty;

    [Range(5, 600)] public int TimeoutSegundos { get; set; } = 60;
    [Range(0, 10)] public int Reintentos { get; set; } = 3;

    /// <summary>Segundos antes del vencimiento en que se renueva el token.</summary>
    [Range(0, 3600)] public int RenovarTokenAntesSegundos { get; set; } = 120;

    public RutasLaserfiche Rutas { get; set; } = new();
    public CamposLaserfiche Campos { get; set; } = new();
    public SimulacionLaserfiche Simulacion { get; set; } = new();
}

public sealed class RutasLaserfiche
{
    public string Token { get; set; } = "v1/Repositories/{repositorio}/Token";
    public string Descarga { get; set; } = "v1/Repositories/{repositorio}/Entries/{entryId}/Laserfiche.Repository.Document/edoc";
    public string Campos { get; set; } = "v1/Repositories/{repositorio}/Entries/{entryId}/fields";
    public string Salud { get; set; } = "v1/Repositories";
}

/// <summary>Nombres de los campos de la plantilla de Laserfiche que actualiza la API.</summary>
public sealed class CamposLaserfiche
{
    public string Estado { get; set; } = "Estado";
    public string EjecutivoAsignado { get; set; } = "EjecutivoAsignado";
    public string TipoRechazo { get; set; } = "TipoRechazo";
    public string ComentarioRevision { get; set; } = "ComentarioRevision";
    public string EtapaRechazo { get; set; } = "EtapaRechazo";
}

public sealed class SimulacionLaserfiche
{
    /// <summary>Carpeta opcional con archivos {entryId}.{ext}. Si no hay archivo, se genera un PDF de prueba.</summary>
    public string? CarpetaArchivos { get; set; }
}
