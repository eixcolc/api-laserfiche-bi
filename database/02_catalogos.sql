/* =====================================================================================
   BILF (Base de Datos Auxiliar LF) - 02_catalogos.sql
   Datos iniciales de catálogos y configuración (criterio 6).

   - Se puede volver a ejecutar sin problema: cada INSERT omite los registros cuyo Codigo ya
     existe.
   - Los Ids se insertan explícitamente (IDENTITY_INSERT) para que sean iguales en todos los
     ambientes. El módulo "Administración Base de Datos Auxiliar LF" puede agregar registros
     después sin indicar Id.
   - Los valores marcados con [EJEMPLO] son provisionales. Hay que reemplazarlos por los reales
     del negocio antes de producción.
   ===================================================================================== */

USE [BILF];
GO

SET NOCOUNT ON;
SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

IF OBJECT_ID(N'cat.TipoExpediente', N'U') IS NULL
BEGIN
    RAISERROR(N'No existen las tablas. Ejecute primero 01_tablas.sql.', 16, 1);
    SET NOEXEC ON;
END
GO

BEGIN TRANSACTION;

/* =====================================================================================
   CATÁLOGOS PEQUEÑOS
   ===================================================================================== */

-- TipoCliente -------------------------------------------------------------------------
SET IDENTITY_INSERT cat.TipoCliente ON;
INSERT INTO cat.TipoCliente (IdTipoCliente, Codigo, Nombre, Orden)
SELECT v.Id, v.Codigo, v.Nombre, v.Orden
FROM (VALUES
    (1, 'N', N'Natural',  1),
    (2, 'J', N'Jurídico', 2)
) v (Id, Codigo, Nombre, Orden)
WHERE NOT EXISTS (SELECT 1 FROM cat.TipoCliente t WHERE t.Codigo = v.Codigo);
SET IDENTITY_INSERT cat.TipoCliente OFF;

-- TipoIdentificacion ------------------------------------------------------------------
SET IDENTITY_INSERT cat.TipoIdentificacion ON;
INSERT INTO cat.TipoIdentificacion (IdTipoIdentificacion, Codigo, Nombre, Orden)
SELECT v.Id, v.Codigo, v.Nombre, v.Orden
FROM (VALUES
    (1, 'DNI',              N'Documento Nacional de Identificación', 1),
    (2, 'CarnetResidencia', N'Carnet de Residencia',                 2),
    (3, 'Pasaporte',        N'Pasaporte',                            3)
) v (Id, Codigo, Nombre, Orden)
WHERE NOT EXISTS (SELECT 1 FROM cat.TipoIdentificacion t WHERE t.Codigo = v.Codigo);
SET IDENTITY_INSERT cat.TipoIdentificacion OFF;

-- Segmentacion [EJEMPLO] --------------------------------------------------------------
SET IDENTITY_INSERT cat.Segmentacion ON;
INSERT INTO cat.Segmentacion (IdSegmentacion, Codigo, Nombre, Orden)
SELECT v.Id, v.Codigo, v.Nombre, v.Orden
FROM (VALUES
    (1, 'Personal',    N'Personal',    1),
    (2, 'Pyme',        N'Pyme',        2),
    (3, 'Empresarial', N'Empresarial', 3),
    (4, 'Corporativo', N'Corporativo', 4)
) v (Id, Codigo, Nombre, Orden)
WHERE NOT EXISTS (SELECT 1 FROM cat.Segmentacion t WHERE t.Codigo = v.Codigo);
SET IDENTITY_INSERT cat.Segmentacion OFF;

-- EstadoExpediente --------------------------------------------------------------------
SET IDENTITY_INSERT cat.EstadoExpediente ON;
INSERT INTO cat.EstadoExpediente (IdEstadoExpediente, Codigo, Nombre, PermiteCambios, Orden)
SELECT v.Id, v.Codigo, v.Nombre, v.PermiteCambios, v.Orden
FROM (VALUES
    (1, 'Abierto',   N'Abierto',   1, 1),
    (2, 'Cerrado',   N'Cerrado',   0, 2),
    (3, 'Archivado', N'Archivado', 0, 3)
) v (Id, Codigo, Nombre, PermiteCambios, Orden)
WHERE NOT EXISTS (SELECT 1 FROM cat.EstadoExpediente t WHERE t.Codigo = v.Codigo);
SET IDENTITY_INSERT cat.EstadoExpediente OFF;

-- EstadoCarga -------------------------------------------------------------------------
SET IDENTITY_INSERT cat.EstadoCarga ON;
INSERT INTO cat.EstadoCarga (IdEstadoCarga, Codigo, Nombre, Descripcion, Orden)
SELECT v.Id, v.Codigo, v.Nombre, v.Descripcion, v.Orden
FROM (VALUES
    (1, 'Importado', N'Importado', N'El documento se importó a Laserfiche y quedó registrado.',       1),
    (2, 'Rechazado', N'Rechazado', N'El documento no pasó las validaciones y no quedó registrado.', 2),
    (3, 'Recibido',  N'Recibido',  N'La API recibió el documento y lo dejó para Import Agent; falta que el workflow lo registre.', 0)
) v (Id, Codigo, Nombre, Descripcion, Orden)
WHERE NOT EXISTS (SELECT 1 FROM cat.EstadoCarga t WHERE t.Codigo = v.Codigo);
SET IDENTITY_INSERT cat.EstadoCarga OFF;

