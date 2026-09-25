/* =====================================================================================
   BILF (Base de Datos Auxiliar LF) - 01_tablas.sql
   Crea esquemas, tablas, llaves, restricciones e índices.

   Orden de ejecución:
     00_crear_base.sql               (crea la base BILF si no existe)
     01_tablas.sql                   (este script)
     02_catalogos.sql                (datos iniciales de catálogos)
     03_procedimientos_triggers.sql  (stored procedures, triggers y vistas)

   El script es para una base vacía. Si detecta que las tablas ya existen, se detiene sin
   modificar nada.
   ===================================================================================== */

USE [BILF];
GO

SET NOCOUNT ON;
SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;   -- requerido por los índices filtrados (sqlcmd lo trae en OFF)
GO

IF OBJECT_ID(N'cat.TipoExpediente', N'U') IS NOT NULL
BEGIN
    RAISERROR(N'La base ya contiene las tablas. Script 01 cancelado.', 16, 1);
    SET NOEXEC ON;
END
GO

/* =====================================================================================
   ESQUEMAS
   ===================================================================================== */
IF SCHEMA_ID(N'cat') IS NULL EXEC(N'CREATE SCHEMA cat AUTHORIZATION dbo;');  -- catálogos y configuración
IF SCHEMA_ID(N'trx') IS NULL EXEC(N'CREATE SCHEMA trx AUTHORIZATION dbo;');  -- transaccional
IF SCHEMA_ID(N'seg') IS NULL EXEC(N'CREATE SCHEMA seg AUTHORIZATION dbo;');  -- seguridad
IF SCHEMA_ID(N'aud') IS NULL EXEC(N'CREATE SCHEMA aud AUTHORIZATION dbo;');  -- bitácora
GO

BEGIN TRANSACTION;

/* =====================================================================================
   CATÁLOGOS PEQUEÑOS (esquema cat)
   Estructura común: Id, Codigo (único y estable), Nombre, Descripcion, Orden, Activo + auditoría
   ===================================================================================== */

CREATE TABLE cat.TipoCliente (
    IdTipoCliente       int IDENTITY(1,1) NOT NULL,
    Codigo              varchar(50)    NOT NULL,
    Nombre              nvarchar(150)  NOT NULL,
    Descripcion         nvarchar(500)  NULL,
    Orden               int            NOT NULL DEFAULT (0),
    Activo              bit            NOT NULL DEFAULT (1),
    CreadoPor           nvarchar(100)  NOT NULL DEFAULT (N'SISTEMA'),
    FechaCreacion       datetime2(3)   NOT NULL DEFAULT (SYSUTCDATETIME()),
    ModificadoPor       nvarchar(100)  NULL,
    FechaModificacion   datetime2(3)   NULL,
    CONSTRAINT PK_TipoCliente PRIMARY KEY CLUSTERED (IdTipoCliente),
    CONSTRAINT UQ_TipoCliente_Codigo UNIQUE (Codigo)
);

CREATE TABLE cat.TipoIdentificacion (
    IdTipoIdentificacion int IDENTITY(1,1) NOT NULL,
    Codigo              varchar(50)    NOT NULL,
    Nombre              nvarchar(150)  NOT NULL,
    Descripcion         nvarchar(500)  NULL,
    Orden               int            NOT NULL DEFAULT (0),
    Activo              bit            NOT NULL DEFAULT (1),
    CreadoPor           nvarchar(100)  NOT NULL DEFAULT (N'SISTEMA'),
    FechaCreacion       datetime2(3)   NOT NULL DEFAULT (SYSUTCDATETIME()),
    ModificadoPor       nvarchar(100)  NULL,
    FechaModificacion   datetime2(3)   NULL,
    CONSTRAINT PK_TipoIdentificacion PRIMARY KEY CLUSTERED (IdTipoIdentificacion),
    CONSTRAINT UQ_TipoIdentificacion_Codigo UNIQUE (Codigo)
);

CREATE TABLE cat.Segmentacion (
    IdSegmentacion      int IDENTITY(1,1) NOT NULL,
    Codigo              varchar(50)    NOT NULL,
    Nombre              nvarchar(150)  NOT NULL,
    Descripcion         nvarchar(500)  NULL,
    Orden               int            NOT NULL DEFAULT (0),
    Activo              bit            NOT NULL DEFAULT (1),
    CreadoPor           nvarchar(100)  NOT NULL DEFAULT (N'SISTEMA'),
    FechaCreacion       datetime2(3)   NOT NULL DEFAULT (SYSUTCDATETIME()),
    ModificadoPor       nvarchar(100)  NULL,
    FechaModificacion   datetime2(3)   NULL,
    CONSTRAINT PK_Segmentacion PRIMARY KEY CLUSTERED (IdSegmentacion),
    CONSTRAINT UQ_Segmentacion_Codigo UNIQUE (Codigo)
);

CREATE TABLE cat.EstadoExpediente (
    IdEstadoExpediente  int IDENTITY(1,1) NOT NULL,
    Codigo              varchar(50)    NOT NULL,
    Nombre              nvarchar(150)  NOT NULL,
    Descripcion         nvarchar(500)  NULL,
    PermiteCambios      bit            NOT NULL DEFAULT (1),   -- 0 = cerrado/archivado: no recibe documentos
    Orden               int            NOT NULL DEFAULT (0),
    Activo              bit            NOT NULL DEFAULT (1),
    CreadoPor           nvarchar(100)  NOT NULL DEFAULT (N'SISTEMA'),
    FechaCreacion       datetime2(3)   NOT NULL DEFAULT (SYSUTCDATETIME()),
    ModificadoPor       nvarchar(100)  NULL,
    FechaModificacion   datetime2(3)   NULL,
    CONSTRAINT PK_EstadoExpediente PRIMARY KEY CLUSTERED (IdEstadoExpediente),
    CONSTRAINT UQ_EstadoExpediente_Codigo UNIQUE (Codigo)
);

CREATE TABLE cat.EstadoCarga (
    IdEstadoCarga       int IDENTITY(1,1) NOT NULL,
    Codigo              varchar(50)    NOT NULL,
    Nombre              nvarchar(150)  NOT NULL,
    Descripcion         nvarchar(500)  NULL,
    Orden               int            NOT NULL DEFAULT (0),
    Activo              bit            NOT NULL DEFAULT (1),
    CreadoPor           nvarchar(100)  NOT NULL DEFAULT (N'SISTEMA'),
    FechaCreacion       datetime2(3)   NOT NULL DEFAULT (SYSUTCDATETIME()),
    ModificadoPor       nvarchar(100)  NULL,
    FechaModificacion   datetime2(3)   NULL,
    CONSTRAINT PK_EstadoCarga PRIMARY KEY CLUSTERED (IdEstadoCarga),
    CONSTRAINT UQ_EstadoCarga_Codigo UNIQUE (Codigo)
);

CREATE TABLE cat.MotivoRechazoCarga (
    IdMotivoRechazoCarga int IDENTITY(1,1) NOT NULL,
    Codigo              varchar(50)    NOT NULL,
    Nombre              nvarchar(150)  NOT NULL,
    Descripcion         nvarchar(500)  NULL,
    Orden               int            NOT NULL DEFAULT (0),
    Activo              bit            NOT NULL DEFAULT (1),
    CreadoPor           nvarchar(100)  NOT NULL DEFAULT (N'SISTEMA'),
    FechaCreacion       datetime2(3)   NOT NULL DEFAULT (SYSUTCDATETIME()),
    ModificadoPor       nvarchar(100)  NULL,
    FechaModificacion   datetime2(3)   NULL,
    CONSTRAINT PK_MotivoRechazoCarga PRIMARY KEY CLUSTERED (IdMotivoRechazoCarga),
    CONSTRAINT UQ_MotivoRechazoCarga_Codigo UNIQUE (Codigo)
);

