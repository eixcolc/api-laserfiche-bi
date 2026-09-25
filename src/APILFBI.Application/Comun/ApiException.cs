namespace APILFBI.Application.Comun;

public sealed record ErrorCampo(string Campo, string Mensaje);

/// <summary>
/// Error controlado. El manejador global lo convierte en una respuesta con el HTTP y el mensaje
/// que indica cat.CodigoRespuesta para <see cref="Codigo"/>.
/// </summary>
public sealed class ApiException(int codigo, string? detalle = null, IReadOnlyList<ErrorCampo>? errores = null, object? datos = null)
    : Exception(detalle ?? $"Código de respuesta {codigo}")
{
    public int Codigo { get; } = codigo;
    public string? Detalle { get; } = detalle;
    public IReadOnlyList<ErrorCampo> Errores { get; } = errores ?? [];

    /// <summary>Detalle adicional para el cliente (ej. el resultado por documento de un lote rechazado).</summary>
    public object? Datos { get; } = datos;
}
