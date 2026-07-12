# macOS IForegroundWindowTitleProvider Implementation Contract

**Date:** 2026-07-12  
**Scope:** Hard-seam macOS foreground window title for the awareness engine  
**Status:** Authoritative design contract (read-only research artifact; no code changes)  
**Closes:** macOS side of AI-1/AI-2/AI-10 (awareness bring-up, readiness map item #3)  
**Siblings:** `linux-foreground-title-contract.md` (template + shared rules), `macos-overlay-contract.md` (shared interop + CI lane), `macos-framesource-contract.md` (shared TCC policy)

This document specifies the behavior contract, backend architecture, and implementation
slices for macOS foreground window title detection (`IForegroundWindowTitleProvider`).
Per the readiness-map governing principle: multi-backend, runtime-selected, graceful
fallback, CI-verified. On macOS the backends differ not by display server (there is
one) but by **which TCC permission gates them** — and that distinction is the design
core of this contract.

> **The correction this contract leads with:** the two candidate APIs are NOT both
> Accessibility-gated. **AXAPI requires TCC Accessibility**; **CGWindowList window
> TITLES (`kCGWindowName`) require TCC Screen Recording** (since macOS 10.15 —
> web-verified, §7.1-VERIFIED row 1), and the CGWindowList call itself NEVER triggers a
> prompt (titles silently come back null when ungranted). This asymmetry creates a
> permission-reuse synergy with the framesource contract: a user who granted Screen
> Recording for capture features gets window titles for free, with no second prompt.

**The honest macOS reality:** with neither permission granted, the title is `null` and
awareness classifies `Unknown` — exactly the stock-GNOME degrade from the Linux
contract. An ambient app never spams TCC prompts to fix that (§5).

---

## 1. IForegroundWindowTitleProvider Behavior Contract

Interface at `CCP.Core/Platform/IForegroundWindowTitleProvider.cs` — UNCHANGED:

```csharp
public interface IForegroundWindowTitleProvider
{
    /// <summary>
    /// Title of the current foreground window, or null/empty when unavailable.
    /// The returned string must never be persisted or logged by callers — it is
    /// memory-only input for activity classification.
    /// </summary>
    string? GetForegroundWindowTitle();
}
```

The seam is **synchronous and cheap-per-call**: `AwarenessService`
(`CCP.Core/Services/Awareness/AwarenessService.cs`) polls every 1.5s from a
`System.Threading.Timer` threadpool callback. Same consequence as Linux: the call must
be a fast in-memory read tolerant of arbitrary (and potentially overlapping) threadpool
threads — which on macOS collides with AppKit/AX threading rules and forces the
snapshot model in §3.1.

### 1.1 Behavioral Requirements (cited from `CCP.Avalonia.Desktop.Windows/Platform/WindowsForegroundWindowTitleProvider.cs`)

| Requirement | Windows Implementation | Citation | macOS disposition |
|---|---|---|---|
| **Title only** | Window title text — NO process name, NO PID | `WindowsForegroundWindowTitleProvider.cs:12,14` + interface doc | AX `kAXTitleAttribute` / CG `kCGWindowName` only; PID is used internally to find the window but never returned (§1.2) |
| **Unicode** | `GetWindowText` with `CharSet.Unicode` | `WindowsForegroundWindowTitleProvider.cs:20-21` | CFString → UTF-16 marshal; full Unicode native |
| **Bounded read** | 512-char buffer — truncate, never overrun | `WindowsForegroundWindowTitleProvider.cs:29-30` | Truncate to 512 chars post-marshal (parity + bounds the privacy-governed memory) |
| **Null-safe** | `null` when no foreground window | `WindowsForegroundWindowTitleProvider.cs:26-27` | `null` on: no frontmost app, AX error, ungranted TCC, redacted CG name |
| **Synchronous, cheap** | Direct Win32 call | whole method `:23-32` | Lock-protected snapshot read; refresh happens off the seam call path (§3.1) |

### 1.2 Privacy Contract (HARD CONSTRAINT — extended for CGWindowList)

From the interface doc and the `AwarenessService` header: the raw title lives in memory
only, never disk, never network, never logged — log lines carry the derived detected
name only.

**CGWindowList extension (mirror of the Linux Wayland-map extension):**
`CGWindowListCopyWindowInfo` returns metadata for EVERY on-screen window — titles,
owner names, PIDs, bounds — not just the foreground one. The hard line covers the whole
result:

- The window-info array is processed and released inside one refresh call; no map of
  all titles is retained (unlike Wayland, macOS is poll-based — retain only the single
  derived foreground title snapshot).
- Owner PID and owner name (`kCGWindowOwnerPID`/`kCGWindowOwnerName`) may be *used*
  internally to select the frontmost app's window, but the seam returns the TITLE
  string only — no app name, no PID (title-only contract).
- Selector/fallback logging carries backend names, TCC states, and reasons ONLY —
  never title content. CI includes the negative log-grep assertion (Linux contract
  Slice C convention).

### 1.3 Consumer Integration

Same two degrade levels as Linux §1.4, same ruling: **register the provider
unconditionally** on macOS and let the backend chain degrade. The user sees "awareness
on, activity unknown" until a permission is granted, and granting one mid-session
upgrades capability without rewiring (the selector re-probes on grant — §5.3).

---

## 2. macOS Backend Architecture

### 2.1 Runtime Backend Selection — keyed on TCC state, not environment

There is one window system; what varies at runtime is which permissions the user has
granted. Preflight both SILENTLY (neither preflight prompts):

- `AXIsProcessTrusted()` — Accessibility granted?
- `CGPreflightScreenCaptureAccess()` — Screen Recording granted? **REFUTED as 10.15
  (web pass 2026-07-12, §7.1 row 3): the docs say 10.15 but the function is NOT
  actually implemented on Catalina — calling it on 10.15 crashes (Apple-confirmed bug
  r.72786842; workaround is to restrict use to macOS 11+). Design fix: the interop
  wrapper resolves the symbol via `dlsym` and gates on `operatingSystemVersion >= 11`;
  when unavailable, treat as ungranted. Moot in practice for CCP (min supported macOS
  for the port is ≥ 12), but the guard is cheap and prevents a crash class.**

```
MacOSTitleProviderBackendSelector (at provider init; re-probed on feature enable and
                                   on a slow re-check timer after a denial — §5.3)
  1. AXIsProcessTrusted() == true
       → AxApiTitleBackend            (full: focused window of frontmost app, event-fresh)
  2. CGPreflightScreenCaptureAccess() == true
       → CGWindowListTitleBackend     (full titles via kCGWindowName; poll-derived)
  3. Neither granted
       → FallbackTitleBackend         (returns null; awareness classifies Unknown)
```

AX outranks CG when both are granted: it reads the *focused window* directly (CG
requires inferring "foreground" from layer-0 ordering, §4.2) and returns one window's
title rather than enumerating all of them (privacy-minimal, §1.2).

### 2.2 Backend Fallback Chain

| Priority | Backend | Capabilities | TCC gate | When Selected |
|---|---|---|---|---|
| 1 | `AxApiTitleBackend` | Full: frontmost app's focused window title | **Accessibility** | granted |
| 2 | `CGWindowListTitleBackend` | Full titles; foreground inferred from front-to-back order | **Screen Recording** (for `kCGWindowName` only) | granted (incl. "for free" via framesource grant) |
| 3 | `AppNameTitleBackend` | Coarse: frontmost app display name via `NSWorkspace` | **NONE** | PARKED — contract deviation, owner ruling required (§4.3) |
| 4 | `FallbackTitleBackend` | Returns `null` | none | no grants |

**Guarantee:** never crash; never prompt from inside the provider; ungranted macOS
gives `null` → awareness runs with `Unknown`.

### 2.3 Seam Structure

```
CCP.Core/Platform/
└── IForegroundWindowTitleProvider.cs        # UNCHANGED

CCP.Avalonia.Desktop.macOS/Platform/
├── MacOSForegroundWindowTitleProvider.cs    # seam impl delegating to selected backend
├── MacOSTitleProviderBackendSelector.cs
├── TitleProviderBackends/
│   ├── IMacOSTitleProviderBackend.cs        # + IDisposable
│   ├── AxApiTitleBackend.cs
│   ├── CGWindowListTitleBackend.cs
│   ├── AppNameTitleBackend.cs               # PARKED (§4.3)
│   └── FallbackTitleBackend.cs
├── MacOSPermissionService.cs                # SHARED with framesource contract: preflight,
│                                            #   request-on-explicit-enable, settings deep links (§5)
└── Interop/
    ├── ObjCInterop.cs                       # SHARED (overlay contract §2.4) — extend, do not fork
    ├── AxInterop.cs                         # NEW: AXUIElement* (ApplicationServices/HIServices)
    └── CoreGraphicsInterop.cs               # SHARED with framesource: CGWindowList*, CFRelease,
                                             #   CGPreflight/RequestScreenCaptureAccess
```

`MacOSPermissionService` and `CoreGraphicsInterop` are SHARED infrastructure with
`macos-framesource-contract.md` — one implementation per process, whichever slice lands
first builds it (Linux `X11Interop` rule).

---

## 3. Threading and Refresh Model (applies to both live backends)

### 3.1 Snapshot model (normative)

AX calls are officially main-thread-recommended; their thread-safety off-main is
undocumented **[confidence: medium — Apple docs are silent; community reports mixed;
§7.1 row 4]**. `CGWindowListCopyWindowInfo` is a CoreGraphics C call generally safe
off-main **[confidence: medium-high]**. Rather than gambling per-API:

- A **refresher** runs on a ~1s cadence while awareness is active, on the AppKit main
  thread via `Dispatcher.UIThread.Post` (cheap: one AX read or one CG enumeration).
- The refresher updates an immutable `string? _snapshot` swapped atomically
  (`Volatile.Write`).
- `GetForegroundWindowTitle()` (threadpool, 1.5s) is a **snapshot read only** — no
  native calls on the seam path. O(1), thread-safe, at most ~1s stale — well inside the
  awareness engine's change-detection tolerance (same design as the Linux Wayland
  backend's event snapshot).
- `Dispose()` stops the refresher and clears the snapshot.

If §7.1 row 4 verification later proves AX is safe from a single dedicated background
thread, the refresher may move off the UI thread — an optimization, not a redesign.

### 3.2 Failure demotion

A refresher call that throws or returns AX errors repeatedly (e.g. permission revoked
mid-session — TCC revocations apply immediately to AX **[confidence: medium]**)
demotes: re-run the selector; if no backend qualifies, swap to `FallbackTitleBackend`,
snapshot `null`, one log line. Never crash, never prompt.

---

## 4. Backend Designs

### 4.1 AxApiTitleBackend (Accessibility)

Per refresh, all C-API calls via P/Invoke on
`/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices`:

```
1. frontmost app PID: NSWorkspace.sharedWorkspace.frontmostApplication.processIdentifier
   (objc_msgSend; zero TCC) — null app → snapshot null
2. appElement = AXUIElementCreateApplication(pid)
3. AXUIElementCopyAttributeValue(appElement, kAXFocusedWindowAttribute, out windowElement)
   — error (kAXErrorAPIDisabled = not trusted; kAXErrorNoValue = no focused window) → null
4. AXUIElementCopyAttributeValue(windowElement, kAXTitleAttribute, out titleCFString)
5. CFString → managed string (CFStringGetLength + CFStringGetCharacters, UTF-16),
   truncate 512
6. CFRelease everything created/copied (Copy/Create rule — every AXUIElementCreate*
   and CopyAttributeValue result is owned by us)
```

Notes:
- Alternative step 1-3: `AXUIElementCreateSystemWide()` + `kAXFocusedApplicationAttribute`
  — one fewer ObjC hop but returns the focused *application* element anyway; the
  NSWorkspace route is simpler to marshal. Either is acceptable; pick one in Slice B.
- `kAXErrorAPIDisabled`/`kAXErrorNotImplemented` per-call are the untrusted/unsupported
  signals — map to `null`, count toward demotion (§3.2). **Web-verified addendum:
  untrusted processes also commonly get `kAXErrorCannotComplete` from
  `AXUIElementCopyAttributeValue` (developer.apple.com/forums/thread/794253) — treat
  all three as the not-trusted/unavailable family.** Some apps (notably some games
  and Electron configurations) return no AX title — `null`, classify Unknown.
- **Sandbox note:** AXUIElement APIs for other apps do not work from a sandboxed app;
  CCP ships unsandboxed (direct download, not App Store) — record this as a
  distribution constraint **[confidence: high — App Store sandbox forbids cross-app AX]**.

### 4.2 CGWindowListTitleBackend (Screen Recording)

Per refresh, pure C API on CoreGraphics:

```
1. frontmost PID via NSWorkspace (as §4.1 step 1)
2. CGWindowListCopyWindowInfo(kCGWindowListOptionOnScreenOnly |
                              kCGWindowListExcludeDesktopElements, kCGNullWindowID)
   — returns CFArray of CFDictionary, front-to-back order
3. First entry where kCGWindowOwnerPID == frontmost PID AND kCGWindowLayer == 0
   (layer 0 = normal app windows; skips menu bar items, our own overlay at level 1000,
   status items)
4. kCGWindowName from that entry → CFString → managed, truncate 512
5. kCGWindowName missing/empty (ungranted OR the app set no title) → null
6. CFRelease the array
```

Notes:
- Front-to-back ordering of `kCGWindowListOptionOnScreenOnly` makes "first matching
  layer-0 window of the frontmost app" a sound foreground proxy **[ordering VERIFIED
  §7.1 row 5 — the SDK header states "in order from front to back"; sheet/modal edge
  cases remain Slice C unit-probe territory]**.
