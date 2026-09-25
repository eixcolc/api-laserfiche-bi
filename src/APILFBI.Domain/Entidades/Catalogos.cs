namespace APILFBI.Domain.Entidades;

/// <summary>
/// Estructura común de todo catálogo. El código de la aplicación referencia los registros por
/// <see cref="Codigo"/>, nunca por <see cref="Id"/>, porque los Ids pueden variar entre ambientes.
/// </summary>
public abstract class Catalogo
{
    public int Id { get; set; }
    public string Codigo { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public string? Descripcion { get; set; }
    public int Orden { get; set; }
    public bool Activo { get; set; }
}

public sealed class TipoCliente : Catalogo;
public sealed class TipoIdentificacion : Catalogo;
public sealed class Segmentacion : Catalogo;
public sealed class EstadoCarga : Catalogo;
public sealed class MotivoRechazoCarga : Catalogo;
public sealed class ReglaCarga : Catalogo;
public sealed class OrigenAsociacion : Catalogo;
public sealed class TipoRechazo : Catalogo;
public sealed class EtapaRechazo : Catalogo;
public sealed class TipoDato : Catalogo;
public sealed class ReglaNormalizacion : Catalogo;
public sealed class TipoOperacion : Catalogo;
public sealed class OrigenOperacion : Catalogo;
public sealed class ResultadoOperacion : Catalogo;
public sealed class TipoExpediente : Catalogo;

public sealed class EstadoExpediente : Catalogo
{
    public bool PermiteCambios { get; set; }
}

public sealed class EstadoDocumento : Catalogo
{
    public bool RequiereTipoRechazo { get; set; }
    public bool RequiereComentario { get; set; }
    public bool SoloSistema { get; set; }
    public bool EsDerivado { get; set; }
}

public sealed class TipoArchivo : Catalogo
{
    public string MimeType { get; set; } = string.Empty;
    public byte[]? FirmaBytes { get; set; }
}

/// <summary>El Id es el ID de negocio que viaja en el XML de carga (ej. 32).</summary>
public sealed class TipoDocumento : Catalogo
{
    public int TamanoMaximoMB { get; set; }
    public int? DiasVigencia { get; set; }
    public int IdReglaCarga { get; set; }
}

public sealed class Llave : Catalogo
{
    public bool IdentificaPersona { get; set; }
    public int IdTipoDato { get; set; }
    public int LongitudMaxima { get; set; }
    public string? ExpresionValidacion { get; set; }
    public string? CatalogoValidacion { get; set; }
    public int IdReglaNormalizacion { get; set; }
}
