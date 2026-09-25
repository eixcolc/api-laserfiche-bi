# Rol
Eres un arquitecto y desarrollador senior de .NET. Vas a construir desde cero, en la carpeta actual,
una API REST en .NET 10 que da al CRM del banco la consulta y gestión de documentos guardados en
Laserfiche 11. La API usa una base SQL Server auxiliar llamada **BILF** ("Base de Datos Auxiliar LF") para
catálogos, parámetros y metadatos transaccionales.

Consumidor principal: el CRM. Todos los contratos (request, response, códigos de error) deben
quedar documentados para que CRM los pueda mapear.

# Arquitectura de la solución completa (contexto)
La carga de documentos **no pasa por la API**:

1. **Obtener el expediente**: el CRM llama a la API con el tipo de expediente y el conjunto de
   llaves del cliente (TipoCliente, noCasoCRM, CIF, RTN, identificación…). La API devuelve el
   `idExpediente` y, si no existía, crea el expediente con sus llaves.
2. **Subir por SFTP**: el CRM sube dos archivos a la carpeta de entrada del servidor Laserfiche:
   `{Correlativo}_archivo.{ext}` y `{Correlativo}_data.xml`. El XML lleva solo el `idExpediente` y
   los datos del documento.
3. **Importar**: Laserfiche Import Agent (instalado en ese servidor) importa el archivo a
   Laserfiche y asigna los metadatos a partir del XML.
4. **Registrar**: un workflow de Laserfiche registra el documento en la Base de Datos Auxiliar LF
   llamando a un stored procedure que entrega este proyecto.
5. **Consultar y gestionar**: la API da acceso a catálogos, consultas transaccionales, descarga del
   documento, cambio de estado y consulta del estado de una carga.

Este proyecto entrega:
- la API;
- la base de datos, con los stored procedures que usa el workflow;
- el contrato del XML (XSD);
- la especificación de los campos de la plantilla de Laserfiche y de lo que deben hacer los
  workflows.

Los workflows los construye el equipo de Laserfiche.

# Glosario (usa estos nombres en código y contratos)
| Término de negocio | Tabla | Notas |
|---|---|---|
| Tipo de expediente (en el requerimiento: "categoría de proceso") | `TipoExpediente` | Ej.: Banca Corporativa, Tarjeta de Crédito. Se llama "tipo de expediente" en todas partes: base de datos, código y contratos (`idTipoExpediente`, `nombreTipoExpediente`, `/tipos-expediente`). En la documentación para CRM se aclara que equivale a la "categoría de proceso" del requerimiento |
| Expediente | `Expediente` | Pertenece a un tipo de expediente y a un **conjunto de llaves**. Su `IdExpediente` lo genera la BD y es lo que viaja en el XML |
| Tipo de documento | `TipoDocumento` | Ej.: id 32. Tiene reglas de vigencia y versión |
| Tipo de archivo | `TipoArchivo` | pdf, docx, xlsx, xlsm, jpg… |
| Estado de documento | `EstadoDocumento` | Aprobado, Rechazado, Pendiente de Revisión, Vencido, Sin subir, Bajo Excepción |
| Documento | `Documento` | Su ID expuesto a CRM es `LaserficheEntryId`, el ID único que genera Laserfiche |
| Llave | `Llave` | Catálogo de llaves: TipoCliente, noCasoCRM, CIF, RTN… Igual que `<llave tipo="...">` del prototipo |
| Valor de llave de un expediente | `LlaveExpediente` | Ej.: expediente 1024, llave CIF, valor 12345678 |
| Llave identificadora | `TipoExpedienteLlave.GrupoIdentificacion` no nulo | Forma parte de la identidad del expediente |
| Llave descriptiva | `TipoExpedienteLlave.GrupoIdentificacion` nulo | Dato informativo (nombre, razón social, segmentación) |
| Grupo de identificación | `TipoExpedienteLlave.GrupoIdentificacion` | Llaves que juntas identifican un expediente. **Todo grupo tiene como mínimo 2 llaves** |
| Llave de persona | `Llave.IdentificaPersona = 1` | CIF, RTN, tipo + número de identificación. Identifica a la persona sin importar el caso; sirve para reutilizar documentos |
| Cliente vinculado / potencial | Expediente con o sin llave CIF | Vinculado: se busca por CIF. Potencial: por RTN (J) o por identificación (N) |
| Correlativo | `CargaDocumento.Correlativo` | Lo genera el CRM y es único por carga |
| Usuario de servicio | Credencial del CRM | Se autentica con usuario y contraseña |
| Usuario de operación | Gerente de cuenta o ejecutivo | Viaja en el payload o en el XML (`usuarioCarga`, `ejecutivoAsignado`) |

# Stack obligatorio
- .NET 10, ASP.NET Core Web API (controllers), C# con nullable activado
- SQL Server. La base se define con **scripts SQL versionados en `database/`**, que son la fuente
  de verdad:
  - `00_crear_base.sql`
  - `01_tablas.sql`
  - `02_catalogos.sql`
  - `03_procedimientos_triggers.sql`

  EF Core 10 se usa solo para mapear esas tablas (database-first, sin generar migraciones). Los
  stored procedures se invocan desde EF o Dapper.
- OAuth 2.0 con emisión de JWT. La API actúa como servidor de tokens; se sugiere OpenIddict, pero
  evalúalo y propón.
- Laserfiche 11 Repository API (REST, self-hosted) para descargar documentos y actualizar campos
  de la plantilla.
  - Todo va detrás de interfaces (`ILaserficheRepositoryService`, `IAsignacionTareaService`) para
    poder cambiar a SDK sin tocar el negocio.
  - Antes de implementar, verifica si el SDK de Laserfiche 11 es compatible con .NET 10 y
    documenta lo que encuentres.
