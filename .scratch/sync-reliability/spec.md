# Sync reliability: implementation plan (bugs 1-5)

Status: ready-for-human
Scope: Mac export stalls, Mac shared-connection races, PC redundant re-import, misleading "Read its file" time, PC encryption pause/dead ends.
Out of scope (separate tickets): encryption decision mismatch between platforms, third-device lockout after Mac DB reset, `daily_usage` rebuild race, tracker loading remote apps, "Sync now" blocking the agent loop, apostrophe in folder path, non-atomic Mac file replace.

## What we know (evidence, not guesses)

- PC imports the Mac file successfully every 15 s (5,021 log lines, 0 sync errors). PC database holds 307 Mac sessions, last at Nov 10 21:19:28.
- The Mac's `.tssync` in OneDrive was last written Nov 10 21:19. The Mac should rewrite it at least every 5 min and after every PC file change. So the Mac stopped *exporting* (or is not running / not uploading).
- Mac code: one `SQLiteDatabase` handle is shared by the main-thread tracker/UI (`AppDelegate.swift:24`, `AppState`) and the background `SyncHost` (`DispatchQueue.global`). `FULLMUTEX` only serialises single calls, not transactions.
- **Unconfirmed hypothesis for bug 1:** a background `ATTACH ... AS snap` + `INSERT` runs inside whatever explicit transaction the main thread has open; `DETACH` then fails ("database snap is locked") and is swallowed by `try?`; every later export fails on `ATTACH`. Export errors are only `NSLog`ged, so the UI still says "Up to date".
- I cannot run the Mac app or Swift tests from this Windows machine. Everything marked **[Mac]** must be built and verified on the Mac.

## Design decisions

1. Sync gets its **own database connection** on the Mac (mirrors Windows, which opens a fresh `Db` per check). No `ATTACH`/`BEGIN` on the connection the UI and tracker use.
2. Imports are **skipped when the remote file has not changed**, using a persisted fingerprint (manifest `hash` + `createdUtc`) in the local `settings` table. No schema migration, so no M006 and no change to `shared/sync-format.md` format version.
3. "Last read" in the UI becomes the **remote file's own timestamp** (`devices.last_file_utc`), not the import time.
4. Export and import are **independent**: a failure in one never blocks the other. Encryption errors pause only the *import of that file* until something relevant changes, never forever.
5. Every non-success sync result is **visible**: logged on both platforms, and the Mac surfaces export failure in `sync.state`.

---

## Phase 0: Confirm the Mac hypothesis before refactoring (small, ~0.5 day) **[Mac]**

Goal: know whether bug 1 is the cause, and get a failing test.

0.1 On the Mac: Console.app, filter `SyncWriter error` / `SQLite`. Look for "database snap is already in use" or "is locked". Record result in this file under Comments.
0.2 Add a durable log on the Mac (`~/Library/Logs/TimeSink/sync.log`, 1 MB roll, like the PC `Log.cs`). Log every `performSync` step with status and error. Needed anyway for Phase 5.
0.3 Add a failing Swift test in `SyncTests.swift`: open an explicit transaction on the shared db from one thread, run `SyncWriter.write` from another, then run it again. Expect: second export succeeds. (Today it should fail; if it does not, revise the hypothesis before continuing.)
0.4 Quick unblock for the user now: quit and relaunch TimeSink on the Mac, press "Sync now", confirm the OneDrive file timestamp updates and that the PC picks up Nov 11-12 Mac sessions.

Exit: hypothesis confirmed or replaced; test exists.

## Phase 1: Isolate sync from the UI/tracker connection (bugs 1, 2) **[Mac]**

Files: `Sync/SyncHost.swift`, `Sync/SyncWriter.swift`, `Sync/SyncReader.swift`, `Storage/SQLiteDatabase.swift`, `AppDelegate.swift`, `AppState.swift`.

