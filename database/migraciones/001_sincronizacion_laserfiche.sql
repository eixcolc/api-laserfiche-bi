/* =====================================================================================
   BILF - Migración 001: outbox de sincronización con Laserfiche.
   Para bases creadas antes de este cambio. Las bases nuevas ya lo tienen en 01_tablas.sql.
   Se puede volver a ejecutar. Después de esta migración, ejecutar 02_catalogos.sql y
   03_procedimientos_triggers.sql.
   ===================================================================================== */

USE [BILF];
GO

SET NOCOUNT ON;
SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

BEGIN TRANSACTION;

IF OBJECT_ID(N'cat.EstadoSincronizacion', N'U') IS NULL
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

IF OBJECT_ID(N'trx.SincronizacionLaserfiche', N'U') IS NULL
BEGIN
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
END

COMMIT TRANSACTION;
GO

PRINT N'Migración 001 aplicada. Ejecute ahora 02_catalogos.sql y 03_procedimientos_triggers.sql.';
GO
