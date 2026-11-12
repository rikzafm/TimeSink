# 07 Integration, docs and rollout

Status: ready-for-human
Spec: ../spec.md (Phase 6)
Blocked by: 02, 03, 05, 06

## Tasks
- Extend `shared/fixtures/sync-import.json` with "unchanged file" and "encryption error then fixed"; run in `SharedFixtureTests.cs` and the Swift fixture loader.
- Update `shared/sync-format.md` 5.2, `docs/manual-tests.md`, and add a `docs/decisions.md` entry (own connection, fingerprint).
- Manual run on real Mac + PC over OneDrive for 30 min: both `.tssync` timestamps advance, one PC import line per Mac change, no `export_failed`; repeat with the Mac popover open and an app-add running.
- Ship the PC build first, then the Mac build; the Mac needs the update for bug 1.

## Comments
