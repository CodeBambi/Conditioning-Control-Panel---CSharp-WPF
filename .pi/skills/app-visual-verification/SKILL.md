---
name: app-visual-verification
description: "Run targeted screenshot verification of a changed or suspicious greenfield Avalonia surface with image-capable Kimi K3, compare against its contract/WPF/img state reference, fix bounded visual defects, and recapture. Use only at explicit visual checkpoints: after a user-visible slice stabilizes, when appearance is reported wrong, before closing a visual task, and for milestone/release matrices. Never launches a slow whole-app screenshot sweep for routine edits."
argument-hint: "Surface or task-board row to capture and verify"
---

# app-visual-verification

Visual verification is a capture, inspect, adjust, recapture loop. Screenshots prove appearance, not interaction. Pair this skill with headed input/focus/window tests from the feature contract.

## Cost policy: targeted by default

Do not run a whole-app smoke test, all-tabs screenshot crawl, or all-layers harness after every change. The first attempt spent too long opening the whole app and still missed visible defects such as incorrect bubble borders. Its smoke/layer tests are historical tooling evidence, not a test strategy to inherit.

Use three levels:

1. **Fast routine gate, no screenshots:** after ordinary code edits, run the smallest affected build/unit/headless test. Do not launch the app unless the task requires headed behavior.
2. **Targeted visual gate:** after a user-visible slice is stable, when a visual regression is reported, or before closing that visual task, launch only the required surface/state and capture 1-3 focused images or a short sequence. This is the default use of this skill.
3. **Matrix visual gate:** run broader theme/language/scaling/platform screenshots only at a named UI milestone, before release, after shared theme/window/composition changes, or when explicitly required by the task board. It must still navigate/capture one state at a time, not blindly keep the whole app running through every screen.

The task spec must name the level and exact capture rows. If it does not, use level 1 for nonvisual work and level 2 for visual work.

## Model requirement

Use exact model `kimi-coding/k3` for image review. It is registered with `input: ["text", "image"]` and was selected by the owner for this skill.

- In a Pi session, switch to `kimi-coding/k3` before inspecting screenshots, or run a dedicated K3 review session with the screenshot attached.
- In a dynamic workflow, route the visual-review agent with `model: "kimi-coding/k3:high"`, not only a generic tier.
- Do not ask a text-only subagent to judge pixels from filenames or logs.

For a deterministic one-shot review, run [invoke-k3-visual-review.ps1](./scripts/invoke-k3-visual-review.ps1) with one or more `-ImagePath` values and a focused `-Prompt`. It launches Pi in print mode with exact provider/model `kimi-coding/k3` and attaches each image as real image input.

## Authority and references

Read before capture:

1. the matching row in `client/docs/task-board.md`;
2. relevant behavior in `client/docs/capability-inventory.md`;
3. relevant decisions in `client/docs/architecture.md`;
4. `client/docs/port-workflow.md` for consultation and evidence rules;
5. `dashboard-design` for themed surfaces;
6. WPF and `img state/` only as visual/behavior evidence.

The first Avalonia attempt can provide screenshot tooling lessons, but its screenshots are not a completion baseline.

## Choose the capture kind

### In-app/window render capture

Best for deterministic dashboard, tab, popup, dialog, and window content. The app renders a named `Window`/surface to PNG through Avalonia, after layout settles. This avoids unrelated desktop content and is portable in principle.

The first attempt demonstrates only the low-level render-to-PNG pattern in `ConditioningControlPanel/tests/CCP.Avalonia.Desktop.Windows.Smoke/SmokeTestRunner.cs` `RenderScreenshotAsync`: update layout, create a `RenderTargetBitmap`, render, and save. Do not copy its whole-app traversal, smoke/layer strategy, or harness wholesale. Implement the smallest targetable greenfield capture seam when the client project exists.

Limitations: native child controls, WebView/video surfaces, transparent/topmost overlays, and OS chrome may not appear correctly in an offscreen render.

### Real desktop/window screenshot

Required for native chrome, ownership/z-order, WebView, video, transparency, overlays, multi-monitor placement, taskbar/Alt-Tab-visible behavior, and rendered composition. Use a platform capture mechanism approved through `avalonia-research`.

- Windows: capture the target window or virtual desktop; include all monitors when testing geometry.
- Linux: capture through an approved X11 or Wayland mechanism. Wayland may require portal/user approval. Record backend, compositor, scaling, and permission behavior.
- Never assume a Windows GDI capture path is portable.

### Short frame sequence

Required for AvatarTube, GIFs, transitions, loading states, video, spiral, tint changes, and other animation. Capture at least two appropriately separated frames or a short sequence. A single screenshot cannot prove liveness.

## Artifact rules

- Store greenfield visual artifacts under `client/artifacts/visual/<task-id-or-surface>/` unless a task specifies another ignored location.
- Use deterministic names: `<platform>-<backend>-<theme>-<scale>-<surface>-<state>-<sequence>.png`.
- Write a small `manifest.json` or Markdown note beside captures with app commit/tree state, platform/backend, monitor bounds/scales, theme, language, surface/state, capture method, and expected reference.
- Do not commit artifacts by default. Commit only deliberately approved golden/reference images.
- Screenshots can contain private content. Before capture, use deterministic safe data; close unrelated apps/notifications; never capture secrets, tokens, private URLs, user media, camera frames, chat history, or personal desktop content.

