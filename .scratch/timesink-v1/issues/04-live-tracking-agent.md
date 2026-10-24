# 04: Live tracking in the Agent

**What to build:** The running Agent detects tracked apps via real process snapshots, handles sleep, lock, shutdown and crash recovery, and writes sessions and daily usage.

**Blocked by:** 03

**Status:** ready-for-agent

- [ ] Notepad added via SQL produces a correct session and daily_usage row within 10 s
- [ ] Sleep cuts the session; killing the Agent mid-session recovers without double counting
- [ ] Agent idle memory and CPU are measured and logged against the budgets

## Answer

Resolved in part. Built and merged. NOT met as written: daily_usage row appears at the first 30 s heartbeat, not within 10 s (the session row appears in about 4 s; decision 41). Idle CPU measured 0.13% of one core after review fixes; the 24 h memory budget is unverified. Real hardware sleep and lock were not tested.
