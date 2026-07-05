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
| Q1 | DONE | **Chaos S5 — draft/boon pool extraction.** ✅ Landed this commit (smart-model): Core `ChaosDraftPool` (+22 tests, Core 448), full AvaloniaChaosService draft port, port-parity-auditor SHIP (30/30 Verified). Smoke gate had been blocked by a pre-existing audio-ducking crash loop, fixed in `8f4db7ca`, not S5. See plan §S5 evidence row.<br/>ORIGINAL SPEC: Core `ChaosDraftPool` from WPF BeginWaveTransition/OnBoonChosen/ResumeAfterDraft/TakenBoonIds/RerollDraft. NOTE: `SyncKnobsFromState()` already routes every boon effect live; call it after any new ApplyBoon path you add. OnBoonChosen is state-mutating: re-read the WPF cites twice before writing. | `docs/chaos-run-engine-port-plan.md` §S5 (WPF ChaosModeService.cs:1448-1487, 1531-1610, 1620-1660, 1510, 1519) | M | Medium |
| Q2 | DONE | **Chaos S6 — payload firing.** ✅ Landed this commit: single payload-factory map, FireScaledPayload/FirePayloadForDetonation/heavy gate/15s video cap ported (Core 450); P3 row CLOSED (per-instance `EffectPayload.Ambient` gates `ArmRandomSegment`) + S1 EXTRA-1 ambient Build branch closed. port-parity-auditor SHIP (8/8 Verified). See plan §S6 evidence row.<br/>ORIGINAL SPEC: FireScaledPayload/FirePayloadForDetonation/HeavyEffectActive/video cap/StingerForVariant + ambient Build branch. | plan §S6 (WPF ChaosModeService.cs:2196-2260, 2192, 1050-1076, 2247; ChaosBubbleVariants.cs:714, 767-773) | M | Medium |
| Q3 | DONE | **Chaos S7 — run lifecycle + economy.** ✅ Landed this commit: NEW pure Core `ChaosEconomy` (+17 tests, Core 467), EndRun/CleanupAfterRun/sentinel/panic/ForceShutdown/AwardLoopTip ported. All 3 P0 invariants verified by port-parity-auditor SHIP — XP-pre-multiplier trap fixed at root, sentinel Clear both sites, meta atomic. See plan §S7 evidence row.<br/>ORIGINAL SPEC: EndRun/CleanupAfterRun/crash sentinel/panic/ForceShutdown/AwardRunRewards into Core `ChaosEconomy`. HARD INVARIANTS: XP paid PRE-multiplier (P0-3); sentinel Clear at BOTH EndRun and CleanupAfterRun (P0-5); chaos_meta.json schema-2 additive atomic save (P0-4). State-mutating: gates + extra care. | plan §S7 (WPF ChaosModeService.cs:3122-3200, 3227-3274, 860, 1048, 285, 3085; ChaosUpgrades.cs:495-521) | L | High-care |
| Q4 | DONE | **Chaos S8 — layer callers.** ✅ Landed this commit: NEW pure Core `ChaosBubbleHints` (KeyFor ladder + TextFor lexicon verbatim from WPF, +46 tests, Core 513); pop-text score floaters wired via `ShowChaosPopText` (treat/prism/defuse pop sites), player SnapRipple at CastRippleWave, hint-marks persisted to `ChaosMeta.State.BubbleHintsLearned`. `--verify-layers` exit 0. Honest frozen-zone STOPs (hint-pill display, residue/tether/trail per-frame visuals need engine state exposure) filed as follow-up rows. See plan §S8 evidence row. | plan §S8 | S | Low |
| Q5 | DONE | **Chaos S9 — verification sweep.** ✅ `--max-benchmark` runs clean (exit 0, all 4 phases incl. Phase 3 heavy chaos, FPS report written) at AvgFPS ~158-161 / peak 203-217, AvgCPU ~20%. User watched the run and confirmed "works as expected" (visual eyeball done); the shutdown segfault is fixed and the benchmark overlays were dropped to 10% + a purple-blue brain-drain-at-100% blackout was fixed. Chaos port S5-S9 complete. Older detail: **Chaos S9 — verification sweep.** `--max-benchmark` 2026-07-05 (re-run after the teardown fix): 4-min max session incl. Phase 3 heavy chaos ran to COMPLETION, exit 0, FPS report written. **Chaos port S5-S8 VALIDATED under max load**: AvgFPS=157, MaxFPS=206, AvgCPU=19.8%, PeakCPU=57.4% (>> aim 60 / floor 30). The shutdown segfault that blocked FPS is FIXED (VideoLayer WaitForTeardown drain; UCE plan Phase E follow-up now RESOLVED); the attention-check benchmark fix lets the video play full duration. STILL OPEN: only the side-by-side-vs-WPF **human eyeball** (inherently the user's). PERF-WATCH: 157 avg is ~12% under the stored 178 optimized reference (not apples-to-apples). | plan §S9 | S | Low |
| Q6 | DONE | **Autonomy slider clamp on load** ✅ Landed this commit: `BambiTakeoverTabViewModel.LoadFromSettings` now clamps + writes back the 4 persisted autonomy values (intensity 0-100, cooldown 10-300, interval 60-600, announcement 0-100) BEFORE assigning to the VM, mirroring WPF MainWindow.Settings.cs:187-193 (#485). Bounds match BambiTakeoverTabView.axaml sliders. | task board "Sync-from-main 6.2.8" S7 | XS | Low |
| Q7 | DONE | **Tray restore off-screen re-center** ✅ Landed this commit: `App.axaml.cs` `RestoreMainWindow` now calls a new `EnsureOnScreen(window)` that re-centers on primary when <60x30 px of the window overlaps any `Screens.All` working area, mirroring WPF TrayIconService.EnsureOnScreen:191 (#475). Grounded every Screens API in proven in-repo usage; coordinate note: Avalonia Position is physical px (no scale), Bounds size is DIP (*RenderScaling), WorkingArea is physical px. | task board S5 | XS | Low |
| Q8 | BLOCKED (STOP fired) | **Core loc mirror decision** ❌ Precondition FAILS - do NOT `git rm -r CCP.Core/Localization`. Verified: the WPF folder IS Content-linked (CCP.Core.csproj:50 `..\Localization\Languages\*.json`), BUT `CCP.Core/Localization/` is NOT a pure mirror - it contains live code `LocalizationManager.cs` (defines `ConditioningControlPanel.Core.Localization.LocalizationManager`, singleton Instance) + `Loc.cs`, compiled by CCP.Core (only PortableModels is `Compile Remove`) and used across the whole Avalonia head (App.axaml.cs + many dialogs/views - grep `LocalizationManager`/`Core.Localization`). The spec's folder-path grep was a false negative (usages are by class/namespace, not path). Deleting the folder breaks the build. RE-SCOPE NEEDED (smart model): the only possible redundancy is the physical `CCP.Core/Localization/Languages/*.json` (9 files) vs the Content-linked WPF JSONs - verify what LocalizationManager loads at runtime before pruning ANY json; the CODE must stay. | task board S8 | XS | Low |
| Q9 | ready | **Flaky cross-thread brush crash hunt** (follow-up row). Symptom: rare unhandled `InvalidOperationException` in `SolidColorBrush.SerializeChanges` -> `Compositor.CommitCore`. Hunt: grep CCP.Avalonia (excluding Compositor/) for assignments to `.Brush`, `.Fill`, `.Foreground`, `.Background`, `SolidColorBrush` inside `Task.Run`, timer callbacks, or event handlers that are not dispatcher-posted; wrap the mutation in `Dispatcher.UIThread.Post`. Fix ONLY clear off-thread sites; if none found, downgrade the row to "monitor" and stop. | port plan Follow-up rows | S | Medium |
| Q10 | ready | **E-Stim + 5 remaining passive overlays as compositor layers** (EStimGlow, EStim bolts, WaveTimer, VibeTrail, FxWindow vignette, SkiaFxOverlay). One overlay per commit. Use `unified-compositor-engine` skill; new Layer classes are allowed, Compositor internals are not. After EStim layers exist, wire the S4b-4 deferred arc visuals (engine already fires the pops; only visuals/audio cues missing) and the E-Stim visual chain callers row. | port plan Follow-up rows + UCE plan queue | L | Medium |
| Q11 | DONE | **Chaos gameplay bark members** ✅ Landed: 9 barks (the 4 named + EndingSoon/TeaseDenied/TeaseDeniedStreak/ComboBig/ComboMilestone), all WPF-cited (BarkService.cs:275-318, ChaosModeService callers). Added as DIM no-ops on IBarkService (CCP.Core/App.cs), raised at the gameplay sites in AvaloniaHeadStubs.cs mirroring the existing `=> RaiseBark("chaos.xxx")` pattern; AvatarTube OnBarkRequested already handles any kind generically (no subscriber edit). Gates: slnf 0, WPF 0, Core 513, smoke clean. | this row + cited comments | S | Low |
| Q12 | ready | **ChaosHud glow effects**: three flat-rendering sites where WPF has DropShadowEffect glows. Convert to Avalonia BoxShadow. Run `avalonia-research` on BoxShadow first. | `CCP.Avalonia/Chaos/ChaosHudWindow.axaml.cs:377,388,547` | S | Low |
| Q13 | ready | **ModCreatorWindow help/resource TODOs (board M-10) - now UNBLOCKED**: `HelpContentService` exists in Core and `AvaloniaModResourceResolver` is DI-registered; wire the remaining TODO sites. | task board :339 (M-10) | S | Low |
| Q14 | ready | **Simplification pass** (2026-07-04 audit; one sub-step per commit, `--fast` gates between, FULL gates before each commit): (a) fix 2 misleading comments: ServiceCollectionExtensions.cs ~:253 "stubs" -> "services", AvaloniaChaosStubs.cs region "static service stubs" -> "static facades over DI services"; (b) split `AvaloniaHeadStubs.cs` (2741 lines, zero stubs) into `AvaloniaChaosService.cs` (1-2512 incl. nested DisposableAction), `AvaloniaAvatarWindowService.cs` (2515-2680), `AvaloniaBarkService.cs` (2682-2718), `AvaloniaMainWindowAdapters.cs` (2720-2741); pure moves, same namespace; then sweep doc refs: task-board :75,:85, avalonia-current-state.md:11, ChaosSpawnCatalog.cs:13 comment; (c) split `AvaloniaChaosStubs.cs` (1186 lines) into `ChaosEnums.cs` (17-75), `ChaosModels.cs` (77-544), `ChaosCatalogData.cs` (633-1173 minus facades), `ChaosStaticFacades.cs` (ChaosMeta :548, RevealService :606, AvaloniaChaosApp :1177); sweep task-board :92, avalonia-current-state.md:18,59,86 (replace stale line-number cites with class-name cites); (d) delete 3 grep-proven dead files: `CCP.Avalonia/Chaos/AvaloniaOutlinedText.cs`, `CCP.Avalonia/Helpers/ScreenWindowHelper.cs`, `CCP.Core/Services/Awareness/IAppClusterMap.cs` (re-verify each with `grep -rlw <ClassName>` over *.cs/*.axaml first; only defining file may appear). | this row | M | Low |
| Q15 | ready | **Screen-shake seam** (gap sweep #1; seam pre-designed, execution mechanical). WPF: `Services/UI/ScreenShakeService.cs` `Shake(double intensity, int durationMs)` shakes root FrameworkElements via TranslateTransform (NOT window positions); callers: Deeper IActionDispatcher.cs:233-234 (`screen_shake` action), ChaosModeService.cs:1680-1684 (gated by `ScreenShakeEnabled`/`ShakeIntensity` settings). Port design: (1) new `CCP.Core/Platform/IScreenShakeService.cs` with `void Shake(double intensity, int durationMs)` + no-op default registration in `CCP.Avalonia/ServiceCollectionExtensions.cs`; (2) Avalonia impl `AvaloniaScreenShakeService`: apply a `TranslateTransform` jitter to MainWindow content root via `DispatcherTimer` (mirror WPF decay curve; read WPF :43-120 for amplitude/decay), restore prior transform on finish, never touch overlay windows; (3) wire the three dropped call sites: IActionDispatcher.cs:271 screen_shake action, AvaloniaHeadStubs chaos Shake() sites :1285,:1350, AvaloniaBubbleService.cs:324; respect `ScreenShakeEnabled`/`ShakeIntensity`. Use `avalonia-research` for RenderTransform-on-Window-content specifics. Core test for intensity/duration clamps. | this row + WPF cites | M | Medium |
| Q16 | ready | **AvatarTube `ContentViewbox_SizeChanged` stale TODO**: empty handler at `CCP.Avalonia/AvatarTube/AvatarTubeWindow.axaml.cs:409` referencing WPF manual sizing. Decide: obsolete in Avalonia (no layered-window hazard) -> delete handler + wiring; or port the WPF logic. Read WPF AvatarTubeWindow sizing first; if unsure after reading, STOP and file blocker. | this row | XS | Low |

## Section 2 — Improvements and additions (rated)

Do these only when Section 1 is drained or the user asks. Rating: MECHANICAL = safe
for this queue's discipline; SMART = needs the planning model; HUMAN = needs the owner.

Done by the smart model 2026-07-04: Avalonia matrix bumped to 12.0.5 + DataGrid 12.0.1
(commit `68879b3f`, all gates green); `ConditioningControlPanel/tools/run-gates.sh` gate runner added; codebase
conventions written into the `mechanical-port-work` skill.

| Idea | Rating | Notes |
|---|---|---|
| FlashImageCache re-key to path-only (#486 second half) | SMART | Interacts with the ref-count release-on-replace contract; prerequisite of the perf-tier port. Board row S1 has the analysis. |
| Performance-tier system port (WPF PerformanceProfile: per-hardware-tier caps for particles, decode dims, BrainDrain factors) | SMART | Workstream; needs slicing. Gap sweep found consumers waiting at BrainDrainLayer.cs:22, AvaloniaOverlayService.cs:741, FlashImageCache.cs:42. Flash cache re-key (row above) is its stated prerequisite. |
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
