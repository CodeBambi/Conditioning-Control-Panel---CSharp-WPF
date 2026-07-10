# SKIA REBUILD GOAL — Windows + Linux, functionality first

Created 2026-07-02 · **APPROVED by owner 2026-07-02** · **Re-crowned 2026-07-10 by the docs rework.**
THE umbrella driver for the entire Avalonia v12 port; every port session reads it before claiming work.
Read order: `docs-index.md` → this file → `avalonia-migration-task-board.md` (claim ONE row) → the row's
detail doc(s). Historical note: superseded `EXECUTION_GOAL.md` (deleted 2026-07-05; its v12 gotchas live
in `crossplatform-rebuild-plan.md` §21) and absorbed the retired `optimization-goal.md` stretch targets.
Completed history is compressed into the shipped ledger below; `docs-index.md` carries the full deletion
record for every removed doc.

## The goal, in one paragraph

Finish rebuilding the Conditioning Control Panel as an Avalonia v12 app whose **every current WPF
feature is fully ported to Avalonia** and WORKS on Windows and Linux: build, launch, and run all
features (or improved versions of them) through the Avalonia heads. Functionality is the contract; the
implementation underneath is not. Old WPF code, old dependencies, and old architectural choices carry
zero sentimental weight: replace anything if the replacement is faster, safer, or simpler, as long as
the user-visible behavior survives or improves. All real-time visuals (engine mode: session effects;
game mode: Chaos) render through the unified Skia compositor, not per-effect windows; non-visual and
interactive features are likewise rehomed onto Avalonia+Core seams so this goal applies to **all
feature ports, not just the UCE**.

## What matters and what does not

| Matters (the contract) | Does not matter |
|---|---|
| Every current feature works end-to-end in the **Avalonia heads** on Windows and Linux | Which library/dependency provides it |
| At least as fast and smooth as WPF; low-end machines are a hard requirement | Whether the code resembles the WPF code |
| Windows AND Linux: build, launch, features function | Matching WPF pixel-for-pixel (keep the design language, see `dashboard-design`) |
| Per-region click-through (team review 2026-07-09): only the color filter + spiral are ambient tinted glass the user works through; every other active layer captures input over its painted region | Keeping legacy per-effect windows |
| Privacy/security posture never regresses (see Guardrails) | Preserving old workarounds whose reason died |

**Acceptance gate:** a ported feature is accepted only when at least as fast and smooth as the WPF head
— preferably measurably improved (startup, memory, FPS, reliability, security). Big changes are
encouraged when they win on merit; record what and why in the task board.

## Rendering doctrine: UCE for all media

Avalonia v12 already renders ALL controls through Skia; standard Avalonia UI stays as controls. Added
doctrine:

1. **Every animated or real-time visual renders as a compositor layer** in the existing
   `CompositorEngine`: one topmost window per monitor, z-ordered `IAvaloniaLayer`s, one 60Hz tick,
   PER-REGION click-through (only color-filter + spiral regions are ambient "tinted glass" passing
   input; every other active layer captures pointer input over its painted region; `AvaloniaMouseHook`
   swallows clicks inside the per-frame capture mask). **No new per-effect `Window`s. Ever.**
2. **Engine mode** (video, flash, subliminal, bouncing text, spiral, brain drain, pink tint, bubbles,
   keyword highlight) and **game mode** (Chaos: field FX, DVD, cascades, cursor glow, vibe trail,
   e-stim arc, banners, wave timer, pop text, announcer) both target the compositor.
3. Windows that remain windows are INTERACTIVE surfaces only: main UI, dialogs, AvatarTube, HUD, lock
   card, quiz/mantra-style interactive overlays. If the user clicks IN it, it may be a window; if it
   just draws, it is a layer.
