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
| Slot station page | client `Resources/web/backroom/stations/slot/` | C2 |
| Host window + service | client `Services/BackRoom/BackRoomHostService.cs` on `ChaosWebViewHost` | C1 |
| Station request relay | client `Services/BackRoom/BackRoomApi.cs` | C1 (shape), C2 (slot ops) |
| Effect dispatcher | client `Services/BackRoom/BackRoomFx.cs` | C3 |
| Media feed | client `Services/BackRoom/BackRoomMedia.cs` | C4 |
| Slot server | CCP-Server `proxy/backroom-slot.js` (pure), `proxy/backroom-routes.js`, `proxy/scripts/sim-backroom-slot.mjs` | S1 |

Origins, same scheme as the Arcademy host: `https://ccp.game/` maps `Resources\web` (Deny), page URL
`https://ccp.game/backroom/index.html`, three.js from `https://ccp.game/vendor/three/` (identical build
to the preview's vendored copy, md5 `5708ce5d`). Local media from `https://ccp.assets/` only.
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
| `media-request` | `reqId, station` | Deal media for a sit-down. Reply `media`. |
| `station-request` | `reqId, station, op, idem?, body` | Relay to the server (section 3). Reply `station-result`. |
| `fx` | `token, fxId, station, symbols?` | Fire an effect (section 4). Reply `fx-ack`. |
| `melt` | `station, left` | Current melted spins left, sent whenever it changes. |

### 2.2 Host -> page

| type | Fields | Meaning |
|---|---|---|
| `init` | `protocol, sp, reduced, motion, intensity, lang, lex, stations[], open` | Boot state. `open` = server door flag. |
| `balance` | `sp, why:'server'\|'earn'\|'sync'` | Authoritative SP changed outside a station result. |
| `media` | `reqId, gifs[4], words[4], seed` | Sit-down media (section 5). |
| `station-result` | `reqId, ok, status, reason?, body` | Server answer, or a host refusal (`offline`, `closed`, `bad_op`). |
| `fx-ack` | `token, fired[], skipped[]` | What actually played. `skipped` entries: `{prim, why:'toggle'\|'motion'\|'calm'\|'busy'\|'unknown'}`. |
| `settings` | `motion, intensity, reduced` | A setting changed while open. |
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
  // ["wheel"] = new[] { ("GET","state"), ("POST","spin") },   // later stations append a row
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
  "floorMs": 800 }
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

- `count` 1..20 paid spins (page default 10, lower if `sp < 10`). With `freeze`, `count` must be 1.
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
- **Rate floor:** `nextBuyAt = now + outcomes.length * SLOT_FLOOR_MS` (800 ms default, env). A plain
  tape before `nextBuyAt` -> `{ok:false, reason:'too_fast', retryInMs}`; the page waits it out silently.
  A freeze spin checks a 700 ms floor against the last buy. Plus the per-account 60/min limiter.
- **Cap (decided):** the SP cap rises from 9,999 to 99,999 for everyone. Server `SKILL_POINTS_CAP` and
  every client clamp (AppSettings, ProfileSyncService merge, anti-cheat) move together in one change,
  ahead of the slot. At 99,999 winnings above the cap are lost and `capped:true` is returned.
- **Sync safety:** `netSp` (won minus spent, lifetime) is added to the SkillPointBackfill sum exactly like
  the old `casino_sparkle_spent`, so a later profile sync never refunds a tape or erases a win. The
  client never sends SP to these routes.
- **Paytable is server code** (`backroom-slot.js` `TABLE_V3`), versioned. The client has no fallback
  table: if `state` fails, the cabinet shows "closed for a moment" and Back.

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
| `spiral-full s` | `OverlayService.ShowOverlayTimed("spiral", ms, opacity)` | `Off` -> static frame |
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
- Symbol id -> media: `gif0..gif3` = `gifs[0..3]`, `sub0..sub3` = `words[0..3]`. Dealt once per sit-down
  and kept until the player stands up, so a reel cell never changes face mid-tape.
