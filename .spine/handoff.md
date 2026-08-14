# HANDOFF — 2026-08-14 — wave 29 LANDED (SP-072), nothing in flight

**Status: NO ACTIVE BATCH.** Wave 29 landed at `c04ecb67` (a real merge — the base moved mid-batch for the
v6.8.0 upstream sync), reconciled in the commit that carries this file, archived.
**Base floor: 1017 unit / 35 headless / 2 NAMED skips, build 0W/0E.**
**Next unused task ID: SP-073.**

## Which phase is yours

Reconcile first, then classify. Do **not** trust this file's phase label — check `spine status --diagnose`.

- **Batch running → port.txt case C: EXIT AT ONCE.**
- **Batch finished / `needs_integrate` → case A: LAND IT.** A finished batch is not a landed one.
- **No batch + claimable work → case B: AUTHOR + LAUNCH ONE WAVE.** This is the expected next phase.

## THE BIG CHANGE THIS PHASE: THE OWNER OPENED THE NETWORK

**Owner decision 2026-08-14, verbatim:** *"we want the same behaviour but how it is done under the hood we
dont care about in the port. So yes we want all the external connections to work for sure."*

- **Authorized:** external network + the credentials those surfaces need, for **all four** v6.8.0 surfaces —
  THE DESCENT (row unblocked), JUST DROP (ungated), remote media in the video path, and the remote-media
  offer prompt. **The "how" is explicitly free:** reproduce the user-observable outcome, never WPF's
  `HttpClient` / header / `X-Auth-Token` mechanics — those are evidence, not a design to copy.
- **NOT authorized, and do not let a packet drift into it:** webcam, biometric, capture, consent, moderation
  and logging boundaries are untouched; secret VALUES still never enter documents, diagnostics or logs
  (names only); the redaction registry still binds every new log site.
- **The "zero external network" claim was never a principle**, only the state of a port nobody had asked to
  connect. It is superseded **for these surfaces**, and its send-attempt-counter proof must be **re-scoped,
  not deleted** — it still proves the AI pipeline makes no unsanctioned call. A packet that deletes that
  proof instead of narrowing it has removed a guard, not updated one.
- **Derived P1 already filed: Linux has no secret store.** `ISecretStore` is DPAPI-proven on Windows and
  returns a typed `Unavailable` on Linux. A Linux secret mechanism (libsecret / Secret Service, researched
  **current**) or an explicit typed refusal — **never a silent plaintext fallback**. Unverifiable here
  (zero WSL distros), so it is a desktop-session row.

## Claimable work (the board is authority, this is a pointer)

**Now unblocked by the owner decision** — but note all three need network + credentials, and the Linux
secret-store row is a real dependency for anything that authenticates:
- **JUST DROP** (M) — the cheapest of the three: upstream itself treats it as a hosted web view, and the
  port has shipped that shape twice (DTRH, Graded Intake), so only order crediting is new.
- **THE DESCENT** (M+) — reads the server's `descent` block; authenticates; carries the Linux dependency.
- **Remote media** (M) — `VideoService` remote clips + `App.OfferRemoteMediaSource`. The offer prompt is a
  separable smaller slice; the video half waits on the still-BLOCKED unified-video row.

**Parity defects in landed code (highest value per unit of work, all headless):**
- **P1 — companion reply hygiene misses the TRANSCRIPT shape.** Upstream fixed it; the port is the old
  anchored-wrapper code, so a multi-speaker reply reaches the bubble half-cleaned and another companion's
  lines render as the current one's. Live user-visible path.
- **P1 — persistence-contract drift:** `SettingsService.MergeBuiltInPresetInto` (upstream merges bundled
  presets into the user's stored document on load — a concept the landed contract lacks) and
  `App.EnsureInstallDateRecorded` (new persisted startup field).
- **P1 — a RATIFIED row's ground truth moved** (quick-toggle). Notice filed; do not silently re-open it.

**Filed at this land:** the **wedged-construction pool-thread residue** row (sibling of SP-071's give-up
residue — do NOT merge them; one counts backgrounded constructions, the other backgrounded teardowns).

**Older, still open:** five disk-store waits; the give-up residue row; the runtime-vacuity CLASS row (wave-29
consult named it overdue at three occurrences); the endpoint-watcher row (Windows-only, headed);
row `:53`'s `§C`/`§D` remainder; T-18; T-19 (half-implemented — the inventory data exists, the
regenerate-and-diff check does not); the tier-1 citation review (9 files); the window-behavior-manifest
re-verification (74 changed); `ProcessEnvCollection`; `CapabilityRegistry` probe; `allowedSkips` bans-are-text;
T-17's auditor run; the named privacy flake.

