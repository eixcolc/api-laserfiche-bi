using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using APILFBI.Tests.Infraestructura;

namespace APILFBI.Tests.Integracion;

[Collection(ColeccionApi.Nombre)]
public sealed class ExpedientesTests(ApiFactory api)
{
    private static string Caso() => Random.Shared.NextInt64(100_000_000, 999_999_999).ToString();

    private static object Solicitud(int idTipoExpediente, params (string Tipo, string Valor)[] llaves) => new
    {
        idTipoExpediente,
        llaves = llaves.Select(l => new { tipo = l.Tipo, valor = l.Valor }),
    };

    private async Task<HttpClient> ClienteAsync(string? usuarioOperacion = "51451")
    {
        var cliente = await api.ClienteAutenticadoAsync();
        if (usuarioOperacion is not null) cliente.DefaultRequestHeaders.Add("X-Operation-User", usuarioOperacion);
        return cliente;
    }

    private static async Task<(HttpStatusCode Status, JsonElement Json)> PostAsync(HttpClient cliente, object cuerpo, string? idempotencia = null)
    {
        var solicitud = new HttpRequestMessage(HttpMethod.Post, "/api/v1/expedientes") { Content = JsonContent.Create(cuerpo) };
        if (idempotencia is not null) solicitud.Headers.Add("Idempotency-Key", idempotencia);
        var r = await cliente.SendAsync(solicitud);
        return (r.StatusCode, await r.Content.ReadFromJsonAsync<JsonElement>());
    }

    [FactSqlServer]
    public async Task Crear_devuelve_201_y_repetir_devuelve_200_con_el_mismo_expediente()
    {
        var cliente = await ClienteAsync();
        var caso = Caso();
        var cuerpo = Solicitud(1, ("TipoCliente", "J"), ("noCasoCRM", caso), ("CIF", "C" + caso), ("RTN", "0801-1999-" + caso[..6]),
                               ("razonSocial", "Empresa Ejemplo S.A."));

        var respuesta = await cliente.PostAsync("/api/v1/expedientes", JsonContent.Create(cuerpo));
        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
        var json = await respuesta.Content.ReadFromJsonAsync<JsonElement>();
        var id = json.GetProperty("data").GetProperty("expediente").GetProperty("idExpediente").GetInt64();
        Assert.Equal($"/api/v1/expedientes/{id}", respuesta.Headers.Location?.ToString());
        Assert.Equal(2, json.GetProperty("codigo").GetInt32());
        Assert.True(json.GetProperty("data").GetProperty("esNuevo").GetBoolean());
        Assert.Equal("J", json.GetProperty("data").GetProperty("expediente").GetProperty("tipoCliente").GetString());

        var (status, repetido) = await PostAsync(cliente, cuerpo);
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(id, repetido.GetProperty("data").GetProperty("expediente").GetProperty("idExpediente").GetInt64());
        Assert.False(repetido.GetProperty("data").GetProperty("esNuevo").GetBoolean());
    }

    [FactSqlServer]
    public async Task Sin_usuario_de_operacion_devuelve_400_codigo_101()
    {
        var cliente = await ClienteAsync(usuarioOperacion: null);

        var (status, json) = await PostAsync(cliente, Solicitud(1, ("TipoCliente", "J"), ("noCasoCRM", Caso()), ("CIF", "X1")));

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Equal(101, json.GetProperty("codigo").GetInt32());
        Assert.Equal("X-Operation-User", json.GetProperty("errores")[0].GetProperty("campo").GetString());
    }

    [FactSqlServer]
    public async Task Sin_llaves_devuelve_400_codigo_101()
    {
        var cliente = await ClienteAsync();

        var (status, json) = await PostAsync(cliente, new { idTipoExpediente = 1 });

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Equal(101, json.GetProperty("codigo").GetInt32());
    }

    [FactSqlServer]
    public async Task Una_sola_llave_identificadora_devuelve_400_codigo_103()
    {
        var cliente = await ClienteAsync();

        var (status, json) = await PostAsync(cliente, Solicitud(1, ("TipoCliente", "J"), ("noCasoCRM", Caso())));

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Equal(103, json.GetProperty("codigo").GetInt32());
    }

    [FactSqlServer]
    public async Task Valor_que_no_cumple_la_expresion_de_la_llave_devuelve_400_codigo_102()
    {
        var cliente = await ClienteAsync();

        var (status, json) = await PostAsync(cliente, Solicitud(1, ("TipoCliente", "J"), ("noCasoCRM", "caso$%&"), ("CIF", "X1")));

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Equal(102, json.GetProperty("codigo").GetInt32());
        Assert.Equal("llaves.noCasoCRM", json.GetProperty("errores")[0].GetProperty("campo").GetString());
    }

