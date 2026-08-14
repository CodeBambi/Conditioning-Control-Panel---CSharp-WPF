# DTRH — "Down the Rabbit Hole" Primer

> **Purpose.** One-load orientation for working on DTRH — for coding sessions (this file
> auto-loads when you touch files under `Resources/web/dtrh/`) and for brainstorming
> sessions (@-mention this file). Read §1–2 to design features in-world; §3–6 to implement them.
>
> **Freshness.** §1–4 (fiction/mechanics/architecture) track the code and rarely rot.
> §7 (backlog) is a **snapshot as of 2026-07-20** — always confirm branch/ship status with
> `git log`/`git branch` before acting on it. Keep this file updated as DTRH evolves.

---

## 0. What DTRH is, in one paragraph

DTRH is a three.js **web game** (under `Resources/web/dtrh/`) hosted inside the C# WPF app
via WebView2, talking to C# over a JSON message bridge. It's an Alice-in-Wonderland-skinned
hypno/conditioning **roguelite**: you fall down a 3D tube ("the Hole"), pop bubbles for
currency, draft boons, and surface richer — then spend in a hub ("the Warren"). It's the
browser port/successor of the older WPF `ChaosModeService` game; the C# path launches DTRH by
default (`ChaosWebGameEnabled`) and falls back to the legacy WPF game only on a WebGL boot error.

**Layer cake:** `boot/bridge` (page lifecycle + C# protocol) → `engine/` (reusable, persona-
agnostic 3D "Fall" engine) → `game/` (DTRH brain: Warren, runs, VN, economy — handed to the
engine as `game`). C# side: `ChaosWebViewHost` (WebView2 wrapper) + `DtrhHostService` (protocol).

---

## 1. THE FICTION (design layer)

**Premise.** *Descent-as-work.* A silent Alice-figure repeatedly falls down the Hole to "work"
— popping soap-bubble "treats" — and brings the proceeds up to spend. In-world thesis:
**"Deeper is simply better. The deeper you go, the better it pays."**
(`assets/vn/cheshire_script.js`). It is a **no-lose** design: detonations cost streak/heat, never
game-over.

**Setting.**
- **The Warren** — the hub. Reworked into an *in-ambient 3D menu*: the idling tube itself is the
  menu, floating stations hung in the bore. Fills up physically as you unlock panes. (`game/warren.js`)
- **The Hole / tube** — the shaft; clicking it starts a **descent** (a run/"fall").
- **The Four Chambers** — a fixed I→IV descent that always completes, each ending in a boon
  "Landing." Maps a hypnotic-deepening curve onto an emotional arc (`game/regions.js`):
  I Curiosity & Denial · II Fear & Confusion · III Bargain & Struggle · IV Surrender & Acceptance.

**Characters.**
- **The Cheshire** — the **active narrator/tutor/merchant**. Purring, playful-with-teeth. Pet
  names (kitten/hon/sweet thing). Owns onboarding + reactive in-run commentary.
  (`assets/vn/cheshire_script.js`, `game/cheshireVn.js`, `game/cheshireGuide.js`)
- **The White Rabbit** — the "darter" bubble you chase. **The Queen** — foreshadowed exactly once
  (lore guardrail). **Circe / Bambi / Sissy** — persona *mods* supplying VN portrait/tint/VO, not
  Wonderland characters. **The Madam** (old WPF "Story mode" narrator) — **not ported** (disabled placeholder).

**Tone.** Coaxing, complicit, faux-innocent seduction wrapped around a deepening-trance arc.

---

## 2. THE PLAYER LOOP + VOCABULARY (design layer)

**Run-brain state machine** (`game/chaosRun.js`):
`boot → warren → requesting → countdown → running → drafting → recap → (warren)`

1. **Warren hub.** Land in the 3D tube-menu. Chrome: currency chips (emotes ✦, gold 🪙),
   corner dock (options / how-to / wake up). Stations: **FALL IN** (portal: pick difficulty +
   length), **Toybox** (spend emotes to level gear/habits/pockets), **The Dials** (spend gold to
   unlock the options-console ladder), **Vanity** (mirror: mantra, stats, rank, diary),
   **The Boudoir** (crafting + the Loom).
