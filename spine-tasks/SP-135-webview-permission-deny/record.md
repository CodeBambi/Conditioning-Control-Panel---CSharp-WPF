# SP-135 record — D250 closed on three hosts; three facts blocked on a shared chokepoint

Branch `lane/SP-135-webview-permission-deny`, base `1a5136beb`.

---

## 1. The slot, and its source

**`add_PermissionRequested` = vtable slot 23, `remove_PermissionRequested` = 24**, 0-based including
IUnknown's three. Derived mechanically from three sources that agree, none of them a count by eye:

| Source | What it says | How it was read |
|---|---|---|
| `Microsoft.Web.WebView2` **1.0.2535.41** `build/native/include/WebView2.h:3287-3618` | 61 slots; 23 = `add_PermissionRequested`, 24 = `remove`, 25 = `add_ProcessFailed`, 26 = `remove` | `grep -o "STDMETHODCALLTYPE \*[A-Za-z0-9_]*"` over the `ICoreWebView2Vtbl` struct range, numbered from 0 |
| `Microsoft.Web.WebView2` **1.0.3179.45** `.../WebView2.h:2875-3206` | identical order at 21-27 | same enumeration |
| **Avalonia.Controls.WebView 12.0.1** — the library that hands this port the pointer | its generated `Avalonia.Controls.Win.WebView2.Interop.ICoreWebView2` ComWrappers vtable has `field[23] add_PermissionRequested_23`, `field[24] remove_PermissionRequested_24`, `field[25] add_ProcessFailed_25` | reflection dump (scratchpad probe, the SP-027 technique) |

**Avalonia's is the strongest of the three because it is not a positional count at all**: the slot
index is in the identifier, and the field index agrees with it independently. (The plan cited a byte
offset of 184; that was *computed* by me from the field order, not declared in metadata — the field
index and the name suffix are the load-bearing facts and both hold. Corrected here.)

**The anchor that makes the enumeration checkable:** all three reproduce `add_ProcessFailed` = 25 and
`remove` = 26, which `DtrhProcessFailed.cs:97-98` has been shipping since SP-027 and which nothing in
this packet chose. `PublishedVtableOrder_...` asserts exactly that, reading the shipping constants out
of the product by reflection, so any insertion, deletion or shift at or before slot 25 reddens.

Supporting COM facts, same header: handler IID `15e1c6a3-c72a-4df3-91d7-d097fbec6bfd` with `Invoke`
at slot 3 (`:39951`); args `get_PermissionKind` slot 4 / `put_State` slot 7 (`:39534-39584`); args3
IID `e61670bc-3dce-4177-86d2-c629ae3cb6ac` with `put_SavesInProfile` at slot 12 (`:39785-39880`);
`COREWEBVIEW2_PERMISSION_STATE_DENY` = 2 and `_KIND_AUTOPLAY` = 9 (`:1984-2004`, identical in both
SDKs).

**Premise re-checked, not assumed:** the same reflection dump shows `NativeWebView`'s complete public
event surface (`EnvironmentRequested, NavigationCompleted, NavigationStarted, NewWindowRequested,
WebMessageReceived, WebResourceRequested, AdapterCreated, AdapterDestroyed`) — no permission event
exists to use instead, and `IWindowsWebView2PlatformHandle` exposes only `CoreWebView2` and
`CoreWebView2Controller` as raw pointers. D250's premise stands.

## 2. What was built

`client/src/CcpClient.Desktop/Features/Dtrh/WebViewPermissionDeny.cs` (new), on the
`DtrhProcessFailed.cs:26-75` model: typed `AttachOutcome`, `VtableSlot<T>`, AddRef/Release ownership,
a never-throwing COM callback, an idempotent detach that tolerates a zombie browser.

- Every permission kind is answered **`Deny`** (`put_State`, slot 7, value 2). Deny is the DEFAULT
  arm, so a kind a future runtime adds is refused rather than prompted; an unreadable kind is denied.
- **`AUTOPLAY` (kind 9) alone is left untouched** at the browser default.
- **`SavesInProfile = FALSE`** through a QI to args3, tolerating `E_NOINTERFACE`.
- `get_Uri` is never read; no origin is ever logged. The diagnostic line carries a kind name and a
  decision.

