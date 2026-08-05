# SP-046 — AI companion slice c7: companion UI surface (record)

**Task:** SP-046 · **Board row:** "Implement AI companion and awareness integration" (P0), slice c7 of `client/docs/ai-companion-admission.md` §8 · **Lane:** lane-1 · **Date:** 2026-08-05

Evidence classes: **U** + **WH-class via avalonia-live** (the verified 27-tool seat: screenshots + semantic trees + synthetic input + binding-error capture — the DISPLAY3-class substitute on this box) + K3 visual review at land (orchestrator). **WSL2 named limit stands** (zero distros — WX render/session facts owner-gated, never faked). No Wayland claims.

---

## 1. WPF archaeology (READ-ONLY, `File.cs:line`)

### 1.1 Badge mechanism (provenance-driven)

- Two badges share the speech bubble's top-left corner slot (`AvatarTubeWindow.xaml:139-174`): `AiBadge` — pink, white 9pt "AI" (`en.json:3675`); `PolicyBadge` — amber #FFC107 "POLICY" (`en.json:3697`). Mutually exclusive.
- Visibility set per bubble in `ShowGiggle`: `AiBadge.Visible iff aiGenerated` (`AvatarTubeWindow.Speech.cs:426-436`). `aiGenerated=true` ONLY for genuine LLM replies; canned/fallback phrases pass false (`Speech.cs:351-354`; `ChatInput.cs:709-712`) — the badge is provenance, never styling.
- Refusal path swaps them post-render: POLICY shown, AI collapsed (`Speech.cs:390-394`). Listening indicator forces both off (`Speech.cs:716-722`).
- **Contract:** badge derives from reply provenance BY TYPE and nothing else (`ai-operation-contract.md` §1: `AiReply.Generated(text, provenance)`); Fallback/Unavailable/Refused are never badged. The WPF bool is superseded by the typed variants (admission §2 rule 4).

### 1.2 Chat input behavior

- Send via Enter (`ChatInput.cs:652-656`) or send button (`:666-669`); Escape closes the panel (`:659-663`). Empty/whitespace no-ops (`:674-675`).
- On send: textbox cleared and panel closed BEFORE the AI call (`:688-689`); user text enters history only after a non-refusal result (`:729-730`).
- In-flight affordance: thinking-dots bubble (`StartThinkingAnimation`, `:705-707`); NO cancel in WPF (fire-and-forget — REJECTED shape, contract §2 rule 3).
- Moderation cooldown: input + send disabled at 0.5 opacity with a 1s countdown timer (`MainWindow.xaml.cs:393-455`).

### 1.3 Memory-clear UI flow (`MainWindow/MainWindow.Patreon.cs:912-953`)

- `MessageBox.Show` Yes/No, Question icon, **default NO**, title "Reset Companion Memory", "can't be undone" body (`:920-927`). On Yes: `AiServiceStrategy.ClearLocalHistory()` + on-screen chat-log clear (`:933-936`) → Information OK box (`:940-945`). On exception: Warning OK box with the error (`:947-955`). Lives on the Companion tab ("Reset Companion Memory" button).
- The OPERATION landed in c4 (`AiMemoryStore.Clear()` — in-memory emptied + document deleted, typed `AiMemoryClearOutcome`); c7 lands the user-reachable CONTROL with this confirm shape. c4 consult note (SP-040 record §3.2 record-only 4): `Clear()` blocks on the write chain — the UI must hop off the UI thread.

### 1.4 Awareness consent + cooldown settings surfaces

