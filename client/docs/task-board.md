# Greenfield Client Task Board

This is the only live product queue for the Windows/Linux Avalonia client. It contains current
outcomes and blockers, not execution logs, archived task records, or workflow-specific state.

## Board Rules

- Reconcile this board with the source tree, current contracts, and verification evidence before
  claiming or completing work.
- A row is `OPEN`, `WIP`, `BLOCKED`, or `DONE`. `WIP` means implementation exists but a named
  acceptance condition is still unresolved; it is not a claim that the feature is complete.
- Each row names its required behavior evidence and verification. Update it with concise evidence
  or the exact blocker when the work changes.
- Concurrent work requires isolated workspaces, disjoint file scope, and one owner for shared
  files such as this board, test-floor metadata, release metadata, and composition roots.
- New rows need an observed parity gap, product defect, safety concern, or owner decision. Do not
  manufacture work from historical workflow artifacts.

## Current Queue

| Priority | Status | Outcome | Required Evidence |
|---|---|---|---|
| P0 | WIP | Admit a haptic provider or keep the capability honestly unavailable. | Owner decision on provider, dependency, privacy boundary, and supported device matrix; Windows and Linux verification for any admitted route. |
| P0 | OPEN | Prove the haptic stop reaches an admitted provider before teardown completes. | Wire-level test with an admitted provider; headed device evidence remains a separate human gate. |
| P0 | WIP | Deliver For You Feed webcam gaze with a user-selectable local WPF-equivalent engine and an admitted third-party deep-learning engine. | Owner decision recorded 2026-08-22: both engines are required and the choice is visible in webcam settings. Admit the named third-party provider/model, execution location, commercial rights, outbound-data policy, and Windows/Linux support; require current consent, separate calibration per engine, and no silent fallback. |
| P1 | OPEN | Deliver the remaining Trainer Card behavior that is meaningful without unapproved sharing. | WPF behavior evidence, current platform research, focused tests, and headed evidence for user-visible work. |
| P1 | OPEN | Define the safe, supported scope for Goon Game beyond local practice mode. | Owner decisions for partner media, voice, invitations, presence, and network boundaries; platform-specific evidence. |
| P1 | OPEN | Make real-desktop pointer coexistence verification reliable or explicitly machine-gated. | Reproduction of the current failure mode and a deterministic Windows desktop validation plan; do not weaken click-through assertions. |
| P1 | OPEN | Validate real media and multi-monitor video behavior on Windows, X11, and Wayland as separately claimed. | Decoding, geometry, compositing, audio, teardown, and headed capture evidence for each supported backend. |
| P1 | OPEN | Maintain an honest upstream payload inventory after legacy changes. | Compare the current source payload, client payload, and inventory; update consumed documents and run the inventory tests. |
| P2 | OPEN | Audit uncited intra-client source references and correct stale behavior claims. | A reproducible detector or a bounded review procedure that distinguishes current source evidence from historical references. |
| P2 | OPEN | Extend deterministic test-wait coverage to unbounded joins and injected timeout budgets. | A focused source guard plus tests that fail on the previously invisible shapes without adding timing sleeps. |

## Completion Criteria

A row is complete only when its behavior, implementation, required Windows/Linux evidence, and
recorded verification agree. A failed or unavailable gate keeps the row `WIP` or `BLOCKED`; it does
not become a successful claim through a compile, stub, no-op fallback, or workflow report.
