# DeviceScope in UsageContext and AppGroups

Status: resolved
Spec: ../spec.md · Plan: ../plan.md (phase 1)
Blocked by: 03, 04

Thread a device scope through the usage queries the way `GamesOnly` is threaded (decision 150): `UsageContext.DeviceScope` (`ThisDevice` or `AllDevices`), `UsageSnapshotLoader` reads `ui.deviceScope`, and `AppGroups.Load(..., deviceScope)` keeps only the apps of the local device in This scope. Merged groups keep their Main app's flags (decision 129). The nine queries pass the scope without repeating filtering logic.

Done when: with one device the suite is unchanged; with `AllDevices` and a second device's apps in the database the queries include them; a test shows the Games filter and the device scope combine.

## Answer

- Added `DeviceScope` property to `UsageContext` (defaulting to `DeviceScope.This`).
- Updated `UsageSnapshotLoader` to read `Setting.DeviceScope` (`ui.deviceScope`) and propagate it to `UsageContext`.
- Updated `IUsageSource.Apps` and `SqliteUsageSource` to take `DeviceScope`.
- Updated `AppGroups.Load` to filter apps by local `device_id` when `deviceScope == DeviceScope.This`, while returning all apps when `deviceScope == DeviceScope.All`.
- Updated `UsageWindow` to query `Apps` and `AllApps` with `Context.DeviceScope`.
- Added `DeviceScopeQueryTests.cs` verifying:
  - In `This` scope, only local apps are returned.
  - In `All` scope, apps from all devices are included.
  - Games filter and device scope combine properly (e.g. `This` + `GamesOnly` returns only local games; `All` + `GamesOnly` returns games across all devices).
  - All existing query suites pass without modification.