2. **Descend.** Click the Hole → C# persists setup, replies a per-run config. **No pre-run
   loadout** — trained **habits** arrive as config knobs; toys/charms are discovered mid-fall.
   Then a 3·2·1·GO countdown.
3. **Region/biome cycle (the fall).** Four chambers, always I→IV. Each: arrival banner, an
   intensity band that *breathes* (calm → local peak), its own sky (weather), spawn profile,
   style. Each chamber rolls one of ~4 **biomes** so no two descents look/play alike; the biome
   owns look + adds a mechanic. Between chambers the tube can present **junctions** (branching
   forks; pick a doorway by clicking its prize card). Moment-to-moment: pop pink **treats** (tap),
   **hold** glowing **live** bubbles until they give (letting one finish detonates a real native
   payload), grab **gold**, chase the **rabbit**, manage **Focus** (spend on ripple/defuse), build
   **combo/streak/heat** for the multiplier.
4. **Boons / the Landing.** Each chamber climax deals a **boon draft**: pick a **mantra** (buff),
   accept a **sin/curse** (risk+reward), or **resist** (+1 resistance). Reroll = "Taking Chances."
5. **Ending — "The Surfacing."** Over the last ~40s the world diegetically melts/drains to full
   white; recap surfaces as the white lifts (`chaosRun.js tickSurfacing`).
6. **Recap → wake up.** Payout (`payout-result`) banks emotes; score = how deep you went. Back to Warren.

**Run lengths / modes.** Presets **240 / 720 / 960 / 1200s** (960 default). Unlocks:
**The Hourglass** (`custom_duration`, 2min–2h slider) · **The Bottomless Fall** (`endless_mode`,
no clock; regions loop I→IV and *deepen* each lap; ends only on hold-ESC).

**Ranks** (descents finished): Curious → Tempted → Slipping → Entranced → Devoted → Claimed.

### Vocabulary (the nouns to keep proposals in-world)
| Term | Meaning · file |
|---|---|
| **Warren / Boudoir / Toybox / Dials / Vanity** | Hub + its stations · `warren.js`, `catalog.js` |
| **Descent / run / fall** | One dive · `chaosRun.js` |
| **Four Chambers / regions / Acts I–IV** | Fixed I→IV descent · `regions.js` |
| **Biome / chamber / mechanic** | ~4 place-flavors per room; each names one `mech` · `biomes.js`, `biomeMech.js` |
| **Boon / mantra / sin (curse)** | Drafted card; buff vs risk+reward · `boons.js` |
| **Draft / Landing / reroll** | Pick-a-boon table at chamber climax · `overlays.js`, `boonPick.js` |
| **Duo/trio (synergy) card** | Boon that only appears when its partner equipment is owned |
| **Junction / fork / antechamber / vein / doorway / prize card** | Branching room; choose a door · `junctions.js` |
| **The Loom / Spiral Maker** | Spiral-GIF editor; saves to CCP library · `loomStudio.js`, `shared/loomField.js` |
| **Emotes (✦) / gold (🪙)** | Soft currency (levels, Toybox) / hard currency (unlocks, Dials) |
| **Habits** | Trained Warren upgrades arriving as run-config knobs (the only "loadout") |
| **Toys / charms / consumables / pockets** | Discovered & grabbed *in the fall*; docked into pocket slots |
| **Crafting / materials / recipes / Paperwall** | Ingredients shed by pops → 3×3 pictogram recipes in Boudoir · `crafting.js` |
| **Bubble menagerie** | live · tease · echo · chaperone · bound · darter · golden · heavy · prism · heart/brittle · `variants.js` |
| **snap / ripple / freeze / focus / combo·streak·heat / resistance** | The verbs + economy knobs · `chaosRun.js` |
| **Weather** | Per-chamber sky w/ pay/heat/fuse/gold scalars · `weather.js` |
| **The Surfacing / Hourglass / Bottomless Fall / Deepening** | White-out ending / custom length / endless / endless filler cards |

---

## 3. RUNTIME ARCHITECTURE (implementation layer)