4. Custom Skia drawing uses the established v12 primitives (`ICustomDrawOperation` +
   `ISkiaSharpApiLeaseFeature` lease, or `CompositionCustomVisualHandler` for render-thread loops).
   Persistent `SKImage`s, engine-owned invalidation, no per-frame `SKBitmap` allocation (see the
   `unified-compositor-engine` skill).

## Porting doctrine: Avalonia everywhere

All user-facing functionality — tabs, dialogs, sessions, progression, integrations, overlays — runs
through Avalonia UI and CCP.Core seams. **The WPF head is the behavior reference ONLY: never modify its
behavior.** New work lands in Avalonia/Core first. Windows never degrades to enable Linux; Linux
degrades gracefully with a recorded gap where the platform genuinely cannot do a thing.

## Workflow execution model (how sessions run)

Port sessions are driven by the pi-dynamic-workflows `workflow` tool: `agent()` / `parallel()` /
`pipeline()` / `phase()`, journaled resume, git-worktree isolation, and the `verify()` / `judgePanel()`
quality patterns (adversarial fact-checking of findings; candidate selection on JUDGMENT outputs).
**Fan work out to agents instead of grinding one context.**

**Model tiers** — the board tags every row so routing is trivial:

| Tier | Model | Allowed work |
|---|---|---|
| small — **MECHANICAL** | `kimi/kimi-k2.7-code-highspeed` | Literal, list-driven execution ONLY: pre-sliced turnkey edits with WPF file:line citations, deletions, sweeps, tracker updates. Dumb but very fast. MUST STOP with a `BLOCKED:` note on the board instead of improvising when a precondition fails or a step is ambiguous. |
| medium — **STANDARD** | `zai/glm-5.2` | Bounded implementation, research digestion, reference reconciliation, routine reviews, inventories. |
| big — **JUDGMENT** | `anthropic/claude-fable-5` | Architecture, slicing, adversarial review, and anything touching state, economy, security, input hooks, or compositor internals. |

**Project agentTypes** (in `.pi/agents/`, usable inside workflows): `wpf-archaeologist` — read-only WPF
behavior-contract extraction with File.cs:line cites (nobody opens the 100KB+ WPF files raw);
`port-slice-executor` — implements ONE pre-planned slice under the iron rules (gates, no TODOs, no
forbidden zones); `port-parity-auditor` — adversarial working-tree diff audit vs WPF ground truth
before commit (mandatory for state/economy/lifecycle diffs).

**Skills are MANDATORY, not optional.** Avalonia v12 is brand-new (2026): LLM training data about it is
stale or actively wrong. Invoke; never re-derive.

| Skill | When |
|---|---|
| `avalonia-research` | Before ANY Avalonia API use, new dependency, bug/exception, and every Linux-specific mechanism; also for finding faster/lighter replacements (standing mandate) |
| `port-plan` | Session start: read trackers, pick ONE task, claim it, slice it |
| `wpf-parity` | Before implementing: extract the WPF behavior contract; after merging main |
| `port-feature` | Implementation workflow + WPF-to-v12 cheatsheet + verification ladder |
| `mechanical-port-work` | Small-tier discipline; the mechanical work queue is the board's tier-tagged live rows |
| `unified-compositor-engine` | All compositor/layer/video work |
| `overlay-clickthrough` | All window ex-style, hook, hit-test, topmost work; Linux click-through design |
| `dashboard-design` | Any user-facing surface; 5-theme reskin is part of done |
| `port-audit` | End of every workstream and after every merge from main |

## Current state (verified LIVE 2026-07-10 by the docs rework)

Branch `feat/crossplatform` @ `5e3ed650` · app **v6.2.11** · working tree clean.
**Nothing is in flight; the task board is the only claim ledger.** Any "claimed / co-agent / WIP" note
found anywhere is historical debris — purge it, do not honor it.

**Gates, re-run live 2026-07-10:** slnf build **0 errors** · Core tests **542/542** (Release, 0
failed). App/`--smoke-test` not re-run in that pass; recorded smoke baseline stays `[SMOKE] Findings:
5`, exit 0 (the `StartSession` blocker IS baseline).

