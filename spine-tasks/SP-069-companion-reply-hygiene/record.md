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

**Order case (framing d):** input `[Category: [Foo]: bar` (a truncated tag nested inside another —
the WPF comment documents truncation cutting a tag before its bracket).
- WPF order (closed passes first): `ClosedCategoryTag` consumes `[Category: [Foo]` → `: bar` survives.
- Permuted (unclosed passes first): `UnclosedKeyedTag` consumes `[Foo]: bar` (end-anchored) leaving
  `[Category: `, which `UnclosedKnownTag` then eats → `""`.
Different outputs → the order is semantic, and the order pin (Step 3) uses exactly this input.
The deep property: the end-anchored unclosed passes must see the string **after** closed tags are
gone, or they can "uncover" a new end fragment that a later unclosed pass then eats differently.

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

## 7. Consults

- **Pre-approach (solo)** — verdict: **proceed, one ordering correction**: H3 detection must also be
  a union (after H1 and after H2), else `[Category: …] {"response":…}` escapes detection and
  post-H2-only detection risks `UnclosedKeyedTag` eating the `"response"` tail. Also: raw SoftHit
  must not short-circuit a hygienic Block; compare the final text against the **raw** so
  hygiene-only changes persist; `StripUnsanctionedLinks`' own `\s{2,}` collapse joining tokens is a
  pre-existing SP-068 property — record, don't fix (that file is out of scope). Verdict surfaced
  cleanly, no stitching. **Actual answering model: `anthropic/claude-opus-5`** (configured solo
  roster in `bpx-consult.json`; the PROMPT's Opus 5 main).

## 8. Engine-review presence (T-2)

(filled per step as reviews return)
