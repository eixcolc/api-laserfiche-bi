using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace APILFBI.Infrastructure.Laserfiche;

/// <summary>
/// Token de la cuenta de servicio de Laserfiche (grant_type=password). Se guarda en memoria de la
/// instancia y se renueva antes de vencer; cada instancia del balanceador obtiene el suyo.
/// </summary>
internal sealed class ProveedorTokenLaserfiche(
    IHttpClientFactory fabricaHttp,
    IOptions<LaserficheOptions> opciones,
    TimeProvider tiempo,
    ILogger<ProveedorTokenLaserfiche> log)
{
    public const string ClienteHttp = "laserfiche-token";

    private readonly SemaphoreSlim _candado = new(1, 1);
    private string? _token;
    private DateTimeOffset _vence;

    public async Task<string> ObtenerAsync(CancellationToken ct)
    {
        if (_token is not null && tiempo.GetUtcNow() < _vence) return _token;

        await _candado.WaitAsync(ct);
        try
        {
            if (_token is not null && tiempo.GetUtcNow() < _vence) return _token;

            var o = opciones.Value;
            using var cliente = fabricaHttp.CreateClient(ClienteHttp);
            using var respuesta = await cliente.PostAsync(o.Rutas.Token.Replace("{repositorio}", Uri.EscapeDataString(o.RepositorioId)),
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "password",
                    ["username"] = o.Usuario,
                    ["password"] = o.Contrasena,
                }), ct);

            if (!respuesta.IsSuccessStatusCode)
            {
                log.LogError("Laserfiche rechazó la autenticación de la cuenta de servicio: {Status}", (int)respuesta.StatusCode);
                throw new ErrorLaserficheException((int)respuesta.StatusCode, "No se pudo autenticar la cuenta de servicio en Laserfiche.");
            }

            var token = await respuesta.Content.ReadFromJsonAsync<RespuestaToken>(ct)
                        ?? throw new ErrorLaserficheException(502, "Laserfiche devolvió un token vacío.");

            _token = token.AccessToken;
            var segundos = Math.Max(30, (token.ExpiresIn ?? 3600) - o.RenovarTokenAntesSegundos);
            _vence = tiempo.GetUtcNow().AddSeconds(segundos);
            return _token;
        }
        finally
        {
            _candado.Release();
        }
    }

    /// <summary>Se llama cuando Laserfiche responde 401 con el token guardado.</summary>
    public void Invalidar() => _token = null;

    private sealed record RespuestaToken(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("expires_in")] int? ExpiresIn);
}

/// <summary>Error de Laserfiche con el HTTP que respondió (0 = sin respuesta).</summary>
internal sealed class ErrorLaserficheException(int status, string mensaje) : Exception(mensaje)
{
    public int Status { get; } = status;
}
