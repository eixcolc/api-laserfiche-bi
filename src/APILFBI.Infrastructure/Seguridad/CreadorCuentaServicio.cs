using APILFBI.Domain.Entidades;
using APILFBI.Infrastructure.Persistencia;
using Microsoft.EntityFrameworkCore;

namespace APILFBI.Infrastructure.Seguridad;

/// <summary>
/// Crea una cuenta de servicio (ej. la del CRM) o le rota el secreto. Devuelve el secreto en claro
/// una sola vez; en la base solo queda el hash.
/// </summary>
public sealed class CreadorCuentaServicio(BilfDbContext db)
{
    public async Task<string> CrearORotarAsync(string clientId, string nombre, IReadOnlyCollection<string> scopes,
        string usuario, CancellationToken ct = default)
    {
        var scopesDb = await db.Scopes.Where(s => scopes.Contains(s.Codigo) && s.Activo).ToListAsync(ct);
        var faltantes = scopes.Except(scopesDb.Select(s => s.Codigo)).ToList();
        if (faltantes.Count > 0)
            throw new InvalidOperationException($"Scopes inexistentes o inactivos: {string.Join(", ", faltantes)}");

        var secreto = HashSecreto.GenerarSecreto();
        var cuenta = await db.CuentasServicio.Include(c => c.Scopes).FirstOrDefaultAsync(c => c.ClientId == clientId, ct);

        if (cuenta is null)
        {
            cuenta = new CuentaServicio { ClientId = clientId, Nombre = nombre, Activo = true, CreadoPor = usuario, FechaCreacion = DateTime.UtcNow };
            db.CuentasServicio.Add(cuenta);
        }

        cuenta.Nombre = nombre;
        cuenta.SecretHash = HashSecreto.Calcular(secreto);
        cuenta.Activo = true;
        cuenta.Scopes.Clear();
        cuenta.Scopes.AddRange(scopesDb.Select(s => new CuentaServicioScope { IdScope = s.Id, CreadoPor = usuario }));

        await db.SaveChangesAsync(ct);
        return secreto;
    }
}
