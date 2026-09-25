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
        if (!actual.IsSuccessStatusCode) throw Traducir((int)actual.StatusCode, entryId, await DetalleAsync(actual, ct));

        var cuerpo = new JsonObject();
        var posiciones = new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase);
        var existentes = await actual.Content.ReadFromJsonAsync<JsonNode>(ct);
        foreach (var campo in existentes?["value"]?.AsArray() ?? [])
        {
            var nombre = campo?["fieldName"]?.GetValue<string>();
            if (nombre is null) continue;
            var originales = campo!["values"]?.AsArray() ?? [];
            if (campos.ContainsKey(nombre))
            {
                posiciones[nombre] = originales.FirstOrDefault()?["position"]?.DeepClone();
                continue;
            }
            var valores = new JsonArray();
            foreach (var v in originales)
                valores.Add(new JsonObject { ["value"] = v?["value"]?.DeepClone(), ["position"] = v?["position"]?.DeepClone() });
            if (valores.Count == 0)
                valores.Add(new JsonObject { ["value"] = null, ["position"] = 0 });
            cuerpo[nombre] = new JsonObject { ["values"] = valores };
        }

        // Mismo formato que devuelve Laserfiche 11: posición del campo (base 0) y, para vaciarlo,
        // un valor null. Laserfiche responde 400 si recibe "values": [].
        foreach (var (nombre, valor) in campos)
            cuerpo[nombre] = new JsonObject
            {
                ["values"] = new JsonArray(new JsonObject
                {
                    ["value"] = string.IsNullOrEmpty(valor) ? null : valor,
                    ["position"] = posiciones.GetValueOrDefault(nombre) ?? JsonValue.Create(0),
                }),
            };

        using var respuesta = await EnviarAsync(() => new HttpRequestMessage(HttpMethod.Put, ruta)
        {
            Content = new StringContent(cuerpo.ToJsonString(), System.Text.Encoding.UTF8, "application/json"),
        }, HttpCompletionOption.ResponseContentRead, ct);

        if (!respuesta.IsSuccessStatusCode) throw Traducir((int)respuesta.StatusCode, entryId, await DetalleAsync(respuesta, ct));
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

    /// <summary>Mensaje de error que devuelve Laserfiche (title/detail del problem+json, o el texto), recortado.</summary>
    private static async Task<string?> DetalleAsync(HttpResponseMessage respuesta, CancellationToken ct)
    {
        try
        {
            var texto = await respuesta.Content.ReadAsStringAsync(ct);
            if (string.IsNullOrWhiteSpace(texto)) return null;
            try
            {
                var json = JsonNode.Parse(texto);
                var partes = new[] { json?["title"], json?["detail"], json?["errorMessage"], json?["message"] }
                    .Select(n => n is JsonValue v && v.TryGetValue<string>(out var s) ? s : null)
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Distinct()
                    .ToList();
                if (partes.Count > 0) texto = string.Join(" | ", partes);
            }
            catch (JsonException) { }
            return texto.Length > 400 ? texto[..400] + "…" : texto;
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            return null;
        }
    }

    private ApiException Traducir(int status, int entryId, string? detalle = null)
    {
        log.LogWarning("Laserfiche respondió {Status} para la entrada {EntryId}: {Detalle}", status, entryId, detalle);
        var sufijo = string.IsNullOrWhiteSpace(detalle) ? "" : $" {detalle}";
        return status switch
        {
            404 => new ApiException(CodigosRespuesta.DocumentoNoEncontrado, "El documento no existe en Laserfiche."),
            503 => new ApiException(CodigosRespuesta.LaserficheNoDisponible),
            504 => new ApiException(CodigosRespuesta.TiempoEsperaLaserfiche),
            _ => new ApiException(CodigosRespuesta.ErrorLaserfiche, $"Laserfiche respondió {status}.{sufijo}"),
        };
    }
}
