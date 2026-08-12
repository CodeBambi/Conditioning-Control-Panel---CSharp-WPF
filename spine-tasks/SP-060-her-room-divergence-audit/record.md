# SP-060 record — Her Room + Awareness divergence audit (zero product code)

**Task:** spine-tasks/SP-060-her-room-divergence-audit · **Review Level:** 2 · **Shape:** zero-product-code audit (SP-050 shape)
**Engine-review presence (T-2):** recorded per `spine_review_step` call below.

**Ground-truth discipline:** upstream citations are against the READ-ONLY `ConditioningControlPanel/**` tree in this worktree (wave-17 lane-2; the tree carries the merged v6.7 upstream). Port citations are against `client/src/**` + `client/docs/ai-operation-contract.md` + the c1–c7 slice records. Enumeration was from the TREE (PROMPT amendment: the sync ledger has been incomplete three times), executed by three read-only archaeology agents (Companion services+Brain; Awareness services+asset; Companion views) and one read-only port-inventory agent; load-bearing claims the verdicts rest on were re-verified by the worker against the tree (cooldown burn semantics `AiAwarenessService.cs:440,472`; dashboard surface `MainWindow.axaml:67`; contract/admission docs read in full by the worker).

---

## Engine-review log (T-2)

| Step | Type | Result | Artifact |
|------|------|--------|----------|
| 1 | plan | **SKIPPED BY DESIGN** (nested reviewer spawn blocked in worker session; `skipped=true, spawnFailed=false` — engine runs reviews after `.DONE`, SP-195) | `.reviews/1-20260812T122035.md` |
| 2 | plan | SKIPPED BY DESIGN (same class, this session) | `.reviews/2-20260812T122245.md` |

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