CREATE TABLE cat.ReglaCarga (
    IdReglaCarga        int IDENTITY(1,1) NOT NULL,
    Codigo              varchar(50)    NOT NULL,
    Nombre              nvarchar(150)  NOT NULL,
    Descripcion         nvarchar(500)  NULL,
    Orden               int            NOT NULL DEFAULT (0),
    Activo              bit            NOT NULL DEFAULT (1),
    CreadoPor           nvarchar(100)  NOT NULL DEFAULT (N'SISTEMA'),
    FechaCreacion       datetime2(3)   NOT NULL DEFAULT (SYSUTCDATETIME()),
    ModificadoPor       nvarchar(100)  NULL,
    FechaModificacion   datetime2(3)   NULL,
    CONSTRAINT PK_ReglaCarga PRIMARY KEY CLUSTERED (IdReglaCarga),
    CONSTRAINT UQ_ReglaCarga_Codigo UNIQUE (Codigo)
);

CREATE TABLE cat.OrigenAsociacion (
    IdOrigenAsociacion  int IDENTITY(1,1) NOT NULL,
    Codigo              varchar(50)    NOT NULL,
    Nombre              nvarchar(150)  NOT NULL,
    Descripcion         nvarchar(500)  NULL,
    Orden               int            NOT NULL DEFAULT (0),
    Activo              bit            NOT NULL DEFAULT (1),
    CreadoPor           nvarchar(100)  NOT NULL DEFAULT (N'SISTEMA'),
    FechaCreacion       datetime2(3)   NOT NULL DEFAULT (SYSUTCDATETIME()),
    ModificadoPor       nvarchar(100)  NULL,
    FechaModificacion   datetime2(3)   NULL,
    CONSTRAINT PK_OrigenAsociacion PRIMARY KEY CLUSTERED (IdOrigenAsociacion),
    CONSTRAINT UQ_OrigenAsociacion_Codigo UNIQUE (Codigo)
);

CREATE TABLE cat.TipoRechazo (
    IdTipoRechazo       int IDENTITY(1,1) NOT NULL,
    Codigo              varchar(50)    NOT NULL,
    Nombre              nvarchar(150)  NOT NULL,
    Descripcion         nvarchar(500)  NULL,
    Orden               int            NOT NULL DEFAULT (0),
    Activo              bit            NOT NULL DEFAULT (1),
    CreadoPor           nvarchar(100)  NOT NULL DEFAULT (N'SISTEMA'),
    FechaCreacion       datetime2(3)   NOT NULL DEFAULT (SYSUTCDATETIME()),
    ModificadoPor       nvarchar(100)  NULL,
    FechaModificacion   datetime2(3)   NULL,
    CONSTRAINT PK_TipoRechazo PRIMARY KEY CLUSTERED (IdTipoRechazo),
    CONSTRAINT UQ_TipoRechazo_Codigo UNIQUE (Codigo)
);

CREATE TABLE cat.EtapaRechazo (
    IdEtapaRechazo      int IDENTITY(1,1) NOT NULL,
    Codigo              varchar(50)    NOT NULL,
    Nombre              nvarchar(150)  NOT NULL,
    Descripcion         nvarchar(500)  NULL,
    Orden               int            NOT NULL DEFAULT (0),
    Activo              bit            NOT NULL DEFAULT (1),
    CreadoPor           nvarchar(100)  NOT NULL DEFAULT (N'SISTEMA'),
    FechaCreacion       datetime2(3)   NOT NULL DEFAULT (SYSUTCDATETIME()),
    ModificadoPor       nvarchar(100)  NULL,
    FechaModificacion   datetime2(3)   NULL,
    CONSTRAINT PK_EtapaRechazo PRIMARY KEY CLUSTERED (IdEtapaRechazo),
    CONSTRAINT UQ_EtapaRechazo_Codigo UNIQUE (Codigo)
);

CREATE TABLE cat.TipoDato (
    IdTipoDato          int IDENTITY(1,1) NOT NULL,
    Codigo              varchar(50)    NOT NULL,
    Nombre              nvarchar(150)  NOT NULL,
    Descripcion         nvarchar(500)  NULL,
    Orden               int            NOT NULL DEFAULT (0),
    Activo              bit            NOT NULL DEFAULT (1),
    CreadoPor           nvarchar(100)  NOT NULL DEFAULT (N'SISTEMA'),
    FechaCreacion       datetime2(3)   NOT NULL DEFAULT (SYSUTCDATETIME()),
    ModificadoPor       nvarchar(100)  NULL,
    FechaModificacion   datetime2(3)   NULL,
    CONSTRAINT PK_TipoDato PRIMARY KEY CLUSTERED (IdTipoDato),
    CONSTRAINT UQ_TipoDato_Codigo UNIQUE (Codigo)
);

CREATE TABLE cat.ReglaNormalizacion (
    IdReglaNormalizacion int IDENTITY(1,1) NOT NULL,
    Codigo              varchar(50)    NOT NULL,
    Nombre              nvarchar(150)  NOT NULL,
    Descripcion         nvarchar(500)  NULL,
    Orden               int            NOT NULL DEFAULT (0),
    Activo              bit            NOT NULL DEFAULT (1),
    CreadoPor           nvarchar(100)  NOT NULL DEFAULT (N'SISTEMA'),
    FechaCreacion       datetime2(3)   NOT NULL DEFAULT (SYSUTCDATETIME()),
    ModificadoPor       nvarchar(100)  NULL,
    FechaModificacion   datetime2(3)   NULL,
    CONSTRAINT PK_ReglaNormalizacion PRIMARY KEY CLUSTERED (IdReglaNormalizacion),
    CONSTRAINT UQ_ReglaNormalizacion_Codigo UNIQUE (Codigo)
);

CREATE TABLE cat.TipoOperacion (
    IdTipoOperacion     int IDENTITY(1,1) NOT NULL,
    Codigo              varchar(50)    NOT NULL,
    Nombre              nvarchar(150)  NOT NULL,
    Descripcion         nvarchar(500)  NULL,
    Orden               int            NOT NULL DEFAULT (0),
    Activo              bit            NOT NULL DEFAULT (1),
    CreadoPor           nvarchar(100)  NOT NULL DEFAULT (N'SISTEMA'),
    FechaCreacion       datetime2(3)   NOT NULL DEFAULT (SYSUTCDATETIME()),
    ModificadoPor       nvarchar(100)  NULL,
    FechaModificacion   datetime2(3)   NULL,
    CONSTRAINT PK_TipoOperacion PRIMARY KEY CLUSTERED (IdTipoOperacion),
    CONSTRAINT UQ_TipoOperacion_Codigo UNIQUE (Codigo)
);

CREATE TABLE cat.OrigenOperacion (
    IdOrigenOperacion   int IDENTITY(1,1) NOT NULL,
    Codigo              varchar(50)    NOT NULL,
    Nombre              nvarchar(150)  NOT NULL,
    Descripcion         nvarchar(500)  NULL,
    Orden               int            NOT NULL DEFAULT (0),
    Activo              bit            NOT NULL DEFAULT (1),
    CreadoPor           nvarchar(100)  NOT NULL DEFAULT (N'SISTEMA'),
    FechaCreacion       datetime2(3)   NOT NULL DEFAULT (SYSUTCDATETIME()),
    ModificadoPor       nvarchar(100)  NULL,
    FechaModificacion   datetime2(3)   NULL,
    CONSTRAINT PK_OrigenOperacion PRIMARY KEY CLUSTERED (IdOrigenOperacion),
    CONSTRAINT UQ_OrigenOperacion_Codigo UNIQUE (Codigo)
);

