using APILFBI.Domain.Entidades;

namespace APILFBI.Infrastructure.Cache;

/// <summary>
/// Relaciona cada entidad cacheada con su tabla en cat.VersionCatalogo, para saber qué recargar
/// cuando cambia una versión.
/// </summary>
internal sealed class CatalogoRegistro
{
    private readonly Dictionary<Type, string> _tablas = [];
    private readonly Dictionary<string, Func<CatalogoCache, CancellationToken, Task>> _precargas = [];

    public IReadOnlyCollection<string> Tablas => _precargas.Keys;

    public CatalogoRegistro Agregar<T>(string tabla) where T : class
    {
        _tablas[typeof(T)] = tabla;
        _precargas[tabla] = (cache, ct) => cache.ObtenerAsync<T>(ct);
        return this;
    }

    public string Tabla<T>() => _tablas.TryGetValue(typeof(T), out var tabla)
        ? tabla
        : throw new InvalidOperationException($"El tipo {typeof(T).Name} no está registrado como catálogo.");

    public Task PrecargarAsync(string tabla, CatalogoCache cache, CancellationToken ct) => _precargas[tabla](cache, ct);

    public static CatalogoRegistro Predeterminado() => new CatalogoRegistro()
        .Agregar<TipoCliente>("TipoCliente")
        .Agregar<TipoIdentificacion>("TipoIdentificacion")
        .Agregar<Segmentacion>("Segmentacion")
        .Agregar<EstadoExpediente>("EstadoExpediente")
        .Agregar<EstadoCarga>("EstadoCarga")
        .Agregar<MotivoRechazoCarga>("MotivoRechazoCarga")
        .Agregar<ReglaCarga>("ReglaCarga")
        .Agregar<OrigenAsociacion>("OrigenAsociacion")
        .Agregar<TipoRechazo>("TipoRechazo")
        .Agregar<EtapaRechazo>("EtapaRechazo")
        .Agregar<TipoDato>("TipoDato")
        .Agregar<ReglaNormalizacion>("ReglaNormalizacion")
        .Agregar<TipoOperacion>("TipoOperacion")
        .Agregar<OrigenOperacion>("OrigenOperacion")
        .Agregar<ResultadoOperacion>("ResultadoOperacion")
        .Agregar<CodigoRespuesta>("CodigoRespuesta")
        .Agregar<EstadoDocumento>("EstadoDocumento")
        .Agregar<TransicionEstadoDocumento>("TransicionEstadoDocumento")
        .Agregar<TipoArchivo>("TipoArchivo")
        .Agregar<TipoDocumento>("TipoDocumento")
        .Agregar<TipoDocumentoTipoArchivo>("TipoDocumentoTipoArchivo")
        .Agregar<TipoExpediente>("TipoExpediente")
        .Agregar<TipoExpedienteTipoDocumento>("TipoExpedienteTipoDocumento")
        .Agregar<Llave>("Llave")
        .Agregar<TipoExpedienteLlave>("TipoExpedienteLlave");
}
