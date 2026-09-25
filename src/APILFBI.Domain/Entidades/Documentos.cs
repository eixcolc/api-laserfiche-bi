namespace APILFBI.Domain.Entidades;

/// <summary>Documento importado a Laserfiche. Lo registra el workflow con trx.usp_RegistrarDocumento.</summary>
public sealed class Documento
{
    public long Id { get; set; }

    /// <summary>ID único que genera Laserfiche. Es el idDocumento que conoce el CRM.</summary>
    public int LaserficheEntryId { get; set; }

    public long? IdCargaDocumento { get; set; }
    public long IdExpedienteOrigen { get; set; }
    public int IdTipoDocumento { get; set; }
    public int IdTipoArchivo { get; set; }
    public int IdEstadoDocumento { get; set; }
    public string NombreArchivo { get; set; } = string.Empty;
    public long? TamanoBytes { get; set; }
    public int Version { get; set; }
    public bool Vigente { get; set; }
    public DateOnly FechaEmision { get; set; }
    public DateTime FechaHoraRecepcion { get; set; }
    public DateOnly? FechaVencimiento { get; set; }
    public string UsuarioCarga { get; set; } = string.Empty;
    public string NombreUsuarioCarga { get; set; } = string.Empty;
    public string? Comentario { get; set; }
}

/// <summary>Relación N:M: un documento puede pertenecer a varios expedientes.</summary>
public sealed class ExpedienteDocumento
{
    public long IdExpediente { get; set; }
    public long IdDocumento { get; set; }
    public int IdOrigenAsociacion { get; set; }
    public DateTime FechaAsociacion { get; set; }
    public bool Vigente { get; set; }
}

/// <summary>Cada carga recibida por SFTP, importada o rechazada.</summary>
public sealed class CargaDocumento
{
    public long Id { get; set; }
    public string Correlativo { get; set; } = string.Empty;
    public long? IdExpediente { get; set; }
    public int? IdTipoDocumento { get; set; }
    public int IdEstadoCarga { get; set; }
    public int? IdMotivoRechazoCarga { get; set; }
    public string? DetalleRechazo { get; set; }
    public int? LaserficheEntryId { get; set; }
    public DateTime FechaHoraRecepcion { get; set; }
}
