# 02: Flyout spike

**What to build:** Clicking the tray icon opens a stub flyout flush against the taskbar edge on the correct monitor (any edge, mixed DPI), and it closes on deactivate without reopening on the closing click.

**Blocked by:** 01

**Status:** ready-for-agent

- [ ] Flyout appears next to the icon on a second monitor and clamps inside the work area
- [ ] Rapid tray clicking causes no reopen flicker
- [ ] Single instance of Agent and UI; clicking while open toggles it closed
- [ ] First paint under 300 ms is logged as FlyoutShown ms=
