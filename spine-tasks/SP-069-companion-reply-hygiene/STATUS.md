## STATUS: SP-069 — Companion reply hygiene: the model's raw text is not the user's text
**Current Step:** Complete (post-REVISE fix landed)
**Last Updated:** 2026-08-13 (worker, final-review REVISE addressed)
**Blockers:** none

**Post-`.DONE` engine reviews:** Step-5 code review APPROVE; final review **REVISE** — the H2
order pin provably passed under all 120 permutations of the five patterns. Fixed: the pin was
replaced with three permutation-distinguishing cases (`H2_OrderPin_ThreePermutationDistinguishers_ProveFixedOrder`),
`record.md` §2/§9 corrected, the R2 bite re-run (12 red, exactly the H2 set), floor bumped
993→995 in the same commit as the tests. Full contract chain re-run green twice after the fix
(995/995 unit, 35/35 headless, exactly the 2 pinned skips, build 0W/0E).

**Floor at authoring:** 946 unit / 35 headless / **2 skipped on Windows** (5 fully-qualified names pinned in
`allowedSkips`; 3 of them execute here, 2 are Linux-gated), build 0W/0E — SP-068, integrate `f2662cd0`.
**NEW EXACT COUNTS (this packet): 995 unit / 35 headless / 2 skipped** — bump `floor.json` `total` in the same commit
as the facts that moved it, with the reason in the message. `allowedSkips` is untouched.
(Originally 993; +2 from the post-REVISE order-pin replacement — one non-biting pin replaced by
three permutation-distinguishing cases.)

