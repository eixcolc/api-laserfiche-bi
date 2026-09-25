using APILFBI.Application.Abstracciones;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace APILFBI.Infrastructure.Salud;

/// <summary>La instancia no recibe tráfico del balanceador hasta terminar de precargar los catálogos.</summary>
internal sealed class CatalogosHealthCheck(ICatalogoCache cache) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default) =>
        Task.FromResult(cache.Precargado
            ? HealthCheckResult.Healthy("Catálogos precargados")
            : HealthCheckResult.Unhealthy("Catálogos aún no precargados"));
}
