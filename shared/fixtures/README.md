# Shared Test Fixtures

This directory contains shared cross-platform test fixtures formatted in JSON. Both the Windows (C#) test suite and the macOS (Swift) test suite load these fixtures to verify that both implementations produce identical results for the tracking engine, database accumulation, midnight/DST handling, crash recovery, and backup/restore.

---

## 1. Fixture Files

| File | Purpose |
|---|---|
| `single-session.json` | Process lifecycle, start/stop offsets, and coalescing multiple processes of one app into a single continuous session. |
| `midnight-and-dst.json` | Sessions crossing local midnight (UTC and `America/New_York`), and DST transitions (23h spring-forward and 25h fall-back). |
| `suspend-resume-and-power.json` | Suspend/resume, tick-gap fallback (missed sleep detection), shutdown event, and `pauseOnSleep = false`. |
| `lock-and-unlock.json` | Lock and unlock events with `countWhenLocked` enabled (`true`) and disabled (`false`). |
| `crash-recovery.json` | Crash recovery: fresh heartbeat continuation, stale heartbeat (>120s) close at `last_seen`, PID reuse, and short session removal. |
| `min-session-and-clock.json` | `minSessionSeconds` filtering and usage subtraction, exact threshold retention, and backwards clock protection. |
| `accumulator-rebuild.json` | Deterministic multi-day scenario verifying incremental `daily_usage` equals a full `RebuildAll()`. |
| `backup-and-restore.json` | Backup create/replace round trip, idempotent double-merge, and rejection of newer schema versions. |
| `sync-import.json` | *(To be added in Ticket 08)* Snapshot import idempotency. |
| `device-overlap.json` | *(To be added in Ticket 06)* Multi-device union uptime with overlap counted once. |

---

## 2. File Structure

Each fixture file has the following top-level structure:

```json
{
  "name": "fixture-name",
  "description": "Human-readable summary",
  "timeZoneId": "UTC",
  "baseTimeUtc": "2026-03-10T12:00:00Z",
  "settings": {
    "minSessionSeconds": 15,
    "pauseOnSleep": true,
    "countWhenLocked": true
  },
  "apps": [
    {
      "id": 1,
      "name": "Notepad",
      "path": "c:\\windows\\notepad.exe",
      "bundleId": "com.microsoft.Notepad",
      "addedUtc": 0
    }
  ],
  "cases": [
    {
      "name": "case-name",
      "description": "Scenario description",
      "events": [ ... ],
      "expectedSessions": [ ... ],
      "expectedDailyUsage": { "2026-03-10": 45 }
    }
  ]
}
```

### Event Object Types (`events`)

| Event Type | Fields | Description |
|---|---|---|
| `proc_start` | `pid`, `appId`, `procStartOffset`, `offset` | Process start reported by process monitor. |
| `proc_exit` | `pid`, `offset` | Process exit reported by process monitor. |
| `tick` | `offset` | Engine timer tick at relative second offset from `baseTimeUtc`. |
| `tick_range` | `from`, `to`, `step` | Sequence of ticks from offset `from` to `to` with given `step` seconds. |
| `clock_set` | `offset` | System clock jump to relative second offset. |
| `suspend` | `offset` | Power suspend event at offset. |
| `resume` | `offset` | Power resume event at offset. |
| `lock` | `offset` | Screen lock event at offset. |
| `unlock` | `offset` | Screen unlock event at offset. |
| `shutdown` | `offset` | System shutdown event at offset. |
| `crash_and_restart` | `offset` | Engine termination and restart at offset. |
| `recover_crash` | `offset` | Call to `RecoverFromCrash(now)`. |

---

## 3. How Tests Load Fixtures

### In C# (`TimeSink.Tests`)
Use `FixtureLoader.Load<T>("fixture-name.json")` from `tests/TimeSink.Tests/Tracking/FixtureLoader.cs`. The test loader deserializes the JSON, creates a test rig, plays the event sequence, and asserts that the resulting SQLite sessions and `daily_usage` records match `expectedSessions` and `expectedDailyUsage`.

### In Swift (`TimeSinkCoreTests`)
Use `Bundle.module.url(forResource:withExtension:)` or read from the repository's `shared/fixtures/` path. Decode using `JSONDecoder`, instantiate `TrackingEngine` with protocol fakes, execute the event sequence, and verify the resulting records.