CREATE TABLE cat.ResultadoOperacion (
    IdResultadoOperacion int IDENTITY(1,1) NOT NULL,
    Codigo              varchar(50)    NOT NULL,
    Nombre              nvarchar(150)  NOT NULL,
    Descripcion         nvarchar(500)  NULL,
    Orden               int            NOT NULL DEFAULT (0),
    Activo              bit            NOT NULL DEFAULT (1),
    CreadoPor           nvarchar(100)  NOT NULL DEFAULT (N'SISTEMA'),
    FechaCreacion       datetime2(3)   NOT NULL DEFAULT (SYSUTCDATETIME()),
    ModificadoPor       nvarchar(100)  NULL,
    FechaModificacion   datetime2(3)   NULL,
    CONSTRAINT PK_ResultadoOperacion PRIMARY KEY CLUSTERED (IdResultadoOperacion),
    CONSTRAINT UQ_ResultadoOperacion_Codigo UNIQUE (Codigo)
);

-- Códigos de respuesta de la API. Codigo es numérico (ej. 1 = éxito), como pide el requerimiento.
CREATE TABLE cat.CodigoRespuesta (
    IdCodigoRespuesta   int IDENTITY(1,1) NOT NULL,
    Codigo              int            NOT NULL,
    Clave               varchar(60)    NOT NULL,   -- nombre estable para el código C# (ej. ExpedienteNoEncontrado)
    Mensaje             nvarchar(300)  NOT NULL,
    HttpStatus          smallint       NOT NULL,
    Descripcion         nvarchar(500)  NULL,
    Activo              bit            NOT NULL DEFAULT (1),
    CreadoPor           nvarchar(100)  NOT NULL DEFAULT (N'SISTEMA'),
    FechaCreacion       datetime2(3)   NOT NULL DEFAULT (SYSUTCDATETIME()),
    ModificadoPor       nvarchar(100)  NULL,
    FechaModificacion   datetime2(3)   NULL,
    CONSTRAINT PK_CodigoRespuesta PRIMARY KEY CLUSTERED (IdCodigoRespuesta),
    CONSTRAINT UQ_CodigoRespuesta_Codigo UNIQUE (Codigo),
    CONSTRAINT UQ_CodigoRespuesta_Clave UNIQUE (Clave),
    CONSTRAINT CK_CodigoRespuesta_HttpStatus CHECK (HttpStatus BETWEEN 100 AND 599)
);

CREATE TABLE cat.EstadoSincronizacion (
    IdEstadoSincronizacion int IDENTITY(1,1) NOT NULL,
    Codigo              varchar(50)    NOT NULL,
    Nombre              nvarchar(150)  NOT NULL,
    Descripcion         nvarchar(500)  NULL,
    Orden               int            NOT NULL DEFAULT (0),
    Activo              bit            NOT NULL DEFAULT (1),
    CreadoPor           nvarchar(100)  NOT NULL DEFAULT (N'SISTEMA'),
    FechaCreacion       datetime2(3)   NOT NULL DEFAULT (SYSUTCDATETIME()),
    ModificadoPor       nvarchar(100)  NULL,
    FechaModificacion   datetime2(3)   NULL,
    CONSTRAINT PK_EstadoSincronizacion PRIMARY KEY CLUSTERED (IdEstadoSincronizacion),
    CONSTRAINT UQ_EstadoSincronizacion_Codigo UNIQUE (Codigo)
);

/* =====================================================================================
   NÚCLEO - CATÁLOGOS (esquema cat)
   ===================================================================================== */

CREATE TABLE cat.EstadoDocumento (
    IdEstadoDocumento   int IDENTITY(1,1) NOT NULL,
    Codigo              varchar(50)    NOT NULL,
    Nombre              nvarchar(150)  NOT NULL,
    Descripcion         nvarchar(500)  NULL,
    RequiereTipoRechazo bit            NOT NULL DEFAULT (0),
    RequiereComentario  bit            NOT NULL DEFAULT (0),
    SoloSistema         bit            NOT NULL DEFAULT (0),   -- solo lo asigna un proceso automático (ej. Vencido)
    EsDerivado          bit            NOT NULL DEFAULT (0),   -- se calcula, nunca se guarda (ej. Sin subir)
    Orden               int            NOT NULL DEFAULT (0),
    Activo              bit            NOT NULL DEFAULT (1),
    CreadoPor           nvarchar(100)  NOT NULL DEFAULT (N'SISTEMA'),
    FechaCreacion       datetime2(3)   NOT NULL DEFAULT (SYSUTCDATETIME()),
    ModificadoPor       nvarchar(100)  NULL,
    FechaModificacion   datetime2(3)   NULL,
    CONSTRAINT PK_EstadoDocumento PRIMARY KEY CLUSTERED (IdEstadoDocumento),
    CONSTRAINT UQ_EstadoDocumento_Codigo UNIQUE (Codigo)
);

CREATE TABLE cat.TransicionEstadoDocumento (
    IdTransicionEstadoDocumento int IDENTITY(1,1) NOT NULL,
    IdEstadoOrigen      int            NOT NULL,
    IdEstadoDestino     int            NOT NULL,
    Activo              bit            NOT NULL DEFAULT (1),
    CreadoPor           nvarchar(100)  NOT NULL DEFAULT (N'SISTEMA'),
    FechaCreacion       datetime2(3)   NOT NULL DEFAULT (SYSUTCDATETIME()),
    ModificadoPor       nvarchar(100)  NULL,
    FechaModificacion   datetime2(3)   NULL,
    CONSTRAINT PK_TransicionEstadoDocumento PRIMARY KEY CLUSTERED (IdTransicionEstadoDocumento),
    CONSTRAINT UQ_TransicionEstadoDocumento UNIQUE (IdEstadoOrigen, IdEstadoDestino),
    CONSTRAINT FK_TransicionEstado_Origen  FOREIGN KEY (IdEstadoOrigen)  REFERENCES cat.EstadoDocumento (IdEstadoDocumento),
    CONSTRAINT FK_TransicionEstado_Destino FOREIGN KEY (IdEstadoDestino) REFERENCES cat.EstadoDocumento (IdEstadoDocumento),
    CONSTRAINT CK_TransicionEstado_Distintos CHECK (IdEstadoOrigen <> IdEstadoDestino)
);

CREATE TABLE cat.TipoArchivo (
    IdTipoArchivo       int IDENTITY(1,1) NOT NULL,
    Codigo              varchar(20)    NOT NULL,   -- extensión sin punto: pdf, docx, xlsx...
    Nombre              nvarchar(150)  NOT NULL,
    Descripcion         nvarchar(500)  NULL,
    MimeType            varchar(150)   NOT NULL,
    FirmaBytes          varbinary(16)  NULL,       -- magic bytes para validar el contenido real
    Orden               int            NOT NULL DEFAULT (0),
    Activo              bit            NOT NULL DEFAULT (1),
    CreadoPor           nvarchar(100)  NOT NULL DEFAULT (N'SISTEMA'),
    FechaCreacion       datetime2(3)   NOT NULL DEFAULT (SYSUTCDATETIME()),
    ModificadoPor       nvarchar(100)  NULL,
    FechaModificacion   datetime2(3)   NULL,
    CONSTRAINT PK_TipoArchivo PRIMARY KEY CLUSTERED (IdTipoArchivo),
    CONSTRAINT UQ_TipoArchivo_Codigo UNIQUE (Codigo)
);

