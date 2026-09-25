using APILFBI.Api.Web;
using APILFBI.Application.Cargas;
using APILFBI.Application.Comun;
using APILFBI.Infrastructure.Cargas;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;

namespace APILFBI.Api.Controllers;

[ApiController]
[Route("api/v1/expedientes/{idExpediente:long}/documentos")]
public sealed class CargasController(CargasService cargas, FabricaRespuestas respuestas) : ControllerBase
{
    /// <summary>
    /// Carga de un documento por la API (alternativa al SFTP, criterios 11 y 12). El archivo y su metadata
    /// van en un solo multipart/form-data. La API valida de inmediato (formato y contenido real, tamaño,
    /// que el tipo aplique al expediente, reemplazo), arma el XML del contrato y deja el par en la carpeta
    /// de Import Agent. Responde 202 (código 5): el resultado final se consulta en GET /cargas/{correlativo}.
    /// Requiere X-Operation-User (es el usuarioCarga); acepta Idempotency-Key.
    /// </summary>
    [HttpPost]
    [Consumes("multipart/form-data")]
    [Authorize(Policy = Scopes.ExpedientesEscribir)]
    [LimiteTamanoCarga]
    [Idempotente]
    [ProducesResponseType<RespuestaApi<RespuestaCarga>>(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Cargar(long idExpediente, [FromForm] FormularioCarga formulario, CancellationToken ct)
    {
        await using var contenido = formulario.Archivo?.OpenReadStream();
        var archivo = formulario.Archivo is null || contenido is null
            ? null
            : new ArchivoCarga(contenido, formulario.Archivo.FileName, formulario.Archivo.Length);

        var solicitud = new SolicitudCargaDocumento(
            formulario.IdTipoDocumento ?? 0, formulario.FechaEmision, formulario.FechaVencimiento, formulario.Comentario,
            formulario.IdDocumentoReemplaza, formulario.NombreUsuarioCarga, formulario.Correlativo, formulario.HashSha256);

        var respuesta = await cargas.RecibirAsync(idExpediente, solicitud, archivo, Request.Path, ct);
        Response.Headers.Location = $"/api/v1/cargas/{respuesta.Correlativo}";
        return respuestas.Exito(HttpContext, respuesta, CodigosRespuesta.CargaRecibida);
    }
}

/// <summary>Campos del formulario: el archivo y la metadata que antes iba en el XML.</summary>
public sealed class FormularioCarga
{
    /// <summary>El documento.</summary>
    [FromForm(Name = "archivo")] public IFormFile? Archivo { get; set; }

    /// <summary>idTipoDocumento del catálogo (GET /tipos-documento).</summary>
    [FromForm(Name = "idTipoDocumento")] public int? IdTipoDocumento { get; set; }

    /// <summary>AAAA-MM-DD.</summary>
    [FromForm(Name = "fechaEmision")] public DateOnly? FechaEmision { get; set; }

    /// <summary>AAAA-MM-DD. Si no viene, se calcula con los días de vigencia del tipo de documento.</summary>
    [FromForm(Name = "fechaVencimiento")] public DateOnly? FechaVencimiento { get; set; }

    [FromForm(Name = "comentario")] public string? Comentario { get; set; }

    /// <summary>idDocumento (ID de Laserfiche) de la versión vigente que se reemplaza.</summary>
    [FromForm(Name = "idDocumentoReemplaza")] public int? IdDocumentoReemplaza { get; set; }

    /// <summary>Nombre del empleado que adjunta; se devuelve como nombreEmpleadoAdjunto.</summary>
    [FromForm(Name = "nombreUsuarioCarga")] public string? NombreUsuarioCarga { get; set; }

    /// <summary>Opcional: si no viene, lo genera la API.</summary>
    [FromForm(Name = "correlativo")] public string? Correlativo { get; set; }

    /// <summary>Opcional: SHA-256 del archivo; si viene y no coincide se rechaza (código 411).</summary>
    [FromForm(Name = "hashSha256")] public string? HashSha256 { get; set; }
}

/// <summary>
/// La API limita el body a 1 MB; este endpoint admite hasta CargaApi:TamanoMaximoSolicitudMB. Es un filtro
/// de recurso porque debe aplicarse antes de que se lea el formulario.
/// </summary>
public sealed class LimiteTamanoCargaAttribute() : TypeFilterAttribute(typeof(FiltroLimiteTamanoCarga));

internal sealed class FiltroLimiteTamanoCarga(IOptions<CargaApiOptions> opciones) : IResourceFilter
{
    public void OnResourceExecuting(ResourceExecutingContext ctx)
    {
        var limite = ctx.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (limite is { IsReadOnly: false })
            limite.MaxRequestBodySize = opciones.Value.TamanoMaximoSolicitudMB * 1024L * 1024L;
    }

    public void OnResourceExecuted(ResourceExecutedContext ctx) { }
}
