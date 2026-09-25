namespace APILFBI.Domain.Entidades;

/// <summary>Registro de la bitácora funcional (aud.Bitacora). Solo inserción.</summary>
public sealed class Bitacora
{
    public long IdBitacora { get; set; }
    public DateTime FechaHoraUtc { get; set; }
    public int IdTipoOperacion { get; set; }
    public int IdOrigenOperacion { get; set; }
    public int IdResultadoOperacion { get; set; }
    public int? CodigoRespuesta { get; set; }
    public long? IdExpediente { get; set; }
    public long? IdDocumento { get; set; }
    public string? Correlativo { get; set; }
    public string UsuarioServicio { get; set; } = string.Empty;
    public string? UsuarioOperacion { get; set; }
    public string? IpOrigen { get; set; }
    public string? Instancia { get; set; }
    public string? CorrelationId { get; set; }
    public string? Endpoint { get; set; }
    public string? MetodoHttp { get; set; }
    public int? DuracionMs { get; set; }
    public string? DatosAnteriores { get; set; }
    public string? DatosNuevos { get; set; }
    public string? Detalle { get; set; }
}
