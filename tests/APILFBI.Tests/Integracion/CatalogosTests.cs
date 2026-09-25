using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using APILFBI.Tests.Infraestructura;

namespace APILFBI.Tests.Integracion;

[Collection(ColeccionApi.Nombre)]
public sealed class CatalogosTests(ApiFactory api)
{
    private static async Task<JsonElement> DataAsync(HttpResponseMessage r)
    {
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        return (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
    }

    private static async Task<int> CodigoAsync(HttpResponseMessage r) =>
        (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("codigo").GetInt32();

    [FactSqlServer]
    public async Task Tipos_documento_trae_estado_y_formatos_permitidos()
    {
        var cliente = await api.ClienteAutenticadoAsync();

        var data = await DataAsync(await cliente.GetAsync("/api/v1/tipos-documento"));

        var ef = data.EnumerateArray().Single(t => t.GetProperty("idTipoDocumento").GetInt32() == 32);
        Assert.Equal("Activo", ef.GetProperty("estado").GetString());
        Assert.Equal("Reemplazar", ef.GetProperty("reglaCarga").GetString());
        var formatos = ef.GetProperty("tiposArchivoPermitidos").EnumerateArray().Select(f => f.GetString()).ToList();
        Assert.Contains("pdf", formatos);
        Assert.DoesNotContain("docx", formatos);
    }

    [FactSqlServer]
    public async Task Tipos_documento_de_un_tipo_de_expediente_se_filtran_por_tipo_de_cliente()
    {
        var cliente = await api.ClienteAutenticadoAsync();

        var natural = await DataAsync(await cliente.GetAsync("/api/v1/tipos-expediente/2/tipos-documento?tipoCliente=N"));
        var ids = natural.GetProperty("tiposDocumento").EnumerateArray().Select(d => d.GetProperty("idTipoDocumento").GetInt32()).ToList();

        Assert.Contains(35, ids);        // constancia de ingresos: solo N
        Assert.Contains(30, ids);        // identificación
        Assert.DoesNotContain(32, ids);  // estados financieros: solo J
        var domicilio = natural.GetProperty("tiposDocumento").EnumerateArray().Single(d => d.GetProperty("idTipoDocumento").GetInt32() == 34);
        Assert.Equal("Ambos", domicilio.GetProperty("aplicaA").GetString());
    }

    [FactSqlServer]
    public async Task Tipos_documento_por_nombre_del_tipo_de_expediente()
    {
        var cliente = await api.ClienteAutenticadoAsync();

        var data = await DataAsync(await cliente.GetAsync("/api/v1/tipos-expediente/tipos-documento?nombreTipoExpediente=banca%20corporativa"));

        Assert.Equal(1, data.GetProperty("idTipoExpediente").GetInt32());
    }

    [FactSqlServer]
    public async Task Tipo_de_expediente_inexistente_devuelve_404_codigo_302()
    {
        var cliente = await api.ClienteAutenticadoAsync();

        var r = await cliente.GetAsync("/api/v1/tipos-expediente/tipos-documento?nombreTipoExpediente=NoExiste");

        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
        Assert.Equal(302, await CodigoAsync(r));
    }

    [FactSqlServer]
    public async Task Tipo_de_cliente_invalido_devuelve_400_codigo_105()
    {
        var cliente = await api.ClienteAutenticadoAsync();

        var r = await cliente.GetAsync("/api/v1/tipos-expediente/1/tipos-documento?tipoCliente=X");

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Equal(105, await CodigoAsync(r));
    }

    [FactSqlServer]
    public async Task Llaves_del_tipo_de_expediente_con_grupos_y_valores_permitidos()
    {
        var cliente = await api.ClienteAutenticadoAsync();

        var data = await DataAsync(await cliente.GetAsync("/api/v1/tipos-expediente/1/llaves?tipoCliente=N"));

        var natural = data.EnumerateArray().Single();
        Assert.Equal("N", natural.GetProperty("tipoCliente").GetString());
        var llaves = natural.GetProperty("llaves").EnumerateArray().ToList();
        var grupo1 = llaves.Where(l => l.GetProperty("grupoIdentificacion").ValueKind == JsonValueKind.Number
                                       && l.GetProperty("grupoIdentificacion").GetInt32() == 1)
                           .Select(l => l.GetProperty("codigo").GetString()).ToList();
        Assert.Equal(["noCasoCRM", "CIF"], grupo1);
        var tipoIdentificacion = llaves.First(l => l.GetProperty("codigo").GetString() == "tipoIdentificacion");
        Assert.Contains("DNI", tipoIdentificacion.GetProperty("valoresPermitidos").EnumerateArray().Select(v => v.GetString()));
    }

    [FactSqlServer]
    public async Task Lista_de_estados_no_incluye_el_estado_derivado_sin_subir()
    {
        var cliente = await api.ClienteAutenticadoAsync();

        var data = await DataAsync(await cliente.GetAsync("/api/v1/catalogos/estados-documento"));

        var codigos = data.EnumerateArray().Select(e => e.GetProperty("codigo").GetString()).ToList();
        Assert.Contains("Rechazado", codigos);
        Assert.DoesNotContain("SinSubir", codigos);
    }

    [FactSqlServer]
    public async Task Catalogo_desconocido_devuelve_400_codigo_100()
    {
        var cliente = await api.ClienteAutenticadoAsync();

        var r = await cliente.GetAsync("/api/v1/catalogos/no-existe");

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Equal(100, await CodigoAsync(r));
    }

    [FactSqlServer]
    public async Task Tipo_de_expediente_con_llaves_invalidas_se_excluye_y_vuelve_al_corregirse()
    {
        var cliente = await api.ClienteAutenticadoAsync();
        async Task<List<string?>> TiposAsync() =>
            (await DataAsync(await cliente.GetAsync("/api/v1/tipos-expediente"))).EnumerateArray()
                .Select(t => t.GetProperty("codigo").GetString()).ToList();

        async Task<List<string?>> EsperarAsync(Func<List<string?>, bool> condicion)
        {
            var tipos = await TiposAsync();
            for (var i = 0; i < 40 && !condicion(tipos); i++)
            {
                await Task.Delay(250);
                tipos = await TiposAsync();
            }
            return tipos;
        }

        try
        {
            // Sin RTN, el grupo 2 de jurídico queda con una sola llave (noCasoCRM).
            await BaseDatosPrueba.EscalarAsync<int?>(api.BaseDatos, "UPDATE cat.Llave SET Activo = 0 WHERE Codigo = 'RTN'; SELECT 1;");
            Assert.Empty(await EsperarAsync(t => t.Count == 0));
        }
        finally
        {
            await BaseDatosPrueba.EscalarAsync<int?>(api.BaseDatos, "UPDATE cat.Llave SET Activo = 1 WHERE Codigo = 'RTN'; SELECT 1;");
        }

        Assert.Contains("BancaCorporativa", await EsperarAsync(t => t.Count == 2));
    }
}
