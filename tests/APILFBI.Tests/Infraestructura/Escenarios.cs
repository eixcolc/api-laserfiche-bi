using System.Net.Http.Json;
using System.Text.Json;

namespace APILFBI.Tests.Infraestructura;

/// <summary>Datos de prueba: expedientes creados por la API y documentos registrados como lo haría el workflow.</summary>
public static class Escenarios
{
    public static string Caso() => Random.Shared.NextInt64(100_000_000, 999_999_999).ToString();
    public static int EntryId() => Random.Shared.Next(1_000_000, 2_000_000_000);

    public static async Task<HttpClient> ClienteOperacionAsync(ApiFactory api)
    {
        var cliente = await api.ClienteAutenticadoAsync();
        cliente.DefaultRequestHeaders.Add("X-Operation-User", "51451");
        return cliente;
    }

    public static async Task<long> CrearExpedienteJuridicoAsync(HttpClient cliente)
    {
        var caso = Caso();
        var r = await cliente.PostAsJsonAsync("/api/v1/expedientes", new
        {
            idTipoExpediente = 1,
            llaves = new[]
            {
                new { tipo = "TipoCliente", valor = "J" }, new { tipo = "noCasoCRM", valor = caso }, new { tipo = "CIF", valor = "Z" + caso },
            },
        });
        r.EnsureSuccessStatusCode();
        return (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data").GetProperty("expediente").GetProperty("idExpediente").GetInt64();
    }

    /// <summary>Registra un documento con trx.usp_RegistrarDocumento y devuelve su LaserficheEntryId.</summary>
    public static async Task<int> RegistrarDocumentoAsync(ApiFactory api, long idExpediente, int tipoDocumento = 32)
    {
        var (entryId, codigo, _) = await IntentarRegistrarAsync(api, idExpediente, tipoDocumento);
        if (codigo != 1) throw new InvalidOperationException($"No se registró el documento de prueba: código {codigo}");
        return entryId;
    }

    /// <summary>Como lo haría el workflow; devuelve el código y el motivo de rechazo (si hubo).</summary>
    public static async Task<(int EntryId, int? Codigo, string? Motivo)> IntentarRegistrarAsync(
        ApiFactory api, long idExpediente, int tipoDocumento = 32, int? reemplaza = null)
    {
        var entryId = EntryId();
        var codigo = await BaseDatosPrueba.EscalarAsync<int?>(api.BaseDatos, $"""
            DECLARE @c int, @m nvarchar(300), @d bigint;
            EXEC trx.usp_RegistrarDocumento @LaserficheEntryId = {entryId}, @IdExpediente = {idExpediente},
                 @Correlativo = N'ESC-{entryId}', @IdTipoDocumento = {tipoDocumento}, @NombreDocumento = N'ESC-{entryId}.pdf',
                 @FechaEmision = '2026-09-01', @UsuarioCarga = '51451', @NombreUsuarioCarga = N'Usuario Prueba',
                 @LaserficheEntryIdReemplaza = {(reemplaza is null ? "NULL" : reemplaza.ToString())},
                 @CodigoRespuesta = @c OUTPUT, @Mensaje = @m OUTPUT, @IdDocumento = @d OUTPUT;
            """);
        var motivo = await BaseDatosPrueba.EscalarAsync<string>(api.BaseDatos, $"""
            SELECT m.Codigo FROM trx.CargaDocumento c
            LEFT JOIN cat.MotivoRechazoCarga m ON m.IdMotivoRechazoCarga = c.IdMotivoRechazoCarga
            WHERE c.Correlativo = N'ESC-{entryId}'
            """);
        return (entryId, codigo, motivo);
    }

    public static async Task<T?> EsperarAsync<T>(Func<Task<T?>> leer, Func<T?, bool> condicion, int intentos = 50)
    {
        var valor = await leer();
        for (var i = 0; i < intentos && !condicion(valor); i++)
        {
            await Task.Delay(100);
            valor = await leer();
        }
        return valor;
    }
}
