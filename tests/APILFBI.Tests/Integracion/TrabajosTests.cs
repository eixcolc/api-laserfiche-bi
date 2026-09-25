using APILFBI.Application.Abstracciones;
using APILFBI.Application.Comun;
using APILFBI.Infrastructure.Laserfiche;
using APILFBI.Infrastructure.Persistencia;
using APILFBI.Infrastructure.Trabajos;
using APILFBI.Tests.Infraestructura;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace APILFBI.Tests.Integracion;

[Collection(ColeccionApi.Nombre)]
public sealed class TrabajosTests(ApiFactory api)
{
    /// <summary>Laserfiche falso para probar reintentos y el orden de procesamiento.</summary>
    private sealed class LaserficheFalso(bool falla) : ILaserficheRepositoryService
    {
        public List<(int EntryId, IReadOnlyDictionary<string, string?> Campos)> Llamadas { get; } = [];

        public Task ActualizarCamposAsync(int entryId, IReadOnlyDictionary<string, string?> campos, CancellationToken ct = default)
        {
            Llamadas.Add((entryId, campos));
            return falla ? throw new ApiException(CodigosRespuesta.LaserficheNoDisponible, "caído") : Task.CompletedTask;
        }

        public Task<ContenidoDocumento> DescargarAsync(int entryId, string? rango, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<(bool Disponible, string Detalle)> VerificarAsync(CancellationToken ct = default) => Task.FromResult((true, "falso"));
    }

    private SincronizadorLaserfiche Sincronizador(ILaserficheRepositoryService laserfiche, int maxIntentos = 10) => new(
        api.Services.GetRequiredService<IDbContextFactory<BilfDbContext>>(),
        laserfiche,
        Options.Create(new LaserficheOptions { Campos = new CamposLaserfiche { Estado = "EstadoDocumentoLF" } }),
        Options.Create(new SincronizacionOptions { MaxIntentos = maxIntentos, EsperaBaseSegundos = 30 }),
        api.Services.GetRequiredService<IBitacoraService>(),
        TimeProvider.System,
        NullLogger<SincronizadorLaserfiche>.Instance);

    /// <summary>Inserta una fila de outbox directamente (sin pasar por la cola de la API).</summary>
    private async Task<long> InsertarPendienteAsync(int entryId, string estado) =>
        (await BaseDatosPrueba.EscalarAsync<long?>(api.BaseDatos, $$"""
            INSERT INTO trx.SincronizacionLaserfiche (IdDocumento, LaserficheEntryId, Campos, IdEstadoSincronizacion, IdOrigenOperacion, CreadoPor)
            OUTPUT inserted.IdSincronizacion
            SELECT d.IdDocumento, d.LaserficheEntryId, N'{"Estado":"{{estado}}"}', 1, 1, N'prueba'
            FROM trx.Documento d WHERE d.LaserficheEntryId = {{entryId}};
            """))!.Value;

    private Task<string?> EstadoSincronizacionAsync(long id) => BaseDatosPrueba.EscalarAsync<string>(api.BaseDatos,
        $"SELECT e.Codigo FROM trx.SincronizacionLaserfiche s JOIN cat.EstadoSincronizacion e ON e.IdEstadoSincronizacion = s.IdEstadoSincronizacion WHERE s.IdSincronizacion = {id}");

    [FactSqlServer]
    public async Task Si_laserfiche_falla_la_fila_queda_pendiente_con_reintento_programado_y_luego_fallida()
    {
        var cliente = await Escenarios.ClienteOperacionAsync(api);
        var doc = await Escenarios.RegistrarDocumentoAsync(api, await Escenarios.CrearExpedienteJuridicoAsync(cliente));
        var id = await InsertarPendienteAsync(doc, "Aprobado");

        await Sincronizador(new LaserficheFalso(falla: true)).ProcesarAsync([id], OrigenesOperacion.Job);

        Assert.Equal("Pendiente", await EstadoSincronizacionAsync(id));
        Assert.Equal(1, await BaseDatosPrueba.EscalarAsync<int?>(api.BaseDatos, $"SELECT Intentos FROM trx.SincronizacionLaserfiche WHERE IdSincronizacion = {id}"));
        var fila = await BaseDatosPrueba.EscalarAsync<string>(api.BaseDatos,
            $"SELECT CONCAT(CASE WHEN ProximoIntentoUtc > SYSUTCDATETIME() THEN 'futuro' ELSE 'pasado' END, '|', UltimoError) FROM trx.SincronizacionLaserfiche WHERE IdSincronizacion = {id}");
        Assert.StartsWith("futuro|", fila);
        Assert.Contains("902", fila);

        // Vencida la espera y agotados los intentos, queda Fallido.
        await BaseDatosPrueba.EscalarAsync<int?>(api.BaseDatos, $"UPDATE trx.SincronizacionLaserfiche SET ProximoIntentoUtc = DATEADD(SECOND, -1, SYSUTCDATETIME()) WHERE IdSincronizacion = {id}; SELECT 1;");
        await Sincronizador(new LaserficheFalso(falla: true), maxIntentos: 2).ProcesarAsync([id], OrigenesOperacion.Job);
        Assert.Equal("Fallido", await EstadoSincronizacionAsync(id));
    }