-- IdTipoDocumento no es IDENTITY: es el ID de negocio que viaja en el XML (ej. 32).
CREATE TABLE cat.TipoDocumento (
    IdTipoDocumento     int            NOT NULL,
    Codigo              varchar(50)    NOT NULL,
    Nombre              nvarchar(150)  NOT NULL,
    Descripcion         nvarchar(500)  NULL,
    TamanoMaximoMB      int            NOT NULL DEFAULT (20),
    DiasVigencia        int            NULL,       -- NULL = no vence
    IdReglaCarga        int            NOT NULL,
    Orden               int            NOT NULL DEFAULT (0),
    Activo              bit            NOT NULL DEFAULT (1),
    CreadoPor           nvarchar(100)  NOT NULL DEFAULT (N'SISTEMA'),
    FechaCreacion       datetime2(3)   NOT NULL DEFAULT (SYSUTCDATETIME()),
    ModificadoPor       nvarchar(100)  NULL,
    FechaModificacion   datetime2(3)   NULL,
    CONSTRAINT PK_TipoDocumento PRIMARY KEY CLUSTERED (IdTipoDocumento),
    CONSTRAINT UQ_TipoDocumento_Codigo UNIQUE (Codigo),
    CONSTRAINT FK_TipoDocumento_ReglaCarga FOREIGN KEY (IdReglaCarga) REFERENCES cat.ReglaCarga (IdReglaCarga),
    CONSTRAINT CK_TipoDocumento_Tamano CHECK (TamanoMaximoMB > 0),
    CONSTRAINT CK_TipoDocumento_Vigencia CHECK (DiasVigencia IS NULL OR DiasVigencia > 0)
);

CREATE TABLE cat.TipoExpediente (
    IdTipoExpediente    int IDENTITY(1,1) NOT NULL,
    Codigo              varchar(50)    NOT NULL,
    Nombre              nvarchar(150)  NOT NULL,
    Descripcion         nvarchar(500)  NULL,
    Orden               int            NOT NULL DEFAULT (0),
    Activo              bit            NOT NULL DEFAULT (0),   -- se activa solo con configuración de llaves válida
    CreadoPor           nvarchar(100)  NOT NULL DEFAULT (N'SISTEMA'),
    FechaCreacion       datetime2(3)   NOT NULL DEFAULT (SYSUTCDATETIME()),
    ModificadoPor       nvarchar(100)  NULL,
    FechaModificacion   datetime2(3)   NULL,
    CONSTRAINT PK_TipoExpediente PRIMARY KEY CLUSTERED (IdTipoExpediente),
    CONSTRAINT UQ_TipoExpediente_Codigo UNIQUE (Codigo),
    CONSTRAINT UQ_TipoExpediente_Nombre UNIQUE (Nombre)
);

CREATE TABLE cat.Llave (
    IdLlave             int IDENTITY(1,1) NOT NULL,
    Codigo              varchar(50)    NOT NULL,   -- igual al atributo tipo del XML: CIF, RTN, noCasoCRM...
    Nombre              nvarchar(150)  NOT NULL,
    Descripcion         nvarchar(500)  NULL,
    IdentificaPersona   bit            NOT NULL DEFAULT (0),
    IdTipoDato          int            NOT NULL,
    LongitudMaxima      int            NOT NULL DEFAULT (100),
    ExpresionValidacion nvarchar(500)  NULL,       -- expresión regular que valida la API
    CatalogoValidacion  sysname        NULL,       -- tabla catálogo contra la que se valida el valor (ej. cat.TipoIdentificacion)
    IdReglaNormalizacion int           NOT NULL,
    Orden               int            NOT NULL DEFAULT (0),
    Activo              bit            NOT NULL DEFAULT (1),
    CreadoPor           nvarchar(100)  NOT NULL DEFAULT (N'SISTEMA'),
    FechaCreacion       datetime2(3)   NOT NULL DEFAULT (SYSUTCDATETIME()),
    ModificadoPor       nvarchar(100)  NULL,
    FechaModificacion   datetime2(3)   NULL,
    CONSTRAINT PK_Llave PRIMARY KEY CLUSTERED (IdLlave),
    CONSTRAINT UQ_Llave_Codigo UNIQUE (Codigo),
    CONSTRAINT FK_Llave_TipoDato FOREIGN KEY (IdTipoDato) REFERENCES cat.TipoDato (IdTipoDato),
    CONSTRAINT FK_Llave_ReglaNormalizacion FOREIGN KEY (IdReglaNormalizacion) REFERENCES cat.ReglaNormalizacion (IdReglaNormalizacion),
    CONSTRAINT CK_Llave_Longitud CHECK (LongitudMaxima BETWEEN 1 AND 250)
);

/* =====================================================================================
   RELACIONES DE CONFIGURACIÓN (esquema cat)
   ===================================================================================== */

-- Formatos permitidos por tipo de documento (criterio 14)
CREATE TABLE cat.TipoDocumentoTipoArchivo (
    IdTipoDocumento     int            NOT NULL,
    IdTipoArchivo       int            NOT NULL,
    Activo              bit            NOT NULL DEFAULT (1),
    CreadoPor           nvarchar(100)  NOT NULL DEFAULT (N'SISTEMA'),
    FechaCreacion       datetime2(3)   NOT NULL DEFAULT (SYSUTCDATETIME()),
    ModificadoPor       nvarchar(100)  NULL,
    FechaModificacion   datetime2(3)   NULL,
    CONSTRAINT PK_TipoDocumentoTipoArchivo PRIMARY KEY CLUSTERED (IdTipoDocumento, IdTipoArchivo),
    CONSTRAINT FK_TDTA_TipoDocumento FOREIGN KEY (IdTipoDocumento) REFERENCES cat.TipoDocumento (IdTipoDocumento),
    CONSTRAINT FK_TDTA_TipoArchivo   FOREIGN KEY (IdTipoArchivo)   REFERENCES cat.TipoArchivo (IdTipoArchivo)
);
CREATE INDEX IX_TDTA_TipoArchivo ON cat.TipoDocumentoTipoArchivo (IdTipoArchivo);

-- Qué documentos lleva cada tipo de expediente (criterio 3). Base de la asociación automática.
-- IdTipoCliente NULL = aplica a natural y jurídico.
CREATE TABLE cat.TipoExpedienteTipoDocumento (
    IdTipoExpedienteTipoDocumento int IDENTITY(1,1) NOT NULL,
    IdTipoExpediente    int            NOT NULL,
    IdTipoDocumento     int            NOT NULL,
    IdTipoCliente       int            NULL,
    Obligatorio         bit            NOT NULL DEFAULT (1),
    Orden               int            NOT NULL DEFAULT (0),
    Activo              bit            NOT NULL DEFAULT (1),
    CreadoPor           nvarchar(100)  NOT NULL DEFAULT (N'SISTEMA'),
    FechaCreacion       datetime2(3)   NOT NULL DEFAULT (SYSUTCDATETIME()),
    ModificadoPor       nvarchar(100)  NULL,
    FechaModificacion   datetime2(3)   NULL,
    CONSTRAINT PK_TipoExpedienteTipoDocumento PRIMARY KEY CLUSTERED (IdTipoExpedienteTipoDocumento),
    CONSTRAINT UQ_TETD UNIQUE (IdTipoExpediente, IdTipoDocumento, IdTipoCliente),
    CONSTRAINT FK_TETD_TipoExpediente FOREIGN KEY (IdTipoExpediente) REFERENCES cat.TipoExpediente (IdTipoExpediente),
    CONSTRAINT FK_TETD_TipoDocumento  FOREIGN KEY (IdTipoDocumento)  REFERENCES cat.TipoDocumento (IdTipoDocumento),
    CONSTRAINT FK_TETD_TipoCliente    FOREIGN KEY (IdTipoCliente)    REFERENCES cat.TipoCliente (IdTipoCliente)
);
CREATE INDEX IX_TETD_TipoDocumento ON cat.TipoExpedienteTipoDocumento (IdTipoDocumento) INCLUDE (IdTipoExpediente, IdTipoCliente, Activo);