- Caché en memoria con `HybridCache`, usando solo el nivel en memoria. **No se usa Redis** ni otra
  caché externa.
- Serilog, FluentValidation, Swagger/OpenAPI con OAuth2, Polly y health checks

# Arquitectura del código
Clean Architecture con estos proyectos:
- `APILFBI.Domain`
- `APILFBI.Application`
- `APILFBI.Infrastructure`
- `APILFBI.Api`
- `APILFBI.Worker` (procesos en segundo plano)
- `APILFBI.Tests` (xUnit)

# Modelo de datos (Base de Datos Auxiliar LF)
Los catálogos se mantienen desde el módulo externo "Administración Base de Datos Auxiliar LF".
Esta API solo los lee. Entrega los datos semilla iniciales (criterio 6).

## Diagrama
```
TipoExpediente ──< TipoExpedienteLlave >── Llave
      │                                      │
      ├──< TipoExpedienteTipoDocumento >── TipoDocumento ──< TipoDocumentoTipoArchivo >── TipoArchivo
      │                                      │
  Expediente ──< LlaveExpediente             │
      │                                      │
      └──< ExpedienteDocumento >── Documento ┘── EstadoDocumento

  Bitacora ── registra toda operación sobre cualquiera de las tablas anteriores
```
(`──<` = uno a muchos; `>──<` = muchos a muchos)

## Principios
- Base de datos relacional en SQL Server, normalizada (3FN), con esquemas separados: `cat` para
  catálogos y configuración, `trx` para datos transaccionales, `seg` para seguridad y `aud` para
  bitácora.
- Integridad referencial obligatoria: toda relación lleva FOREIGN KEY y los catálogos usan
  `ON DELETE NO ACTION`.
- **Estados y tipos siempre como catálogo**. Ningún estado, tipo o clasificación se guarda como
  texto, enum de C# persistido como número suelto o constraint CHECK con valores fijos. Cada uno
  es una tabla catálogo y se referencia por FK.
- Estructura común de todo catálogo: Id (PK), Codigo (único, estable y usado por el código y los
  contratos), Nombre, Descripcion, Orden, Activo y campos de auditoría.
- En C#, los catálogos que la lógica necesita se referencian por `Codigo` con constantes. Nunca
  por Id, porque los Ids pueden variar entre ambientes.
- Las reglas también son datos: transiciones de estado, requisitos por estado y llaves por tipo
  de expediente están en tablas, no en el código.
- Borrado lógico (Activo = 0) en catálogos. Nunca se borra un catálogo que ya fue referenciado.
- Índices en todas las FK y en los campos de búsqueda: (IdLlave, ValorNormalizado),
  LaserficheEntryId y Correlativo.
- Campos de auditoría en todas las tablas: CreadoPor, FechaCreacion, ModificadoPor,
  FechaModificacion. Además `rowversion` en las tablas transaccionales.

## 1. Tablas núcleo
- **cat.TipoExpediente**
  - Columnas: IdTipoExpediente, Codigo, Nombre, Descripcion, Activo.
  - Semilla: Banca Corporativa, Tarjeta de Crédito…
  - Solo puede quedar Activo = 1 si su configuración de llaves es válida (ver
    TipoExpedienteLlave).
- **trx.Expediente**
  - Columnas:
    - IdExpediente (PK, generado; es el que viaja en el XML)
    - IdTipoExpediente (FK), IdTipoCliente (FK), IdEstadoExpediente (FK)
    - FechaApertura, FechaCierre
  - No tiene columnas fijas de cliente: sus datos son sus llaves (LlaveExpediente).
- **cat.TipoDocumento**
  - Columnas:
    - IdTipoDocumento (ej. 32), Codigo, Nombre, Descripcion
    - TamanoMaximoMB
    - DiasVigencia (null = no vence)
    - IdReglaCarga (FK: Reemplazar o Acumular)
    - Activo
- **cat.TipoArchivo**
  - Columnas: IdTipoArchivo, Codigo (extensión: pdf, docx, xlsx, xlsm, jpg…), Nombre, MimeType,
    FirmaBytes (magic bytes, para validar el contenido real), Activo.
- **cat.EstadoDocumento**
  - Columnas:
    - IdEstadoDocumento, Codigo, Nombre
    - RequiereTipoRechazo, RequiereComentario, SoloSistema, EsDerivado
    - Activo
  - Semilla: Aprobado, Rechazado (RequiereTipoRechazo = 1, RequiereComentario = 1),
    PendienteRevision, Vencido (SoloSistema = 1), SinSubir (EsDerivado = 1, nunca se guarda),
    BajoExcepcion (RequiereComentario = 1).
- **trx.Documento**
  - Columnas:
    - IdDocumento (interno)
    - LaserficheEntryId (único; es el ID que se expone a CRM)
    - IdExpedienteOrigen (FK: el expediente del XML), IdTipoDocumento (FK), IdTipoArchivo (FK),
      IdEstadoDocumento (FK), IdCargaDocumento (FK)
    - NombreArchivo, TamanoBytes, HashSha256, Version
    - FechaEmision, FechaHoraRecepcion, FechaVencimiento
    - UsuarioCarga, NombreUsuarioCarga, Comentario
    - IdDocumentoReemplazado (FK a Documento)
- **cat.Llave**
  - Columnas:
    - IdLlave, Codigo (igual al atributo `tipo` del prototipo), Nombre
    - IdentificaPersona
    - IdTipoDato (FK), LongitudMaxima, ExpresionValidacion
    - CatalogoValidacion (ej. `tipoIdentificacion` se valida contra `cat.TipoIdentificacion`)
    - IdReglaNormalizacion (FK)
    - Activo
  - Semilla:
    - TipoCliente
    - noCasoCRM
    - CIF (IdentificaPersona = 1)
    - RTN (IdentificaPersona = 1)
    - tipoIdentificacion (IdentificaPersona = 1)
    - noIdentificacion (IdentificaPersona = 1)
    - NombreComercial, razonSocial, nombreCompleto, segmentacion
  - Agregar una llave nueva es solo un registro aquí, sin cambiar el esquema.
