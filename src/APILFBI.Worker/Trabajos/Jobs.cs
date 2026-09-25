using APILFBI.Infrastructure.Laserfiche;
using APILFBI.Infrastructure.Trabajos;
using Microsoft.Extensions.Options;

namespace APILFBI.Worker.Trabajos;

/// <summary>
/// Job diario a una hora local. Antes de ejecutar toma un bloqueo distribuido en SQL Server: si hay
/// varias instancias del Worker, solo una lo ejecuta.
/// </summary>
internal abstract class JobDiario(BloqueoDistribuido bloqueo, IOptions<WorkerOptions> opciones, TimeProvider tiempo, ILogger log) : BackgroundService
{
    protected abstract string Nombre { get; }
    protected abstract string Hora { get; }
    protected virtual bool EjecutarAlIniciar => false;
    protected abstract Task EjecutarAsync(DateOnly fechaLocal, CancellationToken ct);

    protected WorkerOptions Opciones => opciones.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var zona = opciones.Value.Zona();
        var hora = TimeOnly.ParseExact(Hora, "HH:mm");

        if (EjecutarAlIniciar) await IntentarAsync(zona, stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            var siguiente = ProgramacionDiaria.Siguiente(tiempo.GetUtcNow(), hora, zona);
            log.LogInformation("{Job}: próxima ejecución {Siguiente:u}", Nombre, siguiente);
            await Task.Delay(siguiente - tiempo.GetUtcNow(), tiempo, stoppingToken);
            await IntentarAsync(zona, stoppingToken);
        }
    }

    private async Task IntentarAsync(TimeZoneInfo zona, CancellationToken ct)
    {
        try
        {
            await using var candado = await bloqueo.IntentarAdquirirAsync($"BILF_JOB_{Nombre}", ct);
            if (candado is null)
            {
                log.LogInformation("{Job}: otra instancia lo está ejecutando", Nombre);
                return;
            }
            await EjecutarAsync(ProgramacionDiaria.FechaLocal(tiempo.GetUtcNow(), zona), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogError(ex, "{Job}: falló; se reintentará en la próxima ejecución", Nombre);
        }
    }
}

/// <summary>Pasa a Vencido los documentos cuya fecha de vencimiento ya pasó (el outbox actualiza Laserfiche).</summary>
internal sealed class JobVencimiento(EjecutorVencimiento ejecutor, BloqueoDistribuido bloqueo, IOptions<WorkerOptions> opciones,
    TimeProvider tiempo, ILogger<JobVencimiento> log) : JobDiario(bloqueo, opciones, tiempo, log)
{
    protected override string Nombre => "Vencimiento";
    protected override string Hora => Opciones.HoraVencimiento;
    protected override bool EjecutarAlIniciar => Opciones.EjecutarVencimientoAlIniciar;
    protected override Task EjecutarAsync(DateOnly fechaLocal, CancellationToken ct) => ejecutor.EjecutarAsync(fechaLocal, ct: ct);
}

/// <summary>Borra las llaves de idempotencia vencidas.</summary>
internal sealed class JobLimpiezaIdempotencia(EjecutorLimpiezaIdempotencia ejecutor, BloqueoDistribuido bloqueo, IOptions<WorkerOptions> opciones,
    TimeProvider tiempo, ILogger<JobLimpiezaIdempotencia> log) : JobDiario(bloqueo, opciones, tiempo, log)
{
    protected override string Nombre => "LimpiezaIdempotencia";
    protected override string Hora => Opciones.HoraLimpieza;
    protected override Task EjecutarAsync(DateOnly fechaLocal, CancellationToken ct) => ejecutor.EjecutarAsync(ct);
}

/// <summary>
/// Reintenta las actualizaciones pendientes hacia Laserfiche. No necesita bloqueo: cada fila se
/// reserva en SQL, así que varias instancias pueden correrlo a la vez.
/// </summary>
internal sealed class JobSincronizacionLaserfiche(IServiceScopeFactory scopes, IOptions<WorkerOptions> opciones,
    IOptions<SincronizacionOptions> sincronizacion, ILogger<JobSincronizacionLaserfiche> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(opciones.Value.IntervaloSincronizacionSegundos));
        do
        {
            try
            {
                int procesadas;
                do
                {
                    await using var scope = scopes.CreateAsyncScope();
                    procesadas = await scope.ServiceProvider.GetRequiredService<SincronizadorLaserfiche>().ProcesarPendientesAsync(stoppingToken);
                    if (procesadas > 0) log.LogInformation("Sincronización con Laserfiche: {Procesadas} actualizaciones procesadas", procesadas);
                } while (procesadas >= sincronizacion.Value.Lote && !stoppingToken.IsCancellationRequested);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogError(ex, "Error en la sincronización con Laserfiche; se reintenta en el siguiente ciclo");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
