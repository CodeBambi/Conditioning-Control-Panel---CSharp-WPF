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
| `state` | | `{ ok, code, me:{activity,lock_day,shared}, friends:[F], incoming:[R], outgoing:[R], blocked:[B] }` |
| `poll` | `{ activity?, lock_day?, shared, receipts?:[RQ], leash_report? }` | `{ ok, online:[id], inbox:[I], receipts:[RC], leash? }` |
| `request` | `{ code }` | `{ ok, status }` status: `sent` `accepted` `already` `not_found` `blocked` `self` `full` |
| `accept` | `{ id }` | `{ ok }` |
| `decline` | `{ id }` | `{ ok }` |
| `cancel` | `{ id }` | `{ ok }` (an outgoing request) |
| `remove` | `{ id }` | `{ ok }` |
| `block` | `{ id }` | `{ ok }` (removes the friendship and any requests both ways) |
| `unblock` | `{ id }` | `{ ok }` |
| `squelch` | `{ id, on }` | `{ ok }` |
| `send` | `{ to, kind, poke?, destination?, code?, watch? }` | `{ ok, status, item_id? }` status: `sent` `squelched` `too_fast` `not_friends` `offline` `refused`; `item_id` (16 hex) only with `sent` |
| `report` | `{ id, reason }` | `{ ok }` |

`F` = `{ id, name, avatar, tier, online, activity, lock_day, last_seen, squelched }`
`R` = `{ id, name, avatar, via, at }`
`I` = `{ id, kind, from, from_name, from_avatar, poke, destination, code, watch, at, expires_at }`
`B` = `{ id, name }`: an account this one blocked, newest 200 (FRIENDS-RECEIPTS v1). A reply
without the key is an older server; the client then falls back to what this PC remembers.
`RQ` and `RC`: see Receipts below.

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
- `destination`: `goon backroom remote ramp chess`; `remote` is grammar only: since 2026-09-28 the
  server answers `refused` to a remote invite and the client neither offers nor shows one
