/* =====================================================================================
   BILF (Base de Datos Auxiliar LF) - 03_procedimientos_triggers.sql
   Funciones, stored procedures, triggers, vistas y roles.

   Se puede volver a ejecutar: usa CREATE OR ALTER, y los tipos y roles solo se crean si no
   existen.

   Contenido:
     Funciones   cat.fn_NormalizarValor, aud.fn_Enmascarar, cat.fn_ProblemasConfiguracionLlaves
     Tipos       trx.TipoLlaveValor, trx.TipoCambioEstado
     Bitácora    aud.usp_RegistrarBitacora, trigger de solo inserción
     Config.     cat.usp_ValidarConfiguracionLlaves, triggers de validación de llaves,
                 triggers de versión de catálogos (invalidación de caché)
     Negocio     trx.usp_ObtenerOCrearExpediente    (API)
                 trx.usp_AsociarDocumentosPersona   (interno)
                 trx.usp_RegistrarDocumento         (workflow de Laserfiche)
                 trx.usp_CambiarEstadoDocumentos    (API, criterio 10)
                 trx.usp_VencerDocumentos           (job diario)
     Vistas      trx.vw_LlavesExpediente
     Roles       rol_bilf_api, rol_bilf_workflow, rol_bilf_mantenimiento

   Registro en bitácora: estos stored procedures registran su propia operación. La API no debe
   volver a registrarla; solo registra las operaciones que no pasan por un stored procedure
   (autenticación, consultas, descargas).
   ===================================================================================== */

USE [BILF];
GO

SET NOCOUNT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

/* =====================================================================================
   FUNCIONES
   ===================================================================================== */

-- Normaliza el valor de una llave según su regla (cat.ReglaNormalizacion.Codigo).
CREATE OR ALTER FUNCTION cat.fn_NormalizarValor (@Valor nvarchar(250), @Regla varchar(50))
RETURNS nvarchar(250)
AS
BEGIN
    IF @Valor IS NULL RETURN NULL;

    DECLARE @r nvarchar(250) = TRIM(@Valor), @p int;

    IF @Regla = 'Alfanumerico'
        SET @r = UPPER(REPLACE(REPLACE(REPLACE(REPLACE(@r, N' ', N''), N'-', N''), N'.', N''), N'/', N''));
    ELSE IF @Regla = 'SoloDigitos'
    BEGIN
        SET @p = PATINDEX(N'%[^0-9]%', @r);
        WHILE @p > 0
        BEGIN
            SET @r = STUFF(@r, @p, 1, N'');
            SET @p = PATINDEX(N'%[^0-9]%', @r);
        END
    END
    ELSE IF @Regla = 'Mayusculas'
    BEGIN
        SET @r = UPPER(@r);
        WHILE CHARINDEX(N'  ', @r) > 0 SET @r = REPLACE(@r, N'  ', N' ');
    END

    RETURN @r;
END
GO

-- Enmascara datos personales para la bitácora: deja visibles solo los últimos 4 caracteres.
CREATE OR ALTER FUNCTION aud.fn_Enmascarar (@Valor nvarchar(250))
RETURNS nvarchar(250)
AS
BEGIN
    IF @Valor IS NULL RETURN NULL;
    IF LEN(@Valor) <= 4 RETURN REPLICATE(N'*', LEN(@Valor));
    RETURN REPLICATE(N'*', LEN(@Valor) - 4) + RIGHT(@Valor, 4);
END
GO

-- Problemas de configuración de llaves de un tipo de expediente. Sin filas = configuración válida.
-- Reglas: debe tener llaves; cada tipo de cliente configurado debe tener al menos un grupo de
-- identificación; cada grupo debe tener 2 o más llaves activas y una sola prioridad.
CREATE OR ALTER FUNCTION cat.fn_ProblemasConfiguracionLlaves (@IdTipoExpediente int)
RETURNS TABLE
AS
RETURN
    SELECT CAST(NULL AS int) AS IdTipoCliente, CAST(NULL AS tinyint) AS GrupoIdentificacion,
           CAST(N'El tipo de expediente no tiene llaves configuradas.' AS nvarchar(300)) AS Problema
    WHERE NOT EXISTS (
        SELECT 1 FROM cat.TipoExpedienteLlave tel
        WHERE tel.IdTipoExpediente = @IdTipoExpediente AND tel.Activo = 1)

    UNION ALL

    SELECT c.IdTipoCliente, NULL,
           CAST(CONCAT(N'El tipo de cliente ', tc.Codigo, N' no tiene ningún grupo de identificación.') AS nvarchar(300))
    FROM (SELECT DISTINCT tel.IdTipoCliente
          FROM cat.TipoExpedienteLlave tel
          WHERE tel.IdTipoExpediente = @IdTipoExpediente AND tel.Activo = 1) c
    JOIN cat.TipoCliente tc ON tc.IdTipoCliente = c.IdTipoCliente
    WHERE NOT EXISTS (
        SELECT 1 FROM cat.TipoExpedienteLlave tel
        JOIN cat.Llave l ON l.IdLlave = tel.IdLlave AND l.Activo = 1
        WHERE tel.IdTipoExpediente = @IdTipoExpediente AND tel.IdTipoCliente = c.IdTipoCliente
          AND tel.Activo = 1 AND tel.GrupoIdentificacion IS NOT NULL)

    UNION ALL

    SELECT g.IdTipoCliente, g.GrupoIdentificacion,
           CAST(CONCAT(N'Tipo de cliente ', tc.Codigo, N', grupo ', g.GrupoIdentificacion, N': ',
                CASE WHEN g.Llaves < 2 THEN CONCAT(N'tiene ', g.Llaves, N' llave(s) activa(s); el mínimo es 2.')
                     ELSE N'tiene más de una prioridad.' END) AS nvarchar(300))
    FROM (SELECT tel.IdTipoCliente, tel.GrupoIdentificacion,
                 COUNT(DISTINCT tel.IdLlave) AS Llaves,
                 COUNT(DISTINCT tel.PrioridadGrupo) AS Prioridades
          FROM cat.TipoExpedienteLlave tel
          JOIN cat.Llave l ON l.IdLlave = tel.IdLlave AND l.Activo = 1
          WHERE tel.IdTipoExpediente = @IdTipoExpediente AND tel.Activo = 1
            AND tel.GrupoIdentificacion IS NOT NULL
          GROUP BY tel.IdTipoCliente, tel.GrupoIdentificacion) g
    JOIN cat.TipoCliente tc ON tc.IdTipoCliente = g.IdTipoCliente
    WHERE g.Llaves < 2 OR g.Prioridades > 1;
GO

/* =====================================================================================
   TIPOS TABLA (parámetros de la API)
   ===================================================================================== */
IF TYPE_ID(N'trx.TipoLlaveValor') IS NULL
    CREATE TYPE trx.TipoLlaveValor AS TABLE (
        CodigoLlave varchar(50)   NOT NULL,
        Valor       nvarchar(250) NULL
    );
GO

IF TYPE_ID(N'trx.TipoCambioEstado') IS NULL
    CREATE TYPE trx.TipoCambioEstado AS TABLE (
        IdDocumento        int            NOT NULL,   -- ID único de Laserfiche (el que conoce CRM)
        CodigoEstado       varchar(50)    NOT NULL,
        CodigoTipoRechazo  varchar(50)    NULL,
        CodigoEtapaRechazo varchar(50)    NULL,
        Comentario         nvarchar(1000) NULL
    );
GO

/* =====================================================================================
   BITÁCORA
   ===================================================================================== */

-- Registra una operación. Si no se indica @CodigoResultado, se deriva del HttpStatus del
-- código de respuesta.
CREATE OR ALTER PROCEDURE aud.usp_RegistrarBitacora
    @CodigoTipoOperacion varchar(50),
    @CodigoOrigen        varchar(50)    = 'Api',
    @CodigoRespuesta     int            = NULL,
    @CodigoResultado     varchar(50)    = NULL,
    @IdExpediente        bigint         = NULL,
    @IdDocumento         bigint         = NULL,
    @Correlativo         varchar(50)    = NULL,
    @UsuarioServicio     nvarchar(100),
    @UsuarioOperacion    nvarchar(100)  = NULL,
    @IpOrigen            varchar(45)    = NULL,
    @Instancia           nvarchar(100)  = NULL,
    @CorrelationId       varchar(64)    = NULL,
    @Endpoint            nvarchar(300)  = NULL,
    @MetodoHttp          varchar(10)    = NULL,
    @DuracionMs          int            = NULL,
    @DatosAnteriores     nvarchar(max)  = NULL,
    @DatosNuevos         nvarchar(max)  = NULL,
    @Detalle             nvarchar(2000) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @IdTipoOperacion int, @IdOrigen int, @IdResultado int, @Http smallint;

    SELECT @IdTipoOperacion = IdTipoOperacion FROM cat.TipoOperacion WHERE Codigo = @CodigoTipoOperacion;
    SELECT @IdOrigen = IdOrigenOperacion FROM cat.OrigenOperacion WHERE Codigo = @CodigoOrigen;

    IF @CodigoResultado IS NULL
    BEGIN
        SELECT @Http = HttpStatus FROM cat.CodigoRespuesta WHERE Codigo = @CodigoRespuesta;
        SET @CodigoResultado = CASE
            WHEN @CodigoRespuesta = 3                    THEN 'ExitoParcial'
            WHEN @Http BETWEEN 200 AND 299               THEN 'Exito'
            WHEN @Http IN (401, 403)                     THEN 'NoAutorizado'
            WHEN @Http = 404                             THEN 'NoEncontrado'
            WHEN @Http = 409                             THEN 'Conflicto'
            WHEN @Http = 422                             THEN 'ReglaNegocio'
            WHEN @Http IN (400, 429)                     THEN 'ErrorValidacion'
            WHEN @Http IN (502, 503, 504)                THEN 'ErrorExterno'
            ELSE 'ErrorSistema' END;
    END
    SELECT @IdResultado = IdResultadoOperacion FROM cat.ResultadoOperacion WHERE Codigo = @CodigoResultado;

    IF @IdTipoOperacion IS NULL OR @IdOrigen IS NULL OR @IdResultado IS NULL
        THROW 50001, N'aud.usp_RegistrarBitacora: código de catálogo inválido (tipo de operación, origen o resultado).', 1;

    INSERT INTO aud.Bitacora (IdTipoOperacion, IdOrigenOperacion, IdResultadoOperacion, CodigoRespuesta,
        IdExpediente, IdDocumento, Correlativo, UsuarioServicio, UsuarioOperacion, IpOrigen, Instancia,
        CorrelationId, Endpoint, MetodoHttp, DuracionMs, DatosAnteriores, DatosNuevos, Detalle)
    VALUES (@IdTipoOperacion, @IdOrigen, @IdResultado, @CodigoRespuesta,
        @IdExpediente, @IdDocumento, @Correlativo, @UsuarioServicio, @UsuarioOperacion, @IpOrigen,
        COALESCE(@Instancia, HOST_NAME()), @CorrelationId, @Endpoint, @MetodoHttp, @DuracionMs,
        @DatosAnteriores, @DatosNuevos, @Detalle);
END
GO

