# The Back Room: CONTRACT (v1, draft for approval, 2026-09-13)

The one document every Back Room lane builds against. If code and this file disagree, this file wins
until it is amended here first.

Sources: `backroom-casino-design.md` (sections 2-4, 10.1, 10.2), `house-book.md`,
`C:\Projects\blender-scripting\slot\{REPORT,FACES,BRIEF}.md`, `_review/slot_contract.json`.
Salvaged patterns: client `feat/backroom-integration` (`casino-request` bridge, `kit/api.js`), server
`feat/backroom-s4-gate` (`resolvePlay` shape, seeded rng, id receipts, lock order, `casinoOpenFor`).
Not salvaged: the chips wallet, the cage, the pot, the Arcademy tier gate.

## 0. Ground rules

- **SP only.** The server owns every balance, every draw and every carried state (melt, unplayed tape).
  The page renders. The host carries messages and fires overlays. Nobody else moves SP.
- **No user file leaves the machine.** The server only ever sees symbol ids (`gif2`, `sub1`). Which GIF
  or word stands behind an id is decided by the host and the page, locally.
- **Law I.** Displays may lie (the balance ticks as the tape plays back). The ledger never does.
- **Law VI.** Back is live at every frame. Nothing waits on the network to let you leave.
- **The glb is read-only.** `slot.glb` and its node names are a fixed contract (section 6). If the page
  needs something the model lacks, it goes in section 9 as a request to the owner.

## 1. Pieces and where they live

| Piece | Repo / path | Lane |
|---|---|---|
| Room page, station loader, bridge | client `Resources/web/backroom/` (`index.html`, `room/`, `bridge.js`, `stations.json`) | C1 |
| Walkable 3D room (`room/scene.js`, `fixtures.js`, `screens.js`, `walk.js`, `hud.js`, `room/assets/`) | same folder | R1 |
| Room asset speed pass | client `Scripts/build-backroom-room-assets.mjs` (reads blender-scripting, never writes to it) | R1 |
| Slot station page | client `Resources/web/backroom/stations/slot/` | C2 |
| Daily Daze wheel station page | client `Resources/web/backroom/stations/wheel/` (`station.md`) | CW |
| Host window + service | client `Services/BackRoom/BackRoomHostService.cs` on `ChaosWebViewHost` | C1 |
| Station request relay | client `Services/BackRoom/BackRoomApi.cs` | C1 (shape), C2 (slot ops) |
| Effect dispatcher | client `Services/BackRoom/BackRoomFx.cs` | C3 |
| Media feed | client `Services/BackRoom/BackRoomMedia.cs` | C4 |
| Slot server | CCP-Server `proxy/backroom-slot.js` (pure), `proxy/backroom-routes.js`, `proxy/scripts/sim-backroom-slot.mjs` | S1 |
| Wheel server | CCP-Server `proxy/backroom-wheel.js` (pure, table v2), `proxy/backroom-wheel-routes.js` | S-wheel |
| Hypno v3: host primitives, gates, kit, Soft Hand and Velvet Vortex stations and routes | see 10.13 (pieces table) | H1, K1, G-*, S-* |

