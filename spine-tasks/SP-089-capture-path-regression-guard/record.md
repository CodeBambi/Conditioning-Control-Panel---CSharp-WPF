# SP-089 — record

Branch `sp089-capture-path-regression-guard`, worktree `.claude/worktrees/sp089`, base `feat/crossplatform` at `d8793ee0`.

Baseline in this worktree, before any edit, both steps through `with-slot.mjs --slots 3`:

```
build: Build succeeded. 0 Warning(s) 0 Error(s)
gate:  FLOOR OK: CcpClient.Tests: 1046/1046 total, 2 skipped
       [ChaosTunnelCapabilityTests.Linux_UnavailableNamesTheTunnelsOwnTwoGaps,
        SecretStoreTests.LinuxProbe_TypedOutcome_NeverFaked];
       CcpClient.HeadlessTests: 35/35 total, 0 skipped
```

**Both SP-082 title facts EXECUTED in the baseline** — the only skips are the two Linux-only
pins. This machine is interactive. That is load-bearing for the honesty section: nothing here
observes the locked-session leg.

## 1. Census, re-derived here (Step 1)

`grep TryCaptureForegroundTitle client/` → **5 occurrences, 4 of them code, no fifth**:

| Where | What |
|---|---|
| `client/src/CcpClient.Desktop/Ai/AiAwarenessService.cs:315` | the probe's ternary |
| `:335` | the definition |
| `:579` | the observation seam |
| `client/tests/CcpClient.Tests/AiAwarenessTests.cs:415` | the only execution in `client/tests` |
| `client/docs/task-board.md:97` | prose (the board row this packet closes) |

Both reachability constraints re-measured, both confirmed:

- **`InternalsVisibleTo`: zero hits in `client/src`.** The only two hits under `client/` are prose
  (`client/docs/ai-provider-spike.md:58`, `client/docs/task-board.md:71`).
- **`DllImport`/`LibraryImport`: zero hits anywhere under `client/tests`.** 26 hits under `client/`
  overall, all in `client/src` (11: `SecretStores.cs` 3, `AiAwarenessService.cs` 3,
  `ChaosTunnelWin32.cs` 3, `DtrhCapabilityProbes.cs` 1, `DtrhMotionPreference.cs` 1),
  `client/spikes` (8), `client/tools/verify/capture.ps1` (3), and docs (1 prose).

So `NativeMethods` is genuinely unreachable from a test today, and the suite has no precedent for
declaring its own imports. Both facts below are built inside those constraints.

Pin read from `client/tests/floor/floor.json:4` and `:25`: **1046 / 35**. Not edited.

## 2. Branch decision: **Branch A**

Branch B — red a broken declaration from the test assembly alone — is *partly* achievable: F1 below
is exactly that, and it does **not** pass with `NativeMethods` deleted, so it clears the packet's
stated disqualifier. But B still fails **Completion Criterion 1**, which asks for a fact that
*exercises* the real text-reading P/Invokes:

- the only in-product route to the text half sits behind `GetForegroundWindow()`
  (`AiAwarenessService.cs:343-347`), whose return the test cannot control;
- `NativeMethods` is `private static class` (`:361`) and there is no `InternalsVisibleTo` in
  `client/src` (measured above);
- reflection over `NativeMethods` is forbidden by the packet;
- a test-side re-declaration of `user32` would pass with `NativeMethods` deleted — forbidden, and
  rightly, since it tests Win32 rather than this product.

A source pin proves a declaration *looks* right; it can never prove it *binds*, and it cannot see a
semantic break (`buffer.Capacity` → `length`). By the packet's own standard B is a decoration.
**Branch A selected.** F1 is a supplement to F2, never a substitute — §5 shows each has a mutation
that reds it alone.

### The packet's suggested handle was rejected, on measurement

