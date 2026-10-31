# TimeSink Sync Format and Device Model

Version: 1  
Status: Draft (Ticket 01, Phase 0)  
Companion to: `docs/decisions.md` (decisions 167–172), `spec.md`, `plan.md`

---

## 1. Overview and Principles

TimeSink sync allows two devices (e.g., a Windows PC and a macOS Mac) to share usage history without direct network connections, background servers, or cloud accounts.

1. **The live database is never synced.** Each device writes only its own export file to a shared directory (such as OneDrive, Google Drive, iCloud Drive, or Dropbox) and reads files written by other devices.
2. **Idempotent import.** Importing the same snapshot multiple times produces identical results without duplicate sessions.
3. **Union uptime with overlap counted once.** Headline totals represent true elapsed time when at least one device was active. Overlapping sessions across devices are counted once for headline uptime, while per-device totals and app times are reported alongside.
4. **Resilient isolation.** If sync is disabled, the folder is unmounted, or another machine goes offline, each device continues operating normally as an isolated instance.
5. **Strict privacy boundary.** No window titles, no full paths outside app roots, and no settings are shared across devices.

---

## 2. File Specification

### 2.1 File Name and Container

- File name: `<deviceId>.tssync`
  - Example: `3fa85f64-5717-4562-b3fc-2c963f66afa6.tssync`
- Format: Standard ZIP archive (identical packaging to `.timesink` backup archives).
- Archive contents:
  - `manifest.json`: JSON metadata and integrity hash.
  - `snapshot.db`: SQLite database holding a stripped export of only this device's apps and sessions.

### 2.2 Atomic Writes

To avoid partial reads by sync daemons or the other device:
1. Write snapshot contents to a temporary file: `<deviceId>.tssync.tmp`.
2. Flush and close the temporary file.
3. Atomically rename `<deviceId>.tssync.tmp` to `<deviceId>.tssync`.

Snapshots are written:
- Periodically on a background timer (default: every 5 minutes, only if sessions or apps have changed).
- Cleanly on application exit.
- Immediately when the user clicks **Sync now** in Settings.

---

## 3. Manifest (`manifest.json`)

The manifest contains metadata used to validate the snapshot before opening the database.

```json
{
  "format": 1,
  "schemaVersion": 4,
  "appVersion": "1.0.0",
  "deviceId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "deviceName": "MacBook Pro",
  "platform": "macos",
  "createdUtc": "2026-11-01T04:30:00Z",
  "sessionCount": 1425,
  "hash": "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855"
}
```

### Fields

| Field | Type | Description |
|---|---|---|
| `format` | integer | Sync specification format version (currently `1`). |
| `schemaVersion` | integer | SQLite schema version of `snapshot.db` (matches `PRAGMA user_version`, `4`). |
| `appVersion` | string | Display version of the writing application. |
| `deviceId` | string | UUID of the device that wrote the snapshot. |
| `deviceName` | string | User-visible device name (default: computer name). |
| `platform` | string | Operating system: `"windows"` or `"macos"`. |
| `createdUtc` | string | ISO 8601 UTC timestamp when the snapshot was generated. |
| `sessionCount` | integer | Total count of session records contained in `snapshot.db`. |
| `hash` | string | Lowercase hex SHA-256 hash of `snapshot.db`. |

---

## 4. Snapshot Database (`snapshot.db`)

The SQLite database inside the archive contains only tables and columns necessary for cross-device usage reporting:

### Tables Included
- `apps`: Only apps associated with this `device_id`. Includes `app_id`, `name`, `exe_path`, `bundle_id`, `device_id`, `is_background`, `kind`, and `icon_png` (capped to standard 256x256 app icons).
- `sessions`: Only sessions belonging to apps for this `device_id`.
- `schema_version`: Holds `version = 4`.

### Tables Excluded
- `settings`: Local configuration is never shared.
- `devices`: Managed locally by each device.
- Unrelated internal logs or caches.

---

## 5. Import and Ingestion Rules

### 5.1 Import Key and Deduplication

Sessions are keyed uniquely by:
`(` `device_id`, `bundle_id` *or* `exe_path`, `start_utc` `)`

