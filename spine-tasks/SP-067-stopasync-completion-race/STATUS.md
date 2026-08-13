## STATUS: SP-067 — The StopAsync completion race: a cancelled heartbeat that reports Completed
**Current Step:** Step 1 (in progress)
**Last Updated:** 2026-08-13 (worker, lane-1)
**Blockers:** none

**Floor at authoring:** 900 unit / 35 headless / **2 skipped on Windows** (5 fully-qualified names pinned in `allowedSkips`; 3 of them execute here, 2 are Linux-gated), build 0W/0E — SP-066, integrate `29950e9b`.
**NEW EXACT COUNTS (this packet):** ⚠️ Hydrate — this packet ADDS facts (the zero-tick bindings). State the new exact counts here and bump `client/tests/floor/floor.json` `total` in the SAME commit as the tests that move it, reason in the message. **`allowedSkips`, `admissionRule`, and `skipSemantics` are not to be touched.**

**The defect in one line:** `HeartbeatParticipant.TickLoopAsync` (`client/src/CcpClient.Desktop/Lifecycle/Participants.cs:108`) returns `OperationOutcome.Completed` from a post-loop `return` that is reachable **only** when the token is cancelled — contradicting `async-lifecycle-fault-contract.md` §2:25 and §3.4. The correct shape already exists at `StatusTickerParticipant.cs:150-152`.

### Step 1: Reproduce the race and name the mechanism — RED before any fix — 🔄 In Progress
- [x] Update STATUS.md before starting work
- [x] Contract lines (§2:25, §3.4) read **in the file** and quoted in `record.md` with line numbers
- [x] Bounded-loop probe driving `HeartbeatParticipant` start→stop, recording the terminal outcome per iteration, run against **unmodified** product code
- [x] **RED captured** under `evidence/`: >= 1 iteration reporting `Completed` where the contract requires `Cancelled`, with iteration count and observed hit rate
- [x] Mechanism named precisely: which exit path produced it, why the token was already cancelled there, why the OCE path did not fire — framing (a) explicitly **confirmed or refuted**
- [x] Zero-tick determinism (framing e) stated with the measurement behind it
- [x] Pre-approach solo consult (T-7: `mode: "solo"`, cap the reply, ask narrowly) — verdict + **ACTUAL answering model**; record exactly what surfaced, never stitch a verdict from reasoning

### Step 2: Fix at the source, and sweep the class — ⬜ Not Started
> ⚠️ Hydrate: expand the sweep checkboxes once Step 1's enumeration of `Task<OperationOutcome>` methods exists
- [ ] `HeartbeatParticipant.TickLoopAsync` post-loop return fixed using the **existing in-repo shape**, comment citing the contract section
- [ ] Step-1 probe re-run against fixed code: `Completed` gone across >= the iteration count that produced the RED; GREEN saved beside the RED
- [ ] Every `Task<OperationOutcome>` method in `client/src/**` swept and dispositioned in the record (correct-and-why, or divergent-and-fixed); counts re-derived and reconciled against framing (f)
- [ ] Framing (g) clearance: `LastOutcome` / `Completion` / `Completed` grepped against heartbeat and teardown paths; result stated; any real dependency reported as a finding
- [ ] Zero behavior changed beyond the cancellation-exit outcome value; per-file `git diff` summary in the record

### Step 3: Bind the class so it cannot return — ⬜ Not Started
- [ ] Zero-tick fact at `HeartbeatParticipant` (start → stop immediately → owned completion is `Cancelled`)
- [ ] Zero-tick fact at `StatusTickerParticipant`
- [ ] Zero-tick fact at `AvatarAnimationEngine`
- [ ] Each new fact carries a comment naming **what breaks it** (the defective `return Completed` shape returning)
- [ ] Deterministic either way after the fix — no interleaving dependency, no sleeps, no retries (framing d)
- [ ] Existing loop-outcome tests unchanged in strictness; **zero assertions weakened, zero tolerances widened** — proven per-file in the record
- [ ] `floor.json` `total` bumped in the SAME commit as the new facts, reason in the message; `allowedSkips` untouched
- [ ] `AsyncLifecycleTests.Heartbeat_SkipsProjectionUntilBound_ThenFlowsThroughBoundary` byte-identical unless explicitly justified

### Step 4: Record + pre-completion consult — ⬜ Not Started
- [ ] `record.md` complete: quoted contract lines; probe RED + post-fix GREEN with iterations and hit rates; named mechanism + confirm/refute verdict; sweep table with a disposition per method + magnitude reconciliation; framing-(g) clearance; new facts and what breaks each; floor bump + reason; run table with exact counts and skipped names; consults + **ACTUAL answering models**; engine-review presence per step; intended board filings (named, no row state set)
- [ ] **Honesty cell** — all five: (1) iterations the RED needed and what the probe therefore does NOT bound about the real hit rate; (2) the zero-tick fact is deterministic because **both paths now agree**, not because the scheduler is controlled; (3) whether any `Task<OperationOutcome>` method was dispositioned by reading rather than by executing it; (4) this closes **no product capability** — it removes a lying outcome from an existing one; (5) **Linux unproven** (zero WSL distros — do not fake a Linux run)
- [ ] Named flake `ChaosTunnelLoopbackTests.Logging_RouteClassesOnly_NeverFilenameOrQuery`: if it fired, recorded by name + run number + TRX path, **not** retried away
- [ ] Pre-completion solo consult; verdict + actual model in `record.md`
- [ ] STATUS.md accurate before `.DONE`

### Step 5: Testing & Verification — ⬜ Not Started
- [ ] Contract testCommand passes **through the wrapper** (`verify.mjs` exit 0, build 0W/0E, new exact unit count / 35 headless, skip set exactly the 2 Windows-observed pinned names)
- [ ] **5 consecutive full-suite greens**, >= 1 a **fresh-checkout first-ever build** (cold = a NEW worktree, not a rebuild in place). Per-run table: run, worktree, cold/warm, unit + headless counts, skipped names, TRX path
- [ ] The previously-failing test named in every run's TRX with `outcome="Passed"`
- [ ] `git diff --check` clean
- [ ] `git status --short` shows only File Scope paths
- [ ] `git status --porcelain --ignored=matching -uall` shows no new ignored artifact produced by any run
