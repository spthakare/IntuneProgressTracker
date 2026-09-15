# Intune Progress Tracker v4 architecture

## 1. Production deployment model

The tracker is a **bootstrap/prerequisite component**, not one of the application workloads being monitored.

Recommended Intune layout:

```text
Device/User enrollment
        |
        +--> Tracker Bootstrap (small Win32 package)
        |        |
        |        +--> installs tracker + config
        |        +--> HKLM Run -> interactive-user tracker
        |        +--> tracker waits for IME/policy convergence
        |
        +--> Win32 apps / MSI / PowerShell workloads
                 |
                 +--> normal Intune assignment/dependency processing
```

The bootstrap can be targeted to the same device/user scope as the application wave. Where deterministic ordering is required, use Intune Win32 application dependencies so the bootstrap is a prerequisite for the first application workload.

The tracker itself does not install, restart, retry, or modify the workloads. It only observes local state.

## 2. Hybrid Entra ID + GPO auto-enrollment timing

The tracker may start before IME is fully ready. It therefore has explicit phases:

1. Waiting for IME
2. Starting IME
3. Waiting for first policy evaluation
4. Discovering workloads
5. Installing applications
6. Complete / Action required

It does not treat an empty registry/log state as "complete".

Completion requires:

- IME detected
- IME policy evaluation observed
- discovery stabilization period elapsed
- minimum observation period elapsed
- no-state grace period elapsed
- all tracked workloads complete
- no failed workload
- local IME activity has entered a quiet period

## 3. Assignment discovery without Graph

There are two supported modes.

### Authoritative manifest mode

`RequiredWorkloads.json` is deployed with the bootstrap. This is the deterministic mode and is preferred for a known corporate build profile.

The manifest identifies Win32 app IDs, MSI product codes, and PowerShell script IDs.

### Local discovery mode

If no manifest is present, the tracker uses local IME evidence:

- `Win32Apps` registry state
- `AppWorkload.log`
- `AgentExecutor.log`
- `EnrollmentStatusTracking` MSI state
- `IntuneManagementExtension.log`

This is deliberately treated as **local observed state**, not a claim about the complete cloud assignment graph. The application therefore waits for policy evaluation and stabilization before allowing completion.

## 4. Important limitation

Without Microsoft Graph or another authoritative Intune service/API, a client cannot reliably enumerate every cloud-side group assignment before Intune evaluates it locally. v4 does not pretend otherwise.

For production deterministic behavior, deploy a manifest containing the expected workload IDs. For flexible deployments, use local discovery mode and retain conservative stabilization/quiet-period rules.

## 5. Reboots

A workload returning a reboot-required condition is not counted as installed by default. The UI shows the restart-required state and continues monitoring after reboot/logon.

The tracker also checks common Windows reboot-pending indicators.

## 6. PowerShell scripts

The tracker reads `AgentExecutor.log` for locally executed Intune PowerShell script activity. Microsoft documents this log as the IME log for PowerShell script execution.

Script history is observational. The tracker does not execute or rerun scripts.

## 7. Security

- No Graph credentials.
- No Intune credentials.
- No policy mutation.
- No app installation commands.
- Read-only access to local Intune state and logs.
- Bootstrap requires administrative/SYSTEM installation rights because it writes under Program Files and HKLM Run.
