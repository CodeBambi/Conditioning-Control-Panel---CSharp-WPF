# Avalonia UI Parity Matrix

**Canonical parity EVIDENCE store.** Branch `feat/crossplatform` @ `5e3ed650` · app v6.2.11 · re-crowned
2026-07-10 by the docs rework (absorbed `parity-reverify-triage.md` as the Re-verify queue; that source is
deleted post-merge). The full doc map + read order lives in `docs-index.md`; the umbrella driver is
`skia-rebuild-goal.md`.

> **Nothing is in flight.** Post-crash reconciliation 2026-07-09 established NO active co-agent and NO
> "do-not-touch" lane. Any "claimed / WIP / co-agent lane" note you find in history is debris, not a live
> lock. Open items live in the **task board** (`avalonia-migration-task-board.md`) — this file records
> evidence only; no open work may live only here.

## How to read this matrix (acceptance gate — applies to EVERY row)

1. **Row acceptance = BEHAVIOR PARITY (or a documented improvement) AND the perf gate.** A row is at
   parity only when the feature works end-to-end against the WPF behavior contract AND is at least as
   fast and smooth as the WPF head — preferably measurably improved. The perf baseline is
   `docs/benchmark-optimized.json` (re-baseline caveat: the 2026-07-05 run was environmentally
   invalidated — see task board row #2 / `benchmark-2026-07-05-analysis.md`). Recorded wins to date,
   Avalonia halves artifact-backed: startup ~2.0s (`benchmark-optimized.json` `MainWindowShownMs`
   1976.9), working set ~422MB (`perf-avalonia.json`), Chaos AvgFps 138.7 ≫ 30 floor. UNVERIFIED
   (2026-07-10): the WPF halves "~4.2s / ~1218MB" — evidence gap: NO recorded WPF benchmark artifact
   exists in the repo; re-measure the WPF head before building on any vs-WPF comparison.
2. **FUNCTIONALITY IS THE CONTRACT, IMPLEMENTATION IS NOT.** Implementation details, dependencies, old
   architecture, and old code carry ZERO weight in parity. A feature ported with a different
   dependency/architecture than WPF (e.g. Skia compositor instead of WPF layered windows; LibVLC instead
   of WPF MediaElement) is at parity if the behavior matches and it meets the perf gate. Big-change
   rationale (what/why) is recorded in the task board, not here.
3. **A row earns `[x]` only through a WS0 lot pass:** (1) exercised end-to-end in the running app
   against the WPF behavior contract, (2) adversarial rubric review of the code, (3) proportionate
   optimality check, with evidence recorded in the row. **A smoke-test visit alone never earns `[x]`** —
   that is exactly how the voided 2026-06-23 marks happened (every row marked `[x]` from a harness that
   only proves surfaces open without exceptions; all of those marks are void). The old matrix lives in
   git history (`733c5362` and earlier).
4. **Re-verify against CURRENT WPF `main` (6.2.x moves), not memory.** Grep `<Version>`/`AppVersion`
   fresh; sync deltas are tracked in `crossplatform-rebuild-plan.md`.
5. **Linux:** every row below has a separate Linux sweep status (see the *Linux sweep* section). Zero
   features are swept on Linux as of 2026-07-10 — the WS4 epic (task board row #5) earns them.

## Status legend

- `[ ]` unverified — **default. Do not trust; nothing is "done" until exercised.**
- `[x]` verified — earned WS0-era evidence in the row.
- 🚧 partial — works but with a noted gap (gap has a task-board row).
- ❌ broken / stub.

## WS0 review lots (service-area passes; EARNED — do not reset)

Rows only change through evidence. The lot verdicts below stand; supersession notes call out where
later work changed the *implementation note* (not the verdict) — those rows feed the Re-verify queue.

`Linux` column = `[ ]` for every lot: zero features swept on Linux as of 2026-07-10 (WS4 epic earns them).

| # | Lot | Status | Linux | Evidence / notes |
|---|---|---|---|---|
| 1 | Data/settings persistence + paths | **passed 2026-07-02** | `[ ]` | 7 defects found+fixed (`e9501ce8`,`a2d1b9a8`,`b694b543`): secret-store no-op regression, 2 missing migrations, exit-flush gaps, quests serializer, roadmap drift, Roaming-migration orphaning, corrupt-file quarantine. Core tests 108→119. **ProfileSync shipped LIVE 2026-07-04** (the last WS0 re-open): s7a `4f051ab0` (GDPR export + easter-egg, DeleteAccount stays auth-owned); s7b `80e1442` (live wiring — DI singleton, login/logout/startup, single heartbeat owner, §5 sync triggers, bounded 2s exit sync, cloud backup/restore UI, server-authoritative purchase/oopsie, season-recap nudge). P0 guardrails intact (token strip-before-upload; fresh-defaults cloud-wipe byte-identical to WPF; direct-set; all pinned by tests). Slice-6 economy bug (Math.Max → skills free) caught + fixed pre-commit `766d8322`, pinned by test. #462 pair hardened `fb704a6d`. Prestige/season-reset LOCAL primitives (`OnSeasonReset`/`PermanentIds`/`TrackSkillPointsSpent`). **Follow-up only:** live logged-in purchase/name-change manual run (headed). |
| 2 | Session engine + start/stop | **passed 2026-07-03** | `[ ]` | 15 verified divergences fixed across 4 commits (`9cd64bce`,`70857d09`,`44fc1421`,`61f339fc`): preset apply/restore, session-log single-owner, per-session + manual intensity ramps, scheduler auto start/stop, engine-only plain START, delayed feature starts + bubble bursts, pause stops stimuli, dead VM panic path (P0), lockdown gates, autonomy arm/stop, launch behaviors, conditioning-tracker corruption (P0), achievement snapshots. Merge re-opens RE-CLOSED: P0 data-loss — ramp wrote opacity into auto-saving `settings.Current`, froze at max on crash — FIXED `b1336991` (ported WPF #471/#476 overlay direct-drive + `ReleaseOpacityRampHolds`, +2 regression tests; `IntensityRampService` writes confirmed faithful WPF parity, left as-is); #462 interaction-race cluster RE-CLOSED `4d65e564` (async dispatch + `ForceReset` ported; teardown ordering added to BOTH stop paths — root cause: Avalonia `Video.Stop()` Completes the queue slot, re-arming triggers post-teardown unlike WPF `CloseAll`). Core 119→166. **Follow-up:** live side-by-side pause/panic/scheduler-window vs WPF. |
| 3 | Overlays/compositor + click-through input | **passed 2026-07-03** | `[ ]` | 55 findings fixed across 3 commits (`0c43277f`,`d45eaf9f`,`22798e8e`): P0 capture-affinity dual-surface split (brain-drain excluded from capture again, subliminals stay IN); P0 SKImage dispose-vs-render race; real brain-drain blur + spiral GIF frame animation; engine idle watchdog; physical-px coordinate contract (mixed-DPI hit-tests); hotplug reconcile; single-display honored; FlashClickable/gaze-pop gating; hold-to-defuse parity; live opacity pickup; keyword-highlight live capture toggle; topmost watchdog + DesktopMode. `--verify-spiral` PASS (was always-fail stale harness, rewritten). `.webp` broadened into every extension set; `SubliminalSolidMode` `648d21ac` architecturally moot (shared compositor `SubliminalLayer` is the always-on host). **→ Re-verify queue:** click-through INPUT expectation changed in the 2026-07-09 team review (render evidence stands; only the input contract changed). |
| 4 | Video/audio | **passed 2026-07-03** | `[ ]` | 78 findings fixed across 4 commits (`1bef3b5b`,`32ad38f8`,`d6222069`,`a8d3df33`): P0 ducker crash-persistence + WPF relative/refcounted duck semantics (rescan/retry/watchdog, WebView2 exempt); PrimaryPlaybackTimeMs ×1000 unit bug; shuffled anti-repeat queue + content-pack videos + DisabledAssetPaths; duck/flash/bubbles coordinated; companion −25 XP fail penalty; ESC/panic + strict-key contract; DualMonitorEnabled/FillAllMonitors gates + no-activate secondaries; VideoHardwareDecoding honored; position-based watch credit; VideoAboutToStart + 1.3s pre-announce; FloatingText physical-px + HWND_TOPMOST; wallpaper original restore. `--verify-video` exit 0. **⚠ SUPERSEDED post-lot (2026-07-05 — lot verdict unchanged):** the "legacy video path is the live default / `CCP_UCE_VIDEO=1` opt-in" note is STALE. UCE video Phase E landed (E1 `6180efc2` / E2 `ed636a7c` / E3 `8069cfb7`); the compositor `VideoLayer`/`MandatoryVideoLayer` are the ONLY video path, legacy `AvaloniaMultiMonitorVideoService` DELETED (0 grep matches), no `CCP_UCE_VIDEO` gate. **→ Re-verify queue.** |
| 5 | Speech/mic + gaze/calibration | **passed 2026-07-03** | `[ ]` | 41 findings fixed across 4 commits (`2da98c0e`,`53c4b0ab`,`09d13f55`): P0 wake-calibrate opened mic with no `MicConsentGiven` gate + new Avalonia `MicConsentDialog` (3-step informed consent); P1 one-click webcam GrantConsent bypass rerouted; `ConsentVersion` one source of truth (new Core `WebcamConsent.cs`); the 3 calibration windows were FAKE-SUCCESS shells → made honest + REAL quick-recal (samples OnGazeMove, persists RuntimeOffset) + REAL tracker test; `IWebcamService` seam completed; cold-start race fixed; gaze-focus prerequisite restored; debug-cursor DIP→physical-px; device combo wired; speech device name-match (#441b), honest voice-ack. **PRIVACY GUARDRAIL CLEAN** (full grep sweep: no frame/audio/mic data reaches disk or network on either head; calibration JSON = coefficients only; all tracker log sites aggregate-only). Calibration solver landed `837aaa1d`; update 2026-07-10 — the single `WebcamCalibrationWindow` (708 LOC) now collects iris samples, fits (quality gate `WebcamCalibrationWindow.axaml.cs:298`) and commits calibration (S1c `df06d06d`); the shell note is RESOLVED in `webcam-calibration-port-plan.md`. **Real gap = 16-point window pipeline (~1300–1500 LoC, task-board row → `webcam-calibration-port-plan.md`).** Follow-up: live webcam/gaze/calibration vs WPF with real hardware (W-14 OnGazeMove coordinate-space ambiguity). |
| 6 | Chaos/game mode | **passed 2026-07-03** | `[ ]` | ~71 findings fixed across 3 commits (`b9fb74be`,`3f4cceb8`,`b828fea5`): rank enum order+thresholds+names, DifficultyMult, removed 7 non-WPF boons, killed passive focus regen, armed crash sentinel, bubble-pop achievement + loop tip + skillMult, boon-bar ribbon, ~7 in-run SFX cues (gameplay was silent), 3 lesson ticks (were permanent locks), overlay HiDPI physical-px fixes + DualMonitor honoring, restored entrance animations (ripple/announcer/unlock-card/vibe pulse/keyframes/tether/banner throb), PopText handler-leak, GIF-cascade budget + off-thread flash decode + art cache (OOM guards), timer leaks, dead never-started allocs. PARITY OK: narrative director, ChaosBoonColors, RevealService (18 predicates), lesson defs, meta persistence, catalogues, HUD, SFX resolution, art resolve, intro, SkiaFx/backdrop math, ex-styles, capture-affinity CLEAN, compositor NON-BREAKAGE. **⚠ SUPERSEDED post-lot (2026-07-05 — lot verdict unchanged):** the "SIMPLIFIED STAND-IN" structural verdict is STALE. The faithful chaos run-engine port completed S1–S9 (S1–S4 `2d7bc384`; S5 `490da8c6`; S6 `f5fa0757` `EffectPayload.Ambient` fix; S7 `87515732`; S8 `f0fea4a0`; S9 `1f4c19fc`/`e61633c0`, user-verified); Core floor 513 at S8. **→ Re-verify queue.** |
| 7 | Progression/quests/economy | **passed 2026-07-04** | `[ ]` | ~42 findings fixed across 5 commits (`997aed61`,`750d2615`,`bc1df7bb`,`b1236e8a`,`eaa33975`): **P0** achievements.json single lock-guarded atomic writer + tmp-recovery (was naive WriteAllText + racing writer); **P0** Season Recap ported to Core disk persistence (was dead stubs — permanently broken + rollover lost); XP/economy parity (skill points +1/level per `PointsPerLevel=1`, companion+quest fed base not skill-multiplied XP, `SeasonPeakLevel`, attention-fail negative penalty); prestige accrual (`TrackSkillPointsSpent`/`ReconcileLifetimePointsSpent` + purchase wiring + `OnSeasonReset` prune); quest fixes (daily streak once/day, dirty-only per-tick save, Oopsie streak-fix, skill-tree multipliers, rollover reconcile); rubric (settings.json write debounced, `QuestsTabViewModel` IDisposable leak, cross-thread binding → DispatcherTimer). PARITY OK: XP curve + session multiplier + all achievement predicates + 26 track hooks, quest pool 20+20 (6.1.7 sync) + reroll math + cadences + leaderboard FETCH, skill-tree defs/costs/tiers/prereqs (5 Tier-6 + 11 PermanentIds). DEFER (task-board rows): server-progression-sync (SHIPPED — see row 1); Ditzy Data PRO analytics UI (~832 LoC). |
| 8 | Browser/integrations | **passed 2026-07-04** | `[ ]` | ~40 findings fixed across 4 commits (`ac9f8d9e`,`8213b1d2`,`de2aaa05`): **SECURITY CLEAN** — tokens `[JsonIgnore]`→`SecureAuthTokenStore`/`AvaloniaTokenStorage`→`ISecretStore` (DPAPI/Keychain/AES-GCM), never logged, TLS, CSRF constant-time compare. **account** AC-1/AC-2 (#455) unified `AuthLogoutHelper` (was leaving token+premium+OAuth live), AC-3 (#465) linked-Patreon +14d grace restored, AC-5 SubStar logout, R7 `IEnumerable<IAuthProvider>` (was resolving only the last-registered), R1 drop `cmd.exe` URL interpolation. **companion** C1 (#463) `IAvatarWindowService.IsSpeaking/IsSpeakingAudio` seam + `ShowAvatarLine` 16×750ms busy-retry, C2 wired 13 orphaned reaction handlers. **browser** B1 (P1 login-break) OAuth → system browser via new `IBrowserHost.OpenExternalAsync`, B2 (P1 black-video) Chromium autoplay+anti-MPO flags, B3 ad/tracker/popup blocking. DEFER (task-board rows): Discord Rich Presence (100% unported, dead toggle); companion AI (only stub `AvaloniaQuizAiService`); DTRH "The Fall" web game (corrected 2026-07-10: NOT seam-blocked — `IBrowserHost` is an implemented 11-member seam with `WebView2BrowserHost` et al., and `ChaosTunnelService` already hosts the three.js tunnel through it; the game port itself remains — board row #6). |
| 9 | Tabs and dialogs | **passed 2026-07-04** | `[ ]` | ~50 findings fixed across 7 commits (`5958038f`,`f8a20dce`,`db76fa5d`,`b08320a5`,`3be5c732`,`ad3dc3c5`,`0664f0ee`): **2 P1 crashes** (off-thread `[ObservableProperty]` drop-zone reset → `Dispatcher.UIThread.Post`; CompanionTab Customize-Prompt null `ShowDialog` owner → MainWindow); missing bound controls wired (custom-session Delete + Share-to-Catalogue, LevelFeatures Edit-Phrases, photo-note Input); RemoteControl executor parity (start/stop_autonomy, subliminal pooled flash, video one-shot, mind-wipe count gate); consent/compliance (webcam/mic modal+owned, `ExplicitContentGate`, DeeperTab real `WebcamConsentDialog`); localization (39 keys en.json; L2-09 folder-status both-branches-same-key bug); orphaned-dialog wiring (roadmap, UpdateProgressDialog, OG-welcome); hygiene (dead `AttentionCheckTabViewModel` removed). DI tab-map clean (28 registered + 16 feature-control VMs all reach a real view; no live view falls through to `PlaceholderTabView`). DEFER (task-board rows): CompanionTab full port (entire AI-Brain provider section absent — tied to companion-AI workstream); L5-01 template-map dedup; leaderboard depth. |
| 10 | Theming/mods | **passed 2026-07-04** | `[ ]` | ~24 findings fixed across 3 commits (`bd165158`,`1d902ff0`,`0e6effe5`). **4 P1 security/privacy:** ported the WPF `SanitizeManifest` trust boundary into `AvaloniaModService` (install/disk-load/built-in sanitization — field-length caps, `#RRGGBB` hex validation on all 7 theme colors, per-collection count caps, finite-double guard, control-char + bidi-override strip, required Version/Author + `MinAppVersion`, HTTPS-only on DefaultUrl/DefaultVideoLinks + `FilterHttpsVideoLinks` at use-time); R-01 `AvaloniaContentPackService` ctor folder-creation guard (#450-452 startup crash); R-02 real DI `IPromptValidator`+`IModerationLog` (was no-op shadow — user-authored quiz prompt unmoderated); R-03 startup `CleanupStaleTempFiles` sweep. **Theming** T-02/T-03 live re-skin correct: `AvaloniaThemeService.Apply` updates only `*Color` keys (brushes bind `Color={DynamicResource}`) not ~25 brush objects; `FeatureCard` subscribes `ThemeChanged` to re-run `ApplyActiveState`. All 5 themes reskinned + screenshotted (ccp-default/bambisleep/sissyhypno/drone-mode/locked); all 5 built-in mods still activate post-sanitization. PARITY OK: 5 themes byte-identical (`BuiltInMods.cs`), mod flows, pack crypto Core↔WPF identical. DEFER (task-board rows): T-01 secondary-color divergence (documented), M-08 resource cache, R-04/05 moderation P2s. |
| 11 | Heads/DI/startup | **passed 2026-07-04** | `[ ]` | ~22 findings, **0 P0 / 0 P1 — foundation solid.** VERIFIED CLEAN: DI ordering correct (base fallbacks → head overrides land last, MS.DI last-wins; every `Null*` fallback overridden on the Windows head; NO real capability silently no-op on Windows); all 3 crash handlers present + logging; settings + achievement/quest flushed on EVERY desktop exit path via `desktop.Exit += FlushPersistentState` (closes the WPF 30s-dirty gap); update flags exact match WPF; DPAPI `CurrentUser` scope parity; wallpaper capture/restore real; single-instance file-lock + named-pipe handoff functional; non-Windows degradation clean everywhere (secure keychain/AES, never plaintext; browser/wallpaper/ducking/tray best-effort no-throw). Fixed `92329231`: S-02 best-effort dispose hardware/trigger sources on exit (was leaking LibVLC child windows + handles), S-03 crash-dialog show-once, S-10 deferred OCR try/catch, D-01 `IEnumerable<IAuthProvider>` + `HasPremiumAccess`, H-02 macOS keychain via stdin (no `ps`/proc leak). DEFER (task-board rows): S-01 file-open `--play`/`--edit` 2nd-launch handoff, S-05 no dedicated `crash.log`, S-06 zombie-primary takeover, macOS P3s (H-03/04/05). |

**Per-area percent-ported estimate** (honest read of the row evidence above + the task-board SHIPPED
ledger; not a
re-verification): Lots 1/2/4 = 100% (Windows); 3 ≈92%; 5 ≈85%; 6 ≈93%; 7 ≈90%; 8 ≈65%; 9 ≈95%; 10 ≈98%;
11 ≈60% (Windows ~95%, Linux ~45%). UCE media surface ≈95% (22/22 layers). **Windows head overall
~92%, Linux head overall ~45%.** What holds each area below 100% is recorded above and routed to a
task-board row.

---

## Re-verify queue (the 2026-07-09 triage, absorbed at docs-rework)

> **DoD #5 precursor.** This queue feeds **task-board row #4 (WP4 / WS3 Windows completion sweep)** —
> the headed/runtime pass that earns `[x]` on every row. It does NOT itself mark `[x]`.

### Key finding (read first)

1. **Code-level parity is already verified.** Lots 1–11 each did a multi-agent adversarial review of
   their service area's code vs the WPF contract. The unchecked granular rows (tab views / feature
   controls / dialogs / windows / deeper / main-sync deltas below) are unchecked because those lot
   verdicts live at the LOT row above, not propagated to each surface row — NOT because the code is
   unverified.
2. **What remains for `[x]` is RUNTIME END-TO-END EXERCISE** (the matrix rule: "exercised end-to-end in
   the running app against the WPF behavior contract"). This is exactly the piece the voided 2026-06-23
   marks lacked.
3. **Runtime exercise cannot be automated headlessly for non-compositor rows.** The only automated
   harnesses (`--verify-layers`, `--verify-video`, `--verify-spiral`, `--benchmark`) exercise the
   compositor/video — and that UCE lane has SETTLED (work committed; nothing in flight as of 2026-07-09).
   For all other surfaces there is no harness; earning `[x]` needs a **headed session** exercising each
   feature side-by-side with the WPF head.
4. **Implication:** DoD #5 completion is fundamentally a **headed/manual verification effort**, not a
   headless-agent task. This queue makes it tractable by stating, per row: the lot that verified the
   code, the exact runtime check still needed, and whether later work invalidated the row.

### Rows invalidated by later work (re-verify FIRST under board row #4)

| Row | What changed | Why re-verify | Re-verify check |
|---|---|---|---|
| Lot 3 (overlays) — click-through INPUT | 2026-07-09 team review flipped the contract | Render evidence stands; only the INPUT expectation changed. Per-region: only color-filter + spiral regions stay click-through; **every other active layer (subliminal, flash, brain-drain, bubbles, etc.) now CAPTURES input** over its painted region via the compositor capture mask + `AvaloniaMouseHook` swallow. | Implement the per-region mask + hook swallow (board row #1), then headed-verify: clicks pass through color-filter/spiral; clicking a flash/subliminal/brain-drain region blocks the desktop. |
| Lot 4 (video) — default path | UCE Phase E (`6180efc2`/`ed636a7c`/`8069cfb7`); legacy path DELETED | The lot's "legacy path is live default" note is STALE. The compositor `VideoLayer`/`MandatoryVideoLayer` are now the ONLY video path. | Headed: play each video mode, confirm parity of playback/ducking/multi-monitor/ESC against WPF through the compositor path. |
| Lot 6 (chaos) — run engine | Run engine S1–S9 (`2d7bc384`…`1f4c19fc`/`e61633c0`); user-verified | The lot's "SIMPLIFIED STAND-IN" verdict is STALE. The faithful ~3000-line run engine is ported (economy/spawn/scoring). | Headed: live full Chaos run vs WPF — economy/spawn feel, HiDPI overlay placement, audible SFX, per-region chaos-FX input capture. |

### Concrete divergence surfaced (highest-value open question)

**Avatar `ShowInputPanel` force-foreground — potential cross-app focus disruption (GAP?).**
- WPF (`AvatarTube/AvatarTubeWindow.ChatInput.cs:453-465`): `ShowInputPanel` → `FocusInputAfterLayout()`
  → `TxtUserInput.Focus()` (textbox focus only, within the app).
- Avalonia (`CCP.Avalonia/AvatarTube/AvatarTubeWindow.axaml.cs:862-866` → `ChatInput.cs:90-144`):
  `ShowInputPanel` → `ForceForegroundWindow()` (Win32 `AttachThreadInput` + `SetForegroundWindow` +
  `BringWindowToTop` + `Activate()`; or Topmost-pulse + `Activate()` on Linux/macOS) + `FocusInputAfterLayout`.
- **Why it may be justified:** Avalonia's keyboard-focus model differs from WPF's; a topmost avatar
  window may not receive keyboard input without an explicit foreground grab (plausibly a necessary
  platform adaptation so the chat textbox is typeable). The speech path does NOT call it (only Ctrl+T).
- **Why it may be a regression:** `AttachThreadInput`+`SetForegroundWindow` is an aggressive grab that
  can steal foreground from *another application* the user is using when they press Ctrl+T (WPF's in-app
  `Focus()` cannot). Same risk on Linux/macOS.
- **Not marked `[x]` or `❌`.** Headed check: focus another app, press Ctrl+T, watch whether the avatar
  yanks foreground. If it disrupts, file a task-board row (clamp the grab in-app, or force-foreground
  only when the avatar already has app focus).

### Section triage status (the granular checklists below are the runtime checklist for board row #4)

- **Cross-cutting** — partial (8 rows triaged: multi-monitor CODE✓, auth storage CODE✓/NEEDS-ENV,
  session engine CODE✓, avatar reactions CODE✓ + focus GAP?, chaos economy → re-verify, per-region
  click-through RESOLVED-decision/impl-pending, theme reskin NEEDS-ENV/eyes, perf NEEDS-ENV/re-baseline).
- **Main-sync deltas** — triaged (see below).
- **Tab views (32) / Feature controls (19) / Dialogs (34) / Windows (27) / Deeper (5) / Chaos &
  AvatarTube (3)** — pending headed exercise; code-level parity verified at the owning lot row.

Triage legend: **CODE✓** code-parity confirmed (lot + spot check), runtime pending · **GAP?** possible
divergence, runtime must confirm · **NEEDS-ENV** needs accounts/hardware/Linux/human-eyes not available
headless · (the old "BLOCKED = UCE lane" status is retired — that lane settled 2026-07-09).

### Main-sync deltas (ported from WPF 6.1.7; re-verify against current main)

| Row | Status | Runtime check still needed for `[x]` |
|---|---|---|
| Chaos "Down the Rabbit Hole" main menu (logo, How-to-Play, menu music, fog/intro FX, options) | re-verify (lot 6 run engine S1–S9 done) | Headed: full DTTRH menu + run side-by-side vs WPF. |
| Quest pool refresh (20 free + 20 patron + art) | CODE✓ (lot 7) | Headed: open Quests tab, confirm 20 free + 20 patron load + render art. |
| Auth graceful browser-launch fallback (clipboard + dialog) | CODE✓ (lot 8) | Headed: trigger OAuth, confirm system-browser launch + fallback when browser unavailable. |
| Subliminal double-flash fix (keep-alive windows, no Hide between flashes) | CODE✓ (service) / re-verify (render) | Headed: trigger back-to-back subliminals, confirm no flicker/Hide between. |
| Avatar focus-steal fix (`ShowActivated=false`, `SWP_NOACTIVATE`, no forced chat focus) | CODE✓ + GAP? (lot 8) | Headed: (a) confirm avatar speech doesn't steal focus; (b) see the force-foreground GAP? above. |
| Bubble pace (FIELD_PACE) / ChaosArt / ChaosTuning / Achievement autonomy quests / Lab tab | re-verify (bubble/chaos) / CODE✓ (achievement/Lab) | Headed: full chaos run for bubble/chaos pieces; achievement/Lab exercise. |

---

## Linux sweep status (WS4 — task-board row #5)

**Honest baseline as of 2026-07-10: ZERO features are swept on Linux.** The Linux head builds and
launches in a VM, but `SupportsClickThrough = IsWindows` — there is **no click-through code** on Linux
(no X11 XShape/XFixes input region), **no input hooks**, and **no verified feature sweep**. The `Linux`
column on every WS0 lot row above is `[ ]` for this reason. The WS4 epic earns those marks.

| Linux subsystem | Status (2026-07-10) | WS4 path |
|---|---|---|
| Build + launch | ✅ builds + launches in VM | — |
| Click-through overlays (`IOverlaySurface.SetClickThrough`) | ❌ none (`IsWindows` gate) | X11 XShape/XFixes input region; Wayland best-effort |
| Global mouse / input hooks | ❌ none | evdev / XInput2 / XRecord alternatives |
| Video (libvlc) | 🚧 system-libvlc path unverified | system libvlc packages |
| Wallpaper / WebView / audio ducking | ❌ no verified equivalents | WebKitGTK/system-browser flow; PipeWire/PulseAudio ducking; layer-shell wallpaper — or degrade w/ recorded gap |
| Feature sweep | ❌ 0 of N features exercised | full headed sweep (the Linux mirror of board row #4) |

Per the spirit: **Windows never degrades to enable Linux; Linux degrades gracefully with a recorded gap
where the platform genuinely cannot do a thing.** Mechanism detail lives in `crossplatform-rebuild-plan.md`
(WS4); verification runbook in `linux-vm-testing.md`.

---

## UCE layer verification (dated harness snapshot, `--verify-layers`, 2026-07-04)

> **Note (2026-07-05):** this table is the dated harness snapshot of the 7 layers exercised on
> 2026-07-04. The port now registers **22 compositor layers** (9 session + 12 chaos + 1 attention-check);
> the current full registry + coverage verdicts live in `uce-coverage-audit.md` (reference-only). The
> snapshot below is intentionally not expanded, to preserve the original harness-run record. The full
> 22-layer registry + Z-order lives in `unified-compositor-engine-plan.md`.

Debug-only harness (`LayerVerification.cs`): one app run, each layer exercised through its OWNING
service, asserted (a) registered at the exact z-constant via `engine.GetLayer`, (b) `IsActive` after the
service call, (c) renders — GDI screen-capture MD5 (per-screen working-area + primary center-crop)
before vs during, (d) deactivates after the service stops it. Run:
`dotnet run --project .../CCP.Avalonia.Desktop.Windows.csproj -c Debug -- --verify-layers`
(exit 0 = all pass). 3-screen machine, dashboard hidden during probes.

| Layer | Z | Registered | Activated | Render delta | Teardown | Verdict |
|---|---|---|---|---|---|---|
| FlashLayer (`IFlashService.TriggerFlashOnce`, generated temp PNG) | 30 | ✅ exact | ✅ | ✅ DIFFER (full-screen) | ✅ clean on `Stop()` | **PASS** |
| SubliminalLayer (`FlashSubliminalCustom`, opacity forced 100) | 40 | ✅ exact | ✅ | ✅ DIFFER center-crop — **P0 capture-VISIBLE guardrail held** (WPF `WDA_NONE` contract; a no-delta here = main surface wrongly excluded) | ✅ clean (duration expiry) | **PASS** |
| BubbleLayer (`IBubbleService.Start`/`SpawnOnce` — ambient direct API) | 45 | ✅ exact | ✅ | ✅ DIFFER (full-screen) | ✅ clean on `Stop()` | **PASS** |
| BouncingTextLayer (`IBouncingTextService.Start(textPool)`) | 50 | ✅ exact | ✅ | ✅ DIFFER (full-screen) | ✅ clean on `Stop()` | **PASS** |
| BrainDrainLayer (`ShowOverlaySustained("braindrain")`, sequenced alone) | 55 | ✅ exact | ✅ | ✅ **NO-DELTA on 2/3 screens — P0 capture-EXCLUSION guardrail held** (`WDA_EXCLUDEFROMCAPTURE`; blur on the physical screen, invisible to capture) + `ExcludedWindowCount` 0→1 while active | ✅ clean on `HideOverlaySustained` | **PASS** |
| SpiralLayer (`ShowOverlaySustained("spiral")`) | 60 | ✅ exact | ✅ (decoded + active) | ✅ DIFFER (full-screen) | ✅ released to settings-held (profile `SpiralEnabled=true` re-applied — WPF-parity release, not a leak) | **PASS** |
| PinkTintLayer (`ShowOverlaySustained("pink")`) | 70 | ✅ exact | ✅ | ✅ DIFFER (full-screen) | ✅ released to settings-held (`PinkFilterEnabled=true` re-applied — WPF parity) | **PASS** |
| LockCard | 20 (reserved) | n/a — no `LockCardLayer` exists (grep-verified; z=20 unoccupied) | - | - | - | **SKIPPED** — lock card stays a window (interactive surface), nothing to verify |

Harness-isolation fix that fell out: `App.axaml.cs` `isHarnessRun` now also covers
`--verify-spiral`/`--verify-video`/`--verify-layers` — before, the user's `AutoStartEngine`/
`ForceVideoOnLaunch`/scheduler launch behaviors fired REAL sessions into verify runs.

NOT covered by the harness (needs human eyes — board row #4): WPF side-by-side timing/opacity/easing
parity, per-region click-through INPUT over each effect (board row #1), per-monitor placement on
mixed-DPI, lock-card (interactive window, not a layer). Layer bugs found: **none**.

---

## Cross-cutting (check these first — they affect many screens)

Runtime checklist for board row #4. Code-level parity is verified at the owning lot row; these need
headed exercise. Acceptance for each = behavior parity AND the perf gate (§ How to read this matrix).

- [ ] **Account login + premium gating** (OAuth providers, `HasPremiumAccess` gating) — lot 8; NEEDS-ENV (signed-in account).
- [ ] **START launches the mode** (session enters Running, effects start, stop returns to Idle) — lot 2/5.
- [ ] **Avatar reacts** (click → speech bubble) — lot 8; + the force-foreground GAP? (Re-verify queue).
- [ ] **Chaos run economy** end-to-end ("Down the Rabbit Hole": run lifecycle, boons, XP, narrative) — lot 6; RE-VERIFY (run engine ported S1–S9).
- [ ] **Per-region click-through (team review 2026-07-09):** only **color-filter + spiral** regions pass input; **subliminal, flash, brain-drain, bubbles, every active layer** CAPTURE input over their painted region (compositor capture mask + `AvaloniaMouseHook` swallow). Render path harness-verified 2026-07-04 (`--verify-layers`, table above) — that render evidence stands; only the INPUT expectation changed. **Remaining = impl (board row #1) + headed verify.**
- [ ] **Multi-monitor (N screens)** incl. mixed landscape+portrait, per-monitor scale; single-display setting honored — lot 3; CODE✓ (`AvaloniaScreenProvider`).
- [ ] **Per-mod theme re-skin** across all 5 (CCP Default, Bambi Sleep, Sissy Hypno, Dronification, Circe's Lock) — lot 10; NEEDS-ENV (eyes).
- [ ] **Performance** — startup, working set, effect frame rates vs WPF and `benchmark-optimized.json` — lot 3; NEEDS-ENV (re-baseline, board row #2).

## Tab views (`Views/Tabs`) — code verified at the owning lot; headed exercise = board row #4

- [ ] Awareness · [ ] Achievements · [ ] Animations · [ ] AppInfo · [ ] Assets · [ ] AvailableSubjects
- [ ] BambiTakeover · [ ] BlinkTrainer · [ ] CatalogueSubmissions · [ ] CompanionHub · [ ] Companion
- [ ] Deeper · [ ] DeeperHub · [ ] DeeperSubmissions · [ ] Enhancements · [ ] Haptics · [ ] Lab
- [ ] Leaderboard · [ ] LevelFeatures · [ ] Lockdown · [ ] Marquee · [ ] Patreon · [ ] PresetIO
- [ ] Presets · [ ] Profile · [ ] Quests · [ ] RemoteControl · [ ] Settings/Dashboard (feature cards, helper buttons, START/stop)
- [ ] SheListening (mapped 2026-07-02, `e140e0ff`) · [ ] WebcamEngine (Webcam tab)
- [ ] Placeholder (should NOT appear for any real tab — flag if it does)

## Feature controls (`Features`) — code verified at the owning lot; headed exercise = board row #4

- [ ] AppInfo · [ ] AttentionCheck · [ ] BouncingText · [ ] BubbleCount · [ ] BubblePop · [ ] Flash
- [ ] IntensityRamp · [ ] LockCard · [ ] MindWipe · [ ] PinkFilter · [ ] Scheduler · [ ] SchedulerRamp
- [ ] Spiral · [ ] Subliminal · [ ] System · [ ] Video · [ ] Visuals · [ ] Webcam
- [ ] FeatureSettingsPopup (hosted in `SessionEditorWindow`)

## Dialogs (`Dialogs`) — code verified at the owning lot; headed exercise = board row #4

- [ ] AssetSubmit · [ ] AttentionCheckSettings · [ ] AttentionTargetEditor · [ ] AwarenessPresetDetail
- [ ] CataloguePicker · [ ] CatalogueSubmit · [ ] ChatShortcutCapture · [ ] ColorEditor · [ ] ColorPicker
- [ ] CompanionPhraseEditor · [ ] CompanionPromptEditor · [ ] ContentPolicyWarning · [ ] DisplayName
- [ ] ExplicitContentAcknowledgement · [ ] Input · [ ] KnowledgeLinkEditor · [ ] LocalAiSetupWizard
- [ ] LockCardColor · [ ] Login · [ ] ModManager · [ ] OfflineUsername · [ ] OpenAiCompatibleSamplerSettings
- [ ] RoadmapConfirm · [ ] RoadmapDiary · [ ] RoadmapStart · [ ] RoadmapStep · [ ] SessionEdit
- [ ] TextEditor · [ ] UpdateNotification · [ ] UpdateProgress · [ ] UsernamePicker · [ ] Warning
- [ ] WebcamConsent · [ ] Welcome

## Windows (`Windows`) — code verified at the owning lot; headed exercise = board row #4

- [ ] AnnouncementPopup · [ ] AchievementPopup · [ ] BubbleCount · [ ] BubbleCountResult · [ ] BugReport
- [ ] EasterEgg · [ ] HelpVideo · [ ] LockCard · [ ] Mantra · [ ] MiniPlayer · [ ] ModCreator
- [ ] PinkRushPopup · [ ] PopQuiz · [ ] QuestCompletePopup · [ ] Quiz · [ ] QuizCategoryEditor
- [ ] QuizReportWindow · [ ] SeasonRecap · [ ] SessionComplete · [ ] SessionEditorWindow
- [ ] SessionLogHistory · [ ] Splash · [ ] TutorialOverlay · [ ] WebcamCalibration
- [ ] WebcamGazeTrackerWindow · [ ] WebcamLoadingSplash · [ ] WebcamQuickRecalWindow

## Deeper (`Views/Deeper`) — code verified at the owning lot; headed exercise = board row #4

- [ ] DeeperEditor · [ ] EnhancementPlayer · [ ] GazePicker · [ ] NewEnhancement · [ ] UrlPrompt

## Chaos overlays (`Chaos`) & AvatarTube (`AvatarTube`)

- [ ] Chaos overlays render + animate smoothly; input is per-region (team review 2026-07-09 — only color-filter/spiral regions pass, chaos FX capture over their painted region). RE-VERIFY: full-chaos-run behavior (board row #1 + row #4).
- [ ] AvatarTube: speech
- [ ] AvatarTube: AI chat, emotes, drag/scale/attach, reactions, fullscreen detection
