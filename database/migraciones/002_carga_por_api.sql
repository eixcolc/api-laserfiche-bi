/* =====================================================================================
   BILF - Migración 002: carga de documentos por la API.
   - cat.MotivoRechazoCarga.CodigoRespuesta: el código que se devuelve con cada motivo.
   Para bases creadas antes de este cambio. Las bases nuevas ya lo tienen en 01_tablas.sql.
   Se puede volver a ejecutar. Después, ejecutar 02_catalogos.sql y 03_procedimientos_triggers.sql.
   ===================================================================================== */

USE [BILF];
GO

SET NOCOUNT ON;
SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

IF COL_LENGTH(N'cat.MotivoRechazoCarga', N'CodigoRespuesta') IS NULL
    ALTER TABLE cat.MotivoRechazoCarga ADD CodigoRespuesta int NULL;
GO

IF OBJECT_ID(N'cat.FK_MotivoRechazoCarga_CodigoRespuesta', N'F') IS NULL
    ALTER TABLE cat.MotivoRechazoCarga
        ADD CONSTRAINT FK_MotivoRechazoCarga_CodigoRespuesta FOREIGN KEY (CodigoRespuesta) REFERENCES cat.CodigoRespuesta (Codigo);
GO

PRINT N'Migración 002 aplicada. Ejecute ahora 02_catalogos.sql y 03_procedimientos_triggers.sql.';
GO
