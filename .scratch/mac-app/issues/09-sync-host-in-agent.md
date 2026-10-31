# SyncHost in the Agent

Status: ready-for-agent
Spec: ../spec.md · Plan: ../plan.md (phase 2)
Blocked by: 08

A `SyncHost` in the Agent: write this device's snapshot on a timer (default every 5 minutes while sessions change), on quit and on a "Sync now" signal; watch the sync folder and import other devices' files; back off on errors; log lines only. The Agent stays the only writer of `sessions` (the importer runs there). The sync folder is the per-device `sync.folder` setting; sync does nothing while `sync.enabled` is off or the folder is missing. If no other device's file appears within about 10 minutes of enabling sync, record a "no other device found" state for the UI.

Done when: two installs (or one install plus a fixture file in the folder) exchange data; turning sync off, removing the folder, or an unreadable folder leaves the app working; the Agent's idle budgets in spec section 12 still hold (check memory and CPU).
