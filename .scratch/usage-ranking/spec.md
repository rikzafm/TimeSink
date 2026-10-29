# Usage ranking: one module for Ranking and app names

Status: ready-for-agent

## Problem

The usage queries in `src/TimeSink.Core/Usage` each carry their own copy of the Ranking rule (Background apps last, then most time first, then name A to Z ignoring case) and of the app-name lookup:

- **Ranking.** The `Tier` / amount descending / name comparator is written out in `MonthQuery` (twice), `MatrixQuery` (twice), `WeekQuery` and `YearQuery`. `TodayQuery` repeats it for past days and wraps it in a live-day prefix. `WrappedQuery` repeats the amount/name half three times and uses `Tier == 0` as a filter. `UsageRank.Tier` is only half of the rule, so decision 129 (Background apps) had to edit every query.
- **Naming.** `NameOf(apps, group)` is a private copy in Today, Month, Year, Wrapped and Matrix. Week uses a `names` dictionary and Today's row uses `root.Name`, each with a different fallback. Every Session's group is guaranteed to be in the loaded apps, so those fallbacks never fire.

A change to the rule has to be made in about ten places, and only the end-to-end fixtures pin it.

## Decisions (settled in the architecture grilling)

1. Scope: Ranking and naming only, as a pure refactor with no behaviour change. Month slicing (Year and Matrix per-month loops, Wrapped's hour walk) is a separate, later design.
2. Home: the existing internal `UsageRank` stays the one module. `UsageLoader.LoadApps` keeps its return type (`Dictionary<long, UsageApp>`).
3. Ordering: one entry point taking the items, the loaded apps, a group-id selector and an amount selector. It orders by Background (last), then amount descending, then name case-insensitive in the current culture. The name is derived from the group through `NameOf`, so callers pass no name selector. It returns `List<T>`, so callers cannot add keys.
4. `NameOf(apps, group)` moves into `UsageRank` and keeps the fallback `group.ToString()`. Week's `names` dictionary and Today's `root.Name` fallback switch to it.
5. `Tier` becomes private to `UsageRank`. `IsBackground(apps, group)` is public (internal) and replaces `Tier == 0` in Wrapped's two filters.
6. Today: the past-day branch uses the shared ordering. The live-day branch (running apps first, then most recently closed, Background apps after the rest within each group) stays in `TodayQuery` and reuses `IsBackground` and the shared name comparison. It does not widen the shared interface.
7. Wrapped: `ranked`, `longest` and the per-kind lists use the shared ordering. The re-sort inside `TopOf` is dropped; it filters the already ranked list, whose order it keeps.
8. `AllSorting` is untouched. It sorts rows with their own `Background` flag, has a third bucket (apps that never ran) and header sorts.
9. Glossary: **Background app** and **Ranking** added to `GLOSSARY.md` (done).
10. Recording: one entry in `docs/decisions.md` saying Ranking lives in `UsageRank` and a change to the rule is made there. No ADR.

## Tests

- New, directly on `UsageRank`, with small `UsageApp` dictionaries and no database: Background apps last; amount ties fall back to name ignoring case; a group missing from the apps falls back to its id as text; `IsBackground` is true only when the group's canonical app has the Background flag; the ordering applies the same rule when the amount selector changes (Matrix ranks the same rows by total and by one month).
- Existing tests stay unchanged and green as the regression net: `BackgroundRankingTests` and every query fixture (Today, Week, Month, Year, Matrix, Wrapped).

## Out of scope

Month slicing and uptime-per-month bucketing, Wrapped's hour walk, the three heat-level functions (decisions 67, 96, 127 give them different thresholds), `AllSorting`, any change to what the views show.

## Constraints to keep

Decision 129 (Background ranking), 99 (Wrapped piece offset) and 118 (DST) are untouched by this work.
