# Model Handoff Queue

The single ordered work queue for continuing the Avalonia port with a mechanical-tier
model. **Load the `mechanical-port-work` skill before taking any item.** Take the top
unblocked item unless the user names one. Status values: `ready` | `in-progress` |
`done <commit>` | `BLOCKED (see ledger)`.

Written 2026-07-04 by the planning model after S4b-4 (commit `2d7bc384`). State at
handoff: chaos ENGINE work is complete (S1-S4b, Core tests 426/426, all gates green);
WPF 6.2.8 merged with #480/#483 parity fixes landed; port heads at 6.2.8.

---

## Section 1 — Ready-now queue (in order)

| # | Status | Task | Spec lives at | Effort | Risk |
|---|---|---|---|---|---|
| Q1 | ready | **Chaos S5 — draft/boon pool extraction.** Core `ChaosDraftPool` from WPF BeginWaveTransition/OnBoonChosen/ResumeAfterDraft/TakenBoonIds/RerollDraft. NOTE: `SyncKnobsFromState()` already routes every boon effect live; call it after any new ApplyBoon path you add. OnBoonChosen is state-mutating: re-read the WPF cites twice before writing. | `docs/chaos-run-engine-port-plan.md` §S5 (WPF ChaosModeService.cs:1448-1487, 1531-1610, 1620-1660, 1510, 1519) | M | Medium |
| Q2 | ready | **Chaos S6 — payload firing.** FireScaledPayload/FirePayloadForDetonation/HeavyEffectActive/video cap/StingerForVariant + ambient Build branch. | plan §S6 (WPF ChaosModeService.cs:2196-2260, 2192, 1050-1076, 2247; ChaosBubbleVariants.cs:714, 767-773) | M | Medium |
| Q3 | ready | **Chaos S7 — run lifecycle + economy.** EndRun/CleanupAfterRun/crash sentinel/panic/ForceShutdown/AwardRunRewards into Core `ChaosEconomy`. HARD INVARIANTS: XP paid PRE-multiplier (P0-3); sentinel Clear at BOTH EndRun and CleanupAfterRun (P0-5); chaos_meta.json schema-2 additive atomic save (P0-4). State-mutating: gates + extra care. | plan §S7 (WPF ChaosModeService.cs:3122-3200, 3227-3274, 860, 1048, 285, 3085; ChaosUpgrades.cs:495-521) | L | High-care |
| Q4 | ready | **Chaos S8 — layer callers.** ChaosBubbleHints + wire existing `ShowChaosPopText`/`ChaosFieldRipple`/`SnapRipple`/`SetTether` seams. Verify with `--verify-layers` (expect 15/15). Callers only; Compositor internals are forbidden. | plan §S8 | S | Low |
| Q5 | ready | **Chaos S9 — verification sweep.** Full run side-by-side vs WPF; `--benchmark` during heavy chaos (60fps target / 30 floor); update task-board row + parity matrix chaos rows + goal doc. Human may need to eyeball; prepare evidence and ask. | plan §S9 | S | Low |
| Q6 | ready | **Autonomy slider clamp on load** (6.2.8 row S7). Add `Math.Clamp` when assigning persisted values in `BambiTakeoverTabViewModel` load (~line 321); clamp to each slider's min/max as defined in the corresponding AXAML. Write the clamped value back to settings like WPF does (MainWindow.Settings.cs, 6.2.8 diff). | task board "Sync-from-main 6.2.8" S7 | XS | Low |
| Q7 | ready | **Tray restore off-screen re-center** (6.2.8 row S5). In `App.axaml.cs` `RestoreMainWindow` (~line 551): if less than 60x30 px of the window intersects any `Screens.All` working area, re-center on primary. Mirror WPF TrayIconService `EnsureOnScreen` (6.2.8 diff). Use `avalonia-research` skill for the Screens API before coding. | task board S5 | XS | Low |
| Q8 | ready | **Core loc mirror decision** (6.2.8 row S8). VERIFY first: `grep -rn "Localization/Languages" ConditioningControlPanel/CCP.Core/CCP.Core.csproj` shows the WPF folder is Content-linked (line ~50) and `grep -rn "CCP.Core/Localization" --include=*.cs* -r ConditioningControlPanel` finds no code reference to the LOCAL folder. If both hold, `git rm -r ConditioningControlPanel/CCP.Core/Localization` and note it in AGENTS.md's version-bump list. If either fails: STOP, file blocker. | task board S8 | XS | Low |
| Q9 | ready | **Flaky cross-thread brush crash hunt** (follow-up row). Symptom: rare unhandled `InvalidOperationException` in `SolidColorBrush.SerializeChanges` -> `Compositor.CommitCore`. Hunt: grep CCP.Avalonia (excluding Compositor/) for assignments to `.Brush`, `.Fill`, `.Foreground`, `.Background`, `SolidColorBrush` inside `Task.Run`, timer callbacks, or event handlers that are not dispatcher-posted; wrap the mutation in `Dispatcher.UIThread.Post`. Fix ONLY clear off-thread sites; if none found, downgrade the row to "monitor" and stop. | port plan Follow-up rows | S | Medium |
| Q10 | ready | **E-Stim + 5 remaining passive overlays as compositor layers** (EStimGlow, EStim bolts, WaveTimer, VibeTrail, FxWindow vignette, SkiaFxOverlay). One overlay per commit. Use `unified-compositor-engine` skill; new Layer classes are allowed, Compositor internals are not. After EStim layers exist, wire the S4b-4 deferred arc visuals (engine already fires the pops; only visuals/audio cues missing) and the E-Stim visual chain callers row. | port plan Follow-up rows + UCE plan queue | L | Medium |