| Surface | Status |
|---|---|
| Windows head | **~92%** — WS0 done (11 lots), video through the compositor (legacy path deleted), chaos run engine ported, 22-layer UCE lane complete. Remaining: input mask + hook swallow, FPS re-baseline, completion sweep, verify-set, DEFER backlog |
| UCE media surface | **~95%** — 22 registered layers (9 session + 12 chaos + 1 attention-check), one 60Hz tick, no passive effect window remains. Remaining: per-region capture mask + swallow |
| Linux head | **~45%** — builds and launches in a VM; ZERO click-through code (`SupportsClickThrough = IsWindows`), no input hooks, no verified feature sweep |
| Measured wins vs WPF | Startup ~2.5s vs ~4.2s · working set ~422MB vs ~1218MB · chaos full-run AvgFps 138.7 ≫ 30 floor (2026-07-05; re-baseline caveat on its board row) |

## Shipped ledger (compressed history; hashes are the evidence)

- **WS0 verify-and-correct sweep** — all 11 lots passed (contract + adversarial rubric + optimality);
  parity rows 1–11 earned with per-row evidence; last re-open closed by ProfileSync s7a `4f051ab0` +
  s7b `80e1442`; slice-6 economy bug caught+fixed pre-commit `766d8322`; #462 pair hardened
  `fb704a6d`. Core test floor rose 108 → 542 across the sweep.
- **WS1 video through the compositor** — A `85fa6570` · B `bbdb3077`/`99a50721` · C `07c094e1` ·
  D `37bd454a` (zero-alloc `VideoLayer`) · E1 `6180efc2` · E2 `ed636a7c` · E3 `8069cfb7` — **legacy
  video path DELETED**; compositor `VideoLayer`/`MandatoryVideoLayer` are the only video path.
- **Chaos run engine S1–S9** — S1–S4 `2d7bc384` · S5 `490da8c6` · S6 (`EffectPayload.Ambient` fix)
  `f5fa0757` · S7 (lifecycle + economy) `87515732` · S8 `f0fea4a0` · S9 verify `1f4c19fc`/`e61633c0`
  (benchmark clean, user-confirmed).
- **22-layer window-migration lane COMPLETE** — last passive window (attention check) `57f6f048`;
  dead windows deleted `8df68031`/`16fe5a92`/`c8bb20a1`; e-stim arc `05520f52`; 4 dead unwired
  passive windows DELETED. Per-migration hashes: board ledger. Layer registry with z-order:
  `unified-compositor-engine-plan.md`.
- **Companion AI, all three transports** — cloud `61ca0d1` · local/Ollama `2bd37899` · OpenAI
  `ca873d25` via `AiServiceStrategy`; AI-command dispatch `70cf9803`/`9fa09853`/`424ea528`;
  `IModerationLog` wired `b3b8da4`.

## Open workstreams (priority order; every item lives as a board row — the board is the tracker, this file is the driver)

1. **Per-region UCE input mask + `AvaloniaMouseHook` click-swallow** [JUDGMENT, HUMAN+SMART] — the
   2026-07-09 team review made the swallow path REQUIRED scope: per-frame capture mask = union of
   every non-ambient active layer's painted region; hook swallows clicks inside it (incl. WPF
   hold-to-defuse no-swallow exception); CompositorWindow stays `WS_EX_TRANSPARENT|LAYERED`.
   DELIBERATE, recorded divergence from WPF. Open questions on the row: chaos-run behavior, keyboard
   vs pointer-only, keyword-highlight over the user's own text. Spec: `unified-compositor-engine-plan.md`.
