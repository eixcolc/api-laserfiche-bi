<#
.SYNOPSIS
    Verifica, desde el servidor de la API, que la Repository API de Laserfiche responde como APILFBI
    espera. Es SOLO DE LECTURA, salvo con -ProbarEscritura, que reescribe los campos del documento
    con sus mismos valores (no cambia datos).

.DESCRIPTION
    Comprueba en orden:
      1. Resolución DNS y puerto 443.
      2. Certificado TLS confiable y lista de repositorios (GET v1/Repositories).
      3. Autenticación de la cuenta de servicio (POST v1/Repositories/{repo}/Token).
      4. Metadatos de un documento de prueba (GET .../Entries/{id}).
      5. Campos de la plantilla y su formato (GET .../Entries/{id}/fields), y si existen los campos
         de revisión que la API actualiza.
      6. Descarga del documento electrónico, completa y con Range (GET .../Laserfiche.Repository.Document/edoc).

    Las rutas son las mismas que usa la API por defecto (Laserfiche:Rutas en appsettings.json).

.EXAMPLE
    .\ProbarLaserfiche.ps1 -RepositorioId REPO-QA -EntryId 1234
#>
[CmdletBinding()]
param(
    [string] $UrlBase = 'https://laserfiche-regqa.corporacionbi.com/LFRepositoryAPI/',
    [string] $RepositorioId,
    [int]    $EntryId,
    [System.Management.Automation.PSCredential] $Credencial,
    [string[]] $CamposRevision = @('Estado', 'EjecutivoAsignado', 'TipoRechazo', 'ComentarioRevision', 'EtapaRechazo'),
    # Paso 7 opcional: reescribe los campos del documento con sus valores actuales (mismo PUT que la API)
    [switch] $ProbarEscritura
)

$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
if (-not $UrlBase.EndsWith('/')) { $UrlBase += '/' }
$uri = [Uri]$UrlBase

function Ok($texto)    { Write-Host "  [OK]    $texto" -ForegroundColor Green }
function Falla($texto) { Write-Host "  [FALLA] $texto" -ForegroundColor Red }
function Aviso($texto) { Write-Host "  [AVISO] $texto" -ForegroundColor Yellow }
function Detalle($err) {
    $resp = $err.Exception.Response
    if ($null -ne $resp) {
        $cuerpo = ''
        try { $cuerpo = (New-Object IO.StreamReader($resp.GetResponseStream())).ReadToEnd() } catch { }
        if ($cuerpo.Length -gt 300) { $cuerpo = $cuerpo.Substring(0, 300) + '…' }
        return "HTTP $([int]$resp.StatusCode) $cuerpo"
    }
    return $err.Exception.Message
}

Write-Host ""
Write-Host "1. Red: $($uri.Host):443" -ForegroundColor Cyan
$red = Test-NetConnection -ComputerName $uri.Host -Port 443 -WarningAction SilentlyContinue
if ($red.TcpTestSucceeded) { Ok "Se resuelve a $($red.RemoteAddress) y el puerto 443 responde" }
else { Falla "No hay conexión al puerto 443 (DNS, firewall o proxy)"; return }

Write-Host ""
Write-Host "2. TLS y repositorios: GET v1/Repositories" -ForegroundColor Cyan
try {
    $crudo = Invoke-WebRequest -Uri ($UrlBase + 'v1/Repositories') -Method Get -UseBasicParsing
    Ok "Certificado confiable y la Repository API responde (HTTP $($crudo.StatusCode), $($crudo.Headers['Content-Type']))"
    $lista = @()
    try { $lista = @(($crudo.Content | ConvertFrom-Json) | ForEach-Object { if ($_.value) { $_.value } else { $_ } }) } catch { }
    foreach ($r in $lista) { Write-Host ("          repositorio: {0}  ({1})" -f $r.repoId, $r.repoName) }
    if ($lista.Count -eq 0) {
        Aviso "La respuesta no trae repositorios. Contenido recibido:"
        $texto = [string]$crudo.Content
        if ($texto.Length -gt 500) { $texto = $texto.Substring(0, 500) + '…' }
        Write-Host "          $texto"
    }
    elseif (-not $RepositorioId) { Aviso "Indique -RepositorioId con uno de los anteriores (es Laserfiche:RepositorioId)" }
}
catch {
    $msg = Detalle $_
    if ($msg -match 'SSL|TLS|certificad|trust') { Falla "El servidor no confía en el certificado de Laserfiche: $msg"; Aviso "Instalar en este servidor el certificado de la CA que emitió el de Laserfiche (Equipo local > Entidades de certificación raíz de confianza)" }
    else { Falla $msg }
    return
}

if (-not $RepositorioId) { return }
if (-not $Credencial) { $Credencial = Get-Credential -Message 'Cuenta de servicio de Laserfiche (Laserfiche:Usuario / Contrasena)' }