-- Llaves por tipo de expediente y tipo de cliente.
-- GrupoIdentificacion NULL = llave descriptiva. Todo grupo debe tener 2 o más llaves
-- (lo valida cat.usp_ValidarConfiguracionLlaves en el script 03).
CREATE TABLE cat.TipoExpedienteLlave (
    IdTipoExpedienteLlave int IDENTITY(1,1) NOT NULL,
    IdTipoExpediente    int            NOT NULL,
    IdTipoCliente       int            NOT NULL,
    IdLlave             int            NOT NULL,
    Obligatoria         bit            NOT NULL DEFAULT (0),
    GrupoIdentificacion tinyint        NULL,
    PrioridadGrupo      tinyint        NULL,
    Orden               int            NOT NULL DEFAULT (0),
    Activo              bit            NOT NULL DEFAULT (1),
    CreadoPor           nvarchar(100)  NOT NULL DEFAULT (N'SISTEMA'),
    FechaCreacion       datetime2(3)   NOT NULL DEFAULT (SYSUTCDATETIME()),
    ModificadoPor       nvarchar(100)  NULL,
    FechaModificacion   datetime2(3)   NULL,
    CONSTRAINT PK_TipoExpedienteLlave PRIMARY KEY CLUSTERED (IdTipoExpedienteLlave),
    CONSTRAINT UQ_TEL UNIQUE (IdTipoExpediente, IdTipoCliente, IdLlave, GrupoIdentificacion),
    CONSTRAINT FK_TEL_TipoExpediente FOREIGN KEY (IdTipoExpediente) REFERENCES cat.TipoExpediente (IdTipoExpediente),
    CONSTRAINT FK_TEL_TipoCliente    FOREIGN KEY (IdTipoCliente)    REFERENCES cat.TipoCliente (IdTipoCliente),
    CONSTRAINT FK_TEL_Llave          FOREIGN KEY (IdLlave)          REFERENCES cat.Llave (IdLlave),
    CONSTRAINT CK_TEL_Grupo CHECK (
        (GrupoIdentificacion IS NULL AND PrioridadGrupo IS NULL)
        OR (GrupoIdentificacion IS NOT NULL AND PrioridadGrupo IS NOT NULL)
    )
);
CREATE INDEX IX_TEL_Llave ON cat.TipoExpedienteLlave (IdLlave);

-- Control de versiones de catálogos para invalidar la caché en memoria de cada instancia.
CREATE TABLE cat.VersionCatalogo (
    NombreCatalogo      sysname        NOT NULL,
    Version             bigint         NOT NULL DEFAULT (1),
    FechaModificacion   datetime2(3)   NOT NULL DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT PK_VersionCatalogo PRIMARY KEY CLUSTERED (NombreCatalogo)
);

/* =====================================================================================
   TRANSACCIONAL (esquema trx)
   ===================================================================================== */

CREATE TABLE trx.Expediente (
    IdExpediente        bigint IDENTITY(1,1) NOT NULL,
    IdTipoExpediente    int            NOT NULL,
    IdTipoCliente       int            NOT NULL,
    IdEstadoExpediente  int            NOT NULL,
    FechaApertura       datetime2(3)   NOT NULL DEFAULT (SYSUTCDATETIME()),
    FechaCierre         datetime2(3)   NULL,
    CreadoPor           nvarchar(100)  NOT NULL,
    FechaCreacion       datetime2(3)   NOT NULL DEFAULT (SYSUTCDATETIME()),
    ModificadoPor       nvarchar(100)  NULL,
    FechaModificacion   datetime2(3)   NULL,
    RowVersion          rowversion     NOT NULL,
    CONSTRAINT PK_Expediente PRIMARY KEY CLUSTERED (IdExpediente),
    CONSTRAINT FK_Expediente_TipoExpediente   FOREIGN KEY (IdTipoExpediente)   REFERENCES cat.TipoExpediente (IdTipoExpediente),
    CONSTRAINT FK_Expediente_TipoCliente      FOREIGN KEY (IdTipoCliente)      REFERENCES cat.TipoCliente (IdTipoCliente),
    CONSTRAINT FK_Expediente_EstadoExpediente FOREIGN KEY (IdEstadoExpediente) REFERENCES cat.EstadoExpediente (IdEstadoExpediente),
    CONSTRAINT CK_Expediente_Fechas CHECK (FechaCierre IS NULL OR FechaCierre >= FechaApertura)
);
CREATE INDEX IX_Expediente_TipoExpediente ON trx.Expediente (IdTipoExpediente, IdTipoCliente);
CREATE INDEX IX_Expediente_Estado ON trx.Expediente (IdEstadoExpediente);

-- Valores de llave de cada expediente. Un solo valor vigente por llave y expediente.
CREATE TABLE trx.LlaveExpediente (
    IdLlaveExpediente   bigint IDENTITY(1,1) NOT NULL,
    IdExpediente        bigint         NOT NULL,
    IdLlave             int            NOT NULL,
    Valor               nvarchar(250)  NOT NULL,
    ValorNormalizado    nvarchar(250)  NOT NULL,
    Vigente             bit            NOT NULL DEFAULT (1),
    CreadoPor           nvarchar(100)  NOT NULL,
    FechaCreacion       datetime2(3)   NOT NULL DEFAULT (SYSUTCDATETIME()),
    ModificadoPor       nvarchar(100)  NULL,
    FechaModificacion   datetime2(3)   NULL,
    RowVersion          rowversion     NOT NULL,
    CONSTRAINT PK_LlaveExpediente PRIMARY KEY CLUSTERED (IdLlaveExpediente),
    CONSTRAINT FK_LlaveExpediente_Expediente FOREIGN KEY (IdExpediente) REFERENCES trx.Expediente (IdExpediente),
    CONSTRAINT FK_LlaveExpediente_Llave      FOREIGN KEY (IdLlave)      REFERENCES cat.Llave (IdLlave)
);
CREATE UNIQUE INDEX UX_LlaveExpediente_Vigente ON trx.LlaveExpediente (IdExpediente, IdLlave) WHERE Vigente = 1;
CREATE INDEX IX_LlaveExpediente_Busqueda ON trx.LlaveExpediente (IdLlave, ValorNormalizado) INCLUDE (IdExpediente) WHERE Vigente = 1;

