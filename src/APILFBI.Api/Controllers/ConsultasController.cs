using APILFBI.Api.Web;
using APILFBI.Application.Comun;
using APILFBI.Application.Documentos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace APILFBI.Api.Controllers;

/// <summary>
/// Consultas transaccionales por llaves del cliente (criterios 7 y 8). Usan POST porque llevan
/// datos personales (CIF, DNI, RTN) que no deben quedar en URLs ni en logs del balanceador.
/// Si el expediente aún no existe, se devuelven todos los tipos de documento como "Sin subir".
/// </summary>
[ApiController]
[Route("api/v1/clientes")]
[Authorize(Policy = Scopes.DocumentosLeer)]
public sealed class ConsultasController(DocumentosService documentos, FabricaRespuestas respuestas) : ControllerBase
{
    /// <summary>Criterio 7: cliente natural. Envíe cif, o tipoIdentificacion + noIdentificacion.</summary>
    [HttpPost("natural/documentos/consulta")]
    [ProducesResponseType<RespuestaApi<ConsultaNaturalRespuesta>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Natural([FromBody] ConsultaNatural consulta, CancellationToken ct) =>
        respuestas.Exito(HttpContext, await documentos.ConsultarNaturalAsync(consulta, Request.Path, ct));

    /// <summary>Criterio 8: cliente jurídico. Envíe cif o rtn.</summary>
    [HttpPost("juridico/documentos/consulta")]
    [ProducesResponseType<RespuestaApi<ConsultaJuridicaRespuesta>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Juridico([FromBody] ConsultaJuridica consulta, CancellationToken ct) =>
        respuestas.Exito(HttpContext, await documentos.ConsultarJuridicaAsync(consulta, Request.Path, ct));
}
