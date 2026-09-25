using APILFBI.Application.Abstracciones;
using APILFBI.Application.Comun;
using APILFBI.Infrastructure.Persistencia;
using Microsoft.EntityFrameworkCore;

namespace APILFBI.Infrastructure.Seguridad;

/// <summary>OAuth 2.0 client_credentials (RFC 6749, sección 4.4) contra seg.CuentaServicio.</summary>
internal sealed class AutenticacionService(BilfDbContext db, EmisorToken emisor, TimeProvider tiempo) : IAutenticacionService
{
    public async Task<ResultadoToken> EmitirTokenAsync(SolicitudToken s, CancellationToken ct = default)
    {
        if (!string.Equals(s.GrantType, "client_credentials", StringComparison.Ordinal))
            return new(null, CodigosRespuesta.SolicitudInvalida, "unsupported_grant_type", "Solo se admite grant_type=client_credentials.");

        if (string.IsNullOrWhiteSpace(s.ClientId) || string.IsNullOrEmpty(s.ClientSecret))
            return new(null, CodigosRespuesta.CredencialesInvalidas, "invalid_client");

        var cuenta = await db.CuentasServicio.AsNoTracking()
            .Include(c => c.Scopes).ThenInclude(cs => cs.Scope)
            .FirstOrDefaultAsync(c => c.ClientId == s.ClientId, ct);

        // Se verifica el hash aunque la cuenta no exista, para no revelar qué client_id son válidos por el tiempo de respuesta.
        var secretoValido = HashSecreto.Verificar(s.ClientSecret, cuenta?.SecretHash);
        var vigente = cuenta is { Activo: true }
                      && (cuenta.FechaExpiracionSecreto is null || cuenta.FechaExpiracionSecreto > tiempo.GetUtcNow().UtcDateTime);

        if (cuenta is null || !secretoValido || !vigente)
            return new(null, CodigosRespuesta.CredencialesInvalidas, "invalid_client");

        var permitidos = cuenta.Scopes
            .Where(cs => cs.Scope is { Activo: true })
            .Select(cs => cs.Scope!.Codigo)
            .ToHashSet(StringComparer.Ordinal);

        var solicitados = string.IsNullOrWhiteSpace(s.Scope)
            ? permitidos
            : s.Scope.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet(StringComparer.Ordinal);

        var noPermitidos = solicitados.Except(permitidos).ToList();
        if (noPermitidos.Count > 0)
            return new(null, CodigosRespuesta.SinPermiso, "invalid_scope", $"Scopes no permitidos: {string.Join(' ', noPermitidos)}");

        var scopes = solicitados.Order(StringComparer.Ordinal).ToList();
        var (token, expira) = emisor.Emitir(cuenta.ClientId, scopes);
        return new(new TokenEmitido(token, expira, string.Join(' ', scopes)), CodigosRespuesta.Exito, null);
    }
}