2. **FPS re-baseline @ 240s + MinFps=0 investigation** [JUDGMENT] — AvgFps 138.7 held the floor, but
   MinFps=0 is a ≥1s render stall correlated with LibVLC web-video decode failures (video-path stall,
   NOT a Skia/UCE regression); the 2026-07-05 run is environmentally invalidated vs
   `docs/benchmark-optimized.json` (decode-retry loop ≈4× CPU + 180s→240s drift). Evidence:
   `docs/benchmark-2026-07-05-analysis.md`.
3. **WP2b — optional libmpv engine-swap spike** [JUDGMENT; owner-authorized 2026-07-04;
   benchmark-gated] — primary candidate: libmpv render API via `HanumanInstitute.LibMpv.Avalonia`
   (LGPL build `-Dgpl=false`, near-zero-copy GL, cross-platform); LibVLCSharp 4 D3D11 REJECTED while
   preview (re-check first). Adopt only on ≥20% CPU reduction or measurably smoother 1080p pacing on
   the low-end target, zero behavior regressions, same `IVideoService`/`VideoLayer` seams, one
   engine per commit, revert-not-patch on any Windows regression.
4. **WS3 — Windows completion sweep** [MECHANICAL] — `port-audit` over the whole app; every remaining
   effect-window candidate becomes a layer, is justified interactive, or gets a row; re-verify the
   parity rows invalidated by WS1/WS2 (the matrix's "Re-verify queue"); benchmarks not worse than
   `docs/benchmark-optimized.json` (subject to item 2's re-baseline).
5. **WS4 — Linux bring-up to feature parity** [JUDGMENT for click-through/input; sweep MECHANICAL] —
   X11 first, Wayland best-effort: XShape/XFixes input regions via `IOverlaySurface.SetClickThrough`,
   evdev/XInput2/XRecord global mouse, system libvlc, Linux equivalents-or-recorded-gaps for
   wallpaper/WebView/ducking; then a full per-feature sweep per `docs/linux-vm-testing.md` + the
   parity matrix's Linux section. Mechanism catalogue: `crossplatform-rebuild-plan.md`.
6. **DTRH "The Fall" web mini-game epic** [BLOCKED on the `IBrowserHost` seam → JUDGMENT] —
   Three.js/WebGL roguelite in a WebView host; ~30 web assets portable; host/bridge/telemetry split
   Core-vs-head; chaos meta models to mirror. Phase breakdown: the board's DTRH epic appendix.
7. **v6.2.11 verify-set** [VERIFY → STANDARD] — lock-card repeat (`LockCardWindow.IsAnyOpen`), overlay
   z-order #497 (likely N/A under UCE — confirm), bounce-in-tray, weekly-quest #496, update-restart
   #499 (N/A until an Avalonia installer). Rows on the board.
8. **#493 pair** [STANDARD] — Gif Rain cascade multi-monitor; dashboard bubble motion-override. Not
   started.
9. **Standing DEFER backlog** — Ditzy Data PRO analytics (~832 LoC) · Discord Rich Presence ·
   CompanionTab follow-ups (OpenAI key-entry UI, global chat hotkey) · calibration 16-point pipeline
   (~1300–1500 LoC → `docs/webcam-calibration-port-plan.md`) · voice E2E mic live run
   (→ `docs/voice-port-status.md`) · tutorial system (→ `docs/TUTORIAL_SYSTEM_CONTEXT.md`) ·
   AI-command P3 gaps — each has its board row.

**WS5 (standing, opportunistic): better/faster/safer replacements.** Any session may propose a
replacement (dependency, decoder, IPC, storage, crypto, browser integration) when research shows a
materially faster or more secure option: research first, benchmark before/after, keep the seam, one
replacement per commit, record rationale + pin versions on the board. A replacement that regresses
Windows is reverted, not patched around.

## Loop protocol (how a workflow-driven session runs this goal)

1. **`port-plan`**: read `docs-index.md` → this file → the task board; check `git status` + recent log.
2. **Claim ONE board row** (append-only claim ledger entry). One task per session where possible.
3. **Fan out discovery** via the `workflow` tool: `wpf-archaeologist` for the behavior contract
   (`wpf-parity` discipline); `avalonia-research` for every API touched. Carry conclusions forward,
   never raw file dumps.
4. **Implement** per `port-feature` / `unified-compositor-engine` / `overlay-clickthrough`, routed by
   the row's tier tag (MECHANICAL rows → `port-slice-executor`; JUDGMENT stays with the big model).
   Standing rules: WPF behavior is the contract; new interface members are DIMs with safe no-op bodies
   so fakes keep compiling; never touch `CCP.Avalonia/Compositor/*` internals unless the row says so;
   never touch `tests/.../SmokeTestRunner.cs`; a missing seam/method → board row + `BLOCKED:` note,
   never invented inline.
5. **Audit before commit**: state-mutating or security-sensitive diffs get a fresh-context
   `port-parity-auditor` review (the pattern that caught the slice-6 economy bug). Run the gates below.
6. **Update trackers in the same session**: board row, parity matrix, UCE plan, this file's Current
   state if materially changed. Commit `feat(av): ...` / `fix(av): ...`, one task per commit, tree
   green.
7. **Compact** per Context discipline below.

**Stop conditions** (stop and ask; never improvise past them): a change would diverge from WPF
behavior (product decision needed); research contradicts project code with no safe answer; a guardrail
would be crossed; the tree is red for reasons you do not own. MECHANICAL tier additionally: ANY failed
precondition or ambiguous step → `BLOCKED:` note on the board.

## Gates before EVERY commit (copy-paste; ALL must pass)

```bash
dotnet build ConditioningControlPanel/CCP.Desktop.slnf -clp:ErrorsOnly    # 0 errors
dotnet build ConditioningControlPanel.sln -clp:ErrorsOnly                 # 0 errors (WPF guardrail)
dotnet test ConditioningControlPanel/tests/CCP.Core.Tests/CCP.Core.Tests.csproj -c Release   # ALL pass; count NEVER decreases (floor 542/542, live 2026-07-10 — read the live count)
dotnet run --project ConditioningControlPanel/CCP.Avalonia.Desktop.Windows/CCP.Avalonia.Desktop.Windows.csproj -c Debug -- --smoke-test   # [SMOKE] Findings: 5 = baseline (StartSession blocker IS baseline)
```

- Compositor/video work: also `-- --verify-layers` / `-- --verify-video` (exit 0).
- Render/hot paths: `--benchmark` (and `--max-benchmark`) before/after — not worse than
  `docs/benchmark-optimized.json` (re-baseline caveat: open item 2). Stretch targets (from the retired
  `optimization-goal.md`): startup + 10s working set ≥10% better than baseline; 60fps target / 30fps
  floor on effects.
- WPF reference head: `dotnet run --project ConditioningControlPanel/ConditioningControlPanel.csproj`.
- Linux (in VM, from `ConditioningControlPanel/`): `./build-linux.sh` (see `docs/linux-vm-testing.md`).

## Context discipline (when to compact, and how to stay cheap)

A bloated context produces worse code, not just bigger bills: constraints scroll out of attention,
half-remembered file contents get edited wrong, and reviews go soft. Treat compaction as a quality
gate. Trackers are the external memory; the transcript is disposable.

**Compact at these moments:**
1. After every completed task: trackers updated, committed, THEN compact. Never carry a finished
   task's context into the next one.
2. After every verification milestone inside a long task (green build, lot check passed).
3. After any large read (a 100KB+ file sliced, the task-board ledger, a WPF archaeology dive) ONCE the
   extracted contract/findings are written into a tracker row. Carry the conclusion forward, never the
   file contents.
4. At ~50-60% of the context window, unconditionally: finish the in-flight edit, write down state,
   compact. Do not push to 80% "to finish the task"; that is where mistakes cluster.
5. Before starting a review/audit lot: reviewers start clean so their judgment is not anchored by
   implementation context.

**Before compacting, write down (in the board row or the relevant doc):** the task in progress, the
next concrete step, files touched so far, the WPF contract or research findings extracted, and the
exact commands to re-verify. If a build is red, record why before compacting, never after.

**Never:** compact mid-edit or with unexplained red state; resume after compaction without re-reading
the claimed board row and this goal's relevant workstream.

**Token hygiene while working:**
- Grep for the member, then read the enclosing range. Never full-read the 100KB+ files (list in the
  `wpf-parity` skill); never re-read unchanged files.
- Fan large sweeps (inventories, multi-file reviews, research) out to workflow agents that return
  structured conclusions; keep raw file dumps out of the main context.
- One claimed task per session where possible; a session that sprawls across tasks pays the full
  context twice and does both tasks worse.
- Write findings into trackers the moment they are established, not at session end; anything only in
  the transcript is one compaction away from being lost.

## Definition of Done

- [x] WS0 complete: the ENTIRE port reviewed lot by lot (contract + adversarial rubric + optimality),
  corrections merged, parity matrix re-earned from a full reset with per-row evidence; every
  merge-`5ce70de6` re-open re-closed — the last, ProfileSync slice 7, shipped as s7a `4f051ab0` + s7b
  `80e1442` (2026-07-04).
- [x] Video, audio controls, and attention checks run through the compositor on Windows; legacy video
  windows deleted — Phase E: E1 `6180efc2` / E2 `ed636a7c` / E3 `8069cfb7`; attention check migrated
  to `AttentionCheckLayer` `57f6f048`.
- [ ] All passive Chaos visuals are compositor layers; a full Chaos run holds the FPS floor;
  per-region input mask + `AvaloniaMouseHook` swallow implemented per the 2026-07-09 team review
  (REQUIRED scope). — Layer migration DONE (12 chaos layers; run engine S1–S9); AvgFps 138.7 ≫ 30
  floor held 2026-07-05. REMAINING: open items 1 (mask/swallow) + 2 (re-baseline, MinFps=0).
- [x] No passive effect window remains in `CCP.Avalonia` (audited); interactive windows justified —
  `docs/uce-coverage-audit.md`: 22 registered layers; 4 dead unwired passive windows DELETED.
- [ ] Windows: every parity-matrix item re-verified `[x]` after WS1–3; benchmarks not worse than
  `docs/benchmark-optimized.json` (2026-07-05 comparison environmentally invalidated — LibVLC
  decode-failure retry loop + 180s→240s drift, NOT a code regression; re-baseline = open item 2).
- [ ] Linux: app builds and launches; every feature works, is improved, or degrades gracefully with a
  recorded gap; click-through works on X11; the parity matrix has a completed Linux sweep.
- [ ] 5-theme reskin passes everywhere; no raw loc keys; no stubs/no-ops for shipped features.
- [ ] WPF head still builds and runs (reference until Done is signed off).
- [ ] Trackers truthful: task board, UCE plan, parity matrix, this file.

## Guardrails (non-negotiable)

- Never modify the WPF head's behavior; it is the reference implementation.
- Privacy/security never regress: webcam frames never hit disk/network; deeper-enhancement validation
  stays (NaN/Infinity/UNC/control chars/bounds); no UNC/extended-length paths for `--play`/`--edit`;
  subliminals stay IN screen capture by design (`WDA_NONE`); keyword-highlight/brain-drain capture
  exclusion stays; secrets stay in the secret-store seam.
- `Microsoft.WindowsAppSDK` stays pinned (`ExcludeAssets="all"`); never removed.
- Chokepoint files (DI registrations, `App.axaml`, csproj/slnf, loc JSON) follow the swarm rules in
  `port-plan` when sessions run in parallel.
- Windows never degrades to enable Linux; Linux degrades gracefully where the platform genuinely
  cannot do a thing.
- Out of scope for this goal: Android/macOS feature work (their builds must stay green), iOS,
  server-side changes.
