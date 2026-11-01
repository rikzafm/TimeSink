# OverlapBars helper for HourlyBars, WeekColumns and PaceBars

Status: resolved
Spec: ../spec.md · Plan: ../plan.md (phase 3)
Blocked by: 06, 10
Design: design/mac/TodayAll.dc.html, design/mac/Week.dc.html, design/mac/YearPace.dc.html

One shared geometry helper draws two bars side by side that overlap (each 66% of the column width, overlap 34%, the shorter bar in front, a single device's bar full width). `HourlyBars`, `WeekColumns` and `PaceBars` use it in All scope and draw as today in This scope. Add the legend swatch "Overlapping = both on". Colours come from the device tokens (ticket 10).

Done when: unit tests cover the geometry (both devices, one device, equal heights, zero); the three controls match the canvas boards (Today, Week, Year Pace) in All scope and are unchanged in This scope.
