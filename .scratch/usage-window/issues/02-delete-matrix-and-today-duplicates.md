# Delete Matrix's private loader and Today's date helpers

Status: ready-for-agent
Spec: ../spec.md
Blocked by: 01

`MatrixQuery` uses `UsageLoader.LoadApps`/`LoadSegments` and drops its private `App`/`Seg` copies. `TodayQuery` uses `LocalDays.DateOf`/`StartOf` instead of its private `LocalDate`/`DayStart`.

Done when: `MatrixQueryTests` and Today tests pass unchanged; no private loader or day helper remains in those two files.