Wired at `AdapterCreated`, detached in teardown, on three hosts:
`Features/Dtrh/DtrhHostWindow.axaml.cs`, `Features/Dtrh/DtrhLoomWindow.axaml.cs`,
`Features/Goon/GoonHostWindow.axaml.cs`. The Goon window's class doc, which said no deny hook exists,
was corrected in place.

**The Loom needed a new field and a new dispose line** — it has no `AttachProcessFailedSignal` and no
watchdog, so this is its first native `CoreWebView2` subscription; the comment there says so, because
"follows the existing pattern" would have been false. **The watchdog guard was deliberately NOT
copied**: `DtrhHostWindow.axaml.cs:806` opens `AttachProcessFailedSignal` with
`if (_watchdog is null) return;`, and making a microphone deny conditional on a process-recovery
object would be a silent hole. The deny attaches unconditionally.

### The carve-out was enumerated, not asserted

Every published kind was checked against both payloads before excluding autoplay:

| Kind | In the payloads? | Denying it costs |
|---|---|---|
| microphone (1) | **yes** — `goon/ui/voice/recorder.js:591` `getUserMedia({audio:true})` | the residual D250 names; that is the point |
| camera (2), geolocation (3), notifications (4), other-sensors (5), MIDI (11), local-fonts (10), window-management (12) | no occurrences of `navigator.geolocation`, `Notification.requestPermission`, `requestMIDIAccess` or `queryLocalFonts` anywhere under `Resources/web` | nothing |
| clipboard-read (6) | **the only clipboard call is `navigator.clipboard.writeText`** (`goon/ui/screens/host.js:119-120`) — clipboard WRITE, which is not kind 6 | nothing |
| multiple-automatic-downloads (7), file-read-write (8) | no `showSaveFilePicker` / `showOpenFilePicker` / `download=` anywhere in either payload | nothing |
| **autoplay (9)** | **both payloads start media programmatically** — `dtrh/engine/audioBus.js`, `goon/exec/videos.js`, `goon/ui/hud.js` — and all four WebView2 hosts already pass `--autoplay-policy=no-user-gesture-required` (`DtrhHostWindow.axaml.cs:631`, `GoonHostWindow.axaml.cs:302`, `DtrhLoomWindow.axaml.cs:151`, `IntakeHostWindow.axaml.cs:246`; **`ChaosTunnelWindow.cs` is a fifth `NativeWebView` host and does NOT pass it**) | **silence, and a contradiction with a policy this port already set** — a functional regression rather than a tightening. This is the one load-bearing carve-out |

## 3. Evidence: it fires, and what that does and does not mean

`client/tests/CcpClient.Tests/WebViewPermissionTests.cs`, **14 facts, all passing**.

A fake `ICoreWebView2` is built in native memory: 61 slots, each a distinct thunk that records **its
own index**. What is observed, not inferred:

- `TryAttach` enters **`[1, 23]`** — AddRef, then `add_PermissionRequested` — **and nothing else**.
- The handler pointer handed to slot 23 is then **invoked through its own CCW vtable** (a real
  unmanaged call, the way WebView2 would make it) and **`DENY` (2) arrives at `put_State`**, with
  `SavesInProfile` written FALSE.
- `Dispose` enters **only slot 24**, with this packet's own token, and is idempotent; a failing
  remove (the zombie-browser case) is tolerated.

