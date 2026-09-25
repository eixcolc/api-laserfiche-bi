using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using APILFBI.Tests.Infraestructura;

namespace APILFBI.Tests.Integracion;

[Collection(ColeccionApi.Nombre)]
public sealed class DocumentosTests(ApiFactory api)
{
    private static string Caso() => Random.Shared.NextInt64(100_000_000, 999_999_999).ToString();
    private static int EntryId() => Random.Shared.Next(1_000_000, 2_000_000_000);

    private async Task<HttpClient> ClienteAsync()
    {
        var cliente = await api.ClienteAutenticadoAsync();
        cliente.DefaultRequestHeaders.Add("X-Operation-User", "51451");
        return cliente;
    }

    /// <summary>Crea un expediente jurídico en Banca Corporativa y devuelve su id.</summary>
    private static async Task<long> CrearExpedienteJuridicoAsync(HttpClient cliente, string caso, string cif, string rtn)
    {
        var r = await cliente.PostAsJsonAsync("/api/v1/expedientes", new
        {
            idTipoExpediente = 1,
            llaves = new[]
            {
                new { tipo = "TipoCliente", valor = "J" }, new { tipo = "noCasoCRM", valor = caso },
                new { tipo = "CIF", valor = cif }, new { tipo = "RTN", valor = rtn },
                new { tipo = "segmentacion", valor = "Corporativo" },
            },
        });
        r.EnsureSuccessStatusCode();
        return (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data").GetProperty("expediente").GetProperty("idExpediente").GetInt64();
    }

    /// <summary>Hace lo mismo que el workflow de Laserfiche después de Import Agent.</summary>
    private Task<int?> RegistrarAsync(long idExpediente, int entryId, string correlativo, int tipoDocumento, string archivo) =>
        BaseDatosPrueba.EscalarAsync<int?>(api.BaseDatos, $"""
            DECLARE @c int, @m nvarchar(300), @d bigint;
            EXEC trx.usp_RegistrarDocumento @LaserficheEntryId = {entryId}, @IdExpediente = {idExpediente},
                 @Correlativo = N'{correlativo}', @IdTipoDocumento = {tipoDocumento}, @NombreDocumento = N'{archivo}',
                 @FechaEmision = '2026-09-01', @UsuarioCarga = '51451', @NombreUsuarioCarga = N'Reina Pasita Caceres Palacios',
                 @Comentario = N'Estados financieros al cierre de agosto',
                 @CodigoRespuesta = @c OUTPUT, @Mensaje = @m OUTPUT, @IdDocumento = @d OUTPUT;
            """);

    private static async Task<JsonElement> DataAsync(HttpResponseMessage r)
    {
        Assert.True(r.StatusCode == HttpStatusCode.OK, $"{(int)r.StatusCode}: {await r.Content.ReadAsStringAsync()}");
        return (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
    }

    [FactSqlServer]
    public async Task Consulta_natural_sin_expediente_devuelve_todos_los_tipos_como_sin_subir()
    {
        var cliente = await ClienteAsync();

        var data = await DataAsync(await cliente.PostAsJsonAsync("/api/v1/clientes/natural/documentos/consulta",
            new { idTipoExpediente = 2, noCaso = Caso(), tipoIdentificacion = "DNI", noIdentificacion = Caso() }));

        Assert.Equal(JsonValueKind.Null, data.GetProperty("idExpediente").ValueKind);
        var documentos = data.GetProperty("documentos").EnumerateArray().ToList();
        Assert.Equal(3, documentos.Count);   // identificación, constancia de ingresos, comprobante de domicilio
        Assert.All(documentos, d =>
        {
            Assert.Equal("SinSubir", d.GetProperty("estado").GetString());
            Assert.Equal(JsonValueKind.Null, d.GetProperty("idDocumento").ValueKind);
        });
    }

    [FactSqlServer]
    public async Task Consulta_juridica_devuelve_el_documento_cargado_y_el_resto_sin_subir()
    {
        var cliente = await ClienteAsync();
        var caso = Caso();
        var idExpediente = await CrearExpedienteJuridicoAsync(cliente, caso, "C" + caso, "R" + caso);
        var entryId = EntryId();
        Assert.Equal(1, await RegistrarAsync(idExpediente, entryId, $"CJ-{entryId}", 32, $"CJ-{entryId}_archivo.pdf"));

        var porCif = await DataAsync(await cliente.PostAsJsonAsync("/api/v1/clientes/juridico/documentos/consulta",
            new { idTipoExpediente = 1, noCaso = caso, cif = "C" + caso }));

        Assert.Equal(idExpediente, porCif.GetProperty("idExpediente").GetInt64());
        var documentos = porCif.GetProperty("documentos").EnumerateArray().ToList();
        var ef = documentos.Single(d => d.GetProperty("idTipoDocumento").GetInt32() == 32);
        Assert.Equal(entryId, ef.GetProperty("idDocumento").GetInt32());
        Assert.Equal("PendienteRevision", ef.GetProperty("estado").GetString());
        Assert.Equal("Corporativo", ef.GetProperty("segmentacion").GetString());
        Assert.Equal("2027-09-01", ef.GetProperty("fechaVencimiento").GetString());   // emisión + 365 días
        Assert.Equal("Reina Pasita Caceres Palacios", ef.GetProperty("nombreEmpleadoAdjunto").GetString());
        Assert.Equal("Estados financieros al cierre de agosto", ef.GetProperty("comentario").GetString());
        Assert.Contains(documentos, d => d.GetProperty("idTipoDocumento").GetInt32() == 31 && d.GetProperty("estado").GetString() == "SinSubir");

        // Cliente potencial: el mismo expediente se encuentra por noCaso + RTN.
        var porRtn = await DataAsync(await cliente.PostAsJsonAsync("/api/v1/clientes/juridico/documentos/consulta",
            new { idTipoExpediente = 1, noCaso = caso, rtn = "R" + caso }));
        Assert.Equal(idExpediente, porRtn.GetProperty("idExpediente").GetInt64());

        var porId = await DataAsync(await cliente.GetAsync($"/api/v1/expedientes/{idExpediente}/documentos"));
        Assert.Contains(porId.GetProperty("documentos").EnumerateArray(), d => d.GetProperty("idDocumento").ValueKind == JsonValueKind.Number
                                                                              && d.GetProperty("idDocumento").GetInt32() == entryId);
    }

    [FactSqlServer]
    public async Task Consulta_juridica_sin_cif_ni_rtn_devuelve_400_codigo_101()
    {
        var cliente = await ClienteAsync();

        var r = await cliente.PostAsJsonAsync("/api/v1/clientes/juridico/documentos/consulta", new { idTipoExpediente = 1, noCaso = Caso() });

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        var json = await r.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(101, json.GetProperty("codigo").GetInt32());
        Assert.Equal("cif", json.GetProperty("errores")[0].GetProperty("campo").GetString());
    }

    [FactSqlServer]
    public async Task Estado_de_carga_importada_rechazada_e_inexistente()
    {
        var cliente = await ClienteAsync();
        var caso = Caso();
        var idExpediente = await CrearExpedienteJuridicoAsync(cliente, caso, "K" + caso, "L" + caso);
        var entryOk = EntryId();
        var entryMal = EntryId();
        await RegistrarAsync(idExpediente, entryOk, $"OK-{entryOk}", 32, "archivo.pdf");
        await RegistrarAsync(idExpediente, entryMal, $"MAL-{entryMal}", 32, "archivo.docx");

        var ok = await DataAsync(await cliente.GetAsync($"/api/v1/cargas/OK-{entryOk}"));
        Assert.Equal("Importado", ok.GetProperty("estadoCarga").GetString());
        Assert.Equal(entryOk, ok.GetProperty("idDocumento").GetInt32());

        var mal = await DataAsync(await cliente.GetAsync($"/api/v1/cargas/MAL-{entryMal}"));
        Assert.Equal("Rechazado", mal.GetProperty("estadoCarga").GetString());
        Assert.Equal("FormatoNoPermitido", mal.GetProperty("motivoRechazo").GetString());
        Assert.Equal(JsonValueKind.Null, mal.GetProperty("idDocumento").ValueKind);

        var noExiste = await cliente.GetAsync("/api/v1/cargas/NO-EXISTE-123");
        Assert.Equal(HttpStatusCode.NotFound, noExiste.StatusCode);
        Assert.Equal(304, (await noExiste.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("codigo").GetInt32());
    }

    [FactSqlServer]
    public async Task Descarga_entrega_el_documento_inline_admite_range_y_queda_en_bitacora()
    {
        var cliente = await ClienteAsync();
        var caso = Caso();
        var idExpediente = await CrearExpedienteJuridicoAsync(cliente, caso, "D" + caso, "E" + caso);
        var entryId = EntryId();
        await RegistrarAsync(idExpediente, entryId, $"DS-{entryId}", 32, "Estados financieros.pdf");

        var completo = await cliente.GetAsync($"/api/v1/documentos/{entryId}/contenido");
        Assert.Equal(HttpStatusCode.OK, completo.StatusCode);
        Assert.Equal("application/pdf", completo.Content.Headers.ContentType?.MediaType);
        Assert.Equal("inline", completo.Content.Headers.ContentDisposition?.DispositionType);
        Assert.Contains("Estados financieros.pdf", completo.Content.Headers.ContentDisposition?.ToString());
        Assert.StartsWith("%PDF", Encoding.ASCII.GetString(await completo.Content.ReadAsByteArrayAsync()));

        var solicitud = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/documentos/{entryId}/contenido");
        solicitud.Headers.Range = new RangeHeaderValue(0, 3);
        var parcial = await cliente.SendAsync(solicitud);
        Assert.Equal(HttpStatusCode.PartialContent, parcial.StatusCode);
        Assert.Equal("%PDF", Encoding.ASCII.GetString(await parcial.Content.ReadAsByteArrayAsync()));

        int? registros = 0;
        for (var i = 0; i < 50 && registros == 0; i++)
        {
            await Task.Delay(100);
            registros = await BaseDatosPrueba.EscalarAsync<int?>(api.BaseDatos, $"""
                SELECT COUNT(*) FROM aud.Bitacora b JOIN cat.TipoOperacion o ON o.IdTipoOperacion = b.IdTipoOperacion
                WHERE o.Codigo = 'DescargaDocumento' AND b.Detalle LIKE '%{entryId}%'
                """);
        }
        Assert.True(registros > 0);
    }

    [FactSqlServer]
    public async Task Descarga_de_documento_inexistente_devuelve_404_codigo_301()
    {
        var cliente = await ClienteAsync();

        var r = await cliente.GetAsync("/api/v1/documentos/123/contenido");

        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
        Assert.Equal(301, (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("codigo").GetInt32());
    }

    [FactSqlServer]
    public async Task Health_ready_incluye_laserfiche()
    {
        var r = await api.CreateClient().GetAsync("/health/ready");

        var json = await r.Content.ReadFromJsonAsync<JsonElement>();
        var laserfiche = json.GetProperty("verificaciones").EnumerateArray().Single(v => v.GetProperty("nombre").GetString() == "laserfiche");
        Assert.Equal("Healthy", laserfiche.GetProperty("estado").GetString());
        Assert.Equal("Laserfiche simulado", laserfiche.GetProperty("descripcion").GetString());
    }
}
