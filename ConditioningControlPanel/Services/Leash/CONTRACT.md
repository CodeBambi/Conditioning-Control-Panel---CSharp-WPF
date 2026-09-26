# The Leash: the wire (v1, 2026-09-26)

A friend holds your leash. They see your day, set a task, reward or punish. You can cut it any
time with one click. No free text anywhere: every value is a preset id the server validates.
This file is the authority for both halves: `proxy/leash.js` + `proxy/leash-routes.js` in
CC-Labs-llc/CCP-Server and `Services/Leash/*` here. The Friends wire
(`Services/Friends/CONTRACT.md`) is the base; read it first.

v1 = offer, answer, cut, pause, intensity, report, assign, punish, reward, tug. The one-click
remote is v2 (section "v2 remote", not built yet). Collar tag / anniversaries are v3.

Roles on the wire are `holder` and `leashed`. Chrome never says Dom or Sub: it uses names in
sentences ("Vex holds your leash", "on your leash").

## Door

`POST /v2/leash/<op>`, JSON `{ unified_id, ... }`, `X-Auth-Token`. Same door, same replies and
the same 60/min account rate limit as friends (`friends-routes.js`). Server flag
`LEASH_ENABLED=on`; unset = every op answers `{ ok:false, reason:"off" }` and the poll carries no
`leash` block. Free for every signed-in account (the v2 remote part is tier 1).

Both sides must be friends (`friends:<uid>` holds the other). A block, a friend `remove`, a
`report`, or an account merge/delete ends the leash at once (see Ending).

## The poll piggyback (no extra call)

The friends `poll` body gains an optional `leash_report: R` (sent only by a leashed client, only
while leashed). The poll reply gains `leash: B` whenever `LEASH_ENABLED` and the account is
leashed, holds anyone, or has a pending offer; otherwise the key is absent.
While `B` says the account is leashed or holds anyone, the client polls every 20 s whenever the
app is open (the friends 120 s idle cadence is too slow).

```
B = { me: L | null, holding: [H], offers: [O], events: [E] }

L (me, the leashed side) = {
  holder: P, intensity, since, day, dnd_until, remote_mode, video_max,
  pending: [PUN], assignment: A | null, pardons, stickers: [STK] }
H (one leashed friend, the holder side) = {
  who: P, online, intensity, since, day, dnd_until, video_max,
  report: R | null, week: [W x7], pending: [PUN], assignment: A | null, punished_today }
O (an offer to me) = { from: P, at, expires_at }
P = { id, name, avatar }                       (resolved like friends F; never client-sent)
E = { id, kind, from: P, at, ... }            (delivered once, then dropped)
  kind: tug | reward | punish | assign | answered | ended | assign_done | assign_missed | punish_done
R = { day:"yyyymmdd", minutes, quests_done, quests_total, streak,
      chaster_linked, lock_left_s | null, tab_s | null, assign_done, at }
W = { day:"yyyymmdd", c }   c: g (did the work) | x (idle) | r (punished) | t (today)
PUN = { pid, kind, size, watch?, from: P, at, expires_at }
A = { aid, kind, size, watch?, day, status: open | done | missed, at }
STK = { sticker, from: P, at }
```

`day` on L and H is the leash's day number (1 on the day it went on). `since` and every `at` are
server ISO strings. `R` is written ONLY by the leashed account's own client; the holder only
reads it. The server keeps the last R in `leash_report:<leashed>` (EX 3 days), never in `user:`.

## Grammar (anything else -> `refused`)

- `intensity`: `soft` `standard` `strict`
- `remote_mode`: `ask` (default: 5 s countdown card with Not now) `take` (starts at once). v2 uses it.
- `video_max`: an integer 1..90, minutes (default 30): the longest a leash video may run for the
  leashed side. Only the leashed side sets it. Anything else -> `bad_input`.
- `dnd`: `1h` `4h` `today` `off`   (`today` = until the leashed client's local midnight; the client
  sends `dnd_until` ISO with it and the server clamps it to at most 24 h out)
- punishment `kind` / `size`, and the lowest intensity that allows it:

