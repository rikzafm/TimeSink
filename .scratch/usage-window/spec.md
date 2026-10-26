# Usage window: one module for Sessions as per-Local-day slices

Status: ready-for-agent

## Problem

The usage queries in `src/TimeSink.Core/Usage` repeat the same two jobs:

- **Loading.** `UsageLoader` serves Today, Week, All, AppDetail, Month, Year and Wrapped, but `MatrixQuery.cs` keeps a private `LoadApps`/`LoadSegments` copy that only omits `end_reason`/kind. `TodayQuery` has private `LocalDate`/`DayStart` duplicating `LocalDays`. Nine `Run(string dbPath, ...)` overloads repeat the read-only connection setup (ReadOnly, Pooling=false, `busy_timeout`).
- **Splitting a Session across Local days.** `AllQuery.Add`, `AppDetailQuery`, `WeekQuery` and `UsageUptime.PerDay` each re-derive the split with their own loop and DST handling.

A fix to open-Session handling or DST has to be made in several places, and each query tests DST separately.

## Decisions (settled in the architecture grilling)

1. Scope: the module also owns opening the read-only database. Public `Run(dbPath, ...)` signatures on the queries stay; they delegate to the one opener.
2. Callers receive a flat sequence of day slices and aggregate themselves (All per month, Week per group-day, AppDetail session counts). No pre-aggregated dictionaries.
3. Today's hour columns (`HourSums`) and Matrix's month buckets stay in their own queries; they use the module only for the loader and opener.
4. Matrix's private loader and Today's `LocalDate`/`DayStart` are deleted (`LocalDays` and the shared loader replace them). Matrix then also reads kind/`end_reason`, which it ignores.
5. One public entry point, `UsageWindow`: from a connection, the window, now, zone and running set it returns the apps and the day slices. `Split` is an internal seam, tested directly.
6. A slice is `(Day, Start, End)` clipped to that Local day, plus the original `UsageSeg`. "Longest Session" uses the whole Session; AppDetail counts one Session per Local day it touches.
7. `UsageUptime.PerDay` assigns days through the slices, so its day-assignment loop goes. It passes each day the whole Sessions that touch it, not the clipped slices, because sleep bands end at a Session's real end. `UsageUptime.Seconds` and `SleepBands` are unchanged (Today feeds them whole segments).

## Tests

- New, at the `UsageWindow` interface (existing temp-database fixtures): retired and unknown apps excluded; merged apps share a group; an open Session counts to now when its app is running, otherwise to its last heartbeat.
- New, directly on `Split`: midnight crossing, a 23 h day, a 25 h day, a skipped midnight.
- Existing query tests stay unchanged as regression. `TodayDstTests` stays (hour columns remain in Today).

## Out of scope

Hour-column splitting, Matrix month buckets, any change to what the views show.

## Revision after ticket 03

Decision 5 changed. `UsageWindow` is only `Slices(segs, zone)`, with no "load apps and slices" entry point. All loads retired apps and AppDetail loads alias members, both of which `UsageLoader` excludes, so one loading entry point would have changed what they count. A loader plus `Slices` wrapper for Week alone would have been a three-line pass-through that fails the deletion test. Each query keeps its own loading and passes whole `UsageSeg`s to `Slices`.
