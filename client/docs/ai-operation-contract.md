# AI operation contract

**Date:** 2026-07-21 · **Task:** SP-016 (task-board row 46: "Define provider-neutral AI operation contract") · **Status:** DEFINE-ONLY. No providers, no network calls, no UI, no moderation engine are implemented in this slice. Typed vocabulary + seam declarations + mechanics tests are the deliverable; provider spikes and the companion row come after. Evidence: `spine-tasks/SP-016-ai-operation-contract/record.md`.

This contract instantiates the first-attempt AI lesson (`first-attempt-lessons.md`: "AI provider and effect boundaries were not explicit enough" — ADAPT provider-neutral transport, client-side moderation, explicit local memory, opt-in commands; REJECT string-inferred failures, no cancellation, silent endpoint fallback, remote host called "local", lenient command repair, partial/no-op effects reported as success, awareness context transmission without explicit consent). It **reuses** SP-004 ownership/generation/outcome machinery (`async-lifecycle-fault-contract.md`) and SP-005's declared-only secret seam (`persistence-migration-contract.md` §9); it invents no new async machinery. It obeys SP-006's honesty rule: a declared seam is **not** a capability claim (`runtime-capability-contract.md` §4).

WPF and the first Avalonia attempt are read-only behavioral evidence / lessons-only. Nothing here imports their mechanics; every section cites evidence (`File.cs:line` or `AI_AUDIT.md`) or is marked **greenfield-decision**.

**Owner questions (recorded pending-owner, never answered by this contract):** memory consent scope/retention; moderation policy specifics (category/wordlist governance); endpoint allow-list governance (remote-Ollama admissibility); awareness consent granularity and cooldown values; admissible AI effect commands (task-board "Decisions needed" line 87). Where a value is pending-owner, the contract fixes the **semantics and the seam** and names the value as owner-supplied.

---

## 1. Typed outcomes

Every AI operation terminates in exactly one typed outcome. The terminal-outcome type **is** SP-004's `OperationOutcome` (`async-lifecycle-fault-contract.md` §2): `Completed`, `Cancelled`, or `Failed(kind, reason)`. AI-specific *domain* results (reply text, refusal, fallback classification) ride inside the operation's `Completed` payload as typed values — never as sentinel strings, never as null-with-log.

The domain payload vocabulary:

- `AiReply.Generated(text, provenance)` — a real model reply. `provenance` names the provider *class* (§6) that produced it; UI badge honesty derives from this variant and nothing else (first-attempt `AiReplyResult.IsAiGenerated`, `CCP.Core/Services/Moderation/ModerationRefusal.cs:36` — ACCEPT the idea as vocabulary, not the class).
- `AiReply.Refused(refusal)` — moderation blocked input or output; `refusal` is a typed value (§7), never the first-attempt/WPF sentinel-string channel (`ModerationRefusal.cs:44-61` — REJECTED).
- `AiReply.Unavailable(reason)` — the provider could not produce a reply (offline §11, unconfigured, capability not proven). Not an error; distinct from `Refused`.
- `AiReply.Fallback(text, reason)` — a canned, non-model response. Always distinguishable from `Generated` by type, fixing the WPF string API's fallback/refusal ambiguity (AI_AUDIT §7; first-attempt lesson REJECT "string-inferred failures").

**Evidence:** first-attempt `AiReplyResult` typed reply (`CCP.Core/Services/Moderation/ModerationRefusal.cs:36`) — ACCEPT idea; WPF catch-and-return-null (`CCP.Core/Services/AIService/LocalAiService.cs:392-395`) — REJECTED; SP-004 outcome vocabulary — REUSED.

## 2. Cancellation and generation

No new machinery. Every AI operation is an SP-004 **owned operation**: one owner, a cancellation generation, an owned completion task, a typed terminal outcome (`async-lifecycle-fault-contract.md` §1, §3). Consequences the AI row binds explicitly:

1. In-flight network/inference work observes the generation token; cancellation — never a timeout alone, never the UI thread — is what unblocks it (SP-004 §3 rule 4). Provider timeouts exist as *failure* classifiers, not as the cancellation mechanism.
2. A completion arriving for generation *N* when the owner is at *M > N* is **stale and discarded** at the point of application (SP-004 §3 rule 2): no late reply bubble, no late command execution, no late memory write.
3. **Panic cancellation** (owner stop / teardown) cancels the current generation; in-flight AI work terminates with typed `Cancelled` and is drained by SP-004's guarded `ShutdownAsync` (§6). The WPF/first-attempt fire-and-forget + catch-all shape (`async void ExecuteCommand`, `CCP.Avalonia/Services/Commands/AiCommandService.cs:87`) is REJECTED.

