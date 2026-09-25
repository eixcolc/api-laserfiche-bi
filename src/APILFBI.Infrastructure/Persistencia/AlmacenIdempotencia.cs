using APILFBI.Application.Abstracciones;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace APILFBI.Infrastructure.Persistencia;

/// <summary>
/// aud.IdempotenciaRequest. La reserva es un INSERT: la llave primaria (ClientId, IdempotencyKey)
/// garantiza que solo una instancia del balanceador ejecute la operación.
/// </summary>
internal sealed class AlmacenIdempotencia(IDbContextFactory<BilfDbContext> dbFactory, TimeProvider tiempo) : IAlmacenIdempotencia
{
    public static readonly TimeSpan Vigencia = TimeSpan.FromHours(24);

    public async Task<ResultadoReserva> ReservarAsync(string clientId, string llave, string endpoint, string hashSolicitud, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var ahora = tiempo.GetUtcNow().UtcDateTime;

        for (var intento = 0; intento < 2; intento++)
        {
            try
            {
                await db.Database.ExecuteSqlAsync($"""
                    INSERT INTO aud.IdempotenciaRequest (ClientId, IdempotencyKey, Endpoint, HashRequest, FechaCreacionUtc, FechaExpiracionUtc)
                    VALUES ({clientId}, {llave}, {endpoint}, {hashSolicitud}, {ahora}, {ahora.Add(Vigencia)})
                    """, ct);
                return new ResultadoReserva(EstadoReserva.Reservada);
            }
            catch (SqlException ex) when (ex.Number is 2627 or 2601)
            {
                var existente = await db.Database.SqlQuery<Registro>($"""
                    SELECT Endpoint, HashRequest, HttpStatus, RespuestaJson, FechaExpiracionUtc
                    FROM aud.IdempotenciaRequest WHERE ClientId = {clientId} AND IdempotencyKey = {llave}
                    """).FirstOrDefaultAsync(ct);

                if (existente is null) continue;   // se borró entre el INSERT y el SELECT: reintentar

                if (existente.FechaExpiracionUtc <= ahora)
                {
                    await LiberarAsync(clientId, llave, ct);
                    continue;
                }

                if (existente.HashRequest != hashSolicitud || existente.Endpoint != endpoint)
                    return new ResultadoReserva(EstadoReserva.ContenidoDistinto);

                return existente.HttpStatus is null
                    ? new ResultadoReserva(EstadoReserva.EnProceso)
                    : new ResultadoReserva(EstadoReserva.Completada, existente.HttpStatus, existente.RespuestaJson);
            }
        }

        return new ResultadoReserva(EstadoReserva.EnProceso);
    }

    public async Task CompletarAsync(string clientId, string llave, int httpStatus, string respuestaJson, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await db.Database.ExecuteSqlAsync($"""
            UPDATE aud.IdempotenciaRequest SET HttpStatus = {(short)httpStatus}, RespuestaJson = {respuestaJson}
            WHERE ClientId = {clientId} AND IdempotencyKey = {llave}
            """, ct);
    }

    public async Task LiberarAsync(string clientId, string llave, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await db.Database.ExecuteSqlAsync(
            $"DELETE FROM aud.IdempotenciaRequest WHERE ClientId = {clientId} AND IdempotencyKey = {llave}", ct);
    }

    private sealed class Registro
    {
        public string Endpoint { get; set; } = string.Empty;
        public string HashRequest { get; set; } = string.Empty;
        public short? HttpStatus { get; set; }
        public string? RespuestaJson { get; set; }
        public DateTime FechaExpiracionUtc { get; set; }
    }
}
