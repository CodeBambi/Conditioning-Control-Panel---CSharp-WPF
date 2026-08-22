# Chaos Mode — Agent A: Loop, Cadence & Canonical Event Surface

Research/planning only. No gameplay code changed. Source of truth = code in `Services/Chaos/` + `BubbleService.cs` + `ChaosOverlayWindow`/`ChaosHudWindow`/`ChaosFxWindow`. Where `CHAOS_DESIGN.md` and code disagree, code wins (verified below).

Verified ground-truth deltas vs. the brief:
- `ChaosRunConfig` defaults confirmed (DurationSec=180, WaveCount=5, Difficulty=Easy, StartingShields=3, LiveBubbleShare=0.35, DartersEnabled/BoonDraftEnabled=true). **Note:** `LiveBubbleShare` is read into config but I found **no consumer** — the live/benign split is decided entirely by `ChaosBubbleVariants.Pick` weights + `MinIntensity`, not by `LiveBubbleShare`. Flag for systems agent: this knob is currently inert.
- Entry is **not** one-click from the dashboard. `BtnStartChaos_Click` → opens `ChaosHubWindow.ShowDialog()` (modal lobby) → `BtnBegin_Click` → `SaveToSettings()` + `StartRun()`. So a first run is: open Lab → click hero card → configure/Begin → 3·2·1·GO (3s) → play.
- Restart ("Run It Back") **is** effectively one click: `BtnRunAgain_Click` → `OnRunAgain` → `RunAgain()` closes the overlay then `StartRun()` again — it **bypasses the hub**, reusing the last config. Good. But it still re-pays the 3s countdown each time.

---

## 1. Background-play fitness (playing while doing something else)

**Current state — what helps:**
| Helps | Why |
|---|---|
| Bubbles are real top-most click-through windows | desktop stays clickable; pop = a real click on a real window. |
| HUD only paints its left strip; rest is alpha-0 / click-through (`ChaosHudWindow.ApplyExStyles`, no `WS_EX_TRANSPARENT` but unpainted region passes through) | doesn't block work area. |
| Countdown + FX overlays are `WS_EX_TRANSPARENT | WS_EX_NOACTIVATE` | never steal focus or eat clicks. |
| `ShowActivated=false`, `Focusable=false`, `WS_EX_NOACTIVATE` on every chaos window | foreground app keeps focus. |
| Manual pause freezes field+fuses (`ToggleManualPause` → `SetChaosFrozen`) | step away safely. |

**What hurts background play:**
| Hurts | Detail |
|---|---|
| **Boon draft is modal + focus-stealing** | `ShowBoonDraft` → `SetClickThrough(false)` + `BringToFront()` (`Topmost` toggle + `Activate()` + `Focus()`). Mid-task, every wave boundary yanks focus and dims the screen. With default 5 waves that's **4 forced interrupts** per run. |
| Detonation payloads ARE the real effects | a detonated `video`/`htlink` opens a mandatory video / fullscreen browser over your work. By design, but jarring if you're "barely watching." |
| Countdown friction on every (re)start | 3s × 4 steps @750ms before play; unskippable. Hurts compulsive re-runs most. |
| No "auto-pick / idle-skip" for the draft | if you don't react, the field stays frozen indefinitely ("field frozen — take your time") — run stalls instead of continuing. |

**Proposal (loop tightness for background play):**
- Add a config flag **`DraftAutoResumeSec`** (default 0 = off; e.g. 8s): if no pick, auto-take the skip (+1 shield) and resume, so an unattended run keeps flowing.
- Add **`QuickDraft`** mode: a thin non-modal draft strip docked to the HUD instead of the centered modal+backdrop — no focus steal, click-through elsewhere. Keep the full modal as the default for foreground play.
- Make the GO countdown **skippable on click/keypress**, and skip it entirely on `RunAgain()` (see §3).
- Open question for human: should detonations of `video`/`htlink` be **suppressible** in a "background/ambient" run profile (downgrade to flash), or is the intrusion the point?

---

## 2. Best default run length

**Current:** 180s / 5 waves → 36s/wave. Intensity ramps `0→1` linearly over the full 180s; spawn interval `(1300 − intensity·850)/diffMult` (min 280ms); concurrent cap `(4 + intensity·7)·√diffMult`. So the field only reaches peak density in the last ~30s.

