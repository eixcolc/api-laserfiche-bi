using APILFBI.Application.Comun;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore.Storage;

namespace APILFBI.Api.Web;

/// <summary>
/// Manejador global: convierte cualquier excepción en una respuesta RFC 7807 con código de negocio.
/// Nunca expone detalles internos (stack trace, SQL) al cliente; esos quedan en el log.
/// </summary>
internal sealed class ManejadorExcepciones(FabricaRespuestas respuestas, ILogger<ManejadorExcepciones> log) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext ctx, Exception ex, CancellationToken ct)
    {
        if (ex is OperationCanceledException && ctx.RequestAborted.IsCancellationRequested)
            return true;   // el cliente cerró la conexión; no hay a quién responder

        switch (ex)
        {
            case ApiException api:
                await EscribirAsync(ctx, respuestas.Error(ctx, api.Codigo, api.Detalle, api.Errores, api.Datos));
                return true;

            case BadHttpRequestException bad:
                log.LogWarning(bad, "Solicitud inválida");
                await respuestas.EscribirErrorAsync(ctx, CodigosRespuesta.SolicitudInvalida);
                return true;

            case RetryLimitExceededException or TimeoutException:
            case SqlException sql when EsErrorDeConexion(sql):
                log.LogError(ex, "Base de datos no disponible");
                await respuestas.EscribirErrorAsync(ctx, CodigosRespuesta.BaseDatosNoDisponible);
                return true;

            default:
                log.LogError(ex, "Error no controlado");
                await respuestas.EscribirErrorAsync(ctx, CodigosRespuesta.ErrorInterno);
                return true;
        }
    }

    /// <summary>Timeout, red, servidor o base inaccesible. El resto de SqlException es un error interno.</summary>
    private static bool EsErrorDeConexion(SqlException ex) =>
        ex.Number is -2 or -1 or 2 or 53 or 64 or 233 or 4060 or 10053 or 10054 or 10060 or 11001 or 40197 or 40501 or 40613;

    private static Task EscribirAsync(HttpContext ctx, Microsoft.AspNetCore.Mvc.IActionResult resultado) =>
        resultado.ExecuteResultAsync(new Microsoft.AspNetCore.Mvc.ActionContext
        {
            HttpContext = ctx,
            RouteData = ctx.GetRouteData(),
            ActionDescriptor = new Microsoft.AspNetCore.Mvc.Abstractions.ActionDescriptor(),
        });
}
