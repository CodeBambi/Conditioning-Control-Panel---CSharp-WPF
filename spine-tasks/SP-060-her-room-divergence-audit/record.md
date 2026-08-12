# SP-060 record — Her Room + Awareness divergence audit (zero product code)

**Task:** spine-tasks/SP-060-her-room-divergence-audit · **Review Level:** 2 · **Shape:** zero-product-code audit (SP-050 shape)
**Engine-review presence (T-2):** recorded per `spine_review_step` call below.

**Ground-truth discipline:** upstream citations are against the READ-ONLY `ConditioningControlPanel/**` tree in this worktree (wave-17 lane-2; the tree carries the merged v6.7 upstream). Port citations are against `client/src/**` + `client/docs/ai-operation-contract.md` + the c1–c7 slice records. Enumeration was from the TREE (PROMPT amendment: the sync ledger has been incomplete three times), executed by three read-only archaeology agents (Companion services+Brain; Awareness services+asset; Companion views) and one read-only port-inventory agent; load-bearing claims the verdicts rest on were re-verified by the worker against the tree (cooldown burn semantics `AiAwarenessService.cs:440,472`; dashboard surface `MainWindow.axaml:67`; contract/admission docs read in full by the worker).

---

## Engine-review log (T-2)

| Step | Type | Result | Artifact |
|------|------|--------|----------|
| 1 | plan | **SKIPPED BY DESIGN** (nested reviewer spawn blocked in worker session; `skipped=true, spawnFailed=false` — engine runs reviews after `.DONE`, SP-195) | `.reviews/1-20260812T122035.md` |
| 2 | plan | SKIPPED BY DESIGN (same) | `.reviews/2-20260812T122245.md` |
| 3 | plan | SKIPPED BY DESIGN (same) | `.reviews/3-20260812T122955.md` |
| 4 | plan | SKIPPED BY DESIGN (same) | `.reviews/4-20260812T123502.md` |

---

## Step 1 — upstream enumeration from the tree + pre-approach consult

### Enumeration method

Three parallel read-only `wpf-archaeologist` agents, each with a mandatory-element brief, sliced reads, and a cite-or-UNKNOWN rule:

1. **Companion services + Brain** (`Services/Companion/**` incl. `Brain/`): brain turn pipeline, ChatSession windowing, CompanionSessionStore, PromptAssembler, MemoryStore, MemorySignalWriter, ReactionArbiter+cooldown ledger, AwarenessRouting/Speech, BarkService, Mute Voice Lines, community prompts, personality presets, CompanionService XP, supporting pieces. Full element inventory with `File.cs:line` in agent output; distilled into the audit doc.
2. **Awareness services + asset** (`Services/Awareness/**`, 24 files + `awareness_apps.json`): observer, probes, policy, privacy rules, pause, ledger, live seam, worthiness, cooldowns, intensity+migration, SMTC, projection, prompt builder, reaction service, text hygiene, candidates, consent flow. Full privacy-defaults table in agent output.
3. **Companion views** (`Views/Controls/Companion/**`): Her Room layout zones Z0–Z8, privacy dial copy (en.json-cited), app picker, pause/wipe/forget flows, engine room, permissions grid, workshop cells, transcript window.

### Headline enumeration findings (each expanded with citations in the audit doc)

