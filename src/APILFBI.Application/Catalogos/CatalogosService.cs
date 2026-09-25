using APILFBI.Application.Abstracciones;
using APILFBI.Application.Comun;
using APILFBI.Domain.Entidades;

namespace APILFBI.Application.Catalogos;

public sealed record TipoExpedienteDto(int IdTipoExpediente, string Codigo, string NombreTipoExpediente);

public sealed record TipoDocumentoDto(
    int IdTipoDocumento, string Codigo, string NombreTipoDocumento, string Estado,
    int TamanoMaximoMB, int? DiasVigencia, string ReglaCarga, IReadOnlyList<string> TiposArchivoPermitidos);

public sealed record DocumentoDeTipoExpedienteDto(int IdTipoDocumento, string NombreTipoDocumento, bool Obligatorio, string AplicaA);

public sealed record TiposDocumentoDeExpedienteDto(int IdTipoExpediente, string NombreTipoExpediente, IReadOnlyList<DocumentoDeTipoExpedienteDto> TiposDocumento);

public sealed record LlaveConfiguradaDto(
    string Codigo, string Nombre, bool Obligatoria, int? GrupoIdentificacion, int? PrioridadGrupo,
    string TipoDato, int LongitudMaxima, string? ExpresionValidacion, IReadOnlyList<string>? ValoresPermitidos);

public sealed record LlavesPorTipoClienteDto(string TipoCliente, IReadOnlyList<LlaveConfiguradaDto> Llaves);

public sealed record ItemCatalogoDto(string Codigo, string Nombre);

/// <summary>Consultas de catálogos (criterios 3, 4, 5 y listas del criterio 10). Todo sale de la caché.</summary>
public sealed class CatalogosService(ICatalogoCache catalogos)
{
    /// <summary>Nombres de ruta de GET /catalogos/{nombre}.</summary>
    public static readonly IReadOnlyList<string> ListasDisponibles =
        ["estados-documento", "tipos-rechazo", "etapas-rechazo", "tipos-identificacion", "segmentaciones", "tipos-archivo", "tipos-cliente"];

    /// <summary>Criterio 5. Excluye los tipos con configuración de llaves inválida.</summary>
    public async Task<IReadOnlyList<TipoExpedienteDto>> TiposExpedienteAsync(CancellationToken ct = default)
    {
        var invalidos = await catalogos.ObtenerTiposExpedienteInvalidosAsync(ct);
        return (await catalogos.ObtenerAsync<TipoExpediente>(ct))
            .Where(t => t.Activo && !invalidos.ContainsKey(t.Id))
            .OrderBy(t => t.Orden).ThenBy(t => t.Nombre)
            .Select(t => new TipoExpedienteDto(t.Id, t.Codigo, t.Nombre))
            .ToList();
    }

    /// <summary>Criterio 4: todos los tipos de documento, con su estado.</summary>
    public async Task<IReadOnlyList<TipoDocumentoDto>> TiposDocumentoAsync(CancellationToken ct = default)
    {
        var archivos = (await catalogos.ObtenerAsync<TipoArchivo>(ct)).ToDictionary(a => a.Id);
        var permitidos = (await catalogos.ObtenerAsync<TipoDocumentoTipoArchivo>(ct))
            .Where(p => p.Activo && archivos.TryGetValue(p.IdTipoArchivo, out var a) && a.Activo)
            .ToLookup(p => p.IdTipoDocumento, p => archivos[p.IdTipoArchivo]);
        var reglas = (await catalogos.ObtenerAsync<ReglaCarga>(ct)).ToDictionary(r => r.Id, r => r.Codigo);

        return (await catalogos.ObtenerAsync<TipoDocumento>(ct))
            .OrderBy(t => t.Orden).ThenBy(t => t.Id)
            .Select(t => new TipoDocumentoDto(
                t.Id, t.Codigo, t.Nombre, t.Activo ? "Activo" : "Inactivo", t.TamanoMaximoMB, t.DiasVigencia,
                reglas.GetValueOrDefault(t.IdReglaCarga, "?"),
                permitidos[t.Id].OrderBy(a => a.Orden).Select(a => a.Codigo).ToList()))
            .ToList();
    }

