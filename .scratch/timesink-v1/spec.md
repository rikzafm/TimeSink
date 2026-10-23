Status: ready-for-agent

# TimeSink v1 — Spec

Triage label to apply when published: `ready-for-agent`. Detailed behaviour lives in `implementation.md`; visuals in `design/`; choices in `docs/decisions.md`.

## Problem Statement

I want to know how long my chosen applications (Spotify, Chrome, VS Code, games…) are open on my Windows PC, Steam-style, without surveillance, accounts, or a heavy background process. Existing trackers watch the focused window, record titles, upload data, or sit at hundreds of MB. I want totals for today, this week, month, year and lifetime that I can open instantly from the tray.

## Solution

A tiny always-running tray Agent records when apps I explicitly add are running (open time, not focus time), and a flyout opened from the tray shows Today, Week, Month, Year and All views in a polished "Pulse" design. Everything is stored locally in SQLite, survives sleep, shutdown and crashes without double counting, and can be backed up, restored, merged and exported.

## User Stories

1. As a user, I want the app to start with Windows, so that tracking never depends on me remembering.
2. As a user, I want only apps I add to be recorded, so that my privacy is respected.
3. As a user, I want never to have window titles, keystrokes, URLs or screenshots recorded, so that nothing sensitive is stored.
4. As a user, I want a tray icon, so that I can open the flyout in one click.
5. As a user, I want the flyout to appear next to the tray icon on the right monitor and taskbar edge, so that it feels native.
6. As a user, I want the flyout to close when I click elsewhere, so that it never gets in the way.
7. As a user, I want clicking the tray icon while the flyout is open to close it without it reopening, so that toggling is reliable.
8. As a user, I want the flyout to open in under 300 ms, so that it feels instant.
9. As a user, I want the tray process to use almost no memory or CPU, so that I forget it is there.
10. As a user, I want the tray icon to come back after Explorer restarts, so that tracking is never orphaned.
11. As a user, I want an app to count while any of its processes is alive, so that multi-process apps like Chrome count once.
12. As a user, I want time counted only from when I added the app, so that numbers are honest.
13. As a user, I want sleep and hibernate to pause counting, so that overnight time is not inflated.
14. As a user, I want to choose whether locked-screen time counts, so that it matches how I use my PC.
15. As a user, I want very short sessions ignored (5/15/30 s), so that flashes do not clutter the history.
16. As a user, I want crashes, forced shutdowns and clock changes never to corrupt data or produce negative or doubled time, so that totals are trustworthy.
17. As a user, I want sessions to split correctly at local midnight and across DST changes, so that daily numbers are right.
18. As a user, I want Store (MSIX) apps and portable apps to be trackable, so that nothing I use is excluded.
19. As a user, I want to see a live ticking total today with a "N running" indicator, so that I can see the clock moving.
20. As a user, I want an hourly chart with a typical-day dotted line, a now marker and a sleep-paused band, so that I understand my day.
21. As a user, I want First app, Longest session and vs-yesterday-by-now tiles, so that I get quick insights.
22. As a user, I want app rows sorted by today's time with status ("Running · 1h 02m", "Closed 11:12 am") and a 24-hour strip, so that I see each app's day.
23. As a user, I want to tap an app to see its sessions and a comparison with my 7-day average, so that I can dig in.
24. As a user, I want a "tracking since you signed in" caption early in the day, so that a low total makes sense.
25. As a user, I want running untracked apps listed with one-tap Track buttons and a "Not this one" dismissal, so that adding apps is effortless.
26. As a user, I want a Week view with seven columns, a 20-week heat grid, a streak and per-app weekly totals, so that I see my rhythm.
27. As a user, I want my streak to count any day an app ran and not break until a day ends empty, so that it feels fair.
28. As a user, I want a Month calendar heatmap with weekly totals, hero stats (total, delta over the same days, daily average, best day), a selected-day breakdown and top 4 apps with deltas, so that I can compare months.
29. As a user, I want to page between months and choose the week start day (Mon/Sun/Sat), so that it matches my habits.
30. As a user, I want a Year pace view with projections and an "on pace for N h by December" banner, so that I can see where the year is heading.
31. As a user, I want a Year Wrapped story and an app-by-month matrix, switchable from the Year tab, so that I can explore my year.
32. As a user, I want to save a Wrapped card as a PNG, so that I can keep or share it myself.
33. As a user, I want an All-time table (hours, days used, avg/day, share) with sortable headers, per-app details and a totals row, so that I see lifetime totals.
34. As a user, I want retired apps kept with history but excluded from totals, and restorable, so that stopping tracking is not destructive.
35. As a user, I want to export month or table data to CSV and all data to CSV/JSON, so that my data is mine.
36. As a user, I want to add apps from Running, Installed (A–Z with search and letter rail) and Browse (drop or choose an .exe/.lnk), so that any app can be added.
37. As a user, I want app icons extracted and cached with a letter-tile fallback, so that lists look right.
38. As a user, I want one grouped Settings page (General, Tracking, Appearance, Notifications, Backup and data, About), so that everything is in one place.
39. As a user, I want Dark, Light or System theme and Lime/Blue/Pink accents that apply instantly, so that the app fits my desktop.
40. As a user, I want compact lists, so that more apps fit.
41. As a user, I want optional goal/limit alerts and a weekly summary that only notify, so that I stay informed without being blocked.
42. As a user, I want automatic daily/weekly backups with retention (7 daily + 1 per month) and a Back up now button, so that I never lose history.
43. As a user, I want to restore a backup by Replacing or Merging, with Merge idempotent and a pre-restore safety copy, so that I can recover or combine machines.
44. As a user, I want Delete all history to need typing DELETE, so that it cannot happen by accident.
45. As a user, I want to see the Agent's real memory and CPU in About, so that I can verify it is light.
46. As a user, I want no network access, accounts, telemetry or update checks, so that the app stays private.
47. As a user, I want the UI to never hard-code colours, fonts, shapes or text, so that more themes can be added later as plain files.
48. As a user, I want times to follow my Windows 12/24-hour setting, so that they read naturally.
49. As a user, I want a per-user installer and a portable zip with no admin rights, so that installing is easy and clean uninstall is possible.

