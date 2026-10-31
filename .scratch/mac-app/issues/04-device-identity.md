# Device identity and device settings

Status: ready-for-agent
Spec: ../spec.md · Plan: ../plan.md (phase 1)
Blocked by: 03

Create the local device at first run: a UUID stored in `settings` (`device.id`) and a name (`device.name`, default the machine name), plus a `devices` row with `is_local = 1` and the platform. Add settings `sync.enabled` (default off), `sync.folder` (per device, empty by default) and `ui.deviceScope` (`This`/`All`, global). If a snapshot arrives carrying this device's own id from another machine (a cloned install), the second device regenerates its id and warns.

Done when: tests cover first run, restart (same id), rename, and the duplicate-id case; no screen changes yet.
