using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace APILFBI.Tests.Infraestructura;

/// <summary>
/// Crea una base BILF_TEST_xxx desde los scripts de database/ (los mismos de producción) y la borra al final.
/// Servidor: variable de entorno BILF_TEST_SERVER (por defecto .\SQLEXPRESS, autenticación de Windows).
/// </summary>
public static partial class BaseDatosPrueba
{
    public static string Servidor => Environment.GetEnvironmentVariable("BILF_TEST_SERVER") ?? @".\SQLEXPRESS";

    private static readonly Lazy<bool> ServidorDisponible = new(() =>
    {
        try
        {
            using var cn = new SqlConnection(CadenaConexion("master") + ";Connect Timeout=3");
            cn.Open();
            return true;
        }
        catch
        {
            return false;
        }
    });

    public static bool Disponible => ServidorDisponible.Value;

    public static string CadenaConexion(string baseDatos) =>
        $"Server={Servidor};Database={baseDatos};Trusted_Connection=True;TrustServerCertificate=True;Application Name=APILFBI.Tests";

    [GeneratedRegex(@"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex SeparadorGo();

    public static async Task CrearAsync(string baseDatos)
    {
        var carpeta = BuscarCarpetaDatabase();
        await using var cn = new SqlConnection(CadenaConexion("master"));
        await cn.OpenAsync();

        foreach (var archivo in Directory.GetFiles(carpeta, "0*.sql").Order(StringComparer.Ordinal))
        {
            var sql = (await File.ReadAllTextAsync(archivo))
                .Replace("[BILF]", $"[{baseDatos}]")
                .Replace("N'BILF'", $"N'{baseDatos}'");

            foreach (var lote in SeparadorGo().Split(sql).Where(l => !string.IsNullOrWhiteSpace(l)))
            {
                await using var cmd = new SqlCommand(lote, cn) { CommandTimeout = 120 };
                await cmd.ExecuteNonQueryAsync();
            }
        }
    }

    public static async Task EliminarAsync(string baseDatos)
    {
        SqlConnection.ClearAllPools();
        await using var cn = new SqlConnection(CadenaConexion("master"));
        await cn.OpenAsync();
        await using var cmd = new SqlCommand(
            $"IF DB_ID(N'{baseDatos}') IS NOT NULL BEGIN ALTER DATABASE [{baseDatos}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{baseDatos}]; END", cn);
        await cmd.ExecuteNonQueryAsync();
    }

    public static async Task<T?> EscalarAsync<T>(string baseDatos, string sql)
    {
        await using var cn = new SqlConnection(CadenaConexion(baseDatos));
        await cn.OpenAsync();
        await using var cmd = new SqlCommand(sql, cn);
        var valor = await cmd.ExecuteScalarAsync();
        return valor is null or DBNull ? default : (T)valor;
    }

    private static string BuscarCarpetaDatabase()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidata = Path.Combine(dir.FullName, "database");
            if (File.Exists(Path.Combine(candidata, "01_tablas.sql"))) return candidata;
        }
        throw new DirectoryNotFoundException("No se encontró la carpeta database/ con los scripts.");
    }
}

/// <summary>Prueba que necesita SQL Server; se omite si el servidor no está disponible.</summary>
public sealed class FactSqlServerAttribute : FactAttribute
{
    public FactSqlServerAttribute()
    {
        if (!BaseDatosPrueba.Disponible)
            Skip = $"SQL Server no disponible en {BaseDatosPrueba.Servidor} (variable BILF_TEST_SERVER).";
    }
}
