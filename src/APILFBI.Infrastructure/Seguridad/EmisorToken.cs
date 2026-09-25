using System.Security.Claims;
using APILFBI.Infrastructure.Opciones;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace APILFBI.Infrastructure.Seguridad;

/// <summary>
/// Emite JWT firmados con HMAC-SHA256. Todas las instancias usan la misma llave, así que
/// cualquiera valida un token emitido por otra (no hace falta sticky session).
/// </summary>
public sealed class EmisorToken(IOptions<AuthOptions> opciones, TimeProvider tiempo)
{
    public const string ClaimClientId = "client_id";
    public const string ClaimScope = "scope";

    public (string Token, int ExpiraEnSegundos) Emitir(string clientId, IEnumerable<string> scopes)
    {
        var o = opciones.Value;
        var ahora = tiempo.GetUtcNow().UtcDateTime;
        var expira = ahora.AddMinutes(o.ExpiracionMinutos);

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = o.Emisor,
            Audience = o.Audiencia,
            IssuedAt = ahora,
            NotBefore = ahora,
            Expires = expira,
            Subject = new ClaimsIdentity(
            [
                new Claim(JwtRegisteredClaimNames.Sub, clientId),
                new Claim(ClaimClientId, clientId),
                new Claim(ClaimScope, string.Join(' ', scopes)),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            ]),
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(o.ObtenerLlave()), SecurityAlgorithms.HmacSha256),
        };

        return (new JsonWebTokenHandler().CreateToken(descriptor), o.ExpiracionMinutos * 60);
    }

    public static TokenValidationParameters ParametrosValidacion(AuthOptions o) => new()
    {
        ValidIssuer = o.Emisor,
        ValidAudience = o.Audiencia,
        IssuerSigningKey = new SymmetricSecurityKey(o.ObtenerLlave()),
        ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ClockSkew = TimeSpan.FromSeconds(30),
        NameClaimType = ClaimClientId,
    };
}