-- MotivoRechazoCarga ------------------------------------------------------------------
SET IDENTITY_INSERT cat.MotivoRechazoCarga ON;
INSERT INTO cat.MotivoRechazoCarga (IdMotivoRechazoCarga, Codigo, Nombre, Orden)
SELECT v.Id, v.Codigo, v.Nombre, v.Orden
FROM (VALUES
    (1,  'XmlInvalido',                   N'El XML no cumple con el contrato (XSD)',                      1),
    (2,  'ExpedienteNoExiste',            N'El expediente indicado no existe',                            2),
    (3,  'ExpedienteCerrado',             N'El expediente está cerrado o archivado',                      3),
    (4,  'TipoDocumentoNoExiste',         N'El tipo de documento no existe o está inactivo',              4),
    (5,  'TipoNoPerteneceTipoExpediente', N'El tipo de documento no aplica al tipo de expediente',        5),
    (6,  'TipoNoAplicaTipoCliente',       N'El tipo de documento no aplica al tipo de cliente',           6),
    (7,  'FormatoNoPermitido',            N'El tipo de archivo no está permitido para el tipo de documento', 7),
    (8,  'TamanoExcedido',                N'El archivo supera el tamaño máximo permitido',                8),
    (9,  'HashNoCoincide',                N'El hash SHA-256 no coincide con el archivo recibido',         9),
    (10, 'Duplicado',                     N'El correlativo o el documento ya fue registrado',             10),
    (11, 'DocumentoReemplazaNoExiste',    N'El documento a reemplazar no existe',                         11),
    (12, 'DocumentoReemplazaNoVigente',   N'El documento a reemplazar ya fue reemplazado por una versión más reciente', 12)
) v (Id, Codigo, Nombre, Orden)
WHERE NOT EXISTS (SELECT 1 FROM cat.MotivoRechazoCarga t WHERE t.Codigo = v.Codigo);
SET IDENTITY_INSERT cat.MotivoRechazoCarga OFF;

-- EstadoSincronizacion (outbox hacia Laserfiche) ---------------------------------------
SET IDENTITY_INSERT cat.EstadoSincronizacion ON;
INSERT INTO cat.EstadoSincronizacion (IdEstadoSincronizacion, Codigo, Nombre, Descripcion, Orden)
SELECT v.Id, v.Codigo, v.Nombre, v.Descripcion, v.Orden
FROM (VALUES
    (1, 'Pendiente',    N'Pendiente',    N'Falta actualizar Laserfiche (o se reintentará).',       1),
    (2, 'Sincronizado', N'Sincronizado', N'Laserfiche quedó actualizado.',                          2),
    (3, 'Fallido',      N'Fallido',      N'Se agotaron los reintentos; requiere revisión manual.', 3)
) v (Id, Codigo, Nombre, Descripcion, Orden)
WHERE NOT EXISTS (SELECT 1 FROM cat.EstadoSincronizacion t WHERE t.Codigo = v.Codigo);
SET IDENTITY_INSERT cat.EstadoSincronizacion OFF;

-- ReglaCarga --------------------------------------------------------------------------
SET IDENTITY_INSERT cat.ReglaCarga ON;
INSERT INTO cat.ReglaCarga (IdReglaCarga, Codigo, Nombre, Descripcion, Orden)
SELECT v.Id, v.Codigo, v.Nombre, v.Descripcion, v.Orden
FROM (VALUES
    (1, 'Reemplazar', N'Reemplazar', N'La nueva versión reemplaza a la vigente en los expedientes abiertos.', 1),
    (2, 'Acumular',   N'Acumular',   N'Se permiten varios documentos vigentes del mismo tipo.',              2)
) v (Id, Codigo, Nombre, Descripcion, Orden)
WHERE NOT EXISTS (SELECT 1 FROM cat.ReglaCarga t WHERE t.Codigo = v.Codigo);
SET IDENTITY_INSERT cat.ReglaCarga OFF;

-- OrigenAsociacion --------------------------------------------------------------------
SET IDENTITY_INSERT cat.OrigenAsociacion ON;
INSERT INTO cat.OrigenAsociacion (IdOrigenAsociacion, Codigo, Nombre, Orden)
SELECT v.Id, v.Codigo, v.Nombre, v.Orden
FROM (VALUES
    (1, 'Carga',      N'Carga directa (expediente indicado en el XML)', 1),
    (2, 'Automatica', N'Asociación automática por catálogo',           2),
    (3, 'Manual',     N'Asociación manual por administrador',          3)
) v (Id, Codigo, Nombre, Orden)
WHERE NOT EXISTS (SELECT 1 FROM cat.OrigenAsociacion t WHERE t.Codigo = v.Codigo);
SET IDENTITY_INSERT cat.OrigenAsociacion OFF;

-- TipoRechazo [EJEMPLO] ---------------------------------------------------------------
SET IDENTITY_INSERT cat.TipoRechazo ON;
INSERT INTO cat.TipoRechazo (IdTipoRechazo, Codigo, Nombre, Orden)
SELECT v.Id, v.Codigo, v.Nombre, v.Orden
FROM (VALUES
    (1, 'EstadosFinancierosDescuadrados', N'Estados Financieros Descuadrados',        1),
    (2, 'DocumentoIlegible',              N'Documento ilegible',                      2),
    (3, 'DocumentoIncompleto',            N'Documento incompleto',                    3),
    (4, 'DocumentoVencido',               N'Documento vencido',                       4),
    (5, 'InformacionNoCoincide',          N'La información no coincide con el cliente', 5),
    (6, 'FirmaFaltante',                  N'Falta firma o sello',                     6),
    (7, 'Otro',                           N'Otro',                                    99)
) v (Id, Codigo, Nombre, Orden)
WHERE NOT EXISTS (SELECT 1 FROM cat.TipoRechazo t WHERE t.Codigo = v.Codigo);
SET IDENTITY_INSERT cat.TipoRechazo OFF;

