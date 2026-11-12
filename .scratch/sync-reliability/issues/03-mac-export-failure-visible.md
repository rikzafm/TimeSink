# 03 Make Mac export failures visible and self-healing

Status: resolved
Spec: ../spec.md (Phase 2)
Blocked by: 02

## Tasks
- On export failure set `sync.state = export_failed` and `sync.last_error` (short, no paths); log to NSLog and the file log. Clear on next success.
- Add `SyncDeviceStatus.exportFailed` and a banner with a Retry button in `DevicesSyncView.swift`.
- Retry after 30 s, then on the 5-minute timer.
- Show a warning "Last wrote its file X ago" when our own export is older than 15 min with sync enabled and the folder present.
- Swift test: read-only folder makes state `export_failed`; restoring permissions recovers.

## Done when
A stalled export shows in the UI within one cycle and retries without user action.

## Comments

## Answer
Implemented on branch sync-reliability (see git log). C# verified with dotnet test; Mac Swift written but NOT compiled or run - build and run the Swift tests on a Mac.
