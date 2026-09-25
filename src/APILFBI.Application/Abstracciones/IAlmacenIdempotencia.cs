namespace APILFBI.Application.Abstracciones;

public enum EstadoReserva
{
    /// <summary>Primera vez que llega esta llave: se puede ejecutar.</summary>
    Reservada,
    /// <summary>Otra solicitud con la misma llave se está ejecutando (ej. en otra instancia).</summary>
    EnProceso,
    /// <summary>La llave ya se usó con otro contenido.</summary>
    ContenidoDistinto,
    /// <summary>Ya se ejecutó: se devuelve la respuesta guardada.</summary>
    Completada,
}

public sealed record ResultadoReserva(EstadoReserva Estado, int? HttpStatus = null, string? RespuestaJson = null);

/// <summary>Idempotencia de las operaciones de escritura (header Idempotency-Key), guardada en aud.IdempotenciaRequest.</summary>
public interface IAlmacenIdempotencia
{
    Task<ResultadoReserva> ReservarAsync(string clientId, string llave, string endpoint, string hashSolicitud, CancellationToken ct = default);
    Task CompletarAsync(string clientId, string llave, int httpStatus, string respuestaJson, CancellationToken ct = default);

    /// <summary>Borra la reserva cuando la operación falló por un error que se puede reintentar.</summary>
    Task LiberarAsync(string clientId, string llave, CancellationToken ct = default);
}
