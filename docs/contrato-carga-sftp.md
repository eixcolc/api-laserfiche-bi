# Contrato de carga de documentos por SFTP

**Versión del contrato:** 1.0 · **Criterios:** 11, 12, 13 y 14

La API **no recibe archivos**. El CRM deja cada documento en una carpeta SFTP del servidor de
Laserfiche; Laserfiche Import Agent lo importa y un workflow lo registra en la base BILF. El CRM
consulta el resultado con la API.

## Flujo

```
CRM                          API APILFBI              Servidor Laserfiche
 │  1. POST /expedientes ──────►│                          │
 │  ◄──── idExpediente ─────────│                          │
 │                                                         │
 │  2. SFTP: {Correlativo}_archivo.{ext} + {Correlativo}_data.xml ──►│ Import Agent importa
 │                                                         │ Workflow registra en BILF
 │  3. GET /cargas/{correlativo} ─►│                        │
 │  ◄──── Importado / Rechazado ───│                        │
```

1. **Obtener el expediente.** Con las llaves del cliente se llama a `POST /api/v1/expedientes`. La
   respuesta trae el `idExpediente` que va en el XML. Si el expediente ya existía, devuelve el mismo.
2. **Subir los dos archivos por SFTP** (ver reglas abajo).
3. **Consultar el resultado** con `GET /api/v1/cargas/{correlativo}`. Mientras el workflow no lo
   registre responde 404 (código 304); después responde `Importado` (con `idDocumento`) o
   `Rechazado` (con `motivoRechazo`).

## Reglas de entrega

| Regla | Detalle |
|---|---|
| Nombres | Archivo: `{Correlativo}_archivo.{ext}`. XML: `{Correlativo}_data.xml` |
| Correlativo | Lo genera el CRM. Único por carga. Letras, números, `.`, `_` o `-`, hasta 50 caracteres |
| Orden de subida | Primero el archivo y al final el XML, **ambos con extensión `.tmp`**, y luego se renombran. Así Import Agent nunca toma un par incompleto |
| Formatos | Solo los permitidos para el tipo de documento (criterio 14). Se consultan en `GET /api/v1/tipos-documento` (`tiposArchivoPermitidos`) |
| Tamaño | Hasta el `tamanoMaximoMB` del tipo de documento (mismo endpoint) |
| Codificación del XML | UTF-8 |
| Validación previa | Se recomienda validar el XML contra [xsd/expediente-v1.xsd](xsd/expediente-v1.xsd) antes de subirlo |
| Reintentos | Si una carga fue rechazada, se puede volver a enviar **con el mismo correlativo** una vez corregida. Un correlativo ya importado no se puede reutilizar para otro documento |

## XML de carga

Un solo formato para cliente natural y jurídico: los datos del cliente ya están en el expediente.

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

Hay dos ejemplos que validan contra el XSD: [ejemplo-completo_data.xml](xsd/ejemplo-completo_data.xml)
y [ejemplo-minimo_data.xml](xsd/ejemplo-minimo_data.xml).

| Campo | Obligatorio | Formato | Descripción |
|---|---|---|---|
| `@version` | Sí | `1.0` | Versión del contrato |
| `idExpediente` | Sí | Entero positivo | El que devolvió `POST /expedientes` |
| `correlativo` | Sí | Texto, 50 | Igual al prefijo de los nombres de archivo |
| `tipoDocumento` | Sí | Entero positivo | `idTipoDocumento` del catálogo (`GET /tipos-documento`) |
| `nombreDocumento` | Sí | Texto, 260, con extensión | Nombre del archivo. **La extensión define el formato** que se valida |
| `hashSha256` | No | 64 caracteres hexadecimales | SHA-256 del archivo, para verificar que llegó íntegro |
| `fechaEmision` | Sí | `AAAA-MM-DD` | Fecha de emisión del documento |
| `fechaVencimiento` | No | `AAAA-MM-DD` | Si viene vacía se calcula: `fechaEmision` + días de vigencia del tipo de documento |
| `comentario` | No | Texto, 1000 | Comentario del gerente de cuenta. Se devuelve en las consultas 7 y 8 |
| `idDocumentoReemplaza` | No | Entero positivo | `idDocumento` (ID de Laserfiche) de la versión que se reemplaza |
| `usuarioCarga` | Sí | Texto, 50 | Código del empleado que adjunta (usuario de operación) |
| `nombreUsuarioCarga` | Sí | Texto, 200 | Nombre del empleado. Se devuelve como `nombreEmpleadoAdjunto` |
| `fechaHoraCarga` | Sí | ISO 8601 con zona | Momento de la carga en el CRM |

Los elementos opcionales pueden omitirse o ir vacíos.

## Qué pasa al registrar

El workflow registra el documento con `trx.usp_RegistrarDocumento`, que:

- lo deja en estado **Pendiente de Revisión**;
- lo asocia al expediente del XML y, **automáticamente**, a los demás expedientes abiertos de la
  misma persona cuyo tipo de expediente lleva ese tipo de documento;
- si el tipo de documento es de regla **Reemplazar**, la versión nueva reemplaza a la vigente en
  los expedientes abiertos (la anterior queda como histórico). Con `idDocumentoReemplaza` se
  reemplaza ese documento en particular.

## Motivos de rechazo

`GET /cargas/{correlativo}` devuelve `motivoRechazo` con uno de estos códigos:

| motivoRechazo | Causa | Qué hacer |
|---|---|---|
| `XmlInvalido` | Falta un campo obligatorio o no cumple el formato | Corregir el XML y reenviar |
| `ExpedienteNoExiste` | El `idExpediente` no existe | Obtenerlo con `POST /expedientes` |
| `ExpedienteCerrado` | El expediente está cerrado o archivado | Usar un expediente abierto |
| `TipoDocumentoNoExiste` | El `tipoDocumento` no existe o está inactivo | Revisar `GET /tipos-documento` |
| `TipoNoPerteneceTipoExpediente` | El tipo de documento no aplica al tipo de expediente | Revisar `GET /tipos-expediente/{id}/tipos-documento` |
| `TipoNoAplicaTipoCliente` | El tipo de documento no aplica a ese tipo de cliente | Idem, con `?tipoCliente=` |
| `FormatoNoPermitido` | La extensión no está permitida para el tipo de documento | Convertir a un formato permitido |
| `TamanoExcedido` | El archivo supera el tamaño máximo | Reducir el archivo |
| `HashNoCoincide` | El hash del XML no coincide con el del archivo | Volver a subir el par completo |
| `Duplicado` | El correlativo ya se importó con otro documento | Usar otro correlativo |
| `DocumentoReemplazaNoExiste` | `idDocumentoReemplaza` no existe o es de otro tipo | Corregir o dejar vacío |
