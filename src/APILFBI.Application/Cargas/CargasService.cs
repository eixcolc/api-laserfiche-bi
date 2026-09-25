using System.Globalization;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using APILFBI.Application.Abstracciones;
using APILFBI.Application.Comun;
using APILFBI.Application.Expedientes;
using APILFBI.Domain.Entidades;
using FluentValidation;

namespace APILFBI.Application.Cargas;

public sealed class SolicitudCargaDocumentoValidator : AbstractValidator<SolicitudCargaDocumento>
{
    public SolicitudCargaDocumentoValidator()
    {
        var obligatorio = CodigosRespuesta.CampoObligatorio.ToString(CultureInfo.InvariantCulture);
        var formato = CodigosRespuesta.FormatoCampoInvalido.ToString(CultureInfo.InvariantCulture);

        RuleFor(x => x.IdTipoDocumento).GreaterThan(0).WithErrorCode(obligatorio).WithMessage("idTipoDocumento es obligatorio.");
        RuleFor(x => x.FechaEmision).NotNull().WithErrorCode(obligatorio).WithMessage("fechaEmision es obligatoria (AAAA-MM-DD).");
        RuleFor(x => x.NombreUsuarioCarga).NotEmpty().WithErrorCode(obligatorio).WithMessage("nombreUsuarioCarga es obligatorio.")
            .MaximumLength(200).WithErrorCode(formato).WithMessage("nombreUsuarioCarga admite 200 caracteres.");
        RuleFor(x => x.Comentario).MaximumLength(1000).WithErrorCode(formato).WithMessage("comentario admite 1000 caracteres.");
        RuleFor(x => x.IdDocumentoReemplaza).GreaterThan(0).When(x => x.IdDocumentoReemplaza is not null)
            .WithErrorCode(formato).WithMessage("idDocumentoReemplaza debe ser un entero positivo.");
        RuleFor(x => x.Correlativo).Matches("^[A-Za-z0-9._-]{1,50}$").When(x => !string.IsNullOrEmpty(x.Correlativo))
            .WithErrorCode(formato).WithMessage("correlativo: hasta 50 caracteres; letras, números, '.', '_' o '-'.");
        RuleFor(x => x.HashSha256).Matches("^[A-Fa-f0-9]{64}$").When(x => !string.IsNullOrEmpty(x.HashSha256))
            .WithErrorCode(formato).WithMessage("hashSha256 debe tener 64 caracteres hexadecimales.");
        RuleFor(x => x).Must(x => x.FechaVencimiento is null || x.FechaEmision is null || x.FechaVencimiento >= x.FechaEmision)
            .OverridePropertyName("fechaVencimiento").WithErrorCode(formato).WithMessage("fechaVencimiento no puede ser anterior a fechaEmision.");
    }
}

