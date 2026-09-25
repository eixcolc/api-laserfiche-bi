using APILFBI.Api.Web;
using APILFBI.Application.Comun;
using APILFBI.Application.Documentos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace APILFBI.Api.Controllers;

[ApiController]
[Route("api/v1")]
[Authorize(Policy = Scopes.DocumentosLeer)]
public sealed class DocumentosController(DocumentosService documentos, FabricaRespuestas respuestas) : ControllerBase
{
    /// <summary>Documentos del expediente, igual que las consultas 7 y 8 pero directo por idExpediente.</summary>
    [HttpGet("expedientes/{idExpediente:long}/documentos")]
    [ProducesResponseType<RespuestaApi<DocumentosExpedienteRespuesta>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> DeExpediente(long idExpediente, CancellationToken ct) =>
        respuestas.Exito(HttpContext, await documentos.DocumentosDeExpedienteAsync(idExpediente, Request.Path, ct));

    /// <summary>Estado de una carga hecha por SFTP: Importado (con idDocumento) o Rechazado (con motivo).</summary>
    [HttpGet("cargas/{correlativo}")]
    [ProducesResponseType<RespuestaApi<CargaDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Carga(string correlativo, CancellationToken ct) =>
        respuestas.Exito(HttpContext, await documentos.ObtenerCargaAsync(correlativo, ct));

    /// <summary>
    /// Criterio 9: el documento en stream, para que el CRM lo muestre (Content-Disposition inline).
    /// idDocumento es el ID único de Laserfiche. Admite el header Range.
    /// </summary>
    [HttpGet("documentos/{idDocumento:int}/contenido")]
    [ProducesResponseType(typeof(FileStreamResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status206PartialContent)]
    public async Task<IActionResult> Contenido(int idDocumento, CancellationToken ct)
    {
        var rango = Request.Headers.Range.ToString();
        var (contenido, nombreArchivo, contentType) = await documentos.DescargarAsync(
            idDocumento, string.IsNullOrWhiteSpace(rango) ? null : rango, Request.Path, ct);

        if (contenido.Recurso is not null) Response.RegisterForDispose(contenido.Recurso);

        var disposicion = new ContentDispositionHeaderValue("inline");
        disposicion.SetHttpFileName(nombreArchivo);
        Response.Headers.ContentDisposition = disposicion.ToString();
        Response.Headers.CacheControl = "private, no-store";

        // Laserfiche resolvió el rango: se reenvía tal cual.
        if (contenido.EsParcial)
        {
            await using var stream = contenido.Contenido;
            Response.StatusCode = StatusCodes.Status206PartialContent;
            Response.ContentType = contentType;
            if (contenido.ContentRange is not null) Response.Headers.ContentRange = contenido.ContentRange;
            if (contenido.Longitud is long largo) Response.ContentLength = largo;
            await stream.CopyToAsync(Response.Body, ct);
            return new EmptyResult();
        }

        return new FileStreamResult(contenido.Contenido, contentType) { EnableRangeProcessing = contenido.Contenido.CanSeek };
    }
}
