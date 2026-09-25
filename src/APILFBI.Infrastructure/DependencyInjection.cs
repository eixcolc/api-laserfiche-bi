using System.Security.Cryptography.X509Certificates;
using APILFBI.Application.Abstracciones;
using APILFBI.Application.Documentos;
using APILFBI.Application.Estados;
using APILFBI.Application.Expedientes;
using APILFBI.Infrastructure.Trabajos;
using APILFBI.Infrastructure.Laserfiche;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Http.Resilience;
using APILFBI.Infrastructure.Auditoria;
using APILFBI.Infrastructure.Cache;
using APILFBI.Infrastructure.Opciones;
using APILFBI.Infrastructure.Persistencia;
using APILFBI.Infrastructure.Salud;
using APILFBI.Infrastructure.Seguridad;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace APILFBI.Infrastructure;

public static class DependencyInjection
{
    public const string TagReady = "ready";

    /// <param name="incluirSeguridad">
    /// false para el Worker: no emite tokens ni usa Data Protection, así que no necesita Auth:LlaveFirma.
    /// </param>
    public static IServiceCollection AddInfraestructura(this IServiceCollection services, IConfiguration config, bool incluirSeguridad = true)
    {
        services.AddOptions<CacheOptions>().Bind(config.GetSection(CacheOptions.Seccion)).ValidateDataAnnotations().ValidateOnStart();
        services.AddOptions<BitacoraOptions>().Bind(config.GetSection(BitacoraOptions.Seccion)).ValidateDataAnnotations().ValidateOnStart();

        var cadena = config.GetConnectionString("Bilf")
                     ?? throw new InvalidOperationException("Falta la cadena de conexión ConnectionStrings:Bilf.");

        void Configurar(DbContextOptionsBuilder o) =>
            o.UseSqlServer(cadena, sql => sql.EnableRetryOnFailure(3))
             .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);

        services.AddDbContext<BilfDbContext>(Configurar, ServiceLifetime.Scoped, ServiceLifetime.Singleton);
        services.AddDbContextFactory<BilfDbContext>(Configurar);

        services.AddSingleton(TimeProvider.System);

        // Caché de catálogos en memoria (HybridCache sin nivel distribuido).
        services.AddHybridCache();
        services.AddSingleton(CatalogoRegistro.Predeterminado());
        services.AddSingleton<CatalogoCache>();
        services.AddSingleton<ICatalogoCache>(sp => sp.GetRequiredService<CatalogoCache>());
        services.AddHostedService<MonitorVersionCatalogo>();

        // Bitácora
        services.AddSingleton<BitacoraService>();
        services.AddSingleton<IBitacoraService>(sp => sp.GetRequiredService<BitacoraService>());
        services.AddHostedService<EscritorBitacora>();

        // Repositorios
        services.AddScoped<IRepositorioExpedientes, RepositorioExpedientes>();
        services.AddScoped<IRepositorioDocumentos, RepositorioDocumentos>();
        services.AddScoped<IRepositorioEstados, RepositorioEstados>();
        services.AddSingleton<IAlmacenIdempotencia, AlmacenIdempotencia>();

        if (incluirSeguridad)
            AgregarSeguridad(services, config);

        AgregarLaserfiche(services, config);

        // Outbox hacia Laserfiche: la API lo procesa al instante; el Worker reintenta lo pendiente.
        services.AddOptions<SincronizacionOptions>().Bind(config.GetSection(SincronizacionOptions.Seccion)).ValidateDataAnnotations().ValidateOnStart();
        services.AddTransient<SincronizadorLaserfiche>();
        services.AddSingleton<ColaSincronizacionLaserfiche>();
        services.AddSingleton<IColaSincronizacionLaserfiche>(sp => sp.GetRequiredService<ColaSincronizacionLaserfiche>());
        services.AddHostedService<ProcesadorColaSincronizacion>();

        // Trabajos programados (los usa el Worker)
        services.AddSingleton<BloqueoDistribuido>();
        services.AddSingleton<EjecutorVencimiento>();
        services.AddSingleton<EjecutorLimpiezaIdempotencia>();

        services.AddHealthChecks()
            .AddDbContextCheck<BilfDbContext>("sqlserver", tags: [TagReady])
            .AddCheck<CatalogosHealthCheck>("catalogos", tags: [TagReady])
            .AddCheck<LaserficheHealthCheck>("laserfiche", failureStatus: HealthStatus.Degraded, tags: [TagReady]);

