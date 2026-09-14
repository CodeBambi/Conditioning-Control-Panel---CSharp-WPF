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
| `media-request` | `reqId, station, count?` | Deal media for a sit-down (`count` 1..13, default 4, 10.13.C). Reply `media`. |
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
| `fx-ack` | `token, fired[], skipped[]` | What actually played. `skipped` entries: `{prim, why:'toggle'\|'motion'\|'calm'\|'busy'\|'unknown'}`. |
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

Effect ids are global (every station reuses them) and resolve on the host into primitives:

| fxId | Calm recipe | Normal recipe (design doc) | Full recipe | Gated by |
|---|---|---|---|---|
| `fx.jackpot` | `spiral-full` 2 s + `gif-full` 1.5 s + `sub-single` | hero window 2.4 s: `spiral-full` 2.4 s, then `flash-burst` 4, `gif-rain` 2 s, `glitch-bubbles` 1, `sub-burst9` | hero window 4 s: `spiral-full` 4 s, `flash-burst` 8, `gif-rain` 4 s, `glitch-bubbles` 3, `sub-burst9` twice, `gif-full` 2 s | per primitive |
| `fx.gif_storm` | `flash-burst` 1 | `flash-burst` 4 + `gif-rain` 2 s + `glitch-bubbles` 1 | `flash-burst` 6 + `gif-rain` 3.5 s + `glitch-bubbles` 2 | Flash |
| `fx.sub_cascade` | `sub-seq` 2 + `gif-full` 1.5 s | `sub-burst9` then `gif-full` 1.5 s | `sub-burst9` then `gif-full` 2.5 s | Subliminal, Flash |
| `fx.spiral_full` | `spiral-full` 2.5 s, half opacity | `spiral-full` 2.5 s | `spiral-full` 4 s | Spiral |
| `fx.spiral_brief` | `spiral-full` 1.2 s, half opacity | `spiral-full` 1.2 s | `spiral-full` 2 s | Spiral |
| `fx.gif_burst` | `flash-burst` 1 | `flash-burst` 2 | `flash-burst` 3 | Flash |
| `fx.sub_pair` | `sub-seq` 2 | `sub-seq` 2 then `spiral-full` 1.2 s | `sub-seq` 3 then `spiral-full` 2 s | Subliminal, Spiral |
| `fx.sub_single` | `sub-single` per word | same | same | Subliminal |
| `fx.melt` | `brain-drain-melt` 4 s, half intensity | `brain-drain-melt` 6 s | `brain-drain-melt` 9 s | BrainDrain |

Full never breaks the Brake: no strobe over 6 Hz, one hero at a time, toggles still win.

Primitives (C3 maps them onto existing services; `gif-full` is the only new overlay):

| Primitive | Service call | Motion rule |
|---|---|---|
| `flash-burst n` | `App.Flash.TriggerFlashOnce(n, FlashDuration, null, true)` with the dealt GIFs | `Off` -> n=1 |
| `gif-rain s` | `ChaosGifCascadeOverlay.Show(...)` / `EmiGifRain` constants | skipped at `Reduced`/`Off` |
| `glitch-bubbles n` | `ChaosFlashOverlay.Show(ms, 0.3)` | skipped at `Off` |
| `sub-single` | `App.Subliminal.FlashSubliminalCustom(text, null, null, true)` | always |
| `sub-seq n` / `sub-burst9` | the same, looped at >= 220 ms per word (Brake: no strobe over 6 Hz) | `Reduced` -> `sub-seq 2` |
| `spiral-full s` | ~~`OverlayService.ShowOverlayTimed("spiral", ms, opacity)`~~ amended 10.13.B: plays `spiral-loom` (preset `screen`), a Loom-woven spiral | `Off` -> static frame |
| `brain-drain-melt s` | `ShowOverlayTimed("braindrain_melt", ms, BrainDrainIntensity)` | `Off` -> `braindrain` (no drip) |
| `gif-full s` | NEW: fullscreen single-GIF overlay across screens | `Off` -> still frame |

