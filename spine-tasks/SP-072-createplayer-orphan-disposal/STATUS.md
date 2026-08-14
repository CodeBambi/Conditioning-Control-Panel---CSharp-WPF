## STATUS: SP-072 — An abandoned player construction must never reach the mixer

**Current Step:** 4
**Last Updated:** 2026-08-14 (worker, Step 3 complete — floor 1017/1017 + 35/35 green, bite matrix 3/3 isolated, 20/20 cross-thread repetitions; plan review skipped in-worker per SP-195; Step 4 in progress)
**Blockers:** none

**Floor at authoring:** 1010 unit / 35 headless / **2 skipped on Windows** (5 fully-qualified names pinned in
`allowedSkips`; 3 of them execute here, 2 are Linux-gated), build 0W/0E — SP-071, integrate `d1c69617`.
**This packet ADDS facts:** state the new exact counts and bump `floor.json` `total` in the same commit as
the facts that moved it, with the reason in the message. `allowedSkips` is untouched.

**The hazard in one line:** both `CreatePlayerCore` bodies end with
`_device.MasterMixer.AddComponent(player)` before returning (`SoundFlowAudioBackend.cs:118`,
`SoundFlowDtrhAudio.cs:112`), so a construction whose caller stopped waiting **attaches itself to the live
mixer** — a ghost play plus a leak — and disposing that orphan races device teardown, which SP-071 just
moved onto a background thread.

**THE REQUIRED DELIVERABLE IS THE ORPHAN INVARIANT, NOT THE BOUND.** An abandoned construction never reaches
`MasterMixer`, never plays, is disposed **exactly once**, and its disposal is **ordered** against device
teardown rather than racing it. Orphan safety is the precondition, the way "the give-up path must never
touch `_backend`" was in SP-071.

**The bound is conditional — Step 1 decides it on census evidence, both branches pre-authorized.** If every
caller of both seams can accept a typed no-player outcome (the port's existing `SoundOutcome.Unavailable` /
`Failed` idiom, already used at all three `SoundArbitration` sites), the bound lands here. If any caller
structurally cannot, bound what you can and **name the remainder as the next row** with the caller and the
reason. Do not invent a refusal semantic a caller's contract does not have.

**THE CONSTRAINT THAT DECIDES WHERE THE CODE GOES.** `SoundFlowAudioBackend` and `SoundFlowDtrhAudio` have
**zero test coverage** and cannot be constructed headless (real SoundFlow engine + device). The only audio
facts in the suite drive `FakeBackend` through the seam. **If the mechanism cannot be reached by a headless
fact, it is in the wrong place.** Name the single residual line (the real `AddComponent`) as verified by
reading only.

**Binding rule you must not break:** all SoundFlow player/provider construction runs **off-sync-context**
(SP-025, dump-proven dispatcher deadlock). `SoundFlowDtrhAudio` inlines that logic instead of calling
`OffSyncContext` — say whether the duplication is worth removing here; either way the property survives.

**Row scope guard:** SP-071's *give-up residue accumulation* row stays **OPEN** — it counts outstanding
backgrounded teardowns; this packet is player lifecycle. Do not close it here.

---

### Step 1: Census the callers, decide the bound, design the orphan invariant
**Status:** ✅ Complete
> ⚠️ Hydrate: expand the census rows once the call-site count is confirmed by your own grep

- [x] Update STATUS.md before starting work
- [x] Pre-fix observation captured under `evidence/` (a late construction reaches the mixer and nothing
      disposes it); if unobservable against the real backends, say so and capture the seam-level equivalent
      with the difference named
- [x] **Census of every caller of both `CreatePlayer` seams** — file:line, reaching thread(s), what is held
      while waiting, existing typed no-player path?, cost of a wedged construction beyond the calling thread
- [x] Decision-rule branch chosen and recorded (bound lands here / bound partial + remainder named as a row)
- [x] Orphan invariant written before code (never reaches `MasterMixer`, never plays, disposed exactly once,
      disposal ordered against device teardown, ordinary path unchanged)
- [x] Placement decided on **testability** grounds and justified; residual read-only line named
- [x] SP-025 off-sync-context rule preserved and re-argued if `OffSyncContext` or its inline duplicate moves
- [x] Pre-approach solo consult (`mode: "solo"`); verdict + actual answering model in `record.md`

### Step 2: Implement orphan safety, then the bound your census authorized
**Status:** ✅ Complete

