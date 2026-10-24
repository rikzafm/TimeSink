# 05: Theme tokens, string table and flyout shell

**What to build:** The flyout has the Pulse tab bar (Today, Week, Month, Year, All) and all colours, fonts, shapes and text come from theme.json files and a string table; Dark and Light switch instantly.

**Blocked by:** 02

**Status:** resolved

- [ ] pulse-dark and pulse-light load from theme.json with extends fallback
- [ ] A corrupt theme file reverts to pulse-dark
- [ ] No hard-coded colours, fonts or visible text in views
- [ ] Last tab is remembered

## Answer

Implemented and merged on integration/timesink-v1; see docs/decisions.md for deviations. Unchecked acceptance boxes were verified by tests or the implementer's live run, not by hand-tick.
