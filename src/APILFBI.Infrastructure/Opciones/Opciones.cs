using System.ComponentModel.DataAnnotations;

namespace APILFBI.Infrastructure.Opciones;

public sealed class AuthOptions
{
    public const string Seccion = "Auth";

    [Required] public string Emisor { get; set; } = "APILFBI";
    [Required] public string Audiencia { get; set; } = "APILFBI";

    /// <summary>
    /// Llave de firma HMAC-SHA256 en Base64, de 32 bytes o más. Es la misma en todas las instancias
    /// del balanceador. Va en User Secrets (desarrollo) o Key Vault / variable de entorno (producción).
    /// </summary>
    [Required] public string LlaveFirma { get; set; } = string.Empty;

    [Range(5, 1440)] public int ExpiracionMinutos { get; set; } = 60;

    public byte[] ObtenerLlave()
    {
        var bytes = Convert.FromBase64String(LlaveFirma);
        if (bytes.Length < 32)
            throw new InvalidOperationException("Auth:LlaveFirma debe tener al menos 32 bytes (256 bits).");
        return bytes;
    }
}

public sealed class CacheOptions
{
    public const string Seccion = "Cache";

    /// <summary>Cada cuántos segundos cada instancia revisa cat.VersionCatalogo.</summary>
    [Range(1, 3600)] public int IntervaloRevisionSegundos { get; set; } = 60;

    /// <summary>Expiración de respaldo: aunque no cambie la versión, el catálogo se recarga al vencer.</summary>
    [Range(1, 1440)] public int TtlRespaldoMinutos { get; set; } = 30;
}

public sealed class BitacoraOptions
{
    public const string Seccion = "Bitacora";

    [Range(100, 1_000_000)] public int Capacidad { get; set; } = 10_000;
    [Range(1, 5_000)] public int TamanoLote { get; set; } = 200;
    [Range(50, 60_000)] public int IntervaloVaciadoMs { get; set; } = 2_000;
}
