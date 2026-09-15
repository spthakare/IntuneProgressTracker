[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$exe = Join-Path "$env:ProgramFiles" 'EOG\IntuneProgressTracker\IntuneProgressTracker.exe'

if (-not (Test-Path $exe)) {
    throw "Tracker executable not found: $exe"
}

# The installer runs under SYSTEM. Do not attempt to show a GUI from Session 0.
# If we are in the SYSTEM context, configure the logon Run key and exit.
if ($env:USERNAME -eq 'SYSTEM' -or $env:USERDOMAIN -eq 'NT AUTHORITY') {
    $runKey = 'HKLM:\Software\Microsoft\Windows\CurrentVersion\Run'
    New-Item -Path $runKey -Force | Out-Null
    Set-ItemProperty -Path $runKey -Name 'EOG Intune Progress Tracker' -Value ('"{0}"' -f $exe)
    Write-Output "Configured user-session launch for $exe"
    exit 0
}

Start-Process -FilePath $exe -WorkingDirectory (Split-Path -Parent $exe)
Write-Output "Started tracker: $exe"
