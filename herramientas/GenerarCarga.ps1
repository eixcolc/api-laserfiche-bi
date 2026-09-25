<#
.SYNOPSIS
    Emula al CRM: deja un documento en la carpeta SFTP con el contrato de carga v1.0.

.DESCRIPTION
    Crea el par {Correlativo}_archivo.{ext} + {Correlativo}_data.xml en la carpeta que hace de SFTP,
    igual que lo haría el CRM: sube primero el archivo y al final el XML, ambos como .tmp, y luego
    los renombra. Después hay que procesarlos con SimularCarga.ps1 (que emula Import Agent y el
    workflow de Laserfiche).

    Antes se necesita el idExpediente: se obtiene con POST /api/v1/expedientes.

.EXAMPLE
    .\herramientas\GenerarCarga.ps1 -IdExpediente 1 -TipoDocumento 32 -Archivo C:\temp\estados.pdf

.EXAMPLE
    .\herramientas\GenerarCarga.ps1 -IdExpediente 1 -TipoDocumento 32 -Archivo C:\temp\estados.pdf `
        -Comentario "Cierre de agosto" -IdDocumentoReemplaza 100001
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [long]   $IdExpediente,
    [Parameter(Mandatory)] [int]    $TipoDocumento,
    [Parameter(Mandatory)] [string] $Archivo,
    [string] $Correlativo = ('SIM' + (Get-Date -Format 'yyyyMMddHHmmssfff')),
    [string] $FechaEmision = (Get-Date -Format 'yyyy-MM-dd'),
    [string] $FechaVencimiento = '',
    [string] $Comentario = '',
    [string] $IdDocumentoReemplaza = '',
    [string] $UsuarioCarga = '51451',
    [string] $NombreUsuarioCarga = 'Usuario de prueba',
    [switch] $SinHash,
    [string] $CarpetaSftp = (Join-Path $PSScriptRoot '..\simulacion\sftp')
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $Archivo -PathType Leaf)) { throw "No existe el archivo: $Archivo" }
New-Item -ItemType Directory -Force -Path $CarpetaSftp | Out-Null
$CarpetaSftp = (Resolve-Path $CarpetaSftp).Path

$extension = [IO.Path]::GetExtension($Archivo).ToLowerInvariant()
$nombreArchivo = "${Correlativo}_archivo$extension"
$destinoArchivo = Join-Path $CarpetaSftp $nombreArchivo
$destinoXml = Join-Path $CarpetaSftp "${Correlativo}_data.xml"

$hash = ''
if (-not $SinHash) { $hash = (Get-FileHash -LiteralPath $Archivo -Algorithm SHA256).Hash.ToLowerInvariant() }

# 1. Archivo, como .tmp
Copy-Item -LiteralPath $Archivo -Destination "$destinoArchivo.tmp" -Force

# 2. XML, como .tmp (se arma con XmlWriter para escapar bien los textos)
$config = New-Object System.Xml.XmlWriterSettings
$config.Indent = $true
$config.Encoding = New-Object System.Text.UTF8Encoding($false)
$escritor = [System.Xml.XmlWriter]::Create("$destinoXml.tmp", $config)
try {
    $escritor.WriteStartDocument()
    $escritor.WriteStartElement('expediente')
    $escritor.WriteAttributeString('version', '1.0')
    $escritor.WriteElementString('idExpediente', [string]$IdExpediente)
    $escritor.WriteStartElement('informacionDocumento')
    $escritor.WriteElementString('correlativo', $Correlativo)
    $escritor.WriteElementString('tipoDocumento', [string]$TipoDocumento)
    $escritor.WriteElementString('nombreDocumento', $nombreArchivo)
    $escritor.WriteElementString('hashSha256', $hash)
    $escritor.WriteElementString('fechaEmision', $FechaEmision)
    $escritor.WriteElementString('fechaVencimiento', $FechaVencimiento)
    $escritor.WriteElementString('comentario', $Comentario)
    $escritor.WriteElementString('idDocumentoReemplaza', $IdDocumentoReemplaza)
    $escritor.WriteElementString('usuarioCarga', $UsuarioCarga)
    $escritor.WriteElementString('nombreUsuarioCarga', $NombreUsuarioCarga)
    $escritor.WriteElementString('fechaHoraCarga', (Get-Date).ToString('yyyy-MM-ddTHH:mm:sszzz'))
    $escritor.WriteEndElement()
    $escritor.WriteEndElement()
    $escritor.WriteEndDocument()
}
finally {
    $escritor.Close()
}

# 3. Renombrar: primero el archivo, al final el XML
Move-Item -LiteralPath "$destinoArchivo.tmp" -Destination $destinoArchivo -Force
Move-Item -LiteralPath "$destinoXml.tmp" -Destination $destinoXml -Force

Write-Host "Carga dejada en la carpeta SFTP simulada:" -ForegroundColor Green
Write-Host "  $destinoArchivo"
Write-Host "  $destinoXml"
Write-Host ""
Write-Host "Correlativo: $Correlativo"
Write-Host "Siguiente paso: .\herramientas\SimularCarga.ps1   (y luego GET /api/v1/cargas/$Correlativo)"