-- Identidad de cada grupo de llaves. El índice único impide expedientes duplicados,
-- incluso con varias instancias de la API atendiendo en paralelo.
CREATE TABLE trx.ExpedienteIdentidad (
    IdExpedienteIdentidad bigint IDENTITY(1,1) NOT NULL,
    IdExpediente        bigint         NOT NULL,
    IdTipoExpediente    int            NOT NULL,
    IdTipoCliente       int            NOT NULL,
    GrupoIdentificacion tinyint        NOT NULL,
    IdentidadNormalizada nvarchar(450) NOT NULL,   -- valores del grupo concatenados con '|'
    FechaCreacion       datetime2(3)   NOT NULL DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT PK_ExpedienteIdentidad PRIMARY KEY CLUSTERED (IdExpedienteIdentidad),
    CONSTRAINT FK_ExpedienteIdentidad_Expediente     FOREIGN KEY (IdExpediente)     REFERENCES trx.Expediente (IdExpediente),
    CONSTRAINT FK_ExpedienteIdentidad_TipoExpediente FOREIGN KEY (IdTipoExpediente) REFERENCES cat.TipoExpediente (IdTipoExpediente),
    CONSTRAINT FK_ExpedienteIdentidad_TipoCliente    FOREIGN KEY (IdTipoCliente)    REFERENCES cat.TipoCliente (IdTipoCliente)
);
CREATE UNIQUE INDEX UX_ExpedienteIdentidad ON trx.ExpedienteIdentidad (IdTipoExpediente, IdTipoCliente, GrupoIdentificacion, IdentidadNormalizada);
CREATE INDEX IX_ExpedienteIdentidad_Expediente ON trx.ExpedienteIdentidad (IdExpediente);

-- Identidad de persona (solo llaves con IdentificaPersona = 1). Une expedientes de la misma persona.
CREATE TABLE trx.ExpedientePersona (
    IdExpedientePersona bigint IDENTITY(1,1) NOT NULL,
    IdExpediente        bigint         NOT NULL,
    IdentidadPersonaNormalizada nvarchar(300) NOT NULL,   -- ej. CIF|12345678, DNI|1234567890101
    FechaCreacion       datetime2(3)   NOT NULL DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT PK_ExpedientePersona PRIMARY KEY CLUSTERED (IdExpedientePersona),
    CONSTRAINT FK_ExpedientePersona_Expediente FOREIGN KEY (IdExpediente) REFERENCES trx.Expediente (IdExpediente)
);
CREATE UNIQUE INDEX UX_ExpedientePersona ON trx.ExpedientePersona (IdExpediente, IdentidadPersonaNormalizada);
CREATE INDEX IX_ExpedientePersona_Identidad ON trx.ExpedientePersona (IdentidadPersonaNormalizada) INCLUDE (IdExpediente);

-- Cada carga recibida por SFTP (criterios 11 a 13). Las FK quedan NULL si el XML traía valores
-- inválidos; el contenido original queda en XmlOriginal.
CREATE TABLE trx.CargaDocumento (
    IdCargaDocumento    bigint IDENTITY(1,1) NOT NULL,
    Correlativo         varchar(50)    NOT NULL,
    IdExpediente        bigint         NULL,
    IdTipoDocumento     int            NULL,
    IdEstadoCarga       int            NOT NULL,
    IdMotivoRechazoCarga int           NULL,
    DetalleRechazo      nvarchar(1000) NULL,
    LaserficheEntryId   int            NULL,
    FechaHoraRecepcion  datetime2(3)   NOT NULL DEFAULT (SYSUTCDATETIME()),
    XmlOriginal         xml            NULL,
    CreadoPor           nvarchar(100)  NOT NULL,
    FechaCreacion       datetime2(3)   NOT NULL DEFAULT (SYSUTCDATETIME()),
    ModificadoPor       nvarchar(100)  NULL,
    FechaModificacion   datetime2(3)   NULL,
    RowVersion          rowversion     NOT NULL,
    CONSTRAINT PK_CargaDocumento PRIMARY KEY CLUSTERED (IdCargaDocumento),
    CONSTRAINT UQ_CargaDocumento_Correlativo UNIQUE (Correlativo),
    CONSTRAINT FK_CargaDocumento_Expediente    FOREIGN KEY (IdExpediente)         REFERENCES trx.Expediente (IdExpediente),
    CONSTRAINT FK_CargaDocumento_TipoDocumento FOREIGN KEY (IdTipoDocumento)      REFERENCES cat.TipoDocumento (IdTipoDocumento),
    CONSTRAINT FK_CargaDocumento_EstadoCarga   FOREIGN KEY (IdEstadoCarga)        REFERENCES cat.EstadoCarga (IdEstadoCarga),
    CONSTRAINT FK_CargaDocumento_Motivo        FOREIGN KEY (IdMotivoRechazoCarga) REFERENCES cat.MotivoRechazoCarga (IdMotivoRechazoCarga)
);
CREATE INDEX IX_CargaDocumento_Expediente ON trx.CargaDocumento (IdExpediente);
CREATE INDEX IX_CargaDocumento_LaserficheEntryId ON trx.CargaDocumento (LaserficheEntryId) WHERE LaserficheEntryId IS NOT NULL;

CREATE TABLE trx.Documento (
    IdDocumento         bigint IDENTITY(1,1) NOT NULL,
    LaserficheEntryId   int            NOT NULL,   -- ID único que se expone a CRM
    IdCargaDocumento    bigint         NULL,
    IdExpedienteOrigen  bigint         NOT NULL,
    IdTipoDocumento     int            NOT NULL,
    IdTipoArchivo       int            NOT NULL,
    IdEstadoDocumento   int            NOT NULL,
    NombreArchivo       nvarchar(260)  NOT NULL,
    TamanoBytes         bigint         NULL,
    HashSha256          char(64)       NULL,
    Version             int            NOT NULL DEFAULT (1),
    Vigente             bit            NOT NULL DEFAULT (1),   -- 0 = reemplazado por una versión nueva
    FechaEmision        date           NOT NULL,
    FechaHoraRecepcion  datetime2(3)   NOT NULL DEFAULT (SYSUTCDATETIME()),
    FechaVencimiento    date           NULL,
    UsuarioCarga        varchar(50)    NOT NULL,
    NombreUsuarioCarga  nvarchar(200)  NOT NULL,
    Comentario          nvarchar(1000) NULL,
    IdDocumentoReemplazado bigint      NULL,
    CreadoPor           nvarchar(100)  NOT NULL,
    FechaCreacion       datetime2(3)   NOT NULL DEFAULT (SYSUTCDATETIME()),
    ModificadoPor       nvarchar(100)  NULL,
    FechaModificacion   datetime2(3)   NULL,
    RowVersion          rowversion     NOT NULL,
    CONSTRAINT PK_Documento PRIMARY KEY CLUSTERED (IdDocumento),
    CONSTRAINT UQ_Documento_LaserficheEntryId UNIQUE (LaserficheEntryId),
    CONSTRAINT FK_Documento_Carga            FOREIGN KEY (IdCargaDocumento)       REFERENCES trx.CargaDocumento (IdCargaDocumento),
    CONSTRAINT FK_Documento_ExpedienteOrigen FOREIGN KEY (IdExpedienteOrigen)     REFERENCES trx.Expediente (IdExpediente),
    CONSTRAINT FK_Documento_TipoDocumento    FOREIGN KEY (IdTipoDocumento)        REFERENCES cat.TipoDocumento (IdTipoDocumento),
    CONSTRAINT FK_Documento_TipoArchivo      FOREIGN KEY (IdTipoArchivo)          REFERENCES cat.TipoArchivo (IdTipoArchivo),
    CONSTRAINT FK_Documento_EstadoDocumento  FOREIGN KEY (IdEstadoDocumento)      REFERENCES cat.EstadoDocumento (IdEstadoDocumento),
    CONSTRAINT FK_Documento_Reemplazado      FOREIGN KEY (IdDocumentoReemplazado) REFERENCES trx.Documento (IdDocumento),
    CONSTRAINT CK_Documento_Version CHECK (Version >= 1),
    CONSTRAINT CK_Documento_Hash CHECK (HashSha256 IS NULL OR LEN(HashSha256) = 64),
    CONSTRAINT CK_Documento_NoAutoReemplazo CHECK (IdDocumentoReemplazado IS NULL OR IdDocumentoReemplazado <> IdDocumento)
);
CREATE INDEX IX_Documento_ExpedienteOrigen ON trx.Documento (IdExpedienteOrigen);
CREATE INDEX IX_Documento_TipoDocumento ON trx.Documento (IdTipoDocumento) WHERE Vigente = 1;
CREATE INDEX IX_Documento_Vencimiento ON trx.Documento (FechaVencimiento) INCLUDE (IdEstadoDocumento) WHERE Vigente = 1 AND FechaVencimiento IS NOT NULL;
CREATE UNIQUE INDEX UX_Documento_Carga ON trx.Documento (IdCargaDocumento) WHERE IdCargaDocumento IS NOT NULL;