Write-Host ""
Write-Host "3. Autenticación: POST v1/Repositories/$RepositorioId/Token" -ForegroundColor Cyan
try {
    $cuerpo = @{ grant_type = 'password'; username = $Credencial.UserName; password = $Credencial.GetNetworkCredential().Password }
    $token = Invoke-RestMethod -Uri ($UrlBase + "v1/Repositories/$([Uri]::EscapeDataString($RepositorioId))/Token") -Method Post `
        -ContentType 'application/x-www-form-urlencoded' -Body $cuerpo
    Ok "Token obtenido (vence en $($token.expires_in) s)"
    $h = @{ Authorization = "Bearer $($token.access_token)" }
}
catch { Falla "No se pudo autenticar: $(Detalle $_)"; Aviso "Revisar usuario y contraseña, y si la cuenta es de repositorio o de Windows (dominio\usuario)"; return }

if (-not $EntryId) { Aviso "Indique -EntryId de un documento de prueba para verificar campos y descarga"; return }
$base = $UrlBase + "v1/Repositories/$([Uri]::EscapeDataString($RepositorioId))/Entries/$EntryId"

Write-Host ""
Write-Host "4. Documento de prueba: GET .../Entries/$EntryId" -ForegroundColor Cyan
try {
    $e = Invoke-RestMethod -Uri $base -Headers $h
    Ok ("{0} | tipo: {1} | plantilla: {2} | extensión: {3}" -f $e.name, $e.entryType, $e.templateName, $e.extension)
    if ($e.entryType -ne 'Document') { Aviso "La entrada no es un documento; use el EntryId de un documento" }
}
catch { Falla (Detalle $_); return }

Write-Host ""
Write-Host "5. Campos: GET .../Entries/$EntryId/fields" -ForegroundColor Cyan
try {
    $campos = Invoke-RestMethod -Uri ($base + '/fields') -Headers $h
    if ($null -eq $campos.value) { Falla "La respuesta no trae la propiedad 'value' que la API espera"; $campos | ConvertTo-Json -Depth 5 | Write-Host }
    else {
        Ok "Formato esperado: value[].fieldName / values[].value / values[].position"
        foreach ($c in $campos.value) { Write-Host ("          {0} = {1}" -f $c.fieldName, (($c.values | ForEach-Object { $_.value }) -join ', ')) }
        $nombres = @($campos.value | ForEach-Object { $_.fieldName })
        $faltan = @($CamposRevision | Where-Object { $nombres -notcontains $_ })
        if ($faltan.Count -eq 0) { Ok "La plantilla tiene los campos de revisión: $($CamposRevision -join ', ')" }
        else { Aviso "Faltan en la plantilla (o tienen otro nombre): $($faltan -join ', '). Crearlos o ajustar Laserfiche:Campos" }
    }
}
catch { Falla (Detalle $_) }

Write-Host ""
Write-Host "6. Descarga: GET .../Laserfiche.Repository.Document/edoc" -ForegroundColor Cyan
$destino = Join-Path $env:TEMP "laserfiche-prueba-$EntryId.bin"
try {
    $r = Invoke-WebRequest -Uri ($base + '/Laserfiche.Repository.Document/edoc') -Headers $h -OutFile $destino -PassThru -UseBasicParsing
    Ok ("Descarga completa: HTTP {0}, {1} bytes, Content-Type {2}" -f $r.StatusCode, (Get-Item $destino).Length, $r.Headers['Content-Type'])
}
catch { Falla (Detalle $_) }
try {
    $req = [Net.HttpWebRequest]::Create($base + '/Laserfiche.Repository.Document/edoc')
    $req.Headers.Add('Authorization', $h.Authorization); $req.AddRange(0, 99)
    $resp = $req.GetResponse()
    if ([int]$resp.StatusCode -eq 206) { Ok "Range soportado: HTTP 206, $($resp.ContentLength) bytes" }
    else { Aviso "Range no soportado (HTTP $([int]$resp.StatusCode)): la API entregará el archivo completo, funciona igual" }
    $resp.Close()
}
catch { Aviso "No se pudo probar Range: $(Detalle $_)" }

if ($ProbarEscritura) {
    # Reescribe los campos con sus valores actuales (no cambia datos). Los campos de revisión se arman
    # como los arma la API; los demás se copian tal como los devuelve Laserfiche.
    function Invoke-PruebaPut([string] $titulo, [scriptblock] $valorRevision) {
        $actuales = Invoke-RestMethod -Uri ($base + '/fields') -Headers $h
        $put = [ordered]@{}
        foreach ($c in $actuales.value) {
            $originales = @($c.values | Where-Object { $null -ne $_ })
            if ($CamposRevision -contains $c.fieldName) {
                $valor = ($originales | Where-Object { $null -ne $_.value } | Select-Object -First 1).value
                $pos = if ($originales.Count -gt 0 -and $null -ne $originales[0].position) { $originales[0].position } else { 0 }
                $put[$c.fieldName] = @{ values = @(& $valorRevision $valor $pos) }
            }
            else {
                $put[$c.fieldName] = @{ values = @($originales | ForEach-Object { [ordered]@{ value = $_.value; position = $_.position } }) }
            }
        }
        $json = $put | ConvertTo-Json -Depth 6 -Compress
        Write-Host "  $titulo"
        Write-Host "          $json"
        try {
            Invoke-RestMethod -Uri ($base + '/fields') -Headers $h -Method Put -ContentType 'application/json; charset=utf-8' `
                -Body ([Text.Encoding]::UTF8.GetBytes($json)) | Out-Null
            Ok "Aceptado"; return $true
        }
        catch { Falla (Detalle $_); return $false }
    }

    Write-Host ""
    Write-Host "7. Escritura: PUT .../Entries/$EntryId/fields (reescribe los mismos valores, no cambia datos)" -ForegroundColor Cyan

    # Mismo formato que envía la API: posición que devuelve Laserfiche y, para un campo vacío, value null
    # (Laserfiche 11 responde 400 a "values": []).
    $ok = Invoke-PruebaPut 'Formato de la API (posición de Laserfiche, vacío = value null)' {
        param($v, $p) [ordered]@{ value = $(if ([string]::IsNullOrEmpty($v)) { $null } else { $v }); position = $p } }
    if ($ok) { Ok "La API puede escribir los campos de revisión" }
    else { Aviso "Laserfiche rechazó el formato de la API; enviar esta salida para revisarla" }
}

Write-Host ""
Write-Host "Listo. Si todo está en OK, configure la API con Laserfiche:Simulado = false (ver docs/instalacion-qa.md, sección 10)." -ForegroundColor Cyan
