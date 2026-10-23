# 04: Live tracking in the Agent

**What to build:** The running Agent detects tracked apps via real process snapshots, handles sleep, lock, shutdown and crash recovery, and writes sessions and daily usage.

**Blocked by:** 03

**Status:** ready-for-agent

- [ ] Notepad added via SQL produces a correct session and daily_usage row within 10 s
- [ ] Sleep cuts the session; killing the Agent mid-session recovers without double counting
- [ ] Agent idle memory and CPU are measured and logged against the budgets
