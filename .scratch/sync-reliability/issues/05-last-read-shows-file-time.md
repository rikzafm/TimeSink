# 05 Show the remote file's own time as "last read"

Status: resolved
Spec: ../spec.md (Phase 4)
Blocked by: 04

## Tasks
- `SettingsView.Devices.cs` (~line 722) and `FlyoutWindow.xaml.cs` (~line 261): use `LastFileUtc` instead of import time; do the same in the Mac view and `AppState.swift` (~566).
- Add a neutral stale hint when the remote file is older than 24 h (threshold open, see spec). Not an error state; `UpdateSyncState` stays `synced`.
- Extract the display string to Core (next to `LastBackupText`) so both UIs share it.
- Unit tests: fresh, hours-old, days-old, never read.

## Done when
With today's data the PC shows the Mac file as about 2 days old.

## Comments

## Answer
Implemented on branch sync-reliability (see git log). C# verified with dotnet test; Mac Swift written but NOT compiled or run - build and run the Swift tests on a Mac.
