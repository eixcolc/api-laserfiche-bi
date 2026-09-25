using APILFBI.Application.Abstracciones;
using APILFBI.Application.Comun;
using APILFBI.Domain.Entidades;
using APILFBI.Infrastructure.Opciones;
using APILFBI.Infrastructure.Persistencia;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace APILFBI.Infrastructure.Auditoria;

/// <summary>
/// Escribe la bitácora en lotes: cuando junta <see cref="BitacoraOptions.TamanoLote"/> entradas o
/// cada <see cref="BitacoraOptions.IntervaloVaciadoMs"/>. Al apagar la instancia vacía lo pendiente.
/// </summary>
internal sealed class EscritorBitacora(
    BitacoraService servicio,
    ICatalogoCache catalogos,
    IDbContextFactory<BilfDbContext> dbFactory,
    IOptions<BitacoraOptions> opciones,
    ILogger<EscritorBitacora> log) : BackgroundService
{
    private static readonly string Instancia = Environment.MachineName;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var lote = new List<EntradaBitacora>(opciones.Value.TamanoLote);
        var intervalo = TimeSpan.FromMilliseconds(opciones.Value.IntervaloVaciadoMs);

        try
        {
            while (await servicio.Lector.WaitToReadAsync(stoppingToken))
            {
                using var ventana = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                ventana.CancelAfter(intervalo);
                try
                {
                    while (lote.Count < opciones.Value.TamanoLote)
                    {
                        if (servicio.Lector.TryRead(out var entrada)) lote.Add(entrada);
                        else await servicio.Lector.WaitToReadAsync(ventana.Token);
                    }
                }
                catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
                {
                    // Venció la ventana: se escribe lo que haya.
                }

                await EscribirAsync(lote, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }

        // Apagado: vaciar lo pendiente sin cancelar.
        while (servicio.Lector.TryRead(out var pendiente)) lote.Add(pendiente);
        await EscribirAsync(lote, CancellationToken.None);
    }

    private async Task EscribirAsync(List<EntradaBitacora> lote, CancellationToken ct)
    {
        if (lote.Count == 0) return;
        try
        {
            var tipos = (await catalogos.ObtenerAsync<TipoOperacion>(ct)).ToDictionary(x => x.Codigo, x => x.Id);
            var origenes = (await catalogos.ObtenerAsync<OrigenOperacion>(ct)).ToDictionary(x => x.Codigo, x => x.Id);
            var resultados = (await catalogos.ObtenerAsync<ResultadoOperacion>(ct)).ToDictionary(x => x.Codigo, x => x.Id);

            await using var db = await dbFactory.CreateDbContextAsync(ct);
            foreach (var e in lote)
            {
                if (!tipos.TryGetValue(e.TipoOperacion, out var idTipo) || !origenes.TryGetValue(e.Origen, out var idOrigen))
                {
                    log.LogError("Entrada de bitácora con catálogo inválido: {TipoOperacion}/{Origen}", e.TipoOperacion, e.Origen);
                    continue;
                }

                var http = catalogos.ObtenerCodigoRespuesta(e.CodigoRespuesta)?.HttpStatus
                           ?? (CodigosRespuesta.PorDefecto.TryGetValue(e.CodigoRespuesta, out var d) ? d.Http : 500);

                db.Bitacora.Add(new Bitacora
                {
                    // Forma parte de la llave primaria: se trunca a milisegundos, igual que datetime2(3).
                    FechaHoraUtc = new DateTime(e.FechaHoraUtc.Ticks - e.FechaHoraUtc.Ticks % TimeSpan.TicksPerMillisecond, DateTimeKind.Utc),
                    IdTipoOperacion = idTipo,
                    IdOrigenOperacion = idOrigen,
                    IdResultadoOperacion = resultados[ResultadosOperacion.DesdeHttp(e.CodigoRespuesta, http)],
                    CodigoRespuesta = e.CodigoRespuesta,
                    IdExpediente = e.IdExpediente,
                    IdDocumento = e.IdDocumento,
                    Correlativo = e.Correlativo,
                    UsuarioServicio = Recortar(e.UsuarioServicio, 100)!,
                    UsuarioOperacion = Recortar(e.UsuarioOperacion, 100),
                    IpOrigen = Recortar(e.IpOrigen, 45),
                    Instancia = Instancia,
                    CorrelationId = Recortar(e.CorrelationId, 64),
                    Endpoint = Recortar(e.Endpoint, 300),
                    MetodoHttp = e.MetodoHttp,
                    DuracionMs = e.DuracionMs,
                    DatosAnteriores = e.DatosAnteriores,
                    DatosNuevos = e.DatosNuevos,
                    Detalle = Recortar(e.Detalle, 2000),
                });
            }

            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            log.LogError(ex, "No se pudieron escribir {Cantidad} entradas de bitácora", lote.Count);
        }
        finally
        {
            lote.Clear();
        }
    }

    private static string? Recortar(string? valor, int largo) =>
        valor is null || valor.Length <= largo ? valor : valor[..largo];
}
