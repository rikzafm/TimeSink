# 06 Encryption errors must not wedge sync

Status: ready-for-agent
Spec: ../spec.md (Phase 5)
Blocked by: 04

## Tasks
- `SyncHost.Check`: drop the `return` and `_backoffUntil = MaxValue` on key missing/wrong/remote-not-encrypted. Record state plus a retry fingerprint (remote fingerprint, passphrase file mtime, `sync.encrypted`); skip only that file until it changes; keep exporting and scanning other files.
- A failed export must not skip the import pass (`SyncHost.cs` ~136).
- Log each non-success status once per change (BadHash, NewerFormat, DeviceForgotten, ConflictCopy, Failed); add `sync.last_error`.
- Setting or clearing the passphrase clears the retry and import fingerprints.
- Mac: replace `isPausedDueToEncryptionError` with the same logic, still exporting while paused.
- Add a test documenting the current Mac/PC encrypt-decision mismatch (bug 6) for a later ticket; do not change the rule here.

## Done when
Wrong passphrase gives `encryption_key_wrong` but the export is still written; fixing the passphrase without restart imports and returns to `synced`; replacing the remote file retries. Mirrored on the Mac.

## Comments