## Standing land discipline (unchanged, learned the hard way)

- **Write this file in the AUTHORING commit, before `spine batch start`.**
- **Never trust the gate's own evidence (T-3).** Verify the merged state yourself in a scratch worktree.
  `diff-stat.txt` is a TWO-dot diff — disprove it with three dots.
- **Verify BEFORE `spine integrate`**, not after.
- **Tree identity:** fast-forward → `git diff <verified> HEAD` EMPTY. Merge → the SCOPED form
  `git diff <verified> HEAD -- client/ scripts/ ConditioningControlPanel/ docs/`, naming non-code deltas.
  (Wave 29 was a real merge and still came back EMPTY on both forms.)
- **Full contract, in this order:** `dotnet build client/CcpClient.sln -c Debug --nologo && node client/tests/floor/check-floor.mjs`.
- **The land's LAST action verifies the tree actually being pushed.** Commit the reconciliation FIRST, then
  run the contract, then push.
- **A reviewer's "non-blocking" note is a board row, never a post-verification edit.**
- **Bite matrix, one source at a time**, and check each pin's fixture reaches its mechanism.
- **Prefer asserting from INSIDE the operation over ordering-by-index** (SP-072 beat SP-071's `IndexOf`
  shape by reading the dispose count while the teardown still held the lock).
- **Never set `CCP_DATA_ROOT` for a floor run.** **`allowedSkips` pins 5 names; 2 skip on Windows — the
  asymmetry is CORRECT.**
- **`node .spine/patches/verify.mjs` FAILS in a scratch worktree and that is expected.** Run it in the MAIN
  checkout — done this phase, exit 0.
- **`cmd | tail; echo $?` reports TAIL's exit code.** Use `${PIPESTATUS[0]}` or redirect to a file.
- **Never put backticks inside a double-quoted shell string** — bash command-substitutes them and the words
  vanish from the committed doc (cost a repair commit this phase). Use the edit tool or a quoted heredoc.
- **`ctx_batch_execute` runs its commands through PowerShell, not bash** — `export`, `2>/dev/null` and
  `$HOME` break. Use the plain Bash tool for spine and git.
- **A doc a test READS is code.** `port-audit-prompt.md`, `floor.json`, `vacuous-shape-ledger.json` and
  **`upstream-payload-inventory.json`** are read. `task-board.md`, `port-lessons.md`, `port-digest.md`,
  `upstream-sync.md`, `upstream-citation-inventory.json`, `async-lifecycle-fault-contract.md` and
  `client/memories/**` are not.
- **Landed rows stay WIP/OPEN until the owner ratifies.**
- **Budget the board-row update INTO the land.**
- **Packet template, proven this wave:** tell the worker to create `.DONE` last and **not commit it**.

## Upstream baseline

**WPF `main` is merged to v6.8.0** (`db3e842f`, 125 commits, zero conflicts; ledger
`client/docs/upstream-sync.md` §2026-08-14). The completeness audit that followed found the first pass had
under-filed badly — 297 cited WPF files, 106 changed, not the "12" first reported — and produced
`client/docs/upstream-citation-inventory.json` (keyed by real path, with tier and verdict per entry,
regenerated and diffed each sync). **Nine tier-1 files still have no verdict**; they are owed to a named row.

## Instrument notes

- **Consult truncation is board row T-18.** Solo verdicts surfaced complete on the first call under a
  250-300 word cap again this phase. Use `mode: "solo"` explicitly (T-7).
- **Advisor calls this phase were load-bearing and one was conditional — read the condition, not the
  headline.** The sync-timing consult said WAIT; its own stated gate was "measure whether the guard is
  data-driven", and the measurement resolved the other way, so proceeding followed the advice rather than
  overriding it. Its second point (never hold a merge unpushed, because `port.txt`'s bootstrap tells a
  fresh session to `git reset --hard origin`) was adopted unconditionally.
- **hermes memory is FULL.** The durable record is `client/memories/port-status.md`, current through wave 29.

## Machine facts (laptop)

pi-spine 2.10.0 · durable memory fallback `client/memories/port-status.md` · **WSL zero distros → every
Linux gate is a standing named limit, never faked** · no real audio device, endpoint death or wedged native
construction can be induced here · **MCP 0/3 connected** (cached metadata only; named limit, never a
blocker) · `Z:\CCP Vids`, DISPLAY3 and the WSL2 Linux gate are **DESKTOP-only** · 9 project + 5 engine
patches verified applied at this land (`verify.mjs` exit 0).
