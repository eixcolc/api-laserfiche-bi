namespace APILFBI.Application.Abstracciones;

/// <summary>Quién hace la solicitud actual.</summary>
public interface IContextoUsuario
{
    /// <summary>Usuario de servicio: client_id del token (ej. la cuenta del CRM).</summary>
    string UsuarioServicio { get; }

    /// <summary>Usuario de operación: la persona que hace la acción (header X-Operation-User).</summary>
    string? UsuarioOperacion { get; }

    IReadOnlyCollection<string> Scopes { get; }
    string? CorrelationId { get; }
    string? IpOrigen { get; }
}
