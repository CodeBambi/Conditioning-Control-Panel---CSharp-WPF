# Server spec: Prestige tree + Ditzy Data PRO (CCP-Server changes)

Companion to the client-side Prestige/analytics feature (SkillTree Tier 6 + seasonal re-buy).
The client ships FIRST and is safe against the current server; these changes must land in
CC-Labs-llc/CCP-Server **within the same calendar month** so no season rollover happens
between the two deploys.

## 1. Permanent skill ids (single source of truth)

```
PERMANENT_SKILL_IDS = [
  "pink_hours", "ditzy_data", "hive_mind", "trophy_case", "popular_girl", "eternal_doll",
  "ditzy_data_pro", "season_rewind", "bestie_records", "brain_drain_report", "certified_data_bimbo"
]
```

The client mirrors this in `SkillDefinition.PermanentIds` (Models/SkillTree.cs) — keep in sync.

## 2. Purchase catalog additions (`POST /v2/user/purchase-skill`)

New validatable nodes (cost, prerequisite):

| id | cost | prerequisite |
|---|---|---|
| ditzy_data_pro | 150 | better_quests |
| season_rewind | 250 | ditzy_data_pro |
| bestie_records | 350 | season_rewind |
| brain_drain_report | 500 | bestie_records |
| certified_data_bimbo | 1000 | brain_drain_report |

Existing validation (cost, prereq owned, not already owned, unknown id rejected) unchanged.
Re-purchasing an owned id stays an error — season rollover removing non-permanent ids is
what re-enables purchase.

On success, additionally:
- `lifetime_points_spent += cost` (new per-user field, int, monotonic, never reset)
- include `"lifetime_points_spent"` in the success response body (alongside `skill_points`,
  `unlocked_skills`)

## 3. Season rollover job

Change `unlocked_skills = []` to:

```
unlocked_skills = intersection(unlocked_skills, PERMANENT_SKILL_IDS)
```

**Do NOT reset `skill_points` anymore** (drop the `skill_points = 1` seed) — policy change:
the point balance is permanent across seasons; only the mechanical skills reset. The new
client already treats the balance as monotonic (max-merge on every sync), so an old server
that still zeroes it is harmless to new clients but should be fixed here regardless.
xp/level reset unchanged. `lifetime_points_spent` is never touched by rollover.

**Grandfathering (one-time, first rollover under the new rule):** refund
`sum(cost of removed non-permanent ids)` into `skill_points` — those nodes were bought under
the old keeps-forever expectation; the recurring re-buy sink starts the season after.
Flag per user so it fires once. (Decision: refund recommended; if skipped, announce loudly
in patch notes instead.)

## 4. `/v2/user/sync`

- Include `"lifetime_points_spent"` in every sync response (client adopts via max, monotonic).
- When responding with `level_reset: true`, also include the post-rollover authoritative
  `unlocked_skills`. The new client rebuilds its tree from that list (∪ locally-owned
  permanent ids); `skill_points` is NOT adopted on reset — the balance persists and the
  client max-merges it like any other sync.
- While a `level_reset` is pending for a user, ignore the client-uploaded `unlocked_skills`
  in that request (old clients would otherwise re-upload stale mechanical skills).
- The client also uploads `stats["lifetime_points_spent"]` (advisory). Take
  `max(server, client)` once at migration/backfill; after that, server-only writes.

## 5. One-time migration at deploy

For every user: `lifetime_points_spent = sum(cost of ids currently in unlocked_skills)`.

## 6. Mixed-version behavior (expected, no action)

- Old client + new server at rollover: server keeps permanent ids; old client union-merges
  its local mechanical ids back locally only (soft grandfathering for one season, no exploit —
  purchases stay server-validated).
- New client + old server: client-side prune keeps permanent nodes locally; server thinks the
  user owns nothing until next purchase/sync. Do not let a rollover happen in this state.

## 7. Testing

Use the existing per-account admin `level_reset` flag to exercise the rollover path
mid-month (same hook the client uses for Season Recap testing).
