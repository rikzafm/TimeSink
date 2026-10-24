# 03: Tracking engine in Core

**What to build:** Given fake time, processes and power events, the engine stores correct sessions and daily usage, including sleep, lock, crash recovery, midnight and DST splits, minimum session removal and backwards clocks.

**Blocked by:** 01

**Status:** resolved

- [ ] Schema M001 and migrations exist
- [ ] All tracking tests in implementation.md section 12.1 pass against fakes and a temporary SQLite database
- [ ] Incremental accumulation equals a full rebuild on randomized sessions

## Answer

Implemented and merged on integration/timesink-v1; see docs/decisions.md for deviations. Unchecked acceptance boxes were verified by tests or the implementer's live run, not by hand-tick.