### Boot sequence
`index.html` (import map + `boot.js`) → register bridge handlers + eager-`import('engine/scene.js')`
→ `bridge.announceReady()` (`{type:'ready'}`; host flushes queued msgs) → **`init`** arrives
(runSetup, modId, modContent) → **`manifest`** arrives (media) → `maybeStart()` (needs both):
build `game = createChaosGame(...)` (`chaosRun.js`), then `engine = await scene.start({...game})`.
`scene.start` builds renderer/tunnel/fog/spawner/etc. then **`game.attach(engineSurface)`** — page
boots into the **Warren** over an idling tunnel, *not* a run. Per-descent runs are dealt on demand:
`request-run` → `run-config` → GO → `run-started` → `run-ended` → `payout-result`.

**Safety rails:** progress-aware **45s boot deadline** → `boot-error` (C# downgrades to classic
game); ~2s **heartbeat** rAF feeds the host wedge-watchdog; hold-ESC 1.2s exits.

### The bridge (`bridge.js`, Protocol v1)
`{type, ...}` JSON both ways. Handlers = a Map (one per type) with in-order pre-buffer replay.

**Host → Page:** `init` · `manifest` `{images,videos,skipped,truncated}` · `favorites` (top asset
names → Mirror biomes) · `meta` `{state,rev}` (persistent economy snapshot; re-renders Warren) ·
`run-config` · `payout-result` · `payload-state` (native video covering page → hold the run) ·
`loom-list` · `loom-result` · `fullscreen` · `end-run` · `ping`.

**Page → Host:** `ready` · `log` · `boot-error` · `heartbeat`/`pong` · `request-run` ·
`run-started`/`run-ended` (carries `sessionStats`) · **`meta-command`** (the omnibus persistent-
state writer — 27 call sites; `op: set-num / add-to-set / map-set / purchase-* / craft / …`) ·
`asset-stats` · `fire-payload`/`powerup`/`bark`/`sfx` (native effects/voice) ·
`loom-save`/`loom-delete`/`loom-reveal` · `mute-state` · `freeze-state`/`haptic-state`/`vn-speaking`
· in-world telemetry pings (`booncard`/`hubstation`/`poster`/`veinmouth`/…) · `fullscreen-set` ·
`report-bug` · `exit`/`exit-done`.

### Module map — `engine/` (persona-agnostic 3D "Fall")
- **`scene.js`** — orchestrator: renderer/scene/camera, adaptive-DPR governor, drone+musicbox
  audio bed w/ ducking, builds every subsystem, frame loop, pointer routing, `game.attach`, dispose.
- **`tunnel.js`** — the endless tube: closed-loop CatmullRom "treadmill" spine (integer harmonics →
  seamless), baked `TubeGeometry`. Exports `RADIUS`, `LOOP_DEPTH`, `frameAt/frameAtDepth` (the local
  basis **everything** places content in).
- **`spawner.js`** — recycling ring-buffer of wall **cards** from user media (photos + proximity-
  played videos w/ per-video gain nodes), subliminal word cards, held-card paddle; owns `assetTracker`.
- **`fallNav.js`** — velocity-model camera rail (self-advancing depth, comfort-trim, vein-dive curve, intro plunge).
- **`director.js`** — speed/boost adapter; DTRH intensity sourced from `game.moodIntensity()`.
- **`fx.js`** — tunnel mood engine: ribbons/sparkles/lightning + crossfading **zones** (calm/fog/storm/glimmer/neon).
- **`wallPosters.js`** — Four-Chambers wall dressing (region-scaled density of flat quads).
- **`hubStations.js`** — Warren stations as 3D billboards (dumb presenter; logic in `game/warren.js`).
- **`junctions.js`** — branching tube forks (v6/v7): nested bifurcation geometry + Grand-Boon-room door layouts.
- **`bubbles.js`** — the older/simpler "Sissy Fall" bubble-pop field (the richer surface is `game/chaosField.js`).
- **`boonPick.js`** — boon draft staged as 3D cards in the parked tube (presenter; `onPick/onSkip/onReroll`).
- **`powerupDrops.js`** — one grab-in-tube power-up card every ~60–90s (raycast grab).
- **`driftChain.js`** — continuous hypnotic voiceover: chains `FALL_DRIFT` pools + "good girl" resolve.
- **`panel.js`** — the ⚙ DOM panel (live sliders → `settings.js`). **`settings.js`** — persisted live-tunable `S`.
- **`audioBus.js`** — one shared `AudioContext` + biome-color filter. **`audioLevels.js`** — per-group volumes + VO voice-set.
- **`sessionMetrics.js`** → `run-ended.sessionStats`. **`assetTracker.js`** → `asset-stats` deltas (favorites feedback).
- **`loomSpirals.js`** — shared spiral picker (host `loom-list` ~50/50 w/ bundled).
- **`gifWorker.js` / `loomWorker.js`** — off-thread gif **decode** (spawner) / **encode** (Loom).

