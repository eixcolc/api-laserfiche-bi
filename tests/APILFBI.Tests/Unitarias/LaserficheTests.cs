using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using APILFBI.Application.Comun;
using APILFBI.Infrastructure.Laserfiche;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace APILFBI.Tests.Unitarias;

/// <summary>
/// Pruebas del cliente real de la Repository API contra un servidor falso. Verifican el contrato
/// que asume la integración: token, rutas, reintento con 401, Range y combinación de campos.
/// </summary>
public sealed class LaserficheTests
{
    private sealed class ServidorFalso : HttpMessageHandler
    {
        public List<(HttpMethod Metodo, string Ruta, string? Autorizacion, string? Rango, string? Cuerpo)> Solicitudes { get; } = [];
        public Func<HttpRequestMessage, HttpResponseMessage> Responder { get; set; } = _ => new HttpResponseMessage(HttpStatusCode.OK);
        public int TokensEmitidos { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            var cuerpo = r.Content is null ? null : await r.Content.ReadAsStringAsync(ct);
            Solicitudes.Add((r.Method, r.RequestUri!.AbsolutePath, r.Headers.Authorization?.ToString(), r.Headers.Range?.ToString(), cuerpo));

            if (r.RequestUri.AbsolutePath.EndsWith("/Token"))
            {
                TokensEmitidos++;
                return Json($$"""{"access_token":"tok-{{TokensEmitidos}}","expires_in":3600}""");
            }
            return Responder(r);
        }
    }