**Recommendation: keep a 180s ceiling option but drop the *default* to `120s / 4 waves` (30s/wave).**
Justification:
- Compulsive "one more run" loops want a sub-2-minute commit. At 180s Easy the first ~90s are sparse (low intensity → long spawn intervals, small caps), so the engaging stretch is back-loaded; 120s front-loads more of the ramp into the played time.
- 4 waves = **3 draft interrupts** instead of 4 — less focus-steal per run (§1), still enough boon stacking to feel build-y.
- `FromSettings()` clamps 60–900 and the hub already offers 120/180/300 segments, so this is a default change only (`AppSettings.ChaosRunDurationSec` + `ChaosWaveCount` seed + the hub's `LoadDefaults`), not a range change.
- Keep 180/5 as the "Standard" preset and expose 300/6 as "Long." The Easy/180 combo is fine for a *first* run but is not the best *default* for repeat sessions.

Open question: tie default wave count to length (wave ≈ 30s) so 120→4, 180→6, 300→10 auto-derive, instead of a fixed 5 regardless of length (currently 300s/5 = 60s waves, which feel slack).

---

## 3. Friction map: start → run → results → restart

| Step | Current | Friction | Tightest proposal |
|---|---|---|---|
| Enter | Lab hero card → modal hub (`ShowDialog`) | full config wall before first play | Add **"Quick Start"** button on the hero card that calls `StartRun()` directly with saved settings, skipping the hub. Hub stays for tuning. |
| Countdown | 3·2·1·GO, 4×750ms = 3s, unskippable | repeated on every run/restart | Make click/key **skip-to-GO**; on `RunAgain()` use a **1s "GO!" flash only** (config already loaded, player is primed). |
| Run | timers @250/800ms | fine | — |
| Wave boundary | modal draft, focus-steal, field cleared | §1 | non-modal QuickDraft + auto-resume option. |
| Results | `ShowResults` modal, interactive | fine; shows reached act/wave, best combo, defused/detonated/effects, base×skill=XP | add a one-line **delta vs. last run / best** (needs `ChaosMeta` PB) for compulsion hook. |
| Restart | `BtnRunAgain` → `RunAgain()` → close overlay → `StartRun()` | **good, one click, reuses config**, but re-pays 3s countdown + rebuilds HUD/overlay/FX windows | Keep windows alive across `RunAgain` (don't `Close()` overlay+HUD, just reset state) → instant restart; skip countdown to 1s. |

**Verdict:** restart path is already the strongest part of the loop. The two cheap wins are (a) skippable/short countdown on re-run, and (b) a "Quick Start" that bypasses the hub on first entry.

---

## 4. Moment-to-moment satisfaction & missing juice

**Satisfying now:**
- Pop/defuse → pooled pop SFX + burst/sparkle animation (reused from ambient game).
- `ChaosFxWindow.Pulse` colour-vignettes: green defuse, blue shield-save, red malus, gold combo-milestone — cheap, instant, reads as impact.
- Darter: telegraph flare → throb → bounce-punch on each wall hit → slow-mo on catch (icy blue pulse). Freeze: frost-burst + held edge glow + per-bubble aura + whole-field shudder. These two are the juiciest beats.
- Screen-shake scaled by strength on detonation (absorbed vs. unshielded distinct shake).

**Missing / weak juice (gaps):**
| Gap | Detail |
|---|---|
| **Benign pop has no FX pulse** | `OnBenignPopped` does score+combo+SFX but **no `Pulse`** — the most frequent action is the least juicy. Add a tiny pink/white pulse (low strength). |
| **No near-miss telegraph** | fuse ring shrinks+reddens, but there's no escalating cue (audio tick, pulse, edge-flash) in the last ~800ms before `Detonate()`. A "danger" telegraph would make defuses feel earned. |
| **Combo escalation is flat between milestones** | only every 10 gets a gold pulse + bark. +1 combo is silent. Add rising pop-pitch / micro-pulse that builds, and a distinct **big-combo threshold** (e.g. 25/50) beyond the every-10 milestone. |
| **No shield gain/loss feedback** | `Shields` changes (skip → +1, absorb → −1, `extra_shield` boon → +2) only push a text event; no pulse/sound/HUD flash. |
| **Wave-clear cue is audio-only** | `ChaosSfx.PlayWaveClear()` + `PopAllBubbles()` burst, but no screen pulse or "WAVE N" banner moment. |
| **Heat is invisible as a feel** | `HeatMult` (up to ×2) drives score but has no rising visual/audio temperature; players can't feel they're "hot." |
| **Detonate-absorbed vs unshielded** | distinct internally (blue vs red pulse, shield cost) but **fires the same bark** (`NotifyChaosBubbleDetonated`) — barks can't tell a clutch save from a real hit (see §5). |
| Screen-space readability | live vs benign distinguished by tint + fuse ring + glyph; fine, but during dense late-game with many top-most windows + payload overlays, the fuse ring can be lost. Consider a brighter "armed" outline at low fuse. |

---

## 5. CANONICAL EVENT SURFACE (the deliverable)

Conventions: **Hook** = an existing bark `Notify*` and/or score/juice path fires today. **GAP** = no hook exists; an agent must add a `Notify*` (GamificationBridge `Raise` pattern — no new bus) and/or juice. Firing site is the method in `ChaosModeService` unless noted. All bark events must respect the no-fallthrough priority matcher; intersectional rules (e.g. clutch-save) must outrank generic detonate on the shared trigger.

| Event | Fires when | Payload / context fields | Current hook | Status |
|---|---|---|---|---|
| `RunCountdownBegin` | `ShowCountdown` start (3·2·1) | difficulty, duration, waveCount | none | **GAP** (bark for "here we go") |
| `RunGo` | countdown `onComplete` → `BeginRun` | difficulty | `NotifyChaosRunStarted(difficulty)` fires here (at GO, not at countdown begin) | Hook (named "run started") |
| `BenignPop` | `OnBenignPopped` | variantId, payload(DisplayName), strength, combo | score+combo+SFX; **no Pulse, no bark** | **GAP** (bark + add pulse) |
| `Defuse` | `OnDefused` | variantId, payload, strength, combo | `NotifyChaosBubbleDefused()` + green Pulse + SFX | Hook |
| `DetonateAbsorbed` | `OnDetonated`, `Shields>=cost` | payload, strength, shieldsLeft, doubleOrNothing | blue Pulse + shake; **bark shares `ChaosBubbleDetonated`** | **GAP** (needs own trigger/field to distinguish clutch save) |
| `DetonateUnshielded` | `OnDetonated`, no shields | payload, strength, comboBroken(true) | red Pulse + shake; `NotifyChaosBubbleDetonated(payload)` | Partial (bark can't tell from absorbed) |
| `DarterCatch` | `OnDarterCaught`, `quick=false` | points, combo, slowMo(true) | score+combo+icy Pulse+SFX+slow-mo; **no bark** | **GAP** (bark) |
| `DarterCatchQuick` | `OnDarterCaught`, `quick=true` | points(+90 bonus), combo | stronger icy Pulse; **no bark** | **GAP** (bark; outrank normal catch) |
| `FreezeCatch` | `OnFreezeCaught` | points, combo | frost-burst+edge-hold+field freeze+SFX; **no bark** | **GAP** (bark) |
| `ComboTick` | every `Combo++` (all catch/pop/defuse) | combo, comboMult | **silent** between milestones | **GAP** (rising pop-pitch / micro-pulse) |
| `ComboMilestone` | `CheckComboMilestone`, combo%10==0 | combo | `NotifyChaosComboMilestone(combo)` + gold Pulse | Hook |
| `ComboBig` | combo crosses a high threshold (e.g. 25/50) | combo, threshold | none | **GAP** (distinct big-combo bark + bigger juice) |
| `ComboBroken` | `OnDetonated` unshielded sets `Combo=0` | lostCombo (prev value) | folded into detonate; combo silently zeroed | **GAP** (loss sting + bark) |
| `HeatFull` | `Heat` reaches 1.0 (`OnBenignPopped`/`OnDefused` add) | — | none (no edge crossing detected) | **GAP** (bark + "on fire" visual) |
| `ShieldGained` | skip(+1), `extra_shield` boon(+2), future regen | amount, shieldsNow, source | text event only | **GAP** (pulse/sound + maybe bark) |
| `ShieldLost` | `OnDetonated` absorb path | amount, shieldsNow | text event + blue pulse | **GAP** (distinct shield-break cue) |
| `WaveClear` | `BeginWaveTransition` (draft path) | waveJustCleared | `PopAllBubbles()` + `PlayWaveClear()` SFX; `NotifyChaosWaveEscalated(newWave)` | Partial (no screen pulse/banner; bark is "escalated" not "cleared") |
| `WaveChange` | `WaveIndex` advances (draft + no-draft paths) | wave, act | `NotifyChaosWaveEscalated(wave)` | Hook |
| `ActChange` | `ActIndex` advances (`1+(wave-1)/5`) | act, wave | none (act updated silently alongside wave) | **GAP** (act-up bark = bigger moment than wave) |
| `BoonDraftShown` | `ShowBoonDraft` | wave, options[] (ids/rarities), choiceCount | reveal SFX per card; **no bark** | **GAP** (anticipation bark) |
| `BoonPicked` | `OnBoonChosen`, boon!=null | boon(Name), id, rarity, isCurse | `NotifyChaosBoonPicked(Name)` + pick SFX | Hook (but doesn't distinguish curse) |
| `CursePicked` | `OnBoonChosen`, boon.IsCurse | boon, id, rarity, runMultBonus | shares `ChaosBoonPicked` | **GAP** (own trigger; "risk taken" bark should outrank generic pick) |
| `BoonSkipped` | `OnBoonChosen`, boon==null | shieldsNow (+1) | text event + `Shields+=1`; **no bark** | **GAP** (bark + shield-gain juice) |
| `SlowMoStart` | `ActivateSlowMo` (darter catch) | durationSec(5), factor | time-scale + duration-mult + icy pulse | Hook (juice); **no bark** → GAP |
| `SlowMoEnd` | `EndSlowMo` (timer/teardown) | — | restores scale | **GAP** (no end cue) |
| `FreezeStart` | `ActivateFreeze` (freeze catch) | durationSec(3.5) | frost-burst + edge-hold + field freeze + shudder | Hook (juice); **no bark** → GAP |
| `FreezeEnd` | `EndFreeze` | — | edge-hold fade | **GAP** (thaw cue) |
| `ManualPause` / `ManualResume` | `ToggleManualPause` | paused(bool) | text event + field freeze | **GAP** (optional bark) |
| `RunEnd` | `EndRun` (elapsed≥duration) or `RequestStop` | baseXp, skillMult, finalXp, defused, detonated, effectsFired, bestCombo, reachedAct/wave | `NotifyChaosRunCompleted((int)finalXp)` + meta award | Hook (no field distinguishing natural end vs user-stop) |
| `ResultsShown` | `ShowResults` | same as RunEnd + PB delta (proposed) | modal results UI; **no bark** | **GAP** (bark over results; PB delta is new data) |
| `RunAgain` | `BtnRunAgain_Click` → `RunAgain()` | — | closes+restarts (reuses config) | **GAP** (bark "again?") |
| `RunAbandoned` | `OnOverlayClosed` while `_spawning` (closed mid-run) | elapsed, wave | tears down, **no payout, no bark** | **GAP** (distinct from RunEnd) |

**Surface notes for hooking agents:**
- Detonate currently collapses three semantically different outcomes (clutch absorb / real hit / combo break) into one bark trigger. Split into `DetonateAbsorbed` + `DetonateUnshielded` (+ derive `ComboBroken`) so the no-fallthrough matcher can prioritize the clutch-save line above the generic miss.
- BoonPicked/CursePicked and the SlowMo/Freeze power-ups already have **juice** but **no bark** — easiest high-value bark additions.
- All new `Notify*` methods must be field-level additive on `BarkService` using the existing `Raise(trigger, ctx => ctx.Set(...))` seam. No generic event bus (guardrail).
- ComboTick/HeatFull have **no edge detection today** — adding them means the meta/juice agent must track previous value to fire on crossing, not every frame.

---

## Open questions for the human
1. Default length: OK to drop default to **120s/4 waves** and auto-derive wave count from length (~30s/wave)? Keep 180/5 as a named "Standard" preset.
2. Background profile: should `video`/`htlink` detonations be downgradable to flash in an "ambient/background" run mode?
3. Draft interrupt: ship **non-modal QuickDraft + auto-resume** as default, or keep the centered modal default and make QuickDraft opt-in?
4. Restart: keep HUD/overlay/FX windows alive across `RunAgain` for instant restart (more state to reset, risk of leak) vs. current close-and-rebuild (clean but re-pays 3s)?
5. `LiveBubbleShare` is **inert** — wire it into the variant picker (bias live weights), or remove the knob? (systems-agent territory; flagged here because it affects loop difficulty feel.)
