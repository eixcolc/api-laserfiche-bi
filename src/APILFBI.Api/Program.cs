using System.Text.Json;
using APILFBI.Api.Comandos;
using APILFBI.Api.Web;
using APILFBI.Application;
using APILFBI.Infrastructure;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Serilog;

Console.OutputEncoding = System.Text.Encoding.UTF8;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((ctx, servicios, log) => log
    .ReadFrom.Configuration(ctx.Configuration)
    .ReadFrom.Services(servicios)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Instancia", Environment.MachineName));

// Límite general del body: 1 MB. Solo el endpoint de carga lo amplía (CargaApi:TamanoMaximoSolicitudMB).
// Se configura para Kestrel y para IIS, que tiene su propio límite por defecto (~28 MB).
const long LimiteBody = 1 * 1024 * 1024;
builder.WebHost.ConfigureKestrel(k => k.Limits.MaxRequestBodySize = LimiteBody);
builder.Services.Configure<Microsoft.AspNetCore.Builder.IISServerOptions>(o => o.MaxRequestBodySize = LimiteBody);

builder.Services.AddAplicacion();
builder.Services.AddInfraestructura(builder.Configuration);
builder.Services.AddApiWeb(builder.Configuration);

var app = builder.Build();

if (args.Contains(ComandoCrearCuenta.Nombre))
    return await ComandoCrearCuenta.EjecutarAsync(app.Services, args);

// Publicada en una subruta (ej. https://servidor/expediente). En IIS la subruta de la aplicación la pone
// el módulo automáticamente; esta opción es para Kestrel detrás de un proxy.
var pathBase = app.Configuration["PathBase"];
if (!string.IsNullOrWhiteSpace(pathBase))
    app.UsePathBase(pathBase);

app.UseForwardedHeaders();
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseExceptionHandler();
app.UseSerilogRequestLogging(o => o.EnrichDiagnosticContext = (diag, http) =>
    diag.Set("UsuarioServicio", http.User.FindFirst("client_id")?.Value ?? "(anonimo)"));

var openApiHabilitado = app.Configuration.GetValue("OpenApi:Habilitado", false);

// Swagger UI es un middleware (no un endpoint): va antes de la autorización, porque la política
// por defecto exige token en todo y bloquearía la página.
if (openApiHabilitado)
    app.UseSwaggerUI(o =>
    {
        // Relativa a /swagger/: funciona en la raíz y en una subruta (/expediente/openapi/v1.json).
        o.SwaggerEndpoint("../openapi/v1.json", "APILFBI v1");
        o.RoutePrefix = "swagger";
    });

app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

if (openApiHabilitado)
{
    app.MapOpenApi().AllowAnonymous();
    app.MapGet("/", (HttpContext ctx) => Results.Redirect($"{ctx.Request.PathBase}/swagger")).AllowAnonymous().ExcludeFromDescription();
}

// Salud para el balanceador: live = el proceso responde; ready = puede recibir tráfico.
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false })
   .AllowAnonymous().DisableRateLimiting();
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = c => c.Tags.Contains(APILFBI.Infrastructure.DependencyInjection.TagReady),
    ResponseWriter = (ctx, reporte) => ctx.Response.WriteAsJsonAsync(new
    {
        estado = reporte.Status.ToString(),
        verificaciones = reporte.Entries.Select(e => new { nombre = e.Key, estado = e.Value.Status.ToString(), descripcion = e.Value.Description }),
    }),
}).AllowAnonymous().DisableRateLimiting();

app.MapControllers();

await app.RunAsync();
return 0;

public partial class Program;
