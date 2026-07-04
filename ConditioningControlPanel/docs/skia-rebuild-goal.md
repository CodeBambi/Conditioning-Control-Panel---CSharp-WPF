# SKIA REBUILD GOAL - Windows + Linux, functionality first

Created: 2026-07-02. Status: APPROVED by owner 2026-07-02 — this is the active
autonomous driver. Supersedes `EXECUTION_GOAL.md`
as the active autonomous driver once approved (that doc's goal was declared complete
2026-06-23 and is stale). `unified-compositor-engine-goal.md` and
`unified-compositor-engine-plan.md` remain the detail tracker for Workstreams 1-2 and are
NOT replaced by this file.

## The goal, in one paragraph

Finish rebuilding the Conditioning Control Panel as an Avalonia v12 app whose every
feature WORKS on Windows and Linux: build, launch, and run all current WPF features (or
improved versions of them). Functionality is the contract; the implementation underneath
is not. Old WPF code, old dependencies, and old architectural choices carry zero
sentimental weight: replace anything if the replacement is faster, safer, or simpler,
as long as the user-visible behavior survives or improves. All real-time visuals
(engine mode: session effects; game mode: Chaos) render through the unified Skia
compositor, not per-effect windows.

## What matters and what does not

| Matters (the contract) | Does not matter |
|---|---|
| Every current feature works end-to-end in the running app | Which library/dependency provides it |
| At least as fast and smooth as WPF; low-end machines are a hard requirement | Whether the code resembles the WPF code |
| Windows AND Linux: build, launch, features function | Matching WPF pixel-for-pixel (keep the design language, see `dashboard-design`) |
| Overlays stay tinted glass: user keeps using the PC while conditioning runs | Keeping legacy per-effect windows |
| Privacy/security posture never regresses (see Guardrails) | Preserving old workarounds whose reason died |

"Improved" is explicitly allowed and encouraged: if a feature can be made faster, more
reliable, or more secure with a big change, make the big change. Record what and why in
the task board.

## Rendering doctrine: Skia everywhere

Avalonia v12 already renders ALL controls through Skia; standard Avalonia UI (tabs,
dialogs, dashboard) is therefore already Skia-rendered and stays as Avalonia controls.
The doctrine this goal adds:

1. **Every animated or real-time visual effect renders as a compositor layer** in the
   existing `CompositorEngine` (one topmost click-through window per monitor, z-ordered
   `IAvaloniaLayer`s, one 60Hz tick). No new per-effect `Window`s. Ever.
2. **Engine mode** (session effects: video, flash, subliminal, bouncing text, spiral,
   brain drain, pink tint, bubbles) and **game mode** (Chaos: field FX, DVD, cascades,
   cursor glow, banners, timers, e-stim glow, vibe trails, pop text) both target the
   compositor. Chaos migration is a first-class workstream, not a leftover.
3. Windows that remain windows are the INTERACTIVE surfaces only: main UI, dialogs,
   AvatarTube, lock card, quiz/mantra/HUD-style interactive overlays. Rule of thumb: if
   the user clicks IN it, it may be a window; if it just draws, it is a layer.
4. Custom Skia drawing uses the established v12 primitives (`ICustomDrawOperation` +
   `ISkiaSharpApiLeaseFeature` lease, or `CompositionCustomVisualHandler` for
   render-thread loops). Persistent `SKImage`s, engine-owned invalidation, no per-frame
   `SKBitmap` allocation (see `unified-compositor-engine` skill rules).

## Skills to drive this goal (invoke them; do not re-derive)

| Skill | When |
|---|---|
| `avalonia-research` | MANDATORY before any Avalonia API use, any new dependency, any bug/exception, and every Linux-specific mechanism. Also for finding faster/lighter replacements (that mandate is standing) |
| `port-plan` | Start of every session: read trackers, pick ONE task, claim it, slice it |
| `wpf-parity` | Before implementing: extract the WPF behavior contract; after merging main |
| `port-feature` | The implementation workflow + WPF-to-v12 cheatsheet + verification ladder |
| `unified-compositor-engine` | All compositor/layer/video work (Workstreams 1-3) |
| `overlay-clickthrough` | All window ex-style, hook, hit-test, topmost work; Linux click-through design |
| `dashboard-design` | Any user-facing surface; 5-theme reskin is part of done |
| `port-audit` | End of every workstream and after every merge from main |