- **trx.LlaveExpediente**
  - Columnas: IdLlaveExpediente, IdExpediente (FK), IdLlave (FK), Valor, ValorNormalizado,
    Vigente, más los campos de auditoría.
  - Clave lógica: IdExpediente + IdLlave + ValorNormalizado.
  - Una llave identificadora tiene un solo valor vigente y no cambia de valor. La única excepción
    es agregar una que faltaba, como el CIF de un potencial.
  - Las llaves descriptivas guardan histórico: el valor anterior queda con Vigente = 0.

## 2. Tablas de relación (obligatorias)
- **cat.TipoExpedienteTipoDocumento**
  - Es el catálogo que hace automática la asociación de documentos. Corresponde al criterio 3.
  - Columnas: Id, IdTipoExpediente (FK), IdTipoDocumento (FK), IdTipoCliente (FK, null = aplica a
    natural y jurídico), Obligatorio, Orden, Activo.
  - Único: (IdTipoExpediente, IdTipoDocumento, IdTipoCliente).
- **cat.TipoExpedienteLlave**
  - Define qué llaves lleva cada tipo de expediente y con qué combinaciones se identifica un
    expediente.
  - Columnas: Id, IdTipoExpediente (FK), IdTipoCliente (FK), IdLlave (FK), Obligatoria,
    GrupoIdentificacion (null si es descriptiva), PrioridadGrupo, Orden, Activo.
  - Una llave puede estar en varios grupos (ej. noCasoCRM en el grupo 1 y en el 2).
  - Semilla, igual para todos los tipos de expediente iniciales:

    | Tipo de cliente | Grupo 1 (prioridad 1) | Grupo 2 (prioridad 2) | Descriptivas |
    |---|---|---|---|
    | J | noCasoCRM + CIF | noCasoCRM + RTN | NombreComercial, razonSocial, segmentacion |
    | N | noCasoCRM + CIF | noCasoCRM + tipoIdentificacion + noIdentificacion | nombreCompleto, segmentacion |

  - **Regla obligatoria: todo grupo tiene como mínimo 2 llaves**, y un tipo de expediente no
    puede quedar activo con menos de 2 llaves identificadoras. Se garantiza así:
    - **Configuración**: `cat.usp_ValidarConfiguracionLlaves @IdTipoExpediente` verifica que cada
      tipo de cliente tenga al menos un grupo y que cada grupo tenga 2 o más llaves. Un trigger en
      `TipoExpediente` impide activarlo si la validación falla. La configuración se carga con el
      tipo inactivo y se activa al final, porque un trigger fila por fila rechazaría la primera
      llave de cada grupo.
    - **Arranque de la API**: al precargar la caché se vuelve a validar. Un tipo mal configurado
      se excluye y se registra en la bitácora. El resto de la API sigue funcionando.
    - **Cada request**: debe venir completo al menos un grupo (2 o más llaves identificadoras).
      Si no, se rechaza con su código de error.
- **trx.ExpedienteDocumento** (N:M)
  - Un documento puede pertenecer a varios expedientes.
  - Columnas: IdExpediente (FK), IdDocumento (FK) (clave compuesta), IdOrigenAsociacion (FK:
    Automatica o Manual), FechaAsociacion.
- **cat.TipoDocumentoTipoArchivo**
  - Formatos permitidos por tipo de documento (criterio 14).
  - Columnas: IdTipoDocumento (FK), IdTipoArchivo (FK) (clave compuesta).

## 3. Bitácora (esquema `aud`)
**aud.Bitacora**: registro funcional de toda operación. Es distinto de los logs técnicos de
Serilog; la bitácora es la evidencia de auditoría que se consulta en la base.

- **Columnas**:
  - IdBitacora (bigint identity)
  - FechaHoraUtc (datetime2)
  - IdTipoOperacion (FK a `cat.TipoOperacion`)
  - IdOrigenOperacion (FK a `cat.OrigenOperacion`: API, Workflow, Job, Mantenimiento)
  - IdResultadoOperacion (FK a `cat.ResultadoOperacion`: Exito, ErrorValidacion, Conflicto,
    NoAutorizado, ErrorSistema)
  - CodigoRespuesta
  - IdExpediente (FK, null), IdDocumento (FK, null), Correlativo (null)
  - UsuarioServicio (cuenta del CRM, del workflow o del job), UsuarioOperacion (gerente o ejecutivo)
  - IpOrigen, Instancia (hostname del nodo que atendió), CorrelationId
  - Endpoint, MetodoHttp, DuracionMs
  - DatosAnteriores y DatosNuevos (JSON, solo en operaciones que modifican)
  - Detalle (mensaje o error, sin stack trace)
- **Qué se registra**:
  - autenticación exitosa y fallida;
  - creación y actualización de expediente y sus llaves;
  - conflictos de llaves;
  - consultas transaccionales;
  - **descarga o visualización de cada documento** (quién vio qué);
  - cada cambio de estado;
  - registro de documentos y rechazos de carga (los escriben los stored procedures del workflow);
  - vencimientos automáticos;
  - tipos de expediente excluidos por mala configuración.
- **Tipos de operación iniciales** (`cat.TipoOperacion`): Autenticacion, CrearExpediente,
  ActualizarLlaves, ConflictoLlaves, ConsultaDocumentos, DescargaDocumento, CambioEstado,
  RegistroDocumento, RechazoCarga, VencimientoAutomatico, AsociacionAutomatica, ConfiguracionInvalida.
