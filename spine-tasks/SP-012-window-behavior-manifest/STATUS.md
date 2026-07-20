# STATUS: SP-012 — build per-window behavior manifest

**Current Step:** Complete — all steps done
**Last Updated:** 2026-07-20 (worker session, pre-.DONE)

### Step 1: Retained-window inventory (completeness-checkable) + pre-approach consult
**Status:** ✅ Complete

- [x] STATUS.md updated before starting work
- [x] Mechanical enumeration of ALL WPF windows (auditable method + raw list in record.md)
- [x] Classification: RETAINED vs cross-referenced (overlay/AvatarTube/DTRH rows) vs excluded-with-reason
- [x] Pre-approach solo Fable 5 consult (verdict in record.md BEFORE checkbox)

### Step 2: Manifest authoring — named row per retained window
**Status:** ✅ Complete

- [x] `client/docs/window-behavior-manifest.md` — named row per window, all acceptance fields with File.cs:line evidence + evidence class
- [x] Observation procedure per field (Windows UIA/headed; Linux wmctrl/xprop/XGetImage)
- [x] Platform-matrix columns "pending owner question"; WSLg/X11 only observed env; Wayland §5.1 untouched
- [x] Shared-chrome constraints section (constraints only, no design)

### Step 3: Dashboard observability demonstrator (observation-only)
**Status:** ✅ Complete

- [x] Procedures executed against dashboard on Windows (headed, zero product-code change)
- [x] Same on WSLg/X11 (session facts, never backend claims)
- [x] Unobservable fields recorded "procedure defined, not demonstrable on this window"
- [x] Per-field demonstrator outcome in manifest; evidence pointers in record.md

### Step 4: Board reconciliation + pre-completion consult
**Status:** ✅ Complete

- [x] record.md complete (enumeration, classification, consult verdicts w/ actual answering model, engine-review presence, demonstrator evidence)
- [x] Pre-completion solo Fable 5 consult (verdict in record.md)
- [x] task-board.md manifest row → WIP with evidence + named remaining gates (never DONE)
- [x] STATUS.md accurate before .DONE

### Step 5: Testing & Verification
**Status:** ✅ Complete

- [x] Contract testCommand green (build + both test projects)
- [x] `git diff --check` clean
- [x] `git status --short` = File Scope only
