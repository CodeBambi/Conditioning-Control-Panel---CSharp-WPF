# SP-135 plan checkpoint — deny the WebView2 permission prompt (D250)

Written before any product edit. Review Level 3, step 1.

Base `1a5136beb`, branch `lane/SP-135-webview-permission-deny`, worktree
`.claude/worktrees/agent-ab6495efe1c65fafa`.

---

## 1. Where the slot index comes from (the thing most likely to sink this packet)

**`add_PermissionRequested` is vtable slot 23, `remove_PermissionRequested` is slot 24**, 0-based
including IUnknown's three. Not counted by eye. Two independent sources, each enumerated
mechanically, and each of them independently reproduces the slot the *already-working* code in this
tree uses for `ProcessFailed`.

### Source A — Microsoft's published interface definition, two SDK versions

`Microsoft.Web.WebView2` ships the MIDL-generated C header, whose `ICoreWebView2Vtbl` struct lists
the interface's methods in vtable order:

- `C:\Users\Micha\.nuget\packages\microsoft.web.webview2\1.0.2535.41\build\native\include\WebView2.h:3287-3618`
  (the version `DtrhProcessFailed.cs:15` already cites)
- `C:\Users\Micha\.nuget\packages\microsoft.web.webview2\1.0.3179.45\build\native\include\WebView2.h:2875-3206`

Enumerated with `grep -o "STDMETHODCALLTYPE \*[A-Za-z0-9_]*"` over that range and numbered from 0,
**both versions give the identical order**:

```
21  add_ScriptDialogOpening
22  remove_ScriptDialogOpening
23  add_PermissionRequested
24  remove_PermissionRequested
25  add_ProcessFailed
26  remove_ProcessFailed
```

Signatures (`WebView2.h:3414-3423`) are shape-identical to `add_ProcessFailed`:
`add_PermissionRequested(This, ICoreWebView2PermissionRequestedEventHandler*, EventRegistrationToken* out)`,
`remove_PermissionRequested(This, EventRegistrationToken)`.

### Source B — Avalonia 12.0.1's own generated vtable, which is the library that hands us the pointer

