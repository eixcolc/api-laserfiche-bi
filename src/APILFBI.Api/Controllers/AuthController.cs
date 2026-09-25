using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Serialization;
using APILFBI.Api.Web;
using APILFBI.Application.Abstracciones;
using APILFBI.Application.Comun;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace APILFBI.Api.Controllers;

/// <summary>
/// Criterio 2: el CRM envía usuario (client_id) y contraseña (client_secret) y recibe un token de 1 hora.
/// Sigue RFC 6749: respuesta con access_token/token_type/expires_in y errores invalid_client (401),
/// invalid_scope o unsupported_grant_type (400). Además incluye codigo y mensaje para que CRM los mapee.
/// </summary>
[ApiController]
[Route("api/v1/auth")]
[AllowAnonymous]
public sealed class AuthController(IAutenticacionService autenticacion, IBitacoraService bitacora, FabricaRespuestas respuestas) : ControllerBase
{
    /// <summary>
    /// Criterio 2: token OAuth 2.0 (client_credentials) de 1 hora. Credenciales en el body
    /// (client_id, client_secret) o en el header Authorization: Basic.
    /// </summary>
    [HttpPost("token")]
    [Consumes("application/x-www-form-urlencoded")]
    [EnableRateLimiting(ServiciosWeb.PoliticaToken)]
    [ProducesResponseType<RespuestaToken>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorToken>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ErrorToken>(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Token(
        [FromForm(Name = "grant_type")] string? grantType,
        [FromForm(Name = "client_id")] string? clientId,
        [FromForm(Name = "client_secret")] string? clientSecret,
        [FromForm(Name = "scope")] string? scope,
        CancellationToken ct)
    {
        // Las credenciales pueden venir en el body o en el header Authorization: Basic (RFC 6749, 2.3.1).
        var basic = LeerBasic();
        if (basic is not null) (clientId, clientSecret) = basic.Value;

        var resultado = await autenticacion.EmitirTokenAsync(new SolicitudToken(grantType, clientId, clientSecret, scope), ct);

        bitacora.Encolar(new EntradaBitacora
        {
            TipoOperacion = TiposOperacion.Autenticacion,
            CodigoRespuesta = resultado.Codigo,
            UsuarioServicio = string.IsNullOrWhiteSpace(clientId) ? "(sin client_id)" : clientId,
            IpOrigen = HttpContext.Connection.RemoteIpAddress?.ToString(),
            CorrelationId = HttpContext.TraceIdentifier,
            Endpoint = Request.Path,
            MetodoHttp = Request.Method,
            Detalle = resultado.Exitoso ? $"Scopes: {resultado.Token!.Scope}" : $"{resultado.ErrorOAuth}. {resultado.Detalle}".Trim(),
        });

        Response.Headers.CacheControl = "no-store";
        Response.Headers.Pragma = "no-cache";

        var (_, mensaje) = respuestas.Resolver(resultado.Codigo);
        if (resultado.Exitoso)
        {
            var t = resultado.Token!;
            return Ok(new RespuestaToken(t.AccessToken, "Bearer", t.ExpiresIn, t.Scope, resultado.Codigo, mensaje));
        }

        var http = resultado.ErrorOAuth == "invalid_client" ? StatusCodes.Status401Unauthorized : StatusCodes.Status400BadRequest;
        if (http == StatusCodes.Status401Unauthorized)
            Response.Headers.WWWAuthenticate = "Basic realm=\"APILFBI\"";

        return StatusCode(http, new ErrorToken(resultado.ErrorOAuth!, resultado.Detalle ?? mensaje, resultado.Codigo, mensaje, HttpContext.TraceIdentifier));
    }

    private (string ClientId, string Secret)? LeerBasic()
    {
        if (!AuthenticationHeaderValue.TryParse(Request.Headers.Authorization, out var h)
            || !"Basic".Equals(h.Scheme, StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(h.Parameter))
            return null;
        try
        {
            var texto = Encoding.UTF8.GetString(Convert.FromBase64String(h.Parameter));
            var i = texto.IndexOf(':');
            return i <= 0 ? null : (Uri.UnescapeDataString(texto[..i]), Uri.UnescapeDataString(texto[(i + 1)..]));
        }
        catch (FormatException)
        {
            return null;
        }
    }
}

public sealed record RespuestaToken(
    [property: JsonPropertyName("access_token")] string AccessToken,
    [property: JsonPropertyName("token_type")] string TokenType,
    [property: JsonPropertyName("expires_in")] int ExpiresIn,
    [property: JsonPropertyName("scope")] string Scope,
    [property: JsonPropertyName("codigo")] int Codigo,
    [property: JsonPropertyName("mensaje")] string Mensaje);

public sealed record ErrorToken(
    [property: JsonPropertyName("error")] string Error,
    [property: JsonPropertyName("error_description")] string ErrorDescription,
    [property: JsonPropertyName("codigo")] int Codigo,
    [property: JsonPropertyName("mensaje")] string Mensaje,
    [property: JsonPropertyName("correlationId")] string CorrelationId);
