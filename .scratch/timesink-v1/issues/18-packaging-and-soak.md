# 18: Packaging and soak

**What to build:** An Inno Setup per-user installer and a portable zip install, run and uninstall cleanly, and the budgets are met after a 24-hour soak.

**Blocked by:** 01-17 (all)

**Status:** ready-for-agent

- [ ] Clean-profile install and uninstall work including Run entry removal
- [ ] Agent stays within 15 MB and near 0% CPU after 24 h
- [ ] Manual checklist results recorded in docs/manual-tests.md
- [ ] README written

## Answer

Resolved in part. Build script, portable zip, Inno Setup script, README and blank docs/manual-tests.md are merged. NOT done: clean-profile install/uninstall, 24 h soak, manual checklist results, and the .iss has never been compiled (Inno Setup not installed). No LICENSE file exists.