**A mechanic worth recording, because it cost a cycle:**
`Marshal.GetFunctionPointerForDelegate` registers the managed delegate against the address it returns,
and `Marshal.GetDelegateForFunctionPointer<T>` on that address returns **the same delegate object**
and type-checks it. A fake vtable built from locally declared delegate types therefore makes the
product's own read fail with `InvalidCastException: Unable to cast object of type 'AddDelegate' to
type 'AddPermissionRequestedDelegate'` before any slot arithmetic runs. The fake's slots are built as
the **product's own private delegate types** via `Delegate.CreateDelegate`, which also pins the
product's ABI. Filler slots use the `add` delegate type on purpose, so a wrong ADD slot — the
load-bearing drift — records its own index and fails with the number.

**Evidence class.** This is a unit-level COM interop fact: **neither `draw-verified` nor
`presentation-verified`** (`client/docs/verification-harness.md:56-64`), and it discharges **no headed
gate**. **No browser was ever started.** It does not establish that Chromium raises
`PermissionRequested` for `getUserMedia`, that it honours the deny, or that no prompt is painted. The
gate that would show a denial is a headed Windows run that loads the Goon page, walks the voice
screen's double-locked opt-in and presses record; it was not run, and SP-132's harness routes around
the voice screen by design. The three windows are never shown in any test, so the attach has never
been observed against a real adapter.

### Guards watched red at the committed head `b3e4a48f9` — and TWO of the four mutations found real holes in my own tests

| Mutation | Result |
|---|---|
| `AddPermissionRequestedSlot` 23 to 22 | **4 red**: `PublishedVtableOrder_...` (`Expected: 23 / Actual: 22`) and all three subscription facts, which get `Unavailable` instead of `Attached` because slot 22 is `remove_ScriptDialogOpening`, whose filler thunk answers E_NOTIMPL |
| `DenyState` 2 to 1 (Allow) | **FIRST RUN: 1 red only — a self-consistency hole in my own tests.** The deny facts compared the observed write against `WebViewPermissionDeny.DenyState`, so flipping the constant made them tautological. Fixed: they now assert the transcribed literal `PublishedDenyState = 2`, and the product constant is checked against that literal once. **Re-run: 7 red** |
| `AttachPermissionDeny();` call site deleted from `GoonHostWindow` (the method left in place) | **FIRST RUN: 0 red — all 14 green.** The census guard asserted the hook EXISTED, not that it is CALLED: a hook that never runs, which is this session's signature defect appearing inside the guard written to catch it. Fixed: the guard now requires the call site. **Re-run: 1 red** |
| autoplay carve-out removed | **1 red**: `TheAnswer_LeavesAutoplayAtTheBrowserDefault` |

Both holes were found by mutation rather than by review, and both are fixed in the follow-up commit.

## 4. Hosts covered, and hosts NOT covered

| Host | Covered |
|---|---|
| `Features/Dtrh/DtrhHostWindow.axaml.cs` | **yes** (Windows embedded) |
| `Features/Dtrh/DtrhLoomWindow.axaml.cs` | **yes** (Windows embedded) |
| `Features/Goon/GoonHostWindow.axaml.cs` — the surface D250 was named for | **yes** (Windows embedded) |
| `Features/Intake/IntakeHostWindow.axaml.cs` | **NO** — same `NativeWebView`, outside File Scope |
| `Features/Chaos/ChaosTunnelWindow.cs` | **NO** — same `NativeWebView`, outside File Scope |
| Linux `NativeWebDialog` (WebKitGTK) on all of them | **NO** — typed `unsupported-platform`; no permission hook is exposed anywhere in Avalonia's API |

The census is pinned by a test, so a sixth WebView2 host cannot appear unnoticed.

## 5. A failed attach is typed and reported, never swallowed

`Unavailable(code, detail)` with `unsupported-platform` / `invalid-handle` / `attach-failed` (the last
carrying the HRESULT). Each host logs `PermissionRequested DENY hook UNAVAILABLE (<code>) — <detail>`
and, on the Goon host, that **"the browser can still ask the user for the microphone on this surface
(D250 stands here)"**. The failure path releases the ref it took (asserted). A `put_State` that fails
is reported as its own `WriteFailed` decision and is never rounded up to a deny that did not happen.

## 6. `GoonDoors.cs` — the text is now false, and was NOT edited

`Features/Goon/GoonDoors.cs:105-108` still says: *"The residual is WebView2's own prompt: the voice
screen is reachable from the title menu and its recorder asks the browser for the microphone directly
(ui/voice/recorder.js:591), so the browser can still ask you. Closing that needs a
PermissionRequested-deny hook (D250)."*

**False on the Windows embedded path** as of this packet: the browser can no longer ask, and the hook
exists. **Still true on Linux**, where no hook is reachable. The file is explicitly closed to this
packet. Reported as D294; the correction belongs to whoever owns that sentence. The rest of the
refusal stays true: no capture device is opened, and the missing CROSSING (no peer) is still the
reason the door refuses.

## 7. BLOCKED: three facts need a file this packet may not touch

`VacuousShapeGuardTests` (SP-066) requires every `[Fact]` carrying a silencing shape to be
dispositioned in **`client/tests/floor/vacuous-shape-ledger.json`** — inside `client/tests/floor/**`,
which this packet's File Scope lists as must-not-change. Three facts need a CCW
(`Marshal.GetComInterfaceForObject`, a Windows COM facility), so each carries one `early-return`.

**This was minimised rather than accepted:** the decision half of the handler was extracted into
`AnswerRequest`, which is pure vtable traffic and runs on every platform. That took the ask from
**twelve sites to three** and turned eight Windows-only facts into cross-platform ones — better
evidence, not just fewer entries. The remaining three are genuinely machine-bound.

**The exact entries needed** (append to `entries` in `vacuous-shape-ledger.json`):

```json
{
  "key": "CcpClient.Tests/WebViewPermissionTests.cs::WebViewPermissionTests.TheSubscription_LandsOnSlot23_AndTypesEveryFailure",
  "path": "CcpClient.Tests/WebViewPermissionTests.cs",
  "line": 277,
  "method": "WebViewPermissionTests.TheSubscription_LandsOnSlot23_AndTypesEveryFailure",
  "shapes": ["early-return"],
  "expectDetected": true,
  "verdict": "not-vacuous",
  "reason": "the CCW this fact needs (Marshal.GetComInterfaceForObject) is a Windows COM facility; the non-Windows arm asserts the product's typed unsupported-platform refusal in WindowsOrTypedUnsupported() before returning, so neither arm is silent. The deny DECISION is asserted platform-independently by the eight TheAnswer_* facts in the same file"
},
{
  "key": "CcpClient.Tests/WebViewPermissionTests.cs::WebViewPermissionTests.TheHandlerAttachedAtSlot23_Fires_AndDeniesTheMicrophone",
  "path": "CcpClient.Tests/WebViewPermissionTests.cs",
  "line": 309,
  "method": "WebViewPermissionTests.TheHandlerAttachedAtSlot23_Fires_AndDeniesTheMicrophone",
  "shapes": ["early-return"],
  "expectDetected": true,
  "verdict": "not-vacuous",
  "reason": "same Windows-CCW precondition; the non-Windows arm asserts the typed refusal. This is the fact that proves the pointer handed to slot 23 is the handler that denies"
},
{
  "key": "CcpClient.Tests/WebViewPermissionTests.cs::WebViewPermissionTests.Dispose_DetachesAtSlot24_ToleratesAZombieBrowser_AndIsIdempotent",
  "path": "CcpClient.Tests/WebViewPermissionTests.cs",
  "line": 335,
  "method": "WebViewPermissionTests.Dispose_DetachesAtSlot24_ToleratesAZombieBrowser_AndIsIdempotent",
  "shapes": ["early-return"],
  "expectDetected": true,
  "verdict": "not-vacuous",
  "reason": "same Windows-CCW precondition; detach can only be exercised on a subscription that exists, and the non-Windows arm asserts the typed refusal"
}
```

**The finding underneath it (D295):** `floor.json` has a per-packet delta file precisely so
concurrent lanes never collide on it. **Its sibling chokepoint in the same directory — the
vacuous-shape ledger — has no such mechanism**, so any lane that adds a legitimately OS-bound test is
blocked exactly the way this one is. That is worth a mechanism, not a per-packet amendment each time.

## 8. Floor

Pin **2573 unit / 152 headless**. Declared delta **+14 unit / 0 headless**
(`spine-tasks/SP-135-webview-permission-deny/floor-delta.json`); `floor.json` was never opened.

**Observed: 2587 unit total = 2573 pin + 14 declared** — 2584 passed, 2 known machine-class skips, and
**1 failure: `VacuousShapeGuardTests`, for the scope reason in §7 and nothing else.** The warnings gate
is **0 warnings / 0 errors across all four projects, forced non-incremental**.

## 9. Spec-versus-code discrepancies

1. **The packet says the Loom-style attach follows an existing pattern.** It does for the DTRH and
   Goon hosts; the Loom had neither a signal field nor a dispose line. Written as new code, noted in
   the comment there.
2. **`GoonHostService.cs:490-492` is wrong** about `Handled = true` suppressing the browser's UI; it
   is propagation control. `put_State = DENY` alone is the answer and the suppression, because
   `..._STATE_DEFAULT` is the prompting value. Recorded as D293; the port does not copy the mechanic.
3. **The plan said "all four hosts pass `--autoplay-policy`" and cited three.** There are **five**
   `NativeWebView` hosts; four pass the switch (the fourth is `IntakeHostWindow.axaml.cs:246`) and
   `ChaosTunnelWindow.cs` does not. Corrected here and in the product comment.
4. **The plan cited Avalonia's vtable byte offset (184).** That offset was computed by me from the
   field order; the metadata declares no explicit offsets. The field index and the slot-index suffix
   in the identifier are the facts, and both hold. Corrected in §1.