-- La bitácora es de solo inserción.
CREATE OR ALTER TRIGGER aud.trg_Bitacora_SoloInsercion
ON aud.Bitacora
INSTEAD OF UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    THROW 50030, N'La bitácora es de solo inserción: no se permite modificar ni borrar registros.', 1;
END
GO

/* =====================================================================================
   CONFIGURACIÓN DE LLAVES
   ===================================================================================== */

CREATE OR ALTER PROCEDURE cat.usp_ValidarConfiguracionLlaves
    @IdTipoExpediente int,
    @EsValido         bit = NULL OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT p.IdTipoCliente, p.GrupoIdentificacion, p.Problema
    FROM cat.fn_ProblemasConfiguracionLlaves(@IdTipoExpediente) p;

    SET @EsValido = CASE WHEN EXISTS (SELECT 1 FROM cat.fn_ProblemasConfiguracionLlaves(@IdTipoExpediente)) THEN 0 ELSE 1 END;
END
GO

-- Un tipo de expediente solo puede estar activo con configuración de llaves válida.
CREATE OR ALTER TRIGGER cat.trg_TipoExpediente_ValidarLlaves
ON cat.TipoExpediente
AFTER INSERT, UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    IF NOT EXISTS (SELECT 1 FROM inserted WHERE Activo = 1) RETURN;

    DECLARE @msg nvarchar(2000);
    SELECT TOP (1) @msg = CONCAT(N'No se puede activar el tipo de expediente ', i.Codigo, N'. ', p.Problema)
    FROM inserted i
    CROSS APPLY cat.fn_ProblemasConfiguracionLlaves(i.IdTipoExpediente) p
    WHERE i.Activo = 1;

    IF @msg IS NOT NULL THROW 50010, @msg, 1;
END
GO

-- Los cambios en las llaves de un tipo de expediente activo no pueden dejarlo inválido.
-- Para reconfigurar: desactivar el tipo, cambiar sus llaves y volver a activarlo.
CREATE OR ALTER TRIGGER cat.trg_TipoExpedienteLlave_Validar
ON cat.TipoExpedienteLlave
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted) RETURN;

    DECLARE @msg nvarchar(2000);
    SELECT TOP (1) @msg = CONCAT(N'El cambio deja inválido al tipo de expediente activo ', te.Codigo, N'. ', p.Problema,
                                 N' Desactive el tipo de expediente antes de reconfigurar sus llaves.')
    FROM (SELECT IdTipoExpediente FROM inserted UNION SELECT IdTipoExpediente FROM deleted) a
    JOIN cat.TipoExpediente te ON te.IdTipoExpediente = a.IdTipoExpediente AND te.Activo = 1
    CROSS APPLY cat.fn_ProblemasConfiguracionLlaves(a.IdTipoExpediente) p;

    IF @msg IS NOT NULL THROW 50011, @msg, 1;
END
GO

/* =====================================================================================
   VERSIÓN DE CATÁLOGOS
   Un trigger por cada tabla del esquema cat. Incrementa cat.VersionCatalogo para que cada
   instancia de la API recargue solo ese catálogo en su caché.
   ===================================================================================== */
DECLARE @Tabla sysname, @Sql nvarchar(max);
DECLARE curTablas CURSOR LOCAL FAST_FORWARD FOR
    SELECT name FROM sys.tables
    WHERE schema_id = SCHEMA_ID(N'cat') AND name <> N'VersionCatalogo'
    ORDER BY name;
OPEN curTablas;
FETCH NEXT FROM curTablas INTO @Tabla;
WHILE @@FETCH_STATUS = 0
BEGIN
    SET @Sql = N'CREATE OR ALTER TRIGGER cat.' + QUOTENAME(N'trg_' + @Tabla + N'_Version') + N'
ON cat.' + QUOTENAME(@Tabla) + N'
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted) RETURN;
    UPDATE cat.VersionCatalogo
    SET Version = Version + 1, FechaModificacion = SYSUTCDATETIME()
    WHERE NombreCatalogo = N' + QUOTENAME(@Tabla, '''') + N';
    IF @@ROWCOUNT = 0
        INSERT INTO cat.VersionCatalogo (NombreCatalogo) VALUES (N' + QUOTENAME(@Tabla, '''') + N');
END';
    EXEC sys.sp_executesql @Sql;
    FETCH NEXT FROM curTablas INTO @Tabla;
END
CLOSE curTablas;
DEALLOCATE curTablas;
GO

/* =====================================================================================
   ASOCIACIÓN AUTOMÁTICA (interno)
   Trae a un expediente abierto los documentos vigentes de la misma persona (otros
   expedientes con la misma identidad de persona) cuyo tipo acepta su tipo de expediente.
   - No reutiliza documentos Rechazados ni Vencidos.
   - Regla Reemplazar: solo el más reciente por tipo, y solo si el expediente no tiene ya uno
     vigente de ese tipo.
   - Regla Acumular: todos los que falten.
   ===================================================================================== */
CREATE OR ALTER PROCEDURE trx.usp_AsociarDocumentosPersona
    @IdExpediente bigint,
    @Usuario      nvarchar(100),
    @Asociados    int = 0 OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET @Asociados = 0;

    DECLARE @IdTipoExpediente int, @IdTipoCliente int, @IdOrigenAutomatica int;

    SELECT @IdTipoExpediente = e.IdTipoExpediente, @IdTipoCliente = e.IdTipoCliente
    FROM trx.Expediente e
    JOIN cat.EstadoExpediente ee ON ee.IdEstadoExpediente = e.IdEstadoExpediente AND ee.PermiteCambios = 1
    WHERE e.IdExpediente = @IdExpediente;

    IF @IdTipoExpediente IS NULL RETURN;   -- no existe o está cerrado

    SELECT @IdOrigenAutomatica = IdOrigenAsociacion FROM cat.OrigenAsociacion WHERE Codigo = 'Automatica';

    ;WITH Candidatos AS (
        SELECT DISTINCT d.IdDocumento, d.IdTipoDocumento, d.FechaHoraRecepcion, rc.Codigo AS Regla
        FROM trx.ExpedientePersona pm
        JOIN trx.ExpedientePersona po     ON po.IdentidadPersonaNormalizada = pm.IdentidadPersonaNormalizada
                                         AND po.IdExpediente <> pm.IdExpediente
        JOIN trx.ExpedienteDocumento ed   ON ed.IdExpediente = po.IdExpediente AND ed.Vigente = 1
        JOIN trx.Documento d              ON d.IdDocumento = ed.IdDocumento AND d.Vigente = 1
        JOIN cat.EstadoDocumento es       ON es.IdEstadoDocumento = d.IdEstadoDocumento
                                         AND es.Codigo NOT IN ('Rechazado', 'Vencido')
        JOIN cat.TipoDocumento td         ON td.IdTipoDocumento = d.IdTipoDocumento AND td.Activo = 1
        JOIN cat.ReglaCarga rc            ON rc.IdReglaCarga = td.IdReglaCarga
        WHERE pm.IdExpediente = @IdExpediente
          AND EXISTS (SELECT 1 FROM cat.TipoExpedienteTipoDocumento tetd
                      WHERE tetd.IdTipoExpediente = @IdTipoExpediente
                        AND tetd.IdTipoDocumento  = d.IdTipoDocumento
                        AND tetd.Activo = 1
                        AND (tetd.IdTipoCliente = @IdTipoCliente OR tetd.IdTipoCliente IS NULL))
    ),
    Ordenados AS (
        SELECT c.*, ROW_NUMBER() OVER (PARTITION BY c.IdTipoDocumento
                                       ORDER BY c.FechaHoraRecepcion DESC, c.IdDocumento DESC) AS Rn
        FROM Candidatos c
    )
    INSERT INTO trx.ExpedienteDocumento (IdExpediente, IdDocumento, IdOrigenAsociacion, CreadoPor)
    SELECT @IdExpediente, o.IdDocumento, @IdOrigenAutomatica, @Usuario
    FROM Ordenados o
    WHERE (o.Regla = 'Acumular' OR o.Rn = 1)
      AND NOT EXISTS (SELECT 1 FROM trx.ExpedienteDocumento x
                      WHERE x.IdExpediente = @IdExpediente AND x.IdDocumento = o.IdDocumento)
      AND NOT (o.Regla = 'Reemplazar' AND EXISTS (
                      SELECT 1 FROM trx.ExpedienteDocumento x
                      JOIN trx.Documento dx ON dx.IdDocumento = x.IdDocumento AND dx.Vigente = 1
                      WHERE x.IdExpediente = @IdExpediente AND x.Vigente = 1
                        AND dx.IdTipoDocumento = o.IdTipoDocumento));

    SET @Asociados = @@ROWCOUNT;
END
GO

/* =====================================================================================
   OBTENER O CREAR EXPEDIENTE (API: POST /expedientes)

   Siempre devuelve un result set de errores (vacío si todo salió bien):
     CodigoLlave, CodigoRespuesta, Mensaje
   Códigos: 1 = existía | 2 = creado | 1xx validación | 302/409 | 500/501 conflicto | 503 concurrencia

   @SoloBuscar = 1 (consultas 7 y 8): valida y busca con la misma lógica de identidad, pero no
   crea ni modifica nada ni registra bitácora (la registra la API). Si no existe devuelve 300.
   ===================================================================================== */