- `ChkAwarenessMode` checkbox: toggling sets `AwarenessModeEnabled` AND auto-sets `AwarenessConsentGiven` (`Patreon.cs:1100-1129`; reflects `Enabled && ConsentGiven` at `:829`). Both default false (`AppSettings.cs:2982,2993`).
- Sliders `SliderAwarenessGlobalCooldown` / `SliderAwarenessSameWordCooldown` with "{v}s" labels (`MainWindow.Awareness.cs:62-73`) drive `KeywordGlobalCooldownSeconds` (default 10, clamp 1-300) / `KeywordPerKeywordCooldownSeconds` (default 15, clamp 1-600); UI clamps 1-180 (`AppSettings.cs:4294-4323`).
- Values are owner-pending placeholders (admission §9.2 #4); WPF values recorded as baseline FACTS, never decisions.

### 1.5 Refusal presentation (interactive vs awareness)

- Interactive: `ShowModerationRefusalBubble(source)` (`ChatInput.cs:719-723`; `Speech.cs:366-396`) — localized refusal text, POLICY badge, no sound; prohibited user input never enters history (`ChatInput.cs:698-701`).
- Awareness: never surfaces refusals — `ShowModerationRefusalBubble` has exactly ONE caller; the awareness path falls back to preset phrases badged only when genuinely AI (`Reactions.cs:76-104`). Greenfield: awareness drops refusal/unavailable BY TYPE (contract §4 rule 3; landed c5 machinery).

## 2. Design (post-consult; verdicts in §3.1)

### 2.1 Placement decision (RECORDED with evidence)

**Owned modeless `CompanionWindow`** opened from a dashboard button — NOT embedded in the 520x680 dashboard and NOT through `FeaturePopupManager` (the SP-013 manager is a one-popup demonstrator lifecycle; the companion is a distinct per-window surface with its own close semantics). Evidence: chat is tall, scrollable content; the dashboard is the SP-007/SP-013 demonstrator surface (embedding forces its redesign — larger diff, more risk); the W-04 window-contract machinery (owned, modeless, `ShowInTaskbar=false`, focus restoration) is landed precedent (`FeaturePopupManager.cs`, manifest row W-04; WPF parity `MainWindow.Presets.cs:846-873`). Visual grammar: the evolved dark-neon dashboard grammar (`#FF141018` background, `#FFE066FF` accent, `#FF3A2F3E` borders, card/pill shapes from `MainWindow.axaml`/`FeaturePopupWindow.axaml`) — designed, never WPF-cloned (owner decree 2026-08-04: behavior parity is the constraint, visuals evolve).

### 2.2 Composition wiring (per-file justifications)

- **`Features/Companion/CompanionParticipant.cs` (NEW):** `IBackgroundParticipant` (the `DtrhParticipant` precedent — a participant owns feature machinery; construction starts nothing, SP-003). Owns: `AiOperationPipeline` (real), `AiMemoryStore` (own named owner `"AiMemory"` — the SP-024 lesson), `AiAwarenessService`, `AiCommandExecutor` (`AiExecutionGates.NoneAdmitted` posture), a `CollectingAiDiagnosticsSink`, and the mutable session-state holders (`AiMemoryConsent` default Denied, awareness consent default NotGiven, cooldown values = `AiCooldownValues.WpfBaselinePlaceholder`). The store's consent `Func` reads the participant's mutable holder — runtime-typed-state driving without persistence schema (§2.4).
- **`Lifecycle/CompositionRoot.cs`:** constructs the chain in `DefaultParticipants` (participant added AFTER the demo store — phase-3 start order), registers the `LoopbackOllamaProvider` (probe registered → CapabilityProbes phase proves it), registers the cloud INVENTORY descriptor (typed absence; credentials-absent), **selects LocalOllama as the default selection** (decision recorded §2.3), and adds the memory store to the pre-drain flush closure (SP-005 contract §11).
- **`Program.cs` / `App.axaml.cs`:** ONE new product flag `--ai-ollama-host <uri>` (config seam — consult §3.1 #4: the host is already a `LoopbackOllamaProviderOptions` field; the classifier rejects non-loopback pre-socket, so the flag can NEVER widen the network boundary) + the dashboard button opens the companion window. No harness server ships in product code.
- **No new packages, no new settings schema, no DI.**

### 2.3 Default provider selection (decision, consult §3.1 #2)

Default selection = **LocalOllama**: the only provider whose endpoint class is admissible under the loopback-only placeholder policy; selection ≠ availability (contract §3 rule 3) — without a reachable Ollama the probe yields typed Unavailable and the status surface says so honestly. **Disclosed new startup behavior:** every launch now performs ONE bounded loopback probe (`GET 127.0.0.1:11434/api/version`, ≤2s, cancellable) in the CapabilityProbes phase — SP-006 capability machinery, zero external network, no outbound AI traffic (offline = zero network stands; a probe of the selected backend is the SP-006 mechanism, not AI traffic). Cloud stays inventory (never selected; credentials absent — admission §2 rule 6).

### 2.4 Runtime-only typed settings (no persistence decision)

Awareness consent, memory consent, and cooldown values are SESSION-scoped typed states — every default is an owner-pending placeholder (admission §9.2 #3/#4), so no persistence schema is decided here (the SP-040 `RetentionMaxPairs`-null discipline: a persisted value reads as a decision). The surfaces read/write the live typed states: awareness consent → `AiAwarenessService.Consent` (landed setter); memory consent → the participant's holder (read by the store's consent `Func` at write admission); cooldown values → `AiAwarenessService.Values` (**ONE additive change**: `_values` readonly → settable property with null-check — surface glue, no behavior change when untouched; extend-not-shrink stands, a live cooldown can never be shortened by a value edit — registry `Extend` max-semantics, tested).

### 2.5 Lab instrument (headed evidence, zero product contamination)

The headed lab is a **task-local throwaway node script** (`spine-tasks/SP-046-ai-companion-c7/tools/lab-ollama.mjs` — evidence tooling, never product code): an Ollama-shaped loopback responder (`GET /api/version` → 200 for the probe; `POST /api/chat` → deterministic canned `message.content` reply, a refusal-shape `{refusal:{category}}` for a trigger word, a slow-streamed response for the panic-quiet proof). The app runs with `--ai-ollama-host http://127.0.0.1:<port>/`. The provider, pipeline, moderation, memory, executor, and UI are all REAL product code — the only stand-in is the model backend, which is exactly what the c2 lab is (real Ollama absent on the evidence box, SP-019 limit 1; honestly named, never a real-Ollama claim). Node is present (`node .spine/patches/verify.mjs` is the contract's first command).

### 2.6 Surface design (evolved grammar; hand-authored smallest AXAML)

`Features/Companion/`:
- **`CompanionWindow.axaml/.cs`** — owned modeless window (W-04 shape): header row (title + **status line** bound to the SP-006 capability state), chat `ItemsControl` inside a `ScrollViewer` (vertical only, horizontal disabled — popup contract discipline), input row (`TextBox` + Send button + Enter `KeyBinding`; in-flight = thinking bubble + send disabled + **Stop** button enabled), settings expander (awareness consent toggle, memory consent toggle, four cooldown value boxes with placeholder baselines, memory-clear button), in-window **confirm panel** for memory clear (modal-within-window: dim overlay, "can't be undone", **No focused by default**, Esc = No — WPF default-No parity; re-entrancy-guarded), honest memory note ("memory is saved and can be cleared; it is not yet used as conversation context" — consult §3.1 #6: the surface must never imply recall).
- **`CompanionViewModel.cs`** — hand-rolled INPC (no MVVM package — `MainWindowViewModel` precedent). `ObservableCollection<CompanionBubbleModel>`.
- **`CompanionBubbleModel.cs`** — carries the TYPED `AiReply`; `IsAiBadged` computed by ONE mapping function from the reply TYPE (`Generated` → badged with provenance class; Fallback/Unavailable/Refused → never — consult §3.1 #8: badge computed at construction from type, never a passed-around flag). Refusal bubbles carry a distinct refusal class (the evolved-grammar POLICY-pill equivalent: amber accent treatment); user bubbles carry no badge ever.
- **Badge/status truth (load-bearing):** badge ← reply type only; status ← `CapabilityRegistry.GetState("ai.provider.local-ollama")` only — never a registration/selection fact.
- **Panic-quiet:** Stop → `pipeline.PanicAsync(bounded)` → thinking bubble removed, typed Cancelled observed, THEN `SelectProvider(current)` re-arms the generation (consult §3.1 #7 — without re-arm the surface stays dead; the returned-to-calm state is a WORKING state). Nothing partial surfaces: the stale/discard machinery already guarantees no late bubble.

### 2.7 Bool-door retirement (assigned obligation)

Re-grep (2026-08-05, this worktree): `RunAwarenessAsync(request, awarenessConsent: bool)` call sites in exactly the 6 PROMPT-named files — `AiMemoryPipelineTests.cs:184`, `AiModerationCoverageTests.cs:94,110,278`, `AiModerationPipelineBoundaryTests.cs:103,140,201`, `AiOfflineIntegrationTests.cs:53`, `AiOperationPipelineTests.cs:297`, `AiProviderLabIntegrationTests.cs:329`. All 6 in File Scope → migrate every call site to the typed `AiAwarenessConsent` overload (`true`→`Given`, `false`→`NotGiven`), DELETE the bool overload from `AiOperationPipeline.cs`, update the typed overload's doc comment (the "residual bool door" note retires). All-or-nothing (SP-044 retirement condition); all 6 migrate cleanly → no hard stop.

### 2.8 Tests

- **`CompanionViewModelTests.cs` (NEW, U):** badge truth incl. the falsifiable pair (Fallback bubble with IDENTICAL text to a Generated bubble → no badge); status mapping from each capability state; refusal bubble class discipline (interactive surfaces Refused; awareness-path types never produce bubbles here); clear-flow state machine (default-No, re-entrancy, failure → typed outcome text); consent toggles drive the typed states; cooldown value edit takes effect on the NEXT operation and never shrinks a live cooldown; panic-quiet (real pipeline + controllable fake provider: in-flight send → panic → typed Cancelled, thinking bubble removed, no partial text, re-armed surface works).
- **`CompanionWindowHeadlessTests.cs` (NEW, headless draw-level):** window opens owned from the dashboard button; Enter-send with a fake provider renders a badged bubble (class check); refusal bubble class; confirm panel default state (No focused); zero binding errors.
- **Bool-door:** the 6 migrated files keep their assertions green unchanged (typed overload shares enforcement).

## 3. Consults + research

### 3.1 Pre-approach (Step 1)

**Mode:** solo (council forbidden — T-7; route per the 2026-08-04 rewire). **Actual answering model: NOT exposed by the consult tool output** (no model identity in the response — the T-2 tooling limit recorded since SP-040; session env `PI_MODEL=k3`/`PI_PROVIDER=kimi-coding`; never guessed).

**Verdict (all substantive points ADOPTED):**
1. **Placement:** owned modeless window confirmed; do NOT reuse `FeaturePopupManager` (single-popup demonstrator manager; wrong lifecycle) — dedicated open path with W-04 discipline.
2. **Default selection LocalOllama: DEFENSIBLE with disclosure** — record the new startup behavior (one bounded loopback probe per launch). Adopted (§2.3).
3. **Cooldown values editable: ADOPTED with guardrails** — settable `Values` property (null-checked), session placeholder only (never persisted), extend-not-shrink stands; a test asserts editing a value never shrinks a live cooldown.
4. **In-product fake server: REJECTED.** A listener that exists only for evidence has no place in the shipped binary (attack surface if the flag ever gets set; precedent distinction: DTRH's LoopbackServer is a product mechanism, the b5 flag is failure injection ON it). **ADOPTED alternative:** product flag `--ai-ollama-host <uri>` (config seam; classifier rejects non-loopback pre-socket — the flag can never widen the network boundary) + task-local node responder. Honesty naming: the headed lab is a task-local loopback Ollama-shaped responder; the provider/pipeline/moderation/memory/UI are real; only the model backend is a deterministic stand-in (the c2 lab's own posture).
5. **Confirm panel:** modal-within-window (input to the rest of the surface blocked), Esc = No, No focused by default, clear re-entrancy-guarded.
6. **Memory non-claim STRENGTHENED:** the surface must carry the honest line that memory is not yet used as conversation context — otherwise the chat view implies recall. **Scope divergence (SP-040 forward-reference "prompt assembly lands in c7" vs this packet's actual scope): recorded, NOT built** — §7 orchestrator follow-up.
7. **Panic re-arm (critical catch):** after `PanicAsync` the owner stays cancelled — subsequent sends would terminate Cancelled forever. The surface re-arms via `SelectProvider(current)` after panic; the calm state is a WORKING state; the panic-quiet proof asserts a successful send AFTER panic.
8. **Badge construction:** the bubble carries the typed `AiReply`; the badge is computed from TYPE at construction by one mapping function; the falsifiable test (identical-text Fallback vs Generated) is mandatory.
9. **Binding-error discipline:** compiled bindings + `x:DataType` on every root including DataTemplates; binding-error capture reviewed at every headed checkpoint (zero tolerance).

### 3.2 avalonia-research pass (MANDATORY pre-AXAML; baseline 12.1.1 verified in `CcpClient.Desktop.csproj:25-27`)

| API the surface needs | Current v12 fact | Source (fetched 2026-08-05) |
|---|---|---|
| Compiled bindings | `AvaloniaUseCompiledBindingsByDefault=true` (already set) + `x:DataType` per root; per-node opt-out via `x:CompileBindings` | docs.avaloniaui.net/docs/basics/data/data-binding/compiled-bindings |
| Conditional classes | `Classes.name="{Binding Bool}"` (incl. `!` negation) drives class-gated styles | docs.avaloniaui.net/docs/styling/style-classes |
| Pseudo-classes | WPF triggers → selectors: `:pointerover`, `:pressed`, `:disabled`, `:focus`, `:checked`; no `Style.Triggers`/`DataTrigger`/`VSM` | docs.avaloniaui.net/docs/styling/selectors; docs.avaloniaui.net/docs/migration/wpf/cheat-sheet |
| Chat list | `ItemsControl` + `ItemsSource` + `ItemTemplate` (default panel StackPanel) inside `ScrollViewer` | docs.avaloniaui.net/docs/reference/controls/itemscontrol |
| Item templates | `DataTemplates` collection with type matching (replaces `DataTemplateSelector`) | docs.avaloniaui.net/docs/concepts/templates/data-templates; docs.avaloniaui.net/docs/migration/wpf/ |
| Commands/keys | direct `ICommand` (no RoutedCommand); `KeyBinding Gesture=Enter` (landed precedent `MainWindow.axaml`) | docs.avaloniaui.net/docs/migration/wpf/cheat-sheet |
| Windows | owned modeless `Show(owner)`, `ShowInTaskbar=false` (landed W-04 precedent `FeaturePopupWindow.axaml`) | local landed evidence, SP-013 |

The avalonia-docs MCP search tool returned oversized/empty payloads on this box (recorded); the official docs were fetched directly and indexed — the URLs above are the citations. A-013 advisory (`ValidateXaml`) runs in Step 3 AFTER hand-authoring, per the advisory chain.

### 3.3 Pre-completion (Step 4)

**Mode:** solo (council forbidden — T-7; route per the 2026-08-04 rewire). **Actual answering model: NOT exposed by the consult tool output** (same T-2 tooling limit; session env `PI_MODEL=k3`/`PI_PROVIDER=kimi-coding`; never guessed).

**Verdict: SHIP-WITH-FIXES — four fix-first items (ALL addressed before .DONE), plus answered questions:**
1. **Unavailable pixel overstatement (FIXED):** the record's first draft claimed Unavailable pixel-proof that was only headless. A dead-port headed run captured it (`evidence/10-unavailable-unbadged.png`: status "unavailable (host-unreachable)" + unbadged subdued bubble). The badge discharge is now: Generated/Refused/Unavailable pixel-proven, Fallback type-level.
2. **`--ai-ollama-host` remote boundary was inspection-only (FIXED):** new test `HostOverride_RemoteHost_RejectedPreSocket_ZeroSendAttempts` — a remote override classifies RemoteHostOllama, the probe rejects pre-socket (typed Unavailable endpoint-not-admitted), the pipeline's admission policy rejects the class, ZERO SendAttempts. The privacy claim is now tested, not argued.
3. **Capture validation + manifest (FIXED):** `evidence/manifest.json` written; every PNG validated (480x620 right window, nonzero, non-blank). This validation is what EXPOSED the misaddressed first pass (windowId silently ignored; all pixels re-taken with `target`).
4. **Wording (FIXED):** §5.2 now separates the three pixel-proven classes from the type-level Fallback non-claim.

Consult answers: (a) no dishonest discharge found beyond the four items; (b) the default-select + startup loopback probe disclosure is adequate (loopback-only, bounded, SP-006 mechanism — NOT an offline-violation); (c) the Fallback type-level non-claim is acceptable — c7 must NOT invent a Fallback path; (d) the 3 out-of-scope test edits are mechanical (the ollama-state arm is a NEW assertion, not a weakened pre-existing one); (e) the modal-within-window claim rests on overlay hit-testing + the VM CanSend guard (both present); a note that ClearOutcomeText lingers is cosmetic, not a defect.

## 4. Engine review presence (T-2)

- Step 1 plan review (`spine_review_step step=1 type=plan`): **SKIPPED by runtime** — SP-195 nested-spawn block; artifact `.reviews/1-20260805T001731.md`; `spawnFailed=false` → proceed (engine-owned reviews run post-.DONE).
- Step 2 plan review (`spine_review_step step=2 type=plan`): **SKIPPED by runtime** — same SP-195 block; artifact `.reviews/2-20260805T005252.md`; `spawnFailed=false` → proceed.
- Step 3 plan review (`spine_review_step step=3 type=plan`): **SKIPPED by runtime** — same SP-195 block; artifact `.reviews/3-20260805T011003.md`; `spawnFailed=false` → proceed.

## 5. Evidence summary

### 5.1 Contract testCommand (verbatim, 2026-08-05, this worktree)

1. `node .spine/patches/verify.mjs` → **exit 0** ("all patches applied on all roots").
2. `dotnet build client/CcpClient.sln -c Debug -t:Rebuild --nologo` → **0 Warning(s) 0 Error(s)** (warnings measured on `-t:Rebuild` per the packet clause; one xUnit1031 in the new VM tests was found and FIXED — SeedMemory made async — before this run).
3. `dotnet test client/tests/CcpClient.Tests` → **601 passed / 0 failed** (floor 581).
4. `dotnet test client/tests/CcpClient.HeadlessTests` → **33 passed / 0 failed** (floor 29).
5. `git diff --check` → clean.

Linux contract run: covered by the WSL2 zero-distro named limit (owner-gated, never faked — same disposition as SP-035/038/040/044).

### 5.2 Headed evidence (WH-class via the avalonia-live 27-tool seat; `CCP_MCP=1 --ai-ollama-host http://127.0.0.1:11734/` + task-local `tools/lab-ollama.mjs` — loopback only, zero external network)

| File | Proves |
|------|--------|
| `evidence/01-window-open.png` + `01b-describe-marks.png`/`01b-describe-legend.json` | Dashboard button opens the owned modeless CompanionWindow (26 semantic marks: every control automation-named) |
| `evidence/02-badge-generated.png` + `02-semantic-tree.json` | **Badge accuracy pixels:** the Generated reply bubble carries the "AI · Loopback" badge pill + neon ring; status line reads "Companion provider: available (loopback)" (capability-state-derived) |
| `evidence/03-refusal-bubble.png` + `03-semantic-tree.json` | **Refusal bubble** on the interactive class ("The AI declined to respond under the content policy." — output-side refusal from the lab's refusal shape), NEVER badged; the refused turn never persisted (file held exactly the prior pair — verified via file read) |
| `evidence/04-clear-confirm-default-no.png` | **Clear confirm:** modal-within-window overlay, "can't be undone", and `get_focused_element` = **ConfirmNoButton** (the default-NO proof the headless seat could not make) |
| `evidence/05-clear-outcome.png` + `05-semantic-tree.json` | Post-clear state: chat log emptied, "Companion memory cleared." outcome text; **file-content proof:** `%APPDATA%/CcpClient/ai_memory.json` DELETED (shell-verified in the window before the next send re-persisted — WPF point-in-time clear, memory re-fills) |
| `evidence/06-consent-cooldowns.png` | Consent/cooldown surfaces: awareness consent toggle ON (`IsChecked true` round-trip), global cooldown 10→42 through the VM into the typed `AiCooldownValues` |
| `evidence/07-inflight-before-panic.png` | REAL in-flight operation against the lab (SLOW_ME 8s slow stream): thinking bubble live, Stop enabled |
| `evidence/08-panic-quiet-calm.png` + `08-semantic-tree.json` | **Panic-quiet:** after Stop — thinking bubble GONE, zero partial text, Stop disabled, calm surface (typed Cancelled underneath; nothing partial surfaced) |
| `evidence/09-rearm-post-panic.png` | **Re-arm:** a post-panic send succeeds with a badged reply (the calm state is a WORKING state — consult #7) |
| `evidence/10-unavailable-unbadged.png` + `10-semantic-tree.json` | **Unavailable pixel proof (dead-port run):** status "unavailable (host-unreachable)" (capability-derived); send surfaces the unbadged subdued Unavailable bubble (provider-unproven) |
| `evidence/manifest.json` | Capture manifest (app-visual-verification skill artifact rule): tree state, platform, scaling, provider modes, per-capture states, pixel validation |
| `evidence/app.log`, `app2.log`, `lab.log` | Sensitive-logging discipline: content scan for every synthetic chat string → ZERO hits (chat/memory content never logged) |

**Binding errors:** `get_binding_errors` at every checkpoint AND at session end → **count 0** throughout (both runs).
**Pixel validation (skill artifact rule):** every PNG verified 480x620 (the companion window — correctly targeted), nonzero, non-blank (distinct-byte sample); results in manifest.json.
**Synthetic content only:** every capture carries lab strings ("lab synthetic reply", "REFUSE_ME", "SLOW_ME", "ECHO") — no user data.
**Badge-accuracy discharge (pre-completion consult correction):** Generated = pixel-proven badged; Refused = pixel-proven unbadged; Unavailable = pixel-proven unbadged (dead-port run); **Fallback = TYPE-level only** (the falsifiable identical-text pair in `CompanionViewModelTests`) — no product path emits interactive `AiReply.Fallback` today (Fallback is app-authored awareness canned text); c7 does not invent a Fallback path (consult (c): recorded non-claim, not a gap).
**Teardown note:** the headed runs were closed via taskkill (not the window path); teardown flush is integration-covered (`CompanionCompositionTests.ProductComposition_MemoryPersistsUnderUserDataRoot_FlushedAtTeardown`).
**Capture-pass honesty:** the FIRST headed pass captured the WRONG WINDOW throughout (the `windowId` param is silently ignored by the seat — screenshots defaulted to the MainWindow). Every pixel capture was discarded and re-taken with the correct `target` param; the semantic/property evidence (selector-addressed) was unaffected. Recorded per the honesty framings; the seat's guide names `target`/`handle` only.

### 5.3 A-013 advisory dispositions (`ValidateXaml`, advisory only — PASS is never API-validity proof; the proof is 0W/0E + headed interaction)

- **PASS** (XML/namespace/x:Name/Window-shape heuristics) — recorded as heuristic; the MCP pins Avalonia 11.3.1 (its "enhanced spacing (11.3+)" note confirms the stale baseline the avalonia-research skill warns about).
- **Finding: "25 inline style properties — consider styles"** → **REJECTED:** repeated semantics (chat bubbles, badge pill, buttons, status) ARE class-styled; inline properties remain on one-off elements exactly per the landed grammar lineage (`MainWindow.axaml`, `FeaturePopupWindow.axaml`). No change.
- **Compiler-learned v12 fact (recorded):** `TextBox.Watermark` is OBSOLETE in 12.1.1 → `PlaceholderText` (AVLN5001, fixed during Step 2); `TextBlock` has no `CornerRadius` (the pill is a Border) — both caught by the real compiler, not the advisory.

### 5.4 File-scope audit (`git diff --stat eff91b32..HEAD -- ':!spine-tasks'`)

23 files, +1681/-24. `fileScopeMustChange` `Features/Companion/` present (7 files). **Out-of-scope-named test files touched (documented in STATUS Discoveries):** `CapabilityTests.cs`, `CompositionRootValidationTests.cs`, `IntegrationProofTests.cs` — mechanical enumeration updates forced by the composition (participant count 6→7 + companion arm; capability list + 2 AI state arms). No behavior assertion weakened. No `fileScopeMustNotChange` path touched (`ConditioningControlPanel/**`, `client/CcpClient.sln`, `client/spikes/**`, `.spine/**`, task-board, port-lessons all untouched).

### 5.5 Bool-door retirement (assigned obligation — COMPLETE, no hard stop)

Re-grep found 10 call sites in exactly the 6 PROMPT-named files; all migrated to the typed `AiAwarenessConsent` overload (`Given`/`NotGiven`); the bool overload DELETED from `AiOperationPipeline.cs` (the shared gate survives as private `RunAwarenessCoreAsync`); the typed overload's doc updated (the "residual bool door" note retired). Full suite green — migration is behavior-identical.

## 6. Budgets, surprises, durable-lesson candidates

**Budget:** single session, inside the 4h export; no context-limit exits.

**Surprises:**
1. **A REAL Ollama (0.32.5) now runs on this box** (127.0.0.1:11434; SP-019 limit 1 said absent at spike time). First full-suite run failed 6 tests on environment assumptions (mine asserted Unavailable; pre-existing lab test flaked under parallel load — passed isolated and in the final run). Assertions rewritten environment-honest (typed state either way); the deterministic lab stays the headed instrument via `--ai-ollama-host` (never the live model).
2. **avalonia-live silent-param drops bite twice:** (a) `windowId` on click_at/get_text — clicks need `handle`/`selector` + top-level-relative coords; `automation_action invoke` did not fire Button.Click (click_at works); (b) **`windowId` on screenshot_window** — the ENTIRE first pixel pass captured the MainWindow, caught only by the dimension check (520x680 vs 480x620). Every pixel capture re-taken with `target`. The skill's "confirm the PNG exists with nonzero dimensions" rule is what caught it — dimensions MEAN the right window.
3. **Focus needs layout:** focusing a just-visible control is a no-op until a layout pass — the default-No focus posts at `DispatcherPriority.Loaded`; the headless seat still couldn't prove focus (moved to the headed `get_focused_element` proof, which DID prove it).
4. **The memory-persist "defect" was my own inspection bug:** the file-JSON field is camelCase `turns`; my node one-liner read `j.Turns` and reported EMPTY — I nearly root-caused a nonexistent store defect. A real repro test (`AiMemoryPanicRearmTests`) proved the live behavior correct (clear → panic → re-arm → re-persist works) and stayed as the regression guard. **Verify the inspection before the machinery.**

**Durable-lesson candidates (orchestrator reconciles — enabler 2):**
1. **Environment facts rot:** "no Ollama on the evidence box" was a spike-time fact that became false; capability assertions must be written typed-state-honest, never environment-assumed. (Class: evidence honesty.)
2. **A panic path that leaves the owner cancelled needs an explicit re-arm** — panic-quiet is not "surface calm", it's "surface calm AND working"; the proof must include a post-panic success. (Class: lifecycle discipline.)
3. **MCP tool params fail silently** — validate a drive primitive end-to-end on a cheap target AND validate capture dimensions against the intended window before building an evidence pass on it. (Class: tool-quirk — bit twice in one task.)
4. **Inspection scripts are code too** — a wrong-case JSON field read manufactured a defect hunt; read the raw bytes before theorizing. (Class: debugging discipline.)

## 7. Orchestrator follow-ups (land-time)

1. **Memory→prompt context:** SP-040's record forward-referenced "prompt assembly lands in c7"; SP-046's packet scope does not include it (verified against PROMPT steps/criteria). NOT built; the surface carries the honest non-claim line. Owner/orchestrator schedules prompt-context assembly explicitly (WPF shape: full dialogue history sent per request, `LocalAiService.cs:374-390`).
2. **Awareness title capability not composed:** `AiWindowTitleCapability.Register` was NOT wired into the product composition — no consumer exists in c7 (no awareness reaction loop; observation without a driver would be an unused probe). The seam stays declared (SP-006 honesty); its consumer slice owns the wiring.
3. **K3 visual review** (orchestrator at land, per packet): the badge/refusal/confirm/panic captures in `evidence/` are the review set.
4. **Real-Ollama environment change:** a live Ollama 0.32.5 now runs on the evidence box (STATUS Discoveries) — future AI slices can opt into real-model LAB evidence; the deterministic lab stays the contract instrument.
5. Board/lessons reconciliation (enabler 2): `task-board.md` / `port-lessons.md` untouched by the worker.
