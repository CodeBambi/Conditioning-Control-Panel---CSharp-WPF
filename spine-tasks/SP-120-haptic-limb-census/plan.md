# SP-120 — plan: the mapping METHOD, committed before the first mapping

Branch `lane/SP-120-haptic-limb-census`, base `4276249e`. Pin **2247 unit / 141 headless**.

**Nothing has been mapped yet.** No upstream call site has been enumerated, no verdict has been
assigned, and no vocabulary option has been costed. This document exists so that when those things
happen they are the output of a stated procedure rather than of my judgement in the moment — which is
the ordering SP-116 set, SP-117 made standard and SP-118 and SP-119 followed.

What HAS been read to write this document, and it is deliberately the smallest set that lets a method
be written without pre-judging its answers: this packet's `PROMPT.md`;
`spine-tasks/SP-119-haptic-seam/record.md` (§2.3, §2.4, §5, §6 — the seam's shape and the list this
packet must not inherit); `client/src/CcpClient.Desktop/Haptics/IHapticSink.cs` (the whole file — the
seam's vocabulary is the thing being priced against, so it must be known exactly); the directory
listings of `ConditioningControlPanel/Services/{Flash,Video,Subliminal,Haptics}` and
`client/src/CcpClient.Desktop/{Effects,Haptics}`; `client/docs/wpf-surface-reachability.md` rows
D191-D202 (so my new rows do not collide and do not re-file D197);
`client/tests/CcpClient.Tests/UpstreamPayloadInventoryTests.cs:1-80` (the repo-root anchoring pattern
a pin against the shipping tree has to reuse).

**Not yet opened:** `FlashService.cs`, `VideoService.cs`, `VideoService.Browser.cs`,
`SubliminalService.cs`, `HapticService.cs`, `HapticMixer.cs`, and every file under
`client/src/CcpClient.Desktop/Effects/`. The enumeration starts against those with the rules below
already fixed.

---

## 0. WHAT THIS PACKET PRODUCES, AND THE LINE IT STOPS AT

Produces: a mapping document, a mechanical pin for it, divergence rows, a priced menu of at least
three vocabulary options with one recommendation, and a record.

Does not produce: **one line of product code.** `client/src/**` is closed. The stop rule is a diff
assertion rather than an intention — at the end,
`git diff --stat <base>..HEAD -- client/src client/tools ConditioningControlPanel client/tests/floor`
must be **empty**, and I will run it and quote it in `record.md`. If the census concludes that a limb
needs a product change, the change is *described* in the record as the next packet's work item.

---

## 1. THE UNIVERSE — which bytes the enumeration runs over

The packet says "the whole `Services/{Flash,Video,Subliminal}` family". Taken literally by directory
listing, that family is **eleven** files, not three:

| file | bytes |
|---|---|
| `ConditioningControlPanel/Services/Flash/FlashService.cs` | 206415 |
| `ConditioningControlPanel/Services/Video/VideoService.cs` | 417368 |
| `ConditioningControlPanel/Services/Video/VideoService.Browser.cs` | 37020 |
| `ConditioningControlPanel/Services/Video/DualMonitorVideoService.cs` | 26302 |
| `ConditioningControlPanel/Services/Video/AttentionTargets.cs` | 29456 |
| `ConditioningControlPanel/Services/Video/ScreenMirrorService.cs` | 5307 |
| `ConditioningControlPanel/Services/Video/VideoDiag.cs` | 24348 |
| `ConditioningControlPanel/Services/Video/VideoMetadataCache.cs` | 15863 |
| `ConditioningControlPanel/Services/Video/WallpaperService.cs` | 31104 |
| `ConditioningControlPanel/Services/Subliminal/SubliminalService.cs` | 64845 |
| `ConditioningControlPanel/Services/Subliminal/BouncingTextService.cs` | 42343 |

The two prior counts were derived over three of these, and the correction the packet already knows
about (`VideoService.Browser.cs`) is precisely a file that was outside the searched set rather than a
call that was misread. **The way to stop that happening a third time is to search the whole directory
family and let the alphabet, not the file list, decide what is a site.** So the enumeration runs over
`ConditioningControlPanel/Services/{Flash,Video,Subliminal}/**/*.cs` — every file, including
`Video/Browser/**` if it holds `.cs` files.

**Two widenings beyond the packet's literal scope, both stated so the number is honest about what it
counts:**

- **A whole-tree sweep runs as a CHECK, not as part of the count.** `grep -rn` for the same alphabet
  over all of `ConditioningControlPanel/**/*.cs`, excluding `CCP.*`, `Services/Haptics/**`,
  `Tests/**` and `obj/**`. Any commanding site found OUTSIDE the three-service family is reported as
  an out-of-scope finding with its file and line — it does not silently join the total, and it does
  not silently disappear either. This is the only way to say "fourteen" and mean "fourteen in the
  family, and here is what is outside it".
- **Ported-module membership is a column, not a filter.** A site in `WallpaperService.cs` counts as a
  site in the family even if the port has no wallpaper module; its verdict is then decided by §4 like
  any other, and being unported is a *reason for a verdict*, never a reason to leave a row out.

---

## 2. THE ALPHABET — how a site is FOUND, so that no site can hide behind a name I did not guess

Derived in this order, and every step is mechanical:

1. **Read the app-scope entry point's public surface.**
   `ConditioningControlPanel/Services/Haptics/HapticService.cs` is reached from services as
   `App.Haptics` (SP-119 §5's citations are of that form). Extract every `public` member name from it
   by grep. That list — not a remembered list of four or five method names — is the primary alphabet.
2. **Add every other type reachable through those members** that is itself commanded: whatever
   `App.Haptics.<Property>` exposes (SP-119 names `FunScript` as one such), enumerated the same way.
3. **Add a defensive case-insensitive needle set** so a call that does not go through `App.Haptics`
   at all still surfaces: `haptic`, `vibe`, `vibrat`, `buzz`, `pulse`, `toy`, `lovense`, `buttplug`,
   `funscript`, `HapticLayer`, `SetLayer`.
4. **Grep every file in §1 for every needle, case-insensitively, recording file+line+text.** The
   union of hits is the CANDIDATE set. Nothing is removed from the candidate set silently; every
   candidate line ends up in exactly one bucket in §3, and the census document carries all of them.

`pulse` and `toy` will over-match (WPF has pulsing animations, and "toy" occurs inside unrelated
identifiers). Over-matching is the intended failure direction: a false positive costs a row in the
"accounted for, not a command" table; a false negative is how a count gets corrected for a third
time.

---

## 3. THE COMMAND TEST — which candidates are SITES

A candidate line is a **command site** when all three hold, each decided by looking at the line and
its enclosing statement:

- **C1 — it is a CALL, evaluated for effect.** A method invocation (or an awaited one) whose target
  is the haptic subsystem. A field declaration, a `using`, a type name in a signature, a comment, a
  `?.` null-check of the service itself, or an event *subscription* is not a call evaluated for
  effect.
- **C2 — it flows OUTWARD.** The app is telling the haptic subsystem to do something. A read of a
  setting or of state (`App.Settings.Current.Haptics.X`, `App.Haptics.IsConnected`) flows inward and
  is not a command. An event handler being *registered* against toy input flows inward: the toy is
  driving the app.
- **C3 — its effect is a change in what a device is asked to do.** Directly (start, set a layer, run
  a ladder, stop) or by handing a driving program a script to play. A call that only announces, logs
  or updates a readout is not a command by C3 — and where upstream welds an announce to a play, both
  halves are recorded, the play as the command and the announce as its adjacent row, so that neither
  is silently dropped.

Every candidate is filed into exactly one of five buckets, and **all five appear in the census
document** so the total is auditable:

| bucket | rule |
|---|---|
| `command` | C1 and C2 and C3. **These are the sites, and their count is the number.** |
| `adjacent` | C1 and C2, but not C3; or a command into a subsystem the port has not ported at all (a script player). Named with the reason it is not counted. |
| `inbound` | fails C2: toy input, subscriptions, state reads. |
| `settings` | a read of haptic configuration. |
| `noise` | the alphabet over-matched; not haptics at all. |

**Counting rule, stated before I count:** one site = **one syntactic call expression** in the
shipping source. Two sites that are mutually exclusive branches of one runtime decision are **two
sites**, because they are two lines somebody has to either port or refuse; whether the port merges
them is a *verdict* (`collapsed`, §4), never a subtraction from the upstream count. A single call
inside a loop is one site. A helper called from two places is **one site per call site of the
helper**, and the helper's own body is not a site — otherwise the same command is counted twice.

**Reconciliation obligation.** The packet asserts fourteen. My number is produced by the rules above
without reference to it, and then reconciled: for each of the packet's fourteen, is it in my set; for
each of mine, is it in the packet's. Every asymmetry is named with its bucket and its reason. **If my
number is not fourteen, that is the headline and it goes at the top of the record**, whichever
direction it is out by.

---

## 4. THE PORT TRIGGER POINT — decided by three tests, so a verdict is not a judgement call

A **port trigger point** is a *specific existing statement location* in
`client/src/CcpClient.Desktop/**`, cited as `File.cs:line`, at which the same user-observable moment
occurs as the one that fires the upstream site. Not a class, not a module, not "somewhere in
FlashImagesEffect" — a line that exists today, in the shipping port, and that I can quote.

Three tests, each answerable only by citation on both sides:

- **T1 — MOMENT IDENTITY.** Name the moment in user-observable terms ("a flash image appears", "a
  subliminal phrase is shown", "the video starts playing"). Cite the upstream statement that
  establishes the moment (the enclosing method plus any guard that decides whether the site runs) and
  cite the port statement at which the same moment occurs. If the moment cannot be stated without
  referring to a mechanism (a timer tick, a WPF `Storyboard`), it is restated in outcome terms until
  it can, per the port's standing rule that outcomes port and mechanisms do not.
- **T2 — REACHABILITY IN THE SHIPPING PORT.** The cited port statement runs in a default build.
  Evidenced by an existing test that executes it, or by its being on the module's only delivery path.
  A statement that exists but is reachable only behind an unported feature fails T2.
- **T3 — NO NEW DECISION SMUGGLED IN.** Placing a call at that point requires no policy this port has
  not already decided. If it needs an intensity, a duration, a mode or a priority that the port has
  nowhere to get, T3 fails — and that failure is the whole point of §5, not a defect in the mapping.

Verdicts are exhaustive, mutually exclusive, and each carries **one citation per side**:

| verdict | conditions |
|---|---|
| `present` | T1 and T2 hold, and exactly one port statement is the counterpart. T3 recorded separately as pass/fail. |
| `collapsed` | T1 holds, but N>1 upstream sites share ONE port statement because the port merged the branches. The row names the N upstream lines and the single port line, and says which upstream distinction is lost. |
| `absent-by-decision` | no port counterpart exists, **and I can name the recorded decision that removed it** by `client/src/**` file+line or by a divergence-ledger row id. |
| `absent-unexplained` | no port counterpart and no decision I can name. **This verdict is a finding and goes in the record's headline**, never a shrug. |
| `not-a-site` | the row is in one of the four non-`command` buckets; kept in the document for auditability, excluded from the count. |

**T3 is reported as a separate column for every `present` and `collapsed` row**, because a limb built
only from T1/T2 would be exactly the fait accompli this packet exists to avoid: the rows where T3
fails are the measure of how much vocabulary is actually missing.

---

## 5. PRICING THE VOCABULARY LAYER — fixed axes, chosen before I know the options

The options will be enumerated from the mapping (they are shaped by what the sites actually demand).
Whatever they turn out to be, **each is scored on the same seven axes, and the axes are fixed now** so
that a favourite cannot be constructed by choosing flattering criteria:

1. **Cost in files** — new files, changed files, named. Changed files in `client/src/**` are
   described, never written.
2. **Effect on the SP-119 seam** — exactly one of: *changes `IHapticSink`* / *wraps it, leaving the
   interface byte-identical* / *neither*. If it changes it, which member, and why the SP-119 refusals
   (D2/D3/D4/D6, `spine-tasks/SP-119-haptic-seam/record.md:132-134`) survive or are reopened.
3. **What it forecloses** — what becomes hard or impossible afterwards.
4. **Reproduction table** — for each of the five named upstream behaviours: the **decay ladder**
   (`HapticService.cs:784-787`), the **auto-zero latch** (`SetLayer(..., autoZeroMs:)`), **priority
   arbitration**, the **0.06 floor**, the **0.70 cap** — one of `reproduces` / `approximates (say how
   the observable differs)` / `cannot`.
5. **Where the 10 Hz loop lives, if it needs one** — and whether that is a new lifetime model (a
   thing SP-119 §7 was careful not to invent).
6. **What it does on a build with no admitted provider** — today's only build. An option whose value
   is invisible until a dependency decision is made must say so.
7. **Blast radius on `Effects/**`** — how many of the thirteen ported modules would have to change.

Then **one recommendation, with the reason it beats each rejected option on the axes above, and an
explicit sentence that the decision is the owner's and this packet does not take it.**

---

## 6. THE PIN — how the enumeration is stopped from drifting a third time

The doc is the DATA and the test is the LOGIC (`UpstreamPayloadInventoryTests`'s rule).
`client/tests/CcpClient.Tests/HapticSiteCensusTests.cs` will:

1. Anchor the repo root on `client/CcpClient.sln` (exists in a worktree too). Root-not-found is a
   hard failure, never a skip.
2. Parse `client/docs/haptic-limb-census.md` — every row of every table, giving (file, line, needle,
   bucket, verdict).
3. **Re-run §2's grep against the REAL shipping files at test time** and assert that the set of
   candidate lines equals the set of lines the document accounts for. A moved line, a new haptic call,
   or a row deleted from the doc all red it. **This is the fact that pins the enumeration**: it does
   not trust the document's own total, it recomputes the candidate set from the bytes.
4. Assert each recorded row's cited line still contains its recorded needle — so a citation cannot
   rot into a different statement while keeping its number.
5. Assert every row's verdict is one of §4's five, every `absent-by-decision` row names a decision,
   and every `command` row carries a citation on both sides.
6. **Confirm-or-refute the "~2 s" comment arithmetically**: compute the 8-rung ladder span from the
   constants and assert it against the real total rather than against the comment's claim.
7. Carry an unreachable branch, on the `UpstreamPayloadInventoryTests` model: if
   `ConditioningControlPanel/` is absent, the document-shape assertions still all run, so the guard
   cannot be neutered by a sparse checkout.

Fixture tests pin the parser and the comparer against temp-dir inputs, so the guard is not vacuous on
a day the real tree happens to agree. **No entry is added to `allowedSkips`, and `floor.json` is never
opened**; the delta is declared in `spine-tasks/SP-120-haptic-limb-census/floor-delta.json`.

---

## 7. THE TRAPS, AND THE SPECIFIC THING I WILL DO ABOUT EACH

| trap | what I do |
|---|---|
| census drifts into implementation | the §0 diff assertion, quoted in the record. `Effects/**` and all of `client/src/**` stay byte-identical to base |
| the vocabulary decision taken by implication | §5's rubric is filled for every option before a recommendation is written; the recommendation is a paragraph in a doc, not a file |
| inheriting a number | §2-§3 derive it from bytes; the packet's fourteen is reconciled AFTERWARDS and cited as a claim |
| upstream's missing STOP gets copied | verify `Stop()`, `CloseAll()`, `ForceCleanup()` for haptic references by the §2 alphabet over the enclosing method ranges rather than by reading and remembering; then record it as a divergence with a recommendation, and do not design it |
| "fixing" the readout that fires when nothing can vibrate | verified by reading the announce/play ORDER at the cited lines; recorded as behaviour to preserve, with the note that adding a connected-check at a call site would be a user-visible change |
| citing the wrong file (the SP-113 class) | **every line cited in any artefact is opened and read at the cited number before the citation is written.** Haptic settings are `Models/HapticSettings.cs`; that path is verified before use. Any citation in the packet or in prior artefacts that does not verify is reported, not silently corrected |
| D197 re-filed | new rows start at **D203**; D197's gate disagreement is not re-filed |
| equivalence claims | none made without a `grep` enumeration of every consumer, per the standing rule |

---

## 8. ORDER OF WORK

1. Commit this plan. **Stop for review.**
2. Alphabet (§2) → candidate set → buckets (§3) → the count, reconciled against fourteen.
3. Port trigger points (§4) for every `command` row, both citations, T3 column.
4. Verify the three upstream discrepancies and the missing-STOP finding by grep and by reading.
5. Write `client/docs/haptic-limb-census.md`.
6. Price the vocabulary layer (§5), recommend one.
7. Write `HapticSiteCensusTests.cs` (§6), declare the delta.
8. Divergence rows D203+ in `client/docs/wpf-surface-reachability.md` (divergences only).
9. `node client/tests/floor/check-warnings.mjs`, then `node client/tests/floor/check-floor.mjs`,
   **each alone**. Record observed totals against pin + declared delta.
10. `record.md`; `.DONE` last and uncommitted.