-- EtapaRechazo [EJEMPLO] --------------------------------------------------------------
SET IDENTITY_INSERT cat.EtapaRechazo ON;
INSERT INTO cat.EtapaRechazo (IdEtapaRechazo, Codigo, Nombre, Orden)
SELECT v.Id, v.Codigo, v.Nombre, v.Orden
FROM (VALUES
    (1, 'RevisionGerente', N'Revisión del gerente de cuenta', 1),
    (2, 'AnalisisCredito', N'Análisis de crédito',            2),
    (3, 'Cumplimiento',    N'Cumplimiento',                   3),
    (4, 'Operaciones',     N'Operaciones',                    4)
) v (Id, Codigo, Nombre, Orden)
WHERE NOT EXISTS (SELECT 1 FROM cat.EtapaRechazo t WHERE t.Codigo = v.Codigo);
SET IDENTITY_INSERT cat.EtapaRechazo OFF;

-- TipoDato ----------------------------------------------------------------------------
SET IDENTITY_INSERT cat.TipoDato ON;
INSERT INTO cat.TipoDato (IdTipoDato, Codigo, Nombre, Orden)
SELECT v.Id, v.Codigo, v.Nombre, v.Orden
FROM (VALUES
    (1, 'Texto',    N'Texto',                  1),
    (2, 'Numero',   N'Número',                 2),
    (3, 'Fecha',    N'Fecha (ISO 8601)',       3),
    (4, 'Catalogo', N'Código de un catálogo',  4)
) v (Id, Codigo, Nombre, Orden)
WHERE NOT EXISTS (SELECT 1 FROM cat.TipoDato t WHERE t.Codigo = v.Codigo);
SET IDENTITY_INSERT cat.TipoDato OFF;

-- ReglaNormalizacion ------------------------------------------------------------------
SET IDENTITY_INSERT cat.ReglaNormalizacion ON;
INSERT INTO cat.ReglaNormalizacion (IdReglaNormalizacion, Codigo, Nombre, Descripcion, Orden)
SELECT v.Id, v.Codigo, v.Nombre, v.Descripcion, v.Orden
FROM (VALUES
    (1, 'Ninguna',      N'Ninguna',                 N'Solo quita espacios al inicio y al final.',                    1),
    (2, 'Alfanumerico', N'Alfanumérico',            N'Mayúsculas, sin espacios, guiones ni puntos.',                 2),
    (3, 'SoloDigitos',  N'Solo dígitos',            N'Elimina todo carácter que no sea dígito.',                     3),
    (4, 'Mayusculas',   N'Mayúsculas',              N'Mayúsculas y espacios internos reducidos a uno.',              4)
) v (Id, Codigo, Nombre, Descripcion, Orden)
WHERE NOT EXISTS (SELECT 1 FROM cat.ReglaNormalizacion t WHERE t.Codigo = v.Codigo);
SET IDENTITY_INSERT cat.ReglaNormalizacion OFF;

-- TipoOperacion (bitácora) ------------------------------------------------------------
SET IDENTITY_INSERT cat.TipoOperacion ON;
INSERT INTO cat.TipoOperacion (IdTipoOperacion, Codigo, Nombre, Orden)
SELECT v.Id, v.Codigo, v.Nombre, v.Orden
FROM (VALUES
    (1,  'Autenticacion',         N'Autenticación',                          1),
    (2,  'CrearExpediente',       N'Creación de expediente',                 2),
    (3,  'ObtenerExpediente',     N'Obtención de expediente existente',      3),
    (4,  'ActualizarLlaves',      N'Actualización de llaves de expediente',  4),
    (5,  'ConflictoLlaves',       N'Conflicto de llaves',                    5),
    (6,  'ConsultaDocumentos',    N'Consulta de documentos',                 6),
    (7,  'DescargaDocumento',     N'Descarga o visualización de documento',  7),
    (8,  'CambioEstado',          N'Cambio de estado de documento',          8),
    (9,  'RegistroDocumento',     N'Registro de documento (workflow)',       9),
    (10, 'RechazoCarga',          N'Rechazo de carga',                       10),
    (11, 'AsociacionAutomatica',  N'Asociación automática de documento',     11),
    (12, 'ReemplazoDocumento',    N'Reemplazo de versión de documento',      12),
    (13, 'VencimientoAutomatico', N'Vencimiento automático de documento',    13),
    (14, 'ConfiguracionInvalida', N'Configuración de catálogo inválida',     14),
    (15, 'ConsultaCatalogo',      N'Consulta de catálogo',                   15),
    (16, 'ConsultaBitacora',      N'Consulta de bitácora',                   16),
    (17, 'SincronizacionLaserfiche', N'Actualización de campos en Laserfiche', 17),
    (18, 'LimpiezaIdempotencia',  N'Limpieza de llaves de idempotencia',     18),
    (19, 'RecepcionCarga',        N'Recepción de documento por la API',      19)
) v (Id, Codigo, Nombre, Orden)
WHERE NOT EXISTS (SELECT 1 FROM cat.TipoOperacion t WHERE t.Codigo = v.Codigo);
SET IDENTITY_INSERT cat.TipoOperacion OFF;

