using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using APILFBI.Infrastructure.Laserfiche;
using APILFBI.Tests.Infraestructura;

namespace APILFBI.Tests.Integracion;

/// <summary>POST /expedientes/{id}/documentos: carga por la API que deja el par en la carpeta de Import Agent.</summary>
[Collection(ColeccionApi.Nombre)]
public sealed class CargasTests(ApiFactory api)
{
    private static readonly byte[] Pdf = LaserficheSimulado.GenerarPdf("Documento de prueba de carga por API");

    private static async Task<(HttpStatusCode Status, JsonElement Json, HttpResponseMessage Respuesta)> CargarAsync(
        HttpClient cliente, long idExpediente, byte[] contenido, string nombreArchivo, Dictionary<string, string> campos, string? idempotencia = null)
    {
        using var formulario = new MultipartFormDataContent();
        var archivo = new ByteArrayContent(contenido);
        archivo.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        formulario.Add(archivo, "archivo", nombreArchivo);
        foreach (var (nombre, valor) in campos) formulario.Add(new StringContent(valor), nombre);

        var solicitud = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/expedientes/{idExpediente}/documentos") { Content = formulario };
        if (idempotencia is not null) solicitud.Headers.Add("Idempotency-Key", idempotencia);
        var r = await cliente.SendAsync(solicitud);
        return (r.StatusCode, await r.Content.ReadFromJsonAsync<JsonElement>(), r);
    }

    private static Dictionary<string, string> Campos(int tipoDocumento = 32, string? correlativo = null) => new()
    {
        ["idTipoDocumento"] = tipoDocumento.ToString(),
        ["fechaEmision"] = "2026-09-01",
        ["nombreUsuarioCarga"] = "Reina Pasita Caceres Palacios",
        ["comentario"] = "Estados financieros al cierre de agosto",
        ["correlativo"] = correlativo ?? $"T{Guid.NewGuid():N}"[..30],
    };

    private string[] ArchivosDe(string correlativo) =>
        Directory.Exists(api.CarpetaImportAgent)
            ? Directory.GetFiles(api.CarpetaImportAgent, $"{correlativo}_*").Select(Path.GetFileName).ToArray()!
            : [];

    private Task<int?> CargasRegistradasAsync(string correlativo) =>
        BaseDatosPrueba.EscalarAsync<int?>(api.BaseDatos, $"SELECT COUNT(*) FROM trx.CargaDocumento WHERE Correlativo = '{correlativo}'");