**Evidence:** SP-004 contract (reused); first-attempt lesson "no end-to-end cancellation" — REJECTED.

## 3. Provider switching

**Greenfield-decision** (pre-approach consult, record.md): WPF/first-attempt switch providers by live per-call selection with no generation handling (`CCP.Core/Services/AIService/AiServiceStrategy.cs:43-50`), which is exactly the "provider switching could leave late responses" failure.

The contract:

1. The provider set is a **strategy seam vocabulary** (ADAPTED from the first-attempt strategy seam): providers are identified by stable typed IDs; exactly one is *selected* at a time; the selection is a settings-level fact.
2. **Switch = generation invalidation → token cancellation → stale-application discard** (§2). A provider switch is an owner stop/restart: the generation increments, in-flight work under the old provider is cancelled, and any racing completion is discarded by the generation check. No late results — a reply started under provider A can never be displayed, executed, or remembered after a switch to B.
3. Selection alone never claims availability: a selected-but-unproven provider yields `AiReply.Unavailable` (§1) and its capability state per SP-006 (§11). Switching *to* a provider performs no network call by itself.
4. This contract defines no provider discovery, no health probes, and no fallback chain. **Silent endpoint fallback is rejected** (first-attempt lesson); if a fallback chain is ever wanted it is an owner decision with typed, disclosed transitions.

## 4. Operation classes: interactive vs awareness

Two operation classes with distinct contracts:

- **Interactive** — user-initiated, user-visible request/response: companion chat (WPF `GetBambiReplyExAsync`, `Services/AiService.cs:103`), quiz-style stateless multi-turn (first-attempt `GetRawChatCompletionAsync`, `CCP.Core/Services/AIService/IAiService.cs:47-51`). Every interactive operation returns the §1 typed reply; failures surface to the user as typed state, never as silent drops.
- **Awareness** — system-initiated reactions derived from observed context (window title/URL category tags — WPF `[Category|App|Title|Duration]` packaging, AI_AUDIT §3; keyword-trigger `AvatarComment` routing, `Services/KeywordTriggerService.cs:1280-1328`). Awareness operations:
  1. run only under an explicit, typed **awareness consent** state — the first-attempt lesson REJECTS "awareness context transmission without explicit consent". Consent granularity and default are **pending-owner**; the contract requires the consent check to be code-enforced at operation admission, not a prompt convention.
  2. are subject to typed **cooldown** semantics (per-trigger, global, per-keyword hard cooldown exist in WPF: `KeywordTriggerService.cs:165-172,584-605`; values **pending-owner**). A cooldown-suppressed awareness operation terminates `Completed` with a typed `Suppressed(cooldown)` result — observable, never a silent no-op.
  3. on refusal/unavailable outcomes, drop silently *by type* (no out-of-context refusal bubble — the WPF call-site convention `ModerationRefusal.cs:49-53`, retained as a typed behavior, not sentinel strings).

## 5. Local memory

**Declared-only seam — not a capability.** This contract defines:

1. `IAiMemoryStore` (declared-only, no implementation): append turn / read recent / **explicit clear**. An explicit-clear operation must exist and must be user-reachable; WPF precedent `ClearHistory()` + "Reset Memory" UI (AI_AUDIT §2 "Memoria").
2. **Consent gating as contract**: memory writes occur only under an explicit memory-consent state; scope (global vs per-feature) and retention (cap values; disable = retain dormant vs delete) are **pending-owner** (task-board line 87). The seam is shaped so either retention answer is implementable without contract change.
3. Memory is provider-neutral state, not provider state: switching providers does not implicitly clear memory, and clearing memory is always an explicit operation.
4. Memory content is user data under the persistence contract's authority (`persistence-migration-contract.md`) when persisted; it never flows into diagnostics (§12) and is never a secret (§10).

**Honesty rule (SP-006 §4):** the declared seam claims nothing. The first consumer row implements and proves it.

**Evidence:** WPF local history: 50-pair cap, `ChatMemoryEnabled` default true, file persistence (`CCP.Core/Services/AIService/LocalAiService.cs:36-153`; AI_AUDIT §2 "Memoria") — ADAPTED; quiz in-memory history (`QuizService.cs:175`) — trace.

