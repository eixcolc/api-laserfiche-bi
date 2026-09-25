using APILFBI.Application.Catalogos;
using APILFBI.Application.Expedientes;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace APILFBI.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddAplicacion(this IServiceCollection services)
    {
        services.AddValidatorsFromAssemblyContaining<SolicitudExpedienteValidator>(ServiceLifetime.Singleton);
        services.AddScoped<CatalogosService>();
        services.AddScoped<ExpedientesService>();
        services.AddScoped<Documentos.DocumentosService>();
        services.AddScoped<Estados.EstadosService>();
        services.AddScoped<Cargas.CargasService>();
        return services;
    }
}
