using APILFBI.Application.Expedientes;

namespace APILFBI.Application.Cargas;

/// <summary>
/// Metadata del documento que el CRM envía junto con el archivo (campos sueltos del formulario).
/// Equivale a informacionDocumento del XML de carga; la API arma el XML con estos datos.
/// </summary>
public sealed record SolicitudCargaDocumento(
    int IdTipoDocumento,
    DateOnly? FechaEmision,
    DateOnly? FechaVencimiento,
    string? Comentario,
    int? IdDocumentoReemplaza,
    string? NombreUsuarioCarga,
    string? Correlativo,
    string? HashSha256);

/// <summary>El archivo recibido (sin dependencia de ASP.NET). El stream debe permitir volver al inicio.</summary>
public sealed record ArchivoCarga(Stream Contenido, string NombreOriginal, long TamanoBytes);

/// <summary>Datos con que se arma {Correlativo}_data.xml (contrato v1.0).</summary>
public sealed record DatosXmlCarga(
    long IdExpediente,
    string Correlativo,
    int IdTipoDocumento,
    string NombreDocumento,
    string HashSha256,
    DateOnly FechaEmision,
    DateOnly? FechaVencimiento,
    string? Comentario,
    int? IdDocumentoReemplaza,
    string UsuarioCarga,
    string NombreUsuarioCarga,
    DateTimeOffset FechaHoraCarga);

public sealed record RespuestaCarga(
    string Correlativo,
    string EstadoCarga,
    long IdExpediente,
    int IdTipoDocumento,
    string NombreDocumento,
    long TamanoBytes,
    string HashSha256);

/// <summary>Deja el par archivo + XML en la carpeta que vigila Import Agent.</summary>
public interface IAlmacenCargas
{
    /// <summary>Arma el XML del contrato v1.0 y lo valida contra el XSD.</summary>
    string GenerarXml(DatosXmlCarga datos);

    /// <summary>Escribe primero el archivo y al final el XML, como .tmp, y luego los renombra.</summary>
    Task EscribirAsync(string correlativo, string extension, Stream archivo, string xml, CancellationToken ct = default);
}

public interface IRepositorioCargas
{
    /// <summary>trx.usp_RecibirCarga: valida y reserva el correlativo como Recibido. Devuelve el código de respuesta.</summary>
    Task<int> RecibirAsync(DatosXmlCarga datos, long tamanoBytes, string xml, ContextoOperacion contexto, CancellationToken ct = default);

    /// <summary>trx.usp_AnularRecepcionCarga: libera el correlativo si no se pudo dejar el archivo.</summary>
    Task AnularAsync(string correlativo, CancellationToken ct = default);
}

public sealed record ResultadoEscaneo(bool Limpio, string? Detalle = null);

/// <summary>
/// Escaneo antivirus de los archivos recibidos por la API. La implementación por defecto no escanea;
/// se reemplaza por la que defina el equipo de seguridad (Defender, ICAP, etc.).
/// </summary>
public interface IEscanerAntivirus
{
    Task<ResultadoEscaneo> EscanearAsync(Stream contenido, string nombreArchivo, CancellationToken ct = default);
}
