[CmdletBinding()]
param(
    [string]$Project,
    [string]$Output
)
$ErrorActionPreference = 'Stop'
if (-not $Project) { $Project = Join-Path $PSScriptRoot '..\src\IntuneProgressTracker.csproj' }
if (-not $Output) { $Output = Join-Path $PSScriptRoot '..\package' }
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$publish = Join-Path $root 'publish'
$companyPortal = Join-Path $Output 'CompanyPortal'
$appRoot = Join-Path $companyPortal 'App'
$scriptsRoot = Join-Path $companyPortal 'Scripts'
$installerRoot = Join-Path $companyPortal 'Installer'

Remove-Item $publish -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item $Output -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path $companyPortal, $appRoot, $scriptsRoot, $installerRoot -Force | Out-Null

dotnet publish $Project -c Release -r win-x64 --self-contained false -o $publish

# Copy the published app payload into the Win32 packaging layout.
Copy-Item "$publish\*" $appRoot -Recurse -Force
Copy-Item "$root\config\*" $appRoot -Force
Copy-Item "$root\README.md" $companyPortal -Force

# Keep the bootstrap scripts in a standard Win32 folder layout and add explicit wrapper entry points.
Copy-Item "$root\scripts\Install-TrackerBootstrap.ps1","$root\scripts\Uninstall-TrackerBootstrap.ps1","$root\scripts\Install.ps1","$root\scripts\Uninstall.ps1","$root\scripts\Detection.ps1","$root\scripts\Launch.ps1" $scriptsRoot -Force

# Optional installer metadata directory for packaging tools/processors.
Copy-Item "$root\scripts\Install-TrackerBootstrap.ps1" $installerRoot -Force

Write-Host "Package prepared at $companyPortal"
Write-Host "App payload: $appRoot"
Write-Host "Scripts: $scriptsRoot"
