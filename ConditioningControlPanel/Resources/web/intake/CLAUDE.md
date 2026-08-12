# Graded Intake — Primer

> **Load this instead of re-exploring the feature.** It auto-loads on any session touching
> `Resources/web/intake/**`; `@`-mention it for design/brainstorm sessions that don't open code.
> Companion docs: `Resources/web/dtrh/CLAUDE.md` (the web-core pattern this mirrors),
> `BUILD_PLAN.md` in this folder (the round-by-round build journal — **stale past round 15**, see §9).
>
> Layered so each reader takes only what it needs: §1–2 fiction + player loop (design layer),
> §3–5 web-core architecture, §6 C# host, §7 content pipeline, §8 where-to-change-X,
> §9 status/backlog (dated), §10 gotchas, §11 build/run.
>
> Built 2026-07-22 by fanning 5 reader subagents over the ~23k-line web core, the C# host, and the
> asset pipeline. §1–8 track the code and rarely rot; **§9 is a dated snapshot — verify with git.**
> Keep this updated as the feature evolves rather than re-deriving context.

---

## 0. What Graded Intake is, in one paragraph

A fake clinical study. The user is a **Subject** taking a "Cognitive Response Assessment" (Form CRA-7);
in reality it is a banded hypnotic descent that grades them, classifies them into an archetype route,
and drafts a real CCP Session from what it learned. It is a **decoupled web core** (`Resources/web/intake/`,
plain ESM, no build step) hosted in **WebView2** via the shared `ChaosWebViewHost`, exactly like DTRH —
so it is portable to a gated web/phone app later. It is fully self-contained: DOM/canvas/WebAudio own
every effect, with **zero** coupling to `App.Flash` / `App.Bubbles` / the WPF overlay stack. It lives in
**Exclusives** (its own tab), not the Lab. Since the weekly-pass rework it is t1 for *unlimited* runs but
open to free accounts **once a week** — it doubles as the app's onboarding (§6 Gating). Placeholder product name; rename is one constant
(`PRODUCT_NAME`, `core/contracts.js:44`).

---

## 1. THE FICTION (design layer)

- **Framing**: a clinical assessment that degrades into what it always was. Inspired by signal-response's
  itch.io "Cognitive Performance". The paperwork stays straight-faced while the page does not.
- **Bands** (the spine of everything): **Calibration → Establishing → Deepening → Climax → Recovery.**
  In-fiction they are "Sections"; the player never sees the word "band", a depth number, or a band name.
- **HUD hygiene**: the only in-run chrome is a question counter (which lies — see §4) and a gauge that
  becomes a spiral. **No raw depth or band leaks.** Section interstitials carry the tone shift instead.
- **Voice packs** live per bank in `banks/<niche>.json → theme` (`accent`, `subjectNoun`, `sectionTitles`,
  `interviewer`, `praise`, `introLines`, `outroLines`, `countPhrases`):

  | Niche | Accent | Subject noun | Form | Register | Archetype ladder (5 tiers) |
  |---|---|---|---|---|---|
  | **bambi** | `#ff69b4` | Subject | CRA-7 | "good girl", sparkly, melty | curious-listener → gone-bambi |
  | **drone** | `#35e0c8` | Unit | DroneOS 7.4.1 | "good unit", optimal, sync rising | rogue-node → assimilated |
  | **sissy** | `#ff7fae` | Candidate | Intake Form H-2 | "good girl", perfect princess | curious-newcomer → full-sissy |
  | **circe** | `#d0104a` | Property | Form CL-1 | cold keyholder: verdicts, never coos | unclaimed → hers-entirely |

  Circe's register is a hard rule: **no exclamation marks, no diminutives, no em-dashes.**
- **Designer vocabulary** (keep proposals in-world): *assessment · intake · item* (not "question") *·
  response logged · within tolerance · your file · classification · recorded statements · certificate ·
  debrief · retention schedule*.