- **Reglas**:
  - **Solo inserción**: no se permite UPDATE ni DELETE. Se aplica DENY al usuario de la aplicación
    y un trigger `INSTEAD OF UPDATE, DELETE` que lo rechaza.
  - **Datos personales**: CIF, DNI y RTN se enmascaran en Detalle y en los JSON. Las columnas de
    relación (IdExpediente) permiten rastrear sin exponer el dato.
  - **Escritura**:
    - Las operaciones que modifican datos escriben la bitácora en la misma transacción.
    - Las de solo lectura (consultas y descargas) la escriben en segundo plano en lotes, con un
      `Channel` en memoria y vaciado al apagar la instancia, para no agregar latencia.
  - **Crecimiento**: índices por FechaHoraUtc, CorrelationId, IdExpediente, IdDocumento y
    UsuarioOperacion. La tabla se particiona por mes (o se documenta cómo hacerlo) y la retención
    es configurable; el archivado no se hace desde la API.
  - **Stored procedure** `aud.usp_RegistrarBitacora`: lo usan la API y los otros stored procedures,
    para que el formato sea siempre el mismo.
- **Consulta**: endpoint `GET /bitacora` con filtros por fecha, expediente, documento, usuario y
  tipo de operación, paginado y con scope exclusivo `bitacora.leer`.

## 4. Tablas de apoyo
Complementan el núcleo. Las que dicen "requerida por" salen del requerimiento; las demás son
técnicas. Si alguna no se justifica en el plan, propón quitarla.

**Apoyo a llaves y expedientes:**
- **trx.ExpedienteIdentidad**
  - Columnas: IdExpediente, IdTipoExpediente, IdTipoCliente, GrupoIdentificacion,
    IdentidadNormalizada (valores del grupo concatenados, ej. `536252673|DNI|1234567890101`).
  - Índice único en (IdTipoExpediente, IdTipoCliente, GrupoIdentificacion, IdentidadNormalizada).
  - Evita expedientes duplicados cuando varias instancias atienden la misma solicitud a la vez, y
    es el índice de búsqueda rápida.
- **trx.ExpedientePersona**
  - Columnas: IdExpediente, IdentidadPersonaNormalizada (solo llaves con IdentificaPersona = 1,
    ej. `CIF|12345678`).
  - Índice no único.
  - Une los expedientes de una misma persona entre casos y tipos de expediente, para reutilizar
    documentos.

**Requeridas por el requerimiento:**
- **trx.DocumentoEstadoHistorial** (criterio 10)
  - Columnas: Id, IdDocumento, IdExpediente, IdEstadoAnterior, IdEstadoNuevo, IdTipoRechazo,
    IdEtapaRechazo, Comentario, EjecutivoAsignado, Usuario, Fecha.
- **trx.CargaDocumento** (criterios 11 a 13)
  - Columnas: Id, Correlativo (único), IdExpediente, IdTipoDocumento, IdEstadoCarga,
    IdMotivoRechazoCarga, DetalleRechazo, LaserficheEntryId, FechaHoraRecepcion, XmlOriginal.
- **cat.TransicionEstadoDocumento**: IdEstadoOrigen, IdEstadoDestino, Activo. Es la matriz de
  cambios de estado permitidos.

**Catálogos pequeños:**
- TipoCliente (N, J)
- TipoIdentificacion (DNI, CarnetResidencia, Pasaporte)
- Segmentacion
- EstadoExpediente (Abierto, Cerrado, Archivado)
- EstadoCarga (Importado, Rechazado)
- MotivoRechazoCarga (ExpedienteNoExiste, ExpedienteCerrado, TipoDocumentoNoExiste,
  TipoNoPerteneceTipoExpediente, FormatoNoPermitido, XmlInvalido, Duplicado, HashNoCoincide…)
- ReglaCarga (Reemplazar, Acumular)
- OrigenAsociacion (Automatica, Manual)
- TipoRechazo
- EtapaRechazo
- TipoDato
- ReglaNormalizacion
- TipoOperacion, OrigenOperacion, ResultadoOperacion
- CodigoRespuesta (Codigo, Mensaje, HttpStatus, Metodo)

**Técnicas:**
- seg.CuentaServicio, seg.Scope, seg.CuentaServicioScope
- seg.DataProtectionKeys
- aud.IdempotenciaRequest
- cat.VersionCatalogo (invalidación de caché)

# Caché de catálogos (en memoria, sin Redis)
- **Qué se cachea**: todo lo del esquema `cat`, es decir, las tablas núcleo de catálogo, las de
  relación de configuración y los catálogos pequeños.
- **Qué no se cachea**: los datos transaccionales (`trx`) ni la bitácora.
- **Implementación**:
  - `ICatalogoCache` sobre `HybridCache`, solo con el nivel en memoria. Protege contra que muchas
    solicitudes simultáneas carguen el mismo catálogo a la vez.
  - Los catálogos se precargan al arrancar. `/health/ready` no reporta listo hasta terminar la
    precarga.
- **Invalidación entre instancias** (cada instancia tiene su propia caché):
  - Tabla `cat.VersionCatalogo` con NombreCatalogo, Version y FechaModificacion.
  - Triggers en las tablas `cat` incrementan la versión. Así el módulo de mantenimiento no
    necesita cambios.
  - Cada instancia consulta esa tabla cada N segundos (configurable, por defecto 60) y recarga solo
    el catálogo que cambió.
  - Además, un TTL absoluto configurable sirve como respaldo.
- **Otras cachés en memoria de cada instancia**: el token de Laserfiche (cada instancia obtiene el
  suyo) y la llave de validación del JWT.

# Escalamiento horizontal (detrás de un balanceador de carga)
La API debe poder correr en N instancias idénticas detrás de un balanceador, sin sticky sessions y
sin depender de Redis:
- **Sin estado compartido en memoria**: nada de sesión. Lo único en memoria son cachés de solo
  lectura, reconstruibles.