- **Never prompts** (verified — §7.1-VERIFIED row 1): with Screen Recording ungranted,
  `kCGWindowName` is simply absent. The selector's preflight prevents ever selecting
  this backend ungranted, so "absent name" in a selected backend means "untitled
  window", not "no permission".
- Excludes our own windows (`kCGWindowOwnerPID == getpid()`) defensively — the overlay
  should already be filtered by the layer check.
- This backend is what makes the **framesource synergy** real: Screen Recording granted
  for capture features (see `macos-framesource-contract.md` §4) silently lights up
  awareness titles with zero additional prompts.

### 4.3 AppNameTitleBackend — PARKED pending owner ruling

`NSWorkspace.frontmostApplication.localizedName` (zero TCC) would give awareness the
frontmost *application display name* ("Google Chrome", "Discord") with no permission at
all — and the awareness classifier keys largely on app-name keywords, so this covers a
large fraction of classification value for free.

**Why parked:** the seam and privacy contract say TITLE only, "no process name". An app
display name is arguably neither a window title nor a process name; the Linux contract
resolved the analogous temptation (returning `app_id`) conservatively — title-only.
Shipping this needs an explicit owner ruling that widens the seam contract wording
(e.g. "title, or coarse app display name where titles are permission-gated") plus an
interface-doc update in Core. Until ruled: **honest null beats contract creep.**
Documented here so nobody lands it as a "quick win" without the ruling (Linux §4.3
convention: parked with named unpark criteria).

