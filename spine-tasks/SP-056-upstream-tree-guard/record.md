# SP-056 — Upstream payload-tree guard — record

**Delivered:** `client/docs/upstream-payload-inventory.json` (committed inventory of the seven
top-level upstream payload trees at the v6.7.4 baseline, each with a typed honest disposition)
+ `client/tests/CcpClient.Tests/UpstreamPayloadInventoryTests.cs` (the guard: 19 tests —
1 real-repo guard, 8 fixture/branch tests, 9 malformed-inventory theory cases, 1 parser
round-trip). No `client/src/**` change; the guard is data (inventory) + logic (test).

## 1. Enumeration (real, counted 2026-08-11 against the merged tree)

`find ConditioningControlPanel/Resources/web/<tree> -type f | wc -l` per tree; zero loose
files directly under `web/` (verified — only directories).

| Tree | Files | Disposition | Evidence / owning row |
|---|---|---|---|
| `dtrh` | 1542 | **served** | Linked glob `client/src/CcpClient.Desktop/CcpClient.Desktop.csproj:50` (`web/dtrh/**` → `payload/dtrh`); served read-only by `Features/Dtrh/LoopbackServer.cs`; typed probe `DtrhParticipant.ProbePayloadRoot` |
| `intake` | 2138 | **served** | Same glob convention `CcpClient.Desktop.csproj:59` (→ `payload/intake`); intake host `Features/Intake/IntakeHostWindow.axaml.cs`; typed probe `Features/Intake/IntakeServingRoots.cs` |
| `goon` | 184 | **not-ported** | Row **"Goon Game 1v1 duel host (v6.7 — new upstream product surface)" [P1 OPEN]**. The tree whose silent arrival (suite stayed 683/683 green) motivated this guard. Upstream host: `Services/GoonGame/GoonHostService.cs` |
| `fyp` | 8 | **not-ported** | Row **"For You Feed desktop host + ghost mode (v6.7 — new upstream product surface)" [P1 OPEN]**. Upstream host: `Services/Fyp/FypHostService.cs` |
| `player` | 3 | **not-ported** | Row **"Implement unified fullscreen video presentation" [P0 BLOCKED]**; the out-of-process browser video engine page (`Services/Video/Browser/BrowserVideoEngine.cs`). The engine-default + grace-pause delta is itemized in the **v6.7.x upstream parity backlog** row |
| `tunnel` | 9 | **not-ported** | Row **"Implement web-only DTRH host" [P0 WIP]** — see finding below. The endless three.js rabbit-hole backdrop under the Chaos game (`Chaos/ChaosTunnelService.cs`) |
| `vendor` | 9 | **not-ported** | Rides the tunnel row: top-level `vendor/three` is consumed **only** by `tunnel/index.html`'s import map (`../vendor/three/...`). `dtrh`/`intake` use the three vendored *inside* the dtrh tree; `goon` vendors internally (`goon/vendor/`, per `goon/encode/encodeWorker.js:27-37`) |

**Finding for orchestrator reconciliation (surprise):** the DTRH host row's b1–b5 slice cut
is declared COMPLETE and never enumerated the `tunnel` backdrop surface (nor `vendor`). The
inventory cites that row as the closest owner and names the mismatch in the entry's `note`;
the board row itself is the orchestrator's to reconcile at land (enabler 2 — this worker does
not edit the board).

## 2. Design

**Inventory schema** (`upstream-payload-inventory.json`, schemaVersion 1):
`baseline {upstreamVersion: "v6.7.4", merge: "42286638", recorded}` + `trees[]` with
`name`, `disposition` (`served` | `not-ported`), `fileCountAtBaseline`, and the honest
companion field — `evidence` (serving code path) for served, `boardRow` + `note` for
not-ported. `fileCountAtBaseline` is **record data, not an assertion**: counts legitimately
drift inside known trees, and per-tree count pinning belongs to the SP-009/SP-037/SP-054
manifest tests. The guard pins *membership*, not counts.