## Current state (verified 2026-07-02; re-verify with `port-audit` if this doc is old)

Exists (structurally): full project skeleton builds (`CCP.Desktop.slnf`, 0 errors when
tree is clean); Core tests green; effect services (flash, subliminal, bouncing text,
pink tint, spiral, brain drain, bubbles) render as compositor layers; Avalonia measured
faster than WPF on startup (~2.5s vs ~4.2s) and memory (~422MB vs ~1218MB); CI-style
Linux/macOS builds exist as a workflow file (not active on GitHub).

**Trust level: NONE. Owner's ruling (2026-07-02): the port was built largely by hand and
no prior verification claim is trusted, including the 2026-06-23 parity-matrix sweep and
every `[x]` in `avalonia-ui-parity-matrix.md`. Treat the ENTIRE port (CCP.Core,
CCP.Avalonia, all heads) as unverified until it passes WS0.**

Open (this goal's actual work):
- The whole port needs review and re-verification (WS0). Known-shaky spots to hit early:
  the calibration-overhaul port TRIAGE was resolved in WS0 lot 5 — the earlier BLOCKED STOP
  (stack divergence) was disproven and the core data model + 13 algorithm hunks landed
  (commit 837aaa1d, see docs/avalonia-calibration-overhaul-port.md); the `WebcamCalibrationData.cs`
  "uncommitted WIP" was already committed. The REAL remaining gap is the 16-point calibration
  WINDOW pipeline (sample collection + polynomial fit + persist), which was a fake-success
  shell; lot 5 made it honest ("not available yet") and filed the ~1300-1500 LoC port as its
  own row. Quick-recal and the tracker-test window are now real.
- **Merge sync (main → feat/crossplatform, `5ce70de6`, 2026-07-03):** WPF shipped new
  behavior the port must catch up to; full backlog in the task board under
  "Sync-from-main: merge 5ce70de6". Two VERIFIED P0s re-open closed lots: (1) lot 2 — the
  shared Core session ramp (`SessionService.cs:400,408`, `IntensityRampService.cs:122,128`)
  writes ramped pink/spiral opacity into auto-saving `settings.Current` → data-loss on
  crash (WPF #471/#476 direct-drive + `ReleaseOpacityRampHolds` fix unported); (2) lot 1 —
  `ProfileSyncService` is entirely absent, so the prestige/season-reset policy is missing.
  Remaining-lot scope now also includes: Ditzy Data PRO / Prestige skill-tree analytics
  (lot 7), the #462 interaction-race fix cluster (lots 2/3), #463/#465/#455 companion +
  account integration fixes (lot 8), `SubliminalSolidMode` #461 (lot 3), and animated `.webp`
  (already ~70% covered by `SkiaImageDecoder`/SKCodec — extension-gate broadening only, NO
  new dependency). Core-model deltas (skill nodes, schema-2 recap, prestige field) and the
  new loc keys auto-flow to both heads; only the consuming services/UI need porting.
- UCE video layer does not render; legacy `AvaloniaMultiMonitorVideoService` is the only
  working video path. Audio controls and attention checks bypass the UCE path.
- Chaos overlays (~23 window classes) are not on the compositor.
- Avalonia mouse hook cannot swallow clicks (WPF can): bubble/flash pops leak the click
  to the app underneath. Decide and fix, or explicitly accept and document.
- Linux: head builds and launches in a VM, but there is ZERO click-through code
  (`SupportsClickThrough = IsWindows`), no input hooks, no verified feature sweep.
- Doc drift: several trackers lag the code; fix as encountered.

## Workstreams, in order

### WS0: Verify and correct existing work (the ENTIRE port is unverified)

The port was built by hand. Nothing is assumed correct because it exists, compiles, ran
once, or has a checkmark somewhere. There is NO trusted baseline: prior verification
claims (including the 2026-06-23 sweep) are void. WS0 earns trust area by area, and it
runs FIRST because building WS1+ on unreviewed foundations compounds any mistake.

**Scope:** all of `CCP.Core`, `CCP.Avalonia`, `CCP.Avalonia.Desktop*`, and the WPF-side
seam code (`CCP.WindowsOnly`). First action: reset every row of
`avalonia-ui-parity-matrix.md` to `[ ]` with a note pointing at this goal (the matrix
was reset once before for exactly this reason; repeat it).

**Review lots:** slice the port into area lots and work through them by risk, highest
first: (1) data/settings persistence and paths (data loss is unrecoverable), (2) session
engine + start/stop, (3) overlays/compositor + click-through input, (4) video/audio,
(5) speech/mic + gaze/calibration (lot 5 PASSED 2026-07-03; the calibration port is now
formally re-scoped: core landed, the 16-point window pipeline is filed as its own row), (6) chaos/game mode, (7) progression/quests/economy,
(8) browser/integrations, (9) tabs and dialogs, (10) theming/mods, (11) heads/DI/startup.
A lot is a reviewable unit (one service area or view cluster), small enough to exercise
end-to-end in one session.

**Per review lot, run three checks:**
1. **Correctness vs contract** (`wpf-parity`): extract the WPF behavior for the touched
   feature and exercise the Avalonia side against it in the running app. Any divergence
   is a bug row, fixed before the lot passes.
2. **Adversarial code review** against the skill rubrics: v12 correctness
   (`avalonia-research` + plan section 21 gotchas), compositor doctrine
   (`unified-compositor-engine` rules), overlay input scars (`overlay-clickthrough`),
   DI/seam and ponytail conventions (`port-plan`), theming (`dashboard-design`),
   security/privacy (`port-audit` section 6). Use independent reviewer agents per rubric
   dimension and verify findings before acting; unverified review findings are noise.
3. **Optimality check, proportionate:** flag over-engineering (needless abstractions,
   new deps that did not earn their weight), per-frame allocations, timer sprawl,
   redundant windows that should be layers, and perf regressions (`--benchmark` when the
   lot touches hot paths).

**Correction policy (what "made correct" means):**
- Functional bugs and guardrail violations: fix immediately, in the lot.
- Measurably suboptimal (slower, heavier, less safe, violates a skill rule): fix if the
  change is proportionate; otherwise file a prioritized task-board row with evidence.
- Working but merely unidiomatic/taste-level: leave it (ponytail). Churn is not quality.
- Matrix hygiene: rows only earn `[x]` again through a lot's pass; verification evidence
  (what was exercised, on which head, vs which WPF behavior) goes in the matrix row.
- Triage the BLOCKED calibration-overhaul port explicitly: done in WS0 lot 5 (BLOCKED
  disproven, core landed in 837aaa1d, real gap = 16-point window pipeline filed as a row).
  The "uncommitted WebcamCalibrationData.cs WIP" was already committed before lot 5.

WS0 for an area is done when its lots pass all three checks, corrections are merged, and
the matrix rows for that area are re-verified. Later workstreams may start for an area
once ITS WS0 pass is clean (no need to finish all of WS0 globally first); WS1 (video)
requires lots 2-4 clean since it builds directly on them.

### WS1: Video through the compositor (Windows)
Execute `unified-compositor-engine-plan.md` phases A-E, one unchecked task per
iteration: prove `VideoLayer`/`MandatoryVideoLayer` render, wire audio
(volume/device/mute), rehome attention checks, kill the per-frame `SKBitmap` alloc
(Phase D), verify all layers over video, then flip the default and delete the legacy
video windows (Phase E only after parity is proven by running). Freedom clause applies:
if research shows a better decoder path than LibVLC callbacks (FFmpeg-based, GPU
frames), it may replace LibVLC per-platform, behind the existing seams, with benchmarks.

### WS2: Game mode (Chaos) onto the compositor
Migrate the passive Chaos overlays to layers (field FX, Skia FX, flash, cascades, DVD,
cursor glow, vibe trail, e-stim glow, banners, wave timer, pop text, announcer).
Interactive Chaos surfaces (HUD, boon bar, toy button, unlock card, backdrop, bubbles'
click handling) keep their input model per `overlay-clickthrough` (hook + layer
hit-testing preferred over interactive windows where feasible). Resolve the hook
click-swallow gap here: give `AvaloniaMouseHook` a swallow path (WPF semantics,
including the hold-to-defuse no-swallow exception) or document acceptance. Chaos run
must hold 60fps target / 30fps floor during heavy activity on a low-end machine.

### WS3: Windows completion sweep
`port-audit` over the whole app; every remaining effect-window candidate either becomes
a layer, is justified as interactive, or gets a task-board row. Re-verify the parity
matrix rows invalidated by WS1/WS2. Perf gate: `--benchmark` and `--max-benchmark` not
worse than `docs/benchmark-optimized.json`.

### WS4: Linux bring-up to feature parity
Target: `dotnet build` + launch + full feature sweep on Linux (X11 first; Wayland
best-effort). Per feature: make it work, improve it, or gate it gracefully (never trap
input, never crash). Known mechanisms to research and implement via `avalonia-research`
+ `overlay-clickthrough`:
- Click-through: XShape/XFixes input region on X11 (implement `IOverlaySurface
  .SetClickThrough` for Linux); Wayland input regions where the compositor honors them.
- Global mouse position/clicks for bubbles: evdev/XInput2/XRecord alternatives, or
  fall back to interactive host-overlay input.
- Video: system libvlc (no official Linux NuGet; document required packages), or the
  WS1 replacement decoder if adopted.
- Not portable by nature (wallpaper, WebView2 browser, WASAPI ducking, some hooks):
  research a Linux-native equivalent first (e.g. layer-shell wallpaper, WebKitGTK or
  system-browser flow, PipeWire/PulseAudio ducking); if none is proportionate, degrade
  gracefully and record the gap.
Verification per `docs/linux-vm-testing.md` + `build-linux.sh`; add a Linux column or
section to the parity matrix and sweep every feature there.

### WS5: Better/faster/safer replacements (standing, opportunistic)
Any iteration may propose a replacement (dependency, decoder, IPC, storage, crypto,
browser integration) if research shows a materially faster or more secure option. Rules:
research first, benchmark before/after, keep the seam, one replacement per commit,
record rationale + pin versions in the task board. A replacement that regresses Windows
is reverted, not patched around.

## Loop protocol (how an autonomous session runs this goal)

1. `port-plan`: read this file, the task board, and the UCE plan; check `git status`
   and recent log (parallel WIP exists; a red build may not be yours).
2. Claim ONE task (append-only ledger row, claim commit). If the task builds on an area
   that has not passed WS0 review yet, run that area's WS0 lot first (or as the task).
3. `wpf-parity` for the behavior contract; `avalonia-research` for every API touched.
4. Implement per `port-feature` / `unified-compositor-engine` / `overlay-clickthrough`.
5. Verify: build slnf (`-clp:ErrorsOnly`, 0 errors), Core tests (count never decreases),
   `--smoke-test` (Debug), exercise the feature running side-by-side with WPF, 5-theme
   sweep for UI, multi-monitor for overlays, Linux VM check for WS4 tasks.
6. Update trackers (UCE plan checkboxes, parity matrix, task board, this file's
   "Current state" if it materially changed). Commit `feat(av): ...` / `fix(av): ...`,
   one task per commit, tree green. Then compact per "Context discipline" below.
7. Stop conditions: a change would diverge from WPF behavior (product decision needed);
   research contradicts project code with no safe answer; a guardrail would be crossed;
   or the tree is broken by parallel WIP you do not own.

Commands (from repo root):
```bash
dotnet build ConditioningControlPanel/CCP.Desktop.slnf -clp:ErrorsOnly
dotnet test ConditioningControlPanel/tests/CCP.Core.Tests/CCP.Core.Tests.csproj
dotnet run --project ConditioningControlPanel/CCP.Avalonia.Desktop.Windows/CCP.Avalonia.Desktop.Windows.csproj [-- --smoke-test|--benchmark|--max-benchmark]
dotnet run --project ConditioningControlPanel/ConditioningControlPanel.csproj   # WPF reference
# Linux (in VM, from ConditioningControlPanel/): ./build-linux.sh   (see docs/linux-vm-testing.md)
```

## Context discipline (when to compact, and how to stay cheap)

A bloated context produces worse code, not just bigger bills: constraints scroll out of
attention, half-remembered file contents get edited wrong, and reviews go soft. Treat
compaction as a quality gate. Trackers are the external memory; the transcript is
disposable.

**Compact at these moments:**
1. After every completed task: trackers updated, committed, THEN compact. This is the
   natural boundary; never carry a finished task's context into the next one.
2. After every verification milestone inside a long task (green build, lot check passed).
3. After any large read (a 100KB+ file sliced, the task-board ledger, a WPF archaeology
   dive) ONCE the extracted contract/findings are written into a tracker row. Carry the
   conclusion forward, never the file contents.
4. At ~50-60% of the context window, unconditionally: finish the in-flight edit, write
   down state, compact. Do not push to 80% "to finish the task"; that is where mistakes
   cluster.
5. Before starting a WS0 review lot: reviewers start clean so their judgment is not
   anchored by implementation context.

**Before compacting, write down (in the task board row or the relevant doc):** the
task/lot in progress, the next concrete step, files touched so far, the WPF contract or
research findings extracted, and the exact commands to re-verify. If a build is red,
record why before compacting, never after.

**Never:** compact mid-edit or with unexplained red state; resume after compaction
without re-reading the claimed task-board row and this goal's relevant workstream.

**Token hygiene while working (keeps compaction rare and cheap):**
- Grep for the member, then Read the enclosing range. Never full-read the 100KB+ files
  (the list is in the `wpf-parity` skill); never re-read unchanged files.
- Fan large sweeps (inventories, multi-file reviews, research) out to subagents that
  return structured conclusions; keep raw file dumps out of the main context.
- One claimed task per session where possible; a session that sprawls across tasks pays
  the full context twice and does both tasks worse.
- Write findings into trackers the moment they are established, not at session end;
  anything only in the transcript is one compaction away from being lost.

## Definition of Done

- [~] WS0 complete: the ENTIRE port reviewed lot by lot (contract + adversarial rubric + optimality), corrections merged, the parity matrix re-earned from a full reset with evidence per row, calibration-port blockage resolved or formally re-scoped. Any lot RE-OPENED by a later merge from main (see the task-board "Sync-from-main" backlogs; merge `5ce70de6` re-opened lots 1/2/3/4/6) must be re-closed before WS0 is done.
  - **STATUS 2026-07-04: all 11 review lots have PASSED first-pass** (parity matrix rows 1–11 all `passed`, evidence per row; calibration re-scoped in lot 5). This continuation closed lots 7–11 (progression, browser/integrations, tabs/dialogs, theming/mods, heads/DI/startup) + fixed the lot-2 ramp P0, with the merge `5ce70de6` backlog folded in. **REMAINING before this box can be checked (re-triaged + swept 2026-07-04):** the merge `5ce70de6` re-opens have now been driven down to a SINGLE genuine item. Re-closed this session: **row 4** #462 session-summary defer (`410bef87`); **row 2** #462 interaction-race cluster — ForceReset-before-teardown added to both stop paths + guards 2/3/4 confirmed parity (`4d65e564`); **row 3** animated-.webp gates DONE + `SubliminalSolidMode` #461 architecturally moot/always-on (`648d21ac`, mirrors `FlashSolidMode`); **row 6** `EffectPayload.Ambient` DORMANT — no live bug (`ArmRandomSegment` has zero call sites), folded into the chaos run-engine workstream. **THE SOLE REMAINING WS0 RE-OPEN is `ProfileSyncService` SERVER sync** (`row 1`, ~2800 LOC, server-contract/HMAC/leaderboard-SUBMIT bound; the local prestige/season-reset primitives already landed lot 7). An **evidence-based 7-slice PORT PLAN exists at `docs/profilesync-port-plan.md`** (full public surface, all 14 endpoints, HMAC anti-cheat scheme, xUnit strategy, HARD P0 privacy `ExcludedBackupProperties` verbatim-port requirement), and **slices 1–4 have now landed UNWIRED** (Core 189/189, byte-identical app behavior, NOT in DI — so no live stub); the **full sync round-trip (pull+merge+push) + heartbeat + 401 recovery is done + tested**: slice 1 (`c4b2583a`) = seam + 18 DTOs + HMAC `SignRequest` + 5t; slice 2 (`a3215fc9`) = injectable-handler test ctor + heartbeat + 4t; slice 3 (`fafd22b0`) = pull + full merge + 3 real `IQuestService` DIM methods + 5t, **independently adversarially reviewed SAFE-TO-BANK** (all 5 corruption-critical never-lower invariants byte-faithful; one non-corrupting drift found + fixed); slice 4 (`34fc5f16`) = push/`SyncProfileAsync` (`/v2/user/sync`, leaderboard SUBMIT) with the **fresh-defaults cloud-wipe guard byte-identical to WPF**, `_syncGate`/cooldown/429, and 401 `restore-session` recovery (rotated token adopted only via the secure setter, never logged) + 5t. Each slice was de-risked via delegated fresh-context agents (scout → implementer → adversarial reviewer) that restore full review capacity a deep orchestrator context lacks. **Slices 5–7 remain (fresh-context): cloud backup (P0 `ExcludedBackupProperties` verbatim), server actions (purchase/oopsie/name), GDPR (delete/export), then final DI wiring + live smoke.** The plan (slices 1–4 checked off) makes that pass turnkey. **Standing deferred workstreams** (not merge re-opens; WS1+): Ditzy Data PRO analytics, Discord Rich Presence, companion AI, CompanionTab full port, chaos run-engine faithful port, calibration 16-point pipeline.
- [ ] Video, audio controls, and attention checks run through the compositor on Windows; legacy video windows deleted (WS1 Phase E complete).
- [ ] All passive Chaos visuals are compositor layers; a full Chaos run holds the FPS floor; hook swallow gap resolved or explicitly accepted in the task board.
- [ ] No passive effect window remains in `CCP.Avalonia` (audited); interactive windows are justified.
- [ ] Windows: every parity-matrix item re-verified `[x]` after WS1-3; benchmarks not worse than `benchmark-optimized.json`.
- [ ] Linux: app builds and launches; every feature works, is improved, or degrades gracefully with a recorded gap; click-through works on X11; parity matrix has a completed Linux sweep.
- [ ] 5-theme reskin passes everywhere; no raw loc keys; no stubs/no-ops for shipped features.
- [ ] WPF head still builds and runs (reference until Done is signed off).
- [ ] Trackers truthful: task board, UCE plan, parity matrix, this file.

## Guardrails (non-negotiable)

- Never modify the WPF head's behavior; it is the reference implementation.
- Never delete the legacy video path before WS1 Phase E is proven by running.
- Privacy/security never regress: webcam frames never hit disk/network; deeper-enhancement
  validation stays (NaN/Infinity/UNC/control chars/bounds); no UNC/extended-length paths
  for `--play`/`--edit`; subliminals stay IN screen capture by design (`WDA_NONE`);
  keyword-highlight/brain-drain capture exclusion stays; secrets stay in the secret-store
  seam.
- `Microsoft.WindowsAppSDK` stays pinned (`ExcludeAssets="all"`); never removed.
- Chokepoint files (DI registrations, `App.axaml`, csproj/slnf, loc JSON) follow the
  swarm rules in `port-plan` when sessions run in parallel.
- Windows never degrades to enable Linux; Linux degrades gracefully where the platform
  genuinely cannot do a thing.
- Out of scope for this goal: Android/macOS feature work (their builds must stay green),
  iOS, server-side changes.
