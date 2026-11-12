# 01 Confirm the Mac export stall and add a failing test

Status: ready-for-human
Spec: ../spec.md (Phase 0)
Blocked by: none

## Why
The PC reads the Mac file fine; the Mac's `.tssync` has not changed since Nov 10 21:19. Bug 1's cause (shared connection leaves `snap` attached) is a hypothesis. Needs the Mac to confirm.

## Tasks
- On the Mac, check Console.app for `SyncWriter error` / "snap is already in use" / "is locked". Record the result under Comments.
- Add a durable Mac sync log (`~/Library/Logs/TimeSink/sync.log`, 1 MB roll). Log each `performSync` step, status and error.
- Add a failing Swift test in `SyncTests.swift`: explicit transaction open on the shared db on one thread, `SyncWriter.write` on another, then a second write must succeed.
- Unblock the user: relaunch TimeSink on the Mac, press Sync now, confirm the OneDrive file timestamp advances.

## Done when
Hypothesis confirmed or replaced (written in Comments), and the test exists and fails for the stated reason.

## Comments
