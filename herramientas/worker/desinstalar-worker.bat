@echo off
rem ==========================================================================
rem  Detiene y elimina el servicio APILFBI.Worker. No borra archivos ni logs.
rem  Ejecutar como administrador.
rem ==========================================================================
setlocal
set "SERVICIO=APILFBI.Worker"

net session >nul 2>&1
if errorlevel 1 (
    echo Este archivo debe ejecutarse como administrador.
    pause
    exit /b 1
)

sc.exe query "%SERVICIO%" >nul 2>&1
if errorlevel 1 (
    echo El servicio %SERVICIO% no esta instalado.
    pause
    exit /b 0
)

set "CONFIRMA="
set /p "CONFIRMA=Se detendra y eliminara el servicio %SERVICIO%. Continuar? (S/N): "
if /i not "%CONFIRMA%"=="S" (
    echo Cancelado.
    pause
    exit /b 0
)

echo Deteniendo %SERVICIO%...
powershell -NoProfile -Command "$s = Get-Service '%SERVICIO%'; if ($s.Status -ne 'Stopped') { Stop-Service '%SERVICIO%' -Force; $s.WaitForStatus('Stopped', [TimeSpan]::FromSeconds(60)) }"
if errorlevel 1 (
    echo No se pudo detener el servicio en 60 segundos. Revise el Administrador de tareas.
    pause
    exit /b 1
)

sc.exe delete "%SERVICIO%"
if errorlevel 1 (
    echo No se pudo eliminar el servicio.
    pause
    exit /b 1
)

echo.
echo Servicio %SERVICIO% eliminado. Los archivos y logs se conservan.
echo Si la consola de Servicios esta abierta, cierrela para que desaparezca de la lista.
pause
exit /b 0
