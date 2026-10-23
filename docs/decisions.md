# Decisions log (append-only)

## 2026-10-23 — Design review against the final boards
Source: `design/pulse-boards.html`, `design/themes/theme-variants.html`; decided with the user in a grilling session.

| # | Decision | Replaces / why | Impact |
|---|---|---|---|
| 1 | Settings is **one grouped page** | Spec said quick tiles + drill-down + search; the final board is grouped | `SettingsView` simpler; no search; catalogue kept |
| 2 | Week = the "C · Pulse" board (7 columns, 20-week heat grid, streak, app rows) | Spec placeholder (stacked columns, day panel) | `WeekView`, streak query in `Core/Usage` |
| 3 | Streak day = any tracked app had time that local day; today doesn't break it until it ends empty | Undefined in spec | Needs per-day any-usage query over `daily_usage` |
| 4 | No "Check for updates" | Board shows it; conflicts with Principle 3 (no network) | Omitted from About |
| 5 | `ui.theme` = Dark/Light/System (default System); `ui.accent` = Lime/Blue/Pink | Spec: theme id + free hex | Settings keys changed (§5.1) |
| 6 | **WPF-UI dropped**; plain WPF with own templates | Design is custom flat; lighter | One fewer dependency; own `ToggleSwitch`/`SegmentedControl` etc. |
| 7 | Toolchain: .NET 10 SDK + VS C++ Build Tools installed by the user before M0 | Machine had only .NET 8 runtime, no SDK | M0 blocked until installed; Inno Setup only at M7 |
| 8 | Themes in v1 = `theme.json` tokens + string table + `layout:"pulse"` field; **M6 (installer/validator/gallery/assoc/hot reload) post-v1** | The 9 variants change copy and layout (Arena, Comic, Receipt, Terminal), so a restyle-only package can't deliver them; built-in layout presets are needed anyway | Views take colours/fonts/shapes/text only from theme; later themes = new `theme.json` + presets |
| 9 | Year sub-views via segmented switch Pace \| Wrapped \| Matrix (`ui.lastYearView`) | Boards had no navigation between them | `YearView` hosts three sub-views |
| 10 | Early caption while today < 1 h tracked; untracked-apps card shows any time while such apps exist and `suggestApps` is on | Spec said "when little data exists" | Today view conditions |
| 11 | Wrapped "Save as image" saves a PNG (`RenderTargetBitmap`) | Button in board, not in spec | Local only; no clipboard/share |
| 12 | English only; clock follows Windows 12/24 h; durations `Xh YYm` | Boards hardcode 12 h | Use culture time patterns |
| 13 | Month lists top **4** apps (not 3) | Board shows 4 | Matches design |
| 14 | Footers per design: `+ Add app` on every tab plus per-tab right-side item | Spec said footers vary | See §8.1 |

## 2026-10-23 — Ticket 01 (M0 spike) implementation notes
| # | Decision | Why | Impact |
|---|---|---|---|
| 15 | **Native AOT + SQLite works**; no trimmed fallback needed. Published Agent ~2.8 MB exe + `e_sqlite3.dll` (2 MB), ~14 MB working set idle | M0 go/no-go passed | Spec §2 Agent stays AOT |
| 16 | Agent message window is a **hidden top-level window** (never shown), not `HWND_MESSAGE` | Message-only windows do not receive the `TaskbarCreated` broadcast, so the tray icon would not return after an Explorer restart (verified) | §4/§13 wording "message-only window" superseded |
| 17 | Pinned `SQLitePCLRaw.lib.e_sqlite3` 2.1.14 directly in Core | Default 2.1.11 flagged NU1903 (high severity), fails with TreatWarningsAsErrors | Revisit when `Microsoft.Data.Sqlite` pulls a fixed version |
| 18 | P/Invoke via hand-written `LibraryImport` in `Agent/Native/NativeMethods.cs`, not CsWin32 | Spike needs ~15 calls; spec allows either | Can switch to CsWin32 later |
| 19 | Native AOT publish needs `vswhere.exe` on PATH (`%ProgramFiles(x86)%\Microsoft Visual Studio\Installer`) | ILCompiler's vcvars lookup fails otherwise | Build/publish scripts (M7) must add it |
| 20 | `Agent --smoke <dbPath>` diagnostic opens/migrates a DB and inserts a row | Proves AOT+SQLite headlessly | Dev-only flag |

Open/unchanged (2026-10-23 review): Windows 11 primary, Windows 10 22H2 supported without backdrop; name "TimeSink" is a placeholder.

## 2026-10-23 — Ticket 03 (tracking engine) implementation notes
| # | Decision | Why | Impact |
|---|---|---|---|
| 21 | Engine closes an exited app's session at the tick time (`OnTick(now)`), so the Agent should also call `OnTick` immediately when a process-exit wait fires | `OnTick` is the only detection entry point; keeps the engine free of per-process state | Agent ticket wires exit callbacks to `OnTick(now)` |
| 22 | Invariant: usage accumulated for an open session always covers `[start_utc, last_seen_utc]`; close accumulates `last_seen->end` (signed) and min-session removal subtracts `start->end` | Makes crash recovery and `RebuildAll()` exact (open sessions rebuild up to `last_seen`) | Property test relies on it |
| 23 | `end_reason` for a backwards clock change is `exit`; tick-gap/suspend/lock use `sleep` | Schema CHECK has no better value | |
| 24 | `OpenSession` bumps `start_utc` by whole seconds when `(app_id,start_utc)` already exists | Unique index `ux_sessions_app_start`; avoids failures on fast restart or clock rewind | Start may be a few seconds later than the process start |
| 25 | `daily_usage` rows reaching 0 are deleted; `ITrackingSettings` is read live on each decision | Keeps incremental == rebuild; settings changes apply immediately | |
| 26 | Unreadable processes (`FullPath == null`) match by image name only if exactly one tracked app has that file name | Spec 6.1 | Win32ProcessProvider must leave `FullPath` null when `OpenProcess` fails |

