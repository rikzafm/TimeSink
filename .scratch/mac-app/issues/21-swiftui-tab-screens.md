# SwiftUI tab screens

Status: resolved
Spec: ../spec.md · Plan: ../plan.md (phase 6)
Blocked by: 19, 20
Design: design/mac/Main, TodayAll, Week, Month, YearPace, YearWrapped, YearWrappedApps, YearWrappedHabits, YearMatrix, All and AppDetail (each `.dc.html`; see design/mac/README.md)
Needs a Mac.

Build the popover to match the "TimeSink for Mac" canvas: header (wordmark, This Mac | All devices, games, settings), full-width underline tabs, period chevrons, Today (this Mac and all devices, with overlapping bars, the expanded dial and the summary line), Week, Month, Year › Pace, Wrapped (Total, Top apps, Habits) and Matrix, All (with the Row style choice settled by the user), App detail. Colours come from a token file mirroring the Windows theme JSON; fonts are SF; charts are SwiftUI shapes (no chart library); sample strings live in a string table.

Done when: each canvas board has a matching screen in both device scopes, with real data from the Swift queries.
