# SP-047 — Wire companion memory into prompt context (record)

**Packet:** `spine-tasks/SP-047-memory-prompt-context/PROMPT.md` (Review Level 2)
**Board row:** `client/docs/task-board.md` P0 "Wire companion memory into prompt context" (OPEN, filed 2026-08-05; orchestrator reconciles the row at land — enabler 2, the worker never edits the board)
**Evidence class:** U (a new pair round-trips INTO the next request's prompt — falsifiable). No WH/WX claims. WSL2 named limit: laptop WSL zero distros — U discharges Windows-only, Linux owner-gated, never faked.

## 1. WPF archaeology (READ-ONLY, `File.cs:line`; `ConditioningControlPanel/Services/AIService/LocalAiService.cs`)

- **Prompt assembly (`:531-548`, verified in-tree):** genuine USER chat (`isUser`) appends the new user turn to `_messages`, trims, and sends `outgoing = _messages` — the FULL dialogue list per request: `[system, (enrichment), ...persisted/restored pairs..., new user turn]`.
- **Load-gating (`:111-126`; the `:113` consent check):** `LoadPersistedHistory` checks `ChatMemoryEnabled == false` FIRST and returns — consent off ⇒ the persisted history is NEITHER read NOR written (write side gated identically at `:144` in `PersistHistory`). This packet ports that behavior exactly (phase-consult binding: READ-gating is a BEHAVIOR FACT, not a values decision).
- **Stateless ambient (`:531-560`; the awareness/ambient path):** automated reactions build a STATELESS `[system, (enrichment), userInput]` list, are NEVER appended to `_messages`, and are NEVER persisted (`:587-589`, `:644-646` persist runs `isUser`-only). WPF comment (`:533-538`): sharing the chat thread few-shot-poisoned ambient variety.
- **Enrichment/system exclusion (`:107-135`, `:166-170` `IsDialogueTurn`):** the system prompt and the `"[CONTEXT BLOCK — NOT DIALOGUE]"` enrichment preamble are rebuilt fresh per request and are never dialogue: excluded from persist (`:145-147` via `IsDialogueTurn`) and from pair counting.
- **Trimming (`:92` `MaxPersistedPairs = 50`; `:180-227` in-memory trim before send; `:148-152` persist trim):** WPF trims at BOTH assembly (in-memory, before send) and persist, same 50-pair window, front-trimmed. Greenfield equivalence: the c4 store trims at append (persist) with the same front-trim to `MaxPairs*2`; the assembly reads the already-capped document — the single c4 cap reproduces both WPF trims' observable outcome (the sent window == the persisted window == most recent 50 pairs). No new value decided; `AiMemoryRetention.WpfBaselinePlaceholder` (50) stands.
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

## 4. Engine-review presence (T-2)

| Call | Type | Fired? | Verdict |
|------|------|--------|---------|
| Step 1 | plan | (recorded at step boundary) | |
| Step 2 | plan | | |
| Step 3 | plan | | |
| Step 4 | plan | | |

## 5. Evidence

(filled in Step 3)

## 6. Budgets, surprises, durable-lesson candidates

(filled in Step 3)