- **H1 — The "three privacy levels" are NOT an enum.** No `PrivacyLevel` type exists anywhere in `ConditioningControlPanel` (grep-verified by the agent). The shipped model is emergent from four booleans + two lists: `AwarenessModeEnabled` (default false, `AppSettings.cs:3788`), `AwarenessConsentGiven` (false, `:3799-3807`), `AwarenessConsentShownV2` (false, `:3946`), `UseAwarenessV2` (true, `:3835`); `AwarenessTitleAllowList` ships EMPTY (`:3889-3902`) so "app names only" is the effective default; the UI dial (`AwarenessPrivacyView` + `AwarenessDialCopy.cs:33-38`) maps Off→disable / AppNames→enable+clear-title-list / PageTitles→enable+open-picker (`AwarenessPrivacyRuntimeVm.cs:104-129, 350-357`). The task-board row's "plain-words privacy dial" phrasing is presentation over this emergent model.
- **H2 — `awareness_apps.json` is a DEAD asset.** `AwarenessAppListLoader.GetCategoryApps` has zero callers repo-wide (agent grep); live classification goes through `WindowAwarenessService.CategorizeWindow` (`AwarenessObserverPolicy.cs:384`). The file's own `_comment` says it "mirrors the former embedded dictionaries". **Port guidance: do not port this file as live config.**
- **H3 — Incognito is a hard drop, not a setting.** Case-insensitive multilingual title markers (`AwarenessPrivacyRules.cs:86-95, 204-224`), dropped before any classification (`AwarenessObserverPolicy.cs:319-327`); blank title drops fail-closed (`:277-279`); UI copy: "private and incognito windows are dropped before anything is counted. that one is not a setting." (en.json:4344).
- **H4 — Seeded deny groups** `@passwords` / `@banking` / `@email-titles` apply even before seeding (`AwarenessPrivacyRules.cs:115-120, 459-475`) and are written into the user's list once at consent (`:429-447`).
- **H5 — Pause is exactly 1 hour, process-lifetime only** (`AwarenessPause.cs:9-15, 30`); while paused nothing is observed, counted, or said.
- **H6 — Cloud/local projection split.** Cloud wire: banded/bucketed values only, adult cluster collapses to cluster id, SMTC titles never leave the machine (`AwarenessProjection.cs:10-45, 178-186`); full titles/track/artist only to machine-local Ollama (`:220-239`), licensed by provider check (`AwarenessReactionService.cs:285-292`).
- **H7 — Cooldowns burn on DELIVERY, never attempt** upstream (`ReactionCooldownLedger.cs:119-130`; `WorthinessScorer.cs:226-243`). **The port burns on ATTEMPT** (`AiAwarenessService.cs:440,472` — "RecordFire stamped BEFORE dispatch (WPF `LastTriggeredAt = now` discipline)"), a faithful port of the OLD WPF discipline that upstream v6.7 itself abandoned. Worker-verified against the tree. This is a genuine upstream-drift divergence, not a decided port divergence.
- **H8 — Upstream defaults that matter:** `AwarenessIntensity` default Chatty (`AppSettings.cs:3856`; 0/2/6/12 lines/hr, `AwarenessIntensity.cs:39-54`); reaction cooldown floors 60s global / 90s LLM-to-LLM / 10min per-app / hourly budget (`ReactionCooldownLedger.cs:35-46`); ledger retention 30 days clamp 7–90 (`AppSettings.cs:3905-3914`; `ActivityLedger.cs:107`); `CompanionVoiceLinesMuted` default false (`AppSettings.cs:3736`); `ChatMemoryEnabled` default true (`CompanionPromptSettings.cs:120`); SMTC watcher has NO on/off setting — runs with the observer (`AwarenessObserver.cs:178, 263-264`).
- **H9 — Legacy v1 vs v2:** with `UseAwarenessV2=false` the legacy `WindowAwarenessService` path reportedly sends page titles to cloud (`AwarenessConsentDialog.xaml.cs:60-64`); the v2 kill switch defaults true. The port has no v1 path — the divergence must never be introduced.
- **H10 — The port's dashboard has no tab surface.** Companion opens from a button (`MainWindow.axaml:67`, handler `MainWindow.axaml.cs:83`) into the c7 modeless `CompanionWindow` (W-04 shape). Every "Her Room layout" element is therefore dependency-blocked on an unfiled dashboard-tabs row (named in SP-050's blocked inventory). Worker-verified.

### Privacy-relevant surface (named explicitly, per Step 1 checkbox)

