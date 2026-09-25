using APILFBI.Tests.Infraestructura;

namespace APILFBI.Tests.Integracion;

/// <summary>Reemplazo explícito de versiones (idDocumentoReemplaza del XML de carga).</summary>
[Collection(ColeccionApi.Nombre)]
public sealed class ReemplazoTests(ApiFactory api)
{
    private Task<string?> EstadoAsync(int entryId) => BaseDatosPrueba.EscalarAsync<string>(api.BaseDatos,
        $"SELECT CONCAT('v', Version, ' vigente=', CAST(Vigente AS int)) FROM trx.Documento WHERE LaserficheEntryId = {entryId}");

    [FactSqlServer]
    public async Task Reemplazar_un_documento_vigente_crea_la_version_siguiente_y_deja_la_anterior_como_historico()
    {
        var cliente = await Escenarios.ClienteOperacionAsync(api);
        var exp = await Escenarios.CrearExpedienteJuridicoAsync(cliente);
        var original = await Escenarios.RegistrarDocumentoAsync(api, exp);

        var (nuevo, codigo, _) = await Escenarios.IntentarRegistrarAsync(api, exp, reemplaza: original);

        Assert.Equal(1, codigo);
        Assert.Equal("v1 vigente=0", await EstadoAsync(original));
        Assert.Equal("v2 vigente=1", await EstadoAsync(nuevo));
    }

    [FactSqlServer]
    public async Task Reemplazar_una_version_que_ya_fue_reemplazada_se_rechaza_y_no_deja_dos_vigentes()
    {
        var cliente = await Escenarios.ClienteOperacionAsync(api);
        var exp = await Escenarios.CrearExpedienteJuridicoAsync(cliente);
        var v1 = await Escenarios.RegistrarDocumentoAsync(api, exp);
        var (v2, _, _) = await Escenarios.IntentarRegistrarAsync(api, exp, reemplaza: v1);

        var (_, codigo, motivo) = await Escenarios.IntentarRegistrarAsync(api, exp, reemplaza: v1);

        Assert.Equal(412, codigo);
        Assert.Equal("DocumentoReemplazaNoVigente", motivo);
        Assert.Equal("v2 vigente=1", await EstadoAsync(v2));
        Assert.Equal(1, await BaseDatosPrueba.EscalarAsync<int?>(api.BaseDatos, $"""
            SELECT COUNT(*) FROM trx.ExpedienteDocumento ed JOIN trx.Documento d ON d.IdDocumento = ed.IdDocumento
            WHERE ed.IdExpediente = {exp} AND ed.Vigente = 1 AND d.Vigente = 1 AND d.IdTipoDocumento = 32
            """));
    }

    [FactSqlServer]
    public async Task No_se_puede_reemplazar_un_documento_de_otro_cliente()
    {
        var cliente = await Escenarios.ClienteOperacionAsync(api);
        var expOtroCliente = await Escenarios.CrearExpedienteJuridicoAsync(cliente);
        var ajeno = await Escenarios.RegistrarDocumentoAsync(api, expOtroCliente);
        var exp = await Escenarios.CrearExpedienteJuridicoAsync(cliente);   // otra persona (otro CIF)

        var (_, codigo, motivo) = await Escenarios.IntentarRegistrarAsync(api, exp, reemplaza: ajeno);

        Assert.Equal(301, codigo);
        Assert.Equal("DocumentoReemplazaNoExiste", motivo);
        Assert.Equal("v1 vigente=1", await EstadoAsync(ajeno));
    }
}
