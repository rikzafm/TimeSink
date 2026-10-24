# Manual tests

Source: implementation.md section 12.2. Nothing below has been run yet; every box is unticked on purpose. Fill in
Result (Pass/Fail + notes), Date, Build (version or commit) and Tester when you run each item.

## Checklist (section 12.2)

| Done | Test | How | Result | Date |
|---|---|---|---|---|
| [ ] | Two monitors, taskbar on different edges | Tray icon on each monitor/edge: the flyout opens next to the icon, fully on screen, closes on deactivate | | |
| [ ] | Mixed DPI | Monitors at different scaling (e.g. 100% and 150%): flyout size, text and charts are crisp and correctly placed on both | | |
| [ ] | Explorer restart | Restart explorer.exe (Task Manager > Windows Explorer > Restart): the tray icon comes back, tracking continues | | |
| [ ] | Sleep/resume | Sleep with a tracked app running, resume: session closed at suspend and reopened after, no inflated time | | |
| [ ] | Lock/unlock | Lock and unlock: time follows the "count when locked" setting | | |
| [ ] | Kill Agent mid-session | End `TimeSink.Agent.exe` while a tracked app runs, restart it within 2 minutes (continues) and after more than 2 minutes (closed at last seen); no double counting, no negative durations | | |
| [ ] | Store app | Track Calculator (or another Store app): sessions recorded, shown by package | | |
| [ ] | Elevated app | Track an app run as administrator: it is detected and counted | | |
| [ ] | Rapid tray clicking | Click the tray icon fast: no reopen flicker | | |
| [ ] | Install/uninstall on a clean user profile | See "Installer" below | | |
| [ ] | Theme install via double-click | Post-v1 (installable themes are out of v1 scope, decision 8): record N/A or skip | | |

## Installer (clean profile)

Use a fresh Windows user (or VM). Build with `pwsh installer\Build-Package.ps1` on a machine that has Inno Setup 6
(the `.iss` has not been compiled yet).

| Done | Step | Expected | Result | Date |
|---|---|---|---|---|
| [ ] | Run `TimeSink-Setup-<version>.exe` as a standard user | No UAC prompt; installs to `%LocalAppData%\Programs\TimeSink` | | |
| [ ] | Launch, track an app, enable Start with Windows | `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\TimeSink` = `"...\TimeSink.Agent.exe" --background` | | |
| [ ] | Install again over the running copy | Agent is stopped and replaced, data kept | | |
| [ ] | Uninstall, answer No to data deletion | Agent and UI stopped, files removed, Run value gone, `%LocalAppData%\TimeSink` kept | | |
| [ ] | Reinstall and uninstall, answer Yes | `%LocalAppData%\TimeSink` deleted | | |
| [ ] | Portable zip: unzip to a user folder, run Agent | Works without installer; tray icon and flyout appear | | |

## 24 h soak (budgets, implementation.md section 12)

Budgets: Agent private working set <= 15 MB after 24 h; Agent CPU about 0% (< 0.1% average over 10 min).
Note decision 44/48: the UI About row shows working set, the budget is private bytes.

1. Install (or unzip) the release build on a normal PC and track at least one app that stays open.
2. Start the Agent and note the start time. Use the PC normally for 24 hours, including at least one sleep/resume.
   Do not open the UI more than needed.
3. At start, after 1 h, and after 24 h run in PowerShell:
   ```powershell
   Get-Process TimeSink.Agent | Select-Object StartTime, PrivateMemorySize64, WorkingSet64, CPU, HandleCount
   ```
4. For CPU, sample 10 idle minutes at the end:
   ```powershell
   $p = Get-Process TimeSink.Agent; $c0 = $p.CPU; Start-Sleep 600; $p.Refresh()
   '{0:P3} of one core' -f (($p.CPU - $c0) / 600)
   ```
   (Divide by the logical core count for share of total CPU.)
5. Check `%LocalAppData%\TimeSink\logs\agent.log` for errors and `timesink.db` size.
6. Pass = private bytes <= 15 MB at 24 h, CPU < 0.1% average, no growth trend in handles, no errors, no negative or
   duplicated sessions.

| Sample | Date/time | Private bytes (MB) | Working set (MB) | Handles | CPU s total | Idle CPU % (10 min) |
|---|---|---|---|---|---|---|
| Start | | | | | | |
| +1 h | | | | | | |
| +24 h | | | | | | |

| Field | Value |
|---|---|
| Build / commit | |
| Machine (CPU cores, Windows build) | |
| Tracked apps during soak | |
| Sleep/resume cycles | |
| agent.log errors | |
| DB size at end | |
| Verdict (Pass/Fail) | |
| Date / tester | |
