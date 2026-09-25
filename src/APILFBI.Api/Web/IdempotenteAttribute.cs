using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using APILFBI.Application.Abstracciones;
using APILFBI.Application.Comun;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace APILFBI.Api.Web;

/// <summary>
/// Soporte del header Idempotency-Key en operaciones de escritura. Si el CRM o el balanceador
/// reintentan la misma solicitud, se devuelve la respuesta original sin volver a ejecutarla.
/// Sin el header, la operación se ejecuta normalmente.
/// </summary>
public sealed class IdempotenteAttribute() : TypeFilterAttribute(typeof(FiltroIdempotencia));

internal sealed partial class FiltroIdempotencia(IAlmacenIdempotencia almacen, IContextoUsuario usuario, FabricaRespuestas respuestas)
    : IAsyncActionFilter
{
    public const string Header = "Idempotency-Key";
    public const string HeaderRepetida = "Idempotent-Replayed";

    [GeneratedRegex("^[A-Za-z0-9._:-]{8,100}$")]
    private static partial Regex Formato();

    public async Task OnActionExecutionAsync(ActionExecutingContext ctx, ActionExecutionDelegate next)
    {
        var llave = ctx.HttpContext.Request.Headers[Header].ToString();
        if (string.IsNullOrEmpty(llave))
        {
            await next();
            return;
        }

        if (!Formato().IsMatch(llave))
        {
            ctx.Result = respuestas.Error(ctx.HttpContext, CodigosRespuesta.FormatoCampoInvalido,
                errores: [new ErrorCampo(Header, "Debe tener entre 8 y 100 caracteres: letras, números, '.', '_', ':' o '-'.")]);
            return;
        }

        var clientId = usuario.UsuarioServicio;
        var endpoint = $"{ctx.HttpContext.Request.Method} {ctx.HttpContext.Request.Path}";
        var reserva = await almacen.ReservarAsync(clientId, llave, endpoint, Hash(ctx), ctx.HttpContext.RequestAborted);

        switch (reserva.Estado)
        {
            case EstadoReserva.ContenidoDistinto:
                ctx.Result = respuestas.Error(ctx.HttpContext, CodigosRespuesta.IdempotencyKeyReutilizada);
                return;
            case EstadoReserva.EnProceso:
                ctx.Result = respuestas.Error(ctx.HttpContext, CodigosRespuesta.SolicitudEnProceso);
                return;
            case EstadoReserva.Completada:
                ctx.HttpContext.Response.Headers[HeaderRepetida] = "true";
                ctx.Result = new ContentResult
                {
                    StatusCode = reserva.HttpStatus,
                    Content = reserva.RespuestaJson,
                    ContentType = reserva.HttpStatus >= 400 ? FabricaRespuestas.ContentTypeProblema : "application/json; charset=utf-8",
                };
                return;
        }

        var ejecutado = await next();

        // Las excepciones (validación, reglas de negocio, errores) no se guardan: un reintento las vuelve a evaluar.
        if (ejecutado.Exception is not null && !ejecutado.ExceptionHandled)
        {
            await almacen.LiberarAsync(clientId, llave, CancellationToken.None);
            return;
        }

        if (ejecutado.Result is ObjectResult { Value: not null } resultado && (resultado.StatusCode ?? 200) < 500)
        {
            var json = JsonSerializer.Serialize(resultado.Value, resultado.Value.GetType(), JsonSerializerOptions.Web);
            await almacen.CompletarAsync(clientId, llave, resultado.StatusCode ?? 200, json, CancellationToken.None);
        }
        else
        {
            await almacen.LiberarAsync(clientId, llave, CancellationToken.None);
        }
    }

    /// <summary>Los archivos se representan por nombre, tamaño y hash de su contenido.</summary>
    private static readonly JsonSerializerOptions OpcionesHash = new(JsonSerializerOptions.Web) { Converters = { new ConvertidorArchivo() } };

    /// <summary>Hash de los argumentos ya enlazados: la misma solicitud produce el mismo hash.</summary>
    private static string Hash(ActionExecutingContext ctx)
    {
        var argumentos = ctx.ActionArguments
            .Where(a => a.Value is not CancellationToken)
            .OrderBy(a => a.Key, StringComparer.Ordinal)
            .ToDictionary(a => a.Key, a => a.Value);
        var json = JsonSerializer.Serialize(argumentos, OpcionesHash);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
    }

    private sealed class ConvertidorArchivo : System.Text.Json.Serialization.JsonConverter<IFormFile>
    {
        public override IFormFile Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            throw new NotSupportedException();

        public override void Write(Utf8JsonWriter writer, IFormFile value, JsonSerializerOptions options)
        {
            using var contenido = value.OpenReadStream();
            writer.WriteStartObject();
            writer.WriteString("nombre", value.FileName);
            writer.WriteNumber("bytes", value.Length);
            writer.WriteString("sha256", Convert.ToHexString(SHA256.HashData(contenido)));
            writer.WriteEndObject();
        }
    }
}