**The three layers in one line each (all at the reply seam, upstream of SP-068's link strip):**
- **H1 — reasoning-block strip + tokenizer artifacts:** `<think>`/`<thinking>`/`<reasoning>`/`<thought>`
  blocks (including an unterminated one), an orphan closing tag, `Ġ`→space, `Ċ`→newline. WPF `AiTextHygiene.Clean`.
- **H2 — metadata-tag strip:** five patterns **in a fixed order**, then whitespace collapse and trim. WPF
  `AiTextHygiene.StripMetadataTags`. **The port's own trigger is `AiAwarenessService.cs:229`** — the port sends
  the model `[Category: … | App: … | Title: … | Duration: …]` and strips nothing when it comes back.
- **H3 — envelope-leak DETECTION ONLY:** trimmed text starts `{` **and** contains `"response"` → the existing
  typed `AiReplyCodes.MalformedOutput`. **No lift, no unescape, no repair, no execution** — WPF salvages here
  and the port deliberately does not.

**THE HARD BOUNDARY — only ever remove text, and only ever refuse MORE.** Hygiene changes what the moderation
gate reads, and a gate that sees less can admit more (SP-068's F2 found exactly that class). The rule that
keeps this monotone: **moderation evaluates the UNION** — raw text *and* hygienic text, blocking if **either**
hits. `EvaluateOutput` was verified pure at authoring, so the second call cannot double-count. This diverges
from WPF (which moderates the sanitized text only) in the **fail-closed** direction; record it as a divergence.

**No truncation parity may be claimed.** WPF's same commit raised `MaxTokens` 100→350; **the port sends no
token cap at all**, so there is no port counterpart. Say so; do not imply one.

**`AiEnvelopeValidator` stays unwired.** Detecting a leak is not admitting an envelope. Wiring it would put
model text on the command-execution path — a different row and a widening.

**Four layers changed means four bite tests.** Revert H1, H2, H3 and the union rule one at a time and prove
only that layer's own pins go red. SP-067's land proved a shared revert falsely verifies pins never exercised.

### Step 1: Re-derive the three layers from WPF and prove the ordering is monotone — ✅ Complete
- [x] Update STATUS.md before starting work
- [x] Per layer: WPF mechanism located **by symbol name**; anchor found recorded beside anchor given; divergences noted (framing a)
- [x] H2: five patterns **and their order** transcribed; the case that demonstrates the order constructed — or the honest statement that no such case could be constructed
- [x] H1: block regex, orphan-closer rule and both character mappings transcribed verbatim; the unterminated-block case decided and justified
- [x] H3: two-part predicate confirmed; lift confirmed deliberately not ported; `AiReplyCodes.MalformedOutput` judged honest for the case — if not, **stop and report** (no new `AiReply` case)
- [x] Layer order derived from WPF and stated with its reason
- [x] **Union rule proven, not assumed:** `EvaluateOutput` verified pure; monotonicity argued in writing; adversarial cases constructed in **both** directions
- [x] Boundary clearance written per layer: observed / retained / transmitted / logged, before vs after — every delta **less or equal**
- [x] Any `ai-operation-contract.md` §7 rule 1 wording the change needs is **named, not written** (framing c)
- [x] Pre-approach solo consult (T-7: `mode: "solo"`, ask narrowly, cap the reply) — verdict + **ACTUAL answering model**; never stitch a verdict from reasoning (T-18)

### Step 2: Implement the three layers — removal only — ✅ Complete
- [x] `AiTextHygiene.cs` with exactly one definition per rule, WPF cite per rule, H2's order readable as ordered
- [x] H3 is detection only — no extraction, unescape, repair, execution, or call into `AiEnvelopeValidator`
- [x] Applied at the seam in the derived order, upstream of SP-068's link strip, which is unmodified
- [x] Union moderation rule implemented; **Block if either** raw or hygienic hits; first `SoftHit` taken and named in the record; output blocking stays non-escalating
- [x] Emptied-by-hygiene reply typed from the **existing** vocabulary and never appended to memory (SP-068's atomic turn-pair rule preserved)
- [x] Own-diff grep proves no new log / diagnostic / persist / network call; **no reply fragment, stripped tag, or leaked JSON is ever logged**
- [x] Per-file `git diff` summary in the record; no edit outside the two files

### Step 3: Bind all three, one source at a time — ✅ Complete
- [x] Regression facts per layer (H1 blocks + unterminated + orphan closer + both artifact chars; H2 all five shapes + collapse + trim; H3 detection + typed code)
- [x] **H2's ORDER pinned**, not just its members
- [x] **The port's own trigger pinned** — the `AiAwarenessService.cs:229` shape, echoed back, is stripped; line cited in the test
- [x] **Union rule pinned both ways** — forbidden token visible only in raw blocks; forbidden token visible only after hygiene blocks
- [x] **H3 non-lift pinned** — recoverable `"response"` text is not displayed, not persisted, not executed
- [x] **Negative control per layer**, byte-identical passthrough; includes a legitimate reply containing `{` and one containing a non-metadata bracketed phrase
- [x] **Bite matrix:** H1 alone → only H1's pins red; then H2; then H3; then the union rule. Each RED captured under `evidence/` naming the reverted source; others green
- [x] Landed pipeline/moderation tests unchanged in strictness — zero assertions weakened, zero tolerances widened; proven by per-file diff summary
- [x] `floor.json` `total` bumped in the **same commit** as the new facts, reason in the message; `allowedSkips` / `admissionRule` / `skipSemantics` untouched

### Step 4: Record + pre-completion consult — ✅ Complete
- [x] `record.md` complete per the packet's Step 4 list (anchors found-vs-given, transcribed patterns, H2 order case, layer order, **monotonicity argument + both adversarial cases**, the union recorded as a deliberate fail-closed divergence, H3 non-lift justification, boundary clearance table, bite matrix, floor bump, run table, consults + actual models, engine-review presence, intended board filings)
- [x] **Honesty cell** — including: no truncation parity claimed (the port sends no token cap); this is the **subtractive half** of WPF's fix and a leaked envelope yields **no reply** where WPF shows one; `AiEnvelopeValidator` still unwired; execution vs reading per layer; whether any **real** model output was exercised or only constructed fixtures; **Linux unproven**; hygiene is lossy by design
- [x] Named flake, if it fired, recorded by name + run number + TRX path — never retried away
- [x] Pre-completion solo consult; verdict + actual model recorded
- [x] STATUS.md accurate before `.DONE`; intended board filings named (set no row state)

### Step 5: Testing & Verification — ✅ Complete
- [x] Contract testCommand passes through the wrapper (`verify.mjs` exit 0, build 0W/0E, new exact unit count + 35 headless, skip set exactly the 2 pinned Windows-observed names)
- [x] 3 consecutive full-suite greens, >= 1 a **cold fresh-worktree first-ever build**; per-run table with counts, skipped names, TRX paths
- [x] Bite matrix complete: **four separate reverts, four separate REDs**, each naming the reverted file and the pins that went red, others green
- [x] `git diff --check` clean
- [x] `git status --short` shows only File Scope paths
- [x] `git status --porcelain --ignored=matching -uall` shows no new ignored artifact
