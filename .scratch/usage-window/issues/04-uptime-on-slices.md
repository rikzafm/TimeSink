# UsageUptime.PerDay consumes slices

Status: ready-for-agent
Spec: ../spec.md
Blocked by: 03

`UsageUptime.PerDay` takes slices and drops its own day-assignment loop. `Seconds` and `SleepBands` unchanged.

Done when: Week uptime and Today uptime tests pass unchanged and `PerDay` has no day-walk loop.
