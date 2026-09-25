<#
.SYNOPSIS
    Emula Laserfiche Import Agent + el workflow de registro, para probar la API sin Laserfiche.

.DESCRIPTION
    Por cada par {Correlativo}_archivo.* + {Correlativo}_data.xml de la carpeta SFTP simulada:
      1. Valida el XML contra docs/xsd/expediente-v1.xsd (solo informa; igual que en producción,
         quien decide es el stored procedure).
      2. Asigna un EntryId, como haría Laserfiche al importar.
      3. Llama a trx.usp_RegistrarDocumento: el mismo stored procedure que usará el workflow real.
      4. Si se importó, copia el archivo a la carpeta del Laserfiche simulado como {EntryId}.{ext},
         para que GET /api/v1/documentos/{EntryId}/contenido devuelva el archivo real.
      5. Mueve el par a simulacion\procesados o simulacion\rechazados.

    Usa autenticación de Windows contra la base indicada.

.EXAMPLE
    .\herramientas\SimularCarga.ps1                 # procesa lo que haya y termina
    .\herramientas\SimularCarga.ps1 -Vigilar        # sigue revisando la carpeta hasta Ctrl+C
#>
[CmdletBinding()]
param(
    [string] $Servidor = '.\SQLEXPRESS',
    [string] $BaseDatos = 'BILF',
    [string] $CarpetaSimulacion = (Join-Path $PSScriptRoot '..\simulacion'),
    [switch] $Vigilar,
    [int]    $IntervaloSegundos = 3
)

$ErrorActionPreference = 'Stop'

New-Item -ItemType Directory -Force -Path $CarpetaSimulacion | Out-Null
$CarpetaSimulacion = (Resolve-Path $CarpetaSimulacion).Path
$carpetaSftp       = Join-Path $CarpetaSimulacion 'sftp'
$carpetaLaserfiche = Join-Path $CarpetaSimulacion 'laserfiche'
$carpetaProcesados = Join-Path $CarpetaSimulacion 'procesados'
$carpetaRechazados = Join-Path $CarpetaSimulacion 'rechazados'
foreach ($c in $carpetaSftp, $carpetaLaserfiche, $carpetaProcesados, $carpetaRechazados) { New-Item -ItemType Directory -Force -Path $c | Out-Null }

$cadenaConexion = "Server=$Servidor;Database=$BaseDatos;Integrated Security=True;TrustServerCertificate=True;Application Name=SimularCarga"

$rutaXsd = Join-Path $PSScriptRoot '..\docs\xsd\expediente-v1.xsd'
$esquemas = New-Object System.Xml.Schema.XmlSchemaSet
$esquemas.Add($null, (Resolve-Path $rutaXsd).Path) | Out-Null

function Validar-Xsd([string] $contenido) {
    $errores = New-Object System.Collections.Generic.List[string]
    $config = New-Object System.Xml.XmlReaderSettings
    $config.ValidationType = 'Schema'
    $config.Schemas = $esquemas
    $config.add_ValidationEventHandler({ param($s, $e) $errores.Add($e.Message) })
    try {
        $lector = [System.Xml.XmlReader]::Create((New-Object System.IO.StringReader($contenido)), $config)
        while ($lector.Read()) { }
        $lector.Close()
    }
    catch { $errores.Add($_.Exception.Message) }
    return , $errores
}

function Valor($xml, [string] $ruta) {
    if ($null -eq $xml) { return $null }
    $nodo = $xml.SelectSingleNode($ruta)
    if ($null -eq $nodo -or [string]::IsNullOrWhiteSpace($nodo.InnerText)) { return $null }
    return $nodo.InnerText.Trim()
}

function Parametro([System.Data.SqlClient.SqlCommand] $cmd, [string] $nombre, [System.Data.SqlDbType] $tipo, $valor, [int] $largo = 0) {
    $p = $cmd.Parameters.Add($nombre, $tipo)
    if ($largo -ne 0) { $p.Size = $largo }
    if ($null -eq $valor) { $p.Value = [DBNull]::Value } else { $p.Value = $valor }
}

function Siguiente-EntryId([System.Data.SqlClient.SqlConnection] $cn) {
    $cmd = $cn.CreateCommand()
    $cmd.CommandText = @"
SELECT ISNULL(MAX(v), 100000) + 1 FROM (
    SELECT MAX(LaserficheEntryId) AS v FROM trx.Documento
    UNION ALL SELECT MAX(LaserficheEntryId) FROM trx.CargaDocumento) x
"@
    $siguiente = [int]$cmd.ExecuteScalar()
    # Tampoco reutilizar un EntryId que ya tenga archivo en el Laserfiche simulado.
    while (Get-ChildItem -LiteralPath $carpetaLaserfiche -Filter "$siguiente.*" -ErrorAction SilentlyContinue) { $siguiente++ }
    return $siguiente
}

