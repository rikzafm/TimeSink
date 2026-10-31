# Merge screen with a tab per device

Status: ready-for-agent
Spec: ../spec.md · Plan: ../plan.md (phase 3)
Blocked by: 07, 10
Design: design/mac/Merge.dc.html, design/mac/AppSettings.dc.html

The merge screen "Merge into “X”" gets one underline tab per known device (with a count badge of ticked apps), the existing Tracked / Untracked / All filter inside each tab, one shared preview line ("Chrome (124 h) + chrome.exe (159 h) = 283 h under “Chrome”") and the "Suggested: same name on other device" banner with a "Tick match" button. Per-app settings › Merged apps lists apps of other devices with a device marker and an unmerge button. Ticks on other tabs stay when switching tabs.

Done when: merging a Windows app with a Mac app works end to end in the UI, the screen matches the canvas boards "Merge into app" and "Per-app settings", and unmerge restores the separate rows.
