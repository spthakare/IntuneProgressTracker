# Intune Progress Tracker v4

A Windows WPF companion UI that displays the progress of Intune-managed Win32 applications, MSI applications, and PowerShell scripts during device/user provisioning.

## Design goals

- Bootstrap before the application wave.
- Survive Hybrid Entra ID + GPO auto-enrollment timing.
- Work without Microsoft Graph.
- Never confuse an empty local state with successful installation.
- Support an authoritative workload manifest when exact deployment scope is known.
- Fall back to local IME discovery when no manifest is supplied.
- Remain visible/minimizable until all required workloads complete.
- Close automatically after a stable successful completion state.

## Build

Run from an elevated PowerShell session on a Windows machine with .NET 8 SDK:

```powershell
.\scripts\Build-TrackerPackage.ps1
```

The output package is written to `package`.

## Bootstrap install command

The Intune Win32 bootstrap installer should run as SYSTEM:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Install.ps1
```

The bootstrap installs the tracker under:

`C:\Program Files\EOG\IntuneProgressTracker`

and creates an HKLM Run entry. The WPF application therefore launches in the interactive user's desktop session at logon, rather than trying to display UI from the SYSTEM session.

## Win32 packaging layout

The package is produced in a standard Intune layout:

```text
CompanyPortal/
├── App/
├── Scripts/
│   ├── Install.ps1
│   ├── Uninstall.ps1
│   ├── Detection.ps1
│   ├── Launch.ps1
│   ├── Install-TrackerBootstrap.ps1
│   └── Uninstall-TrackerBootstrap.ps1
├── Installer/
├── README.md
└── App/IntuneProgressTracker.exe
```

The installer scripts intentionally avoid relying on the current working directory and use absolute installation paths, because Intune Win32 apps may execute under SYSTEM on a clean session while the UI must be launched in the active user session only.

## Intune assignment strategy

1. Package the bootstrap as a small Win32 application.
2. Assign it to the same device/user scope that needs the tracker.
3. Make it a dependency/prerequisite of the first tracked Win32 workload where strict ordering is required.
4. Keep normal Intune assignments for the actual applications/scripts/MSIs.
5. Deploy `RequiredWorkloads.json` when a deterministic application set is required.

Do not make the tracker depend on Company Portal or on Microsoft Graph.

## Source files

- `src/` - WPF application and local Intune state engine
- `config/` - tracker configuration and workload manifest
- `scripts/Install-TrackerBootstrap.ps1` - prerequisite installer
- `scripts/Uninstall-TrackerBootstrap.ps1` - cleanup
- `scripts/Build-TrackerPackage.ps1` - publish/package helper
- `docs/ARCHITECTURE.md` - production design
