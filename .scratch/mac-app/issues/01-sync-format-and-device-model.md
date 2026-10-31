# Write the sync format and device model

Status: ready-for-agent
Spec: ../spec.md · Plan: ../plan.md (phase 0)
Blocked by: none

Write `shared/sync-format.md`: file name `<deviceId>.tssync` (zip with `manifest.json` and a stripped `snapshot.db`), manifest fields, atomic write (`.tmp` then rename), import key `(device_id, bundle_id or exe_path, start_utc)`, open sessions closed at `last_seen_utc`, how a mismatched hash, newer `format`, OneDrive placeholder, conflict copy or third device's file is handled (ignored, logged, shown on the Devices screen), and that no window titles or settings are shared. Describe the `devices` table and the `apps.device_id` and `apps.bundle_id` columns from plan.md section 1.

Add `docs/decisions.md` entries: multi-device scope; headline numbers are uptime with overlap counted once; device identity (UUID in `settings`); Principle 3 relaxed to opt-in folder sync with no sockets in the apps; two devices for now; nothing deleted unless explicitly requested. Add Device, Device scope, Snapshot and On both to `GLOSSARY.md`.

Done when: both documents are written, reviewed by the user, and the decisions are numbered in `docs/decisions.md`. No code changes.