    /// <summary>Criterio 3: tipos de documento de un tipo de expediente (por id o por nombre).</summary>
    public async Task<TiposDocumentoDeExpedienteDto> TiposDocumentoDeExpedienteAsync(
        int? idTipoExpediente, string? nombreTipoExpediente, string? tipoCliente, CancellationToken ct = default)
    {
        var tipo = await BuscarTipoExpedienteAsync(idTipoExpediente, nombreTipoExpediente, ct);
        var cliente = await ResolverTipoClienteAsync(tipoCliente, ct);
        var clientes = (await catalogos.ObtenerAsync<TipoCliente>(ct)).ToDictionary(c => c.Id, c => c.Codigo);
        var documentos = (await catalogos.ObtenerAsync<TipoDocumento>(ct)).Where(d => d.Activo).ToDictionary(d => d.Id);

        var filas = (await catalogos.ObtenerAsync<TipoExpedienteTipoDocumento>(ct))
            .Where(r => r.Activo && r.IdTipoExpediente == tipo.Id && documentos.ContainsKey(r.IdTipoDocumento))
            .Where(r => cliente is null || r.IdTipoCliente is null || r.IdTipoCliente == cliente.Id)
            .OrderBy(r => r.Orden).ThenBy(r => r.IdTipoDocumento)
            .Select(r => new DocumentoDeTipoExpedienteDto(
                r.IdTipoDocumento, documentos[r.IdTipoDocumento].Nombre, r.Obligatorio,
                r.IdTipoCliente is null ? "Ambos" : clientes.GetValueOrDefault(r.IdTipoCliente.Value, "?")))
            .ToList();

        return new TiposDocumentoDeExpedienteDto(tipo.Id, tipo.Nombre, filas);
    }

    /// <summary>Llaves que el CRM debe enviar para un tipo de expediente.</summary>
    public async Task<IReadOnlyList<LlavesPorTipoClienteDto>> LlavesAsync(int idTipoExpediente, string? tipoCliente, CancellationToken ct = default)
    {
        var tipo = await BuscarTipoExpedienteAsync(idTipoExpediente, null, ct);
        var cliente = await ResolverTipoClienteAsync(tipoCliente, ct);
        var clientes = (await catalogos.ObtenerAsync<TipoCliente>(ct)).ToDictionary(c => c.Id, c => c.Codigo);
        var llaves = (await catalogos.ObtenerAsync<Llave>(ct)).Where(l => l.Activo).ToDictionary(l => l.Id);
        var tiposDato = (await catalogos.ObtenerAsync<TipoDato>(ct)).ToDictionary(t => t.Id, t => t.Codigo);

        var resultado = new List<LlavesPorTipoClienteDto>();
        foreach (var grupoCliente in (await catalogos.ObtenerAsync<TipoExpedienteLlave>(ct))
                     .Where(c => c.Activo && c.IdTipoExpediente == tipo.Id && llaves.ContainsKey(c.IdLlave))
                     .Where(c => cliente is null || c.IdTipoCliente == cliente.Id)
                     .GroupBy(c => c.IdTipoCliente)
                     .OrderBy(g => g.Key))
        {
            var items = new List<LlaveConfiguradaDto>();
            foreach (var c in grupoCliente.OrderBy(c => c.Orden).ThenBy(c => c.GrupoIdentificacion))
            {
                var l = llaves[c.IdLlave];
                items.Add(new LlaveConfiguradaDto(
                    l.Codigo, l.Nombre, c.Obligatoria, c.GrupoIdentificacion, c.PrioridadGrupo,
                    tiposDato.GetValueOrDefault(l.IdTipoDato, "?"), l.LongitudMaxima, l.ExpresionValidacion,
                    await ValoresPermitidosAsync(l.CatalogoValidacion, ct)));
            }
            resultado.Add(new LlavesPorTipoClienteDto(clientes.GetValueOrDefault(grupoCliente.Key, "?"), items));
        }

        return resultado;
    }

