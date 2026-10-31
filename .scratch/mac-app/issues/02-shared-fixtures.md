# Export shared fixtures and load them in the C# tests

Status: ready-for-agent
Spec: ../spec.md · Plan: ../plan.md (phase 0)
Blocked by: 01

Move the hand-computed numbers in `tests/TimeSink.Tests` into JSON files under `shared/fixtures/`. Each fixture holds input apps and sessions, a time zone, a "now", settings, and the expected results. Add a small C# fixture loader and make the existing tests read the files instead of inline data.

Groups: single session and several processes of one app; midnight split and DST spring-forward and fall-back; suspend, resume and the tick-gap fallback; lock and unlock; crash recovery; `minSessionSeconds` removal and subtraction; clock moving backwards; incremental accumulator equals `RebuildAll()`; backup replace, merge twice, newer schema refused; sync import twice (added in ticket 08); overlap across two devices counted once (added in ticket 06).

Record in the ticket which tests export mechanically and which had to be rewritten.

Done when: the C# suite is green reading the JSON files, and a short `shared/fixtures/README.md` explains the file shape so the Swift tests can load the same files.