- **JWT sin estado**: todas las instancias validan con la misma llave de firma (certificado desde
  Key Vault o el almacén de certificados). Las llaves de Data Protection se guardan en SQL Server.
- **Idempotencia**:
  - La obtención de expediente y el cambio de estado aceptan el header `Idempotency-Key`,
    guardado en SQL.
  - La creación de expediente es segura ante concurrencia gracias al índice único de
    `ExpedienteIdentidad`, más `sp_getapplock` o reintento ante violación de llave única.
- **Concurrencia optimista** con `rowversion` en las tablas transaccionales.
- **Jobs en segundo plano** (vencimiento, alerta de cargas no conciliadas): corren en
  `APILFBI.Worker` o con Hangfire usando almacenamiento en SQL Server, para que se ejecuten una sola
  vez aunque haya varias instancias.
- **Descarga en stream**: el archivo va de Laserfiche al CRM en stream. No se guarda en el disco
  local ni se carga completo en memoria.
- **Salud y ciclo de vida**: `/health/live` y `/health/ready` (SQL Server, Laserfiche y caché
  precargada), `ForwardedHeaders` y apagado controlado, que también vacía la cola de la bitácora.
- **Rate limiting** en memoria por instancia, particionado por cliente OAuth. El límite global se
  aplica en el balanceador o el API gateway.
- **Logs técnicos** (Serilog) con CorrelationId e instancia. El mismo CorrelationId queda en la
  bitácora para cruzar ambos.

# Stored procedures
- **`trx.usp_ObtenerOCrearExpediente`** (lo usa la API):
  - Parámetros: IdTipoExpediente y la lista de llaves como table-valued parameter (CodigoLlave,
    Valor).
  - Pasos:
    1. Valida las llaves contra `TipoExpedienteLlave` y `Llave`: obligatorias, tipo de dato,
       expresión y catálogo de validación. Debe venir completo al menos un grupo (mínimo 2 llaves
       identificadoras).
    2. Normaliza los valores y calcula las identidades de grupo y de persona.
    3. Busca en `ExpedienteIdentidad` en orden de prioridad del grupo: primero noCasoCRM + CIF y
       luego noCasoCRM + RTN o identificación.
    4. Si lo encuentra, agrega las llaves que faltaban y actualiza las descriptivas. Ejemplo: un
       potencial que ahora trae CIF recibe la llave CIF y sigue siendo el mismo expediente.
    5. Si no existe, crea Expediente, LlaveExpediente, ExpedienteIdentidad y ExpedientePersona.
       Le asocia los documentos vigentes de otros expedientes de la misma persona cuyo tipo acepte
       su tipo de expediente.
  - **Conflictos** (no fusiona automáticamente; devuelve un código de conflicto y lo registra en la
    bitácora):
    - distintos grupos apuntan a expedientes diferentes;
    - llega una llave identificadora con un valor distinto al guardado.
  - Devuelve IdExpediente y si es nuevo. Es idempotente y seguro ante concurrencia.
- **`trx.usp_RegistrarDocumento`** (lo usa el workflow de Laserfiche):
  - Parámetros: LaserficheEntryId, IdExpediente y los campos de `informacionDocumento` del XML.
  - Es idempotente por LaserficheEntryId y por Correlativo.
  - En una sola transacción:
    1. Valida que el expediente exista y esté abierto.
    2. Valida que el tipo de documento esté en `TipoExpedienteTipoDocumento` para ese tipo de
       expediente y tipo de cliente, y que el tipo de archivo esté en `TipoDocumentoTipoArchivo`
       (criterio 14).
    3. Crea el Documento en estado "Pendiente de Revisión".
    4. Calcula FechaVencimiento: la del XML o, si no viene, FechaEmision + DiasVigencia.
    5. Aplica la regla de reemplazo.
    6. Crea ExpedienteDocumento para el expediente del XML y ejecuta la asociación automática.
    7. Registra CargaDocumento y la bitácora.
  - Devuelve un código de resultado y un mensaje. Si la validación falla, registra la carga como
    Rechazado con su motivo, para que el workflow mueva el documento a una carpeta de rechazados.
- **`aud.usp_RegistrarBitacora`**: ver la sección de bitácora.
- **`trx.usp_CambiarEstadoDocumentos`** (API, criterio 10):
  - Recibe el lote como table-valued parameter `trx.TipoCambioEstado`.
  - Valida transiciones y requisitos contra los catálogos.
  - Devuelve el resultado por documento. La API actualiza Laserfiche después, solo con los
    documentos aplicados.
- **`trx.usp_VencerDocumentos`** (job diario): procesa por lotes y devuelve los documentos
  vencidos para que el job actualice Laserfiche.
- **Todos ya existen en `database/03_procedimientos_triggers.sql`**, junto con triggers, la vista
  y los roles (`rol_bilf_api`, `rol_bilf_workflow`, `rol_bilf_mantenimiento`). La API los usa
  como están:
  - Los llama sin una transacción externa.
  - Lee el result set y los parámetros OUTPUT.
  - No vuelve a registrar en la bitácora las operaciones que los stored procedures ya registran.
- Documenta en `docs/workflow-laserfiche.md` qué debe hacer cada workflow y con qué parámetros:
  - registro;
  - manejo de rechazados;
  - creación de la tarea al ejecutivo asignado.

# Reglas de negocio
1. **Asociación automática expediente ↔ documento**:
   - Un documento nace en el expediente del XML. Se asocia también a los demás expedientes
     abiertos de la misma persona (`ExpedientePersona`), ya sean otros casos u otros tipos de
     expediente.
   - Aplica solo si esos tipos de expediente incluyen ese tipo de documento en
     `TipoExpedienteTipoDocumento`.
   - noCasoCRM no participa, porque distingue expedientes, no personas.
   - Los expedientes cerrados no se modifican.
   - Todo es idempotente y queda en la bitácora.
