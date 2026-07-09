# SKIA REBUILD GOAL - Windows + Linux, functionality first

Created: 2026-07-02. Status: APPROVED by owner 2026-07-02 — this is the active
autonomous driver. It superseded the former `EXECUTION_GOAL.md` (whose goal was declared
complete 2026-06-23 and had gone stale); **that doc was deleted in the 2026-07-05 docs
cleanup** — its canonical v12 gotcha content lives in `crossplatform-rebuild-plan.md` §21.
`unified-compositor-engine-goal.md` and
`unified-compositor-engine-plan.md` remain the detail tracker for Workstreams 1-2 and are
NOT replaced by this file.

This goal is the umbrella driver for the entire Avalonia v12 port: every WPF feature
must be rehomed onto Avalonia + CCP.Core seams with preserved or improved behavior, and
all real-time visuals must flow through the unified Skia compositor.

## The goal, in one paragraph

Finish rebuilding the Conditioning Control Panel as an Avalonia v12 app whose **every
current WPF feature is fully ported to Avalonia** and WORKS on Windows and Linux: build,
launch, and run all features (or improved versions of them) through the Avalonia heads.
Functionality is the contract; the implementation underneath is not. Old WPF code, old
dependencies, and old architectural choices carry zero sentimental weight: replace
anything if the replacement is faster, safer, or simpler, as long as the user-visible
behavior survives or improves. All real-time visuals (engine mode: session effects;
game mode: Chaos) render through the unified Skia compositor, not per-effect windows;
non-visual and interactive features are likewise rehomed onto Avalonia+Core seams so
that this goal applies to **all feature ports, not just the UCE**.

## What matters and what does not

| Matters (the contract) | Does not matter |
|---|---|
| Every current feature works end-to-end in the **Avalonia heads** on Windows and Linux | Which library/dependency provides it |
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

## Porting doctrine: Avalonia everywhere

All user-facing functionality, including tabs, dialogs, sessions, progression,
integrations, and overlays, must ultimately run through Avalonia UI and CCP.Core seams.
WPF remains the behavior reference only; new feature work and fixes land in
Avalonia/Core first.

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

## Current state (verified 2026-07-04 by full 10-hour audit; **refreshed 2026-07-05** from git evidence — re-verify with `port-audit` if old)

