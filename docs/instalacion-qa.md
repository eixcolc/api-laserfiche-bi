# Instalación en el servidor de QA

Guía para instalar APILFBI en un servidor Windows con IIS (API) y un servicio de Windows (Worker),
contra un SQL Server de QA. Los comandos son para PowerShell ejecutado **como administrador**.

## Resumen

| # | Paso | Quién |
|---|---|---|
| 1 | Prerrequisitos: .NET 10, cuentas de servicio, carpeta de Import Agent, certificado | Infraestructura |
| 2 | Crear la base BILF y dar permisos | DBA |
| 3 | Publicar la API y el Worker | Desarrollo |
| 4 | Configurar QA (`appsettings.QA.json` y secretos) | Desarrollo / Infraestructura |
| 5 | Crear el sitio de la API en IIS | Infraestructura |
| 6 | Instalar el Worker como servicio | Infraestructura |
| 7 | Crear la cuenta de servicio del CRM | Desarrollo |
| 8 | Verificar | Desarrollo |
| 9 | Balanceador (si QA tiene más de una instancia) | Infraestructura |

---

## 1. Prerrequisitos

**En el servidor de aplicaciones**

- Windows Server 2019 o superior con IIS.
- **.NET 10 Hosting Bundle** (instala el runtime de ASP.NET Core y el módulo de IIS). Descargarlo de
  https://dotnet.microsoft.com/download/dotnet/10.0 → "Hosting Bundle". Después de instalarlo:
  ```powershell
  iisreset
  dotnet --list-runtimes     # debe mostrar Microsoft.AspNetCore.App 10.x y Microsoft.NETCore.App 10.x
  ```
- Certificado TLS para el nombre de la API en QA (ej. `apilfbi-qa.<dominio>`).
- Carpeta de logs, por ejemplo `D:\Logs\APILFBI`.

**Cuentas de servicio del dominio** (se recomiendan gMSA para no manejar contraseñas)

| Cuenta | Para | Permisos |
|---|---|---|
| `DOMINIO\svc_apilfbi` | Identidad del pool de IIS y del Worker | SQL: rol `rol_bilf_api` · Escritura en la carpeta de Import Agent · Escritura en `D:\Logs\APILFBI` |
| `DOMINIO\svc_lf_workflow` | Conexión del workflow de Laserfiche a BILF | SQL: rol `rol_bilf_workflow` |

**Carpeta de Import Agent** (la que recibe las cargas por SFTP y por la API)

- Compartida por UNC, por ejemplo `\\servidor-laserfiche\ImportAgent\CRM`.
- `svc_apilfbi`: permiso de **modificar** (crea `.tmp` y los renombra).
- La cuenta de Import Agent: leer y borrar.
- Import Agent debe **ignorar los archivos `.tmp`** y procesar el par cuando exista el `_data.xml`
  (ver [workflow-laserfiche.md](workflow-laserfiche.md)).

---

## 2. Base de datos

Los scripts crean la base `BILF` (si en QA lleva otro nombre, reemplazar `[BILF]` en los cuatro).
Para una base **nueva** no hace falta ejecutar las migraciones: los scripts `0*` ya incluyen todo.

```powershell
cd <carpeta del repositorio>
$sql = 'SERVIDOR-SQL-QA'            # instancia de QA
foreach ($f in '00_crear_base','01_tablas','02_catalogos','03_procedimientos_triggers') {
    sqlcmd -S $sql -E -b -f 65001 -i "database\$f.sql"
}
```

Permisos (los ejecuta el DBA):

```sql
USE [master];
CREATE LOGIN [DOMINIO\svc_apilfbi]     FROM WINDOWS;
CREATE LOGIN [DOMINIO\svc_lf_workflow] FROM WINDOWS;

USE [BILF];
CREATE USER [DOMINIO\svc_apilfbi]     FOR LOGIN [DOMINIO\svc_apilfbi];
CREATE USER [DOMINIO\svc_lf_workflow] FOR LOGIN [DOMINIO\svc_lf_workflow];
ALTER ROLE rol_bilf_api      ADD MEMBER [DOMINIO\svc_apilfbi];
ALTER ROLE rol_bilf_workflow ADD MEMBER [DOMINIO\svc_lf_workflow];
-- Si el módulo "Administración Base de Datos Auxiliar LF" usa su propia cuenta:
-- ALTER ROLE rol_bilf_mantenimiento ADD MEMBER [DOMINIO\cuenta_mantenimiento];
```

> **Catálogos:** los valores marcados `[EJEMPLO]` en `02_catalogos.sql` (tipos de expediente, tipos
> de documento, formatos, tipos y etapas de rechazo, segmentaciones) deben reemplazarse por los
> reales antes de que el CRM empiece a probar, desde el módulo de mantenimiento o editando el script.

