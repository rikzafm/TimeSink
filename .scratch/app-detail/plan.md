# App detail view (double-click an app in All)

Status: ready-for-agent
Design: (design link removed) (420 x 640, Pulse dark/light)

## Behaviour

- Double-clicking a tracked row in the All table opens a full-flyout detail page for that app. Single click keeps selecting the row.
- The page shows: Back, the app's own icon + name + `exe` + tracked-since, a year switcher, three tiles (Last opened, Avg session,
  Times opened) and a year grid of 12 mini-month calendars coloured relative to that app's busiest day.
- Icon: the app's own stored/extracted icon via `AppIcons` (`TryFill`); coloured letter tile only while none exists. TimeSink's own row uses its chosen icon (already handled in `AppIcons.Load`).
- Stats are lifetime for the app (merged apps count under the canonical app, retired apps cannot be opened). The grid follows the year switcher, from the year tracking began to the current year.

## Core (`TimeSink.Core`)

1. `Usage/AppDetailResult.cs`: `AppDetailResult` (AppId, Name, ExeName, FirstTracked, LastOpened?, TotalSeconds, SessionCount, AverageSessionSeconds, LongestSessionSeconds, Year, FirstYear, LastYear, YearSeconds, PeakDaySeconds, Days) and `AppDetailDay` (Date, Seconds, Sessions, Level, Kind: Tracked | BeforeTracking | Future). `LastOpened` = start, length, still running.
2. `Usage/AppDetailQuery.cs`: same rules as `AllQuery` (decision 56: DST-aware clipping to local days, open session counts to `now` if running or `running` is null, otherwise to its heartbeat). One session = one "opening" however many days it spans; a day's session count is the sessions touching that day.
   `Level(seconds, peak)`: 0 when none, else 1-4 by quarters of the peak day.
3. `Theming/AppDetailStringKeys.cs` + both `themes/*/theme.json` (English copy lives in themes, spec 8.1).

## UI (`TimeSink.UI`)

4. `Controls/MonthCells.cs`: draws one month (7 x 6 cells, 10 px, 2 px gap, week start from `week.start`), brushes bound to `Brush.Heat0..4`/`Brush.Border` outline for untracked days, raises `HoverChanged(AppDetailDay?)`.
5. `Views/AppDetailView.cs`: code-built like `AllView`; header, tiles, grid card, hover readout, Less/More legend, footnote. Colours/fonts only from theme resources. Loads off-thread, refreshes every 30 s and on `LiveState.Changed`; icon redraws on `AppIcons.Updated`.
6. `AllView`: `MouseLeftButtonDown` with `ClickCount == 2` calls an `openDetail(appId)` callback; `FlyoutWindow` passes one that sets `Overlay.Content` (like Add app) and clears it on Back.

## Tests / docs

- `AppDetailQueryTests` on `MonthFixture` (hand-computed): totals, session count, avg, longest, last opened, a midnight-spanning session, merged app, year bounds, levels, before-tracking/future kinds.
- `AppDetailStringTests` (both themes define every key); `Level` unit tests.
- `docs/decisions.md` row, `docs/manual-tests.md` steps.

## Review gate

After building: `dotnet build`, `dotnet test`, then compare the running page to the design above (layout, copy, icon, colours, both themes) and list any deviation.

## Out of scope

Keyboard-only entry (Enter on a row), retired-app detail, per-day drill-down.

## Review against the design (done)

Rendered the real `AppDetailView` (dark and light) to PNG from a scratch harness and compared with the board.

Matches: layout order, Back + year pill, 40 px app icon (the app's own icon, e.g. Notepad), three tiles with sub-lines, 4 x 3 mini-month grid with month hours, Less-More legend, hover readout row, footnote, both themes.
Differences: heat uses the theme's five `Brush.Heat*` steps (the board's light palette was a guess); tile values use 18 px literal; page height follows the flyout (620) so the grid card flexes.
Found and fixed in review: reused readout element threw on the first redraw.
Not exercised automatically: the double-click on a row and hover readout (manual-tests.md section "App detail").
