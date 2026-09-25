using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.ComponentModel;
using APILFBI.Application.Abstracciones;
using APILFBI.Domain.Entidades;
using APILFBI.Infrastructure.Opciones;
using APILFBI.Infrastructure.Persistencia;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace APILFBI.Infrastructure.Cache;

/// <summary>
/// Caché de catálogos sobre HybridCache, solo con el nivel en memoria (sin Redis).
/// La clave incluye la versión de cat.VersionCatalogo: cuando la versión cambia, la siguiente
/// lectura usa una clave nueva y recarga solo ese catálogo. HybridCache evita que varias
/// solicitudes simultáneas carguen el mismo catálogo a la vez.
/// </summary>
internal sealed class CatalogoCache(
    HybridCache cache,
    IDbContextFactory<BilfDbContext> dbFactory,
    CatalogoRegistro registro,
    IOptions<CacheOptions> opciones,
    IBitacoraService bitacora,
    ILogger<CatalogoCache> log) : ICatalogoCache
{
    private readonly ConcurrentDictionary<string, long> _versiones = new();
    private FrozenDictionary<int, CodigoRespuesta> _codigos = FrozenDictionary<int, CodigoRespuesta>.Empty;
    private volatile bool _precargado;

    public bool Precargado => _precargado;

    public async Task<IReadOnlyList<T>> ObtenerAsync<T>(CancellationToken ct = default) where T : class
    {
        var tabla = registro.Tabla<T>();
        var version = _versiones.GetValueOrDefault(tabla);
        var ttl = TimeSpan.FromMinutes(opciones.Value.TtlRespaldoMinutos);

        var instantanea = await cache.GetOrCreateAsync(
            $"cat:{tabla}:v{version}",
            (Tabla: tabla, Fabrica: dbFactory),
            static async (estado, token) =>
            {
                await using var db = await estado.Fabrica.CreateDbContextAsync(token);
                var items = await db.Set<T>().AsNoTracking().ToListAsync(token);
                return new Instantanea<T>(items);
            },
            new HybridCacheEntryOptions { Expiration = ttl, LocalCacheExpiration = ttl },
            cancellationToken: ct);

        if (instantanea.Items is IReadOnlyList<CodigoRespuesta> codigos && !ReferenceEquals(codigos, _ultimaListaCodigos))
        {
            _ultimaListaCodigos = codigos;
            _codigos = codigos.Where(c => c.Activo).ToFrozenDictionary(c => c.Codigo);
        }

        return instantanea.Items;
    }

    private IReadOnlyList<CodigoRespuesta>? _ultimaListaCodigos;

    public CodigoRespuesta? ObtenerCodigoRespuesta(int codigo) => _codigos.GetValueOrDefault(codigo);

    /// <summary>
    /// Usa cat.fn_ProblemasConfiguracionLlaves. La clave depende de las versiones de los tres
    /// catálogos involucrados, así que se recalcula solo cuando alguno cambia.
    /// </summary>
    public async Task<IReadOnlyDictionary<int, string>> ObtenerTiposExpedienteInvalidosAsync(CancellationToken ct = default)
    {
        var clave = string.Join(':', "cat:invalidos",
            _versiones.GetValueOrDefault("TipoExpediente"),
            _versiones.GetValueOrDefault("TipoExpedienteLlave"),
            _versiones.GetValueOrDefault("Llave"));
        var ttl = TimeSpan.FromMinutes(opciones.Value.TtlRespaldoMinutos);

        var instantanea = await cache.GetOrCreateAsync(clave, async token =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(token);
            var problemas = await db.Database.SqlQuery<ProblemaTipoExpediente>($"""
                SELECT te.IdTipoExpediente, te.Codigo, p.Problema
                FROM cat.TipoExpediente te
                CROSS APPLY cat.fn_ProblemasConfiguracionLlaves(te.IdTipoExpediente) p
                WHERE te.Activo = 1
                """).ToListAsync(token);

            foreach (var p in problemas)
            {
                log.LogError("Tipo de expediente {Codigo} excluido por configuración de llaves inválida: {Problema}", p.Codigo, p.Problema);
                bitacora.Encolar(new EntradaBitacora
                {
                    TipoOperacion = Application.Comun.TiposOperacion.ConfiguracionInvalida,
                    Origen = Application.Comun.OrigenesOperacion.Api,
                    CodigoRespuesta = Application.Comun.CodigosRespuesta.TipoExpedienteNoEncontrado,
                    UsuarioServicio = "SISTEMA",
                    Detalle = $"Tipo de expediente {p.Codigo} excluido: {p.Problema}",
                });
            }

            IReadOnlyDictionary<int, string> resultado = problemas
                .GroupBy(p => p.IdTipoExpediente)
                .ToDictionary(g => g.Key, g => string.Join(" ", g.Select(p => p.Problema)));
            return new Instantanea<KeyValuePair<int, string>>([.. resultado]);
        }, new HybridCacheEntryOptions { Expiration = ttl, LocalCacheExpiration = ttl }, cancellationToken: ct);

        return instantanea.Items.ToDictionary(kv => kv.Key, kv => kv.Value);
    }

    private sealed class ProblemaTipoExpediente
    {
        public int IdTipoExpediente { get; set; }
        public string Codigo { get; set; } = string.Empty;
        public string Problema { get; set; } = string.Empty;
    }

    /// <summary>Carga todos los catálogos. Si alguno falla, la instancia no queda lista.</summary>
    internal async Task PrecargarAsync(CancellationToken ct)
    {
        await ActualizarVersionesAsync(ct);
        foreach (var tabla in registro.Tablas)
            await registro.PrecargarAsync(tabla, this, ct);
        await ObtenerTiposExpedienteInvalidosAsync(ct);
        _precargado = true;
        log.LogInformation("Catálogos precargados: {Cantidad}", registro.Tablas.Count);
    }

    /// <summary>Lee cat.VersionCatalogo y recarga los catálogos cuya versión cambió.</summary>
    internal async Task<IReadOnlyList<string>> ActualizarVersionesAsync(CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var versiones = await db.VersionesCatalogo.AsNoTracking()
            .Select(v => new { v.NombreCatalogo, v.Version })
            .ToListAsync(ct);

        var cambiados = new List<string>();
        foreach (var v in versiones)
        {
            if (_versiones.TryGetValue(v.NombreCatalogo, out var actual) && actual == v.Version) continue;
            var eraConocida = _versiones.ContainsKey(v.NombreCatalogo);
            _versiones[v.NombreCatalogo] = v.Version;
            if (eraConocida) cambiados.Add(v.NombreCatalogo);
        }

        foreach (var tabla in cambiados.Where(t => registro.Tablas.Contains(t)))
        {
            log.LogInformation("Catálogo {Tabla} cambió; se recarga", tabla);
            await registro.PrecargarAsync(tabla, this, ct);
        }
        if (cambiados.Count > 0)
            await ObtenerTiposExpedienteInvalidosAsync(ct);

        return cambiados;
    }

    /// <summary>
    /// Contenedor inmutable: HybridCache guarda la misma instancia en memoria en vez de
    /// serializarla en cada lectura.
    /// </summary>
    [ImmutableObject(true)]
    internal sealed class Instantanea<T>(IReadOnlyList<T> items)
    {
        public IReadOnlyList<T> Items { get; } = items;
    }
}
