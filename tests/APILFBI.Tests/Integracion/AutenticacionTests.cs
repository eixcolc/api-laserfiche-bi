using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using APILFBI.Tests.Infraestructura;

namespace APILFBI.Tests.Integracion;

[Collection(ColeccionApi.Nombre)]
public sealed class AutenticacionTests(ApiFactory api)
{
    [FactSqlServer]
    public async Task Token_con_credenciales_validas_devuelve_token_de_una_hora()
    {
        var respuesta = await api.CreateClient().PostAsync("/api/v1/auth/token", ApiFactory.FormularioToken("crm-test", api.SecretoCrm));

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.Equal("no-store", respuesta.Headers.CacheControl?.ToString());
        var json = await respuesta.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(string.IsNullOrEmpty(json.GetProperty("access_token").GetString()));
        Assert.Equal("Bearer", json.GetProperty("token_type").GetString());
        Assert.Equal(3600, json.GetProperty("expires_in").GetInt32());
        Assert.Equal(1, json.GetProperty("codigo").GetInt32());
        Assert.Contains("catalogos.leer", json.GetProperty("scope").GetString());
    }

    [FactSqlServer]
    public async Task Token_con_header_Basic_tambien_funciona()
    {
        var cliente = api.CreateClient();
        var solicitud = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/token")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["grant_type"] = "client_credentials" }),
        };
        solicitud.Headers.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"crm-test:{api.SecretoCrm}")));

        var respuesta = await cliente.SendAsync(solicitud);

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
    }

    [FactSqlServer]
    public async Task Secreto_invalido_devuelve_401_invalid_client_con_codigo_200()
    {
        var respuesta = await api.CreateClient().PostAsync("/api/v1/auth/token", ApiFactory.FormularioToken("crm-test", "incorrecto"));

        Assert.Equal(HttpStatusCode.Unauthorized, respuesta.StatusCode);
        Assert.NotEmpty(respuesta.Headers.WwwAuthenticate);
        var json = await respuesta.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("invalid_client", json.GetProperty("error").GetString());
        Assert.Equal(200, json.GetProperty("codigo").GetInt32());
    }

    [FactSqlServer]
    public async Task Cliente_inexistente_responde_igual_que_secreto_invalido()
    {
        var respuesta = await api.CreateClient().PostAsync("/api/v1/auth/token", ApiFactory.FormularioToken("no-existe", "x"));

        Assert.Equal(HttpStatusCode.Unauthorized, respuesta.StatusCode);
        var json = await respuesta.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("invalid_client", json.GetProperty("error").GetString());
    }

    [FactSqlServer]
    public async Task Grant_type_distinto_devuelve_400_unsupported_grant_type()
    {
        var respuesta = await api.CreateClient().PostAsync("/api/v1/auth/token",
            ApiFactory.FormularioToken("crm-test", api.SecretoCrm, grantType: "password"));

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        var json = await respuesta.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("unsupported_grant_type", json.GetProperty("error").GetString());
    }

    [FactSqlServer]
    public async Task Scope_no_asignado_a_la_cuenta_devuelve_400_invalid_scope()
    {
        var respuesta = await api.CreateClient().PostAsync("/api/v1/auth/token",
            ApiFactory.FormularioToken("crm-test", api.SecretoCrm, scope: "bitacora.leer"));

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        var json = await respuesta.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("invalid_scope", json.GetProperty("error").GetString());
    }

    [FactSqlServer]
    public async Task Cada_intento_de_autenticacion_queda_en_la_bitacora()
    {
        var correlationId = $"auth-{Guid.NewGuid():N}"[..30];
        var cliente = api.CreateClient();
        cliente.DefaultRequestHeaders.Add("X-Correlation-Id", correlationId);
        await cliente.PostAsync("/api/v1/auth/token", ApiFactory.FormularioToken("crm-test", "incorrecto"));

        int? codigo = null;
        for (var i = 0; i < 50 && codigo is null; i++)
        {
            await Task.Delay(100);
            codigo = await BaseDatosPrueba.EscalarAsync<int?>(api.BaseDatos,
                $"SELECT CodigoRespuesta FROM aud.Bitacora WHERE CorrelationId = '{correlationId}'");
        }

        Assert.Equal(200, codigo);
        var resultado = await BaseDatosPrueba.EscalarAsync<string>(api.BaseDatos,
            $"SELECT r.Codigo FROM aud.Bitacora b JOIN cat.ResultadoOperacion r ON r.IdResultadoOperacion = b.IdResultadoOperacion WHERE b.CorrelationId = '{correlationId}'");
        Assert.Equal("NoAutorizado", resultado);
    }
}