-- OrigenOperacion (bitácora) ----------------------------------------------------------
SET IDENTITY_INSERT cat.OrigenOperacion ON;
INSERT INTO cat.OrigenOperacion (IdOrigenOperacion, Codigo, Nombre, Orden)
SELECT v.Id, v.Codigo, v.Nombre, v.Orden
FROM (VALUES
    (1, 'Api',           N'API',                              1),
    (2, 'Workflow',      N'Workflow de Laserfiche',           2),
    (3, 'Job',           N'Proceso en segundo plano',         3),
    (4, 'Mantenimiento', N'Módulo de mantenimiento',          4)
) v (Id, Codigo, Nombre, Orden)
WHERE NOT EXISTS (SELECT 1 FROM cat.OrigenOperacion t WHERE t.Codigo = v.Codigo);
SET IDENTITY_INSERT cat.OrigenOperacion OFF;

-- ResultadoOperacion (bitácora) -------------------------------------------------------
SET IDENTITY_INSERT cat.ResultadoOperacion ON;
INSERT INTO cat.ResultadoOperacion (IdResultadoOperacion, Codigo, Nombre, Orden)
SELECT v.Id, v.Codigo, v.Nombre, v.Orden
FROM (VALUES
    (1, 'Exito',           N'Éxito',                1),
    (2, 'ExitoParcial',    N'Éxito parcial',        2),
    (3, 'ErrorValidacion', N'Error de validación',  3),
    (4, 'ReglaNegocio',    N'Regla de negocio',     4),
    (5, 'NoEncontrado',    N'No encontrado',        5),
    (6, 'Conflicto',       N'Conflicto',            6),
    (7, 'NoAutorizado',    N'No autorizado',        7),
    (8, 'ErrorExterno',    N'Error de Laserfiche',  8),
    (9, 'ErrorSistema',    N'Error de sistema',     9)
) v (Id, Codigo, Nombre, Orden)
WHERE NOT EXISTS (SELECT 1 FROM cat.ResultadoOperacion t WHERE t.Codigo = v.Codigo);
SET IDENTITY_INSERT cat.ResultadoOperacion OFF;

-- CodigoRespuesta (criterio 15) -------------------------------------------------------
-- Rangos: 1-9 éxito | 100 validación | 200 seguridad | 300 no encontrado |
--         400 regla de negocio | 500 conflicto | 800 límites | 900 sistema
SET IDENTITY_INSERT cat.CodigoRespuesta ON;
INSERT INTO cat.CodigoRespuesta (IdCodigoRespuesta, Codigo, Clave, Mensaje, HttpStatus)
SELECT v.Id, v.Codigo, v.Clave, v.Mensaje, v.HttpStatus
FROM (VALUES
    -- Éxito
    (1,  1,   'Exito',                        N'Transacción realizada con éxito',                                   200),
    (2,  2,   'Creado',                       N'Recurso creado con éxito',                                          201),
    (3,  3,   'ExitoParcial',                 N'Transacción realizada parcialmente; revise el detalle por ítem',    200),
    (4,  4,   'ContenidoParcial',             N'Contenido parcial del documento',                                   206),
    (45, 5,   'CargaRecibida',                N'Carga recibida; se está importando a Laserfiche',                   202),
    -- Validación (400)
    (5,  100, 'SolicitudInvalida',            N'La solicitud no tiene un formato válido',                           400),
    (6,  101, 'CampoObligatorio',             N'Falta un campo obligatorio',                                        400),
    (7,  102, 'FormatoCampoInvalido',         N'Un campo tiene un formato o tipo de dato inválido',                 400),
    (8,  103, 'LlavesInsuficientes',          N'Debe enviar completo al menos un grupo de llaves identificadoras',  400),
    (9,  104, 'LlaveNoPermitida',             N'La llave no está configurada para el tipo de expediente',           400),
    (10, 105, 'ValorFueraCatalogo',           N'El valor no existe en el catálogo correspondiente',                 400),
    (11, 106, 'LlaveObligatoriaFaltante',     N'Falta una llave obligatoria',                                       400),
    -- Seguridad
    (12, 200, 'CredencialesInvalidas',        N'Usuario o contraseña inválidos',                                    401),
    (13, 201, 'TokenInvalido',                N'Token ausente, inválido o vencido',                                 401),
    (14, 202, 'SinPermiso',                   N'No tiene permiso para esta operación',                              403),
    -- No encontrado (404)
    (15, 300, 'ExpedienteNoEncontrado',       N'El expediente no existe',                                           404),
    (16, 301, 'DocumentoNoEncontrado',        N'El documento no existe',                                            404),
    (17, 302, 'TipoExpedienteNoEncontrado',   N'El tipo de expediente no existe o está inactivo',                   404),
    (18, 303, 'TipoDocumentoNoEncontrado',    N'El tipo de documento no existe o está inactivo',                    404),
    (19, 304, 'CargaNoEncontrada',            N'La carga no existe o aún no ha sido registrada',                    404),
    -- Regla de negocio (422)
    (20, 400, 'TransicionNoPermitida',        N'El cambio de estado no está permitido',                             422),
    (21, 401, 'RequiereTipoRechazo',          N'El estado requiere un tipo de rechazo',                             422),
    (22, 402, 'RequiereComentario',           N'El estado requiere un comentario',                                  422),
    (23, 403, 'EstadoSoloSistema',            N'El estado solo puede asignarlo el sistema',                         422),
    (24, 404, 'EstadoDerivado',               N'El estado se calcula y no puede asignarse',                         422),
    (25, 405, 'ExpedienteCerrado',            N'El expediente está cerrado o archivado',                            422),
    (26, 406, 'TipoDocumentoNoAplica',        N'El tipo de documento no aplica al tipo de expediente o de cliente', 422),
    (27, 407, 'TipoArchivoNoPermitido',       N'El tipo de archivo no está permitido para el tipo de documento',    422),
    (28, 408, 'DocumentoNoPerteneceExpediente', N'El documento no pertenece al expediente indicado',                422),
    (41, 409, 'TipoClienteNoAplica',          N'El tipo de expediente no está configurado para el tipo de cliente', 422),
    (42, 410, 'TamanoExcedido',               N'El archivo supera el tamaño máximo permitido',                      422),
    (43, 411, 'HashNoCoincide',               N'El hash SHA-256 no coincide con el archivo recibido',               422),
    (44, 412, 'DocumentoReemplazaNoVigente',  N'El documento a reemplazar ya fue reemplazado por una versión más reciente', 422),
    (46, 413, 'ArchivoRechazadoAntivirus',    N'El archivo fue rechazado por el antivirus',                         422),
    (47, 506, 'CorrelativoExistente',         N'El correlativo ya fue usado en otra carga',                         409),
    -- Conflicto (409)
    (29, 500, 'ConflictoLlaves',              N'Las llaves enviadas corresponden a expedientes distintos',          409),
    (30, 501, 'LlaveIdentificadoraDistinta',  N'Una llave identificadora no coincide con la registrada',            409),
    (31, 502, 'IdempotencyKeyReutilizada',    N'La llave de idempotencia ya se usó con otro contenido',             409),
    (32, 503, 'ConflictoConcurrencia',        N'El registro fue modificado por otra operación; intente de nuevo',   409),
    (33, 504, 'DocumentoDuplicado',           N'El documento ya fue registrado',                                    409),
    (34, 505, 'SolicitudEnProceso',           N'Una solicitud con la misma llave de idempotencia está en proceso',  409),
    -- Límites
    (35, 800, 'LimiteSolicitudes',            N'Se superó el límite de solicitudes; intente más tarde',             429),
    -- Sistema
    (36, 900, 'ErrorInterno',                 N'Ocurrió un error interno; comuníquese con soporte',                 500),
    (37, 901, 'ErrorLaserfiche',              N'Laserfiche respondió con un error',                                 502),
    (38, 902, 'LaserficheNoDisponible',       N'Laserfiche no está disponible',                                     503),
    (39, 903, 'BaseDatosNoDisponible',        N'La base de datos no está disponible',                               503),
    (40, 904, 'TiempoEsperaLaserfiche',       N'Laserfiche no respondió a tiempo',                                  504)
) v (Id, Codigo, Clave, Mensaje, HttpStatus)
WHERE NOT EXISTS (SELECT 1 FROM cat.CodigoRespuesta t WHERE t.Codigo = v.Codigo);
SET IDENTITY_INSERT cat.CodigoRespuesta OFF;

