# Catálogo de códigos de respuesta

**Criterio 15.** Fuente: tabla `cat.CodigoRespuesta` de la base BILF. El HTTP de cada código sale de
esa tabla; si se agrega o cambia un código, la API lo toma sin desplegar.

## Formato de las respuestas

**Éxito** (`application/json`):
```json
{ "codigo": 1, "mensaje": "Transacción realizada con éxito", "correlationId": "…", "data": { } }
```

**Error** (`application/problem+json`, RFC 7807):
```json
{
  "type": "https://www.rfc-editor.org/rfc/rfc9110#status.400",
  "title": "Falta un campo obligatorio",
  "status": 400,
  "instance": "/api/v1/expedientes",
  "codigo": 101,
  "mensaje": "Falta un campo obligatorio",
  "correlationId": "…",
  "errores": [ { "campo": "X-Operation-User", "mensaje": "…" } ],
  "data": { }
}
```

- `codigo` es el dato que el CRM debe mapear; el HTTP solo agrupa.
- `errores` aparece en los errores de validación, con el campo exacto.
- `data` aparece cuando hay detalle adicional (por ejemplo, el resultado por documento de un
  cambio de estado rechazado).
- `correlationId` identifica la solicitud en logs y bitácora. El CRM puede enviar el suyo en el
  header `X-Correlation-Id`.

## Códigos

| Código | HTTP | Clave | Mensaje |
|---|---|---|---|
| 1 | 200 | Exito | Transacción realizada con éxito |
| 2 | 201 | Creado | Recurso creado con éxito |
| 3 | 200 | ExitoParcial | Transacción realizada parcialmente; revise el detalle por ítem |
| 4 | 206 | ContenidoParcial | Contenido parcial del documento |
| 5 | 202 | CargaRecibida | Carga recibida; se está importando a Laserfiche |
| 100 | 400 | SolicitudInvalida | La solicitud no tiene un formato válido |
| 101 | 400 | CampoObligatorio | Falta un campo obligatorio |
| 102 | 400 | FormatoCampoInvalido | Un campo tiene un formato o tipo de dato inválido |
| 103 | 400 | LlavesInsuficientes | Debe enviar completo al menos un grupo de llaves identificadoras |
| 104 | 400 | LlaveNoPermitida | La llave no está configurada para el tipo de expediente |
| 105 | 400 | ValorFueraCatalogo | El valor no existe en el catálogo correspondiente |
| 106 | 400 | LlaveObligatoriaFaltante | Falta una llave obligatoria |
| 200 | 401 | CredencialesInvalidas | Usuario o contraseña inválidos |
| 201 | 401 | TokenInvalido | Token ausente, inválido o vencido |
| 202 | 403 | SinPermiso | No tiene permiso para esta operación |
| 300 | 404 | ExpedienteNoEncontrado | El expediente no existe |
| 301 | 404 | DocumentoNoEncontrado | El documento no existe |
| 302 | 404 | TipoExpedienteNoEncontrado | El tipo de expediente no existe o está inactivo |
| 303 | 404 | TipoDocumentoNoEncontrado | El tipo de documento no existe o está inactivo |
| 304 | 404 | CargaNoEncontrada | La carga no existe o aún no ha sido registrada |
| 400 | 422 | TransicionNoPermitida | El cambio de estado no está permitido |
| 401 | 422 | RequiereTipoRechazo | El estado requiere un tipo de rechazo |
| 402 | 422 | RequiereComentario | El estado requiere un comentario |
| 403 | 422 | EstadoSoloSistema | El estado solo puede asignarlo el sistema |
| 404 | 422 | EstadoDerivado | El estado se calcula y no puede asignarse |
| 405 | 422 | ExpedienteCerrado | El expediente está cerrado o archivado |
| 406 | 422 | TipoDocumentoNoAplica | El tipo de documento no aplica al tipo de expediente o de cliente |
| 407 | 422 | TipoArchivoNoPermitido | El tipo de archivo no está permitido para el tipo de documento |
| 408 | 422 | DocumentoNoPerteneceExpediente | El documento no pertenece al expediente indicado |
| 409 | 422 | TipoClienteNoAplica | El tipo de expediente no está configurado para el tipo de cliente |
| 410 | 422 | TamanoExcedido | El archivo supera el tamaño máximo permitido |
| 411 | 422 | HashNoCoincide | El hash SHA-256 no coincide con el archivo recibido |
| 412 | 422 | DocumentoReemplazaNoVigente | El documento a reemplazar ya fue reemplazado por una versión más reciente |
| 413 | 422 | ArchivoRechazadoAntivirus | El archivo fue rechazado por el antivirus |
| 500 | 409 | ConflictoLlaves | Las llaves enviadas corresponden a expedientes distintos |
| 501 | 409 | LlaveIdentificadoraDistinta | Una llave identificadora no coincide con la registrada |
| 502 | 409 | IdempotencyKeyReutilizada | La llave de idempotencia ya se usó con otro contenido |
| 503 | 409 | ConflictoConcurrencia | El registro fue modificado por otra operación; intente de nuevo |
| 504 | 409 | DocumentoDuplicado | El documento ya fue registrado |
| 505 | 409 | SolicitudEnProceso | Una solicitud con la misma llave de idempotencia está en proceso |
| 506 | 409 | CorrelativoExistente | El correlativo ya fue usado en otra carga |
| 800 | 429 | LimiteSolicitudes | Se superó el límite de solicitudes; intente más tarde |
| 900 | 500 | ErrorInterno | Ocurrió un error interno; comuníquese con soporte |
| 901 | 502 | ErrorLaserfiche | Laserfiche respondió con un error |
| 902 | 503 | LaserficheNoDisponible | Laserfiche no está disponible |
| 903 | 503 | BaseDatosNoDisponible | La base de datos no está disponible |
| 904 | 504 | TiempoEsperaLaserfiche | Laserfiche no respondió a tiempo |

