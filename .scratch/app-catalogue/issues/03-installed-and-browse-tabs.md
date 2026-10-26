# 03 Installed and Browse tabs

Status: ready-for-agent
Spec: `../spec.md`
Blocked by: 02

## What to build

Move `AddAppView.Installed.cs` onto `AppCatalogue`: track an installed app, Retire and Restore from the Installed tab, and installed-state lookups (`StateOf`), with the installed-apps scan cached lazily inside the module and shared with the merge plan. `AddAppView.Browse.cs` only loses its direct `AppLibrary.Open`; no rules move.

## Acceptance criteria

- [ ] Tests through `AppCatalogue`: installed list marks tracked and Retired apps; track/Retire/Restore return outcomes; the scan runs once per visit.
- [ ] The merge plan and the Installed tab share one scan.
- [ ] `AddAppView.Installed.cs` and `AddAppView.Browse.cs` contain no `AppLibrary.Open`.
- [ ] Installed and Browse tabs behave as before (manual).
- [ ] Build and full test suite pass.
