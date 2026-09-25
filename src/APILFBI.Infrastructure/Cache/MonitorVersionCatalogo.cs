using APILFBI.Infrastructure.Opciones;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace APILFBI.Infrastructure.Cache;

/// <summary>
/// Precarga los catálogos al arrancar y después revisa cat.VersionCatalogo cada N segundos.
/// Si la base no responde al arrancar, reintenta; mientras tanto /health/ready responde 503.
/// </summary>
internal sealed class MonitorVersionCatalogo(
    CatalogoCache cache,
    IOptions<CacheOptions> opciones,
    ILogger<MonitorVersionCatalogo> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var intervalo = TimeSpan.FromSeconds(opciones.Value.IntervaloRevisionSegundos);

        while (!cache.Precargado && !stoppingToken.IsCancellationRequested)
        {
            try
            {
                await cache.PrecargarAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogError(ex, "No se pudieron precargar los catálogos; se reintenta en {Intervalo}", intervalo);
                await Task.Delay(intervalo, stoppingToken);
            }
        }

        using var timer = new PeriodicTimer(intervalo);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await cache.ActualizarVersionesAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Se conserva la caché vigente; se vuelve a intentar en el siguiente ciclo.
                log.LogWarning(ex, "No se pudo revisar cat.VersionCatalogo");
            }
        }
    }
}
