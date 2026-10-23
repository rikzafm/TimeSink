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

Open/unchanged: Windows 11 primary, Windows 10 22H2 supported without backdrop; name "TimeSink" is a placeholder.
