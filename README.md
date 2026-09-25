# APILFBI

API REST (.NET 10) que da al CRM acceso a la gestión documental en Laserfiche 11. Usa la base
SQL Server **BILF**. La especificación completa está en [PROMPT.md](PROMPT.md) y la documentación
para el CRM y el equipo de Laserfiche en [docs/](docs/README.md).

## Estructura

| Carpeta | Contenido |
|---|---|
| `database/` | Scripts de la base BILF (fuente de verdad del esquema): `00` crea la base, `01` tablas, `02` catálogos, `03` stored procedures, triggers, vista y roles |
| `src/APILFBI.Domain` | Entidades |
| `src/APILFBI.Application` | Códigos de respuesta, contratos (caché, bitácora, autenticación) |
| `src/APILFBI.Infrastructure` | EF Core, caché de catálogos, bitácora, emisión de tokens |
| `src/APILFBI.Api` | Controllers, middleware, configuración |
| `src/APILFBI.Worker` | Jobs: vencimiento diario, reintentos hacia Laserfiche, limpieza de idempotencia |
| `database/migraciones/` | Cambios para bases ya creadas (las nuevas los traen en los scripts `0*`) |
| `tests/APILFBI.Tests` | Pruebas unitarias y de integración |

## Puesta en marcha (desarrollo)

1. Crear la base (se puede volver a ejecutar sin problema, salvo `01`, que solo corre en una base vacía):
   ```
   sqlcmd -S .\SQLEXPRESS -E -b -f 65001 -i database\00_crear_base.sql
   sqlcmd -S .\SQLEXPRESS -E -b -f 65001 -i database\01_tablas.sql
   sqlcmd -S .\SQLEXPRESS -E -b -f 65001 -i database\02_catalogos.sql
   sqlcmd -S .\SQLEXPRESS -E -b -f 65001 -i database\03_procedimientos_triggers.sql
   ```
2. Configurar la llave de firma de los tokens (Base64, 32 bytes o más) en User Secrets:
   ```
   dotnet user-secrets set "Auth:LlaveFirma" "<base64>" --project src/APILFBI.Api
   ```
3. Crear la cuenta de servicio del CRM. El comando imprime el secreto **una sola vez**; si se pierde,
   se vuelve a ejecutar y se genera otro:
   ```
   dotnet run --project src/APILFBI.Api -- crear-cuenta --client-id crm-dev --nombre "CRM desarrollo"
   ```
4. Ejecutar la API y abrir Swagger en `/swagger`:
   ```
   dotnet run --project src/APILFBI.Api
   ```
   La colección [APILFBI.Api.http](src/APILFBI.Api/APILFBI.Api.http) tiene ejemplos listos.

## Worker

```
dotnet run --project src/APILFBI.Worker
```

| Job | Cuándo | Qué hace |
|---|---|---|
| Vencimiento | Diario, `Worker:HoraVencimiento` (hora local de `Worker:ZonaHoraria`) | Pasa a Vencido los documentos cuya fecha de vencimiento ya pasó |
| Sincronización Laserfiche | Cada `Worker:IntervaloSincronizacionSegundos` | Reintenta las actualizaciones de campos pendientes (outbox `trx.SincronizacionLaserfiche`) |
| Limpieza de idempotencia | Diario, `Worker:HoraLimpieza` | Borra las llaves de `Idempotency-Key` vencidas (24 h) |

- **Varias instancias:** los jobs diarios usan un bloqueo en SQL Server, así que corren una sola vez aunque haya varias instancias. La sincronización puede correr en todas, porque cada fila se reserva al tomarla.
- **Como servicio de Windows:**
  ```
  sc create APILFBI.Worker binPath="<ruta>\APILFBI.Worker.exe"
  ```
- **Credenciales de Laserfiche:** necesita las mismas que la API, en sus propios User Secrets:
  ```
  dotnet user-secrets set "Laserfiche:Usuario" "<cuenta>" --project src/APILFBI.Worker
  ```

**Cómo se actualiza Laserfiche al cambiar un estado:**
1. `usp_CambiarEstadoDocumentos` guarda el estado y, en la misma transacción, deja la actualización pendiente en `trx.SincronizacionLaserfiche`.
2. La API la envía a Laserfiche en segundo plano, sin hacer esperar al CRM.
3. Si Laserfiche falla, el Worker reintenta con espera creciente. Tras `SincronizacionLaserfiche:MaxIntentos` intentos la fila queda `Fallido`, con el error registrado en la tabla y en la bitácora.

Por documento se aplica siempre el cambio más antiguo primero, para que un estado viejo no pise a uno nuevo.

## Pruebas

```
dotnet test
```