The packet suggests driving the seam with `GetDesktopWindow()`. Measured on this box:
`GetWindowTextLengthW(GetDesktopWindow())` is **0**, so the seam's `length <= 0 → return true` arm
short-circuits and **`GetWindowTextW` is never called** — the packet's own named CharSet mutation
would have stayed GREEN. The shape was "suggested, not mandated". Rejected with it:
`GetShellWindow()` (depends on Explorer being the shell, and the text is not ours),
an arbitrary invalid handle (length 0, same short-circuit), `MainWindowHandle` (zero for a test
host). **Chosen: a message-only window the test creates and owns**, whose title the test authors.
Filed for the orchestrator as an authoring correction — see §6.

## 3. The product change: a pure extraction, shown not asserted

`TryCaptureForegroundTitle` keeps its OS guard and its zero-handle check and now ends with the
single new line `return TryCaptureWindowTitle(hwnd, out title);`. The new
`public static bool TryCaptureWindowTitle(IntPtr hwnd, out string title)` carries the length-and-text
half.

**Mechanical proof of byte-identity — `git diff --numstat` on the product file:**

```
17      0       client/src/CcpClient.Desktop/Ai/AiAwarenessService.cs
```

**17 insertions, ZERO deletions.** Not one pre-existing line was modified, moved or removed: git
matched the ten lines from `var length = NativeMethods.GetWindowTextLengthW(hwnd);` through
`return true;` as unchanged CONTEXT, trailing `// a foreground window with an empty title …`
comment included. The 17 added lines are: the `return TryCaptureWindowTitle(...)` line plus the
brace that closes the old method, a 9-line XML doc, the new signature and its brace,
`title = string.Empty;`, and one blank line.

`title = string.Empty;` is the only added *statement*, and it is forced: the `length <= 0` branch
returns without assigning, so C# definite assignment requires the `out` parameter be set. It
assigns the same value line `:337` already assigned. Disclosed rather than smuggled.

**Behaviour delta, exhaustive over the original's four exits:**

| Exit | Before | After |
|---|---|---|
| not Windows | `false`, `""` | identical — arm untouched in `TryCaptureForegroundTitle` |
| `hwnd == 0` | `false`, `""` | identical — arm untouched |
| `length <= 0` | `true`, `""` (set at `:337`) | `true`, `""` (re-set in the overload — same value) |
| `length > 0` | `true`, `buffer.ToString()` | identical, same capacity arithmetic |

P/Invoke order is unchanged: `GetForegroundWindow` → `GetWindowTextLengthW` → `GetWindowTextW`.
No `[SupportedOSPlatform]` was added — that would exceed "pure extraction", and CA1416 has nothing
to say because the raw user32 imports carry no platform annotation (`DtrhMotionPreference.cs:97`
shows the attribute is opt-in in this tree).

## 4. The two facts

Both live in `client/tests/CcpClient.Tests/AiAwarenessTests.cs`. **Neither can skip, on any
platform.** The SP-082 skip predicates at `:406-411` and `:466-471` are untouched — the diff on the
test file is **253 insertions, 0 deletions**.

- **F2 `CapturePath_OnAWindowTheTestOwns_ReadsBackEveryCharacter_ThroughTheProductsOwnDeclarations`**
  creates a message-only `"STATIC"` window (`HWND_MESSAGE`, parent `-3`) carrying a 22-character
  non-ASCII title, calls the product's `TryCaptureWindowTitle` on that handle, and requires every
  character back. `GetForegroundWindow` is never called, so no foreground window is required.
  A null handle **throws** naming `Marshal.GetLastWin32Error()` — never a skip, never a silent pass.
  `DestroyWindow` runs in a `finally`.
- **F1 `CapturePathDeclarations_PinModuleEntryPointAndCharSet_WithoutExecutingAnything`** reads
  `AiAwarenessService.cs` from source, extracts the `NativeMethods` block by brace matching, and
  compares an ordered whitespace-normalised `attribute => signature` array against the expected
  three. It executes no native code, so it runs identically on Linux.

