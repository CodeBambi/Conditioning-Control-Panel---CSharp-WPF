# Task: SP-069 — Companion reply hygiene: the model's raw text is not the user's text

## Mission

The port puts the model's reply text on the screen **verbatim**. Between `IAiProvider.CompleteAsync`
returning and the text landing in the chat bubble, the conversation history and the disk, exactly two
things happen to it: output moderation reads it, and SP-068's link strip removes invented URLs. Nothing
else. There is no hygiene layer at all — grep-verified at authoring: the port has no `<think>`-block
strip, no metadata-tag strip, and no envelope-leak guard.

WPF has all three, and added the last one as a shipped user-visible bug fix
(`932d829a`, 2026-08-07 — the "raw JSON can never leak into the speech bubble" line in
`client/docs/upstream-sync.md` §C). This packet ports the **outcomes** of that layer, and only the
subtractive half of it.

**The port does not merely lack the filter — it manufactures the input the filter exists to catch.**
`AiAwarenessService.cs:229` sends the model a context line in exactly the shape
`[Category: X | App: Y | Title: Z | Duration: N]`. Local models mirror bracketed metadata back. WPF's
`StripMetadataTags` removes precisely that shape from what the user sees. The port sends the shape and
strips nothing. That is this packet's own trigger, and it must be cited as such — not only WPF.

Three layers, all applied to `AiReply.Generated.Text` before it can reach a bubble, a memory turn, or a file:

- **H1 — reasoning-block strip + tokenizer artifacts.** WPF `AiTextHygiene.Clean`: `<think>`/`<thinking>`/
  `<reasoning>`/`<thought>` blocks including an unterminated one, an orphan closing tag, `Ġ`→space, `Ċ`→newline.
- **H2 — metadata-tag strip.** WPF `AiTextHygiene.StripMetadataTags`: five patterns **in a fixed order**,
  then whitespace collapse and trim. Purely subtractive.
- **H3 — envelope-leak DETECTION, and nothing more.** WPF `LooksLikeEnvelopeLeak`: trimmed text starts with
  `{` **and** contains `"response"`. WPF then tries to lift the field out; **the port does not.** Detection
  yields the existing typed code `AiReplyCodes.MalformedOutput`, whose own docstring already commits the
  port to this answer: *"the model's output could not be validated against the strict envelope schema
  (contract §8 — zero repair)"*. No extraction, no JSON-unescape, no command execution, no repair.

**THE HARD BOUNDARY — this packet only ever REMOVES text, and the moderation gate only ever REFUSES MORE.**
Hygiene changes what the moderation gate sees, and a gate that sees less can admit more — SP-068's F2 found
exactly that class and it is the trap here. The rule that keeps this monotone: **moderation evaluates the
UNION.** It runs on the raw text **and** on the hygienic text, and blocks if **either** hits. The gate can
only refuse more than it does today, never less. `AiModerationBoundary.EvaluateOutput` was verified at
authoring to be a **pure function** — no counters, no escalation, no state — so a second call cannot
double-count. WPF moderates the sanitized text only (`OpenAiCompatibleService.cs:630-631`); the union is a
**deliberate divergence in the fail-closed direction** and must be recorded as one, not smuggled in as parity.

**What is NOT here, stated up front so it cannot be implied later.** WPF's same commit also raised the
chat token budget (`MaxTokens` 100 → 350 when effects are enabled). **The port sends no token cap at all**
(`LoopbackOllamaProvider.BuildBody` emits `{model, messages, stream:false, think:false}` — verified at
authoring), so there is no port counterpart and **no truncation parity may be claimed**. Likewise the
envelope validator (`AiEnvelopeValidator`) has **zero product callers** today, and this packet **does not
wire it in**: detecting a leak is not the same as admitting an envelope, and admitting one would put model
text on the command-execution path, which is a different row and a widening.

## Dependencies

- **Task:** SP-068 (landed `f2662cd0`) — the current floor, the reply-seam this packet edits, and the
  "every change narrows" licence it inherits. SP-068's F3 link strip stays exactly where it is; H1–H3 sit
  **upstream** of it, mirroring WPF's two-stage split (transport hygiene before moderation, brain hygiene after).
