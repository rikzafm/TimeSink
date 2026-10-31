# Export shared fixtures and load them in the C# tests

Status: resolved
Spec: ../spec.md · Plan: ../plan.md (phase 0)
Blocked by: 01

Move the hand-computed numbers in `tests/TimeSink.Tests` into JSON files under `shared/fixtures/`. Each fixture holds input apps and sessions, a time zone, a "now", settings, and the expected results. Add a small C# fixture loader and make the existing tests read the files instead of inline data.

Groups: single session and several processes of one app; midnight split and DST spring-forward and fall-back; suspend, resume and the tick-gap fallback; lock and unlock; crash recovery; `minSessionSeconds` removal and subtraction; clock moving backwards; incremental accumulator equals `RebuildAll()`; backup replace, merge twice, newer schema refused; sync import twice (added in ticket 08); overlap across two devices counted once (added in ticket 06).

Record in the ticket which tests export mechanically and which had to be rewritten.

Done when: the C# suite is green reading the JSON files, and a short `shared/fixtures/README.md` explains the file shape so the Swift tests can load the same files.

## Answer

- Created shared fixture files under `shared/fixtures/`:
  - `single-session.json`
  - `midnight-and-dst.json`
  - `suspend-resume-and-power.json`
  - `lock-and-unlock.json`
  - `crash-recovery.json`
  - `min-session-and-clock.json`
  - `accumulator-rebuild.json`
  - `backup-and-restore.json`
- Created `shared/fixtures/README.md` documenting file schema and runner conventions.
- Added `FixtureLoader.cs` and `SharedFixtureTests.cs` to `tests/TimeSink.Tests/Tracking/`, and refactored `TrackingEngineTests.cs` to delegate directly to the shared fixtures.
- Test suites pass (`dotnet test` runs green for `SharedFixtureTests`, `TrackingEngineTests`, and `BackupRestoreTests`).

### Test Porting Summary

1. **Mechanically Exported**:
   - Single session and several processes of one app (`single-session.json`, from `TrackingEngineTests`)
   - Midnight split UTC and America/New_York across DST change (`midnight-and-dst.json`, from `TrackingEngineTests`)
   - Suspend, resume, tick-gap fallback, pause on sleep off, shutdown event (`suspend-resume-and-power.json`, from `TrackingEngineTests`)
   - Lock and unlock with countWhenLocked true / false (`lock-and-unlock.json`, from `TrackingEngineTests`)
   - Crash recovery: fresh heartbeat continuation, stale heartbeat closure, PID reuse, short session removal (`crash-recovery.json`, from `TrackingEngineTests`)
   - Minimum session seconds threshold removal, subtraction, and exact minimum retention (`min-session-and-clock.json`, from `TrackingEngineTests`)
   - Clock moving backwards safe (`min-session-and-clock.json`, from `TrackingEngineTests`)
   - Backup replace restore round trip, idempotent merge twice, and newer schema refusal (`backup-and-restore.json`, from `BackupRestoreTests`)

2. **Rewritten**:
   - `accumulator-rebuild.json`: `UsageRebuildPropertyTests` used a randomized 70,000-tick loop (`Random(seed)`), which is non-deterministic across platforms. Rewritten into a deterministic multi-app, multi-day session scenario verifying incremental daily_usage equals full RebuildAll().
   - `dst-spring-forward-23h-day` and `dst-fall-back-25h-day`: Derived from `UsageDaysTests` and `WeekQueryTests` to test full 23-hour and 25-hour day session accumulation under America/New_York DST transitions.

3. **Deferred to Scheduled Tickets**:
   - Sync import twice: Ticket 08 (`shared/fixtures/sync-import.json`).
   - Overlap across two devices counted once: Ticket 06 (`shared/fixtures/device-overlap.json`).
