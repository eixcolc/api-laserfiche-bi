using System.Globalization;
using APILFBI.Application.Abstracciones;
using APILFBI.Application.Comun;
using APILFBI.Application.Expedientes;
using APILFBI.Domain.Entidades;
using FluentValidation;

namespace APILFBI.Application.Documentos;

public sealed class DocumentosService(
    IRepositorioExpedientes expedientes,
    IRepositorioDocumentos documentos,
    ILaserficheRepositoryService laserfiche,
    ICatalogoCache catalogos,
    IContextoUsuario usuario,
    IBitacoraService bitacora,
    IValidator<ConsultaNatural> validadorNatural,
    IValidator<ConsultaJuridica> validadorJuridica)
{
    private const string EstadoSinSubir = "SinSubir";

    /// <summary>Criterio 7. Si el expediente aún no existe, devuelve todo como "Sin subir".</summary>
    public async Task<ConsultaNaturalRespuesta> ConsultarNaturalAsync(ConsultaNatural c, string endpoint, CancellationToken ct = default)
    {
        await ValidarAsync(validadorNatural, c, ct);
        var llaves = Llaves(("TipoCliente", "N"), ("noCasoCRM", c.NoCaso), ("CIF", c.Cif),
                            ("tipoIdentificacion", c.TipoIdentificacion), ("noIdentificacion", c.NoIdentificacion));
        var (idExpediente, docs) = await ConsultarAsync(c.IdTipoExpediente, "N", llaves, endpoint, ct);
        return new ConsultaNaturalRespuesta(idExpediente, c.NoCaso, c.Cif, c.TipoIdentificacion, c.NoIdentificacion, c.IdTipoExpediente, docs);
    }

    /// <summary>Criterio 8. Si el expediente aún no existe, devuelve todo como "Sin subir".</summary>
    public async Task<ConsultaJuridicaRespuesta> ConsultarJuridicaAsync(ConsultaJuridica c, string endpoint, CancellationToken ct = default)
    {
        await ValidarAsync(validadorJuridica, c, ct);
        var llaves = Llaves(("TipoCliente", "J"), ("noCasoCRM", c.NoCaso), ("CIF", c.Cif), ("RTN", c.Rtn));
        var (idExpediente, docs) = await ConsultarAsync(c.IdTipoExpediente, "J", llaves, endpoint, ct);
        return new ConsultaJuridicaRespuesta(idExpediente, c.NoCaso, c.Cif, c.Rtn, c.IdTipoExpediente, docs);
    }

    /// <summary>Misma respuesta que 7 y 8, directo por idExpediente.</summary>
    public async Task<DocumentosExpedienteRespuesta> DocumentosDeExpedienteAsync(long idExpediente, string endpoint, CancellationToken ct = default)
    {
        var expediente = await expedientes.ObtenerAsync(idExpediente, ct)
                         ?? throw new ApiException(CodigosRespuesta.ExpedienteNoEncontrado);
        var docs = await ArmarDocumentosAsync(expediente.IdTipoExpediente, expediente.IdTipoCliente, expediente, ct);

        var clientes = await catalogos.ObtenerAsync<TipoCliente>(ct);
        var estados = await catalogos.ObtenerAsync<EstadoExpediente>(ct);
        Registrar(TiposOperacion.ConsultaDocumentos, CodigosRespuesta.Exito, endpoint, idExpediente, detalle: $"Documentos: {docs.Count}");

        return new DocumentosExpedienteRespuesta(expediente.Id, expediente.IdTipoExpediente,
            clientes.First(x => x.Id == expediente.IdTipoCliente).Codigo,
            estados.First(x => x.Id == expediente.IdEstadoExpediente).Codigo, docs);
    }

    /// <summary>Estado de una carga hecha por SFTP (criterios 11-13).</summary>
    public async Task<CargaDto> ObtenerCargaAsync(string correlativo, CancellationToken ct = default)
    {
        var carga = await documentos.ObtenerCargaAsync(correlativo, ct)
                    ?? throw new ApiException(CodigosRespuesta.CargaNoEncontrada);
        var estados = await catalogos.ObtenerAsync<EstadoCarga>(ct);
        var motivos = await catalogos.ObtenerAsync<MotivoRechazoCarga>(ct);

        var estado = estados.First(e => e.Id == carga.IdEstadoCarga).Codigo;
        return new CargaDto(
            carga.Correlativo,
            estado,
            // Solo una carga importada tiene documento en BILF; la rechazada no se puede consultar ni descargar.
            estado == "Importado" ? carga.LaserficheEntryId : null,
            carga.IdExpediente,
            carga.IdTipoDocumento,
            carga.IdMotivoRechazoCarga is { } m ? motivos.FirstOrDefault(x => x.Id == m)?.Codigo : null,
            carga.DetalleRechazo,
            carga.FechaHoraRecepcion);
    }

    /// <summary>Criterio 9: el documento se entrega en stream desde Laserfiche.</summary>
    public async Task<(ContenidoDocumento Contenido, string NombreArchivo, string ContentType)> DescargarAsync(
        int idDocumento, string? rango, string endpoint, CancellationToken ct = default)
    {
        var documento = await documentos.ObtenerPorEntryIdAsync(idDocumento, ct);
        if (documento is null)
        {
            Registrar(TiposOperacion.DescargaDocumento, CodigosRespuesta.DocumentoNoEncontrado, endpoint, detalle: $"LaserficheEntryId: {idDocumento}");
            throw new ApiException(CodigosRespuesta.DocumentoNoEncontrado);
        }

        try
        {
            var contenido = await laserfiche.DescargarAsync(documento.LaserficheEntryId, rango, ct);
            var tipoArchivo = (await catalogos.ObtenerAsync<TipoArchivo>(ct)).FirstOrDefault(t => t.Id == documento.IdTipoArchivo);
            Registrar(TiposOperacion.DescargaDocumento, contenido.EsParcial ? CodigosRespuesta.ContenidoParcial : CodigosRespuesta.Exito,
                endpoint, documento.IdExpedienteOrigen, documento.IdDocumento, $"LaserficheEntryId: {idDocumento}{(rango is null ? "" : $". Range: {rango}")}");
            return (contenido, documento.NombreArchivo, tipoArchivo?.MimeType ?? contenido.ContentType ?? "application/octet-stream");
        }
        catch (ApiException ex)
        {
            Registrar(TiposOperacion.DescargaDocumento, ex.Codigo, endpoint, documento.IdExpedienteOrigen, documento.IdDocumento, ex.Detalle);
            throw;
        }
    }

    private async Task<(long? IdExpediente, IReadOnlyList<DocumentoConsultaDto> Documentos)> ConsultarAsync(
        int idTipoExpediente, string tipoCliente, IReadOnlyList<LlaveValor> llaves, string endpoint, CancellationToken ct)
    {
        var invalidos = await catalogos.ObtenerTiposExpedienteInvalidosAsync(ct);
        var tipo = (await catalogos.ObtenerAsync<TipoExpediente>(ct)).FirstOrDefault(t => t.Id == idTipoExpediente && t.Activo && !invalidos.ContainsKey(t.Id))
                   ?? throw new ApiException(CodigosRespuesta.TipoExpedienteNoEncontrado);
        var idTipoCliente = (await catalogos.ObtenerAsync<TipoCliente>(ct)).First(c => c.Codigo == tipoCliente).Id;

        var contexto = new ContextoOperacion(usuario.UsuarioServicio, usuario.UsuarioOperacion, usuario.IpOrigen, usuario.CorrelationId, endpoint);
        var busqueda = await expedientes.BuscarAsync(tipo.Id, llaves, contexto, ct);

        if (busqueda.Codigo is not (CodigosRespuesta.Exito or CodigosRespuesta.ExpedienteNoEncontrado))
        {
            Registrar(TiposOperacion.ConsultaDocumentos, busqueda.Codigo, endpoint, detalle: DetalleLlaves(llaves));
            throw new ApiException(busqueda.Codigo,
                errores: busqueda.Errores.Select(e => new ErrorCampo(e.CodigoLlave ?? "llaves", e.Mensaje)).ToList());
        }

        Expediente? expediente = busqueda.IdExpediente is { } id ? await expedientes.ObtenerAsync(id, ct) : null;
        var docs = await ArmarDocumentosAsync(tipo.Id, idTipoCliente, expediente, ct);

        Registrar(TiposOperacion.ConsultaDocumentos, CodigosRespuesta.Exito, endpoint, expediente?.Id,
            detalle: $"{DetalleLlaves(llaves)}. {(expediente is null ? "Expediente aún no existe" : $"Documentos: {docs.Count(d => d.IdDocumento is not null)}")}");
        return (expediente?.Id, docs);
    }

    /// <summary>
    /// Arma la lista del expediente: por cada tipo de documento que lleva el tipo de expediente,
    /// sus documentos vigentes o una fila "Sin subir". Al final, los documentos de tipos que no
    /// están en el catálogo (ej. asociados manualmente).
    /// </summary>
    private async Task<IReadOnlyList<DocumentoConsultaDto>> ArmarDocumentosAsync(int idTipoExpediente, int idTipoCliente, Expediente? expediente, CancellationToken ct)
    {
        var tiposDocumento = (await catalogos.ObtenerAsync<TipoDocumento>(ct)).ToDictionary(t => t.Id);
        var estados = (await catalogos.ObtenerAsync<EstadoDocumento>(ct)).ToDictionary(e => e.Id);
        var sinSubir = (await catalogos.ObtenerAsync<EstadoDocumento>(ct)).First(e => e.Codigo == EstadoSinSubir);
        var idSegmentacion = (await catalogos.ObtenerAsync<Llave>(ct)).FirstOrDefault(l => l.Codigo == "segmentacion")?.Id;
        var segmentacion = expediente?.Llaves.FirstOrDefault(l => l.Vigente && l.IdLlave == idSegmentacion)?.Valor;

        var aplicables = (await catalogos.ObtenerAsync<TipoExpedienteTipoDocumento>(ct))
            .Where(r => r.Activo && r.IdTipoExpediente == idTipoExpediente
                        && (r.IdTipoCliente is null || r.IdTipoCliente == idTipoCliente)
                        && tiposDocumento.TryGetValue(r.IdTipoDocumento, out var td) && td.Activo)
            .GroupBy(r => r.IdTipoDocumento)
            .Select(g => g.OrderByDescending(r => r.Obligatorio).First())
            .OrderBy(r => r.Orden).ThenBy(r => r.IdTipoDocumento)
            .ToList();

        var filas = expediente is null ? [] : await documentos.DocumentosDeExpedienteAsync(expediente.Id, ct);
        var porTipo = filas.ToLookup(f => f.IdTipoDocumento);

        DocumentoConsultaDto Mapear(DocumentoExpedienteFila f, bool obligatorio)
        {
            var estado = estados[f.IdEstadoDocumento];
            return new DocumentoConsultaDto(f.LaserficheEntryId, f.IdTipoDocumento,
                tiposDocumento.TryGetValue(f.IdTipoDocumento, out var td) ? td.Nombre : f.IdTipoDocumento.ToString(CultureInfo.InvariantCulture),
                obligatorio, estado.Codigo, estado.Nombre, segmentacion, f.FechaHoraRecepcion, f.FechaVencimiento,
                f.NombreUsuarioCarga, f.Comentario, f.Version);
        }

        var resultado = new List<DocumentoConsultaDto>();
        foreach (var a in aplicables)
        {
            var delTipo = porTipo[a.IdTipoDocumento].OrderByDescending(f => f.FechaHoraRecepcion).ToList();
            if (delTipo.Count == 0)
                resultado.Add(new DocumentoConsultaDto(null, a.IdTipoDocumento, tiposDocumento[a.IdTipoDocumento].Nombre, a.Obligatorio,
                    sinSubir.Codigo, sinSubir.Nombre, segmentacion, null, null, null, null, null));
            else
                resultado.AddRange(delTipo.Select(f => Mapear(f, a.Obligatorio)));
        }

        var idsAplicables = aplicables.Select(a => a.IdTipoDocumento).ToHashSet();
        resultado.AddRange(filas.Where(f => !idsAplicables.Contains(f.IdTipoDocumento))
                                .OrderBy(f => f.IdTipoDocumento).Select(f => Mapear(f, false)));
        return resultado;
    }

    private static IReadOnlyList<LlaveValor> Llaves(params (string Tipo, string? Valor)[] llaves) =>
        llaves.Where(l => !string.IsNullOrWhiteSpace(l.Valor)).Select(l => new LlaveValor(l.Tipo, l.Valor!.Trim())).ToList();

    /// <summary>Resumen para la bitácora: los datos de persona van enmascarados.</summary>
    private static string DetalleLlaves(IReadOnlyList<LlaveValor> llaves) => string.Join(", ", llaves.Select(l =>
        $"{l.Tipo}={(l.Tipo is "CIF" or "RTN" or "noIdentificacion" ? Enmascarar(l.Valor) : l.Valor)}"));

    private static string? Enmascarar(string? v) => v is null ? null : v.Length <= 4 ? new string('*', v.Length) : new string('*', v.Length - 4) + v[^4..];

    private void Registrar(string operacion, int codigo, string endpoint, long? idExpediente = null, long? idDocumento = null, string? detalle = null) =>
        bitacora.Encolar(new EntradaBitacora
        {
            TipoOperacion = operacion,
            CodigoRespuesta = codigo,
            UsuarioServicio = usuario.UsuarioServicio,
            UsuarioOperacion = usuario.UsuarioOperacion,
            IdExpediente = idExpediente,
            IdDocumento = idDocumento,
            IpOrigen = usuario.IpOrigen,
            CorrelationId = usuario.CorrelationId,
            Endpoint = endpoint,
            Detalle = detalle,
        });

    private static async Task ValidarAsync<T>(IValidator<T> validador, T solicitud, CancellationToken ct)
    {
        var validacion = await validador.ValidateAsync(solicitud, ct);
        if (!validacion.IsValid)
            throw new ApiException(int.Parse(validacion.Errors[0].ErrorCode, CultureInfo.InvariantCulture),
                errores: validacion.Errors.Select(e => new ErrorCampo(
                    string.IsNullOrEmpty(e.PropertyName) ? "body" : char.ToLowerInvariant(e.PropertyName[0]) + e.PropertyName[1..],
                    e.ErrorMessage)).ToList());
    }
}
