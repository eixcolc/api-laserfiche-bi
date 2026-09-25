using APILFBI.Application.Abstracciones;
using APILFBI.Application.Cargas;
using APILFBI.Application.Comun;
using APILFBI.Application.Expedientes;
using APILFBI.Domain.Entidades;
using APILFBI.Infrastructure.Laserfiche;

namespace APILFBI.Tests.Unitarias;

public sealed class CargasServiceTests
{
    private sealed class RepositorioFalso : IRepositorioCargas
    {
        public List<string> Anulados { get; } = [];
        public Task<int> RecibirAsync(DatosXmlCarga datos, long tamanoBytes, string xml, ContextoOperacion contexto, CancellationToken ct = default) =>
            Task.FromResult(CodigosRespuesta.CargaRecibida);
        public Task AnularAsync(string correlativo, CancellationToken ct = default) { Anulados.Add(correlativo); return Task.CompletedTask; }
    }

    private sealed class AlmacenQueFalla : IAlmacenCargas
    {
        public string GenerarXml(DatosXmlCarga datos) => "<expediente/>";
        public Task EscribirAsync(string correlativo, string extension, Stream archivo, string xml, CancellationToken ct = default) =>
            throw new IOException("La carpeta de Import Agent no está disponible");
    }

    private sealed class AlmacenOk : IAlmacenCargas
    {
        public string GenerarXml(DatosXmlCarga datos) => "<expediente/>";
        public Task EscribirAsync(string correlativo, string extension, Stream archivo, string xml, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class Antivirus(bool limpio) : IEscanerAntivirus
    {
        public Task<ResultadoEscaneo> EscanearAsync(Stream contenido, string nombreArchivo, CancellationToken ct = default) =>
            Task.FromResult(new ResultadoEscaneo(limpio, limpio ? null : "Eicar-Test-Signature"));
    }

    private sealed class Catalogos : ICatalogoCache
    {
        public bool Precargado => true;
        public Task<IReadOnlyList<T>> ObtenerAsync<T>(CancellationToken ct = default) where T : class =>
            Task.FromResult((IReadOnlyList<T>)(object)new List<TipoArchivo>
            {
                new() { Id = 1, Codigo = "pdf", Activo = true, MimeType = "application/pdf", FirmaBytes = [0x25, 0x50, 0x44, 0x46] },
            });
        public CodigoRespuesta? ObtenerCodigoRespuesta(int codigo) => null;
        public Task<IReadOnlyDictionary<int, string>> ObtenerTiposExpedienteInvalidosAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyDictionary<int, string>>(new Dictionary<int, string>());
    }

    private sealed class Usuario : IContextoUsuario
    {
        public string UsuarioServicio => "crm";
        public string? UsuarioOperacion => "51451";
        public IReadOnlyCollection<string> Scopes => [];
        public string? CorrelationId => "prueba";
        public string? IpOrigen => null;
    }

    private static CargasService Servicio(IRepositorioCargas repo, IAlmacenCargas almacen, bool antivirusLimpio = true) =>
        new(repo, almacen, new Antivirus(antivirusLimpio), new Catalogos(), new Usuario(), new SolicitudCargaDocumentoValidator(), TimeProvider.System);

    private static SolicitudCargaDocumento Solicitud() =>
        new(32, new DateOnly(2026, 9, 1), null, null, null, "Usuario Prueba", "C-1", null);

    private static ArchivoCarga Pdf()
    {
        var bytes = LaserficheSimulado.GenerarPdf("prueba");
        return new ArchivoCarga(new MemoryStream(bytes), "estados.pdf", bytes.Length);
    }

    [Fact]
    public async Task Si_no_se_puede_dejar_el_archivo_para_import_agent_se_libera_el_correlativo()
    {
        var repo = new RepositorioFalso();

        var ex = await Assert.ThrowsAsync<ApiException>(() => Servicio(repo, new AlmacenQueFalla()).RecibirAsync(1, Solicitud(), Pdf(), "/x"));

        Assert.Equal(CodigosRespuesta.ErrorInterno, ex.Codigo);
        Assert.Equal(["C-1"], repo.Anulados);
    }

    [Fact]
    public async Task Archivo_rechazado_por_el_antivirus_devuelve_413_sin_reservar_nada()
    {
        var repo = new RepositorioFalso();

        var ex = await Assert.ThrowsAsync<ApiException>(() => Servicio(repo, new AlmacenOk(), antivirusLimpio: false).RecibirAsync(1, Solicitud(), Pdf(), "/x"));

        Assert.Equal(CodigosRespuesta.ArchivoRechazadoAntivirus, ex.Codigo);
        Assert.Empty(repo.Anulados);
    }

    [Fact]
    public async Task Carga_valida_devuelve_el_correlativo_el_hash_y_el_nombre_del_documento()
    {
        var respuesta = await Servicio(new RepositorioFalso(), new AlmacenOk()).RecibirAsync(7, Solicitud(), Pdf(), "/x");

        Assert.Equal("C-1", respuesta.Correlativo);
        Assert.Equal("Recibido", respuesta.EstadoCarga);
        Assert.Equal("C-1_archivo.pdf", respuesta.NombreDocumento);
        Assert.Equal(64, respuesta.HashSha256.Length);
    }
}
