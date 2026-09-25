# Probar la carga de documentos en local (sin Laserfiche)

En producción la carga pasa por el CRM, el SFTP, Laserfiche Import Agent y el workflow de
registro. En local dos scripts reemplazan esas piezas, y el resto (base BILF, stored procedures y
API) es el mismo:

| Pieza real | En local |
|---|---|
| CRM sube por SFTP | `herramientas\GenerarCarga.ps1` deja el par en `simulacion\sftp\` |
| Import Agent + workflow | `herramientas\SimularCarga.ps1` asigna un EntryId y llama a `trx.usp_RegistrarDocumento` |
| Repositorio de Laserfiche | `simulacion\laserfiche\{EntryId}.{ext}` (lo lee la API en modo simulado) |

La carpeta `simulacion\` no se sube al repositorio.

## Paso a paso

**1. Levantar la API.** Se abre Swagger:
```
dotnet run --project src/APILFBI.Api
```

**2. Obtener el token y el expediente.**
- En Swagger, **Authorize** con la cuenta de desarrollo.
- Llamar a `POST /api/v1/expedientes` con el header `X-Operation-User`. La colección
  [APILFBI.Api.http](../src/APILFBI.Api/APILFBI.Api.http) trae el ejemplo. Anotar el `idExpediente`.

**3a. Cargar por la API (canal recomendado).** En desarrollo, la carpeta de Import Agent del endpoint
es `simulacion\sftp`, así que la carga queda lista para el paso 4. Desde Swagger
(`POST /api/v1/expedientes/{idExpediente}/documentos`, con el header `X-Operation-User`) o con curl:
```powershell
curl.exe -X POST "http://localhost:5242/api/v1/expedientes/2/documentos" -H "Authorization: Bearer <token>" -H "X-Operation-User: 51451" `
  -F "archivo=@herramientas/muestras/estados-financieros.pdf;type=application/pdf" -F "idTipoDocumento=32" `
  -F "fechaEmision=2026-09-01" -F "nombreUsuarioCarga=Usuario de prueba"
```
Responde 202 con el `correlativo`, y la carga queda **Recibido** hasta el paso 4.

**3b. O hacer de CRM por SFTP: dejar un documento en la carpeta simulada.**
```powershell
.\herramientas\GenerarCarga.ps1 -IdExpediente 2 -TipoDocumento 32 -Archivo C:\ruta\estados.pdf -Comentario "Cierre de agosto"
```
Si no tienes un archivo a mano, usa el PDF de muestra
`herramientas\muestras\estados-financieros.pdf`. Es un PDF válido de una página, así que se puede
abrir al descargarlo. Un archivo que solo tenga la extensión `.pdf` se descarga bien, pero el visor
no lo abre.

Opciones útiles: `-Correlativo`, `-FechaEmision`, `-FechaVencimiento`, `-IdDocumentoReemplaza` y
`-SinHash`. Los tipos de documento y sus formatos permitidos se ven en `GET /api/v1/tipos-documento`.

**4. Hacer de Import Agent y del workflow.**
```powershell
.\herramientas\SimularCarga.ps1            # procesa lo pendiente
.\herramientas\SimularCarga.ps1 -Vigilar   # o queda revisando la carpeta hasta Ctrl+C
```
Por cada carga muestra si quedó **IMPORTADO** (con su `idDocumento`) o **RECHAZADO** (con el motivo).

**5. Probar los endpoints.**

| Qué probar | Endpoint |
|---|---|
| Resultado de la carga | `GET /api/v1/cargas/{correlativo}` |
| Documentos del cliente | `POST /api/v1/clientes/juridico/documentos/consulta` (o `natural`) |
| Documentos por expediente | `GET /api/v1/expedientes/{id}/documentos` |
| Ver el archivo (devuelve el archivo real que se subió) | `GET /api/v1/documentos/{idDocumento}/contenido` |
| Cambiar el estado | `POST /api/v1/documentos/estado` |

## Escenarios para probar

| Escenario | Cómo |
|---|---|
| Carga válida | PDF con `-TipoDocumento 32` |
| Formato no permitido | Un `.docx` con `-TipoDocumento 32` → `FormatoNoPermitido` |
| Tipo que no aplica | `-TipoDocumento 35` (solo natural) en un expediente jurídico → `TipoNoAplicaTipoCliente` |
| Expediente inexistente | `-IdExpediente 999999` → `ExpedienteNoExiste` |
| Nueva versión | Otra carga del tipo 32 en el mismo expediente: la anterior queda no vigente y la nueva con `version` 2 |
| Asociación automática | Crear otro expediente de la misma persona (mismo CIF, otro caso o Tarjeta de Crédito) y consultar: el documento aparece sin volver a cargarlo |
| Reintento del workflow | Volver a dejar el mismo par con el mismo correlativo tras un rechazo corregido |

## Limpiar

Los scripts no borran nada de BILF. Para empezar de cero con los archivos, borrar la carpeta
`simulacion\`. Los expedientes y documentos de prueba quedan en la base.
