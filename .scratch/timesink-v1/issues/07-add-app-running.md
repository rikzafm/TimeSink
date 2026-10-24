# 07: Add app: Running

**What to build:** The Add app screen's Running tab lists visible running apps; Track adds the app, extracts its icon, and the Agent tracks it within 10 s without a restart.

**Blocked by:** 06

**Status:** resolved

- [ ] Only visible, non-cloaked, non-tool-window apps are listed; tracked and ignored apps are excluded
- [ ] Track turns into Tracking with a check
- [ ] Icons come from the cache with a letter-tile fallback
- [ ] Store app such as Calculator works

## Answer

Implemented and merged on integration/timesink-v1; see docs/decisions.md for deviations. Unchecked acceptance boxes were verified by tests or the implementer's live run, not by hand-tick.
