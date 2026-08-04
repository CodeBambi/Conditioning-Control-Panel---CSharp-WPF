# SP-042 record — AI companion slice c5: awareness (consent, cooldowns, context packaging, keyword routing)

**Task:** SP-042 · **Board row:** "Implement AI companion and awareness integration" (P0), slice c5 of `client/docs/ai-companion-admission.md` §8 · **Lane:** lane-1 · **Date:** 2026-08-04

Evidence class: **U** (Windows) + Windows title-observation session facts + named Linux limit. **WSL2 named limit (honesty framing (g), probed on this laptop by SP-035/038 precedent):** `wsl -l -q` → empty, exit 0 — WSL installed with ZERO distros; the admission's "WX session facts for window-title observation (X11 only)" is UNDISCHARGEABLE on this machine; recorded as owner-gated (provision a distro), NEVER faked. Windows title-observation facts are the session evidence. No Wayland claim anywhere.

---

## 1. WPF archaeology (READ-ONLY, `File.cs:line`; all lines re-verified 2026-08-04)

### 1.1 Consent pair (both default false)

- `Services/UI/WindowAwarenessService.cs:341-347`: `Start()` gates on `App.Settings?.Current?.AwarenessModeEnabled != true || AwarenessConsentGiven != true` → returns without starting ("feature disabled or no consent").
- `CCP.Core/Models/AppSettings.cs:2982,2993` (verified): `_awarenessModeEnabled = false`, `_awarenessConsentGiven = false`. XML docs: "Requires explicit consent. Privacy-focused: only categorizes, never logs titles" / "Must be true for awareness mode to function."

### 1.2 Reaction cooldown mechanism + the `?? 90` dead-code discrepancy