- SP-046 / SP-047 — the landed companion chat surface and memory→prompt assembly that consume this text.
- SP-044 — the landed command-execution slice that owns `AiEnvelopeValidator`. **Not touched here.**

## Context to Read First

- `client/src/CcpClient.Desktop/Ai/AiOperationPipeline.cs:300-375` — output moderation (`:321`), SP-068's
  link strip (`:343`), memory persist (`:370`), reply application. **This is the seam.**
- `client/src/CcpClient.Desktop/Ai/AiOperationVocabulary.cs:152-195` — the `AiReply` cases and
  `AiReplyCodes`; **`MalformedOutput` (`:188`) is the code H3 uses** — read its docstring before deciding anything
- `client/src/CcpClient.Desktop/Ai/AiModerationBoundary.cs:272-296` — `EvaluateOutput` and the private
  `Evaluate`; confirm for yourself that it is pure before relying on the union rule
- `client/src/CcpClient.Desktop/Ai/AiAwarenessService.cs:229` — **the port's own trigger**: the
  `[Category: … | App: … | Title: … | Duration: …]` line the port sends the model
- `client/src/CcpClient.Desktop/Ai/AiPrivacyFilters.cs:276+` — SP-068's `StripUnsanctionedLinks`, the
  layer H1–H3 must sit upstream of; its comment block is the model for how a ported filter is cited
- `client/src/CcpClient.Desktop/Ai/AiCommandEnvelope.cs:180-250` — `AiEnvelopeValidator`, **read-only
  context**: it exists, it is unwired, and it stays unwired (framing m)
- `client/src/CcpClient.Desktop/Features/Companion/CompanionViewModel.cs:251-270` — `ApplySendResult` and
  `CompanionBubbleModel.ForReply`, the surface that renders whatever survives
- `ConditioningControlPanel/Services/AIService/AiTextHygiene.cs` — `Clean` (`:25`, `:30`, `Ġ`/`Ċ`) and
  `StripMetadataTags` (`:36`, `:40`, `:44`, `:53`, `:61`, collapse+trim `:79-80`) — **H1 and H2's source**
- `ConditioningControlPanel/Services/AIService/AiResponseParser.cs` — `LooksLikeEnvelopeLeak` (`:53`),
  `TryLiftResponseField` (`:60`), `SanitizeResponse` (`:314`), and the `Parse` ladder (`:26`) — **H3's source,
  lift half deliberately not ported** (framing f)
- `ConditioningControlPanel/Services/AIService/OpenAiCompatibleService.cs:620-660` — WPF's moderation
  ordering and its written rationale; the thing the union rule diverges from (framing b)
- `client/docs/ai-operation-contract.md` — §7 rule 1 (moderation ordering), §8 (strict envelope, zero
  repair), §1 (typed replies) — **read-only, framing (c)**
- `client/docs/upstream-sync.md` §C — the two companion lines this packet acts on (**read-only**)
- `client/tests/CcpClient.Tests/AiOperationContractTests.cs`, `AiModerationCoverageTests.cs`,
  `AiPipelineTests.cs` — the landed bindings you must not weaken
- `client/tests/CcpClient.Tests/TestWait.cs` — the shared wait/budget helper; **add no waits outside it**
- `client/tests/floor/floor.json` and `client/tests/floor/check-floor.mjs` — the floor, its `bumpRule`, the
  5 pinned skip names (framings j, l)
- `client/docs/port-workflow.md` — §Verification floor and the `CCP_DATA_ROOT` rule at `:204`
- `docs/constitution.md` — standing orders

## File Scope

- `client/src/CcpClient.Desktop/Ai/AiOperationPipeline.cs` (the seam: hygiene + the union moderation rule)
- **Exactly one new file** `client/src/CcpClient.Desktop/Ai/AiTextHygiene.cs` holding H1/H2/H3 — one
  definition per rule, mirroring the WPF class name so the provenance is legible (framing n)