Rules the host enforces, not the page:
- A primitive whose feature toggle is off is skipped and reported, never forced on.
- New setting `AppSettings.BackRoomFxIntensity` = `Calm | Normal | Full` (default `Normal`), shown in
  the room and in Settings. `Calm` also applies whenever `MotionLevel != Full`, whatever the setting.
- One hero at a time (Brake 2): an `fx` arriving while a hero window runs is queued, merged if it is the
  same id, and dropped after 4 s in queue (`busy`).
- `suspend` or `close` cancels every running primitive the room started.
- Words and GIFs in `symbols` are resolved from the host's own dealt `media` by key. Unknown keys are
  replaced with a random dealt item; nothing from the page is used as a path.

## 5. Media feed

```json
{ "type": "media", "reqId": "...", "seed": 918273,
  "gifs":  [ { "key": "g0", "url": "https://ccp.assets/<folder>/<file>.gif", "w": 480, "h": 270, "src": "pool" },
             { "key": "g3", "url": "https://ccp.game/backroom/stations/slot/fallback/gif3.webp", "src": "fallback" } ],
  "words": [ { "key": "s0", "text": "Drop", "src": "preset" }, { "key": "s1", "text": "...", "src": "pool" } ] }
```

- 4 GIFs from `App.Flash.GetChaosImagePaths` filtered to local animated files (`IsRemotePath` false),
  deduped by FULL PATH (trap 147), shuffled with `seed`; shortfall filled from built-in fallback art.
- Up to 4 words from the active `SubliminalPool` (mode/mod variant as the app uses), shortfall filled
  from presets `Drop, Relax, Let Go, Sink` in that order. Preset text is a lexicon key (Law VII).
- Symbol id -> media: `gif0..gif3` = `gifs[0..3]`, `sub0..sub3` = `words[0..3]` (a 13-GIF deal: 10.13.C). Dealt once per sit-down
  and kept until the player stands up, so a reel cell never changes face mid-tape.