- `code` (only with `goon` or `remote`): `^[A-Z0-9-]{4,12}$`, system-generated join codes
- `code` with `chess`: a Piece by Piece challenge id `^c_[0-9a-f]{16}$` minted by `POST /v2/pbp/challenge` (target = the friend's unified id, allowed only between friends both ways with no block). `send` answers `refused` unless that challenge is the sender's own, still pending, and pointed at `to`. The receiver accepts it with `/v2/pbp/challenge/:id/accept`.
- `watch`: `{ kind, id, title? }`, `kind` in `catalogue` (`id` `^[A-Za-z0-9_-]{1,64}$`), `flavour`
  (`id` in `trance pink frills shiny censored`), `ht` (`id` `^[0-9]{1,8}$`); `title` <= 40 chars,
  letters, digits, spaces and `.,'-` only, stored but never trusted (the receiver resolves the id)
- `activity`: `panel session backroom race goon goon_hosting arcademy breakout deeper remote chess`
  (`chess` added 2026-09-28; a server that predates it stores `online`)
- `reason`: `spam harassment underage impersonation other`
- friend code: `CCP-` + 5 chars from `23456789ABCDEFGHJKLMNPQRSTUVWXYZ`, case-insensitive on input

## Limits (server-enforced, the client mirrors the ones it can)

- 100 friends, 50 incoming requests, 20 requests sent a day, 50 inbox items (oldest dropped)
- one poke per pair per 10 s -> `too_fast`; 30 sends a minute per account -> `too_fast`
- an invite expires 5 minutes after `at` (90 s before FRIENDS-RECEIPTS v1); a poke or watch 24 h
  after; expired items are never delivered. An invite's `joined` / `declined` is still taken for
  30 s past `expires_at`; `expired` only fires after that
- a squelched sender's pokes answer `sent` to the sender and are dropped (they cannot tell)
- a blocked pair: `request` answers `blocked` for the blocker, `not_found` for the blocked;
  `send` answers `not_friends` both ways; inbox items from a newly blocked account are purged

## Presence

`poll` writes `friend_presence:<uid>` = `{ activity, lock_day, at }` with EX 180 when `shared`
is true, else deletes it. Online = key exists. The client polls every 20 s while the drawer is
open or any friend is online and the app is in the foreground, else every 120 s; never when
signed out. `state` is the full list; the drawer calls it on open and every fifth poll.

## Receipts (FRIENDS-RECEIPTS v1, 2026-09-28)

Mirror of `proxy/FRIENDS-RECEIPTS.md` in CCP-Server (#234, leash half #235), friends part. The
leash kinds ride the same channel; their half lives in `Services/Leash/CONTRACT.md`. Additive: an
old desktop never reports a receipt and ignores the reply's `receipts`; an old server ignores
the body's `receipts` and sends none back.

### Ids and states

Every sent item has a 16-hex id: the inbox item's `id`, and the `send` reply's `item_id`.
A squelched send answers `sent` with an `item_id` too, so a squelch cannot be told apart.

| state | set by | kinds |
|---|---|---|
| `arrived` | server, when the recipient's poll drains the item | all |
| `seen` | recipient reports (on screen) | all |
| `joined` | recipient reports | `invite` |
| `declined` | recipient reports (invite) / server on the `decline` op (request) | `invite`, `request` |
| `expired` | server, lazily, once the invite's TTL + 30 s grace passed with no joined/declined | `invite` |
| `accepted` | server, on `accept` or a mutual `request` | `request` |

Order `sent < arrived < seen < joined|declined|expired|accepted`; forward only; the last four are
terminal and rank the same (`ReceiptState.Rank`). A recipient may skip ahead (straight to `joined`).

### Recipient reports (poll body)

```
receipts: [ RQ ]          optional; absent = report nothing
RQ = { id: "<16 hex>", state: "seen" | "joined" | "declined" }      an item (friends or leash)
   | { request_from: "<uid>", state: "seen" }                        a friend request waiting for me
```

- At most 50 a poll are read (`FriendReceipts.MaxReportsPerPoll`), de-duplicated by id / request_from.
- Accepted only from the addressee, only forward, only states the kind can reach, never on a
  squelched item, never while either side blocks the other. Anything else is dropped silently:
  a receipt never fails a poll. A `request_from` seen counts once per request, while it waits.

### Sender receipts (poll reply)

```
receipts: [ RC ]          always present on a new server, may be []
RC = { id, kind, to, to_name, state, at, ref? }
  id       the item id; null for kind "request"
  kind     poke | invite | watch | request | leash_offer | leash_punish | leash_assign | leash_reward | leash_tug
  to       the recipient's unified id
  to_name  the recipient's display_name from its record (never client-sent), or null
  state    see the table
  at       server ISO time of the move
  ref      leash only (the pid or aid)
```

Oldest first, each delivered once (drained). The server keeps the newest 100 per sender for 7 days.
One drain can carry several moves of one item (`arrived` then `seen`): keep the furthest.

### Squelch and block

- A squelched item: the sender hears `sent` + `item_id`, then `arrived` when the recipient next
  polls, never `seen` / `joined` / `declined`; a squelched invite then `expired`s like an ignored one.
- A block purges the blocker's inbox of the blocked side's items and their receipt records: those
  answer nothing, not even `expired`. While either side blocks, no receipt passes between them.
- Friend requests: `cancel` and `block` send nothing back.

### What this client does with them

- Reports `seen` only for things actually on screen, once each (`FriendsSeen`): an invite's knock
  card when it lands, a poke's or a watch's corner notice, a request's corner notice (or the one
  "waiting" notice when it names one person), an incoming request row drawn in an open drawer.
- An invite knocks with `KnockCard`: countdown to `expires_at`, Join (reports `joined`), Not now
  (reports `declined`), the x (puts it away unanswered into the Inbox row while it lives). It
  rings full size for `LandingRules.KnockRingSeconds` (45 s), then tucks to a small strip for
  the rest of the five minutes. A card that runs out says nothing: the server sends `expired`.
- Each friend row shows the latest poke, invite or watch sent to them (`FriendsSentBook`, fed by
  `send`'s `item_id` and `ReceiptsArrived`): sent, arrived, seen, then joined, not now or no answer.
  In memory only, gone 30 minutes after it last moved or on an account change.
- `state.blocked` is the Blocked list. The per-PC `friends_blocked.json` from wave 0 is read only
  when the server sends no list; once it does, the blocks the server lacks are sent once and the
  account's local entries are dropped (`FriendsBlockList.MigrateIfDue`).

### Server keys (read-only here)

`friend_rcpt:<id>` META EX 2 days (leash 4 days), `friend_receipts:<sender>` list LTRIM 100 EX 7 days,
`friend_rcpt_open:<sender>` hash invite id -> expiry ms EX 24 h (the lazy `expired`),
`friend_ghost:<to>` squelched item ids LTRIM 50 EX 24 h, `friend_req_meta:<to>` rows gain `seen_at`.

## Redis keys

`friends:<uid>` set, `friend_in:<uid>` set, `friend_out:<uid>` set, `friend_block:<uid>` set,
`friend_squelch:<uid>` set, `friend_inbox:<uid>` list of `I` JSON (newest first, LTRIM 50),
`friend_presence:<uid>` string EX 180, `friend_code:<code>` -> uid (and `user.friend_code`),
`friend_poke:<from>:<to>` EX 10, `friend_req_day:<uid>:<yyyymmdd>` EX 90000,
`friend_reports` list (`{ by, about, reason, at }`, LTRIM 5000).

Nothing here goes through the profile sync body, the anticheat clamp or the leaderboard cache.
