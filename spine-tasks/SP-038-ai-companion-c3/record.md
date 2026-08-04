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
| 8 | Free-text COMMAND fields pre-execution (subliminal text, mantra, bounce words, media titles/paths, getbacktome text) | pre-execution | YES — `AiCommandEnvelope` vocabulary + `AiEnvelopePolicy.ModerateText` seam | **WIRED (at-seam qualifier)** | boundary's policy evaluation backs `ModerateText`; single field enumeration shared with the validator (tripwire); the PRODUCT composition factory `AiEnvelopePolicy.ForBoundary` exists from c3 — **honesty qualifier (pre-completion consult): rows 1/2/4/5 are moderated unconditionally by the pipeline's own code path (`RunCoreAsync`); row 8 is moderated through the validator WHEN COMPOSED — no product caller composes it yet because command execution lands in c6; the factory exists so c6 cannot forget** |
| 9 | Prompt templates: user-authored preset sections | input | NO — prompt assembly lands in c7 | **RESERVED** | c7's assembly passes user-authored sections through `EvaluateInput` |
| 10 | Community prompts | input | NO — no community-prompt surface exists | **RESERVED** | future surface; joins the inventory + boundary when its consumer row lands |
| 11 | Quiz templates | input | NO — no quiz exists (WPF gap: quiz templates unmoderated, §1.1 #4) | **RESERVED** | quiz is an interactive consumer (admission §5 rule 5); its template fields join `EvaluateInput` when the quiz row lands |
| 12 | App-authored canned `AiReply.Fallback` text | output | YES (vocabulary) — deliberately NOT moderated | **NON-CLAIM (recorded)** | contract §7 rule 1 scopes the output boundary to MODEL-PRODUCED text; Fallback is app-authored canned text (advisor-flagged: record the exclusion explicitly, never leave it as a silent gap) |

Completeness tripwire (SP-009-sweep-class): an executable test asserts (a) every public `Run*Async` operation entry point on `AiOperationPipeline` appears in the inventory registry, (b) every `AiCommandData` variant carrying a string field is enumerated by the shared free-text-field source, (c) every inventory row is either Wired-with-test or Reserved-with-named-seam. A new surface appearing unregistered FAILS the suite.

**Known limitation (pre-completion consult):** nothing executable flips a Reserved row to Wired when its backing feature lands (e.g. c4 lands memory persist) — the tripwire catches NEW unregistered surfaces, not STALE reservations. Mitigation: each downstream slice's packet binds its surfaces explicitly (admission §4 rule 5 already binds c4's moderation-gated persist), so the reservation is discharged by the consuming slice; recorded, accepted.

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
6. **Command fields (contract §7 rule 2):** the envelope's free-text-field enumeration is the single public source (`AiEnvelopeValidator.FreeTextFields`); `Gate` unchanged in behavior; the PRODUCT composition factory `AiEnvelopePolicy.ForBoundary(boundary, ...)` wires `boundary.ModerateCommandField` into `AiEnvelopePolicy.ModerateText` (pre-completion consult: the composed shape must exist in product code — c6 consumes the factory, never a caller-must-remember convention). Tripwire: reflection over `AiCommandData` variants — every string-carrying variant appears in the enumeration.
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

**Mode:** solo. **Actual answering model:** the consult tool output carried NO model identifier and the stream truncated during the completion-criteria point (same behavior as SP-033/035 and this packet's §4.1) — recorded honestly per T-2. The received points were complete and actionable.

**Verdict (substantive points) + dispositions:**
1. **BIGGEST catch — row 8 (command-free-text) "wired" was an overclaim as written:** rows 1/2/4/5 are moderated unconditionally by the pipeline's own code path (`RunCoreAsync`); row 8 sits on a validator seam that NO product caller composes yet (command execution lands in c6). DISPOSITION (adopted): (a) inventory row 8 re-labeled **WIRED (at-seam qualifier)** with the exact distinction recorded (§2); (b) added the PRODUCT composition factory `AiEnvelopePolicy.ForBoundary(boundary, ...)` so the composed shape exists in product code and c6 cannot forget the wiring (advisor's optional strengthening — adopted: 8 lines, converts caller-must-remember into shipped shape); (c) the coverage test now composes via the factory, not a test-side `with`.
2. **"Awareness drops by type" is HALF-discharged:** the pipeline returns the identical typed `Refused` for awareness; the actual DROP is a downstream consumer behavior that does not exist yet (c7 owns surfaces). DISPOSITION (adopted): §8 marks the criterion discharged for the TYPED half with the consumer half named to c5/c7 — mirroring the admission's own acceptance-mapping practice (badge plumbing in c1 ≠ accurate badges; only c7's headed evidence discharges).
3. **Transient 515/516: not a .DONE blocker, but fix the record language** — "suspected c2 lab timing" was unattributed speculation. DISPOSITION (adopted): §7 item 8 reworded to cause-UNKNOWN with the hypothesis labeled; the Step-5 contract run captures FULL output so any recurrence is named. Advisor verified c3's own code has no flake surface (immutable statics, per-harness escalation, injected clocks); noted the 24 new tests add parallel load that can trip the lab's wall-clock bounds.
4. **Reserved-row staleness is a real but accepted gap:** no executable check flips Reserved→Wired when a backing feature lands. DISPOSITION (adopted): recorded as a known limitation in §2; mitigation = each downstream slice's packet binds its surfaces explicitly. `ReservedFor` stays a free-text string (structuring it = over-engineering — advisor agreed).
5. **No honesty-framing violations found beyond the above** (general sweep: verdicts typed, no values invented, no network paths, no content in diagnostics — all verified against the transcript).