---

## 3. Publicar

En la máquina de desarrollo (o en el pipeline):

```powershell
cd <carpeta del repositorio>
dotnet publish src/APILFBI.Api/APILFBI.Api.csproj       -c Release -o publish\api
dotnet publish src/APILFBI.Worker/APILFBI.Worker.csproj -c Release -o publish\worker
```

Copiar al servidor:

```
publish\api     →  D:\Apps\APILFBI\Api
publish\worker  →  D:\Apps\APILFBI\Worker
```

La publicación incluye el `web.config` con el límite de IIS para cargas de hasta 50 MB y **no**
incluye `appsettings.Development.json`.

---

## 4. Configuración de QA

La configuración se toma, en este orden, de `appsettings.json` → `appsettings.QA.json` → variables
de entorno. **Los secretos van solo en variables de entorno** (o en un vault), nunca en archivos.

### 4.1 `appsettings.QA.json` de la API

Crear `D:\Apps\APILFBI\Api\appsettings.QA.json` (no se guarda en el repositorio):

```json
{
  "ConnectionStrings": {
    "Bilf": "Server=SERVIDOR-SQL-QA;Database=BILF;Trusted_Connection=True;TrustServerCertificate=True;Application Name=APILFBI"
  },
  "OpenApi": { "Habilitado": true },
  "CargaApi": { "CarpetaImportAgent": "\\\\servidor-laserfiche\\ImportAgent\\CRM" },
  "Laserfiche": {
    "Simulado": true,
    "RepositorioId": "NOMBRE-REPOSITORIO-QA"
  },
  "Proxy": { "ProxiesConocidos": [] },
  "Serilog": {
    "WriteTo": [
      {
        "Name": "File",
        "Args": {
          "path": "D:\\Logs\\APILFBI\\api-.log",
          "rollingInterval": "Day",
          "retainedFileCountLimit": 30,
          "formatter": "Serilog.Formatting.Compact.CompactJsonFormatter, Serilog.Formatting.Compact"
        }
      }
    ]
  }
}
```

- `Laserfiche:Simulado` queda en `true` hasta tener la cuenta de Laserfiche (sección 10). Así se
  pueden probar la base, los catálogos, los expedientes y la carga desde el primer día.
- `OpenApi:Habilitado: true` publica Swagger en QA. En producción se deja en `false`.

### 4.2 `appsettings.QA.json` del Worker

Crear `D:\Apps\APILFBI\Worker\appsettings.QA.json`:

```json
{
  "ConnectionStrings": {
    "Bilf": "Server=SERVIDOR-SQL-QA;Database=BILF;Trusted_Connection=True;TrustServerCertificate=True;Application Name=APILFBI.Worker"
  },
  "Laserfiche": {
    "Simulado": true,
    "RepositorioId": "NOMBRE-REPOSITORIO-QA"
  },
  "Serilog": {
    "WriteTo": [
      {
        "Name": "File",
        "Args": {
          "path": "D:\\Logs\\APILFBI\\worker-.log",
          "rollingInterval": "Day",
          "retainedFileCountLimit": 30,
          "formatter": "Serilog.Formatting.Compact.CompactJsonFormatter, Serilog.Formatting.Compact"
        }
      }
    ]
  }
}
```

### 4.3 Llave de firma de los tokens

Generarla **una sola vez** para QA. Si hay varias instancias, todas usan la misma:

```powershell
$b = New-Object byte[] 48; (New-Object System.Security.Cryptography.RNGCryptoServiceProvider).GetBytes($b)
[Convert]::ToBase64String($b)       # guardar este valor en el gestor de secretos del equipo
```

---

## 5. Sitio de la API en IIS

