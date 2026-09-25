using APILFBI.Api.Web;
using APILFBI.Application.Comun;
using APILFBI.Application.Expedientes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace APILFBI.Api.Controllers;

[ApiController]
[Route("api/v1/expedientes")]
public sealed class ExpedientesController(ExpedientesService expedientes, FabricaRespuestas respuestas) : ControllerBase
{
    /// <summary>
    /// Paso previo a la carga por SFTP: obtiene el expediente del conjunto de llaves o lo crea.
    /// Devuelve 201 si lo creó y 200 si ya existía. El idExpediente es el que va en el XML de carga.
    /// Requiere el header X-Operation-User; acepta Idempotency-Key.
    /// </summary>
    [HttpPost]
    [Authorize(Policy = Scopes.ExpedientesEscribir)]
    [Idempotente]
    [ProducesResponseType<RespuestaApi<ExpedienteRespuesta>>(StatusCodes.Status200OK)]
    [ProducesResponseType<RespuestaApi<ExpedienteRespuesta>>(StatusCodes.Status201Created)]
    public async Task<IActionResult> ObtenerOCrear([FromBody] SolicitudExpediente solicitud, CancellationToken ct)
    {
        var resultado = await expedientes.ObtenerOCrearAsync(solicitud, Request.Path, ct);
        var respuesta = new ExpedienteRespuesta(resultado.Expediente, resultado.EsNuevo);

        if (!resultado.EsNuevo)
            return respuestas.Exito(HttpContext, respuesta);

        Response.Headers.Location = $"/api/v1/expedientes/{resultado.Expediente.IdExpediente}";
        return respuestas.Exito(HttpContext, respuesta, CodigosRespuesta.Creado);
    }

    /// <summary>Expediente con sus llaves vigentes.</summary>
    [HttpGet("{idExpediente:long}")]
    [Authorize(Policy = Scopes.DocumentosLeer)]
    [ProducesResponseType<RespuestaApi<ExpedienteDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Obtener(long idExpediente, CancellationToken ct) =>
        respuestas.Exito(HttpContext, await expedientes.ObtenerAsync(idExpediente, ct));
}

public sealed record ExpedienteRespuesta(ExpedienteDto Expediente, bool EsNuevo);
