# Friends: the wire (v1, 2026-09-23)

The desktop's friend list. No free text anywhere: pokes, invites and watches are preset ids the
server validates against a fixed grammar. Names and pictures are server-resolved. Presence is
opt-in and defaults off. This file is the authority for both halves: `proxy/friends.js` +
`proxy/friends-routes.js` in CC-Labs-llc/CCP-Server and `Services/Friends/*` here.

## Door

Every op is `POST /v2/friends/<op>` with JSON `{ unified_id, ... }` and the account token in
`X-Auth-Token` (the same pair `BackRoomApi.AppIdentity()` stamps; server side copy
`backroom-routes.js gate()`: `validateAuthToken`, `rateLimitIncr`, no web door in v1).
Replies are `{ ok: true, ... }` or `{ ok: false, reason }`. HTTP 200 for every worded reply,
401 for a bad token, 429 `too_fast` past 60 calls a minute per account, 503 with no Redis.

Ids on the wire are unified ids (`u_...`). The client never shows one.

## Ops

| op | body | reply |
|---|---|---|
| `state` | | `{ ok, code, me:{activity,lock_day,shared}, friends:[F], incoming:[R], outgoing:[R] }` |
| `poll` | `{ activity?, lock_day?, shared }` | `{ ok, online:[id], inbox:[I] }` |
| `request` | `{ code }` | `{ ok, status }` status: `sent` `accepted` `already` `not_found` `blocked` `self` `full` |
| `accept` | `{ id }` | `{ ok }` |
| `decline` | `{ id }` | `{ ok }` |
| `cancel` | `{ id }` | `{ ok }` (an outgoing request) |
| `remove` | `{ id }` | `{ ok }` |
| `block` | `{ id }` | `{ ok }` (removes the friendship and any requests both ways) |
| `unblock` | `{ id }` | `{ ok }` |
| `squelch` | `{ id, on }` | `{ ok }` |
| `send` | `{ to, kind, poke?, destination?, code?, watch? }` | `{ ok, status }` status: `sent` `squelched` `too_fast` `not_friends` `offline` `refused` |
| `report` | `{ id, reason }` | `{ ok }` |

`F` = `{ id, name, avatar, tier, online, activity, lock_day, last_seen, squelched }`
`R` = `{ id, name, avatar, via, at }`
`I` = `{ id, kind, from, from_name, from_avatar, poke, destination, code, watch, at, expires_at }`

- `avatar` is a first-party path on the proxy (`/v2/friends/avatar/<id>`) or null. It is non-null
  only when that account's Arcademy presence rung is `discord` (the existing consent ladder in
  `presence.js`; a friend link does not widen it). The route reuses the presence avatar resolver.
- `name` is `user.display_name`, never anything the client sent.
- `tier` 0 / 1 / 2 from the record (`patreon_tier` through the usual `effectiveTier` helper).
- `online` = the presence key exists (see Presence). `activity` is one of the grammar below or
  `"online"` when the friend shares nothing beyond being here. `lock_day` int or null.
- Timestamps are ISO strings from the server clock.

## Grammar (refuse anything else with `refused`)

- `kind`: `poke` `invite` `watch`
- `poke`: `hi wp tut gl oops thanks drop peek deeper still more sleepy`
- `destination`: `goon backroom remote ramp`
- `code` (only with `goon` or `remote`): `^[A-Z0-9-]{4,12}$`, system-generated join codes
- `watch`: `{ kind, id, title? }`, `kind` in `catalogue` (`id` `^[A-Za-z0-9_-]{1,64}$`), `flavour`
  (`id` in `trance pink frills shiny censored`), `ht` (`id` `^[0-9]{1,8}$`); `title` <= 40 chars,
  letters, digits, spaces and `.,'-` only, stored but never trusted (the receiver resolves the id)
- `activity`: `panel session backroom race goon goon_hosting arcademy breakout deeper remote`
- `reason`: `spam harassment underage impersonation other`
- friend code: `CCP-` + 5 chars from `23456789ABCDEFGHJKLMNPQRSTUVWXYZ`, case-insensitive on input

## Limits (server-enforced, the client mirrors the ones it can)

- 100 friends, 50 incoming requests, 20 requests sent a day, 50 inbox items (oldest dropped)
- one poke per pair per 10 s -> `too_fast`; 30 sends a minute per account -> `too_fast`
- an invite expires 90 s after `at`; a poke or watch 24 h after; expired items are never delivered
- a squelched sender's pokes answer `sent` to the sender and are dropped (they cannot tell)
- a blocked pair: `request` answers `blocked` for the blocker, `not_found` for the blocked;
  `send` answers `not_friends` both ways; inbox items from a newly blocked account are purged

## Presence

`poll` writes `friend_presence:<uid>` = `{ activity, lock_day, at }` with EX 180 when `shared`
is true, else deletes it. Online = key exists. The client polls every 20 s while the drawer is
open or any friend is online and the app is in the foreground, else every 120 s; never when
signed out. `state` is the full list; the drawer calls it on open and every fifth poll.

## Redis keys

`friends:<uid>` set, `friend_in:<uid>` set, `friend_out:<uid>` set, `friend_block:<uid>` set,
`friend_squelch:<uid>` set, `friend_inbox:<uid>` list of `I` JSON (newest first, LTRIM 50),
`friend_presence:<uid>` string EX 180, `friend_code:<code>` -> uid (and `user.friend_code`),
`friend_poke:<from>:<to>` EX 10, `friend_req_day:<uid>:<yyyymmdd>` EX 90000,
`friend_reports` list (`{ by, about, reason, at }`, LTRIM 5000).

Nothing here goes through the profile sync body, the anticheat clamp or the leaderboard cache.