function Procesar-Par([System.IO.FileInfo] $archivoXml) {
    $correlativo = $archivoXml.Name.Substring(0, $archivoXml.Name.Length - '_data.xml'.Length)
    $archivo = Get-ChildItem -LiteralPath $carpetaSftp -Filter "${correlativo}_archivo.*" |
               Where-Object { $_.Extension -ne '.tmp' } | Select-Object -First 1
    if ($null -eq $archivo) {
        Write-Host "[$correlativo] XML sin archivo todavía; se espera." -ForegroundColor DarkYellow
        return
    }

    Write-Host ""
    Write-Host "[$correlativo] Procesando $($archivo.Name)" -ForegroundColor Cyan

    $contenido = [IO.File]::ReadAllText($archivoXml.FullName, [Text.Encoding]::UTF8)
    $errores = Validar-Xsd $contenido
    if ($errores.Count -gt 0) {
        Write-Host "  XML no cumple el XSD: $($errores[0])" -ForegroundColor Yellow
    }

    $xml = $null
    try { $xml = [xml]$contenido } catch { Write-Host "  XML mal formado: $($_.Exception.Message)" -ForegroundColor Yellow }
    $doc = '/expediente/informacionDocumento/'

    $fecha = { param($texto) $d = [datetime]::MinValue
               if ($texto -and [datetime]::TryParseExact($texto, 'yyyy-MM-dd', [Globalization.CultureInfo]::InvariantCulture, 'None', [ref]$d)) { $d } else { $null } }
    $entero = { param($texto) $n = 0; if ($texto -and [long]::TryParse($texto, [ref]$n)) { $n } else { $null } }
    $fechaHora = $null
    $fh = [datetimeoffset]::MinValue
    if ([datetimeoffset]::TryParse((Valor $xml "${doc}fechaHoraCarga"), [ref]$fh)) { $fechaHora = $fh }

    $cn = New-Object System.Data.SqlClient.SqlConnection($cadenaConexion)
    $cn.Open()
    try {
        $entryId = Siguiente-EntryId $cn
        Write-Host "  Import Agent (simulado) asignó EntryId $entryId"

        $cmd = $cn.CreateCommand()
        $cmd.CommandType = [System.Data.CommandType]::StoredProcedure
        $cmd.CommandText = 'trx.usp_RegistrarDocumento'
        Parametro $cmd '@LaserficheEntryId' Int $entryId
        Parametro $cmd '@IdExpediente' BigInt (& $entero (Valor $xml '/expediente/idExpediente'))
        Parametro $cmd '@Correlativo' VarChar $correlativo 50
        Parametro $cmd '@IdTipoDocumento' Int (& $entero (Valor $xml "${doc}tipoDocumento"))
        Parametro $cmd '@NombreDocumento' NVarChar $(if (Valor $xml "${doc}nombreDocumento") { Valor $xml "${doc}nombreDocumento" } else { $archivo.Name }) 260
        Parametro $cmd '@FechaEmision' Date (& $fecha (Valor $xml "${doc}fechaEmision"))
        Parametro $cmd '@UsuarioCarga' VarChar (Valor $xml "${doc}usuarioCarga") 50
        Parametro $cmd '@NombreUsuarioCarga' NVarChar (Valor $xml "${doc}nombreUsuarioCarga") 200
        Parametro $cmd '@FechaVencimiento' Date (& $fecha (Valor $xml "${doc}fechaVencimiento"))
        Parametro $cmd '@Comentario' NVarChar (Valor $xml "${doc}comentario") 1000
        Parametro $cmd '@LaserficheEntryIdReemplaza' Int (& $entero (Valor $xml "${doc}idDocumentoReemplaza"))
        Parametro $cmd '@HashSha256' Char (Valor $xml "${doc}hashSha256") 64
        Parametro $cmd '@HashSha256Calculado' Char ((Get-FileHash -LiteralPath $archivo.FullName -Algorithm SHA256).Hash) 64
        Parametro $cmd '@TamanoBytes' BigInt $archivo.Length
        Parametro $cmd '@FechaHoraCarga' DateTimeOffset $fechaHora
        # Sin la declaración <?xml ... encoding="UTF-8"?>: SQL Server la rechaza si el texto le llega en UTF-16.
        Parametro $cmd '@XmlOriginal' Xml $(if ($xml) { $xml.DocumentElement.OuterXml } else { $null })
        Parametro $cmd '@UsuarioServicio' NVarChar 'SIMULADOR_WORKFLOW' 100

        $lector = $cmd.ExecuteReader()
        $null = $lector.Read()
        $resultado = [pscustomobject]@{
            Codigo = $lector['CodigoRespuesta']; Mensaje = $lector['Mensaje']
            EstadoCarga = $lector['EstadoCarga']; Motivo = $lector['MotivoRechazo']
        }
        $lector.Close()
    }
    finally { $cn.Close() }

    if ($resultado.EstadoCarga -eq 'Importado') {
        Copy-Item -LiteralPath $archivo.FullName -Destination (Join-Path $carpetaLaserfiche "$entryId$($archivo.Extension)") -Force
        Move-Item -LiteralPath $archivo.FullName, $archivoXml.FullName -Destination $carpetaProcesados -Force
        Write-Host "  IMPORTADO. idDocumento (EntryId) = $entryId" -ForegroundColor Green
        Write-Host "  Pruebe: GET /api/v1/cargas/$correlativo  y  GET /api/v1/documentos/$entryId/contenido"
    }
    else {
        Move-Item -LiteralPath $archivo.FullName, $archivoXml.FullName -Destination $carpetaRechazados -Force
        Write-Host "  RECHAZADO: $($resultado.Motivo) (código $($resultado.Codigo)) - $($resultado.Mensaje)" -ForegroundColor Red
        Write-Host "  Pruebe: GET /api/v1/cargas/$correlativo"
    }
}

do {
    $pares = Get-ChildItem -LiteralPath $carpetaSftp -Filter '*_data.xml' | Sort-Object LastWriteTime
    foreach ($xml in $pares) {
        try { Procesar-Par $xml }
        catch { Write-Host "  ERROR procesando $($xml.Name): $($_.Exception.Message)" -ForegroundColor Red }
    }
    if (-not $Vigilar -and $pares.Count -eq 0) { Write-Host "No hay cargas en $carpetaSftp" }
    if ($Vigilar) { Start-Sleep -Seconds $IntervaloSegundos }
} while ($Vigilar)
