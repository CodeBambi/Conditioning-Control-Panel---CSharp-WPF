# SP-044 — AI companion slice c6: command execution (record)

Task: validated envelope → execution plan → gated per-effect dispatch (`AiCommandExecutor`), consent gates post-validation, moderation pre-execution via `AiEnvelopePolicy.ForBoundary`, `NotExecuted(SupersededGeneration)` at execution level (SP-019 limit 7), canary zero-execution proofs, wave-7 assigned obligations (Reserved→Wired flip; bool-door retirement if clean).

## 1. Archaeology (WPF READ-ONLY; File.cs:line)

### 1.1 Effect dispatch + consent gate shapes (WPF)

- **Master gate:** `Services/Commands/AiCommandService.cs:42` — `if (!settings.AllowAiToControlEffects) drop`. Default OFF (`CCP.Core/Models/CompanionPromptSettings.cs:99`).
- **Per-effect gate:** `Services/Commands/AiCommandService.cs:49,182-200` — `IsEffectAllowed` maps each command type to one settings bool; unknown → false. WPF defaults (`CompanionPromptSettings.cs:103-112`): flash/video/audio OFF, **bubbles/subliminal/bounce ON**, overlay(lockcard/haptic/getbacktome) OFF. Video additionally requires `MandatoryVideosEnabled` (`AiCommandService.cs:187`).
- **Per-batch cap:** `AiCommandService.cs:55` — `MaxCommandsPerResponse` (3) counted only for commands that passed both gates; over-cap commands are DROPPED with a log line (no typed verdict).
- **Dispatch:** gate-passed commands go through `CommandFactory.CreateCommand` (`Services/Commands/CommandFactory.cs:22-40`) → per-type `ICommand` executors; `getbacktome` recurses depth ≤ 2 without re-gating.
- **Prompt-side effectsEnabled check:** `Services/AIService/LocalAiService.cs:513`, `OpenAiCompatibleService.cs:481,499` — master toggle also gates whether command JSON is even parsed from a reply.
- **Gated commands in WPF are silently dropped** (log line only). The greenfield types them (`ConsentGated`) — a deliberate strengthening, contract §9.

### 1.2 First-attempt toggle-only gate (REJECTED)

- `CCP.Avalonia/Services/Commands/AiCommandService.cs:87-103`: master gate → per-effect gate → batch cap → dispatch. **No moderation anywhere on command fields**; gated commands silently dropped; `async void ExecuteCommand` (fire-and-forget, exceptions swallowed to log). REJECTED per contract §7 rule 2 (every-command-field moderation is a greenfield decision closing that gap) and the first-attempt lessons (silent drops, string-inferred failures).

### 1.3 SP-019 fuzz outcomes this dispatch must reproduce

