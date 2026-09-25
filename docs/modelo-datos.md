# Modelo de datos — base BILF

Fuente de verdad: los scripts de [database/](../database). Este documento resume las tablas; el
detalle de columnas, llaves e índices está en `01_tablas.sql`.

## Diagrama

```
TipoExpediente ──< TipoExpedienteLlave >── Llave
      │                                      │
      ├──< TipoExpedienteTipoDocumento >── TipoDocumento ──< TipoDocumentoTipoArchivo >── TipoArchivo
      │                                      │
  Expediente ──< LlaveExpediente             │
      │  ├──< ExpedienteIdentidad            │
      │  └──< ExpedientePersona              │
      │                                      │
      └──< ExpedienteDocumento >── Documento ┘── EstadoDocumento
                                      ├──< DocumentoEstadoHistorial
                                      ├──< SincronizacionLaserfiche
                                      └──  CargaDocumento

  Bitacora ── registra toda operación
```
(`──<` = uno a muchos; `>──<` = muchos a muchos)

## Esquemas

| Esquema | Contenido |
|---|---|
| `cat` | Catálogos y configuración. Los mantiene el módulo "Administración Base de Datos Auxiliar LF" |
| `trx` | Datos transaccionales. Se escriben solo mediante stored procedures |
| `seg` | Cuentas de servicio, scopes y llaves de Data Protection |
| `aud` | Bitácora (solo inserción) e idempotencia |

## Tablas núcleo

| Tabla | Para qué | Columnas clave |
|---|---|---|
| `cat.TipoExpediente` | Tipos de expediente ("categorías de proceso") | Codigo, Nombre, Activo (solo activo con llaves válidas) |
| `trx.Expediente` | Expediente de un tipo y un conjunto de llaves | IdExpediente (va en el XML), IdTipoExpediente, IdTipoCliente, IdEstadoExpediente |
| `cat.TipoDocumento` | Tipos de documento | IdTipoDocumento (ID de negocio, ej. 32), TamanoMaximoMB, DiasVigencia, IdReglaCarga |
| `cat.TipoArchivo` | Formatos | Codigo (extensión), MimeType, FirmaBytes |
| `cat.EstadoDocumento` | Estados | Codigo, RequiereTipoRechazo, RequiereComentario, SoloSistema, EsDerivado |
| `trx.Documento` | Documento importado a Laserfiche | LaserficheEntryId (el `idDocumento` del CRM), IdExpedienteOrigen, IdEstadoDocumento, Version, Vigente, FechaVencimiento |
| `cat.Llave` | Tipos de llave (CIF, RTN, noCasoCRM…) | Codigo, IdentificaPersona, IdTipoDato, ExpresionValidacion, CatalogoValidacion |
| `trx.LlaveExpediente` | Valor de cada llave de un expediente | IdExpediente, IdLlave, Valor, ValorNormalizado, Vigente |

## Tablas de relación

| Tabla | Para qué |
|---|---|
| `cat.TipoExpedienteTipoDocumento` | Qué documentos lleva cada tipo de expediente (criterio 3); base de la asociación automática. IdTipoCliente NULL = ambos |
| `cat.TipoExpedienteLlave` | Llaves de cada tipo de expediente y tipo de cliente, en grupos de identificación de **2 o más llaves** |
| `trx.ExpedienteDocumento` | Un documento en varios expedientes (N:M), con su origen (Carga, Automática, Manual) |
| `cat.TipoDocumentoTipoArchivo` | Formatos permitidos por tipo de documento (criterio 14) |

## Apoyo

| Tabla | Para qué |
|---|---|
| `trx.ExpedienteIdentidad` | Identidad de cada grupo de llaves; su índice único impide expedientes duplicados entre instancias |
| `trx.ExpedientePersona` | Identidad de persona (CIF, RTN, identificación); une expedientes de la misma persona |
| `trx.CargaDocumento` | Cada carga por SFTP, importada o rechazada con su motivo |
| `trx.DocumentoEstadoHistorial` | Historial de cambios de estado (criterio 10) |
| `trx.SincronizacionLaserfiche` | Outbox de actualizaciones de campos hacia Laserfiche |
| `cat.TransicionEstadoDocumento` | Cambios de estado permitidos |
| `aud.Bitacora` | Bitácora funcional, solo inserción |
| `aud.IdempotenciaRequest` | Respuestas guardadas por `Idempotency-Key` (24 h) |
| `cat.VersionCatalogo` | Versión por catálogo, para invalidar la caché de cada instancia |

Catálogos pequeños: TipoCliente, TipoIdentificacion, Segmentacion, EstadoExpediente, EstadoCarga,
MotivoRechazoCarga, ReglaCarga, OrigenAsociacion, TipoRechazo, EtapaRechazo, TipoDato,
ReglaNormalizacion, TipoOperacion, OrigenOperacion, ResultadoOperacion, EstadoSincronizacion,
CodigoRespuesta.

## Stored procedures

| Procedimiento | Lo usa | Qué hace |
|---|---|---|
| `trx.usp_ObtenerOCrearExpediente` | API | Busca el expediente por llaves o lo crea (`@SoloBuscar = 1` para las consultas) |
| `trx.usp_RegistrarDocumento` | Workflow | Registra un documento importado, reemplazo y asociación automática |
| `trx.usp_CambiarEstadoDocumentos` | API | Cambio de estado en lote, historial, bitácora y outbox |
| `trx.usp_VencerDocumentos` | Worker | Vencimiento diario por lotes |
| `trx.usp_AsociarDocumentosPersona` | Interno | Lleva a un expediente los documentos vigentes de la misma persona |
| `aud.usp_RegistrarBitacora` | Todos | Inserta en la bitácora |
| `cat.usp_ValidarConfiguracionLlaves` | Mantenimiento | Lista los problemas de configuración de llaves de un tipo de expediente |

## Roles

| Rol | Para |
|---|---|
| `rol_bilf_api` | Cuenta de la API y del Worker: lee todo, escribe mediante stored procedures |
| `rol_bilf_workflow` | Cuenta del workflow: solo `usp_RegistrarDocumento` y la vista de llaves |
| `rol_bilf_mantenimiento` | Módulo de catálogos: edita el esquema `cat` sin poder borrar |
