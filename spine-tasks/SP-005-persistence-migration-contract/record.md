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

Review Level 2. Engine reviews are empirically dead (board row T-2: zero reviews in SP-001…SP-004; `spine_review_step` returns skipped). To be recorded per step as called.

<!-- filled in during Steps 2-5 -->
## Implementation notes, test output, surprises

(pending)