Three privacy levels (emergent, H1), incognito hard-drop (H3), one-hour pause (H5), app-picker scoping (`AwarenessAppCandidates` 40-cap, `:25,36-55`; picker at `AwarenessPrivacyRuntimeVm.cs:624-645`), SMTC media observation (H8, no toggle), Mute Voice Lines (`AppSettings.cs:3736-3747`; enforcement `AvatarTubeWindow.Speech.cs:485-501`), plus the enumeration-surfaced extras: seeded deny groups (H4), activity ledger retention (H8), input-idle/typing-burst probe (`AwarenessProbes.cs:164-289` — no key content, `:155-163`), mic-in-use meeting detection (`AwarenessProbes.cs:299-381`), and the cloud/local projection split (H6). Defaults as facts in the audit doc's defaults table.

### Pre-approach consult (Step 1 gate)

**Mode:** solo (T-7; PROMPT Do-NOT: council errors on the stale synthesizer seat). **Requested route:** Opus 5 main, Fable 5 fallback (pause protocol). **Actual answering model:** NOT surfaced by the consult tool response (no model-identity header — recorded honestly, same provenance discipline as SP-018…SP-050). **Two calls:** the first verdict **TRUNCATED** mid-answer (the SP-027 truncation class) after delivering complete guidance on rubric ordering + items (a)/(b); a second narrow call completed item (c). Truncation recorded, never silently stitched.

**Verdict (both calls, all adopted):**