## 2026-10-23 - Ticket 02 (flyout spike) implementation notes
| # | Decision | Why | Impact |
|---|---|---|---|
| 27 | Flyout geometry, toggle debounce and launch-arg format live in `Core/Flyout` (`FlyoutPlacement`, `FlyoutToggle`, `FlyoutLaunchArgs`) and are unit-tested; Agent/UI only call Win32 and apply the result | Only logic worth testing; keeps Agent/UI thin | Agent passes `--anchor/--monitor/--work/--dpi` (physical px) to the UI; UI places itself with `SetWindowPos` in physical pixels (re-applied on `WM_DPICHANGED`) |
| 28 | Agent and UI are both PerMonitorV2 (Agent via `SetProcessDpiAwarenessContext` at start, UI via manifest) | `Shell_NotifyIconGetRect` / monitor rects must be physical pixels for mixed DPI | Do not remove |
| 29 | Toggle: Agent tracks the UI `Process`; click while running sets named event `Local\TimeSink.UI.Close` (UI closes); `Process.Exited` records the exit time and clicks within 300 ms of it are ignored (implementation.md section 3 rule) | Deactivate-on-click would otherwise reopen the flyout | UI single instance via `Local\TimeSink.UI` mutex; a second launch exits |
| 30 | `FlyoutShown ms=` is logged to `%LOCALAPPDATA%\TimeSink\logs\ui.log`, measured from OS process creation to `ContentRendered` | No logging infrastructure yet | Replace with the shared logger later |
| 31 | Measured cold first paint on the dev machine is about 600-700 ms (R2R, TieredPGO off), above the 300 ms budget | Stub window; WPF/runtime cold start dominates (about 200 ms to App init, about 400 ms to first render) | Needs a dedicated optimisation pass before M2 acceptance (options: Agent pre-warm, trimmed startup path, avoid theme dictionary loads) |

## 2026-10-23 - Ticket 05 (theme tokens, string table, flyout shell) implementation notes
| # | Decision | Why | Impact |
|---|---|---|---|
| 32 | `Core/Theming`: `ThemeLoader` (theme.json -> flat `Theme`, `extends` chain, hex/length/`layout:"pulse"` validation), `StringTable`, `StringKeys`, `TokenCatalog`; any load failure reverts to `pulse-dark` (a broken `pulse-dark` itself throws: it is a shipping bug) | Spec 9 / M2 | Built-ins live in repo `themes/<id>/theme.json`, copied to `<exe>\themes\`; tests load the same files |
| 33 | `ThemeService` (UI) builds one ResourceDictionary (`Brush.*`, `CornerRadius.*`, `Thickness.Border`, `FontFamily.*`, `Double.FontSize.*`, `Str.*`) and swaps it in `MergedDictionaries`; views use only `DynamicResource`. Visible text is `Str.<Key>` resources, so a theme swap also swaps text | Instant Dark/Light switch, no hard-coded values in views | Ctrl+Shift+T flips Dark/Light in place (dev aid until Settings, ticket 16); Shift held at launch forces `pulse-dark`; `ui.theme` Dark/Light/System read at start |
| 34 | Added tokens beyond the spec list: `onAccent` colour and `type.sizes` (small/body/title); `heat` must have 5 entries, `apps` 4-8 | Needed for the active-tab/link styles | Part of `TokenCatalog` |
| 35 | `ui.lastTab` is read/written by the UI through `Core.Data.SettingsStore` (own short connection, best effort, no migration; no-op when the DB does not exist) | IPC arrives in ticket 06; spec says the UI opens the DB itself | Writes happen on tab change, off the UI thread |
| 36 | Cold start measured on the dev machine (R2R self-contained, 5-run medians): about 1100 ms with hardware rendering, about 850 ms with software rendering. Floor: about 100 ms to `Main`, about 300 ms to `OnStartup`, about 240 ms for the window's XAML (not JIT: about 25 ms of it), then first render. The 300 ms goal is **not reachable cold** with WPF here | Item 31 gap | Applied: `RenderMode.SoftwareOnly` (saves about 250 ms; `TS_HW=1` restores GPU), theme JSON + settings read on a background thread overlapping WPF start, hand-rolled colour parsing (no Regex/ColorConverter). Composite R2R made no measurable difference (not adopted) |
| 37 | **Standby UI** for the 300 ms goal: `TimeSink.UI.exe --standby [--keep]` builds and renders off-screen (-32000), then waits on `Local\TimeSink.UI.Show`; the Agent writes the launch geometry to `%LocalAppData%\TimeSink\flyout.args` (`FlyoutLaunchArgs.ToFileText`) and sets the event; the UI moves into place and activates. Measured signal-to-shown 112-125 ms. A standby without `--keep` exits after 10 s | Spec has `ui.instantOpen` (keep UI warm, about 40 MB) | Agent starts a non-keep standby when the mouse moves over the tray icon (15 s cooldown) and a `--keep` standby after every UI exit when `ui.instantOpen` is true/1. A click with no standby is still a cold start (about 850 ms); hover pre-warm only helps if the cursor dwells for the standby's build time. `ui.instantOpen` stays default false per spec |
| 38 | `FlyoutShown` log line now carries a startup breakdown (`main`, `prepared`, `onStartup`, `themed`, `ctor`, `xamlLoaded`, `windowBuilt`, `sourceInit` with cumulative JIT ms); warm shows `warm=1 signalMs=` | Needed to find where the time goes | `StartupTrace` in UI |
