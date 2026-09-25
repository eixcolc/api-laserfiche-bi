namespace APILFBI.Domain.Entidades;

public sealed class Scope : Catalogo;

/// <summary>Usuario de servicio (credenciales del CRM). El secreto se guarda solo como hash.</summary>
public sealed class CuentaServicio
{
    public int Id { get; set; }
    public string ClientId { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public string SecretHash { get; set; } = string.Empty;
    public DateTime? FechaExpiracionSecreto { get; set; }
    public bool Activo { get; set; }
    public string CreadoPor { get; set; } = "SISTEMA";
    public DateTime FechaCreacion { get; set; }
    public List<CuentaServicioScope> Scopes { get; set; } = [];
}

public sealed class CuentaServicioScope
{
    public int IdCuentaServicio { get; set; }
    public int IdScope { get; set; }
    public string CreadoPor { get; set; } = "SISTEMA";
    public Scope? Scope { get; set; }
}