-- Código de respuesta que se devuelve con cada motivo de rechazo de carga.
-- Solo completa los que aún no tienen código (respeta cambios hechos desde el módulo de catálogos).
UPDATE m SET CodigoRespuesta = v.CodigoRespuesta
FROM cat.MotivoRechazoCarga m
JOIN (VALUES
    ('XmlInvalido', 100), ('ExpedienteNoExiste', 300), ('ExpedienteCerrado', 405),
    ('TipoDocumentoNoExiste', 303), ('TipoNoPerteneceTipoExpediente', 406), ('TipoNoAplicaTipoCliente', 406),
    ('FormatoNoPermitido', 407), ('TamanoExcedido', 410), ('HashNoCoincide', 411), ('Duplicado', 504),
    ('DocumentoReemplazaNoExiste', 301), ('DocumentoReemplazaNoVigente', 412)
) v (Codigo, CodigoRespuesta) ON v.Codigo = m.Codigo
WHERE m.CodigoRespuesta IS NULL;

/* =====================================================================================
   NÚCLEO
   ===================================================================================== */

-- EstadoDocumento ---------------------------------------------------------------------
SET IDENTITY_INSERT cat.EstadoDocumento ON;
INSERT INTO cat.EstadoDocumento (IdEstadoDocumento, Codigo, Nombre, RequiereTipoRechazo, RequiereComentario, SoloSistema, EsDerivado, Orden)
SELECT v.Id, v.Codigo, v.Nombre, v.RTR, v.RC, v.SS, v.ED, v.Orden
FROM (VALUES
    --                                                    RTR RC SS ED
    (1, 'PendienteRevision', N'Pendiente de Revisión',     0,  0, 0, 0, 1),
    (2, 'Aprobado',          N'Aprobado',                  0,  0, 0, 0, 2),
    (3, 'Rechazado',         N'Rechazado',                 1,  1, 0, 0, 3),
    (4, 'BajoExcepcion',     N'Bajo Excepción',            0,  1, 0, 0, 4),
    (5, 'Vencido',           N'Vencido',                   0,  0, 1, 0, 5),
    (6, 'SinSubir',          N'Sin subir',                 0,  0, 0, 1, 6)
) v (Id, Codigo, Nombre, RTR, RC, SS, ED, Orden)
WHERE NOT EXISTS (SELECT 1 FROM cat.EstadoDocumento t WHERE t.Codigo = v.Codigo);
SET IDENTITY_INSERT cat.EstadoDocumento OFF;

