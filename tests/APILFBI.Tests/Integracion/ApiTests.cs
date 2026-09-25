using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using APILFBI.Tests.Infraestructura;

namespace APILFBI.Tests.Integracion;

[Collection(ColeccionApi.Nombre)]
public sealed class ApiTests(ApiFactory api)
{
    [FactSqlServer]
    public async Task Sin_token_devuelve_401_con_problem_json_y_codigo_201()
    {
        var respuesta = await api.CreateClient().GetAsync("/api/v1/tipos-expediente");

        Assert.Equal(HttpStatusCode.Unauthorized, respuesta.StatusCode);
        Assert.Equal("application/problem+json", respuesta.Content.Headers.ContentType?.MediaType);
        var json = await respuesta.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(201, json.GetProperty("codigo").GetInt32());
        Assert.False(string.IsNullOrEmpty(json.GetProperty("correlationId").GetString()));
    }

    [FactSqlServer]
    public async Task Token_sin_el_scope_requerido_devuelve_403_con_codigo_202()
    {
        var cliente = await api.ClienteAutenticadoAsync("solo-documentos", api.SecretoSoloDocumentos);

        var respuesta = await cliente.GetAsync("/api/v1/tipos-expediente");

        Assert.Equal(HttpStatusCode.Forbidden, respuesta.StatusCode);
        var json = await respuesta.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(202, json.GetProperty("codigo").GetInt32());
    }

    [FactSqlServer]
    public async Task Tipos_expediente_devuelve_el_sobre_estandar_y_respeta_el_correlation_id()
    {
        var cliente = await api.ClienteAutenticadoAsync();
        cliente.DefaultRequestHeaders.Add("X-Correlation-Id", "prueba-correlation-1");

        var respuesta = await cliente.GetAsync("/api/v1/tipos-expediente");

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.Equal("prueba-correlation-1", respuesta.Headers.GetValues("X-Correlation-Id").Single());
        var json = await respuesta.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, json.GetProperty("codigo").GetInt32());
        Assert.Equal("prueba-correlation-1", json.GetProperty("correlationId").GetString());
        var codigos = json.GetProperty("data").EnumerateArray().Select(t => t.GetProperty("codigo").GetString()).ToList();
        Assert.Contains("BancaCorporativa", codigos);
        Assert.Contains("TarjetaCredito", codigos);
    }

    [FactSqlServer]
    public async Task Correlation_id_invalido_se_reemplaza_por_uno_generado()
    {
        var cliente = api.CreateClient();
        cliente.DefaultRequestHeaders.Add("X-Correlation-Id", "<script>");

        var respuesta = await cliente.GetAsync("/health/live");

        Assert.NotEqual("<script>", respuesta.Headers.GetValues("X-Correlation-Id").Single());
    }

    [FactSqlServer]
    public async Task Health_live_y_ready_responden_200()
    {
        var cliente = api.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await cliente.GetAsync("/health/live")).StatusCode);

        HttpResponseMessage? ready = null;
        for (var i = 0; i < 50; i++)
        {
            ready = await cliente.GetAsync("/health/ready");
            if (ready.StatusCode == HttpStatusCode.OK) break;
            await Task.Delay(200);
        }

        Assert.Equal(HttpStatusCode.OK, ready!.StatusCode);
        var json = await ready.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Healthy", json.GetProperty("estado").GetString());
    }

    [FactSqlServer]
    public async Task Un_cambio_en_un_catalogo_se_refleja_sin_reiniciar_la_api()
    {
        var cliente = await api.ClienteAutenticadoAsync();
        await cliente.GetAsync("/api/v1/tipos-expediente");   // carga la caché

        var nuevoNombre = $"Tarjeta de Crédito {Guid.NewGuid():N}"[..30];
        await BaseDatosPrueba.EscalarAsync<int?>(api.BaseDatos,
            $"UPDATE cat.TipoExpediente SET Nombre = N'{nuevoNombre}' WHERE Codigo = 'TarjetaCredito'; SELECT @@ROWCOUNT;");

        string? nombre = null;
        for (var i = 0; i < 40 && nombre != nuevoNombre; i++)
        {
            await Task.Delay(250);
            var json = await (await cliente.GetAsync("/api/v1/tipos-expediente")).Content.ReadFromJsonAsync<JsonElement>();
            nombre = json.GetProperty("data").EnumerateArray()
                .Single(t => t.GetProperty("codigo").GetString() == "TarjetaCredito")
                .GetProperty("nombreTipoExpediente").GetString();
        }

        Assert.Equal(nuevoNombre, nombre);
    }
}