**Vacuous-shape posture: zero detected shapes, ledger untouched.** Every platform decision lives in
the nested `Sp089CaptureProbe` fixture, which is not a `[Fact]` body and which
`VacuousShapeDetector.Scan` therefore does not analyse (`VacuousShapeDetector.cs:84-107`). Both fact
bodies contain no `OperatingSystem.Is`, no `RuntimeInformation.IsOSPlatform`, no `Assert.Skip*`, no
`File.Exists(`/`Directory.Exists(`, no bare `return;`, and exactly one `Assert.` each at guarding
depth 0. `VacuousShapeGuardTests` passes, so `client/tests/floor/vacuous-shape-ledger.json` is not
touched and no existing ledger entry gained a shape.

**Non-interference with SP-082.** A window parented to `HWND_MESSAGE` is message-only: never
visible, never in the z-order, never activatable, never enumerated, and excluded from
`HWND_BROADCAST`. It cannot become the foreground window, so it cannot flip either SP-082 skip
predicate in either direction. Style `0` (no `WS_VISIBLE`) and the `finally` `DestroyWindow` are
redundant belts, not the argument. Empirically the SP-082 facts executed and passed in the final
gate alongside F2.

**Win32 thread affinity** is the one invariant this change introduces, and it is why F2 is a
synchronous `public void` fact rather than `async Task`: `CreateWindowExW` binds the HWND to the
calling thread, same-process `GetWindowText*` are `WM_GETTEXTLENGTH`/`WM_GETTEXT` sends that go
straight into `DefWindowProc` on the owning thread, and a cross-thread send would block until the
owner pumps — an unbounded hang the timing guard cannot see, because it scans for managed
sleep/delay/clock tokens, not a blocking native send. The comment in the fixture says so.

## 5. Revert matrix — executed, one mutation at a time

Each mutation was applied alone, built through the semaphore, and run; the tree was then restored
from a pristine copy and the restore verified by **SHA-256 of both touched files**, not by eye.

Pre-mutation hashes, and the hash after **every** restore (all four identical, verified each time):

```
client/src/CcpClient.Desktop/Ai/AiAwarenessService.cs
  456E998EACE499AF0AE4DE7CDB2DD29E623D1DE6DF935BB16558FCDBF27DBF01
client/tests/CcpClient.Tests/AiAwarenessTests.cs
  24157A7B4686FC33A9A044CD81922BD18159CC61D04CE5D7A930439A48BD433D
```

`git diff --numstat` after the last restore is again `17 0` / `253 0` — the committed tree is the
pre-mutation tree.

| # | Mutation (one at a time) | F1 | F2 | SP-082 `TitleProbe` | SP-082 `TitleObservation` | Reds |
|---|---|---|---|---|---|---|
| **M1** | `GetWindowTextW` drops `CharSet.Unicode` | **RED** | **RED (host abort)** | GREEN | GREEN | 2 |
| **M2** | `GetWindowTextW` → `GetWindowTextX` (decl + its one call site) | **RED** | **RED** | **RED** | **RED** | 4 |
| **M3** | `GetWindowTextLengthW` drops `CharSet.Unicode` — *negative control* | **RED** | GREEN | GREEN | GREEN | 1 |
| **M4** | text half → `title = string.Empty; return true;` (declarations intact) | GREEN | **RED** | GREEN | **RED** | 2 |

Evidence per row:

- **M1.** F1 red, cleanly, naming the offending element: `Collections differ at index 2 … Expected
  "[DllImport("user32.dll", CharSet = CharSet.Unicode…" Actual "[DllImport("user32.dll")]…"`.
  F2 red by **killing the test host**: `Catastrophic failure: Test process crashed with exit code
  -1073740791` (0xC0000409, `STATUS_STACK_BUFFER_OVERRUN`) — the ANSI marshaller's buffer is
  smaller than the wide bytes the `W` export writes. **This is why F1 earns its place:** M1's red
  through F2 alone names no fact and reports a crashed run, whereas F1 reds cleanly and points at
  the line. (Carried condition 5, discharged: if M1 is what breaks, `check-floor.mjs` reports a
  crashed run rather than a named red, so **Completion Criterion 2 is carried by M1→F1 and
  M2→F1+F2**, not by a clean per-fact red under M1.)