-- TransicionEstadoDocumento [PROPUESTA: validar con negocio] --------------------------
INSERT INTO cat.TransicionEstadoDocumento (IdEstadoOrigen, IdEstadoDestino)
SELECT o.IdEstadoDocumento, d.IdEstadoDocumento
FROM (VALUES
    ('PendienteRevision', 'Aprobado'),
    ('PendienteRevision', 'Rechazado'),
    ('PendienteRevision', 'BajoExcepcion'),
    ('PendienteRevision', 'Vencido'),
    ('Aprobado',          'Rechazado'),
    ('Aprobado',          'Vencido'),
    ('Rechazado',         'PendienteRevision'),
    ('BajoExcepcion',     'Aprobado'),
    ('BajoExcepcion',     'Rechazado'),
    ('BajoExcepcion',     'PendienteRevision'),
    ('BajoExcepcion',     'Vencido'),
    ('Vencido',           'BajoExcepcion')
) v (Origen, Destino)
JOIN cat.EstadoDocumento o ON o.Codigo = v.Origen
JOIN cat.EstadoDocumento d ON d.Codigo = v.Destino
WHERE NOT EXISTS (
    SELECT 1 FROM cat.TransicionEstadoDocumento t
    WHERE t.IdEstadoOrigen = o.IdEstadoDocumento AND t.IdEstadoDestino = d.IdEstadoDocumento
);

-- TipoArchivo -------------------------------------------------------------------------
SET IDENTITY_INSERT cat.TipoArchivo ON;
INSERT INTO cat.TipoArchivo (IdTipoArchivo, Codigo, Nombre, MimeType, FirmaBytes, Orden)
SELECT v.Id, v.Codigo, v.Nombre, v.MimeType, v.FirmaBytes, v.Orden
FROM (VALUES
    (1, 'pdf',  N'PDF',                        'application/pdf',                                                          0x25504446,         1),
    (2, 'docx', N'Word',                       'application/vnd.openxmlformats-officedocument.wordprocessingml.document', 0x504B0304,         2),
    (3, 'xlsx', N'Excel',                      'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',       0x504B0304,         3),
    (4, 'xlsm', N'Excel con macros',           'application/vnd.ms-excel.sheet.macroEnabled.12',                          0x504B0304,         4),
    (5, 'jpg',  N'Imagen JPG',                 'image/jpeg',                                                               0xFFD8FF,           5),
    (6, 'png',  N'Imagen PNG',                 'image/png',                                                                0x89504E470D0A1A0A, 6),
    (7, 'tif',  N'Imagen TIFF',                'image/tiff',                                                               0x49492A00,         7)
) v (Id, Codigo, Nombre, MimeType, FirmaBytes, Orden)
WHERE NOT EXISTS (SELECT 1 FROM cat.TipoArchivo t WHERE t.Codigo = v.Codigo);
SET IDENTITY_INSERT cat.TipoArchivo OFF;

-- TipoDocumento [EJEMPLO: reemplazar por los IDs y nombres reales de Laserfiche] ------
INSERT INTO cat.TipoDocumento (IdTipoDocumento, Codigo, Nombre, TamanoMaximoMB, DiasVigencia, IdReglaCarga, Orden)
SELECT v.Id, v.Codigo, v.Nombre, v.TamanoMaximoMB, v.DiasVigencia, rc.IdReglaCarga, v.Orden
FROM (VALUES
    (30, 'IdentificacionOficial', N'Documento de identificación',            10, NULL, 'Reemplazar', 1),
    (31, 'EscrituraConstitucion', N'Escritura de constitución',              30, NULL, 'Reemplazar', 2),
    (32, 'EstadosFinancieros',    N'Estados financieros',                    30, 365,  'Reemplazar', 3),
    (33, 'ConstanciaRTN',         N'Constancia de RTN',                      10, 365,  'Reemplazar', 4),
    (34, 'ComprobanteDomicilio',  N'Comprobante de domicilio',               10, 90,   'Reemplazar', 5),
    (35, 'ConstanciaIngresos',    N'Constancia de ingresos',                 10, 90,   'Acumular',   6)
) v (Id, Codigo, Nombre, TamanoMaximoMB, DiasVigencia, ReglaCarga, Orden)
JOIN cat.ReglaCarga rc ON rc.Codigo = v.ReglaCarga
WHERE NOT EXISTS (SELECT 1 FROM cat.TipoDocumento t WHERE t.IdTipoDocumento = v.Id OR t.Codigo = v.Codigo);

-- TipoExpediente [EJEMPLO] ------------------------------------------------------------
-- Se insertan INACTIVOS. Se activan al final del script, después de configurar sus llaves.
SET IDENTITY_INSERT cat.TipoExpediente ON;
INSERT INTO cat.TipoExpediente (IdTipoExpediente, Codigo, Nombre, Orden, Activo)
SELECT v.Id, v.Codigo, v.Nombre, v.Orden, 0
FROM (VALUES
    (1, 'BancaCorporativa', N'Banca Corporativa',  1),
    (2, 'TarjetaCredito',   N'Tarjeta de Crédito', 2)
) v (Id, Codigo, Nombre, Orden)
WHERE NOT EXISTS (SELECT 1 FROM cat.TipoExpediente t WHERE t.Codigo = v.Codigo);
SET IDENTITY_INSERT cat.TipoExpediente OFF;

