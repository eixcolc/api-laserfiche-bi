namespace APILFBI.Application.Documentos;

/// <summary>Criterio 7: consulta de documentos de un cliente natural.</summary>
public sealed record ConsultaNatural(int IdTipoExpediente, string? NoCaso, string? Cif, string? TipoIdentificacion, string? NoIdentificacion);

/// <summary>Criterio 8: consulta de documentos de un cliente jurídico.</summary>
public sealed record ConsultaJuridica(int IdTipoExpediente, string? NoCaso, string? Cif, string? Rtn);

/// <summary>
/// Un documento del expediente. Si el tipo de documento aún no tiene archivo, viene con
/// Estado = "SinSubir" e IdDocumento = null.
/// </summary>
public sealed record DocumentoConsultaDto(
    int? IdDocumento,
    int IdTipoDocumento,
    string TipoDocumento,
    bool Obligatorio,
    string Estado,
    string EstadoNombre,
    string? Segmentacion,
    DateTime? FechaHoraRecepcion,
    DateOnly? FechaVencimiento,
    string? NombreEmpleadoAdjunto,
    string? Comentario,
    int? Version);

public sealed record ConsultaNaturalRespuesta(
    long? IdExpediente, string? NoCaso, string? Cif, string? TipoIdentificacion, string? NoIdentificacion,
    int IdTipoExpediente, IReadOnlyList<DocumentoConsultaDto> Documentos);

public sealed record ConsultaJuridicaRespuesta(
    long? IdExpediente, string? NoCaso, string? Cif, string? Rtn,
    int IdTipoExpediente, IReadOnlyList<DocumentoConsultaDto> Documentos);

public sealed record DocumentosExpedienteRespuesta(
    long IdExpediente, int IdTipoExpediente, string TipoCliente, string EstadoExpediente, IReadOnlyList<DocumentoConsultaDto> Documentos);

public sealed record CargaDto(
    string Correlativo, string EstadoCarga, int? IdDocumento, long? IdExpediente, int? IdTipoDocumento,
    string? MotivoRechazo, string? DetalleRechazo, DateTime FechaHoraRecepcion);

/// <summary>Fila de trx.Documento asociada (vigente) a un expediente.</summary>
public sealed record DocumentoExpedienteFila(
    long IdDocumento, int LaserficheEntryId, int IdTipoDocumento, int IdEstadoDocumento, int Version,
    DateTime FechaHoraRecepcion, DateOnly? FechaVencimiento, string NombreUsuarioCarga, string? Comentario);

public sealed record DocumentoArchivo(long IdDocumento, int LaserficheEntryId, long IdExpedienteOrigen, string NombreArchivo, int IdTipoArchivo);

public interface IRepositorioDocumentos
{
    Task<IReadOnlyList<DocumentoExpedienteFila>> DocumentosDeExpedienteAsync(long idExpediente, CancellationToken ct = default);
    Task<DocumentoArchivo?> ObtenerPorEntryIdAsync(int laserficheEntryId, CancellationToken ct = default);
    Task<Domain.Entidades.CargaDocumento?> ObtenerCargaAsync(string correlativo, CancellationToken ct = default);
}
