using APILFBI.Domain.Entidades;

namespace APILFBI.Application.Abstracciones;

/// <summary>
/// Caché en memoria de los catálogos (esquema cat). Cada instancia de la API tiene la suya y la
/// invalida por catálogo cuando cambia cat.VersionCatalogo.
/// </summary>
public interface ICatalogoCache
{
    /// <summary>true cuando terminó la precarga inicial. Lo usa /health/ready.</summary>
    bool Precargado { get; }

    Task<IReadOnlyList<T>> ObtenerAsync<T>(CancellationToken ct = default) where T : class;

    /// <summary>Lectura sincrónica del último catálogo de códigos cargado (null si aún no se cargó).</summary>
    CodigoRespuesta? ObtenerCodigoRespuesta(int codigo);

    /// <summary>
    /// Tipos de expediente activos cuya configuración de llaves es inválida (ej. se desactivó una
    /// llave y un grupo quedó con una sola). Se excluyen de la API. Id → problema.
    /// </summary>
    Task<IReadOnlyDictionary<int, string>> ObtenerTiposExpedienteInvalidosAsync(CancellationToken ct = default);
}
