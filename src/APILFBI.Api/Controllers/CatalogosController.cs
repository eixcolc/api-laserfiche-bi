using APILFBI.Api.Web;
using APILFBI.Application.Catalogos;
using APILFBI.Application.Comun;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace APILFBI.Api.Controllers;

[ApiController]
[Route("api/v1")]
[Authorize(Policy = Scopes.CatalogosLeer)]
public sealed class CatalogosController(CatalogosService catalogos, FabricaRespuestas respuestas) : ControllerBase
{
    /// <summary>Criterio 4: todos los tipos de documento, con su estado y formatos permitidos.</summary>
    [HttpGet("tipos-documento")]
    [ProducesResponseType<RespuestaApi<IReadOnlyList<TipoDocumentoDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> TiposDocumento(CancellationToken ct) =>
        respuestas.Exito(HttpContext, await catalogos.TiposDocumentoAsync(ct));

    /// <summary>
    /// Listas para los campos del CRM (criterio 10): estados-documento, tipos-rechazo, etapas-rechazo,
    /// tipos-identificacion, segmentaciones, tipos-archivo, tipos-cliente.
    /// </summary>
    [HttpGet("catalogos/{nombre}")]
    [ProducesResponseType<RespuestaApi<IReadOnlyList<ItemCatalogoDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Lista(string nombre, CancellationToken ct) =>
        respuestas.Exito(HttpContext, await catalogos.ListaAsync(nombre, ct));
}