Las pruebas de integración crean una base temporal `BILF_TEST_xxx` a partir de los scripts de
`database/` y la borran al terminar. Usan `.\SQLEXPRESS` con autenticación de Windows; otro
servidor se indica con la variable `BILF_TEST_SERVER`. Si no hay SQL Server, esas pruebas se omiten.

## Configuración

| Clave | Para qué |
|---|---|
| `ConnectionStrings:Bilf` | Conexión a BILF |
| `Auth:LlaveFirma` | Llave HMAC de los JWT. **Igual en todas las instancias** del balanceador. Nunca en appsettings |
| `Auth:ExpiracionMinutos` | Vigencia del token (60) |
| `Cache:IntervaloRevisionSegundos` | Cada cuánto cada instancia revisa `cat.VersionCatalogo` (60) |
| `Bitacora:*` | Tamaño de la cola y de los lotes de escritura |
| `RateLimit:*` | Límites por instancia; el límite global va en el balanceador |
| `Proxy:ProxiesConocidos` | IPs del balanceador, para tomar la IP real del cliente de `X-Forwarded-For` |
| `CargaApi:CarpetaImportAgent` | Carpeta que vigila Import Agent, donde la API deja lo que recibe por `POST /expedientes/{id}/documentos`. En producción es una ruta UNC compartida por todas las instancias, con permiso de escritura para la cuenta de la API |
| `CargaApi:TamanoMaximoSolicitudMB` | Tamaño máximo de una carga por la API (50). **El balanceador y IIS deben permitir al menos ese tamaño** |
| `DataProtection:CertificadoThumbprint` | Certificado que cifra las llaves de Data Protection en producción |
| `OpenApi:Habilitado` | Publica `/openapi/v1.json` y `/swagger` |
| `Laserfiche:Simulado` | `true` (por defecto): no se conecta a Laserfiche y entrega documentos de prueba |
| `Laserfiche:UrlBase`, `RepositorioId` | Repository API self-hosted de Laserfiche 11 |
| `Laserfiche:Usuario`, `Contrasena` | Cuenta de servicio de Laserfiche. **Solo en User Secrets o Key Vault** |
| `Laserfiche:Rutas:*` | Plantillas de rutas con `{repositorio}` y `{entryId}`, para ajustarlas a la versión instalada |
| `Laserfiche:Campos:*` | Nombres de los campos de la plantilla que actualiza la API |
| `Laserfiche:TimeoutSegundos`, `Reintentos` | Resiliencia: timeout por intento, reintentos y circuit breaker |
| `Laserfiche:Simulacion:CarpetaArchivos` | Carpeta opcional con archivos `{entryId}.{ext}` para el modo simulado |

## Antivirus en la carga por la API

La API recibe archivos directamente, así que antes de producción conviene escanearlos. Hoy el
escaneo está **apagado**: la implementación por defecto de `IEscanerAntivirus` no escanea. Para
activarlo se registra otra implementación (por ejemplo Microsoft Defender con `MpCmdRun.exe`, o un
servicio ICAP) según la norma del equipo de seguridad. Un archivo rechazado responde el código 413.
Mientras tanto, se puede escanear la carpeta de Import Agent con el antivirus del servidor.

## Conectar Laserfiche real

La integración está escrita contra la Repository API (v1 self-hosted) y probada contra un
servidor simulado, pero **aún no contra un Laserfiche real**. Para conectarla:

1. Configurar la cuenta de servicio y apagar la simulación:
   ```
   dotnet user-secrets set "Laserfiche:Usuario" "<cuenta>" --project src/APILFBI.Api
   dotnet user-secrets set "Laserfiche:Contrasena" "<contraseña>" --project src/APILFBI.Api
   ```
   y en `appsettings`: `Laserfiche:Simulado = false`, `UrlBase` y `RepositorioId`.
2. Verificar `/health/ready`: la verificación `laserfiche` debe decir `Healthy`. Si Laserfiche
   no responde, la instancia queda `Degraded` pero sigue recibiendo tráfico.
3. Validar contra la versión instalada:
   - las rutas de `Laserfiche:Rutas` (token, descarga, campos);
   - que la descarga acepte `Range`;
   - que `PUT .../fields` reemplace todos los campos. La API asume que sí: lee los actuales, los
     combina con los nuevos y envía el conjunto completo.

   Si algo difiere, se ajusta en la configuración o en `Infrastructure/Laserfiche/ClienteLaserficheApi.cs`.

## Balanceador de carga

- Salud: `/health/live` (el proceso responde) y `/health/ready` (SQL Server y catálogos listos).
- No requiere sticky sessions: los tokens son JWT sin estado y la caché es local, de solo lectura,
  y se invalida por versión de catálogo.
