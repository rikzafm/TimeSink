# UsageWindow with Split; All, AppDetail and Week move onto it

Status: ready-for-agent
Spec: ../spec.md
Blocked by: 02

Add `UsageWindow` (single entry point: apps plus day slices) and internal `Split`, per spec decisions 5 and 6. Migrate `AllQuery`, `AppDetailQuery` and `WeekQuery`, deleting their split loops. Add the interface tests and the direct `Split` DST tests listed in the spec.

Done when: the three queries contain no `LocalDays.StartOf(day.AddDays(1)...)` split loop; existing query tests pass unchanged; new tests cover every case in the spec.
