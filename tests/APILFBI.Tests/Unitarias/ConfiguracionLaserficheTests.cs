using APILFBI.Application.Abstracciones;
using APILFBI.Infrastructure;
using APILFBI.Infrastructure.Laserfiche;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;

namespace APILFBI.Tests.Unitarias;

public sealed class ConfiguracionLaserficheTests
{
    private static ServiceProvider Construir(Dictionary<string, string?> valores)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Bilf"] = "Server=.;Database=X;Trusted_Connection=True",
            ["Auth:LlaveFirma"] = Convert.ToBase64String(new byte[32].Select((_, i) => (byte)(i + 1)).ToArray()),
        }.Concat(valores).ToDictionary()).Build();

        var services = new ServiceCollection().AddLogging();
        services.AddInfraestructura(config);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void En_modo_real_se_registra_el_cliente_de_la_repository_api_con_resiliencia_valida()
    {
        using var sp = Construir(new()
        {
            ["Laserfiche:Simulado"] = "false",
            ["Laserfiche:UrlBase"] = "https://servidor-lf/LFRepositoryAPI",
            ["Laserfiche:RepositorioId"] = "REPO",
            ["Laserfiche:Usuario"] = "svc",
            ["Laserfiche:Contrasena"] = "secreto",
        });

        Assert.IsType<ClienteLaserficheApi>(sp.GetRequiredService<ILaserficheRepositoryService>());
        var resiliencia = sp.GetRequiredService<IOptionsMonitor<HttpStandardResilienceOptions>>().Get($"{nameof(ILaserficheRepositoryService)}-standard");
        Assert.Equal(TimeSpan.FromSeconds(60), resiliencia.AttemptTimeout.Timeout);
        Assert.Equal(3, resiliencia.Retry.MaxRetryAttempts);
    }

    [Fact]
    public void En_modo_real_sin_credenciales_la_configuracion_se_rechaza_al_arrancar()
    {
        using var sp = Construir(new() { ["Laserfiche:Simulado"] = "false", ["Laserfiche:UrlBase"] = "https://servidor-lf/" });

        Assert.Throws<OptionsValidationException>(() => sp.GetRequiredService<IOptions<LaserficheOptions>>().Value);
    }

    [Fact]
    public void Por_defecto_se_usa_laserfiche_simulado()
    {
        using var sp = Construir([]);

        Assert.IsType<LaserficheSimulado>(sp.GetRequiredService<ILaserficheRepositoryService>());
    }
}
