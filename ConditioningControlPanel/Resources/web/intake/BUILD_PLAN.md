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

## 7. Open (non-blocking)

- **Product name** — placeholder "Graded Intake"; user will rename. Keep the fiction string in one constant so a
  rename is a one-line change.
- Everything else is confirmed (niches Bambi/Drone/Sissy; three.js in v1; banks AI-baked full; server authorized;
  session-draft in v1; user reviews banks post-hoc).
