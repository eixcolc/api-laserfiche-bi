using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Text.Json;
using APILFBI.Application.Abstracciones;
using APILFBI.Application.Comun;
using APILFBI.Infrastructure.Persistencia;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace APILFBI.Infrastructure.Laserfiche;

public sealed class SincronizacionOptions
{
    public const string Seccion = "SincronizacionLaserfiche";

    /// <summary>Filas que toma cada ciclo.</summary>
    [Range(1, 1000)] public int Lote { get; set; } = 50;

    /// <summary>Reintentos antes de marcar la fila como Fallido (requiere revisión manual).</summary>
    [Range(1, 100)] public int MaxIntentos { get; set; } = 10;

    /// <summary>Espera antes del primer reintento; se duplica en cada intento hasta <see cref="EsperaMaximaMinutos"/>.</summary>
    [Range(1, 3600)] public int EsperaBaseSegundos { get; set; } = 30;
    [Range(1, 1440)] public int EsperaMaximaMinutos { get; set; } = 60;

    /// <summary>Tiempo que una fila queda reservada por la instancia que la procesa.</summary>
    [Range(30, 3600)] public int ReservaSegundos { get; set; } = 300;
}

/// <summary>
/// Procesa el outbox trx.SincronizacionLaserfiche. Varias instancias (API y Worker) pueden correr a
/// la vez: cada fila se reserva con UPDLOCK/READPAST. Por documento se procesa solo la fila pendiente
/// más antigua, para que un estado viejo nunca pise a uno nuevo en Laserfiche.
/// </summary>
public sealed class SincronizadorLaserfiche(
    IDbContextFactory<BilfDbContext> dbFactory,
    ILaserficheRepositoryService laserfiche,
    IOptions<LaserficheOptions> opcionesLaserfiche,
    IOptions<SincronizacionOptions> opciones,
    IBitacoraService bitacora,
    TimeProvider tiempo,
    ILogger<SincronizadorLaserfiche> log)
{
    private sealed record Fila(long IdSincronizacion, long IdDocumento, int LaserficheEntryId, string Campos, int Intentos);

    /// <summary>Procesa filas específicas (la API, justo después del cambio de estado).</summary>
    public Task<int> ProcesarAsync(IReadOnlyCollection<long> idsSincronizacion, string origen, CancellationToken ct = default) =>
        ProcesarInternoAsync(idsSincronizacion, origen, ct);

    /// <summary>Procesa las filas pendientes que ya están listas para reintentarse (Worker).</summary>
    public Task<int> ProcesarPendientesAsync(CancellationToken ct = default) =>
        ProcesarInternoAsync(null, OrigenesOperacion.Job, ct);

    private async Task<int> ProcesarInternoAsync(IReadOnlyCollection<long>? ids, string origen, CancellationToken ct)
    {
        var filas = await ReservarAsync(ids, ct);
        foreach (var fila in filas)
        {
            try
            {
                await laserfiche.ActualizarCamposAsync(fila.LaserficheEntryId, Traducir(fila.Campos), ct);
                await MarcarAsync(fila.IdSincronizacion, "Sincronizado", fila.Intentos + 1, null, null, CancellationToken.None);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                await RegistrarFalloAsync(fila, ex, origen);
            }
        }
        return filas.Count;
    }

    private async Task RegistrarFalloAsync(Fila fila, Exception ex, string origen)
    {
        var intentos = fila.Intentos + 1;
        var error = ex is ApiException api ? $"[{api.Codigo}] {api.Detalle ?? api.Message}" : ex.Message;

        if (intentos >= opciones.Value.MaxIntentos)
        {
            log.LogError(ex, "Sincronización con Laserfiche fallida definitivamente: entrada {EntryId} tras {Intentos} intentos", fila.LaserficheEntryId, intentos);
            await MarcarAsync(fila.IdSincronizacion, "Fallido", intentos, null, error, CancellationToken.None);
            bitacora.Encolar(new EntradaBitacora
            {
                TipoOperacion = "SincronizacionLaserfiche",
                Origen = origen,
                CodigoRespuesta = ex is ApiException a ? a.Codigo : CodigosRespuesta.ErrorLaserfiche,
                UsuarioServicio = "SISTEMA",
                IdDocumento = fila.IdDocumento,
                DatosNuevos = fila.Campos,
                Detalle = $"LaserficheEntryId: {fila.LaserficheEntryId}. Sin sincronizar tras {intentos} intentos: {error}",
            });
            return;
        }

        var o = opciones.Value;
        var espera = TimeSpan.FromSeconds(Math.Min(o.EsperaBaseSegundos * Math.Pow(2, intentos - 1), o.EsperaMaximaMinutos * 60));
        log.LogWarning("No se pudo actualizar Laserfiche (entrada {EntryId}, intento {Intentos}); se reintenta en {Espera}: {Error}",
            fila.LaserficheEntryId, intentos, espera, error);
        await MarcarAsync(fila.IdSincronizacion, "Pendiente", intentos, tiempo.GetUtcNow().UtcDateTime.Add(espera), error, CancellationToken.None);
    }

    /// <summary>Traduce las llaves lógicas del outbox a los nombres de campo configurados en Laserfiche.</summary>
    internal Dictionary<string, string?> Traducir(string camposJson)
    {
        var c = opcionesLaserfiche.Value.Campos;
        var nombres = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Estado"] = c.Estado,
            ["EjecutivoAsignado"] = c.EjecutivoAsignado,
            ["TipoRechazo"] = c.TipoRechazo,
            ["ComentarioRevision"] = c.ComentarioRevision,
            ["EtapaRechazo"] = c.EtapaRechazo,
        };

        using var json = JsonDocument.Parse(camposJson);
        return json.RootElement.EnumerateObject().ToDictionary(
            p => nombres.GetValueOrDefault(p.Name, p.Name),
            p => p.Value.ValueKind == JsonValueKind.Null ? null : p.Value.ToString());
    }

    private async Task<List<Fila>> ReservarAsync(IReadOnlyCollection<long>? ids, CancellationToken ct)
    {
        const string sql = """
            DECLARE @Pendiente int = (SELECT IdEstadoSincronizacion FROM cat.EstadoSincronizacion WHERE Codigo = 'Pendiente');
            UPDATE TOP (@Lote) s
            SET ProximoIntentoUtc = DATEADD(SECOND, @Reserva, SYSUTCDATETIME()), FechaModificacion = SYSUTCDATETIME()
            OUTPUT inserted.IdSincronizacion, inserted.IdDocumento, inserted.LaserficheEntryId, inserted.Campos, inserted.Intentos
            FROM trx.SincronizacionLaserfiche s WITH (UPDLOCK, READPAST, ROWLOCK)
            WHERE s.IdEstadoSincronizacion = @Pendiente
              -- datetime2(3) redondea al milisegundo más cercano (a veces hacia arriba): sin esta tolerancia,
              -- una fila recién insertada puede no ser elegible durante ese mismo milisegundo.
              AND s.ProximoIntentoUtc <= DATEADD(MILLISECOND, 10, SYSUTCDATETIME())
              AND (@Ids IS NULL OR s.IdSincronizacion IN (SELECT CAST(value AS bigint) FROM STRING_SPLIT(@Ids, ',')))
              AND NOT EXISTS (SELECT 1 FROM trx.SincronizacionLaserfiche anterior
                              WHERE anterior.IdDocumento = s.IdDocumento
                                AND anterior.IdEstadoSincronizacion = @Pendiente
                                AND anterior.IdSincronizacion < s.IdSincronizacion);
            """;

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await using var cn = new SqlConnection(db.Database.GetConnectionString());
        await cn.OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.Add(new SqlParameter("@Lote", SqlDbType.Int) { Value = opciones.Value.Lote });
        cmd.Parameters.Add(new SqlParameter("@Reserva", SqlDbType.Int) { Value = opciones.Value.ReservaSegundos });
        cmd.Parameters.Add(new SqlParameter("@Ids", SqlDbType.VarChar, -1) { Value = ids is null ? DBNull.Value : string.Join(',', ids) });

        var filas = new List<Fila>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            filas.Add(new Fila(r.GetInt64(0), r.GetInt64(1), r.GetInt32(2), r.GetString(3), r.GetInt32(4)));
        return filas;
    }

    private async Task MarcarAsync(long id, string estado, int intentos, DateTime? proximoIntento, string? error, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await db.Database.ExecuteSqlAsync($"""
            UPDATE trx.SincronizacionLaserfiche
            SET IdEstadoSincronizacion = (SELECT IdEstadoSincronizacion FROM cat.EstadoSincronizacion WHERE Codigo = {estado}),
                Intentos = {intentos},
                ProximoIntentoUtc = COALESCE({proximoIntento}, ProximoIntentoUtc),
                UltimoError = {(error is null ? null : error.Length > 1000 ? error[..1000] : error)},
                FechaModificacion = SYSUTCDATETIME()
            WHERE IdSincronizacion = {id}
            """, ct);
    }
}
