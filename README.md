# TimeSink

TimeSink is a small Windows tray app that records how long the apps you choose are running. You pick apps; it keeps a
local history of when each ran and shows it in a flyout (Today, Week, Month, Year, All). It never looks at window
titles, documents, focus or idle time, and it never blocks anything.

## Privacy

* Everything stays on your PC, in one SQLite file. No accounts, no cloud sync, no telemetry, no update checks, no
  network access.
* Only the executable path and start/stop times of apps you chose to track are stored. Window titles and content are
  never read or logged.
* You can export CSV/JSON, back up, restore, and delete all history from Settings.

## Install

Requires Windows 11 (Windows 10 22H2 works with flat fallbacks). No admin rights needed.

* **Installer:** run `TimeSink-Setup-<version>.exe`. It installs per user to `%LocalAppData%\Programs\TimeSink`.
  "Start with Windows" is a setting inside the app (Settings), not an installer option.
* **Portable:** unzip `TimeSink-portable-win-x64.zip` anywhere you can write to and run `TimeSink.Agent.exe`
  (the tray app; it starts the UI on demand). Keep `TimeSink.Agent.exe` and `TimeSink.UI.exe` in the same folder.
  The portable copy still keeps its data in `%LocalAppData%\TimeSink` (a data-beside-the-exe mode is not in v1).

## Data location

`%LocalAppData%\TimeSink\`

| Item | Path |
|---|---|
| Database | `timesink.db` (plus `-wal`/`-shm` while running) |
| Backups | `backups\` (`.timesink` files, automatic daily by default) |
| Log | `logs\agent.log` (1 MB x 3, no window titles or content) |

## Backup and restore

Settings has Backup now, Restore (Replace or Merge) and the automatic backup mode/folder. A `.timesink` file is a zip
with `manifest.json` and `timesink.db`. Replace swaps your database (a `pre-restore-...` backup is made first); Merge
adds missing apps and sessions and is safe to run twice. To move to another PC, back up, copy the `.timesink` file, and
restore it there.

## Build

Needs the .NET 10 SDK and the Visual Studio C++ Build Tools (Native AOT). Inno Setup 6 is optional (installer only).

```powershell
pwsh installer\Build-Package.ps1            # publishes Agent (AOT) + UI (ReadyToRun, self-contained)
pwsh installer\Build-Package.ps1 -Test      # ...and verifies the zip, running it against scratch databases
```

Output is in `artifacts\` (gitignored): `TimeSink-portable-win-x64.zip` and, if `iscc.exe` is found,
`TimeSink-Setup-<version>.exe`. `installer\Test-Package.ps1 -Smoke` checks any zip without touching your real data.
Tests: `dotnet test`.

## Uninstall

* **Installer:** Settings > Apps > Installed apps > TimeSink > Uninstall (or the uninstaller in the install folder).
  It stops the Agent and UI, removes the "Start with Windows" Run entry, and asks whether to delete
  `%LocalAppData%\TimeSink` (your history). Answer No to keep it.
* **Portable:** turn off Start with Windows in Settings first (this removes the Run entry), quit from the tray menu,
  then delete the folder. Delete `%LocalAppData%\TimeSink` too if you want your data gone.

## Licence

No licence file has been added yet.
