# 01: Solution and tray Agent spike

**What to build:** A solution with Core, Agent, UI and Tests projects builds. The Agent publishes with Native AOT, shows a tray icon with an Open/Quit menu, opens and migrates the SQLite database, and re-adds its tray icon after Explorer restarts.

**Blocked by:** None (can start immediately; needs the .NET 10 SDK and C++ Build Tools installed)

**Status:** ready-for-agent

- [ ] `dotnet publish` of the Agent with PublishAot succeeds and the exe runs
- [ ] Tray icon with Open and Quit appears and survives an Explorer restart
- [ ] Database is created with the initial schema and a row can be inserted
- [ ] If AOT + SQLite cannot work, the trimmed fallback is recorded in docs/decisions.md