- **M1, the finding I did not expect.** The two SP-082 facts **passed, twice, under a genuinely
  broken `GetWindowTextW` declaration**, on an interactive session where they executed rather than
  skipped. Run separately precisely because the F2 crash would otherwise have masked them. This is
  the packet's thesis confirmed harder than it was stated: the old facts do not catch a CharSet
  regression *even when they run*, because their input is whichever window happens to be focused,
  whose title length they do not control and which decides whether `GetWindowTextW` is reached at
  all. I am reporting the two measurements, not asserting the exact byte arithmetic behind them.
- **M2.** F1 red naming `GetWindowTextX` at position 96. F2 red with
  `System.EntryPointNotFoundException : Unable to find an entry point named 'GetWindowTextX' in DLL
  'user32.dll'`, and the stack runs through
  `AiWindowTitleCapability.TryCaptureWindowTitle` → `Sp089CaptureProbe.Run` — i.e. through the
  **product's** declaration, which is what makes F2 a guard on this product rather than on Win32.
  Both SP-082 facts red too.
- **M3 (negative control).** Exactly one failure in the whole class: F1, at index 1
  (`GetWindowTextLengthW`). F2 and both SP-082 facts green. Confirms the mutation is behaviourally
  inert — that export takes no string parameter and its declared name already ends in `W`, so the
  exact export resolves under either `CharSet` — and therefore that **no executing fact can catch
  it**. F1 is not redundant with F2.
- **M4.** F2 red with the exact value diff:
  `Expected: Sp089Capture { Captured = True, Title = SP-089 éüß 中文テスト Живот }` /
  `Actual: Sp089Capture { Captured = True, Title =  }`. F1 green, declarations untouched.
  Confirms F2 pins *semantics*, not merely execution.

**M3 and M4 are the honest half of this table.** M3 reds F1 alone; M4 reds F2 alone. That, and not
prose, is the argument that these are two facts rather than one written twice.

## 6. Carried conditions from the plan review — each discharged

1. **`SetLastError = true` and `CharSet.Unicode` on the fixture's own imports.** Both added.
   `CreateWindowExW` is declared `[DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]`
   and `DestroyWindow` `[DllImport("user32.dll", SetLastError = true)]`. Without `SetLastError` the
   null-handle throw would print stale error state and lie at exactly the moment someone needs it
   (the reviewer measured a non-zero last-error after a *successful* create); without
   `CharSet.Unicode` the non-ASCII `ProbeTitle` would marshal ANSI into a `W` export and F2 would
   red for a fixture reason wearing a product reason's clothes. The reason is written into the code
   comment, not just here.
2. **The parallelism premise was wrong in a harmless direction.** Re-checked: no
   `CollectionBehavior` attribute and no `xunit.runner.json` anywhere under `client/`, and
   `AiAwarenessTests` carries no `[Collection]` (`:17`), so the class is its own implicit collection
   and F2 never runs concurrently with `TitleProbe_…`. The `HWND_MESSAGE` argument holds either
   way, so nothing in the design changed; this record does **not** assert concurrent execution as
   fact, which the plan did.
3. **`CapabilityState.Available(title)` was dropped.** F2 compares a test-local
   `private sealed record Sp089Capture(bool Captured, string Title)` instead. Same value equality,
   same single `Assert.Equal`, without borrowing a vocabulary whose documented contract
   (`AiAwarenessService.cs:277-285`) is a content-free detail carrying title LENGTH only. Nothing
   leaked either way — the value is a test-authored literal that reaches no sink — but the reuse
   would have taught the opposite of the invariant, and on failure xunit prints the Detail.