- URLs point only at `ccp.assets` (the user's folders, mapped read-only) or `ccp.game`. Keys, never
  paths or URLs, go back to the host in `fx.symbols`. Nothing in `media` is ever sent to the server.

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
- **Visiting.** E holds the room: pose saved, render loop stopped (no rAF), context KEPT. Measured in the
  smoke run: about 40 ms from Back to a drawing frame, against about 1.1 s to boot and decode the room
  again, so the room keeps its context. Back puts you on the exact position and facing.
- **Still.** The floor spiral, bulb chase, wall-picture turn and roulette hub freeze and the head sway is
  off whenever `reduced` is true or `intensity` is `calm` (the Motion button is locked then), or when the
  player picks Motion still. A `settings` frame applies live.
- **Wall screens.** One `media-request` with `station: "room"` at boot. `gifs` that are not
  `src: "fallback"`, point at `ccp.assets` or the page's own origin, and load CORS-clean go on the four
  screens (one turn every 18 s); otherwise the house art (`room/assets/ads/*.webp`) with lexicon
  captions. The preview's local file picker is not carried over.
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
- **Budget.** At 1280x720 on the entry pose: 212 draw calls (the preview draws 1,268 there, 1,312 in its
  own check), no shadows, no post passes, pixel ratio capped at 1.5.

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
- Gates are for DRESSING. The host still enforces every toggle in `BackRoomFxPlan` (its `FxGates` keeps `Melt` and
  `SpiralStill`). A page never forces an effect because a gate said true.
- Page side (K1, `room/loader.js`): `ctx.gates` is a live frozen `{flash, subliminal, spiral, brainDrain}` getter;
  `ctx.onSettings(fn)` subscribes to `{motion, intensity, reduced, gates}` and returns an unsubscribe. A host that sends
  no `gates` reads as all `true` (the host is the enforcer).
- What a station dresses plain when a gate is off (from the first frame, and live on `settings`):

| Gate off | Plain dress |
|---|---|
| `flash` | no `fx.wash` / `fx.gif_from`; card faces show rank and suit with no picture, the sit fan too |
| `spiral` | no `fx.loom_spiral`; card backs are a brass crosshatch, the wheel hub a brass star, the roulette turret dish stays velvet (a Spiral Wake still shows as text and a gold rim glow) |
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
- H1: `IBackRoomMedia.Deal(station, seed, count = 4)`; `BackRoomBridge` reads `count`;
  `BackRoomFxPlan.ResolveSymbols` accepts one or two digit indexes (`g0`..`g12`, `gif0`..`gif12`); `MaxSymbolKeys`
  stays 8.

### 10.13.D The hypno kit (`Resources/web/backroom/shared/hypno/`, K1)

Plain ES modules with no three.js import (three stations upload the kit's canvases themselves). `index.js` re-exports
every name below. Imports: `../../../arcademy/engine/loom/loomField.js` and `../../room/gif-decode.js`.

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
  speed 2; `screen` {6, 2.5, 0.5, log, 1, #ff5fa2 #9b6bff #5fffd0, gradient}, layer2 {enabled, 3, 1.5, 0.3, log, -1,
  #e8c27a}, bg radial #14060f -> #08040e, glow 0.5, pulse {amp 0.08, cycles 1}, speed 1. `wake` and `screen` are the
  two the host plays woven.

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
  loom_spiral; `brainDrain`: haze, tunnel), fires the rest through `ctx.fx` with the table's Normal `args` merged with
  the caller's, and returns the page effect names for the station to run at `strengthK(ctx)`. An unknown id fires
  nothing and logs.
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
split aces one card each, six-card Charlie pays 1:1, no surrender, no insurance, stake 1 or 2 SP, one open hand,
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
| `station.js` | `mount(ctx)`; DOM, buttons (Deal; Hit, Stand, Double, Split from `legal`; bet chip 1 or 2, default 1 below 30 SP; hint toggle off by default, remembered in `localStorage` `br_cards_hint`; "Stand up, sit back down" while no hand is open), the request flow, moments |
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

## 10.14 Follow-up amendment (2026-09-14, owner "Path to play" and build question 6)

Where this section disagrees with anything above, it wins.

1. **Slot table v6:** `emi3` pays 400 at 1 in 6,494 (154 per million, was 1 in 12,987). Every other plain pay
   weight is the v5 weight scaled by about 0.9715; pays, melt, freeze, tape and seed rules are unchanged. Plain
   RTP and every held symbol class stay exactly 1.0200. Paytable constant `TABLE_V6`. Published plain odds:
   `emi3` 1 in 6,494, `gif3same` 1 in 144, `sub3` 1 in 115, `spiral3` 1 in 115, `gif3` 1 in 12, `sub2` 1 in 14,
   `spiral2` 1 in 14, `melt` 1 in 17. Held odds are unchanged except the held spiral table (1 in 10 / 1 in 6 /
   1 in 12, weights 100000 / 173593 / 86779). At the page pace (4.02 s an outcome, 896 outcomes an hour) a
   jackpot lands about once per 7.3 hours of play. The page prints `state.table`; it keeps no copy.
2. **Cards deal floor:** `CARDS_FLOOR_MS` defaults to 5000 (was 8000); `state.floorMs` echoes it.

## 10.15 Playbook Tier A amendment (2026-09-14)

Source: `backroom-casino-playbook.md` section 2, Tier A: the six page-side items that need no table change and no
server work. Where this disagrees with sections 6, 8 or 10.13 for the slot, this wins. Everything here is
presentation over an outcome the tape already carries (section 3.2): the page never weights, moves or re-draws a
stop, it only changes how long it takes to show what the server drew (Law I). The 4 s pace (10.11) and the jackpot
retune owed in `_evidence/brainstorm/DECISIONS.md` both still hold; A1 rides on top of that pace, it does not replace
the decision.

Measured over all 2,197 uniform stop combinations of the table v5 strips (`mock-server.js` STRIPS, 13 cells a reel),
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
table v5 a sub pair already pays `sub2` and a spiral pair already pays `spiral2`, so those two can never be an
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