### Module map — `shared/`
`capability.js` (pre-3D WebGL/tier gate) · `quality.js` (the one `Q` knob; mobile cuts DPR/bloom/
segments) · `assets.js` (THREE texture cache — built-in art only; user media bypasses it) ·
`audioMute.js` (master mute + VN duck; force-mutes stray `<video>/<audio>`) · `fog.js` (GPU
noise-advected points cloud) · `loomField.js` (schema-v2 WebGL spiral field) · `loomSpiral.js`
(schema-v1 2D fallback).

### Module map — top level
`boot.js` (entry) · `bridge.js` (protocol) · `hostMedia.js` (`createHostMediaSource`: URL-only
media pool, shuffled non-repeat deck) · `modContent.js` (active creator-mod DTRH pack).

### Rendering / quality
`capability.js` decides 3D-vs-2D + desktop-vs-mobile **before** any three.js work. `scene.start`
calls `setQuality(tier)` first (resolves the shared `Q` block). On top, `scene.js` runs a live
**adaptive-DPR governor** to hold framerate.

### How media flows
C# `DtrhAssetManifest` enumerates the user's active preset → `manifest` of `https://ccp.assets/…`
URLs (+ `favorites`). `hostMedia.js` ingests (URL-only, no blobs; Chromium cache does the lazy work).
Consumers pull via `draw()/drawKind()/favorite()`: `spawner.js` (wall cards), `wallPosters.js`,
Mirror biomes. **Those four doors are LOCAL-ONLY and must stay that way.** The manifest can also
carry REMOTE entries (absolute scrolller CDN urls, name-prefixed `online<pct>:`) when the user has
moved `AppSettings.MediaSource` off `"local"`; that CDN sends no CORS headers, so `fetch` rejects
and any WebGL/canvas upload throws — which is every consumer above. `hostMedia.js` keeps them in a
second pool reachable only via `drawDom()`, whose sole consumer is `game/payloadFx.js` (the DOM
payload layer: braindrain wash, flash burst, gif cascade, the 15s video card). The C# side caches
the remote pool to `dtrh_remote_media.json` because `DtrhAssetManifest.Build()` is synchronous and
the manifest is posted once — so a cold cache means remote media appears one launch later.
No user media → tube is nearly blank (degrades gracefully). Native mandatory videos
are *not* page media — host lays them over the page + `payload-state` holds the run. `assetTracker`
drains per-asset attention home → C# biases next session's `favorites`.

---

## 4. C# HOST (implementation layer)

All paths under `.../ConditioningControlPanel/`. Stores write to `App.UserDataPath`
= `%APPDATA%/ConditioningControlPanel/`.

- **`Chaos/ChaosWebViewHost.cs`** — the WebView2 wrapper (window + WebView2, per-instance user-data
  folder, virtual-host mappings, hardened settings, queue-until-ready bridge). Configured via
  `ChaosWebViewHost.Options`.
- **`Services/Chaos/DtrhHostService.cs`** — static coordinator; owns Protocol v1 (`OnPageMessage`
  switch). Launch via `DtrhHostService.Launch(testMode)`. Dev args in `App.xaml.cs`: `--dtrh`,
  `--dtrh-m2test`, `--dtrh-spike`. **Note: static class, not an `App.*` service instance.**
- Recovery: heartbeat watchdog (silence >10s mid-run / >20s hub → recover), `ProcessFailed`
  relaunch-once ladder, forced teardown 1200ms after graceful `end-run`.

