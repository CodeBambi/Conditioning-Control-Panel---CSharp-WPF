# SP-088 — record

Packet: `spine-tasks/SP-088-upstream-citation-drift-detector/PROMPT.md` (supersedes SP-080).
Branch `lane/SP-088-upstream-citation-drift-detector`, base `feat/crossplatform` at `cf9f7143`.
Artifacts: `client/tools/citations/detect.mjs`, `client/tools/citations/self-test.mjs`.

**REVISED after final review. Read §0 first.** The review found that this record's honesty section had a
mechanism's failure direction backwards and that one stated safeguard had its hole exactly where the risk
was. Both are corrected, the finding is filed as §7.9, and the tool now measures it. §0 is the whole of it.

**No divergence from the plan's predicted real run.** All six class counts, the total row count and
the exit code came out exactly as predicted (9 / 1 / 0 / 4 / 1 / 1, 16 rows, exit 0), and the §0 revision
changed none of them. The three places where observed behaviour differs from a *plan prediction* are all
about what a **reverted** detector does, not about what the shipped one does; each is stated in §4 rather
than footnoted.

---

## 0. REVISION after final review — the resolver credits first-attempt citations to the shipping tree

The final review found a real defect in this record's honesty section and in the tool's own account of
itself, and I reproduced all of it independently before accepting it. **`CITATION_TOKEN` strips every
citation to a bare basename, so a citation whose text explicitly names the lessons-only tree
(`CCP.Core/Services/AIService/IAiService.cs:47-51`) is silently credited to
`ConditioningControlPanel/Services/AIService/IAiService.cs` whenever that basename resolves uniquely in
the shipping universe.** The text named one tree; the tool credited the other.

Measured on this checkout by an independent script written before the fix, then reproduced by the shipped
tool:

| measurement | observed |
|---|---|
| basenames present in BOTH the shipping and the first-attempt universes | **114** |
| distinct path-qualified `CCP.*`/`tests` citation strings in `client/src/**` + `client/docs/**` | **47** |
| …of those, strings whose basename resolves **uniquely** in shipping (so the shipping path is credited) | **15**, spanning **13** distinct basenames |
| occurrences so credited | **23** |
| shipping paths that pick up at least one such citer | **13** |
| shipping paths whose **every** citer named the first attempt (sole-citation) | **4**, two of them tier 1 |

Three consequences, all applied in this revision:

1. **§10.7 was wrong about the error direction** and is corrected. "Report, never pick" holds only for a
   collision *within* shipping; across the tree boundary the resolver silently picks.
2. **§2 Decision A's blind-spot claim was wrong** and is corrected. The 36/84 drop split can only see
   first-attempt citations that *fail* to collide with a shipping basename; a colliding one is never
   dropped, so the safeguard had its hole exactly where the risk is.
3. The four sole-citation entries are filed as **§7.9**, and the detector now prints the symmetric counter
   in its own summary (fact **F14**, revert **R11**). Fact count 14 → 15; **floor delta stays 0 / 0**
   because the self-test is a node file and adds no .NET test.

One number differs from the reviewer's: the review states "9 shipping paths pick up at least one such
citer"; I measure **13** paths touched by at least one qualified occurrence, and **8** paths where at
least one citer exists *only* because of the misattribution. I could not reconstruct a definition that
yields 9, so I state my own definitions above rather than adopt a number I cannot reproduce. The
load-bearing set — the **four** sole-citation entries and their tiers — reproduces exactly, and I also
confirmed the reviewer's discard of the fifth candidate: `client/docs/row-1-research-inputs.md:173`
genuinely cites the WPF `Services/Deeper/IActionDispatcher.cs:32-38` alongside the `CCP.Core` counterpart
at `:174`, so that entry is not sole and does not belong in the list.

The six classes, the ordering invariant, every row of the real run and the exit contract are **unchanged**
by this revision. Diffing the full report before and after the change yields exactly the six added summary
lines and nothing else.

---

## 1. Step 1 — the premise reproduction, as observed

### 1.1 Absence — confirmed

`grep -rn "upstream-citation-inventory" --include=*.mjs --include=*.js --include=*.cs --include=*.ps1 --include=*.sh`
over the worktree returned **zero matches**. `client/tools/` contained `gate/`, `publish/`, `verify/`,
`wave/`, `port-audit-prompt.md`, `port-loop.ps1` — no `citations/`. Nothing in this repository read the
inventory. No detector had appeared in the worktree.

### 1.2 Inventory shape — every packet number reproduced

| Fact | Packet | Observed |
|---|---|---|
| entries | 297 | **297** |
| tier split | 81 / 146 / 70 | **81 / 146 / 70** |
| entries with `changedAtSync` | 106 | **106** (all `status:"M"`) |
| tier-1 changed | 19 | **19** |
| entries with ≥1 `src:` citer | 65 | **65** |
| entries cited ONLY by `docs:` | 232 | **232** |
| `ambiguousBasenames` | `["MainWindow.xaml"]` | **`["MainWindow.xaml"]`** |
| `db3e842f` / `42286638` resolve | yes | **yes** (`db3e842fc8d4…`, `42286638cae1…`) |
| window paths under `ConditioningControlPanel/` | 344 | **344** |

Citing sources: **20** distinct `docs:`, **73** distinct `src:` (70 `.cs` + 3 `.axaml`, all under
`src/CcpClient.Desktop/`).

### 1.3 The known-answer case — confirmed, all four

Four inventory paths do not exist on disk. Each resolves at **exactly one** other real path, and all four
land in the first-attempt tree:

| inventory `path` | tier | sole candidate | tree |
|---|---|---|---|
| `ConditioningControlPanel/Models/AppSettings.cs` | 1 | `…/CCP.Core/Models/AppSettings.cs` | `ccp-first-attempt` |
| `ConditioningControlPanel/Models/AiCommandData.cs` | 1 | `…/CCP.Core/Models/AiCommandData.cs` | `ccp-first-attempt` |
| `ConditioningControlPanel/Models/CompanionPromptSettings.cs` | 1 | `…/CCP.Core/Models/CompanionPromptSettings.cs` | `ccp-first-attempt` |
| `ConditioningControlPanel/Models/KeywordTrigger.cs` | 1 | `…/CCP.Core/Models/KeywordTrigger.cs` | `ccp-first-attempt` |

