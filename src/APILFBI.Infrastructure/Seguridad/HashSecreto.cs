using System.Security.Cryptography;

namespace APILFBI.Infrastructure.Seguridad;

/// <summary>
/// Hash de secretos de cuentas de servicio con PBKDF2-HMAC-SHA256.
/// Formato guardado: PBKDF2-SHA256$iteraciones$salBase64$hashBase64
/// </summary>
public static class HashSecreto
{
    private const string Prefijo = "PBKDF2-SHA256";
    private const int Iteraciones = 600_000;   // recomendación OWASP para PBKDF2-HMAC-SHA256
    private const int LargoSal = 16;
    private const int LargoHash = 32;

    // Hash ficticio para igualar el tiempo de respuesta cuando el client_id no existe.
    private static readonly Lazy<string> HashFicticio = new(() => Calcular(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))));

    public static string Calcular(string secreto)
    {
        var sal = RandomNumberGenerator.GetBytes(LargoSal);
        var hash = Rfc2898DeriveBytes.Pbkdf2(secreto, sal, Iteraciones, HashAlgorithmName.SHA256, LargoHash);
        return $"{Prefijo}${Iteraciones}${Convert.ToBase64String(sal)}${Convert.ToBase64String(hash)}";
    }

    public static bool Verificar(string secreto, string? guardado)
    {
        var partes = (guardado ?? HashFicticio.Value).Split('$');
        if (partes.Length != 4 || partes[0] != Prefijo || !int.TryParse(partes[1], out var iteraciones))
            return false;

        var sal = Convert.FromBase64String(partes[2]);
        var esperado = Convert.FromBase64String(partes[3]);
        var calculado = Rfc2898DeriveBytes.Pbkdf2(secreto, sal, iteraciones, HashAlgorithmName.SHA256, esperado.Length);
        return CryptographicOperations.FixedTimeEquals(calculado, esperado) && guardado is not null;
    }

    /// <summary>Genera un secreto aleatorio de 256 bits en Base64 URL-safe.</summary>
    public static string GenerarSecreto() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
