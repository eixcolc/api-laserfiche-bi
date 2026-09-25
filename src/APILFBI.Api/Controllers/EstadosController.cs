using APILFBI.Api.Web;
using APILFBI.Application.Comun;
using APILFBI.Application.Estados;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace APILFBI.Api.Controllers;

[ApiController]
[Route("api/v1/documentos/estado")]
public sealed class EstadosController(EstadosService estados, FabricaRespuestas respuestas) : ControllerBase
{
    /// <summary>
    /// Criterio 10: cambia el estado de un lote de documentos del expediente.
    /// Por defecto es todo o nada; con permitirParcial = true aplica los válidos (código 3).
    /// Cada documento trae su propio código. El estado, el tipo de rechazo, la etapa y el ejecutivo
    /// asignado se reflejan en los campos de Laserfiche en segundos (el ejecutivo dispara el workflow).
    /// Requiere X-Operation-User; acepta Idempotency-Key.
    /// </summary>
    [HttpPost]
    [Authorize(Policy = Scopes.DocumentosEstado)]
    [Idempotente]
    [ProducesResponseType<RespuestaApi<RespuestaCambioEstado>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Cambiar([FromBody] SolicitudCambioEstado solicitud, CancellationToken ct)
    {
        var (codigo, respuesta) = await estados.CambiarAsync(solicitud, Request.Path, ct);
        return respuestas.Exito(HttpContext, respuesta, codigo);
    }
}