**Guard logic lives in the test assembly** (`UpstreamPayloadInventoryTests.cs`) because the
file scope forbids `client/src/**` changes — the guard *is* the test; the inventory is the
data; no upstream tree name is hard-coded in the test. Parser (System.Text.Json, strict:
unknown disposition / missing companion field / empty list / duplicate names / non-positive
count → `InventoryFormatException` with a readable message) + pure comparer
(inventory × actual top-level dirs → `UnknownTree(name,count)` / `StaleEntry(name)`
violations; case-insensitive matching, on-disk names reported).

**Non-vacuous repo-root resolution (the vacuous-pass argument, stated explicitly):**

1. Root anchored on `client/CcpClient.sln` walking up from `AppContext.BaseDirectory`
   (exists in every checkout AND in worktrees, where `.git` is a file not a dir).
   Root unresolvable → **throws → test fails**. Never a skip.
2. Inventory missing at the derived path → **hard fail** ("its absence is a failure, not a skip").
3. The inventory parses + well-formedness asserts run on **both** branches:
   non-empty, ≥1 `served` AND ≥1 `not-ported` entry (a gutted/single-sided inventory fails
   even with no reference tree), valid baseline version shape, unique names, positive counts,
   companion fields present. → the unreachable branch cannot pass vacuously.
4. "Unreachable reference tree" means exactly one thing: no `ConditioningControlPanel/`
   directory at all (client-only/sparse context). `ConditioningControlPanel/` present but
   `Resources/web` missing → **fail** (corrupt/partial checkout; refuses to skip).
   `web/` present but zero trees → reachable branch, `Assert.NotEmpty(actual)` fails.
5. The branch taken is written to test output (`ITestOutputHelper`); the TRX captures it
   even on green runs — a permanently-skipping guard is visible in transcripts
   (see evidence: `sp056-guard.trx` carries the "full-compare branch (7 trees on disk: …)" line).

The only remaining vacuous shape would be a checkout with no `ConditioningControlPanel/`
AND a well-formed inventory — which is precisely the published/CI context the packet
sanctions, and it is *observable* (output line says UNREACHABLE with served/not-ported counts).

**Failure message a future sync will read** (unknown tree, verbatim from the red demo):

> upstream payload tree 'goon' (184 files) exists under ConditioningControlPanel/Resources/web
> but is not listed in client/docs/upstream-payload-inventory.json — a new upstream product
> surface must not slip past the port silently (the v6.6.3 → v6.7.4 sync added web/goon/,
> 184 files, with the suite green). Action: file a row in client/docs/task-board.md, add the
> tree to the inventory (disposition 'served' naming the serving code path, or 'not-ported'
> with the board-row reference), and cite it in client/docs/upstream-sync.md.

## 3. Consults

- **Pre-approach (Step 1): solo, APPROVE-with-corrections.** Requested route: default solo
  (packet: Opus 5 main, Fable 5 fallback). **Actual answering model: not identifiable from
  tool output** (the consult tool returns no model identity — the T-7 silent-substitution
  lesson applies; recorded honestly). Corrections applied: (a) anchor on `client/CcpClient.sln`
  not the inventory itself, with missing-inventory = hard fail; (b) "unreachable" defined as
  *no `ConditioningControlPanel/` at all* — half-present reference = fail, empty web/ = fail;
  (c) reachable branch asserts `NotEmpty(actual)`; (d) keep the ≥1-served/≥1-not-ported floor
  (cheap, one-line deletion if the port ever serves everything); (e) verify vendor consumption
  broadly before attribution — done (goon vendors internally; only tunnel uses top-level vendor).
- **Pre-completion (Step 3):** see §6.

## 4. Engine-review presence (T-2 heading format — per-call record)

| Call | Step | Result |
|---|---|---|
| `spine_review_step` step=1 type=plan | 1 | **SKIPPED BY DESIGN** — `skipped:true, spawnFailed:false`, "Nested reviewer spawn blocked inside pi worker session … the batch engine runs reviews after worker success (SP-195)". Artifact `.reviews/1-20260812T003033.md` |
| `spine_review_step` step=2 type=plan | 2 | **SKIPPED BY DESIGN** — same SP-195 response. Artifact `.reviews/2-20260812T003540.md` |

