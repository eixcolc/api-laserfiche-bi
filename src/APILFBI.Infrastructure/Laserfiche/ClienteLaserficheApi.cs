using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using APILFBI.Application.Abstracciones;
using APILFBI.Application.Comun;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly.CircuitBreaker;

namespace APILFBI.Infrastructure.Laserfiche;

/// <summary>
/// Implementación con la Laserfiche Repository API (self-hosted, Laserfiche 11).
/// Las rutas y los nombres de campos salen de <see cref="LaserficheOptions"/>.
/// Reintentos, timeout y circuit breaker los aplica el handler de resiliencia registrado en el HttpClient.
/// </summary>
internal sealed class ClienteLaserficheApi(
    HttpClient http,
    ProveedorTokenLaserfiche tokens,
    IOptions<LaserficheOptions> opciones,
    ILogger<ClienteLaserficheApi> log) : ILaserficheRepositoryService
{
    public async Task<ContenidoDocumento> DescargarAsync(int entryId, string? rango, CancellationToken ct = default)
    {
        var respuesta = await EnviarAsync(() =>
        {
            var solicitud = new HttpRequestMessage(HttpMethod.Get, Ruta(opciones.Value.Rutas.Descarga, entryId));
            if (!string.IsNullOrWhiteSpace(rango) && RangeHeaderValue.TryParse(rango, out var r))
                solicitud.Headers.Range = r;
            return solicitud;
        }, HttpCompletionOption.ResponseHeadersRead, ct);

        if (!respuesta.IsSuccessStatusCode)
        {
            var status = respuesta.StatusCode;
            respuesta.Dispose();
            throw Traducir((int)status, entryId);
        }

        var contenido = respuesta.Content;
        return new ContenidoDocumento(
            await contenido.ReadAsStreamAsync(ct),
            contenido.Headers.ContentType?.MediaType,
            contenido.Headers.ContentLength,
            respuesta.StatusCode == HttpStatusCode.PartialContent,
            contenido.Headers.ContentRange?.ToString(),
            respuesta);
    }

    /// <summary>
    /// La Repository API reemplaza todos los campos del documento al hacer PUT. Por eso primero se
    /// leen los campos actuales, se combinan con los nuevos y se envía el conjunto completo.
    /// </summary>
    public async Task ActualizarCamposAsync(int entryId, IReadOnlyDictionary<string, string?> campos, CancellationToken ct = default)
    {
        var ruta = Ruta(opciones.Value.Rutas.Campos, entryId);

        using var actual = await EnviarAsync(() => new HttpRequestMessage(HttpMethod.Get, ruta), HttpCompletionOption.ResponseContentRead, ct);
        if (!actual.IsSuccessStatusCode) throw Traducir((int)actual.StatusCode, entryId);

        var cuerpo = new JsonObject();
        var existentes = await actual.Content.ReadFromJsonAsync<JsonNode>(ct);
        foreach (var campo in existentes?["value"]?.AsArray() ?? [])
        {
            var nombre = campo?["fieldName"]?.GetValue<string>();
            if (nombre is null || campos.ContainsKey(nombre)) continue;
            var valores = new JsonArray();
            foreach (var v in campo!["values"]?.AsArray() ?? [])
                valores.Add(new JsonObject { ["value"] = v?["value"]?.DeepClone(), ["position"] = v?["position"]?.DeepClone() });
            cuerpo[nombre] = new JsonObject { ["values"] = valores };
        }

        foreach (var (nombre, valor) in campos)
            cuerpo[nombre] = new JsonObject
            {
                ["values"] = string.IsNullOrEmpty(valor) ? new JsonArray() : new JsonArray(new JsonObject { ["value"] = valor, ["position"] = 1 }),
            };

        using var respuesta = await EnviarAsync(() => new HttpRequestMessage(HttpMethod.Put, ruta)
        {
            Content = new StringContent(cuerpo.ToJsonString(), System.Text.Encoding.UTF8, "application/json"),
        }, HttpCompletionOption.ResponseContentRead, ct);

        if (!respuesta.IsSuccessStatusCode) throw Traducir((int)respuesta.StatusCode, entryId);
    }

    public async Task<(bool Disponible, string Detalle)> VerificarAsync(CancellationToken ct = default)
    {
        try
        {
            using var respuesta = await EnviarAsync(() => new HttpRequestMessage(HttpMethod.Get, opciones.Value.Rutas.Salud),
                HttpCompletionOption.ResponseHeadersRead, ct);
            return respuesta.IsSuccessStatusCode
                ? (true, "Laserfiche disponible")
                : (false, $"Laserfiche respondió {(int)respuesta.StatusCode}");
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return (false, $"Laserfiche no disponible: {ex.Message}");
        }
    }

    /// <summary>Envía con el token de la cuenta de servicio; ante un 401 renueva el token y reintenta una vez.</summary>
    private async Task<HttpResponseMessage> EnviarAsync(Func<HttpRequestMessage> crear, HttpCompletionOption modo, CancellationToken ct)
    {
        for (var intento = 1; ; intento++)
        {
            HttpResponseMessage respuesta;
            try
            {
                using var solicitud = crear();
                solicitud.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await tokens.ObtenerAsync(ct));
                respuesta = await http.SendAsync(solicitud, modo, ct);
            }
            catch (ErrorLaserficheException ex)
            {
                throw new ApiException(CodigosRespuesta.ErrorLaserfiche, ex.Message);
            }
            catch (BrokenCircuitException)
            {
                throw new ApiException(CodigosRespuesta.LaserficheNoDisponible, "Laserfiche no está disponible (circuito abierto).");
            }
            catch (HttpRequestException ex)
            {
                log.LogError(ex, "No se pudo conectar con Laserfiche");
                throw new ApiException(CodigosRespuesta.LaserficheNoDisponible);
            }
            catch (Exception ex) when (ex is TimeoutException or Polly.Timeout.TimeoutRejectedException
                                       || (ex is TaskCanceledException && !ct.IsCancellationRequested))
            {
                throw new ApiException(CodigosRespuesta.TiempoEsperaLaserfiche);
            }

            if (respuesta.StatusCode != HttpStatusCode.Unauthorized || intento > 1) return respuesta;

            respuesta.Dispose();
            tokens.Invalidar();
        }
    }

    private string Ruta(string plantilla, int entryId) => plantilla
        .Replace("{repositorio}", Uri.EscapeDataString(opciones.Value.RepositorioId))
        .Replace("{entryId}", entryId.ToString(System.Globalization.CultureInfo.InvariantCulture));

    private ApiException Traducir(int status, int entryId)
    {
        log.LogWarning("Laserfiche respondió {Status} para la entrada {EntryId}", status, entryId);
        return status switch
        {
            404 => new ApiException(CodigosRespuesta.DocumentoNoEncontrado, "El documento no existe en Laserfiche."),
            503 => new ApiException(CodigosRespuesta.LaserficheNoDisponible),
            504 => new ApiException(CodigosRespuesta.TiempoEsperaLaserfiche),
            _ => new ApiException(CodigosRespuesta.ErrorLaserfiche, $"Laserfiche respondió {status}."),
        };
    }
}
