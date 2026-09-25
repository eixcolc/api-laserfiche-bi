using APILFBI.Application.Abstracciones;
using APILFBI.Application.Comun;
using APILFBI.Application.Expedientes;
using FluentValidation;

namespace APILFBI.Application.Estados;

/// <summary>Un documento del lote. IdDocumento es el ID único de Laserfiche; los códigos son de catálogo.</summary>
public sealed record CambioEstadoItem(int IdDocumento, string? CodigoEstado, string? CodigoTipoRechazo, string? CodigoEtapaRechazo, string? Comentario);

/// <summary>Criterio 10: cambio de estado de un lote de documentos de un expediente.</summary>
public sealed record SolicitudCambioEstado(long IdExpediente, string? EjecutivoAsignado, bool PermitirParcial, IReadOnlyList<CambioEstadoItem>? Documentos);

/// <summary>Resultado por documento que devuelve trx.usp_CambiarEstadoDocumentos.</summary>
public sealed record ResultadoCambioEstadoItem(int IdDocumento, bool Aplicado, int Codigo, string? Mensaje, string EstadoSolicitado, long? IdSincronizacion);

public sealed record ResultadoCambioEstado(int Codigo, IReadOnlyList<ResultadoCambioEstadoItem> Documentos);

public sealed record DocumentoCambioEstadoDto(int IdDocumento, string EstadoSolicitado, bool Aplicado, int Codigo, string? Mensaje);

public sealed record RespuestaCambioEstado(long IdExpediente, IReadOnlyList<DocumentoCambioEstadoDto> Documentos);

public interface IRepositorioEstados
{
    Task<ResultadoCambioEstado> CambiarAsync(SolicitudCambioEstado solicitud, ContextoOperacion contexto, CancellationToken ct = default);
}

/// <summary>Cola en memoria para actualizar Laserfiche de inmediato; lo que no se procese lo retoma el Worker.</summary>
public interface IColaSincronizacionLaserfiche
{
    void Encolar(IReadOnlyCollection<long> idsSincronizacion);
}

public sealed class SolicitudCambioEstadoValidator : AbstractValidator<SolicitudCambioEstado>
{
    public const int MaximoDocumentos = 100;

    public SolicitudCambioEstadoValidator()
    {
        var obligatorio = CodigosRespuesta.CampoObligatorio.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var formato = CodigosRespuesta.FormatoCampoInvalido.ToString(System.Globalization.CultureInfo.InvariantCulture);

        RuleFor(x => x.IdExpediente).GreaterThan(0).WithErrorCode(obligatorio).WithMessage("idExpediente es obligatorio.");
        RuleFor(x => x.EjecutivoAsignado).MaximumLength(200).WithErrorCode(formato).WithMessage("ejecutivoAsignado admite 200 caracteres.");
        RuleFor(x => x.Documentos)
            .NotEmpty().WithErrorCode(obligatorio).WithMessage("Debe enviar al menos un documento.")
            .Must(d => d is null || d.Count <= MaximoDocumentos).WithErrorCode(formato).WithMessage($"No se admiten más de {MaximoDocumentos} documentos por lote.");
        RuleForEach(x => x.Documentos).ChildRules(d =>
        {
            d.RuleFor(i => i.IdDocumento).GreaterThan(0).WithErrorCode(obligatorio).WithMessage("idDocumento es obligatorio.");
            d.RuleFor(i => i.CodigoEstado).NotEmpty().WithErrorCode(obligatorio).WithMessage("codigoEstado es obligatorio.")
                .MaximumLength(50).WithErrorCode(formato).WithMessage("codigoEstado admite 50 caracteres.");
            d.RuleFor(i => i.CodigoTipoRechazo).MaximumLength(50).WithErrorCode(formato).WithMessage("codigoTipoRechazo admite 50 caracteres.");
            d.RuleFor(i => i.CodigoEtapaRechazo).MaximumLength(50).WithErrorCode(formato).WithMessage("codigoEtapaRechazo admite 50 caracteres.");
            d.RuleFor(i => i.Comentario).MaximumLength(1000).WithErrorCode(formato).WithMessage("comentario admite 1000 caracteres.");
        });
    }
}

/// <summary>
/// Criterio 10. Las reglas (transiciones, tipo de rechazo, comentario) las valida el stored
/// procedure contra los catálogos. La base es la fuente de verdad; Laserfiche se actualiza
/// después mediante el outbox trx.SincronizacionLaserfiche.
/// </summary>
public sealed class EstadosService(
    IRepositorioEstados repositorio,
    IColaSincronizacionLaserfiche cola,
    IContextoUsuario usuario,
    IValidator<SolicitudCambioEstado> validador)
{
    public async Task<(int Codigo, RespuestaCambioEstado Respuesta)> CambiarAsync(SolicitudCambioEstado solicitud, string endpoint, CancellationToken ct = default)
    {
        var validacion = await validador.ValidateAsync(solicitud, ct);
        if (!validacion.IsValid)
            throw new ApiException(int.Parse(validacion.Errors[0].ErrorCode, System.Globalization.CultureInfo.InvariantCulture),
                errores: validacion.Errors.Select(e => new ErrorCampo(
                    string.Join('.', e.PropertyName.Split('.').Select(p => char.ToLowerInvariant(p[0]) + p[1..])), e.ErrorMessage)).ToList());

        if (usuario.UsuarioOperacion is null)
            throw new ApiException(CodigosRespuesta.CampoObligatorio,
                errores: [new ErrorCampo("X-Operation-User", "Las operaciones de escritura requieren el usuario de operación.")]);

        var contexto = new ContextoOperacion(usuario.UsuarioServicio, usuario.UsuarioOperacion, usuario.IpOrigen, usuario.CorrelationId, endpoint);
        var resultado = await repositorio.CambiarAsync(solicitud, contexto, ct);

        var sincronizaciones = resultado.Documentos.Where(d => d.IdSincronizacion is not null).Select(d => d.IdSincronizacion!.Value).ToList();
        if (sincronizaciones.Count > 0) cola.Encolar(sincronizaciones);

        var respuesta = new RespuestaCambioEstado(solicitud.IdExpediente, resultado.Documentos
            .Select(d => new DocumentoCambioEstadoDto(d.IdDocumento, d.EstadoSolicitado, d.Aplicado, d.Codigo, d.Mensaje))
            .ToList());

        return resultado.Codigo is CodigosRespuesta.Exito or CodigosRespuesta.ExitoParcial
            ? (resultado.Codigo, respuesta)
            : throw new ApiException(resultado.Codigo, datos: respuesta);
    }
}
