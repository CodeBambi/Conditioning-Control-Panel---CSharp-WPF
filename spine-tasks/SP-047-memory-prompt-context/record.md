# SP-047 — Wire companion memory into prompt context (record)

**Packet:** `spine-tasks/SP-047-memory-prompt-context/PROMPT.md` (Review Level 2)
**Board row:** `client/docs/task-board.md` P0 "Wire companion memory into prompt context" (OPEN, filed 2026-08-05; orchestrator reconciles the row at land — enabler 2, the worker never edits the board)
**Evidence class:** U (a new pair round-trips INTO the next request's prompt — falsifiable). No WH/WX claims. WSL2 named limit: laptop WSL zero distros — U discharges Windows-only, Linux owner-gated, never faked.

## 1. WPF archaeology (READ-ONLY, `File.cs:line`; `ConditioningControlPanel/Services/AIService/LocalAiService.cs`)

- **Prompt assembly (`:531-548`, verified in-tree):** genuine USER chat (`isUser`) appends the new user turn to `_messages`, trims, and sends `outgoing = _messages` — the FULL dialogue list per request: `[system, (enrichment), ...persisted/restored pairs..., new user turn]`.
- **Load-gating (`:111-126`; the `:113` consent check):** `LoadPersistedHistory` checks `ChatMemoryEnabled == false` FIRST and returns — consent off ⇒ the persisted history is NEITHER read NOR written (write side gated identically at `:144` in `PersistHistory`). This packet ports that behavior (phase-consult binding: READ-gating is a BEHAVIOR FACT, not a values decision) — "exactly" at the OBSERVABLE conversation level (consent off ⇒ no memory in any prompt, none written), with ONE recorded mechanism divergence: the greenfield store's phase-3 startup LOAD is consent-agnostic (the document is resident for clear/degraded/write machinery; WPF never reads the file under disabled consent) and the inspection read `ReadRecent` stays ungated — only the conversation-consumption read is gated (§3.1 correction 2).
- **Stateless ambient (`:531-560`; the awareness/ambient path):** automated reactions build a STATELESS `[system, (enrichment), userInput]` list, are NEVER appended to `_messages`, and are NEVER persisted (`:587-589`, `:644-646` persist runs `isUser`-only). WPF comment (`:533-538`): sharing the chat thread few-shot-poisoned ambient variety.
- **Enrichment/system exclusion (`:107-135`, `:166-170` `IsDialogueTurn`):** the system prompt and the `"[CONTEXT BLOCK — NOT DIALOGUE]"` enrichment preamble are rebuilt fresh per request and are never dialogue: excluded from persist (`:145-147` via `IsDialogueTurn`) and from pair counting.
- **Trimming (`:92` `MaxPersistedPairs = 50`; `:180-227` in-memory trim before send; `:148-152` persist trim):** WPF trims at BOTH assembly (in-memory, before send) and persist, same 50-pair window, front-trimmed. Greenfield equivalence: the c4 store trims at append (persist) with the same front-trim to `MaxPairs*2`; the assembly reads the already-capped document — the single c4 cap reproduces both WPF trims' observable outcome (the sent window == the persisted window == most recent 50 pairs). No new value decided; `AiMemoryRetention.WpfBaselinePlaceholder` (50) stands. **One-turn boundary divergence (pre-completion consult):** at the cap boundary the assembled request carries ≤MaxPairs PRIOR pairs PLUS the new user turn; WPF's in-memory trim counts the just-appended user turn INSIDE the 50 — a mechanism-level one-turn difference at the exact cap, no value decided.
- **Persist timing (`:603-646`, P2/H5):** persist only after output moderation passes; blocked turns rolled back and never hit disk. Greenfield c4 strengthened to append-NEVER (SP-040) — unchanged here.
- **Default-value tension (VERBATIM, owner question §9.2 #3 — never silently decided):** WPF companions remember by default — `ChatMemoryEnabled` default **true** (`CompanionPromptSettings.cs:120`); the greenfield placeholder does NOT — `AiMemoryConsent` placeholder default **Denied** (`AiMemoryStore.cs` enum doc; the c3-Empty/c6-none-admitted conservative-posture precedent).

## 2. Design (assembly seam)

1. **`AiRequest` gains `IReadOnlyList<AiMemoryTurn>? History = null`** (positional, optional — every existing call site and lab proof compiles/behaves unchanged; empty ⇒ payload byte-identical). Reuses the existing provider-neutral turn vocabulary (contract §5 rule 3) — no parallel context type.
2. **`IAiMemoryStore.ReadPromptContext()`** — the consent-gated conversation-consumption read (the `:113` port): consent checked FIRST; not Granted ⇒ empty, the document's turns untouched. Otherwise returns the current (append-capped) pairs, oldest first, snapshot. On the INTERFACE so any implementation must gate it.
3. **Pipeline assembly (`AiOperationPipeline.RunCoreAsync`):** placed AFTER the admission/moderation chain (offline zero-network unchanged — a short-circuited operation performs no read), BEFORE the owned-op body. Interactive class ONLY: `request = request with { History = _memory.ReadPromptContext() }` — ALWAYS overwritten (assembly is pipeline-owned; a caller-supplied History never leaks into an interactive request). Awareness class: History explicitly stripped (defense in depth — the stateless guarantee is by construction, not by omission).
4. **`LoopbackOllamaProvider.BuildPayload`:** history turns render as `{role: "user"|"assistant", content}` oldest-first BEFORE the final user message (the WPF outgoing-list order minus system/enrichment, which the greenfield request shape does not carry). Empty history ⇒ unchanged payload.
5. **Surface honesty line flip** (`CompanionWindow.axaml:177`): memory IS consumed as conversation context now — the line states consumption happens only under the memory-consent typed state, session-scoped placeholder pending owner decisions. Never implies recall under Denied (the placeholder default).
6. **`CompanionViewModel` unchanged** — assembly is the pipeline's seam; the surface sends bare prompts as before.

## 3. Consults

### 3.1 Pre-approach solo consult (Step 1)

- **Route discipline:** solo only (T-7: council unproven; `kimi-api` unregistered on this laptop). Configured solo model per `~/.pi/agent/bpx-consult.json`: **`anthropic/claude-fable-5`** (the 2026-08-04 rewire's Fable 5 route; the tool does not report a per-call answering model — the configured solo seat is the recorded actual).
- **Verdict (adopted with corrections):** the seam shape (AiRequest.History + store-owned consent-gated read + pipeline assembly + provider rendering) is sound. Corrections applied to the design above:
  1. **Caller-supplied History must never leak:** the pipeline ALWAYS overwrites History on the interactive path and explicitly strips it on the awareness path (a pass-through would make the stateless proof tautological). The awareness negative test is now falsifiable: an awareness caller passes a NON-empty History and the provider receives none.
  2. **Startup-load divergence is a named limit, recorded honestly:** the greenfield store loads the persisted document at phase-3 startup regardless of consent (clear/degraded/write-admission machinery needs it), so under Denied the turns are resident in memory and `ReadRecent` (the inspection read) stays ungated. The `:113` port gates the CONVERSATION-CONSUMPTION read (`ReadPromptContext`) — the observable conversation behavior is identical (consent off ⇒ no memory in the prompt, none written); the resident-document difference vs WPF's never-read-the-file is recorded here, not hidden.
  3. **Read-gating ≠ deletion:** the consent-flip test asserts the file on disk STILL contains the earlier pair after the flip (gating is about consumption, not erasure — erasure is the explicit-clear operation).
  4. **Half-pair hazard recorded, no new machinery:** consent revoked BETWEEN the user and assistant append persists a half pair (SP-040 consult note: revoke BETWEEN operations, never mid-operation). Now observable as a dangling user turn in a later prompt; harmless; recorded.
  5. **Cap equivalence documented** (§1 trimming): one c4 cap reproduces WPF's two 50-pair trims.
  6. **Surface line wording:** "session-scoped placeholder pending owner decisions" — no editorializing beyond the typed-default fact.

### 3.2 Pre-completion solo consult (Step 3)

- **Route:** solo, same configured seat (`anthropic/claude-fable-5` per `bpx-consult.json`).
- **Verdict (adopted with corrections — all applied):**
  1. **§1 "ports exactly" qualified inline** — observable-conversation exact; the startup-load/inspection-read mechanism divergence is named beside the claim so the board cannot lift the phrase out of context.
  2. **One-turn boundary divergence recorded** (§1 trimming): at the cap, assembly carries ≤MaxPairs prior pairs PLUS the new turn; WPF counts the new turn inside the 50.
  3. **Always-overwrite/strip = documented CLOSED DOOR** (kept, not relaxed): a future quiz-style in-memory-history consumer (admission §5 rule 5) cannot pass history through `AiRequest.History` today; retirement condition = an explicit seam change packet when such a consumer lands. Recorded on the field's XML doc + §6.
  4. **Wire test hardened:** first-request payload asserted single-message (empty-history unchanged at WIRE level, not just seam level); `ChatBodies` reads via a lock-guarded snapshot (listener-thread write vs assert-thread read race closed).
  5. **Wire-proof limit recorded:** the listener proves the CLIENT's payload shape, not that a real Ollama accepts multi-turn context — the same lab-class limit SP-035 carries (Ollama absence on the evidence box).
  6. **REQUIRED anti-overclaim line (below):** the mechanism is complete and proven, but under the Denied placeholder default + session-scoped consent, the user-visible sense of "memory" is NOT met by default — the owner default decision (§9.2 #3) gates it.
  7. **Moderation note (below):** assembled history is not re-moderated — every persisted turn passed BOTH boundaries at persist time (append-never invariant); WPF does the same (history is not re-checked per request).
- **Honesty-line verdict:** "while memory consent is on" + the visible toggle is honest; stating "default off" on the surface would editorialize a placeholder pending owner decision. Kept as shipped.

**Anti-overclaim line (consult correction 6, binding for land reconciliation):** memory→prompt assembly is COMPLETE and round-trip-proven, but recall is gated by the memory-consent typed state whose placeholder default is Denied and whose scope is session-only — a fresh launch remembers NOTHING until the user grants consent, and the grant does not survive restart. The user-visible sense of "memory" remains owner-gated (§9.2 #3: default value + scope), exactly the WPF-true-vs-placeholder-Denied tension in §1.

**Moderation note (consult correction 7):** the assembled history is NOT re-evaluated by the moderation boundary per request. Every persisted turn passed the input boundary (user turn) and the output boundary (assistant turn) at persist time under the append-never invariant (SP-040); WPF likewise moderates only the new input/output, never the replayed history. A policy change does not retro-scan stored turns (no policy VALUES exist yet — §9.2 #1).

## 4. Engine-review presence (T-2)

| Call | Type | Fired? | Verdict |
|------|------|--------|---------|
| Step 1 | plan | NO — engine-deferred (SP-195 nested-spawn block, `spawnFailed:false`, artifact `1-20260805T033252.md`) | n/a |
| Step 2 | plan | NO — engine-deferred (SP-195, `spawnFailed:false`, artifact `2-20260805T040117.md`) | n/a |
| Step 3 | plan | NO — engine-deferred (SP-195, `spawnFailed:false`, artifact `3-20260805T041132.md`) | n/a |
| Step 4 | plan | NO — engine-deferred (SP-195, `spawnFailed:false`, artifact `4-20260805T041506.md`) | n/a |

## 5. Evidence

**U class only (Windows; WSL2 named limit — laptop WSL zero distros, Linux owner-gated, never faked).**

Implementation (all in File Scope `client/src/CcpClient.Desktop/Ai/**` + `Features/Companion/**`):
- `AiProviderSeam.cs` — `AiRequest(string Prompt, IReadOnlyList<AiMemoryTurn>? History = null)` (additive optional positional; every pre-existing call site compiles and behaves byte-identically).
- `IAiMemoryStore.cs` + `AiMemoryStore.cs` — `ReadPromptContext()`: consent checked FIRST under the store gate (the `:113` port), snapshot of the append-capped pairs otherwise. Store class doc updated (the c4 "not consumed yet" non-claim discharged; the startup-load divergence recorded).
- `AiOperationPipeline.cs` — assembly in `RunCoreAsync` AFTER the admission/moderation chain, BEFORE the owned-op body: interactive ⇒ History ALWAYS overwritten with `ReadPromptContext()` (empty ⇒ null); awareness/no-memory ⇒ History explicitly stripped. The stale persist comment updated.
- `LoopbackOllamaProvider.cs` — `BuildPayload(AiRequest)`: history turns oldest-first (`user`/`assistant`) before the final user message; empty history ⇒ byte-identical pre-SP-047 payload.
- `CompanionWindow.axaml` — the honest non-claim line flipped to the now-true state (consumption stated as consent-gated; placeholders named; never implies recall under Denied).

Tests (`client/tests/CcpClient.Tests/AiMemoryPromptAssemblyTests.cs`, 8 new):
1. `NewPair_RoundTripsIntoNextRequestsPrompt_InOrder_CurrentPromptExcluded` — THE falsifiable core at the provider seam.
2. `InteractiveCallerSuppliedHistory_NeverLeaks_AssemblyIsPipelineOwned` — consult correction 1, interactive half.
3. `ConsentRevoked_NeitherReadNorWritten_FileKeepsPriorPair_ReadGatingIsNotDeletion` — the `:113` port + consult correction 3 (file byte-identical after revocation).
4. `AwarenessOperation_CallerHistoryStripped_NeverReadsMemory_StatelessByConstruction` — the falsifiable negative proof (non-empty caller History stripped).
5. `AssembledHistory_EqualsPersistedPairs_OnlyUserAssistantRoles_NothingSynthesized` — enrichment/system exclusion is BY CONSTRUCTION (the greenfield request shape carries no system/enrichment channel; WPF's `IsDialogueTurn` has no counterpart to port — recorded, not faked).
6. `Trimming_RidesC4RetentionMechanism_AssemblyCarriesOnlyTheCappedWindow` — retention=1 pair ⇒ assembly carries exactly the capped window; no assembly-side trim exists.
7. `DefaultConsentStore_DeniesWriteAndRead_PlaceholderDefaultIsExecutable` — the Denied placeholder default as executable fact.
8. `WirePayload_MessagesArrayCarriesPersistedPairBeforeNewPrompt` — the WIRE proof: real `LoopbackOllamaProvider` against a self-contained loopback listener (`AiProviderLab.cs` is outside File Scope); the payload's messages array = [persisted user, persisted assistant, new prompt]; stream/think shape intact.

Results: **609/609 unit** (floor 601; 601 + 8 new) + **33/33 headless** (floor 33) green; **0W/0E measured on `-t:Rebuild`** (one CS8618 in the new test file fixed during Step 4). Offline zero-network re-verified (`AiOfflineIntegrationTests` 2/2 — assembly is a pure local read placed after the admission chain; `SendAttempts` remains the sole network instrument). Content-free diagnostics maintained: ZERO new log/diagnostic sites in the diff; prompt/memory content rides only `AiRequest` to the provider (contract §12).

Headless surface tests: unchanged — no headless test asserts the honesty-line text (grep-verified); the flip is AXAML text only. Honestly absent beyond that per the packet's allowance.

**File-Scope expansion (SP-023 norm): ONE mechanical test edit — `client/tests/CcpClient.Tests/AiAwarenessTests.cs`** (`CountingMemoryStore` gained `ReadPromptContext() => []` to satisfy the extended interface; no assertion changed, no behavior weakened). Justification: the interface member is additive and every implementer must compile; the awareness path never consumes context so the stub returns empty. `mustNotChange` paths untouched.

## 6. Budgets, surprises, durable-lesson candidates

- **Budget:** 4h exported at launch; actual worker seat time well inside it (archaeology ~20 min, implement+tests ~50 min).
- **Surprises:** (1) one self-inflicted test bug (assembled window vs post-append store state — fixed by asserting the at-assembly expectation explicitly); (2) the C# conditional-expression typing pitfall on the payload branches (`IEnumerable<anon>` vs `anon[]` — `.ToArray()` both branches).
- **Durable-lesson candidates:** (a) interface additions on seams with test stubs force mechanical out-of-scope stub edits — name them in the packet's File Scope up front when foreseeable; (b) "the assembled history equals the persisted pairs" assertions must snapshot the AT-ASSEMBLY window, not the post-operation store state (the pipeline appends after the reply passes moderation); (c) pipeline-owned request fields that must never pass through caller values should be documented as CLOSED DOORS with a retirement condition, not silently enforced.
