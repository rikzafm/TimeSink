# SwiftUI Settings and Devices and sync

Status: resolved
Spec: ../spec.md · Plan: ../plan.md (phase 6)
Blocked by: 22, 24
Design: design/mac/Settings.dc.html, design/mac/Sync.dc.html
Needs a Mac.

The grouped Settings page (General incl. open at login, Tracking, Appearance, Devices and sync, Backup and data, About) and the Devices and sync screen: sync switch, per-device sync folder picker (`NSOpenPanel`), device name, device cards with the states from ticket 16, "Sync now", forget device, and "Delete this device's history…" with a typed `DELETE` confirmation. Backup, restore, export and delete-all behave as on Windows.

Done when: the screens match the canvas boards "Settings" and "Devices and sync", and a Windows `.timesink` backup restores on the Mac.
