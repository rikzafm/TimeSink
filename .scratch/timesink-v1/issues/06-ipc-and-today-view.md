# 06: IPC and Today view

**What to build:** The Today view shows the live ticking combined total, running count, hourly chart with typical-day line, now marker and sleep band, three stat tiles, and expandable app rows with sessions and 7-day comparison.

**Blocked by:** 04, 05

**Status:** resolved

- [ ] Named-pipe state pushes drive the live total
- [ ] Today numbers match a seeded fixture
- [ ] Row expand shows sessions and average comparison

## Answer

Implemented and merged on integration/timesink-v1; see docs/decisions.md for deviations. Unchecked acceptance boxes were verified by tests or the implementer's live run, not by hand-tick.
