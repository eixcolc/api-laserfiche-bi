using APILFBI.Application.Abstracciones;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace APILFBI.Infrastructure.Salud;

/// <summary>
/// Si Laserfiche no responde, la instancia queda "Degraded" (sigue respondiendo 200): catálogos,
/// expedientes y consultas funcionan sin Laserfiche, y sacar a todas las instancias del balanceador
/// por una falla de Laserfiche dejaría la API completa fuera de servicio.
/// </summary>
internal sealed class LaserficheHealthCheck(ILaserficheRepositoryService laserfiche) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        var (disponible, detalle) = await laserfiche.VerificarAsync(ct);
        return disponible ? HealthCheckResult.Healthy(detalle) : new HealthCheckResult(context.Registration.FailureStatus, detalle);
    }
}
