# TimeSink for Mac: Swift Core, Agent and SwiftUI

Status: needs-triage

Design: the board files in `design/mac/` (see `design/mac/README.md` for the board-to-ticket table and known gaps). They are a snapshot of the "TimeSink for Mac" canvas (https://claude.ai/artifact/Jbj4sP2kLmJX3HpCCX5KAt, private), which renders them. `design/mac/` is tracked by git (the rest of `design/` stays ignored), so a Mac checkout has the files. The boards follow the Windows app, so the two look alike; where a board and the running Windows app disagree, the Windows app wins.

## Decisions made with the user

| # | Decision |
|---|---|
| 1 | The Mac Core, Agent and UI are written in **Swift** (SwiftUI for the UI). |
| 2 | Windows stays **C#** (Core, Agent, WPF). Two codebases, kept compatible by a shared database format, a shared sync format and shared test fixtures. |
| 3 | Sync is **one snapshot file per device** in a user-chosen folder (OneDrive, Google Drive or any synced folder). The live `.db` is never synced. Peer-to-peer over the LAN is out of scope. |
| 4 | Headline numbers are **uptime with overlap counted once** across devices. Per-device hours are shown beside it (decisions 125 and 126 in `docs/decisions.md`). |
| 5 | Apps are identified by bundle ID on the Mac and by exe path on Windows. They count as one app only after the user **merges** them (the existing `canonical_id` merge, extended across devices). |
| 6 | Categories are not part of this app. |

## What carries over from `TimeSink.Core`

The Swift package `TimeSinkCore` ports these. The C# code is the reference.

| C# area | Swift module | Notes |
|---|---|---|
| `Data/` (`Db`, `Migrator`, `M001`..`M003`) | `Data` | Same schema and `PRAGMA user_version`. Never edit a shipped migration. New `M004` adds `device_id` and `platform` (shared with Windows). |
| `Tracking/` (`TrackingEngine`, `ISessionStore`, `IClock`, `IProcessProvider`) | `Tracking` | A pure state machine behind protocols. Port line for line, then run the shared fixtures. |
| `Usage/` (`UsageAccumulator`, `TodayQuery`, `WeekQuery`, `MonthQuery`, `YearQuery`, `AllQuery`, `MatrixQuery`, `WrappedQuery`, `AppDetailQuery`) | `Usage` | Sessions are clipped to local days with DST-aware midnights. Overlap across devices is counted once. |
| `Apps/` (`AppLibrary`, `AppGroups`, `MergePlan`, `AppKind`) | `Apps` | Merge works across devices. |
| `Backup/` (`BackupService`, `RestoreService`, merge, export) | `Backup` | Same `.timesink` zip format, so a Windows backup restores on the Mac. |
| `Settings/` | `Settings` | Same keys and defaults. |
| not ported | | `Ipc` (one process, no pipe), `Theming` (use SwiftUI), Windows icons, Steam scan. |

## Mac specifics

- **One process.** A menu-bar app (`LSUIElement`, `NSStatusItem` plus an `NSPopover` with SwiftUI). It tracks and shows the UI, so there is no IPC and no separate Agent and UI.
- **Process detection** is event-driven, with no polling: `NSWorkspace` `didLaunchApplication` and `didTerminateApplication`, plus an initial `runningApplications` scan. Identity is bundle ID plus launch date.
- **Power and lock:** `willSleep`, `didWake`, `willPowerOff`, and the distributed notifications `com.apple.screenIsLocked` and `com.apple.screenIsUnlocked`. The tick-gap fallback from spec section 6.3 stays.
- **Login item:** `SMAppService.mainApp` (macOS 13 or later).
- **Icons:** `NSWorkspace.icon(forFile:)`, cached as PNG in the `icons` table.
- **SQLite:** system `libsqlite3` through a thin wrapper, or GRDB.swift. Decide in ticket 01; either needs a one-line justification in `docs/decisions.md`.
- **No App Sandbox, no special permissions.** Window titles are never read.

## Sync

- Each device writes only `<folder>/<deviceId>.tssync`: its apps, sessions and device name.
- Each device reads the other devices' files and imports them. The unique key is `(device_id, app, start_utc)`, so importing twice changes nothing. Then `RebuildAll()` runs with the importing device's local time zone.
- OneDrive on macOS keeps files under `~/Library/CloudStorage/OneDrive-...` and may leave placeholders that are not downloaded yet. A placeholder shows as "Pending" on the Devices and sync screen.
- The format is documented in `shared/sync-format.md` and implemented in both C# and Swift.

## Shared fixtures (the contract)

The hand-computed numbers in the C# tests become JSON files under `shared/fixtures/`. Each holds input sessions, a time zone, a "now" and the expected results. The C# tests and the Swift tests both load them.

Fixture groups:
- single session and multiple processes of one app
- midnight split and DST spring-forward and fall-back
- suspend, resume and the tick-gap fallback
- lock and unlock
- crash recovery (continue versus close at `last_seen`)
- `minSessionSeconds` removal and subtraction
- clock moving backwards
- incremental accumulator equals `RebuildAll()`
- backup replace round trip, merge twice is idempotent, newer schema refused
- sync import twice is idempotent
- overlap across two devices is counted once

## Milestones

1. **Contract.** `shared/sync-format.md`, `M004`, fixture export from the C# tests, C# exporter and importer.
2. **Swift Core.** Schema, migrations, engine and accumulator pass the fixtures.
3. **Swift usage queries.** Today, Week, Month, Year, All, Wrapped, Matrix and App detail pass the fixtures.
4. **Mac shell.** Menu-bar app, `NSWorkspace` provider, power events, login item.
5. **SwiftUI views.** Follow the canvas boards.
6. **Sync.** Snapshot writer and reader, folder watcher, merge across devices.
7. **Polish.** Backup and restore, export, soak test, memory target under about 30 MB.

## What the user needs to provide

- A Mac with Xcode, and its macOS version (target macOS 14, floor 13).
- An Apple ID. The free one is enough to run your own build; notarising for others needs the paid program.
- The OneDrive folder path on both machines.

## Working notes

- This repo's tooling runs on Windows, where Swift cannot be compiled or tested. The Swift work should run on the Mac (Claude Code on the Mac against this repo).
- Ask before creating a git branch (the user's standing rule).

## Resolved and open

- SQLite wrapper: the system `SQLite3` library behind a thin wrapper (see plan.md section 6).
- Minimum macOS version: 15 Sequoia (the user wrote 25, which does not exist; macOS 15 was confirmed).
- Two devices for now; device scope is global; nothing is deleted unless explicitly requested; the sync folder is chosen per device.
- Open: which fixtures can be exported mechanically from the existing C# tests and which must be rewritten?