> **Tracker reconciliation 2026-07-09 (post-crash):** a PC crash interrupted several agents mid-flight.
> **No agent or co-agent is working on anything now** — every "co-agent active / parallel WIP /
> do-not-touch lane" note elsewhere in the trackers is HISTORICAL, not live status. The working tree is
> clean (only untracked `smoke-*.log` debris). Confirmed committed since the 07-05 refresh: the E-Stim arc
> (`ChaosEStimArcLayer` Z=125, `05520f52`), the vibe-pop cursor trail (`ChaosVibeTrailLayer` Z=128), and the
> deletion of the 4 dead passive-effect windows (`ChaosEStim`/`EStimGlow`/`VibeTrail`/`SkiaFx`) — **22 UCE
> layers; the window-migration lane is COMPLETE.** Two dead claims were cleared during this reconciliation:
> the **v6.2.10 sync** row reset to `⬜ todo` (merge + version bump + catalogue are committed & done —
> `5603442`/`6014fef`/`5997800`; the two #493 ports are NOT started), and the **MainWindow-position** row
> closed `✅ done` (code committed `3f9c6b17`, only human running-proof pending).

Exists and TRUSTED (earned, not assumed): the **WS0 review sweep is COMPLETE** — all 11
lots passed (contract + adversarial rubric + optimality), parity matrix rows 1–11
`passed` with evidence per row. The 2026-07-02 "trust nothing" ruling is SATISFIED for
everything a lot covered; new/changed code re-earns trust through the playbook gates.
Effect services (flash, subliminal, bouncing text, pink tint, spiral, brain drain,
bubbles) render as compositor layers; Avalonia measured faster than WPF on startup
(~2.5s vs ~4.2s) and memory (~422MB vs ~1218MB).

**Gates snapshot (last full run 2026-07-04, post-WP1):** slnf 0 errors · WPF sln 0 errors ·
Core tests **205/205** at that time · smoke **`[SMOKE] Findings: 5` = baseline, exit 0, no
token material in the output** (the `StartSession` blocker is a known baseline finding) ·
`--verify-video` exit 0. Audit trail: ProfileSync slices 1–6 each independently reviewed or
grep-proven (economy bug caught+fixed pre-commit in slice 6, `766d8322`); #462 pair
re-reviewed SOUND + hardened (`fb704a6d`). **Since then (2026-07-05 refresh, git-verified,
build NOT re-run in this refresh): app is at v6.2.9 "The Fall" (`UpdateService.AppVersion`,
merge `722f7dfa`); the Core test floor rose from 205 to **542/542** (513 at chaos S8
`f0fea4a0`, then 542 after the v6.2.9 sync — most recent gated commits `4984c8d8`/`f33425a4`;
the suite was NOT re-run in this refresh, read the live count from `dotnet test`); 20
compositor layers are now registered (9 session + 10 chaos + 1 attention-check) per
`uce-coverage-audit.md`.**

Open (this goal's actual work — execute via the EXECUTION PLAYBOOK below):
- **WP1 — ProfileSync slice 7 (GDPR + live wiring): ✅ DONE 2026-07-04 — WS0 is COMPLETE.**
  s7a (`4f051ab0`) GDPR export + easter-egg; s7b (`80e1442`) live wiring: DI, login/logout/
  startup, single heartbeat owner, §5 sync triggers + bounded exit sync, cloud backup/restore
  UI, server-authoritative purchase, oopsie, season-recap nudge. P0s intact (token-log diff
  audit clean; pinned tests untouched, Core 205/205). Evidence per checklist step:
  `docs/profilesync-port-plan.md` §8. Follow-up (manual, small): first real logged-in
  purchase/name-change exercise against the live server.
- **UCE video — WS1 COMPLETE incl. Phase E (2026-07-05 refresh).** Phases A–D closed
  2026-07-04 (`85fa6570` A vmem→SKImage render, `bbdb3077`/`99a50721` B audio+attention/
  safety/segment/events, `37bd454a` D.1/D.2 zero-alloc triple-buffered `VideoLayer`;
  `07c094e1` Phase C `--verify-layers`). **Phase E LANDED: E1 `6180efc2` (ESC/panic via the
  global key hook) → E2 `ed636a7c` (default video flipped to the compositor path +
  eyes-verified) → E3 `8069cfb7` (legacy video path DELETED — `AvaloniaMultiMonitorVideoService`
  grep-confirmed 0 matches in `CCP.Avalonia`; `HasOpenVideoWindows => IsPlaying`,
  `PrimaryVideoWindow => null`). The compositor `VideoLayer`/`MandatoryVideoLayer` are now the
  live, only video path — there is no `CCP_UCE_VIDEO` env gate any more.** Remaining WP2 (the
  benchmark-gated libmpv engine-swap spike) is OPTIONAL/opportunistic per the media-engine
  decision record below; the compositor architecture is proven. → WP2 (spike only).
- **WP3 OPENED 2026-07-04 (`0624d639`)**: chaos z-band `100–199` established in `CompositorLayers`
  (WPF `ChaosWindowZ` raise-above-everything evidence; chaos = capture-VISIBLE, main surface);
  **ALL 8 live passive chaos overlays are now compositor layers** (cursor glow `0624d639`,
  pop-text `a8bf6f10`, banner `798b6e64`, announcer `3df5cda7`, flash wash `0e64e4e5`, DVD
  `35418baa`, gif cascade `4c6c5992`, field FX `9fc0b420`; `--verify-layers` 15/15; ~15 legacy
  parity bugs found+fixed en route — per-leg animation durations, DPI px/DIP seams, streamed
  per-frame GIF decodes → decode-once with WPF memory caps, missing run-end teardowns; queue
  rows carry evidence + honest false-positive corrections). **10 chaos layers are now
  registered** (the original 8 + `ChaosFxLayer` `8df68031` + `ChaosWaveTimerLayer` `16fe5a92`);
  dead `AvaloniaBubbleWindow` deleted (`c8bb20a1`, bubbles consolidated into `BubbleLayer`).
  **Chaos run-engine faithful port COMPLETE (2026-07-05 refresh).** S1–S4 (HARD/JUDGMENT:
  ChaosSpawnCatalog + ChaosRunRules + ChaosScoring + ChaosSpawnDirector + all live-lambda
  knobs via ChaosRunKnobs, `2d7bc384`) were done 2026-07-04; **the MECHANICAL slices then all
  landed: S5 draft/boon `490da8c6` · S6 payload dispatch + heavy gate + `EffectPayload.Ambient`
  fix `f5fa0757` · S7 run lifecycle + economy `87515732` · S8 hints + layer production callers
  `f0fea4a0` · S9 verify `1f4c19fc`/`e61633c0` (benchmark clean, user-confirmed).** Handoff
  queue `docs/model-handoff-queue.md` Q1–Q5 = DONE; Core test floor rose to 513/513 at S8.
  Interactive surfaces (HUD, toy button, boon stage, bubbles, unlock card) still stay windows
  pending the hook decision. **Remaining WP3: the `AvaloniaMouseHook` click-swallow decision
  (below) and the FPS-floor gate over a full chaos run (not yet benchmarked in this refresh).**
  → WP3.
- Avalonia mouse hook cannot swallow clicks (WPF can): bubble/flash pops leak the click
  to the app underneath. Decide and fix in WP3, or explicitly accept and document.
- Linux: head builds and launches in a VM, but there is ZERO click-through code
  (`SupportsClickThrough = IsWindows`), no input hooks, no verified feature sweep. → WP5.
- Standing deferred rows from the WS0 sweep (each has a task-board/parity row): Ditzy
  Data PRO analytics UI, Discord Rich Presence, companion AI + CompanionTab full port
  (**companion AI CLOUD + LOCAL (Ollama) + OPENAI transport ported 2026-07-05
  `61ca0d1`/`2bd3789`/`ca873d2` — `CoreAiService`/`LocalAiService`/`OpenAiService` via
  `AiServiceStrategy` make companion chat (persistent multi-turn history on local) + all 5
  awareness reactions functional across all three providers; AI-command execution
  (AllowAiToControlEffects, needs the AiCommandService port) + the OpenAI key-entry UI + the
  global chat hotkey + the HasAiAccess/IModerationLog seams are filed follow-ups**),
  calibration 16-point window pipeline (~1300–1500 LoC), lots 7–11 DEFER rows. (The chaos
  run-engine faithful port — formerly listed here — is now COMPLETE, S1–S9;
  `EffectPayload.Ambient` was unblocked via S6 `f5fa0757`.)

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
video windows (Phase E only after parity is proven by running).

**Media-engine decision record (owner-authorized 2026-07-04):** the owner explicitly
authorized a media-engine swap if it wins on merit. Judged sequencing (do NOT reorder):
1. **WP2a — land UCE video on the CURRENT engine first** (LibVLCSharp 3.x vmem
   callbacks → pooled `SKImage`). One variable at a time in a correctness-critical
   subsystem; this proves the compositor architecture, keeps the Linux path, and
   produces the baseline benchmark.
2. **WP2b — engine-swap spike, benchmark-gated** (AFTER Phase E passes). Primary
   candidate: **libmpv render API** (`HanumanInstitute.LibMpv.Avalonia` exists and is
   maintained; render-API GL path is near-zero-copy; excellent low-end perf + frame
   timing; cross-platform incl. Linux). Secondary: libvlc 4 D3D11 output callbacks —
   REJECTED for now (LibVLCSharp 4 is still preview/nightly as of 2026-07; re-check
   before the spike). Licensing (app is MIT): use mpv's **LGPL build** (`-Dgpl=false`)
   — same posture as today's LibVLC (LGPL dynamic link); plain GPL libmpv would also be
   workable for an MIT open-source app but LGPL-build is the clean choice.
   Acceptance to adopt: ≥20% CPU reduction or measurably smoother frame pacing at 1080p
   on the low-end target, zero behavior regressions (attention checks, multi-monitor,
   loop, volume/device/mute, spikes, mini-player), behind the same `IVideoService`/
   `VideoLayer` seams, one engine per commit, revert-not-patch on any Windows
   regression. This spike is a JUDGMENT task: use the best available model.

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
worse than `docs/benchmark-optimized.json`. This sweep includes any remaining WPF-only
features (tabs, dialogs, integrations, progression, etc.): each must either have an
Avalonia implementation with parity, a documented improvement, or a gated/recorded gap.

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

As features are verified on Linux, any WPF-only behavior must either be ported to
Avalonia in a cross-platform way, or explicitly gated/degraded with a recorded gap; no
feature is considered "done" until its Avalonia implementation is the primary path.
Verification per `docs/linux-vm-testing.md` + `build-linux.sh`; add a Linux column or
section to the parity matrix and sweep every feature there.

### WS5: Better/faster/safer replacements (standing, opportunistic)
Any iteration may propose a replacement (dependency, decoder, IPC, storage, crypto,
browser integration) if research shows a materially faster or more secure option. Rules:
research first, benchmark before/after, keep the seam, one replacement per commit,
record rationale + pin versions in the task board. A replacement that regresses Windows
is reverted, not patched around.

## EXECUTION PLAYBOOK (ordered queue — written so ANY model can execute)

Work strictly top-to-bottom unless the owner redirects. **OWNER REDIRECT 2026-07-04: spend
best-model capacity on the HARDEST parts first — WP2 (UCE video) starts now; WP1's remaining
piece (slice 7b live wiring, MECHANICAL, turnkey checklist) is parked for any-model execution
and stays a WS0 DoD requirement.** Each work package (WP) is
tagged with the model tier it needs: **MECHANICAL** = any model can execute by following
the steps literally (the gates catch mistakes); **JUDGMENT** = use the best available
model (owner has confirmed capacity). If you are a less capable executor: follow steps
literally, run every gate, and STOP with a `BLOCKED:` note in the task board instead of
improvising whenever a precondition fails, a gate goes red, or a step is ambiguous.

### Standing rules (every WP, every session)
1. **Behavior contract**: WPF head behavior is the contract. Internals are free —
   faster/lighter tech is encouraged — but user-visible behavior must survive or improve.
2. **Performance doctrine (owner mandate)**: same behavior with higher frame rate,
   smoother usage, fewer PC resources. Every WP leaves the app at least as fast; WS1/WS2
   target MEASURABLE improvement. Concrete stretch targets (folded in from the retired
   `optimization-goal.md`): startup time and 10-second working set each improve **≥10%**
   vs the recorded baseline; effect/overlay animations hold **60fps** where feasible with
   **30fps** as the floor; a 3-min `--max-benchmark` all-effects run stays not-worse than
   `docs/benchmark-optimized.json`. Run `--benchmark` before/after when touching render or
   hot paths; a regression is reverted, not patched around. Tech choices are pre-decided
   in each WP to remove judgment calls — deviating requires `avalonia-research` evidence
   + benchmarks recorded in the commit.
3. **Gates before EVERY commit** (copy-paste; ALL must pass):
   ```bash
   dotnet build ConditioningControlPanel/CCP.Desktop.slnf -clp:ErrorsOnly    # 0 errors
   dotnet build ConditioningControlPanel.sln -clp:ErrorsOnly                 # 0 errors (WPF guardrail)
   dotnet test ConditioningControlPanel/tests/CCP.Core.Tests/CCP.Core.Tests.csproj -c Release   # ALL pass; count NEVER decreases (currently 199)
   dotnet run --project ConditioningControlPanel/CCP.Avalonia.Desktop.Windows/CCP.Avalonia.Desktop.Windows.csproj -c Debug -- --smoke-test   # 44 tabs / 17 first-chance / 5 findings = baseline (StartSession blocker IS baseline)
   ```
4. One task per commit (`--no-verify`); update the tracker row in the same session;
   never leave a red tree; line-ending CRLF warnings are harmless.
5. **Never touch**: WPF head behavior; `CCP.Avalonia/Compositor/*` internals unless the
   WP says so; `tests/.../SmokeTestRunner.cs`; the Guardrails section below. New
   interface members = C# default interface methods (DIMs) with safe no-op bodies so
   fakes keep compiling; real impls override.
6. If a needed seam/method does not exist: do NOT invent it inline — leave
   `// TODO(WPnn): needs <X>` + file a task-board row, and report it.
7. State-mutating or security-sensitive diffs get an independent fresh-context
   adversarial review before commit (the pattern that caught the slice-6 economy bug
   and the slice-3 drift). If you cannot dispatch reviewers, self-review against the
   WPF source line-by-line and record the comparison in the commit message.

### WP1 — ProfileSync slice 7: GDPR + LIVE WIRING — ✅ DONE 2026-07-04, WS0 FINISHED
Shipped as s7a `4f051ab0` (GDPR export + easter-egg, DeleteAccount stays auth-owned) +
s7b `80e1442` (live wiring: DI, login/logout/startup, single heartbeat owner, §5 sync
triggers + bounded exit sync, cloud backup/restore UI, server-authoritative purchase,
oopsie, season-recap nudge). All P0s survived (token-log diff audit clean; pinned tests
untouched). Acceptance met: checklist complete with per-step evidence
(`docs/profilesync-port-plan.md` §8), gates green (slnf 0 · WPF sln 0 · Core 205/205 ·
smoke `Findings: 5` baseline · `--verify-video` exit 0), parity row 1 re-closed.

### WP2 — WS1 video through the compositor [JUDGMENT — best model]
Detail tracker: `unified-compositor-engine-plan.md` phases A–E, one phase per session.
Tech is pre-decided in the WS1 media-engine decision record above (WP2a current-engine
first, WP2b libmpv spike after Phase E, benchmark-gated). Phase E deletes the legacy
video windows ONLY after side-by-side parity vs WPF is proven by running.

### WP3 — WS2 Chaos onto the compositor [JUDGMENT for the hook/input work; layer
migrations MECHANICAL]
Passive chaos overlays (~23 window classes) become `IAvaloniaLayer`s on the existing
CompositorEngine — no new dependency needed (SkiaSharp already present). One overlay
class per commit. Interactive surfaces keep their input model per `overlay-clickthrough`.
Resolve the `AvaloniaMouseHook` click-swallow gap here (WPF semantics incl.
hold-to-defuse no-swallow exception) or document acceptance. FPS gate: chaos run holds
60fps target / 30fps floor on low-end hardware.

### WP4 — WS3 Windows completion sweep [MECHANICAL]
`port-audit` over the whole app; re-verify parity rows invalidated by WP2/WP3;
benchmarks not worse than `docs/benchmark-optimized.json`; every remaining effect window
converted, justified, or filed.

### WP5 — WS4 Linux bring-up [JUDGMENT for click-through/input; sweep MECHANICAL]
Per the WS4 section below. X11 click-through first (XShape/XFixes via
`IOverlaySurface.SetClickThrough`), then the feature sweep per `docs/linux-vm-testing.md`.

### Backlog (interleave after WP1–2 as owner directs; each has a tracker row)
Ditzy Data PRO analytics UI (~832 LOC) · Discord Rich Presence · companion AI +
CompanionTab full port · chaos run-engine faithful port (unblocks
`EffectPayload.Ambient`) · calibration 16-point window pipeline (~1300–1500 LOC) ·
lots 7–11 DEFER rows (see parity matrix / task board).

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

- [x] WS0 complete: the ENTIRE port reviewed lot by lot (contract + adversarial rubric + optimality), corrections merged, the parity matrix re-earned from a full reset with evidence per row, calibration-port blockage resolved or formally re-scoped. Any lot RE-OPENED by a later merge from main (see the task-board "Sync-from-main" backlogs; merge `5ce70de6` re-opened lots 1/2/3/4/6) must be re-closed before WS0 is done.
  - **STATUS 2026-07-04 (WP1 shipped): all 11 lots PASSED and EVERY merge-`5ce70de6` re-open is RE-CLOSED — the last one, ProfileSync slice 7 (GDPR + live wiring), landed as s7a `4f051ab0` + s7b `80e1442` (parity row 1 re-closed with evidence; gates slnf 0 · WPF 0 · Core 205/205 · smoke baseline · video canary exit 0). WS0 is DONE.** Re-close trail: #462 session-summary (`410bef87`), #462 interaction-race (`4d65e564`, hardened `fb704a6d`), #461 resolved-by-documentation (`648d21ac`), EffectPayload.Ambient dormant (chaos backlog). ProfileSync slices 1–6 of 7 are DONE and UNWIRED (Core 199/199; full sync round-trip + heartbeat + 401 recovery + cloud backup w/ P0 exclusion strip + purchase/oopsie/change-name; each slice independently reviewed or grep-proven — the slice-3 merge got a fresh-context adversarial review, the slice-5 P0 exclusion list is grep-proven 18==18 vs WPF, the slice-6 economy bug was caught+fixed pre-commit). Full history + per-slice evidence: `docs/profilesync-port-plan.md`. Standing deferred workstreams (not re-opens; scheduled after WP1–2): Ditzy Data PRO analytics, Discord Rich Presence, companion AI + CompanionTab, chaos run-engine faithful port, calibration 16-point pipeline.
- [x] Video, audio controls, and attention checks run through the compositor on Windows; legacy video windows deleted (WS1 Phase E complete). — **DONE 2026-07-05: Phase E E1 `6180efc2` / E2 `ed636a7c` / E3 `8069cfb7`; `AvaloniaMultiMonitorVideoService` grep-confirmed 0 matches; attention-check migrated to `AttentionCheckLayer` (`a315cb0f`, last UCE window gap closed).**
- [ ] All passive Chaos visuals are compositor layers; a full Chaos run holds the FPS floor; hook swallow gap resolved or explicitly accepted in the task board. — **Layer migration DONE (10 chaos layers; run-engine S1–S9 complete, S8 `f0fea4a0` wired the production callers). FPS-FLOOR BENCHMARK 2026-07-05 (`docs/benchmark-2026-07-05-analysis.md`, @glm5.2, Release `--max-benchmark`): AvgFps 138.7 ≫ 30 floor across a full run incl. a 60s Chaos phase → floor HELD; MinFps=0 is a ≥1s render stall correlated with LibVLC web-video decode failures (a video-path stall, NOT a Skia/UCE regression). "Not-worse than `benchmark-optimized.json`" is environmentally invalidated on this machine (NOT a code regression): Phase 2 is 120s of web video that FAILED to decode (~half the run) — its LibVLC decode-retry loop accounts for BOTH the AvgFps drop and the ~4× CPU; secondary confound is the 180s→240s duration drift (the extra 60s is the heaviest Chaos phase). REMAINING: re-baseline at 240s (or note the 180→240 drift), investigate the video-failure→render-stall (MinFps=0), and resolve the `AvaloniaMouseHook` click-swallow decision (HUMAN+SMART).**
- [x] No passive effect window remains in `CCP.Avalonia` (audited); interactive windows are justified. — **DONE 2026-07-05 (`docs/uce-coverage-audit.md`): every passive effect renders as a compositor layer (22 registered layers = 9 session + 12 chaos + 1 attention-check, incl. the owner-authorized `ChaosEStimArcLayer` Z=125 + `ChaosVibeTrailLayer` Z=128). The 4 formerly-unwired passive windows (`ChaosEStim`/`EStimGlow`/`VibeTrail`/`SkiaFx`) were DELETED as dead code so they cannot be re-wired; `EStimGlow`'s charge-glow survives only as a DEFERRED FEATURE (the charged-pop mechanic), not a window. Interactive windows justified (audit §C). The window-migration lane is COMPLETE.**
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
