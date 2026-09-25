namespace APILFBI.Application.Abstracciones;

public sealed record SolicitudToken(string? GrantType, string? ClientId, string? ClientSecret, string? Scope);

public sealed record TokenEmitido(string AccessToken, int ExpiresIn, string Scope);

/// <summary>
/// Resultado de la emisión. Si falla, <see cref="ErrorOAuth"/> trae el código de error de
/// RFC 6749 (invalid_client, invalid_scope...) y <see cref="Codigo"/> el código de negocio.
/// </summary>
public sealed record ResultadoToken(TokenEmitido? Token, int Codigo, string? ErrorOAuth, string? Detalle = null)
{
    public bool Exitoso => Token is not null;
}

public interface IAutenticacionService
{
    Task<ResultadoToken> EmitirTokenAsync(SolicitudToken solicitud, CancellationToken ct = default);
}
