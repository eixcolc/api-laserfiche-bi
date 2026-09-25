/* =====================================================================================
   BILF (Base de Datos Auxiliar LF) - 00_crear_base.sql
   Crea la base BILF si no existe. Si ya existe, no hace nada.

   Usa la ubicación de archivos, el tamaño y el collation por defecto del servidor. Si el DBA
   define otros valores (rutas de datos y log, crecimiento, collation), ajusta este script o
   crea la base manualmente y omítelo.
   ===================================================================================== */

USE [master];
GO

IF DB_ID(N'BILF') IS NULL
BEGIN
    CREATE DATABASE [BILF];
    PRINT N'Base BILF creada.';
END
ELSE
    PRINT N'La base BILF ya existe. No se hizo ningún cambio.';
GO

-- Aísla las lecturas de las escrituras sin bloqueos (recomendado con varias instancias de la API).
IF EXISTS (SELECT 1 FROM sys.databases WHERE name = N'BILF' AND is_read_committed_snapshot_on = 0)
    ALTER DATABASE [BILF] SET READ_COMMITTED_SNAPSHOT ON WITH ROLLBACK IMMEDIATE;
GO