`Avalonia.Controls.WebView 12.0.1` contains a ComWrappers-generated
`Avalonia.Controls.Win.WebView2.Interop.ICoreWebView2` whose `__InterfaceImplementationVtable` names
each field with its slot index and lays them out at the corresponding byte offset. Reflection dump
(scratchpad probe, same technique as SP-027's) prints:

```
field[23] add_PermissionRequested_23     offset=184   (= 23 * 8)
field[24] remove_PermissionRequested_24  offset=192
field[25] add_ProcessFailed_25           offset=200
```

### Why this is at least as strong as `ProcessFailed`'s

The enumeration method is validated by a data point I did not choose: it predicts
`add_ProcessFailed` = 25 / `remove` = 26, which is exactly what `DtrhProcessFailed.cs:97-98` uses
today. An off-by-one anywhere at or before slot 25 would have shown up there. `ProcessFailed` itself
rests on one source; this rests on three (two SDK versions plus the shipping Avalonia metadata).

### The other slots and constants, from the same header

| Thing | Value | Source |
|---|---|---|
| `ICoreWebView2PermissionRequestedEventHandler` IID | `15e1c6a3-c72a-4df3-91d7-d097fbec6bfd`, `Invoke` at slot 3 | `WebView2.h:39951-39957` |
| `ICoreWebView2PermissionRequestedEventArgs` IID | `973ae2ef-ff18-4894-8fb2-3c758f046810` | `WebView2.h:39507` |
| args `get_PermissionKind` | slot 4 | `WebView2.h:39534-39584` enumerated |
| args `put_State` | slot 7 | same |
| `...EventArgs3` IID (`SavesInProfile`) | `e61670bc-3dce-4177-86d2-c629ae3cb6ac`, `put_SavesInProfile` at slot 12 | `WebView2.h:39785-39880` enumerated |
| `COREWEBVIEW2_PERMISSION_STATE_DENY` | `2` | `WebView2.h:2002-2004` (identical in 3179.45 at `:2382-2384`) |
| `COREWEBVIEW2_PERMISSION_KIND_MICROPHONE` / `_AUTOPLAY` | `1` / `9` | `WebView2.h:1984-1996` |

### Confirmed prerequisite: interop is the only route

A reflection dump of `Avalonia.Controls.WebView 12.0.1` shows `NativeWebView`'s entire public event
surface is `EnvironmentRequested, NavigationCompleted, NavigationStarted, NewWindowRequested,
WebMessageReceived, WebResourceRequested, AdapterCreated, AdapterDestroyed` — **no permission event,
and no public permission API anywhere in the assembly**; the only permission types are internal
interop. `IWindowsWebView2PlatformHandle` exposes exactly `CoreWebView2` and `CoreWebView2Controller`
as raw pointers. D250's premise holds unchanged.

---

## 2. What gets built

`client/src/CcpClient.Desktop/Features/Dtrh/WebViewPermissionDeny.cs` — new, modelled line for line
on `DtrhProcessFailed.cs:26-75` (same typed `AttachOutcome` shape, same `VtableSlot<T>` helper, same
AddRef/Release ownership discipline, same never-throw callback, same idempotent detach that tolerates
a zombie browser). It lives beside `DtrhProcessFailed` because that file is already the shared
interop seam three hosts consume.

**The policy, and why it is that policy:**

- Every permission kind is answered **`Deny`** (`put_State`, slot 7, value 2).
- **Except `AUTOPLAY` (kind 9), which is left untouched at the browser default.** Reason: all four
  hosts already pass `--autoplay-policy=no-user-gesture-required`
  (`DtrhHostWindow.axaml.cs:631`, `GoonHostWindow.axaml.cs:302`, `DtrhLoomWindow.axaml.cs:151`),
  both payloads play media programmatically (`dtrh/engine/audioBus.js`, `goon/exec/videos.js`,
  `goon/ui/hud.js` all call `.play()`), and denying autoplay would stop the pages' own audio and
  video. That is a functional regression, not a tightening, and it would contradict a policy the
  port has already set elsewhere.
- Unknown / future kinds fall into the deny branch by construction (deny is the default arm), so a
  kind added by a future runtime is denied rather than prompted.
- A kind that cannot be read (`get_PermissionKind` returns a failure HRESULT) is **denied**.
- **`SavesInProfile = false`** via QI to args3, tolerating `E_NOINTERFACE` on an older runtime.
  This is not decoration: `GoonHostService.cs:440-446` documents that WebView2 banks permission
  answers per profile and then serves them *without raising the event at all*. Without this write,
  the port's deny becomes a file in `browser_data_*` that outlives the code, and the handler stops
  being consulted. Upstream sets the same flag for the same stated reason (`:494-499`).
- `get_Uri` is never read and no origin is ever logged. The diagnostic line carries the kind and the
  decision only.

**Attach outcome is typed and never swallowed.** `AttachOutcome.Attached(signal)` or
`AttachOutcome.Unavailable(code, detail)` with codes `unsupported-platform` / `invalid-handle` /
`attach-failed` (the last carrying the HRESULT). Each host logs the failure explicitly and says that
the residual stands, exactly as it does for `ProcessFailed` today. Nothing gets quieter.

## 3. Hosts covered, and hosts NOT covered

| Host | File | Covered? |
|---|---|---|
| DTRH game host (embedded WebView2) | `Features/Dtrh/DtrhHostWindow.axaml.cs` | **yes** |
| DTRH Loom studio (embedded WebView2) | `Features/Dtrh/DtrhLoomWindow.axaml.cs` | **yes** |
| Goon practice host (embedded WebView2) — the D250 surface | `Features/Goon/GoonHostWindow.axaml.cs` | **yes** |
| Intake host (embedded WebView2) | `Features/Intake/IntakeHostWindow.axaml.cs` | **NO — outside this packet's File Scope**; reported as a residual, not fixed |
| Chaos tunnel (embedded WebView2) | `Features/Chaos/ChaosTunnelWindow.cs` | **NO — outside File Scope**; same |
| Linux `NativeWebDialog` (WebKitGTK) on all of the above | — | **NO — typed `unsupported-platform`.** WebKitGTK's `permission-request` signal is not reachable through Avalonia's API; the named limit, never faked |

Attach point is `AdapterCreated` on the UI thread (COM apartment binding), immediately after the
existing `AttachProcessFailedSignal()`; detach sits beside the existing
`_processFailedSignal?.Dispose()` in each window's teardown. Three call sites, symmetric with a
pattern already in the tree.

## 4. How I will prove it FIRES, not that it compiles

`client/tests/CcpClient.Tests/WebViewPermissionTests.cs` (new; pure logic, no Avalonia runtime).

A **fake `ICoreWebView2` built in native memory**: a 61-slot vtable where every slot holds a distinct
thunk that records *its own index* when called (61 separate delegates each closing over its index,
via `Marshal.GetFunctionPointerForDelegate`), IUnknown slots 0-2 implemented for real because the
product calls `Marshal.AddRef`/`Release`. Then:

1. `TryAttach(fake, ...)` → assert the recorded call set is **exactly slot 23** (plus AddRef). A
   wrong constant reds by identity of the recorded index, not by inference.
2. The fake `add` captures the handler pointer the product passed. The test then reads that CCW's
   own vtable and **calls `Invoke` at slot 3 through an unmanaged function pointer** — a real COM
   call into the real CCW, the same path WebView2 would take — handing it a fake args object built
   the same way.
3. Assert the handler read `get_PermissionKind` (slot 4) and wrote `put_State` (slot 7) with `2`
   (`DENY`), and that dispose calls **only** slot 24.

Anti-circularity: the fake's layout is a transcription of the published order, so one test asserts
that the *same transcription* predicts the shipping `DtrhProcessFailed` slot constants, read out of
the product by reflection. If my transcription drifted anywhere at or before slot 25, that test reds.

Planned facts (~13, final count in `record.md`):
slot-order-vs-published-order (incl. the ProcessFailed cross-check) · zero pointer → typed
unavailable · attach touches only slot 23 · failing `add` → typed `attach-failed` with the HRESULT ·
microphone denied · autoplay left at default (`put_State` never called) · every other kind incl. an
unknown high value denied · `SavesInProfile=false` written when args3 is offered · args3 absent
(`E_NOINTERFACE`) still denies · unreadable kind denies · a failing `put_State` never lets an
exception escape into native code · dispose calls only slot 24 and is idempotent · a source-text
guard that all three in-scope hosts attach and dispose the signal.

**Evidence class, stated exactly.** This is a unit-level COM interop fact. It is neither
`draw-verified` nor `presentation-verified` (`client/docs/verification-harness.md:56-64`), and it
**does not discharge a headed gate**. What it will NOT prove: that a real Chromium raises
`PermissionRequested` for `getUserMedia` on the voice screen, that the deny reaches the page as a
`NotAllowedError`, or that no prompt is painted. Reaching that needs a headed Windows run that loads
the Goon page, walks the double-locked opt-in and presses record — SP-132's harness deliberately
walks around the voice screen. That gate stays named and open.

## 5. If the slot could not be established

It was established, so this branch is not taken. Had the two sources disagreed, or had Avalonia's
metadata not corroborated the header, the packet would have shipped **nothing** and said so: a wrong
vtable call is worse than an open residual.

## 6. Findings to report, not to fix

- **`Features/Goon/GoonDoors.cs:105-108` becomes false** once this lands. It says *"so the browser
  can still ask you. Closing that needs a PermissionRequested-deny hook (D250)"*. That file is closed
  to this packet; reported, not edited.
- **Intake and Chaos tunnel keep the residual** (out of File Scope).
- **Linux keeps the residual** (no reachable WebKitGTK permission hook).
- Upstream handles permissions on the Goon host only (`GoonHostService.cs:431`); DTRH/Loom/Intake WPF
  hosts have no handler at all, so denying on DTRH and the Loom is a tightening *beyond* upstream.
- Nothing here widens: no device opened, no grant added, no new persistence (`SavesInProfile=false`),
  no networking, no telemetry. `capability-inventory.md:70` is not engaged.

## 7. Floor

Pin **2573 unit / 152 headless**. Expected delta **unit +13, headless 0**, declared in
`spine-tasks/SP-135-webview-permission-deny/floor-delta.json`; `floor.json` is never opened. The
floor run will therefore report 2586/152 — pin plus declared delta — which is the expected outcome,
not a failure.

## 8. Divergence ids

D289 onward in `client/docs/wpf-surface-reachability.md`, divergence rows only.