- `client/tests/CcpClient.Tests/**` (the new facts + any existing test that legitimately moves)
- `client/tests/floor/floor.json` (count bump only — framing j)
- `spine-tasks/SP-069-companion-reply-hygiene/**`
- **NOT in scope:** any other path under `client/src/**` (including `AiCommandEnvelope.cs`,
  `AiPrivacyFilters.cs`, `AiOperationVocabulary.cs`, `AiAwarenessService.cs`, and everything under
  `Features/`), `client/tools/**`, `client/spikes/**`, `ConditioningControlPanel/**`, `client/docs/**`,
  `docs/constitution.md`, `.spine/**`, `.pi/**`

## Contract

| Field | Value |
|-------|-------|
| testCommand | `node .spine/patches/verify.mjs && dotnet build client/CcpClient.sln -c Debug --nologo && node client/tests/floor/check-floor.mjs` |
| fileScopeMustChange | `client/src/CcpClient.Desktop/Ai/AiOperationPipeline.cs` |
| fileScopeMustNotChange | `ConditioningControlPanel/**`, `client/spikes/**`, `client/tools/**`, `client/docs/**`, `docs/constitution.md`, `.spine/**`, `.pi/**` |
| artifactsMustExist | `spine-tasks/SP-069-companion-reply-hygiene/record.md` |

`check-floor.mjs` is `--no-build` by design, so the explicit `dotnet build` ahead of it is **not optional** —
standalone the wrapper measures the last build, not the working tree, and fails closed naming the wrong
cause (SP-065 land finding). `FloorWrapperGuardTests` binds every packet with ID >= SP-065: **never** call
`dotnet test` outside the wrapper.

## Review Level: 2 (Plan and Code)

Call `spine_review_step` after each step. Scored: blast radius 2 (the **live** user-visible reply path plus a
change to what the moderation gate reads), novelty 1 (the port has no text-hygiene class today —
grep-verified), security 1 (moderation ordering is a safety boundary), reversibility 0 → **Level 2**.
**T-2 heading format is load-bearing** — record engine-review presence/absence per call in `record.md`.
**Authoring rule (SP-034 defect): `grep -c "Review Level" PROMPT.md` >= 2 before launch.** Review Level 2
applies to every step below.

## Steps

### Step 1: Re-derive the three layers from WPF and prove the ordering is monotone

- [ ] Update STATUS.md before starting work
- [ ] For **each** of H1/H2/H3: locate the WPF mechanism **by symbol name**, record the anchor you found
      beside the anchor this packet gave you, and note every divergence (framing a). **A landed audit ages
      into a map, not a citation** — offsets move, semantics persist
- [ ] H2: transcribe the five patterns **and their order**. State plainly what changes if the order is
      permuted, and pick the case that proves it (framing d). If the order turns out not to matter for any
      input you can construct, **say so** rather than asserting significance you cannot demonstrate
