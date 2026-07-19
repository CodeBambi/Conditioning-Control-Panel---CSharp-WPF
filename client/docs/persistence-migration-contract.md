# Persistence and migration contract

**Date:** 2026-07-19 · **Task:** SP-005 (task-board row 4) · **Status:** ratified by implementation + tests in this slice; evidence in `spine-tasks/SP-005-persistence-migration-contract/record.md`

This contract instantiates `architecture-proposal.md` §6 (row-4 column: serializer choice, atomic write/debounce/backup/quarantine parity), the A-014 persistence rule ("user data has one schema/migration/atomic-write/recovery authority; unreadable data is preserved; secrets are excluded; concurrent writes are serialized; graceful-shutdown flush is explicit"), and the first-attempt persistence lesson (`first-attempt-systemic-lessons.md`: ACCEPT atomic replacement / corruption preservation / explicit migration / crash recovery; REJECT scattered saves, detached unordered writes, silent defaults over unreadable user data). It **activates the settings-flush ordering slot SP-003 reserved** at the head of the single guarded teardown (`startup-shutdown-contract.md` §6 rule 5). It implements no feature settings: one demonstrator settings model exercises the whole contract.

WPF evidence (outcomes only, no mechanics transplant): digest in `spine-tasks/SP-005-persistence-migration-contract/record.md` — atomic replacement, crash-mid-write recovery, corruption quarantine with preserved bytes, migrations on load, whole-object replacement with pre-persist notification, local-first write ordering, teardown flush first (`App.xaml.cs:3207-3209`).

---

## 1. One schema/version authority

1. One JSON settings document per file. The document carries an integer `schemaVersion`; the store is the single authority that reads and advances it. No second writer, no per-feature save path (REJECT scattered saves).
2. The version is read at **DOM level** (`JsonNode`) before any typed binding, so version checks and migrations tolerate shapes the current model does not know.
3. Version and migration-journal state are written through **atomically with the document** — there is no separate version file that can go stale (WPF anti-pattern `f403261d`/`eeef31e2`: stale version state surfaced every launch).
4. This slice ships exactly one **demonstrator settings model** (`DemoSettings`) and one demonstrator migration. Feature settings models arrive with their feature rows and must follow this contract.

## 2. Serializer decision: System.Text.Json with the explicit tolerance stack

**Decision: `System.Text.Json` (in-box). Newtonsoft.Json is rejected.**

The tolerance stack, in order:

1. Parse the document to `JsonNode` first. Version checks and migrations run at DOM level, tolerant of unknown shape.
2. Bind the migrated DOM to the typed model. **Every persisted model type must declare a `[JsonExtensionData]` member** so unknown members round-trip verbatim (§6). Extension data preserves unknown members only at declared levels — this is a tested contract rule for every future feature model, not a default.
3. Bind failure (structurally valid JSON that cannot become the model) → corruption quarantine + typed `Degraded` (§5), never silent defaults.

**Why Newtonsoft is rejected:** it is a new package admission (constitution: package admission requires necessity). Its distinctive capability in WPF was per-member salvage — an `Error`-handler that skipped an unparseable member and kept the rest (`SettingsService.cs:103-122`). That salvage compensated for schema churn (a renamed enum wiping every user phrase/pool) that this contract removes structurally: migrations run at DOM level before binding, and unknown members round-trip through extension data instead of being rebound. What remains of Newtonsoft beyond that is redundant with the in-box serializer.

**Revisit trigger:** a migration proves unexpressible at DOM level (e.g., a transform requiring full typed object graphs of two schema generations simultaneously). The admitting row must record the decision and reasons here.

## 3. Atomic write

1. Save writes the whole document to `<file>.tmp` **in the same directory**, flushes (`FileStream.Flush(flushToDisk: true)`), then `File.Move(temp, target, overwrite: true)`. Readers never observe a partial file.
2. The write path carries explicit **failure-injection points** (constructor-injected delegates for the temp-write and the rename) so tests simulate I/O failure and mid-rename crash without touching real corruption.
3. Directory fsync after rename is **not** guaranteed — .NET exposes no portable directory-sync API; the consequence (a power-loss window where the rename itself may be lost) is accepted and recorded, with the §5 recovery path as the mitigation. Revisit if a feature row requires power-loss durability beyond file-level flush.

## 4. Single serialized writer (SP-004 primitive)