Read [capture-matrix.md](./references/capture-matrix.md) for the required state matrix.

## Capture procedure

1. Start from a clean or isolated worktree and record the task row.
2. Build and launch the greenfield client using current project commands. Do not launch the first attempt as the subject.
3. Set deterministic test data, language, theme, window size/position, and monitor layout.
4. Navigate to the exact state using user-visible actions or an approved deterministic verification harness.
5. Wait for layout and asynchronous content to settle. Record the wait/ready signal; do not use arbitrary delays when the app can expose readiness.
6. Capture the correct kind above.
7. Confirm the PNG exists, has nonzero dimensions, and is not wholly blank/transparent.
8. For animations, capture a sequence and verify frame hashes/pixels differ when motion is expected.
9. Attach the image(s) to a K3 review with the contract and reference images.

## K3 review prompt

Ask K3 for structured findings, not aesthetic improvisation:

- identify the surface/state/platform/theme;
- compare to the named WPF or `img state/` reference and the client contract;
- separate definite defects from preferences and unknowns;
- inspect clipping, overlap, alignment, spacing, hierarchy, text truncation, contrast, theme leakage, card on/off/locked state, scroll affordance, native chrome, focus indication, scaling, transparency, z-order, black/blank regions, duplicate avatars, stale frames, and monitor placement;
- name the smallest likely owning component/style/layout rule;
- propose a falsifiable recapture after the fix;
- never infer interaction, audio, focus behavior, click-through, or animation liveness from one still image.
- when a WPF comparison differs, classify whether the cause is an intended greenfield change or a known Avalonia migration difference (selectors/pseudo-classes, template placement, `IsVisible`, BoxShadow, layout transforms, asset URI, window chrome/scaling) before proposing a fix.

The Pi Avalonia MCP may review the small AXAML/layout/selector snippet behind a captured defect, but it cannot inspect rendered pixels or replace K3, headed interaction, native-window capture, or target-platform evidence. Treat its diagnosis as a hypothesis and reject v11/WPF-shaped fixes.

Recommended response shape:

```text
VERDICT: PASS / FIX / BLOCKED
Definite defects:
- observation; contract/reference; likely owner; recapture test
Possible issues:
- what additional state/capture is needed
Matches:
- important requirements visibly satisfied
Interaction gates still required:
- headed checks screenshots cannot prove
```

## Fix loop

1. Triages K3 findings against the contract and source. The model can be wrong; primary evidence wins.
2. Fix only definite in-scope defects. Do not redesign unrelated surfaces.
3. Run build/tests affected by the change.
4. Recapture the identical state with the same dimensions/theme/data.
5. Ask K3 to compare before/after/reference.
6. Repeat with a bounded attempt count from the task spec. If the same defect survives two focused attempts, stop and use council/`avalonia-research` rather than thrashing.
7. Run headed interaction gates after appearance passes.
8. Record capture paths, K3 verdict, accepted/rejected findings, and interaction evidence in `client/docs/task-board.md`.

## Required visual sweeps

For a targeted gate, use only the changed/suspicious state plus one adjacent state likely to regress. User-facing milestone/release reviews may include the broader matrix below, but never run it routinely:

- all five built-in themes;
- supported languages with long strings;
- minimum, normal, and large window sizes;
- 100%, 125%, 150%, and mixed monitor scaling where available;
- primary and secondary monitor placement;
- enabled, disabled, locked, hover/focus, loading, empty, error, and overflow states;
- every feature popup at top, middle, and bottom scroll positions;
- modal/modeless windows with owner visible/minimized/restored;
- AvatarTube static, GIF, idle emote, speech/reaction, attached, and detached states;
- video plus black bars, spiral, and bounded tint;
- Linux X11 and Wayland captures for claimed support.

### Defect-focused examples

- Bubble rendering change: capture one isolated bubble at normal scale, one hovered/clickable state, and one mixed-DPI monitor state. Inspect circle edge, unintended square/rectangular border, clipping, alpha fringe, glow, and click geometry alignment. Do not run every tab.
- Feature popup change: capture top and bottom overflow positions at the affected scale/theme, then separately exercise wheel/thumb/focus behavior. Do not screenshot unrelated dialogs.
- Shared theme change: targeted five-theme capture of the one affected surface; defer the full-app theme matrix to a milestone.
- Avatar animation change: short sequence of the affected mode only; do not run the dashboard/window suite.
- Composition change: capture the exact layer combination and affected monitors; do not invoke unrelated UI smoke screens.

## What screenshots cannot prove

Always supplement screenshots for:

- right-click quick-toggle and service side effects;
- wheel/touch/keyboard/thumb scrolling;
- focus stealing/restoration and modality;
- click-through and click leakage;
- audio playback;
- animation/video frame progression;
- cleanup after close, crash, panic, or display change;
- taskbar/Alt-Tab behavior unless captured/observed explicitly;
- accessibility semantics.

## Stop conditions

Stop and file a blocker when the required platform capture is unavailable, Wayland permission cannot be obtained, native content is missing from the chosen capture method, the image contains sensitive data, reference/contract is ambiguous, K3 lacks the actual image input, or fixes require an architecture/product decision.

## Related skills

- `dashboard-design`, `port-feature`, `wpf-parity`, `avalonia-research`, `overlay-clickthrough`, `unified-compositor-engine`, `port-audit`.
