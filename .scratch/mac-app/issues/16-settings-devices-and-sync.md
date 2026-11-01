# Settings: Devices and sync

Status: resolved
Spec: ../spec.md · Plan: ../plan.md (phase 3)
Blocked by: 09
Design: design/mac/Settings.dc.html, design/mac/Sync.dc.html (both still have the older sub-screen header; use the Windows back-chevron header)

A "Devices and sync" section in the settings catalogue and a Devices and sync screen: a Sync switch (`sync.enabled`), the per-device sync folder with a Change button (each device picks its own path), "This device is called" (`device.name`), device cards with status (Up to date, Synced, Pending, Only two devices are supported yet, No other device found after 10 minutes with the hint that the two devices may point at different folders), last sync time, "Sync now", and "Merge…" banner when apps exist on both devices. Forget a device (stops syncing, keeps history). "Delete this device's history…" is a separate action and needs typing `DELETE`. Nothing is deleted by any other control.

Done when: the screen matches the canvas boards "Settings" and "Devices and sync", every state above can be reached with fixture files, and no action deletes data without the explicit confirmation.
