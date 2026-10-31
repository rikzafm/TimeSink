# Device identity and device settings

Status: resolved
Spec: ../spec.md · Plan: ../plan.md (phase 1)
Blocked by: 03

Create the local device at first run: a UUID stored in `settings` (`device.id`) and a name (`device.name`, default the machine name), plus a `devices` row with `is_local = 1` and the platform. Add settings `sync.enabled` (default off), `sync.folder` (per device, empty by default) and `ui.deviceScope` (`This`/`All`, global). If a snapshot arrives carrying this device's own id from another machine (a cloned install), the second device regenerates its id and warns.

Done when: tests cover first run, restart (same id), rename, and the duplicate-id case; no screen changes yet.

## Answer

- Implemented `DeviceScope` enum (`This`, `All`) and `DeviceRecord` in `TimeSink.Core/Devices/`.
- Implemented `DeviceIdentity` in `TimeSink.Core/Devices/DeviceIdentity.cs`:
  - `GetLocalDevice`: creates/persists local UUID in `settings` (`device.id`) and name (`device.name`, defaulting to `Environment.MachineName`), creating `devices` row with `is_local = 1`.
  - `RenameLocalDevice`: renames local device in `settings` and `devices` table.
  - `HandleClonedDeviceConflict`: detects when an incoming snapshot matches the local `device.id`, regenerates a fresh UUID for the local machine, rebinds local apps, demotes the old record (`is_local = 0`), and returns a warning.
- Added settings to `Setting.cs`: `SyncEnabled` (`sync.enabled`), `SyncFolder` (`sync.folder`), `DeviceScope` (`ui.deviceScope`), `DeviceId` (`device.id`), and `DeviceName` (`device.name`).
- Added test suite `DeviceIdentityTests.cs` covering first run, restart (same id preserved), rename, and cloned duplicate-id regeneration. All tests pass.
