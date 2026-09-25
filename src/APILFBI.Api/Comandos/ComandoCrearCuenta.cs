using APILFBI.Application.Comun;
using APILFBI.Infrastructure.Seguridad;

namespace APILFBI.Api.Comandos;

/// <summary>
/// Crea la cuenta de servicio del CRM o le rota el secreto:
///   dotnet run --project src/APILFBI.Api -- crear-cuenta --client-id crm --nombre "CRM" [--scopes a,b]
/// Imprime el secreto una sola vez; en la base queda solo el hash.
/// </summary>
internal static class ComandoCrearCuenta
{
    public const string Nombre = "crear-cuenta";

    public static async Task<int> EjecutarAsync(IServiceProvider servicios, string[] args)
    {
        var clientId = Argumento(args, "--client-id");
        var nombre = Argumento(args, "--nombre") ?? clientId;
        var scopes = Argumento(args, "--scopes")?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                     ?? [.. Scopes.Todos.Where(s => s != Scopes.BitacoraLeer)];

        if (string.IsNullOrWhiteSpace(clientId))
        {
            Console.Error.WriteLine("Uso: crear-cuenta --client-id <id> [--nombre <nombre>] [--scopes scope1,scope2]");
            return 1;
        }

        await using var scope = servicios.CreateAsyncScope();
        var creador = scope.ServiceProvider.GetRequiredService<CreadorCuentaServicio>();
        var secreto = await creador.CrearORotarAsync(clientId, nombre!, scopes, Environment.UserName);

        Console.WriteLine($"Cuenta: {clientId}");
        Console.WriteLine($"Scopes: {string.Join(' ', scopes)}");
        Console.WriteLine($"Secreto (guárdelo ahora, no se vuelve a mostrar): {secreto}");
        return 0;
    }

    private static string? Argumento(string[] args, string nombre)
    {
        var i = Array.IndexOf(args, nombre);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
}
