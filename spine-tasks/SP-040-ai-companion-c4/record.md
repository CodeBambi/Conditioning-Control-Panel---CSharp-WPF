# SP-040 record — AI companion slice c4: memory (IAiMemoryStore on SP-005 machinery)

**Task:** SP-040 · **Board row:** "Implement AI companion and awareness integration" (P0), slice c4 of `client/docs/ai-companion-admission.md` §8 · **Lane:** lane-1 · **Date:** 2026-08-04

Evidence class: **U** + file-content proof (document deleted on clear; blocked turn never persisted). No WH/WX/LAB claims. No Wayland claims. **WSL2 named limit (this laptop, per SP-038 probe 2026-08-04):** `wsl -l -q` → empty, exit 0 — zero distros; "U both platforms" discharges Windows-only with the Linux run owner-gated, never faked (same disposition as SP-035/SP-038).

---

## 1. WPF archaeology (READ-ONLY, `File.cs:line`)

**Stale-citation corrections (admission/packet cites vs current tree):** the packet cites `ClearHistory()` at `LocalAiService.cs:173-178`; the current tree has it at **`LocalAiService.cs:227-232`** (line drift, same AI_AUDIT-correction pattern). The UI clear flow lives at `MainWindow/MainWindow.Patreon.cs:912-953` (`BtnResetCompanionMemory_Click`, confirm default No) and `:1503-1504` (doc comment); the strategy wrapper is `AiServiceStrategy.cs:104-112` (`ClearLocalHistory` — constructs the local instance if needed only to reach the clear). The in-code comment at `LocalAiService.cs:223-225` ("not yet exposed in the UI") is stale — the admission already records this against `MainWindow.Patreon.cs:962,1539,1570` (line drifted to 912-953/1503).

### 1.1 Memory lifecycle (`Services/AIService/LocalAiService.cs`)

