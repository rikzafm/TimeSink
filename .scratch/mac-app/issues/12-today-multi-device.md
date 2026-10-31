# Today for multiple devices

Status: ready-for-agent
Spec: ../spec.md · Plan: ../plan.md (phase 3)
Blocked by: 06, 11
Design: design/mac/Main.dc.html, design/mac/TodayAll.dc.html

Today in All scope: caption "· All devices", header total is the union uptime, one summary line ("Mac 6h 12m · PC 4h 30m · off 12:58 · 2h 05m on both"), overlapping hourly bars with the "now" marker and legend. Period chevrons: small left and right triangles around the caption using the existing `PeriodNavigator`, the right one dimmed on the latest day. Expanded app row: `SessionDial` gets concentric rings (outer ring first device, second ring second device, inner ring total share, centre % vs the 7-day average) and a stats column with each device's time and the overlap; the row's strip shows one thin strip per device.

Done when: Today in This scope is unchanged, All scope matches the canvas boards "Today · all devices" including the expanded Chrome row, and the view-model tests use fixture data.