## Implementation Decisions

- Two processes: a Native AOT tray Agent (tracking, DB writer, backup scheduler, IPC server) and a short-lived WPF UI process spawned on tray click; single instance of each; named-pipe IPC with current-user-only access; UI reads the DB read-only.
- Tracking is a pure deterministic engine behind clock, process-provider, session-store, and power-event interfaces; one open session per app; 5 s tick, 30 s heartbeat; gap detection, crash recovery, sleep/lock/shutdown rules as in `implementation.md` §6.
- SQLite (WAL) is the single source of truth; daily_usage is a rebuildable cache; merged apps report by canonical id; retired apps excluded from totals; forward-only migrations.
- Usage queries (Today, Week with 20-week grid and streak, Month, Year with projections, All) live in Core and return plain result types the views bind to.
- Theming: token-driven `theme.json` (same format as future packages) for `pulse-dark`/`pulse-light`, per-theme string table, `layout` field accepting only `pulse` in v1; Dark/Light/System and accent presets map onto it; revert to `pulse-dark` on load failure.
- UI is plain WPF (no WPF-UI) with custom templated controls and chart primitives; no chart library.
- Toolchain: .NET 10, C# latest, CsWin32, Microsoft.Data.Sqlite, CommunityToolkit.Mvvm, xUnit + FluentAssertions; Inno Setup at the end.
- Behaviour decisions from the design review are recorded in `docs/decisions.md` (grouped Settings, Pulse Week, streak rule, no update check, Year switch, early-state rules, PNG save, English with Windows clock).

## Testing Decisions

- Good tests assert external behaviour only: given fake time, processes and power events, which sessions and daily totals end up stored; given a database, what a query returns. No assertions on internals or call order.
- One seam: Core's public surface (TrackingEngine + UsageAccumulator + query services + backup/restore/merge + theme loading) driven by fakes and an in-memory or temp SQLite file. UI and Agent stay thin and are checked by the manual checklist (§12.2) and budget measurements.
- Required cases are listed in `implementation.md` §12.1: single and multi-process sessions, midnight and DST splits, suspend/resume and gap fallback, lock/unlock, crash recovery, minimum session removal with subtraction, backwards clock, incremental equals full rebuild (property test), backup round-trip, idempotent merge, newer/older schema handling, theme.json loading and fallback.
- Prior art: none; greenfield repository.

## Out of Scope

Window-title or focus/idle tracking; cloud sync; accounts; telemetry; update checks; localisation beyond English; installable theme packages, validator, gallery, file association and the nine extra theme variants (post-v1); layout presets other than Pulse; per-app blocking; macOS/Linux; multi-user separation; code signing and winget.

## Further Notes

- Prerequisite: install the .NET 10 SDK and the VS C++ Build Tools before M0.
- Milestones M0–M7 in `implementation.md` §13 are the delivery order; M6 is deferred.
- Visual references: `design/` (Pulse boards) and `design/themes/` (post-v1 variants).