2. **"Sin subir"**: es un estado derivado. En la consulta, cada tipo de documento del tipo de
   expediente que no tenga documento se devuelve con estado "Sin subir" y sin ID.
3. **Reemplazo de versiones**:
   - Si ReglaCarga = Reemplazar, el nuevo documento reemplaza al vigente en los expedientes
     abiertos. El anterior queda histórico (IdDocumentoReemplazado).
   - Si el XML trae `idDocumentoReemplaza`, se reemplaza ese documento específico.
   - Si ReglaCarga = Acumular, se agrega sin reemplazar.
4. **Vencimiento**: un job diario pasa a "Vencido" los documentos con FechaVencimiento menor a hoy,
   actualiza el campo en Laserfiche y registra la bitácora.
5. **Transiciones de estado**: se validan contra `TransicionEstadoDocumento` y contra las banderas
   de `EstadoDocumento`.
   - Todo cambio queda en DocumentoEstadoHistorial y en la bitácora.
   - Las reglas se cambian desde el módulo de mantenimiento, sin desplegar código.

# Autenticación y usuarios (criterio 2)
> **Decisiones de la implementación (fase 2):**
> - La API emite sus propios JWT (HMAC-SHA256, llave compartida entre instancias). No usa
>   OpenIddict, porque solo se necesita client_credentials y las cuentas ya están en
>   `seg.CuentaServicio`. Los secretos se guardan con PBKDF2-SHA256.
> - `POST /api/v1/auth/token` sigue RFC 6749 en su formato y códigos HTTP:
>   - 401 `invalid_client`;
>   - 400 `invalid_scope` y `unsupported_grant_type`.
>
>   Es la única excepción a que el HTTP salga de `cat.CodigoRespuesta`. Igual incluye `codigo` y
>   `mensaje` para que el CRM los mapee.
> - La cuenta del CRM se crea con `dotnet run --project src/APILFBI.Api -- crear-cuenta`.

- `POST /api/v1/auth/token`: el CRM envía usuario y contraseña del API (OAuth2 client_credentials
  con client_id y client_secret). Responde un JWT con vigencia de 1 hora (configurable), con
  `access_token`, `token_type` y `expires_in`.
- **Usuario de servicio** = la cuenta del CRM. Es obligatorio en todo endpoint.
- **Usuario de operación** = quien hace la acción: el usuario o ejecutivo en el cambio de estado,
  y `usuarioCarga` en el XML. Se registra en la bitácora, en el historial y en los metadatos de
  Laserfiche.
- La API usa su propia cuenta de servicio para Laserfiche.
- Autorización por scopes: `catalogos.leer`, `expedientes.escribir`, `documentos.leer`,
  `documentos.estado`, `bitacora.leer`.

# Formato de respuesta (criterio 15)
**Se usan los códigos de estado HTTP correctos según el resultado.** Nunca se responde 200 con un
error dentro del cuerpo. Además, el cuerpo lleva el mismo sobre con el `codigo` de negocio, para
que el CRM distinga casos que comparten el mismo HTTP:
```json
{ "codigo": 1, "mensaje": "Transacción realizada con éxito", "correlationId": "...", "data": { } }
```

| HTTP | Cuándo |
|---|---|
| 200 OK | Consulta o actualización exitosa. También en un lote con éxito parcial; en ese caso cada ítem trae su propio código |
| 201 Created | `POST /expedientes` creó el expediente (con header `Location`). Si ya existía, responde 200 |
| 206 Partial Content | Descarga con header Range |
| 400 Bad Request | Estructura o formato inválido: campo obligatorio, tipo de dato, llaves insuficientes, valor fuera de catálogo |
| 401 Unauthorized | Credenciales inválidas, o token ausente o vencido |
| 403 Forbidden | Token válido sin el scope requerido |
| 404 Not Found | Expediente, documento, tipo o carga inexistente |
| 409 Conflict | Conflicto de llaves, concurrencia (rowversion), `Idempotency-Key` reutilizada con otro contenido, documento duplicado |
| 422 Unprocessable Entity | Solicitud bien formada que viola una regla de negocio: transición no permitida, falta tipo de rechazo o comentario, expediente cerrado, tipo de documento que no aplica |
| 429 Too Many Requests | Rate limiting, con header `Retry-After` |
| 500 Internal Server Error | Error no controlado. Nunca expone detalles internos |
| 502 Bad Gateway | Laserfiche respondió con error |
| 503 Service Unavailable | Laserfiche o SQL Server no disponibles, o circuit breaker abierto |
| 504 Gateway Timeout | Laserfiche no respondió a tiempo |

- La relación código de negocio ↔ HTTP está en `cat.CodigoRespuesta` (columna HttpStatus). La API
  toma el HTTP de ahí y nunca lo decide de forma aislada en el código.
- Los errores también siguen RFC 7807: content-type `application/problem+json`, con `codigo`,
  `mensaje` y `correlationId` como extensiones, y `errores[]` por campo en los 400.
- Genera `docs/catalogo-codigos.md` con todos los códigos, su HTTP y en qué métodos aplican.

