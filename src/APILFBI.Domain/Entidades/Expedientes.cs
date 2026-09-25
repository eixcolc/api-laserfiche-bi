namespace APILFBI.Domain.Entidades;

/// <summary>Expediente: pertenece a un tipo de expediente y a un conjunto de llaves.</summary>
public sealed class Expediente
{
    public long Id { get; set; }
    public int IdTipoExpediente { get; set; }
    public int IdTipoCliente { get; set; }
    public int IdEstadoExpediente { get; set; }
    public DateTime FechaApertura { get; set; }
    public DateTime? FechaCierre { get; set; }
    public List<LlaveExpediente> Llaves { get; set; } = [];
}

/// <summary>Valor de una llave en un expediente (ej. CIF = 12345678). Un solo valor vigente por llave.</summary>
public sealed class LlaveExpediente
{
    public long Id { get; set; }
    public long IdExpediente { get; set; }
    public int IdLlave { get; set; }
    public string Valor { get; set; } = string.Empty;
    public string ValorNormalizado { get; set; } = string.Empty;
    public bool Vigente { get; set; }
}