- `WindowAwarenessService.cs:375-388` (`CanReact()`/`CanStillOnReact()`): `var cooldownSeconds = _nextReactionCooldownSeconds > 0 ? _nextReactionCooldownSeconds : (App.Settings?.Current?.AwarenessReactionCooldownSeconds ?? 90);` — **the `?? 90` is DEAD CODE**: `AwarenessReactionCooldownSeconds` is a non-nullable `int` default **10**, setter-clamped 10–600 (`AppSettings.cs:3000-3008`, verified). **Owner question carried verbatim: 10 or 90?** (§9.2 #4.)
- `RollCooldownSeconds()` (`:397-412`): random in [base, max] when `AwarenessCooldownMaxSeconds > base`, else fixed base. `MarkReaction()`/`MarkStillOnReaction()` (`:421-433`) stamp the time and roll the next cooldown.
- Live-check boundary: `(DateTime.Now - _lastReactionTime).TotalSeconds >= cooldownSeconds` — **admitted at exact equality**.
- Still-on milestones: `StillOnMilestonesMinutes = { 1, 5, 10 }` (`:436-438`).

### 1.3 Keyword machinery (trigger/global/per-keyword/loop-protection + gates)

- **Global gate** (`KeywordTriggerService.cs:923,1108,1274`): `(now - _lastGlobalTriggerTime).TotalSeconds < settings.KeywordGlobalCooldownSeconds` → suppressed. Default 10s, clamp 1–300 (`AppSettings.cs:4294` region, verified `_keywordGlobalCooldownSeconds = 10`). XML doc: "hard ceiling on trigger frequency regardless of how many matches are on screen. Primarily prevents the OCR feedback loop."
- **Per-keyword hard cooldown + loop protection = ONE merged mute dict** (`RecordFire`, `:158-187`): `finalMs = Math.Max(loopMs if AwarenessLoopProtectionEnabled else 0, KeywordPerKeywordCooldownSeconds * 1000)`; write is **extend-not-shrink**: `if (!_mutedKeywords.TryGetValue(keyword, out var existing) || existing < expiresAt) _mutedKeywords[keyword] = expiresAt;` (`:178-181`). Defaults: per-keyword 15s clamp 1–600 (`:4314` region, verified `_keywordPerKeywordCooldownSeconds = 15`); loop protection enabled default true, 5000ms (`:4438,4450` region, verified).
- **Live check** (`IsKeywordMuted`, `:82-99`): `DateTime.UtcNow < expiresAt` → muted; expired entries pruned on access. Exact equality = expired = admitted.
- **Per-trigger cooldown** (`CCP.Core/Models/KeywordTrigger.cs:226,230`): `LastTriggeredAt` + `IsOnCooldown => (DateTime.Now - LastTriggeredAt).TotalSeconds < CooldownSeconds`; gate at scan (`KeywordTriggerService.cs:939,1116,1287` `if (trigger.IsOnCooldown) continue;`).

### 1.4 AvatarComment routing + the two admission corrections

- `DispatchAvatarComment` (`KeywordTriggerService.cs:1622-1673`): `RequireAiAvailable && !aiAvailable` → `PickCannedPhrase` → `ShowAvatarLine(canned, aiGenerated: false)` (`:1626-1632`); else **fire-and-forget `_ = Task.Run(...)` (`:1650`)** → `GetKeywordCommentAsync` → null/empty line → canned phrase → `ShowAvatarLine(line, aiGenerated: fromAi)` — **the badge reflects the actual source** (`:1652-1667`).
- Prompt shape (`Services/AiService.cs:196-206`): `IsAvailable` gate → `promptTemplate.Replace("{keyword}", keyword)` or default `"You just caught the user on the word '{keyword}'. React in character, one short line."`
- **Correction 1 (admission §5 rule 4):** the `Task.Run` becomes an SP-004 **owned awareness operation** (panic-cancellable) — c5 routes through `AiOperationPipeline.RunAwarenessAsync`, whose operations are owned on the pipeline's `AsyncOperationOwner`.
- **Correction 2:** the "AI unavailable → canned phrase unbadged" string channel becomes typed `AiReply.Fallback`/`Unavailable` visibility — the reply VARIANT is the badge authority (contract §1: badge derives from `Generated` and nothing else).
- **Removed keyword pre-check (H7)** (`KeywordTriggerService.cs:1638-1646`, comment verified): the upstream `CheckInput` was REMOVED because the service boundary already moderates at the HTTP edge; double-checking produced two log entries + two counter hits per keyword event. Greenfield: one uniform boundary at the operation edge (c3); c5 adds no second check.

### 1.5 Context packaging shape

- `Services/AiService.cs:160-163`: `[Category: {category} | App: {website} | Title: {tabName} | Duration: 0m]`; still-on variant `:182-188` formats duration `Xs`/`Xm`/`Xh`. Local-service packagers (`LocalAiService.cs:405-419`, per SP-038 record §1.1 #3) flow into the same input-moderated response path.

## 2. Design (pre-approach; consult verdict + dispositions in §4.1)

All product code in ONE new file `client/src/CcpClient.Desktop/Ai/AiAwarenessService.cs` (contract-named, fileScopeMustChange). New tests: `client/tests/CcpClient.Tests/AiAwarenessTests.cs` + `AiAwarenessCooldownTests.cs`.

1. **Typed consent (`AiAwarenessConsent`)** — `NotGiven` placeholder default (conservative: no awareness work until an owner-decided consent exists); record shaped so per-field/per-source granularity lands additively (§9.2 #4 owner-pending). **Additive pipeline overload** `RunAwarenessAsync(AiRequest, AiAwarenessConsent)` (consult §4.1 (a)): the typed state reaches the pipeline's admission point; the existing `bool` overload stays (4 out-of-scope test files call it; c4 additive/lane-disjointness precedent) and delegates. Residual bool door recorded honestly.
2. **`AiCooldownRegistry`** — 4 typed classes (`PerTrigger` keyed by triggerId, `Global`, `PerKeyword` keyed by keyword, `LoopProtection` keyed by keyword); injectable `Func<DateTimeOffset>` clock (c3 `AiModerationEscalation` precedent); extend-not-shrink per (kind, key): new expiry = `max(existing live expiry, now + duration)`; live iff `now < expiry` (admitted at exact equality, WPF boundary); expired pruned on access; one lock (escalation shape). **Equivalence note (consult §4.1 (c)):** WPF merges loop-protection and per-keyword into one mute entry with `Math.Max` of the two durations; the greenfield union-of-classes (suppressed if EITHER class is live) is behaviorally identical — recorded, never claimed as a new mechanism. Rolled randomization (`RollCooldownSeconds`, §1.2) deliberately NOT ported: nondeterminism in a test-critical mechanism; `AwarenessCooldownMaxSeconds` recorded as owner-pending value.
3. **`Suppressed(cooldown)` observable outcomes** — service-admission suppression → `AiOperationResult(Completed, null, AiAdmission.Suppressed(AiSuppressionKind.Cooldown))` + content-free diagnostic `suppressed:cooldown` (the kind rides the typed result; keyword NEVER enters diagnostics; `Generation = -1` = no operation began; `DurationMilliseconds = 0`). `SendAttempts` untouched (offline zero-network preserved).
4. **Context packaging** — `AiAwarenessContext(Category, App, Title, DurationText)`; `TryPackage` runs EVERY field through `boundary.EvaluateInput(field, AiModerationSurfaces.AwarenessContextFields)` BEFORE assembly; any `Block` → typed `AiReply.Refused` result, ZERO transmission (no pipeline call); assembled shape is the WPF `[Category: X | App: Y | Title: Z | Duration: N]` verbatim. Fields never into diagnostics/memory (sentinel-string proof test; pipeline memory-append is Interactive-only from c4).
5. **Keyword routing** — `RunKeywordCommentAsync(triggerId, keyword, promptTemplate?, fallbackText?)`: consent → cooldown gates (global, per-keyword, loop-protection, per-trigger — suppress if ANY live) → RecordFire (extend-not-shrink, stamped BEFORE dispatch per WPF `LastTriggeredAt = now` discipline) → prompt assembly (WPF `{keyword}` substitution / default line, §1.4) → pipeline owned awareness operation (panic-cancellable) → typed routing result `AiAwarenessRoutingResult`: `Visible(AiReply.Generated)` | `Visible(AiReply.Fallback(canned, code))` — **keyword path ONLY, and only for provider-Unavailable** (WPF canned-on-unavailable shape, typed per §2 rule 4; badge reflects true source because Fallback is never badged) | `Dropped(AiAwarenessDropKind)` for Refused (BY TYPE — the routing-layer drop c3 deferred; no refusal bubble), provider-Unavailable without canned, Cancelled, and suppressed admissions. **Consult corrections adopted (§4.1 (d)):** NO canned substitution on the window-reaction path (WPF has none there); refusal NEVER falls back to canned — recorded divergence: WPF showed canned on refusal because its string channel collapses refusal→null; greenfield distinguishes by type deliberately. Caller-supplied `fallbackText` inherits c3's app-authored-canned moderation non-claim ONLY if genuinely app-authored — recorded at the seam (WPF sources phrases from `CompanionPhrases`/mods, `:1676-1685`; a mod-phrase consumer would NOT inherit the non-claim).
6. **Window-reaction operation** — `RunReactionAsync(AiAwarenessContext)`: consent → reaction cooldown (`PerTrigger`-class entry keyed by a fixed reaction slot — the WPF `CanReact`/`MarkReaction` mechanism; still-on shares the cooldown mechanism with milestone scheduling recorded as values, §1.2) → packaging (rule 4) → pipeline owned operation → routing result (Unavailable/Refused → Dropped; Generated → Visible).
7. **Title-observation capability** — `"ai.awareness.window-title"` registered via SP-006 probe discipline. Windows probe: `GetForegroundWindow` + `GetWindowTextW` P/Invoke → `Available` with CONTENT-FREE detail (title LENGTH only, never the title); `hwnd == 0` → honest `Unavailable` transient ("no foreground window" — WPF `:596-604` discipline: lock screen/secure desktop). Linux probe → typed `Unavailable` (WSL zero-distro named limit; X11 session facts owner-gated; NO Wayland claim). Capture consumed only under consent + capability Available; titles never logged, never diagnostics, never memory.

## 3. File-scope constraint resolutions

- **No pipeline signature change:** 4 out-of-scope test files call `RunAwarenessAsync(request, awarenessConsent: bool)`. Resolution: additive overload (design rule 1); bool overload preserved.
- **Inventory row stays `Reserved` (consult §4.1 (b)):** `AiModerationCoverageTests.cs` hardcodes 5-Wired/6-Reserved counts + one switch arm per Wired row and is OUT of file scope; flipping `awareness-context-fields` to Wired would red the suite on immutable tests. The row stays Reserved; its `ReservedFor` text is updated IN-SCOPE (string only — no disposition/count change) to state that c5 landed the packaging wiring and the flip is deferred by file scope. Packaging moderation is proven DIRECTLY by new `AiAwareness*` tests against the `awareness-context-fields` surface ID. **Discovery for orchestrator follow-up (mechanical):** flip disposition → Wired; update counts 6/5; add a switch arm asserting blocking-policy refusal on a context field. (SP-038's own recorded limitation: nothing executable flips Reserved→Wired.)

## 4. Consults

### 4.1 Pre-approach (Step 1)

**Mode:** solo (council forbidden — T-7). Route per the 2026-08-04 rewire: Opus 5 main / Fable 5 fallback. **Actual answering model:** the consult tool output carried NO model identifier — recorded honestly per T-2 (same as SP-033/035/038). The received points were complete and actionable.

**Verdict (substantive points) + dispositions:**
1. **(a) Consent seam — leaning adopted:** callers can bypass the service and call the pipeline with a fabricated bool; make the typed consent the admission vocabulary by adding an ADDITIVE typed pipeline overload `RunAwarenessAsync(AiRequest, AiAwarenessConsent)`; keep the bool overload for the out-of-scope tests and RECORD the residual bool door. ADOPTED (design rule 1).
2. **(b) Inventory row — adopted:** a wired surface labeled Reserved is a live false statement in the honesty inventory, but flipping breaks immutable out-of-scope tests. Keep Reserved; update the `ReservedFor` TEXT in-scope to say c5 landed the wiring and the flip is deferred; record the exact mechanical follow-up (counts 6/5, new assertion arm) as a discovery. ADOPTED (§3).
3. **(c) Cooldown classes — equivalence verified:** WPF collapses loop-protection and per-keyword into ONE mute entry with `Math.Max(durations)`; the four-class union (suppressed if either is live) is behaviorally identical. Extend-not-shrink must hold per (kind, key) — `expiry = max(existing live expiry, now + duration)`; live iff `now < expiry` (admitted at equality). Do NOT port the rolled randomization into a test-critical mechanism; record `AwarenessCooldownMaxSeconds` as an owner-pending value. ADOPTED (design rule 2).
4. **(d) Routing/drop semantics — two corrections, both adopted:** (i) canned-fallback substitution exists in WPF ONLY on the keyword AvatarComment path — window reactions return null with no canned; scope `Visible(Fallback)` to the keyword path only. (ii) WPF shows canned on REFUSAL too (its string channel collapses refusal→null); greenfield deliberately distinguishes by type and DROPS refusals (contract §4 rule 3) — record the divergence explicitly. (iii) caller-supplied fallback text inherits c3's app-authored-canned moderation non-claim ONLY when genuinely app-authored; WPF sources phrases from `CompanionPhrases`/mods — a mod-phrase consumer would not inherit the non-claim; record at the seam. ADOPTED (design rule 5).

### 4.2 Pre-completion (Step 4)

**Mode:** solo. Route per the 2026-08-04 rewire: Opus 5 main / Fable 5 fallback. **Actual answering model:** the consult tool output carried NO model identifier — recorded honestly per T-2 (same as §4.1 and SP-033/035/038). The received points were complete and actionable.

**Verdict (substantive points) + dispositions:**
1. **Consent TOCTOU resolves fail-safe:** the service re-reads `Consent` when delegating to the pipeline, so a mid-flight revocation is caught by the pipeline's own check (suppressed, never run). Cooldown stamps burned on a revoked-mid-flight fire match WPF's stamp-before-dispatch discipline. RECORDED (no code change).
2. **Reaction path deliberately does NOT consult the keyword Global class** — matches WPF's two separate cooldown families (WindowAwarenessService reaction cooldown vs KeywordTriggerService global gate). A reader could assume "global" applies to all awareness; RECORDED explicitly in §7 item 9.
3. **PerTrigger is stamped only when the caller passes `perTriggerCooldown`** — WPF per-trigger cooldown is per-trigger config (KeywordTrigger.CooldownSeconds), so a caller without one stamps nothing; the class is still CHECKED (a previously stamped entry suppresses). Semantics RECORDED in §7 item 9.
4. **Double moderation is safe here (H7 analysis):** context fields are evaluated at packaging AND the assembled prompt again at the pipeline input boundary — but the H7 failure (two log entries + two counter hits per event) does NOT recur: awareness input blocks never escalate (c3, WPF discipline) and the boundary emits no log lines. RECORDED in §7 item 10.
5. **Packaging separator injection is a formatting shape, not a boundary:** field values containing `|`/`]` can deform the assembled shape; WPF has no escaping either, and the guard sits outside the model. RECORDED as a non-claim in §7 item 10.
6. **Contract §4 rule 3 vs canned Fallback reconciliation (adopted wording):** the MODEL's Unavailable outcome drops by type; what surfaces is the APP-AUTHORED canned Fallback (admission §5 rule 4's typed replacement of the WPF canned shape) — never the model's outcome bubbling up. RECORDED in §7 item 3's companion note (§2 design rule 5 already carries it).
7. **Badge claim is the TYPED half only:** the headed badge-accuracy proof is c7 (admission §8 acceptance-mapping). §8 table adjusted to say so explicitly.
8. **Title length in the probe Detail is metadata, never content**; capability Detail strings sit outside the §12 diagnostic-record schema. RECORDED in §6.
9. **Title-probe test precondition named (adopted):** the Windows arm requires an interactive desktop session; a comment in the test names it so a future locked-session CI failure is diagnosable, not mysterious. ADOPTED (test comment added).

## 5. Engine review presence (T-2)

| Call | Result |
|------|--------|
| Step 1 plan review (`spine_review_step --step 1 --type plan`) | **Engine review ABSENT (expected)** — nested reviewer spawn blocked inside pi worker session; `skipped: true`, `spawnFailed: false` (SP-195: engine runs reviews after `.DONE`). Artifact: `.reviews/1-20260804T181514.md` |
| Step 2 plan review (`spine_review_step --step 2 --type plan`) | **Engine review ABSENT (expected)** — same SP-195 skip; `spawnFailed: false`. Artifact: `.reviews/2-20260804T182546.md` |
| Step 3 plan review (`spine_review_step --step 3 --type plan`) | **Engine review ABSENT (expected)** — same SP-195 skip; `spawnFailed: false`. Artifact: `.reviews/3-20260804T183322.md` |

## 6. Title-observation session facts (Windows) + redaction registry

**Windows session facts (2026-08-04, this evidence box):** `AiWindowTitleCapability.Probe` ran on the Windows desktop session and returned `CapabilityState.Available` with the content-free detail `"windows foreground window title observation confirmed (title length N; content never logged)"` — a REAL foreground-window title was captured via `GetForegroundWindow` + `GetWindowTextW` (executable proof: `TitleProbe_PlatformTypedState_WindowsAvailable_LinuxUnavailable` asserts Available and that the detail does NOT contain the captured title; `TitleObservation_GatedByConsentAndCapability_TitleNeverLogged` asserts the observed title appears in no diagnostic record). The Linux arm of the same test encodes the typed-Unavailable expectation (`title-observation-linux-unprobed`) for when a distro exists; it is inert here per the WSL zero-distro named limit (header). **No Wayland claim anywhere.**

**Redaction/log-site registry (SP-018 pattern):** c5 adds **ZERO new product log sites**. `AiAwarenessService` / `AiCooldownRegistry` / `AiAwarenessContextPackaging` / `AiWindowTitleCapability` emit no log lines; all outcomes ride typed results + content-free `AiDiagnosticRecord` emissions. New stable diagnostic codes (all content-free tokens — never keywords, titles, fields, or categories):

| Code | Where | Meaning |
|------|-------|---------|
| `suppressed:cooldown` | service admission diagnostic `StableCode` | a cooldown suppressed the operation (the class rides the typed result, never the diagnostic) |
| `suppressed:consent-denied` | service admission diagnostic `StableCode` (mirrors the pipeline's c1 code) | consent not given |
| `refused:input` | service packaging diagnostic `StableCode` (reuses the c3 side-code convention) | a context field blocked at packaging — zero transmission |
| `keyword-fallback` | `AiReply.Fallback.Code` | app-authored canned text surfaced on provider-Unavailable (keyword path only) |

Capability reason codes (additive, defined in `AiAwarenessService.cs` — `Capabilities/` untouched): `title-observation-linux-unprobed`, `no-foreground-window`. Probe Available detail carries title LENGTH only — metadata, never content; capability Detail strings sit outside the §12 diagnostic-record schema (pre-completion consult §4.2 point 8).

Executable content-freedom proofs: `Packaging_BlockingPolicyOnAnyField_ZeroTransmission_TypedDrop` (forbidden token + category code absent from all records), `CooldownSuppressed_KeywordComment_TypedOutcome_ZeroNetwork` (keyword absent), `KeywordRouting_Refused_DropsByType_NeverCanned` (keyword absent), `Packaging_Fields_NeverEnterDiagnosticsOrMemory` (sentinel title absent from diagnostics; memory Appends == 0), `TitleObservation_GatedByConsentAndCapability_TitleNeverLogged` (title absent; zero records). The SP-016 schema-level proof stays green (full suite).

## 7. Deviations and per-change justifications

1. **Additive pipeline overload `RunAwarenessAsync(AiRequest, AiAwarenessConsent)`** (`AiOperationPipeline.cs`, +13 lines): the typed consent state reaches the pipeline's admission point (pre-approach consult §4.1 (a)); the bool overload is preserved because 4 out-of-scope test files call it (c4 additive/lane-disjointness precedent). **Residual bool door recorded:** a caller CAN still invoke the pipeline directly with a fabricated bool; the typed overload is the admitted vocabulary going forward, and c7's UI composition consumes the typed path. No existing test call sites changed (zero edits to out-of-scope files — verified by `git diff --stat`).
2. **Inventory row `awareness-context-fields` stays `Reserved` with updated text** (`AiModerationBoundary.cs`, string-only edit): flipping to Wired would red two assertions in `AiModerationCoverageTests.cs` (hardcoded 5/6 counts + per-Wired switch arm), which is outside SP-042 file scope (FR-WORK-06). The wiring is proven directly by `AiAwarenessTests` against the surface ID. **Orchestrator follow-up (mechanical):** flip disposition → Wired, counts 6/5, add a switch arm asserting a blocking-policy refusal on a context field. (SP-038 record §2's own known limitation: nothing executable flips Reserved→Wired.)
3. **Drop-on-refusal NEVER falls back to canned** (recorded divergence from WPF behavior, pre-approach consult §4.1 (d)): WPF's `DispatchAvatarComment` shows a canned phrase on moderation refusal because its string channel collapses refusal → null (`KeywordTriggerService.cs:1661-1664`); the typed pipeline distinguishes by type and contract §4 rule 3 drops refusal/unavailable by type. Mechanism parity is NOT behavior parity here — deliberate, recorded.
4. **Canned fallback scoped to the keyword path only** (consult §4.1 (d)): WPF has no canned fallback on window reactions; `RunReactionAsync` passes `fallbackText: null` so Unavailable → `Dropped(ProviderUnavailable)`.
5. **Rolled cooldown randomization NOT ported** (`RollCooldownSeconds`, `WindowAwarenessService.cs:397-412`): nondeterminism has no place in a test-critical mechanism; `AwarenessCooldownMaxSeconds` is recorded as an owner-pending value alongside the other §9.2 #4 values.
6. **Service methods take no per-call CancellationToken:** cancellation is generation-based (panic/provider switch) per contract §2 — the owned operation carries the token; admission-time tokens are not part of the pipeline's shape either. (Also removes 17 xUnit1051 analyzer warnings — the 0W gate.)
7. **`Generation = -1` on service-admission diagnostics** (consent/cooldown/packaging refusals): no operation began; honest marker, content-free.
8. **HeadlessTests untouched** — c5 is mechanism + capability probes with no UI surface; recorded honestly absent (fileScope allows "likely none").
9. **Cooldown family separation + per-trigger stamping (pre-completion consult §4.2 points 2-3):** the window-reaction path consults ONLY the reaction cooldown (PerTrigger class, fixed slot) — NOT the keyword Global class; WPF's reaction (`CanReact`) and keyword (`_lastGlobalTriggerTime`) cooldowns are separate families and stay separate. `PerTrigger` is stamped only when the caller supplies `perTriggerCooldown` (WPF per-trigger cooldown is per-trigger config); the class is always CHECKED.
10. **Double moderation + packaging shape (pre-completion consult §4.2 points 4-5):** fields are evaluated at packaging and the assembled prompt again at the pipeline input boundary; the H7 double-accounting failure does not recur (awareness never escalates; the boundary emits no logs). Packaging is a formatting shape with no escaping (WPF identical) — a `|` in a title deforms the shape, never a boundary bypass; recorded non-claim.

## 8. Completion-criteria disposition

| Criterion | Disposition |
|-----------|-------------|
| Code-enforced consent at admission (placeholder default NOT GIVEN; baseline both-false FACTS cited) | **MET** — typed `AiAwarenessConsent` (default `NotGiven`); enforced at the service AND the pipeline (typed overload); `Consent_Default_IsNotGiven_AndNoOperationRuns` proves keyword/reaction/title-observation all refuse with typed Suppressed + zero network; WPF both-false facts cited (§1.1) |
| Typed cooldown machinery: 4 classes, extend-not-shrink, observable `Suppressed(cooldown)` (placeholder values; baselines recorded incl. 10-vs-90) | **MET** — `AiCooldownRegistry` (PerTrigger/Global/PerKeyword/LoopProtection), extend-not-shrink + expiry + boundary-equality tests; `AiCooldownValues.WpfBaselinePlaceholder` pinned by test with the 10-vs-90 owner question verbatim in code + this record (§1.2) |
| Context packaging under consent, every field through the c3 boundary (blocking = zero transmission); fields never in diagnostics/memory | **MET** — `AiAwarenessContextPackaging.TryPackage`; blocking-any-field theory proves zero transmission (SendAttempts 0, provider Calls 0); sentinel-title proof for diagnostics + memory isolation |
| Keyword routing as owned operations (panic-cancellable); typed Fallback/Unavailable visibility; drop-by-type | **MET — TYPED half** — pipeline-owned operations (`KeywordRouting_IsAnOwnedOperation_PanicCancellable`); `AiReply.Fallback` typed visibility (the badge derives from the reply variant by type — plumbing only; the HEADED badge-accuracy proof is c7 per admission §8 acceptance-mapping); `Dropped(RefusedByModeration/ProviderUnavailable/Cancelled)` at the routing layer (the c3 deferral discharged); the model's Unavailable drops by type — what surfaces is the app-authored canned Fallback (§4.2 point 6) |
| Title-observation capability: Windows session facts + Linux typed Unavailable (WSL named limit; no Wayland claim) | **MET (Windows; Linux = named limit)** — §6 session facts; Linux arm typed-Unavailable, owner-gated, never faked |
| Contract green (≥537/29 floor); both solo consults persisted with actual answering models | **MET (Windows; Linux = named WSL limit)** — §9 (564/564 + 29/29); consults in §4.1/§4.2 — the tool carried NO model identifier on either call (T-2 honesty note, same as SP-033/035/038) |

## 9. Step 5 — contract verification transcript

(filled in Step 5)

## 10. Budgets, surprises, durable-lesson candidates

**Budget:** single session, well inside the 4h packet budget; no context-limit exits.

**Surprises:**
1. **The pre-approach consult caught two real misreadings of WPF behavior before they shipped:** (a) I had generalized the canned-fallback to all awareness paths — WPF has it ONLY on the keyword AvatarComment path; (b) WPF shows canned on REFUSAL too (string channel collapses refusal → null) — the greenfield drop-on-refusal is a deliberate divergence that needed explicit recording, not an accidental one.
2. **Two self-inflicted edit breaks caught immediately by build:** a botched pipeline edit dropped a method brace and mangled the PanicAsync doc comment (fixed in the same step; final diff verified cleanly additive, +13 lines).
3. **xUnit1051 analyzer flagged per-call CancellationToken params on the new service methods (17 warnings)** — resolved by REMOVING the params: cancellation is generation-based (contract §2), so the params were false affordances. The 0W gate enforced a BETTER design.
4. **`ArgumentException.ThrowIfNullOrEmpty` throws `ArgumentException`, not `ArgumentNullException`, for empty strings** — test asserted the wrong subtype; fixed to `ThrowsAny<ArgumentException>`.

**Durable-lesson candidates (orchestrator reconciles into port-lessons.md — enabler 2):**
1. **WPF string channels collapse distinct outcomes — porting routing means mapping the COLLAPSE, not the code.** The `GetKeywordCommentAsync` null return conflates refusal, unavailability, and empty reply; each needs its typed disposition decided explicitly, and one of them (canned-on-refusal) is a behavior the typed contract deliberately changes. (Class: WPF-archaeology fidelity.)
2. **Cooldown class unions ≡ merged-max-dict.** When WPF merges two cooldown layers into one dictionary with Math.Max durations, typed separate classes with union suppression are behaviorally identical — record the equivalence and port the extend-not-shrink write verbatim (`existing < expiresAt` gate), not the storage shape. (Class: mechanism porting.)
3. **Per-call CancellationTokens on generation-cancelled services are false affordances.** If cancellation is generation/owner-based (SP-004), per-call tokens mislead callers about what cancellation means — and the analyzer (xUnit1051) will tax every test call site. (Class: API design + analyzer discipline.)
4. **Reserved→Wired inventory flips need file-scope room.** A coverage tripwire with hardcoded counts blocks the discharging slice from flipping its own row when the tripwire test sits outside the packet's file scope; packets that discharge a Reserved surface should name the coverage test in File Scope explicitly. (Class: packet authoring — SP-038's known limitation, now with a concrete collision.)
