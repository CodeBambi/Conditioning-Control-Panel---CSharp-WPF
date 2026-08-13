# Task: SP-068 — Three subtractive privacy filters over the landed awareness and reply paths

## Mission

Board row `:46` (Her Room + Awareness, **OPEN**) carries a landed audit — `client/docs/her-room-divergence-audit.md` (SP-060) — that verdicted 38 elements and sized every ADOPT/MERGE row. Its sharpest finding, quoted on the row itself: **"the strongest adopts are subtractive."** The elements that *remove* or *narrow* data flow carry no privacy-filter pressure, need no owner decree, and can land over the port's already-shipped code.

This packet lands **three** of them. Each one **narrows an existing boundary** in the port's landed AI paths. None adds an observation, a datum, a persisted field, a log line, or a network call.

| # | Filter | Audit row | Audit verdict | What it narrows |
|---|--------|-----------|---------------|-----------------|
| **F1** | Incognito hard-drop | A6 | **ADOPT** | Private/incognito window titles are dropped before anything is counted or packaged |
| **F2** | Title scrubbing | A10 | **ADOPT** | Emails, long digit runs and control characters are stripped from a title, and it is length-capped, before it can leave |
| **F3** | Unsanctioned-link strip | C3 | **MERGE** (strip half only) | Model-invented URLs are removed from companion prose before it reaches the bubble, history, or disk |

**Provenance of this set — state it exactly, do not inflate it (binding).** These three were **derived at authoring** by filtering the audit's own sizing table (`her-room-divergence-audit.md` §5) for rows that are **Size S**, have dependency **"none"**, and need **unit evidence only**. That filter yields six rows; three of them (A11 one-hour pause, D6 two-step inline confirm, D11 transcript window) additionally require headed evidence and are **deferred to a machine that has the owner's evidence display** — they are not dropped and not part of this packet. **F3 (C3) is a `MERGE` row, not an `ADOPT` row**, and the audit sizes only its **strip half** as dependency-free; its title-rewrite half needs a media pool the port does not have. Do not describe this packet as "the audit's six adopts" or as adopting upstream's Her Room redesign. It is three filters, one filter of the table, stated as such.

**Binding framings:**

(a) **Every WPF line number in this packet is a HINT, not a citation — the audit's own cites have already drifted.** Verified at authoring: the audit cites `AwarenessObserverPolicy.cs:319-327` for the incognito drop, but that range is `ResolveOwnProcessName()`; it cites `:277-279` for the blank-title drop, but that range is a `catch` returning `PolicyUnavailable`. The *semantics* are all present in those files — the *offsets* are stale. **Re-derive every anchor yourself by symbol name, and record the anchor you actually found next to the one this packet gave you.** Where they differ, yours wins and the difference is a finding.

(b) **NARROWING ONLY. This is the packet's hard boundary and its whole justification.** Every change must remove or reduce what flows. You may **not**: observe a new datum (process name, app id, window class, dwell time — that is audit rows A1/A9 and is **owner-blocked** on question O2); persist anything new; add any log line carrying a title, a URL, or a fragment of either; change any network call; or widen a consent surface. `client/docs/ai-operation-contract.md` §12 and the c7 honesty line already forbid titles entering diagnostics, logs, or memory — **your filters must not become the first exception.** If a change you are considering adds a datum rather than removing one, it is out of scope: stop and report it.

(c) **If a filter requires editing `client/docs/ai-operation-contract.md`, that filter's contract half STOPS and is reported — you do not edit policy text.** Policy-touching text lands via the orchestrator, never a worker (SP-059 precedent, where a drafted constitution line was applied at land). Implement the mechanism, state precisely what contract wording you believe it needs, and leave the file untouched. A packet that quietly amends the contract to match its own code has inverted the authority order.