1. The persistence store is an SP-003 **background participant** registered first in the composition root, so its load (§5) completes in phase 3 before any consumer participant starts. Construction starts nothing (SP-003 §4.4).
2. Every write is an **owned operation** on the store's `AsyncOperationOwner` (SP-004 §1): one owner, generation, owned completion task, typed terminal outcome. Detached or fire-and-forget writes do not exist.
3. Writes are **serialized by chaining**: each save chains onto the previous write's completion under a lock. There is no queue-consumer loop and no channel. Concurrent saves from any thread produce no interleaved or partial file.
4. The write body serializes the **latest `Current` at execution time**, not a snapshot captured at enqueue. A flush therefore captures every mutation visible when it runs.
5. `Save()` enqueues a write and returns the owned completion (callers may observe the typed outcome; fire-and-forget of a required save is banned per SP-004 §7). `SaveImmediate()` enqueues and **awaits quiescence** (the chained tail), so when it returns, every previously enqueued write has finished.
6. The generation token is **not** passed into the file I/O: a cancellation arriving after a write's I/O has begun must not abort the rename. The token is observed before the I/O starts.
7. Writes are **disabled** when the load ended in the newer-than-app state (§5 rule 6): an older build must never clobber a newer document. A disabled write terminates with a typed `Failed(Degraded, "writes disabled: document schema is newer than this build")` outcome.

## 5. Load, corruption quarantine, and recovery

`Load` runs once, in the store participant's `StartAsync` (phase 3). Its outcome is a typed value, never an exception-as-control-flow:

- `Loaded(model)` — document parsed, migrated (§7), bound.
- `Missing(model)` — no file exists; flagged fresh defaults. Not an error.
- `Quarantined(model, backupPath)` — the file existed but was unparseable at DOM level or failed typed binding. The **original bytes are moved** to `settings.corrupt-<timestamp>.json` — preserved, never deleted, never overwritten — and the store runs on **flagged defaults**. Surfaced as a typed `Degraded` state (`LastLoadOutcome`), inspectable by tests and by later UI rows. Silent defaults over unreadable user data are forbidden (REJECT, first-attempt lesson).
- `NewerSchema(model, fileVersion)` — the document's `schemaVersion` is newer than this build knows. In-memory flagged defaults; **writes disabled** (§4 rule 7); typed `Degraded`. The newer file is left untouched.

Rules:

1. **Crash recovery:** at load, if `<file>.tmp` exists and the main file does not, the temp is adopted as the main file before parsing (interrupted-save recovery). If the adopted temp turns out partial, it fails parse and is quarantined like any corrupt file — never silently defaulted over. A stale temp alongside a valid main file is deleted.
2. **Defaults are never auto-saved because they exist.** The dirty flag is set only by explicit mutation (`Mutate`/`Replace`), never by loading with defaults. A run that loads `Missing`/`Quarantined`/`NewerSchema` defaults and changes nothing writes nothing — the quarantine state is not masked on next launch.
3. Quarantine moves (not copies) so the next save writes a clean file, and records the backup path in the typed outcome for later import/UI surfacing.
4. Load failure kinds reuse the shared vocabulary: `Quarantined`/`NewerSchema` surface as `Degraded`; an unexpected I/O exception escaping load is trapped once at the participant-start boundary per SP-003/SP-004 (typed `Failed`, classified by the owner).

## 6. Unknown-member policy: preserve, never strip

1. Every persisted model type declares `[JsonExtensionData]`; unknown members round-trip verbatim through load → save. A newer build's members survive a round-trip through an older build's model at every declared level.
2. Stripping an unknown member is a contract violation. The round-trip is a tested rule.
3. Members whose *meaning* changed are a migration concern (§7), not a binding concern.

## 7. Migration journal and idempotence

1. The document carries a `migrationJournal`: an array of applied migration IDs, written through atomically with the document (§1 rule 3).
2. `schemaVersion` is the **authority for which migrations must run**; the journal is the audit and idempotence record. Resolution rule: a migration runs when its ID is **not journaled AND its target version exceeds the document's `schemaVersion`**. Idempotent re-run after journal loss is therefore safe by construction.
3. Every migration is **idempotent**: running it twice on the same document yields the same document and exactly one journal entry. Migrations mutate the DOM (`JsonNode`), not the typed model.
4. This slice ships exactly **one demonstrator v0→v1 migration** proving journal + idempotence. It is not a framework; feature migrations arrive with their feature rows.

## 8. Replacement notification

