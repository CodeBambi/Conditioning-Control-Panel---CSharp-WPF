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

The grant is written into its own setting, `AppSettings.InviteGrantUntil`, at the server's exact
end date. `PatreonService.HasPremiumAccess` and `HasAiAccess` OR it in.

- It never writes the Patreon stamps (`PatreonPremiumValidUntil`, `PatreonLabValidUntil`). Other
  code reads those as "a validation came back premium": the quest history, the boot heals and the
  "Premium is yours" card.
- It never goes through `EntitlementTierRule.ExtendGrace`, which would stretch 7 days into the
  14-day subscriber grace.
- `PatreonService.IsInviteWeekOnly` marks premium that comes only from the week. The celebration
  card skips it, so the one-time card is still owed when the friend actually pays.
- An end date more than 10 days out (a week plus clock slack) is ignored, not clamped. A clamp measured from "now" would slide
  forward on every heartbeat and turn one bad server value into open-ended premium.
- Logout and account switch clear it. It is excluded from settings backup and kept local on restore,
  like the Patreon stamps.
- The Lab (tier 2) is never granted. An invite week is vault only.

## Reward ladder

Rewards count converted friends (a first paid month), never redemptions, so handing out codes
alone earns nothing. Each rung is a visible achievement in the `Community` category, so it can be
worn as a title, and it unlocks one wardrobe item (mod bucket `invites`, its own picker tab)
through the registry's `achievement:` gate.

| Converted | Badge (title) | Wardrobe item |
|-----------|---------------|---------------|
| 1 | `invite_first` Brought One Down | charm `invite_pink_envelope` |
| 3 | `invite_hostess` The Welcome Committee | deco `invite_hostess_headset` |
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
    { "code": "K7QM-X2PR-9H", "state": "converted", "invitee_name": "kaycee", "day": 9 },
    { "code": "W4DN-8TZC-3F", "state": "trying",    "invitee_name": "j.doll", "day": 3 },
    { "code": "Q9VB-6MKE-2R", "state": "open" }
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
- Not a subscriber: `{ "ok": false, "reason": "not_subscribed", "converted_total": 3 }`. Include
  `converted_total` whenever it is above 0, so an inviter whose own subscription lapsed still
  collects ladder rewards for friends who convert later.
- Codes are 6 to 24 characters from `A-Z`, `0-9` and `-`. The client upper-cases and strips spaces
  before sending, and accepts the share link `cclabs.app/i/<CODE>`.
- **Mint codes from a CSPRNG with at least 40 bits of randomness:** 8 or more characters from a
  32-symbol alphabet (Crockford base32, no `I L O U`). Never embed the inviter's name or id. A
  readable prefix plus a short suffix can be enumerated, and the code would tell strangers who
  issued it.

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
| `already_had_week` | This account already had an invite week |
| `already_subscribed` | The redeemer already has tier 1 or more |
| `too_fast` | Rate limited (the client maps HTTP 429 to this too) |

Abuse limits the server enforces:

- One invite week per account, ever, and only for accounts that have never had tier 1 or more.
- Only accounts created after the code was minted may redeem it. This stops a subscriber from
  feeding their own codes to old alts. `own_code` alone only blocks the minting account.
- Rate limit failed redeems per account, per IP, and globally (for example 10 failures per account
  per hour, and a global ceiling that alerts). Per-account and per-IP limits alone reset with every
  new throwaway account and proxy.

**Known gap (owner decision):** the client sends no device identifier, and the app has none today.
A determined user can still make a fresh account per code they collect. Closing that needs a
device or install id, which is a privacy trade-off; it is not built.

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

## Vault gate card

Every padlocked tab's unlock button opens `VaultGateDialog`, the vault gate card, in place of the
old jump to Settings · Account (`MainWindow.BtnGateUnlock_Click`; TierGate's "see tiers" toast
opens it too, at the tier the door needs). A patron whose Patreon grant died on this PC still gets
Reconnect instead. The card names the clicked feature (art, title and tagline from
`ExclusiveFeature.All`), lists what the tier holds, shows the price from `VaultOffer.PriceFor` (keep it in
step with Patreon), and offers: open the tier on Patreon, compare tiers, sign in as an existing
supporter, and redeem an invite code once `/v2/invites/mine` answers.

The last-day card (`VaultGateDialog.ShowEnding`) is owed once per invite week inside its last 36
hours, to premium that comes only from the week. It goes through the presenter, so it becomes an
Inbox row when something is quiet.

### `GET /v2/public/supporters` (CCP-Server, new)

Unauthenticated, cacheable for an hour: `{ "count": 1234 }`, the number of active paying
supporters across providers. The card shows it rounded down to the hundred ("Join 1,200+
subjects"), hides it below 100, and hides it entirely while this endpoint does not exist, so it
never shows an invented number.

### Prices

`VaultOffer.PriceFor` is the one copy of the Patreon price list in the client. The card shows euros
when the Windows region's currency is EUR and dollars otherwise.

| Tier | Monthly | Yearly (Patreon only, 2 months free) |
|------|---------|--------------------------------------|
| Basic (tier 1) | €6 / $7.50 | €60 / $75 |
| Prime (tier 2) | €10 / $12.50 | €100 / $125 |

The other payment systems have no yearly plan, so every yearly price the card shows says
"on Patreon". Change a price on Patreon and you must change it here.