**Virtual host mappings** (WebView2 `SetVirtualHostNameToFolderMapping`):
| Origin | Folder | CORS |
|---|---|---|
| `ccp.game` | `{BaseDirectory}/Resources/web` (the game files) | Deny |
| `ccp.assets` | `App.EffectiveAssetsPath` (user preset media) | Allow |
| `ccp.art` | `{BaseDirectory}/assets/Chaos` (bundled sprites/icons/banners) — **local host, NOT a CDN** | Allow |
| `ccp.spirals` | `DtrhLoomStore.SpiralsFolder` (`%APPDATA%/.../Spirals`) | Allow |
| `ccp.mod` | active mod's `resources/dtrh` (if present) | Allow |
| `ccp.content` | `%LOCALAPPDATA%/.../content/Resources/web` — downloaded content packs, a mirror of the `ccp.game` tree | Allow |

**Audio hosts (content packs).** The heavy audio (barks, drone, bubble sfx, vn vo) no longer ships
in the installer: it downloads into `ccp.content`, which mirrors `ccp.game` path-for-path. Runtime
audio URLs therefore go through `shared/audioSrc.js` — `audioUrl(url)` picks the host to try first
(the C# host injects `window.CCP_CONTENT_READY` before any page script runs) and `altAudioUrl` /
`altSrcFor` give each load exactly ONE retry on the other host before the engine's existing silent
degradation takes over. **Manifests never go through it** (`assets/barks/manifest.js` is an
import-time ES module and stays in the installer, as do `bubbles/manifest.js` and `vn/manifest.json`).

**Per-Dtrh service:**
- **`DtrhMetaBridge.cs`** — the page's window onto `ChaosMetaState`. `chaos_meta.json` is
  **C#-owned**; page holds a rev-numbered read snapshot + sends ~25 validated COMMANDS
  (purchase-upgrade/-dial, set-lifetime-boon, craft/consume, material-add, first-time,
  lesson-progress, set-flag, reset-onboarding…). Validation = integrity not anti-cheat.
- **`DtrhAssetManifest.cs`** — builds the `manifest` (honors deselection, format/size caps, 5000 downsample).
- **`DtrhModContent.cs`** — optional per-mod `resources/dtrh` (drift voice, portrait, drone, tint). Never throws.
- **`DtrhSpike.cs`** — throwaway `--dtrh-spike` pipeline-proof harness.

**Stores (all additive telemetry/artifacts; `chaos_meta.json` is the real save):**
| Store | File | Persists |
|---|---|---|
| `DtrhAssetStatsStore` | `dtrh_asset_stats.json` | cumulative per-asset engagement → `favorites` |
| `DtrhSessionStatsStore` | `dtrh_session_stats.json` | lifetime per-run totals + rolling 25-run history (future recap card) |
| `DtrhLoomStore` | `%APPDATA%/.../Spirals/loom_<slug>.gif` (+ `.json`) | player-made spiral GIFs (slug whitelist, 12-file cap, magic-validated) |

**Haptics — `Services/Haptics/DtrhHapticDirector.cs`** — two-layer envelope (NOT 1:1 event→buzz;
Buttplug has ~1.3s latency): **AMBIENT** "depth gauge" floor from throttled `haptic-state`
(long 30s Constant refreshed rarely) + **ACCENTS** (short spikes tapped from the bark stream, 3
tiers, tier-1 micro-events coalesced). Gated on `HapticSettings.Enabled` + `DtrhEnabled`. Lifecycle
taps come from `DtrhHostService` (`OnLaunch/OnRunStarted/OnRunEnded/OnWorldFreeze/OnVideoCovering`).

**Integration points:** launched from the Lab card (`MainWindow.Lab.cs BtnStartChaos_Click`) when
`ChaosWebGameEnabled` + boot-capable, else legacy `App.Chaos`. Gated on the setting + boot-capability,
**not directly on Patreon** in this path; unlocks (`custom_duration`/`endless_mode`/`extreme_tier`)
re-checked C#-side so a stale page can't bypass. Subscribes to `VideoService` (pause/duck/focus/watch-
sec). `RouteBark` maps ~40 events → `App.Bark.NotifyChaos*` (voice stays native). Payout →
`App.Progression.AddXP(..., XPSource.Chaos)` + achievements + skill-tree mult + `RevealService.Sync`.
`ModService.ActivateMod` closes an active host so the next launch picks up new mod mappings.
**`LoomHostService.cs`** — a stripped sibling that opens THE LOOM standalone (Spiral Overlay card).

---

## 5. WHERE TO ADD CONTENT (implementation cheat-sheet)

