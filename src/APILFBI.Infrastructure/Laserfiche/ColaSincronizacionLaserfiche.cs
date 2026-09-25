using System.Threading.Channels;
using APILFBI.Application.Comun;
using APILFBI.Application.Estados;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace APILFBI.Infrastructure.Laserfiche;

/// <summary>
/// Cola en memoria de la API: actualiza Laserfiche en segundo plano apenas se confirma el cambio de
/// estado, sin hacer esperar al CRM. Si la instancia se apaga o Laserfiche falla, las filas siguen
/// pendientes en trx.SincronizacionLaserfiche y el Worker las retoma.
/// </summary>
internal sealed class ColaSincronizacionLaserfiche(ILogger<ColaSincronizacionLaserfiche> log) : IColaSincronizacionLaserfiche
{
    private readonly Channel<long[]> _canal = Channel.CreateBounded<long[]>(
        new BoundedChannelOptions(1_000) { FullMode = BoundedChannelFullMode.DropWrite, SingleReader = true });

    internal ChannelReader<long[]> Lector => _canal.Reader;

    public void Encolar(IReadOnlyCollection<long> idsSincronizacion)
    {
        if (!_canal.Writer.TryWrite([.. idsSincronizacion]))
            log.LogWarning("Cola de sincronización llena; el Worker procesará {Cantidad} actualizaciones", idsSincronizacion.Count);
    }
}

internal sealed class ProcesadorColaSincronizacion(
    ColaSincronizacionLaserfiche cola,
    IServiceScopeFactory scopes,
    ILogger<ProcesadorColaSincronizacion> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var ids in cola.Lector.ReadAllAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<SincronizadorLaserfiche>().ProcesarAsync(ids, OrigenesOperacion.Api, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogError(ex, "Error procesando la sincronización con Laserfiche; el Worker la reintentará");
            }
        }
    }
}