CREATE OR ALTER PROCEDURE trx.usp_ObtenerOCrearExpediente
    @IdTipoExpediente int,
    @Llaves           trx.TipoLlaveValor READONLY,
    @UsuarioServicio  nvarchar(100),
    @UsuarioOperacion nvarchar(100) = NULL,
    @IpOrigen         varchar(45)   = NULL,
    @Instancia        nvarchar(100) = NULL,
    @CorrelationId    varchar(64)   = NULL,
    @Endpoint         nvarchar(300) = NULL,
    @SoloBuscar       bit           = 0,
    @IdExpediente     bigint        = NULL OUTPUT,
    @EsNuevo          bit           = 0    OUTPUT,
    @CodigoRespuesta  int           = NULL OUTPUT,
    @Mensaje          nvarchar(300) = NULL OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Usuario nvarchar(100) = COALESCE(@UsuarioOperacion, @UsuarioServicio);
    DECLARE @IdTipoCliente int, @Operacion varchar(50), @Json nvarchar(max), @Asociados int = 0,
            @Cambios int = 0, @Detalle nvarchar(2000), @Rc int, @Recurso nvarchar(255);

    DECLARE @Errores TABLE (CodigoLlave varchar(50) NULL, CodigoRespuesta int NOT NULL, Mensaje nvarchar(300) NOT NULL);

    DECLARE @Entrada TABLE (
        IdLlave            int NULL,
        CodigoLlave        varchar(50) NOT NULL,
        Valor              nvarchar(250) NOT NULL,
        ValorNormalizado   nvarchar(250) NULL,
        CodigoTipoDato     varchar(50) NULL,
        LongitudMaxima     int NULL,
        CatalogoValidacion sysname NULL,
        CodigoNormalizacion varchar(50) NULL,
        IdentificaPersona  bit NULL);

    DECLARE @Config TABLE (IdLlave int, Obligatoria bit, Grupo tinyint NULL, Prioridad tinyint NULL, Orden int);
    DECLARE @Grupos TABLE (Grupo tinyint PRIMARY KEY, Prioridad tinyint, Identidad nvarchar(4000), Completo bit);
    DECLARE @Personas TABLE (Identidad nvarchar(300) PRIMARY KEY);
    DECLARE @Encontrados TABLE (IdExpediente bigint PRIMARY KEY);

    SELECT @IdExpediente = NULL, @EsNuevo = 0, @CodigoRespuesta = NULL, @Mensaje = NULL;

    /* ---------- 1. Validaciones (sin transacción) ---------- */
    IF NOT EXISTS (SELECT 1 FROM cat.TipoExpediente WHERE IdTipoExpediente = @IdTipoExpediente AND Activo = 1)
        INSERT @Errores VALUES (NULL, 302, N'El tipo de expediente no existe o está inactivo.');

    -- Llaves repetidas en la solicitud
    INSERT @Errores
    SELECT CodigoLlave, 102, N'La llave viene repetida en la solicitud.'
    FROM @Llaves GROUP BY CodigoLlave HAVING COUNT(*) > 1;

    -- Las llaves con valor vacío se tratan como no enviadas (ej. CIF vacío de un cliente potencial)
    INSERT @Entrada (IdLlave, CodigoLlave, Valor, CodigoTipoDato, LongitudMaxima, CatalogoValidacion, CodigoNormalizacion, IdentificaPersona)
    SELECT l.IdLlave, x.CodigoLlave, TRIM(x.Valor), td.Codigo, l.LongitudMaxima, l.CatalogoValidacion, rn.Codigo, l.IdentificaPersona
    FROM (SELECT CodigoLlave, MIN(Valor) AS Valor FROM @Llaves GROUP BY CodigoLlave) x
    LEFT JOIN cat.Llave l               ON l.Codigo = x.CodigoLlave AND l.Activo = 1
    LEFT JOIN cat.TipoDato td           ON td.IdTipoDato = l.IdTipoDato
    LEFT JOIN cat.ReglaNormalizacion rn ON rn.IdReglaNormalizacion = l.IdReglaNormalizacion
    WHERE NULLIF(TRIM(x.Valor), N'') IS NOT NULL;

    INSERT @Errores
    SELECT CodigoLlave, 104, N'La llave no existe o está inactiva.' FROM @Entrada WHERE IdLlave IS NULL;

    -- Tipo de cliente (llave estructural: define qué configuración aplica)
    IF NOT EXISTS (SELECT 1 FROM @Entrada WHERE CodigoLlave = 'TipoCliente')
        INSERT @Errores VALUES ('TipoCliente', 106, N'Falta la llave TipoCliente.');
    ELSE
    BEGIN
        SELECT @IdTipoCliente = tc.IdTipoCliente
        FROM @Entrada e JOIN cat.TipoCliente tc ON tc.Codigo = e.Valor AND tc.Activo = 1
        WHERE e.CodigoLlave = 'TipoCliente';

        IF @IdTipoCliente IS NULL
            INSERT @Errores VALUES ('TipoCliente', 105, N'El tipo de cliente no existe en el catálogo.');
    END

    IF @IdTipoCliente IS NOT NULL AND NOT EXISTS (SELECT 1 FROM @Errores WHERE CodigoRespuesta = 302)
    BEGIN
        INSERT @Config (IdLlave, Obligatoria, Grupo, Prioridad, Orden)
        SELECT tel.IdLlave, tel.Obligatoria, tel.GrupoIdentificacion, tel.PrioridadGrupo, tel.Orden
        FROM cat.TipoExpedienteLlave tel
        JOIN cat.Llave l ON l.IdLlave = tel.IdLlave AND l.Activo = 1
        WHERE tel.IdTipoExpediente = @IdTipoExpediente AND tel.IdTipoCliente = @IdTipoCliente AND tel.Activo = 1;

        IF NOT EXISTS (SELECT 1 FROM @Config)
            INSERT @Errores VALUES ('TipoCliente', 409, N'El tipo de expediente no está configurado para este tipo de cliente.');
        ELSE
        BEGIN
            -- Llaves no configuradas para este tipo de expediente
            INSERT @Errores
            SELECT e.CodigoLlave, 104, N'La llave no está configurada para el tipo de expediente.'
            FROM @Entrada e
            WHERE e.IdLlave IS NOT NULL AND NOT EXISTS (SELECT 1 FROM @Config c WHERE c.IdLlave = e.IdLlave);

            -- Obligatorias faltantes
            INSERT @Errores
            SELECT DISTINCT l.Codigo, 106, N'Falta una llave obligatoria.'
            FROM @Config c JOIN cat.Llave l ON l.IdLlave = c.IdLlave
            WHERE c.Obligatoria = 1 AND NOT EXISTS (SELECT 1 FROM @Entrada e WHERE e.IdLlave = c.IdLlave);
        END
    END

    -- Longitud y tipo de dato (la expresión regular la valida la API)
    INSERT @Errores
    SELECT CodigoLlave, 102, CONCAT(N'El valor supera la longitud máxima de ', LongitudMaxima, N' caracteres.')
    FROM @Entrada WHERE IdLlave IS NOT NULL AND LEN(Valor) > LongitudMaxima;

    INSERT @Errores
    SELECT CodigoLlave, 102, N'El valor debe ser numérico.'
    FROM @Entrada WHERE CodigoTipoDato = 'Numero' AND TRY_CONVERT(decimal(38, 6), Valor) IS NULL;

    INSERT @Errores
    SELECT CodigoLlave, 102, N'El valor debe ser una fecha ISO 8601 (AAAA-MM-DD).'
    FROM @Entrada WHERE CodigoTipoDato = 'Fecha' AND TRY_CONVERT(date, Valor, 23) IS NULL;

    -- Valores de catálogo
    DECLARE @CodLlave varchar(50), @Val nvarchar(250), @Catalogo sysname, @SqlCat nvarchar(max), @Existe bit;
    DECLARE curCat CURSOR LOCAL FAST_FORWARD FOR
        SELECT CodigoLlave, Valor, CatalogoValidacion FROM @Entrada
        WHERE IdLlave IS NOT NULL AND CatalogoValidacion IS NOT NULL;
    OPEN curCat;
    FETCH NEXT FROM curCat INTO @CodLlave, @Val, @Catalogo;
    WHILE @@FETCH_STATUS = 0
    BEGIN
        SET @SqlCat = N'SELECT @Existe = CASE WHEN EXISTS (SELECT 1 FROM '
                    + QUOTENAME(COALESCE(PARSENAME(@Catalogo, 2), N'cat')) + N'.' + QUOTENAME(PARSENAME(@Catalogo, 1))
                    + N' WHERE Codigo = @Valor AND Activo = 1) THEN 1 ELSE 0 END;';
        EXEC sys.sp_executesql @SqlCat, N'@Valor nvarchar(250), @Existe bit OUTPUT', @Valor = @Val, @Existe = @Existe OUTPUT;
        IF @Existe = 0
            INSERT @Errores VALUES (@CodLlave, 105, N'El valor no existe en el catálogo correspondiente.');
        FETCH NEXT FROM curCat INTO @CodLlave, @Val, @Catalogo;
    END
    CLOSE curCat;
    DEALLOCATE curCat;

    /* ---------- 2. Identidades ---------- */
    IF NOT EXISTS (SELECT 1 FROM @Errores)
    BEGIN
        UPDATE @Entrada SET ValorNormalizado = cat.fn_NormalizarValor(Valor, CodigoNormalizacion);

        INSERT @Grupos (Grupo, Prioridad, Identidad, Completo)
        SELECT c.Grupo, MIN(c.Prioridad),
               STRING_AGG(CAST(COALESCE(e.ValorNormalizado, N'') AS nvarchar(max)), N'|') WITHIN GROUP (ORDER BY c.Orden, c.IdLlave),
               CASE WHEN SUM(CASE WHEN e.IdLlave IS NULL THEN 1 ELSE 0 END) = 0 THEN 1 ELSE 0 END
        FROM @Config c
        LEFT JOIN @Entrada e ON e.IdLlave = c.IdLlave
        WHERE c.Grupo IS NOT NULL
        GROUP BY c.Grupo;

        IF NOT EXISTS (SELECT 1 FROM @Grupos WHERE Completo = 1)
            INSERT @Errores
            SELECT NULL, 103, CONCAT(N'Debe enviar completo al menos un grupo de llaves identificadoras. Opciones: ',
                   STRING_AGG(CAST(g.Descripcion AS nvarchar(max)), N' | '), N'.')
            FROM (SELECT c.Grupo, STRING_AGG(CAST(l.Codigo AS nvarchar(50)), N' + ') WITHIN GROUP (ORDER BY c.Orden, c.IdLlave) AS Descripcion
                  FROM @Config c JOIN cat.Llave l ON l.IdLlave = c.IdLlave
                  WHERE c.Grupo IS NOT NULL GROUP BY c.Grupo) g;

        IF EXISTS (SELECT 1 FROM @Grupos WHERE Completo = 1 AND LEN(Identidad) > 450)
            INSERT @Errores VALUES (NULL, 102, N'Los valores de las llaves identificadoras son demasiado largos.');

        -- Identidad de persona: parte de cada grupo completo formada por llaves con IdentificaPersona = 1
        INSERT @Personas (Identidad)
        SELECT DISTINCT p.Identidad
        FROM (SELECT c.Grupo,
                     STRING_AGG(CAST(l.Codigo AS nvarchar(50)), N'+') WITHIN GROUP (ORDER BY c.Orden, c.IdLlave) + N':'
                   + STRING_AGG(CAST(e.ValorNormalizado AS nvarchar(max)), N'|') WITHIN GROUP (ORDER BY c.Orden, c.IdLlave) AS Identidad
              FROM @Config c
              JOIN @Entrada e ON e.IdLlave = c.IdLlave AND e.IdentificaPersona = 1
              JOIN cat.Llave l ON l.IdLlave = c.IdLlave
              JOIN @Grupos g ON g.Grupo = c.Grupo AND g.Completo = 1
              WHERE c.Grupo IS NOT NULL
              GROUP BY c.Grupo) p
        WHERE LEN(p.Identidad) <= 300;
    END

    -- Llaves para la bitácora (datos de persona enmascarados)
    SET @Json = (SELECT e.CodigoLlave AS llave,
                        CASE WHEN e.IdentificaPersona = 1 THEN aud.fn_Enmascarar(e.Valor) ELSE e.Valor END AS valor
                 FROM @Entrada e ORDER BY e.CodigoLlave FOR JSON PATH);

    IF EXISTS (SELECT 1 FROM @Errores)
    BEGIN
        SELECT TOP (1) @CodigoRespuesta = CodigoRespuesta FROM @Errores ORDER BY CASE WHEN CodigoRespuesta = 302 THEN 0 ELSE 1 END, CodigoRespuesta;
        SELECT @Mensaje = Mensaje FROM cat.CodigoRespuesta WHERE Codigo = @CodigoRespuesta;
        SET @Detalle = (SELECT CodigoLlave AS llave, CodigoRespuesta AS codigo, Mensaje AS mensaje FROM @Errores FOR JSON PATH);

        IF @SoloBuscar = 0
            EXEC aud.usp_RegistrarBitacora @CodigoTipoOperacion = 'ObtenerExpediente', @CodigoRespuesta = @CodigoRespuesta,
                 @UsuarioServicio = @UsuarioServicio, @UsuarioOperacion = @UsuarioOperacion, @IpOrigen = @IpOrigen,
                 @Instancia = @Instancia, @CorrelationId = @CorrelationId, @Endpoint = @Endpoint, @MetodoHttp = 'POST',
                 @DatosNuevos = @Json, @Detalle = @Detalle;

        SELECT CodigoLlave, CodigoRespuesta, Mensaje FROM @Errores;
        RETURN;
    END

    /* ---------- Solo búsqueda (consultas): sin bloqueos, sin escrituras ---------- */
    IF @SoloBuscar = 1
    BEGIN
        INSERT @Encontrados (IdExpediente)
        SELECT DISTINCT ei.IdExpediente
        FROM trx.ExpedienteIdentidad ei
        JOIN @Grupos g ON g.Completo = 1 AND ei.GrupoIdentificacion = g.Grupo AND ei.IdentidadNormalizada = g.Identidad
        WHERE ei.IdTipoExpediente = @IdTipoExpediente AND ei.IdTipoCliente = @IdTipoCliente;

        IF (SELECT COUNT(*) FROM @Encontrados) > 1
        BEGIN
            SET @CodigoRespuesta = 500;
            INSERT @Errores VALUES (NULL, 500, N'Las llaves enviadas corresponden a expedientes distintos.');
        END
        ELSE IF EXISTS (SELECT 1 FROM @Encontrados)
        BEGIN
            SELECT @IdExpediente = IdExpediente FROM @Encontrados;
            SET @CodigoRespuesta = 1;
        END
        ELSE
            SET @CodigoRespuesta = 300;

        SELECT @Mensaje = Mensaje FROM cat.CodigoRespuesta WHERE Codigo = @CodigoRespuesta;
        SELECT CodigoLlave, CodigoRespuesta, Mensaje FROM @Errores;
        RETURN;
    END

    /* ---------- 3. Búsqueda y creación (con transacción) ---------- */
    BEGIN TRY
        BEGIN TRANSACTION;

        -- Bloqueo por identidad (en orden, para evitar deadlocks entre instancias)
        DECLARE curLock CURSOR LOCAL FAST_FORWARD FOR
            SELECT N'BILF_EXP_' + CONVERT(varchar(64), HASHBYTES('SHA2_256',
                   CONCAT(@IdTipoExpediente, N'|', @IdTipoCliente, N'|', Grupo, N'|', Identidad)), 2)
            FROM @Grupos WHERE Completo = 1 ORDER BY 1;
        OPEN curLock;
        FETCH NEXT FROM curLock INTO @Recurso;
        WHILE @@FETCH_STATUS = 0
        BEGIN
            EXEC @Rc = sys.sp_getapplock @Resource = @Recurso, @LockMode = 'Exclusive',
                                         @LockOwner = 'Transaction', @LockTimeout = 10000;
            IF @Rc < 0 THROW 50020, N'No se pudo obtener el bloqueo del expediente.', 1;
            FETCH NEXT FROM curLock INTO @Recurso;
        END
        CLOSE curLock;
        DEALLOCATE curLock;

        INSERT @Encontrados (IdExpediente)
        SELECT DISTINCT ei.IdExpediente
        FROM trx.ExpedienteIdentidad ei
        JOIN @Grupos g ON g.Completo = 1 AND ei.GrupoIdentificacion = g.Grupo AND ei.IdentidadNormalizada = g.Identidad
        WHERE ei.IdTipoExpediente = @IdTipoExpediente AND ei.IdTipoCliente = @IdTipoCliente;

        IF (SELECT COUNT(*) FROM @Encontrados) > 1
        BEGIN
            SET @CodigoRespuesta = 500;
            INSERT @Errores VALUES (NULL, 500, N'Las llaves enviadas corresponden a expedientes distintos.');
            SET @Detalle = CONCAT(N'Expedientes en conflicto: ',
                           (SELECT STRING_AGG(CAST(IdExpediente AS varchar(20)), ', ') FROM @Encontrados));
            SET @Operacion = 'ConflictoLlaves';
        END
        ELSE IF EXISTS (SELECT 1 FROM @Encontrados)
        BEGIN
            SELECT @IdExpediente = IdExpediente FROM @Encontrados;

            -- Una llave identificadora no puede cambiar de valor
            INSERT @Errores
            SELECT e.CodigoLlave, 501, N'La llave identificadora no coincide con la registrada en el expediente.'
            FROM @Entrada e
            JOIN trx.LlaveExpediente le ON le.IdExpediente = @IdExpediente AND le.IdLlave = e.IdLlave AND le.Vigente = 1
            WHERE e.IdLlave IN (SELECT IdLlave FROM @Config WHERE Grupo IS NOT NULL)
              AND le.ValorNormalizado <> e.ValorNormalizado;

            IF EXISTS (SELECT 1 FROM @Errores)
            BEGIN
                SET @CodigoRespuesta = 501;
                SET @Operacion = 'ConflictoLlaves';
            END
            ELSE
            BEGIN
                -- Descriptivas que cambiaron: el valor anterior queda como histórico
                UPDATE le
                SET Vigente = 0, ModificadoPor = @Usuario, FechaModificacion = SYSUTCDATETIME()
                FROM trx.LlaveExpediente le
                JOIN @Entrada e ON e.IdLlave = le.IdLlave
                WHERE le.IdExpediente = @IdExpediente AND le.Vigente = 1 AND le.Valor <> e.Valor
                  AND e.IdLlave NOT IN (SELECT IdLlave FROM @Config WHERE Grupo IS NOT NULL);
                SET @Cambios += @@ROWCOUNT;

                -- Llaves nuevas o con valor nuevo
                INSERT INTO trx.LlaveExpediente (IdExpediente, IdLlave, Valor, ValorNormalizado, CreadoPor)
                SELECT @IdExpediente, e.IdLlave, e.Valor, e.ValorNormalizado, @Usuario
                FROM @Entrada e
                WHERE NOT EXISTS (SELECT 1 FROM trx.LlaveExpediente le
                                  WHERE le.IdExpediente = @IdExpediente AND le.IdLlave = e.IdLlave AND le.Vigente = 1);
                SET @Cambios += @@ROWCOUNT;

                -- Identidades que se completaron ahora (ej. un potencial que ya trae CIF)
                INSERT INTO trx.ExpedienteIdentidad (IdExpediente, IdTipoExpediente, IdTipoCliente, GrupoIdentificacion, IdentidadNormalizada)
                SELECT @IdExpediente, @IdTipoExpediente, @IdTipoCliente, g.Grupo, g.Identidad
                FROM @Grupos g
                WHERE g.Completo = 1
                  AND NOT EXISTS (SELECT 1 FROM trx.ExpedienteIdentidad ei
                                  WHERE ei.IdExpediente = @IdExpediente AND ei.GrupoIdentificacion = g.Grupo);

                INSERT INTO trx.ExpedientePersona (IdExpediente, IdentidadPersonaNormalizada)
                SELECT @IdExpediente, p.Identidad FROM @Personas p
                WHERE NOT EXISTS (SELECT 1 FROM trx.ExpedientePersona ep
                                  WHERE ep.IdExpediente = @IdExpediente AND ep.IdentidadPersonaNormalizada = p.Identidad);

                IF @Cambios > 0
                    UPDATE trx.Expediente SET ModificadoPor = @Usuario, FechaModificacion = SYSUTCDATETIME()
                    WHERE IdExpediente = @IdExpediente;

                EXEC trx.usp_AsociarDocumentosPersona @IdExpediente = @IdExpediente, @Usuario = @Usuario, @Asociados = @Asociados OUTPUT;

                SET @CodigoRespuesta = 1;
                SET @Operacion = CASE WHEN @Cambios > 0 THEN 'ActualizarLlaves' ELSE 'ObtenerExpediente' END;
            END
        END
        ELSE
        BEGIN
            INSERT INTO trx.Expediente (IdTipoExpediente, IdTipoCliente, IdEstadoExpediente, CreadoPor)
            SELECT @IdTipoExpediente, @IdTipoCliente, IdEstadoExpediente, @Usuario
            FROM cat.EstadoExpediente WHERE Codigo = 'Abierto';
            SET @IdExpediente = SCOPE_IDENTITY();

            INSERT INTO trx.LlaveExpediente (IdExpediente, IdLlave, Valor, ValorNormalizado, CreadoPor)
            SELECT @IdExpediente, IdLlave, Valor, ValorNormalizado, @Usuario FROM @Entrada;

            INSERT INTO trx.ExpedienteIdentidad (IdExpediente, IdTipoExpediente, IdTipoCliente, GrupoIdentificacion, IdentidadNormalizada)
            SELECT @IdExpediente, @IdTipoExpediente, @IdTipoCliente, Grupo, Identidad FROM @Grupos WHERE Completo = 1;

            INSERT INTO trx.ExpedientePersona (IdExpediente, IdentidadPersonaNormalizada)
            SELECT @IdExpediente, Identidad FROM @Personas;

            EXEC trx.usp_AsociarDocumentosPersona @IdExpediente = @IdExpediente, @Usuario = @Usuario, @Asociados = @Asociados OUTPUT;

            SET @EsNuevo = 1;
            SET @CodigoRespuesta = 2;
            SET @Operacion = 'CrearExpediente';
        END

        -- En los conflictos (500, 501) todavía no se escribió nada: el COMMIT solo libera los bloqueos.
        COMMIT TRANSACTION;

        SELECT @Mensaje = Mensaje FROM cat.CodigoRespuesta WHERE Codigo = @CodigoRespuesta;
        IF @Asociados > 0 SET @Detalle = CONCAT(N'Documentos asociados automáticamente: ', @Asociados);
        IF @CodigoRespuesta = 501
            SET @Detalle = (SELECT CodigoLlave AS llave, Mensaje AS mensaje FROM @Errores FOR JSON PATH);

        EXEC aud.usp_RegistrarBitacora @CodigoTipoOperacion = @Operacion, @CodigoRespuesta = @CodigoRespuesta,
             @IdExpediente = @IdExpediente, @UsuarioServicio = @UsuarioServicio, @UsuarioOperacion = @UsuarioOperacion,
             @IpOrigen = @IpOrigen, @Instancia = @Instancia, @CorrelationId = @CorrelationId, @Endpoint = @Endpoint,
             @MetodoHttp = 'POST', @DatosNuevos = @Json, @Detalle = @Detalle;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;

        IF ERROR_NUMBER() IN (2601, 2627, 1205)   -- llave duplicada o deadlock: otra instancia ganó la carrera
        BEGIN
            SELECT @IdExpediente = NULL, @EsNuevo = 0, @CodigoRespuesta = 503;
            SELECT @Mensaje = Mensaje FROM cat.CodigoRespuesta WHERE Codigo = 503;
            DELETE @Errores;
            INSERT @Errores VALUES (NULL, 503, @Mensaje);
            SET @Detalle = ERROR_MESSAGE();
            EXEC aud.usp_RegistrarBitacora @CodigoTipoOperacion = 'ObtenerExpediente', @CodigoRespuesta = 503,
                 @UsuarioServicio = @UsuarioServicio, @UsuarioOperacion = @UsuarioOperacion, @IpOrigen = @IpOrigen,
                 @Instancia = @Instancia, @CorrelationId = @CorrelationId, @Endpoint = @Endpoint, @MetodoHttp = 'POST',
                 @DatosNuevos = @Json, @Detalle = @Detalle;
        END
        ELSE
        BEGIN
            SET @Detalle = LEFT(CONCAT(N'Error ', ERROR_NUMBER(), N': ', ERROR_MESSAGE()), 2000);
            EXEC aud.usp_RegistrarBitacora @CodigoTipoOperacion = 'ObtenerExpediente', @CodigoRespuesta = 900,
                 @UsuarioServicio = @UsuarioServicio, @UsuarioOperacion = @UsuarioOperacion, @IpOrigen = @IpOrigen,
                 @Instancia = @Instancia, @CorrelationId = @CorrelationId, @Endpoint = @Endpoint, @MetodoHttp = 'POST',
                 @DatosNuevos = @Json, @Detalle = @Detalle;
            THROW;
        END
    END CATCH

    SELECT CodigoLlave, CodigoRespuesta, Mensaje FROM @Errores;