```powershell
Import-Module WebAdministration
$pool = 'APILFBI'
$appcmd = "$env:windir\system32\inetsrv\appcmd.exe"

# Pool sin código administrado, siempre activo (la API tiene procesos en segundo plano:
# caché de catálogos, bitácora y cola hacia Laserfiche; no deben detenerse por inactividad).
if (-not (Test-Path "IIS:\AppPools\$pool")) { New-WebAppPool -Name $pool }   # se puede volver a ejecutar
Set-ItemProperty "IIS:\AppPools\$pool" -Name managedRuntimeVersion -Value ''
Set-ItemProperty "IIS:\AppPools\$pool" -Name startMode -Value 'AlwaysRunning'
Set-ItemProperty "IIS:\AppPools\$pool" -Name processModel.idleTimeout -Value ([TimeSpan]::Zero)
Set-ItemProperty "IIS:\AppPools\$pool" -Name recycling.periodicRestart.time -Value ([TimeSpan]::Zero)
Set-ItemProperty "IIS:\AppPools\$pool" -Name processModel -Value @{ userName = 'DOMINIO\svc_apilfbi'; password = '<contraseña>'; identityType = 3 }
# (con gMSA: userName = 'DOMINIO\svc_apilfbi$' y password vacía)

# Entorno y secretos como variables de entorno del pool (no quedan en archivos de la aplicación)
& $appcmd set config -section:system.applicationHost/applicationPools /+"[name='$pool'].environmentVariables.[name='ASPNETCORE_ENVIRONMENT',value='QA']" /commit:apphost
& $appcmd set config -section:system.applicationHost/applicationPools /+"[name='$pool'].environmentVariables.[name='Auth__LlaveFirma',value='<llave de la sección 4.3>']" /commit:apphost

# Sitio HTTPS
New-Website -Name 'APILFBI' -PhysicalPath 'D:\Apps\APILFBI\Api' -ApplicationPool $pool -Port 443 -Ssl -HostHeader 'apilfbi-qa.<dominio>'
Set-ItemProperty 'IIS:\Sites\APILFBI' -Name applicationDefaults.preloadEnabled -Value $true
# Asociar el certificado TLS al binding 443 (IIS Manager → Sitio → Enlaces, o New-WebBinding / netsh)
```

Si luego se conecta Laserfiche real, se agregan del mismo modo `Laserfiche__Usuario` y
`Laserfiche__Contrasena`.

**Publicación en una subruta** (ej. `https://servidor/expediente`): crear la API como **aplicación**
(no como directorio virtual) dentro del sitio existente, con el pool `APILFBI`:

```powershell
New-WebApplication -Site '<sitio existente>' -Name 'expediente' -PhysicalPath 'D:\Apps\APILFBI\Api' -ApplicationPool 'APILFBI'
```

IIS le pasa la subruta a la API automáticamente: Swagger queda en `/expediente/swagger` y las rutas en
`/expediente/api/v1/...`. Las variables de entorno van en el pool que usa esa aplicación.

**Conexión a SQL Server:** el cliente de SQL de .NET cifra la conexión por defecto. Si el servidor SQL
no tiene un certificado de confianza, agregar `TrustServerCertificate=True` a la cadena. Si se usa
autenticación de SQL (usuario y contraseña) en lugar de Windows, la cadena va como variable de entorno
del pool (`ConnectionStrings__Bilf`) y no en `appsettings.QA.json`, y el usuario debe tener solo el
rol `rol_bilf_api`.

---

## 6. Worker como servicio de Windows

```powershell
$nombre = 'APILFBI.Worker'
New-Service -Name $nombre -BinaryPathName 'D:\Apps\APILFBI\Worker\APILFBI.Worker.exe' `
    -DisplayName 'APILFBI Worker' -StartupType Automatic `
    -Credential (Get-Credential 'DOMINIO\svc_apilfbi')

# Entorno del servicio (el Worker lee DOTNET_ENVIRONMENT)
New-ItemProperty -Path "HKLM:\SYSTEM\CurrentControlSet\Services\$nombre" -Name Environment -PropertyType MultiString `
    -Value @('DOTNET_ENVIRONMENT=QA') -Force

sc.exe failure $nombre reset= 86400 actions= restart/60000/restart/60000/restart/300000   # reinicio automático si falla
Start-Service $nombre
```

Con Laserfiche real se agregan `Laserfiche__Usuario=...` y `Laserfiche__Contrasena=...` a esa
lista `Environment` y se reinicia el servicio.

Se instala **una** instancia del Worker. Si se instalan más, los jobs diarios igual corren una sola
vez (usan un bloqueo en SQL Server).

---

## 7. Cuenta de servicio del CRM

Se crea una vez, desde el servidor, con una cuenta que pueda escribir en el esquema `seg` (DBA o
administrador; la cuenta de servicio de la API no tiene ese permiso a propósito):

```powershell
cd D:\Apps\APILFBI\Api
$env:ASPNETCORE_ENVIRONMENT = 'QA'
$env:Auth__LlaveFirma = '<llave de la sección 4.3>'
dotnet APILFBI.Api.dll crear-cuenta --client-id crm-qa --nombre "CRM QA"
```

Imprime el `client_secret` **una sola vez**: entregarlo al equipo del CRM por un canal seguro. Si se
pierde, se vuelve a ejecutar el mismo comando y se genera otro (el anterior deja de funcionar).

---

## 8. Verificación

```powershell
$base = 'https://apilfbi-qa.<dominio>'
Invoke-RestMethod "$base/health/live"      # Healthy
Invoke-RestMethod "$base/health/ready"     # sqlserver, catalogos y laserfiche en Healthy
```

1. Abrir `https://apilfbi-qa.<dominio>/swagger` y autorizar con `crm-qa`.
2. `GET /api/v1/tipos-expediente` → lista los tipos de expediente de QA.
3. `POST /api/v1/expedientes` (con `X-Operation-User`) → devuelve el `idExpediente`.
4. `POST /api/v1/expedientes/{id}/documentos` con un PDF → 202; el par debe aparecer en la carpeta de
   Import Agent.
