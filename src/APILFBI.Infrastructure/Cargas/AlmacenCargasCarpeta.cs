using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Schema;
using APILFBI.Application.Cargas;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace APILFBI.Infrastructure.Cargas;

public sealed class CargaApiOptions
{
    public const string Seccion = "CargaApi";

    /// <summary>
    /// Carpeta que vigila Import Agent (en producción, una ruta UNC compartida por todas las
    /// instancias). Una ruta relativa se toma desde la carpeta del proyecto.
    /// </summary>
    [Required] public string CarpetaImportAgent { get; set; } = string.Empty;

    /// <summary>Tamaño máximo de la solicitud de carga (archivo + campos). El balanceador debe permitirlo.</summary>
    [Range(1, 2048)] public int TamanoMaximoSolicitudMB { get; set; } = 50;

    /// <summary>El Worker alerta si una carga sigue Recibida después de estos minutos (Import Agent o el workflow no la procesaron).</summary>
    [Range(1, 10080)] public int AlertaRecibidasMinutos { get; set; } = 60;
}

/// <summary>
/// Deja el par {Correlativo}_archivo.{ext} + {Correlativo}_data.xml en la carpeta de Import Agent, con
/// las mismas reglas que el contrato SFTP: primero el archivo y al final el XML, ambos como .tmp, y luego
/// se renombran. Así Import Agent nunca toma un par incompleto.
/// </summary>
internal sealed class AlmacenCargasCarpeta(IOptions<CargaApiOptions> opciones, IHostEnvironment entorno) : IAlmacenCargas
{
    private static readonly Lazy<XmlSchemaSet> Esquema = new(() =>
    {
        using var xsd = typeof(AlmacenCargasCarpeta).Assembly.GetManifestResourceStream("APILFBI.expediente-v1.xsd")
                        ?? throw new InvalidOperationException("No se encontró el XSD embebido del contrato de carga.");
        var esquemas = new XmlSchemaSet();
        esquemas.Add(null, XmlReader.Create(xsd));
        esquemas.Compile();
        return esquemas;
    });

    public string GenerarXml(DatosXmlCarga d)
    {
        var sb = new StringBuilder();
        var config = new XmlWriterSettings { Indent = true, Encoding = new UTF8Encoding(false), OmitXmlDeclaration = true };
        using (var w = XmlWriter.Create(sb, config))
        {
            w.WriteStartElement("expediente");
            w.WriteAttributeString("version", "1.0");
            w.WriteElementString("idExpediente", d.IdExpediente.ToString(CultureInfo.InvariantCulture));
            w.WriteStartElement("informacionDocumento");
            w.WriteElementString("correlativo", d.Correlativo);
            w.WriteElementString("tipoDocumento", d.IdTipoDocumento.ToString(CultureInfo.InvariantCulture));
            w.WriteElementString("nombreDocumento", d.NombreDocumento);
            w.WriteElementString("hashSha256", d.HashSha256);
            w.WriteElementString("fechaEmision", d.FechaEmision.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            w.WriteElementString("fechaVencimiento", d.FechaVencimiento?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty);
            w.WriteElementString("comentario", d.Comentario ?? string.Empty);
            w.WriteElementString("idDocumentoReemplaza", d.IdDocumentoReemplaza?.ToString(CultureInfo.InvariantCulture) ?? string.Empty);
            w.WriteElementString("usuarioCarga", d.UsuarioCarga);
            w.WriteElementString("nombreUsuarioCarga", d.NombreUsuarioCarga);
            w.WriteElementString("fechaHoraCarga", d.FechaHoraCarga.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture));
            w.WriteEndElement();
            w.WriteEndElement();
        }

        var xml = sb.ToString();
        Validar(xml);
        return xml;
    }

    public async Task EscribirAsync(string correlativo, string extension, Stream archivo, string xml, CancellationToken ct = default)
    {
        var carpeta = Path.GetFullPath(Path.Combine(entorno.ContentRootPath, opciones.Value.CarpetaImportAgent));
        Directory.CreateDirectory(carpeta);
        var rutaArchivo = Path.Combine(carpeta, $"{correlativo}_archivo.{extension}");
        var rutaXml = Path.Combine(carpeta, $"{correlativo}_data.xml");

        try
        {
            archivo.Position = 0;
            await using (var destino = new FileStream(rutaArchivo + ".tmp", FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
                await archivo.CopyToAsync(destino, ct);

            await File.WriteAllTextAsync(rutaXml + ".tmp",
                "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" + Environment.NewLine + xml, new UTF8Encoding(false), ct);

            File.Move(rutaArchivo + ".tmp", rutaArchivo);
            File.Move(rutaXml + ".tmp", rutaXml);
        }
        catch
        {
            foreach (var ruta in new[] { rutaArchivo + ".tmp", rutaXml + ".tmp", rutaXml })
                try { File.Delete(ruta); } catch { /* se limpia lo que se pueda */ }
            throw;
        }
    }

    private static void Validar(string xml)
    {
        var errores = new List<string>();
        var config = new XmlReaderSettings { ValidationType = ValidationType.Schema, Schemas = Esquema.Value };
        config.ValidationEventHandler += (_, e) => errores.Add(e.Message);
        using (var lector = XmlReader.Create(new StringReader(xml), config))
            while (lector.Read()) { }

        if (errores.Count > 0)
            throw new InvalidOperationException($"El XML de carga generado no cumple el contrato: {errores[0]}");
    }
}

/// <summary>No escanea. Se reemplaza registrando otra implementación de IEscanerAntivirus.</summary>
internal sealed class EscanerAntivirusDeshabilitado : IEscanerAntivirus
{
    public Task<ResultadoEscaneo> EscanearAsync(Stream contenido, string nombreArchivo, CancellationToken ct = default) =>
        Task.FromResult(new ResultadoEscaneo(true));
}