```
git diff --numstat 42286638..db3e842f -- ConditioningControlPanel/Models/AppSettings.cs
  -> "" (no row at all)
git diff --numstat 42286638..db3e842f -- ConditioningControlPanel/CCP.Core/Models/AppSettings.cs
  -> "441   5   ConditioningControlPanel/CCP.Core/Models/AppSettings.cs"
```

The entry records `add: 441, del: 5`. **The recorded delta was computed against a path the entry does not
name.** Confirmed, not assumed.

### 1.4 The citation count — the third number, and both prior numbers explained

The board row (`client/docs/task-board.md:128`) says **84**; the inventory carries **65**; the shipped
regenerator produces **61**. The gap closes exactly:

- **61** = distinct real *shipping-WPF* paths cited from `client/src/**`. There are also 61 distinct
  resolving basenames in that set, so nothing collapsed.
- **65 − 61 = 4**, and the set difference printed **exactly** the four moved `Models/` paths on the
  inventory side and **nothing** on mine. The inventory's 65 and my 61 are the same set modulo the four
  rot cases. The inventory is not wrong; it is **stale, in precisely the way this packet exists to detect**.
- **84 is reproducible by neither method.** `client/src/**` yields **90** distinct `.cs`/`.xaml` basename
  tokens; **61** resolve to a real shipping-WPF path and **29** do not. The 29 are the port's own files
  (`CompositionRoot.cs`, `AudioSeams.cs`, `SoundArbitration.cs`, `DtrhHostWindow.axaml.cs`, …), the four
  moved `Models/` files, and prose residue (`File.cs`, from the literal phrase `File.cs:line`). 84 sits
  between 61 and 90 and matches no statable rule — it was a hand count.

I edited neither the board nor the ledger. The owed wording is in §6.

---

## 2. The pre-authorized decisions, and the evidence that selected each

### Decision A — two-universe resolver, `MOVED?` with a mandatory tree label. **TAKEN.**

| universe | `.cs`/`.xaml` files | colliding basenames |
|---|---|---|
| FULL: all `ConditioningControlPanel/**` | **1843** | **119** |
| SHIPPING: minus the first-attempt tree | **1009** | **2** |

**293 of 297** inventory paths live in the shipping universe and **0 of 297** under `CCP.*`, so the audit
resolved against the shipping tree. Regenerating against the FULL tree would turn ~119 basenames ambiguous
and emit every first-attempt lessons citation as a `NEW-CITATION` into the failure-evidence tree — the
cry-wolf shape the board row forbids by name. So:

1. **Regeneration** resolves against **SHIPPING** only.
2. **Inventory-path validation** searches **FULL**, because the labels are only constructible if `CCP.*`
   is in the candidate space.

Branches: 1 candidate → `UNRESOLVED (MOVED?)` + label; 0 → `(VANISHED)`; ≥2 → `(AMBIGUOUS)` naming every
candidate with its label. Never picks, never rewrites.

**The boundary is evidence-anchored, not hand-drawn.** `ConditioningControlPanel/ConditioningControlPanel.csproj:10`
sets `DefaultItemExcludes` to `CCP.Core\**;CCP.Avalonia*\**;…;CCP.WindowsOnly\**;tests\**`, and `:52` then
`ProjectReference`s `CCP.Core` back in. `detect.mjs` derives `isFirstAttemptPath()` from exactly that list.
`ConditioningControlPanel/tests/` holds only `CCP.Core.Tests` and `CCP.Avalonia.Desktop.Windows.Smoke`, both
first-attempt, so it is labelled `ccp-first-attempt` — a small extension of the packet's literal wording
("inside one of the `CCP.*` project folders"), taken because labelling the first attempt's own test tree
`shipping-wpf` would be false. Flagged here rather than left to discovery.

The constitutional point is live: four **tier-1** parity claims rest on a path whose only surviving file is
in the lessons-only tree (`docs/constitution.md:32`), while that same file compiles into the shipping
product. The detector **reports and labels**; it does not adjudicate.

**Deliberate consequence, and its two counters.** A token resolving nowhere in SHIPPING is *dropped*, not
re-pointed into `CCP.*`, so the four moved files surface once (from the inventory side) rather than twice.
The summary prints that drop, **split into two numbers** (see §9, reviewer suggestion 4): today
**215 occurrences / 120 distinct names — 36 resolve only under the first-attempt tree, 84 resolve nowhere
in the WPF tree at all**. The 36 are legitimate lessons citations (`AvaloniaAudioPlayer.cs`,
`WebKitGtkBrowserHost.cs`, `LinuxOverlaySurface.cs`, …), not rot; keeping them separate from the 84 is what
makes the counter readable, and it independently vindicates the shipping-only regeneration universe.

**Correction (final review, §0). The drop split is NOT the whole blind spot, and the first version of this
section claimed it was.** A citation is only *dropped* when its basename resolves at **no** shipping path.
The dangerous case is the opposite one: a citation whose text names `CCP.*`/`tests` and whose basename
**does** collide with a shipping file is never dropped at all — it is credited to the shipping path, and
the 36/84 counter cannot see it by construction. The stated safeguard had its hole exactly where the risk
is. So the summary now carries a **second, symmetric counter**, measured today:

```
citations naming CCP.*/tests but CREDITED to a shipping path (basename collision): 23 occurrence(s), 13 distinct name(s), 13 shipping path(s)
  of those, 4 shipping path(s) have NO other citer, so only this misattribution keeps them out of CITATION-GONE:
```

The four are named in the report and filed as **§7.9**. The counter reads the path prefix immediately
before the token and nothing else, so it is a **floor** on the erasure and never a ceiling (§10.10). Bound
by fact **F14**, revert **R11**.

### Decision B — `DELTA-MISMATCH`: **SHIPPED.**

Run across all **106** changed entries: **105 match byte-exactly, 1 fires — 0.9%**, far under the
ten-percent drop threshold. The single hit is the known-answer case (`Models/AppSettings.cs`, recorded
`441/5`, no numstat row at its own path). That 105 of 106 reproduce exactly is itself the proof that the
recorded numbers came from plain `git diff --numstat` over this window with no rename-following, so the
class compares like with like. **No tolerance, no suppression list.** The single hit is also an `UNRESOLVED`
row; both are emitted because they assert different things (one about the path, one about the numbers), and
the overlap is visible in the report rather than hidden.