| Add a… | Edit | Shape / notes |
|---|---|---|
| **Boon / sin** | `game/boons.js` `BOONS[]` | `{id,name,rarity,curse,mult,requiresAny/All?,needsVideo?,desc,flavor,apply(s),applyShielded?}`. Art = `ccp.art/boons/<id>.png`. **Also add id to C# `ChaosBoonPool`.** Endless filler = `DEEPEN_CARDS`. |
| **Grabbable passive** | `game/boonPassives.js` `PASSIVE_APPLY` + catalog def | Mirror `ChaosLifetimeBoons.cs`; numbers from catalog `levelValues`. |
| **Lifetime item def** (toy/accessory/charm) | `game/catalog.js` `LIFETIME_BOONS[]` | `{id,cat,rankFloor,name,glyph,unlockCost,upgradeCosts[],levelValues[],capstone,activeUse?,webOnly?}`. Display+gating only. |
| **Recipe** | `game/crafting.js` `RECIPES[]` | `{id,name,glyph,grid:'row/row/row',resultKind,desc,flavor,effect}`. Grid chars from `MATERIALS`. **Add id to C# `ChaosCraftingIds.cs`**; permanents also need `craftedEffects.js`. Must stay collision-free under crop+mirror. |
| **Material** | `game/crafting.js` `MATERIALS[]` | `{id,ch,name,glyph,weight,tint}` (`ch` = recipe-grid alphabet). |
| **Bubble variant** | `game/variants.js` `VARIANTS[]` | size band + motion + weight + minIntensity + payload; mirror C#. |
| **Biome** | `game/biomes.js` (+ mechanic closure in `game/biomeMech.js`) | Mechanics compose existing verbs; **must restore anything they bend on exit.** |
| **Loom preset** | `game/loomStudio.js` `PRESETS[]` | `[name, paramPatch]` over schema-v2 params. |