## 6. Endpoint classification and disclosure

1. Every endpoint an AI operation may reach carries a typed **endpoint class**:
   - `Loopback` — traffic stays on this machine (default Ollama `http://localhost:11434/`, `LocalAiService.cs:516`).
   - `FirstPartyCloud` — the operated proxy (`codebambi-proxy.vercel.app`, hardcoded `Services/AiService.cs:27`; quiz/community-prompt/bug-report siblings, AI_AUDIT §10).
   - `ThirdPartyCloud` — any other remote host (OpenAI-compatible provider, first-attempt `OpenAiService.cs`).
   - `RemoteHostOllama` — a **user-configured non-loopback Ollama host is remote**, classified exactly like third-party cloud for disclosure purposes. The first-attempt lesson REJECTS "remote host called 'local'" (`first-attempt-lessons.md` rejected assumptions: "A configurable non-loopback Ollama host is remote"). WPF/first-attempt have no such classification (gap; `LocalAiService.cs:406-415` accepts any absolute URI).
2. **Disclosure rule:** before any operation class can reach an endpoint, the endpoint's class must be resolvable from configuration alone (no probe required), and user-visible surfaces must be able to state the class of the destination honestly (§11 offline semantics and the §1 provenance field consume this). Classification is pure host-shape analysis — this contract performs **no network calls** and configures **no real endpoints**.
3. Endpoint **allow-list governance** (which remote hosts, if any, are admissible) is **pending-owner** (task-board line 87). The contract fixes the classification vocabulary and the disclosure rule, not the allow-list.
4. Hardcoded first-party endpoints (cloud proxy, catalogue, bug-report, updates — AI_AUDIT §10 table) are recorded as inventory facts, not admissions; each becomes admissible only when its consumer row lands.

**Evidence:** AI_AUDIT §10 network surface; §1 provider inventory; first-attempt rejected-assumption "Selecting local AI guarantees local-only data".

## 7. Moderation