### Decision C — window: **default `baseline.previous.merge..baseline.merge` from the JSON**, `--since`/`--until` to override. **TAKEN.**

Both endpoints resolve; the window yields the ledger's 344 paths. Each endpoint is verified with
`git rev-parse --verify <sha>^{commit}` **before any diff**; a non-resolving endpoint exits non-zero naming
the SHA and prints **no review list** (fact F12).

### Decision D — **NOT pre-authorized. A deviation, ruled APPROVED at the plan gate. Stated here so the final reviewer rules on it rather than discovering it.**

The packet defines `NEEDS-VERDICT` as verdict "missing or empty". **Implemented literally it fires ZERO
times**: all 19 changed tier-1 entries carry a non-empty verdict. But **9 carry the literal string**
`UNREVIEWED - owed to board row "Tier-1 citation review for the v6.8.0 sync"`, and both
`client/docs/task-board.md:122` and `client/docs/upstream-sync.md:104-112` say nine reviews are owed. A
literal implementation reports **all-clear on precisely the backlog the class exists to surface** — the
"reports nothing to review because it could not read its own input" failure the packet names under
Decision C.

Resolution: fire on empty **or** on one named sentinel, `UNREVIEWED`, anchored at string start,
case-sensitive, **one exported constant** (`UNREVIEWED_SENTINEL`), with the sub-reason on every row
(`NEEDS-VERDICT (empty)` / `(sentinel: UNREVIEWED)`) and both sub-counts in the summary. Not a fuzzy
substring, not a list. Deleting the one constant reverts it (revert **R1**). Observed today: **0 empty +
9 sentinel = 9**, the ledger's own number.

---

## 3. What was built

Two files under `client/tools/citations/`, nothing else anywhere.

- **`detect.mjs`** — Node 20+, core modules only, no npm/`package.json`/lockfile, no shell string. `git`
  runs only through one `execFileSync("git", argv)` helper. All paths normalized to forward slashes. Repo
  root anchored by walking up from `process.cwd()` to `client/CcpClient.sln`
  (`UpstreamPayloadInventoryTests.cs:98-114`), deliberately **not** from the script location — that would
  always find the real repository and make every fixture a lie. Pure core `runDetector({repoRoot, since,
  until, inventoryPath})` returns `{window, rows, summary}` and prints nothing (`RunGuard`/`GuardOutcome`
  at `:56`/`:70`, transposed). CLI adds `--since`, `--until`, `--out`. Output is grouped by class and
  sorted within class, so two runs are byte-identical.
- **`self-test.mjs`** — `node:test` + `node:assert/strict` (core modules; the zero-dependency rule holds).
  15 facts against temp-dir fixture repositories, `ccp-sp088-<randomUUID>`, removed in a `finally`
  (`UpstreamPayloadInventoryTests.cs:546`). **No fact reads today's real tree**, so a citation added
  tomorrow cannot red the self-test.

