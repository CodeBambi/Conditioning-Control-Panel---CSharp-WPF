# Port Status (as of 2026-08-04, second export — laptop resume session)

Branch: `feat/crossplatform` @ SP-037 land (`7e2fd5b8`) + reconcile commit. Pushed.

## New since the first export

- **Resume reconciliation executed (laptop):** waves 1–3 verified; the desktop's parked wave-4 batch `20260722T152755` lane commits NEVER travelled (desktop-local) — wave 4 is a FRESH execution of the packets, not a resume.
- **SP-037 authored + LANDED** (v6.6.3 manifest-drift repair, floor-repair precondition): empirical sweep +7/−1 (board hypothesis said +4 — sweep vindicated), copied-count 1538→1544, floor restored **466/466 + 29/29**; substitution-norm land (engine review chain absent — see park below); row WIP, owner ratifies. Next unused ID = **SP-038**.
- **WAVE 4 LANDED 2026-08-04 (integrate `8efd60b4`; floor now 492/492 + 29/29):** SP-035 (LoopbackOllamaProvider — first real provider; native api/chat; retry default OFF + 5-min WPF-observed timeout via consult corrections; LAB 26/26 Windows; WSL zero-distros named limit) + SP-036 (bounded MCP admission; avalonia-live PROVISIONAL with binding `CCP_MCP=1` condition; Sentry empirically LIVE = de-facto option 3, owner question OPEN; redact-BEFORE-calling binding run-wide). First production wave on the billing-header fix — all engine review spawns green. T-14 filed (lane re-patch recurrence). **Next: SP-038 = AI c3 (moderation boundary, read admission §8 c3 first) + lane partner TBD.**
- **WAVE 5 LANDED 2026-08-04 (integrate `f4eea79e`; floor now 516/516 + 29/29):** SP-038 (c3 moderation boundary — coverage-honesty inventory + tripwire, escalation interactive-only session-scoped, ZERO policy values invented) + SP-039 (T-14 hook — lanes now pre-staged with the main checkout's PATCHED .pi/npm at creation; named gate armed: next wave zero mid-task verify reds). T-15 filed (c2 lab harness hardening — zombie test-host flake class root-caused at T-3). **Next: SP-040 = AI c4 (memory; §4 rule 5 binds moderation-gated persist) + lane partner TBD.**
- **WAVE 4 PARKED (pause protocol, both-routes-failed branch):** anthropic fresh-subprocess route DOWN account-wide (`400 "extra usage"` — engine reviewer spawn + manual `pi -p` probes, opus-5 AND fable-5; in-session consult UNAFFECTED). SP-035+SP-036 stay pending + 2026-08-04-amended (Ollama present on laptop; WSL zero-distros named limit; consult rewire; SP-036 three-seat subject). Owner action: restore spawn capacity (claude.ai/settings/usage) or explicitly accept reduced assurance. Full state: `.spine/handoff.md`. **PARK LIFTED ~15:30 same day: request-shape defect (missing billing-header system[0], hermes #48176), fixed by `__PI_BILLING_HEADER_FIX__` — see incidents file.**
- **Engine restored on laptop:** global pi-spine re-pinned 2.12.2 → admitted 2.10.0 (BOTH settings.json + npm package.json exact), 12 patches applied, verify.mjs green.
- **Laptop bootstrap fixes (durable, in incidents file):** `git config --global core.hidedotfiles false` (hidden-.git EPERM class) + `pi.exe` shim (Node 24 cannot spawn the cmd/shell shims).

## Pause state

~~Parked 2026-08-04 ~13:10 UTC~~ **PARK LIFTED same day ~15:30 local** — the anthropic-400 was a pi request-shape defect (missing `x-anthropic-billing-header` system[0], hermes-agent #48176), fixed by local patch `__PI_BILLING_HEADER_FIX__` on the nested pi-ai (`pi -p` 200 on opus-5 AND fable-5). Wave 4 launches. Re-check the patch after any pi upgrade (npm wipes it).

## Board honesty rule

Landed rows stay WIP until the owner ratifies them. 18 rows were flipped WIP→DONE only with RATIFIED decree citation placed in evidence cells. Never flip without it.

## Landed

- **Run closed out 2026-07-21** — SP-001…SP-020 all landed (19 product/tooling rows,
  ALL WIP pending owner ratification). T-1 CLOSED (durable spine patch mechanism
  delivered by SP-020 and proven on the real tree via post-land reinstall gate).
- **SP-023** (2026-07-21) — DTRH host slice b1, FIRST product-implementation slice.
  FIRST GATE PROVEN: invokeCSharpAction page→host works on NativeWebDialog (WSLg
  transcript) — admission's named risk retired. Host shell: Windows embedded WebView2 12.0.1.
- **SP-024** (2026-07-21) — DTRH host b2: slots/picker/protocol v1. Three save slots =
  4 PersistenceStore<T> instances on SP-005 machinery (index + 3 slots, each its OWN
  named AsyncOperationOwner).
- **SP-025** (2026-07-21) — DTRH host b3: SFX/freeze/tint/video. Backends LIVE-FEED
  admitted (SoundFlow 1.4.1 nupkg-verified + LibVLCSharp 3.10.0 /
  VideoLAN.LibVLC.Windows 3.0.23.1; Linux distro libvlc 3.0.23-1).
  SoundFlow deadlock lesson + rect-persistence binding recorded.
- **SP-026** (2026-07-22) — DTRH host b4: progression/payout/Loom/media. Progression
  rides b2 slot documents — schema stays v1 additive-only, NO parallel meta file.
  Floor 366/29.
- **SP-027** (2026-07-22) — DTRH host b5, FINAL slice. **The b1–b5 slice cut is
  COMPLETE; the DTRH host row is fully sliced and stays WIP with consolidated named
  limits.** Watchdog/exit/injection + ESC forensics delivered.
- **SP-028** (2026-07-22) — T-5 local anchor-patch (parallelism enabler 1).
  `t5-reviews-autoclean` manifest patch: delete .reviews/ inside commitLaneWorktree
  AFTER verdict recording. T-5 CLOSED-by-patch; base install patched.
- **WAVE 1** (2026-07-22, first 2-lane batch, orch tip bff8f037) — SP-029 quips
  arbitration q1 (Audio/SoundArbitration.cs — SP-017 channel ownership verbatim,
  refcounted ducking with panic release-all) + SP-030 admission.
  T-5 post-land gate FAILED (row reopened).
- **WAVE 2** (2026-07-22, integrate 6e1b2f81) — **FIRST AUTO-GATE LAND in project
  history** (engine ran its own merges + opened its own gate).
  SP-031 (T-5 anchor re-base): **the SP-028 premise was FALSIFIED with 3 independent
  proofs** — two-root truth. SP-032: quips q2.
- **WAVE 3** (2026-07-22, integrate 2f77c934) — second consecutive auto-gate land.
  SP-033: AI companion c1 — AiOperationPipeline (SP-004 owned ops); provider seam
  switch = generation invalidation + cancel + stale-drop. SP-034: probe
  (Review-Level authoring defect recorded honestly). **T-5 gate DISCHARGED**
  (full-chain proof on SP-033). T-13 stall-detector DONE.

## Staged / next (wave 4)

- **SP-035** — AI companion slice c2: loopback Ollama provider.
- **SP-036** — audit and admit bounded Avalonia MCP use (A-01...).

## Pause state

2026-07-22 ~16:30 UTC the owner invoked the pause protocol: "Consult fable 5 has
hit limit, pause all work and prepare save spot." Work resumed 2026-08-04 (this
session: git repair + push + this memory export).

## Board honesty rule

Landed rows stay WIP until the owner ratifies them. 18 rows were flipped WIP→DONE
only with RATIFIED decree citation placed in evidence cells. Never flip without it.

## Wave 6 (2026-08-04, integrate 6255a643; floor now 537/537 + 29/29)

- **SP-040 (c4 memory):** AiMemoryStore on SP-005 machinery (own owner; null-on-disk retention discipline; consent placeholder Denied; append-NEVER strengthening; explicit-clear with 3 consult hardenings; named non-claim: persists+clears, context consumption = c7). Row WIP — c5 = awareness next.
- **SP-041 (T-15 lab harness):** ctor ODE race root-caused w/ deterministic repro; fresh-instance-per-bind; leak self-check (static registry + assembly fixture); 5 consecutive greens; zero assertion changes. Row WIP — owner ratifies.
- **T-14 NAMED GATE DISCHARGED → row CLOSED:** hook fired all lanes; lane-1's first red-free contract in 6 packets. Fresh lanes now arrive pre-patched (keep MAIN checkout patched — the hook copies whatever main carries).
- **T-16 filed** (DTRH cap-timer flake class). Next: SP-042 = AI c5 (awareness) + partner TBD.
- **Owner decrees encoded (2026-08-04):** improve-freely mandate (no 1:1 copy anywhere; observable-outcome parity only; improvements a must); use all resources actively ALWAYS (MCP seats within SP-036 rules); hermes caps 5000→10000 (config, restart-effective); avalonia-live verified end-to-end (27 tools; laptop headed-evidence substitute for UI work).

## Wave 7 (2026-08-04, integrate 49c4af7b; floor now 564/564 + 29/29)

- **SP-042 (c5 awareness):** typed consent (NotGiven placeholder; residual bool door + retirement condition in row); 4-class cooldown registry (extend-not-shrink; 10-vs-90 owner question verbatim); packaging under consent through c3 boundary (zero transmission on block); keyword routing owned ops (canned keyword-path-only; refusal drops); title capability Windows-probed. Row WIP — c6 = command execution next.
- **SP-043 (T-16 cap-timer determinism):** REAL 15s SEGMENT_SEC on ManualClock; pre-existing ISoundClock seam; 10 consecutive zero-red runs; row DONE (with T-15, consistency ruling).
- **T-15 + T-16 BOTH DONE** (tooling rows discharge on evidence; owner async-veto standing).
- **Named limits carried on the AI row:** Reserved→Wired flip (c6 owns, coverage test explicitly in File Scope); bool-overload retirement condition; badge-accuracy headed = c7.
- Next: SP-044 = AI c6 (command execution; none-admitted default; provable scope = canary + verdict round-trips + NotExecuted/ConsentGated).

## Wave 8 (2026-08-04, integrate b1a5b5f8; floor now 581/581 + 29/29)

- **SP-044 (c6 command execution):** AiCommandExecutor — generation-first per-command check (SP-019 limit 7 discharged); FromPolicy single consent source; none-admitted default + WPF divergence verbatim; type-level zero-execution + canary silence; Reserved flip LANDED; bool-door retirement blocked honestly (6 files, 3 out-of-scope — all-or-nothing condition recorded; assigned to c7). Row WIP.
- **SP-045 (ManualClock hygiene):** done, grep-proven zero assertion/wall-clock changes.
- **First ZERO-recovery wave** — no merge-stage T-5 cycles at all (T-14 hook + T-15 harness era).
- **Next: SP-046 = c7 companion UI (FIRST UI SLICE)** — improve-don't-clone decree + avalonia-live evidence + A-013 advisory; carries the bool-overload retirement.

## Wave 9 (2026-08-05, integrate 4479689a; floor now 601/601 + 33/33)

- **SP-046 (c7 companion UI):** owned modeless CompanionWindow on the REAL typed pipeline; badge truth type-computed; status from capability state; refusal bubble; memory-clear control (default-No + file deletion); consent/cooldown surfaces; panic-quiet + RE-ARM; bool-door RETIRED; avalonia-live carried the WH-class discharge (windowId silent-drop quirk recorded); K3 review PASS. **The c1–c7 slice cut is COMPLETE; the AI row's acceptance is NOT** (remaining limits on the row: Linux halves, Fallback type-level, reserved moderation rows, memory-not-consumed, none-admitted commands, §9.2 ×7, owner ratification).
- **New row: memory→prompt context (OPEN)** — the real functional gap (WPF: full dialogue history per request).
- **Next: phase-scope re-derivation consult** before further authoring (claimable inventory: prompt-context, dashboard-surface question, DTRH payload-location decision; rest owner-gated/excluded).
- Real Ollama 0.32.5 now runs on the laptop (SP-019 limit 1 stale).

## Wave 10 (2026-08-05, integrate 10f087b9; floor now 614/614 + 33/33)

- **SP-047 (memory->prompt context):** c4 store consumed (consent-gated read; wire-proven; read-gating ≠ deletion). ANTI-OVERCLAIM: recall stays owner-gated (Denied placeholder + session-only; WPF-true tension verbatim). Row WIP.
- **SP-048 (DTRH payload location):** b1's oldest open condition DISCHARGED ON WINDOWS (ratified copy-beside-exe; published boot from a MOVED dir; matrix 18/18). Publish footprint owner fact: 899 MB publish dir / 380 MB payload / 117.5 MB exe. Linux publish named limit.
- Consent-scope divergence is a board named limit now (startup load regardless of consent + ungated ReadRecent; retirement condition recorded).
- Packet-template patch `skill-trx-failure-names` added (TRX logger mandated on full-suite runs).
- Next: SP-049 = Loom studio promotion (v6.6.3 delta; dual archaeology — v6.6.3 payload changes AND b4's landed DtrhLoom).

## Wave 11 (2026-08-05, integrate 7a26a661; floor now 629/629 + 33/33)

- **SP-049 (Loom studio promotion, first v6.6.3 delta):** DtrhLoomWindow (WPF LoomHostService sibling); loom-reveal end-to-end; gifenc save round trip (byte-deterministic ×8); rack-pane limit DISCHARGED AS DRIVEN (painted screenshot = residual laptop-scale limit, zero-code-change discharge condition on a matched-scale machine); boon_pick chain fix (b3 text corrected; ChaosSfx audit row filed — full cue→chain map unaudited); dashboard entry-points row filed (reachability debt).
- **Next: SP-050 = host-obligation audit** across remaining v6.6.3 deltas (Brain Drain + Brain Melt, FX overhaul, Hourglass, Bottomless Fall, NUX, Weekly Intake Pass) — enumerate per-delta client obligations instead of blind feature packets.
- Ten consecutive auto-gate lands; four consecutive zero-recovery waves.

## Wave 13 (2026-08-11, integrate 6507361b; floor now 683/683 + 33/33)

- **SP-052 (b4 ownership-gate defects FIXED):** durMax 7200/1200 at persist AND deal (main's exact shape); Endless knob complete end-to-end; clamp matrix + five-point round-trips green; b4 tests updated+strengthened. Row WIP. Recovery: kimi-403 kill → days-parked → retry/resume both tasks (stale-failure-blocks-merge lesson).
- **SP-053 (reduced-motion probe): VERDICT = INHERITANCE HOLDS on Windows WebView2 151.0.4129.72** (engine-version-scoped; honoring mechanism not built; re-check = runtime version change). Row DONE-with-named-limits. Linux unproven.
- **OWNER INCIDENT: Run A wrote the real %APPDATA%/CcpClient profile** (APPDATA= doesn't redirect .NET GetFolderPath) — slot-1 index restored to WPF fallback defaults, purchases to []; post-run file showed the slot was unused (0 runs/0 sparks). P1 isolation row filed (real seam or backup/restore + m2test declared-fixture discipline); interim rule = backup-before-run.
- Next: SP-054 = Graded Intake web-core host (L, wave to itself).

## UPSTREAM BASELINE MOVED: v6.6.3 → v6.7.4 (2026-08-11, merge `42286638`)

The WPF reference tree on `feat/crossplatform` is no longer v6.6.3. 403 upstream commits merged
(938 files, +221k/−13k); client build 0W/0E and 683/683 green after the merge; `client/**` untouched.
**Everything about the delta is in `client/docs/upstream-sync.md`** (per-item obligations + evidence),
and the recurring procedure is the project skill `wpf-upstream-sync`.

- **New product surfaces (own rows):** Goon Game 1v1 duels (`Services/GoonGame/` + 184-file `web/goon/`
  payload), FYP desktop feed + ghost mode, Her Room companion redesign + Awareness (RECONCILE against
  the port's own c1–c7 companion), Trainer Card profile + wardrobe, Haptics v2 (SET-not-choice provider
  flags + schema-3 migration).
- **P0 parity drift on LANDED port code:** upstream now honors Assets-tree **deselection** in DTRH
  pools (`DtrhAssetManifest.EnumerateActive()`) and Graded Intake (`IntakeHostService.IsAssetActive`);
  the port's pools predate it (#762 #798 #619).
- **SP-054 was in flight at merge time and was NOT retargeted** — its v6.6.3 baseline stays internally
  consistent; the v6.7.x intake delta (new `intake/core/accents.js` +350, `ai.js` +79) is a follow-up row.
- **Guard gap found:** the client asset-manifest parity test gives ZERO signal for upstream payload
  trees the client doesn't ship yet (a 184-file tree appeared, suite stayed green).
- Merge-conflict rule: the WPF tree tracks `main` exactly (`--theirs`); `CCP.Core/` + `CCP.Avalonia.*`
  are abandoned first-attempt residue that manufacture delete/modify conflicts forever.
