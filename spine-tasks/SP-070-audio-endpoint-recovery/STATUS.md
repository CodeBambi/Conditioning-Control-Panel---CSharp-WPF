## STATUS: SP-070 — Audio comes back when the endpoint comes back

**Current Step:** 5
**Last Updated:** 2026-08-14 (worker, Step 5 final checks)
**Blockers:** none

**Floor at authoring:** 996 unit / 35 headless / **2 skipped on Windows** (5 fully-qualified names pinned in
`allowedSkips`; 3 of them execute here, 2 are Linux-gated), build 0W/0E — SP-069, integrate `6feb11e4`.
**This packet ADDS facts:** state the new exact counts and bump `floor.json` `total` in the same commit as
the facts that moved it, with the reason in the message. `allowedSkips` is untouched.

**The defect in one line:** `SoundArbitration.Initialize` sets `_audioDisabledForSession = true` on zero
endpoints (`:214`) or a failed `TryInit` (`:236`), `ReadyLocked` (`:601`) then refuses every play on every
channel forever, and the only product caller of `Initialize` runs once during DTRH host-window construction
(`DtrhHostWindow.axaml.cs:213-220`). WPF fixed exactly this in `d33b5d8d` (#778/#779):
*"`_waveOutPermanentlyUnavailable` is no longer permanent."*

**The defect is PERMANENCE, not disabling.** Refusing playback while the endpoint is dead stays. What changes
is that the refusal expires.

**Direction warning:** SP-068 and SP-069 were subtractive — every change had to narrow. **This one is
restorative.** The safety bound is: a recovery may only restore what a healthy endpoint would already have
permitted, and may never override teardown, panic, or an explicit stop.

**Three non-items from the same WPF commit (record them; never re-file as owed):** the one-shot MTA worker
thread (the NAudio idiom does not exist in this port), the 10-concurrent cap (already landed as
`MaxSfxVoices = 8` with typed drop-on-overflow), and the `IMMNotificationClient` endpoint watcher (its own
board row — Windows-only native, unprovable headless here).

**Everything needed is already injectable:** `ISoundClock` carries `UtcNow` **and** a one-shot `Schedule`
(`AudioSeams.cs:113-135`); the test `ManualClock` (`SoundArbitrationTests.cs:551`) fires due callbacks on
`Advance`; `FakeBackend` (`:465`) constructs both the failure and the recovery. **No wall clock, no real
device, no new seam.**

---

### Step 1: Establish the two facts the design depends on, then design the recovery
**Status:** ✅ Complete (plan review: engine-skipped in-worker, SP-195 — recorded)

- [x] Update STATUS.md before starting work
- [x] WPF recovery re-derived by symbol; found-vs-given recorded for every anchor
- [x] The three non-items stated with reasons (stop and report if any turns out applicable)
- [x] **FACT 1** — the calling thread of the play seam, with the traced call chain
- [x] **FACT 2** — panic and teardown proven separate from `_audioDisabledForSession`
- [x] Design written before code: counter reset by success, clock-gated cooldown, single-flight re-probe
      reusing `Initialize`; cooldown checked before the attempt; `_gate` never held across a backend call;
      discovering caller never blocked
- [x] Knob values adopted with WPF cites (and any divergence argued from the port's shape)
- [x] Bounded-restoration clearance table (teardown, panic, explicit stop, device change, healthy session)
- [x] Pre-approach solo consult (`mode: "solo"`); verdict + actual answering model in `record.md`

### Step 2: Implement the recovery in one file
**Status:** ✅ Complete (plan review: engine-skipped in-worker, SP-195 — recorded; build 0W/0E)

- [x] Disable becomes expiring, not terminal; success path still clears what it clears today
- [x] Re-probe only when suppressed AND cooldown elapsed AND none in flight
- [x] Re-probe reuses `Initialize` with the remembered NAME; never runs under `_gate`
- [x] Success clears + resets; failure re-arms the window
- [x] Teardown and panic untouched
- [x] Transition-only logging; no new observation, persistence, log of user data, or network call
- [x] Typed outcomes only; no exception escapes the play seam
- [x] Product-file `git diff` summarized; no edit outside File Scope

### Step 3: Bind the behavior, one source at a time
**Status:** ✅ Complete (contract green through the wrapper; bite matrix 3 reverts / 3 REDs under `evidence/`)

- [x] The user story as a fact: dead endpoint → refused → endpoint returns → audio plays again
- [x] Failure counting + reset on success
- [x] Cooldown enforced BEFORE the attempt (init call count does not move)
- [x] Single-flight (N concurrent attempts → exactly one init call)
- [x] No busy loop (exactly one attempt per cooldown window)
- [x] Panic and teardown facts
- [x] Negative control: healthy session unaffected — one init call, no extra device calls or log lines
- [x] Bite matrix: three separate reverts, three separate REDs, others green, each captured under `evidence/`
- [x] Landed arbitration facts unchanged in meaning; zero assertions weakened
- [x] `floor.json` `total` bumped in the same commit as the facts

### Step 4: Record + pre-completion consult
**Status:** ✅ Complete (pre-completion consult SHIP; T-18 reasoning-only first call re-asked; races hardened via `_initLock`)

- [x] `record.md` complete (anchors found-vs-given, non-items, FACT 1, FACT 2, design, knobs, clearance
      table, bite matrix, floor bump, run table, consults + actual models, engine-review presence,
      intended board filings with no row state set)
- [x] Honesty cell (no endpoint watcher → recovery waits for the next play attempt; **no real endpoint death
      exercised**; execution vs reading; Linux unproven; the restorative direction and its bound)
- [x] Named flake recorded by name + TRX if it fired, never retried away
- [x] Pre-completion solo consult; verdict + actual model recorded
- [x] STATUS.md accurate before `.DONE`

### Step 5: Testing & Verification
**Status:** 🔵 In Progress

- [x] Contract testCommand green through the wrapper (verify.mjs OK, build 0W/0E, exact counts, 2 pinned skips)
- [x] 3 consecutive full-suite greens, >= 1 fresh-checkout first-ever build; per-run table with TRX paths
- [x] Bite matrix complete (3 reverts, 3 REDs)
- [ ] `git diff --check` clean
- [ ] `git status --short` shows only File Scope paths
- [ ] `git status --porcelain --ignored=matching -uall` shows no new ignored artifact
