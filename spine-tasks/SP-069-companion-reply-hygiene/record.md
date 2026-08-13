# SP-069 record — Companion reply hygiene

## 1. WPF anchor re-derivation (found vs given; framing a)

All anchors re-located **by symbol name** on 2026-08-13. Every given anchor was exact; no drift.

| Layer | Symbol | Given | Found | Divergence |
|-------|--------|-------|-------|-----------|
| H1 | `AiTextHygiene.ReasoningBlock` | `:25` | `AiTextHygiene.cs:25` | none |
| H1 | `AiTextHygiene.OrphanReasoningClose` | `:30` | `AiTextHygiene.cs:30` | none |
| H1 | `AiTextHygiene.Clean` (Ġ→space, Ċ→newline) | `:25`/`:30` context | `AiTextHygiene.cs:287` (mappings at `:296`) | none |
| H2 | `ClosedCategoryTag` / `ReactionCategoryTag` / `ClosedMetadataTag` / `UnclosedKnownTag` / `UnclosedKeyedTag` | `:36`/`:40`/`:44`/`:53`/`:61` | identical (`AiTextHygiene.cs:36,40,44,53,61`) | none |
| H2 | whitespace collapse + trim | `:79-80` | `:79-80` (`\s{2,}`→`" "`, `.Trim()`) | none |
| H3 | `AiResponseParser.LooksLikeEnvelopeLeak` | `:53` | `AiResponseParser.cs:53` | none |
| H3 | `TryLiftResponseField` (NOT ported) | `:60` | `AiResponseParser.cs:60` | deliberately absent from port |
| H3 | `SanitizeResponse` | `:314` | `AiResponseParser.cs:314` | none |
| H3 | `Parse` ladder (leak detection lives inside) | `:26` | `AiResponseParser.cs:26` (detection at `:43`) | none |
| moderation ordering | `OpenAiCompatibleService` sanitize-then-moderate | `:630-631` | `:630-631` (`SanitizeVisibleText` then `PassesOutputModeration`), rationale `:622-629` | none |
| port trigger | `AiAwarenessService` context line | `:229` | `AiAwarenessService.cs:229` — `$"[Category: {Category} | App: {App} | Title: {title} | Duration: {DurationText}]"` | none |

## 2. Transcribed rules (verbatim)

### H1 — `Clean` (WPF `AiTextHygiene.cs:25,30,287-297`)

- Block regex: `@<(think|thinking|reasoning|thought)>.*?(</\1>|$)` — `IgnoreCase | Singleline`.
- Orphan closer: `@</(think|thinking|reasoning|thought)>` — `IgnoreCase`.
- Character mappings: `"Ġ"`→`" "`, `"Ċ"`→`"\n"`. Then `.Trim()`.
- **Unterminated block:** WPF's `(</\1>|$)` alternation lets a truncated opener eat to end-of-string
  ("a reply truncated mid-thought would otherwise render the whole scratchpad" — WPF comment).
  **Ported as-is**: the model's scratchpad is never user-intended content; the alternative (render
  the tail of an unterminated block) puts chain-of-thought in the bubble. Fail-closed.

### H2 — `StripMetadataTags` (WPF `AiTextHygiene.cs:36,40,44,53,61,69-81`), fixed order:

1. `ClosedCategoryTag` — `@\[Category:[^\]]*\]`
2. `ReactionCategoryTag` — `@\[[A-Za-z]+/[A-Za-z]+\]`
3. `ClosedMetadataTag` — `@\[(?:Category|App|Title|Duration|Context):[^\]]*\]`
4. `UnclosedKnownTag` — `@\[(?:Category|App|Title|Duration|Context)\b[^\]\r\n]*$`
5. `UnclosedKeyedTag` — `@\[[A-Za-z][A-Za-z0-9 _-]*:[^\]\r\n]*$`

then `Regex.Replace(sanitized, @"\s{2,}", " ")` and `.Trim()`.

