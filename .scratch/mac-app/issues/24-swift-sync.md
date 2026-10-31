# Swift sync writer and reader

Status: ready-for-agent
Spec: ../spec.md · Plan: ../plan.md (phase 7)
Blocked by: 08, 18
Needs a Mac.

Implement `shared/sync-format.md` in Swift: atomic snapshot writer, reader and import (idempotent, keyed by `(device_id, bundle_id or exe_path, start_utc)`), a folder watcher (FSEvents) and a timer, placeholder detection for `~/Library/CloudStorage/OneDrive-...` (show Pending), conflict copies and a third device's file ignored, the 10-minute "no other device found" state, per-device folder choice. Same rules as ticket 08; the fixtures are shared.

Done when: the shared sync fixtures pass in Swift, and a snapshot written by the C# writer imports on the Mac and the reverse, with identical numbers.
