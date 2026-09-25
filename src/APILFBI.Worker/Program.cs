using APILFBI.Infrastructure;
using APILFBI.Worker.Trabajos;
using Serilog;

Console.OutputEncoding = System.Text.Encoding.UTF8;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddSerilog((servicios, log) => log
    .ReadFrom.Configuration(builder.Configuration)
    .ReadFrom.Services(servicios)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Instancia", Environment.MachineName)
    .Enrich.WithProperty("Aplicacion", "APILFBI.Worker"));

builder.Services.AddInfraestructura(builder.Configuration, incluirSeguridad: false);

builder.Services.AddOptions<WorkerOptions>().Bind(builder.Configuration.GetSection(WorkerOptions.Seccion))
    .ValidateDataAnnotations()
    .Validate(o => TimeOnly.TryParseExact(o.HoraVencimiento, "HH:mm", out _) && TimeOnly.TryParseExact(o.HoraLimpieza, "HH:mm", out _),
              "Las horas del Worker deben tener formato HH:mm.")
    .Validate(o => { try { o.Zona(); return true; } catch { return false; } }, "Worker:ZonaHoraria no es una zona horaria válida.")
    .ValidateOnStart();

builder.Services.AddHostedService<JobSincronizacionLaserfiche>();
builder.Services.AddHostedService<JobVencimiento>();
builder.Services.AddHostedService<JobLimpiezaIdempotencia>();
builder.Services.AddHostedService<JobCargasPendientes>();

// Como servicio de Windows si se instala así; en consola o contenedor funciona igual.
builder.Services.AddWindowsService(o => o.ServiceName = "APILFBI.Worker");

await builder.Build().RunAsync();
