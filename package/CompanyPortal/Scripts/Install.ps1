[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$installScript = Join-Path $scriptRoot 'Install-TrackerBootstrap.ps1'

if (-not (Test-Path $installScript)) {
    throw "Bootstrap installer not found: $installScript"
}

& $installScript @args
exit $LASTEXITCODE
