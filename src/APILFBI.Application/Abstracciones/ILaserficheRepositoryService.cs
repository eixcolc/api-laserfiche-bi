namespace APILFBI.Application.Abstracciones;

/// <summary>
/// Contenido de un documento de Laserfiche. <see cref="Recurso"/> se libera cuando termina de
/// enviarse la respuesta (ej. la respuesta HTTP de Laserfiche que sostiene el stream).
/// </summary>
public sealed record ContenidoDocumento(
    Stream Contenido,
    string? ContentType,
    long? Longitud,
    bool EsParcial,
    string? ContentRange,
    IDisposable? Recurso);

/// <summary>
/// Acceso a Laserfiche 11. La implementación real usa la Repository API; la simulada se activa con
/// Laserfiche:Simulado = true. Se puede cambiar por una basada en el SDK sin tocar el negocio.
/// </summary>
public interface ILaserficheRepositoryService
{
    /// <summary>Descarga el documento electrónico. <paramref name="rango"/> es el header Range del cliente (opcional).</summary>
    Task<ContenidoDocumento> DescargarAsync(int entryId, string? rango, CancellationToken ct = default);

    /// <summary>Actualiza campos de la plantilla del documento; conserva los demás campos.</summary>
    Task ActualizarCamposAsync(int entryId, IReadOnlyDictionary<string, string?> campos, CancellationToken ct = default);

    /// <summary>Verifica la conexión y las credenciales (health check).</summary>
    Task<(bool Disponible, string Detalle)> VerificarAsync(CancellationToken ct = default);
}