- [ ] H1: transcribe the block regex, the orphan-closer rule and both character mappings verbatim. Decide
      and justify the unterminated-block case (WPF's `(</\1>|$)` alternation is the whole question)
- [ ] H3: confirm the two-part predicate from WPF and confirm the **lift is deliberately not ported**
      (framing f). Read `AiReplyCodes.MalformedOutput`'s docstring and state whether it is honest for this
      case; if it is not, **stop and report** — do not add a new `AiReply` case (framing h)
- [ ] Derive the layer order from WPF (`Clean` runs before `Parse`; leak detection is inside `Parse`;
      `StripMetadataTags` runs in `SanitizeResponse` after it) and state the port order you will implement
      with its reason
- [ ] **The union rule, proven not assumed (framing b):** verify for yourself that `EvaluateOutput` is pure.
      Then write the argument that evaluating {raw, hygienic} and blocking on either is **monotone** — the
      set of texts that reach the user after this change is a subset of the set that reaches them today.
      Construct the adversarial case in writing: a forbidden token that is invisible in the raw text and
      visible only after hygiene (JSON escaping, `Ġ`/`Ċ` artifacts, a tag boundary), and the reverse
- [ ] **Boundary clearance, written per layer:** what is observed, retained, transmitted, and logged before
      vs after. Every delta must be **less or equal**. Any "more" is a stop condition
- [ ] Confirm whether any layer needs `ai-operation-contract.md` wording (§7 rule 1 describes the moderation
      ordering this packet changes). If so, name the **exact wording** needed and **leave the file
      untouched** (framing c)
- [ ] **Pre-approach solo consult** (`mode: "solo"` — a bare `consult` hits the council-roster trap, T-7;
      Opus 5 main, Fable 5 fallback). Verdict + **ACTUAL answering model** in `record.md`. **The tool has
      repeatedly returned reasoning-only or mid-sentence-truncated verdicts (waves 17, 21, 22, 23, 25 —
      now board row T-18) — ask narrowly, cap the reply length, record exactly what surfaced, and never
      stitch a verdict out of reasoning.** An unstitched non-verdict is a MISSING consult: re-ask it

### Step 2: Implement the three layers — removal only

- [ ] `AiTextHygiene.cs`: H1, H2 and H3 with **exactly one definition each**, WPF cite in a comment per rule,
      and H2's pattern order expressed so that it is readable as an ordered sequence, not an incidental one
- [ ] H3 is **detection only** — a predicate returning a verdict. No extraction, no JSON-unescape, no
      repair, no command execution, no call into `AiEnvelopeValidator` (framings f, m)
- [ ] Apply at the pipeline seam in the derived order, **upstream** of SP-068's link strip, which stays
      exactly where it is and is not modified
- [ ] Implement the **union moderation rule**: `EvaluateOutput` on the raw text and on the hygienic text;
      **Block if either hits** → the existing `Refused` shape. For `SoftHit`, take the first and say which
      in the record. Blocking must remain non-escalating for output (unchanged behavior)
- [ ] A reply emptied by hygiene is typed from the **existing** `AiReply` vocabulary and is never appended
      to memory — the SP-068 `reply-stripped-empty` precedent, whose atomic turn-pair rule you must preserve
- [ ] **Nothing new is observed, persisted, logged or transmitted.** Prove it: grep your own diff for new
      log/diagnostic/persist/network calls and show the result in the record. **Never log a reply fragment,
      a stripped tag, or leaked JSON** — not even truncated, not even in a test helper that writes to disk
- [ ] Summarize the `git diff` per product file in the record; confirm no edit outside the two files

### Step 3: Bind all three, one source at a time

- [ ] Regression facts per layer: H1 removes think/thinking/reasoning/thought blocks incl. the unterminated
      case and the orphan closer, and maps both artifact characters; H2 removes each of the five shapes,
      collapses whitespace and trims; H3 detects the envelope leak and yields the typed code
- [ ] **Pin H2's ORDER, not just its members** (framing d) — a test that fails if the patterns are permuted,
      built from the case you constructed in Step 1
- [ ] **Pin the port's own trigger** (framing e): the exact `[Category: … | App: … | Title: … | Duration: …]`
      shape that `AiAwarenessService.cs:229` sends the model is stripped when the model echoes it back.
      Build the fixture from that shape; cite the line in the test
- [ ] **Pin the union rule** in both directions: a forbidden token visible only in the raw text blocks, and a
      forbidden token visible only after hygiene blocks. This is the fact that proves the change is monotone
- [ ] **Pin the H3 non-lift**: a leaked envelope whose `"response"` field contains recoverable text yields
      the typed unavailable code and **the text is not displayed, not persisted, and not executed**
- [ ] Include a **negative control per layer**: text that must pass through **byte-identical**, so a pin
      cannot be satisfied by a layer that eats everything. Include at least one legitimate reply containing
      a `{`, and one containing a bracketed phrase that is **not** a metadata tag
- [ ] **BITE TEST, one layer at a time (framing i):** revert H1 alone → only H1's pins go red; then H2 alone;
      then H3 alone; then the union rule alone. Capture each RED under `evidence/` naming the reverted source
      and confirming the other layers' pins stayed green. **A shared revert is not acceptable evidence**
      (SP-067's land proved it falsely verifies pins that were never exercised)
- [ ] Confirm the landed pipeline/moderation tests still assert exactly what they asserted before — **zero
      assertions weakened, zero tolerances widened**; prove it with a per-file `git diff` summary
- [ ] Bump `floor.json` `total` in the **same commit** as the new facts, reason in the message (framing j).
      `allowedSkips`, `admissionRule`, `skipSemantics` untouched

### Step 4: Record + pre-completion consult

- [ ] `record.md`: per-layer WPF anchors (found vs given, framing a); the transcribed patterns with their
      cites; H2's order with the case that demonstrates it; the layer order and its reason; **the monotonicity
      argument and the adversarial cases in both directions**; the union rule recorded as a **deliberate
      divergence from WPF's sanitized-only moderation, in the fail-closed direction**; H3's non-lift decision
      and the `MalformedOutput` justification; the per-layer boundary clearance table; the **bite matrix**
      (one revert per layer plus the union rule, each RED cited); the floor bump with its reason; the run
      table with exact counts and skipped names; consults + **ACTUAL answering models**; engine-review
      presence per step; intended board filings (state them, set no row state)
- [ ] **Honesty cell — required.** At minimum: (1) **no truncation parity is claimed** — the port sends no
      token cap, so WPF's 100→350 fix has no port counterpart (framing g); (2) this is the **subtractive half**
      of WPF's fix — WPF salvages a leaked envelope, the port refuses it, so a user whose model emits an
      envelope gets **no reply** where WPF shows one, and that is a deliberate choice, not an oversight;
      (3) `AiEnvelopeValidator` remains **unwired** and this packet did not change that; (4) which layers were
      verified by execution vs by reading; (5) whether any **real** local model output was exercised, or only
      constructed fixtures — do not imply field evidence you did not gather; (6) **Linux unproven** (zero WSL
      distros on this machine — do not fake a Linux run); (7) hygiene is **lossy by design** — a stripped
      fragment cannot be recovered downstream
- [ ] If the named flake (`ChaosTunnelLoopbackTests.Logging_RouteClassesOnly_NeverFilenameOrQuery`) fired in
      any run, it is recorded by name with run number and TRX path, and was **not** retried away (framing p)
- [ ] **Pre-completion solo consult**; verdict + actual model in `record.md`
- [ ] STATUS.md accurate before `.DONE`; intended board filings named per ENABLER 2 (set no state)

### Step 5: Testing & Verification

- [ ] Contract testCommand passes **through the wrapper** (`verify.mjs` exit 0, build 0W/0E, new exact unit
      count / 35 headless, skip set exactly the 2 Windows-observed pinned names)
- [ ] **3 consecutive full-suite greens**, >= 1 a **fresh-checkout first-ever build** ("cold" is a NEW
      worktree, not a rebuild in place — port-lessons 2026-08-12). Per-run table: run, worktree, cold/warm,
      unit + headless counts, skipped names, TRX path
- [ ] The bite matrix is complete: **four separate reverts, four separate REDs** (H1, H2, H3, union rule),
      each naming the file reverted and the pins that went red — and confirming the others stayed green
- [ ] `git diff --check` clean
- [ ] `git status --short` shows only File Scope paths
- [ ] `git status --porcelain --ignored=matching -uall` shows **no new ignored artifact** produced by any run

## Completion Criteria

- All three layers land in one file with exactly one definition per rule, applied at the reply seam upstream
  of SP-068's link strip, which is unmodified
- Every WPF anchor is re-derived by symbol and recorded found-vs-given; every pattern is transcribed verbatim
  with its cite
- H2's pattern **order** is pinned by a test, not merely preserved by luck
- The union moderation rule is implemented, and its monotonicity is argued in the record **and** pinned by
  facts in both directions
- H3 detects and refuses; it does not lift, unescape, repair, or execute, and `AiEnvelopeValidator` is still
  unwired
- The boundary clearance shows observed/retained/transmitted/logged is **less or equal** for every layer,
  with no new log, persist, or network call, and no reply fragment reaching any log
- Each layer is bound by facts **and a byte-identical negative control**, and proven to bite by **its own**
  revert — four reverts, four REDs
- `ai-operation-contract.md` is unedited; any wording §7 rule 1 needs is named in the record for the orchestrator
- Zero assertions weakened, zero tolerances widened, nothing quarantined, nothing added to `allowedSkips`
- `floor.json` `total` bumped in the same commit as the facts that moved it, reason in the message
- 3 consecutive full-suite greens at the stated exact counts, >= 1 fresh-checkout first-ever build
- The record states plainly that no truncation parity is claimed and that the port refuses a leaked envelope
  where WPF salvages it

## Do NOT

- Port `TryLiftResponseField`, `BalanceBraces`, `RepairJson`, `ExtractOuterJsonObject`, `ParseMixedFormat`,
  or any other repair/extraction path — H3 is **detection only** (framing f)
- Wire `AiEnvelopeValidator` into the live reply path, or route model text toward command execution
  (framing m) — that is a different row and a widening
- Add a token cap, `num_predict`, `MaxTokens`, or any request-shape change, or imply truncation parity
  (framing g)
- Add a new `AiReply` case, or change what an existing case means (framing h)
- Move, modify, or duplicate SP-068's `StripUnsanctionedLinks` or the `AiPrivacyFilters` title filters
- Moderate **only** the hygienic text — the union is what keeps this monotone (framing b)
- Add any observation, persisted field, log line, diagnostic, or network call; never log a reply fragment,
  a stripped tag, or leaked JSON, including in a test helper that writes to disk
- Port `UnwrapSpokenSigil`, the C3 title rewrite, or WPF's canned-fallback provider
- Edit `client/docs/ai-operation-contract.md` or any file under `client/docs/**` (framings c, q)
- Accept a shared revert as bite evidence; each layer is proven by its own revert (framing i)
- Weaken, retry, quarantine, or allowlist any test; add anything to `allowedSkips`; touch `admissionRule`,
  `skipSemantics`, or the 5 pinned names (framings j, l)
- "Fix" the 2 Windows-observed skips or drive the skip count to 0 — **the asymmetry is correct** and driving
  it to 0 regresses SP-066's honesty
- Add waits outside `TestWait`, or a timeout literal that trips SP-063's `"Timeout = TimeSpan."` guard token
  without a marker + pin
- Refactor or rename beyond the two files in File Scope (framing q)
- Widen, disable, or special-case the floor to make one of your own steps pass (framing j)
- Call `dotnet test` outside `check-floor.mjs` (`FloorWrapperGuardTests` binds this packet)
- Export `CCP_DATA_ROOT` process-wide for a suite run (framing o) — it skips the SP-057 pin and blinds the
  exact-count floor (the vacuous-green class SP-062 closed)
- Create any new file under `client/tools/` (`.gitignore:168` — it would pass in-lane and vanish from the
  merged tree)
- Edit `client/docs/task-board.md`, `client/docs/port-lessons.md`, `client/docs/upstream-sync.md`,
  `docs/constitution.md`, `.spine/**`, `.pi/**`, `AGENTS.md`, `CLAUDE.md`; set any board row state
- Use `consult` council mode (T-7: solo only)
- Run `git clean -fdX` repo-wide in the lane — it deletes the T-14-staged `.pi/npm` that `verify.mjs` needs;
  clean **scoped** (`git clean -fdX -- client/`) and only after the contract passes
- Commit build output (`bin/`, `obj/`) or any results file

## Git Commit Convention

- `feat(SP-069): complete Step N — <summary>`

## Documentation Requirements

**Must Update:** `spine-tasks/SP-069-companion-reply-hygiene/record.md`, `STATUS.md`
**Check If Affected:** `client/docs/ai-operation-contract.md` (**read-only for this packet** — §7 rule 1
describes the moderation ordering the union rule changes; state the exact wording needed in `record.md` as a
finding for the orchestrator; do not edit it)
**Explicitly NOT updated by the worker:** `client/docs/task-board.md`, `client/docs/port-lessons.md`,
`client/docs/upstream-sync.md`, `docs/constitution.md`, `.spine/**`

## Amendments

- 2026-08-13 (authoring, orchestrator): **wave 26 runs this row ALONE.** Owner default in force: back to WPF
  parity (wave-24 digest, CONTEXT). A second lane was rejected for the same reason as wave 25: any lane-mate
  that adds or removes a test collides on `floor.json` and the exact count — green alone, RED at merge (the
  SP-054/SP-058 class).
- 2026-08-13 (authoring, orchestrator): **decomposition consult (solo, Opus 5) — verdict surfaced cleanly on
  the first call under a 200-word cap** (the T-18 technique holding for a 6th consecutive wave; recorded, not
  assumed fixed). Verdict: **proceed, with H3 narrowed.** Two substantive corrections, both encoded:
  **(1) do not choose between moderating the raw text and moderating the sanitized text — moderate the UNION**,
  because the gate then only ever refuses more and "every change narrows" survives literally; this is F1's own
  argument (a union is safe when the operation only ever drops) reused for a gate. The divergence from WPF is
  fail-closed and is recorded rather than agonized over. **(2) cut the LIFT from H3** — detect the envelope
  leak and return typed, do not extract: extraction was the packet's only non-subtractive layer, its port
  trigger is the weakest (the port sends no system prompt and never requests an envelope), and dropping it
  deletes the entire JSON-unescape surface. Its two conditions are Step 3's H2-order pin + the
  `AiAwarenessService.cs:229` trigger cite, and the four-way bite matrix.
- 2026-08-13 (authoring, orchestrator): **the advisor's checkable claims were verified before encoding, not
  trusted.** `AiModerationBoundary.Evaluate` (`:279-296`) is a pure token scan with no counter, no escalation
  and no state — so the union rule's second `EvaluateOutput` call cannot double-count; escalation is invoked
  only for interactive **input** blocks (`AiOperationPipeline.cs:267`). `AiRequest` carries only
  `Prompt` + `History`, and `LoopbackOllamaProvider.BuildBody` (`:252-258`) emits `{model, messages,
  stream:false, think:false}` — **no token cap**, which is what makes the truncation half a non-item.
- 2026-08-13 (authoring, orchestrator): **WPF archaeology was performed at authoring and its anchors are given
  as hints, not citations** (framing a). Found at authoring: `AiTextHygiene.Clean` `:25`/`:30`,
  `StripMetadataTags` `:36`/`:40`/`:44`/`:53`/`:61` + collapse `:79-80`; `AiResponseParser.LooksLikeEnvelopeLeak`
  `:53`, `TryLiftResponseField` `:60`, `SanitizeResponse` `:314`, `Parse` `:26`; moderation ordering
  `OpenAiCompatibleService.cs:630-631` with its written rationale at `:622-629`; the fix commit is `932d829a`
  (2026-08-07), distinct from the earlier subtractive tag fix `e43ee5f7` (2026-08-06). **Re-derive every one by
  symbol and record found-vs-given** — SP-068 proved a landed audit's offsets go stale while its semantics hold.
- 2026-08-13 (authoring, orchestrator): **Size M.** Three small pure-text rules, but the weight is evidence:
  an order pin, a monotonicity argument with adversarial cases in both directions, a byte-identical negative
  control per layer, and four independent bite reverts. Not split — the three layers share one seam, one WPF
  commit and one floor bump.
- 2026-08-13 (authoring, orchestrator): **`spine preflight`'s `prelanded-file-scope` warning, if it fires, must
  not be obeyed.** It compares `fileScopeMustChange` against **`main`** (`validate-prompt.mjs:196`), and `main`
  is the still-shipping WPF branch carrying **no `client/` tree at all**, while the contract verifier uses
  `baseBranch` from `.spine/spine-config.json` (`feat/crossplatform`). Following its hint (redirect
  `fileScopeMustChange` to delivery artifacts) would manufacture the contract-passes-on-docs-only class
  (SP-214/SP-457). **`fileScopeMustChange` stays pointed at `AiOperationPipeline.cs`.**
- 2026-08-13 (authoring, orchestrator): machine posture — laptop; **zero WSL distros, so Linux is a standing
  named gate** (do not fake a Linux run); **MCP not re-probed this phase** — a named limit, never a blocker.
  This packet touches **no AXAML**, so the A-013 advisory step is not a gate for it. **`## Review Level: 2`
  heading present + grep-verified >= 2 (SP-034 authoring rule).**
- 2026-08-13 (authoring, orchestrator): **worker board-row obligation.** SP-001's gap recurred at SP-067 and
  again at SP-068. ENABLER 2 keeps `task-board.md` out of worker scope, so the row update is **budgeted into
  the land** by the orchestrator. Name your intended filings precisely in `record.md` — that text is what lands.