Engine code/final reviews: **run by the batch engine after .DONE** (Review Level 2) —
presence/absence of that chain is the engine's journal to record, not the worker's.

## 5. Transcript — the guard bites

All three runs: `dotnet test client/tests/CcpClient.Tests/CcpClient.Tests.csproj -c Debug
--nologo --filter …` in this worktree; transcripts committed under `evidence/`.

1. **Unknown tree → RED** (`demo-unknown-tree-RED.txt`, exit=1): the `goon` entry was
   temporarily deleted from the inventory (the exact v6.7.4 scenario — a tree the inventory
   has never heard of). The guard FAILED naming `'goon' (184 files)` + the three required
   actions (file a board row, add the entry, cite the sync ledger). Standard Output shows
   the reachable-branch line. Inventory restored via `git checkout`.
2. **Stale entry → RED** (`demo-stale-entry-RED.txt`, exit=1): a bogus `zzz-removed-tree`
   entry temporarily injected; the guard FAILED naming the entry + the remove/correct + cite
   action. Restored via `git checkout`.
3. **Real tree → GREEN** (`demo-real-tree-GREEN.txt` + `sp056-guard.trx`, exit=0):
   19/19 passed; the TRX carries the observable branch line
   ("full-compare branch (7 trees on disk: dtrh, fyp, goon, intake, player, tunnel, vendor)").

The fixture suite additionally pins the logic against temp-dir repos (never today's tree
list): unknown-tree message content, stale-entry message, case-insensitive matching,
unreachable-branch well-formedness, gutted-inventory failure on the unreachable branch,
half-present-reference refusal, missing-inventory refusal, empty-web-tree refusal, and 9
malformed-inventory shapes.

## 6. Pre-completion consult

**Solo, verdict: nothing in the design blocks .DONE; three pre-.DONE obligations + one
correction, all discharged.** Actual answering model: **not identifiable from tool output**
(same honesty convention as §3). Correction applied: the baseline-version assertion
`^v\d+\.\d+\.\d+$` would false-red on a two- or four-segment upstream tag — relaxed to
`^v\d+(\.\d+)+$` (a guard that fails for reasons unrelated to the thing it guards loses
trust). Obligations discharged: (1) full contract testCommand run with TRX loggers —
verify.mjs OK (8 project + 5 engine patches), build 0W/0E, **833/833 unit + 33/33 headless**
(arithmetic reconciles exactly: 795 wave-14 floor + 19 SP-055 same-wave lane + 19 this task);
(2) scope verified — `TestResults/` confirmed gitignored, inventory byte-identical to the
committed version after the two red-demo restores, `git diff --check` clean, `git status
--short` shows only File Scope paths; (3) this section filled. The consult independently
endorsed: sln-anchor hard-fail (stricter than the packet's wording, correct — with no
checkout there is no inventory to assert about), the vacuous-pass closure, the
membership-vs-count separation, and the tunnel/vendor handling (board untouched, enabler 2).

## 7. Budgets / surprises / durable-lesson candidates

**Surprises:** (1) the `tunnel`+`vendor` ownership gap (§1 finding) — the guard's very first
enumeration already surfaced a surface no row owns cleanly; (2) `goon` vendors its own deps
internally (upstream comment: headless harnesses serve `goon/` as document root) — top-level
`vendor/` is narrower than its name suggests.

**Durable-lesson candidates:** (a) *membership guards and count guards are separate
instruments* — count-drift assertions on known trees would have made this guard fight the
SP-037/SP-054 manifest tests; the inventory pins membership, manifests pin counts;
(b) *"unreachable" must be defined as the absence of the whole reference, never as a
soft default* — half-present states are corrupt checkouts and must fail (consult correction,
generalizable to every guard that degrades); (c) xunit.v3 3.2.2: `ITestOutputHelper` lives in
the `Xunit` namespace — `Xunit.Abstractions` no longer exists (build error CS0234).

**Budgets:** size S as authored — 2 product files (1 JSON data + 1 test file), 19 tests,
zero `client/src/**` churn, 2 red-demo cycles. Full contract: verify.mjs exit 0, build
0W/0E, 833/833 unit + 33/33 headless, TRX loggers on both full-suite runs.
