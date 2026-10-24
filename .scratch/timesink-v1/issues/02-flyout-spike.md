# 02: Flyout spike

**What to build:** Clicking the tray icon opens a stub flyout flush against the taskbar edge on the correct monitor (any edge, mixed DPI), and it closes on deactivate without reopening on the closing click.

**Blocked by:** 01

**Status:** ready-for-agent

- [ ] Flyout appears next to the icon on a second monitor and clamps inside the work area
- [ ] Rapid tray clicking causes no reopen flicker
- [ ] Single instance of Agent and UI; clicking while open toggles it closed
- [ ] First paint under 300 ms is logged as FlyoutShown ms=

## Answer

Resolved in part. Built and merged (branch integration/timesink-v1). NOT met: first paint under 300 ms. Cold start measures about 450-850 ms; a pre-warmed standby flyout opens in about 120 ms (docs/decisions.md #31-38). Real mixed-DPI, non-bottom taskbar edges and a physical tray click were not exercised.
