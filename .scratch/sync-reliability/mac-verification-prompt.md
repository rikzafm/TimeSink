# Prompt for Claude Code on the Mac (paste everything below the line)

---

You are on the Mac for the TimeSink repo. Read `CLAUDE.md` and `.scratch/sync-reliability/spec.md` first.

## Background
The Windows PC reads the Mac's sync file fine, but the Mac's `.tssync` in OneDrive stopped updating (last write Nov 10 21:19). A branch `sync-reliability` has fixes. The Mac Swift in it was written on Windows and has **never been compiled or run**. Your job is to make it build, make its tests pass, and verify it on this Mac. Do not touch the C# code under `src/` or `tests/`.

## Rules
- Ask me before creating any git branch. Work on `sync-reliability` itself.
- No Co-Authored-By or Claude attribution lines in commits or PR text.
- Fix compile and test failures with the smallest change. If a test fails, decide whether the test or the code is wrong and tell me which, before changing behaviour.
- Do not refactor and do not fix unrelated problems; list them at the end instead.

## Steps
1. Get the code: `git fetch origin && git switch sync-reliability` (if it is not on origin, tell me; I will copy it over).
2. Build: `cd mac/TimeSinkCore && swift build 2>&1 | tail -60`. Fix every compile error. Likely spots: Swift 6 strict-concurrency in the new `SyncTests.swift` closures, `try?` optional flattening in `SyncHost.isEnabled`/`syncFolder`, `XCTAssertEqual(try ...)` autoclosures, the new `Sync/SyncExportHealth.swift`, the banner `Text(... + ...)` in `DevicesSyncView.swift`, and `SyncStates.exportFailed` use.
3. Test: `swift test 2>&1 | tail -80`. Pay special attention to these new tests (SyncTests.swift, SQLiteDatabaseTests.swift):
   - `testExportSurvivesOpenTransactionOnMainConnection`
   - `testMainTransactionsAndPerformSyncLoopLoseNothing`
   - `testLargeImportDoesNotFailMainConnectionWrites` (a 20,000-session import; if the main connection hits SQLITE_BUSY, move `rebuildAll` out of the write transaction in `SyncReader.import` and tell me)
   - `testWriteRecoversFromLeakedSnapAlias`, `testImportRecoversFromLeakedSrcAlias`
   - `testReadOnlyFolderMarksExportFailedAndRecovers`, `testExportFailureRetriesWithoutUserAction` (confirm `SyncWriter.write` really throws on a read-only folder)
   - `testUnchangedRemoteFileIsSkippedUntilHistoryDeleted`
   - `testEncryptionErrorSkipsOnlyThatFileAndRecoversWithoutRestart`
   - `testDocumentsCurrentMismatchMacEncryptsWhenPassphraseExistsEvenIfSyncEncryptedIsOff`
   Also check the shared fixture test for `shared/fixtures/sync-import.json` (case `import-twice-idempotent`): since a second import of an unchanged file is now skipped, the Swift loader may need to clear the `sync.imported.*` settings before the second import, as the C# test does.
4. Confirm the original bug on the OLD behaviour (ticket 01), without changing code: in Console or Terminal run
   `log show --predicate 'process == "TimeSinkApp"' --last 3d | grep -iE "SyncWriter error|snap|locked|already in use" | tail -30`
   and report what you find. Also report the modified time of the `.tssync` files in the OneDrive sync folder (`ls -l@ ~/Library/CloudStorage/OneDrive*/Apps/TimeSink/`).
5. Build and install: `./mac/build-app.sh --install --open`. Then in the app: Settings, Devices and sync. Press Sync now. Report:
   - whether the Mac's `.tssync` modified time advanced (within a minute),
   - what the device rows say (the PC row should read "File from ... ago"),
   - the sync state shown.
6. Soak test: leave it running 15 minutes while you use a tracked app, then confirm the Mac's `.tssync` time kept advancing and no `export_failed` banner appeared. Check `log show --predicate 'process == "TimeSinkApp"' --last 15m | grep -i sync`.
7. Commit your fixes to `sync-reliability` with plain messages. Do not push unless I say so.

## Report back
Start with: build status, test pass/fail counts, then each fix you made (file and why), the ticket 01 evidence (what the log showed), the Sync now result, the soak result, and any unrelated problems you noticed but did not fix.