# Endpoints (/api/v1)
| # | Método | Endpoint | Descripción |
|---|---|---|---|
| 2 | POST | `/auth/token` | Token de 1 hora |
| 5 | GET | `/tipos-expediente` | Tipos de expediente: `idTipoExpediente`, `nombreTipoExpediente` |
| 4 | GET | `/tipos-documento` | Tipos de documento: `idTipoDocumento`, `nombre`, `estado`, `tiposArchivoPermitidos` |
| 3 | GET | `/tipos-expediente/{idTipoExpediente}/tipos-documento` (también `?nombreTipoExpediente=`) | Tipos de documento del tipo de expediente: `idTipoDocumento`, `nombreTipoDocumento`, `obligatorio` |
| — | GET | `/tipos-expediente/{idTipoExpediente}/llaves?tipoCliente=N\|J` | Llaves del tipo de expediente: `codigo`, `obligatoria`, `grupoIdentificacion`, `prioridadGrupo`, `tipoDato` |
| 10 | GET | `/catalogos/estados-documento`, `/catalogos/tipos-rechazo`, `/catalogos/etapas-rechazo`, `/catalogos/tipos-identificacion`, `/catalogos/segmentaciones`, `/catalogos/tipos-archivo` | Catálogos para los campos lista del CRM: `codigo`, `nombre` |
| 11-12 | POST | `/expedientes` | Obtiene o crea el expediente con el conjunto de llaves y devuelve el `idExpediente` para el XML |
| — | GET | `/expedientes/{idExpediente}` | Expediente con sus llaves vigentes |
| 7 | POST | `/clientes/natural/documentos/consulta` | Consulta transaccional, cliente natural |
| 8 | POST | `/clientes/juridico/documentos/consulta` | Consulta transaccional, cliente jurídico |
| 7-8 | GET | `/expedientes/{idExpediente}/documentos` | Misma respuesta que 7 y 8, directo por `idExpediente` |
| 9 | GET | `/documentos/{idDocumento}/contenido` | Stream del archivo con el ID único de Laserfiche. Soporta Range y Content-Disposition inline |
| 10 | POST | `/documentos/estado` | Cambio de estado en lote |
| 11-12 | GET | `/cargas/{correlativo}` | Estado de una carga por SFTP: si no ha llegado a registrarse, Importado (con `idDocumento`) o Rechazado (con código y motivo) |
| — | GET | `/bitacora` | Consulta paginada de la bitácora (scope `bitacora.leer`) |

Las consultas con llaves del cliente usan POST porque llevan datos personales. Así no quedan en
URLs ni en los logs del balanceador.

## Obtener o crear expediente (paso previo a la carga)
Un solo endpoint para natural y jurídico. Las llaves viajan igual que en el prototipo XML:
```json
{
  "idTipoExpediente": 1,
  "llaves": [
    { "tipo": "TipoCliente", "valor": "J" },
    { "tipo": "noCasoCRM", "valor": "536252673" },
    { "tipo": "CIF", "valor": "12345678" },
    { "tipo": "RTN", "valor": "123456789" },
    { "tipo": "NombreComercial", "valor": "Empresa Ejemplo" },
    { "tipo": "razonSocial", "valor": "Empresa Ejemplo S.A." },
    { "tipo": "segmentacion", "valor": "Corporativo" }
  ]
}
```
- **Response**: `idExpediente`, `idTipoExpediente`, `estadoExpediente`, `esNuevo` y las `llaves`
  vigentes.
- Usa `trx.usp_ObtenerOCrearExpediente` y es idempotente.
- Las reglas salen de `TipoExpedienteLlave`. Si no viene al menos un grupo completo (mínimo 2
  llaves identificadoras), se rechaza.

## Consultas 7 y 8
Mantienen los campos explícitos que pide el requerimiento. Internamente, la API convierte esos
campos en llaves y busca el expediente por `ExpedienteIdentidad`. Consultar no crea expedientes:
si no existe, la API devuelve todos los tipos de documento como "Sin subir" e `idExpediente` = null.

## 7. Consulta transaccional, cliente natural
- **Request**:
  - `idTipoExpediente` y `noCaso`: obligatorios
  - `cif`, `tipoIdentificacion` (DNI, Pasaporte, CarnetResidencia) y `noIdentificacion`: debe
    venir `cif`, o el par tipo + número
- **Response**: `idExpediente`, `noCaso`, `cif`, `tipoIdentificacion`, `noIdentificacion`,
  `idTipoExpediente` y `documentos[]`. Cada documento trae:
  - `idDocumento` (ID único de Laserfiche, null si está "Sin subir")
  - `idTipoDocumento`, `tipoDocumento`, `estado`, `segmentacion`
  - `fechaHoraRecepcion`, `fechaVencimiento`
  - `nombreEmpleadoAdjunto` (gerente de cuenta), `comentario`

## 8. Consulta transaccional, cliente jurídico
- **Request**: `idTipoExpediente` y `noCaso` obligatorios. Debe venir `cif` o `rtn`.
- **Response**: `idExpediente`, `noCaso`, `cif`, `rtn`, `idTipoExpediente` y `documentos[]` con los
  mismos campos que el punto 7.

## 10. Cambio de estado en lote
- **Request**:
  - `idExpediente`
  - `ejecutivoAsignado` (nombre, cuando aplique)
  - `documentos[]`, cada uno con:
    - `idDocumento`
    - `codigoEstado`
    - `codigoTipoRechazo` y `codigoEtapaRechazo` (cuando apliquen)
    - `comentario` (cuando aplique)
  - Los estados, tipos de rechazo y etapas viajan siempre como código de catálogo.
- **Response**: `codigo` y `mensaje` generales, más el resultado de cada documento.
- El lote es transaccional por defecto (todo o nada). Hay una opción configurable para permitir
  éxito parcial.
- **Flujo**:
  1. Actualiza la base de datos, que es la fuente de verdad del estado.
  2. Actualiza los campos de la plantilla en Laserfiche: Estado, TipoRechazo, ComentarioRevision,
     EtapaRechazo y EjecutivoAsignado.
  3. Registra historial y bitácora.
- **Tarea al ejecutivo**: cuando viene `ejecutivoAsignado`, un workflow de Laserfiche crea la tarea.
  - Mecanismo recomendado: una regla de inicio del workflow que se dispara cuando cambia el campo
    EjecutivoAsignado.
  - Queda detrás de `IAsignacionTareaService` para poder cambiarlo por una invocación directa al
    Workflow API.
  - El workflow nunca vuelve a escribir el estado, para evitar ciclos.

