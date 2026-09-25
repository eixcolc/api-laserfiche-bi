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

// La API no recibe archivos (la carga va por SFTP): se limita el tamaño del body.
builder.WebHost.ConfigureKestrel(k => k.Limits.MaxRequestBodySize = 1 * 1024 * 1024);

builder.Services.AddAplicacion();
builder.Services.AddInfraestructura(builder.Configuration);
builder.Services.AddApiWeb(builder.Configuration);

var app = builder.Build();

if (args.Contains(ComandoCrearCuenta.Nombre))
    return await ComandoCrearCuenta.EjecutarAsync(app.Services, args);

app.UseForwardedHeaders();
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseExceptionHandler();
app.UseSerilogRequestLogging(o => o.EnrichDiagnosticContext = (diag, http) =>
    diag.Set("UsuarioServicio", http.User.FindFirst("client_id")?.Value ?? "(anonimo)"));

app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

if (app.Configuration.GetValue("OpenApi:Habilitado", false))
{
    app.MapOpenApi().AllowAnonymous();
    app.UseSwaggerUI(o =>
    {
        o.SwaggerEndpoint("/openapi/v1.json", "APILFBI v1");
        o.RoutePrefix = "swagger";
    });
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