4. **§1a's "everything else is prose" framing was overstated; corrected here.** The identifier
   appears in exactly **5** places tree-wide, listed in §1 — `port-status.md`, `port-lessons.md`,
   `window-behavior-manifest.md`, `her-room-divergence-audit.md`, `floor.json:15`,
   `AiAwarenessTests.cs:396` and `AiAwarenessService.cs:280` discuss the capture path *topically*
   but do **not** contain the identifier. The load-bearing count (four code hits, no fifth) was
   right; only the residue framing was loose. Likewise the plan's "eight files with a private
   `FindRepoRoot`" is nine `.cs` files, one of which is the detector rather than a test class; F1's
   copy is a deliberate instance of that documented idiom, not a missed abstraction.
5. **M1's effect on the host is recorded in the shape the reviewer asked for** — see the M1 bullet
   in §5 and the Completion-Criterion-2 attribution there.

## 7. What is NOT proven (Step 5 — the honesty section)

- **The locked-session leg is NOT verified.** The baseline gate reported 2 skips, both Linux-only,
  with both SP-082 title facts *executing* — this machine was interactive for every run in this
  record. F2's locked-session claim rests on a Win32 property (a message-only window can be created
  and read on a non-interactive desktop) that is argued and measured on an **interactive** box,
  never observed under lock. **Manual gate, named and not claimed:** lock the workstation or run
  over a disconnected RDP session, run `node client/tests/floor/check-floor.mjs`, and confirm the
  two SP-082 facts skip while F2 passes.
- **`GetForegroundWindow` is executed by neither new fact.** F2 deliberately bypasses it — that is
  the point of the seam. So an always-zero or otherwise broken `GetForegroundWindow` remains
  **indistinguishable from a locked session**, exactly as before this packet; the SP-082 skip
  absorbs it either way. F1 catches only a rename or module change of that declaration, never a
  wrong return. The packet predicted this and it is correct.
- **A CharSet change on `GetWindowTextLengthW` is behaviourally inert** — M3, measured. No executing
  fact can red it. Only F1's source pin can, and F1 proves the declaration *looks* right, never that
  it *binds*.
- **Neither fact subsumes the other, and neither alone closes the row.** F1 cannot prove binding;
  F2 cannot prove declaration text. M3 and M4 are the evidence.
- **Zero detected shapes is a lexical statement, not a runtime one** (`VacuousShapeDetector.cs:17-27`).
  It means the ledger stays untouched; it does not mean a later edit inside `Sp089CaptureProbe`
  cannot make these facts vacuous. The fixture is where the real platform branch lives, and a
  lexical detector cannot see it — stated as a cost, not hidden.
- **The non-Windows leg of F2 is thin.** It executes product code (`TryCaptureForegroundTitle`) and
  compares what the product actually answered rather than restating a constant, but all it can
  assert off Windows is `false` with an empty title. No Linux machine ran it in this lane.
- **Residual false-red risk, named:** on a hostile window station where `CreateWindowExW` fails, F2
  throws rather than skips. That is the packet's demanded posture — a guard that quietly stands
  down is the hole SP-089 exists to close — but it is a loud failure on a machine where nothing is
  actually broken.
- **No headed gate is discharged.** Nothing in this packet is `presentation-verified`.

## 8. Out of File Scope — filed, not fixed

1. **The packet's own `GetDesktopWindow()` suggestion is false on this machine.** Measured length 0,
   so `GetWindowTextW` is never reached and the packet's named CharSet mutation would have stayed
   green. A packet-authoring correction for the orchestrator (§2).
2. **`client/docs/her-room-divergence-audit.md:51`** cites `client/Ai/AiAwarenessService.cs:312-336`
   — a path that has not existed since the file moved under `client/src/CcpClient.Desktop/`, and a
   range that no longer covers `TryCaptureForegroundTitle` (`:335-359`, now `:335-350` plus the new
   overload). `client/docs/**` is must-not-change for this lane. A live hit for SP-088's citation
   drift detector.
