[CmdletBinding()]
param(
    [string]$SourcePath = (Split-Path -Parent $PSScriptRoot),
    [string]$InstallPath = "$env:ProgramFiles\EOG\IntuneProgressTracker"
)

$ErrorActionPreference = 'Stop'
$runName = 'EOG Intune Progress Tracker'
$runKey = 'HKLM:\Software\Microsoft\Windows\CurrentVersion\Run'
$resolvedSource = if ([string]::IsNullOrWhiteSpace($SourcePath)) { (Split-Path -Parent $PSScriptRoot) } else { (Resolve-Path $SourcePath).Path }

# Windows Win32 packages commonly extract payloads into either a top-level publish folder or a CompanyPortal\App folder.
$appCandidates = @(
    (Join-Path $resolvedSource 'App'),
    (Join-Path $resolvedSource 'publish'),
    $resolvedSource
)

$sourceApp = $null
foreach ($candidate in $appCandidates) {
    if (Test-Path $candidate) {
        $candidateExe = Join-Path $candidate 'IntuneProgressTracker.exe'
        if (Test-Path $candidateExe) {
            $sourceApp = $candidate
            break
        }
    }
}

if ($null -eq $sourceApp) {
    throw "Published tracker not found under: $resolvedSource"
}

$configCandidates = @(
    (Join-Path $resolvedSource 'config'),
    (Join-Path $sourceApp 'config'),
    $sourceApp
)
$configSource = $null
foreach ($candidate in $configCandidates) {
    if (Test-Path $candidate) {
        $configSource = $candidate
        break
    }
}

New-Item -ItemType Directory -Path $InstallPath -Force | Out-Null
Copy-Item (Join-Path $sourceApp '*') $InstallPath -Recurse -Force
if ($configSource -and $configSource -ne $sourceApp) {
    Copy-Item (Join-Path $configSource '*') $InstallPath -Recurse -Force
}

$exe = Join-Path $InstallPath 'IntuneProgressTracker.exe'
if (-not (Test-Path $exe)) { throw "Published tracker not found: $exe" }

# SYSTEM installs the bootstrap; HKLM Run launches the WPF UI in the next interactive user's session.
New-Item -Path $runKey -Force | Out-Null
Set-ItemProperty -Path $runKey -Name $runName -Value ('"{0}"' -f $exe)

# Reset per-user completion markers when this bootstrap is intentionally redeployed.
Get-ChildItem 'C:\Users' -Directory -ErrorAction SilentlyContinue | ForEach-Object {
    Remove-Item (Join-Path $_.FullName 'AppData\Local\EOG\IntuneProgressTracker\.setup-complete') -Force -ErrorAction SilentlyContinue
}

Write-Output "Tracker bootstrap installed: $InstallPath"