/// <summary>
/// Carga de documentos por la API. Valida todo de inmediato (formato, contenido real, tamaño, que el
/// tipo aplique, reemplazo) y deja el par archivo + XML en la carpeta de Import Agent: desde ahí el
/// flujo es el mismo que el de la carga por SFTP (Import Agent + workflow de registro).
/// </summary>
public sealed partial class CargasService(
    IRepositorioCargas repositorio,
    IAlmacenCargas almacen,
    IEscanerAntivirus antivirus,
    ICatalogoCache catalogos,
    IContextoUsuario usuario,
    IValidator<SolicitudCargaDocumento> validador,
    TimeProvider tiempo)
{
    private const int LargoMaximoUsuarioCarga = 50;

    [GeneratedRegex("^[A-Za-z0-9]{1,10}$")]
    private static partial Regex FormatoExtension();

    public async Task<RespuestaCarga> RecibirAsync(long idExpediente, SolicitudCargaDocumento s, ArchivoCarga? archivo, string endpoint,
        CancellationToken ct = default)
    {
        var validacion = await validador.ValidateAsync(s, ct);
        var errores = validacion.Errors.Select(e => new ErrorCampo(Campo(e.PropertyName), e.ErrorMessage)).ToList();
        if (archivo is null || archivo.TamanoBytes == 0)
            errores.Insert(0, new ErrorCampo("archivo", "Debe adjuntar el archivo."));
        if (errores.Count > 0)
        {
            var codigo = archivo is null || archivo.TamanoBytes == 0 ? CodigosRespuesta.CampoObligatorio
                : int.Parse(validacion.Errors[0].ErrorCode, CultureInfo.InvariantCulture);
            throw new ApiException(codigo, errores: errores);
        }

        var usuarioCarga = usuario.UsuarioOperacion
            ?? throw new ApiException(CodigosRespuesta.CampoObligatorio,
                   errores: [new ErrorCampo("X-Operation-User", "Las operaciones de escritura requieren el usuario de operación.")]);
        if (usuarioCarga.Length > LargoMaximoUsuarioCarga)
            throw new ApiException(CodigosRespuesta.FormatoCampoInvalido,
                errores: [new ErrorCampo("X-Operation-User", $"Para cargar documentos admite {LargoMaximoUsuarioCarga} caracteres.")]);

        // Formato: por la extensión y por el contenido real (magic bytes), para no aceptar un archivo renombrado.
        var extension = Path.GetExtension(archivo!.NombreOriginal).TrimStart('.').ToLowerInvariant();
        if (!FormatoExtension().IsMatch(extension))
            throw new ApiException(CodigosRespuesta.TipoArchivoNoPermitido, errores: [new ErrorCampo("archivo", "El archivo debe tener extensión.")]);

        var tipoArchivo = (await catalogos.ObtenerAsync<TipoArchivo>(ct)).FirstOrDefault(t => t.Activo && t.Codigo == extension)
            ?? throw new ApiException(CodigosRespuesta.TipoArchivoNoPermitido, $"El formato .{extension} no está permitido.");
        if (!await CoincideFirmaAsync(archivo.Contenido, tipoArchivo.FirmaBytes, ct))
            throw new ApiException(CodigosRespuesta.TipoArchivoNoPermitido, $"El contenido del archivo no corresponde a un .{extension}.");

        var hash = await CalcularHashAsync(archivo.Contenido, ct);
        if (!string.IsNullOrEmpty(s.HashSha256) && !string.Equals(s.HashSha256, hash, StringComparison.OrdinalIgnoreCase))
            throw new ApiException(CodigosRespuesta.HashNoCoincide);

        var escaneo = await antivirus.EscanearAsync(archivo.Contenido, archivo.NombreOriginal, ct);
        archivo.Contenido.Position = 0;
        if (!escaneo.Limpio)
            throw new ApiException(CodigosRespuesta.ArchivoRechazadoAntivirus, escaneo.Detalle);

        var correlativo = string.IsNullOrEmpty(s.Correlativo) ? GenerarCorrelativo() : s.Correlativo;
        var datos = new DatosXmlCarga(
            idExpediente, correlativo, s.IdTipoDocumento, $"{correlativo}_archivo.{extension}", hash,
            s.FechaEmision!.Value, s.FechaVencimiento, string.IsNullOrWhiteSpace(s.Comentario) ? null : s.Comentario.Trim(),
            s.IdDocumentoReemplaza, usuarioCarga, s.NombreUsuarioCarga!.Trim(), tiempo.GetLocalNow());
        var xml = almacen.GenerarXml(datos);

        var contexto = new ContextoOperacion(usuario.UsuarioServicio, usuarioCarga, usuario.IpOrigen, usuario.CorrelationId, endpoint);
        var resultado = await repositorio.RecibirAsync(datos, archivo.TamanoBytes, xml, contexto, ct);
        if (resultado != CodigosRespuesta.CargaRecibida)
            throw new ApiException(resultado);

        try
        {
            await almacen.EscribirAsync(correlativo, extension, archivo.Contenido, xml, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Sin archivo en la carpeta la carga nunca se importaría: se libera el correlativo.
            await repositorio.AnularAsync(correlativo, CancellationToken.None);
            throw new ApiException(CodigosRespuesta.ErrorInterno, "No se pudo dejar el archivo para Import Agent.");
        }

        return new RespuestaCarga(correlativo, "Recibido", idExpediente, s.IdTipoDocumento, datos.NombreDocumento, archivo.TamanoBytes, hash);
    }

    private string GenerarCorrelativo() =>
        $"API{tiempo.GetUtcNow():yyyyMMddHHmmssfff}{RandomNumberGenerator.GetHexString(4)}";

    private static async Task<bool> CoincideFirmaAsync(Stream contenido, byte[]? firma, CancellationToken ct)
    {
        if (firma is not { Length: > 0 }) return true;
        contenido.Position = 0;
        var inicio = new byte[firma.Length];
        var leidos = await contenido.ReadAtLeastAsync(inicio, firma.Length, throwOnEndOfStream: false, ct);
        contenido.Position = 0;
        return leidos == firma.Length && inicio.AsSpan().SequenceEqual(firma);
    }

    private static async Task<string> CalcularHashAsync(Stream contenido, CancellationToken ct)
    {
        contenido.Position = 0;
        var hash = Convert.ToHexString(await SHA256.HashDataAsync(contenido, ct)).ToLowerInvariant();
        contenido.Position = 0;
        return hash;
    }

    private static string Campo(string propiedad) =>
        string.IsNullOrEmpty(propiedad) ? "body" : char.ToLowerInvariant(propiedad[0]) + propiedad[1..];
}
