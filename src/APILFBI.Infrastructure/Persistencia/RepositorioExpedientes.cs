using System.Data;
using APILFBI.Application.Expedientes;
using APILFBI.Domain.Entidades;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace APILFBI.Infrastructure.Persistencia;

internal sealed class RepositorioExpedientes(BilfDbContext db) : IRepositorioExpedientes
{
    /// <summary>
    /// Llama a trx.usp_ObtenerOCrearExpediente sin transacción externa. El stored procedure valida,
    /// maneja la concurrencia entre instancias y registra la bitácora.
    /// </summary>
    public Task<ResultadoObtenerOCrear> ObtenerOCrearAsync(int idTipoExpediente, IReadOnlyList<LlaveValor> llaves,
        ContextoOperacion contexto, CancellationToken ct = default) =>
        EjecutarAsync(idTipoExpediente, llaves, contexto, soloBuscar: false, ct);

    public Task<ResultadoObtenerOCrear> BuscarAsync(int idTipoExpediente, IReadOnlyList<LlaveValor> llaves,
        ContextoOperacion contexto, CancellationToken ct = default) =>
        EjecutarAsync(idTipoExpediente, llaves, contexto, soloBuscar: true, ct);

    private async Task<ResultadoObtenerOCrear> EjecutarAsync(int idTipoExpediente, IReadOnlyList<LlaveValor> llaves,
        ContextoOperacion contexto, bool soloBuscar, CancellationToken ct)
    {
        var tabla = new DataTable();
        tabla.Columns.Add("CodigoLlave", typeof(string));
        tabla.Columns.Add("Valor", typeof(string));
        foreach (var llave in llaves)
            tabla.Rows.Add(llave.Tipo, (object?)llave.Valor ?? DBNull.Value);

        var idExpediente = new SqlParameter("@IdExpediente", SqlDbType.BigInt) { Direction = ParameterDirection.Output };
        var esNuevo = new SqlParameter("@EsNuevo", SqlDbType.Bit) { Direction = ParameterDirection.Output };
        var codigo = new SqlParameter("@CodigoRespuesta", SqlDbType.Int) { Direction = ParameterDirection.Output };
        var mensaje = new SqlParameter("@Mensaje", SqlDbType.NVarChar, 300) { Direction = ParameterDirection.Output };

        var errores = new List<ErrorLlave>();
        var conexion = (SqlConnection)db.Database.GetDbConnection();
        await db.Database.OpenConnectionAsync(ct);
        try
        {
            await using var cmd = conexion.CreateCommand();
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandText = "trx.usp_ObtenerOCrearExpediente";
            cmd.CommandTimeout = 30;
            cmd.Parameters.Add(new SqlParameter("@IdTipoExpediente", SqlDbType.Int) { Value = idTipoExpediente });
            cmd.Parameters.Add(new SqlParameter("@Llaves", SqlDbType.Structured) { TypeName = "trx.TipoLlaveValor", Value = tabla });
            cmd.Parameters.Add(Texto("@UsuarioServicio", contexto.UsuarioServicio, 100));
            cmd.Parameters.Add(Texto("@UsuarioOperacion", contexto.UsuarioOperacion, 100));
            cmd.Parameters.Add(new SqlParameter("@IpOrigen", SqlDbType.VarChar, 45) { Value = (object?)contexto.IpOrigen ?? DBNull.Value });
            cmd.Parameters.Add(Texto("@Instancia", Environment.MachineName, 100));
            cmd.Parameters.Add(new SqlParameter("@CorrelationId", SqlDbType.VarChar, 64) { Value = (object?)contexto.CorrelationId ?? DBNull.Value });
            cmd.Parameters.Add(Texto("@Endpoint", contexto.Endpoint, 300));
            cmd.Parameters.Add(new SqlParameter("@SoloBuscar", SqlDbType.Bit) { Value = soloBuscar });
            cmd.Parameters.AddRange([idExpediente, esNuevo, codigo, mensaje]);

            await using (var lector = await cmd.ExecuteReaderAsync(ct))
            {
                while (await lector.ReadAsync(ct))
                    errores.Add(new ErrorLlave(
                        lector.IsDBNull(0) ? null : lector.GetString(0),
                        lector.GetInt32(1),
                        lector.GetString(2)));
            }
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }

        return new ResultadoObtenerOCrear(
            codigo.Value is int c ? c : Application.Comun.CodigosRespuesta.ErrorInterno,
            idExpediente.Value is long id ? id : null,
            esNuevo.Value is true,
            errores);
    }

    public Task<Expediente?> ObtenerAsync(long idExpediente, CancellationToken ct = default) =>
        db.Set<Expediente>()
            .Include(e => e.Llaves.Where(l => l.Vigente))
            .FirstOrDefaultAsync(e => e.Id == idExpediente, ct);

    private static SqlParameter Texto(string nombre, string? valor, int largo) =>
        new(nombre, SqlDbType.NVarChar, largo) { Value = (object?)valor ?? DBNull.Value };
}