**Ordering invariant (load-bearing).** `UNRESOLVED` and `AMBIGUOUS` are computed **before**
`CITATION-GONE`, each suppressing its own rows from it. Measured both ways on this tree: with the
suppressions `CITATION-GONE` = **0**; without them it reports **6** — the four moved paths plus **both**
`MainWindow.xaml` candidates — every one already reported under a truer class. (The plan predicted 5; the
shipped tool measures **6**, matching the reviewer's independent prototype. Recorded as measured, per the
plan's own rule.) Six duplicate rows out of sixteen is exactly the cry-wolf shape the row forbids. Bound by
fact **F8**.

**Exit contract.** Exit **0** whenever the detector ran, empty list or not. Exit **1** only when it could
not run honestly, with the reason named on stderr and **no review list on stdout**. Only 0 and 1 are ever
produced, because `client/tools/gate/with-slot.mjs:36-42` reserves 75/70/127/126/130 for wrapper-only
failures and a detector exiting 75 under the wrapper would be indistinguishable from a slot timeout.

---

## 4. The revert matrix — executed for real

Procedure, per row: apply **one** minimal edit to `detect.mjs`, run `node client/tools/citations/self-test.mjs`,
record the red count and which facts reded, restore, and verify the restore by SHA-256. One source at a
time, tree restored between reverts.

Pristine `detect.mjs` SHA-256 for reverts R1–R10, run against the pre-revision file:
`1f1108bfcfa8df3e69cb37c6fe3f9673edf51071cb71b68fbb3f9fe73256448f`.
**Every one of those 14 restores was byte-identical, and the file's SHA-256 after the last revert equalled
the pristine SHA.** R11 was executed after the §0 revision, against the shipped file whose pristine SHA-256
is `8a8bb8477b603d429ce16965db66d85bec2115d3f3af0f9d0a2477658f3d3fa0`; its restore is byte-identical to
that, and 15/15 pass against the restored file.

Baseline: **15 facts, 15 pass, exit 0.**

| Revert | Edit | Behaviour of the reverted detector | Red | Facts reded |
|---|---|---|---|---|
| **R1** | delete the `NEEDS-VERDICT` sentinel disjunct | runs; emits the empty row, omits the sentinel row | 1 | F1 |
| **R1b** | drop the `tier === 1` gate | runs; additionally emits the tier-2 entry | 2 | F1, F2 |
| **R1c** | drop the `changedAtSync` gate | runs; additionally emits the unchanged tier-1 entry | 2 | F1, F2b |
| **R2** | invert the `NEW-CITATION` membership test | runs; class holds the control path instead of the new one | 1 | F3 |
| **R3** | remove the `CITATION-GONE` loop | runs; class empty | 1 | F4 |
| **R4a** | tree-label body → constant `shipping-wpf` | runs; every row emitted, one field wrong | 1 | F5 |
| **R4b** | point the candidate search at the shipping universe | runs; the `CCP.Foo/` case degrades to `VANISHED` | 2 | F5, F6 |
| **R5a** | pick `candidates[0]` instead of emitting `AMBIGUOUS` | runs; `AMBIGUOUS` empty and one path silently keyed | 2 | F7, F8 |
| **R5b** | remove the ambiguous suppression from the gone loop | runs; spurious `CITATION-GONE` for the uncited candidate | 1 | F8 |
| **R6** | compare `add` only in `DELTA-MISMATCH` | runs; the del-only case stops firing | 1 | F9 |
| **R7** | `return outcome.rows.length ? 1 : 0` | runs; exits 1 on a populated list | 1 | F10 |
| **R8** | swallow an unparseable inventory, continue with no entries | runs past the parse, then dies on the **missing window** instead | 1 | F11 |
| **R9** | delete the `rev-parse --verify` precheck | runs; `git diff` fails instead, non-zero for a **different** reason | 1 | F12 |
| **R10** | fall back to `cwd` when the anchor is not found | runs against a rootless tree, then dies on the **unreadable inventory** | 1 | F13 |
| **R11** | `namesFirstAttemptTree()` body → `return false` | runs; every class and every row identical, the first-attempt counter reads `0 / 0 / 0`, 0 sole paths | 1 | F14 |

**Every rule bites. No rule failed to bite, so nothing is recorded as "not a fact".**

R11 is the §0 revision's revert. It is worth stating what it does *not* break: with the rule reverted the
review list is byte-identical, because the counter is a counter and touches no row. The only observable
change is the summary line going to zero — which is precisely why F14 asserts the three sub-counts and the
`solePaths` array by name, and asserts that `formatReport` actually prints them, rather than asserting that
"something changed".

Three reverts produced a *different* defined outcome than the plan predicted, and all three are caught only
because each fact asserts the **specific named check** rather than a bare failure
(`client/tools/verify/self-test.ps1:38-42` is the precedent):

- **R8** — the plan predicted "exits 0 with an empty list". Observed: it exits non-zero, because the
  swallowed inventory also has no `baseline`, so the window check kills it. F11 bites on
  `the failure must name the inventory it could not read`.
- **R9** — the reviewer flagged (suggestion 3) that this revert might not bite if the git helper threw,
  since Node's `execFileSync` error text embeds the argv and would still contain the SHA. **Decided before
  writing F12:** the git helper *never* throws on a non-zero status; it returns a typed
  `{ok, stdout, stderr, status}` and each caller names its own failure. So after R9 the detector still exits
  non-zero and its message still contains the SHA (it is inside the range string) — and F12 bites purely on
  `the failure must name the rev-parse precheck that tripped`. Verified: the failing assertion is exactly
  that one.
- **R10** — the plan predicted "empty list, exit 0". Observed: it exits non-zero because the rootless tree
  has no readable inventory. F13 bites on `the failure must name the ANCHOR it looked for`.

---

## 5. Step 5 — the real run, verbatim

`node client/tools/citations/detect.mjs`, cwd inside this worktree, **exit code 0**.
Counts: `NEEDS-VERDICT` **9** (0 empty + 9 sentinel), `NEW-CITATION` **1**, `CITATION-GONE` **0**,
`UNRESOLVED` **4**, `AMBIGUOUS` **1**, `DELTA-MISMATCH` **1**. **Total 16 rows.**

**The four `UNRESOLVED` rows from Step 1 all appear. I observed exactly 4 — the packet's number — and all
four are `MOVED?` → `ccp-first-attempt`.**

```
UPSTREAM CITATION REVIEW LIST
window: 42286638..db3e842f
inventory: 297 entries (106 changed in window) | sources: 128 under client/src, 33 under client/docs
universe: 1009 shipping / 1843 full WPF files | regenerated: 292 real paths cited

## NEEDS-VERDICT (9)
  ConditioningControlPanel/AvatarTube/AvatarTubeWindow.Avatar.cs  [tier 1]  (sentinel: UNREVIEWED)
      cited by: docs:capability-inventory.md, docs:first-attempt-lessons.md
      action: review this tier-1 file's upstream change and record a verdict in the inventory
  ConditioningControlPanel/AvatarTube/AvatarTubeWindow.ChatInput.cs  [tier 1]  (sentinel: UNREVIEWED)
      cited by: docs:ai-companion-admission.md, docs:window-behavior-manifest.md
      action: review this tier-1 file's upstream change and record a verdict in the inventory
  ConditioningControlPanel/Chaos/ChaosWebViewHost.cs  [tier 1]  (sentinel: UNREVIEWED)
      cited by: docs:task-board.md, docs:upstream-sync.md, docs:window-behavior-manifest.md, src:src/CcpClient.Desktop/Features/Dtrh/DtrhHostWindow.axaml.cs, src:src/CcpClient.Desktop/Features/Intake/IntakeHostWindow.axaml.cs, src:src/CcpClient.Desktop/Features/Intake/IntakeProtocol.cs
      action: review this tier-1 file's upstream change and record a verdict in the inventory
  ConditioningControlPanel/MainWindow/MainWindow.Lab.cs  [tier 1]  (sentinel: UNREVIEWED)
      cited by: docs:capability-inventory.md, docs:task-board.md, docs:window-behavior-manifest.md, src:src/CcpClient.Desktop/Features/Dtrh/DtrhLaunchCoordinator.cs
      action: review this tier-1 file's upstream change and record a verdict in the inventory
  ConditioningControlPanel/MainWindow/MainWindow.Patreon.cs  [tier 1]  (sentinel: UNREVIEWED)
      cited by: docs:ai-companion-admission.md, docs:window-behavior-manifest.md
      action: review this tier-1 file's upstream change and record a verdict in the inventory
  ConditioningControlPanel/MainWindow/MainWindow.Settings.cs  [tier 1]  (sentinel: UNREVIEWED)
      cited by: docs:main-sync-2026-08-04.md, docs:task-board.md, docs:window-behavior-manifest.md, src:src/CcpClient.Desktop/Features/Intake/IntakeSettingsDocument.cs
      action: review this tier-1 file's upstream change and record a verdict in the inventory
  ConditioningControlPanel/Services/AiService.cs  [tier 1]  (sentinel: UNREVIEWED)
      cited by: docs:ai-companion-admission.md, docs:ai-operation-contract.md, docs:ai-provider-spike.md, docs:capability-inventory.md, docs:task-board.md, src:src/CcpClient.Desktop/Ai/AiAwarenessService.cs, src:src/CcpClient.Desktop/Ai/AiOperationPipeline.cs, src:src/CcpClient.Desktop/Ai/AiOperationVocabulary.cs, src:src/CcpClient.Desktop/Ai/AiProviderSeam.cs
      action: review this tier-1 file's upstream change and record a verdict in the inventory
  ConditioningControlPanel/Services/Companion/Brain/CompanionBrain.cs  [tier 1]  (sentinel: UNREVIEWED)
      cited by: docs:her-room-divergence-audit.md, docs:task-board.md, src:src/CcpClient.Desktop/Ai/AiOperationPipeline.cs, src:src/CcpClient.Desktop/Ai/AiPrivacyFilters.cs
      action: review this tier-1 file's upstream change and record a verdict in the inventory
  ConditioningControlPanel/Services/KeywordTriggerService.cs  [tier 1]  (sentinel: UNREVIEWED)
      cited by: docs:ai-companion-admission.md, docs:ai-operation-contract.md, docs:her-room-divergence-audit.md, docs:row-1-research-inputs.md, docs:task-board.md, src:src/CcpClient.Desktop/Ai/AiAwarenessService.cs
      action: review this tier-1 file's upstream change and record a verdict in the inventory

## NEW-CITATION (1)
  ConditioningControlPanel/Services/Companion/Brain/CompanionTurn.cs  [-]  (cited by the port, absent from the inventory)
      cited by: docs:task-board.md
      action: add this path to the inventory with a tier, or drop the citation

## CITATION-GONE (0)
  (none)

## UNRESOLVED (4)
  ConditioningControlPanel/Models/AiCommandData.cs  [tier 1]  (MOVED?)
      candidate: ConditioningControlPanel/CCP.Core/Models/AiCommandData.cs  [ccp-first-attempt]
      cited by: docs:ai-operation-contract.md, src:src/CcpClient.Desktop/Ai/AiCommandEnvelope.cs
      action: the recorded path does not exist; its basename resolves at exactly one other real path (ConditioningControlPanel/CCP.Core/Models/AiCommandData.cs, tree ccp-first-attempt) — confirm the move and re-key the entry, or retire the claim
  ConditioningControlPanel/Models/AppSettings.cs  [tier 1]  (MOVED?)
      candidate: ConditioningControlPanel/CCP.Core/Models/AppSettings.cs  [ccp-first-attempt]
      cited by: docs:ai-companion-admission.md, docs:first-attempt-lessons.md, docs:her-room-divergence-audit.md, docs:task-board.md, docs:upstream-sync.md, src:src/CcpClient.Desktop/Ai/AiAwarenessService.cs, src:src/CcpClient.Desktop/Companion/CompanionState.cs, src:src/CcpClient.Desktop/Features/Companion/CompanionViewModel.cs, src:src/CcpClient.Desktop/Features/Dtrh/DtrhNativeEffects.cs, src:src/CcpClient.Desktop/Features/Dtrh/DtrhUserMedia.cs, src:src/CcpClient.Desktop/Features/Intake/IntakeSettingsDocument.cs, src:src/CcpClient.Desktop/Persistence/AssetSelectionDocument.cs
      action: the recorded path does not exist; its basename resolves at exactly one other real path (ConditioningControlPanel/CCP.Core/Models/AppSettings.cs, tree ccp-first-attempt) — confirm the move and re-key the entry, or retire the claim
  ConditioningControlPanel/Models/CompanionPromptSettings.cs  [tier 1]  (MOVED?)
      candidate: ConditioningControlPanel/CCP.Core/Models/CompanionPromptSettings.cs  [ccp-first-attempt]
      cited by: docs:ai-companion-admission.md, docs:her-room-divergence-audit.md, docs:task-board.md, src:src/CcpClient.Desktop/Ai/AiCommandExecutor.cs, src:src/CcpClient.Desktop/Ai/AiMemoryStore.cs, src:src/CcpClient.Desktop/Features/Companion/CompanionParticipant.cs
      action: the recorded path does not exist; its basename resolves at exactly one other real path (ConditioningControlPanel/CCP.Core/Models/CompanionPromptSettings.cs, tree ccp-first-attempt) — confirm the move and re-key the entry, or retire the claim
  ConditioningControlPanel/Models/KeywordTrigger.cs  [tier 1]  (MOVED?)
      candidate: ConditioningControlPanel/CCP.Core/Models/KeywordTrigger.cs  [ccp-first-attempt]
      cited by: src:src/CcpClient.Desktop/Ai/AiAwarenessService.cs
      action: the recorded path does not exist; its basename resolves at exactly one other real path (ConditioningControlPanel/CCP.Core/Models/KeywordTrigger.cs, tree ccp-first-attempt) — confirm the move and re-key the entry, or retire the claim

## AMBIGUOUS (1)
  MainWindow.xaml  [-]  (2 candidates)
      candidate: ConditioningControlPanel/MainWindow/MainWindow.xaml  [shipping-wpf]
      candidate: ConditioningControlPanel/Resources/Theme/MainWindow.xaml  [shipping-wpf]
      cited by: docs:task-board.md, docs:upstream-sync.md, docs:window-behavior-manifest.md
      action: cite this file by its real path; the basename alone cannot identify it, so no candidate was chosen

## DELTA-MISMATCH (1)
  ConditioningControlPanel/Models/AppSettings.cs  [tier 1]  (no numstat row at the entry's own path (recorded +441/-5))
      cited by: docs:ai-companion-admission.md, docs:first-attempt-lessons.md, docs:her-room-divergence-audit.md, docs:task-board.md, docs:upstream-sync.md, src:src/CcpClient.Desktop/Ai/AiAwarenessService.cs, src:src/CcpClient.Desktop/Companion/CompanionState.cs, src:src/CcpClient.Desktop/Features/Companion/CompanionViewModel.cs, src:src/CcpClient.Desktop/Features/Dtrh/DtrhNativeEffects.cs, src:src/CcpClient.Desktop/Features/Dtrh/DtrhUserMedia.cs, src:src/CcpClient.Desktop/Features/Intake/IntakeSettingsDocument.cs, src:src/CcpClient.Desktop/Persistence/AssetSelectionDocument.cs
      action: the recorded delta was computed against a path this entry does not name; re-key or re-measure

## SUMMARY
  NEEDS-VERDICT: 9
  NEW-CITATION: 1
  CITATION-GONE: 0
  UNRESOLVED: 4
  AMBIGUOUS: 1
  DELTA-MISMATCH: 1
  NEEDS-VERDICT breakdown: 0 empty, 9 sentinel
  dropped citation tokens (resolve at no shipping path): 215 occurrence(s), 120 distinct name(s) — 36 resolve only under the first-attempt tree (CCP.*/tests), 84 resolve nowhere in the WPF tree
  citations naming CCP.*/tests but CREDITED to a shipping path (basename collision): 23 occurrence(s), 13 distinct name(s), 13 shipping path(s)
    of those, 4 shipping path(s) have NO other citer, so only this misattribution keeps them out of CITATION-GONE:
      ConditioningControlPanel/Localization/LocalizationManager.cs
      ConditioningControlPanel/Services/AIService/IAiService.cs
      ConditioningControlPanel/Services/Compositor/CompositorEngine.cs
      ConditioningControlPanel/Services/Compositor/CompositorLayers.cs
  TOTAL ROWS: 16

This is a REVIEW LIST, not a failure. Exit 0 means the detector ran.
```

The six lines from `citations naming CCP.*/tests …` down to the fourth sole path are the **only** difference
between this run and the pre-revision run (§0). I captured both reports and diffed them: every row of every
class, every other counter and the total are byte-identical, which is what proves the added measurement did
not disturb which citations get credited.

Note on `AMBIGUOUS`: all three citers are prose **about the collision itself** ("basenames collide —
`MainWindow.xaml`"). The class is nearly inert on today's tree, which is exactly why its correctness is
proven by fixtures (F7, F8) and not by this run.

---

## 6. SCOPE PROBLEM — no standing gate in this repository runs the self-test

**Stated plainly and in its own section, as the packet requires.**

`client/tests/floor/check-floor.mjs` discovers only csproj entries whose solution path starts with `tests/`
(`:80-107`) and runs them with `dotnet test --no-build` (`:253`). **A node script under
`client/tools/citations/` is invisible to it.** I added no csproj, no `.cs`, and touched no solution file.

Therefore: **`client/tools/citations/self-test.mjs` is run by nothing on a schedule, in CI, or at land. The
detector can rot in exactly the way the citations it watches rot.** Its 15 facts hold today because I ran
them today; nothing will notice when they stop holding.

**The follow-up, named precisely so it is owed work rather than phantom debt:** a single `.cs` fact in
`client/tests/CcpClient.Tests/` that shells `node client/tools/citations/self-test.mjs` and asserts exit 0.
That would put the detector on the floor for the cost of one test.

**It is out of this packet's scope.** `client/tests/CcpClient.Tests/**` is SP-086's `fileScopeMustChange`
this wave, and this packet's scope is exactly `client/tools/citations/**`. I did not write it, did not stub
it, and left no commented-out version or TODO. This sentence is the whole deliverable for it.

I also did not wire the detector into `check-floor.mjs`: out of scope, and it would convert a review list
into a red gate, which the board row forbids by name.

---

## 7. Out of File Scope — filed, not fixed

I edited nothing under `client/docs/`, `.claude/`, `ConditioningControlPanel/`, or the board. The exact
wording I believe is correct is quoted below for the orchestrator to apply at land (SP-059 precedent).

**7.1 `client/docs/upstream-sync.md:145-146` is wrong as written.** It currently claims every
`File.cs:line` in the port still resolves. Owed wording:

> **No dangling citations in this sync window:** no cited file was deleted or renamed by upstream between
> `42286638` and `db3e842f`. That was checked, not assumed. It is **not** the same claim as "every citation
> resolves": four inventory paths — `Models/AppSettings.cs`, `Models/AiCommandData.cs`,
> `Models/CompanionPromptSettings.cs`, `Models/KeywordTrigger.cs` — do not resolve on disk today. They moved
> to `CCP.Core/Models/` in `adccc2e9`, before this window. `node client/tools/citations/detect.mjs` reports
> all four as `UNRESOLVED (MOVED?) → ccp-first-attempt` on every run.

**7.2 `client/docs/upstream-sync.md:116`** — the table row `| Cited WPF files considered | 84 (src comments
only) | **297** (src + docs) |`. Owed footnote:

> The `84` was a hand count and is reproducible by no rule: `client/src/**` contains **90** distinct
> `.cs`/`.xaml` basename tokens, of which **61** resolve to a real shipping-WPF path. The inventory's **65**
> `src:`-cited entries are those 61 plus the four moved `Models/` paths.

The same `84` appears in the board row at `client/docs/task-board.md:128`.

**7.3 `ambiguousBasenames` is incomplete.** `SeasonRecapCard.xaml` also collides in the shipping universe
(`ConditioningControlPanel/Controls/SeasonRecapCard.xaml` vs
`ConditioningControlPanel/Resources/Theme/SeasonRecapCard.xaml`) and is not in the declared list. It is
uncited today so it produces no row, but **the declared list is wrong now and will produce a wrong answer
the day it is cited.**

**7.4 `Resources/Theme/MainWindow.xaml`'s `citedBy` is an inference, not an observation.** No source text
cites that path. Both `MainWindow/MainWindow.xaml` and `Resources/Theme/MainWindow.xaml` carry the identical
`citedBy: ["docs:window-behavior-manifest.md"]`, i.e. one ambiguous citation was expanded onto both
candidates — and the `Resources/Theme/` entry then carries a real `changedAtSync` of `+102/−62`. Worth a
schema note separating **observed** citations from **ambiguity expansion**.

**7.5 The inventory needs a documented `UNREVIEWED` sentinel** (Decision D), or the 9 owed entries should
carry an empty verdict instead. The convention is currently undocumented and load-bearing.

**7.6 Board line drift.** The packet cites `client/docs/task-board.md:124` as the T-19 row; today the T-19
row is at **`:128`** and `:124` is a different row. The tier-1 citation-review row is at **`:122`**. Filed
so findings are not attributed to a neighbouring row.

**7.7 The skill hook** (`.claude/**`, out of scope): `wpf-upstream-sync` should invoke
`node client/tools/citations/detect.mjs` at each sync, or the tool will be rediscovered rather than run.

**7.8** The missing standing gate — see §6, which is its own section because it is owed work.

**7.9 Four inventory entries are held in the inventory only by a citation that names the FIRST-ATTEMPT
tree.** This is the §0 finding and the most valuable thing this packet turned up. For each of the four,
every citing document cites a `CCP.*` path; no port source cites the shipping path at all. Because the
resolver keys on the basename, the citation was credited to the shipping entry and the entry therefore
never reached `CITATION-GONE`. Verified three ways: my own scan, the shipped counter, and by hand in the
citing documents (`git grep -n` over `client/docs` and `client/src` returns no shipping-tree citation for
any of the four).

| inventory entry (shipping path) | tier | the citation that keeps it, in the citing doc's own words |
|---|---|---|
| `ConditioningControlPanel/Services/AIService/IAiService.cs` | **1** | `ai-operation-contract.md:51` — "quiz-style stateless multi-turn (first-attempt `GetRawChatCompletionAsync`, `CCP.Core/Services/AIService/IAiService.cs:47-51`)"; `first-attempt-lessons.md:108` — "**Exact evidence:** `ConditioningControlPanel/CCP.Core/Services/AIService/IAiService.cs`, …" |
| `ConditioningControlPanel/Services/Compositor/CompositorLayers.cs` | **1** | `capability-inventory.md:223` — "First-attempt UCE ordering: `ConditioningControlPanel/CCP.Avalonia/Compositor/CompositorLayers.cs`"; `first-attempt-lessons.md:23` and `:48` — the same `CCP.Avalonia` path with line ranges |
| `ConditioningControlPanel/Services/Compositor/CompositorEngine.cs` | 3 | `first-attempt-lessons.md:24` — "The engine owns the update and repaint decision … `ConditioningControlPanel/CCP.Avalonia/Compositor/CompositorEngine.cs:540-598`" |
| `ConditioningControlPanel/Localization/LocalizationManager.cs` | 3 | `first-attempt-systemic-lessons.md:53` — "Localization loads output files from a fixed directory (`CCP.Core/Localization/LocalizationManager.cs:119-174`)" |

Two of the four are **tier 1**: a parity claim recorded against the shipping product but evidenced only
from the tree `docs/constitution.md:32` classes as lessons/failure evidence. `LocalizationManager.cs`
carries a live `changedAtSync` of `+8/−2` as well, so an upstream change to a shipping file whose only
"citer" is a first-attempt citation was recorded as covered by this sync.

**Editing `client/docs/upstream-citation-inventory.json` is out of scope**, so this is orchestrator-applied
at land, the same route §7.3 (`SeasonRecapCard.xaml`) takes. The wording I believe is correct, shown for
`IAiService.cs` and identical in shape for the other three:

```json
{
  "path": "ConditioningControlPanel/Services/AIService/IAiService.cs",
  "tier": 1,
  "citedBy": [],
  "citedByFirstAttempt": [
    "docs:ai-operation-contract.md -> ConditioningControlPanel/CCP.Core/Services/AIService/IAiService.cs:47-51",
    "docs:first-attempt-lessons.md -> ConditioningControlPanel/CCP.Core/Services/AIService/IAiService.cs"
  ],
  "note": "No port source cites this shipping path. The two documents previously listed in citedBy cite the CCP.Core file, which docs/constitution.md:32 classes as lessons-only evidence. A tier-1 parity claim cannot rest on it: either a shipping-tree citation is owed, or this entry is retired and the claim re-tiered."
}
```

The `citedByFirstAttempt` key is a **proposal**, not a decision I am entitled to make: the alternative that
needs no schema change is `citedBy: []` plus the same `note`, which turns all four into honest
`CITATION-GONE` rows on the next run. Either way the entry must stop recording a first-attempt citation as
if it were a shipping one. §7.4's request for a schema note separating **observed** citations from
**inferred** ones is the same schema question and should be settled once, not twice.

This is **evidence** for the tier-1 citation-review board row, not a verdict on it. The remaining 9 of the
13 credited names each have at least one genuine shipping citer, so no other entry hangs on the
misattribution today; the erasure is still there and the counter now reports it on every run.

I closed, edited and claimed no board row. This output is **evidence** for the tier-1 review row and the
`window-behavior-manifest.md` row, not a verdict on either.

---

## 8. Verification

Three separate commands, nothing chained. Build first even with no C# change, because the floor wrapper
runs `dotnet test --no-build` and a stale `bin/` reports a count unrelated to the source.

| Command | Result |
|---|---|
| `with-slot --slots 3 -- dotnet build client/CcpClient.sln -c Debug --nologo` | **Build succeeded. 0 Warning(s), 0 Error(s).** |
| `with-slot --slots 3 -- node client/tests/floor/check-floor.mjs` | **FLOOR OK**, exit 0 |
| `node client/tools/citations/self-test.mjs` | **15 tests, 15 pass, 0 fail**, exit 0 |

**Floor numbers.** Declared delta is **0 / 0**, so the observed total must equal the pin exactly:

| Project | Pin (`floor.json`) | Observed | Delta |
|---|---|---|---|
| `CcpClient.Tests` | 1028 | **1028** | 0 |
| `CcpClient.HeadlessTests` | 35 | **35** | 0 |

Skips were exactly the two pinned OS-gated ones
(`ChaosTunnelCapabilityTests.Linux_UnavailableNamesTheTunnelsOwnTwoGaps`,
`SecretStoreTests.LinuxProbe_TypedOutcome_NeverFaked`). `client/tests/floor/floor.json` was not opened or
edited; the delta is declared in
`spine-tasks/SP-088-upstream-citation-drift-detector/floor-delta.json`.

---

## 9. Inherited obligations from the plan review — each discharged

The approved plan carried **no `## Carried conditions` section**. The reviewer's approval carried six
**non-blocking suggestions**, which are obligations approved *with*, not waived. Each is discharged below.

| # | Suggestion | Discharged how |
|---|---|---|
| 1 | No fact pinned the `changedAtSync` gate on `NEEDS-VERDICT`; add an unchanged tier-1 entry with an empty verdict as a third negative control, or an R1c revert. | **Both.** The `needsVerdictFixture` carries entry `A5.cs` (tier 1, empty verdict, **not** changed in the window) and fact **F2b** asserts it produces no row. Revert **R1c** drops the gate and reds F1 + F2b (§4). |
| 2 | No fixture cited from a `src:` source, so `client/src/**` had no bite while `client/docs/**` did; make F7's ambiguous citer a `src:` file. | **Done.** F7's citer is `client/src/CcpClient.Desktop/Thing.cs`, and F7 asserts the row's `citedBy` is exactly `["src:src/CcpClient.Desktop/Thing.cs"]` — so deleting the `client/src/**` scan root reds F7 (and F8). F3/F4 keep citing only from `docs:`, so both roots now bite. |
| 3 | F12's post-revert outcome had two plausible behaviours; decide the git helper's failure policy first, and record F12 as "not a fact" if the revert does not bite. | **Decided before writing F12, and it bites.** `runGit` **never throws** on a non-zero git status; it returns a typed `{ok, stdout, stderr, status}` and each caller names its own failure (documented in the helper's header comment). F12 therefore asserts the **named** check — `SHA does not resolve` — not merely a non-zero exit or the presence of the SHA, both of which survive the revert. Executed: R9 reds F12 on exactly that assertion (§4). Not recorded as "not a fact" because it is one. |
| 4 | Split the drop counter: ~36 of the dropped names resolve under `CCP.*`/`tests` and are legitimate lessons citations, the rest resolve nowhere. | **Done.** The summary line reports the split: today `215 occurrence(s), 120 distinct name(s) — 36 resolve only under the first-attempt tree (CCP.*/tests), 84 resolve nowhere in the WPF tree`. The reasoning, including that this split independently vindicates the shipping-only regeneration universe, is in §2 Decision A. **The final review then showed this split is not the whole blind spot** — it cannot see a first-attempt citation whose basename collides with a shipping file, because such a citation is credited rather than dropped. The symmetric counter added in §0 closes that half. |
| 5 | The plan's "without suppression `CITATION-GONE` reports 5" measured 6 in the reviewer's prototype; record what the shipped tool prints. | **Recorded as measured: 6**, not the plan's 5 — the four moved `Models/` paths plus **both** `MainWindow.xaml` candidates (§3). The shipped number after suppression, 0, reproduces exactly. |
| 6 | The `SeasonRecapCard.xaml` gap in `ambiguousBasenames` is a real finding worth keeping prominent. | **Filed as §7.3**, in the out-of-scope findings list rather than a footnote, with both colliding real paths named and the note that it is inert only because nothing cites it yet. |

---

## 10. Honesty — what this work does NOT prove

1. **No standing gate runs the self-test.** Repeated from §6 because it is the single biggest limitation:
   the facts hold only as of this run.
2. **No Linux execution of any kind.** Everything here ran on Windows 11 with Node v24.5.0. The tool uses
   only core modules, an argv array for `git`, and forward-slash normalization, and it contains no
   platform branch — but *"it should be portable"* is a design claim, not a verified one. **No Linux or
   WSLg run was performed.**
3. **The tool is proven against fixtures plus one real run on one checkout.** The fixtures pin the *rules*;
   the single real run pins today's *answers*. It has never been run at an actual upstream sync, which is
   the workflow it exists for, and it has never been run against a second window.
4. **`--out` is exercised by no fact.** The flag is implemented and the real run did not use it. Its
   behaviour on a path that cannot be created is untested.
5. **The `DELTA-MISMATCH` "like with like" argument is inductive.** 105 of 106 entries reproducing exactly
   is strong evidence that the recorded numbers came from plain `numstat` with no rename-following, but it
   is inference from agreement, not a record of the original method. A future sync that records deltas a
   different way would make this class fire broadly, and the honest response then is to re-run Decision B,
   not to add a tolerance.
6. **The four `UNRESOLVED` rows are reported, not diagnosed.** The detector observes that the paths do not
   exist and that exactly one candidate survives in the lessons-only tree. Whether each parity claim is
   still *true* is a review question for the board row, and this packet deliberately does not answer it.
7. **Basename resolution is the whole resolver, and it fails in TWO directions — the first version of this
   item named only one of them.** A path-qualified citation contributes only its basename.
   - *Within the shipping universe* the failure is conservative: a citation that names a real path whose
     basename is ambiguous is reported as `AMBIGUOUS` even though its own text was unambiguous. Report,
     never pick. That is why `MainWindow.xaml` is one row rather than being resolved from the three citers'
     surrounding text.
   - *Across the tree boundary it is NOT conservative, and it silently picks.* A citation naming a
     `CCP.*`/`tests` path whose basename resolves uniquely in shipping is credited to the shipping path,
     with no label and no row. Measured: 23 occurrences, 13 distinct names, 13 shipping paths, and 4 of
     those paths have no other citer at all (§0, §7.9). The summary counter added in this revision
     *reports* that; it does not fix it. Fixing it would mean resolving a citation by its written path
     instead of its basename, which is a different tool and a different packet.
8. **Citation line numbers are not validated**, by instruction. The `:NNN` suffix is matched and discarded.
   A citation pointing at the right file and the wrong line is invisible to this tool.
9. **The drop counter is a counter, not a class.** 84 names resolve nowhere in the WPF tree; the tool
   reports the number and does not enumerate them. If a genuine rot case hides in that 84, this tool
   currently reports it only as an increment.
10. **The first-attempt counter is a FLOOR on the erasure, never a ceiling.** It reads the path prefix
    written immediately before the token and nothing else. A bare basename sitting in first-attempt prose
    is counted as a shipping citation and is invisible to the counter as well — `first-attempt-lessons.md:108`
    lists `LocalAiService.cs`, `OpenAiService.cs` and `AiResponseParser.cs` unqualified in the same sentence
    as two path-qualified `CCP.Core`/`CCP.Avalonia` citations, and only the qualified two are counted. So
    the true number of shipping entries whose evidence is really first-attempt evidence is **at least** the
    reported one and may be larger. Sentence-level provenance is not something I can specify from this
    packet's evidence, so it is stated as a limit rather than guessed at.
11. **The four §7.9 entries are reported, not adjudicated.** Whether each parity claim is still true, and
    whether a tier-1 claim may rest on lessons-only evidence at all, belongs to the tier-1 citation-review
    board row. This packet supplies the fact and stops.
