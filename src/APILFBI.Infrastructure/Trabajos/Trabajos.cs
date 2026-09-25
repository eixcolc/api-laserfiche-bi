using System.Data;
using APILFBI.Infrastructure.Persistencia;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace APILFBI.Infrastructure.Trabajos;

public static class ProgramacionDiaria
{
    /// <summary>Próximo instante (UTC) en que la hora local <paramref name="hora"/> ocurre en la zona indicada.</summary>
    public static DateTimeOffset Siguiente(DateTimeOffset ahoraUtc, TimeOnly hora, TimeZoneInfo zona)
    {
        var ahoraLocal = TimeZoneInfo.ConvertTime(ahoraUtc, zona);
        var candidata = ahoraLocal.Date.Add(hora.ToTimeSpan());
        if (candidata <= ahoraLocal.DateTime) candidata = candidata.AddDays(1);
        return new DateTimeOffset(candidata, zona.GetUtcOffset(candidata)).ToUniversalTime();
    }

    public static DateOnly FechaLocal(DateTimeOffset ahoraUtc, TimeZoneInfo zona) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(ahoraUtc, zona).DateTime);
}

/// <summary>
/// Bloqueo distribuido con sp_getapplock (dueño: la sesión). Garantiza que un job diario corra una
/// sola vez aunque haya varias instancias del Worker. El bloqueo dura mientras la conexión siga abierta.
/// </summary>
public sealed class BloqueoDistribuido(IDbContextFactory<BilfDbContext> dbFactory)
{
    /// <summary>Devuelve el bloqueo o null si otra instancia lo tiene (no espera).</summary>
    public async Task<IAsyncDisposable?> IntentarAdquirirAsync(string recurso, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var cn = new SqlConnection(db.Database.GetConnectionString());
        await cn.OpenAsync(ct);

        await using var cmd = new SqlCommand("sys.sp_getapplock", cn) { CommandType = CommandType.StoredProcedure };
        cmd.Parameters.AddWithValue("@Resource", recurso);
        cmd.Parameters.AddWithValue("@LockMode", "Exclusive");
        cmd.Parameters.AddWithValue("@LockOwner", "Session");
        cmd.Parameters.AddWithValue("@LockTimeout", 0);
        var resultado = cmd.Parameters.Add(new SqlParameter("@Resultado", SqlDbType.Int) { Direction = ParameterDirection.ReturnValue });
        await cmd.ExecuteNonQueryAsync(ct);

        if ((int)resultado.Value >= 0) return new Bloqueo(cn, recurso);

        await cn.DisposeAsync();
        return null;
    }

    private sealed class Bloqueo(SqlConnection cn, string recurso) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            try
            {
                await using var cmd = new SqlCommand("sys.sp_releaseapplock", cn) { CommandType = CommandType.StoredProcedure };
                cmd.Parameters.AddWithValue("@Resource", recurso);
                cmd.Parameters.AddWithValue("@LockOwner", "Session");
                await cmd.ExecuteNonQueryAsync();
            }
            finally
            {
                await cn.DisposeAsync();   // cerrar la sesión también libera el bloqueo
            }
        }
    }
}

/// <summary>Pasa a Vencido los documentos con fecha de vencimiento anterior al corte (trx.usp_VencerDocumentos, por lotes).</summary>
public sealed class EjecutorVencimiento(IDbContextFactory<BilfDbContext> dbFactory, ILogger<EjecutorVencimiento> log)
{
    public async Task<int> EjecutarAsync(DateOnly fechaCorte, int lote = 500, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await using var cn = new SqlConnection(db.Database.GetConnectionString());
        await cn.OpenAsync(ct);

        var total = 0;
        while (true)
        {
            await using var cmd = new SqlCommand("trx.usp_VencerDocumentos", cn) { CommandType = CommandType.StoredProcedure, CommandTimeout = 300 };
            cmd.Parameters.Add(new SqlParameter("@FechaCorte", SqlDbType.Date) { Value = fechaCorte.ToDateTime(TimeOnly.MinValue) });
            cmd.Parameters.Add(new SqlParameter("@Lote", SqlDbType.Int) { Value = lote });

            var vencidos = 0;
            await using (var r = await cmd.ExecuteReaderAsync(ct))
                while (await r.ReadAsync(ct)) vencidos++;

            total += vencidos;
            if (vencidos < lote) break;
        }

        log.LogInformation("Vencimiento con corte {FechaCorte}: {Total} documentos", fechaCorte, total);
        return total;
    }
}

/// <summary>Borra las llaves de idempotencia vencidas (aud.IdempotenciaRequest).</summary>
public sealed class EjecutorLimpiezaIdempotencia(IDbContextFactory<BilfDbContext> dbFactory, ILogger<EjecutorLimpiezaIdempotencia> log)
{
    public async Task<int> EjecutarAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var total = 0;
        int borradas;
        do
        {
            borradas = await db.Database.ExecuteSqlAsync(
                $"DELETE TOP (5000) FROM aud.IdempotenciaRequest WHERE FechaExpiracionUtc < SYSUTCDATETIME()", ct);
            total += borradas;
        } while (borradas == 5000);

        log.LogInformation("Llaves de idempotencia vencidas borradas: {Total}", total);
        return total;
    }
}