- If a session already exists with the same key, it is updated only if the snapshot's end time or `last_seen_utc` is later.
- Because the live session on the writing device might still be open when exported, open sessions in `snapshot.db` are closed at `last_seen_utc` on import. If a subsequent snapshot shows the session continued, the end time expands accordingly.
- App records are matched by `(device_id, exe_path)` on Windows and `(device_id, bundle_id)` on macOS.
- Merged apps: Cross-device merges point `canonical_id` to the target app's ID. Importing a snapshot does not overwrite user merge links configured locally.

### 5.2 Edge Cases and Error Handling

1. **Hash mismatch**: If the SHA-256 hash of extracted `snapshot.db` does not match `manifest.json.hash`, the snapshot is considered corrupt or incomplete. It is ignored and logged.
2. **Newer format version**: If `format > 1` or `schemaVersion` is higher than supported, the file is ignored, a log entry is written, and the Devices screen shows **Update required**.
3. **Cloud placeholders (OneDrive / iCloud / Dropbox)**:
   - On macOS, OneDrive placeholders live in `~/Library/CloudStorage/OneDrive-...` with dataless file attributes.
   - Files marked as cloud placeholders (not yet downloaded locally) are not opened.
   - The Devices and sync screen displays status as **Pending**.
4. **Cloud conflict copies**:
   - Files matching sync conflict patterns (e.g., `<deviceId> (1).tssync`, `<deviceId> - Copy.tssync`, `<deviceId>-conflict-*.tssync`) are ignored and logged.
5. **Third device file**:
   - TimeSink supports exactly two active devices in the UI.
   - If a third device's `.tssync` file appears in the sync folder, it is not imported. The Devices and sync screen displays a note explaining the two-device limit.
6. **Self-snapshot**:
   - A device ignores its own `<deviceId>.tssync` file when scanning the sync folder.
   - If a snapshot carries this device's own `deviceId` but originates from an external file with conflicting hardware or installation timestamps (a cloned install), the local device generates a new UUID, updates its local settings, and logs a warning.
7. **Offline / Missing folder**:
   - If the sync directory is unreachable or deleted, the app continues operating offline without errors, retaining all previously imported data.

---

## 6. Schema Changes (Migration M004)

Migration `M004_Devices` updates `PRAGMA user_version` to `4` on both Windows (C#) and macOS (Swift).

### 6.1 `devices` Table

```sql
CREATE TABLE devices (
    device_id TEXT PRIMARY KEY,
    name TEXT NOT NULL,
    platform TEXT NOT NULL,          -- 'windows' or 'macos'
    is_local INTEGER NOT NULL,       -- 1 for current machine, 0 for remote
    first_seen_utc TEXT NOT NULL,
    last_import_utc TEXT,
    last_file_utc TEXT,
    state TEXT NOT NULL DEFAULT 'active' -- 'active' or 'forgotten'
);
```

### 6.2 `apps` Table Rebuild

SQLite cannot alter inline `UNIQUE` constraints (`exe_path COLLATE NOCASE UNIQUE`). Therefore, `M004` rebuilds the table:
1. Create `apps_new`:
   ```sql
   CREATE TABLE apps_new (
       app_id TEXT PRIMARY KEY,
       name TEXT NOT NULL,
       exe_path TEXT NOT NULL,
       bundle_id TEXT,               -- macOS CFBundleIdentifier
       device_id TEXT NOT NULL,      -- Foreign key / association to devices
       color_index INTEGER NOT NULL DEFAULT 0,
       is_game INTEGER NOT NULL DEFAULT 0,
       is_background INTEGER NOT NULL DEFAULT 0,
       kind INTEGER,
       canonical_id TEXT,
       retired_utc TEXT,
       created_utc TEXT NOT NULL,
       FOREIGN KEY (canonical_id) REFERENCES apps_new(app_id)
   );
   CREATE UNIQUE INDEX idx_apps_device_exe ON apps_new(device_id, exe_path COLLATE NOCASE);
   CREATE INDEX idx_apps_device_bundle ON apps_new(device_id, bundle_id);
   CREATE INDEX idx_apps_canonical ON apps_new(canonical_id);
   ```
2. Copy existing apps, assigning `device_id = <local_device_id>`.
3. Drop old `apps` table and rename `apps_new` to `apps`.
4. Recreate dependent foreign keys and indexes.

Before running migration `M004`, an automatic backup `pre-migrate-<timestamp>.timesink` is written to the backup folder.