- URLs point only at `ccp.assets` (the user's folders, mapped read-only) or `ccp.game`. Keys, never
  paths or URLs, go back to the host in `fx.symbols`. Nothing in `media` is ever sent to the server.

## 6. Slot station and the glb contract

Assets copied (never edited) from `blender-scripting/slot/out/` into `stations/slot/assets/`:
`slot.glb` (627,560 bytes), `emi-faces-slot.png`, `emi-face-map.json`. Room art from `slot/refs/`:
`backroom_final.png` into `backroom/room/`.

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
[ { "id": "slot",      "spot": "bottom-left",  "state": "live", "entry": "stations/slot/station.js",
    "hotspot": [x, y, w, h], "stand": [x, y], "labelKey": "br_station_slot" },
  { "id": "wheel",     "spot": "top-left",     "state": "soon", "labelKey": "br_station_wheel" },
  { "id": "scratcher", "spot": "bottom-right", "state": "soon", "labelKey": "br_station_scratcher" },
  { "id": "cards",     "spot": "top-right",    "state": "soon", "labelKey": "br_station_cards" },
  { "id": "counter",   "spot": "top-middle",   "state": "soon", "labelKey": "br_station_counter" } ]
```

Coordinates are pixels of `backroom_final.png`; the room scales them. `soon` stations get a hotspot, a
walk, and a dust-sheet card, and never load code.

Station module shape (the room calls nothing else):

```js
export async function mount(ctx) {
  // ctx = { root, bridge, request(op, body, idem?), fx(fxId, symbols?), media(), sp(), onSp(fn),
  //         reduced, motion, intensity, lex(key, fallback), standUp() }
  return {
    open(),                 // take the screen; resolve when interactive (Back is live before this)
    close(),                // Promise, settles within 420 ms, flushes its own cursor
    suspend(on),            // stop audio/fx
    destroy(),              // free WebGL context, textures, listeners
  };
}
```

- A station owns ONE WebGL canvas, created in `open` and disposed in `close`, so at most one context is
  alive. Its glb, textures and node contract live in its own folder with its own `station.md`.
- It talks to the server only through `ctx.request`, which becomes `station-request` with its id.
- It fires only global fx ids. A new effect is a new row in section 4 plus a C# recipe; unknown ids are
  skipped with `why:'unknown'`, so a page can ship ahead of the host.
- Server side, a station adds `proxy/backroom-<id>.js` (pure) and routes under `/v2/backroom/<id>/`,
  its state under `user.backroom.<id>`, and reuses the shared helpers: auth, door flag, locks, receipts,
  floor, cap, `netSp`.
- Adding a station touches: `stations.json` (one row), the host `Ops` table (one row), the fx table if
  it needs new effects, and its own folders. The room, bridge and window never change.

## 8. Feel hooks (for F1, cited from THE HOUSE BOOK)

- Law VIII: lever, freeze and Back answer within 100 ms, before any network reply.
- THE THUD per reel stop (340 ms, `cubic-bezier(.2,1.5,.4,1)`), left to right.
- THE BANK: tokens fly `payout_spawn` -> SP readout; the readout ticks per landing, never before.
- THE CHIME LADDER on wins, one family; drops an octave while `meltLeft > 0` (Brake 5).
- THE MASCOT GLANCE: EMI face within 100 ms, never the same pose twice in a row.
- THE BREATH: exactly one element (the lever handle at rest), paused during celebrations.
- Brake 3: repetition shrinks the party. Brake 6: `none` gets a shiver and a muted thud, never silence.
- Law VI: reduced motion takes the settled STATE; a tape still plays one outcome per press.

## 9. Owner decisions (2026-09-13) and requests

1. **Freeze:** ONE column per spin (`freeze.col`), 2 SP. The page lights only one freeze button at a
   time; picking another moves the hold. (The preview's multi-freeze is not carried over.)
2. **Cap:** raised to 99,999 everywhere (section 3.4). Clip and flag above it.
3. **Soft launch:** door flag `BACKROOM_OPEN` + `BACKROOM_TESTERS`.
4. **Tape size:** default 10 spins, max 20.
5. **Intensity:** `Calm | Normal | Full`, default `Normal`.
6. **Model requests:** none. Every node the page drives is present. Station hotspot rects are measured
   on the room art by C1, not a model change.