1.1 `SQLiteDatabase.transaction` -> `BEGIN IMMEDIATE`. Avoids read-to-write upgrade `SQLITE_BUSY` once two connections write.
1.2 `SyncHost` gets `init(dbPath:key:timeZone:keyManager:)`. It owns a private `SQLiteDatabase` opened lazily inside `performSync`, protected by the existing `lock`. Keep `init(db:)` for in-memory tests.
1.3 On any thrown error in `performSync`: close the private connection and reopen on next run (discards leaked attaches/transactions).
1.4 Defensive: before `ATTACH ... AS snap` / `AS src`, check `PRAGMA database_list`; if the alias exists, `DETACH` it first. Replace `try? DETACH` in `defer` with a logged `do/catch`.
1.5 `AppDelegate`: construct `SyncHost(dbPath: dbPath, ...)`. `AppState` keeps its own `db` for UI reads/writes.
1.6 Verify the main tracker still notices apps added by an import: `dataVersion` already adds `PRAGMA data_version` (bumped by other connections' commits). Add a test that an import on the sync connection changes `SQLiteSessionStore.dataVersion` on the main connection.
1.7 Check WAL behaviour: both connections need `busy_timeout = 5000` (already set in `init`). Confirm no `SQLITE_BUSY` surfaces in `TrackingEngine.onTick` during a 1 MB import; if it does, move import's `rebuildAll` out of the long write window.

Tests: Phase 0.3 test passes; concurrent main-thread `db.transaction` + `performSync` loop (100 iterations) with no failures and no lost tracker writes; existing `SyncTests` still pass.
Risks: second connection triples key-derivation cost (256k PBKDF2) per open: acceptable once per 5 min, but keep the connection alive between syncs if profiling shows >200 ms.
Exit: export can no longer be wedged by UI activity.

## Phase 2: Make Mac export failures visible and self-healing (bug 1, observability) **[Mac]**

2.1 In `performSync`, split export and import into independent `do/catch` blocks (already so on Mac; keep). On export failure: write `sync.state = export_failed` and `sync.last_error` (short text, no paths), NSLog + file log.
2.2 Add `SyncDeviceStatus.exportFailed` ("Couldn't write sync file") and a banner in `DevicesSyncView.swift` next to the existing encryption banners, with a "Retry" button calling `syncNow()`.
2.3 Retry policy: on export failure retry after 30 s, then 5 min (reuse the 5-minute timer); clear `export_failed` on next success.
2.4 Staleness hint: if our own last export is older than 15 min while sync is enabled and the folder exists, show "Last wrote its file X ago" in warning colour.

Tests: Swift test forcing `SyncWriter.write` to throw (read-only folder) -> state becomes `export_failed`, then recovers after permissions restored.
Exit: a stalled export is visible within one sync cycle and retries on its own.

## Phase 3: Stop the PC re-importing an unchanged file (bug 3) **[Windows, then mirror on Mac]**

Files: `SyncReader.cs`, `SyncHost.cs`, `SyncImportResult.cs` (+ `SyncStatus.Unchanged`), `DeviceIdentity.cs`, Swift equivalents.

3.1 Add `SyncStatus.Unchanged` (Swift: `.unchanged`).
3.2 `SyncReader.Import`: after parsing the manifest (cheap: read `manifest.json` only, before extracting `snapshot.db`), look up `settings['sync.imported.<deviceId>']`. If it equals `"<hash>:<createdUtc>"` and the remote device row exists and `state = 'active'`, return `Unchanged` without extracting, decrypting or rebuilding.
3.3 On successful import, store the fingerprint inside the same transaction as the ingest, so a rolled-back import never records it.
3.4 `SyncHost.Check`: add an in-memory fast path keyed by `(path, length, lastWriteUtc)` to skip even opening the zip. Treat `Unchanged` like success for state (`synced`) but do **not** call `SetSetting(SyncLastImportUtc)` or log.
3.5 Invalidate the fingerprint when local data it depends on changes: `DeviceIdentity.ForgetDevice`, `DeleteDeviceHistory`, `HistoryDeleter.DeleteAll`, and the Mac equivalents (`AppState.forgetDevice`, `deleteDeviceHistory`, `DataExport.deleteAllHistory`). Restore needs no change (settings travel with the DB). Also clear it when `sync.encrypted` or the passphrase changes.
3.6 Log rate: one line per *actual* import; none for `Unchanged`.
3.7 Optional hardening (only if wanted): rebuild `daily_usage` for just the remote device's apps instead of `RebuildAll()`. Needs a new `ISessionStore` method on both platforms; not required for the bug.

Tests (C#, `SyncHostTests`/`SyncWriterReaderTests`): second `Check()` on an unchanged file returns `Unchanged` and performs zero writes; a re-exported file imports again; deleting device history makes the next check re-import; `daily_usage` unchanged after 20 consecutive checks. Mac: mirror in `SyncTests.swift`.
Risk: a stale fingerprint hides a needed re-import. Mitigated by 3.5 and by tying the fingerprint to the device row existing.
Exit: PC log shows one import line per Mac file change; CPU and log growth drop to near zero when idle.

## Phase 4: Correct "last read" in the UI (bug 4)

4.1 `SettingsView.Devices.cs:722-726`: show `d.LastFileUtc` ("Mac's file from 2 days ago") instead of `LastImportUtc`. Keep import time only as a tooltip/secondary text if desired. Do the same in `FlyoutWindow.xaml.cs:261` (uses `SyncLastImportUtc`) and the Mac view (find the equivalent in `DevicesSyncView.swift`/`AppState.swift:566-567`).
4.2 `last_file_utc` is only updated on a real import (Phase 3 skips unchanged), which is the right semantics: it is the newest file we have actually read.
4.3 Add a stale-remote hint: if the remote file is older than a threshold (suggest 24 h) show a neutral "Other device hasn't synced since <date>" line. Do not turn it into an error state: a sleeping Mac is normal.
4.4 `UpdateSyncState` stays `synced` when a remote device exists; wording changes are UI-only.

Tests: unit-test the display string builder (extract to Core so both UIs use it, e.g. next to `LastBackupText`), covering fresh, hours-old, days-old and never-read.
Exit: with today's data the PC says "Mac's file from 2 days ago", which immediately points at the Mac side.

## Phase 5: Encryption errors must not wedge sync (bug 5) **[Windows, then Mac]**

Files: `SyncHost.cs`, `SyncAgentHost.cs`, `SyncReader.cs`, `Setting.cs` strings; Swift `SyncHost.swift`, `AppState.swift`.

5.1 Windows `SyncHost.Check`: remove `return` + `_backoffUntil = MaxValue` on `EncryptionKeyMissing/Wrong/RemoteNotEncrypted`. Instead record `state` and a **retry fingerprint** = (remote fingerprint, local passphrase file mtime, `sync.encrypted`). Skip importing that file until the fingerprint changes; keep exporting and keep scanning other files.
5.2 Decouple export from import: a failed export logs and records `export_failed` but still attempts the import pass (currently `return`s at `SyncHost.cs:136`).
5.3 Log every non-success status once per change: `BadHash`, `NewerFormat`, `DeviceForgotten`, `ConflictCopy`, `Failed`, `SelfFile` (debug only). Add `sync.last_error` setting for the UI.
5.4 Dead-end exits: setting a passphrase (`OnSyncPassphraseSet`/`setSyncPassphrase`) or clearing it already calls `SyncNow`; make both also clear the retry fingerprint and invalidate the Phase 3 import fingerprint.
5.5 Mac `isPausedDueToEncryptionError`: replace the permanent flag with the same retry-fingerprint logic so it resumes when the passphrase, `sync.encrypted` or the remote file changes, and still exports while paused.
5.6 Do **not** change the encrypt-or-not decision rule in this phase (bug 6), but add a test documenting the current Mac/PC mismatch so the follow-up ticket has a red test to flip.

Tests: C# `SyncHostTests`: wrong passphrase -> state `encryption_key_wrong`, export still written; correct passphrase later (no restart) -> imports and state `synced`; remote file replaced -> retried. Mirror on Mac.
Exit: no state requires an app restart or manual "Sync now" to recover.

## Phase 6: Integration, docs, rollout

6.1 Cross-platform fixture: extend `shared/fixtures/sync-import.json` with an "unchanged file" and an "encryption error then fixed" scenario; run in `SharedFixtureTests.cs` and the Swift fixture loader.
6.2 Update `shared/sync-format.md` section 5.2 (new `Unchanged` behaviour, retry rules) and `docs/manual-tests.md` (stalled-export and wrong-passphrase checklists). Add an entry to `docs/decisions.md` for the own-connection decision and fingerprint design.
6.3 Manual end-to-end on real hardware: Mac and PC on OneDrive; leave both running 30 min; confirm timestamps on both `.tssync` files advance, PC log has one import line per Mac change, no `export_failed` on the Mac; then repeat with the Mac popover open and an app-add transaction running.
6.4 Rollout order: ship Windows (Phases 3-5 are PC-safe on their own and cannot harm the Mac), then Mac build. Tell the user the Mac app must be updated for bug 1 to be fixed; the PC changes alone will not make the Mac export again.

## Order and dependencies

0 -> 1 -> 2 (Mac track) ; 3 -> 4 -> 5 (PC first, then mirror). The two tracks are independent until Phase 6. Phase 3 should land before Phase 4 so the "last read" time means what the UI says. Suggested ticket split under `.scratch/sync-reliability/issues/`: 01 (Phase 0), 02 (Phase 1), 03 (Phase 2), 04 (Phase 3), 05 (Phase 4), 06 (Phase 5), 07 (Phase 6).

## Open questions for the owner

- Stale-remote threshold for the Phase 4 hint (24 h proposed).
- Should "export_failed" also appear on the PC flyout, or only in Settings?
- OK to add the Mac file log in Phase 0 (a new log location), or reuse `NSLog` only?
