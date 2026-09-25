namespace APILFBI.Application.Comun;

/// <summary>
/// Códigos de negocio de cat.CodigoRespuesta. El HTTP y el mensaje de cada código se leen del
/// catálogo; <see cref="PorDefecto"/> solo se usa si el catálogo aún no está disponible
/// (ej. la base de datos no responde al arrancar).
/// </summary>
public static class CodigosRespuesta
{
    public const int Exito = 1;
    public const int Creado = 2;
    public const int ExitoParcial = 3;
    public const int ContenidoParcial = 4;
    public const int CargaRecibida = 5;

    public const int SolicitudInvalida = 100;
    public const int CampoObligatorio = 101;
    public const int FormatoCampoInvalido = 102;
    public const int LlavesInsuficientes = 103;
    public const int LlaveNoPermitida = 104;
    public const int ValorFueraCatalogo = 105;
    public const int LlaveObligatoriaFaltante = 106;

    public const int CredencialesInvalidas = 200;
    public const int TokenInvalido = 201;
    public const int SinPermiso = 202;

    public const int ExpedienteNoEncontrado = 300;
    public const int DocumentoNoEncontrado = 301;
    public const int TipoExpedienteNoEncontrado = 302;
    public const int TipoDocumentoNoEncontrado = 303;
    public const int CargaNoEncontrada = 304;

    public const int TransicionNoPermitida = 400;
    public const int RequiereTipoRechazo = 401;
    public const int RequiereComentario = 402;
    public const int EstadoSoloSistema = 403;
    public const int EstadoDerivado = 404;
    public const int ExpedienteCerrado = 405;
    public const int TipoDocumentoNoAplica = 406;
    public const int TipoArchivoNoPermitido = 407;
    public const int DocumentoNoPerteneceExpediente = 408;
    public const int TipoClienteNoAplica = 409;
    public const int TamanoExcedido = 410;
    public const int HashNoCoincide = 411;
    public const int DocumentoReemplazaNoVigente = 412;
    public const int ArchivoRechazadoAntivirus = 413;

    public const int ConflictoLlaves = 500;
    public const int LlaveIdentificadoraDistinta = 501;
    public const int IdempotencyKeyReutilizada = 502;
    public const int ConflictoConcurrencia = 503;
    public const int DocumentoDuplicado = 504;
    public const int SolicitudEnProceso = 505;
    public const int CorrelativoExistente = 506;

    public const int LimiteSolicitudes = 800;

    public const int ErrorInterno = 900;
    public const int ErrorLaserfiche = 901;
    public const int LaserficheNoDisponible = 902;
    public const int BaseDatosNoDisponible = 903;
    public const int TiempoEsperaLaserfiche = 904;

    /// <summary>Respaldo para los códigos que usa la infraestructura cuando el catálogo no está cargado.</summary>
    public static readonly IReadOnlyDictionary<int, (int Http, string Mensaje)> PorDefecto = new Dictionary<int, (int, string)>
    {
        [Exito] = (200, "Transacción realizada con éxito"),
        [SolicitudInvalida] = (400, "La solicitud no tiene un formato válido"),
        [CampoObligatorio] = (400, "Falta un campo obligatorio"),
        [FormatoCampoInvalido] = (400, "Un campo tiene un formato o tipo de dato inválido"),
        [CredencialesInvalidas] = (401, "Usuario o contraseña inválidos"),
        [TokenInvalido] = (401, "Token ausente, inválido o vencido"),
        [SinPermiso] = (403, "No tiene permiso para esta operación"),
        [LimiteSolicitudes] = (429, "Se superó el límite de solicitudes; intente más tarde"),
        [ErrorInterno] = (500, "Ocurrió un error interno; comuníquese con soporte"),
        [BaseDatosNoDisponible] = (503, "La base de datos no está disponible"),
    };
}
