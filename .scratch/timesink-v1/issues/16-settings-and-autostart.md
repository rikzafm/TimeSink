# 16: Settings page and autostart

**What to build:** A single grouped Settings page with the sections in the design, a working Start with Windows toggle, Dark/Light/System, Lime/Blue/Pink accents and a live Agent footprint in About.

**Blocked by:** 05

**Status:** resolved

- [ ] All rows render from one settings catalogue and persist
- [ ] Start with Windows writes and removes the HKCU Run entry
- [ ] Theme and accent apply instantly
- [ ] No Check for updates row

## Answer

Implemented and merged on integration/timesink-v1; see docs/decisions.md for deviations. Unchecked acceptance boxes were verified by tests or the implementer's live run, not by hand-tick.
