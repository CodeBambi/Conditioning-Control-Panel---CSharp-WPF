# SP-038 record — AI companion slice c3: moderation boundary

**Task:** SP-038 · **Board row:** "Implement AI companion and awareness integration" (P0), slice c3 of `client/docs/ai-companion-admission.md` §8 · **Lane:** lane-1 · **Date:** 2026-08-04

Evidence class: **U** (boundary-coverage tests enumerate the surface/command-field inventory). No WH/WX/LAB claims. No Wayland claims. **WSL2 named limit (probe 2026-08-04, this laptop):** `wsl -l -q` → empty, exit 0 — WSL installed with ZERO distros; the "U both platforms" class discharges Windows-only with the Linux run recorded as owner-gated (provision a distro = owner decision), never faked (same disposition as SP-035 §3.2, sanctioned by the 2026-08-04 amendment pattern).

---

## 1. WPF archaeology (READ-ONLY, `File.cs:line`)

### 1.1 The 4 moderation call sites — exact input/output positions

1. **`Services/AiService.cs:274` (cloud INPUT):** inside `GetAiResponseAsync(userInput, systemPrompt, returnRefusalSentinel)` — offline check first (`:263-268`), then `guard.CheckInput(userInput)` BEFORE the HTTP request ("prohibited inputs never leave the client"). Block → `ModerationLog.Record(category, "input", "cloud")`; escalation `ModerationCounter.RecordHit` ONLY when `returnRefusalSentinel` (interactive chat path — "content the user actually typed"; awareness/keyword/lockscreen/video paths pass false, `:278-284`). Return = `ModerationRefusal.InputSentinel` (interactive) or null (background). Soft path: `Allow && Category == ProfessionalAdvice` → log-only (`:288`+).
2. **`Services/AiService.cs:409` (cloud OUTPUT):** after sanitize, `guard.CheckOutput(sanitized)` before display; block → log + comment "model OUTPUT that trips the filter is never the user's doing" (no escalation), return OutputSentinel/null.
3. **`Services/AIService/LocalAiService.cs:415` (local INPUT):** the awareness-context packagers (`GetWindowReactionAsync`/`GetStillOnReactionAsync` build `[Category: … | App: … | Title: … | Duration: …]`, `:405-419`) flow into the same `GetAiResponseAsync`; the local service's own input/output moderation sits inside its shared response path (`:415` input, `:555` output) — same Layer-1 code-side guard, prompt cannot bypass.
4. **`Services/Quiz/QuizService.cs:561` (quiz OUTPUT):** `guard.CheckOutput(content)` on AI-generated questions/archetypes; block → log + `OutputBlocked` (fail-closed discard, fallback takes over); comment: quiz output is never user-typed → no escalation. Quiz has NO input-moderation call site (templates unmoderated in WPF — the gap §3 rule 1 closes by design).

**Command fields are NEVER moderated in WPF/first-attempt** — toggle-only gating `CCP.Avalonia/Services/Commands/AiCommandService.cs:87-103` (lessons-only). Greenfield closes this (contract §7 rule 2).

### 1.2 ModerationCounter mechanism (`Services/Moderation/ModerationCounter.cs`)