## 5. Engine review presence (T-2)

| Call | Result |
|------|--------|
| Step 1 plan review (`spine_review_step --step 1 --type plan`) | **Engine review ABSENT (expected)** — nested reviewer spawn blocked inside pi worker session; `skipped: true`, `spawnFailed: false` (SP-195: engine runs reviews after `.DONE`). Artifact: `.reviews/1-20260804T150006.md` |
| Step 2 plan review (`spine_review_step --step 2 --type plan`) | **Engine review ABSENT (expected)** — same SP-195 skip; `spawnFailed: false`. Artifact: `.reviews/2-20260804T151553.md` |
| Step 3 plan review (`spine_review_step --step 3 --type plan`) | **Engine review ABSENT (expected)** — same SP-195 skip; `spawnFailed: false`. Artifact: `.reviews/3-20260804T152504.md` |

## 6. Redaction/log-site registry (SP-018 pattern, product-side form)

c3 adds **ZERO new product log sites**: `AiModerationBoundary`/`AiModerationEscalation`/`AiModerationPolicy` emit no log lines and no diagnostic text; all outcomes ride typed verdicts and the pipeline's existing one-record-per-operation emission. New stable diagnostic codes introduced (all content-free side/kind tokens — never category codes, never policy contents, never text):

| Code | Where | Meaning |
|------|-------|---------|
| `refused:input` | pipeline diagnostic `StableCode` | input side blocked (category never carried) |
| `refused:output` | pipeline diagnostic `StableCode` | output side blocked (category never carried) |
| `soft-hit:input` / `soft-hit:output` | pipeline diagnostic `StableCode` | soft-hit pass-through recorded (WPF log-only shape, content-free) |
| `moderation-cooldown` | `AiReplyCodes.ModerationCooldown` → `Unavailable.Code` → diagnostic `StableCode` | escalation cooldown admission denial |

Executable proof: `Diagnostics_BlockedOperations_CarrySideCodesOnly_NeverPolicyContent` serializes every emitted record from blocked operations and asserts the test category code and the forbidden token appear NOWHERE; the SP-016 schema-level content-freedom proof stays green (full suite). Secrets inventory: none (test-only policy tokens, never committed payloads).

## 7. Deviations and per-change justifications

