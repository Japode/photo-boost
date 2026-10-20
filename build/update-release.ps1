# =============================================================================
# update-release.ps1 - Define o numero de versao do PhotoBoost em um unico passo.
# Depois do update-release.ps1 do claude-tray.
#
# Atualiza os dois lugares que carregam a versao, e que um teste mantem iguais
# (tests\PhotoBoost.Architecture.Tests\VersionTests.cs):
#   - Directory.Build.props            -> <Version>x.y.z</Version>
#   - src\PhotoBoost.App\app.manifest  -> assemblyIdentity version="x.y.z.0"
# O instalador le a versao do exe publicado, entao nao ha um terceiro lugar.
#
# Uso:  build\update-release.cmd 0.1.0
#       powershell -File build\update-release.ps1 -Version 0.1.0
# =============================================================================
param(
    [Parameter(Mandatory = $true)]
    [string]$Version
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent

if ($Version -notmatch '^[0-9]+\.[0-9]+\.[0-9]+$') {
    throw "Versao invalida: '$Version'. Use o formato x.y.z (ex.: 0.1.0)."
}

# Sempre UTF-8 sem BOM: no Windows PowerShell 5.1, Get-Content usa o codepage ANSI por padrao.
$utf8 = New-Object System.Text.UTF8Encoding($false)
function Read-Utf8([string]$path) { [System.IO.File]::ReadAllText($path, $utf8) }
function Write-Utf8([string]$path, [string]$text) { [System.IO.File]::WriteAllText($path, $text, $utf8) }

# --- 1) Directory.Build.props: <Version> -------------------------------------
$propsPath = Join-Path $root 'Directory.Build.props'
$props = Read-Utf8 $propsPath
if ($props -notmatch '<Version>\s*([0-9]+\.[0-9]+\.[0-9]+)\s*</Version>') {
    throw 'Nao foi possivel encontrar <Version> em Directory.Build.props'
}
$old = $Matches[1]
$props = $props -replace '<Version>\s*[0-9]+\.[0-9]+\.[0-9]+\s*</Version>', "<Version>$Version</Version>"
Write-Utf8 $propsPath $props
Write-Host "Directory.Build.props: $old -> $Version"

# --- 2) app.manifest: assemblyIdentity version (x.y.z.0) ---------------------
$manifestPath = Join-Path $root 'src\PhotoBoost.App\app.manifest'
$manifest = Read-Utf8 $manifestPath
$pattern = '(<assemblyIdentity\s+version=")[0-9]+\.[0-9]+\.[0-9]+\.[0-9]+(")'
if ($manifest -notmatch $pattern) {
    throw 'Nao foi possivel encontrar assemblyIdentity version em app.manifest'
}
$manifest = $manifest -replace $pattern, "`${1}$Version.0`${2}"
Write-Utf8 $manifestPath $manifest
Write-Host "app.manifest: assemblyIdentity $Version.0"

Write-Host ''
Write-Host "Versao definida como v$Version."
