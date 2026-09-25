using APILFBI.Api.Web;
using APILFBI.Application.Catalogos;
using APILFBI.Application.Comun;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace APILFBI.Api.Controllers;

/// <summary>Tipos de expediente ("categorías de proceso" en el requerimiento).</summary>
[ApiController]
[Route("api/v1/tipos-expediente")]
[Authorize(Policy = Scopes.CatalogosLeer)]
public sealed class TiposExpedienteController(CatalogosService catalogos, FabricaRespuestas respuestas) : ControllerBase
{
    /// <summary>Criterio 5: lista de tipos de expediente.</summary>
    [HttpGet]
    [ProducesResponseType<RespuestaApi<IReadOnlyList<TipoExpedienteDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Listar(CancellationToken ct) =>
        respuestas.Exito(HttpContext, await catalogos.TiposExpedienteAsync(ct));

    /// <summary>Criterio 3: tipos de documento de un tipo de expediente. tipoCliente (N/J) es opcional.</summary>
    [HttpGet("{idTipoExpediente:int}/tipos-documento")]
    [ProducesResponseType<RespuestaApi<TiposDocumentoDeExpedienteDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> TiposDocumento(int idTipoExpediente, [FromQuery] string? tipoCliente, CancellationToken ct) =>
        respuestas.Exito(HttpContext, await catalogos.TiposDocumentoDeExpedienteAsync(idTipoExpediente, null, tipoCliente, ct));

    /// <summary>Criterio 3 por nombre (o código) del tipo de expediente.</summary>
    [HttpGet("tipos-documento")]
    [ProducesResponseType<RespuestaApi<TiposDocumentoDeExpedienteDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> TiposDocumentoPorNombre([FromQuery] string? nombreTipoExpediente, [FromQuery] string? tipoCliente, CancellationToken ct) =>
        respuestas.Exito(HttpContext, await catalogos.TiposDocumentoDeExpedienteAsync(null, nombreTipoExpediente, tipoCliente, ct));

    /// <summary>Llaves que el CRM debe enviar en POST /expedientes para este tipo de expediente.</summary>
    [HttpGet("{idTipoExpediente:int}/llaves")]
    [ProducesResponseType<RespuestaApi<IReadOnlyList<LlavesPorTipoClienteDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Llaves(int idTipoExpediente, [FromQuery] string? tipoCliente, CancellationToken ct) =>
        respuestas.Exito(HttpContext, await catalogos.LlavesAsync(idTipoExpediente, tipoCliente, ct));
}