-- Relación N:M: un documento puede pertenecer a varios expedientes.
CREATE TABLE trx.ExpedienteDocumento (
    IdExpediente        bigint         NOT NULL,
    IdDocumento         bigint         NOT NULL,
    IdOrigenAsociacion  int            NOT NULL,
    FechaAsociacion     datetime2(3)   NOT NULL DEFAULT (SYSUTCDATETIME()),
    Vigente             bit            NOT NULL DEFAULT (1),   -- 0 = desasociado (ej. reemplazado por otra versión)
    CreadoPor           nvarchar(100)  NOT NULL,
    FechaCreacion       datetime2(3)   NOT NULL DEFAULT (SYSUTCDATETIME()),
    ModificadoPor       nvarchar(100)  NULL,
    FechaModificacion   datetime2(3)   NULL,
    CONSTRAINT PK_ExpedienteDocumento PRIMARY KEY CLUSTERED (IdExpediente, IdDocumento),
    CONSTRAINT FK_ExpedienteDocumento_Expediente FOREIGN KEY (IdExpediente)       REFERENCES trx.Expediente (IdExpediente),
    CONSTRAINT FK_ExpedienteDocumento_Documento  FOREIGN KEY (IdDocumento)        REFERENCES trx.Documento (IdDocumento),
    CONSTRAINT FK_ExpedienteDocumento_Origen     FOREIGN KEY (IdOrigenAsociacion) REFERENCES cat.OrigenAsociacion (IdOrigenAsociacion)
);
CREATE INDEX IX_ExpedienteDocumento_Documento ON trx.ExpedienteDocumento (IdDocumento) INCLUDE (Vigente);

-- Historial de cambios de estado (criterio 10).
CREATE TABLE trx.DocumentoEstadoHistorial (
    IdDocumentoEstadoHistorial bigint IDENTITY(1,1) NOT NULL,
    IdDocumento         bigint         NOT NULL,
    IdExpediente        bigint         NULL,
    IdEstadoAnterior    int            NULL,
    IdEstadoNuevo       int            NOT NULL,
    IdTipoRechazo       int            NULL,
    IdEtapaRechazo      int            NULL,
    Comentario          nvarchar(1000) NULL,
    EjecutivoAsignado   nvarchar(200)  NULL,
    UsuarioOperacion    nvarchar(100)  NOT NULL,
    FechaHoraUtc        datetime2(3)   NOT NULL DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT PK_DocumentoEstadoHistorial PRIMARY KEY CLUSTERED (IdDocumentoEstadoHistorial),
    CONSTRAINT FK_DEH_Documento      FOREIGN KEY (IdDocumento)      REFERENCES trx.Documento (IdDocumento),
    CONSTRAINT FK_DEH_Expediente     FOREIGN KEY (IdExpediente)     REFERENCES trx.Expediente (IdExpediente),
    CONSTRAINT FK_DEH_EstadoAnterior FOREIGN KEY (IdEstadoAnterior) REFERENCES cat.EstadoDocumento (IdEstadoDocumento),
    CONSTRAINT FK_DEH_EstadoNuevo    FOREIGN KEY (IdEstadoNuevo)    REFERENCES cat.EstadoDocumento (IdEstadoDocumento),
    CONSTRAINT FK_DEH_TipoRechazo    FOREIGN KEY (IdTipoRechazo)    REFERENCES cat.TipoRechazo (IdTipoRechazo),
    CONSTRAINT FK_DEH_EtapaRechazo   FOREIGN KEY (IdEtapaRechazo)   REFERENCES cat.EtapaRechazo (IdEtapaRechazo)
);
CREATE INDEX IX_DEH_Documento ON trx.DocumentoEstadoHistorial (IdDocumento, FechaHoraUtc);
CREATE INDEX IX_DEH_Expediente ON trx.DocumentoEstadoHistorial (IdExpediente) WHERE IdExpediente IS NOT NULL;

-- Outbox de actualizaciones a Laserfiche. El stored procedure que cambia el estado inserta aquí en
-- la misma transacción; la API y el Worker lo procesan y reintentan si Laserfiche falla.
-- Campos: JSON con llaves lógicas (Estado, TipoRechazo, EtapaRechazo, ComentarioRevision,
-- EjecutivoAsignado) que la API traduce a los nombres de campo configurados en Laserfiche.
CREATE TABLE trx.SincronizacionLaserfiche (
    IdSincronizacion    bigint IDENTITY(1,1) NOT NULL,
    IdDocumento         bigint         NOT NULL,
    LaserficheEntryId   int            NOT NULL,
    Campos              nvarchar(max)  NOT NULL,
    IdEstadoSincronizacion int         NOT NULL,
    IdOrigenOperacion   int            NOT NULL,
    Intentos            int            NOT NULL DEFAULT (0),
    ProximoIntentoUtc   datetime2(3)   NOT NULL DEFAULT (SYSUTCDATETIME()),
    UltimoError         nvarchar(1000) NULL,
    CreadoPor           nvarchar(100)  NOT NULL,
    FechaCreacion       datetime2(3)   NOT NULL DEFAULT (SYSUTCDATETIME()),
    FechaModificacion   datetime2(3)   NULL,
    CONSTRAINT PK_SincronizacionLaserfiche PRIMARY KEY CLUSTERED (IdSincronizacion),
    CONSTRAINT FK_SincLF_Documento FOREIGN KEY (IdDocumento)            REFERENCES trx.Documento (IdDocumento),
    CONSTRAINT FK_SincLF_Estado    FOREIGN KEY (IdEstadoSincronizacion) REFERENCES cat.EstadoSincronizacion (IdEstadoSincronizacion),
    CONSTRAINT FK_SincLF_Origen    FOREIGN KEY (IdOrigenOperacion)      REFERENCES cat.OrigenOperacion (IdOrigenOperacion),
    CONSTRAINT CK_SincLF_Campos CHECK (ISJSON(Campos) = 1)
);
CREATE INDEX IX_SincLF_Pendientes ON trx.SincronizacionLaserfiche (IdEstadoSincronizacion, ProximoIntentoUtc) INCLUDE (LaserficheEntryId);
CREATE INDEX IX_SincLF_Documento ON trx.SincronizacionLaserfiche (IdDocumento);

/* =====================================================================================
   SEGURIDAD (esquema seg)
   ===================================================================================== */

