# Parity Re-Verification Triage (DoD #5) — STARTED 2026-07-05

**Scope:** `docs/skia-rebuild-goal.md` DoD item 5 — "every parity-matrix item re-verified `[x]` after
WS1-3." This triage is the honest precursor to earning `[x]`: it maps each unchecked matrix row to
its verification path so a headed/runtime pass (human or headed session) can execute efficiently.
**It does NOT mark `[x]`** — per the matrix's own reset rule, "a smoke-test visit alone never earns
`[x]`," and headless code-parity alone is the *rubric* check, not the *exercised-end-to-end* check.

## Key finding (read first)

1. **Code-level parity is already verified.** Lots 1-11 (parity-matrix WS0 review) each did a
   multi-agent adversarial review of their service area's code vs the WPF contract. The unchecked
   granular rows (tab views / feature controls / dialogs / windows / deeper / main-sync deltas) are
   unchecked because those lot verdicts live at the LOT row, not propagated to each surface row —
   NOT because the code is unverified.
2. **What remains for `[x]` is RUNTIME END-TO-END EXERCISE** (matrix rule: "exercised end-to-end in
   the running app against the WPF behavior contract"). This is the piece the voided 2026-06-23 marks
   lacked.
3. **Runtime exercise cannot be automated headlessly for non-compositor rows.** The only automated
   verification harnesses (`--verify-layers`, `--verify-video`, `--verify-spiral`, `--benchmark`)
   exercise the compositor/video — currently the co-agent's active UCE lane (do-not-touch per owner,
   2026-07-05). For all other surfaces there is no harness; earning `[x]` needs a **headed session**
   exercising each feature side-by-side with the WPF head.
4. **Implication:** DoD #5 completion is fundamentally a **headed/manual verification effort**, not a
   headless-agent task. This triage makes it tractable by stating, per row: the lot that verified the
   code, the exact runtime check still needed, and whether the row is in the blocked lane.

## Status legend (triage-specific; distinct from matrix `[ ]/[x]/🚧/❌`)

- **CODE✓** — code-level parity confirmed (lot citation + spot check); runtime exercise pending.
- **GAP?** — code-parity surfaced a *possible* divergence; runtime check must confirm whether it's a bug.
- **BLOCKED** — in the co-agent's UCE/bubble/video lane; defer until that lane settles.
- **NEEDS-ENV** — needs accounts / hardware / Linux / human-eyes not available headless.

## Section: Main-sync deltas ("ported from WPF 6.1.7; re-verify against current main")

| Row | Status | Code evidence | Runtime check still needed for `[x]` |
|---|---|---|---|
| Chaos "Down the Rabbit Hole" main menu (logo, How-to-Play, menu music, fog/intro FX, options) | **BLOCKED** | lot 6 (chaos run-engine S1-S9 complete) | Co-agent chaos/UCE lane. Defer. |
| Quest pool refresh (20 free + 20 patron + art) | **CODE✓** | lot 7 ("quest pool 20+20 (6.1.7 sync)" verified); quests in shared `CCP.Core/Services/Progression/QuestDefinitionService.cs` | Headed: open Quests tab, confirm 20 free + 20 patron load + render art. |
| Auth graceful browser-launch fallback (clipboard + dialog) | **CODE✓** | lot 8 (B1 OAuth → system browser via `IBrowserHost.OpenExternalAsync`; Linux/macOS system-browser degradation) | Headed: trigger OAuth, confirm system-browser launch + clipboard/dialog fallback when browser unavailable. |
| Subliminal double-flash fix (keep-alive windows, no Hide between flashes) | **CODE✓** (service) / **BLOCKED** (render) | lot 3 (`SubliminalLayer` always-on shared host; `SubliminalSolidMode` architecturally moot) | Render path = UCE lane. Headed: trigger back-to-back subliminals, confirm no flicker/Hide between. |
| Avatar focus-steal fix (`ShowActivated=false`, `SWP_NOACTIVATE`, no forced chat focus) | **CODE✓ + GAP?** | lot 8 (C1 avatar seam). `SWP_NOACTIVATE` mirrored (`AvatarTubeWindow.Windowing.cs`); `ShowActivated=false` (`AvatarRandomBubble.cs:104`); `ForceForegroundWindow()` (`ChatInput.cs:90`) called ONLY from `ShowInputPanel()` (user Ctrl+T, `axaml.cs:864`) — NOT the speech path → the no-forced-focus-on-speech fix IS honored. **GAP?:** Avalonia `ShowInputPanel` force-foregrounds the WINDOW (`AttachThreadInput`/`SetForegroundWindow`/`Activate`) vs WPF's textbox-`Focus()`-only — could yank foreground from *other apps* on Ctrl+T. | Headed: (a) confirm avatar speech/updates don't steal focus; (b) with focus in another app, press Ctrl+T, confirm whether foreground is yanked from the other app (the GAP?). |
| Bubble pace (FIELD_PACE) / ChaosArt / ChaosTuning / Achievement autonomy quests / Lab tab | **BLOCKED** (bubble/chaos) / **CODE✓** (achievement/Lab) | FIELD_PACE/ChaosArt/ChaosTuning = co-agent lane; achievement autonomy quests + Lab tab = lot 7/9 | Bubble/chaos pieces defer; achievement/Lab headed exercise. |

## Concrete divergence surfaced this pass (highest-value output)

**Avatar `ShowInputPanel` force-foreground (potential cross-app focus disruption).**
- WPF `AvatarTube/AvatarTubeWindow.ChatInput.cs:453-465`: `ShowInputPanel` → `FocusInputAfterLayout()`
  → `TxtUserInput.Focus()` + `Keyboard.Focus(...)` (textbox focus only, within the app).
