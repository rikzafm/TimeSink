# Multi-device plan: Windows C# + Mac Swift, with sync

Status: needs-triage
Companion to [spec.md](spec.md). The UI target is the board files in `design/mac/` (tracked by git; the README there maps boards to tickets), a snapshot of the "TimeSink for Mac" canvas (https://claude.ai/artifact/Jbj4sP2kLmJX3HpCCX5KAt). Where a board and the running Windows app disagree, the Windows app wins.

Estimates are rough guesses for one developer working part-time. They have not been measured, so treat them as ordering and relative size, not a promise.

## 0. Principles for the multi-device work

1. **Sync never touches the live database.** Each device writes only its own snapshot file into a shared folder and reads the others'.
2. **One idempotent import.** Importing the same snapshot twice changes nothing. The existing Restore › Merge (decision 52) already works this way, so sync import reuses it.
3. **Headline numbers are uptime, overlap counted once** (decisions 125 and 126). Per-device hours sit beside them. App times are summed ("combined") and labelled that way.
4. **Every device keeps working alone.** Sync off, folder missing, or the other device offline must leave the app unchanged.
5. **Windows and Mac stay compatible through files, not code:** the database schema, the sync format and shared test fixtures.
6. **No categories, no network code in the apps.** The synced folder does the networking.

## 1. Data model changes (one migration, shared by both apps)

`M004_Devices` (C#) and the matching Swift migration. `PRAGMA user_version` becomes 4 on both.

| Change | Detail |
|---|---|
| New table `devices` | `device_id TEXT PRIMARY KEY` (UUID), `name`, `platform` (`windows`/`macos`), `is_local INTEGER`, `first_seen_utc`, `last_import_utc`, `last_file_utc`, `state` (`active`/`forgotten`). |
| `apps.device_id TEXT NOT NULL` | Existing rows get the local device id. |
| Unique key | `exe_path` is unique today (`COLLATE NOCASE UNIQUE`). It must become unique per device: `(device_id, exe_path)`. SQLite cannot drop an inline unique, so this is a **table rebuild** (create, copy, rename, recreate indexes). Test it against a backed-up real database first. |
| `apps.bundle_id` (nullable) | Mac identity. `exe_path` holds the app path on the Mac. |
| Local settings | `device.id`, `device.name` (defaults to the machine name), `sync.enabled`, `sync.folder`, `ui.deviceScope` (`This`/`All`). |
| Sessions | No column change. A session belongs to an app, and the app belongs to a device. |

Rules:
- The local device is created at first run (UUID stored in `settings`).
- Merging apps across devices uses the existing `canonical_id`, which may point at an app of another device.
- Retiring, renaming and background/kind flags stay per app. A merged group takes the Main app's flags (decision 129).
- Forgetting a device keeps its history. Deleting that history is a separate, explicit action (decision 5 in section 6).

## 2. Sync format (`shared/sync-format.md`)

- File: `<folder>/<deviceId>.tssync` (a zip like `.timesink`: `manifest.json` plus `snapshot.db`, a stripped SQLite copy with only this device's apps and sessions).
- `manifest.json`: `format`, `schemaVersion`, `appVersion`, `deviceId`, `deviceName`, `platform`, `createdUtc`, `sessionCount`, `hash`.
- Written atomically: write `<deviceId>.tssync.tmp`, then rename. Written on a timer (default every 5 minutes while sessions change), on quit and on "Sync now".
- Import key: `(device_id, bundle_id or exe_path, start_utc)`. Open sessions in a snapshot are closed at `last_seen_utc`.
- Reading: ignore a file whose hash does not match, a newer `format`, or a OneDrive placeholder that is not downloaded (shown as **Pending**).
- Cloud conflict copies (`... (2).tssync`) are ignored with a log line.
- No window titles, no icons larger than the app icon PNG, no settings.

## 3. Phases

### Phase 0: Contract and decisions (shared, about 3 days)

Goal: both apps can be built against the same written contract before any feature code.

Work:
- Write `shared/sync-format.md` and the device model above.
- Add `docs/decisions.md` entries: multi-device scope, uptime union rule, device identity, relaxed Principle 3 (opt-in folder sync, no sockets in the apps).
- Export the C# test fixtures to `shared/fixtures/*.json` (input sessions, zone, now, expected numbers) and make the existing C# tests load them. Start with the groups listed in spec.md.
- Add a `GLOSSARY.md` section: Device, Device scope, Snapshot, On both.

Done when: the contract is reviewed, the C# tests pass reading the JSON fixtures, and nothing user-visible has changed.

### Phase 1: Windows Core, device-aware queries (about 1.5 weeks)

Goal: the existing queries can answer "this device" and "all devices" with overlap counted once.

Work:
- `M004_Devices` and `DeviceIdentity` (create and cache the local device).
- `UsageContext` gets `DeviceScope` (like `GamesOnly`, decision 150); `AppGroups.Load(..., deviceScope)` filters apps. Add `Setting.DeviceScope` (`ui.deviceScope`) alongside `GamesOnly`.
- **Uptime union:** `UsageUptime.Seconds` works on one device's sessions (first session to last, minus sleep). For all devices, compute each device's uptime intervals, then union them. Add `UsageUptime.Intervals` and `UsageUptime.Union`.
- Result types gain per-device figures: Today (hourly counts per device, device uptimes, overlap), Week and Month (day totals per device and overlap), Year (month totals per device), All (per-app devices), App detail (hours per device), Wrapped (device split with overlap).
- Merge across devices: `MergePlan` may list apps from other devices, grouped by device; `AppCatalogue` and `AppLibrary` learn about `device_id`.
- Backup and restore carry the `devices` table.

Tests: fixtures for overlapping devices (union once), one device only, device in another time zone, DST day, forgotten device, merge across devices, and M004 on a real-sized database.

Done when: all existing tests still pass with the local device only, and the new fixtures pass for `DeviceScope = All`.

### Phase 2: Windows sync engine (about 1 week)

Goal: two Windows installs (or a Windows install and a fixture file) exchange data through a folder.

Work:
- `SyncWriter` and `SyncReader` in Core, built on `BackupService` and the Merge import.
- `SyncHost` in the Agent: timer, file watcher, "Sync now" signal, backoff, placeholder detection. The Agent remains the only writer of `sessions` (the importer runs there).
- Settings keys and strings for the Devices and sync screen (phase 3).
- Failure handling: unreadable folder, full disk, corrupt file, skewed clocks (never reorder by file time; use session times), duplicate device ids (regenerate and warn).
- Log lines only, no telemetry.

Tests: write then read the same snapshot, import twice, partial file, placeholder file, device removed, clock skew, 100k-session snapshot size and time.

Done when: two machines sharing a folder show each other's apps and sessions after a sync, and `RebuildAll()` after import equals the incremental result.

### Phase 3: Windows UI for multiple devices (about 2 weeks)

Goal: the Windows flyout shows the multi-device design. Each item names the canvas board it follows.

| Screen | Change |
|---|---|
| **Header (all tab screens)** | Add the **This Mac | All devices** control (on Windows: **This PC | All devices**) between the wordmark and the games and settings icons, next to `GamesButton` in `FlyoutWindow`. Remembered as `ui.deviceScope`. Hidden when no other device exists and sync is off. |
| **Today** | Period chevrons (`PeriodNavigator` already exists). Caption reads "· All devices" in All scope. Add the one-line device summary ("Mac 6h 12m · PC 4h 30m · off 12:58 · 2h 05m on both"). `HourlyBars`: bars overlap side by side per device in All scope (Mac green, PC blue, shorter bar in front). Legend adds "Overlapping = both on". Header total is the union uptime. |
| **Today, expanded app row** | `SessionDial` gains rings: outer ring first device, second ring second device, inner ring total share, centre % vs 7-day average. A stats column lists each device's time and the overlap. The row's session strip shows one thin strip per device. |
| **Week** | Chevrons. `WeekColumns` draws overlapping bars per day. Summary line "Mac … · PC … · … on both". The label above each bar pair is the day's union uptime. |
| **Month** | Centred chevron title. Summary line under the month total. Day cells stay one union hour value. The selected-day panel stays by app. |
| **Year › Pace** | `PaceBars` draws overlapping bars per month. Summary line for the year. |
| **Year › Wrapped** | Total card's device bar becomes overlapping Mac/PC bars with "N h on both". Other cards unchanged. |
| **Year › Matrix, Top apps, Habits** | Unchanged (app-level facts). |
| **All** | Device dots next to each app name (lime circle Mac, blue square PC); row detail unchanged. In "This PC" scope, other-device-only apps are hidden. |
| **App detail** | A device split bar under the lifetime tiles. The year grid sums all devices unless a scope is chosen. |
| **Add app › Running and Installed** | Local device only. No change except icon-button styling already designed. |
| **Add app › Tracked** | Show device dots and the merged count badge for apps that exist on more than one device. |
| **Merge into app** | New **device tabs** (one per known device, with a tick count badge), the Tracked / Untracked / All filter inside each tab, one shared preview line, and a "Suggested: same name on other device" banner. |
| **Per-app settings** | "Merged apps" lists apps from other devices with a device marker and an unmerge button. |
| **Settings** | New **Devices and sync** section (switch, folder picker, device list with status Up to date / Synced / Pending, "Sync now", "This device is called"). |
| **Devices and sync screen** | Device cards, sync folder row with change button, last sync time, merge hint banner. |

Cross-cutting UI work:
- Device identity in the theme: two device tokens plus a **shape** (circle and square), so colour is never the only cue. Add tokens to `pulse-dark`/`pulse-light` (`Brush.DeviceA`, `Brush.DeviceB`, and a neutral for a third device).
- New string-table keys for every new label (Principle 7: no inline text).
- Controls reuse the overlap geometry: `OverlapBars` helper shared by `HourlyBars`, `WeekColumns` and `PaceBars` (width 66% per bar, overlap 34%, shorter in front).
- A third device: the UI supports exactly two active devices. A third snapshot is not imported; the Devices and sync screen explains why (decision 3 in section 6). The neutral device token is not needed yet.

Tests: view model tests with fixture data in All and This scope; screenshot checks against the canvas boards; the manual checklist in `docs/manual-tests.md` gets a two-device section.

Done when: with a sync folder holding another device's snapshot, every screen above matches the canvas in both scopes, and with sync off the app looks exactly as before.

### Phase 4: Swift Core (about 2 weeks, needs the Mac only for `swift test`)

Goal: the Swift package reproduces the C# results.

Work:
- `TimeSinkCore` package: Data (schema and migrations through M004), Tracking engine and accumulator, Usage queries with `DeviceScope`, Apps (groups, merge across devices), Backup and sync import, Settings.
- Load `shared/fixtures/*.json` in the Swift tests.
- SQLite: the system `SQLite3` library behind a thin wrapper (decision 2 in section 6).

Done when: all fixtures pass in Swift, and a database written by C# opens in Swift (and the reverse) with identical query results.

### Phase 5: Mac shell and tracking (about 1 week)

Goal: a menu-bar app that records real usage on the Mac.

Work:
- `NSStatusItem` with the menu-bar total, an `NSPopover` host, `LSUIElement`.
- `NSWorkspace` provider for launch and quit, initial scan, bundle ID identity.
- Sleep, wake, lock, unlock and power-off handling, the tick-gap fallback, crash recovery at launch.
- `SMAppService` login item, with the setting in the UI.
- App icons from `NSWorkspace`.

Done when: a day of real use produces correct sessions, survives sleep and a force quit, and idles at near 0% CPU.

### Phase 6: SwiftUI views (about 2 weeks)

Goal: the Mac popover matches the canvas.

Work (in this order): header, tabs and chevron navigation, Today (this Mac), Today (all devices) with overlapping bars and the dial rings, Week, Month, Year (Pace, Wrapped, Matrix), All, App detail, Add app (Running, Installed, Tracked, Browse) with the icon buttons, per-app settings, Merge with device tabs, Settings, Devices and sync.
- Charts drawn with SwiftUI shapes or Swift Charts; no chart library.
- Fonts: SF; colours from a token file mirroring the Windows theme JSON.

Done when: each canvas board has a matching screen, in This Mac and All devices scopes.

### Phase 7: Mac sync and cross-platform integration (about 1 week, needs both machines)

Goal: the real Mac and PC see each other.

Work:
- Swift `SyncWriter`/`SyncReader`, folder watcher (FSEvents), placeholder handling for `~/Library/CloudStorage/OneDrive-...`.
- Merge across devices between a Mac app and a Windows app (Chrome, VS Code).
- Two-machine test script: both on, one asleep, one offline, clock difference, device renamed, device forgotten, OneDrive conflict copy.

Done when: both apps show identical All-devices numbers for the same day (within one sync interval), and the union uptime matches a hand count.

### Phase 8: Polish and release (about 1 week)

- 24-hour soak on both apps; Mac memory target under about 30 MB; Windows budgets unchanged.
- Backup and restore across platforms (a Windows `.timesink` restores on the Mac).
- Docs: README (both apps), install notes (free Apple ID build, right-click › Open on a second Mac), `docs/manual-tests.md`.
- Installer and release notes for the Windows update that adds the migration (back up first).

Done when: both apps pass the manual checklist and the budgets.

## 4. Ordering and parallel work

```
P0 contract ──► P1 Windows Core ──► P2 Windows sync ──► P3 Windows UI ──┐
      │                                                                   ├──► P7 integration ──► P8
      └──► P4 Swift Core ──► P5 Mac shell ──► P6 SwiftUI views ───────────┘
```

- P1 to P3 can start now on this Windows machine.
- P4 to P6 need a Mac and start after the P0 fixtures exist. P4 can overlap with P2.
- The two tracks meet in P7.

## 5. Risks

| Risk | Mitigation |
|---|---|
| The `apps` table rebuild in M004 loses data or breaks foreign keys | Automatic pre-migration backup (`pre-migrate-*.timesink`), a migration test on a copy of a real database, and the migration is forward-only like the others. |
| Windows and Mac queries drift | Shared fixtures are the contract; every query change updates the fixture first. |
| Union uptime is wrong across time zones or DST | Intervals are UTC; only day bucketing uses the viewer's zone. Fixtures for both. |
| OneDrive placeholders and conflict copies | Detect and show **Pending**; never read a file whose hash fails. |
| A device is renamed, reinstalled or cloned (duplicate id) | Device id lives in `settings`; on a duplicate id the second device regenerates its id and warns. |
| Mac idle memory or Swift tooling issues | Phase 5 measures early; fall back to a smaller popover if needed. |
| UI crowding with a third device | Cap the design at two active devices at first. |

## 6. Decisions on the earlier open questions

1. **Minimum macOS: 15 Sequoia.** The user wrote "25"; there is no macOS 25, and macOS 15 was confirmed. It sets which `SMAppService` and SwiftUI APIs may be used.
2. **SQLite on the Mac:** the system `SQLite3` library behind a thin Swift wrapper (about 200 lines: open, prepare, bind, step, transaction, backup). It ships with macOS (no dependency, no extra megabytes) and the project already needs only WAL, upsert and `VACUUM INTO`. GRDB.swift is the fallback if the wrapper grows beyond that. Record the choice in `docs/decisions.md`.
3. **Devices:** two for now. The UI is drawn for two. If a third device's snapshot appears, the app does not import it and shows "Only two devices are supported yet" on the Devices and sync screen. Nothing is deleted.
4. **Device scope:** global. One This device | All devices setting (`ui.deviceScope`) applies to every tab.
5. **Deleting data:** nothing is deleted unless the user explicitly asks. Forgetting a device stops syncing it and keeps its history. Removing its history is a separate action on the Devices screen ("Delete this device's history…") that needs typing `DELETE`, like Delete all history.
6. **Sync location:** chosen **per device**. `sync.folder` is a local setting, and each device picks its own path, because the same OneDrive folder lives at a different path on each machine. The Devices and sync screen shows a warning if no other device's file appears within about 10 minutes, which usually means the two devices point at different folders.

## 7. Proposed tickets

Create these under `.scratch/mac-app/issues/` once the plan is approved.

| # | Ticket | Phase |
|---|---|---|
| 01 | Write `shared/sync-format.md` and the device model | 0 |
| 02 | Export shared fixtures and load them in the C# tests | 0 |
| 03 | `M004_Devices` migration and table rebuild | 1 |
| 04 | `DeviceIdentity` and device settings | 1 |
| 05 | `DeviceScope` in `UsageContext` and `AppGroups` | 1 |
| 06 | Union uptime and per-device figures in the query results | 1 |
| 07 | Merge across devices in `MergePlan` and `AppLibrary` | 1 |
| 08 | `SyncWriter` and `SyncReader` | 2 |
| 09 | `SyncHost` in the Agent | 2 |
| 10 | Header device scope control | 3 |
| 11 | Overlap bars in `HourlyBars`, `WeekColumns` and `PaceBars` | 3 |
| 12 | Today: summary line, chevrons, dial rings | 3 |
| 13 | Week, Month, Year device summaries | 3 |
| 14 | All and Tracked device dots, App detail split | 3 |
| 15 | Merge screen device tabs | 3 |
| 16 | Settings: Devices and sync section and screen | 3 |
| 17 | Swift package, schema and migrations | 4 |
| 18 | Swift tracking engine and accumulator | 4 |
| 19 | Swift usage queries | 4 |
| 20 | Mac menu-bar shell, `NSWorkspace` provider, power events | 5 |
| 21 | SwiftUI tab screens | 6 |
| 22 | SwiftUI Add app, per-app settings, Merge | 6 |
| 23 | SwiftUI Settings and Devices and sync | 6 |
| 24 | Swift sync writer and reader | 7 |
| 25 | Two-machine integration tests | 7 |
| 26 | Soak, docs, release | 8 |
