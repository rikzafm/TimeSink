# 01 Running Candidates, track/dismiss and per-visit state

Status: ready-for-agent
Spec: `../spec.md`

## What to build

Introduce `AppCatalogue` in `Core/Apps` with the seam and its real and fake adapters, and move the Running tab onto it.

- Seam: window capture, icon extraction, installed-apps scan (only capture and icon extraction are used in this slice; define the installed scan on the seam now so the fake is complete).
- Running Candidates from one path, used by both `AddAppView.Refresh` and `UntrackedCardView`.
- Track and dismiss with outcomes (done, or database busy/missing).
- Per-visit state: rows tracked this visit stay as "Tracking", the added-elsewhere set, the "Added N" count.
- `AddAppView.cs` (Running tab) and `UntrackedCardView.cs` stop calling `AppLibrary.Open` and `RunningWindows.Capture` directly.

## Acceptance criteria

- [ ] `AppCatalogue` tests (temp SQLite DB, fake adapters) cover: running Candidates exclude tracked and ignored apps; a tracked-this-visit row stays in the list; the count includes apps added elsewhere; busy/missing DB gives an outcome and leaves the row untracked.
- [ ] Only `SqliteException` and `IOException` are treated as busy/missing.
- [ ] Running tab and untracked card behave as before (manual: `docs/manual-tests.md` Add app section).
- [ ] Build and full test suite pass.
