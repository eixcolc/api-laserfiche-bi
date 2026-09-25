# Workflows de Laserfiche

Hay dos workflows. Ambos los construye el equipo de Laserfiche; este documento define qué deben
hacer y con qué datos.

## Conexión a la base BILF

- Crear en Workflow una conexión a SQL Server, base `BILF`, con una cuenta dedicada.
- El DBA agrega esa cuenta al rol **`rol_bilf_workflow`**:
  ```sql
  ALTER ROLE rol_bilf_workflow ADD MEMBER [DOMINIO\svc_lf_workflow];
  ```
- Ese rol solo puede ejecutar `trx.usp_RegistrarDocumento` y leer `trx.vw_LlavesExpediente`.

---

## Workflow 1: registro de documentos

**Regla de inicio:** documento creado en `\EXPEDIENTE\Entrada\` con la plantilla del CRM (después de que
Import Agent lo importa).

### Pasos

1. **Validar** que tenga los campos obligatorios de carga (ver [plantilla-laserfiche.md](plantilla-laserfiche.md)).
   Si faltan, igual se llama al stored procedure: rechazará con `XmlInvalido` y dejará la carga
   registrada para que el CRM vea el motivo.

2. **Ejecutar el stored procedure** (actividad de consulta SQL / "Custom Query"):

   ```sql
   EXEC trx.usp_RegistrarDocumento
        @LaserficheEntryId          = ?,   -- Entry ID del documento
        @IdExpediente               = ?,   -- campo IdExpediente
        @Correlativo                = ?,   -- campo Correlativo
        @IdTipoDocumento            = ?,   -- campo TipoDocumento
        @NombreDocumento            = ?,   -- campo NombreDocumento
        @FechaEmision               = ?,   -- campo FechaEmision
        @UsuarioCarga               = ?,   -- campo UsuarioCarga
        @NombreUsuarioCarga         = ?,   -- campo NombreUsuarioCarga
        @FechaVencimiento           = ?,   -- campo FechaVencimiento (vacío → NULL)
        @Comentario                 = ?,   -- campo Comentario (vacío → NULL)
        @LaserficheEntryIdReemplaza = ?,   -- campo IdDocumentoReemplaza (vacío → NULL)
        @HashSha256                 = ?,   -- campo HashSha256 (vacío → NULL)
        @HashSha256Calculado        = ?,   -- opcional: hash calculado del documento electrónico
        @TamanoBytes                = ?,   -- opcional: tamaño del documento electrónico
        @FechaHoraCarga             = ?,   -- campo FechaHoraCarga
        @UsuarioServicio            = N'WORKFLOW_LF';
   ```

   Los valores vacíos deben enviarse como `NULL`, no como texto vacío.

3. **Leer el resultado.** El stored procedure devuelve **una fila**:

   | Columna | Tipo | Valor |
   |---|---|---|
   | CodigoRespuesta | int | 1 si se registró; si no, el código del motivo |
   | Mensaje | nvarchar | Descripción |
   | IdDocumento | bigint | Id interno en BILF (NULL si fue rechazado) |
   | LaserficheEntryId | int | El Entry ID recibido |
   | EstadoCarga | varchar | `Importado` o `Rechazado` |
   | MotivoRechazo | varchar | Código del motivo (NULL si se importó) |

4. **Si `EstadoCarga = Importado`:**
   - Campo `Estado` = `PendienteRevision`.
   - Campo `EstadoCargaBILF` = `Importado`.
   - Opcional: completar los campos informativos desde `trx.vw_LlavesExpediente`.
   - Mover a `\EXPEDIENTE\Expedientes\{TipoExpediente}\{IdExpediente}\`.

5. **Si `EstadoCarga = Rechazado`:**
   - Campos `EstadoCargaBILF` = `Rechazado` y `MotivoRechazoBILF` = `MotivoRechazo`.
   - Mover a `\EXPEDIENTE\Rechazados\{AAAA-MM}\`.
   - El CRM se entera con `GET /api/v1/cargas/{correlativo}`.

6. **Si la consulta SQL falla** (base caída, timeout): reintentar. El stored procedure es
   **idempotente**: si el documento o el correlativo ya se registraron, responde éxito sin
   duplicar, así que reintentar siempre es seguro.

### Qué hace el stored procedure por dentro

Valida el expediente (existe y está abierto), el tipo de documento, que aplique al tipo de
expediente y de cliente, el formato por la extensión de `NombreDocumento`, el tamaño y el hash.
Luego crea el documento, aplica la regla de reemplazo de versiones, lo asocia al expediente y
automáticamente a los demás expedientes abiertos de la misma persona, y registra la bitácora. Todo
en una transacción.

---

## Workflow 2: tarea al ejecutivo asignado

**Regla de inicio:** cambio en el campo `EjecutivoAsignado` (y que no quede vacío).

La API escribe ese campo cuando el CRM cambia el estado de un documento y envía
`ejecutivoAsignado` (criterio 10). Si el CRM no lo envía, la API no toca el campo.

### Pasos

1. Leer `EjecutivoAsignado`, `Estado`, `TipoRechazo`, `ComentarioRevision` y `EtapaRechazo`.
2. Resolver el usuario de Laserfiche del ejecutivo. El CRM envía un **nombre**; la
   correspondencia nombre → usuario la define el equipo de Laserfiche (por ejemplo, una lista o
   una consulta al directorio).
3. Crear la tarea asignada a ese usuario, con el estado y el motivo de rechazo en la descripción.

### Reglas

- **Nunca escribir** los campos `Estado`, `TipoRechazo`, `ComentarioRevision` ni `EtapaRechazo`.
  El estado se cambia solo por la API, donde queda validado, en el historial y en la bitácora. Si
  el workflow los escribiera, BILF y Laserfiche quedarían distintos.
- La actualización de campos llega a Laserfiche unos segundos después del cambio en el CRM (la API
  la envía en segundo plano y el Worker la reintenta si Laserfiche no responde).