## Section 2 — Improvements and additions (rated)

Do these only when Section 1 is drained or the user asks. Rating: MECHANICAL = safe
for this queue's discipline; SMART = needs the planning model; HUMAN = needs the owner.

| Idea | Rating | Notes |
|---|---|---|
| FlashImageCache re-key to path-only (#486 second half) | SMART | Interacts with the ref-count release-on-replace contract; prerequisite of the perf-tier port. Board row S1 has the analysis. |
| Spanker toy port (arms `ChaosKnobs.SpankerOn`) | MECHANICAL | Engine side is DONE (S4b-3/4: spank routing, growth, redirect, mow sweep all live). Remaining: the toy acquisition/activation UI + per-run arming in AvaloniaChaosService. WPF cites in plan Follow-up rows. |
| Avatar quick-menu engine-stop + `IVideoService.IsStrictActive` (#479) | MECHANICAL | Board row S4 has exact guards; resolve the Engine-menu-item semantics divergence noted there. |
| Glitch trigger-bubbles port (with `DurationMult=1.2` exemption) | SMART | Whole trigger-bubbles feature is unported; needs a slicing pass first. |
| Corner GIF feature incl. #474 live-update semantics | SMART | Needs a compositor corner layer design (existing TODO in orchestrator line ~309). |
| In-app FPS HUD (debug-only overlay showing compositor frame time) | MECHANICAL | Value: makes the S9/UCE FPS gates checkable without PresentMon. Debug builds only; render as a tiny compositor layer caller or window overlay, NOT inside Compositor internals. |
| Hook click-swallow decision (WP3 JUDGMENT row) | HUMAN+SMART | Product decision about input stealing; do not attempt. |
| Parity matrix re-earning (WS0) | HUMAN | Owner ruling: every checkmark needs side-by-side human verification. Prepare evidence only. |
| Linux head end-to-end exercise (build-linux.sh + manual pass) | HUMAN | Agent can fix what CI catches; a real feature pass needs eyes. |
| Enhanced-videos prompt modal ordering (#481) | MECHANICAL | Only WHEN the Deeper startup prompt gets ported; board row S6 has the contract. |

## Section 3 — DO NOT ATTEMPT (any model)

- Rewriting or "improving" `BubbleEngine.cs`, `ChaosSpawnDirector.cs`, or any S1-S4b
  Core chaos file beyond what a queue item explicitly says. They are WPF-faithful and
  test-pinned; cleverness here is regression.
- Anything inside `CCP.Avalonia/Compositor/**` (new Layer classes in Layers/ are fine
  when a queue item says so).
- WPF-head code changes (`Services/**`, `MainWindow/**`, ...). The WPF head only
  changes via merges from main.
- Changing persisted JSON schemas (settings, chaos_meta, sessions, .ccpenh) or the
  atomic-write patterns.
- Version bumps, release/installer work, startup/single-instance logic: user-driven.
- Adding NuGet packages without the `avalonia-research` skill's vetting step AND a
  user go-ahead.

## Section 4 — Blocked / Questions ledger (append-only)

| Date | Item | What was found | Where (file:line) | Why stopped |
|---|---|---|---|---|
| (empty) | | | | |
