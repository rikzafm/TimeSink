# TimeSink — Implementation Spec

Audience: an AI coding agent (and humans) implementing the app from scratch.
Read this whole file before writing code. Where this spec and your instincts disagree, follow the spec. Where the spec is silent, choose the simplest option that respects the **Principles** and note the choice in `docs/decisions.md`.

---

## 0. One-paragraph summary

TimeSink is a Windows desktop app that records, **Steam-style**, how long the user's chosen applications are *running* (not focused), and shows today / week / month / year / lifetime totals in a modern dropdown flyout from the system tray. It auto-starts at sign-in and must be extremely light when idle. It is **all C#**, split into a tiny always-running **Agent** (tray + tracking + database writer) and a **UI** process that only exists while the flyout is open.

## 1. Principles (non-negotiable)

1. **Lightweight first.** The always-running process must stay ≤ 15 MB private working set and ~0 % CPU when idle. Every dependency must justify its weight.
2. **Opt-in tracking.** Only apps the user explicitly adds are recorded. Never record anything else. Never record window titles, keystrokes, URLs, or screenshots.
3. **Local only.** No network access, no telemetry, no accounts. (Theme/online gallery is explicitly out of scope for v1.)
4. **Open time, not focus time.** An app counts while any of its processes is alive. No idle detection, no foreground hooks.
5. **Data safety.** The SQLite database is the single source of truth. Crashes, sleep, shutdown and restore must never corrupt it or double count time.
6. **Single user, multi-monitor, per-user install.** No admin rights required anywhere.
7. **No hard-coded colours/fonts/shapes/visible text in UI code.** Everything visual goes through theme tokens and everything user-visible through the string table (§9) from the first screen. Visible text is English in v1 but never inlined in views.

## 2. Target platform and toolchain

| Item | Decision |
|---|---|
| OS | Windows 11 (primary). Windows 10 22H2 supported with flat fallbacks (no Mica/backdrop). |
| Runtime | `net10.0` (LTS). The dev machine had no SDK at spec time: the user installs the .NET 10 SDK plus the Visual Studio C++ Build Tools (required by Native AOT) before M0. Inno Setup 6 is needed only at M7. |
| Language | C# latest, `Nullable=enable`, `TreatWarningsAsErrors=true`, file-scoped namespaces. |
| Agent | Native AOT (`PublishAot=true`), `OutputType=WinExe`, **no WPF/WinForms**. AOT-safe code only: no reflection, `System.Text.Json` source generators, `LibraryImport`/CsWin32 for P/Invoke. |
| UI | Plain WPF with our own control templates driven by theme tokens (**no WPF-UI**; the Pulse design is custom/flat), MVVM with `CommunityToolkit.Mvvm` source generators. Self-contained, ReadyToRun, **no trimming** (WPF trimming is unsupported). |
| Database | SQLite via `Microsoft.Data.Sqlite` (+ bundled native SQLite). WAL mode. |
| Win32 | `Microsoft.Windows.CsWin32` source generator for P/Invoke (AOT-friendly). |
| Tests | xUnit + FluentAssertions. |
| Installer | Inno Setup 6, per-user. Portable zip as second artifact. |

Shell: development happens on Windows; commands below use PowerShell.

## 3. Solution layout

```
timesink/
├─ implementation.md                 (this file)
├─ docs/decisions.md                 (append-only log of choices you made)
├─ src/
│  ├─ TimeSink.Core/                 net10.0, no UI, AOT-compatible
│  │  ├─ Data/                       schema.sql, Migrations/, Db.cs, repositories
│  │  ├─ Tracking/                   TrackingEngine, ISessionStore, IClock, IProcessProvider, IPowerEvents
│  │  ├─ Usage/                      UsageAccumulator, query services (Today/Month/Year/All)
│  │  ├─ Backup/                     BackupService, RestoreService, MergeService, CsvJsonExport
│  │  ├─ Ipc/                        Pipe protocol messages + client/server
│  │  ├─ Settings/                   SettingKeys, SettingsStore
│  │  └─ Theming/                    ThemeManifest, TokenCatalog, StringTable (ThemeValidator/ContrastLint: post-v1)
│  ├─ TimeSink.Agent/                net10.0-windows, PublishAot
│  │  ├─ Native/                     NativeMethods.txt (CsWin32), MessageWindow, TrayIcon
│  │  ├─ Win32ProcessProvider.cs, Win32PowerEvents.cs, FlyoutLauncher.cs, AutoBackupScheduler.cs
│  │  └─ Program.cs
│  ├─ TimeSink.UI/                   net10.0-windows, WPF
│  │  ├─ App.xaml(.cs), FlyoutWindow, Shell/, Views/, ViewModels/, Controls/, Theming/
│  │  └─ Services/                   IconService, InstalledAppsService, RunningAppsService, ThemeService
│  └─ TimeSink.Installer/            Inno Setup script + publish scripts
├─ tests/TimeSink.Tests/
├─ themes/                           built-in theme sources (pulse-dark, pulse-light, ...)
└─ design/                           approved Pulse boards (HTML); design/themes/ = post-v1 theme variants (see §14)
```

Project references: `Agent → Core`, `UI → Core`, `Tests → Core` (and `Agent`/`UI` where testable). Core must not reference WPF or Win32-only assemblies except through interfaces.

## 4. Process model