1. **BLOCKED-ON-OWNER is a hard filter evaluated FIRST, not a verdict of last resort.** Per binding framing (a), consent defaults / retention / moderation policy / product identity are BLOCKED-ON-OWNER no matter how technically easy.
2. **(a) `ChatMemoryEnabled` upstream-true vs port-Denied → BLOCKED-ON-OWNER, not KEEP.** The port's Denied is a *placeholder explicitly pending owner* (admission §9.2 #3), NOT a decided divergence. The KEEP-unless-new-evidence rule covers *decided* divergences; placeholders-pending go to the owner list with the status quo preserved.
3. **(b) Boundary-broadening awareness elements (SMTC, activity ledger retention, app-name observation, input/mic probes) → BLOCKED-ON-OWNER each.** BUT the audit must actively hunt the **subtractive** elements — they are the strong ADOPT candidates: incognito hard-drop, deny-list mechanism, title scrubbing (emails/≥6-digit runs/length caps), pause, the "what she can see" wire view. Narrowing a boundary is not broadening one.
4. **Seeding defaults into user settings is a consent-default touch** → mechanism ADOPT, seed contents/defaults BLOCKED-ON-OWNER (split the row).
5. **(c) The Her Room layout is NOT one row — split per section.** Discriminator: does the section merely expose landed behavior (ADOPT/MERGE), or encode a decision (identity, tone, consent, retention, entitlement → BLOCKED-ON-OWNER)? Identity rows: naming/metaphor, relationship constellation (also retention-backed), spice switch, attention gauge (entitlement/upsell + product voice). Transparency rows (wire view, dial copy, incognito line, pause): ADOPT. Permissions grid / Engine Room: affordance ADOPT, values/custom-endpoint BLOCKED-ON-OWNER. Memory diary: retention model BLOCKED-ON-OWNER, but the two-step inline arm/confirm forget pattern is its own MERGE row against the port's modal.
6. **Guardrail (verified, H10):** check for a dashboard tab surface before sizing layout rows — there is none; sizing names the dashboard-tabs dependency instead of inventing S/M/L.
7. **Guardrail 2:** Companion XP/leveling belongs to the Trainer Card/gamification subsystem — cross-row pointer, never silently absorbed.
8. **No fifth verdict value** (packet pins four); dependency-blocked work carries the dependency in the sizing column (SP-050's lesson honored in shape, not vocabulary).

**Rubric corrections applied to the table:** placeholder-pending ≠ decided (item 2); subtractive-element class added to ADOPT criteria (item 3); row-splitting rule for mixed mechanism+value rows (items 4-5).

---

## Step 2 — port-side inventory (what c1–c7 actually landed)

**Method:** one read-only Explore agent over `client/src/CcpClient.Desktop/**` + `Features/**` + the four slice records (SP-042/044/046/047), briefed to cite contract section + `File.cs:line` per element and to list deliberate divergences with their decision citations. The worker read `client/docs/ai-operation-contract.md` and `client/docs/ai-companion-admission.md` in full directly (157 + 123 lines — the port side of every comparison) and spot-verified load-bearing claims against the tree.

### Landed surface (per element — full citations in the audit doc's table)

- **Typed replies** (contract §1): `AiReply.Generated/Refused/Unavailable/Fallback` (`AiOperationVocabulary.cs:208,219,230,241`).
- **Pipeline**: SP-004 owned operations; provider switch = generation invalidation + cancel + stale-discard; panic = same machinery; offline = zero network with send-attempt-counter proof; endpoint classes `Loopback/FirstPartyCloud/RemoteHostOllama/ThirdPartyCloud` with a loopback-only admission placeholder (pre-socket rejection).
- **Memory** (contract §5, admission §4): `AiMemoryStore` on SP-005 machinery; consent enum `AiMemoryConsent {Denied, Granted}` default **Denied** (`AiMemoryStore.cs:68`); pair cap 50 recorded as `WpfBaselinePlaceholder` (`:59`); chat pairs only — no facts store; explicit clear with file-delete proof (c4) + user-reachable control (c7); SP-047 memory→prompt wiring: consent-gated `ReadPromptContext()`, interactive always-overwrites `AiRequest.History`, awareness strips it.
- **Awareness** (contract §4, admission §5): consent record default **NotGiven** (`AiAwarenessService.cs:32,36`), code-enforced at admission; cooldown registry 4 classes (`PerTrigger/Global/PerKeyword/LoopProtection`), extend-not-shrink, values 10/10/15/5s all `WpfBaselinePlaceholder` (`:78`); typed `Suppressed(cooldown)`; context packaging `[Category|App|Title|Duration]` (`:172`) with every field through the moderation input boundary; **window-title observation Windows-only** via P/Invoke (`:312-336`), Linux typed Unavailable; **burn-on-attempt** (`:440,472` — H7 divergence); **no awareness consumer wired** — services exist, no UI integration, no app picker/pause/dial/ledger/SMTC/scorer/arbiter.
- **Moderation** (contract §7): boundary wired for chat input, awareness context fields, command free-text fields, output text; reserved for templates/community/quiz (no such surfaces exist); placeholder verdict-rejected-only policy; values owner-pending (§9.2 #1).
- **Commands** (contract §8/§9, c6): strict envelope, atomic rejection, per-command verdicts; gates post-validation; master + all per-effect defaults **OFF** (deliberate conservative placeholder, `c6 record §2.1`); `NotExecuted(EffectUnavailable)` typed placeholder (no effect backends exist).
- **UI** (c7): modeless `CompanionWindow` (button-launched, `MainWindow.axaml:67` — no tab surface, H10); chat bubbles with type-driven badge honesty; refusal bubbles; Stop = panic; awareness + memory consent checkboxes (session-scoped); 4 cooldown value boxes; clear-memory with in-window confirm modal; honesty line naming the placeholder posture.

### Defaults and named limits as facts

Full table in the audit doc. The load-bearing ones: memory consent **Denied** (placeholder, admission §9.2 #3); awareness consent **NotGiven** (placeholder, §9.2 #4); cooldowns 10/10/15/5 WPF-baseline placeholders; the recorded **10s-vs-dead-`?? 90`** discrepancy (`WindowAwarenessService.cs:374-388`, admission §5 rule 3 — owner question, still open); SP-047's WPF-true-vs-placeholder tension (upstream `ChatMemoryEnabled` default true vs port Denied — placeholder-pending, NOT decided).

### Deliberate divergences already decided (KEEP unless new evidence; audit must not silently reopen)

| Divergence | Decision citation |
|---|---|
| Provider switch = generation invalidation (rejects WPF live-switch) | contract §3 rules 1–4; admission §2 rules 1–2 |
| Refusal/unavailable awareness outcomes drop BY TYPE (no canned refusal bubble) | contract §4 rule 3; c5 record §2 design rule 5 |
| Panic cancellation via SP-004 machinery (rejects fire-and-forget) | contract §2 rule 3; admission §7 |
| Strict envelope, atomic rejection, zero repair | contract §8 (greenfield-decision, SP-016 record) |
| Handler exceptions propagate (rejects swallow-and-log) | c6 record §2.1 |
| Command gates default all-OFF | c6 record §2.1 + admission §8 — recorded as the **conservative pending-owner posture**, deliberate-not-silent BUT self-declared placeholder: goes to the owner list as a question per consult item 2, while the mechanism stays KEEP |
| Memory pairs-only model (system/enrichment/ambient never persisted) | admission §4 rule 1 |

**New-evidence check:** H7 (burn-on-attempt vs upstream's v6.7 burn-on-delivery) is NOT one of these — the port's discipline was ported from old WPF (`LastTriggeredAt = now`) and upstream itself abandoned it; the table treats it as upstream drift → MERGE candidate, no boundary touched.

---

## Step 3 — divergence table + privacy verdicts

The table lives in `client/docs/her-room-divergence-audit.md` §3 (groups A awareness observation & privacy / B reaction pipeline / C brain & memory / D Her Room UI / E asset row). 37 element rows + 1 asset row; every row carries upstream `File.cs:line`, port counterpart or explicit "none", user-observable difference, one of the four pinned verdicts, a data-boundary line (O/R/T — "none" stated explicitly where true), and a reason. Verdict distribution: ADOPT 14 (incl. all the subtractive/transparency rows), KEEP 11 (incl. decided divergences + refuted-migration + dead-asset rows), MERGE 8, BLOCKED-ON-OWNER 17 (some rows carry split verdicts, e.g. mechanism A / values B — counts are per row-half where split). The owner decision list (audit §4) is 12 plain questions with options and consequences.

**Boundary-line discipline:** every row states newly observed/retained/transmitted data. Rows A1, A7, A12-A14, B10 carry new-observation or new-retention lines and are B regardless of technical ease (hard filter, framing (b)). Rows A3/A4/A6/A8/A10/A11/A16/D12 state "none" or "narrows" explicitly — the subtractive class the pre-approach consult told the audit to hunt.

---

## Step 4 — sizing verdicts, audit doc, pre-completion consult

**Audit doc:** `client/docs/her-room-divergence-audit.md` — self-contained: verdict vocabulary, upstream enumeration headlines U1-U10, port landed-state summary, the 38-row table (§3A-3E), the 12-question owner list (§4), sizing verdicts with named dependencies (§5), UNKNOWN rows (§6), durable-lesson candidates (§7).

**Sizing:** every ADOPT/MERGE row carries S/M/L (or unsizable-with-dependency, SP-050 shape), evidence class (U/WH/WX/LAB), named dependencies, and an honest limit shape. Two standing dependencies dominate: the unfiled **dashboard-tabs row** (all Her Room UI rows — the port's companion is a button-launched modeless window, `MainWindow.axaml:67`) and **owner question O2** (app identity) for everything reading the observation pipeline.

### Pre-completion consult (Step 4 gate)

**Mode:** solo (same route discipline as Step 1). **Requested route:** Opus 5 main, Fable 5 fallback. **Actual answering model:** NOT surfaced by the consult tool (same provenance discipline as Step 1). **One call; the verdict TRUNCATED** near the end (SP-027 class, second occurrence this task) — but every correction arrived before the cut, and the truncated tail was mid-scan of already-covered ADOPT rows. Corrections delivered and ALL applied to the audit doc:

1. **A4/A8 internal-consistency fix (applied):** the per-app title allow-list and the deny-list's app-id/display-name match arms require an app identifier; the port does not observe process names (A1 is BLOCKED). The rows now carry the explicit constraint: implementable ONLY against the caller-supplied `App` field (`AiAwarenessService.cs:172`) — implementing against an observed process name would widen the boundary A1 is blocked on. The §5 sizing row for A4 lost its "filter is independent" claim.
2. **B4 hazard — the consult's strongest finding (applied):** burn-on-delivery is NOT a one-line flip. The port's burn-before-dispatch doubles as its anti-retry-storm gate (`AiAwarenessService.cs:472` "a failed operation still gates"); upstream tolerates delivery-only burn because its arbiter single-flight + 8s timeout + bark fallback + worthiness pacing suppress retry storms — machinery the port lacks. The table and sizing now name the hazard and require failure-class backoff (or retained attempt-gating on failure) in any packet that flips the burn point, plus a failing-provider storm test.
3. **Dependency-blocked ADOPT rows marked inline (applied):** verdict cells now read `A (dep-blocked: O2)` / `(dep-blocked: dashboard-tabs)` / `(dep-blocked: audio/bark row)` so the verdict column cannot be misread in isolation (A9, D2, D4, D8, D9, D12, C13, A16).
4. **E1 residual risk recorded (applied):** the zero-callers claim rests on a repo-wide grep; a reflection/DI invocation would not show — recorded, not resolved.
5. **Owner-question wording discipline (applied):** Q12's "the audit recommends No" became "the constitution's standing posture is no broadening absent a decree — only the owner can lift it" (the audit poses questions; it does not recommend on the capture boundary). Q5's "the audit recommends adopting" became "the audit's row A4 marks that narrowing adoptable on technical grounds ... the decision stays yours".
6. **Consistency confirmed (no change):** memory-default B and effect-gate-values B are the same posture (placeholder-pending ≠ decided); the consult explicitly re-verified this pairing.

**Post-correction self-check:** verdict counts unchanged (17 B / 14 A / 11 K / 8 M; split rows counted per half); every correction narrowed claims, none widened a boundary.

### Intended board filings (orchestrator writes them at land — ENABLER 2)

- The "Her Room + Awareness reconcile" row: evidence pointer to `client/docs/her-room-divergence-audit.md`; row stays OPEN pending the owner's §4 answers (12 questions).
- No new rows filed by this audit — every element that would become work is either (a) owner-blocked (awaits the decree, then rows/packets), or (b) dependency-blocked behind rows already named on the board (dashboard-tabs in SP-050's blocked inventory; audio/bark per SP-015 limit 5). The orchestrator may file one "apply the subtractive awareness adopts" row (A3/A4/A6/A8-mech/A10/A11 — six S-sized rows, one packet shape) since those need no owner answer; the audit deliberately does not set that row's state.

### Durable-lesson candidates (for the contract owner to consider at land)

Audit §7: (1) marketing model vs code model — enumerate the tree; (2) shipped assets can be dead (`awareness_apps.json`); (3) upstream drifted toward burn-on-delivery after the port snapshotted — not every delta is a port gap, but flips carry machinery hazards (B4); (4) the strongest adopts are subtractive.

---

## Step 5 — testing & verification

- `node .spine/patches/verify.mjs` — OK, all patches applied on all roots.
- `dotnet build client/CcpClient.sln -c Debug` — 0 errors.
- `dotnet test client/tests/CcpClient.Tests` (TRX attached) — **Passed: 862, Failed: 0, Total: 862** — EXACTLY the floor (zero product change confirmed by count).
- `dotnet test client/tests/CcpClient.HeadlessTests` (TRX attached) — **Passed: 33, Failed: 0, Total: 33** — EXACTLY the floor.
- `git diff --check` — clean. `git status --short` — clean (all File Scope paths committed; lane changes touch only `spine-tasks/SP-060-her-room-divergence-audit/**` and `client/docs/her-room-divergence-audit.md`).

## Completion-criteria cross-check

- Every enumerated upstream element has a sourced verdict with a port-side counterpart or explicit "no counterpart" — audit §3, 38 rows, zero UNKNOWN verdicts (4 UNKNOWN citation rows, §6, none verdict-load-bearing).
- Every row states newly observed/retained/transmitted data — the boundary column, "none" stated explicitly.
- Single owner decision list — audit §4, 12 plain questions with options and consequences.
- Sizing verdicts for every ADOPT/MERGE row — audit §5 with evidence classes, named dependencies, honest limit shapes.
- `client/docs/her-room-divergence-audit.md` stands alone; `ai-operation-contract.md` untouched (also task-board/port-lessons/upstream-sync untouched — ENABLER 2).
- Zero product change (862/33 exact); both solo consults persisted above with actual-model provenance honestly recorded as not-surfaced.
