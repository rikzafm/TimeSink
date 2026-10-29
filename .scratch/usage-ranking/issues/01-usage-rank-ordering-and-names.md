# UsageRank owns the ordering and NameOf; six queries move onto it

Status: ready-for-agent
Spec: ../spec.md

Add the ordering entry point and `NameOf` to `UsageRank`, make `Tier` private and expose `IsBackground`, per spec decisions 3 to 7. Migrate `MonthQuery`, `MatrixQuery`, `WeekQuery`, `YearQuery`, `WrappedQuery` and the past-day branch of `TodayQuery`, deleting their private `NameOf` copies and inline comparators. Add the direct `UsageRank` tests listed in the spec, and the `docs/decisions.md` entry (spec decision 10).

Do not create a branch without asking first. Do not touch `AllSorting`, and do not change the live-day ordering in `TodayQuery` beyond reusing `IsBackground` and the shared name comparison.

Done when:
- no query contains `UsageRank.Tier(`, a private `NameOf`, or an inline `ThenBy(... CurrentCultureIgnoreCase)` ranking comparator, apart from `TodayQuery`'s live-day branch;
- every existing test passes unchanged, including `BackgroundRankingTests` and all query fixtures;
- the new `UsageRank` tests cover every case in the spec;
- `docs/decisions.md` records that Ranking lives in `UsageRank`.

## Comments

Implemented, uncommitted on master (no branch created). Full suite: 645 passed, 0 failed, including 9 new `UsageRankTests`. Decision 137 added. Follow-up noticed, out of scope: `BarDetail.cs:59` ranks by amount and name without the Background rule.
