# UsageDays.Slices; All, AppDetail and Week move onto it

Status: ready-for-agent
Spec: ../spec.md
Blocked by: 02

Add `UsageDays.Slices` (Sessions cut at Local days, each slice keeping its whole Session), per spec decisions 5 and 6. Migrate `AllQuery`, `AppDetailQuery` and `WeekQuery`, deleting their split loops. Add the direct `Slices` tests listed in the spec.

Done when: the three queries contain no `LocalDays.StartOf(day.AddDays(1)...)` split loop; existing query tests pass unchanged; the new `Slices` tests cover every case in the spec.
