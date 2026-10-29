# TimeSink

A Windows tray app that records how long chosen apps are running. The user decides which apps to track; everything else is ignored.

## Language

### Apps

**Tracked app**:
An app the user has chosen to record. Its running time is counted from the moment it was added.
_Avoid_: Monitored app, watched app

**Retired app**:
A formerly tracked app that is no longer recorded but keeps its history. It can be restored. The UI says "stopped".
_Avoid_: Deleted app, removed app, disabled app

**Main app**:
The Tracked app that absorbs the history of one or more Merged apps and is the only one of them shown in the views.
_Avoid_: Keeper, canonical app, primary app

**Merged app**:
An app folded into a Main app, so its running time counts under the Main app. It can be unmerged or promoted to Main app. A Merged app whose Main app is retired or gone counts as its own group.
_Avoid_: Alias, child app, duplicate

**Background app**:
A Tracked app that runs all the time without the user using it (Steam, Wallpaper Engine). It is listed after every other app, and its time still counts in the totals.
_Avoid_: Idle app, always-on app

**Candidate**:
Any app the merge picker offers for folding into a Main app: a Tracked, Retired, already Merged or merely installed app.
_Avoid_: Merge target, option

**Palette slot**:
The index into the theme's `apps` colours that an app always uses, derived from its Main app id. Every view colours an app by its Palette slot.
_Avoid_: Color index, palette index, app colour

**App icon**:
The PNG stored with an app, or extracted for a Candidate. An app without one shows its letter tile.
_Avoid_: Logo, thumbnail

**App tile**:
The square face of an app in a row or header: its App icon, or its first letter on its Palette slot colour when it has none. That letter fallback is the letter tile.
_Avoid_: Avatar, badge

### Usage

**Session**:
One continuous run of a Tracked app. The sum of its Sessions is the app's running time.
_Avoid_: Run, launch

**Ranking**:
The default order the views list apps in: Background apps last, then most time first, then name A to Z ignoring case.
_Avoid_: Sort order, ordering

**Local day**:
A calendar day in the user's time zone. It lasts 23 or 25 hours when daylight saving changes.
_Avoid_: Day (unqualified), UTC day

### Screens

**Add app screen**:
The full-flyout overlay with the Running, Installed, Tracked and Browse tabs, plus the App settings and Merge screens it opens. `AddAppNavigation` decides which of them shows.
_Avoid_: Add dialog, picker
