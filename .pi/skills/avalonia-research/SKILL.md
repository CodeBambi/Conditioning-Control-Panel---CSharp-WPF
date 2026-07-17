---
name: avalonia-research
description: "Mandatory current-source research for every Avalonia v12 API, AXAML, styling, rendering, input, windowing, lifecycle, packaging, native interop, and third-party package decision in the greenfield Windows/Linux client under client/. Rejects stale v11 guidance and old-port implementation assumptions, records evidence in client docs, and requires council review for dependency/platform architecture."
---

# avalonia-research

Applies to greenfield client work under `client/`. The legacy WPF application is behavioral evidence. `ConditioningControlPanel/CCP.*` and old port plans are historical evidence only.

## Why

Avalonia v12 is current and training data, old tutorials, samples, and packages may target incompatible APIs or behavior. Never encode a floating patch version here. Read the approved candidate/baseline from `client/docs/architecture.md` and project files once they exist, then verify current releases.

## Research protocol

### 1. Read client authority first

Read the relevant sections of:

- `client/docs/architecture.md`;
- `client/docs/capability-inventory.md`;
- `client/docs/first-attempt-lessons.md`;
- `client/docs/task-board.md`;
- `client/docs/port-workflow.md`.

Determine the user-observable outcome and platform acceptance before researching a mechanism.

### 2. Inspect local historical evidence narrowly

WPF may establish behavior. The first attempt may reveal an API experiment, defect, or measurement, but every claim is a hypothesis until reverified. Never promote its helper, package, interop, compositor, timer, or workaround directly into architecture.

### 3. Verify current primary sources

Use, in order:

1. official Avalonia v12 docs and API reference, beginning with [Migrating from WPF](https://docs.avaloniaui.net/docs/migration/wpf/) and the [WPF to Avalonia cheat sheet](https://docs.avaloniaui.net/docs/migration/wpf/cheat-sheet) for WPF-shaped work;
2. official Avalonia repository source, releases, issues, PRs, and discussions;
3. official platform/library documentation;
4. NuGet/npm/package repository and license metadata;
5. minimal restore/build/runtime spike on Windows and Linux.

Reject v11/v10 answers unless current v12 sources explicitly confirm them. Record exact version/date/URL in the task evidence, not as a permanent skill constant.

### 3a. Use the Pi Avalonia MCP as a secondary reviewer

When the `decriptor/AvaloniaUI.MCP` server is available in Pi, use its focused tools after reading the client contract and before finalizing Avalonia-shaped code. It is useful for:

- heuristic AXAML/XML review through its XAML validation tool;
- a second pass over binding, layout, styling, startup, threading, accessibility, and performance concerns;
- control-reference discovery and candidate selector/theme/responsive-layout ideas;
- identifying questions that require deeper official-doc or runtime verification.

Do not treat MCP output as primary evidence or paste generated output directly into `client/`. The upstream server currently has one public commit from June 2025, no published GitHub release, and pins Avalonia `11.3.1`; its validator uses `XDocument` plus hand-written rules rather than the Avalonia v12 compiler. Confirm every recommendation against 2026 official v12 docs/API, selected project packages, a real build, and headed Windows/Linux behavior.

Known unsafe or stale upstream examples include marking `DockPanel`, `UniformGrid`, and `Viewbox` unavailable; preserving WPF `RelativeSource Self`; generating WPF `Storyboard`, `EventTrigger`, `VisualStateManager`, and `Style TargetType`; and emitting questionable virtualization and custom-control APIs. Never use its WPF conversion, project/architecture generation, custom-control generation, animation/storyboard generation, or package recommendations as implementation authority.

The upstream server also configures a fixed external Sentry DSN with tracing and profiling at startup. Until the local Pi installation is verified to disable outbound telemetry, do not send proprietary source, secrets, paths, user data, or security/privacy-sensitive material to it. If the MCP is unavailable or not admitted, continue with official sources; absence is not a blocker.

The official migration index was updated in 2026 and identifies the major conceptual shifts: CSS-like selectors/classes/pseudo-classes instead of WPF triggers, `DataTemplates` collections, strongly typed Avalonia properties, pointer events and routing strategies, and control/package differences. The cheat sheet was updated in 2026 and is the default translation reference. Verify deeper topic pages before coding; a compact mapping is not a runtime guarantee.

The Avalonia blog article [The Expert Guide to Porting WPF Applications to Avalonia](https://avaloniaui.net/blog/the-expert-guide-to-porting-wpf-applications-to-avalonia) is older methodology guidance. Use its dependency/platform audit, incremental vertical-slice, small-commit, early-on-screen, and cross-platform testing advice. Do not import its literal "port, not refactor", comment-out/uncomment workflow, per-view estimates, XPF/Hybrid suggestion, macOS/mobile scope, or generic package recommendations when they conflict with the owner-approved greenfield `client/` architecture.

### 4. Package admission

Before adding a dependency verify:

- exact stable version and target framework;
- explicit Avalonia v12 compatibility;
- Windows and Linux support, including native runtime requirements;
- maintenance/release state and repository ownership;
- license and distribution consequences;
- package size, transitive dependencies, trimming/AOT implications where relevant;
- whether framework/standard-library/native platform features already solve it;
- runtime behavior through a focused spike, not restore alone.

A newest release targeting v11 is rejected. A Windows-only implementation cannot satisfy a Windows/Linux contract. Pin admitted versions.

### 5. Platform research

Research Windows and Linux separately. For Linux distinguish X11 and Wayland, distribution packages, compositor/window manager behavior, graphics stack, native handles, and fallback semantics. A no-op, external browser, or disabled feature is not equivalent support.

### 6. Record evidence

Write durable findings to the smallest applicable document:

- `architecture.md`: approved mechanism/package/platform decisions and alternatives;
- `capability-inventory.md`: user-facing/platform consequence;
- `first-attempt-lessons.md`: historical claim, current verification, `ACCEPT`/`ADAPT`/`REJECT`;
- `task-board.md`: spike, blocker, result, or product decision;
- a future `client/docs/contracts/` file when one exists.

Cite URLs. Label claims `VERIFIED`, `INFERRED`, `UNVERIFIED`, or `PRODUCT DECISION REQUIRED`. Fix stale client docs when primary evidence contradicts them.

## Consultation

After primary research, follow `port-workflow.md`:

- solo for bounded API interpretation;
- council before dependency admission, native interop, rendering/windowing/input architecture, security/privacy, or cross-platform capability decisions;
- debate when two materially different approaches remain viable.

Supply sources and alternatives. Inspect the fit ledger. Consultation is advisory and cannot replace runtime evidence.

## Stop conditions

Stop before implementation if reliable v12 evidence is absent, sources conflict, Linux behavior is unknown, a package fails admission, an unstable API is the only path without an approved spike, or the research would broaden safety/privacy. File the exact blocker in `client/docs/task-board.md`.

## Related skills

- `wpf-parity`, `port-plan`, `port-feature`, `overlay-clickthrough`, `unified-compositor-engine`.