- **Pair cap:** `MaxPersistedPairs = 50` (`:92`) — "file stays small (<200KB typical)". FACT, never a decision (§9.2 #3 owner-pending).
- **Persist shape (`PersistHistory`, `:131-160`):** consent check FIRST (`ChatMemoryEnabled == false` → return, `:140`); filters to `IsDialogueTurn` (user/assistant, non-empty, not the enrichment `[CONTEXT BLOCK — NOT DIALOGUE]` preamble, `:166-170`); trims from the front to last 50 pairs (`:148-152`); writes a bare JSON array of `{Role, Content}` to `local_chat_history.json` in the user-data root (`:93-94`). No schemaVersion, no journal, no quarantine — WPF raw shape; greenfield uses SP-005 machinery instead (admission §4 rule 2, DECIDED).
- **Load (`LoadPersistedHistory`, `:104-129`):** same consent check first (`:113` — disabled = neither restored nor written); skips empty/unknown roles; best-effort catch-all → empty history (greenfield replaces with typed quarantine→Degraded).
- **Clear (`ClearHistory`, `:227-232`):** `_messages.Clear()` + `File.Delete(HistoryFilePath)` — in-memory emptied AND document deleted. The c4 explicit-clear operation ports exactly this outcome.
- **UI clear flow (`MainWindow.Patreon.cs:912-953`):** confirm dialog (default **No**, "can't be undone") → strategy `ClearLocalHistory()` → also clears the avatar chat-log bubble store → done/failure message. c4 lands the OPERATION only; the user-facing control is c7 (admission §8 mapping).
- **Stateless ambient path (`:476-502`):** automated/ambient reactions send a STATELESS `[system, (enrichment), userInput]` and are NEVER appended or persisted (comment: ambient turns used to become few-shot examples the model parroted). c4: awareness-class operations never persist (negative proof).
- **Moderation-gated persist precedent (`:546-586` region, current tree `:603-630`):** P2/H5 comment — persistence is DEFERRED until output moderation passes; previously a prohibited assistant turn (and its user turn) landed on disk and was reloaded next launch. On output block WPF rolls the turns back out of the in-memory list and never calls `PersistHistory` — "the file on disk remains at the prior known-clean state". Persist call site: `_ = Task.Run(PersistHistory)` after the output check passes, user chat only (`:644-646`).
- **Consent default FACT:** `ChatMemoryEnabled` default **true** (`CCP.Core/Models/CompanionPromptSettings.cs:120`) — "Default is on so existing users keep the behavior they had before this toggle existed." Recorded as baseline, never a decision (§9.2 #3).

## 2. Design (pre-approach; consult verdict in §3.1)

### 2.1 Files

| File | Contents |
|------|----------|
| `client/src/CcpClient.Desktop/Ai/AiMemoryStore.cs` (contract-named, fileScopeMustChange) | `AiMemoryDocument` (schema v1); `AiMemoryRetention` (mechanism record + `WpfBaselinePlaceholder`); `AiMemoryConsent` (typed consent state); `AiMemoryWriteAdmission` (typed write outcome); `AiMemoryStore : IAiMemoryStore` on ONE `PersistenceStore<AiMemoryDocument>` with its OWN named `AsyncOperationOwner` |
| `client/src/CcpClient.Desktop/Ai/AiOperationPipeline.cs` | WIRING ONLY: optional 6th ctor param `IAiMemoryStore? memory = null` (additive-optional per the lane-disjointness constraint — SP-041's lab harness files keep compiling UNCHANGED; the c3 required-ctor precedent adapted for lane disjointness); persist of the per-turn pair inside the owned operation body after the output boundary passes, interactive class only |
| `client/tests/CcpClient.Tests/AiMemoryStoreTests.cs` (NEW) | store round-trips, quarantine→Degraded, journal, unknown-member preserve, consent denied/admitted, pair-cap mechanism, both-answers schema shape, explicit-clear file-content proof |
| `client/tests/CcpClient.Tests/AiMemoryPipelineTests.cs` (NEW) | persist-after-output-pass file proof; blocked turn never persisted (zero file content); awareness never persisted; provider switch never clears; consent-denied persist no-op |
| `client/tests/CcpClient.Tests/AiOperationPipelineTests.cs` + `AiOfflineIntegrationTests.cs` | NO edits expected (optional param keeps call sites compiling); recorded honestly if touched |
| `client/tests/CcpClient.HeadlessTests/**` | none expected — no UI surface in c4; recorded if absent |

### 2.2 Document schema (admission §4 rule 3's both-answers-additively condition)

```csharp
public sealed class AiMemoryDocument
{
    public const int CurrentSchemaVersion = 1;
    public List<AiMemoryTurn> Turns { get; set; } = [];   // provider-neutral user/assistant pairs only
    public bool Disabled { get; set; }                    // disable FLAG — orthogonal to retention
    public int? RetentionMaxPairs { get; set; }           // retention POLICY field — null = NO policy recorded (consult §3.1 #1)
    public DateTimeOffset? DormantSinceUtc { get; set; }  // dormant marker REPRESENTABLE from v1 (null = active)
    [JsonExtensionData] public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}
```

Both owner answers implementable additively, no schema change: disable = retain-dormant → `Disabled=true` + `DormantSinceUtc` set + `Turns` retained; disable = delete → `Disabled=true` + `Turns` emptied. c4 mechanics do NOT consult `Disabled`/`RetentionMaxPairs`/`DormantSinceUtc` (placeholders proving the schema shape — a test round-trips them); enforcement runs on the ctor-injected `AiMemoryRetention` (single test-controllable source). NO value is decided (§9.2 #3). **Consult correction adopted:** `RetentionMaxPairs` is `int?` default **null**, never a persisted 50 — a persisted number would read as a decided policy value and would conflate "owner decided 50" with "default wrote 50"; the WPF baseline 50 lives ONLY in the injected `WpfBaselinePlaceholder` mechanism record, clearly labeled baseline-fact.

### 2.3 Store semantics

- ONE `PersistenceStore<AiMemoryDocument>` (`ai_memory.json` in the user-data root) with its OWN named owner `"AiMemory"` (the SP-024 lesson: N stores on one owner cancel each other's writes). Start/Stop/Flush delegate to the store (it is the SP-003 participant).
- Quarantine→Degraded, migration journal, unknown-member preserve: inherited from SP-005 machinery, surfaced via `LastLoadOutcome` passthrough (`IsDegraded`).
- **Consent (contract §5 rule 2; admission §4 rule 5):** typed `AiMemoryConsent { Granted, Denied }` injected as `Func<AiMemoryConsent>` (state may change at runtime). Checked at WRITE ADMISSION inside `Append`: denied → `LastWriteAdmission = ConsentDenied` and return — typed no-op, never silent, never throws. **Placeholder default = Denied** (conservative-posture precedent: c3 `AiModerationPolicy.Empty`, c6 none-admitted — stricter than the WPF baseline deliberately, recorded; owner-pending §9.2 #3). WPF baseline FACT: default true (`CompanionPromptSettings.cs:120`). Reads are NOT gated in c4 — WPF's load-skip-under-disabled (`:113`) belongs to the dormant-semantics owner question; recorded, never silently extended (the SP-038 escalation-scope lesson).
- **Pair-cap mechanism (value owner-pending):** ctor-injected `AiMemoryRetention(MaxPairs)`; `AiMemoryRetention.WpfBaselinePlaceholder = new(50)` recorded as baseline FACT (c3 `AiEscalationThresholds.WpfBaselinePlaceholder` precedent). Append trims from the front to MaxPairs*2 turns (WPF `:148-152` outcome).
- **Explicit clear (contract §5 rule 1; WPF `:227-232`):** `Clear()` empties in-memory state AND deletes the persisted document: under the store's OWN gate (serializing Clear against Append — consult §3.1 #3: a racing Append mid-Clear must not recreate the file with only its turn) `Mutate(turns.Clear)` → `SaveImmediate()` (the chained write snapshots the EMPTY state, clears dirty — so teardown flush cannot resurrect) → `File.Delete(document)` + `File.Delete(stray .tmp)` (an orphaned temp would otherwise be adopted by the next load — resurrection path closed). Sync-block is deadlock-safe: SP-004 `RunAsync` bodies run on `Task.Run` with `ConfigureAwait(false)` (OperationRegistry.cs:216-221), no captured context. **WritesDisabled (newer-schema) case (consult §3.1 #3):** Clear does NOT delete a newer-version document — an older build never clobbers a newer document (SP-005 contract §4 rule 7 discipline); in-memory state empties and the outcome surfaces typed (`ClearOutcome.Degraded`). **Quarantined backups are NEVER deleted by clear** — `.corrupt-*.json` artifacts are SP-005 contract-preserved (§5 rule 3); recorded explicitly so "clear deletes the persisted document" is never misread as touching preservation artifacts. A subsequent Append re-persists (WPF: clear is point-in-time, memory re-fills). A fresh load after clear → `Missing` → empty (never a resurrected document).
- **`IAiMemoryStore` interface UNCHANGED** (SP-016 declaration stands): `ReadRecent`/`Append`/`Clear`. The typed write admission surfaces as the concrete store's `LastWriteAdmission` property — observable, content-free (a state token, never content).

### 2.4 Pipeline persist position (admission §4 rule 5; discharges c3 inventory row 6's Reserved→Wired seam)

Inside the owned operation body (the c3-validated position): after the `IsLive` stale check AND after `EvaluateOutput` passes, before `reply = produced` — interactive class only, `AiReply.Generated` only (model-produced pairs; app-authored Fallback text is c3's recorded non-claim row 12):

- **Blocked output** → the body returns at the boundary's Block branch BEFORE the append — the pair never enters the store. Greenfield STRENGTHENS WPF's append-then-roll-back into append-never (outcome identical: "the file on disk remains at the prior known-clean state", `:624-630`); the typed `Refused` outcome is c3's. Recorded as a mechanism simplification, not a behavior change.
- **Blocked input / unproven provider / cooldown** → the operation never reaches the body — nothing persists.
- **Stale/cancelled** → discarded before the boundary (c3 rule: discarded replies have no side effects) — nothing persists.
- **Awareness class** → no append on that path at all (WPF stateless ambient, `:476-502`) — negative proof.
- **Provider switch** → `SelectProvider` never touches memory (contract §5 rule 3) — test.
- Persist = two `Append` calls (user turn from `request.Prompt`, assistant turn from the passed `Generated.Text`). The save is enqueued (not awaited) inside the operation — WPF's `_ = Task.Run(PersistHistory)` latency discipline (`:644`); file-content proofs await store quiescence test-side.
- No new diagnostic record, no new log site: the persist admission is observable on the store (`LastWriteAdmission`), content-free by construction (§12 rule 3 — memory content never enters diagnostics).

### 2.5 Rejected alternative on record (packet honesty framing (i))

A dedicated memory-persistence seam duplicates SP-005's migration/quarantine/schema machinery for zero gain (admission §4 rule 2's recorded consult). Re-verified against the tree: `PersistenceStore<T>` is generic and already carries every required behavior; c4 adds ZERO persistence machinery. The one gap — file DELETE on clear — is satisfied by the existing API composition in §2.3 (write-empty-then-delete through the chained writer), no store edit needed (Persistence/ is out of File Scope).

## 3. Consults

### 3.1 Pre-approach (Step 1)

**Mode:** solo (council forbidden — T-7). Route per the 2026-08-04 rewire: Opus 5 main / Fable 5 fallback. **Actual answering model:** the consult tool output carried NO model identifier and the stream truncated after question 3's verdict (same truncation behavior as SP-033/035/038) — recorded honestly per T-2. The received points were complete and actionable; questions 4–6 (optional-ctor shape, consent default, pair-cap subtlety) were not visibly ruled on — the optional-ctor shape is packet-mandated (lane-disjointness amendment), the consent default follows the packet's own conservative-posture instruction (honesty framing (d): "decided with the consult and recorded" — recorded here as Denied per the c3-Empty/c6-none-admitted precedent), and the pair-cap mechanism mirrors WPF's exact trim (§2.3).

**Verdict (substantive points) + dispositions:**
1. **Schema sufficient for both answers WITH ONE REQUIRED CHANGE (adopted):** `RetentionMaxPairs` must be `int?` default **null** — a persisted 50 would read as a decided policy value and conflate "owner decided" with "default wrote". Null on disk = explicitly "no policy recorded"; the injected placeholder does enforcement. Also adopted: `DormantSinceUtc` as typed `DateTimeOffset?` (STJ ISO-8601; malformed → SP-005 quarantine, the honest failure). Per-turn timestamps NOT needed — the §9.2 #3 retention question is the pair cap (WPF baseline 50), and per-turn fields could be added additively later if an owner answer needs them.
2. **Append-never is an honest strengthening WITH ONE REQUIRED NON-CLAIM (adopted):** end state identical to WPF's append-then-rollback (neither turn survives an output block — WPF removes BOTH the assistant and the producing user turn, `:624-630`). But WPF's `_messages` is BOTH the persistence buffer AND the live prompt context; greenfield's store is persistence-only and **nothing consumes memory as conversation context yet** — prompt assembly lands in c7. c4's claim is exactly "memory persists and clears; consumption as prompt context is c7" — recorded here and in §8 completion dispositions, never as "memory works".
3. **Clear() race-free WITH THREE REQUIRED HARDENINGS (all adopted, §2.3):** (a) serialize Clear against Append under the store's own gate — a racing Append mid-Clear would otherwise recreate the file containing only its turn; (b) delete the stray `.tmp` too — an orphaned temp is adopted by the next load (SP-005 crash recovery), a resurrection path; (c) WritesDisabled (newer-schema) → Clear empties in-memory but does NOT delete the newer document (an older build never clobbers a newer one — SP-005 §4 rule 7); typed Degraded clear outcome. Quarantined `.corrupt-*.json` backups are contract-preserved and never deleted by clear — recorded explicitly (§2.3).

### 3.2 Pre-completion (Step 4)

**Mode:** solo (council forbidden — T-7). **Actual answering model:** the consult tool output carried NO model identifier on either call (T-2 honesty note, same as SP-033/035/038). The FIRST call's stream truncated mid fix-first item A (same truncation behavior as §3.1); a SECOND targeted call re-derived items B/C and the record-only items from the tree — both calls recorded honestly.

**Verdict: three fix-first items (ALL adopted before .DONE), four record-only.**

Fix-first (adopted):
- **A. The blocked-turn proof was absence-of-file on a virgin store, not the P2/H5 claim.** ADOPTED: `OutputBlockedTurn_TypedRefusal_RolledBack_PriorKnownCleanStateSurvives` pre-seeds a prior known-clean pair ON DISK, runs the output-blocked turn, and asserts the file is BYTE-IDENTICAL to the prior clean state ("the file on disk remains at the prior known-clean state", LocalAiService.cs:624-630). The virgin-store zero-file test is retained as a complementary proof.
- **B. `Clear()` could report `Cleared` with the document still on disk** (TryDelete swallows IOException/UnauthorizedAccess; the outcome was set unconditionally). ADOPTED: `AiMemoryClearOutcome.Failed` added; the outcome is set from reality (`File.Exists` after the deletes) — a privacy operation must not lie.
- **C. `Append` discarded the write completion — a faulted persist was silent** (Admitted ≠ persisted). ADOPTED: `LastWriteCompletion` exposes the admitted write's owned completion as a typed outcome; the round-trip test asserts Completed on it.

Record-only (folded into §7/§9, no code change):
1. **Placement is intent, not a shipped fact** — no product composition constructs `AiMemoryStore` yet; "in the user-data root" discharges when the composition/c7 slice wires it (§7 item 9 names this).
2. **The cap trims TURNS, not pairs** — `RemoveRange` can leave a leading orphan assistant turn; identical to WPF's front-trim (`:148-152`), so parity — recorded so "pair cap" is never read as "pairs always intact".
3. **Consent is evaluated per `Append`** — revocation between the user and assistant append persists a half pair; harmless today (no context consumption until c7); named for c7's consent surface: revoke BETWEEN operations, not mid-operation.
4. **`Clear()` blocks on the write chain** — deadlock-safe (Task.Run bodies) but c7's UI must hop off the UI thread; recorded now rather than discovered headed. Quarantine-backup survival (§2.3) is a privacy-visible consequence — owner-ledger candidate (§9.2 #3 family).

## 4. Engine review presence (T-2)

| Call | Result |
|------|--------|
| Step 1 plan review (`spine_review_step --step 1 --type plan`) | **Engine review ABSENT (expected)** — nested reviewer spawn blocked inside pi worker session; `skipped: true`, `spawnFailed: false` (SP-195: engine runs reviews after `.DONE`). Artifact: `.reviews/1-20260804T165440.md` |
| Step 2 plan review (`spine_review_step --step 2 --type plan`) | **Engine review ABSENT (expected)** — same SP-195 skip; `spawnFailed: false`. Artifact: `.reviews/2-20260804T170307.md` |
| Step 3 plan review (`spine_review_step --step 3 --type plan`) | **Engine review ABSENT (expected)** — same SP-195 skip; `spawnFailed: false`. Artifact: `.reviews/3-20260804T170837.md` |

## 5. Redaction/log-site registry

c4 adds ZERO new product log sites (design §2.4): `AiMemoryStore` emits no log lines (the underlying SP-005 store's fixed-string logs — incl. the quarantine path notice — are existing SP-005-admitted behavior, unchanged); the pipeline persist path emits no new diagnostic record (persist admission observable on the store as a content-free state token). Memory content NEVER enters diagnostics (contract §12 rule 3) and is NEVER a secret (contract §10 — no ISecretStore involvement).

## 6. Step 5 — contract verification transcript

Exact contract chain (Windows lane worktree, 2026-08-04):
1. `node .spine/patches/verify.mjs` → **exit 0** ("all patches applied on all roots" — 7 project + 5 engine, no lane drift this time).
2. `dotnet build client/CcpClient.sln -c Debug -t:Rebuild --nologo` → **Build succeeded. 0 Warning(s) 0 Error(s)** (warnings measured on `-t:Rebuild` per the packet clause).
3. `dotnet test client/tests/CcpClient.Tests -c Debug` → **537/537** (516 restored floor + 21 new: 13 `AiMemoryStoreTests` + 8 `AiMemoryPipelineTests` — incl. the consult-strengthened prior-known-clean rollback proof).
4. `dotnet test client/tests/CcpClient.HeadlessTests -c Debug` → **29/29** (floor 29 — no new headless tests; c4 has no UI surface, honestly absent).
5. `git diff --check` → clean. File-scope audit `git diff --stat e2c830b3..HEAD -- ':!spine-tasks'` → ONLY `client/src/CcpClient.Desktop/Ai/**` (3 files: AiMemoryStore.cs NEW, AiOperationPipeline.cs wiring, IAiMemoryStore.cs additive converter) + `client/tests/CcpClient.Tests/AiMemory*.cs` (2 NEW files); `fileScopeMustChange` `AiMemoryStore.cs` present; NO `fileScopeMustNotChange` path touched (SP-041's three lab files untouched — additive-optional ctor verified by full-suite compile).

Linux contract run: covered by the WSL zero-distro named limit (§ header) — owner-gated, never faked (same disposition as SP-035/SP-038).

## 7. Deviations and per-change justifications

1. **Pipeline ctor gains OPTIONAL 6th param `IAiMemoryStore? memory = null`** (additive-optional). Justification: the packet's lane-disjointness amendment — SP-041 owns the three lab harness files in the same wave, so existing call sites must compile WITHOUT edits; the c3 required-ctor precedent is adapted for lane disjointness exactly as the amendment prescribes. Verified: `AiProviderLab.cs`, `AiProviderLabIntegrationTests.cs`, `LoopbackOllamaProviderTests.cs` untouched (fileScopeMustNotChange honored); `AiOperationPipelineTests.cs` and `AiOfflineIntegrationTests.cs` needed NO edits either (optional param) — recorded honestly against the packet's "ONLY if the pipeline signature changes" allowance: signature widened additively, no call-site churn.
2. **`IAiMemoryStore` interface unchanged; additive members only in its file** — `[JsonConverter(typeof(JsonStringEnumConverter))]` on `AiMemoryRole` so the persisted document carries readable role names (the WPF persisted shape, PersistedTurn Role strings) instead of opaque numbers in a user-data document. The SP-016 declaration (ReadRecent/Append/Clear) stands verbatim.
3. **Append-never instead of WPF's append-then-rollback** (mechanism strengthening, outcome identical — consult §3.1 #2): the persist point sits after the output boundary inside the live operation, so a blocked turn never enters the store; WPF's P2/H5 end state ("the file on disk remains at the prior known-clean state", LocalAiService.cs:624-630) is preserved by construction.
4. **Consent placeholder default = Denied** — deliberately stricter than the WPF baseline FACT (ChatMemoryEnabled default true, CompanionPromptSettings.cs:120), per the conservative-posture precedent (c3 Empty policy, c6 none-admitted) and the packet's honesty framing (d). Owner-pending (§9.2 #3); recorded, never silent.
5. **Reads NOT consent-gated in c4** — WPF's load-skip-under-disabled (LocalAiService.cs:113) belongs to the dormant-semantics owner question; recorded, never silently extended (the SP-038 escalation-scope lesson).
6. **RetentionMaxPairs null on disk** (consult §3.1 #1) — a persisted 50 would read as a decided policy value; enforcement runs on the injected `AiMemoryRetention.WpfBaselinePlaceholder` (WPF baseline FACT 50, LocalAiService.cs:92), never on a document default.
7. **Clear keeps a newer-schema document** (typed Degraded clear outcome) — an older build never clobbers a newer document (SP-005 contract §4 rule 7); quarantined `.corrupt-*.json` backups are contract-preserved and never deleted by clear (consult §3.1 #3).
8. **HeadlessTests untouched** — c4 is mechanism + file-content proofs with no UI surface (the user-facing clear control is c7); honestly absent per the packet's "likely none" allowance.
9. **No product composition wiring** — verified by grep: no product CompositionRoot constructs the pipeline yet (composition lands in a later slice, c3 record §7 item 1); the memory store's participant wiring lands with its first product consumer.

## 8. Completion-criteria disposition

| Criterion | Disposition |
|-----------|-------------|
| First IAiMemoryStore on SP-005 machinery: own named owner, schemaVersion + journal, quarantine→Degraded, unknown-member preserve | **MET** — `AiMemoryStore` on ONE `PersistenceStore<AiMemoryDocument>` with owner "AiMemory"; proofs in `AiMemoryStoreTests` (round-trip, quarantine bytes-preserved, schemaVersion+journal on disk, unknown-member round-trip) |
| Consent-gated writes code-enforced at admission (placeholder default recorded; WPF baseline fact cited) | **MET** — `Append` checks the injected typed `AiMemoryConsent` at write admission; denied = typed `ConsentDenied` no-op (never silent, never throws); placeholder default Denied recorded (§7 item 4); WPF baseline true cited (`CompanionPromptSettings.cs:120`) |
| Moderation-gated persist with rollback: blocked turn never persisted (file-content proof); awareness never persisted; provider switch never implicitly clears | **MET** — `AiMemoryPipelineTests`: output-blocked turn → typed Refused + ZERO file content; input-blocked → nothing; awareness Generated → negative proof (file never created); switch → memory survives |
| Explicit-clear operation: in-memory emptied + document deleted (file-content proof) | **MET** — `ExplicitClear_EmptiesStateAndDeletesDocument_FileContentProof`: bytes gone, .tmp gone, teardown flush cannot resurrect, fresh load → Missing → empty |
| Retention/disable schema shaped for both owner answers additively (no values decided) | **MET** — `BothAnswersSchemaShape_..._RoundTripWithoutSchemaChange`; RetentionMaxPairs null on disk; consult §3.1 #1 |
| Contract green (≥516/29 floor); both solo consults persisted with actual answering models | **MET (Windows; Linux = named WSL limit)** — 536/536 + 29/29 (§6); both consults recorded with the honest no-model-identifier note (T-2, same as SP-033/035/038) |

**c3 inventory row 6 discharged:** the Reserved→Wired seam (model-produced text PERSISTED) is consumed — `EvaluateOutput` gates the persist path (§2.4; packet honesty framing (e)).

**Named non-claim (consult §3.1 #2):** c4 proves memory PERSISTS and CLEARS. Nothing consumes memory as conversation context yet — prompt assembly lands in c7. "Memory works" as user-visible recall is NOT claimed here.

## 9. Budgets, surprises, durable-lesson candidates

**Budget:** single session, well inside the 4h packet budget; no context-limit exits.

**Surprises:**
1. **Stale packet citation:** `ClearHistory` cited at `LocalAiService.cs:173-178` actually lives at `:227-232` (line drift; the AI_AUDIT-correction pattern). Recorded with corrected cites (§1).
2. **Self-inflicted edit slip:** one pipeline edit clipped the lambda's closing `});` — caught by the immediate build, restored with the persist block inserted. No behavioral consequence; recorded for honesty.
3. **Test-harness mask:** the store-test helper's `consent ?? Granted` fallback masked the store's own placeholder default — the placeholder-default test now constructs the store WITHOUT the consent seam. Lesson: defaults-under-test must not be shadowed by test helpers.
4. **Enum serialization default:** STJ serializes enums as numbers by default — caught while writing the both-answers hand-written JSON (`"role": "User"` would have quarantined). Fixed with `JsonStringEnumConverter` on `AiMemoryRole` (readable user-data document, WPF shape).

**Durable-lesson candidates (orchestrator reconciles into port-lessons.md — enabler 2):**
1. **Placeholder policy values belong in injected mechanism records, never in persisted document defaults** — a persisted value reads as a decision and conflates "owner decided" with "default wrote"; null on disk = honestly undecided (consult-caught, §3.1 #1). (Class: values-pending honesty.)
2. **Explicit-clear on SP-005 machinery = write-empty-then-delete through the chained writer, plus the stray .tmp** — dirty must be cleared before the delete or teardown flush resurrects the file; the orphaned temp is a second resurrection path via crash-recovery adopt. (Class: persistence lifecycle.)
3. **Clear must not delete what the store is forbidden to write** — a newer-schema document survives clear (typed Degraded outcome); quarantine backups are contract-preserved artifacts, never part of "the persisted document" a clear deletes. (Class: deletion vs preservation discipline.)
4. **A typed outcome must report reality, never intent** — Clear's outcome is set from File.Exists AFTER the deletes (a privacy operation that silently fails is worse than none); and admission ≠ persistence — an admitted write's disk result is a separately observable typed completion. (Class: typed-honesty discipline; pre-completion consult B/C.)
