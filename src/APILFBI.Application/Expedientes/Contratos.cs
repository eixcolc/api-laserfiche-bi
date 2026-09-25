using APILFBI.Domain.Entidades;

namespace APILFBI.Application.Expedientes;

/// <summary>Una llave tal como viaja en el prototipo: &lt;llave tipo="CIF"&gt;12345678&lt;/llave&gt;.</summary>
public sealed record LlaveValor(string? Tipo, string? Valor);

public sealed record SolicitudExpediente(int IdTipoExpediente, IReadOnlyList<LlaveValor>? Llaves);

public sealed record LlaveExpedienteDto(string Tipo, string Valor);

public sealed record ExpedienteDto(
    long IdExpediente,
    int IdTipoExpediente,
    string TipoExpediente,
    string TipoCliente,
    string EstadoExpediente,
    DateTime FechaApertura,
    DateTime? FechaCierre,
    IReadOnlyList<LlaveExpedienteDto> Llaves);

public sealed record ResultadoExpediente(ExpedienteDto Expediente, bool EsNuevo);

/// <summary>Error por llave que devuelve trx.usp_ObtenerOCrearExpediente.</summary>
public sealed record ErrorLlave(string? CodigoLlave, int Codigo, string Mensaje);

public sealed record ResultadoObtenerOCrear(int Codigo, long? IdExpediente, bool EsNuevo, IReadOnlyList<ErrorLlave> Errores);

/// <summary>Datos de quién y desde dónde, para la bitácora que escriben los stored procedures.</summary>
public sealed record ContextoOperacion(string UsuarioServicio, string? UsuarioOperacion, string? IpOrigen, string? CorrelationId, string? Endpoint);

public interface IRepositorioExpedientes
{
    Task<ResultadoObtenerOCrear> ObtenerOCrearAsync(int idTipoExpediente, IReadOnlyList<LlaveValor> llaves,
        ContextoOperacion contexto, CancellationToken ct = default);

    /// <summary>Misma validación e identidad que <see cref="ObtenerOCrearAsync"/>, sin crear ni modificar. 300 si no existe.</summary>
    Task<ResultadoObtenerOCrear> BuscarAsync(int idTipoExpediente, IReadOnlyList<LlaveValor> llaves,
        ContextoOperacion contexto, CancellationToken ct = default);

    /// <summary>El expediente con sus llaves vigentes, o null si no existe.</summary>
    Task<Expediente?> ObtenerAsync(long idExpediente, CancellationToken ct = default);
}
