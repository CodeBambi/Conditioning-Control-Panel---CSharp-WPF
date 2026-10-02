# Invite Week Primer

Paying subscribers hand out a few invite codes a month. A new account that redeems one gets the vault
(tier 1) free for 7 days, with no card and nothing that renews. When an invited friend pays for a
month, the inviter climbs a reward ladder of cosmetics.

The app is the thin half. The server owns the codes, the one-week-per-account rule, conversion
tracking and the counts. The app reads the results and never decides who is entitled.

## Client map

| File | Role |
|------|------|
| `Services/Invites/InviteRules.cs` | Pure: code normalisation, reply parsing, `ApplyGrant` |
| `Services/Invites/InviteApi.cs` | The wire: `POST /v2/invites/mine` and `/v2/invites/redeem` |
| `Services/Invites/InviteGrantSync.cs` | Live half: applies a grant on the UI thread, saves, repaints gates |
| `Services/Invites/InviteRewards.cs` | The inviter's reward ladder; unlocks badges off a `mine` snapshot |
| `Services/Account/V2AuthService.cs` | Reads `invite_grant_until` off `/v2/user/profile` |
| `Services/Settings/ProfileSyncService.cs` | Reads `invite_grant_until` off the heartbeat |

## How the week unlocks the app

The grant is written into `AppSettings.PatreonPremiumValidUntil`, the window every premium gate
already reads through `HasPremiumAccess`, at the server's exact end date.

- It never goes through `EntitlementTierRule.ExtendGrace`, which would stretch 7 days into the
  14-day subscriber grace.
- It never shortens a longer window a real subscription already stamped.
- An end date more than 8 days out is clamped to 8 days, so a bad server value cannot hand out more.
- The Lab (tier 2) window is never touched. An invite week is vault only.

## Reward ladder

Rewards count converted friends (a first paid month), never redemptions, so handing out codes
alone earns nothing. Each rung is a visible achievement in the `Community` category, so it can be
worn as a title, and it unlocks one wardrobe item (mod bucket `invites`, its own picker tab)
through the registry's `achievement:` gate.

| Converted | Badge (title) | Wardrobe item |
|-----------|---------------|---------------|
| 1 | `invite_first` Brought One Down | charm `invite_pink_envelope` |
| 3 | `invite_hostess` The Hostess | deco `invite_hostess_headset` |
| 5 | `invite_pied_piper` Pied Piper | charm `invite_recruiter_rose` |
| 10 | `invite_recruiter_chief` Recruiter in Chief | deco `invite_velvet_crown` |

`InviteRewards.Apply` runs on every `mine` read and is idempotent.

The badge and wardrobe PNGs in this first cut are generated placeholders (flat vector, neon text).
Replace them with real art at the same paths and sizes: badges 1024x1024 in
`Resources/achievements/`, wardrobe items 512x512 transparent in `Resources/cosmetics/invites/`.

Outside this repo, per CLAUDE.md "Add a New Achievement": add the four ids to CCP-Server
`proxy/data/achievements.json` (or the Discord announce 400s), upload the badges plus 256px webp
siblings to `cclabs-site/achievements/`, and add the four wardrobe ids to `proxy/cosmetics.js` if
it validates deco and charm ids.

## Server contract (CCP-Server)

All calls use the usual door: `POST`, JSON body with `unified_id`, header `X-Auth-Token`.
Refusals are `{ "ok": false, "reason": "<word>" }` with HTTP 200 or 4xx.

### `POST /v2/invites/mine`

For a subscriber (effective tier 1 or more, whitelist included):

```json
{
  "ok": true,
  "resets_at": "2026-11-01T00:00:00Z",
  "converted_total": 4,
  "codes": [
    { "code": "PINK-MIA-7Q4X", "state": "converted", "invitee_name": "kaycee", "day": 9 },
    { "code": "PINK-MIA-2B8R", "state": "trying",    "invitee_name": "j.doll", "day": 3 },
    { "code": "PINK-MIA-K3ZZ", "state": "open" }
  ]
}
```

- Allowance: tier 1 gets 2 codes per calendar month (UTC), tier 2 gets 3. Codes are minted lazily on
  the first `mine` of the month. Unused codes expire at `resets_at`; they do not roll over.
- `state` is `open` (not redeemed), `trying` (redeemed; `day` is the day of the week, 1 to 7, or more
  once the week has ended unpaid) or `converted` (the friend's first paid month landed).
- `converted_total` is lifetime, not monthly. The reward ladder reads it.
- `invitee_name` is the friend's display name. Send it only for `trying` and `converted`; the client
  drops it on `open` regardless.
- Not a subscriber: `{ "ok": false, "reason": "not_subscribed" }`.
- Codes are 6 to 24 characters from `A-Z`, `0-9` and `-`. The client upper-cases and strips spaces
  before sending, and accepts the share link `cclabs.app/i/<CODE>`.

### `POST /v2/invites/redeem`

Body adds `"code"`. On success:

```json
{ "ok": true, "grant_until": "2026-10-09T12:00:00Z" }
```

Refusal reasons the client words for the user:

| Reason | Meaning |
|--------|---------|
| `unknown_code` | No such code, or it expired at its month's reset |
| `used` | Someone already redeemed it |
| `own_code` | The redeemer minted it |
| `already_had_week` | This account (or this hardware hash) already had an invite week |
| `already_subscribed` | The redeemer already has tier 1 or more |
| `too_fast` | Rate limited (the client maps HTTP 429 to this too) |

Abuse limits: one invite week per account, ever; also one per hardware hash if the server keeps one.
Rate limit redeem attempts per account and per IP so codes cannot be brute-forced.

### `invite_grant_until`

While a grant is live, add `invite_grant_until` (ISO 8601 UTC) to the top level of the heartbeat
reply and to `/v2/user/profile`. Omit it (or send `null`) otherwise.

Do **not** fold the grant into `effective_tier` or `patreon_tier`. The client's tier path stamps a
14-day grace on a rise, which would double the week. Server-side gates that serve tier 1 features
(the AI chat proxy, awareness, content delivery) must, however, treat a live grant as tier 1.

### Conversion

When a redeemer's first paid month lands from any provider (Patreon, SubscribeStar, site checkout),
mark their code `converted`, increment the inviter's `converted_total`, and keep the count even if
the friend later cancels.

## Status

- Client core (this primer, the wire, the grant): built.
- Server endpoints: not built yet. Until they ship, `mine` answers 404, the client reads that as
  "no invites", and the invites UI stays hidden.
