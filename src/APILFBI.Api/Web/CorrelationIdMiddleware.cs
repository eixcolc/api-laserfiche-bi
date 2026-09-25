using System.Text.RegularExpressions;
using Serilog.Context;

namespace APILFBI.Api.Web;

/// <summary>
/// Toma el header X-Correlation-Id (si es válido) o genera uno. Queda en HttpContext.TraceIdentifier,
/// en el header de respuesta, en los logs y en la bitácora, para poder cruzarlos.
/// </summary>
public sealed partial class CorrelationIdMiddleware(RequestDelegate next)
{
    public const string Header = "X-Correlation-Id";

    [GeneratedRegex("^[A-Za-z0-9._-]{1,64}$")]
    private static partial Regex Formato();

    public static bool EsValido(string? valor) => valor is not null && Formato().IsMatch(valor);

    public async Task InvokeAsync(HttpContext ctx)
    {
        var entrante = ctx.Request.Headers[Header].ToString();
        var id = EsValido(entrante) ? entrante : Guid.NewGuid().ToString("N");

        ctx.TraceIdentifier = id;
        ctx.Response.OnStarting(() =>
        {
            ctx.Response.Headers[Header] = id;
            return Task.CompletedTask;
        });

        using (LogContext.PushProperty("CorrelationId", id))
            await next(ctx);
    }
}