END
GO

/* =====================================================================================
   REGISTRAR DOCUMENTO (workflow de Laserfiche, después de que Import Agent importa el archivo)

   Devuelve un result set de una fila: CodigoRespuesta, Mensaje, IdDocumento, LaserficheEntryId,
   EstadoCarga, MotivoRechazo. Si EstadoCarga = 'Rechazado', el workflow mueve el documento a la
   carpeta de rechazados.
   Es idempotente: si el mismo documento o correlativo ya fue registrado, responde éxito sin
   duplicar.
   ===================================================================================== */
CREATE OR ALTER PROCEDURE trx.usp_RegistrarDocumento
    @LaserficheEntryId          int,
    @IdExpediente               bigint,
    @Correlativo                varchar(50),
    @IdTipoDocumento            int,
    @NombreDocumento            nvarchar(260),
    @FechaEmision               date,
    @UsuarioCarga               varchar(50),
    @NombreUsuarioCarga         nvarchar(200),
    @FechaVencimiento           date           = NULL,
    @Comentario                 nvarchar(1000) = NULL,
    @LaserficheEntryIdReemplaza int            = NULL,   -- idDocumentoReemplaza del XML
    @HashSha256                 char(64)       = NULL,   -- hash que declara el XML
    @HashSha256Calculado        char(64)       = NULL,   -- hash calculado por el workflow (opcional)
    @TamanoBytes                bigint         = NULL,
    @FechaHoraCarga             datetimeoffset(0) = NULL,
    @XmlOriginal                xml            = NULL,
    @UsuarioServicio            nvarchar(100)  = N'WORKFLOW_LF',
    @CodigoRespuesta            int            = NULL OUTPUT,
    @Mensaje                    nvarchar(300)  = NULL OUTPUT,
    @IdDocumento                bigint         = NULL OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @IdTipoExpediente int, @IdTipoCliente int, @PermiteCambios bit, @Extension varchar(20),
            @IdTipoArchivo int, @TamanoMaximoMB int, @DiasVigencia int, @Regla varchar(50),
            @IdCarga bigint, @EstadoCargaPrevio varchar(50), @EntryPrevio int,
            @Motivo varchar(50), @IdReemplazaExplicito bigint, @Version int = 1, @IdReemplazado bigint,
            @Asociados int = 0, @Reemplazados int = 0, @Detalle nvarchar(2000), @Rc int, @Recurso nvarchar(255),
            @Usuario nvarchar(100) = CAST(@UsuarioCarga AS nvarchar(100));

    DECLARE @Reemplazar TABLE (IdDocumento bigint PRIMARY KEY, Version int);

    SELECT @CodigoRespuesta = NULL, @Mensaje = NULL, @IdDocumento = NULL;

    BEGIN TRY
        BEGIN TRANSACTION;

        SET @Recurso = N'BILF_DOC_' + CONVERT(varchar(64), HASHBYTES('SHA2_256', COALESCE(@Correlativo, N'') + N'|' + CAST(@LaserficheEntryId AS nvarchar(20))), 2);
        EXEC @Rc = sys.sp_getapplock @Resource = @Recurso, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 10000;
        IF @Rc < 0 THROW 50021, N'No se pudo obtener el bloqueo del documento.', 1;

        /* ---------- Idempotencia ---------- */
        SELECT @IdDocumento = IdDocumento FROM trx.Documento WHERE LaserficheEntryId = @LaserficheEntryId;
        IF @IdDocumento IS NOT NULL
        BEGIN
            SET @CodigoRespuesta = 1;
            COMMIT TRANSACTION;
            SELECT @Mensaje = Mensaje FROM cat.CodigoRespuesta WHERE Codigo = 1;
            SELECT @CodigoRespuesta AS CodigoRespuesta, @Mensaje AS Mensaje, @IdDocumento AS IdDocumento,
                   @LaserficheEntryId AS LaserficheEntryId, 'Importado' AS EstadoCarga, CAST(NULL AS varchar(50)) AS MotivoRechazo;
            RETURN;
        END

        SELECT @IdCarga = c.IdCargaDocumento, @EstadoCargaPrevio = ec.Codigo, @EntryPrevio = c.LaserficheEntryId
        FROM trx.CargaDocumento c JOIN cat.EstadoCarga ec ON ec.IdEstadoCarga = c.IdEstadoCarga
        WHERE c.Correlativo = @Correlativo;

        /* ---------- Validaciones (la primera que falle define el motivo) ---------- */
        SELECT @IdTipoExpediente = e.IdTipoExpediente, @IdTipoCliente = e.IdTipoCliente, @PermiteCambios = ee.PermiteCambios
        FROM trx.Expediente e JOIN cat.EstadoExpediente ee ON ee.IdEstadoExpediente = e.IdEstadoExpediente
        WHERE e.IdExpediente = @IdExpediente;

        SELECT @TamanoMaximoMB = td.TamanoMaximoMB, @DiasVigencia = td.DiasVigencia, @Regla = rc.Codigo
        FROM cat.TipoDocumento td JOIN cat.ReglaCarga rc ON rc.IdReglaCarga = td.IdReglaCarga
        WHERE td.IdTipoDocumento = @IdTipoDocumento AND td.Activo = 1;

        IF CHARINDEX('.', @NombreDocumento) > 0
            SET @Extension = LOWER(RIGHT(@NombreDocumento, CHARINDEX('.', REVERSE(@NombreDocumento)) - 1));
        SELECT @IdTipoArchivo = IdTipoArchivo FROM cat.TipoArchivo WHERE Codigo = @Extension AND Activo = 1;

        IF @LaserficheEntryIdReemplaza IS NOT NULL
            SELECT @IdReemplazaExplicito = IdDocumento FROM trx.Documento
            WHERE LaserficheEntryId = @LaserficheEntryIdReemplaza AND IdTipoDocumento = @IdTipoDocumento;

        SET @Motivo = CASE
            WHEN NULLIF(TRIM(@Correlativo), '') IS NULL OR @LaserficheEntryId IS NULL
              OR @FechaEmision IS NULL OR NULLIF(TRIM(@UsuarioCarga), '') IS NULL          THEN 'XmlInvalido'
            WHEN @EstadoCargaPrevio = 'Importado' AND @EntryPrevio <> @LaserficheEntryId     THEN 'Duplicado'
            WHEN @IdTipoExpediente IS NULL                                                  THEN 'ExpedienteNoExiste'
            WHEN @PermiteCambios = 0                                                        THEN 'ExpedienteCerrado'
            WHEN @Regla IS NULL                                                             THEN 'TipoDocumentoNoExiste'
            WHEN NOT EXISTS (SELECT 1 FROM cat.TipoExpedienteTipoDocumento
                             WHERE IdTipoExpediente = @IdTipoExpediente AND IdTipoDocumento = @IdTipoDocumento
                               AND Activo = 1)                                              THEN 'TipoNoPerteneceTipoExpediente'
            WHEN NOT EXISTS (SELECT 1 FROM cat.TipoExpedienteTipoDocumento
                             WHERE IdTipoExpediente = @IdTipoExpediente AND IdTipoDocumento = @IdTipoDocumento
                               AND Activo = 1 AND (IdTipoCliente = @IdTipoCliente OR IdTipoCliente IS NULL)) THEN 'TipoNoAplicaTipoCliente'
            WHEN @IdTipoArchivo IS NULL
              OR NOT EXISTS (SELECT 1 FROM cat.TipoDocumentoTipoArchivo
                             WHERE IdTipoDocumento = @IdTipoDocumento AND IdTipoArchivo = @IdTipoArchivo
                               AND Activo = 1)                                              THEN 'FormatoNoPermitido'
            WHEN @TamanoBytes > CAST(@TamanoMaximoMB AS bigint) * 1048576                   THEN 'TamanoExcedido'
            WHEN @HashSha256 IS NOT NULL AND @HashSha256Calculado IS NOT NULL
              AND UPPER(@HashSha256) <> UPPER(@HashSha256Calculado)                        THEN 'HashNoCoincide'
            WHEN @LaserficheEntryIdReemplaza IS NOT NULL AND @IdReemplazaExplicito IS NULL THEN 'DocumentoReemplazaNoExiste'
        END;

        /* ---------- Rechazo ---------- */
        IF @Motivo IS NOT NULL
        BEGIN
            SET @CodigoRespuesta = CASE @Motivo
                WHEN 'XmlInvalido'                   THEN 100
                WHEN 'Duplicado'                     THEN 504
                WHEN 'ExpedienteNoExiste'            THEN 300
                WHEN 'ExpedienteCerrado'             THEN 405
                WHEN 'TipoDocumentoNoExiste'         THEN 303
                WHEN 'TipoNoPerteneceTipoExpediente' THEN 406
                WHEN 'TipoNoAplicaTipoCliente'       THEN 406
                WHEN 'FormatoNoPermitido'            THEN 407
                WHEN 'TamanoExcedido'                THEN 410
                WHEN 'HashNoCoincide'                THEN 411
                WHEN 'DocumentoReemplazaNoExiste'    THEN 301
            END;
            SELECT @Mensaje = Nombre FROM cat.MotivoRechazoCarga WHERE Codigo = @Motivo;

            -- Un correlativo ya importado con otro documento no se toca; solo se registra en bitácora.
            IF @Motivo <> 'Duplicado' AND NULLIF(TRIM(@Correlativo), '') IS NOT NULL
            BEGIN
                IF @IdCarga IS NULL
                    INSERT INTO trx.CargaDocumento (Correlativo, IdExpediente, IdTipoDocumento, IdEstadoCarga,
                        IdMotivoRechazoCarga, DetalleRechazo, LaserficheEntryId, XmlOriginal, CreadoPor)
                    SELECT @Correlativo,
                           CASE WHEN @IdTipoExpediente IS NOT NULL THEN @IdExpediente END,
                           CASE WHEN @Regla IS NOT NULL THEN @IdTipoDocumento END,
                           (SELECT IdEstadoCarga FROM cat.EstadoCarga WHERE Codigo = 'Rechazado'),
                           (SELECT IdMotivoRechazoCarga FROM cat.MotivoRechazoCarga WHERE Codigo = @Motivo),
                           @Mensaje, @LaserficheEntryId, @XmlOriginal, @UsuarioServicio;
                ELSE
                    UPDATE trx.CargaDocumento
                    SET IdExpediente = CASE WHEN @IdTipoExpediente IS NOT NULL THEN @IdExpediente END,
                        IdTipoDocumento = CASE WHEN @Regla IS NOT NULL THEN @IdTipoDocumento END,
                        IdEstadoCarga = (SELECT IdEstadoCarga FROM cat.EstadoCarga WHERE Codigo = 'Rechazado'),
                        IdMotivoRechazoCarga = (SELECT IdMotivoRechazoCarga FROM cat.MotivoRechazoCarga WHERE Codigo = @Motivo),
                        DetalleRechazo = @Mensaje, LaserficheEntryId = @LaserficheEntryId, XmlOriginal = @XmlOriginal,
                        FechaHoraRecepcion = SYSUTCDATETIME(), ModificadoPor = @UsuarioServicio, FechaModificacion = SYSUTCDATETIME()
                    WHERE IdCargaDocumento = @IdCarga;
            END

            COMMIT TRANSACTION;

            SET @Detalle = CONCAT(N'Motivo: ', @Motivo, N'. LaserficheEntryId: ', @LaserficheEntryId);
            EXEC aud.usp_RegistrarBitacora @CodigoTipoOperacion = 'RechazoCarga', @CodigoOrigen = 'Workflow',
                 @CodigoRespuesta = @CodigoRespuesta,
                 @IdExpediente = @IdExpediente, @Correlativo = @Correlativo, @UsuarioServicio = @UsuarioServicio,
                 @UsuarioOperacion = @Usuario, @Detalle = @Detalle;

            SELECT @CodigoRespuesta AS CodigoRespuesta, @Mensaje AS Mensaje, CAST(NULL AS bigint) AS IdDocumento,
                   @LaserficheEntryId AS LaserficheEntryId, 'Rechazado' AS EstadoCarga, @Motivo AS MotivoRechazo;
            RETURN;
        END

        /* ---------- Registro ---------- */
        IF @IdCarga IS NULL
        BEGIN
            INSERT INTO trx.CargaDocumento (Correlativo, IdExpediente, IdTipoDocumento, IdEstadoCarga, LaserficheEntryId, XmlOriginal, CreadoPor)
            SELECT @Correlativo, @IdExpediente, @IdTipoDocumento, IdEstadoCarga, @LaserficheEntryId, @XmlOriginal, @UsuarioServicio
            FROM cat.EstadoCarga WHERE Codigo = 'Importado';
            SET @IdCarga = SCOPE_IDENTITY();
        END
        ELSE
            UPDATE trx.CargaDocumento   -- reintento de una carga que antes fue rechazada
            SET IdExpediente = @IdExpediente, IdTipoDocumento = @IdTipoDocumento,
                IdEstadoCarga = (SELECT IdEstadoCarga FROM cat.EstadoCarga WHERE Codigo = 'Importado'),
                IdMotivoRechazoCarga = NULL, DetalleRechazo = NULL, LaserficheEntryId = @LaserficheEntryId,
                XmlOriginal = @XmlOriginal, FechaHoraRecepcion = SYSUTCDATETIME(),
                ModificadoPor = @UsuarioServicio, FechaModificacion = SYSUTCDATETIME()
            WHERE IdCargaDocumento = @IdCarga;

        -- Documentos que la nueva versión reemplaza
        IF @IdReemplazaExplicito IS NOT NULL
            INSERT @Reemplazar SELECT IdDocumento, Version FROM trx.Documento WHERE IdDocumento = @IdReemplazaExplicito AND Vigente = 1;
        ELSE IF @Regla = 'Reemplazar'
            INSERT @Reemplazar
            SELECT DISTINCT d.IdDocumento, d.Version
            FROM trx.ExpedienteDocumento ed
            JOIN trx.Documento d ON d.IdDocumento = ed.IdDocumento AND d.Vigente = 1 AND d.IdTipoDocumento = @IdTipoDocumento
            JOIN trx.Expediente e ON e.IdExpediente = ed.IdExpediente
            JOIN cat.EstadoExpediente ee ON ee.IdEstadoExpediente = e.IdEstadoExpediente AND ee.PermiteCambios = 1
            WHERE ed.Vigente = 1
              AND (ed.IdExpediente = @IdExpediente
                   OR ed.IdExpediente IN (SELECT po.IdExpediente
                                          FROM trx.ExpedientePersona pm
                                          JOIN trx.ExpedientePersona po ON po.IdentidadPersonaNormalizada = pm.IdentidadPersonaNormalizada
                                          WHERE pm.IdExpediente = @IdExpediente));

        SELECT @Version = COALESCE(MAX(Version), 0) + 1 FROM @Reemplazar;
        SELECT TOP (1) @IdReemplazado = r.IdDocumento
        FROM @Reemplazar r JOIN trx.Documento d ON d.IdDocumento = r.IdDocumento
        ORDER BY d.FechaHoraRecepcion DESC, d.IdDocumento DESC;

        INSERT INTO trx.Documento (LaserficheEntryId, IdCargaDocumento, IdExpedienteOrigen, IdTipoDocumento, IdTipoArchivo,
            IdEstadoDocumento, NombreArchivo, TamanoBytes, HashSha256, Version, FechaEmision, FechaHoraRecepcion,
            FechaVencimiento, UsuarioCarga, NombreUsuarioCarga, Comentario, IdDocumentoReemplazado, CreadoPor)
        SELECT @LaserficheEntryId, @IdCarga, @IdExpediente, @IdTipoDocumento, @IdTipoArchivo,
               es.IdEstadoDocumento, @NombreDocumento, @TamanoBytes, UPPER(@HashSha256), @Version,
               @FechaEmision, COALESCE(CAST(SWITCHOFFSET(@FechaHoraCarga, '+00:00') AS datetime2(3)), SYSUTCDATETIME()),
               COALESCE(@FechaVencimiento, DATEADD(DAY, @DiasVigencia, @FechaEmision)),
               @UsuarioCarga, @NombreUsuarioCarga, @Comentario, @IdReemplazado, @UsuarioServicio
        FROM cat.EstadoDocumento es WHERE es.Codigo = 'PendienteRevision';
        SET @IdDocumento = SCOPE_IDENTITY();

        -- Los reemplazados dejan de estar vigentes y salen de los expedientes abiertos
        -- (en los cerrados se conservan como histórico).
        IF EXISTS (SELECT 1 FROM @Reemplazar)
        BEGIN
            UPDATE d SET Vigente = 0, ModificadoPor = @UsuarioServicio, FechaModificacion = SYSUTCDATETIME()
            FROM trx.Documento d JOIN @Reemplazar r ON r.IdDocumento = d.IdDocumento;
            SET @Reemplazados = @@ROWCOUNT;

            UPDATE ed SET Vigente = 0, ModificadoPor = @UsuarioServicio, FechaModificacion = SYSUTCDATETIME()
            FROM trx.ExpedienteDocumento ed
            JOIN @Reemplazar r ON r.IdDocumento = ed.IdDocumento
            JOIN trx.Expediente e ON e.IdExpediente = ed.IdExpediente
            JOIN cat.EstadoExpediente ee ON ee.IdEstadoExpediente = e.IdEstadoExpediente AND ee.PermiteCambios = 1
            WHERE ed.Vigente = 1;
        END

        -- Asociación al expediente del XML
        INSERT INTO trx.ExpedienteDocumento (IdExpediente, IdDocumento, IdOrigenAsociacion, CreadoPor)
        SELECT @IdExpediente, @IdDocumento, IdOrigenAsociacion, @UsuarioServicio
        FROM cat.OrigenAsociacion WHERE Codigo = 'Carga';

        -- Asociación automática a los demás expedientes abiertos de la misma persona
        INSERT INTO trx.ExpedienteDocumento (IdExpediente, IdDocumento, IdOrigenAsociacion, CreadoPor)
        SELECT DISTINCT e.IdExpediente, @IdDocumento,
               (SELECT IdOrigenAsociacion FROM cat.OrigenAsociacion WHERE Codigo = 'Automatica'), @UsuarioServicio
        FROM trx.ExpedientePersona pm
        JOIN trx.ExpedientePersona po ON po.IdentidadPersonaNormalizada = pm.IdentidadPersonaNormalizada
                                     AND po.IdExpediente <> pm.IdExpediente
        JOIN trx.Expediente e ON e.IdExpediente = po.IdExpediente
        JOIN cat.EstadoExpediente ee ON ee.IdEstadoExpediente = e.IdEstadoExpediente AND ee.PermiteCambios = 1
        WHERE pm.IdExpediente = @IdExpediente
          AND EXISTS (SELECT 1 FROM cat.TipoExpedienteTipoDocumento tetd
                      WHERE tetd.IdTipoExpediente = e.IdTipoExpediente AND tetd.IdTipoDocumento = @IdTipoDocumento
                        AND tetd.Activo = 1 AND (tetd.IdTipoCliente = e.IdTipoCliente OR tetd.IdTipoCliente IS NULL));
        SET @Asociados = @@ROWCOUNT;

        COMMIT TRANSACTION;

        SET @CodigoRespuesta = 1;
        SELECT @Mensaje = Mensaje FROM cat.CodigoRespuesta WHERE Codigo = 1;

        SET @Detalle = CONCAT(N'LaserficheEntryId: ', @LaserficheEntryId, N'. Versión: ', @Version,
                              N'. Reemplazados: ', @Reemplazados, N'. Asociados automáticamente: ', @Asociados);
        EXEC aud.usp_RegistrarBitacora @CodigoTipoOperacion = 'RegistroDocumento', @CodigoOrigen = 'Workflow',
             @CodigoRespuesta = 1, @IdExpediente = @IdExpediente, @IdDocumento = @IdDocumento,
             @Correlativo = @Correlativo, @UsuarioServicio = @UsuarioServicio, @UsuarioOperacion = @Usuario,
             @Detalle = @Detalle;

        SELECT @CodigoRespuesta AS CodigoRespuesta, @Mensaje AS Mensaje, @IdDocumento AS IdDocumento,
               @LaserficheEntryId AS LaserficheEntryId, 'Importado' AS EstadoCarga, CAST(NULL AS varchar(50)) AS MotivoRechazo;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        SET @Detalle = LEFT(CONCAT(N'Error ', ERROR_NUMBER(), N': ', ERROR_MESSAGE(), N'. LaserficheEntryId: ', @LaserficheEntryId), 2000);
        EXEC aud.usp_RegistrarBitacora @CodigoTipoOperacion = 'RegistroDocumento', @CodigoOrigen = 'Workflow',
             @CodigoRespuesta = 900, @IdExpediente = @IdExpediente, @Correlativo = @Correlativo,
             @UsuarioServicio = @UsuarioServicio, @UsuarioOperacion = @Usuario, @Detalle = @Detalle;
        THROW;
    END CATCH
