# AvatarTube Demonstrator — Rendered Animation Evidence (SP-015)

**Status:** DEMONSTRATOR — explicitly labeled in-window, really-functioning, superseded by the
first real AvatarTube feature, owner may async-veto. Synthetic generated asset packs only (no
WPF assets copied). Row stays `WIP` — named limits below.

**Framing decisions (packet + pre-approach consult):** the pinned Avalonia 12.1.0 surface has
**no public animated-GIF decode/animate API** (verified on the pinned package XML + pinned
source tree, never the docs site — `Avalonia.Base.xml`/`Avalonia.Controls.xml` zero gif
mentions; `src/Avalonia.Base/Media/Imaging/` static-bitmap only; the cache's `AvaloniaGif
1.0.0` targets Avalonia 0.9.3). The demonstrator shape is therefore **own-frame composition**:
packs are still-frame sequences + per-frame delay metadata; ONE SP-004-owned operation
advances frames on monotonic-deadline accumulation. The acceptance's timing properties are
directly testable because the timing machinery is ours.

## Machine-checkable evidence format

Every synthetic frame carries a **pixel counter strip** (bottom-left 40x4 of each 96x128
cell): magenta marker + 2 pack bits + 3 clip bits + 4 frame bits, luminance-decoded with
ambiguity rejection. Frame delays are **NON-UNIFORM** (430-1400ms — a uniform-delay asset
cannot falsify multiplied speed; >= ~400ms so slow captures sample every hold >= 2x).
Generation is deterministic (pure integer math, no PRNG/clock); committed assets are
pixel-identical to regeneration (unit test) and SHA-256 recorded in
`spine-tasks/SP-015-avatartube-animation/record.md`. Both packs are routed through SP-009's
manifest (5 embedded entries, case-exact IDs) — `--verify-assets` green on Debug AND Release.
There is **no mod loader** — "mod switching" = two packs through the same manifest path
(SP-009: schema covers mod, instances do not; real mod-loader semantics = named limit).

Evidence flow: capture (CopyFromScreen stage crop / XGetImage full window) -> app-side strip
decode (`--avatar-strip-decode [--scan]`) -> pure sequence evaluator (`--avatar-sequence`)
emitting named verdicts. Temporal math lives in the evaluator, never in scripts.

## Windows-headed evidence (66 gates green — `headed-evidence.ps1`, run.log)

| Behavior | Evidence |
|---|---|
| Static pose fades + cycling | G1: 4 distinct pose frames across 28 captures; dip-fade union no-blank; frames-advance / monotonic-modular / no-dup-run / float verdicts ALL PASS |
| Looping animation + cadence | G2: 3/3 discriminating runs fit the declared 1x schedule (speed 0.924-1.014); 2x AND 0.5x schedules REJECTED (multiplied-speed falsified); no dup-run beyond hold |
| Idle/idle2 crossfade rotation | G3: clips 1 and 2 observed; 39 captures no-blank; mid-fade capture artifact K3-reviewed PASS |
| Talk -> reaction -> idle | G4: strip order 3(talk) -> 4(reaction) -> 1(idle) observed (synthetic declared-duration trigger — audio pipeline is a BLOCKED row, named limit) |
| Click reaction + cooldown | G5: real avatar click -> clip 5 rendered; second click inside 3000ms traced `click-cooldown-ignored`; return to idle |
| Gentle float | float-liveness verdict in G1/G2: same-frame content centroid oscillates up to 8.2px, bounded (content-only transform; window position unchanged — G10 covers window move) |
| Pause/resume | G7: paused frame held identical 3324ms (6 captures); resume -> SUCCESSOR frame (3->4); cadence-unchanged-after-resume (pre+post fit ONE schedule shifted by the pause, speed 0.915) |
| Pack switching ("mod") | G8: 17 post-switch captures all decode pack 1 from frame 0 — old pack fully disposed, never two avatars; probe confirms 0->1->0 |
| Attach/detach | G9: detached -> WS_EX_TOPMOST set, GW_OWNER != dashboard (Avalonia 12.1.0 parents ShowInTaskbar=false windows to its hidden OffscreenParent — pinned `WindowImpl.SetParent`), pipeline preserved across both switches (frames-advance/no-blank/monotonic across the combined sequence); attached -> topmost cleared, GW_OWNER == dashboard; **detached tube survives owner minimize** (behavioral ownerless) |
| Owner transitions | G10: real caption drag (+136,+88) -> tube delta (136,88) exact. G11: owner minimize -> tube hidden + `pause-begin` traced; restore -> visible + `pause-end` traced; successor semantics per G7 |
| Leak long-run | G12: 25 attach/detach/pack-switch cycles -> REAL OperationRegistry counts stable (outstanding=2 = heartbeat+engine, subscribers=1). First-attempt timer/subscription-leak REJECT lessons cited in record.md |
| Cleanup | G13: tube close -> window gone; dashboard close -> process exit 0 |
| Undecodable asset | G14 (corrupt-demo): pack switch -> typed SP-006 `Degraded(asset-undecodable)` + static fallback rendered (fallback strip pack 3 clip 7; K3 PASS); bounded diagnostics (exactly one decode attempt per switch); UX choice (warning vs diagnostics-only) pending-owner, never implemented |

Demonstrator constants (WPF-parity values, **pending-owner**): fade 1000ms, min-hold 2000ms,
click cooldown 3000ms, talk lead-out 500ms, dip 0.3/150ms, float ±4 DIP/2000ms, quantum 16ms.

## WSLg/X11 session facts (`wslg-evidence.sh`; no input automation — SP-008 named limit)

- **Contract on WSL2** (native-dir `~/ccp-sp015`, rsync, never /mnt/e): `dotnet build
  CcpClient.sln` green; **176/176 CcpClient.Tests + 22/22 CcpClient.HeadlessTests** —
  identical counts to Windows.
- **Session:** WAYLAND_DISPLAY=wayland-0, DISPLAY=:0 (XWayland), kernel 6.6.114.1 WSL2.
- **Render:** 16/16 XGetImage full-window captures (320x380) of the animated demo
  (`--avatartube-demo --avatar-animate` — opens animated, no input needed); K3 review PASS
  (label, avatar art, strip, capability/probe texts, controls all render).
- **Frame deltas + no blanks:** full-window strip scan (`--scan`) — **ALL 5 evaluator
  verdicts PASS**: frames-advance (8 distinct decoded frames), no-blank (16 captures,
  saturated-content union for crossfade blends), float-liveness (centroid oscillation 4.4px
  across 4 same-frame groups, window-relative), monotonic-modular-advance,
  no-duplicate-run-beyond-hold.
- **NOT claimed on Linux:** cadence/timing (WSLg capture jitter supports deltas + no-blanks
  as session facts only — schedule-fit verdicts were computed but are NOT cited as cadence
  evidence), click/input (no input automation on WSLg), owner-transition gates
  (Windows-headed). Wayland §5.1 untouched (WSLg is XWayland).
- **Harness findings (recorded in record.md):** `date +%N` leading-zero octal trap in bash
  arithmetic aborted earlier capture loops mid-run; per-shot decode inflated the capture
  period past a frame hold (capture-then-batch-decode instead).

## Named limits (row stays WIP)

1. **Mod-loader semantics:** two synthetic packs via the SP-009 manifest path; no mod loader exists.
2. **Owner constants:** transition/liveness values are demonstrator constants pending-owner.
3. **Undecodable-asset UX:** typed capability state + static fallback + bounded diagnostics implemented as MECHANISM; warning-vs-diagnostics choice pending-owner (log-only today, matching WPF).
4. **Linux cadence/click/owner gates:** cadence Windows-headed only (WSLg jitter); click + owner transitions Windows-headed named gates; Wayland §5.1 untouched.
5. **Talk triggering:** synthetic declared-duration trigger substitutes for the audio/bark pipeline (BLOCKED audio row).
6. **Contract-only owner transitions** (Step-1 archaeology): monitor changes/mixed scaling, detached drag/resize widget contract, quiz z-order exceptions, fullscreen-detection hide, DPI-change quiesce — not demonstrable on the single-scale single-monitor box.