    [FactSqlServer]
    public async Task Por_documento_se_procesa_primero_el_cambio_mas_antiguo_y_se_traducen_los_nombres_de_campo()
    {
        var cliente = await Escenarios.ClienteOperacionAsync(api);
        var doc = await Escenarios.RegistrarDocumentoAsync(api, await Escenarios.CrearExpedienteJuridicoAsync(cliente));
        var viejo = await InsertarPendienteAsync(doc, "Rechazado");
        var nuevo = await InsertarPendienteAsync(doc, "Aprobado");
        var laserfiche = new LaserficheFalso(falla: false);

        await Sincronizador(laserfiche).ProcesarAsync([viejo, nuevo], OrigenesOperacion.Job);
        Assert.Single(laserfiche.Llamadas);
        Assert.Equal("Rechazado", laserfiche.Llamadas[0].Campos["EstadoDocumentoLF"]);

        await Sincronizador(laserfiche).ProcesarAsync([viejo, nuevo], OrigenesOperacion.Job);
        Assert.Equal("Aprobado", laserfiche.Llamadas[1].Campos["EstadoDocumentoLF"]);
        Assert.Equal("Sincronizado", await EstadoSincronizacionAsync(nuevo));
    }

    [FactSqlServer]
    public async Task Vencimiento_pasa_a_vencido_y_deja_la_actualizacion_para_laserfiche()
    {
        var cliente = await Escenarios.ClienteOperacionAsync(api);
        var doc = await Escenarios.RegistrarDocumentoAsync(api, await Escenarios.CrearExpedienteJuridicoAsync(cliente));
        await BaseDatosPrueba.EscalarAsync<int?>(api.BaseDatos, $"UPDATE trx.Documento SET FechaVencimiento = '2000-01-01' WHERE LaserficheEntryId = {doc}; SELECT 1;");

        var vencidos = await api.Services.GetRequiredService<EjecutorVencimiento>().EjecutarAsync(new DateOnly(2000, 1, 2));

        Assert.Equal(1, vencidos);
        Assert.Equal("Vencido", await BaseDatosPrueba.EscalarAsync<string>(api.BaseDatos,
            $"SELECT e.Codigo FROM trx.Documento d JOIN cat.EstadoDocumento e ON e.IdEstadoDocumento = d.IdEstadoDocumento WHERE d.LaserficheEntryId = {doc}"));
        Assert.Equal("{\"Estado\":\"Vencido\"}", await BaseDatosPrueba.EscalarAsync<string>(api.BaseDatos,
            $"SELECT TOP 1 Campos FROM trx.SincronizacionLaserfiche WHERE LaserficheEntryId = {doc} ORDER BY IdSincronizacion DESC"));
    }

    [FactSqlServer]
    public async Task El_bloqueo_distribuido_lo_tiene_una_sola_instancia_a_la_vez()
    {
        var bloqueo = api.Services.GetRequiredService<BloqueoDistribuido>();
        var recurso = $"PRUEBA_{Guid.NewGuid():N}";

        var primero = await bloqueo.IntentarAdquirirAsync(recurso);
        Assert.NotNull(primero);
        Assert.Null(await bloqueo.IntentarAdquirirAsync(recurso));

        await primero.DisposeAsync();
        await using var tercero = await bloqueo.IntentarAdquirirAsync(recurso);
        Assert.NotNull(tercero);
    }

    [FactSqlServer]
    public async Task Limpieza_borra_solo_las_llaves_de_idempotencia_vencidas()
    {
        var vencida = $"vencida-{Guid.NewGuid():N}";
        var vigente = $"vigente-{Guid.NewGuid():N}";
        await BaseDatosPrueba.EscalarAsync<int?>(api.BaseDatos, $"""
            INSERT INTO aud.IdempotenciaRequest (ClientId, IdempotencyKey, Endpoint, HashRequest, FechaExpiracionUtc)
            VALUES ('prueba', '{vencida}', 'x', REPLICATE('0', 64), DATEADD(DAY, -1, SYSUTCDATETIME())),
                   ('prueba', '{vigente}', 'x', REPLICATE('0', 64), DATEADD(DAY, 1, SYSUTCDATETIME()));
            SELECT 1;
            """);

        await api.Services.GetRequiredService<EjecutorLimpiezaIdempotencia>().EjecutarAsync();

        Assert.Equal(1, await BaseDatosPrueba.EscalarAsync<int?>(api.BaseDatos,
            $"SELECT COUNT(*) FROM aud.IdempotenciaRequest WHERE IdempotencyKey IN ('{vencida}', '{vigente}')"));
    }
}