    /// <summary>Listas para los campos del CRM (estados, tipos de rechazo, etc.): solo registros activos.</summary>
    public async Task<IReadOnlyList<ItemCatalogoDto>> ListaAsync(string nombre, CancellationToken ct = default) => nombre switch
    {
        "estados-documento" => Items((await catalogos.ObtenerAsync<EstadoDocumento>(ct)).Where(e => !e.EsDerivado)),
        "tipos-rechazo" => Items(await catalogos.ObtenerAsync<TipoRechazo>(ct)),
        "etapas-rechazo" => Items(await catalogos.ObtenerAsync<EtapaRechazo>(ct)),
        "tipos-identificacion" => Items(await catalogos.ObtenerAsync<TipoIdentificacion>(ct)),
        "segmentaciones" => Items(await catalogos.ObtenerAsync<Segmentacion>(ct)),
        "tipos-archivo" => Items(await catalogos.ObtenerAsync<TipoArchivo>(ct)),
        "tipos-cliente" => Items(await catalogos.ObtenerAsync<TipoCliente>(ct)),
        _ => throw new ApiException(CodigosRespuesta.SolicitudInvalida,
                 $"Catálogo desconocido. Disponibles: {string.Join(", ", ListasDisponibles)}."),
    };

    private static List<ItemCatalogoDto> Items(IEnumerable<Catalogo> items) =>
        items.Where(i => i.Activo).OrderBy(i => i.Orden).ThenBy(i => i.Nombre).Select(i => new ItemCatalogoDto(i.Codigo, i.Nombre)).ToList();

    private async Task<IReadOnlyList<string>?> ValoresPermitidosAsync(string? catalogoValidacion, CancellationToken ct) =>
        catalogoValidacion switch
        {
            "cat.TipoCliente" => Items(await catalogos.ObtenerAsync<TipoCliente>(ct)).Select(i => i.Codigo).ToList(),
            "cat.TipoIdentificacion" => Items(await catalogos.ObtenerAsync<TipoIdentificacion>(ct)).Select(i => i.Codigo).ToList(),
            "cat.Segmentacion" => Items(await catalogos.ObtenerAsync<Segmentacion>(ct)).Select(i => i.Codigo).ToList(),
            _ => null,
        };

    private async Task<TipoExpediente> BuscarTipoExpedienteAsync(int? id, string? nombre, CancellationToken ct)
    {
        if (id is null && string.IsNullOrWhiteSpace(nombre))
            throw new ApiException(CodigosRespuesta.CampoObligatorio,
                errores: [new ErrorCampo("nombreTipoExpediente", "Indique el id o el nombre del tipo de expediente.")]);

        var invalidos = await catalogos.ObtenerTiposExpedienteInvalidosAsync(ct);
        var tipo = (await catalogos.ObtenerAsync<TipoExpediente>(ct)).FirstOrDefault(t =>
            t.Activo && !invalidos.ContainsKey(t.Id)
            && (id is not null ? t.Id == id : string.Equals(t.Nombre, nombre!.Trim(), StringComparison.OrdinalIgnoreCase)
                                              || string.Equals(t.Codigo, nombre.Trim(), StringComparison.OrdinalIgnoreCase)));

        return tipo ?? throw new ApiException(CodigosRespuesta.TipoExpedienteNoEncontrado);
    }

    private async Task<TipoCliente?> ResolverTipoClienteAsync(string? codigo, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(codigo)) return null;
        return (await catalogos.ObtenerAsync<TipoCliente>(ct)).FirstOrDefault(c => c.Activo && string.Equals(c.Codigo, codigo.Trim(), StringComparison.OrdinalIgnoreCase))
               ?? throw new ApiException(CodigosRespuesta.ValorFueraCatalogo,
                      errores: [new ErrorCampo("tipoCliente", "Use N (natural) o J (jurídico).")]);
    }
}
