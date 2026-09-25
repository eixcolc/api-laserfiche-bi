using System.Data;
using APILFBI.Application.Cargas;
using APILFBI.Application.Comun;
using APILFBI.Application.Expedientes;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace APILFBI.Infrastructure.Persistencia;

internal sealed class RepositorioCargas(BilfDbContext db) : IRepositorioCargas
{
    public async Task<int> RecibirAsync(DatosXmlCarga datos, long tamanoBytes, string xml, ContextoOperacion contexto, CancellationToken ct = default)
    {
        var codigo = new SqlParameter("@CodigoRespuesta", SqlDbType.Int) { Direction = ParameterDirection.Output };
        var conexion = (SqlConnection)db.Database.GetDbConnection();
        await db.Database.OpenConnectionAsync(ct);
        try
        {
            await using var cmd = conexion.CreateCommand();
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandText = "trx.usp_RecibirCarga";
            cmd.Parameters.Add(new SqlParameter("@IdExpediente", SqlDbType.BigInt) { Value = datos.IdExpediente });
            cmd.Parameters.Add(new SqlParameter("@Correlativo", SqlDbType.VarChar, 50) { Value = datos.Correlativo });
            cmd.Parameters.Add(new SqlParameter("@IdTipoDocumento", SqlDbType.Int) { Value = datos.IdTipoDocumento });
            cmd.Parameters.Add(new SqlParameter("@NombreDocumento", SqlDbType.NVarChar, 260) { Value = datos.NombreDocumento });
            cmd.Parameters.Add(new SqlParameter("@TamanoBytes", SqlDbType.BigInt) { Value = tamanoBytes });
            cmd.Parameters.Add(new SqlParameter("@UsuarioServicio", SqlDbType.NVarChar, 100) { Value = contexto.UsuarioServicio });
            cmd.Parameters.Add(new SqlParameter("@UsuarioOperacion", SqlDbType.NVarChar, 100) { Value = (object?)contexto.UsuarioOperacion ?? DBNull.Value });
            cmd.Parameters.Add(new SqlParameter("@LaserficheEntryIdReemplaza", SqlDbType.Int) { Value = (object?)datos.IdDocumentoReemplaza ?? DBNull.Value });
            cmd.Parameters.Add(new SqlParameter("@XmlGenerado", SqlDbType.Xml) { Value = xml });
            cmd.Parameters.Add(new SqlParameter("@IpOrigen", SqlDbType.VarChar, 45) { Value = (object?)contexto.IpOrigen ?? DBNull.Value });
            cmd.Parameters.Add(new SqlParameter("@Instancia", SqlDbType.NVarChar, 100) { Value = Environment.MachineName });
            cmd.Parameters.Add(new SqlParameter("@CorrelationId", SqlDbType.VarChar, 64) { Value = (object?)contexto.CorrelationId ?? DBNull.Value });
            cmd.Parameters.Add(new SqlParameter("@Endpoint", SqlDbType.NVarChar, 300) { Value = (object?)contexto.Endpoint ?? DBNull.Value });
            cmd.Parameters.Add(codigo);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }

        return codigo.Value is int c ? c : CodigosRespuesta.ErrorInterno;
    }

    public async Task AnularAsync(string correlativo, CancellationToken ct = default) =>
        await db.Database.ExecuteSqlAsync($"EXEC trx.usp_AnularRecepcionCarga @Correlativo = {correlativo}", ct);
}
