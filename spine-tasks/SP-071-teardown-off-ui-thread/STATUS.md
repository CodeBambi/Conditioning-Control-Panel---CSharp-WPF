## STATUS: SP-071 — Host close must not wait on a wedged native audio probe

**Current Step:** done
**Last Updated:** 2026-08-14 (worker, all steps complete — .DONE)
**Blockers:** none

**Floor at authoring:** 1005 unit / 35 headless / **2 skipped on Windows** (5 fully-qualified names pinned in
`allowedSkips`; 3 of them execute here, 2 are Linux-gated), build 0W/0E — SP-070, integrate `9e6498b6`.
**This packet ADDS facts:** state the new exact counts and bump `floor.json` `total` in the same commit as
the facts that moved it, with the reason in the message. `allowedSkips` is untouched.

**The defect in one line:** `SoundArbitration.Dispose` takes `_initLock` (`:1087-1091`) before
`_backend.Dispose()` (`:1093`), `TeardownBarkPipeline` (`DtrhHostWindow.axaml.cs:255-262`) is called from
the host-window close handler (`:153`) on the **UI thread**, and the only case with a probe in flight is a
**dead endpoint** — this feature's own failure mode. The host is a non-modal child window
(`DtrhLaunchCoordinator.cs:167`), so close is **not** process exit.

**THE TRAP — do not take the obvious fix.** A timeout on the `_initLock` acquisition that then continues
runs `_backend.Dispose()` while a native init is in flight: the **process-fatal** concurrent-native-call
class `_initLock` exists to prevent. **The fix is moving the teardown off the UI thread** — unbounded lock
wait on a background thread; bounded UI-side wait with a typed give-up that **never touches the backend**.

**The invariant to write down first:** exactly one thread ever disposes the backend; never while a native
call is in flight; the UI caller's wait is bounded; the give-up path never touches the backend; after a
give-up the backgrounded teardown still completes and still disposes exactly once; `Dispose` stays
idempotent.

**WPF parity, not invention:** `5a168554` ("stop the UI thread joining a wedged render thread, and name the
next one") is upstream's pass over this class for the v6.6.3 hang cluster. Port the **remedy shape** (bound
the wait, degrade instead of block, name what cannot be bounded), never its WPF-specific mechanics. The
port's own `async-lifecycle-fault-contract.md` §5 already makes the UI boundary post-only.

**Out of scope by decision, filed as its own row at land:** `SoundFlowAudioBackend.CreatePlayer`
(`:108` → `OffSyncContext.Run`) and `SoundFlowDtrhAudio.CreatePlayer` (`:100`) block the UI thread inside a
native `AssetDataProvider` construction. They change a **synchronous seam contract** and carry an
**orphan-disposal** hazard (a late construction adds itself to `MasterMixer`). Census them, do not fix them.

---

### Step 1: Prove the block, census the class, then design the handoff
**Status:** ✅ Complete (plan review: engine-skipped, SP-195 — recorded in record.md)

- [x] Update STATUS.md before starting work
- [x] Captured pre-fix RED under `evidence/` (fake parked in `TryInit`, `Dispose` does not return)
- [x] Caller chain re-derived with own cites (thread, close handler, process survives)
- [x] **Census of every blocking wait in `client/src/**`** — file:line, reaching thread(s), bounded?, what
      it waits on, consequence if it never returns; the two `CreatePlayer` sites named as a separate packet
- [x] Invariant written first, then the design that satisfies it (single disposer, never concurrent with a
      native call, bounded UI wait, give-up never touches the backend, completion still disposes once,
      idempotent)
- [x] Why a lock timeout is the WRONG fix, stated plainly (stop condition if the design contains it)
- [x] Budget chosen with in-repo justification and its home named; no wall clock
- [x] Reopened-host answer recorded
- [x] Pre-approach solo consult (`mode: "solo"`); verdict + actual answering model in `record.md`

### Step 2: Implement the handoff in one file
**Status:** ✅ Complete (plan review: engine-skipped, SP-195)

- [x] UI-safe work stays on the caller; backend teardown handed to a background thread
- [x] Bounded UI-side wait with a typed, once-logged give-up that never touches `_backend`
- [x] Background teardown takes `_initLock`, waits as long as needed, disposes exactly once
- [x] `Dispose` idempotent (no double dispose, no second teardown, prompt return)
- [x] `PanicReset` placement decided deliberately and justified
- [x] Every SP-070 property preserved (no post-teardown probe, play seam never takes `_initLock`, one-way
      lock order)
- [x] Transition-only logging; nothing new observed, persisted, or transmitted
- [x] No new dispatch primitive, no awaitable UI dispatch, no `SynchronizationContext.Current` capture
- [x] Product-file `git diff` summarized; no edit outside File Scope

### Step 3: Bind the behavior, one source at a time
**Status:** ✅ Complete (plan review: engine-skipped, SP-195; bite matrix A/B/C captured under evidence/)

- [x] Bounded-return fact (the pin that captures today's behavior as RED)
- [x] **Ordering fact:** backend not disposed while the native call is in flight (assert the order, not
      merely that nothing threw)
- [x] Completion fact: after a give-up, teardown still disposes exactly once
- [x] Idempotence fact
- [x] Negative control: ordinary teardown observably unchanged, no give-up line
- [x] SP-070's teardown fact and all landed facts green and unchanged in meaning
- [x] Bite matrix: separate reverts, separate REDs, each pin's fixture shown to reach its mechanism
- [x] No timing-dependent determinism; no waits outside `TestWait`
- [x] `floor.json` `total` bumped in the same commit as the facts

### Step 4: Record + pre-completion consult
**Status:** ✅ Complete (record.md; 2 consults, solo, verdicts + models recorded; pre-completion consult's 2 record edits applied)

- [x] `record.md` complete (pre-fix RED, caller chain, **census table**, invariant + design, why-not-timeout,
      budget justification, reopened-host answer, bite matrix, floor bump, run table, consults + actual
      models, engine-review presence, intended board filings incl. the `CreatePlayer` row)
- [x] Honesty cell (fake vs real wedged native call + manual gate; give-up residue; execution vs reading;
      Linux unproven; **this closes one member of the class, not the class**; + residual PanicReset native
      wait, pre-completion consult finding)
- [x] Named flake recorded by name + TRX if it fired, never retried away (did NOT fire in any run)
- [x] Pre-completion solo consult; verdict + actual model recorded
- [x] STATUS.md accurate before `.DONE`

### Step 5: Testing & Verification
**Status:** ✅ Complete

- [x] Contract testCommand green through the wrapper (verify.mjs OK, build 0W/0E, 1010 unit / 35 headless, 2 pinned skips)
- [x] 3 consecutive full-suite greens, run 2 a fresh-checkout first-ever build (`C:\Code\sp071-cold`); per-run table with TRX paths in record.md
- [x] Cross-thread facts run 20x filtered (23 matched facts), zero flakes
- [x] Bite matrix complete (A/B/C, evidence/bite-revert-*.txt)
- [x] `git diff --check` clean
- [x] `git status --short` shows only File Scope paths
- [x] `git status --porcelain --ignored=matching -uall` shows no new ignored artifact
