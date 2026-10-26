# AppCatalogue: deepen the Add-app workflow

Status: ready-for-agent

Outcome of an architecture review plus a grilling session (2026-10-27). Vocabulary: `GLOSSARY.md` (Tracked app, Retired app, Main app, Merged app, Candidate) and the codebase-design terms (module, interface, seam, adapter).

## Problem

The rules of the Add-app flow live in the four-file partial class `src/TimeSink.UI/Views/AddAppView.*.cs` and in `UntrackedCardView.cs`, where they can only be reached through WPF:

- Merge-Candidate assembly (`MergeCandidates`) and merge confirmation (`ConfirmMerge`): unmerge unticked, restore a Retired app, add-then-merge an installed one, icon fallback.
- Merge picking: ticked keys, add/remove counts, "starts tracking" note.
- Per-visit state: `_session`, `_addedElsewhere`, the "Added N" count, rows tracked this visit staying on screen as "Tracking".
- The running-apps recipe (excluded list, window capture, `RunningApps.Select`, icons), copied in `AddAppView.Refresh` and `UntrackedCardView`.
- About 20 `try { using var lib = AppLibrary.Open(path); ... } catch { return; }` sites that swallow every exception.

`AppLibrary` (SQLite adapter) is well tested; none of the rules above are.

## Decisions

1. **New module `AppCatalogue` in `Core/Apps`**, opened once per Add-app visit with the DB path and the seam below. `AppLibrary` is unchanged and stays beneath it as the SQLite adapter.
2. **`UntrackedCardView` shares it**: running Candidates come from one path.
3. **One seam, two adapters** (real Win32 and a fake for tests) covering: running-window capture (`RunningWindows.Capture`), icon extraction (`IconService.ExtractPng`) and the installed-apps scan (`InstalledApps`). `AppLibrary` is not faked; tests use a temp SQLite DB.
4. **Outcomes, not swallowed exceptions**: operations return an outcome (done, or database busy/missing). Only `SqliteException` and `IOException` count as busy/missing; other exceptions propagate. The view keeps today's behaviour (button stays "Track" so the user can retry).
5. **Per-visit state lives in the module**: tracked-this-visit rows, added-elsewhere set, the "Added N" count.
6. **Merge picking is a merge plan owned by the module**: open it for a Main app, get Candidates, toggle by key, read a summary (adds, removes, starts tracking), confirm. The view draws checkboxes and the button from the summary.
7. **Snapshots, not view-held lists**: the module returns immutable snapshots (tracked, retired, Candidates) and caches the slow installed-apps scan lazily inside itself. Candidates carry PNG bytes when known; background tile loading stays in the view.
8. **Pass-through edits** (Rename, SetKind, SetBackground, Retire, Restore, MakeMain, Unmerge, ClearHistory) go through one "apply change" intent with a small set of change kinds, returning the same outcome, so the views never open `AppLibrary`.
9. **Out of scope**: `UpgradeTrackedIcons` and the static `_hiResIcons` / `_trackedIconsHiRes` caches stay in the UI; only a track-time PNG goes through the seam. `SteamAutoTracker` and `AppIcons` keep calling `AppLibrary` directly. `AddAppView.Browse.cs` only loses its direct `AppLibrary.Open`.

## Tests

- Existing Apps tests (`AppLibraryTests`, `TrackedTabTests`, `RetireRestoreTests`, `BrowseTests`, `InstalledAppsTests`, `RunningAppsTests`, `TodayEarlyTests`) stay; they cover the adapter and pure helpers and nothing they test moves.
- New tests cross `AppCatalogue`'s interface only, with a temp SQLite DB and the fake adapters: merge-plan combinations, a Retired app re-added then merged, the Windows-directory filter, tracked-this-visit carry-over, busy/missing outcomes.

## Order of work

Four slices, each leaving the app working: see `issues/01` to `issues/04`.

## Not decided here

The exact method names and types of `AppCatalogue`'s interface. Design them at slice 01, and use the design-it-twice pattern if the shape is contested.