- **VO is one voice for every niche** (bambi's), deliberately. The arc is crisp clinical professionalism
  at heat 0 curdling to sweetly-predatory intimacy at heat 4–5. Register comes from tags + settings, not
  from casting.

---

## 2. THE PLAYER LOOP + THE FOUR INVARIANTS

**Loop**: menu → briefing (consent card, Subject #NNNN) → ~81 graded beats across four descent bands,
interludes every ~8 → Recovery (4 beats walking depth to exactly 0) → staged outro (grade → route reveal
→ recorded statements → stats → the weave → certificate) → a real Session is drafted and saved.

One `depth` scalar (0..1) drives the entire effect stack through **one** curve, `depthToChannels(depth)`
(`core/contracts.js:436`) → `flashRate, flashOpacity, subDensity, duckDepth, bubbleRate, binauralDepth,
bgIntensity`. Nothing anywhere re-derives an effect strength from depth on its own.

**The invariants** (verbatim block at `core/contracts.js:15-36` — read it before touching steering,
effects, or Recovery):

1. **FRICTION, NOT LOCKOUT.** Every steer must leave the user's chosen answer — *including a refusal* —
   completable with effort. Slow, small, slippery, nagging: yes. Impossible: never. `ctx.forceComplete`
   is the guaranteed hatch and beats.js honours it.
2. **REWARD INTENSITY IS 0..1 AND CLAMPED TO USER CAPS** at the effect layer (`clampToCaps`). Nobody
   hardcodes an absolute strength.
3. **RECOVERY REVERSES DEPTH 1→0 AND IS NON-SKIPPABLE.** A run isn't done until the user has surfaced.
   `STEER_BAND_WEIGHT[Recovery] = 0` — recovery never coerces. Endless is opt-in and still exits via Recovery.
4. These are **UX invariants, NOT `Services/Moderation`** (that layer is content-legality/CCBill only).

Two more standing rules: **stats are strictly local**, user-viewable/exportable/deletable, with no
FOMO or streak-pressure framing; and **the AI never sees a transcript** — `/intake/ai` gets a compact
structured request only.

---

## 3. RUNTIME ARCHITECTURE — boot, bridge, module map

### Boot sequence (`boot.js`, 2282 lines) — order is load-bearing

`index.html` is a bare shell (canvas `#intake-bg`, `#intake-stage`, `#intake-hud`, `#intake-aside`,
`#intake-overlay`, `#intake-loader`) with an importmap pointing `three` at `../dtrh/vendor/three/`.
`boot.js` is the only module entry.

1. Import-time: DOM handles guarded on `typeof document`; `window.onerror`/`unhandledrejection`/
   `intake-log` seams installed; side-effect import of `ui/fullscreen.js` (binds F11 first).
2. `shim.announceReady()` **after** `onBoot` registers — the host flushes its queued `init` on `ready`.
3. `loadBank(niche)` → `themeOf(bank)` → `applyTheme()` (writes `--intake-accent`/`-2` on `<html>`).
4. Stack via `loadOptional(path, factory, fallback)` — reward, stats, beats, effects, audio, background,
   subliminals, colorFlash, steering. A failed import **logs** and falls back to a stub (silent fallbacks
   once hid dead modules for days).
5. `resetPalette()` / `resetSpiralLog()` / `resetMediaLog()` **before** `createEngine` (module singletons).
6. Menu → `buildHud` → briefing → **`installPause()`** (after briefing, before first beat — its timer shim
   only governs timers created after it).
7. Beat loop: interstitial if new band → subliminal bed lifecycle → `setDepthEverywhere` →
   `pause.setBand` → `background.nextBiome()` + route tint → `beats.render(beat)` →
   **`harvestColorPick` before `engine.next(ev)`** → `CARD_SWAP_EXTRA_MS` (1000 ms floor) → `pause.gate()`
   → 3 % giggle roll.
8. Run end: pause disposed → counter torn down → depth 0 + `audio.emerge()` → `stats.record()` →
   `shim.emitResult()` → `runOutro()`.

### The bridge (`web-shim.js`, PROTOCOL 1)

`isHosted = !!window.chrome.webview`. One `message` listener demuxes by `m.type` into a handler Map with
a pre-buffer. **One handler per type** — a second `on('x')` silently displaces the first.

- **Page → host**: `ready` · `log` (400-char clamp) · `boot-error` · `heartbeat` · `pong` ·
  `quiz-result` · `exit` · `intake-close` (jumpscare abort) · `fullscreen-set` · `loom-save` ·
  `intake-save-image` · `need-remote`.
- **Host → page**: `init {config, ai}` · `fullscreen {on}` · `session-drafted {ok,name,path}` ·
  `loom-result` · `intake-save-image-result` · `ping` · `payload-state` · `end-run` *(no page handler)* ·
  `assets-append {images}` · `online-status {ok,error,added}`.
- **Remote media** (`config.remoteMedia`): the `media` manifest is local and one-shot, so remote
  stills arrive later — the page posts `need-remote`, the host replies `assets-append`, and
  web-shim pushes the urls **into the existing `media.images` array** (never a reset), which is
  why `effects.js` holds that array by reference instead of copying it. Remote stills never enter
  `media.gifs` (they do not animate) and are explicitly excluded from the two CORS-hostile paths:
  `wallDecals.js` (WebGL texture upload) and `grid.js`'s frozen-tile canvas.
- Standalone path: `?niche=&endless=&steer=&ai=&token=&m2test=&subject=` + `localStorage['intake.bootConfig']`;
  results to `intake.lastResult` / `intake.resultHistory`.

### Module map

| File | Owns |
|---|---|
| `core/contracts.js` | **THE contract.** Enums, shapes, `depthToChannels`, caps, factory signatures, invariants. Nothing else may redefine a shape that lives here. |
| `core/engine.js` | Band/depth state machine, prompt selection, belief ladder, velocity pacing, steer rolls, route reveal, finalize. |
| `core/reward.js` | The decoupling schedule + `pickKind` + intensity math. |
| `core/stats.js` | Local retention (IDB → localStorage → memory), `feedForward`, aggregates, recap pruning. |
| `core/ai.js` | `POST /intake/ai` proxy + deterministic offline stub. |
| `core/palette.js`, `spiralLog.js`, `mediaLog.js` | Per-run ledgers (harvested colours, spirals, media). **Module singletons — reset per run.** |
| `render/beats.js` (3.9k) | Every mechanic, the card, bubbles, timers, jumpscare, freeze gate, corruption. |
| `render/steering.js` | 19 engine-rolled steers + 3 self-rolled; the escape guard. |
| `render/effects.js` | Garnish rotation, gif burst/rain, ambient drift, see-through cards. |
| `render/background.js` | three.js tube + DTRH biome deck + 2D fallback. |
| `render/audio.js` | Binaural bed, SFX, VO, chimes, error/glitch synth, the event seams. |
| `render/subliminals.js` | The user's own subliminal pool, phase-gated. |
| `render/colorFlash.js`, `optionTint.js`, `wallDecals.js` | Colour harvest flash, option tinting, tube wall media. |
| `ui/menu.js`, `pause.js`, `options.js`, `prefs.js`, `history.js`, `fullscreen.js`, `corruption.js`, `menuMusic.js`, `menuVoice.js`, `kawaii.css` | The kawaii shell: title screen, pause + its timing shim, settings, Records Office, window mode, palette/music corruption ratchet. |

CSS lives in **three tiers**: `styles.css` (skeleton + shell screens), `ui/kawaii.css` (shell kit, mounted
on demand keyed on `link[data-kw-kit="1"]`), and **~8 JS template literals injected once by id**
(`IB_CSS`, `IXEV_CSS`, `IXFZ_CSS`, `IXCR_CSS`, effects' `CSS`, steering's sheet, `SUB_CSS`,
`ix-counter-css`, `ix-outro-css`, history's `IXH_CSS`). **Grepping `styles.css` for a class will miss
almost everything.**

---

## 4. THE DESCENT — engine, reward, steering

### Bands and pacing (`core/engine.js`)

- `BASE_BEATS` = Calibration **21** / Establishing **25** / Deepening **19** / Climax **16**;
  `MIN_BEATS 5`, `MAX_BEATS 34`, `RECOVERY_BEATS 4`, **`EXPECTED_DESCENT 81`**.
- `BAND_DEPTH_FLOOR` = 0.00 / 0.18 / 0.42 / 0.72 / 0.00 (Recovery).
- **Playing fast never shortens the run** (`plannedBeats`): positive velocity returns base; only negative
  velocity stretches. Velocity `v = clamp(-1,1, v*0.7 + dv)`, `dv` +0.18 correct / −0.22 wrong /
  +0.12 under 1.8 s / −0.15 over 5 s or timeout.
- `HEAT_WINDOW` = Calib [0,1] · Estab [1,2] · Deep [2,4] · Climax [3,5].
- Selection order: plain slot → colour harvest → trick → belief ladder (55 % in Estab/Deep/Climax) →
  `eligible('strict' → 'wide' → 'any')` → last-resort `usedIds.clear()`.
- **PLAIN beats** (`PLAIN_SHARE` Deepening .20 / Climax .15) are *scheduled*, evenly spaced from the live
  planned count — not rolled. They are the only unsteered option beats in the deep half.
- Interludes every 8 beats (`'watch'` in Deepening, `'breathe'` in Climax); never graded, never advance
  the counter. ≥1 BubblePop guaranteed per descent band.
- Route reveal: **signed, heat-weighted votes** (`HEAT_VOTE [0.15…2.6]`, refusals vote *against*),
  a `commitment` scalar, and a tier window with both a ceiling and a floor. Susceptibility =
  `peakDepth × (0.35 + 0.65 × scoreRate)`; `peakDepth` itself is untouched because C# maps it to difficulty.

### Reward decoupling (`core/reward.js`)

Mode per band: Calibration **Honest** → Establishing **SpicierPick** → Deepening **ScaleWithScore** →
Climax **VariableRatio** (fire chance 0.30→0.60, `JACKPOT_ROLL 0.85`, `NEAR_MISS_WINDOW 0.08`) → Recovery
Honest with base chance 0. `pickKind` order: Recovery→none · BubblePop→bubble · 18 % gifburst ·
5 % gifrain · heat≥4 drop · heat≥3 praise · depth≥.6 flash · depth≥.3 bubble · else chime. Streak juice
`×(1 + min(streak,8)×0.03)`.

### Steering (`render/steering.js`)

`STEER_BAND_WEIGHT`: 0 / .35 / .65 / 1.0 / 0. 19 engine-rolled installers (Magnet, Flee, Exile, Crowd,
SizeSkew, OpacitySkew, OccludeGif, Defocus, LateBloom, DragReveal, HoldRefuse, ShrinkHit, NestedNag,
OverflowHit, AssistClick, Tunnel, DriftResolve, Decay, MeltAway) plus **three self-rolled** in priority
order — `BottomlessNo` (5 %) → `HoverSwap` (6 %) → `MouseHijack` (40 % after 9 s of dwell, disabled under
reduced motion). Self-rolled steers yield to `REFUSAL_GATE_STEERS` and `POSITION_STEERS`; **the engine's
`STEER_POOL` is untouchable** — self-rolled steers roll themselves.

The **escape guard** is what makes invariant #1 real: `bump()` accumulates attempts/dwell, and at
`ESCAPE_EFFORT 6` / `ESCAPE_MS 5000` it runs every `onFrictionRelease` (re-forming melted options,
disarming covers, standing down hijacks) then `forceComplete`. Every veto handler lets synthetic clicks
through (`!e.isTrusted && clientX===0`) — that's what makes `forceComplete` un-vetoable.

### Set-pieces (mutually exclusive per beat via `gateUsedThisBeat`)

- **DDLC jumpscare** — "are you sure?" with a drifting Yes. 2 / 3.5 / 5 % by band, behind a once-per-run
  `GLITCH_RUN_CHANCE 0.10` eligibility roll. Catching Yes closes the host: **clean abort, no session.**
- **Freeze gate** — 3 % of wrong presses in Deepening/Climax; 20 s of tremble/tint/riser with a
  `sure_<niche>_NN` taunt, then a fake cursor force-picks the correct option.
- **Corrupted question** — 5/10 %: either a 2.6 s RGB-ghost scramble (with chopped VO and a broken-GIF
  canvas) or a 9 s melt. **Options and grading are never touched.**
- **The lying counter** (`boot.js`) — display-only, monotone, never truthful before 90 % through; from
  Deepening it can be knocked off the HUD permanently (25 % per click).
- **Quit-generation egg** (`ui/pause.js`) — the confirming Quit press *queues a build* at 15 s; ticks below
  10 cost 1.16× the last, and at 3 a 1-in-3 roll fails and re-queues at 8–9. Terminates with probability 1.
  Uses **captured native timers** because the pause shim freezes the globals.

---

## 5. BANKS & CONTENT SHAPE

`banks/{bambi,drone,sissy,circe}.json` — **~630 prompts each** (bambi 630 / drone 630 / sissy 629 /
circe 629), each with 5 tiered archetypes and 3 ladders × 4 rungs.

Prompt entry: `id`, `text`, `answer` (index | bool | verbatim string | slider number), `heat` 0-5, `tags[]`,
`flavors[]`, `weight`, `mechanicHints[]`, optional `options[]`, `requires{}`, `affirmsMantra`, `trick`,
`freeChoice`. `requires` supports `minDepth`, `band`, `tagsAny`, `tagsAll`, `affirmed`, `minScoreRate`
(the last two are implemented but unused by every shipped bank).

- Heat distribution per bank: h0 **285** · h1–h2 ~56 each · h3 ~115 · h4 62 · h5 56 · 52 tricks.
- **`shr_h0_*` = 205 shared-baseline prompts with identical ids in all four banks** (only `tags` differ, in
  6 of 205, because tags must vote that bank's own archetypes). One VO clip serves all four niches.
- Niche-specific ids are `<prefix>_h<heat>_<slug>` with prefixes `bmb`/`drn`/`sis`/`crc`.
- **The h5 fact**: every heat-5 prompt carries `requires: {band:"climax"}` (mostly `minDepth 0.8-0.85`).
  Only Climax ever consumes heat 5 — h5 is **max-intensity climax material, not aftercare.** Recovery's
  lines are hardcoded in the engine and never read the bank.

---

## 6. C# HOST

`Services/Quiz/IntakeHostService.cs` — mirrors `DtrhHostService`. Reuses **`ChaosWebViewHost`** (shared
with DTRH, Loom and the Bureau — edits there hit four features).

- **Launch**: `GradedIntakeTabView` (or the Dashboard pass card) → `MainWindow.Lab.cs:BtnStartIntake_Click`
  → gates (`IsActive` → `App.IntakePass.CanStartIntake` → `App.Ai.IsAvailable`) → `Launch()`.
  `Launch(duckMainWindow:)` is opted OUT for a user's first-ever run.
- **Window**: start URL `https://ccp.game/intake/index.html`; vhosts `ccp.game` → `Resources/web` (Deny)
  and `ccp.assets` → `App.EffectiveAssetsPath` (Allow); user data folder `browser_data_intake`;
  `--autoplay-policy=no-user-gesture-required` for the bed.
- **BootConfig** (`OnPageReady`): `niche` (mod-derived: Dronification→drone, SissyHypno→sissy,
  Locked→circe, else bambi; `SafeNiche` silently falls back to bambi if the bank is missing), `caps`
  (all 1.0 today), `endless:false`, `steerValve:1.0`, `priorRun:null` (the page's stats own continuity),
  `micEnabled` (= `MicConsentGiven`), `media` (10 sampled gifs/images as `ccp.assets` URLs + a mod-aware
  `bubbleSprite` **data URI**), `subjectId` (4 digits persisted at `%APPDATA%/…/intake_subject.txt` —
  deliberately not a setting), `subliminals` (≤400 enabled phrases, whisper clips inlined as data URIs
  because the audio dirs are outside both vhosts; **gated on `SubAudioEnabled`**; 512 KB/clip, 6 MB total),
  and `ai {serverBase, authToken}` where the token is the **Patreon** bearer.
- **Watchdogs**: 5 s heartbeat timer, >20 s silence or `ProcessFailed` → `Recover()` — dispose + relaunch
  **once** per app session, always windowed.
- **Run end** (`OnQuizResult`): XP `25 + 50×peakDepth + 5×min(mantras,5)`, hard-capped at 100 →
  `IntakeProfiler` (5 axes: Blankness, Service, Arousal, Presentation, Autonomy-inverted) →
  `QuizSessionGenerator.GenerateSession(QuizRunResult)` → **auto-saved, no dialog** to
  `%APPDATA%/…/CustomSessions` → toast → reply `session-drafted {ok,name,path}`.
  Mantras are seeded **verbatim** into `SubliminalPhrases`, `BouncingTextPhrases` **and** `LockCardPhrases`.
- **Profiler pitfall (documented in the class comment, do not "fix" it back)**: scoring is **binary
  heat-weighted endorsement**, not index-lean, because options are escalation-ordered in only 3 of 4 banks
  (bambi 76 % / drone 87 % / sissy 65 % / **circe 27 %**). A headless "always click the bottom option"
  player scored 1.00 on every axis under the lean formula. `chosenIndex` defaults to **−1**, not 0, and is
  used only as a presence check.
- **Windowing**: fullscreen is C#-owned borderless (`AppSettings.IntakeFullscreen`), never the browser
  Fullscreen API (which eats the first Escape). **Known limit: always fills the PRIMARY monitor**
  (`ApplyWindowMode` hardcodes 0,0 + `PrimaryScreen`; shared with DTRH). Launch plain-minimises MainWindow
  (auto-duck) and restores it from the single `DisposeAll` funnel. MainWindow minimise no longer collapses
  the game: `WM_SHOWWINDOW`/`SW_PARENTCLOSING` is vetoed *before* the hide, with `SW_SHOWNA` repair.
- **Gating**: **`App.IntakePass.State`** (`Services/Progression/IntakePassService.cs`), NOT raw
  `HasPremiumAccess`. Four states — `Premium` (unlimited, unchanged) · `NeedsLogin` (the pass is
  per-account) · `Available` (free, one unspent run this week) · `Spent`. Authority is an **ISO week key**
  (`AppSettings.IntakePassSpentWeek`, Monday 00:00 local), not a 7-day delta, so the weekly beat is the
  same for everyone; a spend stamped in the future is read as a rolled-back clock and keeps the door shut.
  **The pass is spent on COMPLETION only** (`IntakeHostService.OnQuizResult`), never at launch, so a crash
  or an abort costs nothing. Enforced/painted in **four** places — `BtnStartIntake_Click`,
  `RefreshGradedIntakeGate()` (4-state copy swap), `SubBadgeGradedIntake` in
  `RefreshExclusivesSubmenuLocks()`, and the Dashboard pass card. The premium-rail lock still keys off
  `HasPremiumAccess` **on purpose** — the rail is a patron amenity as a whole; the free entry point is the
  Dashboard card. **Pop Quiz sits deliberately outside the gate.** `ChipGradedIntake` navigates rather than
  toggles, so it has no status dot.
- **Punch card**: completing an intake queues *half* a stamp against the drafted session's id
  (`IntakePunchCardService.NotifyIntakeCompleted`); the hole lands only once that session is actually run
  (≥50 % elapsed or a natural finish). Eight holes, first one free. See the service's class remarks —
  notably that the completion prize is deliberately NOT granted client-side.

---

## 7. CONTENT PIPELINE (VO / SFX / music)

`assets/vo/` **1988 mp3 + `vo_manifest.json`** · `assets/sfx/` **29 mp3 (14 ids) + `sfx_manifest.json`** ·
`assets/music/menu-theme.mp3`. All normalized to **−22.4 LUFS**. No csproj edit is ever needed for new
clips — `Resources\web\**\*` is glob-included (with `CLAUDE.md` excluded, which is why this file never ships).

VO ids: `intro_1..3`, `menu_welcome`, **`q_<bankPromptId>`** (spoken text = bank `text` verbatim),
`sure_<niche>_01..20` (freeze-gate taunts). Manifest entries carry `{id, text, v3, settings, file}` where
`v3` is the tagged eleven_v3 string. **A missing manifest entry is silent, never an error** — which is why
clips can land after the playback wiring.

Generators live in `C:\Projects\ccp-trailer` (separate repo, `.env`-gated, never print keys):

- `build_intake_manifest.py` — the **authoring brain**. Walks the banks, emits one directed entry per prompt
  from a per-heat preset table (`BAND_TAGS`, `BAND_SETTINGS` stability .50→.22 / style .22→.74,
  `place_ellipsis` ≤1 and none at h0, non-verbal tags at h2+). A **verbatim guard** asserts the generated
  `v3` still equals the bank `text` — any violation aborts with no write.
- `add_*_intake_vo.py` — append-only feeders that import the above and reuse its preset table.
- `gen_intake_vo.py` (serial, missing-only, `--dry`/`--limit`/`--force`) and `gen_intake_vo_par.py`
  (parallel, multi-lane, `--probe`).
- `gen_intake_sfx.py` — the SPEC list **inside the file** is the source of truth; it *writes*
  `sfx_manifest.json`. API duration floor is 0.5 s (shorter cues are requested at the floor then trimmed;
  normalize **before** trim or LAME asserts).

**Add a VO line**: author the prompt → `build_intake_manifest.py` (or a small `add_*.py`) →
`gen_intake_vo.py --dry --limit 5` → `gen_intake_vo.py` → rebuild or hot-copy. Questions need no wiring
(`'q_'+prompt.id` is automatic); anything else needs an `audio.voice('<id>')` call site.
**Add an SFX cue**: append a SPEC dict → `--dry` → `--ids <newid>` → add the id to
`SFX_MANIFEST_FALLBACK` in `render/audio.js` → fire via `audio.sfx(id)` or the `intake-sfx` event seam.

ElevenLabs lanes: **lane 1 is exhausted for TTS and has no `sound_generation` scope**; everything shipped
so far ran on lane 2; lane 3 is untouched reserve. `eleven_v3` has **no speed parameter** — pace is text
only, and breath tags cost 1–2 s each.

---

## 8. WHERE TO CHANGE X

| Want to… | Edit |
|---|---|
| Rename the product | `core/contracts.js:44` `PRODUCT_NAME` (one line) |
| Retune band lengths | `BASE_BEATS` in `core/engine.js` — **and `EXPECTED_DESCENT` by hand** (§10) |
| Change a reward's odds | `pickKind` / mode table in `core/reward.js` |
| Add a steer | `render/steering.js` `INSTALLERS` + the `Steer` enum in contracts. Self-rolled steers roll themselves and must yield to the refusal/position gates |
| Add a beat mechanic | `Mechanic` enum in contracts + the switch at `render/beats.js:1218` |
| Add questions | `banks/<niche>.json`; then §7 for the VO |
| Add a niche | ship `banks/<niche>.json` (else `SafeNiche` silently serves bambi) + a `Niche` entry + the mod mapping in `IntakeHostService.DesiredNiche()` |
| Add a BootConfig field | `IntakeHostService.OnPageReady` **and** `fromHostInit` in `web-shim.js` |
| Add a page→host message | the switch in `OnPageMessage` + a handler; reply convention `{type:"x-result", ok, …}` |
| Change the gate | **three places**: `BtnStartIntake_Click`, `RefreshGradedIntakeGate()`, the XAML overlay |
| Change the drafted session | `QuizSessionGenerator.GenerateSession(QuizRunResult,…)` (tiers in `BuildSettings`, axis→knob table in `ApplyRunShaping`, copy in `GetFallbackContent`) |
| Change how a run is *read* | `IntakeProfiler` tag sets / `ScoreAxis` |
| Change window behaviour | `ChaosWebViewHost` — **shared with DTRH/Loom/Bureau** |

---

## 9. STATUS & BACKLOG — snapshot 2026-07-22 (VERIFY with git before acting)

- **Everything is committed and pushed.** Branch `fix/web-video-interruptions`, HEAD `4395a01a` (v6.5.0);
  `main` (checked out in the worktree `C:\Projects\ccp-wt-main`) is at the same commit and level with
  `origin/main`. Intake history: `dd57e4b4` (v1) → `e2b89599` (rounds 12-15) → `07246691` → `1b952dc2` →
  `837ed65a` → `935e9b98` → `69086418` → `62218480` → `8e5dcf08` (Lab rebrand) → `87d6e1e4` (moved to
  Exclusives).
- **`BUILD_PLAN.md` is stale past round 15** and its "UNCOMMITTED" status lines are false. It has no record
  of: the Records Office, the menu shell, profiling, band dilution, gif rain, the closing-judgment rework,
  independent minimise / auto-duck / fullscreen / the session-drafted reply, or the move out of the Lab.
  It also predates the **circe** niche entirely, lists ~half the real module tree, and quotes stale bank
  and VO counts. Treat §1/§4 of that doc as history, not as a map.
- **Play-test is the standing open gate** — every round since 7b ends "play-test pending".
- **OPEN OWNER QUESTION (still unresolved)**: is Recovery (lv5) a sanctuary or full-intensity? Today it is
  a sanctuary, which makes OccludeGif's lv5 branch and GifBurst's opacity-1.00 rung unreachable dead code.
- **MouseHijack is LIVE (not dead code).** It gates to Deepening(§3)/Climax(§4) — the owner's "round 3
  onward" with the Recovery sanctuary excluded — for MC4/YesNo/Mono/Destruct beats. As of 2026-07-24 it is
  **timer-triggered, not idle-triggered**: ~2s after the prompt line finishes presenting (steering reads
  `ctx.lineRevealMs` = beats.js `computeOptionsHold()`, the options-hold reveal) it rolls once at ~2%
  (`MOUSEHIJACK_CHANCE`). The old ~9s idle-dwell arm (`MOUSEHIJACK_LINGER_MS`) is **removed** — in normal
  fast play the idle timer never tripped, so the effect essentially never fired. The effect itself is
  unchanged (virtual pink dot, 0.25→0.90 gain ramp, 150ms-dwell auto-commit via `forceComplete`, >2500px
  fight / Escape abort, reduced-motion + coarse-pointer disable). Note: "round 3 onward" *could* be read to
  include Recovery(§5); it is deliberately excluded as the sanctuary band — owner may revisit.
- Deferred / known limits: user-facing niche + endless + steer pickers (host sends fixed values); real
  effect-cap wiring (`caps` ship at 1.0); fullscreen is primary-monitor only; `GifBurst` has no hydra
  (concurrent rolls are skipped, not queued); the classic quiz (`QuizService`/`QuizWindow`) and its Past
  Quizzes list are `Collapsed` "pending removal" — `RefreshPastQuizzes` early-returns while they are hidden.
- No `.mjs` test scripts are checked in; the round-by-round harness tests were ad hoc. The standing gate is
  the **import sweep** (nothing may throw at import).

---

## 10. GOTCHAS (the expensive ones)

1. **WebView2 has no devtools.** Any silently-caught init failure is an invisible feature. Always wire the
   **`intake-log`** seam. This cost a day when the tube was flat-void for weeks.
2. **The tube shader**: three.js prepends `precision highp float;` to *both* stages, so declaring the
   fragment stage `mediump` is a hard **link error** that throws nothing and draws nothing. Both stages are
   `highp`, and `renderer.compile()` is forced at init so the fault surfaces loudly.
3. **`EXPECTED_DESCENT` (81) must equal `sum(BASE_BEATS)` by hand.** It is not computed, and it drives both
   the commitment scalar and `primaryShare` — retune a band without it and grades silently skew.
4. **`rollSteer` has no probability gate.** `PLAIN_SHARE` is the *only* thing producing an unsteered option
   beat in the deep half. Set it to 0 and Deepening/Climax go back to wall-to-wall interference.
5. **Nothing may throw at module import.** A throw is a silent infinite loader. Hence every DOM/`window`
   touch is guarded and `stats.js` touches no storage API at import.
6. **`stage.innerHTML = ''` on every render.** Anything that must outlive a card is parented to `<body>`:
   the pause button, the bubble flight layer, effects' garnish/burst/rain layers, the chain layer, both
   falling clones.
7. **The pause timing shim is global and permanent** — it replaces the timer globals and patches
   `performance.now`/`Date.now`, hands out virtual ids from `0x40000000` (never pass one to a native clear),
   and is deliberately never uninstalled. Anything that must keep moving while paused **must capture the
   natives first** (`pause.js`'s `N.*`, `menuMusic.js`'s module IIFE).
8. **Escape is four rungs deep** (steering hijack disarm → pause opens → Options closes → pause closes),
   held together by a `suppressUntil` handshake. Do not add a fifth meaning. F11 is the only unconditional
   window-mode key.
9. **`is-correct` / `is-answer` must never gain visual styling** — they are DOM hooks. The pink-means-correct
   colour tell was the single biggest early play-test complaint.
10. **Reward determinism has one hole**: `pickKind` uses `Math.random()` while the rest of `reward.js` runs
    on the seeded `hash01` stream.
11. **Duplicated by design, no shared module**: `backdropRef` (beats + effects, ref-counted via
    `body.dataset.ixBackdrop`), `freshSpiralParams` + `window.__ixSpiralSig`, and the three CustomEvent name
    literals (`intake-sfx`, `intake-garnish`, `intake-log`). Effects and steering hold **no** audio handle —
    that is why the seams exist.
12. **`'gifburst'` / `'gifrain'` are string literals**, not `RewardKind` members — a known contract gap.
13. **`quiz-result` is one-way.** The page mirrors C#'s session-naming rule to guess a name
    (`derived:true`), then the later `session-drafted` reply overwrites it. The certificate's exit is held
    shut until that reply lands (or 12 s), because clicking early tore the window down mid-draft and the
    `QuizRunResult` is not retained.
14. **Hot-copy works**: web-only changes can be robocopy'd into
    `bin\Debug\net8.0-windows10.0.19041.0\win-x64\Resources\web\intake\` and a fresh run picks them up with
    no rebuild and no app restart. An incremental build does **not** reliably refresh web assets.
15. **One build at a time** if two sessions share the checkout — a torn build reads exactly like a crash bug.

---

## 11. BUILD / RUN / DEV ENTRY POINTS

```bash
cd ConditioningControlPanel && dotnet build && dotnet run   # then Exclusives → Graded Intake
```

- **Browser (no WPF)**: open/serve the tree and hit `index.html?niche=sissy&m2test` — `?m2test` strips
  pacing, menu, pause and briefing. Prefs land in `localStorage['intake.prefs']`.
- **Headless engine driver**: `harness.html` — engine + ai + contracts only, no render layer. Pick a niche
  and an answer strategy (`best`/`worst`/`chase`/`random`), dumps every `BeatSpec` and the final
  `QuizRunResult`.
- **Import sweep** (the standing gate): every module must import without throwing.
- Ship checklist that has held up: sweep green → `dotnet build` 0 errors → **output verified fresh** →
  relaunch.