        return services;
    }

    private static void AgregarSeguridad(IServiceCollection services, IConfiguration config)
    {
        services.AddOptions<AuthOptions>().Bind(config.GetSection(AuthOptions.Seccion))
            .ValidateDataAnnotations()
            .Validate(o => { try { o.ObtenerLlave(); return true; } catch { return false; } },
                      "Auth:LlaveFirma debe ser Base64 de al menos 32 bytes.")
            .ValidateOnStart();

        services.AddSingleton<EmisorToken>();
        services.AddScoped<IAutenticacionService, AutenticacionService>();
        services.AddScoped<CreadorCuentaServicio>();

        // Llaves de Data Protection compartidas entre instancias (seg.DataProtectionKeys).
        // En producción se cifran con un certificado del almacén de Windows (DataProtection:CertificadoThumbprint).
        var dataProtection = services.AddDataProtection().SetApplicationName("APILFBI").PersistKeysToDbContext<BilfDbContext>();
        var thumbprint = config["DataProtection:CertificadoThumbprint"];
        if (!string.IsNullOrWhiteSpace(thumbprint))
            dataProtection.ProtectKeysWithCertificate(BuscarCertificado(thumbprint));
    }

    private static void AgregarLaserfiche(IServiceCollection services, IConfiguration config)
    {
        var seccion = config.GetSection(LaserficheOptions.Seccion);
        services.AddOptions<LaserficheOptions>().Bind(seccion)
            .ValidateDataAnnotations()
            .Validate(o => o.Simulado
                           || (Uri.TryCreate(o.UrlBase, UriKind.Absolute, out _)
                               && !string.IsNullOrWhiteSpace(o.RepositorioId)
                               && !string.IsNullOrWhiteSpace(o.Usuario)
                               && !string.IsNullOrWhiteSpace(o.Contrasena)),
                      "Con Laserfiche:Simulado = false hay que configurar UrlBase, RepositorioId, Usuario y Contrasena.")
            .ValidateOnStart();

        var lf = seccion.Get<LaserficheOptions>() ?? new LaserficheOptions();
        if (lf.Simulado)
        {
            services.AddSingleton<ILaserficheRepositoryService, LaserficheSimulado>();
            return;
        }

        var urlBase = new Uri(lf.UrlBase.EndsWith('/') ? lf.UrlBase : lf.UrlBase + "/");
        var intento = TimeSpan.FromSeconds(lf.TimeoutSegundos);
        var reintentos = Math.Max(1, lf.Reintentos);

        void Resiliencia(HttpStandardResilienceOptions o)
        {
            o.AttemptTimeout.Timeout = intento;
            o.TotalRequestTimeout.Timeout = intento * (reintentos + 1) + TimeSpan.FromSeconds(5);
            o.Retry.MaxRetryAttempts = reintentos;
            o.CircuitBreaker.SamplingDuration = intento * 2;
        }

        services.AddSingleton<ProveedorTokenLaserfiche>();
        services.AddHttpClient(ProveedorTokenLaserfiche.ClienteHttp, c =>
        {
            c.BaseAddress = urlBase;
            c.Timeout = Timeout.InfiniteTimeSpan;
        }).AddStandardResilienceHandler(Resiliencia);

        services.AddHttpClient<ILaserficheRepositoryService, ClienteLaserficheApi>(c =>
        {
            c.BaseAddress = urlBase;
            c.Timeout = Timeout.InfiniteTimeSpan;   // lo controla el handler de resiliencia
        }).AddStandardResilienceHandler(Resiliencia);
    }

    private static X509Certificate2 BuscarCertificado(string thumbprint)
    {
        foreach (var ubicacion in new[] { StoreLocation.LocalMachine, StoreLocation.CurrentUser })
        {
            using var almacen = new X509Store(StoreName.My, ubicacion);
            almacen.Open(OpenFlags.ReadOnly);
            var encontrados = almacen.Certificates.Find(X509FindType.FindByThumbprint, thumbprint.Replace(" ", ""), validOnly: false);
            if (encontrados.Count > 0) return encontrados[0];
        }
        throw new InvalidOperationException($"No se encontró el certificado {thumbprint} para Data Protection.");
    }
}
