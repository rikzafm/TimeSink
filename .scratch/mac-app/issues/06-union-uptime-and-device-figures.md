# Union uptime and per-device figures in the query results

Status: ready-for-agent
Spec: ../spec.md · Plan: ../plan.md (phase 1)
Blocked by: 02, 05

`UsageUptime.Seconds` works on one device (first session to last, minus sleep). For All scope compute each device's uptime intervals (`UsageUptime.Intervals`) and union them (`UsageUptime.Union`) so an hour both devices were on counts once. Add to the results: Today (hourly distinct apps per device, each device's uptime, "on both" seconds), Week and Month (day totals per device plus overlap), Year (month totals per device plus overlap), All (devices per app), App detail (hours per device), Wrapped (Mac, PC and overlap hours so they reconcile with the total).

All intervals are UTC; only day bucketing uses the viewer's zone.

Done when: fixtures pass for overlap counted once, one device only, a device in another time zone, a DST day and a forgotten device; headline = device A + device B - on both in every result.
