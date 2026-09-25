using System.Data;
using APILFBI.Application.Comun;
using APILFBI.Application.Estados;
using APILFBI.Application.Expedientes;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace APILFBI.Infrastructure.Persistencia;

internal sealed class RepositorioEstados(BilfDbContext db) : IRepositorioEstados
{
    /// <summary>
    /// Llama a trx.usp_CambiarEstadoDocumentos sin transacción externa. El stored procedure valida
    /// contra los catálogos, bloquea las filas, registra historial, bitácora y el outbox hacia Laserfiche.
    /// </summary>
    public async Task<ResultadoCambioEstado> CambiarAsync(SolicitudCambioEstado s, ContextoOperacion contexto, CancellationToken ct = default)
    {
        var tabla = new DataTable();
        tabla.Columns.Add("IdDocumento", typeof(int));
        tabla.Columns.Add("CodigoEstado", typeof(string));
        tabla.Columns.Add("CodigoTipoRechazo", typeof(string));
        tabla.Columns.Add("CodigoEtapaRechazo", typeof(string));
        tabla.Columns.Add("Comentario", typeof(string));
        foreach (var d in s.Documentos ?? [])
            tabla.Rows.Add(d.IdDocumento, d.CodigoEstado!.Trim(), Nulo(d.CodigoTipoRechazo), Nulo(d.CodigoEtapaRechazo), Nulo(d.Comentario));

        var codigo = new SqlParameter("@CodigoRespuesta", SqlDbType.Int) { Direction = ParameterDirection.Output };
        var mensaje = new SqlParameter("@Mensaje", SqlDbType.NVarChar, 300) { Direction = ParameterDirection.Output };
        var items = new List<ResultadoCambioEstadoItem>();

        var conexion = (SqlConnection)db.Database.GetDbConnection();
        await db.Database.OpenConnectionAsync(ct);
        try
        {
            await using var cmd = conexion.CreateCommand();
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandText = "trx.usp_CambiarEstadoDocumentos";
            cmd.CommandTimeout = 60;
            cmd.Parameters.Add(new SqlParameter("@IdExpediente", SqlDbType.BigInt) { Value = s.IdExpediente });
            cmd.Parameters.Add(new SqlParameter("@Documentos", SqlDbType.Structured) { TypeName = "trx.TipoCambioEstado", Value = tabla });
            cmd.Parameters.Add(Texto("@UsuarioServicio", contexto.UsuarioServicio, 100));
            cmd.Parameters.Add(Texto("@UsuarioOperacion", contexto.UsuarioOperacion, 100));
            cmd.Parameters.Add(Texto("@EjecutivoAsignado", Nulo(s.EjecutivoAsignado) as string, 200));
            cmd.Parameters.Add(new SqlParameter("@PermitirParcial", SqlDbType.Bit) { Value = s.PermitirParcial });
            cmd.Parameters.Add(new SqlParameter("@IpOrigen", SqlDbType.VarChar, 45) { Value = (object?)contexto.IpOrigen ?? DBNull.Value });
            cmd.Parameters.Add(Texto("@Instancia", Environment.MachineName, 100));
            cmd.Parameters.Add(new SqlParameter("@CorrelationId", SqlDbType.VarChar, 64) { Value = (object?)contexto.CorrelationId ?? DBNull.Value });
            cmd.Parameters.Add(Texto("@Endpoint", contexto.Endpoint, 300));
            cmd.Parameters.AddRange([codigo, mensaje]);

            await using (var r = await cmd.ExecuteReaderAsync(ct))
            {
                while (await r.ReadAsync(ct))
                    items.Add(new ResultadoCambioEstadoItem(
                        IdDocumento: r.GetInt32(0),
                        Aplicado: r.GetBoolean(1),
                        Codigo: r.IsDBNull(2) ? CodigosRespuesta.ErrorInterno : r.GetInt32(2),
                        Mensaje: r.IsDBNull(3) ? null : r.GetString(3),
                        EstadoSolicitado: r.GetString(5),
                        IdSincronizacion: r.IsDBNull(6) ? null : r.GetInt64(6)));
            }
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }

        return new ResultadoCambioEstado(codigo.Value is int c ? c : CodigosRespuesta.ErrorInterno, items);
    }

    private static object Nulo(string? valor) => string.IsNullOrWhiteSpace(valor) ? DBNull.Value : valor.Trim();

    private static SqlParameter Texto(string nombre, string? valor, int largo) =>
        new(nombre, SqlDbType.NVarChar, largo) { Value = (object?)valor ?? DBNull.Value };
}