# Contrato de carga por SFTP (criterios 11, 12, 13 y 14)
La API no recibe archivos. Este proyecto define y documenta el contrato en
`docs/contrato-carga-sftp.md`, y el XSD en `docs/xsd/expediente-v1.xsd`.

**Reglas de entrega para el CRM:**
1. Antes de subir, el CRM obtiene el `idExpediente` con `POST /expedientes`.
2. Sube primero el archivo y al final el XML, ambos con extensión `.tmp`, y luego renombra. Así
   Import Agent nunca procesa un par incompleto.
3. El nombre del archivo es `{Correlativo}_archivo.{ext}` y el del XML es
   `{Correlativo}_data.xml`. El correlativo es único y lo genera el CRM.
4. Solo se permiten los tipos de archivo configurados por tipo de documento (criterio 14). La
   validación final la hace `usp_RegistrarDocumento`.
5. El CRM consulta el resultado con `GET /cargas/{correlativo}`.

**XML de carga** (un solo formato para natural y jurídico):
```xml
<?xml version="1.0" encoding="UTF-8"?>
<expediente version="1.0">
  <idExpediente>1024</idExpediente>
  <informacionDocumento>
    <correlativo>000123</correlativo>
    <tipoDocumento>32</tipoDocumento>
    <nombreDocumento>000123_archivo.pdf</nombreDocumento>
    <hashSha256>9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08</hashSha256>
    <fechaEmision>2026-09-01</fechaEmision>
    <fechaVencimiento></fechaVencimiento>
    <comentario>Estados financieros al cierre de agosto</comentario>
    <idDocumentoReemplaza></idDocumentoReemplaza>
    <usuarioCarga>51451</usuarioCarga>
    <nombreUsuarioCarga>Reina Pasita Caceres Palacios</nombreUsuarioCarga>
    <fechaHoraCarga>2026-09-24T10:15:00-06:00</fechaHoraCarga>
  </informacionDocumento>
</expediente>
```

**Reglas del XML:**
- `idExpediente`, `correlativo`, `tipoDocumento`, `nombreDocumento`, `fechaEmision`,
  `usuarioCarga`, `nombreUsuarioCarga` y `fechaHoraCarga` son obligatorios.
- `hashSha256`, `fechaVencimiento`, `comentario` e `idDocumentoReemplaza` son opcionales.
- Las fechas van en ISO 8601.
- Si `fechaVencimiento` viene vacía, se calcula con los días de vigencia del tipo de documento.
- **Plantilla de Laserfiche**: especifica en `docs/plantilla-laserfiche.md` los campos y su mapeo
  desde el XML. Incluye:
  - IdExpediente y los datos del documento;
  - los campos de revisión: Estado, EjecutivoAsignado, TipoRechazo, ComentarioRevision y
    EtapaRechazo.

  Si se quiere mostrar las llaves en Laserfiche, el workflow las toma de la vista
  `trx.vw_LlavesExpediente`, que muestra una fila por expediente con las llaves en columnas.

# Requisitos no funcionales
- Errores con el sobre estándar y un middleware global que nunca expone detalles internos
- Nada de secretos en appsettings: User Secrets en desarrollo, variables de entorno o Key Vault en
  producción
- Configuración con el patrón Options: `LaserficheOptions`, `AuthOptions`, `CacheOptions`,
  `BitacoraOptions` (tamaño de lote, retención), `AsociacionOptions`, `CargaOptions`
- Polly en Laserfiche: reintentos, timeout y circuit breaker
- Los datos personales (CIF, DNI, RTN) se enmascaran en logs y en la bitácora

# Entregables
1. Solución completa que compila, con:
   - los scripts de `database/` (tablas, catálogos, stored procedures y triggers);
   - los datos semilla (criterio 6): tipos de expediente, tipos de documento, tipos de archivo,
     llaves, sus tablas de relación, estados, transiciones, tipos de rechazo, tipos de operación,
     códigos de respuesta y cuenta de servicio de desarrollo.
2. Script SQL completo generado
3. Documentación:
   - `docs/modelo-datos.md`, con el diagrama y el diccionario de datos de cada tabla
   - `docs/catalogo-codigos.md`
   - `docs/contrato-carga-sftp.md`
   - `docs/xsd/expediente-v1.xsd`
   - `docs/plantilla-laserfiche.md`
   - `docs/workflow-laserfiche.md`
   - contrato OpenAPI exportado para CRM
4. README con arquitectura, despliegue detrás de un balanceador, estrategia de caché y
   configuración de OAuth2 y Laserfiche
5. Colección `.http` con cada método
6. Pruebas:
   - unitarias: "Sin subir", transiciones de estado, validación de llaves, invalidación de caché,
     enmascaramiento en bitácora;
   - de los stored procedures contra SQL Server en contenedor:
     - obtener o crear expediente: concurrencia, prioridad de grupo, potencial que pasa a
       vinculado, conflictos, rechazo con una sola llave;
     - tipo de expediente con un grupo de 1 llave que no se puede activar;
     - registro idempotente, asociación automática, reemplazo de versiones, rechazos;
     - la bitácora no se puede modificar ni borrar;
   - de integración con Laserfiche simulado.

# Forma de trabajo
- Antes de escribir código, presenta el plan: estructura de proyectos, diagrama del modelo de datos,
  flujo obtener expediente → SFTP → Import Agent → workflow → stored procedure, matriz de estados,
  estrategia de caché y escalamiento. Espera mi confirmación.
- Si algo no está definido, pregúntame en lugar de suponerlo.
- Implementa por etapas y compila y corre las pruebas al terminar cada una.