5. Cuando Import Agent y el workflow lo procesen, `GET /api/v1/cargas/{correlativo}` → `Importado`.
6. Revisar `D:\Logs\APILFBI\api-*.log` y `worker-*.log`: el Worker debe mostrar la próxima ejecución
   de Vencimiento y LimpiezaIdempotencia.

**Problemas comunes**

**Diagnosticar un 500.30 (la API no arranca).** Lo más rápido es ejecutarla en consola en el servidor,
con la misma configuración del pool. El error aparece en pantalla:

```powershell
cd D:\Apps\APILFBI\Api
$env:ASPNETCORE_ENVIRONMENT = 'QA'
$env:Auth__LlaveFirma = '<llave de la sección 4.3>'
dotnet .\APILFBI.Api.dll
```

- **Si en consola arranca bien** ("Now listening on…"), el problema es que al pool de IIS le faltan esas
  variables de entorno (sección 5). También se ve en el Visor de eventos → Registros de Windows →
  Aplicación, origen **IIS AspNetCore Module V2**.
- **Si falla en consola**, el mensaje dice qué falta (ej. `Auth:LlaveFirma debe ser Base64 de al menos 32
  bytes`).

| Síntoma | Causa probable |
|---|---|
| HTTP 500.30 al abrir el sitio | Falta `Auth__LlaveFirma` o `ASPNETCORE_ENVIRONMENT=QA` en el pool (sin este último no se lee `appsettings.QA.json`) |
| `/health/ready` con `sqlserver` en Unhealthy | La cuenta del pool no tiene login en SQL o falta el rol `rol_bilf_api` |
| Log: *"The certificate chain was issued by an authority that is not trusted"* (la API responde 503, código 903) | El SQL usa un certificado que el servidor no reconoce. Agregar `TrustServerCertificate=True` a la cadena (API y Worker) y reciclar el pool |
| La carga devuelve 500 "No se pudo dejar el archivo para Import Agent" | `svc_apilfbi` no tiene permiso de escritura en la carpeta UNC |
| La carga de un archivo grande devuelve 413 o se corta | El balanceador o un proxy intermedio limita el tamaño; debe permitir 50 MB |
| El Worker no arranca | Revisar el Visor de eventos → Aplicación, y que `DOTNET_ENVIRONMENT=QA` esté en la clave `Environment` del servicio |

---

## 9. Balanceador (si QA tiene más de una instancia)

- Sondeo de salud: `GET /health/ready` (200 = recibe tráfico).
- Sin afinidad de sesión (sticky sessions no son necesarias).
- Reenviar `X-Forwarded-For` y `X-Forwarded-Proto`, y poner las IP del balanceador en
  `Proxy:ProxiesConocidos` para que la bitácora registre la IP real del CRM.
- Permitir solicitudes de hasta **50 MB** hacia `/api/v1/expedientes/*/documentos`.
- Todas las instancias con la misma `Auth__LlaveFirma`, la misma base y la misma carpeta de Import Agent.

---

## 10. Conectar Laserfiche real (cuando se tenga la cuenta)

1. En los dos `appsettings.QA.json`: `"Laserfiche": { "Simulado": false, ... }` (la `UrlBase` ya está
   en `appsettings.json`; `RepositorioId` con el repositorio de QA).
2. Agregar `Laserfiche__Usuario` y `Laserfiche__Contrasena` al pool de IIS y al servicio del Worker.
3. Reciclar el pool y reiniciar el Worker.
4. `/health/ready` → `laserfiche` en `Healthy`.
5. Validar la lista de verificación de la sección "Conectar Laserfiche real" del [README](../README.md):
   rutas de la Repository API, `Range` en la descarga y actualización de campos.

---

## Actualizar a una versión nueva

1. Revisar si hay migraciones nuevas en `database/migraciones/` y ejecutarlas en orden; luego
   `02_catalogos.sql` y `03_procedimientos_triggers.sql` (se pueden volver a ejecutar).
2. Copiar `app_offline.htm` a `D:\Apps\APILFBI\Api` (IIS detiene la API), reemplazar los archivos
   publicados **sin borrar `appsettings.QA.json`**, y quitar `app_offline.htm`.
3. `Stop-Service APILFBI.Worker`, reemplazar los archivos (igual, conservando `appsettings.QA.json`),
   `Start-Service APILFBI.Worker`.
4. Verificar con la sección 8.
