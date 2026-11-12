# 02 Give Mac sync its own database connection

Status: ready-for-agent
Spec: ../spec.md (Phase 1)
Blocked by: 01

## Tasks
- `SQLiteDatabase.transaction` uses `BEGIN IMMEDIATE`.
- `SyncHost.init(dbPath:key:timeZone:keyManager:)` with a private connection, opened lazily under the existing lock; keep `init(db:)` for in-memory tests.
- On any error in `performSync`, close and reopen the private connection next run.
- Before `ATTACH ... AS snap/src`, check `PRAGMA database_list` and detach a leftover alias; replace `try? DETACH` with logged `do/catch`.
- `AppDelegate` builds `SyncHost(dbPath:)`; `AppState` keeps its own `db`.
- Test that an import on the sync connection changes `SQLiteSessionStore.dataVersion` on the main connection.
- Check no `SQLITE_BUSY` reaches `TrackingEngine.onTick` during a large import.

## Done when
Ticket 01's test passes; 100 iterations of main-thread transaction + `performSync` show no failures or lost tracker writes; existing `SyncTests` pass. Must be built and tested on the Mac.

## Comments
