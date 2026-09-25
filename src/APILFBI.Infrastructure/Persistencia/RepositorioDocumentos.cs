using APILFBI.Application.Documentos;
using APILFBI.Domain.Entidades;
using Microsoft.EntityFrameworkCore;

namespace APILFBI.Infrastructure.Persistencia;

internal sealed class RepositorioDocumentos(BilfDbContext db) : IRepositorioDocumentos
{
    public async Task<IReadOnlyList<DocumentoExpedienteFila>> DocumentosDeExpedienteAsync(long idExpediente, CancellationToken ct = default) =>
        await (from ed in db.Set<ExpedienteDocumento>()
               join d in db.Set<Documento>() on ed.IdDocumento equals d.Id
               where ed.IdExpediente == idExpediente && ed.Vigente
               select new DocumentoExpedienteFila(d.Id, d.LaserficheEntryId, d.IdTipoDocumento, d.IdEstadoDocumento, d.Version,
                   d.FechaHoraRecepcion, d.FechaVencimiento, d.NombreUsuarioCarga, d.Comentario))
            .ToListAsync(ct);

    public Task<DocumentoArchivo?> ObtenerPorEntryIdAsync(int laserficheEntryId, CancellationToken ct = default) =>
        db.Set<Documento>()
            .Where(d => d.LaserficheEntryId == laserficheEntryId)
            .Select(d => new DocumentoArchivo(d.Id, d.LaserficheEntryId, d.IdExpedienteOrigen, d.NombreArchivo, d.IdTipoArchivo))
            .FirstOrDefaultAsync(ct);

    public Task<CargaDocumento?> ObtenerCargaAsync(string correlativo, CancellationToken ct = default) =>
        db.Set<CargaDocumento>().FirstOrDefaultAsync(c => c.Correlativo == correlativo, ct);
}