- Avalonia `CCP.Avalonia/AvatarTube/AvatarTubeWindow.axaml.cs:862-866`: `ShowInputPanel` →
  `ForceForegroundWindow()` + `FocusInputAfterLayout()`. `ForceForegroundWindow()`
  (`ChatInput.cs:90-144`) uses Win32 `AttachThreadInput` + `SetForegroundWindow` + `BringWindowToTop`
  (+ `Activate()`), or a Topmost-pulse + `Activate()` on Linux/macOS.
- **Why it may be justified:** Avalonia's keyboard-focus model differs from WPF's; a topmost avatar
  window may not receive keyboard input without an explicit foreground grab. So this is plausibly a
  necessary platform adaptation for the chat textbox to be typeable.
- **Why it may be a regression:** `AttachThreadInput`+`SetForegroundWindow` is an aggressive grab that
  can steal foreground from *another application* the user is actively using when they press Ctrl+T
  (WPF's in-app `Focus()` cannot). On Linux/macOS the Topmost-pulse + `Activate()` has the same risk.
- **Not marked `[x]` or `❌`** — needs a headed observation (focus another app, press Ctrl+T, watch
  whether the avatar yanks foreground). If it does disrupt, file a task-board row (clamp the grab to
  in-app, or only force-foreground when the avatar already has app focus).

## Section: Cross-cutting (partial — high-value rows spot-checked)

| Row | Status | Code evidence | Runtime check still needed for `[x]` |
|---|---|---|---|
| Multi-monitor screen enumeration (per-screen bounds/DPI/primary/hot-plug) | **CODE✓** | `CCP.Avalonia/Platform/AvaloniaScreenProvider.cs`: enumerates `Screens.All`/`.Primary`, maps per-screen bounds + working area + scaling; `ComputeLayoutSignature` dedupes spurious `Screens.Changed` (count/bounds/scaling/primary); `DisplayChangeCoordinator.NotifyDisplayChange` mirrors WPF's layered-spawn quiesce; `EnsureAttached()` self-heals the early-construction attach race. Bounds are device-pixels in both heads (WPF `Screen.Bounds`, Avalonia `Screen.Bounds`); Scaling provided for DIP conversion. Arguably better-engineered than WPF's raw `Screen.AllScreens`. | Headed: multi-monitor rig, drag main window across monitors, trigger an overlay/effect, confirm it lands on the correct monitor at correct DPI; hot-plug a monitor mid-run, confirm overlays rebuild without blink. |
| Account login persists (DPAPI tokens, 24h cache) | **CODE✓** (storage) / **NEEDS-ENV** | lot 8 (auth): `ISecretStore` seam; Avalonia desktop head uses DPAPI on Windows, in-memory fallback elsewhere. | Headed + account: log in, restart, confirm session restored; verify token cache 24h expiry. Linux/macOS secret-store parity (currently in-memory fallback) is a known NEEDS-ENV gap. |
| START launches selected mode (session engine) | **CODE✓** (engine) / **BLOCKED** (effect render) | lot 5 (session engine). START→mode logic is Core/portable; modes that render effects route through the compositor (co-agent lane). | Headed: start each mode, confirm correct effect launches. Effect render = UCE lane. |
| Avatar reacts (companion speech/barks on events) | **CODE✓** (reactions) + **GAP?** (focus) | lot 8 (avatar seam). Reaction logic portable; **see Main-sync avatar row** for the Ctrl+T `ForceForegroundWindow` cross-app-focus GAP?. | Headed: trigger events (level-up, quest complete, session event), confirm avatar reacts; confirm speech does NOT steal focus. |
| Chaos economy (XP/buffs/economy ticks) | **BLOCKED** | lot 6. Chaos lane (co-agent). | Defer until chaos lane settles. |
| Overlays click-through (selected regions interactive) | **NEEDS-ENV** (human decision) | DoD #3b `AvaloniaMouseHook` click-swallow — needs owner product decision (port WPF swallow semantics vs accept+document gap). | Human decision + headed verification. |
| Per-mod theme reskin (every surface re-skins across 5 themes) | **NEEDS-ENV** (eyes) | DoD #7. Theming lane (non-conflicting). | Headed + eyes: load each theme × each mod, screenshot every surface. Multi-session. |
| Performance (startup/working-set/FPS vs WPF + benchmark-optimized.json) | **NEEDS-ENV** | DoD #3 (FPS floor 138.7 ≫ 30 HELD this session). benchmark-optimized.json comparison environmentally confounded on this machine (web-video decode failure). | Re-baseline on a machine where web video decodes (filed). |

## Saturation read (2026-07-05)

Spot-checked the two highest platform-divergence-risk Cross-cutting rows:
- **Multi-monitor**: solid (`AvaloniaScreenProvider` well-engineered) — CODE✓, no gap.
- **Avatar focus**: the one real divergence found this pass (Main-sync row, GAP?).

**Pattern:** Avalonia infrastructure rows (screen, auth storage seam, session engine) are CODE✓ and
well-engineered; the lots already did this work. The remaining `[x]` gap is **runtime exercise**, and
the only divergences headless code-parity surfaces are subtle **platform-adaptation** ones (Win32
focus P/Invoke). Continuing headless code-parity has **diminishing returns**; the high-value path to
close DoD #5 is a **headed runtime verification pass** (human or headed session) using this triage as
the checklist, plus headed confirmation of the avatar force-foreground GAP?.

## Next sections (multi-session; append below as each is triaged)

- [x] Cross-cutting (partial — 8 rows triaged above)
- [ ] Tab views (32)
- [ ] Feature controls (18)
- [ ] Dialogs (33)
- [ ] Windows (27)
- [ ] Deeper (5)
- [ ] Chaos overlays & AvatarTube (3) — mostly BLOCKED (co-agent lane)