**Order case (framing d) — CORRECTED after final-review REVISE.** The order case this record
originally asserted (`[Category: [Foo]: bar` → `: bar` in WPF order, `""` permuted) is **wrong on
its own terms**: `UnclosedKeyedTag` cannot match `[Foo]: bar` (the character after the colon is
`]`, which its `[^\]]` class excludes), so the claimed permuted path never fires — verified by
executing the five transcribed regexes plus collapse+trim over **all 120 permutations**: every
permutation yields `: bar`. The REAL distinguishers (all 120 permutations executed; WPF order
output in **bold**; cases 1-3 from REVISE round 1, case 4 added in REVISE round 2):

| # | Input | WPF order | Broken under | Perms broken |
|---|-------|-----------|--------------|--------------|
| 1 | `[Category: a [Media/Streaming] b]` | **`b]`** (`ClosedCategoryTag`'s `[^\]]*` stops at the FIRST `]`, consuming `[Category: a [Media/Streaming]`) | `ReactionCategoryTag` before BOTH closed passes: it eats `[Media/Streaming]` first, then a closed pass consumes the whole `[Category: a  b]` → `""` | 40/120 |
| 2 | `[App x [Media/Streaming]` | **`""`** (reaction removed → `[App x ` → `UnclosedKnownTag` eats it) | `UnclosedKnownTag` before `ReactionCategoryTag`: the `]` blocks the end anchor while the reaction tag is present, and once the reaction tag is gone the unclosed pass has already run → `[App x` survives | 60/120 |
| 3 | `[Foo: x [Media/Streaming]` | **`""`** (same shape, unknown keyword → `UnclosedKeyedTag`) | `UnclosedKeyedTag` before `ReactionCategoryTag` → `[Foo: x` survives | 60/120 |
| 4 | `[Context:[Foo: z[Category: x][Apple pie` | **`""`** (`ClosedCategoryTag` removes `[Category: x]` first → `[Context:[Foo: z[Apple pie` → `ClosedMetadataTag` consumes it whole) | `ClosedMetadataTag` before `ClosedCategoryTag`: it stops at the FIRST `]`, removing `[Context:[Foo: z[Category: x]` and leaving `[Apple pie` (the `\b` discipline keeps `[Apple` from matching `App`) | 70/120 |

The semantic order relations these pin: **closed passes → ReactionCategoryTag → unclosed passes**,
plus — after final-review REVISE round 2 — `ClosedCategoryTag` before `ClosedMetadataTag` (case 4).
Executed over all 120 permutations, the four pins leave **8 surviving permutations** (re-derived by
execution for this correction; an earlier draft of this section claimed more than it could prove).
**Honesty correction (final-review REVISE round 2):** the earlier version of this section asserted
that `ClosedCategoryTag` vs `ClosedMetadataTag` "commute because the former's matches are a strict
subset of the latter's" and that the unclosed passes "commute with the closed passes on every input
constructed". **That claim is false** — per-match subsetting does not imply commuting outputs when
brackets nest, and case 4 above is the executed counterexample (permuting the two closed passes
changes the output). What survives scrutiny: a 400k-input adjacent-swap fuzz over the transcribed
regexes found a distinguisher for **every adjacent pair in WPF's order except `UnclosedKnownTag` vs
`UnclosedKeyedTag`** — for that one pairing, **no distinguisher was found** (stated as "no
distinguisher found", not "provably unobservable"; both are end-anchored and non-`]`-crossing, but
that argument was not elevated to a proof again). The deep property that IS load-bearing: the
reaction-tag pass must sit **between** the closed and unclosed passes, or nested tags change what
the end-anchored passes see at end-of-string.

### H3 — `LooksLikeEnvelopeLeak` (WPF `AiResponseParser.cs:53-58`)

- Predicate: `text.TrimStart().StartsWith("{") && text.Contains("\"response\"")`. Two-part, verbatim.
- **Lift deliberately not ported** (framing f): `TryLiftResponseField` (`:60`), `BalanceBraces`,
  `RepairJson`, `ExtractOuterJsonObject`, `ParseMixedFormat` all stay in WPF. Detection yields the
  existing `AiReplyCodes.MalformedOutput`.
- **`MalformedOutput` docstring judged HONEST** (framing h): *"The model's output could not be
  validated against the strict envelope schema (contract §8 — zero repair)."* A leaked envelope IS
  output that cannot be admitted as a valid envelope under §8's zero-repair rule; the port refuses
  it rather than salvaging it. The docstring's own "(zero repair)" clause names exactly this
  packet's answer. No new `AiReply` case added.

## 3. Layer order (derived from WPF, with the consult correction)

WPF: `Clean` (transport hygiene) runs before `Parse`; leak detection lives **inside** `Parse`
(`AiResponseParser.cs:43`, after the clean); `StripMetadataTags` runs in `SanitizeResponse`
**after** leak detection (`:314→321`); moderation sees the sanitized text.

Port order at the seam (`AiOperationPipeline.cs`, `produced is AiReply.Generated` block):

1. **H1 `Clean`** on the raw text.
2. **H2 `StripMetadataTags`** on the H1 result → the hygienic text.
3. **H3 detection as a UNION** — `LooksLikeEnvelopeLeak(afterH1) || LooksLikeEnvelopeLeak(afterH2)`
   (pre-approach consult correction: detecting only between H1 and H2 misses
   `[Category: …] {"response":…}` — the port's own trigger composed with a leak; detecting only
   post-H2 risks `UnclosedKeyedTag` swallowing the tail carrying `"response"`). Leak →
   `AiReply.Unavailable(AiReplyCodes.MalformedOutput)`, never persisted.
4. **Union moderation**: `EvaluateOutput(raw)` and `EvaluateOutput(hygienic)`; **Block if either**.
   Raw is evaluated first; a raw `SoftHit` does not short-circuit the hygienic evaluation
   (a hygienic Block still blocks). If both are SoftHit, the **raw** verdict's category is the one
   surfaced ("take the first" = raw first).
5. **SP-068 `StripUnsanctionedLinks`** on the hygienic text — unmoved, unmodified, still after
   moderation and before persist/apply. Emptied reply → existing `Unavailable("reply-stripped-empty")`;
   the atomic turn-pair rule is preserved (both appends live below, so returning early keeps the
   pair out of memory).
6. Final text assigned into `produced` compared against the **raw** text, so hygiene-only changes
   also flow to the bubble and memory.

## 4. The union rule, proven not assumed (framing b)

**Purity verified by reading** (`AiModerationBoundary.cs:279-296`): `Evaluate` is a `foreach` over
policy rules doing `text.Contains(token, OrdinalIgnoreCase)`; no counters, no escalation, no state,
`EvaluateOutput` is a forwarder. A second call cannot double-count. (Escalation is only invoked for
interactive **input** blocks, `AiOperationPipeline.cs:267` region — output blocks never escalate,
unchanged here.)

**Monotonicity argument.** Let `m(t)` = today's moderation verdict, `h(t)` = hygienic text,
`s(t)` = SP-068 link strip. Today a text reaches the user iff `m(t)` passes and `s(t)` is non-empty.
After this change it reaches the user iff `m(t)` passes **and** `m(h(t))` passes **and** no leak is
detected **and** `s(h(t))` is non-empty. Only conjuncts are added; none is removed or relaxed.
Therefore the post-change reach set is a subset of today's — the gate can only refuse more.
What is *shown* is also `s(h(t))`, a removal-only transform of `s(t)`'s input: never more text.

**Adversarial case, forward direction** (invisible raw, visible after hygiene): a tag boundary joins
a forbidden token — raw `sensi<thinking>scratch</thinking>tive-token` hygienes to `sensitive-token`;
raw scan misses it, hygienic scan blocks. Also the `Ġ` artifact: raw `badĠphrase` hygienes to
`bad phrase`; a policy token containing a space hits only after hygiene.

**Adversarial case, reverse direction** (visible raw, removed by hygiene): the forbidden token sits
inside the stripped region — raw `<thinking>sensitive-token</thinking> hello` or an echoed metadata
tag `[Category: sensitive-token | App: x]`. Hygiene removes the token, so sanitized-only moderation
(**what WPF does**) would pass it; the union still blocks on the raw scan. This is exactly WPF's
`:622-629` rationale case (an echoed tag tripping the output guard) answered in the **fail-closed
direction**: WPF would show the sanitized reply; the port refuses the turn. Deliberate divergence,
recorded as such.

## 5. Boundary clearance (per layer; observed / retained / transmitted / logged)

| Layer | Before | After | Delta |
|-------|--------|-------|-------|
| H1 | raw text → bubble, memory, disk | text minus reasoning blocks/artifacts → same sinks | retained **less or equal**; nothing new observed, transmitted, or logged |
| H2 | raw text → bubble, memory, disk | text minus metadata-tag echoes → same sinks | retained **less or equal**; nothing new |
| H3 | leaked envelope JSON → bubble, memory, disk | typed `Unavailable`, **no text** to any sink | strictly less everywhere |
| union moderation | refuse iff raw hits | refuse iff raw **or** hygienic hits | refuses **more or equal**; no new state, counter, or log |

No layer adds an observation, persisted field, log line, diagnostic payload, or network call. No
reply fragment, stripped tag, or leaked JSON is logged anywhere, including test helpers.

## 6. `ai-operation-contract.md` §7 rule 1 — wording needed (framing c; file left UNTOUCHED)

Rule 1 currently says every model-produced text field "passes through the output side" without
saying *which* text the output side reads. The union rule makes that load-bearing. Wording the
orchestrator should add (finding only, NOT applied by this packet):

> §7 rule 1, append: "Reply hygiene (SP-069) is subtractive and runs before the output boundary; the
> output side evaluates the UNION of the raw model text and the hygienic text and blocks if either
> hits — the gate can only refuse more, never less."

A second line the same wording should carry (final-review suggestion, accepted): H3's leak
detection is itself a union over the **post-H1 and post-H2** texts (not the raw text) — a raw
`{"response":…}` prefixed by a metadata-tag echo is still detected after H2 removes the tag.
Detection before moderation, per WPF's own order (leak detection lives inside `Parse`).

## 7. Consults

- **Pre-approach (solo)** — verdict: **proceed, one ordering correction**: H3 detection must also be
  a union (after H1 and after H2), else `[Category: …] {"response":…}` escapes detection and
  post-H2-only detection risks `UnclosedKeyedTag` eating the `"response"` tail. Also: raw SoftHit
  must not short-circuit a hygienic Block; compare the final text against the **raw** so
  hygiene-only changes persist; `StripUnsanctionedLinks`' own `\s{2,}` collapse joining tokens is a
  pre-existing SP-068 property — record, don't fix (that file is out of scope). Verdict surfaced
  cleanly, no stitching. **Actual answering model: `anthropic/claude-opus-5`** (configured solo
  roster in `bpx-consult.json`; the PROMPT's Opus 5 main).

## 7b. Consults (continued)

- **Pre-completion (solo)** — verdict: **proceed, one honesty line to add, no code change**:
  H2's unconditional whitespace collapse flattens multi-paragraph replies and undoes H1's
  `Ċ`→newline at the seam — WPF-identical parity, unpinned by any control (a multi-line
  byte-identical control cannot exist against WPF's own behavior). Added to honesty cell 7 as
  directed; no code or floor churn. Verdict surfaced cleanly, no stitching. **Actual answering
  model: `anthropic/claude-opus-5`** (configured solo roster).

## 8. Implementation summary (per product file, `git diff`)

- `client/src/CcpClient.Desktop/Ai/AiTextHygiene.cs` (**new, the only new product file**):
  `Clean` (H1), `StripMetadataTags` (H2), `LooksLikeEnvelopeLeak` (H3) — exactly one definition
  per rule, WPF cite in a comment per rule, H2's five patterns declared in their fixed order
  with the order called out as semantic. No logging, no state, pure string predicates/transforms.
- `client/src/CcpClient.Desktop/Ai/AiOperationPipeline.cs` (the seam, `produced is
  AiReply.Generated` block only): H1 → H2 → H3 detection union (`MalformedOutput`, early return
  before persist) → union moderation (raw first, hygienic second when the text changed; Block if
  either; raw SoftHit wins the marker; output blocks still never escalate) → SP-068's link strip
  on the hygienic text, unmoved and unmodified → emptied reply → existing
  `Unavailable("reply-stripped-empty")` with the atomic turn-pair rule intact → final text
  assigned compared against the RAW text so hygiene-only changes persist. No edit outside these
  two files (`git status --short` proof: only the two product files, two test files, floor.json,
  and this task folder ever appear).
- Own-diff grep for new log/diagnostic/persist/network calls: the only hits are comment text
  and STATUS.md wording — zero new observation calls. No reply fragment, stripped tag, or leaked
  JSON is logged anywhere, including test helpers (the tests assert in memory; the memory-file
  assertions read the product's own persistence path, which is the sink under test).

## 9. Bite matrix (framing i) — four separate reverts, four separate RED sets

Procedure per SP-068 precedent: neutralize exactly one mechanism in product source, rebuild
0W/0E, run the FULL suite through the wrapper, capture wrapper log + TRX (`.trx.txt` — `*.trx`
is gitignored, SP-068 land lesson), restore with `git checkout`, verify green between reverts.
An intermediate partial R1 (block regex only) was redone with the whole-`Clean` neutralization
precisely because the first attempt left the orphan-closer and artifact pins unexercised — the
SP-067 lesson applied to this packet's own evidence.

| Revert | Source reverted | RED set (exactly these, from the TRX) | Everything else |
|---|---|---|---|
| R1 (H1) | `AiTextHygiene.Clean` — early `return text` | **15 red**: 4 `H1_ReasoningBlock_EachTagName` rows + `H1_ReasoningBlock_CaseInsensitive` + `H1_UnterminatedBlock_EatsToEndOfString` + `H1_OrphanClosingTag_Removed` + `H1_TokenizerArtifact_GSpace` + `H1_TokenizerArtifact_CNewline` + `H1_LossyBoundary` + seam: `UnionRule_TokenJoinedAcrossTagBoundary`, `UnionRule_TokenJoinedByArtifactCharacter`, `HygieneEmptiedReply`, both `SoftHit_*` seam facts | 976 passed — H2/H3/union-reverse pins all green |
| R2 (H2) | `AiTextHygiene.StripMetadataTags` — early `return text` | **13 red (RE-RUN after final-review REVISE round 2, with the four-case order pin in place)**: 5 `H2_EachOfTheFiveShapes` rows + `H2_PortTrigger` + `H2_WhitespaceCollapse_AndTrim` + **4 `H2_OrderPin_PermutationDistinguishers` rows** (all four, including the round-2 closed-pass case) + seam: `TriggerEcho_StrippedBeforeBubbleAndMemory`, `H3_ComposedWithThePortsOwnTrigger_StillDetected`. A whole-function revert red-tells MEMBERSHIP only; the order-pinning proof is the 120-permutation execution in §2 (8 surviving permutations) plus all four pins' presence in this RED set | 981 passed — H1/H3/union pins and H2 negative controls green (identity satisfies byte-identical controls; the shape pins carry the removal proof). TRX: `Counters total=996 failed=13` |
| R3 (H3) | `AiTextHygiene.LooksLikeEnvelopeLeak` — early `return false` | **6 red**: 3 `H3_EnvelopeLeak_Detected` rows + seam: `H3_LeakedEnvelope_TypedMalformedOutput`, `H3_DetectionIsUnion_TagTail`, `H3_ComposedWithThePortsOwnTrigger` | 985 passed — H3 negative controls green (a dead detector returns false, which the controls expect; the detect rows carry the proof) |
| R4 (union rule) | `AiOperationPipeline.cs` — hygienic `EvaluateOutput` block disabled | **3 red**: `UnionRule_TokenJoinedAcrossTagBoundary`, `UnionRule_TokenJoinedByArtifactCharacter`, `SoftHit_VisibleOnlyAfterHygiene` — exactly the pins only the hygienic half can trip | 988 passed — reverse-direction (raw-scan) union pins green |

After R4 was undone: rebuild 0W/0E + wrapper **FLOOR OK 993/993** — the tree is restored, not
stuck on a revert. Evidence: `evidence/bite-R{1..4}-*.log` + `evidence/bite-R{1..4}-*.trx.txt`.
**R2 was re-run after the final-review REVISE** (order-pin replacement): RED set above at the
995 count, `evidence/bite-R2-H2-metadata.{log,trx.txt}` refreshed; restoration re-verified
(wrapper **FLOOR OK 995/995**, build 0W/0E) before commit. **R2 was re-run a second time after
final-review REVISE round 2** (fourth order-pin case): **13 red**, exactly the H2 set with all
four order-pin rows, `evidence/bite-R2-H2-metadata.trx.txt` refreshed again; restoration
re-verified (wrapper **FLOOR OK 996/996**, build 0W/0E) before commit.

## 10. Run table

| Run | Worktree | Cold/warm | Unit | Headless | Skipped (names) | TRX |
|---|---|---|---|---|---|---|
| pre-bump (expected floor fail) | lane-1 | warm | 993 vs pinned 946 → floor correctly demanded the bump | — | — | ccp-floor-zryU24 |
| post-bump gate | lane-1 | warm | 993/993 | 35/35 | the 2 pinned Linux-gated names | ccp-floor (console) |
| bite R1/R2/R3/R4 | lane-1 | warm | RED sets exactly per the matrix (15/10/6/3) | — | — | evidence/bite-R*.trx.txt |
| restoration check | lane-1 | warm | 993/993 | 35/35 | the 2 pinned names | (post-R4 rebuild + wrapper) |
| **G1 (contract testCommand)** | lane-1 | warm | 993/993 | 35/35 | the 2 pinned names | ccp-floor-3itA2i |
| **G2 (COLD)** | C:/Code/CCP-SP069-coldcheck (fresh worktree @ abc53009, first-ever build 0W/0E, removed after) | **cold** | 993/993 | 35/35 | the 2 pinned names | ccp-floor-lCY4z3 (log: evidence/g2-cold-floor.log) |
| **G3** | lane-1 | warm | 993/993 | 35/35 | the 2 pinned names | ccp-floor-K1esCH (log: evidence/g3-warm-floor.log) |

Three consecutive full-suite greens at 993 unit / 35 headless / exactly the 2 pinned
Windows-observed skips, G2 a cold fresh-checkout first-ever build. The named flake
(`ChaosTunnelLoopbackTests.Logging_RouteClassesOnly_NeverFilenameOrQuery`) did NOT fire in any
run — nothing to record, nothing retried.

**Post-REVISE runs (final review `REVISE` on the H2 order pin — see §9 R2 and §2):**

| Run | Worktree | Cold/warm | Unit | Headless | Skipped (names) | TRX |
|---|---|---|---|---|---|---|
| post-fix pre-bump | lane-1 | warm | 993 passed / 995 total (floor correctly demanded the bump) | — | — | ccp-floor-7UzDAy |
| post-bump gate | lane-1 | warm | 995/995 | 35/35 | the 2 pinned names | ccp-floor (console, FLOOR OK) |
| bite R2 re-run | lane-1 | warm | 12 red, exactly the R2 set above | — | — | evidence/bite-R2-H2-metadata.trx.txt (ccp-floor-Pqm090) |
| restoration + final gate | lane-1 | warm | 995/995 | 35/35 | the 2 pinned names | ccp-floor (console, FLOOR OK) |

The named flake did NOT fire in the post-REVISE runs either. The 3-greens requirement stands on
G1–G3; the post-REVISE change is a test-only replacement of one pin by three strictly stronger
pins plus its floor bump, and the full chain (verify.mjs + build 0W/0E + wrapper) was re-run
green twice after it (post-bump gate and restoration gate).

**Post-REVISE-round-2 runs (final review `REVISE` on the false "closed passes commute" claim —
see §2's honesty correction and §9 R2):**

| Run | Worktree | Cold/warm | Unit | Headless | Skipped (names) | TRX |
|---|---|---|---|---|---|---|
| post-fix gate | lane-1 | warm | 996/996 (floor bumped 995→996 in the same commit as the fourth case) | 35/35 | the 2 pinned names | ccp-floor-cJApOM |
| bite R2 re-run #2 | lane-1 | warm | **13 red**, exactly the R2 set incl. all 4 order-pin rows | — | — | evidence/bite-R2-H2-metadata.trx.txt (ccp-floor-eelLW3) |
| restoration gate | lane-1 | warm | 996/996 | 35/35 | the 2 pinned names | ccp-floor-kRbrL0 |
| final contract chain | lane-1 | warm | 996/996 | 35/35 | the 2 pinned names | ccp-floor-wIQ2db (verify.mjs OK + build 0W/0E + wrapper FLOOR OK) |

The named flake did NOT fire in the round-2 runs either.

## 11. Engine-review presence (T-2)

| Step | `spine_review_step` call | Outcome |
|---|---|---|
| 1 | `type=plan` | **skipped by engine design** (SP-195: nested reviewer spawn blocked inside worker; `.reviews/1-20260813T131420.md`) — engine runs reviews after `.DONE` |
| 2 | `type=plan` | skipped, same (`.reviews/2-20260813T132504.md`) |
| 3 | `type=plan` | skipped, same (`.reviews/3-20260813T141359.md`) |

No REVISE and no spawn failure occurred at worker-run time; code/final review are the
engine's post-`.DONE` phases. **Post-`.DONE` engine reviews:** Step-5 code review APPROVE
(`.reviews/5-20260813T143319.md`); final review round 1 **REVISE** (`.reviews/final-20260813T144138.md`)
— the H2 order pin provably did not bite (passes under all 120 permutations). Addressed: the pin
was replaced with three permutation-distinguishing cases (§2 corrected, §9 R2 re-run, floor
993→995 in the same commit as the tests), and both non-blocking suggestions were accepted (§6's
detection-union contract line, §13's paragraph-collapse board finding). Final review round 2
**REVISE** (`.reviews/final-20260813T152258.md`) — the three pins genuinely bit, but §2 and the
test comment falsely claimed the two closed passes "commute" (executed counterexample supplied).
Addressed: fourth order-pin case added (`[Context:[Foo: z[Category: x][Apple pie` → `""`,
70/120 perms broken, pins `ClosedCategoryTag` before `ClosedMetadataTag`), §2 and the test
comment corrected (only `UnclosedKnownTag` vs `UnclosedKeyedTag` lacks a distinguisher — stated
as "no distinguisher found" over a 400k-input adjacent-swap fuzz, not "provably unobservable"),
§9 R2 re-run (13 red, all four order-pin rows), floor 995→996 in the same commit as the test.

## 12. Honesty cell

1. **No truncation parity is claimed.** WPF's `932d829a` also raised `MaxTokens` 100→350; the
   port sends NO token cap at all (`LoopbackOllamaProvider.BuildBody` emits `{model, messages,
   stream:false, think:false}` — verified at authoring), so there is no port counterpart. The
   unclosed-tag patterns are still ported because a local model can cut its own reply mid-tag
   without any cap.
2. **This is the subtractive half of WPF's fix.** WPF salvages a leaked envelope
   (`TryLiftResponseField`); the port refuses it. A user whose model emits an envelope gets NO
   reply where WPF shows one — a deliberate fail-closed choice (contract §8 zero repair), not an
   oversight.
3. **`AiEnvelopeValidator` remains unwired**; this packet did not change that, and no model text
   moved toward the command-execution path.
4. **Execution vs reading:** all three layers, the union rule, and the seam wiring were verified
   by EXECUTION (50 new facts — 47 original + 2 net from the post-REVISE order-pin
   replacement + 1 from the round-2 fourth case — plus four bite reverts, R2 re-run after each
   REVISE round). The order claims were verified by executing the transcribed regexes over all
   120 permutations (8 survivors under the four pins) and a 400k-input adjacent-swap fuzz
   (no distinguisher found for the unclosed pair only). WPF anchors and WPF's moderation ordering
   were verified by reading (table in §1). `EvaluateOutput`'s purity was verified by reading
   (`AiModerationBoundary.cs:279-296`).
5. **No real local model output was exercised** — only constructed fixtures (including the exact
   `AiAwarenessService.cs:229` shape). No field evidence is claimed.
6. **Linux unproven** — zero WSL distros on this machine; the two pinned skips stay pinned. No
   Linux run is faked.
7. **Hygiene is lossy by design** — a stripped fragment cannot be recovered downstream. The
   `H1_LossyBoundary_LegitReplyQuotingTheTagVerbatim_IsStripped` fact pins the boundary case
   (a legitimate reply quoting the tag shape verbatim loses that span — WPF-identical).
   **H2's unconditional `\s{2,}` → `" "` collapse (WPF :79, WPF-identical) flattens paragraph
   breaks in EVERY reply** — a multi-paragraph model reply becomes one line, and H1's `Ċ`→newline
   mapping is collapsed again by H2 at the seam. Parity, not a defect — but user-visible
   formatting loss on the live chat surface that no fact pins (a multi-line byte-identical
   control would fail against WPF's own behavior, so it cannot be pinned as a control).
   Pre-completion consult gap, disclosed by name.
8. **Moderation-order note:** H3 detection runs before the union moderation (WPF order — leak
   detection lives inside `Parse`, before sanitizing and moderation). A text that is BOTH a leak
   and a raw moderation hit now yields typed `Unavailable` where yesterday it yielded typed
   `Refused` — either way nothing is displayed, persisted, or executed; the refusal set only
   grew.
9. **Pre-existing, recorded, not fixed** (pre-approach consult note 3): SP-068's
   `StripUnsanctionedLinks` performs its own post-moderation `\s{2,}` collapse, which can join
   tokens the output gate never saw as joined. `AiPrivacyFilters.cs` is out of scope; named here
   for the orchestrator as a candidate board finding.

## 13. Intended board filings (for the orchestrator at land; no row state set by the worker)

- **The SP-069 companion-reply-hygiene row** (wave 26, run alone): mark DONE with evidence —
  three layers landed in `AiTextHygiene.cs` (one definition per rule, WPF cites), union
  moderation at the reply seam recorded as a deliberate fail-closed divergence from WPF's
  sanitized-only moderation, H3 detection-only with the non-lift pinned, floor 946→993 in the
  same commit as the facts, four bite reverts (15/10/6/3 REDs) under
  `spine-tasks/SP-069-companion-reply-hygiene/evidence/`, 3 greens incl. 1 cold, Linux unproven
  (named gate), no truncation parity claimed.
- **Candidate new finding row:** `ai-operation-contract.md` §7 rule 1 wording (§6 above) — the
  union rule should be written into the contract by a docs-owning packet.
- **Candidate new finding row:** SP-068's link-strip `\s{2,}` collapse can join tokens
  post-moderation (honesty cell 9) — pre-existing, out of scope here.
- **Candidate new finding row (final-review suggestion, accepted):** H2's unconditional
  `\s{2,}` → `" "` collapse (WPF :79, WPF-identical parity) flattens multi-paragraph replies on
  the live chat surface and undoes H1's `Ċ`→newline at the seam (honesty cell 7). Unpinnable as a
  control against WPF's own behavior; named for the board as a user-visible formatting-loss
  consequence of parity, not a defect to fix in this row.