- 62-case matrix (`client/spikes/CcpSpike.AiProvider/Fuzz.cs`), ported as `AiEnvelopeFuzzRegressionTests.cs`: whole-envelope atomic rejection on any invalid member; valid siblings of a rejected envelope carry `NotExecuted(EnvelopeRejected)`; zero-execution proven by a test-side **canary** (records every plan command handed to it; a rejected envelope has NO plan — internal ctor — so the canary cannot be invoked; a valid payload's plan must record EXACTLY its commands — the falsifiable pair).
- **SP-019 limit 7** (`client/docs/ai-provider-spike.md:78`): `NotExecuted(SupersededGeneration)` is not validator-reachable — no execution pipeline existed; supersession was proven at operation level only. **This slice lands the per-command verdict at execution level.**

### 1.4 Landed mechanics consumed (never re-implemented)

- `AiEnvelopeValidator` (`AiCommandEnvelope.cs`): Phase 1 schema validation of every command → atomic rejection; Phase 2 gating = consent toggles then moderation on every free-text field (`Gate`, `FreeTextFields` — the single enumeration source). `AiExecutionPlan` internal ctor — only the validator constructs one.
- `AiEnvelopePolicy.ForBoundary` (`AiCommandEnvelope.cs`): the PRODUCT composition point wiring the c3 boundary into validation. c6 consumes it; never re-implements.
- `AsyncOperationOwner` generation machinery (`AiOperationPipeline` uses `Begin()`/`IsLive(generation)`): the supersession mechanism the executor checks at dispatch.
- c5 packaging (`AiAwarenessContextPackaging.TryPackage`, `AiAwarenessService.cs:191-218`): every awareness context field through `EvaluateInput` on the `awareness-context-fields` surface pre-assembly — the landed wiring behind the Reserved→Wired flip.

## 2. Design

### 2.1 `AiCommandExecutor` (contract-named; NEW `client/src/CcpClient.Desktop/Ai/AiCommandExecutor.cs`)

Envelope → plan → dispatch:

```
AiEnvelopeValidator.Validate(raw, AiEnvelopePolicy.ForBoundary(...))   // c1/c3 shipped
  → AiEnvelopeResult { Verdicts, Plan }                                 // Plan = Valid commands within cap only
AiCommandExecutor.Execute(plan, gates)
  → per command, IN ORDER:
     1. !gates.IsGenerationLive(gates.Generation) → NotExecuted(SupersededGeneration)   // SP-019 limit 7 — checked PER COMMAND before each dispatch; a mid-dispatch flip supersedes the rest, never a late apply
     2. !gates.MasterEffectsEnabled               → ConsentGated("master")              // WPF AiCommandService.cs:42 shape, typed not dropped
     3. !gates.IsEffectAllowed(kind)              → ConsentGated(kind token)            // WPF IsEffectAllowed shape, typed not dropped
     4. no handler registered for kind            → NotExecuted(EffectUnavailable)      // typed placeholder: NO effect backends exist in the greenfield client
     5. handler.Execute(command)                  → Valid                               // the canary/back-end seam
```

- **Gates are post-validation, re-evaluated at dispatch** (contract §8 rule 6: gating after validation, before execution). Consent state captured at validation can change before dispatch; the executor re-checks against the CURRENT gate state — a command validated under master-ON and dispatched under master-OFF is `ConsentGated("master")` at execution, typed, never partially applied.
- **Closed vocabulary:** execution adds exactly one new `AiNotExecutedReason` member — `EffectUnavailable` (kind admitted by gates but no effect backend exists; the typed placeholder). `ConsentGated`/`EnvelopeRejected`/`SupersededGeneration`/`CapExceeded`/`ModerationBlocked` are the shipped c1 vocabulary. Content-free: verdicts carry stable tokens only, never command-field contents.
- **`IAiEffectHandler` seam** (`void Execute(AiCommand)`): the dispatch target. NO product implementations exist (no flash/subliminal/spiral backends); tests inject canary handlers recording every invocation — zero-execution proofs are falsifiable (canary MUST record exactly the admitted commands on the happy path; MUST be silent on every rejected/gated/stale class). The executor is SYNCHRONOUS (no backends exist — async speculative; upgrade path recorded) and does NOT catch handler exceptions (a faulting handler faults the dispatch — honest, never the WPF/first-attempt swallow-and-log).
- **`AiExecutionGates.FromPolicy(policy, generation, isGenerationLive)`** (consult §3.1.1 — single consent source: the gates derive from the SAME `AiEnvelopePolicy` the validator consumed) and **`AiExecutionGates.NoneAdmitted(generation, isLive)`** — the pending-owner default (admission §9.2 #5): master OFF, zero per-effect allowances. **DIVERGENCE (deliberate, recorded, never silent):** the WPF baseline (contract §8 rule 6; `CompanionPromptSettings.cs:106-110`) has master OFF but bubbles/subliminal/bounce ON; the greenfield pipeline executes only the owner-admitted subset, default NONE (conservative pending-owner posture — admission §8 c6 row, verbatim acknowledgment).
- **Atomic envelope semantics stand:** a rejected envelope has no plan (internal ctor) — the executor is unreachable; valid siblings keep their `NotExecuted(EnvelopeRejected)` verdicts from validation; nothing executes. The canary proves ZERO invocation on every rejected class.
- **Moderation pre-execution:** wired through `ForBoundary` (consumed, never re-implemented). A moderated free-text field = `ModerationBlocked` at the envelope, the command never enters the plan, zero dispatch.
- No WH/WX claims: provable scope is U-only per the admission's conditioned shrink (no effect surfaces exist; laptop WSL has zero distros — U discharges Windows-only here, Linux run owner-gated, never faked).

### 2.2 Test plan (NEW `client/tests/CcpClient.Tests/AiCommandExecutorTests.cs`)

1. None-admitted default: valid plan dispatched under `NoneAdmitted` → every command `ConsentGated("master")`, canary silent (WPF-divergence posture proven).
2. Master ON, no per-effect allowance → `ConsentGated(kind)` per command, canary silent.
3. Master ON + subset admitted + canary → admitted kinds execute in envelope order (canary records exactly), others `ConsentGated`; verdict order = plan order.
4. Admitted kind with NO handler → `NotExecuted(EffectUnavailable)` (typed placeholder).
5. **Stale generation before dispatch** (real `OperationRegistry`/`AsyncOperationOwner`: validate → plan under gen N, `Begin()` to N+1, dispatch) → every command `NotExecuted(SupersededGeneration)`, canary silent — SP-019 limit 7 discharged.
6. **Mid-dispatch supersession**: canary for command 1 flips the generation → command 1 executes, the rest `NotExecuted(SupersededGeneration)` — never a late apply.
7. Zero-execution across every rejected class (malformed JSON, unknown command, out-of-range, moderated field via `ForBoundary`, mixed valid+invalid): rejected envelopes have no plan (executor unreachable — type-level); mixed-envelope valid siblings carry `NotExecuted(EnvelopeRejected)`; moderated command = `ModerationBlocked`, never in plan; canary silent throughout.
8. Consent flip between validation and dispatch (validated master-ON, dispatched master-OFF) → `ConsentGated("master")` at execution, canary silent — post-validation gating proven.
9. Cap overflow: 4 valid commands → 3 in plan, 4th `NotExecuted(CapExceeded)`; dispatch executes exactly 3.

### 2.3 Assigned obligations (wave-7 land)

- **Reserved→Wired flip:** `AiModerationSurfaces.AwarenessContextFields` → `Wired`, `ReservedFor=null`, entry point named `AiAwarenessContextPackaging.TryPackage`; coverage test counts 5/6 → **6/5** + one assertion arm (packaging blocks a forbidden field → false + typed refusal; clean context packages) in `AiModerationCoverageTests.cs`.
- **Bool-door retirement:** grep (2026-08-04, this worktree) finds `RunAwarenessAsync(request, awarenessConsent: bool)` call sites in **6 test files**: `AiMemoryPipelineTests.cs:184`, `AiModerationCoverageTests.cs:94,110,257`, `AiModerationPipelineBoundaryTests.cs:103,140,201`, `AiOfflineIntegrationTests.cs:53`, `AiOperationPipelineTests.cs:297`, `AiProviderLabIntegrationTests.cs:329`. The packet's "4 call sites" estimate is stale. `AiMemoryPipelineTests.cs`, `AiModerationPipelineBoundaryTests.cs`, `AiProviderLabIntegrationTests.cs` are OUTSIDE SP-044 File Scope — migration cannot be "typed overload everywhere", so the bool overload CANNOT be deleted. **Disposition (pending grep re-proof at Step 3): record the resistance, leave the overload + retirement condition intact.**

## 3. Consults

### 3.1 Pre-approach (Step 1)

Solo consult, 2026-08-04, with the full archaeology + design above. **Actual answering model: NOT exposed by the consult tool output** (no model identity in the response; session model per env is `PI_MODEL=k3`/`PI_PROVIDER=kimi-coding`; the 2026-08-04 rewire names Opus 5 as the intended main route — identity not verifiable from the tool; recorded as a tooling limit, never guessed). Verdict (substance, adopted):

1. **Two gate-truths risk (ADOPTED):** executor gates derived from a SEPARATE consent source than the validator's `AiEnvelopePolicy` could drift. Fix: `AiExecutionGates` gets a factory deriving from the same `AiEnvelopePolicy` instance (single source); deliberate-flip tests prove the dispatch re-gate catches consent changed between validation and dispatch.
2. **Verdict semantics naming (ADOPTED):** the execution result type is named/documented so `Valid` in an EXECUTION result means "dispatched", distinct from validation-time `Valid`.
3. **Check order generation-first: CONFIRMED** (stale generation is a hard stop; nothing may apply under a dead generation).
4. **`EffectUnavailable`: KEEP** (packet-sanctioned; the alternatives — throw, mis-type as ConsentGated, fake Valid — are all dishonest). **Contract §9's reason set {envelope-rejected, cap-exceeded, superseded-generation} is extended in code; the contract doc is outside worker scope — recorded here as an orchestrator follow-up (land-time reconciliation), constitution authority-order compliant.**
5. **Handler exceptions (ADOPTED):** the executor does NOT catch; a faulting handler faults the dispatch call — honest, never silent. Deliberate divergence from the WPF/first-attempt swallow-and-log (`AiCommandService.cs` top-level catch). Documented on the seam.
6. **Sync executor (ADOPTED):** no backends exist; async is speculative. Signature change recorded as the upgrade path when a real async backend lands.
7. **No product call site yet (ADOPTED as recorded non-claim):** the executor is a mechanism proven against the REAL `AsyncOperationOwner`; c7 owns companion-UI composition (whether/when reply commands are dispatched is an owner-facing behavior decision, not this slice's to make).
8. **Bool-door (ADOPTED):** leave ALL call sites untouched (mixed typed/bool state would complicate the next packet's grep); record the exact retirement condition + the stale "4 call sites" count; orchestrator schedules full migration in a packet covering all 6 files.
9. **Reserved flip naming (ADOPTED):** `OperationEntryPoint = "AiAwarenessContextPackaging.TryPackage"` has precedent — `CommandFreeText` names `AiEnvelopeValidator.FreeTextFields` (a non-pipeline seam). Tripwire re-verified: it iterates pipeline `Run*Async` methods requiring a matching Wired input row; a Wired row naming a non-pipeline seam is unconstrained. Grep confirmed `AiModerationCoverageTests.cs` is the ONLY test file touching dispositions — no other suite breaks on the flip.
10. Verdict round-trips made EXPLICIT: end-to-end tests assert the full per-command verdict sequence envelope-JSON → validation → dispatch.

### 3.2 Pre-completion (Step 4)

(pending)

## 4. Engine review presence (T-2)

(pending — recorded per `spine_review_step` call)

## 5. Budgets, surprises, durable-lesson candidates

(pending)