-- Llave -------------------------------------------------------------------------------
-- Codigo = atributo "tipo" de <llave> en el prototipo.
SET IDENTITY_INSERT cat.Llave ON;
INSERT INTO cat.Llave (IdLlave, Codigo, Nombre, IdentificaPersona, IdTipoDato, LongitudMaxima, ExpresionValidacion, CatalogoValidacion, IdReglaNormalizacion, Orden)
SELECT v.Id, v.Codigo, v.Nombre, v.IP, td.IdTipoDato, v.Longitud, v.Expresion, v.Catalogo, rn.IdReglaNormalizacion, v.Orden
FROM (VALUES
    --                                                             IP  TipoDato    Long  Expresión                  Catálogo                  Normalización
    (1,  'TipoCliente',        N'Tipo de cliente',                  0, 'Catalogo',   1,  NULL,                      N'cat.TipoCliente',        'Alfanumerico', 1),
    (2,  'noCasoCRM',          N'Número de caso CRM',               0, 'Texto',     50,  N'^[A-Za-z0-9\-]{1,50}$',  NULL,                      'Alfanumerico', 2),
    (3,  'CIF',                N'CIF',                              1, 'Texto',     50,  NULL,                      NULL,                      'Alfanumerico', 3),
    (4,  'RTN',                N'RTN',                              1, 'Texto',     20,  NULL,                      NULL,                      'Alfanumerico', 4),
    (5,  'tipoIdentificacion', N'Tipo de identificación',           1, 'Catalogo',  50,  NULL,                      N'cat.TipoIdentificacion', 'Ninguna',      5),
    (6,  'noIdentificacion',   N'Número de identificación',         1, 'Texto',     30,  NULL,                      NULL,                      'Alfanumerico', 6),
    (7,  'NombreComercial',    N'Nombre comercial',                 0, 'Texto',    200,  NULL,                      NULL,                      'Ninguna',      7),
    (8,  'razonSocial',        N'Razón social',                     0, 'Texto',    200,  NULL,                      NULL,                      'Ninguna',      8),
    (9,  'nombreCompleto',     N'Nombre completo',                  0, 'Texto',    200,  NULL,                      NULL,                      'Ninguna',      9),
    (10, 'segmentacion',       N'Segmentación',                     0, 'Catalogo',  50,  NULL,                      N'cat.Segmentacion',       'Ninguna',      10)
) v (Id, Codigo, Nombre, IP, TipoDato, Longitud, Expresion, Catalogo, Normalizacion, Orden)
JOIN cat.TipoDato td           ON td.Codigo = v.TipoDato
JOIN cat.ReglaNormalizacion rn ON rn.Codigo = v.Normalizacion
WHERE NOT EXISTS (SELECT 1 FROM cat.Llave t WHERE t.Codigo = v.Codigo);
SET IDENTITY_INSERT cat.Llave OFF;

/* =====================================================================================
   RELACIONES DE CONFIGURACIÓN
   ===================================================================================== */

-- TipoDocumentoTipoArchivo [EJEMPLO] (criterio 14) ------------------------------------
INSERT INTO cat.TipoDocumentoTipoArchivo (IdTipoDocumento, IdTipoArchivo)
SELECT td.IdTipoDocumento, ta.IdTipoArchivo
FROM (VALUES
    ('IdentificacionOficial', 'pdf'), ('IdentificacionOficial', 'jpg'), ('IdentificacionOficial', 'png'),
    ('EscrituraConstitucion', 'pdf'),
    ('EstadosFinancieros',    'pdf'), ('EstadosFinancieros',    'xlsx'), ('EstadosFinancieros', 'xlsm'),
    ('ConstanciaRTN',         'pdf'),
    ('ComprobanteDomicilio',  'pdf'), ('ComprobanteDomicilio',  'jpg'), ('ComprobanteDomicilio', 'png'),
    ('ConstanciaIngresos',    'pdf'), ('ConstanciaIngresos',    'docx')
) v (TipoDocumento, TipoArchivo)
JOIN cat.TipoDocumento td ON td.Codigo = v.TipoDocumento
JOIN cat.TipoArchivo   ta ON ta.Codigo = v.TipoArchivo
WHERE NOT EXISTS (
    SELECT 1 FROM cat.TipoDocumentoTipoArchivo t
    WHERE t.IdTipoDocumento = td.IdTipoDocumento AND t.IdTipoArchivo = ta.IdTipoArchivo
);

-- TipoExpedienteTipoDocumento [EJEMPLO] (criterio 3) ----------------------------------
-- TipoCliente NULL = aplica a natural y jurídico.
INSERT INTO cat.TipoExpedienteTipoDocumento (IdTipoExpediente, IdTipoDocumento, IdTipoCliente, Obligatorio, Orden)
SELECT te.IdTipoExpediente, td.IdTipoDocumento, tc.IdTipoCliente, v.Obligatorio, v.Orden
FROM (VALUES
    ('BancaCorporativa', 'IdentificacionOficial', NULL, 1, 1),
    ('BancaCorporativa', 'EscrituraConstitucion', 'J',  1, 2),
    ('BancaCorporativa', 'EstadosFinancieros',    'J',  1, 3),
    ('BancaCorporativa', 'ConstanciaRTN',         'J',  1, 4),
    ('BancaCorporativa', 'ComprobanteDomicilio',  NULL, 0, 5),
    ('TarjetaCredito',   'IdentificacionOficial', 'N',  1, 1),
    ('TarjetaCredito',   'ConstanciaIngresos',    'N',  1, 2),
    ('TarjetaCredito',   'ComprobanteDomicilio',  NULL, 1, 3),
    ('TarjetaCredito',   'EstadosFinancieros',    'J',  1, 4),
    ('TarjetaCredito',   'ConstanciaRTN',         'J',  1, 5)
) v (TipoExpediente, TipoDocumento, TipoCliente, Obligatorio, Orden)
JOIN cat.TipoExpediente te ON te.Codigo = v.TipoExpediente
JOIN cat.TipoDocumento  td ON td.Codigo = v.TipoDocumento
LEFT JOIN cat.TipoCliente tc ON tc.Codigo = v.TipoCliente
WHERE NOT EXISTS (
    SELECT 1 FROM cat.TipoExpedienteTipoDocumento t
    WHERE t.IdTipoExpediente = te.IdTipoExpediente
      AND t.IdTipoDocumento  = td.IdTipoDocumento
      AND (t.IdTipoCliente = tc.IdTipoCliente OR (t.IdTipoCliente IS NULL AND tc.IdTipoCliente IS NULL))
);

