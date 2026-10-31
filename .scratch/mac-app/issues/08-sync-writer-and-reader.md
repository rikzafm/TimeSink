# SyncWriter and SyncReader in Core

Status: ready-for-agent
Spec: ../spec.md · Plan: ../plan.md (phase 2)
Blocked by: 01, 03, 07

Implement `shared/sync-format.md` in C#. The writer produces `<deviceId>.tssync` atomically (`.tmp`, then rename) from this device's apps and sessions only. The reader imports another device's file through the existing Merge import (decision 52), keyed by `(device_id, exe_path or bundle_id, start_utc)`, closes open sessions at `last_seen_utc`, then runs `RebuildAll()`.

Rules: ignore a file with a bad hash, a newer `format`, an undownloaded placeholder (report Pending), a cloud conflict copy, and a third device's file (report "Only two devices are supported yet"). Never reorder anything by file time. Nothing is deleted.

Done when: fixtures pass for write then read, import twice changes nothing, partial file, placeholder file, third device ignored, clock skew, and a 100k-session snapshot imports in an acceptable time (record the number).