    [FactSqlServer]
    public async Task Carga_valida_deja_el_par_para_import_agent_y_queda_recibida_hasta_que_el_workflow_la_registra()
    {
        var cliente = await Escenarios.ClienteOperacionAsync(api);
        var exp = await Escenarios.CrearExpedienteJuridicoAsync(cliente);
        var campos = Campos();
        var correlativo = campos["correlativo"];

        var (status, json, respuesta) = await CargarAsync(cliente, exp, Pdf, "estados.pdf", campos);

        Assert.Equal(HttpStatusCode.Accepted, status);
        Assert.Equal(5, json.GetProperty("codigo").GetInt32());
        Assert.Equal($"/api/v1/cargas/{correlativo}", respuesta.Headers.Location?.ToString());
        Assert.Equal(correlativo, json.GetProperty("data").GetProperty("correlativo").GetString());

        // El par quedó completo (sin .tmp) y el XML cumple el contrato con los datos del formulario.
        Assert.Equal([$"{correlativo}_archivo.pdf", $"{correlativo}_data.xml"], ArchivosDe(correlativo).Order());
        Assert.Equal(Pdf, await File.ReadAllBytesAsync(Path.Combine(api.CarpetaImportAgent, $"{correlativo}_archivo.pdf")));
        var xml = XDocument.Load(Path.Combine(api.CarpetaImportAgent, $"{correlativo}_data.xml"));
        var doc = xml.Root!.Element("informacionDocumento")!;
        Assert.Equal(exp.ToString(), xml.Root.Element("idExpediente")!.Value);
        Assert.Equal("51451", doc.Element("usuarioCarga")!.Value);                  // viene del header X-Operation-User
        Assert.Equal($"{correlativo}_archivo.pdf", doc.Element("nombreDocumento")!.Value);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(Pdf)).ToLowerInvariant(), doc.Element("hashSha256")!.Value);

        var recibida = (await (await cliente.GetAsync($"/api/v1/cargas/{correlativo}")).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        Assert.Equal("Recibido", recibida.GetProperty("estadoCarga").GetString());

        // El workflow registra la carga igual que una que llegó por SFTP.
        var entryId = Escenarios.EntryId();
        Assert.Equal(1, await BaseDatosPrueba.EscalarAsync<int?>(api.BaseDatos, $"""
            DECLARE @c int, @m nvarchar(300), @d bigint;
            EXEC trx.usp_RegistrarDocumento @LaserficheEntryId = {entryId}, @IdExpediente = {exp}, @Correlativo = '{correlativo}',
                 @IdTipoDocumento = 32, @NombreDocumento = N'{correlativo}_archivo.pdf', @FechaEmision = '2026-09-01',
                 @UsuarioCarga = '51451', @NombreUsuarioCarga = N'Reina Pasita Caceres Palacios',
                 @CodigoRespuesta = @c OUTPUT, @Mensaje = @m OUTPUT, @IdDocumento = @d OUTPUT;
            """));
        var importada = (await (await cliente.GetAsync($"/api/v1/cargas/{correlativo}")).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        Assert.Equal("Importado", importada.GetProperty("estadoCarga").GetString());
        Assert.Equal(entryId, importada.GetProperty("idDocumento").GetInt32());
    }

    [FactSqlServer]
    public async Task Formato_no_permitido_para_el_tipo_se_rechaza_al_instante_sin_dejar_archivos()
    {
        var cliente = await Escenarios.ClienteOperacionAsync(api);
        var exp = await Escenarios.CrearExpedienteJuridicoAsync(cliente);
        var campos = Campos();
        byte[] docx = [0x50, 0x4B, 0x03, 0x04, 1, 2, 3, 4];   // firma ZIP válida de un .docx, pero el tipo 32 no admite docx

        var (status, json, _) = await CargarAsync(cliente, exp, docx, "carta.docx", campos);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, status);
        Assert.Equal(407, json.GetProperty("codigo").GetInt32());
        Assert.Empty(ArchivosDe(campos["correlativo"]));
        Assert.Equal(0, await CargasRegistradasAsync(campos["correlativo"]));
    }

    [FactSqlServer]
    public async Task Archivo_renombrado_cuyo_contenido_no_es_pdf_se_rechaza()
    {
        var cliente = await Escenarios.ClienteOperacionAsync(api);
        var exp = await Escenarios.CrearExpedienteJuridicoAsync(cliente);

        var (status, json, _) = await CargarAsync(cliente, exp, Encoding.ASCII.GetBytes("esto no es un pdf"), "falso.pdf", Campos());

        Assert.Equal(HttpStatusCode.UnprocessableEntity, status);
        Assert.Equal(407, json.GetProperty("codigo").GetInt32());
        Assert.Contains("contenido", json.GetProperty("detail").GetString());
    }

    [FactSqlServer]
    public async Task Hash_declarado_que_no_coincide_se_rechaza_con_411()
    {
        var cliente = await Escenarios.ClienteOperacionAsync(api);
        var exp = await Escenarios.CrearExpedienteJuridicoAsync(cliente);
        var campos = Campos();
        campos["hashSha256"] = new string('a', 64);

        var (status, json, _) = await CargarAsync(cliente, exp, Pdf, "estados.pdf", campos);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, status);
        Assert.Equal(411, json.GetProperty("codigo").GetInt32());
    }

    [FactSqlServer]
    public async Task Sin_archivo_o_sin_usuario_de_operacion_devuelve_400_codigo_101()
    {
        var cliente = await Escenarios.ClienteOperacionAsync(api);
        var exp = await Escenarios.CrearExpedienteJuridicoAsync(cliente);

        using var sinArchivo = new MultipartFormDataContent();
        foreach (var (n, v) in Campos()) sinArchivo.Add(new StringContent(v), n);
        var r1 = await cliente.PostAsync($"/api/v1/expedientes/{exp}/documentos", sinArchivo);
        var j1 = await r1.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.BadRequest, r1.StatusCode);
        Assert.Equal(101, j1.GetProperty("codigo").GetInt32());
        Assert.Equal("archivo", j1.GetProperty("errores")[0].GetProperty("campo").GetString());

        var sinUsuario = await api.ClienteAutenticadoAsync();
        var (status, json, _) = await CargarAsync(sinUsuario, exp, Pdf, "estados.pdf", Campos());
        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Equal("X-Operation-User", json.GetProperty("errores")[0].GetProperty("campo").GetString());
    }

    [FactSqlServer]
    public async Task Reglas_del_expediente_y_del_tipo_se_validan_igual_que_en_el_registro()
    {
        var cliente = await Escenarios.ClienteOperacionAsync(api);
        var exp = await Escenarios.CrearExpedienteJuridicoAsync(cliente);

        var (s1, j1, _) = await CargarAsync(cliente, exp, Pdf, "ingresos.pdf", Campos(tipoDocumento: 35));   // solo aplica a naturales
        Assert.Equal(HttpStatusCode.UnprocessableEntity, s1);
        Assert.Equal(406, j1.GetProperty("codigo").GetInt32());

        var (s2, j2, _) = await CargarAsync(cliente, 999999999, Pdf, "estados.pdf", Campos());
        Assert.Equal(HttpStatusCode.NotFound, s2);
        Assert.Equal(300, j2.GetProperty("codigo").GetInt32());

        var v1 = await Escenarios.RegistrarDocumentoAsync(api, exp);
        await Escenarios.IntentarRegistrarAsync(api, exp, reemplaza: v1);
        var reemplazo = Campos();
        reemplazo["idDocumentoReemplaza"] = v1.ToString();
        var (s3, j3, _) = await CargarAsync(cliente, exp, Pdf, "estados.pdf", reemplazo);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, s3);
        Assert.Equal(412, j3.GetProperty("codigo").GetInt32());
    }

    [FactSqlServer]
    public async Task Archivo_que_supera_el_tamano_del_tipo_de_documento_se_rechaza_con_410()
    {
        var cliente = await Escenarios.ClienteOperacionAsync(api);
        var exp = await Escenarios.CrearExpedienteJuridicoAsync(cliente);
        var grande = new byte[11 * 1024 * 1024];                  // el tipo 30 admite 10 MB
        "%PDF-1.4\n"u8.CopyTo(grande);

        var (status, json, _) = await CargarAsync(cliente, exp, grande, "identificacion.pdf", Campos(tipoDocumento: 30));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, status);
        Assert.Equal(410, json.GetProperty("codigo").GetInt32());
    }

    [FactSqlServer]
    public async Task Correlativo_en_uso_devuelve_409_codigo_506()
    {
        var cliente = await Escenarios.ClienteOperacionAsync(api);
        var exp = await Escenarios.CrearExpedienteJuridicoAsync(cliente);
        var campos = Campos();
        await CargarAsync(cliente, exp, Pdf, "estados.pdf", campos);

        var (status, json, _) = await CargarAsync(cliente, exp, Pdf, "otro.pdf", campos);

        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Equal(506, json.GetProperty("codigo").GetInt32());
    }

    [FactSqlServer]
    public async Task Reintento_con_la_misma_idempotency_key_devuelve_la_misma_respuesta_sin_duplicar_archivos()
    {
        var cliente = await Escenarios.ClienteOperacionAsync(api);
        var exp = await Escenarios.CrearExpedienteJuridicoAsync(cliente);
        var campos = Campos();
        campos.Remove("correlativo");                             // lo genera la API
        var llave = $"carga-{Guid.NewGuid():N}";

        var (s1, j1, _) = await CargarAsync(cliente, exp, Pdf, "estados.pdf", campos, llave);
        var (s2, j2, r2) = await CargarAsync(cliente, exp, Pdf, "estados.pdf", campos, llave);

        Assert.Equal(HttpStatusCode.Accepted, s1);
        Assert.Equal(HttpStatusCode.Accepted, s2);
        Assert.Equal("true", r2.Headers.GetValues("Idempotent-Replayed").Single());
        var correlativo = j1.GetProperty("data").GetProperty("correlativo").GetString()!;
        Assert.StartsWith("API", correlativo);
        Assert.Equal(correlativo, j2.GetProperty("data").GetProperty("correlativo").GetString());
        Assert.Equal(2, ArchivosDe(correlativo).Length);

        // La misma llave con otro archivo es un error del cliente.
        var (s3, j3, _) = await CargarAsync(cliente, exp, LaserficheSimulado.GenerarPdf("otro"), "estados.pdf", campos, llave);
        Assert.Equal(HttpStatusCode.Conflict, s3);
        Assert.Equal(502, j3.GetProperty("codigo").GetInt32());
    }
}
