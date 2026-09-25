using System.Text.RegularExpressions;
using APILFBI.Application.Abstracciones;
using APILFBI.Application.Comun;
using APILFBI.Domain.Entidades;
using FluentValidation;

namespace APILFBI.Application.Expedientes;

public sealed class ExpedientesService(
    IRepositorioExpedientes repositorio,
    ICatalogoCache catalogos,
    IContextoUsuario usuario,
    IValidator<SolicitudExpediente> validador)
{
    private static readonly TimeSpan TiempoMaximoRegex = TimeSpan.FromMilliseconds(100);

    /// <summary>POST /expedientes: obtiene el expediente de ese conjunto de llaves o lo crea.</summary>
    public async Task<ResultadoExpediente> ObtenerOCrearAsync(SolicitudExpediente solicitud, string endpoint, CancellationToken ct = default)
    {
        var validacion = await validador.ValidateAsync(solicitud, ct);
        if (!validacion.IsValid)
            throw new ApiException(
                int.Parse(validacion.Errors[0].ErrorCode, System.Globalization.CultureInfo.InvariantCulture),
                errores: validacion.Errors.Select(e => new ErrorCampo(CamelCase(e.PropertyName), e.ErrorMessage)).ToList());

        if (usuario.UsuarioOperacion is null)
            throw new ApiException(CodigosRespuesta.CampoObligatorio,
                errores: [new ErrorCampo("X-Operation-User", "Las operaciones de escritura requieren el usuario de operación.")]);

        var invalidos = await catalogos.ObtenerTiposExpedienteInvalidosAsync(ct);
        if (invalidos.ContainsKey(solicitud.IdTipoExpediente))
            throw new ApiException(CodigosRespuesta.TipoExpedienteNoEncontrado, "El tipo de expediente tiene una configuración de llaves inválida.");

        var llaves = solicitud.Llaves!;
        await ValidarExpresionesAsync(llaves, ct);

        var contexto = new ContextoOperacion(usuario.UsuarioServicio, usuario.UsuarioOperacion, usuario.IpOrigen, usuario.CorrelationId, endpoint);
        var resultado = await repositorio.ObtenerOCrearAsync(solicitud.IdTipoExpediente, llaves, contexto, ct);

        if (resultado.Codigo is not (CodigosRespuesta.Exito or CodigosRespuesta.Creado) || resultado.IdExpediente is null)
            throw new ApiException(resultado.Codigo,
                errores: resultado.Errores.Select(e => new ErrorCampo(e.CodigoLlave is null ? "llaves" : $"llaves.{e.CodigoLlave}", e.Mensaje)).ToList());

        var expediente = await ObtenerAsync(resultado.IdExpediente.Value, ct);
        return new ResultadoExpediente(expediente, resultado.EsNuevo);
    }

    public async Task<ExpedienteDto> ObtenerAsync(long idExpediente, CancellationToken ct = default)
    {
        var expediente = await repositorio.ObtenerAsync(idExpediente, ct)
                         ?? throw new ApiException(CodigosRespuesta.ExpedienteNoEncontrado);

        var tipos = await catalogos.ObtenerAsync<TipoExpediente>(ct);
        var clientes = await catalogos.ObtenerAsync<TipoCliente>(ct);
        var estados = await catalogos.ObtenerAsync<EstadoExpediente>(ct);
        var llaves = (await catalogos.ObtenerAsync<Llave>(ct)).ToDictionary(l => l.Id);

        return new ExpedienteDto(
            expediente.Id,
            expediente.IdTipoExpediente,
            tipos.First(t => t.Id == expediente.IdTipoExpediente).Codigo,
            clientes.First(c => c.Id == expediente.IdTipoCliente).Codigo,
            estados.First(e => e.Id == expediente.IdEstadoExpediente).Codigo,
            expediente.FechaApertura,
            expediente.FechaCierre,
            expediente.Llaves
                .Where(l => l.Vigente)
                .OrderBy(l => llaves.GetValueOrDefault(l.IdLlave)?.Orden ?? int.MaxValue)
                .Select(l => new LlaveExpedienteDto(llaves.GetValueOrDefault(l.IdLlave)?.Codigo ?? l.IdLlave.ToString(), l.Valor))
                .ToList());
    }

    /// <summary>cat.Llave.ExpresionValidacion la valida la API (SQL Server 2022 no tiene expresiones regulares).</summary>
    private async Task ValidarExpresionesAsync(IReadOnlyList<LlaveValor> llaves, CancellationToken ct)
    {
        var catalogo = (await catalogos.ObtenerAsync<Llave>(ct)).Where(l => l.Activo).ToDictionary(l => l.Codigo, StringComparer.Ordinal);
        var errores = new List<ErrorCampo>();

        foreach (var llave in llaves.Where(l => !string.IsNullOrWhiteSpace(l.Valor)))
        {
            if (!catalogo.TryGetValue(llave.Tipo!, out var definicion) || string.IsNullOrEmpty(definicion.ExpresionValidacion))
                continue;
            try
            {
                if (!Regex.IsMatch(llave.Valor!.Trim(), definicion.ExpresionValidacion, RegexOptions.None, TiempoMaximoRegex))
                    errores.Add(new ErrorCampo($"llaves.{llave.Tipo}", "El valor no tiene el formato esperado."));
            }
            catch (RegexMatchTimeoutException)
            {
                errores.Add(new ErrorCampo($"llaves.{llave.Tipo}", "El valor no tiene el formato esperado."));
            }
        }

        if (errores.Count > 0)
            throw new ApiException(CodigosRespuesta.FormatoCampoInvalido, errores: errores);
    }

    private static string CamelCase(string propiedad) =>
        string.IsNullOrEmpty(propiedad) ? "body"
        : string.Join('.', propiedad.Split('.').Select(p => char.ToLowerInvariant(p[0]) + p[1..]));
}
