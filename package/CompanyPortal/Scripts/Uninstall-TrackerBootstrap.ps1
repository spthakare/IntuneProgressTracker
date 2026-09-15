$ErrorActionPreference = 'SilentlyContinue'
$runKey = 'HKLM:\Software\Microsoft\Windows\CurrentVersion\Run'
$installPath = "$env:ProgramFiles\EOG\IntuneProgressTracker"

Remove-ItemProperty -Path $runKey -Name 'EOG Intune Progress Tracker' -ErrorAction SilentlyContinue
Stop-Process -Name IntuneProgressTracker -Force -ErrorAction SilentlyContinue
Remove-Item $installPath -Recurse -Force -ErrorAction SilentlyContinue
Write-Output "Tracker bootstrap removed from $installPath"