(d) **F1 — the incognito drop. WPF has TWO marker lists and you must reconcile them, not double-port them.** Verified at authoring: `ConditioningControlPanel/Services/Awareness/AwarenessPrivacyRules.cs` defines `IncognitoMarkers` (~`:192`) with `LooksIncognito` (~`:308`), applied at ~`:279-280` as `Drop(AwarenessDropReason.Incognito)`; **and** `ConditioningControlPanel/Services/Awareness/AwarenessObserverPolicy.cs` defines its **own** `IncognitoMarkers` array (~`:169`) with `IsIncognitoTitle` (~`:346`), applied at ~`:264-266` under the comment *"Incognito first. Before classification, before the lists, before anything is resolved."* Determine whether the two lists agree, and port **one** list into the port with the union or the divergence stated explicitly. The port must have exactly one definition (the SP-055 `IsAssetActive` discipline: one definition, every consumer routed through it).

(e) **F1 — the blank-title case is a real decision, not a detail, and WPF's answer is split across two rules.** `IsIncognitoTitle` returns **false** for a blank title (fail-*open* — verified at `AwarenessObserverPolicy.cs:348`); blank frames never reach it because they are dropped earlier by a different rule (`FrameDrop.NoForeground`, ~`:249,:255`). The port's path is shaped differently: `AiWindowTitleCapability.TryCaptureForegroundTitle` returning false already yields a typed `Unavailable`. **Establish empirically whether an empty-or-whitespace title can be returned as a successful capture in the port**, then decide that case explicitly and say why. Copying `return false` without checking would import a fail-open branch into a path that has no earlier guard.

(f) **F2 — the scrubber's rules are exact values, and the projection cap is NOT one of them.** Verified at authoring in `AwarenessPrivacyRules.cs`: `SanitizeTitleForWire` (~`:346`), `MaxTitleLength = 80` (~`:372`), `EmailPattern` = `[\w.+-]+@[\w-]+\.[\w.-]+` (~`:444-445`), `LongDigitsPattern` = `\d{6,}` (~`:447-448`), and a whitespace collapse feeding `SanitizeDisplayName(..., MaxTitleLength)` (~`:367`). Port the rules **verbatim with their WPF citations**; do not round the numbers, do not "improve" the regexes. The audit also names a **120**-char cap in `AwarenessProjection.cs` — that is the **cloud wire** path. **The port has no cloud provider** (admission §2 rule 6: loopback Ollama only), so the 120 cap has no consumer here. Record it as **deliberately not ported, with that reason** — never silently.

(g) **F3 — order is the whole point, and the port's site is a persist path.** WPF strips *before the text reaches "the bubble, history, or disk"* (`ConditioningControlPanel/Services/Companion/Brain/CompanionBrain.cs:263`, comment verbatim), applying `AiTextHygiene.UnwrapSpokenSigil` then `AiTextHygiene.StripUnsanctionedLinks` at `:264,:269` (chat) and `:356-357` (reaction). In the port, `AiReply.Generated` text is handled in `client/src/CcpClient.Desktop/Ai/AiOperationPipeline.cs`: output moderation at `:319`, **memory persist at `:344`** (`_memory.Append(... passed.Text)`), reply application after. **Derive the correct insertion point and justify it against both WPF's rule and the port's contract ordering** (`ai-operation-contract.md` §7 rule 1 places output moderation after the stale check and before reply application). Stripped text must never reach memory, the bubble, or disk. Note explicitly that this changes what c4/SP-040 persists — less text — and prove that is the only persistence change.

(h) **F3 — the "nothing survived" case must be typed, and you may not invent a new reply case.** WPF, at `CompanionBrain.cs:279`: when the text is *"[n]othing but sigil shell, or nothing but invented links"*, there is **no reply at all**. The port's vocabulary is `AiReply.Generated | Fallback | Refused | Unavailable` (`AiOperationVocabulary.cs:157`+). Choose the existing case that is honest for an emptied reply and justify it from the contract. **Adding a new `AiReply` case, or changing what an existing case means, is a contract change → framing (c): stop and report.**

