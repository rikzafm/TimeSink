# 02 Merge plan

Status: ready-for-agent
Spec: `../spec.md`
Blocked by: 01

## What to build

Move merge-Candidate assembly, picking and confirmation out of `AddAppView.Tracked.cs` into `AppCatalogue`.

- Open a merge plan for a Main app: Candidates are other Tracked apps, apps already merged into it (listed ticked), Retired apps with history (minus trivial ones and Windows-directory processes), and installed apps not already known. Sorted by name.
- Toggle a Candidate by key; read a summary: adds, removes, starts-tracking.
- Confirm: unmerge unticked Merged apps; for ticked ones restore a Retired app, or add-then-merge an installed one (icon fallback through the seam); record apps added elsewhere.
- View (`BuildMergeScreen`, `MergeTargetRow`, `ConfirmMerge`) renders from the plan and no longer holds `_mergePicked`.

## Acceptance criteria

- [ ] Tests through `AppCatalogue`: all four Candidate kinds, unmerge by unticking, a Retired app re-added then merged, an installed app added then merged, Windows-directory filter, summary counts and starts-tracking flag.
- [ ] Confirm failing midway reports an outcome and does not leave the view in a changed state.
- [ ] Merge screen behaves as before (manual).
- [ ] Build and full test suite pass.
