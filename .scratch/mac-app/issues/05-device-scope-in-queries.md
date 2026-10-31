# DeviceScope in UsageContext and AppGroups

Status: ready-for-agent
Spec: ../spec.md · Plan: ../plan.md (phase 1)
Blocked by: 03, 04

Thread a device scope through the usage queries the way `GamesOnly` is threaded (decision 150): `UsageContext.DeviceScope` (`ThisDevice` or `AllDevices`), `UsageSnapshotLoader` reads `ui.deviceScope`, and `AppGroups.Load(..., deviceScope)` keeps only the apps of the local device in This scope. Merged groups keep their Main app's flags (decision 129). The nine queries pass the scope without repeating filtering logic.

Done when: with one device the suite is unchanged; with `AllDevices` and a second device's apps in the database the queries include them; a test shows the Games filter and the device scope combine.
