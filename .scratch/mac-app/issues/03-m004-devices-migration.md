# M004: devices table and per-device app uniqueness

Status: ready-for-agent
Spec: ../spec.md · Plan: ../plan.md (phase 1, section 1)
Blocked by: 01

Add `M004_Devices` (forward-only, idempotent, never edited once shipped). Create `devices`; add `apps.device_id` (existing rows get the local device id) and `apps.bundle_id`; change the `exe_path` unique key to `(device_id, exe_path)`. SQLite cannot drop an inline unique, so rebuild `apps` (create, copy, rename, recreate indexes and foreign keys). Create an automatic `pre-migrate-<timestamp>.timesink` backup before running. Backup and restore carry the `devices` table.

Tests: migrate a copy of a real-sized database; sessions, merges (`canonical_id`), icons, goals and `ignored_suggestions` all survive; running it twice changes nothing; an older backup migrates forward; a newer schema is refused.

Done when: `PRAGMA user_version` is 4 after migration, the suite is green, and a manual migration of the user's real database (a copy) shows identical totals before and after.
