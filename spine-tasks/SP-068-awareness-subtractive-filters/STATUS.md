## STATUS: SP-068 — Three subtractive privacy filters over the landed awareness and reply paths
**Current Step:** Step 1 — Re-derive the three behaviors from WPF and clear the boundary
**Last Updated:** 2026-08-13 (worker, step 1 in progress)
**Blockers:** none

**Floor at authoring:** 903 unit / 35 headless / **2 skipped on Windows** (5 fully-qualified names pinned in `allowedSkips`; 3 of them execute here, 2 are Linux-gated), build 0W/0E — SP-067, integrate `75a09d61`.
**NEW EXACT COUNTS (this packet):** to be recorded by the worker — this packet adds facts; bump `total` in the same commit as the tests that move it.

**The three filters in one line each:**
- **F1 (audit row A6, ADOPT):** private/incognito window titles are dropped before anything is counted or packaged — WPF has **two** marker lists to reconcile into **one** port definition.
- **F2 (audit row A10, ADOPT):** emails, `\d{6,}` runs and control chars stripped, whitespace collapsed, capped at **80** — verbatim values, WPF-cited.
- **F3 (audit row C3, MERGE — strip half only):** model-invented URLs removed from companion prose **before** it reaches the bubble, history, or disk.

**The hard boundary:** every change **narrows**. Nothing new is observed, persisted, logged, or transmitted. If a change adds a datum rather than removing one, it is out of scope — stop and report.

**Three filters mean three bite tests.** Revert one filter at a time and prove only its own pins go red. SP-067's land proved a shared revert falsely verifies pins that were never exercised.

### Step 1: Re-derive the three behaviors from WPF and clear the boundary — 🔄 In progress
- [x] Update STATUS.md before starting work
- [x] Per filter: WPF mechanism located **by symbol name**; anchor found recorded beside anchor given; divergences noted (framing a — the audit's cites are already proven stale)
- [x] F1: WPF's two incognito marker lists reconciled (agree or differ, exactly how); single port-side definition decided
- [x] F1: empirically established whether an empty/whitespace title can be a **successful** capture in the port; blank case decided explicitly with its reason
- [x] F2: scrubber values transcribed with WPF cites; the 120-char projection cap stated as deliberately not ported, with the reason (no cloud provider — admission §2 rule 6)
- [x] F3: insertion point derived against WPF's rule and the port's contract ordering; what reaches memory before vs after stated
- [x] F3: emptied-reply outcome chosen from the **existing** `AiReply` vocabulary and justified; if none is honest → **stop and report**
- [x] Boundary clearance written per filter: observed / retained / transmitted / logged, before vs after — every delta **less or equal**
- [x] Any `ai-operation-contract.md` wording the filters need is **named, not written** (framing c)
- [x] Pre-approach solo consult (T-7: `mode: "solo"`, ask narrowly, cap the reply) — verdict + **ACTUAL answering model**; never stitch a verdict from reasoning

### Step 2: Implement the three filters — narrowing only — ⬜ Not started
- [ ] F1 applied at the port's title seam before the title can be packaged or returned; WPF cite in a comment
- [ ] F2 applied to the title with verbatim values; WPF cite in a comment
- [ ] F3 strip applied at the derived point with the emptied-reply case typed; WPF cite in a comment
- [ ] Exactly **one** definition per filter; every consumer routed through it (SP-055 discipline)
- [ ] Own-diff grep proves no new log / diagnostic / persist / network call
- [ ] Per-file `git diff` summary in the record; no edit outside F1/F2/F3

### Step 3: Bind all three, one source at a time — ⬜ Not started
- [ ] Regression facts per filter (drop, strip, cap, URL removal, and stripped text never reaching memory or the bubble)
- [ ] **Negative control per filter** — input that must pass through unchanged, so a filter that eats everything cannot satisfy the pin
- [ ] **Bite matrix:** F1 reverted alone → only F1's pins red; then F2; then F3. Each RED captured under `evidence/` naming the reverted source
- [ ] Landed awareness/moderation tests unchanged in strictness — zero assertions weakened, zero tolerances widened; proven by per-file diff summary
- [ ] `floor.json` `total` bumped in the **same commit** as the new facts, reason in the message; `allowedSkips` / `admissionRule` / `skipSemantics` untouched

### Step 4: Record + pre-completion consult — ⬜ Not started
- [ ] `record.md` complete per the packet's Step 4 list (anchors found-vs-given, marker reconciliation, blank-title decision, scrubber values + not-ported cap, F3 insertion point + persistence delta, emptied-reply typing, boundary clearance table, bite matrix, floor bump, run table, consults + actual models, engine-review presence, intended board filings)
- [ ] **Honesty cell** — including: F1/F2 harden a path with **no product consumer today** (F3's path is live); execution vs reading per filter; this is **three rows of one audit table filtered at authoring**, not a verdict on Her Room, and row `:46` stays OPEN; deferred halves named (sigil unwrap, C3 title rewrite, 120-char cap, headed rows A11/D6/D11); **Linux unproven**; filters are lossy by design
- [ ] Named flake, if it fired, recorded by name + run number + TRX path — never retried away
- [ ] Pre-completion solo consult; verdict + actual model recorded
- [ ] STATUS.md accurate before `.DONE`; intended board filings named (set no row state)

### Step 5: Testing & Verification — ⬜ Not started
- [ ] Contract testCommand passes through the wrapper (`verify.mjs` exit 0, build 0W/0E, new exact counts, skip set exactly the 2 Windows-observed pinned names)
- [ ] **3 consecutive full-suite greens**, >= 1 a fresh-checkout first-ever build; per-run table (run, worktree, cold/warm, counts, skipped names, TRX path)
- [ ] Bite matrix complete: three separate reverts, three separate REDs, other filters' pins green each time
- [ ] `git diff --check` clean
- [ ] `git status --short` shows only File Scope paths
- [ ] `git status --porcelain --ignored=matching -uall` shows no new ignored artifact