    private sealed class FabricaHttp(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false) { BaseAddress = new Uri("https://lf/api/") };
    }

    private static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static (ClienteLaserficheApi Cliente, ServidorFalso Servidor) Crear()
    {
        var servidor = new ServidorFalso();
        var opciones = Options.Create(new LaserficheOptions { Simulado = false, UrlBase = "https://lf/api/", RepositorioId = "REPO1", Usuario = "svc", Contrasena = "x" });
        var tokens = new ProveedorTokenLaserfiche(new FabricaHttp(servidor), opciones, TimeProvider.System, NullLogger<ProveedorTokenLaserfiche>.Instance);
        var http = new HttpClient(servidor, disposeHandler: false) { BaseAddress = new Uri("https://lf/api/") };
        return (new ClienteLaserficheApi(http, tokens, opciones, NullLogger<ClienteLaserficheApi>.Instance), servidor);
    }

    [Fact]
    public async Task Descarga_usa_la_ruta_configurada_y_reutiliza_el_token()
    {
        var (cliente, servidor) = Crear();
        servidor.Responder = _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent("%PDF"u8.ToArray()) };

        await using (var c1 = (await cliente.DescargarAsync(77, null)).Contenido) { }
        await using (var c2 = (await cliente.DescargarAsync(78, null)).Contenido) { }

        Assert.Equal(1, servidor.TokensEmitidos);
        var descarga = servidor.Solicitudes.First(s => s.Ruta.Contains("/Entries/"));
        Assert.Equal("/api/v1/Repositories/REPO1/Entries/77/Laserfiche.Repository.Document/edoc", descarga.Ruta);
        Assert.Equal("Bearer tok-1", descarga.Autorizacion);
    }

    [Fact]
    public async Task Ante_un_401_renueva_el_token_y_reintenta_una_vez()
    {
        var (cliente, servidor) = Crear();
        var llamadas = 0;
        servidor.Responder = _ => ++llamadas == 1
            ? new HttpResponseMessage(HttpStatusCode.Unauthorized)
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3]) };

        var contenido = await cliente.DescargarAsync(5, null);

        Assert.Equal(2, servidor.TokensEmitidos);
        Assert.Equal("Bearer tok-2", servidor.Solicitudes.Last().Autorizacion);
        contenido.Recurso?.Dispose();
    }

    [Fact]
    public async Task Documento_inexistente_en_laserfiche_se_traduce_a_codigo_301()
    {
        var (cliente, servidor) = Crear();
        servidor.Responder = _ => new HttpResponseMessage(HttpStatusCode.NotFound);

        var ex = await Assert.ThrowsAsync<ApiException>(() => cliente.DescargarAsync(5, null));

        Assert.Equal(CodigosRespuesta.DocumentoNoEncontrado, ex.Codigo);
    }

    [Fact]
    public async Task El_header_range_se_reenvia_y_la_respuesta_206_queda_marcada_como_parcial()
    {
        var (cliente, servidor) = Crear();
        servidor.Responder = _ =>
        {
            var r = new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = new ByteArrayContent("%PDF"u8.ToArray()) };
            r.Content.Headers.ContentRange = new System.Net.Http.Headers.ContentRangeHeaderValue(0, 3, 1000);
            return r;
        };

        var contenido = await cliente.DescargarAsync(9, "bytes=0-3");

        Assert.Equal("bytes=0-3", servidor.Solicitudes.Last().Rango);
        Assert.True(contenido.EsParcial);
        Assert.Equal("bytes 0-3/1000", contenido.ContentRange);
        contenido.Recurso?.Dispose();
    }

    [Fact]
    public async Task Actualizar_campos_conserva_los_campos_existentes_que_no_cambian()
    {
        var (cliente, servidor) = Crear();
        servidor.Responder = r => r.Method == HttpMethod.Get
            ? Json("""{"value":[{"fieldName":"Estado","values":[{"value":"PendienteRevision","position":0}]},{"fieldName":"IdExpediente","values":[{"value":"1024","position":0}]},{"fieldName":"TipoRechazo","values":[{"value":null,"position":0}]}]}""")
            : new HttpResponseMessage(HttpStatusCode.OK);

        await cliente.ActualizarCamposAsync(11, new Dictionary<string, string?> { ["Estado"] = "Aprobado", ["TipoRechazo"] = null });

        var put = servidor.Solicitudes.Single(s => s.Metodo == HttpMethod.Put);
        Assert.Equal("/api/v1/Repositories/REPO1/Entries/11/fields", put.Ruta);
        var cuerpo = JsonNode.Parse(put.Cuerpo!)!;
        Assert.Equal("Aprobado", cuerpo["Estado"]!["values"]![0]!["value"]!.GetValue<string>());
        Assert.Equal(0, cuerpo["Estado"]!["values"]![0]!["position"]!.GetValue<int>());
        Assert.Equal("1024", cuerpo["IdExpediente"]!["values"]![0]!["value"]!.GetValue<string>());
        var vacio = Assert.Single(cuerpo["TipoRechazo"]!["values"]!.AsArray())!;
        Assert.Null(vacio["value"]);
        Assert.Equal(0, vacio["position"]!.GetValue<int>());
    }

    [Fact]
    public async Task Actualizar_campos_usa_la_posicion_que_devuelve_laserfiche_o_cero()
    {
        var (cliente, servidor) = Crear();
        servidor.Responder = r => r.Method == HttpMethod.Get
            ? Json("""{"value":[{"fieldName":"Estado","values":[{"value":"PendienteRevision","position":1}]}]}""")
            : new HttpResponseMessage(HttpStatusCode.OK);

        await cliente.ActualizarCamposAsync(11, new Dictionary<string, string?> { ["Estado"] = "Aprobado", ["ComentarioRevision"] = "ok" });

        var cuerpo = JsonNode.Parse(servidor.Solicitudes.Single(s => s.Metodo == HttpMethod.Put).Cuerpo!)!;
        Assert.Equal(1, cuerpo["Estado"]!["values"]![0]!["position"]!.GetValue<int>());
        Assert.Equal(0, cuerpo["ComentarioRevision"]!["values"]![0]!["position"]!.GetValue<int>());
    }

    [Fact]
    public async Task Error_de_laserfiche_incluye_su_mensaje()
    {
        var (cliente, servidor) = Crear();
        servidor.Responder = r => r.Method == HttpMethod.Get
            ? Json("""{"value":[]}""")
            : new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent("""{"title":"Invalid field value position."}""", System.Text.Encoding.UTF8, "application/json"),
            };

        var ex = await Assert.ThrowsAsync<ApiException>(() => cliente.ActualizarCamposAsync(11, new Dictionary<string, string?> { ["Estado"] = "Aprobado" }));

        Assert.Equal(CodigosRespuesta.ErrorLaserfiche, ex.Codigo);
        Assert.Contains("Invalid field value position.", ex.Message);
    }

    [Fact]
    public async Task Credenciales_rechazadas_por_laserfiche_se_traducen_a_codigo_901()
    {
        var servidor = new ServidorFalso();
        var opciones = Options.Create(new LaserficheOptions { Simulado = false, RepositorioId = "R", Usuario = "u", Contrasena = "mala" });
        var fabrica = new FabricaHttp(new RechazaToken());
        var tokens = new ProveedorTokenLaserfiche(fabrica, opciones, TimeProvider.System, NullLogger<ProveedorTokenLaserfiche>.Instance);
        var cliente = new ClienteLaserficheApi(new HttpClient(servidor) { BaseAddress = new Uri("https://lf/api/") }, tokens, opciones, NullLogger<ClienteLaserficheApi>.Instance);

        var ex = await Assert.ThrowsAsync<ApiException>(() => cliente.DescargarAsync(1, null));

        Assert.Equal(CodigosRespuesta.ErrorLaserfiche, ex.Codigo);
    }

    [Fact]
    public async Task Sin_conexion_con_laserfiche_se_traduce_a_codigo_902()
    {
        var (cliente, servidor) = Crear();
        servidor.Responder = _ => throw new HttpRequestException("sin conexión");

        var ex = await Assert.ThrowsAsync<ApiException>(() => cliente.DescargarAsync(1, null));

        Assert.Equal(CodigosRespuesta.LaserficheNoDisponible, ex.Codigo);
    }

    [Fact]
    public void El_pdf_simulado_es_un_pdf_valido_con_xref_correcto()
    {
        var pdf = LaserficheSimulado.GenerarPdf("Prueba (1)");
        var texto = Encoding.ASCII.GetString(pdf);

        Assert.StartsWith("%PDF-1.4", texto);
        Assert.EndsWith("%%EOF\n", texto);
        var inicioXref = int.Parse(texto[(texto.LastIndexOf("startxref\n", StringComparison.Ordinal) + 10)..].Split('\n')[0]);
        Assert.StartsWith("xref", texto[inicioXref..]);
    }

    private sealed class RechazaToken : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest));
    }
}
