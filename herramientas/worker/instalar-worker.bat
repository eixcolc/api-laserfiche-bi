@echo off
rem ==========================================================================
rem  Instala APILFBI.Worker como servicio de Windows.
rem  Ejecutar como administrador desde la carpeta donde quedo el Worker.
rem  Los secretos (cadena de conexion, clave de Laserfiche) se piden al
rem  instalar y se guardan en el entorno del servicio, no en este archivo.
rem ==========================================================================
setlocal
set "SERVICIO=APILFBI.Worker"
set "ENTORNO=QA"

net session >nul 2>&1
if errorlevel 1 (
    echo Este archivo debe ejecutarse como administrador.
    pause
    exit /b 1
)

powershell -NoProfile -ExecutionPolicy Bypass -Command "& ([scriptblock]::Create(((Get-Content -LiteralPath '%~f0' -Raw -Encoding UTF8) -split '(?m)^#PS\s*$', 2)[1])) '%~dp0'"
set "RESULTADO=%errorlevel%"
echo.
pause
exit /b %RESULTADO%

#PS
$ErrorActionPreference = 'Stop'
$carpeta  = $args[0]
$servicio = $env:SERVICIO
$entorno  = $env:ENTORNO
$exe      = Join-Path $carpeta 'APILFBI.Worker.exe'

function Leer-Secreto([string] $texto) {
    $seguro = Read-Host $texto -AsSecureString
    $bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($seguro)
    try { [Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr) }
    finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr) }
}

if (-not (Test-Path $exe)) { Write-Host "No se encontro $exe" -ForegroundColor Red; exit 1 }
if (Get-Service $servicio -ErrorAction SilentlyContinue) {
    Write-Host "El servicio $servicio ya existe. Ejecute primero desinstalar-worker.bat." -ForegroundColor Yellow
    exit 1
}
if (-not (Test-Path (Join-Path $carpeta "appsettings.$entorno.json"))) {
    Write-Host "Aviso: no existe appsettings.$entorno.json en $carpeta (UrlBase, RepositorioId, Simulado, logs)." -ForegroundColor Yellow
}

Write-Host "Instalando $servicio (entorno $entorno) desde $carpeta"
Write-Host ""

# Cuenta del servicio
$cuenta = Read-Host 'Cuenta del servicio (DOMINIO\usuario). Enter = LocalSystem'
$parametros = @{
    Name           = $servicio
    BinaryPathName = '"' + $exe + '"'
    DisplayName    = 'APILFBI Worker'
    Description    = 'Jobs de APILFBI: vencimiento, sincronizacion con Laserfiche, limpieza y alertas.'
    StartupType    = 'Automatic'
}
if ($cuenta) { $parametros.Credential = Get-Credential -UserName $cuenta -Message "Clave de $cuenta" }

# Entorno del servicio (lo vacio no se agrega y se usa lo de appsettings)
$entornoServicio = @("DOTNET_ENVIRONMENT=$entorno")
$cadena = Leer-Secreto 'Cadena de conexion a BILF (Enter = la de appsettings)'
if ($cadena) { $entornoServicio += "ConnectionStrings__Bilf=$cadena" }
$usuarioLf = Read-Host 'Usuario de Laserfiche (Enter = no configurar, modo simulado)'
if ($usuarioLf) {
    $entornoServicio += "Laserfiche__Usuario=$usuarioLf"
    $entornoServicio += "Laserfiche__Contrasena=$(Leer-Secreto "Clave de $usuarioLf en Laserfiche")"
}

New-Service @parametros | Out-Null
New-ItemProperty -Path "HKLM:\SYSTEM\CurrentControlSet\Services\$servicio" -Name Environment `
    -PropertyType MultiString -Value $entornoServicio -Force | Out-Null
# Reinicio automatico si el proceso falla: 1 min, 1 min, 5 min; el contador se reinicia cada dia
sc.exe failure $servicio reset= 86400 actions= restart/60000/restart/60000/restart/300000 | Out-Null

Write-Host ""
Write-Host "Servicio creado. Iniciando..."
try {
    Start-Service $servicio
    Start-Sleep -Seconds 5
    $estado = (Get-Service $servicio).Status
    if ($estado -eq 'Running') { Write-Host "OK: $servicio en ejecucion." -ForegroundColor Green }
    else { Write-Host "El servicio quedo en estado $estado. Revise el log del Worker y el Visor de eventos (Aplicacion)." -ForegroundColor Yellow }
}
catch {
    Write-Host "No se pudo iniciar: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host 'Si el error es 1069 (inicio de sesion), la cuenta necesita el derecho "Iniciar sesion como servicio" (secpol.msc).' -ForegroundColor Yellow
    Write-Host 'Revise tambien el log del Worker y el Visor de eventos (Aplicacion).' -ForegroundColor Yellow
    exit 1
}
