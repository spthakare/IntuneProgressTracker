[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$exe = Join-Path "$env:ProgramFiles" 'EOG\IntuneProgressTracker\IntuneProgressTracker.exe'

if (Test-Path $exe) {
    exit 0
}

exit 1
