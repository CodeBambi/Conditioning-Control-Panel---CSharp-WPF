# SP-005 — persistence and migration contract: evidence record

**Task:** `client/docs/task-board.md` row 4 · **Batch:** spine-20260719T010403 lane-1 · **Review Level:** 2

---

## Pre-approach consult (solo Fable 5, 2026-07-19)

Full outline submitted (schema authority, STJ tolerance stack, atomic write, serialized writer via OperationRegistry, quarantine, journal, replacement notification, secret seam, import/backup, teardown flush + panic policy, debounce policy-only, Core deferral). Verdict received (reply truncated mid-sentence at the mid-rename recovery analysis — received portion below, applied in full):

**(a) Flush-at-head-of-teardown vs pending write race:**
- Flush must await **quiescence** (queue empty + last write done), not just its own enqueued entry.
- The writer must serialize the **latest Current at dequeue time**, not a snapshot captured at enqueue time — then the flush write captures everything mutated up to that moment.
- Saves enqueued after teardown begins are not guaranteed — state this explicitly in the contract.
- Do **not** pass the generation token into the file I/O itself (a cancel-after-flush-started must not abort the rename).
- FlushAsync must be idempotent and a **no-op if the store never loaded or is not dirty** (startup-failure path with a quarantined load + defaults in memory must not write).
- **Defaults must never be auto-saved just because they exist** — the dirty flag is set only by explicit mutation, never by loading with defaults (WPF's exact bug class: fresh defaults overwriting quarantine state / masking a corrupt file).
- Deadlock check: write op runs on the thread pool with `ConfigureAwait(false)`, no UI dependency — flush awaited inside `ShutdownAsync` (called via `GetAwaiter().GetResult()` from the Exit handler) cannot deadlock.

**(b) Panic-path flush:** ATTEMPT on every path is defensible (WPF did; user-data-loss evidence outweighs), **but** the flush needs a **bounded wait** (mirror the 5s CancelAndDrainAsync backstop) so a wedged writer can never hang shutdown; outcome logged. Serialization failure becomes a typed failure that skips the write; atomic replacement prevents partial corruption; residual risk of a poisoned-but-serializable state is low for settings.

**(c) Tolerance stack / journal at feature scale:**
- `[JsonExtensionData]` preserves unknown members **only at declared levels** — the contract must require an extension-data member on **every persisted model type** (or scope the guarantee explicitly).
- Journal vs SchemaVersion dual authority: **SchemaVersion is the authority for which migrations run**; the journal is the audit/idempotence record. Conflict-resolution rule: run any migration whose ID is **not journaled AND whose target version exceeds the document version** (idempotent re-run is safe by construction).
- **Downgrade case (file version newer than the app knows) is missing from the outline and must be added:** typed `Degraded`, in-memory flagged defaults, **writes disabled** so an older build never clobbers a newer file (WPF stale-version lesson `f403261d`/`eeef31e2`).

**(d) Scope creep:** outline is mostly disciplined. `ISecretStore` stays a named seam (minimal interface, no implementation). Import/backup boundary stays contract-only. The store **should** be a background participant (load in `StartAsync` at phase 3, registered before other participants so settings are loaded before consumers start). Writes: **chain each save onto the previous write's completion under a lock** rather than a channel/queue-consumer loop — simpler; each write is its own owned operation with a typed outcome.

**Truncation note:** the reply cut off during the mid-rename recovery analysis (partial-temp adoption). Applied disposition, consistent with WPF and the received reasoning: temp-exists-without-main → adopt temp as main (a partial temp fails parse → quarantine, never silent); temp-alongside-valid-main → delete temp.

## WPF persistence evidence digest (outcomes only, read-only archaeology)

Sources: `ConditioningControlPanel/CCP.Core/Services/Settings/SettingsService.cs:81-190` (load/recovery/quarantine/migrations), `:374-444` (save/backup/replace), `ConditioningControlPanel/App.xaml.cs:3206-3209` (teardown flush-first). Focused-history leads from `first-attempt-systemic-lessons.md`: `b694b543`, `a2d1b9a8`, `e9501ce8`, `03d91c86`, `750d2615`, `f403261d`/`eeef31e2`.

- **Atomic replacement (VERIFIED):** save writes the full document to `settings.json.tmp` then `File.Move(temp, path, overwrite: true)` (`:397-401`). Outcome: readers never see a partial file.
- **Crash recovery (VERIFIED):** load adopts `settings.json.tmp` when it exists and the main file does not ("Recovering settings from interrupted save"); a stale temp alongside a valid main is deleted (`:86-93`).
- **Per-member tolerance (VERIFIED, superseded by design):** Newtonsoft `Error` handler skips unparseable members and keeps the rest — "a renamed enum must NOT discard the whole document and reset every phrase/pool" (`:103-122`). Outcome retained; mechanism replaced by DOM-level migration + `[JsonExtensionData]` round-trip (Newtonsoft is a new package admission — rejected).
- **Corruption quarantine (VERIFIED):** an unparseable file is **moved** to `settings.corrupt-<timestamp>.json` — preserved, never deleted, never overwritten by defaults; `WasSettingsFileCorrupt`/`LastCorruptBackupPath`/`WasSettingsFileMissing` flags let the UI/import surface it (`:158-190`). Outcome: silent defaults over unreadable user data are forbidden.
- **Migrations on load (VERIFIED):** named migrations run at load (`MigrateAuthToken`, `MigrateKeywordTriggerActions`, `MergeBuiltInAwarenessPresets`, `MigrateFromContentModeToMod`, `MigrateLoudnessThreshold`, `:135-139`). History `e9501ce8` restored migrations + exit flush after they were lost.
- **Whole-object replacement + notification (VERIFIED):** `RestoreFrom`/`Reset` replace `Current` and fire `CurrentReplaced` **before** persisting, "so any backups they seed get written too" (`:436-450`).
- **Debounced coalescing (VERIFIED, policy-only here):** 500 ms trailing debounce coalesces saves; `SaveImmediate` cancels the pending debounce and writes now (`:372-390`). Retained as policy; the timer itself is not implemented in this slice (no feature-scale churn consumer).
- **Local-first write ordering (VERIFIED):** cloud backup is chained strictly after the local atomic save (`:405-428`; race fix `03d91c86`). Outcome retained as the import/backup boundary: backup consumes the persisted document, never precedes it.
- **Teardown flush FIRST (VERIFIED):** `App.xaml.cs:3207-3209` — `Settings?.SaveImmediate()` runs **first** in shutdown, before cloud sync (bounded 2 s) and before any service disposal: "flush any pending debounced writes and ensure final state is on disk."
- **Version-state staleness (VERIFIED anti-pattern):** `f403261d`/`eeef31e2` — stale version state caused an update popup every launch. Consequence here: version/journal state must be written through atomically with the document, and a newer-than-app document must never be clobbered by an older build.

## Engine-review presence/absence

Review Level 2. Engine reviews are empirically dead (board row T-2: zero reviews in SP-001…SP-004; `spine_review_step` returns skipped). Per-step calls:

- Step 1 plan review: **skipped** (`skipped=true, reviewLevel=0`) — 5th consecutive batch with no engine review. Mandatory Fable consults carry the quality gate.
- Step 2 plan review: **skipped** (`skipped=true, reviewLevel=0`) — same T-2 disposition.
- Step 3 plan review: **skipped** (`skipped=true, reviewLevel=0`) — same T-2 disposition.

<!-- filled in during Steps 2-5 -->
## Implementation notes, test output, surprises

### Serializer decision
System.Text.Json (in-box) with the explicit tolerance stack (JsonNode DOM-level version/migrations → typed bind with `[JsonExtensionData]` round-trip → bind failure quarantine + Degraded). Newtonsoft rejected as a new package admission; its per-member salvage compensated for schema churn the DOM-level migration journal + extension-data round-trip replace. Revisit trigger recorded in contract §2.

### Core-deferral decision
Store landed in `client/src/CcpClient.Desktop/Persistence/`; no `CcpClient.Core` created (proposal §1 pre-decision; A-014 YAGNI). Revisit trigger: first second-assembly consumer (contract §13).

### Panic-path flush policy
ATTEMPT on every teardown path including panic (contract §11 rule 4): the single guarded entry point is the only teardown; flush is bounded (5 s backstop) and never throws; serialization failure becomes a typed failure that skips the write; atomic replacement prevents partial corruption. Deliberate no-flush rejected on user-data-preservation grounds (WPF flushed on all paths; first attempt lost exit flush once, `e9501ce8`).

### Implementation shape (as built)
- `PersistenceStore<TModel>` — SP-003 participant, registered first; load in `StartAsync` (phase 3). Typed `LoadOutcome` (Loaded/Missing/Quarantined/NewerSchema); `IsDegraded` surface.
- Writes: chained serialization — each `Save` chains onto the previous write's completion under a lock (per pre-approach consult; no channel/queue-consumer loop). Write body serializes the LATEST `Current` at execution time; generation token observed before I/O, never passed into it. `SaveImmediate` = enqueue + await quiescence. `FlushAsync(boundedWait)` = no-op unless Running+dirty+writes-enabled; awaits quiescence with bounded wait, logs outcomes, never throws.
- Dirty discipline: only `Mutate`/`Replace` (and migration write-through) set dirty; loading defaults never does — `Defaults_NeverAutoSaved_LoadingDefaultsLeavesNoFile` and `Shutdown_CleanStore_WritesNothing` prove it.
- Teardown wiring: `ApplicationHost` gained an optional `preDrainFlush` delegate invoked at SP-003's reserved head slot (guarded, logged, never throws); `CompositionRoot.Build` wires `store.FlushAsync(DefaultFlushTimeout=5s)` when the participants list contains the store. `ParticipantInfrastructure` gained `ILogSink` so the default store registration logs.
- Activation edit to `startup-shutdown-contract.md` §6 rule 5: RESERVED → ACTIVATED (single-line edit citing this task), plus the matching doc-comment update in `ApplicationHost.ShutdownAsync`.

### Surprises
1. **`StreamWriter` dispose closed the `FileStream` before `Flush(flushToDisk: true)`** — every real write failed with `ObjectDisposedException` while the injected-hook tests passed, masking it until the scratch repro. Fixed by flushing to disk inside the writer scope. Lesson: the failure-injection hooks proved the typed-outcome path but not the real I/O path; the real path is only covered by tests that write actual files (all present).
2. **C# field initializers cannot reference instance members** — the `ParticipantsFactory` default moved into a constructor body. Cosmetic.
3. Nested `TempDir.Path(string)` helper shadows `System.IO.Path` inside the nested class — qualified names needed there.

### Test output — Windows (contract testCommand)
`dotnet build client/CcpClient.sln -c Debug --nologo` → **0 Warning(s), 0 Error(s)** (.NET SDK 10, net10.0).
`dotnet test client/tests/CcpClient.Tests/CcpClient.Tests.csproj -c Debug --nologo` → **Passed: 48, Failed: 0** (34 SP-003/SP-004 intact + 11 persistence + 3 teardown-flush).

### Test output — WSL2 Linux (in-packet gate)
Environment: WSL2 Ubuntu (distro `Ubuntu`), dotnet SDK **10.0.110**; `client/` copied to native dir `~/ccp-sp005` (SP-002 pattern — /mnt/e build avoided).
Build: **0 Warning(s), 0 Error(s)**. Tests: **Passed: 48, Failed: 0** — identical suite, so rename/flush/quarantine semantics are exercised on Linux file I/O, including atomic `File.Move(overwrite: true)`, `Flush(flushToDisk: true)`, temp adoption, and quarantine moves.

### Engine reviews
Steps 1–3 plan reviews: all `skipped` (see list above; Step 3 recorded below at commit time). Fable solo consults are the active quality gate per the packet.
