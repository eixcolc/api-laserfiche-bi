namespace APILFBI.Application.Abstracciones;

/// <summary>
/// Entrada de bitácora que registra la API. Las operaciones que pasan por stored procedures
/// (crear expediente, registrar documento, cambio de estado) ya se registran en la base y no se
/// deben registrar otra vez aquí.
/// </summary>
public sealed record EntradaBitacora
{
    public required string TipoOperacion { get; init; }
    public string Origen { get; init; } = "Api";
    public required int CodigoRespuesta { get; init; }
    public required string UsuarioServicio { get; init; }
    public string? UsuarioOperacion { get; init; }
    public long? IdExpediente { get; init; }
    public long? IdDocumento { get; init; }
    public string? Correlativo { get; init; }
    public string? IpOrigen { get; init; }
    public string? CorrelationId { get; init; }
    public string? Endpoint { get; init; }
    public string? MetodoHttp { get; init; }
    public int? DuracionMs { get; init; }
    public string? DatosAnteriores { get; init; }
    public string? DatosNuevos { get; init; }
    public string? Detalle { get; init; }
    public DateTime FechaHoraUtc { get; init; } = DateTime.UtcNow;
}

public interface IBitacoraService
{
    /// <summary>Encola la entrada; se escribe en lotes en segundo plano para no agregar latencia.</summary>
    void Encolar(EntradaBitacora entrada);
}
