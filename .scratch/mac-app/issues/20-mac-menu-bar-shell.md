# Mac menu-bar shell and tracking

Status: resolved
Spec: ../spec.md · Plan: ../plan.md (phase 5)
Blocked by: 18
Needs a Mac.

A single menu-bar process (`LSUIElement`): `NSStatusItem` showing today's total and an `NSPopover` host. `NSWorkspace` process provider (launch and terminate notifications plus an initial `runningApplications` scan, identity is bundle ID plus launch date), sleep, wake, lock, unlock and power-off handling, the tick-gap fallback, crash recovery at launch, `SMAppService` login item (setting in the UI), icons from `NSWorkspace.icon(forFile:)` cached as PNG. No sandbox, no window titles.

Done when: a day of real use records correct sessions, survives sleep and a force quit, idles near 0 percent CPU, and memory is recorded against the 30 MB target.
