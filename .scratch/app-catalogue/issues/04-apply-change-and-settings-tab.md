# 04 Apply-change intent and the Settings tab

Status: ready-for-agent
Spec: `../spec.md`
Blocked by: 03

## What to build

Route the pass-through edits in `AddAppView.AppSettings.cs` and the Tracked tab (Rename, SetKind, SetBackground, Retire, Restore, MakeMain, Unmerge, ClearHistory) through one "apply change" intent on `AppCatalogue` with a small set of change kinds and the shared outcome. Tracked snapshots come from the module, so the view holds no `_tracked` or `_library` lists.

## Acceptance criteria

- [ ] Tests through `AppCatalogue`: each change kind applies and returns done; a busy/missing DB returns the outcome; a blank Rename is rejected as `AppLibrary` does today.
- [ ] No `AppLibrary.Open` remains under `src/TimeSink.UI/Views/AddAppView*` or in `UntrackedCardView.cs` (the other callers named in the spec are unchanged).
- [ ] Settings tab behaves as before (manual).
- [ ] Build and full test suite pass.
