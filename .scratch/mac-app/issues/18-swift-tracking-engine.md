# Swift tracking engine and usage accumulator

Status: ready-for-agent
Spec: ../spec.md · Plan: ../plan.md (phase 4)
Blocked by: 02, 17
Needs a Mac.

Port `TrackingEngine`, `ISessionStore` (SQLite store), `IClock`, `IProcessProvider` and `UsageAccumulator` (including `RebuildAll()`) to Swift as a pure state machine behind protocols, following `TimeSink.Core/Tracking` line for line: one open session per app, 30 s heartbeat, midnight split, `minSessionSeconds`, sleep and lock handling, the 20 s tick-gap fallback, crash recovery, clock moving backwards.

Done when: every fixture group for tracking and accumulation passes in Swift, including "incremental equals `RebuildAll()`".
