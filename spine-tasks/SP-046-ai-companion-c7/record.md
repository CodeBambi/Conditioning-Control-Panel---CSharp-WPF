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

## 4. Engine review presence (T-2)

(filled as reviews are called)

## 5. Evidence summary (Step 3/5)

(filled at consolidation)

## 6. Budgets, surprises, durable-lesson candidates

(filled at consolidation)

## 7. Orchestrator follow-ups (land-time)

1. **Memory→prompt context:** SP-040's record forward-referenced "prompt assembly lands in c7"; SP-046's packet scope does not include it (verified against PROMPT steps/criteria). NOT built; the surface carries the honest non-claim line. Owner/orchestrator schedules prompt-context assembly explicitly (WPF shape: full dialogue history sent per request, `LocalAiService.cs:374-390`).
2. Board/lessons reconciliation (enabler 2): `task-board.md` / `port-lessons.md` untouched by the worker.
