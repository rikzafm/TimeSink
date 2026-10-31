# TimeSink for Mac: design boards

A snapshot of the "TimeSink for Mac" design canvas (https://claude.ai/artifact/Jbj4sP2kLmJX3HpCCX5KAt, private). Saved from the live canvas version `1793480538-a598`.

Each `*.dc.html` is one board. They are Design Component pages (`<x-dc>` markup plus a small `DCLogic` script) and need the canvas runtime (`support.js`) to render, so view them in the canvas rather than opening them directly. `canvas.json` holds the board positions, titles and order. The numbers in the boards are sample data.

Size of every board: 440 x 700 (a 28 px menu bar strip plus a 400 x 640 popover).

## Boards and the tickets that use them

| File | Board title | Tickets |
|---|---|---|
| `Main.dc.html` | Today · this Mac | 12, 21 |
| `TodayAll.dc.html` | Today · all devices | 11, 12, 21 |
| `Week.dc.html` | Week | 13, 21 |
| `Month.dc.html` | Month | 13, 21 |
| `YearPace.dc.html` | Year · Pace | 13, 21 |
| `YearWrapped.dc.html` | Year · Wrapped 1 Total | 13, 21 |
| `YearWrappedApps.dc.html` | Year · Wrapped 2 Top apps | 21 |
| `YearWrappedHabits.dc.html` | Year · Wrapped 5 Habits | 21 |
| `YearMatrix.dc.html` | Year · Matrix | 21 |
| `All.dc.html` | All (has a Row style tweak: icons or labels) | 14, 21 |
| `AppDetail.dc.html` | App detail (open from All) | 14, 21 |
| `AppSettings.dc.html` | Per-app settings (opened from Tracked) | 15, 22 |
| `Merge.dc.html` | Merge into app (a tab per device) | 15, 22 |
| `Add.dc.html` | Add app · Running | 22 |
| `AddApps.dc.html` | Add app · Installed | 22 |
| `AddTracked.dc.html` | Add app · Tracked | 14, 22 |
| `AddChoose.dc.html` | Add app · Browse | 22 |
| `Settings.dc.html` | Settings | 16, 23 |
| `Sync.dc.html` | Devices and sync | 16, 23 |

## Known gaps (as of this snapshot)

- These boards still use the older sub-screen header (a "‹ Back" bar) and should follow the Windows back-chevron header: `AppDetail`, `Settings`, `Sync`.
- Only two devices are drawn. A third device's snapshot is ignored by design.
- The All board has two row layouts behind the Row style tweak; the user has not chosen which one the Windows app currently uses.
- Year Matrix, Wrapped Top apps and Wrapped Habits show no device split by design.
- Wrapped Top games and Top gacha cards are not drawn; they reuse the Top apps podium layout.
- The Windows boards in `design/Pulse*.html` are older than the Windows app in some places (for example All). When a board and the running Windows app disagree, the Windows app wins.

## Conventions the boards follow

- Two device markers: this device's platform A is a green circle, B a blue square (colour is never the only cue).
- Overlapping hours: bars of both devices overlap side by side; the headline number counts overlap once.
- Period navigation uses small left and right triangles around the caption, the right one dimmed on the latest period.
- App tiles are dark squares with a coloured letter; the real app icon replaces the letter at runtime.
- Icon buttons on app rows: closed eye = Track, gamepad = game type, layers = Merge, eye = stop tracking, slashed eye = Not this one.