### 4.4 FallbackTitleBackend

Identical shape to the Linux one: returns `null`, logs the reason once
("no Accessibility or Screen Recording permission — awareness will classify activity
as Unknown; enable in Settings → Awareness"), reason strings carry TCC state names
only, never title content. Also the demotion target (§3.2).

---

## 5. TCC Permission UX (CRITICAL judgment — shared policy with the framesource contract)

An AMBIENT app cannot spam permission prompts. Normative policy, owned by the shared
`MacOSPermissionService`:

1. **Never prompt at launch, never prompt from a poll path.** Preflights
   (`AXIsProcessTrusted()`, `CGPreflightScreenCaptureAccess()`) are silent and run
   freely; *requests* are user-action-gated.
2. **Prompt only on explicit user action:** the user enabling awareness (or a
   capture-dependent feature) in settings. First enable of awareness offers ONE choice
   surface explaining the two paths: "Best: grant Accessibility" →
   `AXIsProcessTrustedWithOptions(kAXTrustedCheckOptionPrompt=true)` (shows the system
   prompt + System Settings redirect); or "Also works: Screen Recording" (if the user
   is enabling capture features anyway — one grant serves both).
3. **Denied / dismissed:** feature stays enabled but degraded — title `null`,
   activity `Unknown`, ONE non-modal notice ("awareness is running without window
   access — activity shows as Unknown; click to grant") that deep-links to
   `x-apple.systempreferences:com.apple.preference.security?Privacy_Accessibility`
   (or `?Privacy_ScreenCapture`). No re-prompt loop; re-request only on another
   explicit click.
4. **Grant timing reality (first-run vs deferred) — corrected after web verification:**
   - Accessibility: **do NOT promise no-relaunch pickup.** Current reporting (filed
     against Sequoia in 2025, still present in macOS 26/Tahoe per fazm.ai's
     production-failure-modes writeup) is that `AXIsProcessTrusted` reads from a
     **per-process cache populated at first call** — a grant made after the process
     first probed may not be observed until relaunch. Design fix: the degrade notice
     for the AX path says "if titles don't appear after granting, restart CCP"; the
     60s re-probe is kept (it works when the cache behaves) but is NOT relied on
     (§7.1 row 6).
   - Screen Recording grants require an **app relaunch** to take effect — VERIFIED
     (ubiquitous current guidance, e.g. Electron ecosystem + multiple 2025-2026 macOS
     permission guides) — the notice must say so ("restart CCP to finish enabling").
   - The selector re-probes on: explicit enable, notice click, and a slow (60s)
     re-check timer while awareness is enabled-but-degraded — best-effort out-of-band
     pickup, with the relaunch caveat surfaced honestly for BOTH permissions.
5. **TCC identity gotcha (dev vs shipped):** TCC grants key on the responsible binary.
   `dotnet run` attributes the grant to the `dotnet` host binary — grants made during
   development do NOT carry to the bundled `.app`, and vice versa. The shipped macOS
   head must be a proper `.app` bundle with a stable bundle identifier for grants to
   persist across updates **[confidence: high — standard TCC behavior]**. No
   usage-description Info.plist key is required for Accessibility or Screen Recording
   (unlike camera/mic) — **VERIFIED: no such plist key exists for Screen Recording
   (stackoverflow.com/questions/62641652) or Accessibility; the TCC dialogs for these
   services are system-worded, not app-worded (§7.1 row 7)**.

---

## 6. Implementation Slice Plan

All files under `CCP.Avalonia.Desktop.macOS/Platform/…` (§2.3). Standard repo gates
apply. CI uses the shared **macos-smoke** lane defined in
`macos-overlay-contract.md` §6, including its TCC.db grant recipe
(§7.1-VERIFIED there): CI *can* pre-grant `kTCCServiceAccessibility` and
`kTCCServiceScreenCapture` to the test binary via sqlite on macos-latest — which makes
BOTH live backends CI-exercisable, something the Linux GNOME path never got. What CI
cannot do: exercise the real prompt dialogs and the System Settings grant flow — those
are the explicit real-Mac manual rows.

### Slice A: Selector + Fallback + DI + Permission Preflight (Foundation)

**Files:**
- `Platform/MacOSForegroundWindowTitleProvider.cs`
- `Platform/MacOSTitleProviderBackendSelector.cs`
- `Platform/TitleProviderBackends/IMacOSTitleProviderBackend.cs`
- `Platform/TitleProviderBackends/FallbackTitleBackend.cs`
- `Platform/MacOSPermissionService.cs` (preflight-only surface in this slice)
- `Platform/Interop/CoreGraphicsInterop.cs` (`CGPreflightScreenCaptureAccess`),
  `Platform/Interop/AxInterop.cs` (`AXIsProcessTrusted`)
- `Program.cs` — `services.AddSingleton<IForegroundWindowTitleProvider, MacOSForegroundWindowTitleProvider>()`

**CI (macos-smoke, NO TCC grants — the ungranted path is the test):**
```bash
dotnet run --project ConditioningControlPanel/CCP.Avalonia.Desktop.macOS -- --smoke-test --verify-titleprovider-fallback
# Assert: both preflights return false (clean runner) → FallbackTitleBackend selected;
# "unavailable" logged once with TCC-state reason; null returned; AwarenessService.Start()
# proceeds past the null-provider guard and runs with Unknown activity; no prompt
# appeared (no TCC dialog process spawned — assert no UserNotificationCenter window via
# permission-free CGWindowList enumeration)
```

**Acceptance:**
- [ ] Selection keyed on live preflights with unit tests over (AX, SR) grant permutations
- [ ] Null returned; reason logged exactly once; never any title content in logs
- [ ] Provider registered unconditionally; awareness starts and classifies Unknown
- [ ] No prompt is ever triggered by init/poll paths (CI negative assert)

### Slice B: AXAPI Backend

**Files:**
- `Platform/Interop/AxInterop.cs` (extend: `AXUIElementCreateApplication`,
  `AXUIElementCopyAttributeValue`, CFString marshal, `CFRelease`)
- `Platform/Interop/ObjCInterop.cs` (extend if overlay Slice A hasn't landed:
  NSWorkspace frontmostApplication)
- `Platform/TitleProviderBackends/AxApiTitleBackend.cs` (snapshot refresher per §3.1)

**CI (macos-smoke, TCC-granted lane — `kTCCServiceAccessibility` sqlite insert):**
```bash
# Harness opens its own titled helper window ("Test Window Title 12345"), activates it
# (our own app — activation is permission-free), waits 2 refresh ticks:
dotnet run --project ConditioningControlPanel/CCP.Avalonia.Desktop.macOS -- --smoke-test --get-foreground-title
# Assert: output contains "Test Window Title 12345"
# Cross-app variant: `open -a TextEdit` + poll → assert a non-null title while TextEdit
# is frontmost [may be image-flaky; continue-on-error with the log recorded]
# Negative tests:
#  - close the focused window mid-poll → null or next title, process alive
#  - revoke via `tccutil reset Accessibility <bundle-id>` mid-run → demotion to
#    fallback, one log line, no crash, NO prompt
# Privacy negative: grep app logs for the test title — must NOT appear
```

**Acceptance:**
- [ ] Frontmost-PID → focused window → title chain with CFRelease on every owned ref
      (leak-checked: 1000 refresh cycles, stable RSS)
- [ ] Snapshot model: seam call does zero native work; ≤1s staleness
- [ ] Unicode title (emoji/CJK) survives round trip; 512 truncation
- [ ] `kAXErrorAPIDisabled` and revocation → demotion, never crash/prompt
- [ ] Privacy log-grep negative assertion green

### Slice C: CGWindowList Backend

**Files:**
- `Platform/Interop/CoreGraphicsInterop.cs` (extend: `CGWindowListCopyWindowInfo`,
  CFArray/CFDictionary/CFNumber accessors)
- `Platform/TitleProviderBackends/CGWindowListTitleBackend.cs`

**CI (macos-smoke, TCC-granted lane — `kTCCServiceScreenCapture` sqlite insert, the
SAME grant row the framesource lane installs — assert the sharing works):**
```bash
# AX deliberately NOT granted in this job → selector must pick CGWindowListTitleBackend
dotnet run --project ConditioningControlPanel/CCP.Avalonia.Desktop.macOS -- --smoke-test --get-foreground-title
# Same titled-helper assertion as Slice B; additionally assert the selected-backend log
# line says CGWindowList (proves the priority-2 arm)
# Ungranted variant (no TCC row): preflight false → backend not selected → fallback
# (proves we never read redacted nulls and misreport "untitled")
```

**Acceptance:**
- [ ] Layer-0 + frontmost-PID + front-to-back selection logic (unit-tested against a
      fabricated window-info list; edge cases: no layer-0 window, sheets, our own overlay)
- [ ] Whole CFArray processed and released per refresh; no all-titles retention (§1.2)
- [ ] Selected only when preflight true; redaction never misread as "untitled"
- [ ] Same privacy/leak/Unicode asserts as Slice B

### Slice D: Permission-Request UX

**Files:**
- `Platform/MacOSPermissionService.cs` (extend: `AXIsProcessTrustedWithOptions` prompt,
  `CGRequestScreenCaptureAccess`, settings deep links, relaunch notice)
- Settings surface wiring (awareness enable flow), non-modal degrade notice

**CI:** unit tests with a mocked permission service (prompt flows can't run headless —
the system dialog needs a real user). Manual checklist (real Mac, once, recorded):
- [ ] First awareness enable → ONE prompt surface, correct system dialog appears
- [ ] Deny → degraded-but-running, single non-modal notice, deep link opens the right
      Settings pane, no re-prompt loop
- [ ] Grant Accessibility → titles flow without relaunch (or the relaunch note is
      shown if §7.1 row 6 says otherwise)
- [ ] Grant Screen Recording → relaunch notice shown; titles flow after relaunch
- [ ] Out-of-band grant in System Settings while degraded → 60s re-probe picks it up
- [ ] `.app`-bundle identity: grants persist across app restart and across a version
      update (§5.5)

**Acceptance:**
- [ ] §5 policy fully enforced (prompts only on explicit action; every denial path
      lands in a degrade, never a loop)
- [ ] Selector re-probe triggers: enable, notice click, 60s degraded timer

### Slice E: (PARKED) AppNameTitleBackend

Parked per §4.3. Unpark criteria: owner ruling widens the seam contract wording (Core
interface doc update included in the slice). Until then: no code.

---

## 7. Risk / Unknowns

### 7.1 Claims to verify before the relevant slice lands ([confidence:] tags; driver has web tools)

All rows re-verified in the 2026-07-12 §7.1 web pass (evidence in §7.1-VERIFIED).

| # | Claim | Verdict (web pass 2026-07-12) | Source / evidence | Blocks |
|---|---|---|---|---|
| 1 | `kCGWindowName` requires Screen Recording (10.15+); the call itself never prompts | **VERIFIED (incl. current OS)** — titles silently omitted without the grant on 10.15 through current macOS; layer/bounds/owner-PID stay available without any TCC; no prompt is ever triggered by the enumeration itself | ryanthomson.net Catalina permissions article; stackoverflow.com/questions/56597221 + /59337022; Apple Support screen-recording TCC guide (no behavior change reported through Sequoia) | — |
| 2 | AXAPI cross-app reads require TCC Accessibility; `AXIsProcessTrustedWithOptions` prompt option shows the system dialog | **VERIFIED** — untrusted reads fail (`kAXErrorAPIDisabled`/`kAXErrorCannotComplete`); prompt-option flow is the standard shipping pattern | developer.apple.com/forums/thread/794253; Apple AX docs | B, D |
| 3 | `CGPreflightScreenCaptureAccess`/`CGRequestScreenCaptureAccess` availability (10.15 vs 11.0) | **REFUTED as 10.15** — documented 10.15 but not implemented there (crashes on Catalina; Apple bug r.72786842); functional macOS 11+. §2.1 design-fixed: dlsym + version gate | developer.apple.com/forums/thread/683860; stackoverflow.com/questions/56597221 (comments) | A, D |
| 4 | AX API thread-safety off the main thread (refresher placement) | **UNCERTAIN** — Apple docs remain silent; community mixed; §3.1 main-thread snapshot design already absorbs this, so non-blocking | — | B (optimization only) |
| 5 | `kCGWindowListOptionOnScreenOnly` returns front-to-back order; first layer-0 window of frontmost PID ≈ focused window | **VERIFIED (ordering)** — SDK header + docs literally state "ordered from front to back"; sheets/modal edge cases remain Slice C unit-probe territory | CoreGraphics `CGWindow.h` ("in order from front to back"); stackoverflow.com/questions/5286274 | C |
| 6 | Accessibility grants take effect without app relaunch; Screen Recording requires relaunch | **REFUTED (AX half) / VERIFIED (SR half)** — `AXIsProcessTrusted` uses a per-process cache populated at first call (Sequoia-era bug, still current), so mid-session grant pickup is NOT reliable; SR relaunch requirement confirmed. §5.4 wording design-fixed | fazm.ai/t/macos-accessibility-automation; multiple 2025-2026 SR permission guides + Electron reports | D (notice wording) |
| 7 | No Info.plist usage-description key required for Accessibility/Screen Recording | **VERIFIED** — no such key exists for either service (unlike camera/mic) | stackoverflow.com/questions/62641652 | D |
| 8 | `tccutil reset` usable in CI for the revocation negative test | **UNCERTAIN** — standard system tool and runners have passwordless sudo, but not directly proven on the current image; keep continue-on-error | — | B |
| 9 | CI TCC.db sqlite grant recipe works on the CURRENT macos-latest image (schema drift) | **VERIFIED (recipe exists; image-sensitive)** — same posture as overlay row 2; one report of a Screen-Recording grant not taking effect for Terminal exists (#8951), reinforcing the assert-the-preflight lane design | actions/runner-images #7792, #8951, #8214 | B, C |

### 7.2 Genuine Unknowns (real-desktop only)

| Risk | Impact | Mitigation |
|---|---|---|
| Electron/game apps with empty AX titles | `null` titles for those apps | Classifier already tolerates Unknown; CG backend may still see a CG-level name — cross-check at Slice C |
| TCC grant caching across app updates (path/signature changes) | Grants silently lost after update → sudden Unknown | Stable bundle id + signing; degrade notice re-appears by design |
| Fullscreen apps/Spaces: frontmost app with zero layer-0 on-screen windows on the active Space | CG backend returns null while AX still answers | AX outranks CG when both granted; accept null otherwise |
| 1s refresh vs rapid title churn (browser tabs) | Slight lag | Awareness change-detection already debounces (Linux §7.2 parity) |
| macOS 15+ TCC re-authorization for screen capture — **VERIFIED: MONTHLY re-confirm** (weekly in early Sequoia betas, relaxed to monthly in beta 6 and shipped that way; reboot-prompts dropped) | Recurring degrade for the CG backend when the user misses the re-confirm | Re-probe timer + notice already handle it; prefer AX path in user guidance (AX has no re-auth cycle) — sources: 9to5mac.com/2024/08/14 monthly-prompt report; tidbits.com Sequoia permissions coverage |

### 7.3 Configuration Notes

| Setup | Path | Notes |
|---|---|---|
| User grants Accessibility | AX backend | Best: focused-window-accurate, privacy-minimal |
| User grants Screen Recording only (capture features) | CGWindowList backend | Titles for free with the capture grant — document in user guidance |
| No grants | fallback | Awareness on, activity Unknown, one notice |
| App Store distribution (hypothetical) | AX backend impossible (sandbox) | CCP ships unsandboxed; revisit only if distribution changes |

---

## 8. CI Verification Matrix

| Slice | macos-smoke (no TCC) | macos-smoke (AX granted) | macos-smoke (SR granted, AX not) | Mocked unit | Manual (real Mac) |
|---|---|---|---|---|---|
| A Selector+Fallback+DI | Required | — | — | Required (grant permutations) | — |
| B AXAPI | Negative (not selected) | Required (+ revocation, leak, privacy-grep) | — | — | — |
| C CGWindowList | Negative (not selected) | — | Required (+ redaction-vs-untitled) | Required (ordering edge cases) | — |
| D Permission UX | — | — | — | Required | Required once (full §6-D checklist) |
| E (parked) | — | — | — | — | — |

CI job additions: three grant-permutation variants of the shared macos-smoke lane
(no-TCC / AX-row / SR-row). The TCC insert step is `continue-on-error` with the job
asserting the preflight result matches the intended permutation — if the image schema
drifted, the job reports "recipe broken" instead of a false red.

---

## 9. Summary

- **4 active slices + 1 parked**; DI + fallback land FIRST so every backend is a pure
  capability upgrade (Linux Slice-A convention).
- **Backend chain keyed on TCC state, not environment:** AXAPI (Accessibility) →
  CGWindowList (Screen Recording — the two APIs are gated by DIFFERENT permissions,
  the correction this contract leads with) → honest `null`. App-name zero-TCC tier
  parked pending an owner contract ruling.
- **Permission-reuse synergy:** the framesource contract's Screen Recording grant
  lights up titles with no extra prompt — one grant, two features.
- **Ambient prompt policy (shared `MacOSPermissionService`):** silent preflights
  always; system prompts only on explicit user enable/click; denial → degraded-running
  with one non-modal notice + Settings deep link; relaunch realities surfaced honestly.
- **Snapshot threading model:** native reads on a ~1s main-thread refresher; the
  synchronous seam reads an atomic snapshot — no AX threading gamble, no seam-path
  native calls.
- **Privacy hardened:** CGWindowList's all-windows result is processed-and-released
  per refresh, title-only crosses the seam, CI carries the negative log-grep.
- **CI:** macos-latest headed lane with sqlite TCC grants makes BOTH live backends
  CI-exercisable (better than the Linux GNOME story); prompt dialogs and grant-timing
  are the explicit real-Mac manual rows.

---

## Sources

- `CCP.Core/Platform/IForegroundWindowTitleProvider.cs` — interface + privacy doc
- `CCP.Avalonia.Desktop.Windows/Platform/WindowsForegroundWindowTitleProvider.cs` — reference impl
- `CCP.Core/Services/Awareness/AwarenessService.cs` — poll cadence, privacy header, null guard
- `docs/linux-foreground-title-contract.md` — template, selection-order lessons,
  snapshot model, privacy-map extension
- `docs/macos-overlay-contract.md` — shared ObjC interop + macos-smoke lane + TCC.db recipe
- `docs/linux-macos-readiness-map.md` — governing principle
- Apple: AXUIElement (ApplicationServices), CGWindowListCopyWindowInfo,
  AXIsProcessTrustedWithOptions, CGPreflightScreenCaptureAccess

### 7.1-VERIFIED (web research, 2026-07-12 — drafting pass + full §7.1 verify pass)
| Claim | Result | Source |
|-------|--------|--------|
| `kCGWindowName` gated by **Screen Recording** (not Accessibility) since macOS 10.15; `CGWindowListCopyWindowInfo` itself never triggers the permission dialog — names are silently omitted when ungranted | **VERIFIED** — and re-confirmed current: the redaction model (title omitted, layer/bounds/PID available, zero prompt) is unchanged through Sonoma/Sequoia; the §2.1 Screen-Recording preflight gate is the correct design | ryanthomson.net "Screen Recording Permissions in Catalina are a Mess"; stackoverflow.com/questions/56597221 + /59337022 (kCGWindowName omitted without the grant; "doesn't trigger the privacy alert") |
| GitHub macOS runner TCC.db sqlite grant recipe | **VERIFIED (exists; image-sensitive)** | carried from `macos-overlay-contract.md` §7.1-VERIFIED (actions/runner-images #9529) |
| `CGPreflightScreenCaptureAccess` on 10.15 | **REFUTED** — crashes on Catalina despite documented 10.15 availability (Apple bug r.72786842); restrict to macOS 11+ | developer.apple.com/forums/thread/683860 |
| `AXIsProcessTrusted` per-process caching breaks mid-session grant pickup | **CONFIRMED CURRENT** (Sequoia 2025 → macOS 26) — §5.4 notice wording updated to include an AX relaunch caveat | fazm.ai/t/macos-accessibility-automation |
| CGWindowList front-to-back ordering | **VERIFIED** | CoreGraphics `CGWindow.h` header comment; stackoverflow.com/questions/5286274 |
| No usage-description plist key for AX/SR | **VERIFIED** | stackoverflow.com/questions/62641652 |
| macOS 15 screen-capture re-authorization is MONTHLY | **VERIFIED** | 9to5mac.com/2024/08/14; daringfireball.net 2024-08-17; tidbits.com Sequoia coverage |
