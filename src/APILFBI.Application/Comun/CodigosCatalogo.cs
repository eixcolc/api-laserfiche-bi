namespace APILFBI.Application.Comun;

/// <summary>Códigos estables de los catálogos que la lógica necesita conocer.</summary>
public static class TiposOperacion
{
    public const string Autenticacion = "Autenticacion";
    public const string ConsultaDocumentos = "ConsultaDocumentos";
    public const string DescargaDocumento = "DescargaDocumento";
    public const string ConsultaCatalogo = "ConsultaCatalogo";
    public const string ConsultaBitacora = "ConsultaBitacora";
    public const string ConfiguracionInvalida = "ConfiguracionInvalida";
}

public static class OrigenesOperacion
{
    public const string Api = "Api";
    public const string Job = "Job";
}

public static class ResultadosOperacion
{
    public const string Exito = "Exito";
    public const string ExitoParcial = "ExitoParcial";
    public const string ErrorValidacion = "ErrorValidacion";
    public const string ReglaNegocio = "ReglaNegocio";
    public const string NoEncontrado = "NoEncontrado";
    public const string Conflicto = "Conflicto";
    public const string NoAutorizado = "NoAutorizado";
    public const string ErrorExterno = "ErrorExterno";
    public const string ErrorSistema = "ErrorSistema";

    /// <summary>Misma regla que aud.usp_RegistrarBitacora: el resultado se deriva del HTTP.</summary>
    public static string DesdeHttp(int codigoRespuesta, int http) => codigoRespuesta switch
    {
        CodigosRespuesta.ExitoParcial => ExitoParcial,
        _ => http switch
        {
            >= 200 and < 300 => Exito,
            401 or 403 => NoAutorizado,
            404 => NoEncontrado,
            409 => Conflicto,
            422 => ReglaNegocio,
            400 or 429 => ErrorValidacion,
            502 or 503 or 504 => ErrorExterno,
            _ => ErrorSistema,
        },
    };
}

/// <summary>Scopes OAuth (seg.Scope). También son los nombres de las políticas de autorización.</summary>
public static class Scopes
{
    public const string CatalogosLeer = "catalogos.leer";
    public const string ExpedientesEscribir = "expedientes.escribir";
    public const string DocumentosLeer = "documentos.leer";
    public const string DocumentosEstado = "documentos.estado";
    public const string BitacoraLeer = "bitacora.leer";

    public static readonly IReadOnlyList<string> Todos =
        [CatalogosLeer, ExpedientesEscribir, DocumentosLeer, DocumentosEstado, BitacoraLeer];
}
