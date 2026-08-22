# WPF Surface Reachability

## Purpose

This is the active index for evaluating whether a WPF behavior is reachable in the greenfield
client. The shipping WPF product under `ConditioningControlPanel/` is read-only behavioral
evidence. The client preserves user-observable outcomes, not WPF classes, platform calls, or
historical implementation structure.

This document is intentionally an index rather than an execution diary. Current decisions,
contracts, tests, and source citations are the authority; retired workflow records and validators
are not evidence for a present product claim.

## Evidence Rules

1. Start from the WPF source that controls the user-observable behavior. Cite the exact source
   path and statement when recording a parity claim.
2. Classify the client state as `implemented`, `partial`, `blocked`, `not planned`, or `unknown`.
   A successful build, a stub, or a Windows-only no-op is never an implementation claim.
3. State Windows and Linux evidence separately. Where Linux behavior differs by X11 and Wayland,
   record that distinction rather than treating one environment as proof for the other.
4. Preserve capability boundaries. A feature may consume an admitted capability, but it must not
   silently create a second implementation of windowing, input, media, persistence, or network
   behavior inside a feature module.
5. Make unported behavior visible. Do not add a disabled control, fake success state, or silent
   fallback for functionality that has not been admitted.

## Current Authorities

| Concern | Current authority |
|---|---|
| Product priorities, status, and owner decisions | [task-board.md](task-board.md) and [owner-decisions.md](owner-decisions.md) |
| Architectural and platform decisions | [architecture.md](architecture.md) |
| Runtime capabilities and their typed states | [capability-inventory.md](capability-inventory.md) and [runtime-capability-contract.md](runtime-capability-contract.md) |
| Startup, shutdown, async work, and persistence | [startup-shutdown-contract.md](startup-shutdown-contract.md), [async-lifecycle-fault-contract.md](async-lifecycle-fault-contract.md), and [persistence-migration-contract.md](persistence-migration-contract.md) |
| Windows, Linux, focus, and window behavior | [window-behavior-manifest.md](window-behavior-manifest.md) |
| Visual and headed evidence | [verification-harness.md](verification-harness.md) |
| DTRH payload host behavior | [dtrh-admission.md](dtrh-admission.md) and [webview-dtrh-spike.md](webview-dtrh-spike.md) |
| For You Feed and Goon Game assessment | [fyp-census.md](fyp-census.md) and [goon-game-census.md](goon-game-census.md) |
| Haptic behavior inventory | [haptic-limb-census.md](haptic-limb-census.md) |
| Trainer Card assessment | [trainer-card-census.md](trainer-card-census.md) |

## Reachability Guide

### Dashboard and feature rows

The desktop dashboard is the current host for admitted feature rows. Each row must have a stable
identity, an honest availability state, and one command path for UI, keyboard, automation, and
settings changes. The quick-toggle and card contracts define the current interaction rules.

When WPF behavior depends on a session schedule, overlay surface, audio output, input capture, or
video playback, the client feature must use the corresponding admitted capability. A feature cannot
claim equivalence merely because it renders a similar control.

### Windows, overlays, and input

Window ownership, focus, activation, topmost state, click-through, taskbar presence, and capture
visibility are operating-system behavior. The window behavior manifest defines the current evidence
and the remaining Windows/X11/Wayland gates. Overlay and input claims require headed verification;
headless checks may only establish draw or structural facts.

### Media and audio

Media success is earned from a concrete operating-system read-back or typed capability outcome.
Decoded bytes alone do not prove visible presentation, and initiating playback does not prove an
audible result. The verification harness names the boundary between what automated checks establish
and what still requires headed or human evidence.

### Web payloads

Linked payload bytes remain owned by the WPF tree. The greenfield client hosts only the payloads and
routes explicitly admitted by current contracts. It does not copy or rewrite payload bytes merely to
make a port appear complete. Browser, bridge, origin, user-media, and permission behavior require
separate Windows and Linux proof.

### Privacy, network, and sensors

Any path that transmits data, accesses a camera or microphone, stores secrets, or selects a remote
provider needs an explicit owner decision, consent model, and typed refusal behavior. The current
For You Feed assessment retains the local WPF-equivalent gaze behavior as the default and requires
a separate admission process before a selectable third-party deep-learning gaze engine can appear.

## Updating This Index

When a new parity question arises:

1. Read the controlling WPF source and the existing client authority for that capability.
2. Record the behavior in the narrowest current contract or census that owns it.
3. Add focused tests or headed evidence appropriate to the behavior and platform claim.
4. Update the task board when a behavior remains blocked, partially implemented, or needs an owner
   decision.

Do not add retired workflow history, runner output, named harness assumptions, or archive paths to
this document. The current source, tests, contracts, and verification evidence are sufficient to
re-derive a live port decision.
