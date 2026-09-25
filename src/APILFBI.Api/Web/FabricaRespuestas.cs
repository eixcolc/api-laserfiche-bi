using APILFBI.Application.Abstracciones;
using APILFBI.Application.Comun;
using Microsoft.AspNetCore.Mvc;

namespace APILFBI.Api.Web;

/// <summary>Sobre estándar de las respuestas exitosas.</summary>
public sealed record RespuestaApi<T>(int Codigo, string Mensaje, string? CorrelationId, T? Data);

/// <summary>
/// Construye todas las respuestas. El HTTP y el mensaje salen de cat.CodigoRespuesta: el código
/// nunca decide el HTTP por su cuenta.
/// </summary>
public sealed class FabricaRespuestas(ICatalogoCache catalogos)
{
    public const string ContentTypeProblema = "application/problem+json";

    public (int Http, string Mensaje) Resolver(int codigo)
    {
        var c = catalogos.ObtenerCodigoRespuesta(codigo);
        if (c is not null) return (c.HttpStatus, c.Mensaje);
        return CodigosRespuesta.PorDefecto.TryGetValue(codigo, out var d)
            ? d
            : CodigosRespuesta.PorDefecto[CodigosRespuesta.ErrorInterno];
    }

    public IActionResult Exito<T>(HttpContext ctx, T data, int codigo = CodigosRespuesta.Exito)
    {
        var (http, mensaje) = Resolver(codigo);
        return new ObjectResult(new RespuestaApi<T>(codigo, mensaje, ctx.TraceIdentifier, data)) { StatusCode = http };
    }

    public ProblemDetails Problema(HttpContext ctx, int codigo, string? detalle = null, IReadOnlyList<ErrorCampo>? errores = null, object? datos = null)
    {
        var (http, mensaje) = Resolver(codigo);
        var problema = new ProblemDetails
        {
            Type = $"https://www.rfc-editor.org/rfc/rfc9110#status.{http}",
            Title = mensaje,
            Status = http,
            Detail = detalle,
            Instance = ctx.Request.Path,
        };
        problema.Extensions["codigo"] = codigo;
        problema.Extensions["mensaje"] = mensaje;
        problema.Extensions["correlationId"] = ctx.TraceIdentifier;
        if (errores is { Count: > 0 })
            problema.Extensions["errores"] = errores.Select(e => new { campo = e.Campo, mensaje = e.Mensaje }).ToList();
        if (datos is not null)
            problema.Extensions["data"] = datos;
        return problema;
    }

    public IActionResult Error(HttpContext ctx, int codigo, string? detalle = null, IReadOnlyList<ErrorCampo>? errores = null, object? datos = null)
    {
        var problema = Problema(ctx, codigo, detalle, errores, datos);
        return new ObjectResult(problema) { StatusCode = problema.Status, ContentTypes = { ContentTypeProblema } };
    }

    /// <summary>Para middleware y eventos de autenticación, donde no hay IActionResult.</summary>
    public async Task EscribirErrorAsync(HttpContext ctx, int codigo, string? detalle = null)
    {
        if (ctx.Response.HasStarted) return;
        var problema = Problema(ctx, codigo, detalle);
        ctx.Response.StatusCode = problema.Status!.Value;
        await ctx.Response.WriteAsJsonAsync(problema, (System.Text.Json.JsonSerializerOptions?)null, ContentTypeProblema);
    }
}