CREATE TABLE seg.Scope (
    IdScope             int IDENTITY(1,1) NOT NULL,
    Codigo              varchar(50)    NOT NULL,   -- ej. documentos.leer
    Nombre              nvarchar(150)  NOT NULL,
    Descripcion         nvarchar(500)  NULL,
    Orden               int            NOT NULL DEFAULT (0),
    Activo              bit            NOT NULL DEFAULT (1),
    CreadoPor           nvarchar(100)  NOT NULL DEFAULT (N'SISTEMA'),
    FechaCreacion       datetime2(3)   NOT NULL DEFAULT (SYSUTCDATETIME()),
    ModificadoPor       nvarchar(100)  NULL,
    FechaModificacion   datetime2(3)   NULL,
    CONSTRAINT PK_Scope PRIMARY KEY CLUSTERED (IdScope),
    CONSTRAINT UQ_Scope_Codigo UNIQUE (Codigo)
);

-- Credenciales del CRM (usuario de servicio). El secreto se guarda solo como hash.
CREATE TABLE seg.CuentaServicio (
    IdCuentaServicio    int IDENTITY(1,1) NOT NULL,
    ClientId            varchar(100)   NOT NULL,
    Nombre              nvarchar(150)  NOT NULL,
    SecretHash          varchar(500)   NOT NULL,
    FechaExpiracionSecreto datetime2(3) NULL,
    Activo              bit            NOT NULL DEFAULT (1),
    CreadoPor           nvarchar(100)  NOT NULL DEFAULT (N'SISTEMA'),
    FechaCreacion       datetime2(3)   NOT NULL DEFAULT (SYSUTCDATETIME()),
    ModificadoPor       nvarchar(100)  NULL,
    FechaModificacion   datetime2(3)   NULL,
    CONSTRAINT PK_CuentaServicio PRIMARY KEY CLUSTERED (IdCuentaServicio),
    CONSTRAINT UQ_CuentaServicio_ClientId UNIQUE (ClientId)
);

CREATE TABLE seg.CuentaServicioScope (
    IdCuentaServicio    int            NOT NULL,
    IdScope             int            NOT NULL,
    CreadoPor           nvarchar(100)  NOT NULL DEFAULT (N'SISTEMA'),
    FechaCreacion       datetime2(3)   NOT NULL DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT PK_CuentaServicioScope PRIMARY KEY CLUSTERED (IdCuentaServicio, IdScope),
    CONSTRAINT FK_CSS_CuentaServicio FOREIGN KEY (IdCuentaServicio) REFERENCES seg.CuentaServicio (IdCuentaServicio),
    CONSTRAINT FK_CSS_Scope          FOREIGN KEY (IdScope)          REFERENCES seg.Scope (IdScope)
);

-- Llaves de ASP.NET Data Protection compartidas entre instancias
-- (estructura esperada por Microsoft.AspNetCore.DataProtection.EntityFrameworkCore).
CREATE TABLE seg.DataProtectionKeys (
    Id                  int IDENTITY(1,1) NOT NULL,
    FriendlyName        nvarchar(max)  NULL,
    Xml                 nvarchar(max)  NULL,
    CONSTRAINT PK_DataProtectionKeys PRIMARY KEY CLUSTERED (Id)
);

/* =====================================================================================
   BITÁCORA E IDEMPOTENCIA (esquema aud)
   ===================================================================================== */

-- Bitácora funcional. Solo inserción: el trigger que impide UPDATE y DELETE está en el script 03.
-- IdExpediente e IdDocumento NO llevan FK a propósito: la bitácora nunca debe fallar, aunque
-- registre un id inválido (ej. un XML con un expediente que no existe).
-- El PK agrupado por (FechaHoraUtc, IdBitacora) deja la tabla lista para particionar por mes.
CREATE TABLE aud.Bitacora (
    IdBitacora          bigint IDENTITY(1,1) NOT NULL,
    FechaHoraUtc        datetime2(3)   NOT NULL DEFAULT (SYSUTCDATETIME()),
    IdTipoOperacion     int            NOT NULL,
    IdOrigenOperacion   int            NOT NULL,
    IdResultadoOperacion int           NOT NULL,
    CodigoRespuesta     int            NULL,
    IdExpediente        bigint         NULL,
    IdDocumento         bigint         NULL,
    Correlativo         varchar(50)    NULL,
    UsuarioServicio     nvarchar(100)  NOT NULL,
    UsuarioOperacion    nvarchar(100)  NULL,
    IpOrigen            varchar(45)    NULL,
    Instancia           nvarchar(100)  NULL,
    CorrelationId       varchar(64)    NULL,
    Endpoint            nvarchar(300)  NULL,
    MetodoHttp          varchar(10)    NULL,
    DuracionMs          int            NULL,
    DatosAnteriores     nvarchar(max)  NULL,
    DatosNuevos         nvarchar(max)  NULL,
    Detalle             nvarchar(2000) NULL,
    CONSTRAINT PK_Bitacora PRIMARY KEY CLUSTERED (FechaHoraUtc, IdBitacora),
    CONSTRAINT FK_Bitacora_TipoOperacion      FOREIGN KEY (IdTipoOperacion)      REFERENCES cat.TipoOperacion (IdTipoOperacion),
    CONSTRAINT FK_Bitacora_OrigenOperacion    FOREIGN KEY (IdOrigenOperacion)    REFERENCES cat.OrigenOperacion (IdOrigenOperacion),
    CONSTRAINT FK_Bitacora_ResultadoOperacion FOREIGN KEY (IdResultadoOperacion) REFERENCES cat.ResultadoOperacion (IdResultadoOperacion),
    CONSTRAINT CK_Bitacora_DatosAnteriores CHECK (DatosAnteriores IS NULL OR ISJSON(DatosAnteriores) = 1),
    CONSTRAINT CK_Bitacora_DatosNuevos     CHECK (DatosNuevos IS NULL OR ISJSON(DatosNuevos) = 1)
);
CREATE INDEX IX_Bitacora_Expediente    ON aud.Bitacora (IdExpediente, FechaHoraUtc) WHERE IdExpediente IS NOT NULL;
CREATE INDEX IX_Bitacora_Documento     ON aud.Bitacora (IdDocumento, FechaHoraUtc) WHERE IdDocumento IS NOT NULL;
CREATE INDEX IX_Bitacora_CorrelationId ON aud.Bitacora (CorrelationId) WHERE CorrelationId IS NOT NULL;
CREATE INDEX IX_Bitacora_Usuario       ON aud.Bitacora (UsuarioOperacion, FechaHoraUtc) WHERE UsuarioOperacion IS NOT NULL;
CREATE INDEX IX_Bitacora_TipoOperacion ON aud.Bitacora (IdTipoOperacion, FechaHoraUtc);

-- Idempotencia de POST /expedientes y POST /documentos/estado.
CREATE TABLE aud.IdempotenciaRequest (
    ClientId            varchar(100)   NOT NULL,
    IdempotencyKey      varchar(100)   NOT NULL,
    Endpoint            nvarchar(300)  NOT NULL,
    HashRequest         char(64)       NOT NULL,   -- SHA-256 del body, para detectar la misma llave con otro contenido
    HttpStatus          smallint       NULL,       -- NULL = en proceso
    RespuestaJson       nvarchar(max)  NULL,
    FechaCreacionUtc    datetime2(3)   NOT NULL DEFAULT (SYSUTCDATETIME()),
    FechaExpiracionUtc  datetime2(3)   NOT NULL,
    CONSTRAINT PK_IdempotenciaRequest PRIMARY KEY CLUSTERED (ClientId, IdempotencyKey)
);
CREATE INDEX IX_IdempotenciaRequest_Expiracion ON aud.IdempotenciaRequest (FechaExpiracionUtc);

COMMIT TRANSACTION;
GO

PRINT N'01_tablas.sql ejecutado correctamente.';
GO

SET NOEXEC OFF;
GO