| kind | size (pick one) | meaning at the gate | soft | standard | strict |
|---|---|---|---|---|---|
| `lines` | 3, 5, 10 | complete N lock cards | yes | yes | yes |
| `pink` | 10, 15, 20 | a session of N minutes with the pink filter | yes | yes | yes |
| `bubbles` | 50, 100, 200 | pop N bubbles | | yes | yes |
| `detention` | 10, 20, 30 | a session of N minutes before anything else | | yes | yes |
| `video` | 1..90 (minutes) + `watch` | watch it until it ends or `size` minutes of real watching, whichever is first (friends `watch` grammar: `catalogue` or `ht`); the server lowers `size` to the leashed side's `video_max`, never refuses for it | | yes | yes |
| `chaster` | 900, 1800, 3600 | seconds booked on the leashed player's tab, no gate | | | yes |

- reward `kind`: `sticker` (`sticker` in `good star pet heart wow`), `credit` (size 900 or 1800,
  seconds taken off the tab; waits on the tab like every credit), `pardon` (one token, held max 3),
  `praise` (`poke` in `good proud cute more`)
- assignment `kind` / `size`: `minutes` (15, 30, 60, conditioned minutes today), `quests`
  (1, 2, 3 daily quests done today), `video` (1 + `watch`)

The server refuses a punishment above the leashed side's intensity with `not_allowed`. The
client hides those rows on the holder side (never greyed).

## Ops

| op | who | body | reply |
|---|---|---|---|
| `offer` | holder | `{ to }` | `{ ok, status }` `sent` `already` `not_friends` `cooldown` `full` `taken` `self` |
| `answer` | leashed | `{ from, accept, intensity? }` | `{ ok, status }` `on` `declined` `gone` |
| `cut` | leashed | `{}` | `{ ok }` (always ok, even with nothing to cut) |
| `release` | holder | `{ who }` | `{ ok }` (the holder lets go) |
| `settings` | leashed | `{ intensity?, dnd?, dnd_until?, remote_mode?, video_max? }` | `{ ok, me: L }` |
| `assign` | holder | `{ who, kind, size, watch? }` | `{ ok, status }` `sent` `replaced` `dnd` `refused` |
| `punish` | holder | `{ who, kind, size, watch? }` | `{ ok, status }` `sent` `queued` `not_allowed` `cap` `dnd` `refused` |
| `reward` | holder | `{ who, kind, sticker?, size?, poke? }` | `{ ok, status }` `sent` `full` `refused` |
| `tug` | holder | `{ who }` | `{ ok, status }` `sent` `too_fast` `dnd` |
| `complete` | leashed | `{ pid }` | `{ ok }` (the gate is done; the holder gets `punish_done` with `punishment: { pid, kind, size }` when it was really pending) |
| `pardon` | leashed | `{ pid }` | `{ ok, status }` `pardoned` `none_left` |

`who` / `to` / `from` are unified ids. `dnd` replies carry `dnd_until` so the holder's card can
say until when.

## Rules (server-enforced; the client mirrors what it can)

- One holder per leashed account; a holder holds up to 5 (`full`). `taken` = the target already
  has a holder. An offer lives 7 days; a new offer from the same holder replaces the old one.
- Offer only to a friend online now or seen in the last 7 days (the client greys the chip; the
  server does not check presence).
- After a `cut`, a `release` or a decline, the same holder cannot offer that account again for
  24 h (`cooldown`).
- The leashed side alone sets intensity, any time. Lowering it drops any pending punishment the
  new level no longer allows (dropped, not converted).
- DND: while `dnd_until` is in the future, `assign`, `punish`, `tug` (and v2 remote) answer `dnd`
  and deliver nothing. Rewards still land. DND never ends the leash.
- Punishments: 3 a day per leashed account (UTC day, `cap`); pending queue max 5 (`queued` when
  one is already pending: the gate shows them one at a time, oldest first); each expires 72 h
  after `at` and is dropped silently. `chaster` punishments never queue: they are delivered as an
  event and complete themselves on the client.
- One open assignment at a time; a new `assign` replaces the open one (`replaced`). An assignment
  belongs to the leashed side's local `day` from the latest R. The leashed client reports
  `assign_done: true` in R when it met it; the server marks it `done`, adds an automatic `good`
  sticker from the holder and emits `assign_done` to the holder. When an R arrives with a later
  `day` and the assignment was still open, it is `missed` (`assign_missed` to the holder).
- Week strip W: `g` if that day's assignment was done, or with no assignment the day's R showed
  minutes >= 15; `r` if a punishment was sent that day (r wins over g); `x` otherwise; the
  latest day is `t`. Seven days, oldest first.
