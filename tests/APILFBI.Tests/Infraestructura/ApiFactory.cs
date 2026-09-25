using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using APILFBI.Application.Comun;
using APILFBI.Infrastructure.Seguridad;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace APILFBI.Tests.Infraestructura;

/// <summary>
/// Levanta la API en memoria contra una base de prueba propia. Crea dos cuentas:
/// "crm-test" con todos los scopes de CRM y "solo-documentos" solo con documentos.leer.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public string BaseDatos { get; } = $"BILF_TEST_{Guid.NewGuid():N}"[..20];
    public string SecretoCrm { get; private set; } = string.Empty;
    public string SecretoSoloDocumentos { get; private set; } = string.Empty;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Pruebas");
        builder.UseSetting("ConnectionStrings:Bilf", BaseDatosPrueba.CadenaConexion(BaseDatos));
        builder.UseSetting("Auth:LlaveFirma", Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
        builder.UseSetting("Cache:IntervaloRevisionSegundos", "1");
        builder.UseSetting("Bitacora:IntervaloVaciadoMs", "100");
        builder.UseSetting("RateLimit:TokenPorMinuto", "1000");
        builder.UseSetting("Serilog:MinimumLevel:Default", "Warning");
    }

    public async Task InitializeAsync()
    {
        if (!BaseDatosPrueba.Disponible) return;
        await BaseDatosPrueba.CrearAsync(BaseDatos);

        await using var scope = Services.CreateAsyncScope();
        var creador = scope.ServiceProvider.GetRequiredService<CreadorCuentaServicio>();
        SecretoCrm = await creador.CrearORotarAsync("crm-test", "CRM pruebas",
            [Scopes.CatalogosLeer, Scopes.ExpedientesEscribir, Scopes.DocumentosLeer, Scopes.DocumentosEstado], "pruebas");
        SecretoSoloDocumentos = await creador.CrearORotarAsync("solo-documentos", "Solo documentos", [Scopes.DocumentosLeer], "pruebas");
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        if (BaseDatosPrueba.Disponible) await BaseDatosPrueba.EliminarAsync(BaseDatos);
    }

    public static FormUrlEncodedContent FormularioToken(string clientId, string secreto, string? scope = null, string grantType = "client_credentials")
    {
        var campos = new Dictionary<string, string> { ["grant_type"] = grantType, ["client_id"] = clientId, ["client_secret"] = secreto };
        if (scope is not null) campos["scope"] = scope;
        return new FormUrlEncodedContent(campos);
    }

    public async Task<HttpClient> ClienteAutenticadoAsync(string clientId = "crm-test", string? secreto = null, string? scope = null)
    {
        var cliente = CreateClient();
        var respuesta = await cliente.PostAsync("/api/v1/auth/token", FormularioToken(clientId, secreto ?? SecretoCrm, scope));
        respuesta.EnsureSuccessStatusCode();
        var json = await respuesta.Content.ReadFromJsonAsync<JsonElement>();
        cliente.DefaultRequestHeaders.Authorization = new("Bearer", json.GetProperty("access_token").GetString());
        return cliente;
    }
}

[CollectionDefinition(Nombre)]
public sealed class ColeccionApi : ICollectionFixture<ApiFactory>
{
    public const string Nombre = "API";
}