3. **`AiAwarenessService.cs:280`** doc comment names "GetForegroundWindow + GetWindowTextW", omitting
   `GetWindowTextLengthW`. The file is in scope but the packet limits the edit to the extraction, so
   it is left alone.
4. **Latent buffer-sizing coupling.** `nMaxCount = buffer.Capacity` on a `StringBuilder` ties
   correctness to the marshaller's char width; when the CharSet is lost the failure mode is a
   process abort rather than a clean error — measured, M1, exit 0xC0000409. Not a defect today, and
   changing it would be a forbidden behaviour change. A `LibraryImport` + `Span<char>` rewrite would
   remove the class.

## 9. A stale-build trap I walked into, and what it cost

Recorded because it nearly produced a false red and because the standing mitigation did not catch it.

The first "final" gate reported **2 failures** — F2 and `TitleObservation_…` — with F2 showing
`Title = ""`. That is M4's signature exactly, and the cause was mechanical: I restored the product
file between mutations with `Copy-Item` from a pristine copy, and **`Copy-Item` preserves the source
file's LastWriteTime**. The restored file's mtime (00:30:04) was therefore *older* than the DLL that
M4's build had produced (00:39:18), so MSBuild's incremental check considered the output up to date
and **skipped compilation entirely**. The gate ran **M4's stubbed binary against pristine source**.

The wave rule "build immediately before the gate" exists for exactly this class and did not help:
the build ran, printed `Build succeeded`, and was a genuine no-op. What settled it was measurement,
not inference — a standalone Win32 probe run at the same moment showed the mechanism working
perfectly (message-only STATIC window, `len=15`, exact round-trip, input desktop `Default`
reachable), which ruled out the machine and pointed at the binary. Refreshing the source timestamp
forced a real recompile and the failures vanished.

**This does not touch the mutation matrix in §5.** Each of M1–M4 was applied with an editor write,
which stamps a fresh mtime and forces a recompile, and each run showed that mutation's own distinct
signature (M1 the CharSet diff and the 0xC0000409 abort, M2 `GetWindowTextX`, M3 index 1 alone,
M4 the empty-title value diff). Only the post-restore build — the one with no edit in front of it —
was stale. The committed content is byte-identical to the pre-mutation tree: the SHA-256 pair in §5
was re-verified *after* the timestamp refresh and is unchanged, and `git diff --numstat` is still
`17 0` / `253 0`.

**Method note for the next lane:** verify a restore by hashing *and* confirm the rebuild actually
recompiled, or restore with a write that updates mtime. A content hash proves the source; it says
nothing about the binary the gate will run.

## 10. Final verification

Build then gate, in this worktree, both through `node client/tools/gate/with-slot.mjs --slots 3`:

```
build: Build succeeded. 0 Warning(s) 0 Error(s)
gate:  FLOOR CHECK FAILED (SP-065):
         CcpClient.Tests: FLOOR VIOLATION — total drift: 1048 result(s) (pin total 1046).
       Passed! - Failed: 0, Passed: 1046, Skipped: 2, Total: 1048 - CcpClient.Tests.dll
       skips: [ChaosTunnelCapabilityTests.Linux_UnavailableNamesTheTunnelsOwnTwoGaps,
               SecretStoreTests.LinuxProbe_TypedOutcome_NeverFaked]
```

**This is the designed state for a bound lane.** Observed **1048 / 35**; pin **1046 / 35**; declared
delta `spine-tasks/SP-089-capture-path-regression-guard/floor-delta.json` = **+2 unit / +0 headless**.
`observed == pin + declared` in both projects: **1046 + 2 = 1048**, **35 + 0 = 35**. Zero failures,
zero bad outcomes, and the only two skips are the same two Linux-only pins as the baseline — the
SP-082 title facts executed and passed alongside the new facts. The one violation reported is the
count drift this packet declares. `client/tests/floor/floor.json` is **not** edited; the land sums
the deltas.