(i) **F3 — only the strip half. The sigil-unwrap half is deferred by name.** The audit's C3 row lists three parts (sigil unwrap, unsanctioned-URL strip, off-pool title rewrite) and its §5 sizing admits **"Strip only"** as the dependency-free half. Port `StripUnsanctionedLinks` (its URL surface is `AiTextHygiene.cs:147` `AnyUrl`). **Do not** port `UnwrapSpokenSigil` (`:89`) or the title rewrite. Record both as deferred **with the reason**, so a later reader sees a decision rather than an omission. If while reading you conclude the strip is incorrect or unsafe without the unwrap, that is a finding to report — not a licence to widen the packet.

(j) **Three filters mean THREE bite tests, proven one source at a time.** SP-067's land proved a shared revert falsely verifies pins that were never exercised: reverting one fix left the other two pins green. Every regression pin you add must be shown to **fail when, and only when, its own filter is reverted** — one filter at a time, each with its captured RED. Five green suite runs bound nothing about whether a new pin can fail.

(k) **The awareness path has NO product consumer today, and the record must say so.** Verified at authoring: `ObserveForegroundTitle` (`AiAwarenessService.cs:531`) and `RunReactionAsync` (`:497`) are called only from tests; the only product-side wiring is the moderation surface registration (`AiModerationBoundary.cs:110`, `awareness-context-fields` → `Wired`). So F1 and F2 harden a boundary on machinery that is **real, typed and moderation-wired but not yet driven by a user path.** That is worth doing — it is the boundary that a future consumer will inherit — but **do not claim a user-visible privacy improvement for F1/F2.** F3 differs: its path is live (the companion window's replies). State the asymmetry plainly.

(l) **Floor discipline (`floor.json` `bumpRule`).** Floor at authoring: **903 unit / 35 headless / 2 skipped on Windows, build 0W/0E** (SP-067, integrate `75a09d61`). This packet **adds** facts. Bump `total` in the **same commit** as the tests that move it, reason in the message. Never widen, disable, or special-case the floor to make one of your own steps pass. Do not touch `allowedSkips`, `admissionRule`, or `skipSemantics`.

(m) **THE PINNED SKIPS ARE CORRECT. DO NOT "FIX" THEM.** `client/tests/floor/floor.json` pins **5** fully-qualified names in `allowedSkips`; exactly **2** skip on this Windows machine (`SecretStoreTests.LinuxProbe_TypedOutcome_NeverFaked`, `ChaosTunnelCapabilityTests.Linux_UnavailableNamesTheTunnelsOwnTwoGaps` — both Linux-gated), the other 3 execute. A worker reading "2 skips" and finding 5 names has **not** found a defect. Driving the skip count to 0 regresses the honesty SP-066 landed.

(n) **If the named flake `ChaosTunnelLoopbackTests.Logging_RouteClassesOnly_NeverFilenameOrQuery` fires in any run, record it by name with the run number and TRX path.** It guards a privacy boundary and has its own row; never retry it away, never quarantine it, never list it in `allowedSkips`.

(o) **Never export `CCP_DATA_ROOT` process-wide for a suite run** (`client/docs/port-workflow.md:204`). It makes the SP-057 pin skip and blinds the exact-count floor — the vacuous-green class SP-062 closed.

(p) **ENABLER 2: the worker does NOT edit `client/docs/task-board.md`, `client/docs/port-lessons.md`, `client/docs/upstream-sync.md`, or `client/docs/her-room-divergence-audit.md`.** Name intended filings in `record.md`; the orchestrator reconciles at land.

(q) **Product code is in scope — narrowly.** The licence covers the three filter sites and the one place each is applied. No opportunistic refactor, no renaming, no "while I was in there". Every product line you touch must be traceable to F1, F2 or F3.

## Dependencies

- **Task:** SP-067 (landed `75a09d61`) — the current floor, the wrapper contract, and the bite-test discipline this packet inherits.
- SP-060 (landed `eb1f60d4`) — `client/docs/her-room-divergence-audit.md`, the evidence this packet acts on. **The audit is evidence, not a decree**: it is the source of the rows and their sizes, never authority to adopt anything it verdicted `B` or `BLOCKED-ON-OWNER`.
- SP-040 / SP-046 / SP-047 — the landed companion memory store, chat surface and prompt-context assembly that F3's insertion point sits inside.

## Context to Read First

- `client/docs/her-room-divergence-audit.md` — rows **A6**, **A10**, **C3** in §3A/§3C; the sizing entries for the same three in §5; and §1 headlines **U3** (incognito is a hard drop, not a setting) and **U4**
- `client/src/CcpClient.Desktop/Ai/AiAwarenessService.cs` — `AiAwarenessContext` (`:172`), `AiAwarenessContextPackaging.TryPackage` (`:194`), `RunReactionAsync` (`:497`), `ObserveForegroundTitle` (`:531-547`), `AiWindowTitleCapability` (`:269+`) — **the F1/F2 site**
- `client/src/CcpClient.Desktop/Ai/AiOperationPipeline.cs:300-350` — output moderation (`:319`), memory persist (`:344`), reply application — **the F3 site**
- `client/src/CcpClient.Desktop/Ai/AiOperationVocabulary.cs:157+` — the `AiReply` cases (framing h)
- `client/src/CcpClient.Desktop/Ai/AiModerationBoundary.cs:110` — the `awareness-context-fields` surface registration
- `client/docs/ai-operation-contract.md` — §7 rule 1 (ordering), §12 (titles never enter diagnostics/logs/memory), §2 rule 6 (loopback-only endpoint admission) — **read-only, framing (c)**
- `ConditioningControlPanel/Services/Awareness/AwarenessPrivacyRules.cs` — `IncognitoMarkers`, `LooksIncognito`, `SanitizeTitleForWire`, `MaxTitleLength`, `EmailPattern`, `LongDigitsPattern` (framings d, f)
- `ConditioningControlPanel/Services/Awareness/AwarenessObserverPolicy.cs` — the second marker list, `IsIncognitoTitle`, the drop ordering and the blank-title rule (framings d, e)
- `ConditioningControlPanel/Services/AIService/AiTextHygiene.cs` — `StripUnsanctionedLinks` and its `AnyUrl` surface (`:147`); `UnwrapSpokenSigil` (`:89`) is **out of scope** (framing i)
- `ConditioningControlPanel/Services/Companion/Brain/CompanionBrain.cs:255-285, 350-360` — the application order and the "nothing survived" outcome (framings g, h)
- `client/tests/CcpClient.Tests/AiAwarenessTests.cs` and `client/tests/CcpClient.Tests/AiModerationCoverageTests.cs:118-135` — the landed bindings you must not weaken
- `client/tests/CcpClient.Tests/TestWait.cs` — the shared wait/budget helper; **add no waits outside it** (SP-063 discipline)
- `client/tests/floor/floor.json` and `client/tests/floor/check-floor.mjs` — the floor, its `bumpRule`, the 5 pinned skip names (framings l, m)
- `client/docs/port-workflow.md` — §Verification floor and the `CCP_DATA_ROOT` rule at `:204`
- `docs/constitution.md` — standing orders

## File Scope

- `client/src/CcpClient.Desktop/Ai/AiAwarenessService.cs` (F1 + F2 applied at the title seam)
- `client/src/CcpClient.Desktop/Ai/AiOperationPipeline.cs` (F3 applied at the reply seam)
- **At most one new file** under `client/src/CcpClient.Desktop/Ai/` holding the filter rules, if one shared home is the honest shape (justify it in the record; do not create three)
- `client/tests/CcpClient.Tests/**` (the new facts + any existing awareness/pipeline tests that legitimately move)
- `client/tests/floor/floor.json` (count bump only — framing l)
- `spine-tasks/SP-068-awareness-subtractive-filters/**`
- **NOT in scope:** any other path under `client/src/**`, `client/tools/**`, `client/spikes/**`, `ConditioningControlPanel/**`, `client/docs/**` (including `ai-operation-contract.md` — framing c), `docs/constitution.md`, `.spine/**`, `.pi/**`

## Contract

| Field | Value |
|-------|-------|
| testCommand | `node .spine/patches/verify.mjs && dotnet build client/CcpClient.sln -c Debug --nologo && node client/tests/floor/check-floor.mjs` |
| fileScopeMustChange | `client/src/CcpClient.Desktop/Ai/AiAwarenessService.cs` |
| fileScopeMustNotChange | `ConditioningControlPanel/**`, `client/spikes/**`, `client/tools/**`, `client/docs/**`, `docs/constitution.md`, `.spine/**`, `.pi/**` |
| artifactsMustExist | `spine-tasks/SP-068-awareness-subtractive-filters/record.md` |

`check-floor.mjs` is `--no-build` by design, so the explicit `dotnet build` ahead of it is **not optional** — standalone the wrapper measures the last build, not the working tree, and fails closed naming the wrong cause (SP-065 land finding). `FloorWrapperGuardTests` binds every packet with ID >= SP-065: **never** call `dotnet test` outside the wrapper.

## Review Level: 2 (Plan and Code)

Call `spine_review_step` after each step. Scored: blast radius 2 (three privacy-boundary filters across the awareness and reply paths, one of them inside the memory-persist path), novelty 1 (the port has **no** text-hygiene class today — grep-verified), security 1 (privacy-boundary code, even though every change narrows), reversibility 0 → **Level 2**. **T-2 heading format is load-bearing** — record engine-review presence/absence per call in `record.md`. **Authoring rule (SP-034 defect): `grep -c "Review Level" PROMPT.md` >= 2 before launch.** Review Level 2 applies to every step below.

## Steps

### Step 1: Re-derive the three behaviors from WPF and clear the boundary

- [ ] Update STATUS.md before starting work
- [ ] For **each** of F1/F2/F3: locate the WPF mechanism **by symbol name**, record the anchor you found beside the anchor this packet gave you, and note every divergence (framing a)
- [ ] F1: reconcile WPF's **two** incognito marker lists — agree or differ, and exactly how (framing d); decide the single port-side definition
- [ ] F1: determine empirically whether the port can return an empty/whitespace title as a **successful** capture, and decide the blank case explicitly with its reason (framing e)
- [ ] F2: transcribe the scrubber's exact values with their WPF cites; state the 120-char projection cap as deliberately not ported and why (framing f)
- [ ] F3: derive the insertion point against WPF's rule and the port's contract ordering; state what reaches memory before and after (framing g)
- [ ] F3: choose the typed outcome for an emptied reply from the **existing** vocabulary and justify it (framing h). If no existing case is honest, **stop and report** — do not add one
- [ ] **Boundary clearance (framing b), written out per filter:** what is observed, retained, transmitted, and logged before vs after. Every delta must be **less or equal**. Any "more" is a stop condition
- [ ] Confirm whether any filter needs `ai-operation-contract.md` wording; if so, name the exact wording needed and **leave the file untouched** (framing c)
- [ ] **Pre-approach solo consult** (`mode: "solo"` — a bare `consult` hits the council-roster trap, T-7; Opus 5 main, Fable 5 fallback). Verdict + **ACTUAL answering model** in `record.md`. **The tool has repeatedly returned reasoning-only or mid-sentence-truncated verdicts (waves 17, 21, 22, 23, and again at this packet's own authoring consult) — ask narrowly, cap the reply length, record exactly what surfaced, and never stitch a verdict out of reasoning**

### Step 2: Implement the three filters — narrowing only

- [ ] F1: one incognito definition, applied at the port's title seam **before** the title can be packaged or returned to a caller; WPF cite in a comment
- [ ] F2: the scrubber applied to the title with its verbatim values; WPF cite in a comment
- [ ] F3: `StripUnsanctionedLinks` ported (strip half only — framing i) and applied at the derived point, with the emptied-reply case typed per Step 1; WPF cite in a comment
- [ ] Each filter has **exactly one** definition and every consumer routes through it (SP-055 discipline). No second copy, no inline re-implementation
- [ ] **Nothing new is observed, persisted, logged or transmitted** (framing b). Prove it: grep your own diff for new log/diagnostic/persist calls and show the result in the record
- [ ] Summarize the `git diff` per product file in the record; confirm no edit outside F1/F2/F3 (framing q)

### Step 3: Bind all three, one source at a time

- [ ] Add regression facts for each filter: F1 drops on markers (and the blank case as decided), F2 strips emails / 6+ digit runs / control chars and caps length, F3 removes invented URLs and never lets stripped text reach memory or the bubble
- [ ] Include a **negative control per filter**: input that must pass through **unchanged**, so the pin cannot be satisfied by a filter that eats everything
- [ ] **BITE TEST, one filter at a time (framing j):** revert F1 alone → only F1's pins go red; then F2 alone; then F3 alone. Capture each RED under `evidence/` with the reverted source named. A shared revert is not acceptable evidence
- [ ] Confirm the landed awareness/moderation tests still assert exactly what they asserted before — **zero assertions weakened, zero tolerances widened**; prove it with a per-file `git diff` summary
- [ ] Bump `floor.json` `total` in the **same commit** as the new facts, reason in the message (framing l). `allowedSkips`, `admissionRule`, `skipSemantics` untouched

### Step 4: Record + pre-completion consult

- [ ] `record.md`: the per-filter WPF anchors (found vs given, framing a); the two-marker-list reconciliation; the blank-title decision and its evidence; the scrubber values with cites and the not-ported 120 cap; F3's insertion point with the before/after persistence statement; the emptied-reply typing with its justification; the per-filter boundary clearance table; the **bite matrix** (one revert per filter, each RED cited); the floor bump with its reason; the run table with exact counts and skipped names; consults + **ACTUAL answering models**; engine-review presence per step; intended board filings (state them, set no row state)
- [ ] **Honesty cell — required.** At minimum: (1) **F1 and F2 harden a path with no product consumer today** (framing k) — say it plainly and do not claim a user-visible improvement for them; F3's path **is** live; (2) which filters were verified by execution vs by reading; (3) that this set is **three rows of one audit table filtered at authoring**, not the audit's verdict on Her Room, and that row `:46` stays OPEN with its 12 owner questions unanswered; (4) the deferred halves by name (sigil unwrap, C3 title rewrite, the 120-char projection cap, and the three headed-evidence rows A11/D6/D11) with their reasons; (5) **Linux unproven** (zero WSL distros on this machine — do not fake a Linux run); (6) that a stripped/scrubbed value cannot be recovered downstream, i.e. these filters are lossy by design
- [ ] If the named flake fired in any run, it is recorded by name with run number and TRX path, and was **not** retried away (framing n)
- [ ] **Pre-completion solo consult**; verdict + actual model in `record.md`
- [ ] STATUS.md accurate before `.DONE`; intended board filings named per ENABLER 2 (set no state)

### Step 5: Testing & Verification

- [ ] Contract testCommand passes **through the wrapper** (`verify.mjs` exit 0, build 0W/0E, new exact unit count / 35 headless, skip set exactly the 2 Windows-observed pinned names)
- [ ] **3 consecutive full-suite greens**, >= 1 a **fresh-checkout first-ever build** ("cold" is a NEW worktree, not a rebuild in place — port-lessons 2026-08-12). Per-run table: run, worktree, cold/warm, unit + headless counts, skipped names, TRX path
- [ ] The bite matrix is complete: **three separate reverts, three separate REDs**, each naming the file reverted and the pins that went red — and confirming the other filters' pins stayed green
- [ ] `git diff --check` clean
- [ ] `git status --short` shows only File Scope paths
- [ ] `git status --porcelain --ignored=matching -uall` shows **no new ignored artifact** produced by any run

## Completion Criteria

- All three filters land, each with exactly one definition, each applied before the value it narrows can escape
- Every WPF anchor is re-derived by symbol and recorded found-vs-given; the two incognito marker lists are reconciled explicitly
- The blank-title case and the emptied-reply case are both **decided and justified**, not inherited
- The boundary clearance shows observed/retained/transmitted/logged is **less or equal** for every filter, with no new log, persist, or network call
- Each filter is bound by facts **and a negative control**, and proven to bite by **its own** revert — three reverts, three REDs
- `ai-operation-contract.md` is unedited; any wording it needs is named in the record for the orchestrator
- Zero assertions weakened, zero tolerances widened, nothing quarantined, nothing added to `allowedSkips`
- `floor.json` `total` bumped in the same commit as the facts that moved it, reason in the message
- 3 consecutive full-suite greens at the stated exact counts, >= 1 fresh-checkout first-ever build
- The record states the deferred halves by name and that F1/F2 harden a path with no product consumer yet

## Do NOT

- Add any observation, persisted field, log line, or network call — the packet is narrowing-only (framing b)
- Implement app-identity, deny-list contents, the app picker, the privacy dial, the activity ledger, or anything else the audit verdicted `B` or `BLOCKED-ON-OWNER` (rows A1/A4/A5/A7/A8/A9) — those need owner answers this packet does not have
- Edit `client/docs/ai-operation-contract.md` or any file under `client/docs/**` (framings c, p)
- Port `UnwrapSpokenSigil`, the C3 title rewrite, or the 120-char projection cap (framings f, i)
- Add a new `AiReply` case or change what an existing case means (framing h)
- Log, diagnose, or persist a title, a URL, or a stripped fragment — including in a test helper that writes to disk
- Accept a shared revert as bite evidence; each filter is proven by its own revert (framing j)
- Weaken, retry, quarantine, or allowlist any test; add anything to `allowedSkips`; touch `admissionRule`, `skipSemantics`, or the 5 pinned names (framings l, m)
- "Fix" the 2 Windows-observed skips or drive the skip count to 0 (framing m)
- Add waits outside `TestWait`, or a timeout literal that trips SP-063's `"Timeout = TimeSpan."` guard token without a marker + pin
- Refactor or rename beyond the three filter sites (framing q)
- Widen, disable, or special-case the floor to make one of your own steps pass (framing l)
- Call `dotnet test` outside `check-floor.mjs` (`FloorWrapperGuardTests` binds this packet)
- Export `CCP_DATA_ROOT` process-wide for a suite run (framing o)
- Create any new file under `client/tools/` (`.gitignore:168` — it would pass in-lane and vanish from the merged tree)
- Edit `client/docs/task-board.md`, `client/docs/port-lessons.md`, `client/docs/upstream-sync.md`, `client/docs/her-room-divergence-audit.md`, `docs/constitution.md`, `.spine/**`, `.pi/**`, `AGENTS.md`, `CLAUDE.md`; set any board row state
- Use `consult` council mode (T-7: solo only)
- Run `git clean -fdX` repo-wide in the lane — it deletes the T-14-staged `.pi/npm` that `verify.mjs` needs; clean **scoped** (`git clean -fdX -- client/`) and only after the contract passes
- Commit build output (`bin/`, `obj/`) or any results file

## Git Commit Convention

- `feat(SP-068): complete Step N — <summary>`

## Documentation Requirements

**Must Update:** `spine-tasks/SP-068-awareness-subtractive-filters/record.md`, `STATUS.md`
**Check If Affected:** `client/docs/ai-operation-contract.md` (**read-only for this packet** — if a filter needs contract wording, state the exact wording in `record.md` as a finding for the orchestrator; do not edit it)
**Explicitly NOT updated by the worker:** `client/docs/task-board.md`, `client/docs/port-lessons.md`, `client/docs/upstream-sync.md`, `client/docs/her-room-divergence-audit.md`, `docs/constitution.md`, `.spine/**`

## Amendments

- 2026-08-13 (authoring, orchestrator): **wave 25 runs this row ALONE.** The four remaining suite-hardening rows ride along with parity work rather than owning a wave (owner default in force: back to WPF parity, recorded in the wave-24 digest and CONTEXT). A second lane was rejected: any lane-mate that adds or removes a test collides on `floor.json` and the exact count — green alone, RED at merge (the SP-054/SP-058 class).
- 2026-08-13 (authoring, orchestrator): **decomposition consult (solo, Opus 5) — first call returned reasoning with no verdict (6th occurrence of the truncation class, waves 17/21/22/23 precedent); a narrow re-ask capped at 150 words surfaced the verdict cleanly.** Verdict: **proceed**, and *"an audit is not a decree" is not violated — the authorization is board row :46 (queue authority), not the audit, and narrowing a boundary is not adopting upstream's redesign; the decree the wave-17 land protected is replacing landed c1–c7 architecture, which three subtractive filters over existing paths are not.* Three conditions, all encoded: (1) do not call these "the board's six" — **C3 is a `MERGE` row, not `ADOPT`**, and the set was re-derived at authoring by filtering §5 → the provenance paragraph and framing (i); (2) any filter needing `ai-operation-contract.md` edited stops and is filed, policy text lands via the orchestrator (SP-059 precedent) → framing (c); (3) three filters = three bite tests, one source at a time (SP-067 precedent) → framing (j) and Step 3. **The advisor's checkable claim was verified before encoding, not trusted:** the audit's §3C row for C3 does carry verdict `M`.
- 2026-08-13 (authoring, orchestrator): **the audit's line cites are STALE and this was proven at authoring, not assumed** — `AwarenessObserverPolicy.cs:319-327` is `ResolveOwnProcessName()`, and `:277-279` is a `catch` returning `PolicyUnavailable`, not the blank-title drop. Every WPF offset in this packet is therefore a hint to re-derive by symbol (framing a). The semantics were each confirmed present at authoring by grep; only the offsets moved.
- 2026-08-13 (authoring, orchestrator): **the sizing pass over the four big v6.7 surfaces (Goon `:44`, FYP `:45`, Trainer Card `:51`, Haptics v2 `:52`) is DEFERRED, MACHINE-GATED — not dropped.** The wave-16 consult's constraint ("they stay undecomposed until a sizing pass follows", CONTEXT `:249/:261`) still stands. Three of those four are headed/payload-heavy and cannot be executed on this laptop (no DISPLAY3, no `Z:\CCP Vids`, zero WSL distros), so a sizing pass authored here would produce a plan that sits unexecuted. Recorded in CONTEXT and raised in the owner digest.
- 2026-08-13 (authoring, orchestrator): **Size S–M.** Three small filters, but the weight is evidence: per-filter boundary clearance, a negative control per filter, and three independent bite reverts. Not split into three packets — they share one audit, one authority chain and one floor bump, and splitting would triple the merge cost against `floor.json` for three sub-S rows.
- 2026-08-13 (authoring, orchestrator): **`spine preflight`'s `prelanded-file-scope` check passed BEFORE the authoring commit and fired as a WARNING immediately after it** (preflight still passed overall). Recorded exactly that way because the timing is the tell: the warning tracks whether the path exists in the commit range it compares, not whether the contract is real. The standing analysis still holds and is why the check is never obeyed blindly here: it compares `fileScopeMustChange` against **`main`** (`validate-prompt.mjs:196`), and `main` is the still-shipping WPF branch carrying **no `client/` tree at all**, while the contract verifier uses `baseBranch` from `.spine/spine-config.json` (`feat/crossplatform`). Following the warning's suggested hint (redirect `fileScopeMustChange` to delivery artifacts) would manufacture the contract-passes-on-docs-only class (SP-214/SP-457). **If it fires during this batch, do not act on it — `fileScopeMustChange` stays pointed at `AiAwarenessService.cs`.**
- 2026-08-13 (authoring, orchestrator): machine posture — laptop; **zero WSL distros, so Linux is a standing named gate** (do not fake a Linux run); **MCP 0/3 connected** (`avalonia-docs`/`avalonia-live` cached only, `avalonia-ui` not connected) — a named limit, never a blocker. This packet touches **no AXAML**, so the A-013 advisory step is not a gate for it. **`## Review Level: 2` heading present + grep-verified >= 2 (SP-034 authoring rule).**
- 2026-08-13 (authoring, orchestrator): **worker board-row obligation.** SP-001's gap recurred at SP-067 (its three-dot diff touched zero files under `client/docs/`). ENABLER 2 keeps `task-board.md` out of worker scope, so the row update is **budgeted into the land** by the orchestrator. Name your intended filings precisely in `record.md` — that text is what lands.