END
GO

/* =====================================================================================
   CAMBIAR ESTADO DE DOCUMENTOS EN LOTE (API: POST /documentos/estado, criterio 10)

   Valida contra cat.TransicionEstadoDocumento y las banderas de cat.EstadoDocumento.
   @PermitirParcial = 0: si un documento falla, no se aplica ninguno.
   Devuelve un result set por documento: IdDocumento, Aplicado, CodigoRespuesta, Mensaje, HttpStatus.
   La actualización de campos en Laserfiche la hace la API después, con los documentos aplicados.
   ===================================================================================== */
CREATE OR ALTER PROCEDURE trx.usp_CambiarEstadoDocumentos
    @IdExpediente      bigint,
    @Documentos        trx.TipoCambioEstado READONLY,
    @UsuarioServicio   nvarchar(100),
    @UsuarioOperacion  nvarchar(100),
    @EjecutivoAsignado nvarchar(200) = NULL,
    @PermitirParcial   bit           = 0,
    @EsSistema         bit           = 0,
    @IpOrigen          varchar(45)   = NULL,
    @Instancia         nvarchar(100) = NULL,
    @CorrelationId     varchar(64)   = NULL,
    @Endpoint          nvarchar(300) = NULL,
    @CodigoRespuesta   int           = NULL OUTPUT,
    @Mensaje           nvarchar(300) = NULL OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Items TABLE (
        Fila               int IDENTITY(1,1) PRIMARY KEY,
        LaserficheEntryId  int NOT NULL,
        IdDocumento        bigint NULL,
        IdEstadoActual     int NULL,
        CodigoEstadoActual varchar(50) NULL,
        CodigoEstado       varchar(50) NOT NULL,
        IdEstadoNuevo      int NULL,
        RequiereTipoRechazo bit NULL,
        RequiereComentario bit NULL,
        SoloSistema        bit NULL,
        EsDerivado         bit NULL,
        CodigoTipoRechazo  varchar(50) NULL,
        IdTipoRechazo      int NULL,
        CodigoEtapaRechazo varchar(50) NULL,
        IdEtapaRechazo     int NULL,
        Comentario         nvarchar(1000) NULL,
        CodigoRespuesta    int NULL,
        Aplicado           bit NOT NULL DEFAULT (0));

    DECLARE @PermiteCambios bit, @ExisteExpediente bit = 0, @Errores int, @Aplicados int,
            @Fila int, @IdDoc bigint, @Cod int, @Ant nvarchar(max), @Nue nvarchar(max), @Det nvarchar(2000);
    DECLARE @Sincronizaciones TABLE (IdDocumento bigint PRIMARY KEY, IdSincronizacion bigint);

    SELECT @CodigoRespuesta = NULL, @Mensaje = NULL;

    BEGIN TRY
        BEGIN TRANSACTION;

        SELECT @ExisteExpediente = 1, @PermiteCambios = ee.PermiteCambios
        FROM trx.Expediente e JOIN cat.EstadoExpediente ee ON ee.IdEstadoExpediente = e.IdEstadoExpediente
        WHERE e.IdExpediente = @IdExpediente;

        SET @CodigoRespuesta = CASE
            WHEN NULLIF(TRIM(@UsuarioOperacion), N'') IS NULL THEN 101
            WHEN NOT EXISTS (SELECT 1 FROM @Documentos)       THEN 101
            WHEN @ExisteExpediente = 0                        THEN 300
            WHEN @PermiteCambios = 0                          THEN 405
        END;

        IF @CodigoRespuesta IS NULL
        BEGIN
            -- Estado actual con bloqueo de fila: otra instancia no puede cambiarlo en paralelo
            INSERT @Items (LaserficheEntryId, IdDocumento, IdEstadoActual, CodigoEstadoActual, CodigoEstado, IdEstadoNuevo,
                           RequiereTipoRechazo, RequiereComentario, SoloSistema, EsDerivado,
                           CodigoTipoRechazo, IdTipoRechazo, CodigoEtapaRechazo, IdEtapaRechazo, Comentario)
            SELECT x.IdDocumento, d.IdDocumento, d.IdEstadoDocumento, ea.Codigo, x.CodigoEstado, en.IdEstadoDocumento,
                   en.RequiereTipoRechazo, en.RequiereComentario, en.SoloSistema, en.EsDerivado,
                   NULLIF(TRIM(x.CodigoTipoRechazo), ''), tr.IdTipoRechazo,
                   NULLIF(TRIM(x.CodigoEtapaRechazo), ''), er.IdEtapaRechazo,
                   NULLIF(TRIM(x.Comentario), N'')
            FROM @Documentos x
            LEFT JOIN trx.Documento d WITH (UPDLOCK, HOLDLOCK) ON d.LaserficheEntryId = x.IdDocumento
            LEFT JOIN cat.EstadoDocumento ea ON ea.IdEstadoDocumento = d.IdEstadoDocumento
            LEFT JOIN cat.EstadoDocumento en ON en.Codigo = x.CodigoEstado AND en.Activo = 1
            LEFT JOIN cat.TipoRechazo tr     ON tr.Codigo = x.CodigoTipoRechazo AND tr.Activo = 1
            LEFT JOIN cat.EtapaRechazo er    ON er.Codigo = x.CodigoEtapaRechazo AND er.Activo = 1;

            UPDATE i SET CodigoRespuesta = CASE
                WHEN (SELECT COUNT(*) FROM @Items i2 WHERE i2.LaserficheEntryId = i.LaserficheEntryId) > 1 THEN 100
                WHEN i.IdDocumento IS NULL                                                   THEN 301
                WHEN NOT EXISTS (SELECT 1 FROM trx.ExpedienteDocumento ed
                                 WHERE ed.IdExpediente = @IdExpediente AND ed.IdDocumento = i.IdDocumento
                                   AND ed.Vigente = 1)                                       THEN 408
                WHEN i.IdEstadoNuevo IS NULL                                                 THEN 105
                WHEN i.EsDerivado = 1                                                        THEN 404
                WHEN i.SoloSistema = 1 AND @EsSistema = 0                                    THEN 403
                WHEN NOT EXISTS (SELECT 1 FROM cat.TransicionEstadoDocumento t
                                 WHERE t.IdEstadoOrigen = i.IdEstadoActual AND t.IdEstadoDestino = i.IdEstadoNuevo
                                   AND t.Activo = 1)                                         THEN 400
                WHEN i.RequiereTipoRechazo = 1 AND i.CodigoTipoRechazo IS NULL               THEN 401
                WHEN i.CodigoTipoRechazo IS NOT NULL AND i.IdTipoRechazo IS NULL             THEN 105
                WHEN i.CodigoEtapaRechazo IS NOT NULL AND i.IdEtapaRechazo IS NULL           THEN 105
                WHEN i.RequiereComentario = 1 AND i.Comentario IS NULL                       THEN 402
                ELSE 1 END
            FROM @Items i;

            SELECT @Errores = COUNT(*) FROM @Items WHERE CodigoRespuesta <> 1;

            IF @Errores = 0 OR @PermitirParcial = 1
            BEGIN
                UPDATE d
                SET IdEstadoDocumento = i.IdEstadoNuevo,
                    ModificadoPor = @UsuarioOperacion, FechaModificacion = SYSUTCDATETIME()
                FROM trx.Documento d JOIN @Items i ON i.IdDocumento = d.IdDocumento
                WHERE i.CodigoRespuesta = 1;

                INSERT INTO trx.DocumentoEstadoHistorial (IdDocumento, IdExpediente, IdEstadoAnterior, IdEstadoNuevo,
                    IdTipoRechazo, IdEtapaRechazo, Comentario, EjecutivoAsignado, UsuarioOperacion)
                SELECT i.IdDocumento, @IdExpediente, i.IdEstadoActual, i.IdEstadoNuevo,
                       i.IdTipoRechazo, i.IdEtapaRechazo, i.Comentario, @EjecutivoAsignado, @UsuarioOperacion
                FROM @Items i WHERE i.CodigoRespuesta = 1;

                -- Outbox: actualización pendiente de los campos en Laserfiche, en la misma transacción.
                -- EjecutivoAsignado solo se envía si viene (no se borra el que ya tenga el documento).
                INSERT INTO trx.SincronizacionLaserfiche (IdDocumento, LaserficheEntryId, Campos, IdEstadoSincronizacion, IdOrigenOperacion, CreadoPor)
                OUTPUT inserted.IdDocumento, inserted.IdSincronizacion INTO @Sincronizaciones
                SELECT i.IdDocumento, i.LaserficheEntryId,
                       CASE WHEN @EjecutivoAsignado IS NULL THEN JSON_MODIFY(c.Json, '$.EjecutivoAsignado', NULL) ELSE c.Json END,
                       (SELECT IdEstadoSincronizacion FROM cat.EstadoSincronizacion WHERE Codigo = 'Pendiente'),
                       (SELECT IdOrigenOperacion FROM cat.OrigenOperacion WHERE Codigo = CASE WHEN @EsSistema = 1 THEN 'Job' ELSE 'Api' END),
                       @UsuarioOperacion
                FROM @Items i
                CROSS APPLY (SELECT (SELECT i.CodigoEstado AS Estado, i.CodigoTipoRechazo AS TipoRechazo,
                                            i.CodigoEtapaRechazo AS EtapaRechazo, i.Comentario AS ComentarioRevision,
                                            @EjecutivoAsignado AS EjecutivoAsignado
                                     FOR JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES) AS Json) c
                WHERE i.CodigoRespuesta = 1;

                UPDATE @Items SET Aplicado = 1 WHERE CodigoRespuesta = 1;
            END

            SELECT @Aplicados = COUNT(*) FROM @Items WHERE Aplicado = 1;
            SET @CodigoRespuesta = CASE
                WHEN @Errores = 0 THEN 1
                WHEN @Aplicados > 0 THEN 3
                ELSE (SELECT TOP (1) CodigoRespuesta FROM @Items WHERE CodigoRespuesta <> 1 ORDER BY Fila) END;
        END

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        SET @Det = LEFT(CONCAT(N'Error ', ERROR_NUMBER(), N': ', ERROR_MESSAGE()), 2000);
        EXEC aud.usp_RegistrarBitacora @CodigoTipoOperacion = 'CambioEstado', @CodigoRespuesta = 900,
             @IdExpediente = @IdExpediente, @UsuarioServicio = @UsuarioServicio, @UsuarioOperacion = @UsuarioOperacion,
             @IpOrigen = @IpOrigen, @Instancia = @Instancia, @CorrelationId = @CorrelationId, @Endpoint = @Endpoint,
             @MetodoHttp = 'POST', @Detalle = @Det;
        THROW;
    END CATCH

    SELECT @Mensaje = Mensaje FROM cat.CodigoRespuesta WHERE Codigo = @CodigoRespuesta;

    /* Bitácora: una fila por documento (o una sola si la solicitud falló antes de evaluar documentos) */
    IF NOT EXISTS (SELECT 1 FROM @Items)
        EXEC aud.usp_RegistrarBitacora @CodigoTipoOperacion = 'CambioEstado', @CodigoRespuesta = @CodigoRespuesta,
             @IdExpediente = @IdExpediente, @UsuarioServicio = @UsuarioServicio, @UsuarioOperacion = @UsuarioOperacion,
             @IpOrigen = @IpOrigen, @Instancia = @Instancia, @CorrelationId = @CorrelationId, @Endpoint = @Endpoint,
             @MetodoHttp = 'POST';
    ELSE
    BEGIN
        DECLARE curBit CURSOR LOCAL FAST_FORWARD FOR
            SELECT Fila, IdDocumento, CASE WHEN Aplicado = 1 OR CodigoRespuesta <> 1 THEN CodigoRespuesta ELSE 3 END,
                   (SELECT i.CodigoEstadoActual AS estado FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                   (SELECT i.CodigoEstado AS estado, i.CodigoTipoRechazo AS tipoRechazo, i.CodigoEtapaRechazo AS etapaRechazo,
                           i.Comentario AS comentario, @EjecutivoAsignado AS ejecutivoAsignado, i.Aplicado AS aplicado
                    FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                   CONCAT(N'LaserficheEntryId: ', i.LaserficheEntryId)
            FROM @Items i;
        OPEN curBit;
        FETCH NEXT FROM curBit INTO @Fila, @IdDoc, @Cod, @Ant, @Nue, @Det;
        WHILE @@FETCH_STATUS = 0
        BEGIN
            EXEC aud.usp_RegistrarBitacora @CodigoTipoOperacion = 'CambioEstado', @CodigoRespuesta = @Cod,
                 @IdExpediente = @IdExpediente, @IdDocumento = @IdDoc, @UsuarioServicio = @UsuarioServicio,
                 @UsuarioOperacion = @UsuarioOperacion, @IpOrigen = @IpOrigen, @Instancia = @Instancia,
                 @CorrelationId = @CorrelationId, @Endpoint = @Endpoint, @MetodoHttp = 'POST',
                 @DatosAnteriores = @Ant, @DatosNuevos = @Nue, @Detalle = @Det;
            FETCH NEXT FROM curBit INTO @Fila, @IdDoc, @Cod, @Ant, @Nue, @Det;
        END
        CLOSE curBit;
        DEALLOCATE curBit;
    END

    SELECT i.LaserficheEntryId AS IdDocumento, i.Aplicado, i.CodigoRespuesta, cr.Mensaje, cr.HttpStatus,
           i.CodigoEstado AS EstadoNuevo, s.IdSincronizacion
    FROM @Items i
    LEFT JOIN cat.CodigoRespuesta cr ON cr.Codigo = i.CodigoRespuesta
    LEFT JOIN @Sincronizaciones s ON s.IdDocumento = i.IdDocumento
    ORDER BY i.Fila;
END
GO

/* =====================================================================================
   VENCER DOCUMENTOS (job diario)
   Pasa a Vencido los documentos vigentes con FechaVencimiento anterior a @FechaCorte, si la
   transición está permitida. Procesa por lotes: el job lo llama hasta que devuelva 0 filas.
   Devuelve los documentos vencidos para que el job actualice el campo Estado en Laserfiche.
   ===================================================================================== */
CREATE OR ALTER PROCEDURE trx.usp_VencerDocumentos
    @FechaCorte date = NULL,
    @Lote       int  = 500
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    SET @FechaCorte = COALESCE(@FechaCorte, CAST(SYSDATETIME() AS date));

    DECLARE @IdVencido int = (SELECT IdEstadoDocumento FROM cat.EstadoDocumento WHERE Codigo = 'Vencido');
    DECLARE @Vencidos TABLE (IdDocumento bigint PRIMARY KEY, LaserficheEntryId int, IdEstadoAnterior int, IdExpedienteOrigen bigint);

    BEGIN TRANSACTION;

    UPDATE TOP (@Lote) d
    SET IdEstadoDocumento = @IdVencido, ModificadoPor = N'SISTEMA', FechaModificacion = SYSUTCDATETIME()
    OUTPUT inserted.IdDocumento, inserted.LaserficheEntryId, deleted.IdEstadoDocumento, inserted.IdExpedienteOrigen
    INTO @Vencidos
    FROM trx.Documento d
    WHERE d.Vigente = 1
      AND d.FechaVencimiento < @FechaCorte
      AND d.IdEstadoDocumento <> @IdVencido
      AND EXISTS (SELECT 1 FROM cat.TransicionEstadoDocumento t
                  WHERE t.IdEstadoOrigen = d.IdEstadoDocumento AND t.IdEstadoDestino = @IdVencido AND t.Activo = 1);

    INSERT INTO trx.DocumentoEstadoHistorial (IdDocumento, IdExpediente, IdEstadoAnterior, IdEstadoNuevo, Comentario, UsuarioOperacion)
    SELECT IdDocumento, IdExpedienteOrigen, IdEstadoAnterior, @IdVencido, N'Vencimiento automático', N'SISTEMA'
    FROM @Vencidos;

    INSERT INTO aud.Bitacora (IdTipoOperacion, IdOrigenOperacion, IdResultadoOperacion, CodigoRespuesta,
                              IdExpediente, IdDocumento, UsuarioServicio, UsuarioOperacion, Instancia, Detalle)
    SELECT (SELECT IdTipoOperacion FROM cat.TipoOperacion WHERE Codigo = 'VencimientoAutomatico'),
           (SELECT IdOrigenOperacion FROM cat.OrigenOperacion WHERE Codigo = 'Job'),
           (SELECT IdResultadoOperacion FROM cat.ResultadoOperacion WHERE Codigo = 'Exito'),
           1, v.IdExpedienteOrigen, v.IdDocumento, N'JOB_VENCIMIENTO', N'SISTEMA', HOST_NAME(),
           CONCAT(N'LaserficheEntryId: ', v.LaserficheEntryId, N'. Fecha de corte: ', CONVERT(char(10), @FechaCorte, 23))
    FROM @Vencidos v;

    -- Outbox: el Worker actualiza el campo Estado en Laserfiche.
    INSERT INTO trx.SincronizacionLaserfiche (IdDocumento, LaserficheEntryId, Campos, IdEstadoSincronizacion, IdOrigenOperacion, CreadoPor)
    SELECT v.IdDocumento, v.LaserficheEntryId, N'{"Estado":"Vencido"}',
           (SELECT IdEstadoSincronizacion FROM cat.EstadoSincronizacion WHERE Codigo = 'Pendiente'),
           (SELECT IdOrigenOperacion FROM cat.OrigenOperacion WHERE Codigo = 'Job'),
           N'SISTEMA'
    FROM @Vencidos v;

    COMMIT TRANSACTION;

    SELECT IdDocumento, LaserficheEntryId FROM @Vencidos ORDER BY IdDocumento;
END
GO

/* =====================================================================================
   VISTA: una fila por expediente con sus llaves vigentes en columnas.
   La usa el workflow para completar campos en Laserfiche. Al agregar una llave nueva en
   cat.Llave, hay que agregar su columna aquí.
   ===================================================================================== */
CREATE OR ALTER VIEW trx.vw_LlavesExpediente
AS
SELECT e.IdExpediente,
       te.Codigo AS TipoExpediente,
       tc.Codigo AS TipoCliente,
       ee.Codigo AS EstadoExpediente,
       MAX(CASE WHEN l.Codigo = 'noCasoCRM'          THEN le.Valor END) AS noCasoCRM,
       MAX(CASE WHEN l.Codigo = 'CIF'                THEN le.Valor END) AS CIF,
       MAX(CASE WHEN l.Codigo = 'RTN'                THEN le.Valor END) AS RTN,
       MAX(CASE WHEN l.Codigo = 'tipoIdentificacion' THEN le.Valor END) AS tipoIdentificacion,
       MAX(CASE WHEN l.Codigo = 'noIdentificacion'   THEN le.Valor END) AS noIdentificacion,
       MAX(CASE WHEN l.Codigo = 'NombreComercial'    THEN le.Valor END) AS NombreComercial,
       MAX(CASE WHEN l.Codigo = 'razonSocial'        THEN le.Valor END) AS razonSocial,
       MAX(CASE WHEN l.Codigo = 'nombreCompleto'     THEN le.Valor END) AS nombreCompleto,
       MAX(CASE WHEN l.Codigo = 'segmentacion'       THEN le.Valor END) AS segmentacion
FROM trx.Expediente e
JOIN cat.TipoExpediente te   ON te.IdTipoExpediente = e.IdTipoExpediente
JOIN cat.TipoCliente tc      ON tc.IdTipoCliente = e.IdTipoCliente
JOIN cat.EstadoExpediente ee ON ee.IdEstadoExpediente = e.IdEstadoExpediente
LEFT JOIN trx.LlaveExpediente le ON le.IdExpediente = e.IdExpediente AND le.Vigente = 1
LEFT JOIN cat.Llave l            ON l.IdLlave = le.IdLlave
GROUP BY e.IdExpediente, te.Codigo, tc.Codigo, ee.Codigo;
GO

/* =====================================================================================
   ROLES
   El DBA crea los logins o usuarios y los agrega al rol que corresponda, por ejemplo:
     ALTER ROLE rol_bilf_api ADD MEMBER [DOMINIO\svc_apilfbi];
   ===================================================================================== */
IF DATABASE_PRINCIPAL_ID(N'rol_bilf_api') IS NULL           CREATE ROLE rol_bilf_api;
IF DATABASE_PRINCIPAL_ID(N'rol_bilf_workflow') IS NULL      CREATE ROLE rol_bilf_workflow;
IF DATABASE_PRINCIPAL_ID(N'rol_bilf_mantenimiento') IS NULL CREATE ROLE rol_bilf_mantenimiento;
GO

-- API: lee todo y escribe solo mediante stored procedures (salvo tablas técnicas).
GRANT SELECT  ON SCHEMA::cat TO rol_bilf_api;
GRANT SELECT  ON SCHEMA::trx TO rol_bilf_api;
GRANT SELECT  ON SCHEMA::seg TO rol_bilf_api;
GRANT EXECUTE ON SCHEMA::trx TO rol_bilf_api;
GRANT EXECUTE ON SCHEMA::cat TO rol_bilf_api;
GRANT EXECUTE ON SCHEMA::aud TO rol_bilf_api;
GRANT EXECUTE ON TYPE::trx.TipoLlaveValor   TO rol_bilf_api;
GRANT EXECUTE ON TYPE::trx.TipoCambioEstado TO rol_bilf_api;
GRANT SELECT, INSERT ON aud.Bitacora TO rol_bilf_api;                     -- inserción por lotes desde la API
DENY  UPDATE, DELETE ON aud.Bitacora TO rol_bilf_api;
GRANT SELECT, INSERT, UPDATE, DELETE ON aud.IdempotenciaRequest TO rol_bilf_api;
GRANT SELECT, INSERT, UPDATE, DELETE ON seg.DataProtectionKeys  TO rol_bilf_api;
GRANT UPDATE ON seg.CuentaServicio TO rol_bilf_api;                       -- rotación del secreto
GRANT SELECT, UPDATE ON trx.SincronizacionLaserfiche TO rol_bilf_api;     -- outbox hacia Laserfiche (API y Worker)
GO

-- Workflow de Laserfiche: solo registra documentos y lee la vista de llaves.
GRANT EXECUTE ON trx.usp_RegistrarDocumento TO rol_bilf_workflow;
GRANT SELECT  ON trx.vw_LlavesExpediente    TO rol_bilf_workflow;
DENY  UPDATE, DELETE ON aud.Bitacora        TO rol_bilf_workflow;
GO

-- Módulo "Administración Base de Datos Auxiliar LF": mantiene catálogos con borrado lógico.
GRANT SELECT, INSERT, UPDATE ON SCHEMA::cat TO rol_bilf_mantenimiento;
DENY  DELETE ON SCHEMA::cat                 TO rol_bilf_mantenimiento;
GRANT EXECUTE ON cat.usp_ValidarConfiguracionLlaves TO rol_bilf_mantenimiento;
GO

PRINT N'03_procedimientos_triggers.sql ejecutado correctamente.';
GO