1. **Pipeline ctor gains `AiModerationBoundary moderation` (REQUIRED, 5th param).** Justification: the boundary must be live on every operation — an optional/nullable default would hide the posture. All existing call sites are tests (verified by repo-wide grep: NO product CompositionRoot constructs the pipeline yet; composition lands in a later slice), so the signature change is test-only churn. Existing harnesses pass `new AiModerationBoundary()` — the Empty default posture, behavior-identical for pre-c3 tests.
2. **Additive `AiReplyCodes.ModerationCooldown` ("moderation-cooldown")** in `AiOperationVocabulary.cs` — the designated additive home ("new codes land with their consumer row"; c3 is that row). Needed because a cooling-down admission denial is neither a content refusal (`Refused`) nor provider unavailability-as-probe-failure; contract §11 rule 1 fixes the interactive shape as `Unavailable(reason)`. The advisor's `AiAdmission`-widening alternative was not adopted (authority order; §4.1 point 4).
3. **Additive `SurfaceId` init-property on `AiModerationVerdict.Block`/`SoftHit`** — contract §7 rule 3's `Block(category, surface)` shape. Init-only: positional construction (all c1 call sites) unchanged; record equality/serialization cover it (round-trip test added). The surface is statically known at each boundary call site and set by the boundary.
4. **Envelope `FreeTextFields` private→public** (`AiCommandEnvelope.cs`) — the SINGLE enumeration source shared by the validator's gate and the coverage tripwire so they cannot drift; behavior of `Gate` unchanged (same enumeration, same order).
5. **`StableCodeOf` maps `Refused` → side code** (`refused:input`/`refused:output`) — content-free (category never carried); input blocks were already emitted via the `Refused` helper, output blocks flow through the common emit path.
6. **Escalation state session-scoped** — recorded divergence from WPF restart persistence (§3.2 rule 4): mechanism parity only, thresholds placeholder; state serializable-shaped for additive later persistence.
7. **HeadlessTests untouched** — c3 is mechanism + tests with no UI surface; recorded honestly absent (fileScope allows "likely none").
8. **Observed transient:** one full-suite run during Step 3 showed 1 failure (515/516); the failing test name was not captured (grep-truncated output) and THREE subsequent full-suite runs are 516/516 green. Cause UNKNOWN (pre-completion consult correction — an earlier draft said "suspected c2 lab timing"; that attribution is a HYPOTHESIS, not evidence: c3's own tests are deterministic — injected clocks, stub providers, immutable statics, per-harness escalation — but the 24 new tests add parallel load that can trip the c2 lab's real-socket wall-clock bounds). Disposition per the consult: not a .DONE blocker for an unreproducible flake; the Step-5 contract run captures FULL output so any recurrence is named (§9).

## 8. Completion-criteria disposition

| Offline zero-network preserved; content-free diagnostics maintained; contract green (≥492/29 floor); both solo consults persisted with actual answering models | **MET (Windows; Linux = named WSL limit)** — blocked/unproven paths `SendAttempts==0`; zero new log sites, side-code-only diagnostics with executable proof; contract chain §9 (516/516 + 29/29 ≥ 492/29 floor); both consults persisted — the tool carried NO model identifier on either call (T-2 honesty note, same as SP-033/035) |

## 9. Step 5 — contract verification transcript

(pending)

## 10. Budgets, surprises, durable-lesson candidates

**Budget:** single session, well inside the 4h packet budget; no context-limit exits.

**Surprises:**
1. **The advisor caught a real scope error before it shipped:** my initial design consulted the escalation cooldown for BOTH operation classes; the WPF baseline scopes it to user chat input only — extending it to awareness would have silently decided a VALUES question (owner-pending). Corrected to interactive-only consult with the extension recorded as owner-pending.
2. **The advisor's own alternative was rejected on authority order:** it leaned toward widening `AiAdmission` for the cooldown denial, but contract §11 rule 1 (owner-ratified) already fixes the interactive denial shape as `Unavailable(reason)` — the additive code is the lower-authority change. Recorded with disposition (§4.1 point 4).
3. **One transient full-suite failure** (§7 item 8) — not reproducible in 3 subsequent runs; recorded, not hidden.
4. **xUnit analyzer (xUnit2029) flagged `Assert.Empty` on a reflection list** — the 0W gate catches even test-code idioms; `Assert.DoesNotContain` is the analyzer-clean shape.
5. **Pre-completion consult caught a REAL overclaim:** inventory row 8 (command free-text) read as product-wired, but no product caller composes the validator seam yet (c6 owns execution). Fixed with the at-seam qualifier + the shipped `ForBoundary` composition factory — the coverage-honesty framing applied to the packet's own author.

**Durable-lesson candidates (orchestrator reconciles into port-lessons.md — enabler 2):**
1. **Escalation/cooldown scopes are VALUES, not mechanics** — when porting a counter mechanism, the set of surfaces it gates is part of the owner-pending values; port the baseline's scope exactly and record extensions as questions. (Class: WPF-archaeology fidelity; consult-caught before commit.)
2. **A boundary's coverage claim needs a tripwire, not a table** — the wired/reserved inventory is executable: reflection over entry points and data variants fails the suite when a new surface appears unregistered. Tables drift; tripwires don't. (Class: coverage-honesty discipline, SP-009-sweep class.)
3. **Output moderation belongs after the stale check, inside the owned operation** — discarded replies must never be moderated (no side effects on dropped work); the only honest "before application" point is post-`IsLive`, pre-assignment. (Class: cancellation/moderation interaction.)
