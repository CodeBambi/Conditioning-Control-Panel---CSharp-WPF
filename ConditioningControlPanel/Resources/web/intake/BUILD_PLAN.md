# Build Plan: "Graded Intake" — decoupled web-core quiz rework

> **New session? Start here.** This plan is self-contained. Canonical design rationale is in memory
> `quiz-rework-intake-web-core.md` (loads via MEMORY.md). Do **Phase 0 first** (it's the contract that
> lets everything else run in parallel), then fan out Phase 1 as parallel Opus agents, then Phase 2.
> Driver should be **Opus** (the leaf agents are Opus too — this keeps Fable out of the build loop).

**Working name:** `intake` (folder) / "Graded Intake" (fiction — **PLACEHOLDER, user will rename**).
**Repo:** `C:\Projects\Conditioning-Control-Panel---CSharp-WPF\ConditioningControlPanel` (+ server `CC-Labs-llc/CCP-Server`).
**Design converged 2026-07-20.** Inspired by signal-response's itch.io "Cognitive Performance" fake-study game.

---

## 0. Locked decisions (all confirmed by user)

| Decision | Choice |
|----------|--------|
| Build target | **Decoupled web core**, DtRH-style (`Resources/web/intake/`), hosted in **WebView2** in the Lab; portable to a gated phone/web app later |
| Effect layer | **Fully self-contained in-page** (DOM/canvas/WebAudio) — like DtRH. NO coupling to WPF `App.Flash`/`App.Bubbles`/etc. |
| AI transport | **Server proxy (CCP-Server `POST /intake/ai`)** — key + entitlement gate + moderation server-side. Server work **authorized**. |
| Niches (v1) | **Bambi, Drone, Sissy** — confirmed. Each needs 3–5 sub-archetypes for the route reveal. |
| Preset banks | **AI-bake full banks; user reviews later** — do NOT gate on pre-review; ship complete banks to the user. |
| three.js tube bg | **In v1** (Agent F active) — simplified DtRH tube, phone-safe. |
| Session draft | **In v1** — web core emits `QuizRunResult` JSON → C# `GenerateSession`. |
| Endless mode | Opt-in only; default intake terminates + Recovery band wakes up. |
| Reward decoupling | Clamp intensity to user caps; guaranteed reversible Recovery; session-scoped. Guardrails are UX invariants, **NOT** `Services/Moderation` (that's content-legality/CCBill only). |

**Core loop:** banded descent Calibration → Establishing → Deepening → Climax → Recovery; one `depth` scalar
(0..1) drives the whole effect stack + synth binaural; a reward schedule that decouples reward from correctness
as depth climbs; a reactive `requires`-gated belief-ladder; route-reveal (primary+secondary archetype from
trajectory, not RNG); mixed response mechanics; a coercive-answer "steering" layer; full local retention stats
that feed forward into the next run.

---

## 1. Module layout (file ownership — keeps parallel agents from colliding)

```
Resources/web/intake/
  BUILD_PLAN.md                  (this file)
  index.html                     [Phase 0]  page shell + WebView2/standalone bootstrap
  web-shim.js                    [Phase 0]  DtRH-style bridge swap (Lab bridge vs standalone browser)
  harness.html                   [Phase 0]  headless-ish page to drive the engine without full UI
  core/
    contracts.js                 [Phase 0]  ALL shared types/JSON schemas — the parallelization contract
    ai.js                        [Phase 0]  askAI() interface + server-proxy fetch impl (pairs with Agent H)
    engine.js                    [Agent A]  band/depth state machine, sequencer, route reveal, ladder gating
    reward.js                    [Agent B]  reward schedule + depth→effect-channel map + cap clamp
    stats.js                     [Agent G]  local persistence, longitudinal aggregates, feed-forward
  render/
    beats.js                     [Agent C]  response mechanics: MC4, Y/N, bubble-pop, mantra, check-in, mono, funnel, destruct
    steering.js                  [Agent D]  coercive-UI catalog applied per beat (magnet/flee/exile/occlusion/hitbox/destruct)
    effects.js                   [Agent E]  in-page flashes, subliminals, gif bursts, bubble visuals
    audio.js                     [Agent E]  WebAudio binaural (synth, one depth scalar) + chimes
    background.js                [Agent F]  three.js simplified tube bg (phone-safe, graceful fallback)
  banks/
    bambi.json                   [Content]  AI-baked full bank
    drone.json                   [Content]
    sissy.json                   [Content]
```

C# side (Phase 2, single agent — touches existing files):
```
Windows/IntakeWindow.xaml(.cs)             [Agent I]  WebView2 host in Lab (copy DtRH hosting pattern)
Models/QuizRunResult.cs (new DTO)          [Agent I]  C# mirror of emitted JSON
Services/Quiz/QuizSessionGenerator.cs      [Agent I]  extend GenerateSession to consume QuizRunResult
MainWindow/MainWindow.Lab.cs + LabTabView  [Agent I]  Lab entry-point button
```

Server (Phase 1, parallel — separate repo):
```
CC-Labs-llc/CCP-Server: POST /intake/ai    [Agent H]  entitlement-gated proxy, server-side moderation, OpenRouter
```

---

## 2. Phase 0 — Foundations (BLOCKS everything; the driver does this first)

Deliverable: `contracts.js` + `index.html` + `web-shim.js` + `harness.html` + `core/ai.js` stub, all runnable
(empty engine returns canned beats). Nail this before any fan-out — it's the contract every agent builds against.

**`contracts.js` must define:**
- `Band` enum; `DepthState { depth, velocity, band }`.
- `PromptBank` schema: `{ niche, archetypes[], prompts[], ladders[] }` where
  `prompt = { id, text, answer, heat(0..5), tags[], flavors[], requires?, weight, mechanicHints[] }`.
- `Route { primary, primaryArchetypeId, secondaryArchetypeId?, primaryShare, secondaryShare }`.
- `BeatSpec` (engine → render): `{ band, depth, mechanic, prompt, options[], steerRoll, rewardPlan }`.
- `AnswerEvent` (render → engine): `{ value|chosenIndex, latencyMs, mechanic, timedOut }`.
- `RewardMode` {Honest, SpicierPick, ScaleWithScore, VariableRatio}; `RewardEvent { fire, intensity(0..1), kind }`.
- `SteerContext` hook API — how `steering.js` registers behaviors against option elements exposed by `beats.js`
  (define HERE so Agents C and D can build in parallel instead of serially).
- **Depth→channel map** (single source of truth): depth 0..1 → {flashRate, flashOpacity, subDensity, duckDepth,
  bubbleRate, binauralDepth, bgIntensity}. Every effect agent reads this; nobody hardcodes.
- `QuizRunResult` schema (final emit): `{ route, peakDepth, deepestBand, rewardProfile{chasedReward,chaseMagnitude},
  trajectory[AnswerRecord], affirmedMantras[], tagTallies{}, totalScore, maxScore }`.
- `askAI(request) → Promise<response>` signature (impl in `core/ai.js`, backed by Agent H's endpoint).

**Write these invariants into the contract as comments (they are load-bearing):**
- **Friction-not-lockout:** every steer must leave the refusal completable with effort.
- Reward `intensity` is 0..1 and MUST be multiplied by the user's configured caps at the effect layer.
- Recovery band reverses depth 1→0 and is non-skippable by default.

---

## 3. Phase 1 — Parallel Opus agents (all against Phase-0 contracts; disjoint files)

Give each agent: its brief below + `contracts.js` + this plan + the memory design note. Model **opus**.
A,B,E,F,G,H and Content are fully parallel. C→D overlap via the Phase-0 `SteerContext` hook (or run D right after C).
Agent I is Phase 2, serial, alone (only one touching existing C#).

**Agent A — Engine core** (`core/engine.js`): band/depth state machine; beat sequencer; **route reveal** (secondary
archetype from trajectory, not RNG); **reactive belief-ladder** (`requires`-gating + depth-velocity: score-4 spam
raises velocity / can skip a band, timid answers slow it); progressive route shares 5%→45%. Consumes `PromptBank`,
emits `BeatSpec`, consumes `AnswerEvent`. Done: a full run sequences all 5 bands and yields a `QuizRunResult`.

**Agent B — Reward scheduler + depth map** (`core/reward.js`): `RewardMode` per band
(Honest→SpicierPick→ScaleWithScore→VariableRatio), variable-ratio at climax; emits `RewardEvent` intensity 0..1;
owns the depth→channel math (reads Phase-0 map); enforces cap-clamp. Done: correct `RewardEvent` for
band+depth+chosenScore, and depth drives a documented channel vector.

**Agent C — Render + response mechanics** (`render/beats.js`): DOM/canvas renderers for MC4, Yes/No, **bubble-pop
answers** (centerpiece: the effect bubble IS the answer/reward), mantra/say-it (mic; degrade to type-it), check-in
sliders, mono-choice, funnel (wrong dissolves→respawns as right), destruct-on-select (correct melts/shatters/falls
but registers). Consumes `BeatSpec`, returns `AnswerEvent` incl. `latencyMs`. Exposes option elements via
`SteerContext`. Done: each mechanic renders and returns a valid event.

**Agent D — Steering catalog** (`render/steering.js`): coercive-UI layer applied per beat via `steerRoll`, weighted
by band (Calibration none → Climax heavy), no back-to-back repeats. Catalog: magnet, flee, exile, crowd, size/opacity
skew, occluding draggable GIF, defocus, late-bloom, drag-to-reveal, hold-to-refuse, shrinking hitbox, nested nag,
destruct/overflow-hitbox/assisted-click, mono/funnel/tunnel, drift-and-resolve, decay. **Friction-not-lockout
enforced.** Intensity valve ("play it straight" setting). Reuse the app's existing "Dark Patterns mode" vocabulary.
Done: each steer togglable, band-weighted rolling works, refusal always completable.

**Agent E — In-page effects + audio** (`render/effects.js`, `render/audio.js`): flashes, subliminals, gif bursts,
bubble visuals — DOM/canvas, driven by depth + `RewardEvent`, clamped to caps. WebAudio **binaural** from one depth
scalar (2 osc L/R detuned → lowpass → gain → limiter; beat 10→3.5 Hz, carrier 174→196 Hz as depth 0→1; `rampTo`
glides; emerge reverses for Recovery). Reward chimes. Done: depth 0→1 audibly+visibly ramps the stack; Recovery reverses.

**Agent F — three.js tube background** (`render/background.js`): simplified DtRH tube, depth-reactive tint/speed,
**phone-safe** (low poly, capped DPR, graceful fallback to flat canvas/CSS on weak/no-WebGL devices), never blocks the
loop, off-switch. Done: 60fps desktop, degrades on mobile, off works.

**Agent G — Retention / stats** (`core/stats.js`): local persistence (IndexedDB, localStorage fallback);
longitudinal aggregates — journey (depth-over-time, session count, cumulative trance time, deepest band), **archetype
drift** trend, curiosities (aggregated reward-response + tag tallies), commitment ledger (mantras affirmed); stats
view; **feed-forward** (next intake opens referencing last run). Strictly local, user-viewable/exportable/**deletable**,
no FOMO/streak-pressure framing. Done: run persists, aggregates render, next run reads prior state.

**Agent H — Server AI proxy** (CCP-Server): `POST /intake/ai` — entitlement-gated (reuse Patreon/whitelist gate),
**server-side moderation** on input+output, OpenRouter call, **compact structured request** (route/depth/band/last-3-
answers/tag-tallies — NOT full transcript). Returns AI-accent beats + profile/synthesis. Done: gated + moderated,
valid accent for a compact request, unauthed/over-quota rejected.

**Content stream — AI-bake preset banks** (after Phase-0 schema): draft full `bambi.json`/`drone.json`/`sissy.json`
to the `PromptBank` schema — neutral-trivia camouflage at every heat level, heat-banded suggestive lines,
`requires`-gated ladder rungs, per-niche archetypes (reuse existing `QuizCategoryDefinition.Archetypes` where present).
Ship COMPLETE banks (user reviews later; do not gate on pre-review).

---

## 4. Phase 2 — Integration (after core runs; single Opus agent, touches existing C#)

**Agent I — WebView2 host + session-draft bridge:**
- `IntakeWindow` hosting `Resources/web/intake/index.html` — copy the DtRH WebView2 hosting pattern exactly.
- Lab entry point (`MainWindow.Lab.cs` / `LabTabView`).
- C# `QuizRunResult` DTO mirroring the emitted JSON; receive over the WebView2 message channel.
- **Extend `QuizSessionGenerator.GenerateSession`** to consume `QuizRunResult` instead of a raw score: peakDepth →
  base intensity, route → content theming, rewardProfile → variable vs steady effects, tagTallies → subsystem
  emphasis, **affirmedMantras → seed `SessionTextContent.SubliminalPhrases`/`BouncingTextPhrases` verbatim**. Keep the
  old score-bucket path as fallback for the classic quiz.
Done: an in-Lab run drafts a themed CCP session end-to-end.

---

## 5. Phase 3 — Playtest + tune (user-driven, in the Lab)

Extensive Lab testing before any web move. Tune: band pacing over 25–30 beats (fatigue), steer intensity curve,
reward-decoupling feel, binaural comfort, phone-viewport sanity in WebView2. Web deploy (gated cclabs-site page) is a
separate later effort — NOT in this plan.

---

## 6. Dependency graph & orchestration

```
Phase 0 (contracts) ──┬─> A engine ─────┐
                      ├─> B reward ──────┤
                      ├─> C render ─┬─> D steering (Phase-0 SteerContext lets them overlap)
                      ├─> E effects ─────┤
                      ├─> F background ───┤  (isolated)
                      ├─> G stats ───────┤
                      ├─> H server ──────┤  (separate repo, fully parallel)
                      └─> Content banks ─┘  (needs only the schema)
                                         └─> Phase 2: Agent I (integration + C# bridge)
                                                        └─> Phase 3: user playtest
```

- **Do NOT fan out before Phase 0 lands.**
- A, B, E, F, G, H, Content: fully parallel, disjoint files.
- C then D (or overlap via `SteerContext`).
- Agent I serial, last, alone (only one touching existing C#).
- Isolation `worktree` only if you deliberately run a same-file pair concurrently; disjoint ownership makes plain
  parallel spawns safe. The driver can use the Workflow tool for the Phase-1 fan-out or spawn Agents directly.

---

## 7b. Presentation rework batch (2026-07-20, post-v1)

The v1 build was mechanically complete but presentationally bland. A second fan-out added (all additive
to the Phase-0 contracts; see contracts.js for BeatMeta / BankTheme+themeOf / MediaManifest / the optional
reactive Background API / RewardEvent.jackpot-nearMiss-streak / Mechanic.Interlude):

- **Shell fiction** (boot/index/styles/web-shim): clinical briefing intro w/ consent card + Subject #,
  feed-forward "returning subject" callout, band interstitials (section cards, tone degrades with depth),
  Climax glitch break, in-fiction HUD (Q-counter + gauge-to-spiral dial; no raw depth/band leaks),
  interviewer asides, staged outro ceremony (grade -> route reveal -> recorded statements -> stats ->
  certificate card).
- **Beats**: BubblePop rebuilt as true floating bubbles (spawn bottom, drift up, respawn; pop chains);
  correct-answer color tell removed; Climax decoy pop-storm; band-flavored card transitions; typewriter/
  breathe prompt text in deep bands; shrinking-ring timed beats; Interlude renderers (watch/breathe);
  streak tracking + background forwarding.
- **Effects/audio**: 3-5 no-repeat variants per RewardKind; media-backed Drop/jackpot (host manifest via
  ccp.assets); jackpot ceremony + near-miss tease; streak meter w/ shatter; per-kind audio voices,
  streak pitch-rise, jackpot sting.
- **Tube**: per-band dressings (sterile -> vein-throb -> dawn), answer surges/stalls, reward strobes,
  transition set-pieces (iris/fork/plunge/surface), route-hue tint, steer warp; still 1 draw call.
- **Engine/reward**: velocity never shortens a run anymore (fast play intensifies instead; base 5/6/7/6
  descent), BeatMeta on every beat, seeded interviewer lines/interludes/timed beats, per-band bubble
  guarantee, VR jackpot/near-miss, streak multiplier.
- **Banks**: theme blocks (section-title arcs, interviewer lines, praise, intro/outro copy), archetype
  hues/blurbs, bubblepop coverage across heats, +4 prompts each.
- **C# host**: init now carries `media` (sampled ccp.assets gif/image URLs) + `subjectId` (persisted
  `%LOCALAPPDATA%\ConditioningControlPanel\intake_subject.txt`); quiz-result grants XP (25 + depth + mantras, cap 100).

Status: built + verified headless (import sweep, seeded engine E2E, real-bank full run, dotnet build).
Play-test pending (Phase 3).

## 7c. Play-test feedback batch 1 (2026-07-20, same day)

First play-test verdict: "way better", five notes. All built the same evening (driver + 2 agents):

1. **Interstitial read time** — section cards hold 4.3s (Climax 5.3s), still click-to-skip (boot.js).
2. **Real bubbles, fourth wall broken** (beats.js) — `media.bubbleSprite` (the app's mod-aware
   `bubble.png`, PNG-encoded data URI from C#) is the bubble body; bubbles live on a fixed
   full-viewport `.ib-flightlayer` (z5) and ENTER from a random screen edge/corner, flying
   1.2-2.2s (poppable mid-flight) to their slot over the card, then the old rise/sway. BubblePop
   "let it float away": the release-flavored option (FLOAT_RX label match, else last non-affirming)
   commits as a deliberate un-steered answer when its bubble fully drifts offscreen after the beat
   window; escape+10s hard backstop keeps invariant #1.
3. **Ambient asset layer** (effects.js) — from depth 0.2, a faded GIF/still from the manifest
   drifts across (14-26s) or ghosts in-place (4-8s) every 45s->18s w/ depth; opacity 0.10-0.28 x
   visual cap; max 2; z1 (behind the card); killed by recover()/reduced-motion.
4. **Reward garnishes** (effects.js + audio.js) — big rewards sometimes pair with ONE of: pink
   wash / braindrain (DTRH sf-pfx port: dim + blur + luminosity-blended image wash, ~5s) /
   subliminal flashes (2-4 blinks of `media.subliminals` else theme.praise) / LOOM SPIRAL (live
   `dtrh/shared/loomField.js` render, 2 session-stable randomParams2 looks, 4-5s faded fullscreen;
   lazy import, WebGL w/ 2D fallback, rAF only while visible). Jackpot always garnishes (drain/
   spiral favored); Chime 35-75% past depth 0.35; one at a time, no-repeat-last, never in Recovery.
   Audio: spiral shimmer + subliminal tick cues via a loose `intake-garnish` CustomEvent seam.
5. **C# host** — manifest gains `bubbleSprite` (ModResourceResolver-first, pack-resource fallback)
   + `subliminals` (active SubliminalPool keys, shuffled, cap 40); manifest now ships even with no
   images folder.

Status: built, import-sweep green (12/12), dotnet build 0 errors, output verified fresh, app
relaunched for play-test round 2.

## 7d. Play-test feedback batch 2 (2026-07-20, same day, rounds 2-3)

User notes across two quick rounds: everything too small; bubbles too fast + all of them floated
away (only "let it float away" should, committing with the chime, others must stay put); never saw
a spiral/braindrain/flash reward (one pink only); "drag to reveal" + other hidden fixed widgets
clipping at the top-left; make ALL text way bigger and bold. All BUILT (driver + same 2 agents):

1. **Typography** (driver: styles.css + steering.js + effects.js; bubbles agent: beats.js) — global
   big-and-bold pass. Shell base 18px/600; .intake-q 38/700; .intake-opt 24/700; section title
   48/700 + flavor 26/600; briefing 42/700 + typelines 23/600; asides 23/600; HUD 18/700; grade
   letter 116/800; outro/cert set bumped; cards widened (beat 760, screen 760, outro 720, cert 600).
   beats.js: .ib-q 38/700, buttons 24/700, bubble labels 29/700 (decoy 22), mantra 40/700, CheckIn
   readout 32/700, minis 16/700. effects.js: praise to 88px max, whispers 20-30 bold, sub words
   24-68px, garnish words to 128px max.
2. **Bubble motion round 3** (bubbles agent, beats.js) — wander model KILLED. Entry flight slowed
   to 2.8-4.5s; after landing bubbles HOVER AT THEIR SLOT (sine bob <=14px, 4-7s periods, viewport
   clamp; deterministic spread slots so big bubbles never stack). ONLY the FLOAT_RX bubble ever
   exits: 40-70px/s drift after the escape window opens, full exit commits via normal finalize
   (chime path); all others fade in place on resolve; escape+10s backstop kept. Sizes +30% again:
   answers 195-247px, decoys 130-169px, scale clamp floor 0.45.
3. **Garnish rotation** (effects agent, effects.js) — probability gates REPLACED by a shuffled-bag
   rotation [pink, sublim, drain, spiral]: every fired reward with intensity >=0.2 at depth >=0.1
   draws the NEXT garnish; reshuffle on empty w/ boundary no-repeat guard; jackpot force-consumes
   drain-or-spiral from the rotation; loomDead degrades to a 3-cycle. `createGarnishBag(rng)`
   exported pure; legacy pickGarnish/garnishChance kept for tests but off the live path.
4. **Widget corner-parking** (driver, steering.js) — `parkPlacement()` (exported, rand-injectable):
   fixed widgets (drag-reveal veil, occlude sticker) anchored to degenerate/offscreen rects (bubble
   beats measure anchors mid-flight) were clipping at the viewport top-left; now they park in a
   RANDOM corner inset ~50-140px from the edges, and valid anchors are clamped fully onscreen.
5. **Diagnosability** (driver boot.js + effects agent) — `'intake-log'` CustomEvent seam forwards
   render-layer messages to shim.log (C# log); effects emits loom import failure + first spiral
   frame markers, so a silently-dead spiral is now visible in logs.

Status: built, import-sweep green (12/12), dotnet build 0 errors, output verified fresh, app
relaunched for play-test round 4 (pid at relaunch: 9780).

Round-4/5 follow-ups (same evening):
6. **Float-away drift ungated** (bubbles agent, beats.js) — round 3 had gated the drift behind the
   12-18s escape window, so the float bubble read as broken. Now it hovers 2.5-3.5s after landing,
   then drifts up ±23° at a speed computed so the full exit lands 12-18s after the beat opened
   (clamp 25-45px/s); poppable throughout; backstop rebased to exitBudget+10s; all other bubbles
   still never exit.
7. **Real CCP chimes** (effects agent, audio.js) — reward chime now plays a random real chime1-3
   .mp3 (the app's quiz chimes; byte-identical copies already served under ccp.game at
   dtrh/assets/bubbles/sfx/, resolved via import.meta.url so no C# work). Lazy fetch+decode, routed
   through the existing limiter/caps, +1 semitone per streak via playbackRate; synth bell is the
   permanent fallback (file:// or load failure -> 'intake-log' diagnostic). Garnish cues unchanged.
Also this evening (unrelated to intake): two crashes while play-testing were torn-build artifacts /
the other session's uncommitted VideoService vmem composite (native 0xc0000005 ~1s into playback);
mitigated by setting VideoBlurredBackgroundEnabled=false (settings.json, backup .bak-preblurfix).

Round-5 batch (Opus subagents per user token pref; same evening):
8. **Float drift 2x** (beats.js) — exitBudget 7-10s from beat open, speed clamp 50-90px/s, backstop
   auto-rebased.
9. **Mantra skip** (beats.js) — secondary `ib-alt ib-mantra-skip` button "Can't speak, I got my
   mouth full right now" under the say-it controls; commits via the scaffold's timeout path
   `finalize(undefined, undefined, true)` = NOT an affirmed mantra.
10. **"Just watch" interlude = real Loom spiral, FULLSCREEN** (beats.js) — `.ib-loomlayer` fixed
   z5 layer, lazy loomField.js import (fresh randomParams2/interlude, WebGL + 2D fallback,
   'intake-log' on failure -> old card visual), bold "Just watch" clamp(40px,7vw,80px)/800 fading
   over 2.5-4s.
11. **Breathing interlude rework** (beats.js) — fixed z1 `.ib-breathback`: random media.gifs URL
   fullscreen cover at 0.24 opacity, loom spiral fallback when no gifs; breath focus = actual
   media.bubbleSprite PNG (190-240px) on the same 8s ib-breathcycle keyframes (circle kept if no
   sprite).
12. **Steer.MeltAway** (contracts +1 enum key, engine Deepening+Climax pools, steering.js
   installer+CSS) — armed wrong options destruct on press WITHOUT committing (random MELT squash/
   blur/drip vs FALL drop-off-viewport, on a fixed-position clone so tf state never fights);
   ~half armed at low intensity -> all at high; invariant #1 kept: presses bump the guard (misses
   funnel via the EVASIVE tracker to the preserved layout slot), frictionRelease re-forms all
   destroyed options + disarms the veto, cleanup re-forms too.
Status: sweep 12/12, build 0 errors, output fresh, relaunched (pid 11392).

### 7e. Rounds 6-7 (play-test feedback, 2026-07-20 evening)
13. **Cover steers truly anchor** (boot.js + steering.js) — steering now receives `media` from
   boot; new `whenAnchored(S, el, cb, tries=40)` rAF helper re-measures until the option rect is
   valid + stable 2 frames (<1px), else cb(null). OccludeGif + DragReveal build INSIDE the
   callback: cover the option (rect +12px overhang, clamped) so it reads as MISSING from the
   card; null -> steer skipped entirely (corner-parking removed for these two). OccludeGif
   renders a random `media.gifs` <img> sticker (±4deg tilt composed into drag transform, error ->
   gradient+💠 fallback); drag-clear threshold `max(c.w*0.6, 90)`; opacity .3s reveal.
14. **Card tilt + sway** (beats.js) — from qIndex>=4, 25% chance: static ±5-10deg tilt
   (`.ib-tiltwrap`) + near-imperceptible ±0.6deg sway 9-14s alternate (`.ib-swaywrap`, nested so
   entry/exit transforms compose); question-style cards only (no Interlude/BubblePop); no sway
   under reduced motion.
15. **15% melt-on-card-click** (beats.js) — clicks on card chrome (not controls) roll 15%: card
   melts (700ms squash/drip/blur/fade, forwards), holds 500ms, re-forms (400ms) fully
   interactive; once per beat; pointer-events off during melt; timers in beat cleanups; animates
   the card element inside the tilt wrappers; reduced-motion = 0.2s fades.
16. **Cardless breathing interlude** (beats.js) — breathe branch hides the card; fixed
   `.ib-breathstage` (z5, pointer-events none) holds just the bubble + phrase over the gif
   backdrop; bubble DOUBLED (clamp 380px/40vw/480px, circle fallback 320-460px) with outer
   `.ib-breathgrow` scale 1->1.3 across min(interlude duration, 20s) composing with the 8s
   ib-breathcycle; phrase 800-weight clamp(24-34px) white + dark glow.
17. **Corner-garble fix + opaque veil** (steering.js) — Crowd decoys + Exile push + OverflowHit
   catch-pad now whenAnchored-gated (skip on null; decoy width floor 120px + onscreen clamp);
   audit: Magnet/Flee/AssistClick re-measure per tick, interaction-time measurers safe. Veil
   `.intake-steer-veil` now solid #262640 + faint border + muted text = unreadable missing slot.
Status: sweep 12/12 both rounds, build 0 errors, output fresh (note: output path moved to
bin\Debug\net8.0-windows10.0.19041.0\win-x64\ after the bureau-game branch csproj change),
relaunched (pid 17500). Round-7 play-test pending.
18. **BubblePop loom backdrop + float bubble rises** (beats.js, round 8) — `mountLoomSpiral`
   hoisted to render-scope (shared with interludes); every BubblePop beat mounts a fresh-params
   loom spiral `.ib-bubbleloom` (fixed z1, 25% opacity, pointer-events none, behind card z2 +
   flight layer z5), torn down with beat cleanups, skipped on reduced motion / import fail.
   FLOAT_RX bubble no longer flies to a hover slot: spawns below the bottom edge (15-85% x band,
   dodges the card span), rises 100-140px/s with ±30-50px sine sway, ~8-11s to cross; full exit
   above the TOP commits via the same deliberate finalize path; clickable all the way; backstop
   exitBudget(8-11s)+12s; reduced-motion path unchanged. Dead drift-branch code removed.
Status: sweep 12/12, build 0 errors, output fresh, relaunched (pid 14500).
19. **Slider auto-rise + wheel** (beats.js, round 9) — new `wireSlider()`: mouse wheel = ±3% of
   range on any in-beat range input (preventDefault, dispatches same bubbling `input`); from
   qIndex>=3 the value creeps UP on a cleanup-registered rAF tick, 2%/s ramping linearly to 7%/s
   at qIndex 26; any trusted gesture (pointerdown/input/keydown/wheel) pauses 1.8s (synthetic
   rises are isTrusted=false so no self-pause); caps at max, never auto-commits. Today only the
   CheckIn `.ib-slider` exists; helper is generic.
20. **See-through cards over backdrops** (beats.js + effects.js + styles.css, round 9) —
   ref-counted `ix-backdrop-live` body class (`backdropRef()` duplicated per file, counter in
   body.dataset.ixBackdrop): beats flags .ib-bubbleloom/.ib-loomlayer/.ib-breathback; effects
   flags pink wash/drain/loom spiral garnishes (decrement funneled through removeNode with an
   idempotence marker; sublim words/ambient/gif tiles deliberately unflagged). styles.css: while
   live, .ib-card/.ib-bubble-card/.intake-screen background -> rgba(37,37,66,.42) with .6s fade
   both ways (base rule carries the transition); text/borders untouched.
Status: sweep 12/12, build 0 errors, output fresh, relaunched (pid 21276). Round-9 play-test
pending.
21. **~50-question runs + repetition fix** (engine.js + beats.js horizon, round 10) — band bases
   9/11/16/14 = 50 graded (was 5/6/7/6=24); MIN/MAX_BEATS 5/22; EXPECTED_DESCENT 50;
   HARD_BEAT_CAP 1600 (endless backstop, scaled by ratio not +20% — flagged); interludes now
   INTERLUDE_EVERY=8 -> ~2 per deep band, evenly spaced, never on band-final beat. REPETITION
   root causes: (a) no-seed runs hashed a CONSTANT ('run') -> identical mulberry32 stream every
   play; now fresh entropy per run, explicit cfg.seed still reproduces; (b) dry heat-window
   cleared usedIds mid-run; now widen ladder strict->wide->any (never re-serve, last-resort
   excludes lastServedId). Smoke: 50 graded exact, zero repeats, timid 68 still zero repeats.
   beats.js wireSlider horizon 26->50.
22. **Banks x3 rewritten to ~200 prompts each** (bambi 201 / drone 201 / sissy 200, round 10) —
   per niche: 40 heat-0 calibration (trivia+innocent personality) + ~32 per heat 1-5,
   progressively explicit at 4-5 per owner direction; existing prompts kept, schema/tag vocab/
   ladders untouched; validateBank 0 errors all three + structural self-checks (unique ids, mc4
   4-options, boolean yesno/bubblepop, mantra affirmsMantra, band/minDepth gating copied from
   existing tiers).
Status: sweep 12/12, build 0 errors, output fresh, relaunched (pid 2180). Round-10 play-test
pending. NOTE: stale scratchpad verify-intake-engine.mjs asserts old 24-beat numbers.

### 7f. Round 11 (2026-07-20 late evening)
23. **Tilt drifts in** (beats.js) — .ib-tiltwrap transition transform 5s ease-in-out; mounts at
   0deg, 600ms settle timer applies target ±5-10deg; RM = static tilt, no drift.
24. **Beats layer fades** (beats.js) — fadeLayerIn/fadeLayerOutRemove: bubbleloom/loomlayer/
   breathback fade in 2-2.2s, out 0.8s (remove @1s, idempotent, dataset.ibFadingOut guard);
   backdropRef(false) still fires AT cleanup (not delayed).
25. **Spiral variety** (beats.js + effects.js) — REAL BUG: effects showLoomSpiral cached TWO
   randomParams2 rolls at first import and alternated them all session. Cache deleted; every
   mount rolls fresh through shared freshSpiralParams guard (window.__ixSpiralSig, cross-module
   so garnish-after-interlude can't repeat either); beats palette widened (theme accents +
   shuffled LOOM_SWATCHES slice + white).
26. **Garnish/gif fades** (effects.js) — wash/drain/spiral 1.6s in / 0.9s out (was 0.2-0.7s
   pops); reward/jackpot gifs floored 1.2s in / 0.7s out; preempt fast-fade 250ms + dispose
   instant kept; backdropRef release moved to START of fade-out (releaseBackdrop idempotent w/
   removeNode marker).
27. **DragReveal spring-back** (steering.js) — threshold-disarm removed; veil follows drag
   (opacity floor .85), on release 250ms grace + 400ms ease return (total ~650ms exposure
   window); regrab mid-return resumes from rendered position (DOMMatrix m41); frictionRelease
   still permanently disarms; RM = instant return.
28. **Cover anchors survive typewriter** (steering.js) — whenAnchored now needs 45 consecutive
   quiet frames (~750ms; movement resets streak not budget, cap 600 frames) so covers appear
   AFTER typing settles; new followAnchor watcher (200ms cadence) glides sticker/veil to the
   option's new spot on >2px shift, deferred while dragging, veil spring-back home updated;
   wa-test.mjs 7/7.
29. **Breathe bubble poppable** (beats.js + audio.js) — bubble pointer-enabled (stage stays
   none); pop plays real Pop.mp3 (lazy decode a la chimes, synth fallback, audio.pop) + 250ms
   pop anim + 6 fragments; ends interlude early via the natural finalize(undefined,true,false)
   path after 240ms; then one local subliminal flash from 10 niche-neutral lines (~900ms,
   self-cleaning, outlives teardown); double-fire guarded (popped/committed).
30. **Late-band card zoom** (beats.js) — Climax+Recovery question cards: outermost .ib-zoomwrap
   scale 1 -> cap, transition 15s ease-out, starts at 600ms settle; cap = min(1.18, 1.06 +
   max(0,qIndex-30)*0.006); same exclusions as tilt; RM = no zoom.
Status: sweep 12/12, build 0 errors, output fresh, relaunched (pid 1488). Round-11 play-test
pending.

### 7g. Round 12 (2026-07-20, evening)

31. **DDLC "are you sure?" jumpscare** (beats.js + audio.js + web-shim.js + IntakeHostService.cs) —
   wrong discrete presses (scoreDelta<=0) in Deepening/Climax/Recovery roll 2%/3.5%/5%; once per
   run (_ixEventFired reset in createBeats). Intercepted in attempt() BEFORE tryCommit (press never
   commits). audio.suspendBed/resumeBed (idempotent, glided) + glitch() (5 bit-crush stabs + noise)
   + errorSpam(8). body.ix-freeze pauses .intake-stage/.ixfx-* animations; overlay .ixev-root
   z2147483000 on body (paper card, "are you sure?" + :), Yes/No). Yes pinned fixed, drifts to a
   random off-screen point over 8-12s w/ 2-sine wander, shrinks to >=28px hit height; fully
   off-viewport => graceful No. Caught Yes: glitch + error spam + steps(10) glitch overlay + 1s
   fullscreen media gif @180ms + intake-close @1200ms => shim.closeHost() => C# case "intake-close"
   sets _exiting + DisposeAll (clean abort, no session draft). No => full restore, beat live.
   RM: linear drift, black flash instead of glitch. Timer cleared on event start.
32. **VO pilot (10 clips)** — new C:\Projects\ccp-trailer\gen_intake_vo.py (manifest-driven,
   missing-only, --dry/--limit/--force, auto-normalize -22.4 LUFS); Bambi voice, eleven_v3,
   [soothing] tag + stability .62/style .18 = calm clinical register. assets\vo\vo_manifest.json:
   intro_1..3 (authored) + q_<bankPromptId> x7 (verbatim, heat 0-4 spread). All 10 OK via lane-2
   failover (LANE 1 QUOTA EXHAUSTED - top up before the ~600-line full run). Playback NOT wired
   (future: fetch ccp.game/intake/assets/vo/<file>, missing entry = text-only).
33. **SFX gap map** (proposal only, scratchpad intake-sfx-proposal.md) — audio.js inventory (14
   synth voices + chimes/Pop.mp3 samples); 7 must ids (~14 clips: melt/reform/spiral up+down/
   glitch/error/freeze) + 13 nice (~20 clips) + 12 advised-silent + 2 zero-cost re-wires
   (centerpiece pop -> Pop.mp3, jackpot -> GG/Burst.mp3). Integration convention: assets\sfx\ +
   sfx_manifest.json + lazy loadSfx + audio.sfx(id) additive, synth fallbacks retained. Owner
   picking via tkinter; generation NOT started.
34. **DTRH tube background** (background.js + boot.js + styles.css) — imports dtrh/game/biomes.js
   BIOMES_ALL (pure data; tunnel.js NOT copied); dressFromBiome() drives the existing intake tube
   shader; 16-biome shuffled deck, depth-biased (calm shallow, wild deep), nextBiome() per beat via
   safeCall in boot.js beat loop, ~1.4s crossfade; sterile band dress until first question. z1
   body::after vignette scrim (~18% darken, RM 34% flat); DPR cap 1.5; visibilitychange rAF pause;
   dispose extended. Speed/glow/throb capped below game levels.
35. **SFX batch GENERATED** (gen_intake_sfx.py in ccp-trailer, assets\sfx\) — 39/39 clips, 25 ids
   (A:7 + B:13 + C-overrides typewriter-tick/card-entry/card-exit/incorrect-feedback), all lane 2,
   normalized -22.4 LUFS, sfx_manifest.json gains 0.9/0.4-0.5/0.15 + loop flags (veil-drag, breathe
   pair, pressure-heartbeat). LANE 1 KEY LACKS sound_generation SCOPE (401 missing_permissions, not
   quota) — failover extended to cover it. API floor 0.5s: error-blip/typewriter requested 0.5s then
   trimmed w/ 20ms declick fade (normalize BEFORE trim — LAME asserts on <0.5s inputs). NOT WIRED:
   playback integration (loadSfx + audio.sfx(id) + centerpiece-pop/jackpot re-wires) awaits user
   clip review via picker. Open: climax-glitch gain 0.5 (soothing read) vs 0.9.
36. **Slider auto-rise fix** (beats.js) — root cause: range input has default step=1, so the
   per-frame float creep (~0.03-0.12/frame) written to slider.value snapped back to the integer
   every frame (accumulator lived IN the quantized DOM value => pinned at 50 forever; wheel's 3-unit
   jump worked, the tell). Fix: float accumulator decoupled from DOM (acc advances, DOM gets
   Math.round(acc)); pause absorbs user drags via acc=cur() so resume never jumps. Band boost:
   Climax x1.5 (~9%/s late), Recovery x1.8 (~12.6%/s); base ramp 2.5->7%/s from qIndex 3. RM still
   rises. slider-rise-test.mjs 8/8 (incl. old-loop-stuck-at-50 repro); sweep 12/12; exports intact.
37. **Melt = skip, no reform** (beats.js) — owner review verdict: melted card advances the run.
   Melt commit clears timeoutTimer then ~700ms anim + ~250ms hold => finalize(undefined, undefined,
   true) (the mantra-skip/timeout path: scoreDelta 0, not wrong, jumpscare CANNOT fire — bypasses
   attempt()). Reform code + ib-cardreform keyframes REMOVED (they lived in beats.js IB_CSS, not
   styles.css); .ib-cardmelt-anim pinned forwards !important so band EXIT classes can't re-show the
   melted card. MeltAway steer untouched (melts option clones, reformAll is invariant-#1
   load-bearing). melt-skip-test.mjs 11/11; sweep 12/12.
38. **SFX/VO round-2 after owner picker review** — SFX: 10 files discarded (band-transition x2,
   climax-glitch, typewriter-tick, veil-drag x2, veil-spring, breathe-exhale, card-reform x2 —
   removed from disk+manifest+generator SPEC), gains cut (loom-spiral-up/down, pink-wash,
   sticker-drag 0.15; breathe-inhale 0.10), 9 regenerated (card-entry/exit = heavy felt slide
   prompts 1.0s; breathe-inhale distant/faint; error-blip x2 / freeze-sting-1 / glitch-burst-2 /
   funnel-dissolve / surface-bloom fresh takes). Final: 29 mp3s / 18 ids, kept files
   hash-verified untouched. VO: ALL 10 re-rendered --force with per-line v3 direction (manifest
   schema v2: per-entry v3 text + settings); malice arc = stability falls/style rises with heat
   (0.50/0.22 h0 brisk-legit -> 0.26/0.68 h4 intimate whisper); intro reworded for cadence,
   questions verbatim + tags/ellipses only; band->preset table documented in gen_intake_vo.py
   report for the ~600-line run. Assets + beats.js hot-copied to win-x64 output (sfx 30, vo 11
   files mirrored). User re-reviewing the 19 redone clips in scoped picker.
39. **SFX final set + integration WIRED** (audio.js + beats.js + effects.js + steering.js +
   boot.js) — after 2nd owner review the library locked at 14 ids / 21 clips (card-entry/exit,
   breathe-inhale, funnel-dissolve cues died; error-blip/freeze-sting/glitch-burst renumbered to
   dense 1..n, hash-verified pure renames). audio.sfx(id, intensity?, {variant, loop}) additive
   surface: lazy manifest fetch merged over inline SFX_MANIFEST_FALLBACK, per-id buffer cache,
   shared limiter, manifest gain x clamped intensity, no-repeat-last variants, loop handles w/
   stop(fade). effects/steering reach audio via `intake-sfx` CustomEvent seam (no boot rewiring).
   Jumpscare glitch()/errorSpam() + jackpot chime prefer real samples, synth fallback kept.
   Wiring: card-melt@melt-start, spiral up/down@all-3 loom mounts/unmounts, freeze-sting@
   startSureEvent, destruct-shatter, incorrect-feedback@wrong-commit (post-jumpscare-gate),
   pressure-heartbeat loop@timer p>=0.8 w/ cleanup stop(0.4), pink/drain wash@mount,
   sticker-drag@drag-start+clear, surface-bloom@emerge, briefing-open@run-start, BubblePop
   onPop->audio.pop() (was missing). sfx-spec-test.mjs ALL PASS; sweep 12/12; 5 files hot-copied
   + hash-verified.
40. **VO round 3 (pacing)** — ellipses cut to <=1/line (0 on h0), [soft exhale]/[gentle breath]/
   [soft chuckle]/[breathes softly] connectives, 3 outliers re-rolled shorter-take-kept; durations
   h4 6.0s, intros 9.9-13.4s (breath tags cost ~1-2s each; eleven_v3 HAS NO speed param —
   text-driven pace only). Malice-arc settings unchanged. Mirrored to output. User auditioning in
   scoped picker.
41. **VO playback WIRED** (audio.js + beats.js + boot.js) — audio.voice(id,{onEnd}) additive:
   lazy manifest, LRU-8 buffer cache, VOICE_GAIN 0.8 via shared limiter, one-line-at-a-time, bed
   ducks to 60% under voice (composes w/ suspendBed: suspend captures un-ducked level, subsumes
   duck); intro_1..3 chain thru briefing (Begin/skip = voiceStop); questions speak on text reveal
   via beat.prompt.id -> 'q_'+id (all prompt mechanics incl. BubblePop; missing entry = silent);
   stops on resolve (cleanups) + jumpscare freeze. vo-spec.mjs 22/22; sweep 12/12; hot-copied.
42. **Slider band-gate + tilt rework** (beats.js) — auto-rise ONLY Climax (~2.5s delay, 3%/s) /
   Recovery (~1s delay, 6%/s); qIndex ramp dropped; wheel everywhere. Tilt regated band>=
   Establishing (was qIndex>=4 = still lv1, the "since the start" bug); caps ±4/8/16/32 deg by
   band, floor 30% of cap, 25% roll kept, transition 5s->16s slooow lean; RM = no tilt.
   slider-rise-test 9/9.
43. **BottomlessNo steer** (steering.js + contracts.js) — 5% self-rolled overlay (engine
   STEER_POOL untouched) on YesNo/MC4 wrong/refuse options, bands Deepening+; capture-phase veto,
   fixed clone gravity-falls off-screen (~820ms) revealing same button, N=6..10 draws, (N+1)th
   press commits real (jumpscare path intact); yields to other refusal-gate steers; guard.bump per
   fall + frictionRelease disarm (invariant #1); RM = 150ms fades. bottomless-test 13/13.
44. **Tube background FIXED** (background.js + boot.js) — root cause: frag shader `precision
   mediump float` vs three-prepended highp vert = uniform precision mismatch = hard LINK failure;
   three.js only console.errors link fails + render() doesn't throw = silent flat void. Fix: highp
   both stages (matches dtrh tunnel.js). Hardening: logSeam() -> intake-log CustomEvent,
   renderer.debug.onShaderError + forced renderer.compile in initWebGL (fail -> loud + 2D
   fallback), loadOptional logs import failures (was bare catch{} -> stub). Headless chromium
   repro: pre-fix blank, post-fix full spiral tube. Import map three -> dtrh/vendor (was fine).
45. **VO production batch COMPLETE** — 598 clips synthesized (595 new + 3 pilot re-rolls), 0
   failures, all lane 2 (~54k chars, lane 3 reserve untouched). Library: 605 mp3 = 3 intro + 602
   questions (bambi 201 / drone 201 / sissy 200, ids globally unique), manifest 605 entries exact,
   verbatim-guard 0 violations, all -22.4 LUFS, mirrored to output (7 kept pilots byte-identical).
   Authoring tool: ccp-trailer\build_intake_manifest.py (idempotent, band->preset table, <=1
   ellipsis, breath/chuckle ~1-in-5 on h2+ only, inevitability tags h2+ w/ falling intonation on
   yes/no). Re-rolls: intro_3 pauses restored, h2 "[alluring, almost menacing] a while?", h3
   rhetorical falling confirmation.
### 7h. Round 13 (2026-07-20, late night)

46. **Cards always translucent** (beats.js IB_CSS + styles.css) — all card-family fills permanent
   rgba(37,37,66,0.5) + backdrop-filter blur(9px) brightness(.72); opts/inputs rgba .5 + blur(4);
   ix-backdrop-live kept as thinner 0.35 state; text full-alpha; jumpscare paper card excluded.
   Key finding: question/bubble/interlude cards are ONE element (.ib-card + modifier classes).
47. **OccludeGif multi-cover lv4** (steering.js) — Climax 2 covers -> 3 @ depth>=0.9; covers are
   WRONG options (steered pick = correct stays visible, preserves baseline intent); per-cover
   independent GIF/rotation/anchor/follow/drag-clear, stagger 170-230ms, shared 150ms sfx
   throttle, frictionRelease removes all. NOTE: Recovery(lv5) branch exists but UNREACHABLE —
   engine STEER_BAND_WEIGHT[Recovery]=0 ("recovery never coerces" invariant); flagged to owner,
   awaiting ruling whether to open lv5. occlude-test.mjs green.
48. **GG/Burst removed from intake** (audio.js) — owner heard them in-run; whole jackpot sample
   path deleted (loader/cache/warm/exports), jackpot = synth sting only; dtrh files untouched;
   sfx-spec-test updated + green.
49. **End-run recap redesign** (boot.js, scoped <style id="ix-outro-css">) — was ~2x viewport w/
   cut-off Assessment Record + no scroll affordance (old flow dimmed ceremony AND appended full
   second cert = duplication, removed). Now ONE fitted certificate: grade+classification 2-col
   headrow, 3-up stats, compact mantras, record folded in as footer (2-col rows, seal top-right,
   handoff + hosted-only exit button preserved); clamp() sizing keyed to vh; .ix-outro-scroll
   frame (max-height 100dvh-28px, thin scrollbar, overflow-only fade+chevron hint) = nothing ever
   unreachable. RM: fade transition disabled.
50. **HoverSwap steer** (steering.js + contracts.js) — ~6% of BINARY beats (YesNo, or MC4 with
   exactly 2 options) in Deepening/Climax; pointerenter on WRONG option swaps the pair (~210ms
   transform exchange, cursor lands on correct), N=4..6, 350ms re-entry debounce; skipped while
   the primary button is held (never swaps mid-click); exhaustion leaves current positions (no
   snap-back tell); frictionRelease glides both home; never intercepts clicks; transform-only so
   tab/AT order never moves; reduced-motion OR sparkles-off = no swap at all (hoverSwapMuted,
   gated at the roll site so MouseHijack can still take the beat); yields to refusal gates +
   position steers + BottomlessNo. hoverswap-test 5028 asserts green.
51. **GifBurst reward** (effects.js + reward.js) — 'gifburst' kind, 18% roll (after BubblePop,
   before heat rules); ONE gif 30-50vmin ±8deg, pop-in, opacity by depth->band ladder
   .15/.30/.50/.75 (1.00 rung unreachable — rewards never fire in Recovery, flagged); click <6px
   = dismiss; fling >=0.45px/ms release velocity = momentum throw off-screen; slow release =
   drop-in-place; 6s pausable cap then 700ms fade; NO HYDRA (concurrent roll skipped, not
   queued); z7 .ixfx-burst-root; no backdropRef; RM = click-dismiss only. gifburst-test 19/19.
   (Report suggests optional contracts.js RewardKind.GifBurst canonicalization — literal
   duplication pattern used instead, consistent w/ codebase.)
52. **User-pool subliminals** (IntakeHostService.cs + web-shim.js + boot.js + NEW
   render\subliminals.js; sweep now 13 modules) — C# reads App.Settings.Current.SubliminalPool
   (per-mod, enabled only), replicates SubliminalService.FindLinkedAudio (mod flashes_audio ->
   Resources\sub_audio, 21 whispers); audio dirs OUTSIDE both vhosts -> clips inlined as data:
   URIs (512KB/clip, ~6MB budget), config.subliminals[{text,audio?}] cap 400 shuffled. Web:
   flash every 8-20s (tightens w/ depth), upper/lower-third placement, opacity .35->.85*caps,
   no-repeat-5, own AudioContext gain .5 LRU-10 skip>8s clips; pauses on ix-freeze/overlay;
   disposed at Recovery start + outro. FLAGGED: plays audio whenever clip exists (ignores WPF
   SubAudioEnabled toggle — owner may want it gated).
53. **MouseHijack steer** (steering.js + contracts.js) — armed silently on Deepening/Climax
   MC4/YesNo/Mono/Destruct (no other self-rolled gate, no position steers — CONFLICT ruling,
   yields); 9s un-committed linger (skips mid-drag/freeze, 6 rechecks) -> 40% roll once; engage:
   cursor:none (leak-proof class) + 14px glow-dot virtual cursor z2147480000, pull blend
   0.25->0.90 over 5s, in-rect 150ms dwell -> forceComplete(correct) thru normal finalize;
   fight-abort 2500px opposing movement or Esc = restore + NO commit (guard.bump per 800px,
   verified aborts before escapeEffort=6); restoration on every teardown path; RM = disabled
   entirely. hijack-test 39/39.
Status round 13: items 46-53 ALL LIVE; C# rebuilt 0 errors (subliminals config), ALL intake web
files hash-verified fresh, relaunched pid 11848. Play-test pending. UNCOMMITTED. OPEN QUESTIONS
for owner: (1) open Recovery lv5 to steers/rewards ("recovery never coerces" now clips
multi-covers, hijack, AND GifBurst's 100% rung) or keep sanctuary; (2) subliminal audio: respect
WPF SubAudioEnabled toggle or keep play-when-available.

### 7i. Round 14 (owner feedback 0721)
54. **Boring-pool dilution** (banks/*.json) - heat-0 doubled 40->80 per bank (+40x3 mundane
   trivia, schema-mirrored: mc4/yesno/checkin mix, entry-archetype-neutral tags, NOT in
   ladders); +6 boring at h3 AND +6 at h4 per bank (no requires gate -> surface via heat
   window mid-lategame as contrast). 156 new prompts, 0 id collisions, all banks parse.
55. **Melt 1-in-30** (beats.js) - MELT_CHANCE = 1/30 (was 0.15); comments synced.
56. **Slider auto-rise 30% roll** (beats.js) - SLIDER_AUTORISE_CHANCE = 0.30, rolled once per
   slider beat after the band gate (Climax/Recovery), fail = early return, fully manual;
   wheel-nudge + float-accumulator untouched.
57. **Subliminal audio respects WPF toggle** (IntakeHostService.cs) - OWNER RULING resolved
   open question (2): BuildSubliminalPool gates ALL audio resolve/inline on
   App.Settings.Current.SubAudioEnabled (same flag SubliminalService gates whispers on,
   verified 3 call sites); off = text-only entries, zero audio I/O.
58. **"sure_" taunt VO corpus** (assets/vo) - 60 clips, 20/niche (sure_bambi/sissy/drone_01..20),
   bambi=saccharine-condescending, sissy=teasing-feminizing, drone=clipped-clinical-cold
   (stab .50/style .30 for synthetic edge; others .34/.55); -22.4 LUFS; lane 2 carried;
   add_sure_taunts.py additive/idempotent. Manifest 665 entries at that point.
59. **Wrong-answer FREEZE GATE 33%** (beats.js + audio.js + background.js + boot.js) - hook in
   attempt() post-jumpscare-gate pre-incorrect-feedback (suppressed when fired); qualifies
   MC4/YesNo w/ known-correct + delta<=0; mutual-exclusion flag gateUsedThisBeat BOTH
   directions vs jumpscare. Freeze: background.setFrozen(true) (new API, rAF held), ix-freeze
   (subliminals+CSS anims), timer ring banks frozenAccum + heartbeat silenced/re-armed,
   timeoutTimer cleared, melt guarded, steers released idempotently. Red typewriter subtitle
   (bottom, z2147479500) mirrors manifest text (fallback "Are you sure, sweetie?"), VO
   sure_<niche>_NN via audio.voice, niche plumbed boot->createBeats. 20s escalation: tremble
   p^2*10px (RM: static), red tint 0->0.5 (z2147479000), audio.rumbleRiser (sub-sine+detuned
   tri 38->60Hz + noise bed, tremolo depth/rate climb, gain 0->0.5, instant stop). Timeout:
   local glow-dot eases to correct, tryCommit(force) -> normal finalize (real chime), instaclear.
   gateCleanup in cleanups[], idempotent, committed-latch = no double-resolution.
   freeze-gate-test 35/35; no regressions (melt 11/11, hoverswap 5028, gifburst 19, occlude).
60. **Giggle sprinkles 3%** (boot.js + sfx_manifest.json) - BS-mode (Bambi Sleep) giggles from
   Resources\sounds\giggle1-8 -> 7 mp3s copied as giggle-1..7 (skipped the lone .wav, loader
   is mp3-hardcoded); manifest gain 0.3 = 30% amplitude; GIGGLE_CHANCE=0.03 rolled after
   engine.next() per beat, !step.done guard (never over outro); random variant per call.
61. **Boring-question VO batch** (assets/vo) - 156 q_* clips for item 54's prompts; h0
   professional/brisk no-ellipsis, h3 slyly/darkly-amused, h4 intimate-whisper/certain w/
   inevitability falling-intonation heuristic on yes/no; -22.4 LUFS, lane 2, 0 failures.
   Manifest total 821 entries. add_boring_intake_vo.py additive/idempotent.
62. **Tilt<->auto-rise coupling** (beats.js) - single shared roll: wireSlider sets
   sliderAutoRoseArmed (render-scoped, switch runs before tilt block); tilt sign forced +mag
   (clockwise = leans toward slider's increasing side) when armed, random coin-flip otherwise;
   magnitude/band caps/16s ease/RM-disable unchanged. tilt-sign-glitch-run-test 9/9.
63. **Glitch easter egg 1-run-in-10** (beats.js) - GLITCH_RUN_CHANCE=0.10, lazy once-per-run
   eligibility roll in maybeFireSureEvent BEFORE per-beat IX_EVENT_CHANCE roll; lazy placement
   = ineligible runs return WITHOUT setting gateUsedThisBeat so freeze gate stays free on that
   press (tested). Eligible runs fire at same unpredictable mid-run point as before.
Status round 14: items 54-63 ALL LIVE; C# rebuilt 0 errors (SubAudioEnabled gate), 871 intake
files hash-verified fresh in output, relaunched pid 16016. Play-test pending. UNCOMMITTED.
REMAINING OPEN QUESTION for owner: (1) Recovery lv5 sanctuary vs full-intensity (unchanged).
Question (2) RESOLVED this round (toggle respected, item 57).

### 7j. Round 15 (owner feedback 0721 late)
64. **Subliminals gated to phase 2+** (boot.js) - no layer/timer/AudioContext during Calibration;
   lazy one-shot mount on first Establishing beat (beat.band gate in loop), Recovery
   dispose/never-rearm preserved.
65. **+1s card-swap breather** (boot.js) - CARD_SWAP_EXTRA_MS=1000 after engine.next on the
   active-answer path only; excluded: outro, timeouts/melt (timedOut), interludes, bandNew
   (interstitial owns pacing), jumpscare (host closes); freeze-gate gains it (negligible).
   Exit anim + afterglow cover the gap. m2Test=0ms.
66. **Shared baseline 200** (banks x3) - shr_h0_* ids, SAME entries in all 3 banks (only tags
   differ, per-bank neutral archetype sets); ONE VO clip serves all mods. h0 now 280/bank.
   Not in ladders, 0 collisions.
67. **Thematic 120/mod** (banks x3) - +24 per heat h1-h5 per bank, schema/mechanics-mix/
   tag-vocab/requires-gates mirrored per tier; totals 573/572/573 prompts. FINDING: h5 maps
   to CLIMAX band (HEAT_WINDOW.Climax=[3,5]); engine Recovery = hardcoded lines, never reads
   banks -> h5 authored max-intensity climax-gated (minDepth .8-.85) matching existing, NOT
   aftercare (all 3 agents verified vs engine.js).
68. **VO: 560 new clips** (assets/vo) - 200 shared-baseline (h0 professional/brisk) + 360
   thematic (24/heat/niche, full h1->h5 malice arc, inevitability falling-intonation h2+,
   mantras on low-stability intimate presets). Manifest 1381 entries, all -22.4 LUFS, lane 2,
   0 failures. New scripts add_shared_baseline_vo.py + add_thematic_intake_vo.py (additive).
Status round 15: items 64-68 ALL LIVE; web-only wave, NO rebuild (C# untouched), 1431 intake
files mirrored + hash-verified into running build (pid 16016) - fresh intake run picks it up.
Play-test pending. UNCOMMITTED. OPEN QUESTION (1) Recovery lv5 unchanged.

## 7. Open (non-blocking)

- **Product name** — placeholder "Graded Intake"; user will rename. Keep the fiction string in one constant so a
  rename is a one-line change.
- Everything else is confirmed (niches Bambi/Drone/Sissy; three.js in v1; banks AI-baked full; server authorized;
  session-draft in v1; user reviews banks post-hoc).
