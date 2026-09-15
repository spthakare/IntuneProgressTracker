# v4 Intune deployment recipe

## Bootstrap application

Create one small Win32 application called:

`EOG - Intune Progress Tracker Bootstrap`

Install command:

```text
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Install-TrackerBootstrap.ps1
```

Uninstall command:

```text
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Uninstall-TrackerBootstrap.ps1
```

Install behavior: **System**.

Detection should be based on the installed tracker executable, for example:

`C:\Program Files\EOG\IntuneProgressTracker\IntuneProgressTracker.exe`

Do not use the WPF UI process as the detection rule.

## Ordering

The bootstrap should be assigned to the same scope as the tracked application wave. For a deterministic sequence, make the first tracked Win32 app depend on the bootstrap.

Do not add the other tracked apps as dependencies of the tracker. The tracker is the observer, not a dependency of the workloads it observes.

## Manifest strategy

For a standard EOG build profile, deploy a manifest containing the expected workload IDs. This can be done by replacing `config\RequiredWorkloads.json` in the bootstrap package before packaging.

For devices where the workload set changes by user/group, local discovery mode can be used. In that mode the tracker waits for IME policy evaluation and a stabilization/quiet period before it can close.

## Hybrid GPO auto-enrollment

The expected timing is:

1. Windows device is domain/hybrid joined.
2. GPO automatic MDM enrollment occurs.
3. Intune receives the device/user enrollment.
4. IME installs when a qualifying workload is assigned.
5. Bootstrap installs the tracker.
6. User logon starts the tracker in the user's desktop session.
7. Tracker initially shows `Waiting for Intune Management Extension`.
8. IME policy evaluation appears locally.
9. Win32/MSI/PowerShell local state begins arriving.
10. Tracker transitions through downloading/installing/waiting/restart-required/failed/installed.
11. Tracker closes only after the configured convergence conditions are satisfied.

The application never assumes that the first local registry scan represents the final assignment set.