`catalog.js` also owns (ported from C#): **RANKS**, **UPGRADES** (gold "habits"), the gold shop,
lessons, first-times, diary/codex verb sheet, how-to cards. `metaView(meta)` wraps a `chaos_meta`
snapshot in the hub query surface.

**Cross-system wiring to respect:** everything writes to one run-state `st` in `chaosRun.js` (boons,
passives, crafted permanents, biome mechanics all mutate it; `chaosField.js`/`fieldFx.js` read it per
frame). Duo/trio boons gate on owned equipment ids; `duoPartnerScore` baits the missing partner into
drops/doorways. Junction `'draft'` mode *is* the boon draft rendered as a room (shares `boonPick.js`
primitives). Player-woven Loom spirals join the overlay pool via `loomSpirals.pickSpiralUrl()`.

---

## 6. NARRATIVE SYSTEMS (implementation layer)

Two eras coexist; the **Cheshire layer supersedes** the old one (`CHESHIRE_DISABLED = false`).
- **Cheshire FTUE arc** — `cheshireGuide.js` (brain) + `cheshireVn.js` (mouth), tracked by one
  persisted int `meta.tutorialStage` (0..6, ARC_DONE=6 terminal). Three surfaces: `scene()`
  fullscreen between-run, `say()` non-blocking corner, `overlay()` rare full-stop. Script in
  `assets/vn/cheshire_script.js`. Reactive: `bark(event)` tees through the guide; a claimed event
  plays a Cheshire line and the native bark stands down (no double voice). Veterans get stamped
  ARC_DONE on first read so they never see the tutorial.
- **`happyPath.js`** — the *mechanical* rig for scripted first descents (run 1 = treats-only, no
  drafts/darters; run 2 debuts braindrain + lucky bubble). Cheshire owns the words, happyPath the spawns.
- **`lessons.js` / `lessonCard.js`** — one-card freeze the first time you meet a mechanic;
  suppressed during the tutorial arc but discoveries still recorded (`covers:`).
- **`hubGuide.js` / `vnPortrait.js`** — legacy fallback, only built when `CHESHIRE_DISABLED`.
- All three channels (VN/VO, lesson cards, native barks) share **one narration cooldown** owned by chaosRun.

---

## 7. BACKLOG & OPEN THREADS — snapshot 2026-07-20 (VERIFY with git before acting)

> This section rots. It's distilled from the auto-memory notes on 2026-07-20. Confirm current
> branch/ship status with `git log --oneline` / `git branch` before starting or proposing work.
> Fuller detail lives in the `dtrh-*` memory files (see `memory/MEMORY.md`).

### In-flight / unshipped (don't collide with these)
- **`feat/dtrh-v2-pass-part-1`** (local, NOT pushed): Cheshire VN tutorial (VO 173/173, committed
  281f2c51); narration de-storm (shared 20s gate); 3 new duo boons Riptide/Racing Thoughts/Short
  Circuit (**card art missing — nano-banana TODO**); Circe biome VO 576/576 rendered but toggle
  HIDDEN (`CIRCE_VO_READY=false`); full-pockets→recharge; Emotes hero HUD + gold ticker;
  unpickable-drop-during-draft fix; VN cutaway polish; droplet/voice/video/ripple bugfix batch.
- **`feat/creator-mod-pipeline`** (UNCOMMITTED): junction vein biome-dress (16 shader dresses);
  Grand Boon Rooms (every 10th room, 4–5 doors, junctions v7).
- **`feat/dtrh-mv-director`** (2 commits unpushed): MV director mode + VEIN_PLUNGE/dome-retint;
  needs keeper scene picks, real `song.mp3`, cue re-time.
- **`feat/wpf-compositor`**: "The Surfacing" ending (committed 6c9ae413, unpushed).
- Biome-identity pass Phases 1+2 implemented but UNCOMMITTED on main.

### Merged but play-test PENDING
PR #72 dollhouse arc (a0d12901) · Crafting Parts 1–3 (1de9c74c, pushed; **28 PNGs unapproved,
`musicbox1.mp3` missing**) · worktable QoL + pocket-watch gate (c0bdcfd5) · haptics director
(PR #74) · Journey Rooms + 16 biomes (**576 VO lines need TTS; verdict-crown/contract art placeholder**)
· save slots (9f313a78).

### Shipped on main
Endless mode + custom duration (Hourglass + Bottomless Fall) · crafting system (Loom/recipes/dolls/
Boudoir/Paperwall) · haptics director · Loom web fixes back-port (dithering/centerpiece/shapes) ·
16-biome journey + ACT I–IV rename + biome roulette · dollhouse 3D hub + gold/drops economy (PR #72)
· Four Chambers → biomes · Esc ladder + C#-owned fullscreen · sfx mix routing · 20 boon-card PNGs.

### Design pillars / constraints (durable)
- **VN portrait beats DISABLED for release** (`VN_BEATS_DISABLED=true`) — don't advertise VN in
  patch notes until Circe art + web VO land. (Doesn't affect the live Cheshire path.)
- **Difficulty/pace decoupled:** pay (`difficultyMult`, payout-only) separate from pace (`DIFF_PACE`).
  "Inescapable" ≈ old Gentle pace; Gentle now far calmer. (`CHAOS_DESIGN.md` pace tables are stale.)
- **Narration anti-storm:** single shared 20s gate across VN/VO/lesson cards, drop-not-queue.
- **FTUE:** guided first-20-min (hub welcome/return beats) replaces pre-run loadout/lesson gates;
  in-run lesson cards fire once-ever; fully resettable via `reset-onboarding` bridge op.
- **Currency split:** gold = unlocks, emotes/drops = levels. Emotes is the hero HUD number.
- **Web-only JS changes need a rebuild** to copy into `bin` before they run in-app.

### Gotchas
- A JS module that **throws at import** = silent infinite loader spin (a stray backtick in a
  template literal did it). Diagnose with **headless msedge console**, not `node --check`.
- Loom WATCH ~2× frame-doubling can exceed the `DtrhLoomStore` base64 size gate.
- Save migrations can re-lock existing saves (dials) — bump `SchemaVersion` deliberately.

---

## 8. Build / run / dev entry points
- Build+run the app: `cd ConditioningControlPanel && dotnet build && dotnet run` (Windows-only, .NET 8).
- Launch DTRH directly: run with `--dtrh` (or `--dtrh-m2test` clone-save test mode, `--dtrh-spike` harness).
- The page has **no devtools** (host disables them) — JS logs go over the bridge to `logs/`.
  For import-error diagnosis, open the page in headless msedge to see the console.