- Tug: one per pair per 10 s (`too_fast`).
- Stickers belong to the leashed account: `leash_stickers:<uid>` survives a cut, a release and a
  new holder (max 60, oldest dropped).

## Ending

`cut` (leashed), `release` (holder), block, friend remove, report, account merge or delete all
end the leash the same way: delete `leash:<leashed>`, SREM from `leash_held:<holder>`, drop the
pending queue and the open assignment, set the 24 h cooldown, and emit `ended` to BOTH sides.
The `ended` event carries no reason: the holder side reads "The leash came off". Stickers stay.
`leash_cut` joins `panic` / `emergency_exit` / `safeword` / `unlink` in `TabPrices.NeverPriced`.
Cutting is never priced, never gated, never reachable by the holder, and every leashed surface
(the leashed card, the gate, the v2 remote banner, the tray) has the scissors.

## Redis keys

`leash:<leashed>` JSON `{ holder, intensity, since, dnd_until, remote_mode, pardons }`,
`leash_held:<holder>` set, `leash_offer:<leashed>` hash holder -> at (7 d, field-level expiry
checked on read), `leash_cool:<holder>:<leashed>` EX 86400, `leash_report:<leashed>` R EX 259200,
`leash_days:<leashed>` hash yyyymmdd -> g|x|r (HDEL past 8 days), `leash_pending:<leashed>` list
of PUN, `leash_assign:<leashed>` A, `leash_pday:<leashed>:<yyyymmdd>` EX 90000,
`leash_events:<uid>` list of E (LTRIM 50, drained by the poll), `leash_tug:<holder>:<leashed>`
EX 10, `leash_stickers:<uid>` list of STK (LTRIM 60).

Nothing here goes through the profile sync body, the anticheat clamp or the leaderboard cache.

## The client (v1)

- The leashed client builds R from what it already counts: minutes today from the feature day
  log (`cm`), daily quests done/total, the streak, and `ChasterService.Lock` / the tab only when
  Chaster is linked. `assign_done` is decided by `LeashAssignRule` from the same numbers (video:
  the verified-watch time from the gate's watch).
- The gate: pending[0] stands over the panel at the next idle moment (the Homework gate's busy
  rule: never over a session, game, lock card, fullscreen effect, modal or a hidden panel), and
  every launcher game tile opens it first. Panic, the emergency exit and Cut leash are ALWAYS on
  the gate. Completing the task posts `complete`; the pardon button posts `pardon` when a token
  is held. A `chaster` PUN books `leash` on the tab (+size, row id `leash`, gate Free, only
  through `ChasterService` and its limits) and posts `complete` at once. A `credit` reward books
  `leash_credit` (-size) the same way. When the Homework gate and the leash gate are both due,
  the leash gate goes first.
- Local limits the client adds: none of the effects a leash triggers may enable Strict Lock or
  touch the panic key.
- A video (Hypnotube, or a catalogue entry whose media is a local file) plays in its own window:
  92% of the screen, resizable, F11 fullscreen. The video fills the page and cannot be clicked,
  paused, sought or put in the page's own fullscreen. A punishment window will not close: a close
  shakes it and names the holder. It ends when the video ends, at its cap, on a cut, or on a panic
  press (which always works; the punishment stays pending). A video task (assignment) closes normally.
- Hold to cut: while leashed, holding the panic key (Escape by default) for 5 s anywhere asks
  "Cut the leash?". A held key is one panic press; its repeats are swallowed. It works with the
  panic key switched off. The explainer and the leashed card say so; there is no cut button on the video.

## v2 remote (NOT in v1, recorded so v1 does not paint it into a corner)

`remote` op (holder, tier 1 on the leashed side) -> the server mints a remote session on a new
`leash` tier (the `full` tier minus `enable_strict_lock` and `disable_panic`), stores the code + PIN, and hands them ONLY to the
holder's authenticated CCP, which opens the existing controller page already paired. The leashed
client gets `remote` as an event: `remote_mode: ask` shows "<name> wants the remote" with a 5 s
countdown and Not now; `take` starts at once. Offline: queued, 90 s join window after the next
open, still behind the countdown card. The leashed client refuses `enable_strict_lock`,
`disable_panic` and `start_session {strict_lock:true}` on a leash session even if the server
let one through.
