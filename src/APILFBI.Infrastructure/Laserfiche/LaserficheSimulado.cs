using System.Text;
using APILFBI.Application.Abstracciones;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace APILFBI.Infrastructure.Laserfiche;

/// <summary>
/// Laserfiche simulado (Laserfiche:Simulado = true). Entrega el archivo {entryId}.* de la carpeta
/// configurada o, si no existe, un PDF generado. Los streams son navegables, así que el header
/// Range lo resuelve ASP.NET.
/// </summary>
internal sealed class LaserficheSimulado(IOptions<LaserficheOptions> opciones, IHostEnvironment entorno, ILogger<LaserficheSimulado> log) : ILaserficheRepositoryService
{
    public Task<ContenidoDocumento> DescargarAsync(int entryId, string? rango, CancellationToken ct = default)
    {
        // Una ruta relativa se toma desde la carpeta del proyecto (content root).
        var carpeta = opciones.Value.Simulacion.CarpetaArchivos;
        if (!string.IsNullOrWhiteSpace(carpeta))
            carpeta = Path.GetFullPath(Path.Combine(entorno.ContentRootPath, carpeta));

        if (!string.IsNullOrWhiteSpace(carpeta) && Directory.Exists(carpeta))
        {
            var archivo = Directory.EnumerateFiles(carpeta, $"{entryId}.*").FirstOrDefault();
            if (archivo is not null)
            {
                var stream = File.OpenRead(archivo);
                return Task.FromResult(new ContenidoDocumento(stream, null, stream.Length, false, null, null));
            }
        }

        var pdf = GenerarPdf($"Documento simulado - Laserfiche EntryId {entryId}");
        return Task.FromResult(new ContenidoDocumento(new MemoryStream(pdf, writable: false), "application/pdf", pdf.Length, false, null, null));
    }

    public Task ActualizarCamposAsync(int entryId, IReadOnlyDictionary<string, string?> campos, CancellationToken ct = default)
    {
        log.LogInformation("[Laserfiche simulado] Entrada {EntryId}: {Campos}", entryId,
            string.Join(", ", campos.Select(c => $"{c.Key}={c.Value}")));
        return Task.CompletedTask;
    }

    public Task<(bool Disponible, string Detalle)> VerificarAsync(CancellationToken ct = default) =>
        Task.FromResult((true, "Laserfiche simulado"));

    /// <summary>PDF mínimo válido de una página con una línea de texto.</summary>
    internal static byte[] GenerarPdf(string texto)
    {
        var textoSeguro = texto.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");
        var flujo = $"BT /F1 18 Tf 72 720 Td ({textoSeguro}) Tj ET";
        string[] objetos =
        [
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 4 0 R /Resources << /Font << /F1 5 0 R >> >> >>",
            $"<< /Length {Encoding.ASCII.GetByteCount(flujo)} >>\nstream\n{flujo}\nendstream",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
        ];

        var sb = new StringBuilder("%PDF-1.4\n");
        var posiciones = new List<int>();
        for (var i = 0; i < objetos.Length; i++)
        {
            posiciones.Add(Encoding.ASCII.GetByteCount(sb.ToString()));
            sb.Append($"{i + 1} 0 obj\n{objetos[i]}\nendobj\n");
        }

        var inicioXref = Encoding.ASCII.GetByteCount(sb.ToString());
        sb.Append($"xref\n0 {objetos.Length + 1}\n0000000000 65535 f \n");
        foreach (var p in posiciones) sb.Append($"{p:D10} 00000 n \n");
        sb.Append($"trailer\n<< /Size {objetos.Length + 1} /Root 1 0 R >>\nstartxref\n{inicioXref}\n%%EOF\n");
        return Encoding.ASCII.GetBytes(sb.ToString());
    }
}