- [x] Abandonment decided **before** the player can reach the mixer; abandoned player disposed on the
      constructing thread and never added — only pool disposers (P3/P4) dispose, under the lifecycle lock; only the waiter ever attaches
- [x] Exactly one path disposes an abandoned player, exactly once, including during concurrent device
      teardown; ordering primitive = the factory `_lifecycle` leaf lock + CAS latch; deadlock-order argued in record.md (caller paths bounded TryEnter — the consult's SP-071-class fix)
- [x] Non-abandoned path observably unchanged (same object, volume, attachment, wrapper; unwrapped exception surface)
- [x] Bound implemented (Step 1 authorized it), typed `PlayerConstructionTimeoutException` → existing refusal vocabulary — never a null or placeholder player
- [x] Transition-only logging (one line per abandonment); nothing new observed, persisted, or transmitted (grep shown in record.md)
- [x] No awaitable UI dispatch, no `SynchronizationContext.Current` capture, no new dispatch primitive
- [x] Every SP-070 / SP-071 property preserved (SoundArbitration.cs untouched; suite proves in Steps 3/5)
- [x] Per-file `git diff` summarized; no edit outside File Scope

### Step 3: Bind the behavior, one source at a time
**Status:** ✅ Complete

- [x] Orphan fact: `Construction_Abandoned_NeverAttached_NeverPlayed_DisposedOnce` — asserted from the fake's own event record
- [x] Exactly-once fact: `Construction_CompletionRacesAbandonment_DisposedExactlyOnce` — both disposers provably armed (log-hook rendezvous), CAS latch admits one
- [x] Ordering fact: `Construction_OrphanDisposal_OrderedAgainstDeviceTeardown` — non-overlap observed from INSIDE the parked teardown + UntilSync-fails-on-absence (no IndexOf-sentinel shape)
- [x] Negative control: `Construction_Ordinary_AttachedOnce_NeverDisposed_NoAbandonmentLine`
- [x] Bound's caller behavior proven: `PlaySfx_ConstructionTimeout_TypedFailed_NeverInPool`, `PlayVoice_ConstructionTimeout_TypedFailed_ChannelStaysIdle` (+ torn-down refusal fact)
- [x] Every landed SoundArbitration + DTRH-effects fact green and unchanged in meaning (test file diff = pure addition + one fake hook)
- [x] Bite matrix: 3 reverts captured separately under evidence/ (bite-1 abandonment mark → orphan pin RED at UntilSync + documented cascade; bite-2 CAS latch → ONLY exactly-once pin RED, 3/3; bite-3 ordering lock → ONLY ordering pin RED at its ordering assertion, 3/3); restore verified 42/42 green
- [x] No timing-dependent determinism; every rendezvous is a gate/signal; no waits outside TestWait
- [x] `floor.json` `total` bumped 1010 → 1017 in the same commit as the facts (reason in message)

### Step 4: Record + pre-completion consult
**Status:** 🔶 In Progress

- [ ] `record.md` complete (pre-fix observation, census table, decision-rule branch + reason, invariant +
      design, testability/placement argument with the residual read-only line, deadlock-order argument,
      bite matrix, floor bump, run table, consults + actual models, engine-review presence, intended board
      filings)
- [ ] Honesty cell (fake vs a real wedged `AssetDataProvider`; the real `AddComponent` line never exercised;
      any caller left unbounded; execution vs reading; **Linux unproven**; whether SP-071's give-up residue
      row got cheaper or is untouched)
- [ ] Named flake recorded by name + TRX if it fired, never retried away
- [ ] Pre-completion solo consult; verdict + actual model recorded
- [ ] STATUS.md accurate before `.DONE`; intended board filings named (set no row state)

### Step 5: Testing & Verification
**Status:** ⬜ Not Started

- [ ] Contract testCommand green through the wrapper (verify.mjs exit 0, build 0W/0E, new exact unit count /
      35 headless, skip set exactly the 2 pinned Windows names)
- [ ] 3 consecutive full-suite greens, >= 1 a fresh-checkout first-ever build; per-run table with TRX paths
- [ ] Cross-thread facts run >= 20x filtered, zero flakes, count stated
- [ ] Bite matrix complete (each revert named with the pins it reddened and the pins that stayed green)
- [ ] `git diff --check` clean
- [ ] `git status --short` shows only File Scope paths
- [ ] `git status --porcelain --ignored=matching -uall` shows no new ignored artifact
- [ ] `.DONE` created as the last action and **NOT committed** (the engine's lane-commit stages it)
