# Swift package, SQLite wrapper, schema and migrations

Status: resolved
Spec: ../spec.md · Plan: ../plan.md (phase 4)
Blocked by: 02, 03
Needs a Mac (Swift cannot be built or tested on the Windows machine).

Create `mac/TimeSinkCore` (Swift Package, macOS 15). Add a thin wrapper over the system `SQLite3` library (open, prepare, bind, step, transactions, WAL pragmas, backup) and the schema with migrations M001 to M004 matching `PRAGMA user_version`. Record the SQLite choice in `docs/decisions.md` (system library, GRDB as fallback). Load `shared/fixtures/*.json` in the tests. Ask before creating a git branch.

Done when: `swift test` passes; a database created by the C# app opens in Swift and the reverse, with the same `user_version` and tables; the wrapper has tests for errors, transactions and the backup call.

Implementation:
- Created `mac/TimeSinkCore` (Swift 6, macOS 15).
- Implemented `SQLiteDatabase` and `SQLiteStatement` over system `libsqlite3`.
- Implemented `M001_Initial`, `M002_BackgroundApps`, `M003_AppKind`, `M004_Devices` in Swift matching C# schema v4.
- Added comprehensive unit tests in `SQLiteDatabaseTests`, `MigrationTests`, `CrossPlatformDatabaseTests`, and `FixtureLoaderTests`.
- All 11 `swift test` tests pass.
- Decision 175 recorded in `docs/decisions.md`.
