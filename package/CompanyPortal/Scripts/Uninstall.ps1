[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$uninstallScript = Join-Path $scriptRoot 'Uninstall-TrackerBootstrap.ps1'

if (-not (Test-Path $uninstallScript)) {
    throw "Bootstrap uninstaller not found: $uninstallScript"
}

& $uninstallScript @args
exit $LASTEXITCODE