1. **Every-surface rule:** every text field that enters an AI request — chat input, awareness context fields, prompt templates (user-authored preset sections, community prompts, quiz templates; AI_AUDIT §8 input pipeline) — passes through the input side of the moderation boundary. Every model-produced text field that will be shown, spoken, persisted, or executed passes through the output side.
2. **Every-command-field rule:** the strict command envelope (§8) moderates *every free-text command field* (subliminal text, mantra text, bounce words, media titles) before execution. WPF/first-attempt gate commands on master/per-effect toggles only (`CCP.Avalonia/Services/Commands/AiCommandService.cs:87-103`) — command-field moderation is a **greenfield-decision** closing that gap.
3. **Verdict taxonomy (typed):** `Pass` / `Block(category, surface)` / `SoftHit(category, surface)` — adapted from first-attempt `ModerationResult` (`CCP.Core/Services/Moderation/IModerationGuard.cs:7-22`). Refusals are typed values carrying `ModerationSource` (`Input` | `Output`) and a category (`ModerationRefusal.cs:10-26`); the sentinel-string channel is REJECTED.
4. The guard is code outside the model: user-authored prompt sections can never widen or bypass it (first-attempt `IModerationGuard` doc, `IModerationGuard.cs:25-31`; AI_AUDIT §7 — WPF's prompt-side-only refusal is the failure this closes).
5. **Policy values pending-owner:** the category list, wordlist contents, and soft-hit handling policy are owner decisions (task-board line 87). This contract defines the verdict taxonomy and the boundary placement; it implements **no moderation engine**.

## 8. Strict command envelope

**Greenfield-decision** (pre-approach consult, record.md): the WPF/first-attempt envelope behavior (lenient JSON repair, brace-balancing, mixed-format salvage — `CCP.Core/Services/AIService/AiResponseParser.cs:159-232`, `CCP.Core/Models/AiCommandData.cs:36-47`) is exactly what is superseded.

1. **Schema authority:** one typed envelope schema — `{ reply, commands[] }` where each command has a typed `command` discriminator and a per-command typed data shape (the 11 WPF command types are the inventory baseline: flash_image, bubbles, subliminal, mantra_lockscreen, spiral, pink, bounce, haptic, video, audio, getbacktome — `CCP.Avalonia/Services/Commands/AiCommandService.cs`; admissibility of each is **pending-owner**). The schema — never the model's prose — defines what parses.
2. **Reject-by-default:** unknown command → rejected; missing/mistyped/extra-significant fields → rejected; malformed JSON → rejected. **Zero repair:** no brace balancing, no markdown-fence extraction, no mixed-format salvage, no retry-with-append.
3. **Whole-envelope atomic rejection:** validation of *every* command runs before execution of *any*. One invalid command rejects the entire envelope — **zero commands execute** on mixed/invalid payloads (the board spike row's acceptance wording is decisive). The WPF per-command drop with surviving-sibling execution (`AiCommandService.cs:103`) is REJECTED ("partial/no-op effects reported as success").
4. **Zero execution semantics are types, not runtime hope:** an envelope that failed validation has no executable representation — the validator returns per-command verdicts (§9), and only a fully-valid envelope can be converted into an execution plan. Tests assert this at the type/API level.
5. Bounds live in the schema per command (WPF clamp inventory as baseline facts: flash amount 0–8 / duration 0–10 s / size 0–150 %; bubbles 0–10 /min; subliminal ≤ 80 chars, opacity 0–60; spiral/pink 0–30; mantra ≤ 200 chars, amount 0–5; haptic 0..`MaxAiHapticIntensity` (default 0.6), duration 0–10 s; getbacktome delay 1–600 s, depth ≤ 2; per-response cap 3 — `AiCommandService.cs:47,146-260`, AI_AUDIT §2 capability surface). Out-of-range is a rejection verdict, never a silent clamp-to-valid. Media paths: root-contained under the assets tree, traversal rejected, extension allow-list (`AiCommandService.cs:290-309` — ACCEPTED as envelope rules).
6. Execution of a valid envelope remains gated on the master + per-effect consent toggles (defaults conservative: master OFF; only bubbles/subliminal/bounce on — `CompanionPromptSettings` via AI_AUDIT §9 defaults) and on moderation (§7 rule 2). Gating happens *before* execution but *after* validation; a gated-out command is a typed verdict, not a drop.

## 9. Per-command results

**Greenfield-decision** (named acceptance item; WPF has log-only per-command failure).

1. Every command in a submitted envelope receives exactly one typed verdict in the envelope result, in order:
   - `Valid` — schema-valid and executable (execution may still be gated; §8 rule 6);
   - `UnknownCommand` — carries NO payload: the submitted name is raw model output and never enters a verdict;
   - `MalformedData(field, code)` — `field` is a schema-known name or the stable token `(unrecognized)`, never a model-supplied name; `code` is a stable machine token, never raw JSON or user text;
   - `OutOfRange(field, limit)` — names the field and the violated bound;
   - `ModerationBlocked(category)` — §7 rule 2;
   - `ConsentGated(toggle)` — master/per-effect gate denied;
   - `NotExecuted(reason)` — the command was valid but did not run, with `reason` ∈ { `envelope-rejected`, `cap-exceeded`, `superseded-generation`, `consent-gated`, `moderation-blocked`, `effect-unavailable` }. A valid sibling in a rejected envelope is `NotExecuted(envelope-rejected)` — never silently dropped. (Reason set extended 2026-08-04 at the SP-044 land: `superseded-generation` now lands at execution level — SP-019 limit 7 discharged; `consent-gated`/`moderation-blocked` = the post-validation gate/moderation outcomes, typed never dropped (the WPF silent-drop strengthening); `effect-unavailable` = admitted by gates but no effect backend exists — the typed placeholder while no greenfield effect surfaces exist. The execution result's `Valid` means DISPATCHED, distinct from validation-time `Valid`.)
2. The envelope result is a typed, serializable value — the honest record of *why nothing ran* — and is what the spike row's "zero execution on mixed/invalid payloads" asserts against.
3. Execution results (when an execution row lands) extend this vocabulary per command; they never collapse two distinct failures into one string.
4. A rejected envelope surfaces **no reply text**: the result's `Reply` is null whenever `Accepted` is false. Reply moderation is the operation pipeline's output boundary (§7 rule 1), not the validator's — the validator's job is to ensure a rejected envelope hands nothing showable or executable to anyone.

## 10. Secret storage

1. The settings document never carries AI secrets (provider keys, tokens) — SP-005's rule (`persistence-migration-contract.md` §9) applies unchanged; a persisted model stores an opaque secret *name*, never a value.
2. The seam **is** SP-005's declared-only `ISecretStore` (`Get`/`Set`/`Delete` by name) — REUSED, still declared-only. This slice adds no implementation and no second seam.
3. WPF/first-attempt auth facts are inventory only: cloud auth via `X-Auth-Token`/Patreon Bearer (AI_AUDIT §9), OpenRouter key server-side. The first row with a secret consumer (provider spike) implements the seam against a platform store and records the admission.

## 11. Offline behavior

1. Offline/unavailability is **typed per operation class**, never a swallowed null:
   - Interactive → `AiReply.Unavailable(reason)` (§1); the user sees honest typed state.
   - Awareness → typed drop (§4 rule 3).
   - Command execution → `NotExecuted` with a typed reason on the envelope result (§9).
2. A provider's readiness is a **capability state** per SP-006: registration/selection/OS checks never yield availability; only a real probe of the selected backend does (`runtime-capability-contract.md` §2). This slice declares the mapping; provider probes land with the spike row. WPF's identity-check `IsAvailable` (`Services/AiService.cs:52`) is evidence of the wrong shape, not a model.
3. **Offline = zero network.** When no provider is proven available, the AI subsystem performs no outbound AI traffic — no speculative retries, no endpoint guessing (AI_AUDIT §10 inventory is the ceiling of what may ever be contacted).
4. Local (loopback-class) operations degrade independently of cloud: a cloud outage never blocks loopback operations and vice versa; the two classes report their own typed states (§6 disclosure consumes this).

## 12. Content-free diagnostics

**Schema-level rule, not a convention.**

1. The diagnostic record for AI operations (`AiDiagnosticRecord` in the typed vocabulary) exposes **only**: enums, stable machine-readable codes, counts, durations, generation identifiers, provider/endpoint *classes* (§6), and per-command verdict codes (§9). It has **no free-text field** that could carry a prompt, a completion, a user message, a window title, a keyword, or command payload text. Failure `reason` fields carry exception **class names** or stable codes — never exception messages, which can embed user input (pre-approach consult condition, record.md). Per-command verdict codes come from the closed mapping `AiDiagnosticCodes.VerdictCode` — verdict TYPE names only; `Field`/`CategoryCode`/reason payloads never enter diagnostics.
2. The content-freedom proof is **structural**: a test asserts the serialized property set of the diagnostic record is exactly the closed allow-list of content-free fields. A test that checks one instance's values is not a schema proof.
3. Diagnostics never contain memory contents (§5), secrets or secret values (§10), or raw envelopes. Logs that carry user text remain governed by the global privacy rules and are outside the diagnostic record's schema entirely.
4. First-attempt `ConnectionDiagnosticResult`/`DiagnosticCategory` (`CCP.Core/Services/AIService/OpenAiService.cs:419,459`) — ADAPTED as the idea of typed endpoint diagnostics, content-free.

## 13. Implementation-topology neutrality

**Greenfield-decision.** This contract fixes vocabulary, seams, and mechanics — it does not decide: where providers live (in-process vs out-of-process), whether the strategy is DI-selected or registry-selected, whether moderation runs in-process or behind a service boundary, how memory is persisted, or what the execution pipeline's threading topology is. Any topology that satisfies §§1–12 and the SP-003/SP-004/SP-005/SP-006 contracts is conformant. Provider spikes and the companion implementation row make topology decisions with runtime evidence; this contract neither requires nor forbids any of them.

---

## Conformance checklist (tested in this slice)

- Envelope validation: valid envelope validates; unknown command, malformed data, out-of-range, and malformed-JSON envelopes are rejected with typed per-command verdicts; a mixed envelope (valid + invalid) rejects atomically and every entry carries a verdict (`Valid` siblings → `NotExecuted(envelope-rejected)`).
- Zero execution semantics asserted as types: no API path converts an invalid envelope into an executable plan.
- Per-command results round-trip through serialization; verdict vocabulary is closed.
- Diagnostic content-freedom schema proof: the diagnostic record's serialized property set is exactly the content-free allow-list; no string field can carry prompt/completion/user text.
- Generation-invalidation reuse demonstrated against the real SP-004 registry: a switched (restarted) owner cancels the in-flight generation (typed `Cancelled`) and a stale-generation completion is not applied.
- Serialization round-trips of every vocabulary type.
- Seams (`IAiMemoryStore`, `ISecretStore`) are declarations only — no implementation exists in this slice (SP-006 honesty rule).
