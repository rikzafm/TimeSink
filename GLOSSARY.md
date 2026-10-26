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
An app folded into a Main app, so its running time counts under the Main app. It can be unmerged or promoted to Main app.
_Avoid_: Alias, child app, duplicate

**Candidate**:
Any app the merge picker offers for folding into a Main app: a Tracked, Retired, already Merged or merely installed app.
_Avoid_: Merge target, option

### Usage

**Session**:
One continuous run of a Tracked app. The sum of its Sessions is the app's running time.
_Avoid_: Run, launch

**Local day**:
A calendar day in the user's time zone. It lasts 23 or 25 hours when daylight saving changes.
_Avoid_: Day (unqualified), UTC day