    [FactSqlServer]
    public async Task Tipo_de_expediente_inexistente_devuelve_404_codigo_302()
    {
        var cliente = await ClienteAsync();

        var (status, json) = await PostAsync(cliente, Solicitud(999, ("TipoCliente", "J"), ("noCasoCRM", Caso()), ("CIF", "X1")));

        Assert.Equal(HttpStatusCode.NotFound, status);
        Assert.Equal(302, json.GetProperty("codigo").GetInt32());
    }

    [FactSqlServer]
    public async Task Llaves_de_expedientes_distintos_devuelven_409_codigo_500()
    {
        var cliente = await ClienteAsync();
        var caso = Caso();
        await PostAsync(cliente, Solicitud(1, ("TipoCliente", "J"), ("noCasoCRM", caso), ("CIF", "A" + caso)));
        await PostAsync(cliente, Solicitud(1, ("TipoCliente", "J"), ("noCasoCRM", caso), ("RTN", "B" + caso)));

        var (status, json) = await PostAsync(cliente, Solicitud(1, ("TipoCliente", "J"), ("noCasoCRM", caso), ("CIF", "A" + caso), ("RTN", "B" + caso)));

        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Equal(500, json.GetProperty("codigo").GetInt32());
    }

    [FactSqlServer]
    public async Task Idempotency_key_repetida_devuelve_la_respuesta_original_sin_reejecutar()
    {
        var cliente = await ClienteAsync();
        var llave = $"idem-{Guid.NewGuid():N}";
        var cuerpo = Solicitud(2, ("TipoCliente", "N"), ("noCasoCRM", Caso()), ("tipoIdentificacion", "DNI"), ("noIdentificacion", Caso()));

        var (status1, json1) = await PostAsync(cliente, cuerpo, llave);
        var solicitud = new HttpRequestMessage(HttpMethod.Post, "/api/v1/expedientes") { Content = JsonContent.Create(cuerpo) };
        solicitud.Headers.Add("Idempotency-Key", llave);
        var r2 = await cliente.SendAsync(solicitud);

        Assert.Equal(HttpStatusCode.Created, status1);
        Assert.Equal(HttpStatusCode.Created, r2.StatusCode);
        Assert.Equal("true", r2.Headers.GetValues("Idempotent-Replayed").Single());
        var json2 = await r2.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(json1.GetProperty("data").GetProperty("expediente").GetProperty("idExpediente").GetInt64(),
                     json2.GetProperty("data").GetProperty("expediente").GetProperty("idExpediente").GetInt64());

        var (status3, json3) = await PostAsync(cliente, Solicitud(2, ("TipoCliente", "N"), ("noCasoCRM", Caso()), ("CIF", "otro")), llave);
        Assert.Equal(HttpStatusCode.Conflict, status3);
        Assert.Equal(502, json3.GetProperty("codigo").GetInt32());
    }

    [FactSqlServer]
    public async Task Solicitudes_simultaneas_con_las_mismas_llaves_crean_un_solo_expediente()
    {
        var cliente = await ClienteAsync();
        var caso = Caso();
        var cuerpo = Solicitud(1, ("TipoCliente", "N"), ("noCasoCRM", caso), ("CIF", "P" + caso));

        var resultados = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => PostAsync(cliente, cuerpo)));

        Assert.All(resultados, r => Assert.True(r.Status is HttpStatusCode.OK or HttpStatusCode.Created or HttpStatusCode.Conflict,
            $"Estado inesperado {r.Status}: {r.Json}"));
        var exitosos = resultados.Where(r => r.Status is HttpStatusCode.OK or HttpStatusCode.Created).ToList();
        Assert.Single(exitosos, r => r.Status == HttpStatusCode.Created);
        Assert.Single(exitosos.Select(r => r.Json.GetProperty("data").GetProperty("expediente").GetProperty("idExpediente").GetInt64()).Distinct());
    }

    [FactSqlServer]
    public async Task Obtener_expediente_devuelve_sus_llaves_vigentes_y_404_si_no_existe()
    {
        var cliente = await ClienteAsync();
        var caso = Caso();
        var (_, creado) = await PostAsync(cliente, Solicitud(1, ("TipoCliente", "J"), ("noCasoCRM", caso), ("CIF", "G" + caso), ("segmentacion", "Corporativo")));
        var id = creado.GetProperty("data").GetProperty("expediente").GetProperty("idExpediente").GetInt64();

        var r = await cliente.GetAsync($"/api/v1/expedientes/{id}");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var llaves = (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data").GetProperty("llaves")
            .EnumerateArray().ToDictionary(l => l.GetProperty("tipo").GetString()!, l => l.GetProperty("valor").GetString());
        Assert.Equal(caso, llaves["noCasoCRM"]);
        Assert.Equal("Corporativo", llaves["segmentacion"]);

        var noExiste = await cliente.GetAsync("/api/v1/expedientes/999999999");
        Assert.Equal(HttpStatusCode.NotFound, noExiste.StatusCode);
    }
}