**Cuáles reintentar:** 503 (código 503) se puede reintentar de inmediato. 429 (800), 502, 503 y
504 (901 a 904) se reintentan con espera; 429 trae el header `Retry-After`. El resto no cambia con
un reintento.

## Códigos por método

Todos los métodos protegidos pueden devolver además **201, 202, 800, 900 y 903**.

| # | Método | Códigos propios |
|---|---|---|
| 2 | `POST /auth/token` | 1 · 100 (`unsupported_grant_type`, HTTP 400) · 200 (`invalid_client`, HTTP 401) · 202 (`invalid_scope`, **HTTP 400**) · 800 |
| 5 | `GET /tipos-expediente` | 1 |
| 3 | `GET /tipos-expediente/{id}/tipos-documento` | 1 · 105 (`tipoCliente`) · 302 |
| 3 | `GET /tipos-expediente/tipos-documento?nombreTipoExpediente=` | 1 · 101 · 105 · 302 |
| 4 | `GET /tipos-documento` | 1 |
| — | `GET /tipos-expediente/{id}/llaves` | 1 · 105 · 302 |
| 10 | `GET /catalogos/{nombre}` | 1 · 100 (catálogo desconocido) |
| 11-12 | `POST /expedientes` | 1 (existía) · 2 (creado) · 100 · 101 · 102 · 103 · 104 · 105 · 106 · 302 · 409 · 500 · 501 · 502 · 503 · 505 |
| — | `GET /expedientes/{id}` | 1 · 300 |
| 7 | `POST /clientes/natural/documentos/consulta` | 1 · 100 · 101 · 102 · 103 · 104 · 105 · 302 · 409 · 500 |
| 8 | `POST /clientes/juridico/documentos/consulta` | 1 · 100 · 101 · 102 · 103 · 104 · 105 · 302 · 409 · 500 |
| 7-8 | `GET /expedientes/{id}/documentos` | 1 · 300 |
| 11-12 | `POST /expedientes/{id}/documentos` (carga por la API) | 5 (recibida) · 101 · 102 · 300 · 301 · 405 · 406 · 407 · 410 · 411 · 412 · 413 · 502 · 505 · 506 |
| 11-13 | `GET /cargas/{correlativo}` | 1 · 304 (`estadoCarga`: Recibido, Importado o Rechazado) |
| 9 | `GET /documentos/{idDocumento}/contenido` | HTTP 200 o 206 con el archivo · 301 · 901 · 902 · 904 |
| 10 | `POST /documentos/estado` | 1 · 3 · 100 · 101 · 102 · 300 · 405 · 502 · 505 · y el código del primer documento con error (ver abajo) |

La excepción del token (`invalid_scope` con HTTP 400) se debe a que ese endpoint sigue el estándar
OAuth 2.0 (RFC 6749).

## Códigos por documento en `POST /documentos/estado`

Cada documento del lote trae su propio `codigo`:

| Código | Causa |
|---|---|
| 1 | Aplicado (o válido, pero no aplicado porque otro documento del lote falló y el lote es todo o nada) |
| 100 | El documento viene repetido en el lote |
| 105 | Estado, tipo de rechazo o etapa inexistente o inactivo |
| 301 | El documento no existe |
| 400 | Transición no permitida desde el estado actual |
| 401 | Rechazado sin tipo de rechazo |
| 402 | El estado exige comentario (Rechazado, Bajo Excepción) |
| 403 | Vencido: solo lo asigna el sistema |
| 404 | Sin subir: se calcula, no se asigna |
| 408 | El documento no pertenece al expediente |

Respuesta general: **1** si se aplicaron todos; **3** si `permitirParcial = true` y se aplicó al
menos uno; si no se aplicó ninguno, el código del primer documento con error y su HTTP.

### Cambios de estado permitidos

Valores iniciales de `cat.TransicionEstadoDocumento` (se mantienen desde el módulo de catálogos):

| Desde | Hacia |
|---|---|
| PendienteRevision | Aprobado, Rechazado, BajoExcepcion, Vencido* |
| Aprobado | Rechazado, Vencido* |
| Rechazado | PendienteRevision |
| BajoExcepcion | PendienteRevision, Aprobado, Rechazado, Vencido* |
| Vencido | BajoExcepcion |

\* Vencido solo lo asigna el job diario.

## Códigos del registro de documentos (workflow)

`trx.usp_RegistrarDocumento` devuelve estos códigos al workflow; el CRM los ve como
`motivoRechazo` en `GET /cargas/{correlativo}`:

| Código | motivoRechazo |
|---|---|
| 1 | — (Importado) |
| 100 | XmlInvalido |
| 300 | ExpedienteNoExiste |
| 301 | DocumentoReemplazaNoExiste |
| 303 | TipoDocumentoNoExiste |
| 405 | ExpedienteCerrado |
| 406 | TipoNoPerteneceTipoExpediente, TipoNoAplicaTipoCliente |
| 407 | FormatoNoPermitido |
| 410 | TamanoExcedido |
| 411 | HashNoCoincide |
| 412 | DocumentoReemplazaNoVigente |
| 504 | Duplicado |
