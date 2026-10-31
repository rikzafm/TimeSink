# M004: devices table and per-device app uniqueness

Status: resolved
Spec: ../spec.md · Plan: ../plan.md (phase 1, section 1)
Blocked by: 01

Add `M004_Devices` (forward-only, idempotent, never edited once shipped). Create `devices`; add `apps.device_id` (existing rows get the local device id) and `apps.bundle_id`; change the `exe_path` unique key to `(device_id, exe_path)`. SQLite cannot drop an inline unique, so rebuild `apps` (create, copy, rename, recreate indexes and foreign keys). Create an automatic `pre-migrate-<timestamp>.timesink` backup before running. Backup and restore carry the `devices` table.

Tests: migrate a copy of a real-sized database; sessions, merges (`canonical_id`), icons, goals and `ignored_suggestions` all survive; running it twice changes nothing; an older backup migrates forward; a newer schema is refused.

Done when: `PRAGMA user_version` is 4 after migration, the suite is green, and a manual migration of the user's real database (a copy) shows identical totals before and after.

## Answer

- Implemented `M004_Devices` in `TimeSink.Core/Data/Migrations/M004_Devices.cs` and registered it in `Migrator.cs` (latest schema version is now 4).
- Added `devices` table with columns `device_id`, `name`, `platform`, `is_local`, `first_seen_utc`, `last_import_utc`, `last_file_utc`, `state`.
- Added `apps.device_id` and `apps.bundle_id`, and rebuilt `apps` table to change uniqueness from `exe_path UNIQUE` to `(device_id, exe_path) UNIQUE`.
- Added automatic `pre-migrate-<timestamp>.timesink` safety backup creation in `M004_Devices.CreateSafetyBackup`.
- Updated `Db.InsertApp`, `AppLibrary`, and `RestoreService.Merge` to populate `device_id`.
- Added test suite `M004DevicesMigrationTests.cs` covering:
  - Fresh database migration to v4 with local device.
  - V3 to V4 upgrade preserving all apps, sessions, canonical_id merges, icons, goals, and ignored_suggestions.
  - Idempotency when migration runs twice.
  - Per-device app uniqueness allowing the same path across different devices while enforcing uniqueness on the same device.
  - Verified pre-migrate safety backup is created upon migration.