-- TipoExpedienteLlave -----------------------------------------------------------------
-- Misma configuración para todos los tipos de expediente del script.
-- Cada grupo tiene 2 o más llaves (regla obligatoria):
--   J: grupo 1 = noCasoCRM + CIF   | grupo 2 = noCasoCRM + RTN
--   N: grupo 1 = noCasoCRM + CIF   | grupo 2 = noCasoCRM + tipoIdentificacion + noIdentificacion
INSERT INTO cat.TipoExpedienteLlave (IdTipoExpediente, IdTipoCliente, IdLlave, Obligatoria, GrupoIdentificacion, PrioridadGrupo, Orden)
SELECT te.IdTipoExpediente, tc.IdTipoCliente, l.IdLlave, c.Obligatoria, c.Grupo, c.Prioridad, c.Orden
FROM (VALUES
    -- TipoCliente, Llave,               Oblig, Grupo, Prioridad, Orden
    ('J', 'TipoCliente',        1, NULL, NULL, 1),
    ('J', 'noCasoCRM',          1, 1,    1,    2),
    ('J', 'CIF',                0, 1,    1,    3),
    ('J', 'noCasoCRM',          1, 2,    2,    2),
    ('J', 'RTN',                0, 2,    2,    4),
    ('J', 'NombreComercial',    0, NULL, NULL, 5),
    ('J', 'razonSocial',        0, NULL, NULL, 6),
    ('J', 'segmentacion',       0, NULL, NULL, 7),
    ('N', 'TipoCliente',        1, NULL, NULL, 1),
    ('N', 'noCasoCRM',          1, 1,    1,    2),
    ('N', 'CIF',                0, 1,    1,    3),
    ('N', 'noCasoCRM',          1, 2,    2,    2),
    ('N', 'tipoIdentificacion', 0, 2,    2,    4),
    ('N', 'noIdentificacion',   0, 2,    2,    5),
    ('N', 'nombreCompleto',     0, NULL, NULL, 6),
    ('N', 'segmentacion',       0, NULL, NULL, 7)
) c (TipoCliente, Llave, Obligatoria, Grupo, Prioridad, Orden)
CROSS JOIN cat.TipoExpediente te
JOIN cat.TipoCliente tc ON tc.Codigo = c.TipoCliente
JOIN cat.Llave l        ON l.Codigo  = c.Llave
WHERE te.Codigo IN ('BancaCorporativa', 'TarjetaCredito')
  AND NOT EXISTS (
    SELECT 1 FROM cat.TipoExpedienteLlave t
    WHERE t.IdTipoExpediente = te.IdTipoExpediente
      AND t.IdTipoCliente    = tc.IdTipoCliente
      AND t.IdLlave          = l.IdLlave
      AND (t.GrupoIdentificacion = c.Grupo OR (t.GrupoIdentificacion IS NULL AND c.Grupo IS NULL))
);

/* =====================================================================================
   SEGURIDAD
   La cuenta de servicio del CRM NO se inserta aquí: su secreto se genera y se guarda como hash
   desde la API (User Secrets en desarrollo, Key Vault en producción).
   ===================================================================================== */
SET IDENTITY_INSERT seg.Scope ON;
INSERT INTO seg.Scope (IdScope, Codigo, Nombre, Orden)
SELECT v.Id, v.Codigo, v.Nombre, v.Orden
FROM (VALUES
    (1, 'catalogos.leer',       N'Consultar catálogos',                   1),
    (2, 'expedientes.escribir', N'Obtener o crear expedientes',           2),
    (3, 'documentos.leer',      N'Consultar y descargar documentos',      3),
    (4, 'documentos.estado',    N'Cambiar el estado de documentos',       4),
    (5, 'bitacora.leer',        N'Consultar la bitácora',                 5)
) v (Id, Codigo, Nombre, Orden)
WHERE NOT EXISTS (SELECT 1 FROM seg.Scope t WHERE t.Codigo = v.Codigo);
SET IDENTITY_INSERT seg.Scope OFF;

/* =====================================================================================
   VERSIÓN DE CATÁLOGOS (una fila por cada tabla del esquema cat)
   ===================================================================================== */
INSERT INTO cat.VersionCatalogo (NombreCatalogo)
SELECT t.name
FROM sys.tables t
WHERE t.schema_id = SCHEMA_ID(N'cat')
  AND t.name <> N'VersionCatalogo'
  AND NOT EXISTS (SELECT 1 FROM cat.VersionCatalogo v WHERE v.NombreCatalogo = t.name);

/* =====================================================================================
   ACTIVAR TIPOS DE EXPEDIENTE
   Va al final, cuando sus llaves ya están configuradas. Cuando exista el trigger del
   script 03, esta instrucción valida que cada grupo tenga 2 o más llaves.
   ===================================================================================== */
UPDATE cat.TipoExpediente
SET Activo = 1, ModificadoPor = N'SISTEMA', FechaModificacion = SYSUTCDATETIME()
WHERE Codigo IN ('BancaCorporativa', 'TarjetaCredito')
  AND Activo = 0;

COMMIT TRANSACTION;
GO

PRINT N'02_catalogos.sql ejecutado correctamente.';
GO

SET NOEXEC OFF;
GO