```
 sign-in ──► TimeSink.Agent.exe --background (HKCU Run key)
                │  tray icon, message-only window, tracking loop, DB writer, auto-backup
                │
 tray click ──► spawns TimeSink.UI.exe --anchor L,T,R,B --work L,T,R,B --dpi N
                │  reads DB read-only; talks to Agent over named pipe
                └─ exits when flyout deactivates / is dismissed
```

* **Single instance** per process type via named mutexes: `Local\TimeSink.Agent`, `Local\TimeSink.UI`.
* **Flyout toggle race:** clicking the tray icon deactivates the flyout, which closes it, and the same click would reopen it. The Agent records `lastUiClosedUtc` (from the UI's exit) and **ignores a tray click within 300 ms of it**.
* **If the UI is already running** when the tray is clicked: signal the named event `Local\TimeSink.UI.Toggle`; the UI closes itself.
* **Anchor geometry:** the Agent computes the tray icon rect with `Shell_NotifyIconGetRect` (using `hWnd`+`uID`, not GUID), picks the monitor with `MonitorFromRect`, and passes that monitor's **work area** to the UI. The UI positions the flyout flush against the taskbar edge (taskbar may be on any edge) and clamps it inside the work area. UI is PerMonitorV2 DPI-aware.
* **Agent resilience:** re-add the tray icon on the `TaskbarCreated` broadcast (explorer restart). Handle `WM_DPICHANGED`/display change by recomputing nothing persistent (rect is computed per click).

## 5. Data model (SQLite)

File: `%LocalAppData%\TimeSink\timesink.db`. Pragmas on open: `journal_mode=WAL`, `synchronous=NORMAL`, `foreign_keys=ON`, `busy_timeout=5000`. Schema version via `PRAGMA user_version`; migrations are idempotent, forward-only C# classes in `Core/Data/Migrations/` (`M001_Initial`, …). **Never edit a shipped migration.**

```sql
-- M001_Initial
CREATE TABLE apps (
  id            INTEGER PRIMARY KEY,
  canonical_id  INTEGER NULL REFERENCES apps(id),         -- merge target (Chrome Beta -> Chrome)
  exe_path      TEXT NOT NULL COLLATE NOCASE UNIQUE,      -- normalized full path (see §6.2)
  package_family TEXT NULL,                               -- for Store/MSIX apps
  display_name  TEXT NOT NULL,
  category      TEXT NOT NULL DEFAULT 'Other',
  state         TEXT NOT NULL DEFAULT 'tracked' CHECK (state IN ('tracked','retired')),
  added_utc     INTEGER NOT NULL,                         -- unix seconds
  retired_utc   INTEGER NULL
);

CREATE TABLE sessions (
  id            INTEGER PRIMARY KEY,
  app_id        INTEGER NOT NULL REFERENCES apps(id),
  start_utc     INTEGER NOT NULL,
  end_utc       INTEGER NULL,                              -- NULL while open
  last_seen_utc INTEGER NOT NULL,                          -- heartbeat
  pid           INTEGER NULL,
  proc_start_utc INTEGER NULL,                             -- process creation time (identity check)
  end_reason    TEXT NULL CHECK (end_reason IN ('exit','sleep','shutdown','crash','removed','restore'))
);
CREATE INDEX ix_sessions_app_start ON sessions(app_id, start_utc);
CREATE UNIQUE INDEX ux_sessions_app_start ON sessions(app_id, start_utc);  -- merge idempotency

-- Materialized, rebuildable from sessions (see §6.5)
CREATE TABLE daily_usage (
  app_id  INTEGER NOT NULL REFERENCES apps(id),
  day     TEXT NOT NULL,                                   -- local date 'yyyy-MM-dd'
  seconds INTEGER NOT NULL,
  PRIMARY KEY (app_id, day)
) WITHOUT ROWID;

CREATE TABLE icons    (app_id INTEGER PRIMARY KEY REFERENCES apps(id), png BLOB NOT NULL, hash TEXT NOT NULL);
CREATE TABLE settings (key TEXT PRIMARY KEY, value TEXT NOT NULL);
CREATE TABLE goals    (app_id INTEGER NULL REFERENCES apps(id), kind TEXT NOT NULL CHECK (kind IN ('limit','target')),
                       seconds_per_day INTEGER NOT NULL, PRIMARY KEY (app_id, kind));
CREATE TABLE ignored_suggestions (exe_path TEXT PRIMARY KEY COLLATE NOCASE, utc INTEGER NOT NULL);
```

**Rules**
* All timestamps are **UTC unix seconds**. "Day" is computed in the user's **local** time zone.
* Merged apps: reports group by `COALESCE(canonical_id, id)`. Sessions always keep the real `app_id`.
* "Retired" apps keep history but are **excluded from all totals** and from the library's main list (shown in a separate "Retired" section; Restore flips `state` back to `tracked`).
* "Combined app time" in the UI is the **sum across apps** (time can exceed wall-clock). Label it as such.
* Only the Agent writes `sessions`/`daily_usage`. The UI may write `apps`, `goals`, `settings`, `ignored_suggestions` and then sends `reloadApps` to the Agent.

### 5.1 Settings keys (defaults)

| Key | Type | Default | Meaning |
|---|---|---|---|
| `startup.enabled` | bool | `true` | Writes/removes the HKCU Run entry |
| `week.start` | `Mon\|Sun\|Sat` | `Mon` | |
| `tray.click` | `Flyout\|Window` | `Flyout` | v1 implements `Flyout` only |
| `tracking.pauseOnSleep` | bool | `true` | |
| `tracking.countWhenLocked` | bool | `true` | |
| `tracking.minSessionSeconds` | `5\|15\|30` | `15` | sessions shorter than this are deleted on close |
| `tracking.suggestApps` | bool | `true` | |
| `ui.theme` | `Dark\|Light\|System` | `System` | `System` follows Windows and resolves to `pulse-dark` / `pulse-light` |
| `ui.accent` | `Lime\|Blue\|Pink` | `Lime` | accent preset (Lime `#c6f26b`); maps to a colour override on the active theme |
| `ui.lastTab` / `ui.lastYearView` | string | `Today` / `Pace` | remembered tab and Year sub-view (`Pace\|Wrapped\|Matrix`) |
| `ui.compact` | bool | `false` | |
| `ui.icon` | string | `comic.ticks` | App icon id, `style.motif` (decision 124) |
| `ui.reduceEffects` | bool | follows Windows animation setting | |
| `ui.instantOpen` | bool | `false` | keep UI warm (≈40 MB) |
| `alerts.goals` | bool | `true` | |
| `alerts.weekly` | bool | `false` | |
| `backup.mode` | `Off\|Daily\|Weekly` | `Daily` | |
| `backup.folder` | path | `%LocalAppData%\TimeSink\backups` | |
| `backup.lastUtc` | int | `0` | |

## 6. Tracking engine (the core of the product)

Implement as a **pure, deterministic state machine** in `Core/Tracking` so it is fully unit-testable with fakes. No Win32 calls inside it.

```csharp
interface IClock            { DateTimeOffset UtcNow { get; } }
interface IProcessProvider  { IReadOnlyList<ProcInfo> Snapshot(); }            // pid, name, fullPath?, startUtc
interface ISessionStore     { /* open/close/heartbeat sessions, accumulate daily usage, list tracked apps */ }
sealed class TrackingEngine {
  void OnTick(DateTimeOffset now);            // every 5 s
  void OnSuspend(DateTimeOffset now);         // PBT_APMSUSPEND
  void OnResume(DateTimeOffset now);
  void OnLock(DateTimeOffset now);  void OnUnlock(DateTimeOffset now);
  void OnShutdown(DateTimeOffset now);        // WM_ENDSESSION / process exit
  void ReloadTrackedApps();
  void RecoverFromCrash(DateTimeOffset now);  // at Agent start
}
```

### 6.1 Detection loop (Agent side)
* Timer every **5 s** (message-window `SetTimer`, not a busy thread).
* `Win32ProcessProvider.Snapshot()` uses `CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS)` → list of (pid, image name).
* For each process whose **image name matches any tracked app's file name**, open it with `PROCESS_QUERY_LIMITED_INFORMATION`, read `QueryFullProcessImageNameW` and `GetProcessTimes` (creation time), and compare the **full path** to the tracked path. Cache `pid+creationTime → appId` so each process is resolved once.
* Packaged (Store) apps: resolve `GetPackageFamilyNameW`/package path when the image is under `WindowsApps`; match on `package_family`.
* For matched processes keep the process handle and call `RegisterWaitForSingleObject`; when it fires, **post a message to the message window** (no work on the thread-pool thread) so the engine closes the session at the exact exit time.
* Processes that cannot be opened (elevated/protected): match by image name only if the user's tracked path has that unique file name; otherwise ignore. Log once.

### 6.2 Path normalization
Lower-case drive letter, resolve `\\?\` prefix, expand 8.3 names, resolve environment variables only when adding (store the resolved real path). Compare case-insensitively.

### 6.3 Session lifecycle
* An app has **at most one open session**. It opens when ≥ 1 matching process exists and closes when none exist.
* Opening: `start_utc = max(process creation time, engineStartOrResume time)`. **Do not back-fill** time before the Agent started or before the app was added (counting starts at "add" time).
* Heartbeat: every 30 s update `last_seen_utc` for open sessions and call `UsageAccumulator.Add(appId, from, to)` for the delta since the previous accumulation. Accumulator splits at **local midnight** and writes `daily_usage` (UPSERT add).
* Close: set `end_utc`, `end_reason`, final accumulate. If `end_utc - start_utc < minSessionSeconds` delete the session and **subtract** what was accumulated for it.
* **Sleep/hibernate:** on suspend (and on resume as a safety net) close all open sessions at the suspend time with reason `sleep` when `tracking.pauseOnSleep`; on resume re-detect and open new sessions. **Gap detection fallback:** if a tick arrives and `now - lastTick > 20 s`, treat it as a missed sleep: close sessions at `lastTick`, then re-detect. (Modern Standby does not always deliver suspend before freezing.)
* **Lock/unlock** (`WTSRegisterSessionNotification`): if `tracking.countWhenLocked == false`, close on lock (`end_reason='sleep'`) and re-open on unlock; otherwise ignore.
* **Shutdown/logoff** (`WM_QUERYENDSESSION`/`WM_ENDSESSION`): close all sessions (`shutdown`), flush DB.
* **Crash recovery** (Agent start): for each session with `end_utc IS NULL`: if the same `pid`+`proc_start_utc` is still alive and `now - last_seen_utc ≤ 120 s`, **continue** it; else set `end_utc = last_seen_utc`, `end_reason='crash'`.
* **Tracked app removed/retired while running:** close with reason `removed`.
* **System clock changes:** if `now < lastTick`, close at `lastTick` and reopen (never produce negative durations).

### 6.4 Overlap rules
* Ten Chrome processes = **one** session (per app, not per process).
* Merged apps (`canonical_id`) remain separate sessions; only reports union them by canonical id by **summing** (as with all "combined" figures).

### 6.5 Daily usage cache
`daily_usage` is derived. Provide `UsageAccumulator.RebuildAll()` that recomputes from `sessions` (used after restore/merge and by a hidden "Repair" command). Unit-test that incremental accumulation equals a full rebuild.

### 6.6 Agent power/efficiency
Call `SetProcessInformation(ProcessPowerThrottling)` (EcoQoS) and set `BELOW_NORMAL_PRIORITY_CLASS` at start. Batch DB writes in the 30 s heartbeat; session open/close are immediate.

## 7. IPC

* Transport: named pipe `\\.\pipe\TimeSink.<userSid>`; ACL: current user only. Agent is the server; UI is the client. Messages are length-prefixed (4-byte LE) UTF-8 JSON using source-generated `System.Text.Json` contexts.
* UI → Agent: `reloadApps`, `pauseWrites {reason}`, `resumeWrites`, `backupNow`, `getState`.
* Agent → UI: `state { paused, running: [{appId, sessionStartUtc}] }` (pushed on change; also the response to `getState`), `backupResult {ok, path, error?}`.
* History queries do **not** use IPC: the UI opens the DB read-only (`Mode=ReadOnly`) and queries directly.
* The UI computes live durations locally as `now - sessionStartUtc` and ticks once per second.

## 8. UI application

### 8.1 Shell
* `FlyoutWindow`: borderless, `ShowInTaskbar=false`, `Topmost`, fixed **420 × 620 DIP**, corner radius via `DWMWA_WINDOW_CORNER_PREFERENCE`, closes on `Deactivated` (except while a modal dialog is open).
* Tab bar: **Today · Week · Month · Year · All**. Remember last tab in `settings` (`ui.lastTab`).
* Header: app dot + "TimeSink" wordmark, settings button. Footer (every tab): **+ Add app** on the left; right side per tab: Today "Updated just now", Month "Export month (CSV)", All "Export table (CSV)", Year/Wrapped "Save as image", Week "Last backup · N days ago".
* Times of day follow the Windows 12/24-hour setting; dates use the Windows short-date order with English names; durations are always `Xh YYm`. All text is English in v1 and comes from the string table (§9).
* Cold start budget: **< 300 ms** to first paint (ReadyToRun, defer all non-visible view construction, no synchronous icon extraction on the UI thread).

### 8.2 Visual language (Pulse)
Default theme `pulse-dark`. Tokens (hex): `surface #11140f`, `surfaceAlt #181d13`, `panel #141910`, `border #2b3125`, `borderSoft #232a1d`, `text #eef2e6`, `textSoft #cfd6c4`, `textMuted #9aa391`, `accent #c6f26b`, `warning #ffb86b`, `danger #ff8a7a`; heat ramp `#1b2116 #2d3d1c #46691f #75a826 accent`; app palette `#8fd3ff #c6f26b #ffb86b #d3a6ff #ff9fb4 #ffe08a`. Fonts: Segoe UI Variable Text; tabular figures for all durations. Radii 8/10/12; 1 px borders; no drop shadows. Text contrast ≥ 4.5:1 (3:1 for ≥ 24 px).

### 8.3 Views (visual reference boards are listed in §14; behaviours are authoritative here)

**Today** (`TodayView`)
* Header total: **PC uptime** today (decision 125) as `Xh YYm` plus ticking seconds; combined app time shows on the right of the "Tracked today" line; pill "N running" with a pulsing dot.
* Hourly chart (24 columns, height ∝ number of apps open that hour, max scale 6): current hour in accent; future hours show a dotted "typical day" marker (average of the same weekday over the last 8 weeks); a "now" marker with time label; hatched band for sleep-paused spans.
* Three stat tiles: First app opened · Longest session (duration + app icon) · vs yesterday-by-now (uptime delta in minutes).
* App rows (sorted by today's time): icon, name, status line (`Running · 1h 02m session` / `Closed 11:12 am`), today's time, 24-hour mini timeline strip of its sessions. Tap to expand: session list (`8:00 am → 12:09 pm · 4h 09m`) and comparison vs 7-day average.
* **Early-morning state:** while today's uptime is **< 1 h**, show the "Good morning · tracking since you signed in at H:MM" caption and the hint that the typical-day dotted line fills in as the day goes on.
* **Untracked-running-apps card** ("N running apps aren't tracked", per-app **Track** buttons that add the app immediately; "Not this one" dismisses) shows **any time** such apps exist and `tracking.suggestApps` is on, above a "Tracked today" list. The footer then reads "Only apps you pick are recorded".

**Week** (`WeekView`, board `PulseWeek`) — per the "C · Pulse" board: caption + total, 7 single-colour columns (Mon-first per `week.start`; the current day in accent) with the axis labels, a **20-week heat grid** (140 cells, heat ramp) with a **N-day streak** label, and per-app rows (letter tile, name, week total, 10-point sparkline). A streak day = any tracked app had time that local day (above `minSessionSeconds`); today does not break the streak until it ends without time. Footer shows "Last backup · N days ago".

**Month** (`MonthView`) — calendar heatmap
* Month navigation (‹ ›), Monday-first grid (honour `week.start`), day cell shows day number and hours (`7h`), heat level thresholds `<1h, <3h, <5h, <7h, ≥7h`, a 40 px "Week" total column, today underlined, future days dashed/empty.
* Hero: month total, delta vs previous month **over the same number of days**, daily average, best day.
* Selected-day panel: date, total, stacked bar by app, "Mostly <app> · time".
* Top **4** apps with time and delta vs last month. Footer: **Export month (CSV)**.

**Year** (`YearView`) — default sub-view is *monthly pace*
* 12 monthly columns; months before tracking began are dotted/empty; the current month shows a hatched projected extension (`monthHours / daysElapsed * daysInMonth`); next months show dashed ghost bars at the average of the last 3 months; dashed average line (full tracked months only).
* Banner: "On pace for N h by December, based on your last 3 months". Tap a month → panel with top apps and delta vs previous month.
* Sub-views via a 3-way segmented switch under the tab bar (**Pace | Wrapped | Matrix**, remembered in `ui.lastYearView`): **Wrapped** (3 cards: total hours with "N days and M hours" equivalent; top-3 podium; habits: longest streak, busiest month, peak hour, % before noon) and **App × month matrix** (hours per app per month, colour relative to each app's own best month, tap a row or month header for a summary). Wrapped has a **Save as image** button that renders the visible card to a PNG via a save dialog (`RenderTargetBitmap`, local only).

**All** (`AllView`) — dense table
* Columns: App · Hours · Days used · Avg/day · Share (bar + %). Header buttons sort (tap again flips). Row tap shows a detail line (first tracked, best month, longest session). Totals row. "Retired · not counted" rows below, dimmed, with stopped date. Footer: Export table (CSV).

**Add app** (`AddAppView`, tabs *Running · Installed · Browse…*)
* *Running:* processes that own a visible, non-cloaked, non-tool top-level window (`EnumWindows` + `IsWindowVisible` + `DwmGetWindowAttribute(DWMWA_CLOAKED)==0` + not `WS_EX_TOOLWINDOW`), excluding already-tracked and "ignored suggestion" paths. Each row: icon, name, open-for duration, **Track** button → becomes "Tracking ✓".
* *Installed:* A–Z grouped list with search, built from Start Menu shortcuts (`%ProgramData%` and `%AppData%` `Microsoft\Windows\Start Menu\Programs`, resolve `.lnk` via `IShellLink`) plus packaged apps (`PackageManager`/`shell:AppsFolder`). Letter rail on the right.
* *Browse…:* drop zone for `.exe`/`.lnk`, "Choose file…" button, and shortcuts to Program Files, Program Files (x86), `%LocalAppData%\Programs`, WindowsApps.
* Adding an app: insert into `apps` (display name = file description or file name), extract the icon, send `reloadApps`. **Time counts from the moment of adding.**
* Optional suggestions (if `tracking.suggestApps`): running untracked apps ranked by how long they have been running today; "Not this one" inserts into `ignored_suggestions`.

**Settings** (`SettingsView`, board `PulseSettings`) — **one grouped scrolling page** (no search, no quick tiles, no drill-down)
* Sections and rows (label · description · kind), rendered declaratively from a settings catalogue (id, label, description, kind = `switch | segmented | button | info`, current value):
  * **General:** Start with Windows (switch); Week starts on (Mon/Sun/Sat).
  * **Tracking:** Pause while the PC sleeps; Keep counting when locked; Ignore short sessions (5s/15s/30s); Suggest apps I use.
  * **Appearance:** Theme (Dark/Light/System, "follows Windows when set to System"); Accent (Lime/Blue/Pink); Compact lists.
  * **Notifications:** Goal and limit alerts; Weekly summary.
  * **Backup and data:** Automatic backup (Off/Daily/Weekly, "keeps the last 7 plus one per month"); Back up now ("Last backup: N days ago"); Restore from backup (Choose file…); Export all data (CSV or JSON); Delete all history (danger).
  * **About:** Resource use (live Agent private working set + CPU %, e.g. "11 MB · 0% CPU"); Tracking since (first tracked date, "N apps tracked"). **No "Check for updates"** (Principle 3).
* Back up now shows "Done ✓" and updates the last-backup line after completing.

**Dialogs** (modal inside the flyout): *Restore* (file picker → validation result → **Replace** or **Merge** → progress → summary), *Delete all history* (type `DELETE` to confirm), *Stop tracking an app* (retire; explains history is kept).

### 8.4 Charts and controls
Draw charts with lightweight WPF primitives (`Border`/`Rectangle`/`Canvas`/`DrawingVisual`). **No chart library.** Provide reusable controls in `UI/Controls`: `HourlyBars`, `SessionStrip`, `CalendarHeat`, `StackedBar`, `PaceBars`, `HeatMatrix`, `SortableHeader`, `ToggleSwitch`, `SegmentedControl`. All colours come from theme resources via `DynamicResource`.

### 8.5 Icons
`IconService` extracts icons with `IShellItemImageFactory` (64 px), stores a PNG blob + hash in `icons`, and serves from cache. Fall back to a coloured letter tile (first letter, palette colour by app id) if extraction fails.

## 9. Theming framework

> **v1 scope (decided, see `docs/decisions.md`):** the engine below minus package install. v1 ships token-driven theming with `theme.json` files for the built-ins `pulse-dark` and `pulse-light` (same format as the future `.tsktheme`), a per-theme **string table** for all visible text, and a `layout` field that only accepts `"pulse"`. §9.2 packaging limits, §9.3 `ThemeValidator`/`ContrastLint`, §9.4 install UX, file association, hot reload and Shift safe-mode UI are **post-v1** (old milestone M6). The nine extra variants in `design/themes/` (Doodle, Arena, Neon, Sakura, Receipt, Comic, Terminal, Glass, Poster) are the post-v1 target: Glass, Poster, Doodle, Neon (and Sakura's colours/fonts) fit tokens + effects + string table; Arena, Comic, Receipt and Terminal also need built-in layout presets (`layout: "arena"|"comic"|"receipt"|"terminal"`) compiled into the app. A package may select a preset by name but never define one.
> **Appearance settings mapping:** `ui.theme` Dark/Light/System picks `pulse-dark` / `pulse-light` (System follows Windows); `ui.accent` Lime/Blue/Pink overrides the theme's `accent` (+ heat ramp top).

### 9.1 Layers
| Layer | Controls | Who may ship it |
|---|---|---|
| Tokens | colours, fonts, corner shape, borders, shadows, density, chart style | Installable packages |
| Effects | a **fixed menu**: `scanlines`, `halftone`, `vignette`, `glow`, `backdropBlur` (themes choose and set strength; they cannot define new ones) | Installable packages |
| Layout presets | structural pieces (e.g. level badge, starburst, zigzag receipt edge) | **Built-in only** |

Installable themes can restyle but never add/move content.

### 9.2 Package format `.tsktheme` (zip)
```
theme.json            required
preview.png           required, 420x620
fonts/*.ttf|otf|woff2 optional (LICENSE required if present)
assets/*.png|svg      optional
LICENSE.txt           required if fonts present
```
Limits: ≤ 20 files, ≤ 5 MB total, extension allow-list above, **reject** any path containing `..`, absolute paths, or reserved names (zip-slip). **Never accept XAML, DLL, scripts.**

```jsonc
{
  "schema": 1,
  "id": "com.example.neon",            // reverse-DNS, [a-z0-9.-]
  "name": "Neon", "author": "Example", "version": "1.0.0",
  "minAppVersion": "1.0.0",
  "extends": "pulse-dark",             // missing tokens fall back to this
  "colors": {
    "surface": "#07060d", "surfaceAlt": "#0f0c1c", "panel": "#0f0c1c", "border": "#2a2347",
    "text": "#d1f7ff", "textSoft": "#bfeaf2", "textMuted": "#8a86a8",
    "accent": "#05d9e8", "accent2": "#ff2a6d", "warning": "#f9f002", "danger": "#ff5c7a",
    "heat": ["#15102a","#35185a","#7a1f78","#ff2a6d","#05d9e8"],   // exactly 5
    "apps": ["#05d9e8","#ff2a6d","#f9f002","#b967ff"]               // 4–8
  },
  "type": { "ui": "Chakra Petch", "numbers": "Share Tech Mono", "labelCase": "upper|none", "tracking": 1.2 },
  "shape": { "corner": "round|chamfer|square", "radius": [8,10,12], "border": 1, "shadow": "none|soft|hard" },
  "density": { "rowHeight": 40, "gap": 8 },
  "chart": { "bars": "solid|segmented|halftone", "barRadius": 2, "glow": 0.0 },
  "effects": { "overlay": "none|scanlines|halftone|vignette", "overlayStrength": 0.0, "backdropBlur": false },
  "layout": "pulse",                   // v1: only "pulse"; post-v1 built-in presets by name
  "strings": { "appName": "TimeSink", "addApp": "+ Add app" },  // overrides for the string table; missing keys fall back to extends
  "performance": "low|medium|high"
}
```
JSON Schema lives at `themes/theme.schema.json` and is the validation source of truth (generate C# validation from the same rules; do not duplicate by hand without a test that keeps them in sync).

### 9.3 Runtime
* `Core/Theming`: `ThemeManifest`, `ThemeValidator`, `ContrastLint` (WCAG: text/surface ≥ 4.5, textMuted/surface ≥ 4.5, accent/surface ≥ 3 for UI, text on accent ≥ 4.5), `TokenCatalog` (the complete list of token keys and their WPF resource keys).
* `UI/Theming/ThemeService`: resolve `extends` chain → flat token set → build a `ResourceDictionary` (`Brush.*`, `CornerRadius.*`, `Thickness.*`, `FontFamily.*`, `Double.*`) → swap it in `Application.Resources.MergedDictionaries`. All styles use `DynamicResource`, so switching is instant with no restart.
* Fonts load from a private copy via `FontFamily(new Uri(...), "./#Name")` (nothing installed system-wide).
* Effects are implemented once as overlay controls/brushes; `ui.reduceEffects` or `performance` gating disables them. `backdropBlur` only on Windows 11 build ≥ 22621, else flat fallback.
* Store: `%LocalAppData%\TimeSink\themes\<id>\`. Built-ins ship in the install folder `themes\` and use **the same format**.
* **Failure handling:** wrap apply in try/catch → revert to `pulse-dark`, show a toast. Holding Shift when opening the flyout forces `pulse-dark` for that session.
* **Dev mode:** `FileSystemWatcher` on a loose theme folder hot-reloads on save.

### 9.4 Install UX (Settings → Appearance → Themes)
Gallery of preview cards; **Install from file…**; drag-and-drop a `.tsktheme` onto the flyout; double-click a `.tsktheme` in Explorer (file association registered per-user by the installer → `TimeSink.UI.exe --install-theme "<path>"`). The install dialog shows preview, name, author, version, bundled fonts and licences, validation result and contrast warnings, with **Install** and **Install and apply**. Also: uninstall, **Export current as theme**, Open themes folder.

### 9.5 Built-in themes
v1 ships `pulse-dark` and `pulse-light` (System = follow Windows; default). Post-v1 order of ease: token-only `glass`, `poster`, `doodle`, `neon`, `sakura`; then layout-preset themes `arena`, `comic`, `receipt`, `terminal`. Reference boards: `design/themes/`.

## 10. Backup, restore, export

* **Format** `.timesink` = zip: `manifest.json` `{ "format":1, "schemaVersion":N, "appVersion":"x.y.z", "createdUtc":… }` + `timesink.db`.
* **Create:** SQLite online backup API (or `VACUUM INTO`) to a temp file while the Agent keeps running; zip with the manifest. Safe at any time.
* **Auto-backup** (Agent, `backup.mode`): on startup and on a timer, if the interval elapsed → `daily-YYYYMMDD.timesink` / `weekly-…`. Retention: last 7 daily + one per month for the last 12 months. Update `backup.lastUtc`.
* **Restore (Replace):** validate zip + manifest + integrity (`PRAGMA integrity_check`); refuse `schemaVersion` newer than the app; migrate older copies forward in a temp file; auto-create `pre-restore-<timestamp>.timesink`; UI sends `pauseWrites`; Agent closes its DB handle and acks; swap the DB file; `RebuildAll()` daily usage; `resumeWrites`; restart the Agent's tracking state.
* **Restore (Merge):** open the backup (migrated temp copy) read-only. For each backup app, match current `apps` by `exe_path` (case-insensitive) else insert (preserving `state`, `category`, `display_name`). Build an id map. `INSERT OR IGNORE` sessions using the unique `(app_id, start_utc)` index (**idempotent — importing the same file twice changes nothing**). Never overwrite existing settings. `RebuildAll()`.
* **Export:** CSV (`app,start_utc,end_utc,seconds`; and a daily-totals CSV) and JSON of the same. **Delete all history:** requires typing `DELETE`; deletes sessions/daily_usage/icons for all apps, keeps the app list and settings.

## 11. Installer and autostart

* **Publish:** Agent `dotnet publish -c Release -r win-x64 -p:PublishAot=true`; UI `dotnet publish -c Release -r win-x64 --self-contained -p:PublishReadyToRun=true` (no trimming).
* **Inno Setup (per-user):** `PrivilegesRequired=lowest`, install to `{localappdata}\Programs\TimeSink`, optional "Start with Windows" task (default on) writing `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\TimeSink` = `"…\TimeSink.Agent.exe" --background`; register `.tsktheme` and `.timesink` under `HKCU\Software\Classes`; on uninstall stop both processes, remove the Run entry and file associations, and ask whether to delete `%LocalAppData%\TimeSink`.
* The in-app **Start with Windows** toggle writes/removes the same Run value (so portable use works too).
* **Portable zip:** same binaries; data folder defaults to `%LocalAppData%\TimeSink` (a `portable.flag` file next to the exe switches to `.\data`).
* Later (not v1): winget manifest; code signing via Azure Trusted Signing.

## 12. Quality, performance and logging

**Budgets (verify before declaring v1 done)**
| Metric | Budget | How to check |
|---|---|---|
| Agent private working set, idle, after 24 h | ≤ 15 MB | `Get-Process TimeSink.Agent \| Select PrivateMemorySize64` |
| Agent CPU, idle | ≈ 0 % (< 0.1 % average over 10 min) | Task Manager / `Get-Counter` |
| UI cold start to first paint | < 300 ms | stopwatch log line `FlyoutShown ms=` |
| DB size | ≈ ≤ 1 MB per year of typical use | file size |
| Agent crash recovery | no double counting, no negative durations | tests below |

**Logging:** plain-text rolling log in `%LocalAppData%\TimeSink\logs` (1 MB × 3). No telemetry. Never log window titles or user content.

**Code conventions:** no reflection in Core/Agent; `async` only where I/O requires it; public APIs documented only when non-obvious; keep files small; no new NuGet packages without a one-line justification in `docs/decisions.md` (allowed by default: CsWin32, Microsoft.Data.Sqlite, CommunityToolkit.Mvvm, WPF-UI, xUnit, FluentAssertions).

### 12.1 Required automated tests (Core, using fakes)
* Single session open/close with exact duration; multiple processes of one app = one session.
* Session crossing local midnight splits correctly into two `daily_usage` days; DST spring-forward and fall-back days.
* Suspend/resume: sessions closed at suspend time and reopened after resume; gap detection fallback (tick gap > 20 s) behaves identically.
* Lock/unlock respects `countWhenLocked`.
* Crash recovery: continue when process identity matches and `last_seen` ≤ 120 s; else close at `last_seen`.
* `minSessionSeconds`: short sessions are removed **and** their accumulated usage subtracted.
* Clock moving backwards never yields negative durations.
* Incremental `UsageAccumulator` equals `RebuildAll()` on randomized session sets (property test).
* Backup → Replace restore round-trip; Merge twice is idempotent; newer-schema backup is refused; older-schema backup is migrated.
* Theme (v1): `theme.json` loads; `extends` fallback fills missing tokens and strings; a corrupt file reverts to `pulse-dark`. (Package install, zip-slip/oversize/extension rejection, contrast lint tests: post-v1.)

### 12.2 Manual test checklist (record results in `docs/manual-tests.md`)
Two monitors with taskbar on different edges; mixed DPI; explorer restart (tray icon returns); sleep/resume; lock/unlock; kill Agent mid-session; Store app (e.g. Calculator); elevated app; rapid tray clicking (no reopen flicker); install/uninstall on a clean user profile; theme install via double-click.

## 13. Milestones (do in order; each ends with a green build and its acceptance checks)

**M0 — Spike (go/no-go).** Create the solution. Agent: Native AOT, hidden message window, tray icon with a context menu (Open, Quit), SQLite open/migrate/insert. UI: stub flyout positioned from tray rect on the correct monitor and closing on deactivate. *Accept:* `dotnet publish` of the Agent with `PublishAot=true` succeeds and runs; tray icon survives explorer restart; flyout appears next to the icon on a second monitor. **If AOT + SQLite fails after reasonable effort:** fall back to a self-contained trimmed ReadyToRun Agent (expect ~25 MB, budget → ≤ 25 MB) and record this in `docs/decisions.md`.

**M1 — Core + tracking engine.** Schema, migrations, repositories, `TrackingEngine`, `UsageAccumulator`, Win32 providers, crash recovery, power/lock events. *Accept:* all §12.1 tracking tests pass; running Notepad (added manually via SQL) produces a correct session and `daily_usage` row; sleep cuts it.

**M2 — Theme tokens + flyout shell + Today.** `TokenCatalog`, `ThemeService`, `pulse-dark`/`pulse-light`, shell with tab bar, IPC, `TodayView` incl. early-morning state. No hard-coded colours. *Accept:* live ticking total; switching dark/light is instant; flyout < 300 ms.

**M3 — Add app + icons.** Running/Installed/Browse, icon cache, suggestions, ignore list, retire/restore. *Accept:* adding an app makes the Agent track it within 10 s without restart; Store app works.

**M4 — Month, Year, All, Week (simple).** Controls in §8.4; queries in `Core/Usage`. *Accept:* numbers match a hand-computed fixture dataset (seed with a fixture generator); exports produce valid CSV.

**M5 — Settings, autostart, backup/restore, export, delete.** Settings catalogue + search, Run-key toggle, auto-backup scheduler, Restore dialogs. *Accept:* backup→restore (Replace) and double Merge verified; auto-backup retention honoured.

**M6 — (post-v1) Theme install framework.** `.tsktheme` validation, install dialog, gallery, file association, export-current-as-theme, hot reload, safe-mode Shift, extra themes and layout presets. Not part of v1; in v1 only the `theme.json` loader, `extends` fallback, string table and revert-to-`pulse-dark` on load failure are built (in M2). *Accept (M2):* both built-ins load from `theme.json`; a corrupt theme file reverts to `pulse-dark`.

**M7 — Packaging and polish.** Inno Setup installer, portable zip, 24-hour soak test, budgets in §12, manual checklist, README. *Accept:* clean-machine install/uninstall; all budgets met.

Rough effort: 5–6 weeks part-time for one developer.

## 14. Visual design reference

The approved design (`TimeSink flyout designs FINAL`) is saved in `design/`: `pulse-boards.html` (all boards in one canvas) and one HTML file per board, each a self-contained interactive prototype (open in a browser; colours, spacing and copy are in its inline script/CSS). Boards, all 420 × 620:
`PulseWeek` (the "C · Pulse" board), `PulseToday`, `PulseTodayEarly`, `PulseMonth`, `PulseYearPace`, `PulseYearWrapped`, `PulseYearMatrix`, `PulseAllTable`, `PulseAdd` (running / installed / browse), `PulseSettings` (one grouped page). Where a board and this spec disagree, the **board wins for layout and copy; this spec wins for behaviour**. Post-v1 theme variants are in `design/themes/` (see §9).

## 15. Explicitly out of scope for v1
Window-title or document tracking; focus/idle tracking; cloud sync; accounts; online theme gallery; installable theme packages (M6); update checks; localisation (English only); multi-user/SID separation; per-app screen-time blocking (limits only **notify**); macOS/Linux; installable layout presets.

## 16. Open decisions (defaults already chosen — change only with a note in `docs/decisions.md`)
1. Settings layout: one grouped page (matches the final design).
2. Default Month view: calendar. Default Year view: monthly pace (Pace | Wrapped | Matrix switch).
3. Week tab: the "C · Pulse" board (bar chart, 20-week heat grid, streak, app rows).
4. Themes in v1: token/`theme.json` engine + string table + `layout` field for `pulse-dark`/`pulse-light`. Installable packages, validator, gallery and the nine extra variants are post-v1.
5. Windows baseline: Windows 11 primary; Windows 10 22H2 supported without backdrop effects.
6. App name/branding: "TimeSink" (placeholder).

## 17. How to work
1. Start with M0. Do not begin M1 until M0's acceptance passes.
2. After each milestone: build everything (`dotnet build -c Release`), run `dotnet test`, and summarise what is done/not done.
3. Prefer small commits per task. Do not rewrite shipped migrations.
4. When you must deviate from this spec, append a dated entry to `docs/decisions.md` (what, why, impact) and continue.
5. If blocked on something only the user can decide (branding, signing, licence), stop and ask with a recommended default.