1. `Replace(newModel)` performs whole-object replacement of `Current` (import/restore shape), marks the store dirty, and raises `SettingsReplaced` **before** persisting — listeners re-bind to the new instance and anything they seed is captured by the subsequent save (WPF `RestoreFrom`/`CurrentReplaced` outcome, `SettingsService.cs:436-450`).
2. Delivery context, documented per SP-004 §5.4:

   | Stream | Producer context | Delivery context | Stale handling |
   |---|---|---|---|
   | `SettingsReplaced` → listeners | Caller of `Replace` (any thread; serialized under the store's mutation lock) | **Synchronously on the caller's context**, inside the store lock, before the save is enqueued | Listeners must be re-entrant-safe and must not touch UI directly; UI projection goes through `IUiDispatch.Post` with a generation check inside the delegate (SP-004 §5.5) |

   Handlers that throw are isolated per-handler (logged via `ILogSink`), so one listener cannot break replacement for the others; the event never faults the store.
3. UI consumers never subscribe to the model object directly across a replacement: they subscribe to `SettingsReplaced` and re-read `Current`.

## 9. Secret exclusion (seam only)

1. The settings file **never carries secrets** (tokens, credentials, keys). A persisted model type that needs a secret stores an opaque reference (name), never the value.
2. The named seam is `ISecretStore` (`Get`/`Set`/`Delete` by name) — **declared, not implemented**. The first row with a secret consumer (Patreon/auth, AI provider keys) implements it against a platform store and records the admission. No secrets flow through this slice's document, tests, or logs.

## 10. Import/backup boundary

1. **Import** flows through the same load/validate/quarantine path as startup load: the imported document is parsed at DOM level, migrated, bound, and on success installed via `Replace` (§8); on failure the import is rejected with a typed outcome and the current document is untouched. A blind copy-over never happens.
2. **Backup** is an explicit file copy of the current good document, taken only after a successful save (local-first ordering; WPF race fix `03d91c86`). Backup scheduling and cloud sync are later rows' consumers — this contract defines only the ordering boundary: backup consumes the persisted document, never precedes it.

## 11. Teardown flush (SP-003 reserved slot — ACTIVATED)

1. The store's flush is wired at the **reserved position at the head of `ApplicationHost.ShutdownAsync`**, before `Registry.CancelAndDrainAsync` cancels generations and before reverse-order participant stop (`startup-shutdown-contract.md` §6 rule 5 — the reservation this task activates; WPF outcome `App.xaml.cs:3207-3209`: settings flush FIRST).
2. Flush semantics: `FlushAsync(boundedWait)` is a no-op when the store never loaded or is not dirty (§5 rule 2); otherwise it enqueues a final write and awaits quiescence with a **bounded wait** (backstop only — the chained writer is the mechanism). A flush that exceeds the bound is logged and teardown continues; teardown never throws (SP-003 invariant).
3. Because flush runs before generation cancel, the final write is an ordinary live operation — no special-cased uncancellable path.
4. **Panic-path flush policy: ATTEMPT on every path, including panic.** The single guarded entry point is the only teardown, so close, startup-failure, and panic all reach the same flush. Serialization failure produces a typed failure that skips the write; atomic replacement prevents partial corruption; the bounded wait prevents a wedged writer from hanging shutdown. Deliberate no-flush on panic is rejected: user-data preservation outweighs the residual risk of persisting a semantically odd but serializable state (WPF flushed on all paths; the first attempt lost exit flush once already, `e9501ce8`).
5. Saves enqueued after teardown begins are **not guaranteed** — stated explicitly. `Save`/`SaveImmediate` called after `ShutdownAsync` has begun terminate with a typed `Cancelled` outcome (generation already cancelled at registration) rather than faulting or silently succeeding.

## 12. Debounce policy (stated, NOT implemented)

Rapid feature-scale mutations should be coalesced by a **trailing debounce**: mutations mark dirty and arm a short timer; the timer's expiry performs one serialized write of the latest state; `SaveImmediate`/flush cancels a pending debounce and writes now (WPF shape, 500 ms trailing). **This slice implements `Save`/`SaveImmediate` only** — the debounce timer arrives with the first feature row whose mutation rate needs it. Rationale: no feature-scale churn exists, debounce tests are timer-flaky, and A-014 forbids infrastructure without a consumer. The admitting row must add the timer inside the store (never per-feature) and test it behind a controllable clock.

## 13. Landing spot and Core deferral

The store lands in `client/src/CcpClient.Desktop/Persistence/` — **no `CcpClient.Core` assembly is created**. The architecture proposal pre-decided this (§1: "rows 2–4 have a landing spot before Core exists"; A-014 YAGNI constraint). **Revisit trigger: the first second-assembly consumer** (e.g., an Android head or a tool needing the store) — that row extracts the portable parts and records the decision.

---

## Conformance checklist (tested in this slice)

- Corrupt file → quarantine with preserved original bytes + typed `Degraded` + flagged defaults; never silent.
- Simulated crash mid-rename → temp adoption recovers on next load; stale temp beside valid main is deleted.
- Unknown member round-trips through load → save verbatim (preserve, never strip).
- Demonstrator migration is idempotent: run twice → same document, exactly one journal entry.
- Concurrent writes from multiple threads are serialized: no interleaved or partial file; final content is the latest state.
- `Replace` raises `SettingsReplaced` before persisting, on the documented caller context; a throwing handler is isolated.
- Dirty-at-shutdown is flushed before reverse-order participant stop executes; flush is a no-op when clean/never-loaded; repeated shutdown remains a no-op (SP-003 invariants intact; all SP-003/SP-004 tests pass).
- Load outcomes are typed: `Loaded` / `Missing` / `Quarantined` / `NewerSchema` (writes disabled); defaults are never auto-saved.
- Contract testCommand passes on Windows and WSL2 Linux (rename/flush semantics exercised on both).
