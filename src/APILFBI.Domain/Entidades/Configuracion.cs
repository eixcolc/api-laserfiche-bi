namespace APILFBI.Domain.Entidades;

public sealed class TransicionEstadoDocumento
{
    public int Id { get; set; }
    public int IdEstadoOrigen { get; set; }
    public int IdEstadoDestino { get; set; }
    public bool Activo { get; set; }
}

/// <summary>Formatos permitidos por tipo de documento (criterio 14).</summary>
public sealed class TipoDocumentoTipoArchivo
{
    public int IdTipoDocumento { get; set; }
    public int IdTipoArchivo { get; set; }
    public bool Activo { get; set; }
}

/// <summary>Qué documentos lleva cada tipo de expediente. IdTipoCliente null = aplica a ambos.</summary>
public sealed class TipoExpedienteTipoDocumento
{
    public int Id { get; set; }
    public int IdTipoExpediente { get; set; }
    public int IdTipoDocumento { get; set; }
    public int? IdTipoCliente { get; set; }
    public bool Obligatorio { get; set; }
    public int Orden { get; set; }
    public bool Activo { get; set; }
}

/// <summary>Llaves por tipo de expediente. GrupoIdentificacion null = llave descriptiva.</summary>
public sealed class TipoExpedienteLlave
{
    public int Id { get; set; }
    public int IdTipoExpediente { get; set; }
    public int IdTipoCliente { get; set; }
    public int IdLlave { get; set; }
    public bool Obligatoria { get; set; }
    public byte? GrupoIdentificacion { get; set; }
    public byte? PrioridadGrupo { get; set; }
    public int Orden { get; set; }
    public bool Activo { get; set; }
}

public sealed class CodigoRespuesta
{
    public int Id { get; set; }
    public int Codigo { get; set; }
    public string Clave { get; set; } = string.Empty;
    public string Mensaje { get; set; } = string.Empty;
    public short HttpStatus { get; set; }
    public string? Descripcion { get; set; }
    public bool Activo { get; set; }
}

public sealed class VersionCatalogo
{
    public string NombreCatalogo { get; set; } = string.Empty;
    public long Version { get; set; }
    public DateTime FechaModificacion { get; set; }
}
