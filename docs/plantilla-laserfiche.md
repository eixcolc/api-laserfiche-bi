# Plantilla de Laserfiche

Campos que debe tener la plantilla de los documentos que carga el CRM, y quién escribe cada uno.
Los nombres son los que la API espera por defecto; si la plantilla usa otros, los de revisión se
cambian en `Laserfiche:Campos` del `appsettings` de la API y del Worker.

## 1. Campos de carga (los llena Import Agent desde el XML)

| Campo | Tipo | Largo | Origen en `{Correlativo}_data.xml` | Obligatorio |
|---|---|---|---|---|
| IdExpediente | Número | — | `idExpediente` | Sí |
| Correlativo | Texto | 50 | `informacionDocumento/correlativo` | Sí |
| TipoDocumento | Número | — | `informacionDocumento/tipoDocumento` | Sí |
| NombreDocumento | Texto | 260 | `informacionDocumento/nombreDocumento` | Sí |
| HashSha256 | Texto | 64 | `informacionDocumento/hashSha256` | No |
| FechaEmision | Fecha | — | `informacionDocumento/fechaEmision` | Sí |
| FechaVencimiento | Fecha | — | `informacionDocumento/fechaVencimiento` | No |
| Comentario | Texto | 1000 | `informacionDocumento/comentario` | No |
| IdDocumentoReemplaza | Número | — | `informacionDocumento/idDocumentoReemplaza` | No |
| UsuarioCarga | Texto | 50 | `informacionDocumento/usuarioCarga` | Sí |
| NombreUsuarioCarga | Texto | 200 | `informacionDocumento/nombreUsuarioCarga` | Sí |
| FechaHoraCarga | Fecha y hora | — | `informacionDocumento/fechaHoraCarga` | Sí |

> **A verificar con la versión instalada de Import Agent:** que pueda tomar metadatos de un XML
> propio con esta estructura (mapeo por ruta de elemento). Si no puede, Import Agent importa el
> archivo y el XML como documentos, y el workflow de registro lee el XML y llena estos campos antes
> de llamar al stored procedure.

## 2. Campos de registro (los llena el workflow de registro)

| Campo | Tipo | Largo | Valor |
|---|---|---|---|
| EstadoCargaBILF | Texto | 20 | `Importado` o `Rechazado` (columna `EstadoCarga` del stored procedure) |
| MotivoRechazoBILF | Texto | 50 | Columna `MotivoRechazo` si fue rechazado |

## 3. Campos de revisión (los escribe la API)

La API los actualiza cuando el CRM cambia el estado (criterio 10) y el Worker cuando vence un
documento. **El workflow no debe escribirlos**, salvo el estado inicial.

| Campo | Tipo | Largo | Valor |
|---|---|---|---|
| Estado | Texto | 50 | Código del estado: `PendienteRevision`, `Aprobado`, `Rechazado`, `BajoExcepcion`, `Vencido`. El workflow de registro pone `PendienteRevision` |
| EjecutivoAsignado | Texto | 200 | Nombre enviado por el CRM. **Su cambio dispara el workflow de tarea** |
| TipoRechazo | Texto | 50 | Código de `cat.TipoRechazo`, o vacío |
| ComentarioRevision | Texto | 1000 | Comentario del cambio de estado |
| EtapaRechazo | Texto | 50 | Código de `cat.EtapaRechazo`, o vacío |

La API envía **códigos** (no nombres), porque son estables y los workflows pueden compararlos. La
lista vigente de cada catálogo está en `GET /api/v1/catalogos/{nombre}`.

## 4. Campos informativos (opcionales, los llena el workflow)

Si se quiere ver en Laserfiche los datos del cliente, el workflow los toma de la vista
`trx.vw_LlavesExpediente` por `IdExpediente`:

| Campo | Columna de la vista |
|---|---|
| NoCasoCRM | `noCasoCRM` |
| CIF | `CIF` |
| RTN | `RTN` |
| TipoIdentificacion / NoIdentificacion | `tipoIdentificacion` / `noIdentificacion` |
| NombreCliente | `razonSocial` (jurídico) o `nombreCompleto` (natural) |
| TipoCliente | `TipoCliente` |
| TipoExpediente | `TipoExpediente` |

## Estructura de carpetas sugerida

```
\EXPEDIENTE\Entrada\                                        ← destino de Import Agent
\EXPEDIENTE\Expedientes\{TipoExpediente}\{IdExpediente}\    ← documentos importados
\EXPEDIENTE\Rechazados\{AAAA-MM}\                           ← cargas rechazadas
```

Un documento puede pertenecer a varios expedientes (asociación automática). En Laserfiche vive
**una sola vez**, en la carpeta del expediente del XML; las demás asociaciones están en BILF.
