# Usage loader and palette slot

Status: ready-for-human

Two deepenings from the architecture review of 2026-10-27. Delivered as two commits on `master`, loader first. No new branch.

## Part 1: one Session loader for every usage query

`UsageLoader` is meant to be the one apps/sessions loader (decision 121), but `AllQuery` and `AppDetailQuery` still run their own `SELECT ... FROM apps` / `FROM sessions` and re-derive Main-app grouping and the open-Session end rule.

### Decisions

1. `UsageApp` gains the app's state and retired timestamp. `UsageLoader.LoadApps` takes a flag to include Retired apps; the default stays tracked only (Today, Month, Year, Wrapped, Matrix, AppDetail).
2. An alias whose Main app is retired or absent is its own group everywhere (the loader's current rule, `UsageLoader.cs:35`). `AllQuery` changes: such an alias no longer folds into the retired Main app's row. Add an All test pinning this.
3. `AllQuery` loads segments once with Retired apps included, derives the rows from them, and filters to tracked groups for the PC uptime footer. No second pass over `sessions`.
4. `AppDetailQuery` calls the loader and keeps the segments whose `Group` is its app. No loader parameter.
5. The open-Session end rule (`running is null || running.Contains(appId)` gives `now`, else clamp to `last_seen_utc`) exists once, in the loader.

### Tests

- New loader tests: Main-app grouping, retired-Main fallback, open Session with and without a running list, sleep end reason, the Retired flag.
- `AllQueryTests` and `AppDetailQueryTests` stay as behaviour tests. Remove a rule case from them only where a loader test covers it.
- All results other than the alias-of-retired case must not change.

### Done when

`AllQuery` and `AppDetailQuery` contain no `FROM apps` / `FROM sessions` SQL, and the suite passes.

## Part 2: palette slot and heat scale

### Decisions

1. New Core module `AppPalette`: one function from (Main app id, theme colour count) to a palette slot. Core does not read the theme; the count is a parameter.
2. One UI helper returns the brush resource key (`"Brush.App" + slot`) for an app id, reading the count from the current theme. It replaces the `"Brush.App" + ...` expressions in Today, Week, Month, All, AppDetail and the Year views. Lookups stay DynamicResource-style (decisions 33, 114).
3. Every view colours by app, including the Year matrix, pace and wrapped views, which currently colour by row position with a hard-coded 6. Add `AppId` to those row records where missing. Decision 34 allows 4 to 8 colours; `MatrixGrid`'s `PaletteSlot` takes the slot from the app.
4. Add app and the untracked card keep the exe-path hash (`LetterTile.PaletteIndex`) because the app has no id yet. Known quirk: its colour changes once tracked. Out of scope.
5. `MonthQuery.HeatLevel` and `WeekQuery.HeatLevel` become one function (same 1/3/5/7 h steps). `MatrixQuery.Level` and `AppDetailQuery.Level` are peak-relative ladders and stay where they are.

### Tests

- `AppPalette`: wraps for counts 4 to 8, count 1, negative-safe, same id gives the same slot.
- Heat function: step boundaries, tested once.
- Year views: a test that colours follow the app, not the row order, for a 4-colour and an 8-colour theme where practical without WPF (slot function level).

### Done when

No view contains `% 6` or `Range(0, 6)` for the palette, the `"Brush.App" +` expression exists in one place plus the exe-path hash callers, and the suite passes.

## Glossary

Add **Palette slot** to `GLOSSARY.md` (done with this spec).

## Not in scope

Moving DB writes behind IPC (decisions 49, 123). Candidates 2, 4, 5, 6 of the review.
