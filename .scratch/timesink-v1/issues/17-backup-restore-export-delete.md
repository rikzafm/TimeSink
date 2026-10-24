# 17: Backup, restore, export and delete

**What to build:** Automatic and manual backups with retention, Replace and Merge restore dialogs, export of all data, and Delete all history.

**Blocked by:** 04, 16

**Status:** resolved

- [ ] Backup then Replace restore round-trips
- [ ] Merging the same backup twice changes nothing
- [ ] Newer-schema backups are refused and older ones migrated
- [ ] Retention keeps 7 daily plus one per month
- [ ] Delete requires typing DELETE

## Answer

Implemented and merged on integration/timesink-v1; see docs/decisions.md for deviations. Unchecked acceptance boxes were verified by tests or the implementer's live run, not by hand-tick.