Origins, same scheme as the Arcademy host: `https://ccp.game/` maps `Resources\web` (Deny), page URL
`https://ccp.game/backroom/index.html`, three.js from `https://ccp.game/vendor/three/` (identical build
to the preview's vendored copy, md5 `5708ce5d`). Local media from `https://ccp.assets/` only.
The room page maps `three` and `three/addons/` with an importmap to `/vendor/three/`. The one addon it
added is `addons/libs/meshopt_decoder.module.js` (three r169, upstream md5 `cd7a7b72`, vendored with a
provenance header) for the `EXT_meshopt_compression` glbs in `room/assets/`.
The window is its own `ChaosWebViewHost` (not an Arcademy wing), windowed, `OwnedByMainWindow`, free for
every user. Browser argument string is a constant (AGENTS.md: it must not vary per launch).

## 2. Host <-> page bridge (protocol 1)

Envelope: one flat JSON object with a kebab-case `type`, as in `arcademy/bridge.js`. The page may send
only `ready`, `log` and `exit` before `init` arrives; everything else queues. Requests carry a `reqId`
(page-minted, 16+ chars) and always get exactly one reply, refusals and timeouts included. A page
request never rejects: a missing reply resolves as `{ok:false, reason:'timeout'}` after 6 s.

### 2.1 Page -> host

| type | Fields | Meaning |
|---|---|---|
| `ready` | `protocol:1` | Page booted. Host replies `init`. |
| `log` | `level, msg` | To Serilog, tag `BackRoom`. |
| `exit` | `reason:'back'\|'key'` | Player left the room. Host closes the window after `exit-done`. |
| `exit-done` | | Page has settled (cursor flushed, overlays released). |
| `station-open` | `station` | Station took the screen. Host logs day-log event `e_backroom_<station>`. |
| `station-close` | `station` | Station gave the screen back. |
| `media-request` | `reqId, station, count?, source?` | Deal media for a sit-down (`count` 1..13, default 4; `source` may narrow but never widen, 10.13.C / section 5). Reply `media`. |
| `station-request` | `reqId, station, op, idem?, body` | Relay to the server (section 3). Reply `station-result`. |
| `fx` | `token, fxId, station, symbols?, args?` | Fire an effect (section 4, `args` 10.13.B). Reply `fx-ack`. |
| `fx-tunnel` | `station, level` | Continuous tunnel vision level 0..1, at most 10 a second, no reply (10.13.B). |
| `fx-release` | `token, station` | Fade out what that `fx` token still holds on screen, no reply (10.13.B). |
| `melt` | `station, left` | Current melted spins left, sent whenever it changes. |

### 2.2 Host -> page

| type | Fields | Meaning |
|---|---|---|
| `init` | `protocol, sp, reduced, motion, intensity, gates, lang, lex, stations[], open` | Boot state. `open` = server door flag. `gates` 10.13.A. |
| `balance` | `sp, why:'server'\|'earn'\|'sync'` | Authoritative SP changed outside a station result. |
| `media` | `reqId, gifs[4], words[4], seed` | Sit-down media (section 5). |
| `station-result` | `reqId, ok, status, reason?, body` | Server answer, or a host refusal (`offline`, `closed`, `bad_op`). |
| `fx-ack` | `token, fired[], skipped[]` | What actually played. `skipped` entries: `{prim, why:'busy'\|'unknown'}` (the authored show, 2026-09-15: no setting skips a primitive; `toggle`/`motion`/`calm` are gone). |
| `settings` | `motion, intensity, reduced, gates` | A setting changed while open (`gates` 10.13.A). |
| `suspend` | `on, reason:'panic'\|'focus'\|'minimise'` | Stop audio and fx now; `on:false` resumes. |
| `close` | `reason:'app-exit'\|'panic'` | Host wants the window gone. Page answers `exit-done` within 300 ms; host force-closes at 800 ms. |

Key snippet (page side, the only way a station talks to the server):

```js
// stations never see the auth token, the base url, or a fetch
const res = await bridge.request(
  { type: 'station-request', station: 'slot', op: 'tape',
    idem: mintId(),                     // reused verbatim on retry of the SAME intent
    body: { count: 10, cursor: { tapeId, played } } },
  'station-result', m => m.reqId === reqId, 6000);
if (!res.ok) return refusal(res.reason); // never throws; 'timeout' and 'offline' are reasons too
```

The balance rule the page renders (Law I, THE BANK):

```js
// server balance is already settled for the whole tape; unplayed wins have not "landed" yet
shownSp = serverSp - tape.outcomes.slice(tape.played).reduce((s, o) => s + o.pay, 0);
```

The host adopts `body.sp` into `Settings.SkillPoints` the moment a result arrives (true value), and never
pushes `balance` for a result the page itself requested.

## 3. Server API

Base `https://codebambi-proxy.vercel.app`. Auth like `purchase-skill`: `X-Auth-Token` +
`unified_id`, `validateAuthToken`. No tier gate. Soft-launch door: env `BACKROOM_OPEN=on` or uid in
`BACKROOM_TESTERS` (salvaged `casinoOpenFor`); closed answers `403 {ok:false, reason:'closed'}` before
any lock. Refusals are HTTP 200 `{ok:false, reason}` unless noted.

Host relay whitelist (the only C# that changes when a station is added, one row per station):

```csharp
static readonly Dictionary<string, (string Method, string Path)[]> Ops = new() {
  ["slot"] = new[] { ("GET","state"), ("POST","tape"), ("POST","cursor") },
  ["wheel"] = new[] { ("GET","state"), ("POST","spin") },
  ["cards"] = new[] { ("GET","state"), ("POST","deal"), ("POST","hit"), ("POST","stand"), ("POST","double"), ("POST","split") },
  ["roulette"] = new[] { ("GET","state"), ("POST","spin"), ("POST","cursor") },   // 10.13.E
};
// op -> {METHOD} /v2/backroom/{station}/{op}
```

### 3.1 `GET /v2/backroom/slot/state`

```json
{ "ok": true, "sp": 57, "open": true,
  "melt": 2,
  "tape": { "id": "t_9f3c...", "played": 4, "outcomes": [ /* full tape, see 3.2 */ ] },
  "shown": ["gif1", "spiral0", "sub3"],
  "table": { "v": 3, "stake": 1, "freezeCost": 1, "jackpot": 2500, "rtp": 1.02, "rtpFrozen": 1.02,
             "lines": [ { "id": "emi3", "pays": 2500, "odds": "1 in 25,000" }, "..." ] },
  "strips": [ ["gif0","spiral0","sub0", "...13 ids"], ["..."], ["..."] ],
  "floorMs": 3000 }
```

`table.lines` is the published odds (10.1) and is what the page prints. `strips` are server-owned and
fixed per table version; the page paints reel cells in exactly this order.

### 3.2 `POST /v2/backroom/slot/tape`

Request:

```json
{ "unified_id": "...", "idem": "32-hex", "count": 10,
  "cursor": { "tapeId": "t_9f3c...", "played": 10 },
  "freeze": { "col": 1 } }
```

- `count` 1..20 paid spins. The page default is `defaultTapeCount(sp) = clamp(floor(sp / 5), 1, 10)`
  (16 SP -> 3, 50+ SP -> 10); a manual pick goes up to 20. The page never offers a count above what
  the balance affords. With `freeze`, `count` must be 1.
- Cost: `count` SP, or `1 + freezeCost` for a freeze spin. `sp < cost` -> `insufficient` + `sp`.
- **Plain tape:** refused with `tape_unplayed` (body carries the stored tape) unless
  `cursor.played === stored.outcomes.length`. The page just resumes that tape.
- **Freeze:** allowed mid-tape. The held column shows `shown[col]` as of `cursor.played` (or the last
  freeze outcome if newer). It draws from the frozen table, is appended to a `freezes` list, and never
  voids or reorders the tape.

Response:

```json
{ "ok": true, "idem": "...", "sp": 51, "spBefore": 57, "cost": 10, "capped": false,
  "melt": 0, "jackpot": 2500,
  "tape": { "id": "t_a771...", "played": 0, "outcomes": [
    { "i": 0, "kind": "paid",  "stops": [3, 7, 11], "symbols": ["gif1","gif1","gif1"],
      "line": "gif3same", "pay": 40, "halved": false, "meltLeft": 0, "freeLeft": 0,
      "fx": ["fx.gif_storm"], "subs": [] },
    { "i": 1, "kind": "paid",  "stops": [9, 0, 5], "symbols": ["sub0","spiral0","melt"],
      "line": "melt", "pay": 0, "halved": false, "meltLeft": 3, "freeLeft": 0,
      "fx": ["fx.melt", "fx.sub_single"], "subs": ["sub0"] },
    { "i": 2, "kind": "free",  "...": "free spins and re-spins are expanded inline, cost 0" }
  ] } }
```

Outcome fields: `kind` = `paid | free | respin | freeze`; `line` = a section 4 id or `none`; `pay` is
after melt halving (`floor`); `meltLeft`/`freeLeft` are the state AFTER this outcome; `fx` is the ordered
effect list; `subs` lists the subliminal symbols showing (they always play, rule 10.2).

### 3.3 `POST /v2/backroom/slot/cursor`

`{unified_id, tapeId, played}` -> `{ok}`. Presentation only (the tape is already settled). Sent by the
host on `exit`/`close` and on `station-close`; also folded into every `tape` request. A lost cursor
means the player re-watches a few settled spins, nothing more.

### 3.4 Draw, settle, persist

- **Storage:** user record field `backroom` = `{ slot: { melt, tape, freezes, shown, n, nextBuyAt, netSp } }`
  on `user:<uid>`. Receipts in `backroom:ids:<uid>` (hash, 30-day TTL, trimmed above 5,000).
- **Locks (salvaged order):** `purchase_lock:<uid>` (NX EX 10, shared with purchase-skill so SP never
  races a skill buy) -> `backroom_lock:<uid>` (NX EX 5). Miss -> `409 {reason:'busy'}`.
- **Idempotency:** `idem` matches `^[A-Za-z0-9_-]{16,64}$`. A seen `idem` returns the stored receipt
  byte-identical, no second debit. Check the receipt before the rate floor.
- **Rng:** seeded, never `Math.random`: `rngFor(uid, n, 'slot')` where `n` is the lifetime draw counter
  (salvaged FNV-1a + mulberry32). Outcome class first, then dress the stops to match (salvaged
  `drawTripleClass` -> `dressTriple` shape). The malus only dresses onto a `none` class.
- **Order:** melt is consumed in DRAW order. A freeze bought mid-tape sees the melt left at the END of
  the stored tape. Playback may show a stale melt count for a few spins (Law I, display only).
- **Rate floor:** `nextBuyAt = now + outcomes.length * SLOT_FLOOR_MS` (3000 ms default, env; 10.12). A plain
  tape before `nextBuyAt` -> `{ok:false, reason:'too_fast', retryInMs}`; the page waits it out silently.
  A freeze spin checks a 3000 ms floor against the last buy. Plus the per-account 60/min limiter.
- **Cap (decided):** the SP cap rises from 9,999 to 99,999 for everyone. Server `SKILL_POINTS_CAP` and
  every client clamp (AppSettings, ProfileSyncService merge, anti-cheat) move together in one change,
  ahead of the slot. At 99,999 winnings above the cap are lost and `capped:true` is returned.
- **Sync safety:** `netSp` (won minus spent, lifetime) is added to the SkillPointBackfill sum exactly like
  the old `casino_sparkle_spent`, so a later profile sync never refunds a tape or erases a win. The
  client never sends SP to these routes.
- **Paytable is server code** (`backroom-slot.js` `TABLE_V3`), versioned. The client has no fallback
  table: if `state` fails, the cabinet shows "closed for a moment" and Back.

### 3.5 Daily Daze wheel (`/v2/backroom/wheel/*`, amended 2026-09-14)

One free spin per account per UTC day (owner decision, wheel option A). No stake, no floor enforced.

- `GET state` -> `{ ok, sp, open, day:"YYYY-MM-DD", spun, result|null, snoozeCarry, nextResetAt, jackpot:{ amount,
  odds:"1 in N"|"never", wonToday, eligible }, slices:[ { id, label, pay, width, odds } ], floorMs }`. `width` is the
  drawn width in degrees (the picture), `odds` the published chance (the ledger; `"never"` when it cannot be won
  now). The jackpot slice's `pay` is the day's pot. Slices carry no kind: `jackpot` and `snooze` are known by id.
- `POST spin {idem}` -> `{ ok, sp, result:{ day, sliceId, sliceIndex, pay, snoozeCarryPaid, jackpot, jackpotFallback,
  snoozed, total, capped }, jackpot, snoozeCarry, nextResetAt }`. `total` is pay + carry before the cap; `sp` is
  the balance after it.
- Refusals: `already_spun` (+ `result, sp, jackpot, snoozeCarry, nextResetAt`), `bad_request`, and `busy` and
  `too_fast` as HTTP 200 (not the slot's 409); `closed` is 403.
- The page lands on `result.sliceIndex` whatever the drag, seeded inside the drawn slice by the day, and shows the
  stored landing and a countdown on reopen. ~~Effects are existing ids only~~ SUPERSEDED by 10.13.F (wheel): the landing
  fires the v3 moments sized by `wheelSize(result)`, no section 4 ids. Details: `stations/wheel/station.md`.

## 4. Lines and effect ids

A spin pays its single best line, top row first. Class counts are over the three payline symbols.

| Line id | Rule | Pays (draft, sim tunes) | fx (in order) |
|---|---|---|---|
| `emi3` | 3 EMI | jackpot 2,500 | `fx.jackpot` |
| `gif3same` | 3 of the same GIF | 40 | `fx.gif_storm` |
| `sub3` | 3 subliminals | 15 | `fx.sub_cascade` |
| `spiral3` | 3 spirals, any mix | 10 + 3 free | `fx.spiral_full` |
| `gif3` | 3 GIFs, any mix | 3 | `fx.gif_burst` |
| `sub2` | exactly 2 subliminals | 2 | `fx.sub_pair` |
| `spiral2` | exactly 2 spirals | 1 + 1 re-spin | `fx.spiral_brief` |
| `melt` | malus on a no-pay spin | 0, melt 3 | `fx.melt` |
| `none` | nothing | 0 | none (page: SHIVER + muted thud) |
| (overlay) | any subliminal showing, not already in the line fx | 0 | `fx.sub_single` |

Effect ids are global (every station reuses them) and resolve on the host into primitives. **The Back Room is an
AUTHORED show (owner direction, 2026-09-15):** every fx id always plays its full recipe. The app's Flash /
Subliminal / Spiral / Brain Drain / Tunnel toggles and the room's own switches gate NOTHING here. One authored row
per id; `Full` keeps the counts and stretches every duration x1.3; `Calm` is "gentle": every opacity and strength
x0.5, every duration kept, never a step skipped.

| fxId | Authored recipe (Normal) | `args` it reads |
|---|---|---|
| `fx.jackpot` (hero, 4 s) | `spiral-full` 4 s at 0.7; then at 4 s: `flash-burst` 8 (medium, opacity 1.0, 300 ms apart), `gif-rain` 4 s (19 GIFs at 0.9), `glitch-bubbles` 3, `sub-burst9` twice (the second right after the first), `gif-full` 2 s at 0.8 | |
| `fx.gif_storm` | `flash-burst` 5 + `gif-rain` 14 GIFs over 3 s at 0.9 + `glitch-bubbles` 1 | |
| `fx.sub_cascade` | `sub-burst9` (9 words, onsets 350 ms apart) then `gif-full` 1.5 s at 0.8 | `wordsShown` |
| `fx.spiral_full` | `spiral-full` 4 s, alpha 0.7 | |
| `fx.spiral_brief` | `spiral-full` 1.5 s, alpha 0.55 | |
| `fx.gif_burst` | `flash-burst` 5 (medium images, opacity 1.0, 300 ms apart) | `count` 1..8 (the slot's GIF tease sends 1) |
| `fx.sub_pair` | `sub-seq` 2 then `spiral-full` 1.5 s at 0.55 | `wordsShown` |
| `fx.sub_single` | `sub-single` per word, onsets 500 ms apart | `wordsShown` |
| `fx.melt` | `brain-drain-melt` 6 s, alpha ramping 0 to 0.8 | |

Every word is VISIBLE then FADES, never a blink: in 80 ms, hold 400 ms, out 350 ms, full opacity. Every spiral fades
in over 250 ms and out over 500 ms (the same for `fx.loom_spiral`); a fullscreen glitch pulse is 600 ms at 0.35; a
wash is 900 ms at default strength 0.7. `args.wordsShown: true` on the three word ids means the page rendered the
words itself: the host leaves the word steps out and plays only the rest (single: nothing; pair: the spiral; cascade:
the fullscreen GIF), each at its authored offset.

Exactly two safety lines remain, and neither one skips a step:
- **Reduced motion** (the effective `MotionLevel` below `Full`, which is also where the OS animation flag lands):
  flash onsets are capped at 3 Hz (334 ms apart) and every spiral plays its slower variant (the weave at half speed).
  Nothing is stilled, renamed or dropped.
- **Calm**: every opacity and strength x0.5, every duration kept.

Full never breaks the Brake: no strobe over 6 Hz, one hero at a time.

Primitives (C3 maps them onto existing services; `gif-full` is the only new overlay):

| Primitive | Service call |
|---|---|
| `flash-burst n` | `App.Flash.TriggerFlashOnce(n, FlashDuration, 100, true, new FlashBurstLook(opacity, gapMs))` with the dealt GIFs: medium images, authored opacity, authored stagger |
| `gif-rain s` | `ChaosGifCascadeOverlay.Show(...)`, the count over the seconds at the authored opacity |
| `glitch-bubbles n` | `ChaosFlashOverlay.Show(600, 0.35)` per pulse, 600 ms apart |
| `sub-single` | `App.Subliminal.FlashSubliminalCustom(text, opacity, 400, true, 80, 350)` |
| `sub-seq n` / `sub-burst9` | the same, at 500 ms / 350 ms per word (the pacer's floor is the burst gap, across fx) |
| `spiral-full s` | ~~`OverlayService.ShowOverlayTimed("spiral", ms, opacity)`~~ amended 10.13.B: plays `spiral-loom` (preset `screen`), a Loom-woven spiral, at the authored alpha and fades |
| `brain-drain-melt s` | `ShowOverlaySustained("braindrain_melt", ...)` ramped 0 to the authored alpha over the step, then released |
| `gif-full s` | fullscreen single-GIF overlay across screens at the authored opacity |

Rules the host enforces, not the page:
- No primitive is ever skipped for a setting. The only skips are `busy` (the hero queue, the Brake gaps) and
  `unknown` (an id the host does not know, or media it does not have).
- Setting `AppSettings.BackRoomFxIntensity` = `Calm | Normal | Full` (default `Normal`), shown in the room and in
  Settings. It is never forced by the motion level.
- One hero at a time (Brake 2): an `fx` arriving while a hero window runs is queued, merged if it is the
  same id, and dropped after 4 s in queue (`busy`).
- `suspend` or `close` cancels every running primitive the room started.
- Words and GIFs in `symbols` are resolved from the host's own dealt `media` by key. Unknown keys are
  replaced with a random dealt item; nothing from the page is used as a path.

## 5. Media feed

```json
{ "type": "media-request", "reqId": "...", "station": "room", "count": 8, "source": "online" }

{ "type": "media", "reqId": "...", "seed": 918273, "source": "online",
  "gifs":  [ { "key": "g0", "url": "https://ccp.assets/<folder>/<file>.gif", "w": 480, "h": 270, "src": "pool" },
             { "key": "g1", "url": "https://ccp.assets/.temp/ccp_temp_remote_<guid>.webm", "w": 338, "h": 450, "src": "online" },
             { "key": "g3", "url": "https://ccp.game/backroom/stations/slot/fallback/gif3.webp", "src": "fallback" } ],
  "words": [ { "key": "s0", "text": "Drop", "src": "preset" }, { "key": "s1", "text": "...", "src": "pool" } ] }
```

**Where the pictures come from (the source).** `AppSettings.BackRoomMediaSource` is the room's OWN
choice, separate from the app-wide `MediaSource`: `auto` (follow the app, the default) | `local` |
`online` | `mixed` | `bundled`. `BackRoomHostService.EffectiveMediaSource` is the one resolver, and it
is also where `online` / `mixed` collapse to `local` without `HasRemoteMediaConsent` - the deal never
decides that for itself. `media-request.source` may NARROW the resolved source; it can never widen it
past consent. The `media` reply echoes `source` as what the deal actually resolved to, so the room's
picker can tell the player what they GOT rather than what they asked for.

- `local` deals from `App.Flash.SnapshotLocalImagePaths` filtered to local animated files
  (`IsRemotePath` false), deduped by FULL PATH (trap 147), shuffled with `seed`; shortfall filled from
  built-in fallback art. `bundled` deals that fallback art ON PURPOSE, even with a full library.
- `online` and `mixed` deal from a warm pool the host fills from the player's Scrolller niches
  (`BackRoomMediaSubs` minus `BackRoomMediaSubsOff`, capped at `BackRoomMediaSubCap`; an empty list
  follows the app's own picker). `mixed` rolls `RemoteMediaRatio` per pick. The fetch is the same
  `Services/Fyp/Online` stack the flashes use, on the user's device, direct from the provider: nothing
  routes through CC Labs infrastructure (`IFeedSource`). Degrades one way only - no warm pictures
  falls back to stills, then `local`, then `bundled`. A wall showing a still is fine; a wall showing
  nothing is a bug.
- Remote entries are materialized to a real file first (`RemoteMediaCache.MaterializeAsync`), which
  lands under `App.GetMediaTempPath()` - that is `{EffectiveAssetsPath}\.temp`, INSIDE the `ccp.assets`
  mapping. So a remote picture is an ordinary `ccp.assets` url: CORS-clean for WebGL, allowed by the
  page's own `allowed()` check, and still resolvable back to a file for the host's fullscreen
  `gif_from` / `gif_full` / `wash`. Materialize ONCE PER PROVIDER ENTRY ID and keep the path: every
  call mints a new guid, and the page's url-keyed dedupe cannot collapse two paths for one picture.
- `src` is `pool` (the player's own files), `online` (Scrolller) or `fallback` (built-in art).
  `room/screens.js` hangs anything that is not `fallback`.
- **Clips are for the WALLS, stills for the STATIONS.** Scrolller's clip feed is webm/mp4 and its
  picture feed is static, byte-verified (`RemoteMediaFormats`): a remote STILL never animates. The
  desktop host is WebView2, i.e. Chromium, so it plays a clip natively and needs no transcode hop -
  `room/clip-source.js` paints it into a canvas on the room's own `tick()` clock and hands back the
  same source shape a decoded GIF does. Only `station: "room"` gets clips. The card table deals up to
  13 media per sit-down and the slot paints into three WebGL reel textures; thirteen decoding videos
  is not a trade worth making, and `stations/slot/media.js` decodes through `decodedSource` with an
  `<img>` still fallback, neither of which takes a webm.
- Up to 4 words from the active `SubliminalPool` (mode/mod variant as the app uses), shortfall filled
  from presets `Drop, Relax, Let Go, Sink` in that order. Preset text is a lexicon key (Law VII).
- Symbol id -> media: `gif0..gif3` = `gifs[0..3]`, `sub0..sub3` = `words[0..3]` (a 13-GIF deal: 10.13.C). Dealt once per sit-down
  and kept until the player stands up, so a reel cell never changes face mid-tape.
- URLs point only at `ccp.assets` (the user's folders and its `.temp` materializations, mapped
  read-only) or `ccp.game`. Keys, never paths or URLs, go back to the host in `fx.symbols`. Nothing in
  `media` is ever sent to the server. The host logs COUNTS only: never a path, never a url, and a
  remote host only through `Logging.UrlLog.Host`.

## 6. Slot station and the glb contract

Assets copied (never edited) from `blender-scripting/slot/out/` into `stations/slot/assets/`:
`slot.glb` (627,560 bytes), `emi-faces-slot.png`, `emi-face-map.json`. Room art from `slot/refs/`:
`backroom_final.png` into `backroom/room/` (no longer drawn by the room; it is the EMI ring card art).

The 3D room does NOT load the station's `slot.glb`. It reads optimized copies built by
`Scripts/build-backroom-room-assets.mjs` into `room/assets/` (`shell.glb`, `slot.glb`, `wheel.glb`,
`counter.glb`, `card-table.glb`, `roulette.glb`, `ads/*.webp`). The copies keep every node name the room
looks up, checked by the script and by `smoke/room-check.mjs`: `ceiling`, `spiral_inlay`,
`media_screen_0..3`, `sconce_globe*`, `lights_chase_*`, `bulb_*`, `canopy_bulb_*`, `rim_bulb_*`,
`EMI_glass`, `marquee`, `screen_jackpot`, `screen_status`, `reel_1..3`, `title_screen`, `status_screen`,
`center_spiral`, `inset_spiral_*`, `emi_dealer`, `golden_emi_attendant`, `alcove_return*`. Everything else
in those copies is joined per material and may lose its name. The room never moves a node that carries
a mesh (quantized offsets): the floor turns in its shader and reels rest by texture offset.

Driven nodes (verified present in the glb 2026-09-13): `cabinet`, `reel_1..3` (X axle, `reel_mat_1..3`),
`reel_window`, `lever` (hinge empty, X axis), `freeze_1..3` (`freeze_mat_1..3`), `screen_jackpot`,
`screen_status`, `marquee` + `marquee_glow`, `lights_chase_00..29`, `payout_tray`, `payout_spawn`,
`emi_topper` (face material `emi_face`), `cam_seat`, `cam_target`.

Optional too (the page degrades with a console warning when either is missing): `wand_head`, the wand
tip the room reads to find where a chess handle sits on the lever, and `wand_switch`, its companion in
the same wand assembly, masked out of the merged cabinet geometry with it.

- Reel cells: 13 cells along U, painted from `strips[r]` of `state`, rotated and reflected exactly as
  `preview/app.js` `paintStrips` does. Stop `k` rests at `((k + .5) / 13 - .5) * 2PI`.
- Face atlas: `repeat (151/1672, 136/137)`, `offset ((i*152 + .5)/1672, .5/137)`, Nearest, no mips.
  States: `idle0_0`=3, `hearts`=7, `spirals`=8, `melt`=9, `jackpot`=10.
- Screens and marquee are runtime canvases; the marquee text is a lexicon key (slot name is open).

## 7. Stations: how a future station plugs in

`backroom/stations.json` is the registry the room reads at boot:

```json
[ { "id": "slot", "variant": "violet", "name": "Candy Violet", "labelKey": "br_station_slot_violet",
    "state": "live", "entry": "stations/slot/station.js",
    "approach": [-3.95, 1.65, 2.3], "look": [-5.9, 1.4, 2.3],
    "fixture": { "file": "slot.glb", "position": [-5.9, 0.026, 2.3], "yaw": 1.5708, "scale": 1.1,
                 "palette": { "candy_rose": "ac83ed", "...": "rrggbb" },
                 "faces": true, "reels": true, "labels": { "marquee": "@name" },
                 "bounds": { "min": [x, y, z], "max": [x, y, z] } } },
  { "id": "wheel", "name": "Daily Daze", "labelKey": "br_station_wheel", "state": "live",
    "entry": "stations/wheel/station.js", "...": "..." } ]
```

One row per fixture, metres, y up (numbers from blender-scripting `backroom/out/placements.json`). Rows:
`counter` (The Prize Parlour), `wheel` (Daily Daze), `slot` x3 (`rose`, `violet`, `mint`: the SAME
station in three colours), `cards` (Soft Hand, Twenty-One), `roulette` (Velvet Vortex). No scratcher.
`id` is the station (and the host `Ops` key); `id` + `variant` is unique per row. `approach` is where
E works (within 1.65 m) and where the room view drops you; `look` is what you face there; `bounds`
(plus a 0.25 m body radius) is the fixture's collision box. Optional fixture fields: `heightScale`,
`preserveCharacter {name, factor}`, `omitPrefixes`, `palette`, `faces`, `reels`, `labels`
(node -> lexicon key, `@name` = the row's label), `hub` (the roulette idle spiral). `soon` rows get the
fixture, a Visit prompt and a dust-sheet card, and never load code.

Station module shape (the room calls nothing else):

```js
export async function mount(ctx) {
  // ctx = { root, bridge, request(op, body, idem?), fx(fxId, symbols?, args?), media({count}?), sp(), onSp(fn),
  //         reduced, motion, intensity, lex(key, fallback), standUp(), variant, hostBack, spReadout,
  //         gates, onSettings(fn), fxTunnel(level), fxRelease(token) }   // the last four and args/count: 10.13
  return {
    open(),                 // take the screen; resolve when interactive (Back is live before this)
    close(),                // Promise, settles within 420 ms, flushes its own cursor
    suspend(on),            // stop audio/fx
    destroy(),              // free WebGL context, textures, listeners
  };
}
```

- A station owns ONE WebGL canvas, created in `open` and disposed in `close`. The room keeps its own
  context while a station is open, so at most TWO contexts are alive (the room, held and not drawing;
  the station, drawing), never two drawing. Amended 10.13.D: plus the hypno kit's ONE shared Loom context,
  created on first use inside `open` and freed in `close`, so at most three alive and two drawing. Its glb, textures and node contract live in its own folder
  with its own `station.md`.
- `ctx.variant` is `{ id, name, palette }` for a row with a `variant` (palette = material name ->
  `rrggbb`, null for the base colour) and `null` otherwise. Honouring it is optional: a station that
  ignores it shows its base colours. For C2: recolour the close-up cabinet by material name the way the
  room does (`material.clone(); color.set('#' + palette[name])`) and put `variant.name` on the marquee.
- `ctx.hostBack === true` means the room draws the only Back (the HUD chip, always on top). The station
  hides every Back of its own (chip and card buttons) and still stands up on Escape. A standalone
  harness passes nothing and keeps the station's own Back.
- It talks to the server only through `ctx.request`, which becomes `station-request` with its id.
- It fires only global fx ids. A new effect is a new row in section 4 plus a C# recipe; unknown ids are
  skipped with `why:'unknown'`, so a page can ship ahead of the host.
- Server side, a station adds `proxy/backroom-<id>.js` (pure) and routes under `/v2/backroom/<id>/`,
  its state under `user.backroom.<id>`, and reuses the shared helpers: auth, door flag, locks, receipts,
  floor, cap, `netSp`.
- Adding a station touches: `stations.json` (one row), the host `Ops` table (one row), the fx table if
  it needs new effects, and its own folders. The room, bridge and window never change.

### 7.1 3D room amendments (2026-09-13, R1)

- **Walk.** WASD or arrows (Shift runs at 4.8 m/s, else 3.25), drag to look, E visits the nearest station
  within 1.65 m, M toggles the room view (ceiling off, one button per station that drops you at its
  approach). Back / Escape closes, in order: an open station or card, the room view, the room. No touch
  stick and no pointer lock (desktop WebView2 is the target).
- **Leaving a seat.** Seated at a station, a tap that lands on the room instead of the station (the floor,
  a wall, another fixture, empty air) and a step backwards (S, the down arrow, the touch stick pushed back)
  both go through the same Back path as the chip: the station settles first (Law VI), then the camera walks
  back to where you stood. A tap on the station's own meshes, its runtime dressing or its own controls never
  leaves, and neither does a drag past the room's tap slop.
- **Visiting.** E holds the room: pose saved, render loop stopped (no rAF), context KEPT. Measured in the
  smoke run: about 40 ms from Back to a drawing frame, against about 1.1 s to boot and decode the room
  again, so the room keeps its context. Back puts you on the exact position and facing.
- **Still.** The floor spiral, bulb chase, wall-picture turn and roulette hub freeze and the head sway is
  off whenever `reduced` is true or `intensity` is `calm` (the Motion button is locked then), or when the
  player picks Motion still. A `settings` frame applies live.
- **Wall screens.** One `media-request` with `station: "room"` at boot. `gifs` that are not
  `src: "fallback"`, point at `ccp.assets` or the page's own origin, and load CORS-clean go on the room's
  own four wall screens (`media_screen_0..3`) and on whatever Room Service has switched on, which is two
  further packs of wall screens, four and six, and the ceiling projection; one turn every 18 s on the wall
  screens, 4.5 s on the ceiling projection. Otherwise the house art (`room/assets/ads/*.webp`) with lexicon captions. The preview's local
  file picker is not carried over.
- **Playing GIFs (amended 2026-09-13).** Dealt GIFs play, decoded in the page with WebCodecs
  `ImageDecoder` (Chromium 94+, no vendored decoder; a page without it shows the first frame). Caps: a
  picture advances only while a screen showing it is inside the camera frustum (not in the room view,
  not while a station holds the room), at most 12 frames a second, into a canvas texture of at most
  384 px on the long edge, and at most one new decode per rendered frame. Still (reduced, Calm, Motion
  still) shows the first frame.
- **The SP chip (`ctx.spReadout`, amended 2026-09-14).** The room's HUD chip is the only SP on screen
  and the room owns its rule: it always shows Law I `shownSp` = `state.sp` minus what the tape still
  owes, on every repaint (init, a `balance` frame, a `station-result` that carried `sp`, a station
  closing, a station reopening). `ctx.spReadout` is `{ set(value), owe(n), thud(), target() }`:
  `owe(n)` is the pays still unplayed on the tape, a number or a reader `() => n` the room calls on each
  repaint while the station is open; `set(value)` shows a number as is while THE BANK flies and
  `set(null)` goes back to the rule; `thud()` is the landing mini-thud on the chip (lit, not scaled, when
  reduced); `target()` is the chip's box for token flights. When a station closes the room freezes a
  reader to its last answer and drops any `set` value, so the chip neither dips on Back nor on reopen
  before the station has its tape. A station hands over a plain number in `close()` and registers its
  reader only once its tape state is back. A `station-result` repaints the chip on the next frame, after
  the station has adopted the same reply. Stations never look the chip up by id or observe it.
- **HUD keys (amended 2026-09-14).** Room HUD buttons drop focus on pointerup, and while walking or with
  a station open, Space and Enter on a HUD button do nothing (the station's keys are the station's).
- **Room Service props (amended 2026-09-15).** Every one of the ten catalogue props has a fixed spot in
  the room, not only a bay in the cabinet: the ROOM SERVICE machine on the right wall by the entrance
  (walk up and press E, or click it), the three chess sculptures on their pedestals, three plants (the
  entrance monstera, the northwest hanging ivy, a terrarium on a plinth beside the Prize Parlour) and
  three wall frames (two above the left-wall booths, the wide billboard over the door). The six
  decoration props are on when the room opens and switch off and on again from the panel, session only:
  no persistence and no bridge message, like every other Room Service change. The spots are the table in
  `room/assets/customization/README.md`. No station, cabinet or screen moves for them, the approved
  left-wall booth positions are untouched, and the only walk volumes added are the two floor plants.
- **Room Service, one button (amended 2026-09-15).** Picking an item in the cabinet IS the Use: it goes on
  the focused pedestal or cabinet there and then, and the only control under the miniature is a single
  Remove button, shown while that piece is fitted there and gone when it is not. A right-click on an item
  takes that piece off wherever it sits, and no browser menu opens over the stage. A tap on the lever
  close-up pulls that cabinet for fun: the handle swings on its own axle and the drums roll to a random
  face. No server call, no SP, no outcome, no effects, and nothing reads where they land.
- **Budget.** At 1280x720 on the entry pose: 277 draw calls with the whole Room Service collection placed
  (264 before it; the preview draws 1,268 there, 1,312 in its own check), no shadows, no post passes,
  pixel ratio capped at 1.5.

## 8. Feel hooks (for F1, cited from THE HOUSE BOOK)

- Law VIII: lever, freeze and Back answer within 100 ms, before any network reply.
- THE THUD per reel stop (340 ms, `cubic-bezier(.2,1.5,.4,1)`), left to right.
- THE BANK: tokens fly `payout_spawn` -> SP readout; the readout ticks per landing, never before.
- THE CHIME LADDER on wins, one family; drops an octave while `meltLeft > 0` (Brake 5).
- THE MASCOT GLANCE: EMI face within 100 ms, never the same pose twice in a row.
- THE BREATH: exactly one element (the lever handle at rest), paused during celebrations.
- Brake 3: repetition shrinks the party. Brake 6: `none` gets a shiver and a muted thud, never silence.
- Law VI: reduced motion takes the settled STATE; a tape still plays one outcome per press.
- THE ALMOST (A1 + A2, 10.15): a live pair on reels 1 and 2 holds reel 3 (900-1,400 ms, halved while melted), and a
  no-pay spin one cell off the line ghosts that cell gold and snaps back once. The tape already holds the outcome.
- THE BANK, proportional (A3, 10.15): the count-up is sized to the win (500 ms to about 6 s), the chime ladder
  climbs with it, one press settles it at once (Law VI, Brake 7).
- Law VIII (A4, 10.15): attract mode drifts after 25 s seated idle and any press ends it inside 100 ms. No SP moves.
- THE MASCOT GLANCE (A5, 10.15): EMI wiggles wherever she lands, after that reel's thud, and glances.
- Law IX (A6, 10.15): a win frames its own row and the frame pulses for the length of the rollup.
- THE BANK and deck V (B1, 10.16): the spiral jar ticks on each spiral's own thud (Law X), and a full jar is a
  tier 2 party plus `fx.spiral_full` before its 3 free spins play. Reduced motion takes the settled fill.
- Brake 1 (B2, 10.16): the floor bell is one line of text at a time, rotating every 8 s, never a sound. Somebody
  else's win is not the viewer's party.
- Brake 1 (B3, 10.16): the welcome-back comp arrives as a glance and one chime, never a fanfare. Arriving is not
  an earned moment.
- THE ALMOST and Law IX (C1, 10.16): an `emi2` pair takes A1's full 1,400 ms gold hold and never halves it, gets
  no shiver and no ALMOST tell, and the re-spin's `emi3` plays THE REVEAL as any jackpot does.
- Law I (C2, 10.16): "MUST HIT" reads off the server's `jackpot.mustHit`, in the wheel station and on the room's
  jackpot chip. No new fx.

## 9. Owner decisions (2026-09-13) and requests

1. **Freeze:** ONE column per spin (`freeze.col`), 2 SP. The page lights only one freeze button at a
   time; picking another moves the hold. (The preview's multi-freeze is not carried over.)
2. **Cap:** raised to 99,999 everywhere (section 3.4). Clip and flag above it.
3. **Soft launch:** door flag `BACKROOM_OPEN` + `BACKROOM_TESTERS`.
4. **Tape size:** default `defaultTapeCount(sp)` (at most 10, shorter on a small balance), manual max 20.
5. **Intensity:** `Calm | Normal | Full`, default `Normal`.
6. **Model requests:** none. Every node the page drives is present. Station approach points and bounds
   come from blender-scripting `backroom/out/placements.json`, not a model change.

## 10. Checkpoint 1 amendments (2026-09-13)

Where these disagree with the sections above, these win.

1. **Table v5:** `emi3` pays 400 at 1 in 12,987 (was 2,500 at 1 in 25,000). Paytable constant `TABLE_V5`.
   Superseded by table v6 (10.14).
2. **Freezes are sealed from melt:** a freeze spin and everything it expands into neither consume nor
   start melt; `melt` comes back exactly as it went in.
3. **A re-spin keeps the hold:** a `spiral2` re-spin won on a freeze spin repeats the frozen table with the
   same held column.
4. **A held melt is a blank:** a melt symbol in the held column is carried, not drawn, and counts as a
   blank on the payline, never a malus.
5. **`freeze.col` is 0-based:** 0, 1 or 2.
6. **Keyed seed:** `rngFor(key, uid, n, 'slot')` = HMAC-SHA256 under `BACKROOM_SEED_KEY` (server only,
   32+ chars). Without the key the door stays shut for everyone, testers included.
7. **Third lock:** after `purchase_lock` and `backroom_lock`, `user_write_lock:<uid>` (NX EX 5), the lock
   `/v2/user/sync` re-reads under. Any miss is `409 {reason:'busy'}`.
8. **Receipts trimmed to 500:** arrival order in `backroom:idorder:<uid>`; above 500 the OLDEST are
   trimmed, never the whole hash.
9. **Tape default:** `defaultTapeCount(sp)` (section 3.2, decision 4).
10. **Freeze response shape** (as S1 implemented it). The freeze outcomes are in `freeze.outcomes`, NOT
    in `tape`; `tape` is the stored tape's id and cursor only (no `outcomes`), or `null` with no tape:

```json
{ "ok": true, "idem": "...", "sp": 49, "spBefore": 50, "cost": 2, "capped": false,
  "melt": 0, "jackpot": 400,
  "tape": { "id": "t_9f3c...", "played": 3 },
  "freeze": { "col": 1, "held": "gif1", "outcomes": [
    { "i": 0, "kind": "freeze", "...": "same outcome fields as 3.2, expansions inline" } ] } }
```

11. **Pace (owner decision):** the page plays one outcome in about 4 s (was about 2 s), every paid, free
    and re-spin outcome included, so a small SP balance lasts about twice as long.
12. **Server floor 3000 ms per outcome:** `SLOT_FLOOR_MS` defaults to 3000 (was 800) and
    `SLOT_FREEZE_FLOOR_MS` to 3000 (was 700), env overrides kept; `state.floorMs` is 3000. A freeze right
    after a freeze waits `max(3000, that freeze's outcomes x 3000)`. A modified client tops out near
    1,200 outcomes an hour against the page's 900.

## 10.13 Hypno v3 amendment (2026-09-14)

Binding spec: the owner-approved playable mockup `hypno-spins-v3.html` and its notes `hypno-spins-v3-notes.md` (owner
verdict: "seems good, implement this inside our game"). Where this section disagrees with anything above, it wins. The
"Hypno decisions (2026-09-14)" in `_evidence/brainstorm/DECISIONS.md` (Spiral Dial, suits carrying words, Dealt Mat,
existing fx ids only) are SUPERSEDED and are not built.

**Laws for every v3 lane**

1. **Looks only.** Rules, odds, pays, stakes, floors and tables stay exactly as the sims ship them (`backroom-cards.js`
   RULES_V1, `backroom-roulette.js` TABLE_V1, `backroom-wheel.js` TABLE_V2). Anything that would change EV stops the
   lane and is reported. A landing always shows the server's result; choreography only decorates the way there.
2. **Every spiral comes from the Loom.** Pages draw fields only through `arcademy/engine/loom/loomField.js`
   (`createFieldRenderer`, `normalizeParams2`, `loopMs2`, `drawFallbackFrame`, which uses `loomSpiral.js`), imported
   through the kit (10.13.D), never forked or copied. Host fullscreen spirals are Loom-woven GIFs (10.13.B).
3. **Handedness.** A Loom field that pulls inward runs with `direction: 1` and a phase that increases while the object
   turns clockwise on screen: its arms lead at the rim, so it reads inward (the owner checks this on screen). Physical
   trailing shapes (taffy slices, smear ghosts, turret arms, chip vortex paths, velvet wake) bend AGAINST their motion:
   `a = base - k * u`, `base` growing in the direction of motion, `u` 0 at the hub to 1 at the rim.
4. **The Lighthouse beam turns on its own clock**, never locked to the rotor: `beamAngle = -0.7 * t` rad (t in s,
   against the rotor), half-width 0.24 rad, lighting every number it passes. The mockup's rotor-locked first cut (which
   only ever lit 0 and 32) is a known bug and is not copied.
5. **The Brake.** Colour washes at most one per 360 ms, peak alpha 0.42, never white. Tunnel vision never closes: the
   centre stays clear. Sparks are single 0.5 s fades at least 340 ms apart. Bulb chases at most 2.4 steps a second.
6. **Calm is the mockup's reduced motion.** Whenever `intensity` is `calm` or `reduced` is true (section 4 forces Calm
   below MotionLevel Full): every strength x0.5, every fullscreen duration x0.6, settled states instead of travel,
   slow-motion floors raised (wheel 0.32 -> 0.6, roulette 0.42 -> 0.7). The page halves what it draws itself; the host
   halves what it draws. Nobody halves twice: a page always sends Normal values in `args`.
7. **Integrated GPUs in WebView2.** One shared Loom WebGL context per page (the kit's), backing store at most 512 px on
   the long side (card backs 256), no per-frame `backdrop-filter` in any page. The mockup's "Soft room" page blur is not
   built; it is the host `haze` primitive, Full only.
8. **Out of scope:** the notes' six "its own game" ideas (the deck is the player, the hole card thins, sittings as
   sessions, faceless tells, insurance as look closer, doubling doubles the moment), subliminal words on the screen
   overlay (not wired in v3), a fullscreen GIF on ordinary card wins, the kaleidoscope win and the tilted bowl.

**Pieces and lanes**

| Piece | Repo / path | Lane |
|---|---|---|
| `init.gates`, the five new primitives, `fx.args`, `fx-tunnel`, `fx-release`, the 13-GIF deal, Ops rows, spiral source, `br_*` lexicon prefix | client `Services/BackRoom/*`, new overlays in `Services/BackRoom/Overlays/` | H1 |
| Hypno kit, loader ctx additions, `room/gif-decode.js`, woven spirals | client `Resources/web/backroom/shared/hypno/`, `room/loader.js`, `room/gif.js`, `Scripts/weave-backroom-spirals.mjs` | K1 |
| Daily Daze v3 | client `stations/wheel/` | G-wheel |
| Soft Hand station | client `stations/cards/` + its `stations.json` row | G-cards |
| Velvet Vortex station | client `stations/roulette/` + its `stations.json` row | G-roulette |
| Soft Hand routes | CCP-Server `proxy/backroom-cards-routes.js` (pure `backroom-cards.js` as on `feat/br2-sim-cards-b`) | S-cards |
| Velvet Vortex routes | CCP-Server `proxy/backroom-roulette-routes.js` (pure `backroom-roulette.js` as on `feat/br2-sim-roulette-b`) | S-roulette |

Station lanes never touch C#. They ship English fallbacks in the page and list their `br_cards_*` / `br_roulette_*` keys
in `station.md`; the integration pass adds them to `Localization/Languages/en.json`. H1 sends every `en.json` key that
starts with `br_` in `init.lex` (the `LexKeys` list stops growing per station). A page may ship ahead of the kit or the
host: a missing `ctx` member is feature-detected and the effect is skipped, never faked.

### 10.13.A `init.gates`

```json
{ "type": "init", "...": "section 2.2 fields", "gates": { "flash": true, "subliminal": true, "spiral": true, "brainDrain": false } }
{ "type": "settings", "motion": "full", "intensity": "normal", "reduced": false, "gates": { "flash": true, "...": "all four, every time" } }
```

- Sources: `AppSettings.FlashEnabled`, `SubliminalEnabled`, `SpiralEnabled`, `BrainDrainEnabled`. The host pushes a
  full `settings` frame when any of the four, `MotionLevel` or `BackRoomFxIntensity` changes, and on `CurrentReplaced`.
- Gates are for DRESSING only. Since the authored show (2026-09-15) the host gates NOTHING on them: `BackRoomFxPlan`
  has no `FxGates` any more, and every fx id plays its full recipe whatever the toggles say.
- Page side (K1, `room/loader.js`): `ctx.gates` is a live frozen `{flash, subliminal, spiral, brainDrain}` getter;
  `ctx.onSettings(fn)` subscribes to `{motion, intensity, reduced, gates}` and returns an unsubscribe. A host that sends
  no `gates` reads as all `true` (the host is the enforcer).
- What a station dresses plain when a gate is off (from the first frame, and live on `settings`):

| Gate off | Plain dress |
|---|---|
| `flash` | no `fx.wash` / `fx.gif_from`; card faces show rank and suit with no picture, the sit fan too |
| `spiral` | no `fx.loom_spiral`; card backs are a brass crosshatch, the wheel hub a brass star, the roulette turret dish stays velvet (a Spiral Wake still shows as text and a gold rim glow). **The desktop host now always sends this `true`** - it used to carry `AppSettings.SpiralEnabled`, the panel's fullscreen overlay toggle, which is not the room's dressing and is coin-flipped by Randomize. The plain dress stays specified for a future room-level switch. |
| `brainDrain` | no `fx-tunnel`, no `fx.haze`; in-station dims (the long last turn's stage edges) stay |
| `subliminal` | nothing in v3 |

### 10.13.B Fullscreen effects: host primitives and page effects

**Rule.** The app's signature effects (colour flash, fullscreen spiral, fullscreen GIF, tunnel vision, the soft room
blur) are real app overlays on the desktop, fired through fx ids and gated by the app toggles. Everything drawn inside
the station view is the page's own.

| v3 effect (mockup) | Owner | fx / message |
|---|---|---|
| Flash (soft colour wash, optional picture in the middle) | host `wash` | `fx.wash` |
| Fullscreen GIF growing out of a card, slice or pocket | host `gif-from` | `fx.gif_from` |
| Fullscreen Loom spiral (wheel jackpot, Spiral Wake screen) | host `spiral-loom` | `fx.loom_spiral` |
| Tunnel vision (tunnel turn, losing edges, tunnel run) | host `tunnel` | `fx-tunnel` |
| Soft room (roulette haze) | host `haze` (the app's BrainDrain blur, no drip) | `fx.haze`, Full only |
| Subliminal words | not wired in v3 | none |
| Loom hub, moire rim, taffy, long last turn, quiet room, Loom backs, your deck, breathing lamp, ripple felt, chip vortex, win tunnel, ace glow, sit fan, drifting rim, lighthouse, fret rattle, velvet wake, turret whirl | page | none |

**Wire.** `fx` gains an optional `args` object; a picture rides in `symbols` as a dealt key (`g0`..`g12`), as in
section 4. The host validates everything and never uses page text as a path.

```json
{ "type": "fx", "token": "32-hex", "fxId": "fx.gif_from", "station": "wheel", "symbols": ["g2"],
  "args": { "from": { "x": 612, "y": 188, "w": 60, "h": 44 }, "ms": 3400, "scale": 1 } }
{ "type": "fx", "token": "...", "fxId": "fx.wash", "station": "cards", "symbols": ["g11"], "args": { "color": "#5fffd0", "strength": 0.7 } }
{ "type": "fx", "token": "...", "fxId": "fx.loom_spiral", "station": "roulette", "args": { "preset": "wake", "hold": true, "alpha": 0.65 } }
{ "type": "fx-release", "token": "<that token>", "station": "roulette" }
{ "type": "fx-tunnel", "station": "wheel", "level": 0.62 }
```

| `args` field | Used by | Host validation |
|---|---|---|
| `color` | `fx.wash` | `^#[0-9a-fA-F]{6}$`, else `#9b6bff`; HSL lightness capped at 0.72, saturation floored at 0.30, so never white |
| `strength` | `fx.wash` | clamped 0.1..1, default 0.7 |
| `from` | `fx.gif_from` | `{x,y,w,h}` CSS px of the page viewport (`getBoundingClientRect` space), finite, w and h 8..viewport; missing or bad = grow from the window centre |
| `ms` | `fx.gif_from` 1500..5000 (default 3400), `fx.loom_spiral` 1000..20000 (default 4200), `fx.haze` 1000..20000 | clamped |
| `scale` | `fx.gif_from` | 0.3..1, default 1 (below 1: no dim, it sits inside a running spiral) |
| `preset` | `fx.loom_spiral` | `screen` or `wake`, else `screen` |
| `hold` | `fx.loom_spiral`, `fx.haze` | `true` = stays until `fx-release` or the 20 s cap; `ms` ignored |
| `alpha` | `fx.loom_spiral` | 0.3..0.9, default 0.85 |

**Recipes** (new `BackRoomFxPlan` rows; none is a hero, so they open no hero window and wait behind a running slot hero
like any non-hero, section 4):

| fxId | Calm | Normal (the mockup) | Full | Gated by |
|---|---|---|---|---|
| `fx.wash` | `wash`, peak x0.5 | `wash`: 80 ms up, exponential decay 4.5/s, gone at 900 ms, peak 0.42 x strength; with a gif key the picture sits in the middle at 42% of the screen height (4:3 cover box), alpha min(1, env x 1.3) x 0.85 x strength | as Normal | Flash |
| `fx.gif_from` | `gif-from`, duration x0.6, dim x0.8 | `gif-from`: grows from `from` to cover the screen over 700 ms (ease in-out), holds, fades over the last 900 ms; the screen dims to 45% behind it unless `scale < 1` | as Normal | Flash |
| `fx.loom_spiral` | `spiral-loom`, alpha x0.5, duration x0.6 | `spiral-loom`: fade in 800 ms, hold `ms` or until released, fade out 1200 ms | as Normal | Spiral |
| `fx.haze` | skipped `calm` | skipped `calm` | `haze`: BrainDrain blur without drip at 0.5 x the user's BrainDrainIntensity, until released | BrainDrain |
| message `fx-tunnel` | `tunnel`, level x0.5 | `tunnel`, level as sent | as Normal | BrainDrain |

**Primitives** (H1; sink methods on `IBackRoomFxSink`, overlays in `Services/BackRoom/Overlays/`):

| Primitive | Sink call | Screens | Motion rule |
|---|---|---|---|
| `wash` | `Wash(Color rgb, double peak, BackRoomGif? picture)` | the virtual screen, like the `gif-full` hero; the picture on the room window's screen | Off keeps it (a wash is not motion) |
| `gif-from` | `GifFrom(BackRoomGif gif, Rect fromScreenDip, int ms, double scale, bool still)` | the screen holding the room window | Off: no growth, full size with a 300 ms fade, still frame |
| `spiral-loom` | `SpiralLoom(string gifPath, int ms, double alpha, bool hold, bool still)` | one field over the virtual screen | Off: the woven GIF's first frame |
| `haze` | `BrainDrain(ms, 0.5, melt: false)` (existing) | as the app's BrainDrain | as `brain-drain` |
| `tunnel` | `Tunnel(double level)` | one vignette per screen, centred on that screen | Off: the level steps without easing |

- **Tunnel shape** (per screen, `R = hypot(w, h) / 2`, `k` = the eased level): a radial gradient, transparent at
  `R x (1 - 0.72k)` to `rgba(6,3,12, 0.94 x min(1, 1.3k))` at `R x (1.12 - 0.5k)`. It eases toward the wanted level at
  1.4/s closing and 2.2/s opening. A level not refreshed within 1500 ms eases back to 0 by itself; `suspend`, `close`,
  `exit` and that station's `station-close` cancel it at once. At most 10 updates a second are applied (a later one in
  the same 100 ms replaces the earlier).
- **Wash gap.** A wash inside 360 ms of the previous one is dropped and acked `{prim:'wash', why:'busy'}` (the mockup
  drops, it does not delay). New strobe channel `Wash`, 360 ms.
- **One at a time.** At most one `gif-from` on screen: another while it shows is acked `busy`. At most one
  `spiral-loom`: a new one replaces the running one (the old fades out over 1200 ms). `fx-release` fades out whatever
  the token still holds (`spiral-loom` 1200 ms, `haze` as BrainDrain releases); a hold ends by itself at 20 s.
  `station-close` releases every hold that station started. `CancelAll` stops all five.
- **Coordinates.** `from` is CSS px in the page viewport. The host maps it to screen DIPs through the WebView2
  control's on-screen origin and `CoreWebView2Controller.ZoomFactor` / `RasterizationScale`. A minimised or off-screen
  room window grows from the centre of the primary screen.
- **Spiral source (owner law).** `OverlayService` `"spiral"` plays `GetSpiralPath()`: the user's `SpiralPath`, a random
  pick from its folder or the Spirals library when `SpiralRandomize` is on, else the mod default from
  `ModResourceResolver.ResolveSpiralUri()`. None of those is guaranteed to be Loom-woven, so the Back Room stops using
  it. `spiral-loom` plays, in order: (1) the player's own weave, when `SpiralPath` is a `loom_<slug>.gif` inside
  `DtrhLoomStore.SpiralsFolder` (the CCP Spirals library the Loom writes); (2) the bundled weave for the preset,
  `Resources/web/backroom/shared/hypno/spirals/<preset>.gif` (`screen.gif`, `wake.gif`), woven by K1 from
  `LOOM_PRESETS.screen` / `.wake` with the Loom's own encoder (`dtrh/engine/loomWorker.js`, driven headless by
  `Scripts/weave-backroom-spirals.mjs`, never at runtime), each with its `.json` Loom v2 sidecar, format `wide`, long
  side 720, at most 4 MB. A missing file is skipped `unknown`. Frames decode off the UI thread.
- **`spiral-full` (section 4) moves to the same source.** Its recipes are unchanged; its call becomes
  `SpiralLoom(<preset screen path>, ms, opacity x level, hold: false, still)`, so the slot's spirals are Loom-woven too.

Page side (K1, `room/loader.js`): `ctx.fx(fxId, symbols?, args?)` posts `fx` (with `args` when given) and returns the
ack promise carrying the token synchronously (`p.token`); `ctx.fxRelease(token)` posts `fx-release`;
`ctx.fxTunnel(level)` posts `fx-tunnel` (the kit throttles it, 10.13.D).

### 10.13.C Media for a sit-down

- `media-request` gains `count` (integer 1..13, default 4; anything else reads as 4). The host deals `count` GIFs for
  that station and keeps the deal until that station's next `media-request`. Words stay 4.
- A sit-down is one station visit: `open` asks, standing up (`close`, Back) ends it, sitting down again re-deals.
  Soft Hand may also re-deal in the station with "Stand up, sit back down" (only while no hand is open).
- **The 13-GIF deal (cards).** The host shuffles the pool with `seed` exactly as section 5 and deals distinct pool GIFs
  until it has `count` or the pool runs out (probe budget `max(48, count x 4)`). With at least one pool GIF it does NOT
  pad with fallback art: `gifs.length = min(count, found)`. With none it deals the 4 fallback loops. Keys are
  `g0`..`g{n-1}` in deal order.
- **Value mapping (page).** Values in `DECK_VALUES` order `A 2 3 4 5 6 7 8 9 T J Q K` (index 0..12, the pure module's
  rank order) wear `gifs[i % gifs.length]`: fewer than 13 GIFs cycle. A re-deal changes the mapping.
- **Wheel and roulette** ask `count: 4` and give `fx.gif_from` the key `deck.pickKey(<result string>)`, so the picture
  is stable for a result.
- **A source change re-deals mid-sit-down, at three different speeds (2026-09-17).** The page dispatches a
  plain `br-media-changed` window event when a `settings` frame reports a different EFFECTIVE source - only the
  effective one, so flipping between two settings that resolve to the same pool does not throw away a dealt
  wall. `room/screens.js` bumps its source version and re-deals at once; overlays follow because the next fx
  resolves against the new pool; a SEATED station only sets a flag and re-deals on the next sit-down
  (`stations/slot/station.js`), because a reel cell must not change face mid-tape. The room says so:
  `br_media_timing` tells the player that walls and new flashes change now, game artwork changes on the next
  visit, and the current hand and prepaid spins are kept.
- H1: `IBackRoomMedia.Deal(station, seed, count = 4)` and `DealAsync` (remote is inherently async; `Deal` serves
  whatever is already warm and never waits); `BackRoomBridge` reads `count` and the optional `source`;
  `BackRoomFxPlan.ResolveSymbols` accepts one or two digit indexes (`g0`..`g12`, `gif0`..`gif12`); `MaxSymbolKeys`
  stays 8.

### 10.13.D The hypno kit (`Resources/web/backroom/shared/hypno/`, K1)

Plain ES modules with no three.js import (three stations upload the kit's canvases themselves). `index.js` re-exports
every name below. Imports: `../../../arcademy/engine/loom/loomField.js` and `../../room/gif-decode.js`.

**Amended 2026-09-17: `room/clip-source.js` is a second three-free picture module, alongside `gif-decode.js`.**
It plays a webm/mp4 into a canvas and returns the identical source shape, so `room/gif.js` wraps either one in the
same `CanvasTexture` and nothing downstream learns a new kind of picture. It paints on `tick()` rather than being a
three.js `VideoTexture` deliberately: this section's rule is that the room's clock owns when a picture costs
anything, and a `VideoTexture` would upload every frame the video decoded whether the room wanted it or not. The
same two ceilings apply as to a decoded GIF (`MAX_EDGE`, the pixel budget, `MEDIA_LIMITS.loadMs`), and `still`
PAUSES the video rather than merely not drawing it - an off-screen video that keeps decoding is exactly the
offscreen work the render budget exists to stop.

```js
// loom.js - one shared Loom GL context per page, over the real loomField.js
export const LOOM_PRESETS;   // frozen, normalizeParams2'd Loom schema v2: { backs, hub, whirl, wake, screen }
export const LOOM_BACKING;   // frozen { long: 512, small: 256 }
export function phaseForAngle(presetName, rad);   // clockwise screen radians -> phase 0..1 whose layer-1 rotation is rad
export function createLoomKit({ still = false, log = null } = {});
//   -> { webgl,                                   // boolean
//        draw(ctx2d, presetName, x, y, w, h, { now, angle, alpha = 1, backing = 'long' } = {}),
//        paint(canvas, presetName, { now, angle } = {}),   // into a caller-owned canvas (a CanvasTexture), <= 512 px
//        setStill(on), dispose() }
```

- Phase: with `angle`, `phaseForAngle(name, angle)`; else `(now % loopMs2(q)) / loopMs2(q)`. `still` holds phase 0 (or
  the last `angle`). `phaseForAngle` uses loomField's own span rule (`2PI / arms x (arms % n === 0 ? n : arms)`,
  `n = min(colors, 6)`, times `speedMul`, signed by `direction`): a helper beside the module, not a fork of it.
- One GL canvas for the page, made on first `draw`/`paint`, its backing resized to the requested aspect (quantized to
  0.05) at the `backing` long side. The same preset at the same phase and aspect inside one frame renders once (every
  card back shares one render). No WebGL: `drawFallbackFrame`. `dispose()` loses the context (`WEBGL_lose_context`) and
  is called from the station's `close`.
- `LOOM_PRESETS` (the mockup's presets, in schema v2, `bg` solid unless noted):
  `backs` layer {arms 4, turns 2, duty 0.5, log, direction 1, #ff69b4 #8a5cff, hard}, bg #14060f, glow 0.25, speed 2;
  `hub` {3, 1.5, 0.55, golden, 1, #e8c27a #ff5fa2 #9b6bff, hard}, bg #1a0f2b, glow 0.35, speed 3 (driven by angle);
  `whirl` {2, 2.5, 0.45, ribbon, 1, #5fffd0 #9b6bff, hard}, bg #1c1230, glow 0.4, wobble {amp 0.12, freq 3, cycles 1}
  (driven by angle); `wake` {4, 3, 0.5, log, 1, #5fffd0 #3a1f5c, hard}, bg #0a0614, glow 0.45, wobble {0.15, 2, 1},
  speed 2; `screen` {6, 2.5, 0.5, log, 1, #ff5fa2 #9b6bff #5fffd0, gradient}, layer2 {enabled, 3, 1.5, 0.3, log, 1,
  #e8c27a}, bg radial #14060f -> #08040e, glow 0.5, pulse {amp 0.08, cycles 1}, speed 1. `wake` and `screen` are the
  two the host plays woven. The gold layer2 of `screen` turns the SAME way as layer 1 (owner, 2026-09-14; the mockup
  counter-turned it).

```js
// media.js - the dealt GIFs drawn in the page (cards: 13; wheel and roulette: keys only)
export const DECK_VALUES;   // frozen ['A','2','3','4','5','6','7','8','9','T','J','Q','K']
export async function createDeck(ctx, { count = 13, maxEdge = 192, still = false } = {});
//   -> { seed, keys, size,                        // keys: string[] in deal order; size = keys.length >= 1
//        keyFor(value), keyAt(i),                 // value 'A'..'K' or a card code 'Qh'; index i % size
//        pickKey(seedString),                     // stable key for a result (FNV-1a of the string)
//        draw(ctx2d, key, x, y, w, h, { alpha = 1 } = {}),   // cover fit, clipped to the rect
//        image(key),                              // the source canvas, or an <img> still, for a texture
//        tick(now), setStill(on), dispose() }
```

- `createDeck` calls `ctx.media({ count })`. Decoding reuses the room's: K1 moves the three-free part of `room/gif.js`
  into `room/gif-decode.js` as `decodedSource(url, { maxEdge = 384, maxFps = 12 }) -> { canvas, animated, frames, index,
  tick(now, still), dispose() } | null`, and `room/gif.js` `animatedSource` wraps it with the CanvasTexture exactly as
  today (room smoke unchanged). Deck caps: at most 13 sources, `maxEdge` 192, 12 fps each, at most ONE decode started
  per `tick`, only sources drawn since the last tick advance; no decoder (or a failure) draws an `<img>` still.

```js
// moments.js - a game moment + size -> host fx calls and page effects, with the gates applied
export const MOMENTS;              // frozen table, 10.13.F
export function wheelSize(result); // 'quiet' | 'flash' | 'gif' | 'jackpot'
export function strengthK(ctx);    // 0.5 when ctx.reduced or ctx.intensity === 'calm', else 1 (page-drawn effects only)
export function createMoments(ctx, { station });
//   -> { play(id, { color, strength, from, gif } = {}),   // -> { tokens: string[], page: string[], held: boolean }
//        tunnel(level),     // 0..1; posts on change at most every 100 ms and re-posts every 1000 ms while > 0
//        holdScreen(on),    // cards: while on, play() fires no host fx ({held: true}) and tunnel(> 0) is ignored
//        release(tokens),   // fx-release each
//        cancel(),          // tunnel(0) now, release every hold, stop timers (suspend, close)
//        dispose() }
```

- `play` looks the id up in `MOMENTS`, drops the host steps whose gate is off (`flash`: wash, gif_from; `spiral`:
  loom_spiral; `brainDrain`: haze; `tunnel`: tunnel, the Back Room's own gate, amended 2026-09-14), fires the rest
  through `ctx.fx` with the table's Normal `args` merged with the caller's, and returns the page effect names for the
  station to run at `strengthK(ctx)`. An unknown id fires nothing and logs.
- Tests: `shared/hypno/tests/*.test.mjs` (node) and `shared/hypno/tests/kit-check.mjs` (headless, a mock host that
  records `fx`, `fx-tunnel` and `fx-release`; `KIT_PORT` default 8896, debug +500). K1 exports that mock as
  `shared/hypno/tests/mock-host.js` `createMockHost()` for the station checks.

### 10.13.E Server API: Soft Hand and Velvet Vortex

Shared, exactly as 3.4, 3.5 and section 10: base url and auth; `gate(req, res, { soft: true })` (the limiter's
`too_fast` and a lock miss's `busy` answer HTTP 200, like the wheel); the door (`backroomOpenFor`: `BACKROOM_OPEN`,
`BACKROOM_TESTERS`, no `BACKROOM_SEED_KEY` = shut; closed = `403 {ok:false, reason:'closed'}` before any lock, but a
settled `idem` still replays); the three locks in order (`withLocks`); receipts in `backroom:ids:<uid>` checked under
the locks and before any floor, replayed byte for byte; `idem` `^[A-Za-z0-9_-]{16,64}$`; keyed rng
`rngFor(key, uid, n, <station>)`, never `Math.random`; `CAP` = `SKILL_POINTS_CAP` 99,999 (winnings above it are lost,
`capped: true`); `netSp` on the station ledger (`backroomNetSp` already sums every station into the SkillPointBackfill,
so a later sync never refunds a stake or erases a win). The client never sends SP. Both files register from
`backroom-routes.js` with the helper object the wheel gets. Logs: counts and results only, uid through `clean`.

**Soft Hand (`/v2/backroom/cards/*`, `proxy/backroom-cards-routes.js`).** Rules = `backroom-cards.js` RULES_V1: 6 decks
shuffled for every hand, dealer peek, S17, blackjack 2:1, double on any two cards, split once, double after split,
split aces one card each, six-card Charlie pays 1:1, no surrender, no insurance, stake 1, 2 or 3 SP (owner update 2026-09-16), one open hand,
auto-stand after 24 h.

- Ledger `user.backroom.cards = { v: 1, hand: <engine state, hole card and shoe pointer included> | null, openedAt: ms,
  n: lifetime hands, nextDealAt: ms, netSp }`. A finished hand stays in `hand` until the next deal. Only
  `publicHand(hand)` ever leaves the server.
- `GET state` -> `{ ok, sp, open, hand: publicHand | null, legal: string[], hint: 'hit'|'stand'|'double'|'split'|null,
  autoStandAt: ISO | null, rules: { v, decks, dealerHitsSoft17, blackjackPays, charlie, stakes, doubleAfterSplit,
  splitAcesOneCard, maxHands }, floorMs }`. Read-only (an expired hand is auto-stood by the next POST).
  `legal = legalActions(hand, sp)`, `hint = hint(hand, sp)`, always sent; the page shows the hint only when the player
  turned it on (off by default).
- `POST deal {idem, stake}`. A bad `idem` or a stake outside `rules.stakes` -> `bad_request`. Under the locks: the
  receipt; an open hand older than `autoStandMs` is `autoStand`ed and settled first (its return credited, sent back as
  `autoStood`); `canDeal` -> `hand_open` (+ `hand, legal, hint`); `now < nextDealAt` -> `too_fast` + `retryInMs`;
  `sp < stake` -> `insufficient` + `sp`. Then `deal({ id: 'h_' + n.toString(36) + '_' + <idem hash base 36>, n, stake },
  shoeFor(key, uid, n))`, debit the stake, credit `result.returned` if the deal settled at once (a blackjack on either
  side), `n += 1`, `openedAt = now`, `nextDealAt = now + CARDS_FLOOR_MS`.
- `POST hit | stand | double | split {idem, handId, step}`. Under the locks: the receipt; an expired hand -> auto-stand,
  settle, refuse `auto_stood` (+ `hand, sp`); no hand, a finished hand or a `handId` mismatch -> `no_hand`;
  `step !== hand.step` -> `stale` (+ `hand, legal, hint`: a double submit); `act(hand, move, shoeFor(key, uid, hand.n),
  sp)` not ok -> `illegal` (+ `hand, legal, hint`). Debit `cost` (double and split cost one more bet); if the hand is now
  done, credit `result.returned`.
- Every success: `{ ok, idem, sp, spBefore, cost, returned, capped, hand: publicHand, legal, hint, autoStood?:
  publicHand, autoStandAt }`. `cost` = SP this call took, `returned` = SP this call credited (0 while the hand is
  open), `sp` after both, `netSp += sp - spBefore`.
- Refusals: `closed` (403), `bad_request`, `hand_open`, `no_hand`, `stale`, `illegal`, `insufficient`, `too_fast`,
  `auto_stood`, `busy`. Env `CARDS_FLOOR_MS`, default 5000 (the sim's bot floor; 10.14), between deals only; moves have no
  floor beyond the 60/min limiter. `state.floorMs` echoes it.

**Velvet Vortex (`/v2/backroom/roulette/*`, `proxy/backroom-roulette-routes.js`).** Rules = `backroom-roulette.js`
TABLE_V1: 5 bet kinds (straight `s0`..`s36` pays 36; `rose`, `plum` pay 2; rows `sip` 1-12, `sink` 13-24, `deep` 25-36
pay 3; totals returned, chip included), Spiral Wake 29 in 600 doubles every winning chip, whole SP, 1..3 per spot and
1..3 per spin, any layout covering all of 1-36 refused, 1..5 spins of one layout per request, all debited and settled
at once.

- Ledger `user.backroom.roulette = ensureRoulette(...)` = `{ tape, n: lifetime spins, nextBuyAt: ms, netSp }`.
- `GET state` -> `{ ok, sp, open, tape: tapeJson | null (only while unplayed), table: publicTable(), wheel: WHEEL,
  rose: ROSE, spots: SPOT_IDS, floorMs }`. The page paints pocket order and colours from this, never its own copy.
- `POST spin {idem, count, bets: [{spot, amt}], cursor?: {tapeId, played}}`. `parseSpinBody` null -> `bad_request`.
  Under the locks: the receipt, then `settle({ uid, seedKey, roulette, sp, req, now, floorMs: ROULETTE_FLOOR_MS,
  cap: CAP })` with its refusals verbatim: `bad_layout` + `why` (`empty | too_many_spots | unknown_spot | bad_amount |
  duplicate_spot | stake_cap | covers_all | bad_count`), `tape_unplayed` + `sp, tape`, `too_fast` + `retryInMs`,
  `insufficient` + `sp`. Success = the receipt verbatim: `{ ok, idem, sp, spBefore, cost, won, capped, tape: { id, bets,
  played: 0, outcomes: [{ i, pocket, color, wake, pay, fx }] } }`. `pay` is the total a spin returns. `fx` stays in the
  receipt as the pure module writes it; the v3 page does not fire it.
- `POST cursor {tapeId, played}` -> `{ ok }` (`applyCursor`, forward only; a bad shape is `bad_request`). The host
  flushes it on `station-close` like the slot's, and it is folded into the next `spin`.
- Env `ROULETTE_FLOOR_MS`, default 6000 per spin (`nextBuyAt = now + count x floor`). Refusals: `closed` (403),
  `bad_request`, `bad_layout`, `tape_unplayed`, `too_fast`, `insufficient`, `busy`.
- Law I on the page: `shownSp = sp - sum(outcomes[played..].pay)`.

S-lane tests: `scripts/test-backroom-cards-routes.mjs`, `scripts/test-backroom-roulette-routes.mjs` (receipts, locks,
door, floors, cap, auto-stand, stale step, cover-all), plus rows in `proxy/docs/env-vars.md`.

### 10.13.F Station specs

**Common.** A station fires moments only through `createMoments` and never calls the section 4 ids. A moment fires on
the frame the page SHOWS the result (Law I), never on the server reply. `suspend(true)` and `close()` call
`moments.cancel()` and dispose the Loom kit and the deck. Headless checks use the kit's mock host; evidence goes under
`_evidence/hypno/<lane>/`.

**MOMENTS** (Normal `args`; Calm is applied by the host and by `strengthK`):

| id | Fires on | Host, same frame, in order | Page |
|---|---|---|---|
| `wheel.turn` | continuous while the long last turn runs | `tunnel(dim x 0.85)` | `last_turn` |
| `wheel.land.quiet` | Snooze, 1, 2, 3 | none | `quiet_room` |
| `wheel.land.flash` | 5, 8, 12 | `fx.wash {color: slice colour, strength 0.55}` | `quiet_room` |
| `wheel.land.gif` | 20, 40, 100 (Dazed, the jackpot fallback included) | `fx.wash {slice colour, 0.9}`, `fx.gif_from {from: slice rect, ms 3400}` | `quiet_room` |
| `wheel.land.jackpot` | the pot | `fx.loom_spiral {preset screen, ms 4200, alpha 0.9}`, `fx.gif_from {from: slice rect, ms 4600, scale 0.46}`, `fx.wash {#e8c27a, 1}` | `quiet_room`, THE REVEAL |
| `cards.sit` | open, and "Stand up, sit back down" | none | `sit_fan` |
| `cards.bloom` | a paid player blackjack (`outcome === 'blackjack'`), the frame the second player card finishes turning | `fx.gif_from {from: ace rect, ms 4000}` with the ace's key, `fx.wash {#ff5fa2, 0.8}` | `ace_glow` |
| `cards.win` | the settled hand shows `result.net > 0` | `fx.wash {#5fffd0, 0.7}` with the key of the highest-value card (ace highest) in the player's winning hands; after a bloom, `fx.wash {#5fffd0, 0.9}` with no picture | `win_tunnel`, `chip_vortex` |
| `cards.lose` | `net < 0` | `tunnel` 0.75 x sin(PI x p) over 2600 ms, then 0 | `chip_vortex` to the dealer |
| `cards.push` | `net === 0` | none | none |
| `roulette.run` | continuous from Spin to the landing frame | `tunnel(0.75 x clamp(0.35 + abs(ballSpeed) / 8, 0, 1))`, ball speed in rad/s; Full: `fx.haze {hold}` | `lighthouse`, `fret_rattle`, `velvet_wake` (Full) |
| `roulette.wake` | a spin with `outcome.wake`, at its Spin frame | `fx.loom_spiral {preset wake, hold, alpha 0.65}` | `turret_whirl` |
| `roulette.land.miss` | landing, `pay === 0` | release the spin's holds | `chip_vortex` to the bowl |
| `roulette.land.win` | `pay > 0`, no wake, no straight on the pocket | release; `fx.wash {pocket colour, 0.6}` | chips slide in |
| `roulette.land.big` | `pay > 0` and (`wake` or a straight on the pocket) | release; `fx.wash {pocket colour, 1}`, `fx.gif_from {from: pocket rect, ms 3600}` | chips, plus the pulled pair on a wake |

`wheelSize(result)`: `jackpot` when `result.jackpot`; `quiet` when `snoozed` or `pay <= 3`; `flash` when `pay <= 12`;
else `gif`. Pocket colour: 0 `#5fffd0`, rose `#ff5fa2`, plum `#9b6bff`. **Nothing fullscreen while a cards decision is
open:** the station calls `moments.holdScreen(true)` when a reply shows `hand.done === false`, and `holdScreen(false)`
on the frame the settled hand is shown (a bloom comes only with a hand that is already decided).

**Daily Daze (`stations/wheel/`, G-wheel).** Model: `assets/wheel.glb` (node contract `nodes.js`), the three.js close-up
as today. New `hypno.js` (pure: the slow-motion time warp, the quiet room colour curve, the taffy shear, the hub phase)
with `tests/hypno.test.mjs`. `feel.js` loses `FX_BY_TIER`, `fxFor` and `usesGifs` (tiers, sounds and tokens stay).
`station.js` plays moments. `scene.js` draws:

| Effect | Where | When | Rule |
|---|---|---|---|
| Loom hub | a runtime disc on `wheel_rotor` over the hub, sized from `hub_lip` (else `hub_spiral`) bounds, CanvasTexture 256 fed by `kit.paint('hub')`; it replaces the neon tube, which stays when neither node exists | always | `angle = -rotor.rotation.z` (clockwise on screen) plus 0.35 rad/s of idle drift, so it keeps pulling inward at rest; `spiral` off: brass star |
| The long last turn | the landing plan's clock | while the planned speed is under 1.6 rad/s | time scale `lerp(0.32, 1, clamp(speed / 1.6))` (Calm 0.6); the landing angle and slice never change; stage edges dim to 0.65 x dim x k; caption `br_wheel_slowly` ("s l o w l y") |
| Quiet room | slice materials | landing | every other slice mixes toward #2a2238 by `min(0.78, clamp(since x 3) x k x 1.6)`; colour flows back from 1.1 s, by angular distance, over 0.8 s; the landed slice keeps its colour with a mint outline |
| Moire rim | two runtime line rings at the rim | always, Full only | 60 lines each, one ring at the rotor angle, one at 0.9 x angle + 0.3 |
| Taffy slices | runtime sector geometry plus up to 4 ghost copies at alpha 0.16 | while turning, Full only | shear `min(speed x 0.11, 1.9) x k`, trailing (law 3) |

The `from` rect is `scene.project('landed')` as a 60 x 44 CSS px box. A reopen after spinning fires nothing. Snooze
keeps THE SHIVER and the yawn. `wheel-check.mjs` adds strips for the flash, gif and jackpot moments and asserts the
calls the mock host recorded.

**Soft Hand (`stations/cards/`, G-cards).** Model situation: the `cards` row in `stations.json` uses the room fixture
`card-table.glb` (a room copy with no page node contract), and `blender-scripting/card-table/out/card-table.glb` has no
station contract either. So the station draws a **2D canvas table** filling the station view, the mockup's table with
no three.js and no guessed geometry: felt under a lamp pool, the printed arc `br_cards_print` ("BLACKJACK PAYS 2 TO 1 ·
DEALER STANDS ON ALL 17s · SIX CARDS WIN"), the shoe, the chip spot, the dealer's and the player's hands (split hands
side by side). A 3D close-up is a later amendment with its own node contract.

| File | What |
|---|---|
| `station.js` | `mount(ctx)`; DOM, buttons (Deal; Hit, Stand, Double, Split from `legal`; bet chips 1 to 3 with +/- controls, default 1 below 30 SP; "Stand up, sit back down" while no hand is open), the request flow, moments |
| `hand.js` | pure: reading `publicHand`, the button set, the same idem on `busy`/`timeout` retries, `stale` adopts the returned hand, Law I |
| `table.js` | the canvas renderer: lamp, felt, print, cards, shoe, chips, win tunnel, chip vortex, sit fan |
| `feel.js` | pure: result -> moment id, the highest-card key, timings |
| `mock-server.js`, `dev.html` | harness on the 10.13.E shapes with scripted fixture shoes (never a copy of the server rules) |
| `tests/*.test.mjs`, `tests/cards-check.mjs` | node tests; headless check, `CARDS_PORT` default 8898, debug +500 |
| `station.md`, `station.css` | as the wheel's |

| Effect | When | Rule |
|---|---|---|
| Loom backs | always | `kit.draw(ctx2d, 'backs', ...)` clipped to each face-down card, one render per frame for all; `spiral` off: crosshatch |
| Your deck | always | the value's dealt picture at 85% under a pale wash (#f7f0fb at 0.3) and a corner halo, rank and suit on top with a white glow, classic layout untouched; `flash` off: plain faces |
| Breathing lamp | always, held during a celebration | the lamp pool breathes on a 10 s period (six a minute), amplitude x k; shadows follow |
| Ripple felt | a card lands, Full only | one ripple per landed card, 3.2 s |
| Chip vortex | after settle | winnings spiral to the player, a lost bet to the dealer, 2 s, trailing |
| Win tunnel | after a win settles | seven nested frames zooming for 3 s, `lighter`, alpha 0.5 x k |
| Ace glow | with `cards.bloom` | rose glow around the ace, 1.4 s |
| Sit fan | `cards.sit` | the 13 values fly out of the shoe face down, flip face up in a row, hold about 2 s while the lamp brightens, fly back; 4.4 s total; Back is live throughout |

The dealer's hole card stays a Loom back until `hand.done`. Reopening with an open hand plays the sit fan, then shows
the hand with its decisions live (and `holdScreen(true)`).

**Velvet Vortex (`stations/roulette/`, G-roulette).** Model situation: the `roulette` row uses the room fixture
`roulette.glb` (`hub: true` drives the room's idle hub only; no page node contract), and
`blender-scripting/roulette/out/roulette.glb` with `wheel-layout.json` has no station contract. So the station draws a
**2D canvas bowl and mat** filling the station view, the mockup's bowl, with pocket order and colours from `state.wheel`
and `state.rose`, and a mat with the 37 straights, rose, plum, sip, sink and deep.

| File | What |
|---|---|
| `station.js` | `mount(ctx)`; DOM, chip picker (total 1..3), spins picker 1..5, Spin, tape playback (about 8 s a spin, the sim's page pace), cursor, moments |
| `tape.js` | pure: building a layout, a client cover-all check that only disables Spin (the server decides), Law I, idem reuse, `tape_unplayed` resume |
| `bowl.js` | the canvas bowl: drifting rim cache, rotor, pockets, lighthouse, the ball's run, rattle, sparks, turret whirl, velvet wake |
| `mat.js` | the canvas mat and chip stacks |
| `feel.js` | pure: outcome -> moment id, pocket colour, timings |
| `mock-server.js`, `dev.html`, `tests/*.test.mjs`, `tests/roulette-check.mjs` | harness with seeded fixture outcomes; node tests; headless check, `ROULETTE_PORT` default 8899, debug +500 |
| `station.md`, `station.css` | as the wheel's |

| Effect | When | Rule |
|---|---|---|
| Drifting rim | always | peripheral-drift print on two rim rings, drawn once into an offscreen cache per size |
| Lighthouse turret | always | law 4; the check asserts at least 30 distinct numbers lit over 10 s |
| Fret rattle | every drop | slow motion 0.42 (Calm 0.7), up to three fret clips each with a spark (law 5), planned BACKWARDS from `outcome.pocket` so the ball always settles there |
| Velvet wake | the ball run, Full only | brushed-nap sheen behind the ball, settling over 1.3 s |
| Turret whirl (Spiral Wake) | spins with `outcome.wake`, from Spin to rest | the turret dish becomes `kit.draw('whirl')` at 0.85 x fade x k with `angle` = the rotor's clockwise angle x 2.2; a winning stack gets a second pair of chips pulled out of the whirlpool; the turret arms trail the rotor |

Each spin of a tape plays `roulette.run` (plus `roulette.wake` when it wakes) and ends in exactly one
`roulette.land.*`, which releases that spin's holds first. The beam, the rim and the turret keep turning at rest; Calm
shows them still.

## 10.14 Owner decisions after the v3 build (2026-09-14)

Binding: the owner's "Hypno v3 build questions" and "Path to play" surveys (`_evidence/brainstorm/DECISIONS.md`, afternoon
of 2026-09-14). Where this section disagrees with anything above, 10.13 included, it wins. Laws 1 to 8 of 10.13 still
hold; the only EV change is the slot table (decision 5), and it keeps 102%.

**1. Tunnel vision is the Back Room's own switch (`gates.tunnel`).**

- `AppSettings.BackRoomTunnel`, default `true`. It is shown only in the room's Options (decision 12), never in the main
  Settings window, because nothing outside the Back Room uses it. A settings file written before the key existed reads
  as on; a player's off is kept.
- `init.gates` and every `settings.gates` carry `tunnel` (and `melt`, decision 3) beside the four toggles of 10.13.A:
  `{ "flash", "subliminal", "spiral", "brainDrain", "tunnel", "melt" }`, sent in full every time. The host pushes a
  full `settings` frame when either switch changes.
- `fx-tunnel` is gated by `tunnel`, NOT by `brainDrain`: the message is dropped while it is off, and turning it off
  under a running tunnel cancels the tunnel at once (not after the 1500 ms self-release). `FxGates.Tunnel` enforces it
  in `BackRoomFx.Tunnel`; the page gate is for dressing only, as in 10.13.A.
- Pages (K1 kit, `room/main.js`, `room/loader.js`): `ctx.gates.tunnel`, a missing key reads as `true`. `createMoments`
  drops its tunnel steps (the wheel's long last turn, the roulette run, the cards' losing edges) when `tunnel` is off.
  The 10.13.A plain-dress table becomes:

| Gate off | Plain dress |
|---|---|
| `brainDrain` | no `fx.haze`; in-station dims (the long last turn's stage edges) stay |
| `tunnel` | no `fx-tunnel`; in-station dims stay |

**2. Fullscreen Loom spiral: one field per screen.** `spiral-loom` (and section 4's `spiral-full`, which plays through
it) draws one field on every monitor, centred on that monitor, like the tunnel's vignettes, instead of one field over
the virtual screen. Each field is the woven GIF at UniformToFill, CENTRED, inside a cell clipped to its monitor
(`BackRoomLoomSpiralOverlay.BuildCell`: the Image carries no size of its own and is centred both ways, so a weave whose
shape differs from the monitor's, a square or tall Loom weave or a 16:10, 21:9 or portrait screen, is cropped from its
middle and the eye sits at the monitor's centre). The cells are `BackRoomOverlayMath.ScreenCells`: physical monitor
bounds mapped through the overlay window's OWN DPI (the mixed-DPI rule of 10.13.B's overlays), laid out again on a DPI
change. The overlay window re-reads the virtual screen whenever it wakes from its idle hide, so a monitor plugged in or
removed gets its cell from the next spiral after a quiet spell; one plugged in under a running spiral waits for that. One decode feeds every field; the fades, the
replace-and-release rules and the 20 s hold cap are unchanged. The 10.13.B primitive row reads: `spiral-loom` | one
field per screen, centred on each | Off: the woven GIF's first frame.

**3. Brain Drain melt is ON by default in the Back Room.**

- What kept it dark: `fx.melt` (the slot's melt) needed the app-wide `AppSettings.BrainDrainEnabled` (default `false`)
  and lost its drip without `AppSettings.BrainDrainMeltEnabled` (default `false`), so a player who never opened Brain
  Drain never saw a Back Room melt.
- Now `AppSettings.BackRoomMelt`, default `true`, is the room's own switch. Superseded 2026-09-15 by the authored
  show: `fx.melt` ALWAYS plays (6 s, alpha ramping 0 to 0.8, the drip included, at every motion level), the switch
  dresses the room's Options only, and `fx.haze` is gated by nothing either. The app-wide Brain Drain toggles never
  gated it, and nothing about the app's own Brain Drain feature or its defaults changed.
- The page sees it as `gates.melt` (dressing only).

**4. Screen preset: both layers turn the same way.** `LOOM_PRESETS.screen` layer2 (the thin gold layer) runs
`direction: 1` like layer 1, so everything in the jackpot spiral reads inward; the mockup counter-turned it and is not
copied. `shared/hypno/spirals/screen.gif` is re-woven from it with the Loom's own encoder (512 x 288, 72 frames,
5,693,013 bytes); `wake.gif` is unchanged. `kit-check.mjs` measures layer 2 on the kit and on the woven file.

**5. Slot jackpot about 1 in 6,500, table v6 at 102%.** `emi3` pays 400 at 1 in 6,494 (154 per million, was 1 in
12,987 in table v5, section 10 item 1). Every other plain pay weight is the v5 weight scaled by about 0.9715; pays,
melt, freeze, tape and seed rules are unchanged. Plain RTP and every held symbol class stay exactly 1.0200. Paytable
constant `TABLE_V6`. Published plain odds: `emi3` 1 in 6,494, `gif3same` 1 in 144, `sub3` 1 in 115, `spiral3` 1 in 115,
`gif3` 1 in 12, `sub2` 1 in 14, `spiral2` 1 in 14, `melt` 1 in 17. Held odds are unchanged except the held spiral table
(1 in 10 / 1 in 6 / 1 in 12, weights 100000 / 173593 / 86779). At the page pace (4.02 s an outcome, 896 outcomes an
hour) a jackpot lands about once per 7.2 hours of play. The page prints `state.table`; it keeps no copy.

**6. Cards deal floor 5000 ms.** `CARDS_FLOOR_MS` defaults to 5000 (was 8000); `state.floorMs` echoes it. Rules and pays
are unchanged. Bot estimate from the cards sim (`_evidence/hypno-followup/d-server/sims/sim-backroom-cards.log`, basic
strategy, stake 2 SP): 720 hands an hour at the floor, expected 28.2 SP an hour (was 17.6 at 450 hands under 8000 ms);
a human at one hand per 12 s is 11.8.

**7. XP: Back Room effects pay like the app's own effects.** Through the app's award path, never a new one:

| Primitive | XP | Paid by |
|---|---|---|
| `flash-burst` | FlashService's own: 4 per image (8 with the flash sound), x the lucky flash roll, `XPSource.Flash` | FlashService (unchanged) |
| `sub-single`, `sub-seq`, `sub-burst9` | 10 per word, `XPSource.Subliminal` | SubliminalService.FlashSubliminalCustom (unchanged) |
| `gif-full`, `gif-from`, `wash` WITH a picture | 4 per picture shown (FlashService's base with no sound), x `SkillTree.RollLuckyFlash`, `XPSource.Flash` | the dispatcher, once the overlay reports the picture on screen (`BackRoomFxXp`) |
| `wash` without a picture, `spiral-full`, `spiral-loom`, `brain-drain`, `brain-drain-melt`, `haze`, `tunnel`, `gif-rain`, `glitch-bubbles` | none (they pay nothing in normal play) | none |

- No double counting: a primitive that reuses a service that already pays is never paid by the dispatcher.
- Paid only when the picture is on screen: a busy-dropped wash or gif-from, a merged duplicate, a cancelled onset, a
  picture with no local file, one an overlay refuses while a display change settles, and one that fails to decode or
  comes too late pay nothing. The hero (`gif-full`) pays once its window has taken the picture. Caps and gates are `ProgressionService.AddXP`'s own: login (or offline username), idle
  suppression of `Flash` and `Subliminal`, the skill and Cycle multipliers.
- `IBackRoomFxSink.GifFull`, `Wash` and `GifFrom` take a `shown` callback, which the overlay calls on the UI thread when
  the picture is on; each onset pays at most once. `GifFrom` still returns false for no local file (the slot frees).

**8. GIF shortfall: cycle the player's own GIFs in every game (amends section 5).** Section 5's "shortfall filled from
built-in fallback art" is replaced, for every station and every `count`: with at least one pool GIF the host deals
`min(count, found)` pool GIFs and never pads with fallback art (10.13.C's rule, now for the slot's 4-GIF deal too);
with none it deals the 4 fallback loops. Pages cycle: the slot's symbol `gifN` wears `gifs[N % gifs.length]`, a card
value `i` wears `gifs[i % gifs.length]` (10.13.C). The host's `ResolveSymbols` cycles a GIF index past the deal the same way (`gif3` on a 2-GIF
deal is `g1`), so the fullscreen picture always matches the one on the page. Word indexes past the deal still resolve
to a random dealt word.

**9. Daily Daze hub under Calm.** The Loom hub keeps turning under Calm at half strength, as in the mockup; only the
OS reduced-motion setting (`prefers-reduced-motion`) or the app's Motion Off holds it still. The hub angle ACCUMULATES
(`stations/wheel/hypno.js` `stepHub`): each frame `hubRot += (abs(speed) x 0.9 + 0.35 x clamp01(scale)) x k x dtS`,
with `speed` the rotor's rad/s (either sign), `scale` the long last turn's time scale, `k` 1 (Calm 0.5) and `dtS` the
frame in seconds. So it always turns clockwise whichever way the wheel is flung, keeps its drift at rest and slows with
the warped clock. This replaces 10.13.F's `angle = -rotor.rotation.z` plus 0.35 rad/s row.

**10. Soft Hand.**

- Suspend (panic press 1, minimise) keeps the dealt deck and the sit-down; only leaving the station (Back, `close`,
  "Stand up, sit back down") re-deals on the next sit-down.
- The dealer is named Emi in every line and label that names the dealer.
- Deal cannot be pressed by any path while a fullscreen moment runs; presses are dropped, not queued. While the last
  hand's bloom, win flash or losing edges are on screen, Deal is disabled and reads "One moment", and the Space and
  Enter keys, repeated clicks, any room or host relay and a direct call into the deal action all do nothing (the page
  refuses inside the deal action, not only on the button). Once the moment ends the player presses Deal again. The
  server floor of decision 6 still applies.

**11. Unchanged and confirmed.** The slot's `spiral-full` plays the Loom weave (10.13.B, kept). Roulette under
Calm/reduced: the ball still runs at the slow-motion floor; rotor, beam and chips still. Roulette pace fits 8 s a spin.
Wheel taffy ghosts drawn over the slices. Fullscreen spiral budget (amends 10.13.B): the Loom encoder's own output is
accepted, long side 640 (512 when a weave runs past the worker's 6 MB soft cap), at most the Loom store's 8 MB cap,
judged on screen; `screen.gif` is 5.69 MB at 512.

**12. The room's Options.** The room HUD gets an Options pill beside Room view and Motion, opening a small card:
Effects (Calm / Normal / Full, the existing `AppSettings.BackRoomFxIntensity`, still also in Settings), Tunnel vision
(On/Off) and Melt (On/Off). While MotionLevel is below Full the card notes that Calm is in use. Escape or Back closes
the card before anything else in the room. Lexicon keys `br_opt_title`, `br_opt_effects`, `br_opt_calm`,
`br_opt_normal`, `br_opt_full`, `br_opt_calm_forced`, `br_opt_tunnel`, `br_opt_melt`, `br_opt_on`, `br_opt_off`.

- Page -> host (section 2.1): `{ "type": "room-option", "key": "tunnel" | "melt", "value": true | false }`,
  `{ "key": "intensity", "value": "calm" | "normal" | "full" }`,
  `{ "key": "mediaSource", "value": "auto" | "local" | "online" | "mixed" | "bundled" }`,
  `{ "key": "subVolume" | "sfxVolume" | "musicVolume", "value": 0..100 }` (an integer; a fraction and the
  string `"50"` are both dropped), or
  `{ "key": "mediaSubAdd" | "mediaSubRemove" | "mediaSubToggle", "value": "<niche>" }` where the niche matches
  `^[A-Za-z0-9_]{2,40}$`. No reply. Any other shape (a string `"false"`, a number where a string belongs, a
  level out of range, another key) is dropped; nothing is written once the room is closing. The host writes
  the setting and saves.
- **One niche per press, never a list.** The host owns the selection and the page reads it back off the next
  frame, so the two cannot disagree about what is selected. A list on this wire would make the page the owner
  and the host a stenographer. The cap is enforced host-side as well as in the room: a page is not a
  gatekeeper. Names are one community whatever their case. A niche switched off KEEPS its place in the list so
  it can be switched back on; a niche removed loses its disabled flag too, or the name would sit in the
  disabled list forever, shown by nothing and cleared by nothing.
- Host -> page (section 2.2): `init` and `settings` gain `intensityChoice` (`calm` | `normal` | `full`, the player's
  own choice), because `intensity` reads `calm` whenever MotionLevel is below Full. The page shows a press at once
  and the next `settings` frame has the last word.
- `init` and `settings` also gain `media` (`{source, effective, subs[], off[], cap, consented, ratio}`) and
  `audio` (`{sub, sfx, music}`, each 0..1 - the page hands them straight to the kit's bus setters and the
  music element). `effective` is the resolved source, so the page never has to know the app's own setting,
  and `consented` false means the picker paints the online rows DISABLED rather than hiding them: the player
  needs to see why Scrolller is not on offer.
- The Options card also carries the room's three levels - **Subliminal**, **Game sounds**, **Music and
  room** (`br_opt_vol_sub`, `br_opt_vol_sfx`, `br_opt_vol_music`) - plus Quality, and a pill that opens the
  picture picker (`br_media_*`). Dragging a level previews it; releasing it commits, so one drag is one
  settings write and not eighty. A `settings` frame repaints a level unless the player is holding it.
- The three buses live in `shared/sound/kit.js` under the existing master: subliminal is the whisper bed and
  `breath`, the bed bus is `ambience` and `spiral`, and SFX is everything else including both rolls. A
  load-time guard refuses a cue that names no bus. **Music and room** drives the soundtrack directly and the
  bed bus normalised against the soundtrack's own 0.15 default, because the beds already sit 24 dB under
  everything: one number scaling both would leave the ambience inaudible at the default.

## 10.15 Playbook Tier A amendment (2026-09-14)

Source: `backroom-casino-playbook.md` section 2, Tier A: the six page-side items that need no table change and no
server work. Where this disagrees with sections 6, 8 or 10.13 for the slot, this wins; the owner's decisions in 10.14
win over this. Everything here is
presentation over an outcome the tape already carries (section 3.2): the page never weights, moves or re-draws a
stop, it only changes how long it takes to show what the server drew (Law I). The 4 s pace (10.11) and table v6
(10.14 item 5: `emi3` 400 at 1 in 6,494, kept by the owner with no retune to 8 h) both still hold; A1 rides on top of
that pace, it does not replace either decision. A1's hold stretches the average outcome by about 5% (the table
below), so the table's jackpot lands about once per 7.6 h of play at the page pace instead of 7.25 h; the odds and
the pays do not move.

Measured over all 2,197 uniform stop combinations of the slot strips (`mock-server.js` STRIPS, 13 cells a reel; table
v6 changed pay weights only, the strips are the v5 strips),
which is what the numbers below are sized against:

| | Rate | Notes |
|---|---|---|
| A live pair on reels 1 and 2 | 390 of 2,197, 17.8% (about 1 in 6) | gif 52, spiral 117, sub 208, EMI 13 |
| A1's cost | +192 ms a spin on average | about +0.2 s, one outcome about 4.2 s instead of 4.0 s |
| A natural near miss (A2) | 9 of 2,197, 0.41% of spins, 0.95% of the 945 no-pay rows | gif 7, EMI 2 |

**A1 The anticipation reel** (built, lane F1-A). A live pair on reels 1 and 2 keeps reel 3 spinning past its normal
stop before THE THUD. Live pair, and the hold (`pace.js` `ANTICIPATION`, chosen by `feel.anticipation`):

| Pair | Hold | Melted (Brake 5) |
|---|---|---|
| The SAME gif id on both reels | 900 ms | 450 ms |
| Two spirals, any mix | 1,100 ms | 550 ms |
| Two subliminals, any mix | 1,100 ms | 550 ms |
| Two EMI | 1,400 ms, the bulbs go gold | 700 ms, never gold |

Two different gifs are NOT a live pair: `gif3same` is the line a gif pair is live for. During the hold reel 3 keeps
its blur, the marquee takes a `tease` mood (`tease_gold` on the EMI pair), the cabinet bulbs drop to 0.62 of their
emissive, and a tone climbs from reel 2's thud to reel 3's (`STAGGER_MS` + the hold, `sound.rise`, a synth sweep, no
new files). THE BREATH is already paused for the whole spin (Law III), and stays paused. **Reduced motion and Calm
keep the hold and keep the tone** (the economy pace never depends on the motion setting, `pace.js` header); they drop
the light change only. A frozen reel 3 never holds. `reelStopMs(i, PACE, holdMs)`, `reelsMs` and `outcomeMs` take the
hold; `PACE` itself is unchanged.

**A2 THE ALMOST on the strip** (built, lane F1-A; House Book move THE ALMOST, Law X). After reel 3 thuds on a spin
that pays nothing (`line === 'none'`) under a live pair, if the cell one step above or below the payline on reel 3
would have completed the line (the same gif for a gif pair, any spiral for a spiral pair, any sub for a sub pair, EMI
for an EMI pair), that cell ghosts to gold and snaps back once: 620 ms in total, gold in over 500 ms and ONE 120 ms
snap back, sharing the frame with the no-pay muted thud and THE SHIVER (Law X, one gesture one beat). A ghost note
sits under that thud; no second beat is added. It fires at most once a spin, and the cell below the line is read
first. Reduced motion: no travel and no repaint, one gold tint on the reel for 120 ms, then settled (Law VI).

The reel window shows the neighbouring cells, so the ghost needs no nudge: measured from `assets/slot.glb`, the drum
is r 0.43 with 13 cells (27.7 deg each) and `reel_window` is 0.39 tall on the same centre, so the payline cell fills
0.206 of the window and roughly half of each neighbour is inside it, foreshortened by the curve.

**No stop weighting anywhere.** The stops are whatever the server drew; the tell shows that truth, it never
manufactures it. The sim may REPORT the natural near-miss rate per table version (an optional `table.almostRate`
field is allowed, and is not required; nothing on the page reads it). Note for the owner, from the table above: under
tables v5 and v6 a sub pair already pays `sub2` and a spiral pair already pays `spiral2`, so those two can never be an
ALMOST, which leaves the tell to gif and EMI pairs at about 1 spin in 244. Widening it to "one short of a BETTER
line" (a spiral pair that was one cell off `spiral3`) is a table-shaped decision and is NOT built.

**A3 Proportional rollup** (sibling lane; THE BANK, Law XII). THE BANK's count-up scales with the win instead of a
flat 500 ms: tier 1 unchanged at 500 ms, tier 2 about 1,200 ms, tier 3 about 2,000 ms, the jackpot about 6,000 ms.
The chime ladder's pitch follows the rollup. One lever press, or Back, skips to settled (Law VI, Brake 7); reduced
motion is settled at once. Token flight (560 ms each, 70 ms stagger) and the counts by tier are unchanged.

**A4 Attract mode** (sibling lane; Law VIII, House Book deck II). After 25 s seated and idle the reels drift slowly,
the lights chase once and EMI winks. No SP moves and no tape is touched. Any press ends it within 100 ms (Law VIII).
Off under Calm, off under reduced motion, and off while melted (Brake 5).

**A5 EMI land-wiggle** (sibling lane; THE MASCOT GLANCE, Law XIII). Whenever the EMI symbol lands on any reel it
wiggles once after that reel's thud, and EMI glances. It pays nothing and says nothing about the next spin. Reduced
motion: none.

**A6 Payline frame** (sibling lane; Law IX). After a win the winning row gets a frame that pulses for the length of
the rollup (A3), so the two end together. No frame on a no-pay spin: that beat belongs to THE SHIVER.

**Kept out, by design** (playbook section 3, and this stays true for every later lane): near-miss stop weighting
(virtual reel mapping that parks a symbol just off the line more often than chance), losses disguised as wins
(celebrating a pay smaller than the stake), time-on-device design (no clocks removed, no moved exits), bet nudges and
denomination tricks, and low-balance nudges. The room mints SP and sells nothing; insufficient stays a quiet state.

## 10.16 Playbook Tier B and C amendment (2026-09-14)

Source: `backroom-casino-playbook.md` section 2 (Tier B: B1 the spiral jar, B2 the floor bell, B3 the
welcome-back comp; Tier C: C1 the EMI pair free re-spin, C2 must-hit-by on the wheel) and section 3 (kept
out), with `house-book.md` deck V "The Rake" and the Brake. Owner decisions it is built on:
`_evidence/brainstorm/DECISIONS.md` (wheel pot 250 +25/day cap 1,000, a win about every 10 days at any
community size, the 3-day account gate, Dazed 100 for young accounts, "do not overcomplicate this").
Where this section disagrees with anything above, it wins. Tier A (10.15) is unchanged and rides on top.

Everything here is still Law I: the server draws, the page shows. The jar, the bell, the comp, the re-spin
and must-hit-by are all decided server side and carried in the tape or the state; no page ever weights a
stop, mints an SP or invents an entry. The room mints SP and sells nothing (10.16.F).

Numbers below are decisions. `(owner may retune)` marks the ones the owner might want to move; everything
else is load-bearing arithmetic and moves only with a re-sim.

### 10.16.A B1 the spiral jar (slot, positive persistent state)

The melt is the negative carried state. The jar is its mirror: spirals the player has already seen, saved
up, paid back as free spins. Deck V's "sunk-cost bar toward the next rank", on the cabinet.

**Server state.** `user.backroom.slot.jar`, an integer `0 .. table.jar.size - 1`, next to `melt` in the slot
ledger. `emptySlot()` seeds it at 0; `ensureSlot()` coerces it with
`Math.min(Math.max(int(raw.jar), 0), TABLE.jar.size - 1)` so a stored value can never fire a jar on load.

**What fills it.** The count of spiral symbols SHOWN on any reel, in draw order, on outcomes of kind `paid`,
`free`, `respin`, `jar` and `emi_respin`. It does NOT count outcomes of kind `freeze`, nor any outcome a
freeze expanded into. (The playbook lists the freeze; it is excluded here for a reason. A freeze is sealed
from the melt (10.10.2) so that no freeze policy can beat 102%. Holding a spiral guarantees at least one
spiral a spin, so a jar that filled on freeze spins would let a held-spiral freeze farm jar spins and lift
`rtpFrozen` for that class from 1.0200 to about 1.14. The jar is a plain-play credit exactly as the melt is a
plain-play debt: only plain outcomes earn it, only plain outcomes are halved by it.)

**What a full jar does.** When the count reaches `table.jar.size` it drops by `table.jar.size` (the remainder
stays: 99 + 2 spirals fires at 100 and leaves 1) and pushes `table.jar.free` outcomes of `kind: 'jar'` onto the
same expansion queue `spiral3` uses, so `drain()` expands them inline at the point the jar filled, in draw
order, before the next paid spin. One outcome can fire the jar at most once (three spirals is at most 3 of
100), and the queue cap `MAX_OUTCOMES` (500) is unchanged.

**What a jar spin is.** Exactly a `spiral3` free spin with a different name: drawn from the plain table,
costing 0, halved while melted, consuming melt in draw order, able to land the `melt` malus and able to
expand further (`spiral3` into free spins, `spiral2` into a re-spin, `emi2` into an `emi_respin`), and its own
spirals fill the jar again. (The playbook's "never halved, no melt draw, no malus" describes the FREEZE seal,
not today's spiral free spins, which are not sealed. "Same as spiral free spins today" is the binding half of
that sentence: jar spins are not sealed. Owner may revisit.)

**Table v7 fields** (`TABLE_V7` in `backroom-slot.js`, replacing `TABLE_V6`):

```js
jar: Object.freeze({ size: 100, free: 3 }),   // (owner may retune, see the cost table below)
```

`kind` (section 3.2) is amended to `paid | free | respin | freeze | jar | emi_respin`.

**Outcome fields.** Every outcome gains `jarN`: the jar count AFTER this outcome, `0 .. jar.size - 1`,
alongside `meltLeft` and `freeLeft`. A `freeze` outcome carries back the jar it did not change. `freeLeft`
keeps its meaning (outcomes still queued) and now counts jar spins and the `emi_respin` too.

**`state` route.** `GET /v2/backroom/slot/state` gains a top-level `"jar": 17` (the stored count) and
`table.jar` = `{ "size": 100, "free": 3 }` in the published table. The page keeps no copy of either.

**RTP.** The jar is pure added return: at table v6 dressing a plain outcome shows 0.642 spirals on average, so
the decided 100-jar fills every 155.4 outcomes (the 25-jar of the first draft filled every 39.0, and took the
whole game from 1.0200 to 1.1178 left alone). Table v7 must land back at 1.0200 over the whole game,
jar spins and C1 included, with the jackpot at 1 in 6,493 per plain outcome (10.14). The retune is the same
move v6 made on v5: **only the six everyday pay weights scale by one factor** (`gif3same`, `sub3`, `spiral3`,
`gif3`, `sub2`, `spiral2`). `melt` stays 58,900, `emi3` and `emi2` are fixed by 10.16.D, pays, `meltSpins`,
`freeSpins`, `respins`, `stake`, `freezeCost` and the strips do not move, and `none` takes the remainder.
FOUR of the five `frozen` tables are UNCHANGED, and the held-SPIRAL one is re-tuned because its sealed free
spins read the plain table: `spiral2` 173,593 -> **177,589** and `sub2` 86,779 -> **88,795**, with `spiral3`
pinned and the 2:1 kept, so every held class still lands at exactly 1.0200 (CCP-Server #176).

The solved factor for the decided 100 / 3 is **0.977450**, with `gif3` then polished ALONE to land the exact
1.0200 (83,759 -> 83,746), giving the FINAL v7 plain weights over `DEN` 1,000,000 (CCP-Server #176):

| Line | v6 | v7 | Published odds (v7) |
|---|---|---|---|
| `emi3` (drawn direct) | 154 | **92** | 1 in 10,870 |
| `emi2` (new, 10.16.D) | - | **8,207** | 1 in 122 |
| `gif3same` | 6,940 | **6,784** | 1 in 147 |
| `sub3` | 8,675 | **8,479** | 1 in 118 |
| `spiral3` | 8,675 | **8,479** | 1 in 118 |
| `gif3` | 85,691 | **83,746** | 1 in 12 |
| `sub2` | 69,394 | **67,829** | 1 in 15 |
| `spiral2` | 69,394 | **67,829** | 1 in 15 |
| `melt` | 58,900 | **58,900** | 1 in 17 |
| `none` | 692,177 | **689,655** | - |

Plus the re-spin weight `respin.emi` = **7,555** (1 in 132) from 10.16.D. The jackpot, counting both paths,
is 154.004 per million plain outcomes = **1 in 6,493** (the 6,494 was v6's exact-154 figure). The `gif3`
polish that lands the exact 1.0200 is done, the way v6 polished `gif3` (10.14).

**Sim requirement** (`scripts/sim-backroom-slot.mjs`, extended by the server slot lane): exact whole-game RTP
1.0200 +- 0.0002 with the jar and the re-spin in, `exactFrozenRtp` still 1.0200 for all five held classes, the
jackpot 1 in 6,493 per plain outcome with the re-spin's share reported, and three new reported figures:
outcomes per paid spin, jar fires per 100 outcomes, and hit frequency. Expected at the decided 100 / 3: jar
fires every **155.4** outcomes (about 10.4 minutes at the 4.02 s pace) and hit frequency 24.89% -> **24.33%**
(still inside the playbook's 20-40% band); the 1.2066 outcomes per paid spin and the 12.1-outcome tape below
were the 25 / 3 draft's and both come down with the bigger jar. A 10-spin tape still becomes more outcomes, so
`nextBuyAt = outcomes.length * SLOT_FLOOR_MS` scales with it and nothing in 10.12 moves.

What the jar size costs, for the owner (each row re-solved to 1.0200):

| `jar.size` / `jar.free` | scale factor | jar fills every | hit frequency |
|---|---|---|---|
| 25 / 3 | 0.916369 | 39.0 outcomes, 2.6 min | 22.81% |
| 50 / 3 | 0.957035 | 77.8 outcomes, 5.2 min | 23.82% |
| **100 / 3 (decided 2026-09-14 evening: a jar is a come-back reason, not a rhythm)** | 0.977450 | 155.4 outcomes, 10.4 min | 24.33% |
| 200 / 5 | 0.980807 | 310.8 outcomes, 20.8 min | 24.41% |

**Client.** There is no glb node for a jar and no model request is allowed (section 9.6), so the jar is a DOM
element, exactly the pattern `.slot-payline` uses (10.15 A6, `scene.js` `paylineRect()`):

- `station.js` builds `<div class="slot-jar" aria-hidden="true" hidden><i></i><span></span></div>` next to
  `.slot-payline`. `station.css` styles it as a narrow upright tube, `position: absolute; z-index: 2;
  pointer-events: none`, `<i>` the fill and `<span>` the count.
- `scene.js` gains `jarRect()` next to `paylineRect()`: the projected bounds of `payout_tray` (fallback
  `cabinet`), taken at the cabinet's LEFT edge in screen space, one `reel_window` height tall. No new
  material, no new geometry, nothing added to the glb. The page also prints `17 / 25` inside it, so the jar
  survives motion level 0 (Brake 9).
- It fills per spiral landing, on that reel's THUD (Law X, one gesture one beat): a spin showing two spirals
  ticks the tube twice, on reel 1's thud and reel 2's, never before.
- A full jar is a tier 2 party (`feel.js` `recipe`, `tier: 2`: two notes, jolt, chase, screen) plus
  `fx.spiral_full` (section 4, an existing id, no new recipe), and THEN the `kind: 'jar'` outcomes play as
  free spins do. Brake 2: if the same outcome also won a line, the two merge into the higher party and the
  jar's own note is dropped.
- Reduced motion: no travel, the settled fill and the settled count (Law VI). Calm: the fill only, no party;
  `fx.spiral_full` still fires at its Calm recipe.
- Lexicon: `br_slot_jar` ("Spiral jar"), `br_slot_jar_full` ("The jar spills: {0} free spins").
- **Not drawn from the room.** The room would need this account's slot state, which it does not fetch. There
  is a `ctx` channel now, `ctx.revealedWin(amount, tier, text)` (`room/loader.js` -> `scene.celebrate`),
  which a station posts once a paid result has been revealed and the room spends on that fixture's coin
  shower; it carries the pay, its tier and the win text, never the jar's fill. So the room's `screen_status`
  label is unchanged and the jar still lives on the close-up cabinet only. (The playbook's "shows from
  across the room" is not built. Owner may revisit.)

### 10.16.B B2 the floor bell (community big-win ticker)

The casino rings a bell when a machine pays big. Social proof, Brake 1: it is somebody else's party, so it is
a line of text and never a sound.

**Server storage.** One Redis list, in `backroom-routes.js` `K`:

```js
bell: 'backroom:bell',                              // LPUSH + LTRIM 0 19, newest first, no TTL
bellRate: (uid) => `backroom_bell_rate:${uid}`,     // the read limiter
```

Capped at **20** entries by `LTRIM K.bell 0 19` in the same MULTI as the settle that wrote it (the
`extra(tx)` hook `writeSettled` already takes), so an entry and the receipt that earned it land together or
neither does.

**Entry shape**, JSON, at most 160 bytes (the route drops `name` rather than exceed it):

```json
{ "t": 1757890123456, "station": "slot", "line": "spiral3", "pay": 10, "name": null }
```

`t` = server `Date.now()`. `station` = `slot | wheel | cards | roulette`. `line` = the id below. `pay` = the
SP paid. `name` = the winner's display name at the time of the win, or `null`.

**What rings it.** Big lines only, and **at most one entry per settle**: the route picks the single best line
in the receipt it just wrote (up to 20 slot outcomes, up to 5 roulette spins, one hand, one wheel spin).
Without that cap a `spiral3` at 1 in 126 would write on roughly every second tape.

| Station | `line` | Fires on |
|---|---|---|
| `slot` | `emi3` | the jackpot |
| `slot` | `gif3same` | 3 of the same GIF |
| `slot` | `sub3` | 3 subliminals |
| `slot` | `spiral3` | 3 spirals |
| `wheel` | `jackpot` | the shared pot fell (never the Dazed fallback) |
| `wheel` | `dazed` | the 100 slice, the biggest everyday slice |
| `wheel` | `deep` | the 40 slice, the second biggest |
| `cards` | `blackjack` | a hand whose `outcome === 'blackjack'`, a natural twenty-one (`backroom-cards.js`) |
| `roulette` | `wake` | a spin with `wake === true` and `pay > 0`, the Spiral Wake double |

**Opt-in.** `user.backroom.bellName`, a boolean, absent reads as false. The name shown is the account's
`display_name` as it read at the moment of the win: trimmed, inner whitespace collapsed, **truncated to 24
characters**, `null` if it is not a non-empty string. It is stored ON the entry and never looked up at read
time, so a later rename does not rewrite history and reading the bell never touches another user's record. No
user file, no uid, no PII beyond that one opted-in display name.

**Routes.** Both ride the existing relay shape `{METHOD} /v2/backroom/{station}/{op}`, so the only C# that
changes is one `Ops` row (section 3), which the integration pass adds:

```csharp
["bell"] = new[] { ("GET","state"), ("POST","opt") },
```

- `GET /v2/backroom/bell/state` -> `{ ok, open, entries: [ ...at most 20, newest first ], optIn, visit: { day, comp } }`.
  One `LRANGE K.bell 0 19`. Auth and the door flag exactly like every backroom route (`closed` is 403 before
  any lock; a shut door still answers `state` with `open:false` so the room can show the sign). This route is
  also the room's visit ping (10.16.C) and is the only writer of `lastVisit`.
- `POST /v2/backroom/bell/opt { on }` -> `{ ok, optIn }`. Writes `user.backroom.bellName = !!on` under the
  three locks in the usual order. `bad_input` (HTTP 200) when `on` is not a boolean.
- Limiter: the shared per-account 60/min still applies, and on top of it `rateLimitIncr(K.bellRate(uid), 60)`
  caps `bell/state` at **12 a minute** per account. Over it the answer is HTTP 200
  `{ ok:false, reason:'too_fast', retryInMs }` (the wheel's soft convention, not the slot's 409) and the page
  keeps the entries it has.

**Client.** `room/hud.js` is plain DOM; `room/screens.js` is a shader on wall meshes that would need a new
texture and a new uniform per screen. The bell goes in the HUD.

- `hud.js` adds `<div class="br-bell" role="status" aria-live="polite" hidden><span></span></div>` under the
  `br-nav` pills, plus a `bell(entries)` method. `room.css` styles one line, one text colour, no background
  flash.
- Fetched by `room/main.js` on room open and again after each `station-close`. **Never polled while seated**,
  and never while a station holds the screen.
- **One line on screen at a time**, rotating through the entries every **8,000 ms**, newest first, wrapping.
  No sound at any intensity (Brake 1). Hidden under the existing `br-visiting` class and in the room view
  (`br-overview`), exactly as the Visit prompt is. Reduced motion and Calm: the line still rotates (it is
  text, not motion) but it cross-fades in 0 ms instead of 200 ms.
- Relative time comes from `entry.t` against the CLIENT clock, display only (Law I): under 60 s
  `br_bell_ago_now`, under 60 min `br_bell_ago_min`, under 24 h `br_bell_ago_hour`, else `br_bell_ago_day`.
- Anonymous entries read "someone hit 3 spirals 4 min ago"; an opted-in entry puts the stored name where
  "someone" was. Lexicon keys, English fallbacks in the page (Law VII): `br_bell_someone` ("someone"),
  `br_bell_slot_emi3`, `br_bell_slot_gif3same`, `br_bell_slot_sub3`, `br_bell_slot_spiral3`,
  `br_bell_wheel_jackpot`, `br_bell_wheel_slice`, `br_bell_cards_blackjack`, `br_bell_roulette_wake`,
  `br_bell_ago_now`, `br_bell_ago_min`, `br_bell_ago_hour`, `br_bell_ago_day`.
- **Room Options.** The opt-in toggle is a row in the room's Options panel, which 10.14 built (the third
  `br-pill` in `hud.js`'s `br-nav`, the `.br-options` card with the Effects intensity control and the Tunnel
  vision and Melt switch rows, `onOption(key, value)` -> `room-option`, painted by `options(v)`). The bell
  opt-in is the LAST switch row of that panel, `br_bell_optin` ("Show my name on the floor bell"), off by
  default. It is the player's own server setting, not a host setting: its press goes to a separate HUD
  callback `onBellOpt(on)`, which posts `bell/opt`, and the server's answer is painted back by
  `hud.bellOptIn(checked)` (an optimistic tick is put back on refusal).

### 10.16.C B3 the welcome-back comp

A loyalty comp: EMI hands a returning player five spins on the house. It costs the room five spins of return
and it gates nothing.

**Server state.** `user.backroom.lastVisit`, the UTC day number (`Math.floor(now / 86400000)`, the wheel's
`dayOf`), directly on `user.backroom` because the room owns it, not a station. Absent reads as -1.

**The visit ping.** `GET /v2/backroom/bell/state` (10.16.B) is the room's `state`/init call and the only
writer of `lastVisit`. When `today > lastVisit` it takes the three locks and, in one write:

1. reads `gap = today - lastVisit`;
2. mints a comp when ALL of: `gap >= 3`; the account is at least **3 whole days** old (the wheel's
   `jackpotEligible` / `createdMs`, the same gate as the wheel jackpot, so young accounts get no comp); and
   `user.backroom.slot.comp` is absent (an unspent comp is never replaced and never stacks);
3. sets `user.backroom.lastVisit = today`.

When `today === lastVisit` the route writes nothing at all, so the repeat calls after each station close are a
plain read.

The comp is stored as `user.backroom.slot.comp = { id, spins: 5, day }` with
`id = 'c_' + day.toString(36) + '_' + (econ.hash(uid) >>> 0).toString(36)`: deterministic per account per grant
day, so a retried ping cannot mint two. `spins` is **5** (owner may retune). Not daily: the wheel owns daily,
and a comp is only ever a RETURN after 3 or more missed days.

**`state` route.** `GET /v2/backroom/slot/state` gains `"comp": { "spins": 5, "id": "c_..." }` or `null`. The
slot state route never mints; it reports what the ping stored.

**Spending it.** `POST /v2/backroom/slot/tape` with `{ idem, comp: "<id>" }`:

- `comp` matches `^c_[A-Za-z0-9_-]{4,48}$`; `parseTapeBody` answers null (-> `bad_input`) otherwise.
- With `comp`, `freeze` must be absent and `count` must be absent or exactly `5`; anything else is `bad_input`.
- **Cost 0 SP.** `insufficient` therefore cannot happen, which is the whole point of a comp: a player at 0 SP
  can still play it.
- Five spins drawn from the NORMAL plain table. **Melt applies**: comp spins are halved while melted, consume
  melt in draw order and can land the `melt` malus. The comp is a gift, not a cleanse.
- They fill the jar and can expand into free spins, re-spins, jar spins and an `emi_respin` exactly as paid
  spins do.
- `tape_unplayed` still applies (finish the tape you have). The rate floor still applies
  (`now < slot.nextBuyAt` -> `too_fast`).
- Refusals: `comp_none` when no comp is stored, `comp_used` when the stored comp's id does not match the one
  sent (already spent, or from an older grant). Both HTTP 200. A retry with the SAME `idem` replays the stored
  receipt byte for byte as always; `comp_used` is for a fresh `idem` re-using a spent id.
- On success `settle()` clears `slot.comp`, and the receipt carries `"cost": 0` and
  `"comp": { "id": "c_...", "spins": 5 }` beside `tape`.
- `netSp` needs no special case: `slot.netSp += spAfter - sp` at a 0 cost already books it as a 0-cost tape,
  so `/v2/user/sync`'s `SkillPointBackfill` never refunds or erases it. Receipts are written and trimmed
  exactly as any other tape's.

**Client.** EMI hands it over; there is no ceremony (Brake 1: arriving is not an earned moment).

- On sit-down `station.js` reads `state.comp`. If it is there: the HUD status line reads `br_slot_comp` ("On
  the house: {0} spins"), a small chip `<span class="slot-comp">` sits on the cabinet beside the SP readout
  until the comp is spent, and EMI takes the `hearts` face for one `glanceHoldMs`.
- The first tape BUY of the sit-down is the comp (`request('tape', { comp: id })`), not the first press: a
  press that only plays an outcome already on the tape buys nothing and never spends it, and neither does a
  refused buy. Every
  press after that is a normal paid tape at `defaultTapeCount(sp)`. The spin button's `<small>` reads
  `br_slot_comp_cost` ("Free") for that one press.
- No party: a glance and one chime (`sound.chime` at tier 1, `tokens: false`), never a fanfare, never a
  REVEAL. The five spins themselves play exactly like paid spins and celebrate on their own merits.
- `mock-server.js` gains the comp: a `comp` option in `createMockServer`, `comp` in the `state` body, the
  0-cost tape branch and the `comp_none` / `comp_used` refusals, so the node tests drive it without a server.
- Lexicon: `br_slot_comp`, `br_slot_comp_cost`, `br_slot_comp_chip` ("On the house").

### 10.16.D C1 the EMI pair free re-spin (table v7)

The chase moment. Two EMI on reels 1 and 2, and reel 3 comes back for a second look.

**The new line.** `emi2` joins `LINES` between `emi3` and `gif3same`, and `lineOf()` checks it FIRST, before
`melt`:

```js
if (symbols[0] === 'emi' && symbols[1] === 'emi' && symbols[2] !== 'emi') return 'emi2';
```

So `emi, emi, melt` reads `emi2` and does NOT start the melt. `emi, X, emi` and `X, emi, emi` are unchanged
(`none` or `melt`): the re-spin is about reels 1 and 2, the same pair A1 already holds reel 3 for (10.15).
`emi2` **pays 0**. `FX.emi2 = []`: no effect id of its own, because the re-spin IS the event. It is a
plain-band outcome in every other way: it is halved (of 0) and consumes one melt while melted, and its spirals
fill the jar.

**The re-spin.** An `emi2` outcome puts exactly ONE outcome of `kind: 'emi_respin'` at the FRONT of the
expansion queue, so `drain()` plays it immediately after its parent even when free or jar spins are already
queued behind it (jar spins themselves queue behind a line's own free spins, so a `spiral3` that also fills
the jar drains `free, free, free, jar, jar, jar`). It holds reels 1 and 2 as `emi` and redraws reel 3 only,
from a dedicated two-band re-spin weight set, not from the plain table:

```js
respin: Object.freeze({ emi: 7555 }),   // over DEN; whatever is left draws a non-EMI, non-melt reel 3
```

- `emi` -> `symbols = ['emi','emi','emi']`, `line: 'emi3'`, `pay: table.pays.emi3` (400), `fx: ['fx.jackpot']`.
- otherwise -> reel 3 is drawn uniformly from the 11 non-EMI, non-`melt` symbols, `line: 'none'`, `pay: 0`,
  `fx: []` (plus `fx.sub_single` when the drawn symbol is a subliminal, section 4's overlay rule).
- **It can never land `melt`** (the symbol is not in its draw) and **is never halved** (`halved: false`): it
  neither consumes nor starts melt, like a freeze outcome. It expands into nothing: an `emi_respin` never
  queues a further re-spin, free spin or jar spin, and never fires a second `emi2`.
- Its spirals DO fill the jar (it descends from a plain-band spin).

**The arithmetic.** Measured over table v6's own class-first dressing (the sim, not a guess): a plain outcome
dresses EMI onto reels 1 and 2 with reel 3 something else on 11 of the 945 `none` triples and 1 of the 394
`melt` triples, which is **8,206.58 per million = 1 in 121.8**. `emi2`'s weight is set to that natural rate,
**8,207**, so the pair shows exactly as often as it does today; all that changes is what happens next. The
weight comes out of `none`, which pays 0, so lifting it costs no return.

Target: the jackpot is **1 in 6,493** (154.004 per million plain outcomes; 10.14's 1 in 6,494 was v6's
exact-154 figure), with about 40% of jackpots arriving through the re-spin.

```
 jackpot per plain outcome = P(emi3 drawn direct) + P(emi2) x P(re-spin lands emi)
                    154e-6 = 92e-6              + 8,207e-6 x q
                         q = 62e-6 / 8,207e-6 = 0.0075545   ->  respin.emi = 7,555 / 1,000,000  (1 in 132)
                 delivered = 92.000 + 62.004 = 154.004 per million = 1 in 6,493
             re-spin share = 62.004 / 154.004 = 40.3%
```

So the plain `emi3` weight drops from **154 to 92** (a direct 1 in 6,494 becomes a direct 1 in 10,870) and the
missing 62 per million comes back through the re-spin.

The re-spin's jackpot is never halved, while a direct `emi3` still is. 16.65% of plain outcomes are halved
(the melt chain's stationary share, unchanged), so moving 62 per million of jackpot into an unhalvable band
adds `62e-6 x 400 x 0.166497 = +0.00206` SP per outcome. That, plus the jar, is what the 0.977450 retune in
10.16.A absorbs. The re-spin also adds `8,207e-6` outcomes per plain outcome to the tape, which is inside the
1.2066 outcomes-per-paid-spin figure.

**Published table.** `publicTable()` gains an `emi2` row
(`{ id: 'emi2', pays: 0, odds: '1 in 122', respin: 1 }`), a `respin` block
(`{ emi: '1 in 132', jackpotShare: 0.40 }`), the `jar` block (10.16.A), and a headline
`jackpotOdds: '1 in 6,493'` so the page prints the TOTAL jackpot chance next to the direct-draw row.
Published odds must always be the total: the direct row alone would understate the jackpot, and published odds
are a playbook "already in" (10.1).

**Freeze.** A freeze spin never draws `emi2` and never starts a re-spin, and neither does a `spiral2` re-spin
that repeats a frozen table; under the seal `emi2` reads as `none`, exactly as `melt` does (10.10.2), so there
is no chase from a 2 SP freeze. The five `frozen` tables gain no `emi2` band, and C1 moves none of their
weights, so every held class stays at exactly 1.0200 (`exactFrozenRtp`); the one frozen re-tune in this
amendment is the held-SPIRAL table's, and the jar is what moved it (10.16.A). That also settles the held-reel-3
case the playbook asks about: a held reel 3 cannot be redrawn, so there could be no re-spin anyway; sealing the
whole freeze from C1 gives the same answer in every column and keeps "a freeze buy is worth the same in every
state" (`backroom-slot.js` header) literally true. (Owner may revisit: a chase moment on a 2 SP freeze would
need all five conditional tables re-tuned.)

**Sim requirement.** `sim-backroom-slot.mjs` reports, on top of 10.16.A's numbers: whole-game RTP 1.0200, the
jackpot 1 in 6,493 per plain outcome, the re-spin's share of jackpots (expect 40.3%) and the `emi2` frequency
(expect 1 in 122, about one chase every 8 minutes at the 4.02 s pace). A 30,000,000 paid-spin Monte Carlo
through the real draw pinned RTP 1.02024, 1.20658 outcomes per paid spin, `emi2` 1 in 122.9, jar fires 1 in
39.3 outcomes and a re-spin share of 40.85% (run at 25 / 3; jackpot-count noise at that size is about
+- 0.001 RTP).

**Client.** The re-spin is a second beat, not a second spin.

- `tape.js` treats `emi_respin` like `respin`: it plays on its own, with NO extra lever press, right after the
  `emi2` outcome, and the cursor advances through both.
- The `emi2` outcome itself plays as a no-pay landing with A1's EMI-pair anticipation already on it (10.15:
  1,400 ms hold, bulbs gold). It gets NO shiver and NO ALMOST tell (A2 is the release when the pair misses;
  here it has not missed yet), and `recipe()` answers `tier: 0` with `party: 'hold'`, a new quiet party that is
  a muted thud and nothing else.
- Then reels 1 and 2 stay exactly where they are, reel 3 spins again and takes the **full 1,400 ms gold hold**
  (`ANTICIPATION`'s EMI row, `pace.js`), **never halved here**, not even while melted: the Brake 5 halving in
  10.15 applies to A1's anticipation, not to this beat, because this beat IS the event, so it stays GOLD while
  melted as well (A1's own melted EMI hold is still halved and still never gold). Then THE THUD. On
  `emi3` the REVEAL plays exactly as any jackpot does (Law IX, once per sit-down).
- `pace.js`: an `emi_respin` outcome costs `SPIN_MS` for reel 3 only, plus the 1,400 ms hold, plus `THUD_MS`,
  `REVEAL_MS` and `BREATH_MS`: **4,660 ms** (1,800 + 1,400 + 340 + 600 + 520). `outcomeMs` takes a `kind` so
  the sim and the page agree.
- Reduced motion and Calm keep the hold and the tone and drop the light change, exactly as A1 does (10.15).
- Lexicon: `br_slot_line_emi2` ("2 EMI"), `br_slot_respin` ("One more look").
- `mock-server.js` gains the `emi2` class, the `emi_respin` outcome and the v7 weights, so the node tests and
  `dev.html` see the same shapes.

### 10.16.E C2 must-hit-by on the wheel jackpot

The climb becomes the event: at 1,000 it has to fall.

**Wheel table v3** (`backroom-wheel.js`, `TABLE_V3` replacing `TABLE_V2`): `v: 3`, and one new jackpot field:

```js
jackpot: Object.freeze({ start: 250, perDay: 25, cap: 1000, mustHitBy: 1000, minAccountDays: 3, targetDays: 10, minSpinners: 100 }),
```

Nothing else moves: not a slice, not a weight, not a width, not `snoozeCarry`, not `virtualStake`. `mustHitBy`
MUST equal `cap` (a test pins `TABLE_V3.jackpot.mustHitBy === TABLE_V3.jackpot.cap`) so the two numbers cannot
drift apart: the pot is lazy (`J = min(start + perDay * (day - seedDay), cap)`) and would otherwise sit at the
cap forever without reaching a separate must-hit line.

**The rule.**

```js
/** True while today's pot sits at the must-hit line and nobody has taken it today. */
const mustHitNow = (day, seedDay, table = TABLE_V3) =>
  jackpotAmount(day, seedDay, table) >= table.jackpot.mustHitBy && !potWonToday(day, seedDay);
```

`spin(input)` takes a new `input.mustHit` boolean and computes
`forced = input.mustHit === true && input.eligible !== false && !potTaken`.
`drawSlice(rng, odds, table, eligible, forced)` **still spends the same rng call**, so a seeded re-draw stays
aligned:

```js
const roll = Math.floor(rng() * U32);
if (forced || roll < odds.threshold) return sliceById(table, eligible ? 'jackpot' : GATE_FALLBACK);
```

- **Eligible spinner, pot unclaimed:** forced true, they take J, and the pot re-seeds exactly as today
  (`seedDay = day + 1`, tomorrow starts at 250 again).
- **Young account (under `minAccountDays`):** NOT forced. Their check runs at the normal odds; a natural hit
  still pays Dazed 100 as today and does not claim, so a young account never consumes the forced hit.
- **Pot already taken today:** `potTaken` makes `forced` false, so the second spinner that day draws at the
  normal odds and not a forced Dazed. This is the rule that stops must-hit-by minting Dazed 100 to every
  spinner in the room on the forced day.

**The race.** Unchanged: one atomic `SET backroom:wheel:jackpot:<day> "<uid>|<J>" NX EX 3d` claims the pot and
exactly one caller gets OK. The loser re-runs the same seeded draw with
`{ ...input, seedDay: day + 1, mustHit: false }`, which marks the pot taken and lands the Dazed fallback, never
a second pot. `readRoom` already folds a foreign claim into `seedDay = max(seedDay, day + 1)`, which drops J to
250 and makes `mustHitNow` false for everyone who reads after the claim lands, so the exposure is only
genuinely concurrent requests.

**`state`.** `GET /v2/backroom/wheel/state`'s `jackpot` object gains `mustHit`:

```json
"jackpot": { "amount": 1000, "odds": "1 in 6,644", "wonToday": false, "eligible": true, "mustHit": true }
```

`mustHit` is `mustHitNow(day, seedDay)`: a room fact, true whenever J is at the cap and nobody has taken it
today, whatever this account's age. `eligible` already says whether THIS account can win it. `POST spin`'s
response carries the same `jackpot` object, so the chip settles correctly after a win.

**Sim requirement.** `scripts/sim-backroom-wheel.mjs` reports, per room size (20 / 100 / 700 / 3,000 / 10,000
spinners), the mean days between wins and the distribution of the wait, with and without `mustHitBy`. Expected,
and pinned by an exact calculation: the pot reaches the cap on day 30 after its seed day, so

| | today (v2) | must-hit-by (v3) |
|---|---|---|
| Mean wait between wins | 10.00 days | **9.62 days** |
| Longest possible wait | unbounded | **31 days** |
| Cycles that end on the forced day | - | **4.24%** |
| Mean pot paid | 465.5 SP | **465.5 SP** |

Identical at every room size, because the per-spin odds already scale with yesterday's spinner count so the
room wins about every `targetDays` days whatever its size. **Must-hit-by costs the house nothing in
expectation** (the pot was already capped at 1,000, so the money is the same and only the wait is bounded), and
the sim must confirm exactly that: mean pot paid unchanged to within noise, and never more than one J minted
per UTC day at any room size.

**Client.** `stations/wheel/station.js` and `room/main.js` read `jackpot.mustHit`:

- The wheel station's jackpot readout (`readout.js`) prints `br_wheel_must_hit` ("MUST HIT") in place of the
  odds line while `mustHit` is true, with the amount still shown.
- The room's jackpot chip, the `screen_jackpot` label on the wheel fixture (`br_room_label_jackpot`), reads the
  same key, and the room's bell line (10.16.B) carries it as a standing first entry while it is true.
- **No new fx**, no new sound, no colour change beyond the gold the jackpot slice already has. The wheel's v3
  hypno moments (10.13.F) are untouched.
- Lexicon: `br_wheel_must_hit` ("MUST HIT"), `br_wheel_must_hit_room` ("The pot has to fall today").

### 10.16.F Kept out (playbook section 3)

Everything 10.15 kept out stays kept out, and none of B1, B2, B3, C1 or C2 reopens any of it. No near-miss stop
weighting: the stops are still whatever the server drew, `emi2` is dressed at its natural rate and the re-spin
draws from a published weight, so nothing is parked just off the line more often than chance. No losses
disguised as wins: `emi2` pays 0 and gets a muted thud, not a party, and the jar's party fires on the jar
filling (a real 3-free-spin event), never on a losing spin. No time-on-device design: the clock stays, Back
stays live at every frame, the bell never polls while seated, attract mode still stops on any press, and the
comp is five spins that end. No bet nudges and no denomination tricks: the stake is still 1 SP flat and the
comp does not change it. No low-balance nudges: the comp is offered on a RETURN after three or more missed
days, never on a low balance, and `insufficient` stays a quiet state. And the three Tier B items gate no
content and push no purchase: the room mints SP, sells nothing, and the jar, the bell and the comp are all paid
in SP that came from the room in the first place.

### 10.16.G Lane split

Four lanes, no shared file. Each can be briefed from this section alone.

| Lane | Items | Files it may touch |
|---|---|---|
| **Server slot** (S-b1) | A + D | CCP-Server `proxy/backroom-slot.js` (table v7, `jar`, `emi2`, `emi_respin`, the `comp` seam in `parseTapeBody` / `settle`, `publicTable`), `proxy/scripts/sim-backroom-slot.mjs`, `proxy/scripts/test-backroom-slot.mjs` |
| **Server room** (S-b2) | B + C + E | CCP-Server `proxy/backroom-routes.js` (`K.bell`, `K.bellRate`, the two `bell` routes, `lastVisit`, the comp grant, passing `comp` through the tape route, `comp` and `jar` in the slot `state` body, the bell write in each station's settle), `proxy/backroom-wheel.js` (`TABLE_V3`, `mustHitNow`, `spin`, `drawSlice`), `proxy/backroom-wheel-routes.js`, `proxy/scripts/sim-backroom-wheel.mjs`, `proxy/scripts/test-backroom-wheel.mjs`, `proxy/scripts/test-backroom-wheel-routes.mjs`, `proxy/scripts/test-backroom-routes.mjs` |
| **Client slot** (C-b1) | A + C + D | client `Resources/web/backroom/stations/slot/` only: `station.js`, `scene.js`, `feel.js`, `pace.js`, `tape.js`, `station.css`, `mock-server.js`, `station.md`, `tests/` |
| **Client room** (C-b2) | B + E | client `Resources/web/backroom/room/hud.js`, `room/main.js`, `room/room.css`, `stations/wheel/station.js`, `stations/wheel/readout.js`, `stations/wheel/mock-server.js`, `stations/wheel/station.md`, `smoke/room-check.mjs` |

- The two server lanes share no file. `backroom-slot.js` is the slot lane's; `backroom-routes.js` is the room
  lane's. The slot lane ships the 0-cost `comp` branch in `settle()` to 10.16.C's spec even though the grant
  that fills `slot.comp` is the room lane's, so neither lane waits on the other.
- The two client lanes share no file. `stations/slot/` is the slot lane's; `room/` and `stations/wheel/` are
  the room lane's. `stations.json` is touched by neither.
- Neither client lane touches C# and neither touches `Localization/Languages/en.json`. Each ships English
  fallbacks in the page and lists its new `br_*` keys in its own `station.md` (the room lane lists the room's
  in the `room/hud.js` header); the integration pass adds them to `en.json`, and the host sends every `br_*`
  key in `init.lex` (10.13).
- The one C# change in the whole amendment is the `Ops` row
  `["bell"] = new[] { ("GET","state"), ("POST","opt") }` in `Services/BackRoom/BackRoomApi.cs`, which the
  integration pass makes.
- Order: nothing blocks. The client lanes drive their own `mock-server.js`, so they can be built and tested
  before either server lane deploys. The server slot lane's table v7 and the server room lane's wheel v3 are
  independent re-sims.

## 10.17 The Prize Parlour: buying (2026-09-14)

The counter is the Back Room's only drain. This section is buying only: catalog, buy route, ownership on the
server, the counter station page (2D cards), the C# ownership read and the Discord role. What a prize DOES
(Jackpot Remix, Flashes v2, Bubbles v2, Racing Thoughts level locks) belongs to the prize effects work, which
reads ownership only through `OwnershipService.IsGranted(grantId)` (10.17.E).

Owner decisions (2026-09-14): bundles in any order (no prerequisites, no discount for a repeated 00);
Discord role needs a linked Discord to buy, permanent, grant only, never revoked; prizes 4-8 priced at
k = 2 over draft 1; prizes 1-3 fixed. One-time ownership, no refunds, no re-buys, no gifting. Prize spend
stays outside prestige (`lifetime_points_spent` is not touched). Nothing is sold before it works: every row
ships OFF and is switched on by env (10.17.B).

### 10.17.A Catalog v1

Prize ids are the `prize_id` metadata already baked into the approved shelf props (`counter.glb`
`shelf_<prizeId>` groups, blender-scripting `counter/prize-build/out/prizes-manifest.json`), so the room,
the cards and the server share one id. Ids are immutable; names are presentation (lexicon keys).

| prizeId | priceSp | grants | order |
|---|---:|---|---:|
| `jackpot_remix` | 15 | `fx.jackpot_remix` | 1 |
| `rt_demo` | 20 | `rt.original.00` | 2 |
| `high_roller` | 40 | `discord.high_roller` | 3 |
| `flashes_v2` | 240 | `fx.flash.drift_bounce`, `fx.flash.pendulum` | 4 |
| `bubbles_v2` | 240 | `fx.bubble.rain`, `fx.bubble.spiral_in` | 5 |
| `rt_bundle_1` | 1,200 | `rt.original.01`, `.02`, `.03` | 6 |
| `rt_bundle_2` | 3,600 | `rt.original.00`, `.04`, `.05`, `.06` | 7 |
| `rt_bundle_3` | 9,000 | `rt.original.00`, `.07`, `.08`, `.09`, `.10` | 8 |

Whole shelf 14,355 SP. Grant ids are the client contract with the effects work and never change:
`fx.jackpot_remix`, `fx.flash.drift_bounce`, `fx.flash.pendulum`, `fx.bubble.rain`, `fx.bubble.spiral_in`,
`rt.original.00` .. `rt.original.10` (two digits, the race `trackNum`, NOT the display `n`), and the
server-only `discord.high_roller`. An account's grants are the union over owned prizes, so `rt.original.00`
owned twice is one grant.

The catalog lives in server code (`proxy/backroom-counter.js`, `CATALOG_V1`), versioned by an integer
`catalogVersion` (1). Changing any `priceSp` or `grants` bumps it. Lexicon keys per row:
`br_prize_<prizeId>_name`, `br_prize_<prizeId>_blurb`. Racing Thoughts rows also carry
`br_prize_rt_note` ("A first demo built on the original files. More themes and mods are coming, on request."),
wording subject to the rights check before those rows go on sale.

### 10.17.B On sale

A row is buyable when its id is in env `BACKROOM_COUNTER_ON` (comma list, or `*`). Unset means every row
is `soon`. The door (3) applies first: closed accounts get 403 `closed` like every station. The owner turns
a row on only when its content works (`high_roller` first; the effect and RT rows when those lanes land).
`high_roller` additionally needs `DISCORD_HIGH_ROLLER_ROLE_ID`; without it the row reports `soon`.

### 10.17.C Server API

Host `Ops` row: `["counter"] = new[] { ("GET","state"), ("POST","buy") }`.

`GET /v2/backroom/counter/state` (a read under the shared 60/min limiter; it takes locks only to retry a
pending role, 10.17.D):

```json
{ "ok": true, "open": true, "sp": 812, "catalogVersion": 1,
  "discordLinked": true,
  "prizes": { "revision": 3, "grants": ["fx.jackpot_remix", "rt.original.00"] },
  "catalog": [
    { "id": "jackpot_remix", "priceSp": 15, "grants": ["fx.jackpot_remix"], "order": 1,
      "nameKey": "br_prize_jackpot_remix_name", "blurbKey": "br_prize_jackpot_remix_blurb",
      "sale": "on", "owned": { "at": 1789400000000, "paidSp": 15 } },
    { "id": "high_roller", "priceSp": 40, "grants": ["discord.high_roller"], "order": 3,
      "nameKey": "br_prize_high_roller_name", "blurbKey": "br_prize_high_roller_blurb",
      "sale": "on", "owned": null, "needs": "discord" },
    { "id": "rt_bundle_3", "priceSp": 9000, "...": "...", "sale": "soon", "owned": null }
  ],
  "delivery": { "high_roller": { "status": "pending", "tries": 1, "at": 1789400000000 } } }
```

`sale` is `on | soon`. `needs: "discord"` appears on `high_roller` when `discordLinked` is false. `delivery`
lists only prizes with an outside delivery (today only `high_roller`), `status` =
`pending | granted | not_in_guild | failed`.

`POST /v2/backroom/counter/buy` `{ unified_id, idem, prizeId, catalogVersion }`:

- `idem` as 3.4. `prizeId` must be a catalog id and `catalogVersion` an integer, otherwise `bad_input`.
- Order inside the locks (`purchase_lock` then `backroom_lock`, 3.4; a miss is HTTP 200 `busy`):
  1. receipt: a seen `idem` replays its stored receipt byte-identical. The receipt stores the prizeId; the
     same `idem` with a different `prizeId` is `idem_mismatch`, no write;
  2. `catalogVersion` differs: `catalog_changed` plus the fresh `catalog` and `catalogVersion`;
  3. row not `on`: `unavailable`;
  4. already owned: `owned` plus `prizes`;
  5. `high_roller` with no `user.discord_id`: `discord_required`;
  6. `sp < priceSp`: `insufficient` plus `sp`;
  7. settle in ONE atomic user write plus receipt: `sp -= priceSp`; `counter.owned[prizeId] = { at, paidSp,
     catalogVersion }`; `counter.netSp -= priceSp`; `counter.revision += 1`; for `high_roller`
     `counter.delivery.high_roller = { status: "pending", discordId, tries: 0, at }`.
- Success:

```json
{ "ok": true, "idem": "...", "prizeId": "flashes_v2", "paidSp": 240, "spBefore": 812, "sp": 572,
  "catalogVersion": 1, "prizes": { "revision": 4, "grants": ["..."] },
  "delivery": null }
```

  For `high_roller`, `delivery` is the status after the bounded grant attempt (10.17.D). The receipt stores
  the settle result; a replay returns it unchanged (the page refreshes `state` for live delivery).
- Refusals (all HTTP 200 `{ok:false, reason}`): `bad_input`, `idem_mismatch`, `catalog_changed`,
  `unavailable`, `owned`, `discord_required`, `insufficient`, `busy`; `closed` is 403. No rate floor
  (buying is rare); the shared 60/min limiter applies.

Storage: `user.backroom.counter = { netSp, revision, owned: { <prizeId>: { at, paidSp, catalogVersion } },
delivery: { high_roller: { status, discordId, tries, at } } }`. `backroomNetSp` already sums every
station's `netSp`, so `counter.netSp` joins the SkillPointBackfill sum and a later `/v2/user/sync` never
refunds a purchase. Receipts share `backroom:ids:<uid>` under the key `counter:<idem>`.

Pure module `proxy/backroom-counter.js`: `CATALOG_V1`, `CATALOG_VERSION`, `saleOf(id, env)`, `grantsOf(user)`
(sorted, deduped), `prizesBlock(user)` returning `{revision, grants}`, `parseBuyBody(body)`,
`settleBuy({ user, body, env, now })` returning `{ refusal }` or `{ user, receipt }`. Routes in
`proxy/backroom-counter-routes.js`, registered from `backroom-routes.js` with one call, reusing auth, door,
limiter, locks and the receipt helpers.

### 10.17.D Ownership everywhere else (server)

- **Profile sync.** `/v2/user/sync` and the auth validate response each gain the same `prizes` block
  (`prizesBlock(user)`). Ownership is never read from an upload: any `prizes`, `backroom` or grant field a
  client sends is ignored.
- **Account merge.** `/admin/merge-accounts` unions `backroom.counter.owned` (keeping the earlier `at`),
  sums `counter.netSp`, sets `revision` to the max plus 1, and keeps a `granted` delivery over a `pending`
  one. Logged in `merge_audit_log` like the other merged fields.
- **Discord role.** `discord-roles.js` gains `ensurePrizeRole({ unifiedId, discordId, reason }, deps)`: the
  same bounded PUT (4 s abort, never rejects) with role `DISCORD_HIGH_ROLLER_ROLE_ID`, its own dedupe marker
  namespace (`role_grant_prize:<uid>`, value `<discordId>:<roleId>`) and the same result statuses.
  Delivery: the buy settles and RELEASES both locks, then AWAITS one attempt (no fire-and-forget: Vercel
  freezes it), then writes the status under `user_write_lock` touching only `counter.delivery.high_roller`
  (`tries += 1`, `status`, `at`). A failed attempt never loses ownership and never debits again.
- **Retries.** A `pending`, `not_in_guild` or `failed` delivery is retried, awaited and bounded, from
  (a) `GET counter/state` when the last try is older than 10 minutes, and (b) the existing
  `queueSubscriberRoleSync` call sites (Discord link, login/validate), which also call `ensurePrizeRole` for
  an owner of `high_roller`. The role goes to the CURRENTLY linked `discord_id`; a relink grants to the new
  account and leaves the old account's role alone (grant only).

### 10.17.E Client ownership (C#)

- `OwnershipService` (PR H0, off main): `IsGranted(grantId)`, `Grants`, `OwnershipChanged`,
  `ApplySnapshot(accountId, revision, grants)` (ignores another account's snapshot and any lower revision),
  `Clear()`, constants `PrizeGrants.*` and `PrizeGrants.RacingTrack(trackNum)`. In-memory only, no settings
  flag. DEBUG builds only: env `CCP_PRIZE_GRANTS` (`fx.*`, `rt.original.*`, `*`, exact ids) for desk tests.
- **Feeds.** (1) ProfileSyncService applies the `prizes` block from sync and validate responses; logout and
  account switch call `Clear()`. (2) `BackRoomApi` applies the `prizes` block from every `counter` reply
  (state, buy success, `owned`) for the account it sent, so a buy unlocks in the app before the next sync.
- Effects and RT code consume `IsGranted` and `OwnershipChanged` only and never touch ProfileSyncService.
  The per-device arrival animation for new RT levels is effects/RT work, keyed on `OwnershipChanged`.

### 10.17.F The counter station page

`stations/counter/` (`station.js`, `cards.js`, `station.css`, `mock-server.js`, `station.md`, `tests/`,
`art/<prizeId>.webp`). Module shape per 7. v1 is DOM only (no WebGL canvas), so it adds no context.

- **Open.** `request('state')`, then one card per catalog row in `order`. Back is live before and during
  the read (Law VI). A failed `state` shows `br_counter_closed` and Back.
- **Card.** Art (`art/<prizeId>.webp`, a still rendered from the approved shelf prop; a CSS plate with the
  name when missing), name, blurb, price, and one of: `Owned` (plus the Discord delivery line for
  `high_roller`), `Soon` (dust sheet, no button), `Link Discord first` (`needs: "discord"`, no button),
  `Short by N` (price over `ctx.sp()`, button disabled) or `Buy`. Racing Thoughts cards show `br_prize_rt_note`.
- **Buy.** Buy opens an inline confirm on the card (name, price, balance after, Confirm / Cancel). Confirm
  sends `request('buy', { prizeId, catalogVersion }, idem)` with ONE idem per confirm, reused on retry. The
  button shows pending; Back still works (a buy in flight settles on the server; the next `state` shows
  it). Success: the card flips to Owned, `ctx.spReadout.set(sp)` then `set(null)` and `thud()`, one
  `sound.chime` (Brake 1: a small earned moment, no REVEAL, no tokens). Refusals `insufficient`, `owned`,
  `unavailable` and `discord_required` repaint from the reply or a fresh `state`; `catalog_changed` repaints
  the new catalog and asks again (never auto-buys at a new price); `busy` and network errors keep the
  confirm open with `br_counter_retry`.
- **Reduced / Calm.** No card tilt or flip animation; the state swap is instant.
- **Lexicon.** `br_counter_title`, `br_counter_closed`, `br_counter_retry`, `br_counter_buy`,
  `br_counter_confirm`, `br_counter_cancel`, `br_counter_after` ("Balance after: {0}"), `br_counter_owned`,
  `br_counter_soon`, `br_counter_short` ("Short by {0}"), `br_counter_link_discord`,
  `br_counter_delivery_pending`, `br_counter_delivery_granted`, `br_counter_delivery_not_in_guild`,
  `br_counter_delivery_failed`, `br_prize_rt_note`, and the 16 `br_prize_<id>_name|_blurb`. English
  fallbacks in the page; `en.json` rows by the integration pass; other locales later.
- **Registry.** The `stations.json` counter row gains `"entry": "stations/counter/station.js"` and
  `"state": "live"` in the integration pass only. Testers then see the counter live, even if every card is `Soon`.
- **Later, not v1.** Clicking a shelf prop opens its card (room raycast on `shelf_<prizeId>`); a 3D hero of
  the selected prop.

### 10.17.G Lane split

| Lane | Base | Files it may touch |
|---|---|---|
| **S-counter** (server core + routes) | server `feat/br2-bc-room` | `proxy/backroom-counter.js`, `proxy/backroom-counter-routes.js`, one register line in `proxy/backroom-routes.js`, `proxy/scripts/test-backroom-counter*.mjs` |
| **S-reach** (sync block, merge, Discord) | S-counter | `proxy/server.js` (sync/validate `prizes`, merge union, role call sites), `proxy/discord-roles.js`, the counter routes' delivery step, their tests |
| **H-own** (C# feeds) | `feat/br2-ownership-api` (H0) | `Services/Settings/ProfileSyncService.cs` (apply block, clear on logout/switch), its tests |
| **C-counter** (page) | this contract branch | `Resources/web/backroom/stations/counter/**`, a still-render script under `Scripts/` |
| **Integration** | merge of C-counter + H-own | `Services/BackRoom/BackRoomApi.cs` (Ops row, apply block), `stations.json` counter row, `Localization/Languages/en.json` counter rows, room smoke |

No two lanes share a file. Every PR under 600 changed lines, draft only.


### 10.18 Seated game stage (draft 2026-09-15)

This amendment permits cards and roulette to replace their canvas view with fixture-backed 3D,
while retaining the audited station state machine and standalone canvas fallback. Wheel migration
waits for its slice decision. No rules, probabilities, stakes, pays or outcome timing change.
A station opts in with `export const roomStage = true`; absent that flag its existing path remains.
The loader provides optional `ctx.stage` before mount and disposes it on failure, close or timeout.
The room seats with the existing `go(row)` pose, keeps rendering, drops walking and look input,
and restores the exact prior pose on exit. Back, SP, Options and the bell remain accessible.

Stage exposes `renderer`, `scene`, `camera`, `canvas`, `fixture`, `pick(event, objects)` and
`register(view)`. Picking returns intersections against only the supplied objects. A view may
attach runtime meshes to its fixture and register `update(dt, still)`, optional
`draw(renderer, camera)` and `dispose()`. Updates precede room drawing; optional overlay draws
share its renderer, camera and full-canvas scissor without clearing the room. The returned
unregister function disposes once; closing or halting also removes registrations. Views must
restore changed fixture state and release their own textures, geometry and listeners in dispose.
No view creates a WebGL context. Texture canvases are permitted; context checks distinguish them
from WebGL canvases and do not create a context while counting. `.br-seat` holds controls below
the persistent room chrome, passes empty-space input through, and fits 400 px without overflow.

## 10.19 Daily Daze reward wheel (owner approved 2026-09-15)
One free UTC-day spin remains. Ordinary results: Pocket Sparkles 15 SP 30%; Good Behaviour 30 SP 25%; Keep the Change 60 SP 15%; Spoiled Rotten 150 SP 5%; Room Service 10%; Seeing Double 12%; Head Empty 3%. Existing shared jackpot draw remains separate. Slice geometry follows server widths, never model placeholders.
Room Service grants a random unowned collectible decoration; a complete collection pays 75 SP. Collectibles are the six optional Room Service props: monstera, ivy, terrarium, gallery, portraits, billboard. Existing core customization remains free. Ownership is server-authoritative; no purchase or local ownership inference.
Seeing Double lasts 24 hours from the win, refreshes without stacking, and doubles actual Back Room winnings including SP gifts and jackpots, excluding returned stakes. A blackjack push gains no bonus. Server receipts own credited amounts; prepaid tapes settle their bonus at purchase time. Head Empty pays nothing and has a short quiet reaction.
Presentation: pink and cream enamel, brass dividers, large SP amounts with short names; Room Service cloche reveal; Seeing Double spiral-eye mark and a timed x2 charm by the balance; Head Empty bare cream wedge; Spoiled Rotten raspberry and coin shower. No auto-enable of decorations, no guest pass or bonus spins. All effects obey motion settings. Reward revelation occurs at the landed frame; server state may never reveal it early through another surface.

## 10.20 The slot on a phone (2026-09-15, phone desk run)
- **Tap target.** Any tap on a cabinet enters it. The room's glow (the bulb auras, the roulette motes) is one
  scene-level Points cloud and is never a surface: `raycast` is a no-op on it, and a bulb batched into a scene-level
  InstancedMesh maps back to its station by instance (`userData.rows`). No hitbox is enlarged.
- **`export const roomBehind = true`** (amends the station module shape in section 7): a station that keeps its own
  canvas may ask the loader to leave its root see-through (`.br-through`), so the room's pan-in is its load screen
  and its canvas comes up over the room's held frame. No loading card. The room still holds on arrival (one drawing
  context) and Back pans out to the exact walking pose; Motion off and reduced motion snap both ways. The slot's
  pan-in target is its reel window and jackpot screen, fitted on height alone (seat-camera.js `travelOnly`).
- **Sideways nudge.** In portrait on a coarse pointer the slot shows `br_slot_rotate` once a session, dismissed by a
  tap on the pill, hidden in landscape. A phone on its side (height at most 500 px) takes a 110 px left column for
  the pills, Freeze and Odds, Spin and the face keep the right corners, and the reel window fills the band between
  (stations/slot/scene.js `sideband`, the seat-camera mechanism: a span per axis and a view offset).

## 10.21 The spoken subliminal word (2026-09-15)

**Rule.** The slot's word beat used to be spoken by the browser's own `speechSynthesis`, which is a placeholder voice
on whatever the WebView happened to have. The words themselves were the player's subliminal pool. Both halves now come
from the app.

**The words.** `BackRoomMedia` deals the sit-down's four words out of everything CCP counts as selected AND active
right now, in this order:

| Source | Which rows | Setting |
|---|---|---|
| Subliminal pool | keys whose value is `true` | `Settings.SubliminalPool` (the app writes the per-mode / per-mod variant into it, and a running session prescribes its own) |
| Awareness keyword triggers | every trigger with `Enabled` | `Settings.KeywordTriggers` - the player's own plus the ones an installed preset (`KeywordTriggerPreset.MasterEnabled`) cloned in |

Deduped case-insensitively, shuffled with the deal's own seed. The four preset words (`br_word_drop`, `_relax`,
`_let_go`, `_sink`) only fill a SHORTFALL, so a player with nothing active still gets a full deal and a player with an
active list never hears the presets. A dealt word may be a whole trigger phrase: the page wraps it, it is never
truncated.

**The voice.** Each word is spoken by the HOST, which owns a real chain and the app's chosen audio output device:

**Its level is the ROOM's, not the app's (amended 2026-09-17).** This used to be
`MasterVolume x SubAudioVolume`, and it was the only piece of Back Room audio that read the app's settings at
all: everything else in the room is Web Audio inside WebView2 and never saw them. So a session preset moving
`MasterVolume` turned the casino's whisper down and left every lever, reel, win and the soundtrack exactly where
they were - the wrong half of the mix. It now reads `AppSettings.BackRoomSubVolume`, the room's own subliminal
level (10.14), on the same 1.5 power curve, so the whisper's character at a given level is unchanged and 0 is
still a deliberate mute. The output DEVICE is still the app's: the picker's choice is about hardware, not mix.
Where this paragraph disagrees with anything above about volume, this one wins.

| `source` | What plays |
|---|---|
| `clip` | the player's OWN audio for that phrase: an enabled keyword trigger's PlayAudio action (or its legacy `AudioFilePath`), else `KeywordTriggerService.FindLinkedAudio` - the active mod's `resources/sounds/flashes_audio`, then `Resources/sub_audio`. The same precedence the subliminal whisper already uses. |
| `preset` | a bundled Back Room clip: `Resources/Audio/backroom/words/words.json` maps a NORMALISED phrase to a plain file name in that folder. Ships with an empty map; the owner generates the clips. |
| `tts` | Windows speech (`Windows.Media.SpeechSynthesis`, no package - the project already targets `net8.0-windows10.0.19041.0`), a female voice in the machine's language where there is one, rate 0.75, pitch 0.9, rendered to a cached wav. |
| `none` | nothing played. ONLY then does the page speak the word itself with `speechSynthesis` (rate 0.85 / 0.7 reversed, pitch 0.8) - which is also what happens with no host at all, the Vercel phone playtest. |

**Normalised phrase.** Lower case, every run of non-letter / non-digit collapsed to one space, trimmed. `"Let Go!"`,
`"let  go"` and `"LET GO"` are one word. The preset clip's file stem is that with spaces as hyphens: `let-go.mp3`.
`scripts/backroom-words-manifest.mjs` prints the slugs the owner has to generate.

**Wire.**

```json
{ "type": "word.speak", "token": "32-hex", "text": "Let Go", "reversed": false, "seed": 2913771 }
{ "type": "word-ack", "token": "32-hex", "source": "clip", "durationMs": 640 }
{ "type": "word.stop" }
```

| Field | Host validation |
|---|---|
| `text` | 1..200 characters, else acked `none`. Never used as a path: a preset clip is found by looking the NORMALISED phrase up in the manifest, whose values must match `^[a-z0-9][a-z0-9_.-]{0,63}$` and resolve inside the words folder. |
| `reversed` | boolean only. `true` = the easter egg: the host decodes the clip and plays its sample FRAMES back to front, which is true backwards audio. The page still mirrors and reverses the SPELLING; it must not also speak the reversed spelling when the host answered anything but `none`. |
| `seed` | the outcome's seed (uint32), so a replayed outcome picks the same thing. |
| `token` | page-minted, correlates the ack. Exactly one `word-ack` per `word.speak`, `none` included, so the page never waits on a missing reply (its own cap is 1200 ms). |
| `durationMs` | how long the host's audio runs, 0 when silent (a deliberate mute reports its `source` with duration 0, so the page stays quiet instead of shouting the browser voice over it). |

**Timing.** The word beat asks on the frame the word's zoom-in starts (`WORD_IN_MS` 80 ms), so the audio and the
zoom open together. A chain of 2-3 words is `WORD_GAP_MS` (500 ms) onset to onset as before, but a `durationMs`
longer than `WORD_MS` (980 ms) holds the next onset until the clip is done, capped at `MAX_WORD_HOLD_MS` (3000 ms)
so a bad duration can never stall the beat. `callout.cancel()`, `cancelWords()`, `suspend` and `close` all send
`word.stop` and the host cuts the line (Law VI); a new `word.speak` also cuts the one before it, exactly as
`speechSynthesis.cancel()` did.

**Files.** Page: `shared/hypno/voice.js` (the adapter), `shared/hypno/callout.js` (`voice` option, the chain hold).
Host: `Services/BackRoom/BackRoomVoice.cs`, `IBackRoomVoice` / `BackRoomVoiceAck` in `BackRoomContracts.cs`,
`NullBackRoomVoice` in `BackRoomStubs.cs` (what the dev rig and the suite run on).

## 10.22 The reward pass (owner approved 2026-09-16)

Source: `house-book.md` laws IX, X, XII and XIII with Brakes 2, 3, 5, 8 and 9, and the owner's revisit of the
sentence 10.16.A closes on. The reward vocabulary was fully built and almost entirely spent at ONE fixture: the
slot has the bank, the ladder, the payline frame and the coin shower; the wheel carries half a copy; the
roulette and the cards have next to nothing, and at the roulette the SP number simply changes, which is Law XII
broken outright. `arcademy/shell/counterfx.js` has exported `sparkBurst`, `warmGlow`, `ghostGold` and `countUp`
since the Arcademy shipped, and the Back Room calls none of them. This section is that pass. Where it disagrees with
sections 8, 10.13, 10.15 or 10.16 for any station, this wins; 10.16.F (the room mints SP and sells nothing) and
Law I are untouched by it.

Everything here is still presentation over a settled number. The page never mints, weights or re-draws
anything; every effect below is a picture of something the tape or the server already decided.

**Amendment to 10.16.A.** That section ends "(The playbook's 'shows from across the room' is not built. Owner
may revisit.)" The owner has revisited, and it IS built. Strike that sentence. It is replaced by:

> The playbook's "shows from across the room" is built (10.22). Every fixture with a payout node gets the
> room-side coin shower, sized by the tier the shared spine decides. The jar's fill is still close-up only:
> what crosses the room is the PAY, never a station's private state.

### 10.22.A The room-side shower is every fixture's, not the slot's

`room/fixtures.js` builds a `payouts` entry behind `if (row.id === 'slot')`. That test goes. Any fixture whose
model carries a payout node takes a `createCoinShower`, and `room.celebrate(key, amount, tier, text)` finds it
by row key exactly as it does today. A fixture with no payout node is not an error and is not a fallback: it
simply has no shower, and its station's close-up party is the whole beat.

`room/coin-shower.js` is unchanged - it already reads tier 1-4 as 7 / 16 / 32 / 64 coins, already refuses an
amount of 0 or less, and already draws nothing while `still` (Law VI, Brake 8). The spine hands it a tier of 0
for a small win, and 0 never reaches it: a tier 1 is a close-up event and does not show from across the room
(Law IX - a small win never gets confetti).

### 10.22.B All four stations announce a paid result

`ctx.revealedWin(amount, tier, text)` (`room/loader.js` -> `scene.celebrate` -> `room.celebrate`) is called by
the slot alone today. From here, every station calls it ONCE, on the frame a paid result is revealed:

| Station | The frame it announces on | Amount |
|---|---|---|
| slot | reel 3's thud, the landed line's own frame (unchanged) | `o.pay` |
| wheel | the pointer settles, the landed slice's frame | `r.pay` |
| roulette | the ball comes to rest and the read is settled | `read.pay` |
| cards | the settle frame, after the hole card has turned | `hand.result.net` |

Rules for all four. Once a result, never per hand, per chip or per line: a split that won three ways announces
its net once (Brake 2). Never before the reveal - an announcement is the room learning what the player has just
learnt, so it can never tell the room anything first (Law I). A losing, pushed or snoozed result announces
nothing at all; the room is not told about a miss. `tier` is the spine's `plan.shower`, not the station's own
rung. Back, suspend and close fire nothing more: what is already falling is cleared by the room's
station-close, never by the page (Law VI).

### 10.22.C The spine: `shared/win/`

ONE place decides what a win is worth and what it may spend. Stations keep their own recipes and keep deciding
their own outcomes; they stop deciding their own restraint.

| File | What it owns |
|---|---|
| `shared/win/tier.js` | What a win is WORTH, 0-4. It normalises what a station's recipe already decided, so a tier 3 at the roulette buys the party a tier 3 at the slot buys. It replaces no station `tierOf`. |
| `shared/win/plan.js` | What a win may SPEND: `winPlan(tier, ctx)` -> a frozen `{ bank, shower, ladder, sparkle, reveal, glow, emi, partyMs }`. |
| `shared/win/bank.js` | THE BANK (Law XII), one copy of the move `stations/slot/bank.js` and `stations/wheel/bank.js` are two copies of. |
| `shared/win/ladder.js` | THE CHIME LADDER, lifted out of `stations/slot/feel.js`. |

All four are PURE - no DOM, no three, no audio, no timers - on the rule `stations/slot/feel.js` already lives
by, and all four are held by `shared/win/tests/`.

**`plan.js` is where the Brake lives.** Law IX and Brakes 2, 3 and 5 are enforced there and NOWHERE else. A
station that re-derives any of the following is a bug:

- **Law IX** sizes it: tier 1 a chime, tier 2 two notes and a jolt, tier 3 THE THUD, tier 4 THE REVEAL. A
  small win never gets sparkle, a shower or a reveal. The hero plays once a sit-down; the second jackpot of a
  sit-down is a very good tier 3.
- **Brake 2** merges: `mergePlans(a, b)` returns the HIGHER plan, never the sum. Two parties on one frame are
  one party.
- **Brake 3** wears it down: the first three wins of a rung get the fanfare, then a rung down, and from the
  fortieth it is a thud and the tokens. The ledger is `freshSit` / `sitPlan` / `afterParty`, one per sit-down.
- **Brake 5** quiets it: melted drops the rung to 1, kills the shower, the sparkle, the glow and the reveal,
  and drops the ladder an octave. The bank still flies - a melted win is quiet, not invisible.
- **Law VI** settles it: reduced motion returns the settled state, `bank: 0` and `partyMs: 0`, with the cue
  still playing. Calm is NOT reduced motion: the decoration goes, the value still moves.
- **Brake 8** respects the device: a lite board flies 4 tokens, never 7, drops the sparks, and keeps every
  sound.

### 10.22.D Three moves enter service

| Move | Enters at | Spec |
|---|---|---|
| **THE SPARKLE BURST** | tier 3 (7 sparks) and tier 4 (9), `plan.sparkle` | `counterfx.sparkBurst(host, { count })`, 5-9 pink and gold sparks under 600 ms. Never under lite, never under Calm, never under reduced motion, one burst a moment. It accompanies a big event and is never the event itself. |
| **THE GLOW** | every paying tier, `plan.glow` | `counterfx.warmGlow(node)`, warm cut, in fast and out slow, 480 ms. 0 while melted (Brake 5) and under reduced motion. Calm keeps it: a warm cut is not travel. |
| **THE JACKPOT LADDER** | the rungs themselves, `plan.ladder` and `plan.octave` | `ladder.js`: +1 semitone a step, cap 7, never a step closer than the 6 Hz floor, an octave down while melted. Tier 1 is the landing note alone. The royal rung is tier 4, and it is the rung that returns the flag a recap may stamp. |

`countUp` stays the Arcademy's: in the Back Room the readout is counted by THE BANK's own rollup, which ticks on
the landings (Law X) instead of on a timer. `ghostGold` is already in service as THE ALMOST (10.15 A2).

**Not built, and staying that way.** No new near-miss weighting, no losses disguised as wins, no second hero in
a beat, and no ceremony that a station may start without a plan. A station may always spend LESS than its plan;
it may never spend more.

### 10.22.E The room-side echo, as built (lane BR2-room, 2026-09-16)

10.22.A says a fixture with no payout node "simply has no shower". Every room glb was then read node
by node, and NOT ONE of them carries a payout node: not `slot.glb`, which is the cabinet the whole
move was written on, and not `wheel.glb`, `roulette.glb`, `card-table.glb` or `counter.glb` either.
The slot's shower was never aimed by a node at all - `coin-shower.js` drops coins at a hard-coded
(0, .276, .45) in the CABINET's own space, which is the Candy Rose tray and nothing else's.

So that sentence is replaced, and the rest of 10.22.A stands:

> An authored payout node (`payout_spawn`, `payout_tray`) is believed first and nothing in the room
> carries one today. A fixture without one is measured instead: its own bounds give a tray line,
> centred across the face the player walks up to (the row's `approach`), .72 of the way out toward
> that face and .155 of the way up. Those two fractions ARE the Candy Rose tray, so the fixture the
> numbers came from keeps them to the centimetre and every other fixture gets the same tray in its
> own proportions. `room/payout-anchor.js` owns the arithmetic and a host group carries it; a coin
> is the same size in the room at every fixture scale. A fixture with no visible geometry at all has
> no shower, and that is the only case that has none.

Three more things the floor does with a win, all of them sized by `shared/win/plan.js` and none of
them decided anywhere else (`room/win-echo.js`):

| The floor | When | What restraint takes it |
|---|---|---|
| THE LINE on the fixture's own screen | every echo, for `plan.partyMs` + 1800 ms | nothing. It is text and it survives Calm, reduced motion and motion level 0 (Brake 9). It restores whatever the screen said before the win, which is not always the boot label - the wheel carries MUST HIT (10.16.E). |
| THE AURA leaning gold | `plan.shower > 0` | Calm, reduced motion, melt and Brake 3, exactly as the coins are. |
| THE BOARD, the Parlour marquee carrying the winner's name and line | `plan.reveal`, so once a visit | the same, plus the hero cap: the second jackpot of a visit is a very good tier 3 and does not take the board. |

The room keeps its OWN Brake 3 ledger, and it is a second scope rather than a second opinion: a
station's ledger wears its close-up party down over one sit-down, the room's wears the floor's echo
down over the whole visit. A win is announced with the station's `plan.shower` and the room asks the
plan again with the ROOM's motion state, so Calm keeps the news and drops the decoration.

The echo is driven by the room's own accumulated clock, which STOPS while a station holds the screen.
A win taken at a cabinet therefore does not start ageing until the player is back on their feet: the
walk-back is not a timer, it is the room resuming.

Two more moves enter service with it, both `arcademy/shell/counterfx.js` calls the Back Room had
never made: THE GLOW on the SP chip when a bank token lands (`room/main.js` `spReadout.thud`, never
under reduced motion, where the existing lit branch is already the state), and the same warm cut with
a short drop and a squash under it when a won decoration is finally placed at Room Service
(`room/prop-landing.js`) - the one reward in the room with no number on it, which used to arrive by
`visible = true`.



### Physical feedback pass (2026-09-16)

- Reel startup recoil and damped settle bounce scroll the shared cabinet's shallow reel UVs. They do not rotate the shallow meshes or move any result/stop deadline. The lever rebounds and a new melt result gives one cabinet shiver.
- Cards use authored deck transforms, varied felt landings and weighted flips. Card landing cues occur on rendered touchdown, never when a suspended queue is flushed.
- Wheel peg clicks follow pointer crossings; the cabinet takes a short starting recoil. Roulette chips drop and settle at the bet location, and each result briefly traces from pocket to board without implying a win.
- Counter ownership applies as soon as the server confirms; a prize-art drop into a tray is presentation only. Back, failure handling and idempotency are unchanged.
- Calm, Off, reduced motion and suspension settle cosmetic responses. No new effects setting, spin delay, economy change or result weighting is added.
- `shared/sound/kit.js` remains the only audio context and mixer. `foley.js` supplies physical cues and loads three locally bundled ElevenLabs sound effects (card slide, cabinet knock, chip placement) after the first gesture. Ready samples replace the procedural cue; pending or failed samples use the immediate procedural fallback. No runtime generation or credentials.
- Foley follows master mute/volume, Calm trim, suspend, stop and disposal. Preloading never plays a cue. A late decode cannot populate a replaced context. The owner's quiet ambience and B/A lever/reel selection are retained.

### Desktop blackjack table controls (owner update 2026-09-16)

The player total sits left of the active hand. Totals 18-21 use a lifted green emphasis, lower totals settle gently, and bust totals shake and sink. Still/Calm keeps the number without motion. The table shows one chip per SP, with a selector for base stakes 1-3; split and doubled hands display their actual committed stake. Controls follow the hand: Stand left, slightly larger Hit right, Split below Stand and Double below Hit. Stakes remain governed by the server-advertised rules. The test preview enables 3 SP; the matching private-server change must ship before account-backed play offers it.


### Table feel follow-up (2026-09-16)

Blackjack reserves chip space outside six-card player footprints, including split hands.
One chip remains one SP. Bets add/drop and lift/remove; Double places the extra stake
before its last card travels. Split stakes follow their own hands. The active hand has
a restrained rim; finished hands square up. Dealer reveal contact gets a soft landing.
Settlement uses each server hand result: losses collect toward the dealer, wins return
the stake with the paid chips, pushes stay. These are presentation only.

Cards and roulette accept a limited drag on empty felt (about 4 degrees sideways,
2.6 degrees vertically), with eased motion and Center view. Game targets own their
existing gestures. Off/Calm, suspension, transitions and seat disposal cancel the look.
Projected hand totals and bets follow the table; blackjack action controls stay steady
while looking. No extra renderer, network request or server outcome change.


### Slot chase rewards: bonus Daily Daze spins (2026-09-17)

The slot prize sheet shows a server-selected, column-specific combination: one specific GIF,
one spiral of any style, and one wildcard. A session token is minted once per room page lifetime;
returning to a slot does not change it. POST slot/chase `{session,cursor?}` registers the target.
An unfinished prepaid tape pins its original target until consumed. The GIF deal remains stable
within that page session, and missing pictures use distinct fallback artwork rather than aliases.

`wheelChase {id,symbols,oneIn,bonusSpins}` appears on chase/state/tape replies. Symbols use gif0..3,
`spiral`, and `*`. Only paid plain outcomes qualify, never freeze, complimentary or free outcomes.
Every real match carries `wheelBonus:1`. Existing stop weights and SP payouts are unchanged.
The current exact average is 1 in 44.1558 paid spins, not a guaranteed interval or a progress meter.
The displayed odds round to 1 in 44. Credits settle atomically with the tape; the slot announces
one only on its matching landing and never reveals credits from its still-unplayed outcomes.

Wheel state/spin replies carry `bonusSpins`, `canSpin`, `bonusMode`, and authoritative `slices`.
Daily allowance is consumed first. Further spins spend earned credits with ordinary rewards,
including decoration/Seeing Double/Head Empty, but exclude the growing jackpot and its daily
spinner count. Replayed requests reuse their receipt and never consume another credit. The
last bonus landing persists. UI uses reply slices before landing, including old daily receipts.
Unsupported chase endpoints leave legacy slot play available without the bonus row.

This increases direct expected SP by about 0.646 per paid spin, or 0.815 with the decoration
collection complete, before Seeing Double. It is additional return, not a retune back to 102%.

### Back Room text and flash previews (2026-09-17)

Announcer and word callouts use bundled Fredoka with rounded system fallbacks, larger fitted text, squash/wobble entry and a soft breathing hold. Motion Off and reduced motion retain the quiet fade. All four station announcers read live motion settings.

Authored Back Room flash bursts may sample Still, Drift and Bounce, or Pendulum without owning Flashes v2. This room-only presentation does not grant a prize, change ambient settings, or bypass global motion controls. Desktop compositor previews stay within peripheral lanes; classic flash windows remain still. The browser stand-in uses the shared bounded preview keyframes. Shatter remains the existing interactive prize effect, not part of this automatic motion sample.

### Interactive flash showcase (2026-09-17)

Back Room flashes accept a drag and release without V2 ownership. A third of releases sample shatter; the rest slide or fling according to gesture speed. Full-motion shatter uses nine picture tiles. This replaces the earlier preview-only restriction on shatter, but it still requires user interaction and never runs on expiry. Motion Off uses a quiet dismissal. Desktop uses the existing compositor drag and shatter engines; classic windows retain their normal dismissal. Ambient preferences and prize grants are unchanged. Shared subliminal text targets 30vh with a thicker pink stroke, fitting down only where the viewport requires it.

Roulette exit exception requested 2026-09-17: block station exit while requesting/playing a spin or while interactive browser flashes remain, including a 500 ms double-click grace after removal. Background tap-to-exit is restricted to the bottom 8 percent (maximum 60 px); explicit Back works once the guard clears. This supersedes unconditional Back during roulette play. Desktop landscape uses a higher camera angle; compact phone framing stays intact. All Back Room flash previews are 20 percent larger and drift at varied slow speeds when motion is enabled.

## Casino and racing window transfer (2026-09-17)
The racing cabinet opens Racing Thoughts in the current window with `game-open {game:"race"}`. The host acknowledges `game-open-result` and finishes the room close handshake before transferring its browser. Race init sets `settings.returnToCasino`; normal exit returns to `/backroom/index.html?raceReturn=1`. A bounded one-use camera pose survives; no balance or reward state is restored from it. The separate Play entry retains normal exit behavior. Native callbacks are invalidated on transfer, and each run may settle rewards once. Preview uses the same camera contract with a local-ledger-only adapter and full same-origin navigation.

**The cabinet is not a purchase door (2026-09-18).** The stack that wrote this section had the host validate canonical original-track ownership before the transfer, and refuse with `reason:"locked"`. That check is disarmed: the owner removed Racing Thoughts' tier gate on 2026-09-17 and chose open testing on both surfaces, and a purchase check is the same closed door under another name. The rule survives whole in `Services/Race/RacingAccess.cs` behind one constant, so the cabinet's only refusal today is `reason:"busy"` when a race window is already up. ONE PAYOUT PER RUN is a separate rule and is in force: `RaceRunLifecycle` latches the run and never asks about ownership.
