# 04 Stop re-importing an unchanged remote file

Status: ready-for-agent
Spec: ../spec.md (Phase 3)
Blocked by: none

## Tasks
- Add `SyncStatus.Unchanged` (Swift `.unchanged`).
- `SyncReader.Import`: read the manifest first; if `settings['sync.imported.<deviceId>']` equals `"<hash>:<createdUtc>"` and the remote device row is active, return `Unchanged` without extracting or rebuilding.
- Record the fingerprint inside the ingest transaction on success.
- `SyncHost.Check`: in-memory skip keyed by (path, length, lastWriteUtc). `Unchanged` keeps state `synced`, does not set `SyncLastImportUtc`, logs nothing.
- Clear the fingerprint in `ForgetDevice`, `DeleteDeviceHistory`, `HistoryDeleter.DeleteAll`, passphrase or `sync.encrypted` change, and the Mac equivalents (`forgetDevice`, `deleteDeviceHistory`, `DataExport.deleteAllHistory`).
- Do PC (C#) first, then mirror on the Mac.
- Optional: rebuild `daily_usage` only for the remote device's apps.

## Done when
Second `Check()` on an unchanged file does zero writes; a re-exported file imports; deleting device history forces a re-import; `daily_usage` unchanged after 20 checks; PC log has one import line per Mac change. C# tests and mirrored Swift tests added.

## Comments