- Constants `:84-87`: `WarningThreshold=3`, `CooldownThreshold=5`, `WindowMinutes=10`, `CooldownMinutes=5` — **recorded as baseline, never as decision** (admission §3 rule 4; §9.2 #1 owner-pending).
- `RecordHit` `:108-138`: prune expired (rolling 10-min window) → append hit → count ≥5 and no active cooldown → cooldown = now+5min (fire `CooldownStarted`); else count ≥3 and `!_warningShown` → one-shot warning (`WarningTriggered`); hits during an active cooldown do NOT stack a longer cooldown (`:114-117` comment).
- `GetState` `:160-200`: prune; expired cooldown → clear cooldown + hits + warning flag (fresh start), fire `CooldownEnded`; state = `(HitsInLastTenMinutes, WarningTriggered, CooldownActive, CooldownEndsAt)`.
- State location: persisted JSON `%APPDATA%/ConditioningControlPanel/moderation-counter.json` (`:96-104`), ISO-8601 UTC, fire-and-forget save ("a failed write must not break the moderation pipeline").

### 1.3 Sentinel channel — REJECTED (`Services/Moderation/ModerationRefusal.cs:53-54`)

`InputSentinel = "##CCP_MODERATION_REFUSAL_INPUT##"` / `OutputSentinel` string channel bubbled through the string API; awareness call sites convert sentinel → null to silently drop (`:49-53` doc). The typed replacement already landed in c1: `AiReply.Refused(AiModerationRefusal(category, AiModerationSource))`. c3 produces those typed values from the boundary; no string matching anywhere.

### 1.4 Removed keyword pre-check — why the boundary is uniform (`Services/KeywordTriggerService.cs:1638-1646`, H7)

WPF REMOVED the upstream `CheckInput` pre-check in the keyword path because the service boundary already moderates at the HTTP edge; double-checking produced two log entries + two counter hits per keyword event, accelerating the user toward 3-hit/5-hit unfairly. Lesson ported: **one boundary at the operation edge, exactly one hit accounting per blocked event** — c3's single pipeline-level boundary is the uniform shape; per-surface hit accounting beyond the WPF baseline is a policy-value matter (§9.2), not a reason to skip a surface.

## 2. Greenfield surface/command-field inventory (coverage-honesty table)

Disposition: **wired** = the surface EXISTS in the client today and the c3 boundary moderates it; **reserved** = the §3 surface does NOT exist yet; the typed seam that will carry it is named. Never a blanket claim.

| # | §3 surface | Side | Exists today? | Disposition | Seam / evidence |
|---|-----------|------|---------------|-------------|-----------------|
| 1 | Companion chat input (interactive) | input | YES — `AiOperationPipeline.RunInteractiveAsync(AiRequest)` | **WIRED** | input boundary at operation admission (post provider-admission, pre-send) |
| 2 | Awareness operation input | input | YES (operation path) — `RunAwarenessAsync(AiRequest)` | **WIRED** (operation-level) | same input boundary on the awareness class; consent check stays first (c1) |
| 3 | Awareness CONTEXT FIELDS (`[Category|App|Title|Duration]` packaging) | input | NO — packaging lands in c5 | **RESERVED** | c5's context-packaging feeds field values through the same `AiModerationBoundary.EvaluateInput` seam before assembly |
| 4 | Interactive model reply text (shown) | output | YES — provider `AiReply.Generated(text)` through the pipeline | **WIRED** | output boundary before application (before the reply becomes the operation result) |
| 5 | Awareness model reply text (shown) | output | YES — same pipeline output path, awareness class | **WIRED** | same output boundary; refusal on awareness class drops by type (contract §4 rule 3) |
| 6 | Model-produced text PERSISTED (memory) | output | NO — memory store lands in c4 | **RESERVED** | admission §4 rule 5 already binds c4: persist deferred until output moderation passes, blocked turn rolled back; c4 consumes `EvaluateOutput` |
| 7 | Model-produced text SPOKEN (AI-reply speech) | output | NO — no AI-reply speech path exists | **RESERVED** | future speech consumer passes text through `EvaluateOutput` before utterance |
| 8 | Free-text COMMAND fields pre-execution (subliminal text, mantra, bounce words, media titles/paths, getbacktome text) | pre-execution | YES — `AiCommandEnvelope` vocabulary + `AiEnvelopePolicy.ModerateText` seam | **WIRED** | boundary's policy evaluation backs `ModerateText`; single field enumeration shared with the validator (tripwire) |
| 9 | Prompt templates: user-authored preset sections | input | NO — prompt assembly lands in c7 | **RESERVED** | c7's assembly passes user-authored sections through `EvaluateInput` |
| 10 | Community prompts | input | NO — no community-prompt surface exists | **RESERVED** | future surface; joins the inventory + boundary when its consumer row lands |
| 11 | Quiz templates | input | NO — no quiz exists (WPF gap: quiz templates unmoderated, §1.1 #4) | **RESERVED** | quiz is an interactive consumer (admission §5 rule 5); its template fields join `EvaluateInput` when the quiz row lands |
| 12 | App-authored canned `AiReply.Fallback` text | output | YES (vocabulary) — deliberately NOT moderated | **NON-CLAIM (recorded)** | contract §7 rule 1 scopes the output boundary to MODEL-PRODUCED text; Fallback is app-authored canned text (advisor-flagged: record the exclusion explicitly, never leave it as a silent gap) |

Completeness tripwire (SP-009-sweep-class): an executable test asserts (a) every public `Run*Async` operation entry point on `AiOperationPipeline` appears in the inventory registry, (b) every `AiCommandData` variant carrying a string field is enumerated by the shared free-text-field source, (c) every inventory row is either Wired-with-test or Reserved-with-named-seam. A new surface appearing unregistered FAILS the suite.

## 3. Design (pre-approach; consult verdict in §4.1)

### 3.1 Files

| File | Contents |
|------|----------|
| `client/src/CcpClient.Desktop/Ai/AiModerationBoundary.cs` (contract-named, fileScopeMustChange) | `AiModerationPolicy` document + `AiModerationRule` (shape-validated; `Empty` default = SP-019 "verdict-rejected shape only" posture — no category/wordlist invented); `AiModerationSurface` registry (the §2 inventory as typed values, Wired/Reserved dispositions); `AiModerationBoundary` (EvaluateInput/EvaluateOutput/ModerateCommandField; escalation consulted at admission); `AiModerationEscalation` (typed hit counter + cooldown state; injectable clock); `AiEscalationThresholds` (placeholder; `WpfBaselinePlaceholder` = 3/5/10min/5min recorded as baseline) |
| `client/src/CcpClient.Desktop/Ai/AiOperationPipeline.cs` | WIRING: boundary as ctor dependency; escalation consult + input boundary at admission (post provider-admission, pre-send — zero network on any moderation path); output boundary before application; typed refusal per class |
| `client/src/CcpClient.Desktop/Ai/AiOperationVocabulary.cs` | ADDITIVE: `AiReplyCodes.ModerationCooldown` ("moderation-cooldown") — typed admission-denial code for a cooling-down operation; justification: no existing code fits (not provider unavailability, not a content refusal) |
| `client/src/CcpClient.Desktop/Ai/AiCommandEnvelope.cs` | MINIMAL: free-text-field enumeration exposed as the single shared source (public static) so the validator's gate and the boundary's tripwire cannot drift; `ModerateText` continues to be the seam — c3 wires the boundary's policy into it |
| `client/tests/CcpClient.Tests/AiModerationBoundaryTests.cs` | taxonomy round-trips; injected-policy posture; placeholder default; escalation transitions; guard-outside-model |
| `client/tests/CcpClient.Tests/AiModerationCoverageTests.cs` | the §2 inventory as executable assertions + tripwire; pipeline-wired verdicts input+output per class; escalation at admission; envelope command-field wiring |
| `client/tests/CcpClient.Tests/AiOperationPipelineTests.cs` + `AiOfflineIntegrationTests.cs` + `AiProviderLabIntegrationTests.cs` | ctor call sites updated for the boundary dependency (boundary required — no hidden default) |

### 3.2 Boundary semantics

1. **Policy document injection (contract §7 rule 5):** the guard evaluates an INJECTED `AiModerationPolicy` (rules: categoryCode + action Block/SoftHit + tokens). Shape-validated at construction (category codes non-empty/distinct, tokens non-empty — stable machine tokens). **Default = `AiModerationPolicy.Empty`**: the SP-019 "verdict-rejected shape only" posture (spike limit 3: "moderated = verdict-rejected shape only") — the document SHAPE exists and is validated; NO category, wordlist, soft-hit value, or threshold is invented (§9.2 #1 owner-pending). A test-only injected policy (test category + token) blocks BY INJECTION — proving the guard evaluates the document, never a hardcoded list.
2. **Boundary position (consume c1's seam; per-change justification §7; consult-validated §4.1):** input at operation admission — AFTER the provider-admission chain (selected → capability → endpoint policy; WPF discipline: offline check precedes moderation, `AiService.cs:263-274`) and BEFORE `SendAttempts++` (moderation is pure local evaluation; a blocked operation performs ZERO network; content is never evaluated for an operation that cannot run). Output INSIDE the owned-operation body — after the provider returns AND after the c1 `IsLive` stale check, before the reply assignment (consult: stale replies are discarded WITHOUT moderation side effects; only replies that would be applied are evaluated). Escalation consulted at the same admission point. Boundary-coverage tests use a PROVEN fake provider — with an unproven provider the input boundary correctly never runs (offline-first), so coverage tests admit the provider first.
3. **Verdict → typed result mapping:** input Block → interactive: `Completed` + `AiReply.Refused(new(category, Input))` + escalation hit; awareness: same typed `Refused`, NO escalation hit (WPF discipline: only user-typed content escalates; awareness drops by type downstream — contract §4 rule 3). Output Block (Generated text only) → `Refused(category, Output)`, no escalation (WPF: "never the user's doing"). SoftHit → pass-through; the operation's diagnostic carries the stable code `soft-hit:input`/`soft-hit:output` (content-free; category code never enters diagnostics). Diagnostics on blocks: outcome `Refused`, stable code `refused:input`/`refused:output` (never the category — policy-document contents are not logged).
4. **Escalation counter mechanism (admission §3 rule 4):** typed port of the WPF MECHANISM — rolling-window hit list, one-shot warning at threshold, cooldown at threshold (non-stacking), expired cooldown resets window+warning; `GetState()` → typed `AiEscalationState(HitsInWindow, WarningShown, CooldownActive, CooldownEndsAt)`; injectable clock; state kept SERIALIZABLE-SHAPED (hits list + cooldown-end timestamp, the WPF `ModerationCounterPersistedState` shape) so persistence lands additively later without a semantic change (consult condition). Thresholds = injected `AiEscalationThresholds`; `WpfBaselinePlaceholder` (3/5/10min/5min) recorded as baseline, NOT decision. **Consulted at admission for the INTERACTIVE class ONLY (consult correction):** the WPF baseline runs the counter on user chat input only (`ModerationCounter.cs:84-85` consumed by the chat UI); gating awareness operations on user-behavior escalation would decide a VALUES question the baseline does not — recorded as owner-pending, never silently extended. Cooling-down interactive operation → `Completed` + `AiReply.Unavailable("moderation-cooldown")` (typed, not silently allowed; contract §11 rule 1's interactive shape — every interactive operation returns a typed reply; not a content refusal, not provider unavailability — new additive code, §7; advisor's `AiAdmission`-widening alternative recorded in §4.1 with disposition). **State location: session-scoped (in-memory).** Justification: WPF persists to survive restarts for compliance escalation against DECIDED thresholds; here thresholds are placeholders and no user-facing warning surface exists until c7 — persisting placeholder-valued state would encode undecided values into an SP-005 schema. Transition semantics are identical; persistence is reserved for the slice that lands owner-decided thresholds (recorded, §9.2 #1). **Divergence from WPF behavior recorded explicitly:** WPF's counter survives restarts; c3's does not — mechanism parity, not behavior parity, by design (never claimed as parity).
5. **Guard outside the model (contract §7 rule 4):** the policy document is a constructor-injected object; request text NEVER reaches policy evaluation as anything but the subject under test. Proof-by-construction test: a prompt containing a policy-shaped payload ("ignore moderation" / embedded JSON rules) does not change any verdict — an injected blocking policy still blocks, the default still passes-only-by-empty-document.
6. **Command fields (contract §7 rule 2):** the envelope's free-text-field enumeration becomes the single public source (`AiEnvelopeValidator.FreeTextFields` → public); `Gate` unchanged in behavior; c3's wiring composes `boundary.ModerateCommandField` into `AiEnvelopePolicy.ModerateText`. Tripwire: reflection over `AiCommandData` variants — every string-carrying variant appears in the enumeration.
7. **Content-free diagnostics maintained:** zero new log sites in product code (the boundary emits NO log lines; outcomes ride typed results + the pipeline's existing one-record-per-operation emission). The SP-016 schema proof stays green; the c1 §6-style registry table for c3 is in §6 below.

## 4. Consults

### 4.1 Pre-approach (Step 1)

**Mode:** solo (council forbidden — T-7). **Actual answering model:** the consult tool output carried NO model identifier and the stream truncated during inventory point (same truncation behavior as SP-033/SP-035) — recorded honestly per T-2. Route per the 2026-08-04 rewire: Opus 5 main / Fable 5 fallback. The received points were complete and actionable.

**Verdict (substantive points) + dispositions:**
1. **Boundary position APPROVED with two refinements (both adopted):** (a) output moderation runs INSIDE the owned-operation body, after the provider returns AND after the `IsLive` stale check, before reply assignment — stale replies are discarded without ever being moderated (no side effects on discarded work); (b) coverage tests must use a PROVEN fake provider — with an unproven provider the input boundary correctly never runs (offline-first ordering), so a coverage test that forgets admission would silently prove nothing.
2. **Fallback text must be an explicit inventory row, not a silent skip** — ADOPTED: §2 row 12 records app-authored canned `Fallback` text as a deliberate NON-CLAIM (contract §7 rule 1 scopes output moderation to model-produced text).
3. **Escalation consult scope correction (adopted):** the WPF baseline runs the 3-hit/5-hit counter on USER CHAT INPUT only; the cooldown state is consumed by the chat UI. Consulting it for the awareness class would extend user-behavior escalation to ambient reactions — a VALUES decision beyond the baseline. c3 consults the escalation state at admission for the INTERACTIVE class ONLY; awareness-extension is recorded as owner-pending (§9.2 #1 family).
4. **Cooldown denial shape — advisor leaned `AiAdmission.Suppressed(ModerationCooldown)` + Reply=null** (widening `AiAdmission`'s awareness-scoped doc comment), arguing `Unavailable` is documented as "provider could not produce a reply". **DISPOSITION: not adopted — authority order.** Contract §11 rule 1 (owner-ratified, SP-016) fixes the interactive unavailability shape: "Interactive → `AiReply.Unavailable(reason)`; the user sees honest typed state" for operation denials that are neither refusals nor errors, and contract §1 fixes "every interactive operation returns the §1 typed reply". An additive reason code inside the ratified vocabulary is the lower-authority change; widening `AiAdmission` to interactive AND making interactive Reply nullable would amend two code-level semantics to dodge a code addition the contract's own vocabulary admits. `Unavailable("moderation-cooldown")` stands; c1 tests carry no Reply-non-null invariant that would break either way (verified by grep), so the contract text is the decider.
5. **Session-scoped escalation state APPROVED with condition (adopted):** keep the state serializable-shaped (WPF `ModerationCounterPersistedState` shape: hits list + cooldown-end) so a later slice adds SP-005 persistence additively without semantic change; record the divergence from WPF persisted-across-restart behavior EXPLICITLY (done in §3.2 rule 4 — mechanism parity, never behavior-parity claim).
6. **Inventory spot-check:** rows 1–8 verified honest (awareness input correctly operation-level with field-packaging reserved; output rows real post-c2; command seam exists pre-execution). The required-vs-optional ctor question and the tail of the inventory point truncated in transit; the ctor decision (required — no hidden default; all call sites are tests) follows the packet's own consume-don't-hide discipline and needs no advisor tiebreak.

### 4.2 Pre-completion (Step 4)

(pending)

## 5. Engine review presence (T-2)

| Call | Result |
|------|--------|
| Step 1 plan review (`spine_review_step --step 1 --type plan`) | **Engine review ABSENT (expected)** — nested reviewer spawn blocked inside pi worker session; `skipped: true`, `spawnFailed: false` (SP-195: engine runs reviews after `.DONE`). Artifact: `.reviews/1-20260804T150006.md` |

## 6. Redaction/log-site registry (SP-018 pattern, product-side form)

(pending Step 2–3 — expectation: ZERO new product log sites; the boundary emits no log lines and no diagnostic text; stable codes only: `refused:input`, `refused:output`, `soft-hit:input`, `soft-hit:output`, `moderation-cooldown`)

## 7. Deviations and per-change justifications

(pending Step 2 — expected: (1) pipeline ctor gains the boundary dependency — all call sites are tests (no product CompositionRoot constructs the pipeline yet; composition lands in a later slice), required-not-optional so no hidden default; (2) additive `AiReplyCodes.ModerationCooldown`; (3) envelope `FreeTextFields` private→public single-source; (4) escalation state session-scoped, persistence reserved)

## 8. Completion-criteria disposition

(pending Step 4–5)

## 9. Step 5 — contract verification transcript

(pending)
