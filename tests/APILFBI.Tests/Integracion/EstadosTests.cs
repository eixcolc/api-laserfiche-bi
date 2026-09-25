using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using APILFBI.Tests.Infraestructura;

namespace APILFBI.Tests.Integracion;

[Collection(ColeccionApi.Nombre)]
public sealed class EstadosTests(ApiFactory api)
{
    private static object Item(int idDocumento, string estado, string? tipoRechazo = null, string? etapa = null, string? comentario = null) =>
        new { idDocumento, codigoEstado = estado, codigoTipoRechazo = tipoRechazo, codigoEtapaRechazo = etapa, comentario };

    private static async Task<(HttpStatusCode Status, JsonElement Json)> CambiarAsync(HttpClient cliente, object cuerpo)
    {
        var r = await cliente.PostAsJsonAsync("/api/v1/documentos/estado", cuerpo);
        return (r.StatusCode, await r.Content.ReadFromJsonAsync<JsonElement>());
    }

    [FactSqlServer]
    public async Task Rechazado_sin_tipo_de_rechazo_devuelve_422_codigo_401_y_no_aplica_nada()
    {
        var cliente = await Escenarios.ClienteOperacionAsync(api);
        var exp = await Escenarios.CrearExpedienteJuridicoAsync(cliente);
        var doc = await Escenarios.RegistrarDocumentoAsync(api, exp);

        var (status, json) = await CambiarAsync(cliente, new { idExpediente = exp, documentos = new[] { Item(doc, "Rechazado", comentario: "No cuadra") } });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, status);
        Assert.Equal(401, json.GetProperty("codigo").GetInt32());
        var item = json.GetProperty("data").GetProperty("documentos")[0];
        Assert.False(item.GetProperty("aplicado").GetBoolean());
        Assert.Equal(401, item.GetProperty("codigo").GetInt32());
    }

    [FactSqlServer]
    public async Task Rechazo_completo_se_aplica_y_se_sincroniza_con_laserfiche()
    {
        var cliente = await Escenarios.ClienteOperacionAsync(api);
        var exp = await Escenarios.CrearExpedienteJuridicoAsync(cliente);
        var doc = await Escenarios.RegistrarDocumentoAsync(api, exp);

        var (status, json) = await CambiarAsync(cliente, new
        {
            idExpediente = exp,
            ejecutivoAsignado = "Ejecutivo Uno",
            documentos = new[] { Item(doc, "Rechazado", "EstadosFinancierosDescuadrados", "AnalisisCredito", "Mes de febrero y marzo no cuadra") },
        });

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(1, json.GetProperty("codigo").GetInt32());
        Assert.True(json.GetProperty("data").GetProperty("documentos")[0].GetProperty("aplicado").GetBoolean());

        var documentos = (await (await cliente.GetAsync($"/api/v1/expedientes/{exp}/documentos")).Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("data").GetProperty("documentos").EnumerateArray();
        Assert.Contains(documentos, d => d.GetProperty("idDocumento").ValueKind == JsonValueKind.Number
                                         && d.GetProperty("idDocumento").GetInt32() == doc && d.GetProperty("estado").GetString() == "Rechazado");

        // El outbox se procesa en segundo plano con Laserfiche simulado.
        var estado = await Escenarios.EsperarAsync(() => BaseDatosPrueba.EscalarAsync<string>(api.BaseDatos, $"""
            SELECT TOP 1 e.Codigo FROM trx.SincronizacionLaserfiche s
            JOIN cat.EstadoSincronizacion e ON e.IdEstadoSincronizacion = s.IdEstadoSincronizacion
            WHERE s.LaserficheEntryId = {doc} ORDER BY s.IdSincronizacion DESC
            """), e => e == "Sincronizado");
        Assert.Equal("Sincronizado", estado);

        var campos = await BaseDatosPrueba.EscalarAsync<string>(api.BaseDatos,
            $"SELECT TOP 1 Campos FROM trx.SincronizacionLaserfiche WHERE LaserficheEntryId = {doc} ORDER BY IdSincronizacion DESC");
        using var jsonCampos = JsonDocument.Parse(campos!);
        Assert.Equal("Rechazado", jsonCampos.RootElement.GetProperty("Estado").GetString());
        Assert.Equal("Ejecutivo Uno", jsonCampos.RootElement.GetProperty("EjecutivoAsignado").GetString());
        Assert.Equal("EstadosFinancierosDescuadrados", jsonCampos.RootElement.GetProperty("TipoRechazo").GetString());
    }

    [FactSqlServer]
    public async Task Sin_ejecutivo_asignado_no_se_envia_ese_campo_a_laserfiche()
    {
        var cliente = await Escenarios.ClienteOperacionAsync(api);
        var exp = await Escenarios.CrearExpedienteJuridicoAsync(cliente);
        var doc = await Escenarios.RegistrarDocumentoAsync(api, exp);

        await CambiarAsync(cliente, new { idExpediente = exp, documentos = new[] { Item(doc, "Aprobado") } });

        var campos = await BaseDatosPrueba.EscalarAsync<string>(api.BaseDatos,
            $"SELECT TOP 1 Campos FROM trx.SincronizacionLaserfiche WHERE LaserficheEntryId = {doc} ORDER BY IdSincronizacion DESC");
        using var json = JsonDocument.Parse(campos!);
        Assert.False(json.RootElement.TryGetProperty("EjecutivoAsignado", out _));
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("TipoRechazo").ValueKind);   // se limpia en Laserfiche
    }

    [FactSqlServer]
    public async Task Lote_parcial_aplica_los_validos_y_devuelve_codigo_3()
    {
        var cliente = await Escenarios.ClienteOperacionAsync(api);
        var exp = await Escenarios.CrearExpedienteJuridicoAsync(cliente);
        var doc = await Escenarios.RegistrarDocumentoAsync(api, exp);

        var (status, json) = await CambiarAsync(cliente, new
        {
            idExpediente = exp,
            permitirParcial = true,
            documentos = new[] { Item(doc, "Aprobado"), Item(424242, "Aprobado") },
        });

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(3, json.GetProperty("codigo").GetInt32());
        var items = json.GetProperty("data").GetProperty("documentos").EnumerateArray().ToList();
        Assert.True(items.Single(i => i.GetProperty("idDocumento").GetInt32() == doc).GetProperty("aplicado").GetBoolean());
        Assert.Equal(301, items.Single(i => i.GetProperty("idDocumento").GetInt32() == 424242).GetProperty("codigo").GetInt32());
    }

    [FactSqlServer]
    public async Task Transicion_no_permitida_devuelve_422_codigo_400()
    {
        var cliente = await Escenarios.ClienteOperacionAsync(api);
        var exp = await Escenarios.CrearExpedienteJuridicoAsync(cliente);
        var doc = await Escenarios.RegistrarDocumentoAsync(api, exp);
        await CambiarAsync(cliente, new { idExpediente = exp, documentos = new[] { Item(doc, "Rechazado", "DocumentoIlegible", comentario: "Ilegible") } });

        var (status, json) = await CambiarAsync(cliente, new { idExpediente = exp, documentos = new[] { Item(doc, "Aprobado") } });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, status);
        Assert.Equal(400, json.GetProperty("codigo").GetInt32());
    }

    [FactSqlServer]
    public async Task Vencido_solo_lo_asigna_el_sistema_422_codigo_403()
    {
        var cliente = await Escenarios.ClienteOperacionAsync(api);
        var exp = await Escenarios.CrearExpedienteJuridicoAsync(cliente);
        var doc = await Escenarios.RegistrarDocumentoAsync(api, exp);

        var (status, json) = await CambiarAsync(cliente, new { idExpediente = exp, documentos = new[] { Item(doc, "Vencido") } });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, status);
        Assert.Equal(403, json.GetProperty("codigo").GetInt32());
    }

    [FactSqlServer]
    public async Task Expediente_inexistente_devuelve_404_codigo_300()
    {
        var cliente = await Escenarios.ClienteOperacionAsync(api);

        var (status, json) = await CambiarAsync(cliente, new { idExpediente = 999999999, documentos = new[] { Item(1, "Aprobado") } });

        Assert.Equal(HttpStatusCode.NotFound, status);
        Assert.Equal(300, json.GetProperty("codigo").GetInt32());
    }

    [FactSqlServer]
    public async Task Sin_usuario_de_operacion_devuelve_400_codigo_101()
    {
        var cliente = await api.ClienteAutenticadoAsync();

        var (status, json) = await CambiarAsync(cliente, new { idExpediente = 1, documentos = new[] { Item(1, "Aprobado") } });

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Equal(101, json.GetProperty("codigo").GetInt32());
    }
}
