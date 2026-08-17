#!/usr/bin/env node
// detect.mjs — upstream citation drift detector (SP-088, board row "Tier-1 citation
// review for the v6.8.0 sync" / the citation-drift row).
//
// WHY THIS EXISTS
// The port's parity claims are `File.cs:line` citations into the read-only WPF tree.
// When upstream rewrites, moves or deletes one of those files the citation stops
// describing the code it points at, and nothing in this repository notices. The
// v6.8.0 sync's single most valuable finding was produced by intersecting 344
// upstream-changed files against the port's citations BY HAND. The data half landed
// as client/docs/upstream-citation-inventory.json; this file is the missing CHECK.
//
// IT IS A REVIEW LIST, NOT A RED TEST. A changed upstream file is not automatically
// a defect, and a guard that cries wolf gets disabled. See the exit contract.
//
// MECHANISM
//   1. Anchor the repo root by walking UP from process.cwd() to the directory holding
//      client/CcpClient.sln (the anchor UpstreamPayloadInventoryTests.cs:98-114 uses,
//      because it survives worktrees where .git is a file, not a directory).
//   2. Read the committed inventory. Never write it.
//   3. Verify BOTH window endpoints with `git rev-parse --verify <sha>^{commit}`
//      BEFORE any diff. A detector that reports "nothing to review" because it could
//      not read its own baseline is worse than no detector.
//   4. Build TWO universes of real WPF files (see TWO UNIVERSES below).
//   5. Regenerate the citation set from BOTH client/src/** and client/docs/**, keyed
//      by REAL PATH. Never collapse by basename.
//   6. Diff against the inventory and emit the REVIEW LIST, grouped by class.
//
// TWO UNIVERSES, AND WHY
//   SHIPPING = ConditioningControlPanel/** minus the first-attempt tree. The exclusion
//   list is not invented here: it is copied from the shipping csproj's own
//   DefaultItemExcludes (ConditioningControlPanel/ConditioningControlPanel.csproj:10),
//   which excludes CCP.*\** and tests\**. Measured on this tree: 1009 files, 2 colliding
//   basenames.
//   FULL = all of ConditioningControlPanel/**. Measured: 1843 files, 119 colliding
//   basenames.
//   REGENERATION resolves against SHIPPING only. Resolving text against FULL would turn
//   ~119 basenames ambiguous and would emit every first-attempt lessons citation
//   (AvaloniaAudioPlayer.cs, WebKitGtkBrowserHost.cs, ...) as a NEW-CITATION pointing
//   into the failure-evidence tree — the cry-wolf shape the board row forbids by name.
//   INVENTORY-PATH VALIDATION (Decision A) searches FULL, because the labels
//   `shipping-wpf` / `ccp-first-attempt` are only constructible if CCP.* is in the
//   candidate space. docs/constitution.md:32 makes CCP.* lessons-only evidence, so a
//   citation that silently re-points there has changed the CLASS of evidence behind a
//   landed claim. ConditioningControlPanel.csproj:52 ProjectReferences CCP.Core into
//   the shipping product, which is exactly why this is a LABELLED REPORT, never a
//   decision. Report; never repair.
//
//   Deliberate consequence: a token that resolves nowhere in SHIPPING is DROPPED, not
//   re-pointed into CCP.*. That keeps one row per real problem (the four moved Models/
//   files surface once, from the inventory side). The drop is not a blind spot: the
//   summary prints the drop counters, split into "resolve only under the first-attempt
//   tree" (legitimate lessons citations) and "resolve nowhere in the WPF tree at all".
//
// ORDERING INVARIANT — LOAD-BEARING
//   UNRESOLVED and AMBIGUOUS are computed BEFORE CITATION-GONE, and each suppresses its
//   own rows from it. Measured on this tree: with the suppressions CITATION-GONE is 0;
//   without them it reports 6, every one of which is already reported under a truer
//   class. Six duplicate rows out of sixteen is cry-wolf. Bound by self-test fact F8.
//
// EXIT CODE CONTRACT — THE WHOLE DIFFERENCE BETWEEN A USEFUL TOOL AND A DISABLED ONE
//   0        the detector RAN and produced a review list — whether that list is empty
//            or two hundred rows long. A non-empty list is NOT a failure.
//   1        the detector could not run honestly: inventory missing or unparseable,
//            ConditioningControlPanel/ absent, a baseline SHA that does not resolve, a
//            repo root it cannot find. Errors go to stderr and NO review list is printed.
//   Only 0 and 1 are ever produced, deliberately: client/tools/gate/with-slot.mjs:36-42
//   reserves 75/70/127/126/130 for wrapper-only failures, so a detector exiting 75 under
//   the wrapper would be indistinguishable from a slot timeout.
//
// PORTABILITY
//   Node 20+, zero npm dependencies, no package.json, no lockfile, no shell. Only
//   node:child_process, node:fs, node:path, node:url — all core and identical on
//   Windows and Linux. git is invoked through execFileSync with an ARGV ARRAY, never a
//   shell string. Every path is normalized to forward slashes before comparison. There
//   is no wall-clock wait, no retry loop and no sleep anywhere in this file.
//
// WHAT THIS FILE DELIBERATELY DOES NOT DO
//   - It does not validate citation LINE NUMBERS. The `:NNN` suffix is matched and then
//     discarded; there is no line-number field in the row shape.
//   - It does not write anything except the opt-in `--out <path>` target. A generated
//     report committed into the tree is the next thing to rot.
//   - It does not pick a candidate when a basename is ambiguous, and it does not treat a
//     CCP.* path as the shipping tree without labelling it.

import { execFileSync } from "node:child_process";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";

// ------------------------------------------------------------------- vocabulary

/** The six review classes. A typed vocabulary, not free strings
 *  (precedent: UpstreamPayloadInventoryTests.cs:72-77 ViolationKind/Violation). */
export const CLASS = Object.freeze({
  NEEDS_VERDICT: "NEEDS-VERDICT",
  NEW_CITATION: "NEW-CITATION",
  CITATION_GONE: "CITATION-GONE",
  UNRESOLVED: "UNRESOLVED",
  AMBIGUOUS: "AMBIGUOUS",
  DELTA_MISMATCH: "DELTA-MISMATCH",
});

/** Emission order of the groups in the report. */
const CLASS_ORDER = [
  CLASS.NEEDS_VERDICT,
  CLASS.NEW_CITATION,
  CLASS.CITATION_GONE,
  CLASS.UNRESOLVED,
  CLASS.AMBIGUOUS,
  CLASS.DELTA_MISMATCH,
];

/** The two tree labels Decision A requires on every UNRESOLVED candidate. */
export const TREE_SHIPPING = "shipping-wpf";
export const TREE_CCP = "ccp-first-attempt";

/** DEVIATION FROM THE PACKET, ruled APPROVED at the plan gate.
 *  The packet defines NEEDS-VERDICT as verdict "missing or empty". Implemented
 *  literally it fires ZERO times: all 19 changed tier-1 entries carry a non-empty
 *  verdict, but NINE carry this literal sentinel, and both client/docs/task-board.md
 *  and client/docs/upstream-sync.md:104-112 say nine reviews are owed. A literal
 *  implementation reports all-clear on precisely the backlog the class exists to
 *  surface. So the class fires on empty OR on this one sentinel, anchored at string
 *  start, case-sensitive, ONE named constant — deleting this constant reverts the
 *  widening (self-test revert R1). Every row carries its sub-reason and the summary
 *  carries both sub-counts, so the literal branch stays legible. */
export const UNREVIEWED_SENTINEL = "UNREVIEWED";

/** Sub-reasons. Named so a fact can assert the SPECIFIC check that tripped rather
 *  than merely that something tripped (precedent: client/tools/verify/self-test.ps1:38-42). */
export const REASON = Object.freeze({
  VERDICT_EMPTY: "empty",
  VERDICT_SENTINEL: `sentinel: ${UNREVIEWED_SENTINEL}`,
  MOVED: "MOVED?",
  VANISHED: "VANISHED",
  AMBIGUOUS: "AMBIGUOUS",
  NO_NUMSTAT_ROW: "no numstat row at the entry's own path",
});

/** Thrown for every "could not run honestly" condition. Carries the exit-1 contract. */
export class DetectorError extends Error {}

// -------------------------------------------------------------------- utilities

const toPosix = (p) => p.replace(/\\/g, "/");

/** Citation token: a bare `Name.cs` / `Name.xaml` filename. The character class
 *  excludes "/" and "\", so a path-qualified citation such as
 *  `ConditioningControlPanel/Services/AIService/AiTextHygiene.cs` yields its BASENAME,
 *  which is what the inventory is keyed on. Greedy matching means `FeatureCard.xaml.cs`
 *  is one token, not `FeatureCard.xaml`. A trailing `:NNN` is simply never captured. */
const CITATION_TOKEN = /[A-Za-z0-9_][A-Za-z0-9_.-]*\.(?:cs|xaml)\b/g;

/** Directories never descended into, in either the WPF tree or the port tree. */
const SKIP_DIRS = new Set(["bin", "obj", ".git", "node_modules", "TestResults"]);

/** First-attempt tree segments, copied from the shipping csproj's own
 *  DefaultItemExcludes (ConditioningControlPanel/ConditioningControlPanel.csproj:10:
 *  `CCP.Core\**;CCP.Avalonia*\**;...;CCP.WindowsOnly\**;tests\**`). `tests/` holds only
 *  CCP.Core.Tests and CCP.Avalonia.Desktop.Windows.Smoke, both first-attempt. Deriving
 *  the boundary from the csproj rather than drawing it by hand means the two universes
 *  agree with what actually compiles into the shipping product. */
function isFirstAttemptPath(relPath) {
  const m = /^ConditioningControlPanel\/([^/]+)\//.exec(relPath);
  if (!m) return false;
  const seg = m[1];
  return seg === "tests" || seg.startsWith("CCP.");
}

/** Decision A's mandatory label. A row cannot exist without one. */
export function treeLabel(relPath) {
  return isFirstAttemptPath(relPath) ? TREE_CCP : TREE_SHIPPING;
}

function walkFiles(dir, keep, out = []) {
  let entries;
  try {
    entries = fs.readdirSync(dir, { withFileTypes: true });
  } catch (err) {
    if (err.code === "ENOENT" || err.code === "ENOTDIR") return out;
    throw err;
  }
  for (const entry of entries) {
    if (SKIP_DIRS.has(entry.name)) continue;
    const full = path.join(dir, entry.name);
    if (entry.isDirectory()) walkFiles(full, keep, out);
    else if (keep(entry.name)) out.push(toPosix(full));
  }
  return out;
}

function indexByBasename(relPaths) {
  const index = new Map();
  for (const rel of relPaths) {
    const base = rel.slice(rel.lastIndexOf("/") + 1);
    if (!index.has(base)) index.set(base, []);
    index.get(base).push(rel);
  }
  for (const list of index.values()) list.sort();
  return index;
}

// ----------------------------------------------------------------- git plumbing

/** The ONLY subprocess call site in this file.
 *
 *  FAILURE POLICY, decided deliberately and pinned by self-test fact F12: this helper
 *  NEVER throws on a non-zero git status. It returns a typed {ok, stdout, stderr,
 *  status} and every caller decides what a failure means and says so BY NAME. If it
 *  threw instead, deleting the rev-parse precheck would still surface a non-zero exit
 *  with the SHA embedded in Node's own error text, F12 would pass with its rule
 *  reverted, and the precheck would not be a fact at all.
 *
 *  stderr is captured rather than inherited because git writes CRLF warnings there on
 *  Windows; only the exit status is ever read as failure. */
function runGit(repoRoot, argv) {
  try {
    const stdout = execFileSync("git", ["-C", repoRoot, ...argv], {
      encoding: "utf8",
      stdio: ["ignore", "pipe", "pipe"],
      maxBuffer: 64 * 1024 * 1024,
    });
    return { ok: true, stdout, stderr: "", status: 0 };
  } catch (err) {
    return {
      ok: false,
      stdout: typeof err.stdout === "string" ? err.stdout : "",
      stderr: typeof err.stderr === "string" ? err.stderr : String(err.message ?? err),
      status: typeof err.status === "number" ? err.status : 1,
    };
  }
}

/** Decision C. Verified BEFORE any diff, and the failure names the SHA.
 *  The phrase "does not resolve" is the named check F12 asserts on. */
function verifyEndpoint(repoRoot, sha, which) {
  const result = runGit(repoRoot, ["rev-parse", "--verify", `${sha}^{commit}`]);
  if (!result.ok) {
    throw new DetectorError(
      `baseline ${which} SHA does not resolve in this checkout: ${sha} ` +
        `(git rev-parse --verify exited ${result.status}). No review list was printed: ` +
        `a detector that reports "nothing to review" because it could not read its own ` +
        `baseline is worse than no detector.`,
    );
  }
  return result.stdout.trim();
}

/** `git diff --numstat <since>..<until> -- ConditioningControlPanel/` as a Map.
 *  Both endpoints are already proven to resolve, so a failure here is a genuine
 *  could-not-run condition and is named as such — never silently treated as "no rows". */
function readNumstat(repoRoot, since, until) {
  const range = `${since}..${until}`;
  const result = runGit(repoRoot, ["diff", "--numstat", range, "--", "ConditioningControlPanel/"]);
  if (!result.ok) {
    throw new DetectorError(
      `git diff --numstat failed for window ${range} (exit ${result.status}): ${result.stderr.trim()}`,
    );
  }
  const map = new Map();
  for (const line of result.stdout.split("\n")) {
    const m = /^(\d+|-)\t(\d+|-)\t(.+)$/.exec(line);
    if (m) map.set(toPosix(m[3]), { add: m[1], del: m[2] });
  }
  return map;
}

// ------------------------------------------------------------------- repo root

/** Walk UP from startDir to the directory containing client/CcpClient.sln.
 *  Same anchor and same refuse-to-skip stance as UpstreamPayloadInventoryTests.cs:98-114.
 *
 *  Anchored on the CALLER'S cwd rather than on this file's own location, deliberately:
 *  anchoring on the script location would always find the real repository and would make
 *  every fixture a lie (self-test fact F13). Not finding the anchor is a hard failure,
 *  never a skip and never a silent fallback to cwd. */
export function findRepoRoot(startDir) {
  let dir = path.resolve(startDir);
  for (;;) {
    if (fs.existsSync(path.join(dir, "client", "CcpClient.sln"))) return toPosix(dir);
    const parent = path.dirname(dir);
    if (parent === dir) {
      throw new DetectorError(
        `could not find the repository root: no directory at or above ${toPosix(path.resolve(startDir))} ` +
          `contains client/CcpClient.sln`,
      );
    }
    dir = parent;
  }
}

// ------------------------------------------------------------------- the core

/** Pure core: takes a repo root, returns {window, rows, summary}, PRINTS NOTHING.
 *  Transposition of RunGuard(root, log) -> GuardOutcome
 *  (UpstreamPayloadInventoryTests.cs:56, :70) so fixtures can drive every branch. */
export function runDetector({ repoRoot, since, until, inventoryPath } = {}) {
  if (!repoRoot) throw new DetectorError("runDetector requires a repoRoot");
  const root = toPosix(path.resolve(repoRoot));
  const invPath = inventoryPath
    ? toPosix(path.resolve(inventoryPath))
    : `${root}/client/docs/upstream-citation-inventory.json`;

  // --- 1. inventory (read-only, always)
  let inventoryRaw;
  try {
    inventoryRaw = fs.readFileSync(invPath, "utf8");
  } catch {
    throw new DetectorError(`inventory not readable at ${invPath}`);
  }
  let inventory;
  try {
    inventory = JSON.parse(inventoryRaw);
  } catch (err) {
    throw new DetectorError(`inventory at ${invPath} is not parseable JSON: ${err.message}`);
  }
  if (!inventory || !Array.isArray(inventory.entries)) {
    throw new DetectorError(`inventory at ${invPath} has no "entries" array`);
  }

  // --- 2. the WPF tree must be here at all
  const wpfRoot = path.join(root, "ConditioningControlPanel");
  if (!fs.existsSync(wpfRoot)) {
    throw new DetectorError(`ConditioningControlPanel/ is absent under ${root} — nothing to resolve citations against`);
  }

  // --- 3. window endpoints, verified BEFORE any diff (Decision C)
  const baseline = inventory.baseline ?? {};
  const sinceSha = since ?? baseline?.previous?.merge;
  const untilSha = until ?? baseline?.merge;
  if (!sinceSha || !untilSha) {
    throw new DetectorError(
      `no change window: pass --since/--until, or give the inventory a baseline.previous.merge ` +
        `and baseline.merge (got since=${sinceSha ?? "none"} until=${untilSha ?? "none"})`,
    );
  }
  verifyEndpoint(root, sinceSha, "since");
  verifyEndpoint(root, untilSha, "until");

  // --- 4. the two universes
  const isWpfSource = (name) => /\.(?:cs|xaml)$/i.test(name);
  const fullUniverse = walkFiles(wpfRoot, isWpfSource).map((p) => p.slice(root.length + 1));
  const shippingUniverse = fullUniverse.filter((p) => !isFirstAttemptPath(p));
  const fullIndex = indexByBasename(fullUniverse);
  const shippingIndex = indexByBasename(shippingUniverse);

  // --- 5. regenerate from BOTH roots
  //   client/src/** : .cs and .axaml, the two extensions the committed src: labels use.
  //   client/docs/**: .md, recursive. 232 of the 297 entries are cited ONLY from here;
  //                   a src-only scan is blind to 78 percent of the inventory.
  const srcFiles = walkFiles(path.join(root, "client", "src"), (n) => /\.(?:cs|axaml)$/i.test(n));
  const docFiles = walkFiles(path.join(root, "client", "docs"), (n) => /\.md$/i.test(n));
  const sources = [
    ...srcFiles.map((f) => ({ file: f, label: `src:${f.slice(`${root}/client/`.length)}` })),
    ...docFiles.map((f) => ({ file: f, label: `docs:${f.slice(`${root}/client/docs/`.length)}` })),
  ];

  const regenerated = new Map(); // real shipping path -> Set(citer label)
  const ambiguousTokens = new Map(); // token -> {candidates[], citers:Set}
  let droppedOccurrences = 0;
  const droppedNames = new Set();

  for (const { file, label } of sources) {
    let text;
    try {
      text = fs.readFileSync(file, "utf8");
    } catch {
      continue; // a file that vanished mid-scan is not a reason to lie about the rest
    }
    for (const token of new Set(text.match(CITATION_TOKEN) ?? [])) {
      const candidates = shippingIndex.get(token);
      if (!candidates) {
        droppedOccurrences++;
        droppedNames.add(token);
        continue;
      }
      if (candidates.length > 1) {
        // NEVER pick. There is no selection expression in this branch.
        if (!ambiguousTokens.has(token)) ambiguousTokens.set(token, { candidates, citers: new Set() });
        ambiguousTokens.get(token).citers.add(label);
        continue; // contributes NOTHING to the keyed set — it cannot collapse two paths into one
      }
      const real = candidates[0];
      if (!regenerated.has(real)) regenerated.set(real, new Set());
      regenerated.get(real).add(label);
    }
  }

  const droppedFirstAttemptOnly = [...droppedNames].filter((n) => fullIndex.has(n));
  const droppedNowhere = [...droppedNames].filter((n) => !fullIndex.has(n));

  const rows = [];
  const add = (row) => rows.push(row);
  const entryByPath = new Map(inventory.entries.map((e) => [e.path, e]));

  // --- 6. NEEDS-VERDICT (tier-1, changed in the window, no usable verdict)
  let verdictEmpty = 0;
  let verdictSentinel = 0;
  for (const entry of inventory.entries) {
    if (!entry.changedAtSync) continue; // the changed-in-window gate (fact F1's third control)
    if (entry.tier !== 1) continue; // the tier gate (fact F2)
    const verdict = typeof entry.verdict === "string" ? entry.verdict : "";
    const isEmpty = verdict.trim() === "";
    const isSentinel = verdict.startsWith(UNREVIEWED_SENTINEL);
    if (!isEmpty && !isSentinel) continue;
    if (isEmpty) verdictEmpty++;
    else verdictSentinel++;
    add({
      cls: CLASS.NEEDS_VERDICT,
      path: entry.path,
      tier: entry.tier,
      citedBy: [...(entry.citedBy ?? [])],
      reason: isEmpty ? REASON.VERDICT_EMPTY : REASON.VERDICT_SENTINEL,
      action: "review this tier-1 file's upstream change and record a verdict in the inventory",
    });
  }

  // --- 7. UNRESOLVED (Decision A) — computed BEFORE CITATION-GONE
  const unresolvedPaths = new Set();
  for (const entry of inventory.entries) {
    if (fs.existsSync(path.join(root, entry.path))) continue;
    unresolvedPaths.add(entry.path);
    const base = entry.path.slice(entry.path.lastIndexOf("/") + 1);
    // Searches the FULL universe on purpose: the labels are only constructible if the
    // first-attempt tree is in the candidate space.
    const candidates = (fullIndex.get(base) ?? []).filter((c) => c !== entry.path);
    if (candidates.length === 1) {
      add({
        cls: CLASS.UNRESOLVED,
        path: entry.path,
        tier: entry.tier,
        citedBy: [...(entry.citedBy ?? [])],
        reason: REASON.MOVED,
        candidates: candidates.map((c) => ({ path: c, tree: treeLabel(c) })),
        action:
          `the recorded path does not exist; its basename resolves at exactly one other real path ` +
          `(${candidates[0]}, tree ${treeLabel(candidates[0])}) — confirm the move and re-key the entry, or retire the claim`,
      });
    } else if (candidates.length === 0) {
      add({
        cls: CLASS.UNRESOLVED,
        path: entry.path,
        tier: entry.tier,
        citedBy: [...(entry.citedBy ?? [])],
        reason: REASON.VANISHED,
        candidates: [],
        action: "the recorded path does not exist and its basename resolves nowhere under ConditioningControlPanel/",
      });
    } else {
      add({
        cls: CLASS.UNRESOLVED,
        path: entry.path,
        tier: entry.tier,
        citedBy: [...(entry.citedBy ?? [])],
        reason: REASON.AMBIGUOUS,
        candidates: candidates.map((c) => ({ path: c, tree: treeLabel(c) })),
        action: `the recorded path does not exist and its basename resolves at ${candidates.length} real paths — none chosen`,
      });
    }
  }

  // --- 8. AMBIGUOUS citations — also computed BEFORE CITATION-GONE
  for (const [token, { candidates, citers }] of ambiguousTokens) {
    add({
      cls: CLASS.AMBIGUOUS,
      path: token,
      tier: null,
      citedBy: [...citers].sort(),
      reason: `${candidates.length} candidates`,
      candidates: candidates.map((c) => ({ path: c, tree: treeLabel(c) })),
      action: "cite this file by its real path; the basename alone cannot identify it, so no candidate was chosen",
    });
  }

  // --- 9. NEW-CITATION
  for (const [real, citers] of [...regenerated.entries()].sort()) {
    if (entryByPath.has(real)) continue;
    add({
      cls: CLASS.NEW_CITATION,
      path: real,
      tier: null,
      citedBy: [...citers].sort(),
      reason: "cited by the port, absent from the inventory",
      action: "add this path to the inventory with a tier, or drop the citation",
    });
  }

  // --- 10. CITATION-GONE — the RESIDUE after UNRESOLVED and AMBIGUOUS claim their rows.
  // Each suppression is bound by its own fact (F8 for the ambiguous half). Without them
  // this class reports 6 rows on today's tree, all six already reported under a truer class.
  const ambiguousBasenames = new Set(ambiguousTokens.keys());
  for (const entry of inventory.entries) {
    if (regenerated.has(entry.path)) continue;
    if (unresolvedPaths.has(entry.path)) continue; // already an UNRESOLVED row
    const base = entry.path.slice(entry.path.lastIndexOf("/") + 1);
    if (ambiguousBasenames.has(base)) continue; // already an AMBIGUOUS row
    add({
      cls: CLASS.CITATION_GONE,
      path: entry.path,
      tier: entry.tier,
      citedBy: [...(entry.citedBy ?? [])],
      reason: "no port source cites this path any more",
      action: "retire the entry, or restore the citation the port dropped",
    });
  }

  // --- 11. DELTA-MISMATCH (Decision B: SHIPPED)
  // Compares each changed entry's recorded add/del against `git diff --numstat` for that
  // entry's OWN path across the recorded window. Measured fire rate on this tree: 1 of
  // 106 changed entries (0.9%), far under the ten-percent drop threshold, and 105 of 106
  // reproduce byte-exactly — which is itself the proof that the recorded numbers came
  // from plain numstat over this window with no rename-following, so this class compares
  // like with like. No tolerance, no suppression list.
  const numstat = readNumstat(root, sinceSha, untilSha);
  let deltaMismatch = 0;
  for (const entry of inventory.entries) {
    const recorded = entry.changedAtSync;
    if (!recorded) continue;
    const observed = numstat.get(entry.path);
    if (!observed) {
      deltaMismatch++;
      add({
        cls: CLASS.DELTA_MISMATCH,
        path: entry.path,
        tier: entry.tier,
        citedBy: [...(entry.citedBy ?? [])],
        reason: `${REASON.NO_NUMSTAT_ROW} (recorded +${recorded.add}/-${recorded.del})`,
        action: "the recorded delta was computed against a path this entry does not name; re-key or re-measure",
      });
      continue;
    }
    if (Number(observed.add) !== recorded.add || Number(observed.del) !== recorded.del) {
      deltaMismatch++;
      add({
        cls: CLASS.DELTA_MISMATCH,
        path: entry.path,
        tier: entry.tier,
        citedBy: [...(entry.citedBy ?? [])],
        reason: `recorded +${recorded.add}/-${recorded.del}, numstat +${observed.add}/-${observed.del}`,
        action: "re-measure the entry's delta over the recorded window",
      });
    }
  }

  // Deterministic output: two runs are byte-identical and pasteable into a ledger.
  rows.sort((a, b) => {
    const ci = CLASS_ORDER.indexOf(a.cls) - CLASS_ORDER.indexOf(b.cls);
    if (ci !== 0) return ci;
    if (a.path !== b.path) return a.path < b.path ? -1 : 1;
    return a.reason < b.reason ? -1 : a.reason > b.reason ? 1 : 0;
  });

  const byClass = Object.fromEntries(CLASS_ORDER.map((c) => [c, rows.filter((r) => r.cls === c).length]));

  return {
    window: { since: sinceSha, until: untilSha },
    rows,
    summary: {
      inventoryEntries: inventory.entries.length,
      changedEntries: inventory.entries.filter((e) => e.changedAtSync).length,
      sourcesScanned: { src: srcFiles.length, docs: docFiles.length },
      universe: { full: fullUniverse.length, shipping: shippingUniverse.length },
      regeneratedPaths: regenerated.size,
      byClass,
      needsVerdict: { empty: verdictEmpty, sentinel: verdictSentinel },
      deltaMismatch,
      dropped: {
        occurrences: droppedOccurrences,
        distinctNames: droppedNames.size,
        firstAttemptOnly: droppedFirstAttemptOnly.length,
        nowhere: droppedNowhere.length,
      },
      totalRows: rows.length,
    },
  };
}

// -------------------------------------------------------------------- reporting

export function formatReport(outcome) {
  const { window, rows, summary } = outcome;
  const out = [];
  out.push("UPSTREAM CITATION REVIEW LIST");
  out.push(`window: ${window.since}..${window.until}`);
  out.push(
    `inventory: ${summary.inventoryEntries} entries (${summary.changedEntries} changed in window) | ` +
      `sources: ${summary.sourcesScanned.src} under client/src, ${summary.sourcesScanned.docs} under client/docs`,
  );
  out.push(
    `universe: ${summary.universe.shipping} shipping / ${summary.universe.full} full WPF files | ` +
      `regenerated: ${summary.regeneratedPaths} real paths cited`,
  );
  out.push("");

  for (const cls of CLASS_ORDER) {
    const group = rows.filter((r) => r.cls === cls);
    out.push(`## ${cls} (${group.length})`);
    if (group.length === 0) {
      out.push("  (none)");
    }
    for (const row of group) {
      const tier = row.tier == null ? "-" : `tier ${row.tier}`;
      out.push(`  ${row.path}  [${tier}]  (${row.reason})`);
      if (row.candidates && row.candidates.length > 0) {
        for (const c of row.candidates) out.push(`      candidate: ${c.path}  [${c.tree}]`);
      }
      out.push(`      cited by: ${row.citedBy.length > 0 ? row.citedBy.join(", ") : "(nothing)"}`);
      out.push(`      action: ${row.action}`);
    }
    out.push("");
  }

  out.push("## SUMMARY");
  for (const cls of CLASS_ORDER) out.push(`  ${cls}: ${summary.byClass[cls]}`);
  out.push(`  NEEDS-VERDICT breakdown: ${summary.needsVerdict.empty} empty, ${summary.needsVerdict.sentinel} sentinel`);
  out.push(
    `  dropped citation tokens (resolve at no shipping path): ${summary.dropped.occurrences} occurrence(s), ` +
      `${summary.dropped.distinctNames} distinct name(s) — ` +
      `${summary.dropped.firstAttemptOnly} resolve only under the first-attempt tree (CCP.*/tests), ` +
      `${summary.dropped.nowhere} resolve nowhere in the WPF tree`,
  );
  out.push(`  TOTAL ROWS: ${summary.totalRows}`);
  out.push("");
  out.push("This is a REVIEW LIST, not a failure. Exit 0 means the detector ran.");
  return out.join("\n");
}

// -------------------------------------------------------------------------- CLI

function parseArgs(argv) {
  const opts = { since: undefined, until: undefined, out: undefined };
  for (let i = 0; i < argv.length; i++) {
    const arg = argv[i];
    const eq = arg.indexOf("=");
    const flag = arg.startsWith("--") && eq > 0 ? arg.slice(0, eq) : arg;
    const inline = arg.startsWith("--") && eq > 0 ? arg.slice(eq + 1) : undefined;
    const take = () => {
      if (inline !== undefined) return inline;
      if (i + 1 >= argv.length) throw new DetectorError(`${flag} needs a value`);
      i += 1;
      return argv[i];
    };
    switch (flag) {
      case "--since":
        opts.since = take();
        break;
      case "--until":
        opts.until = take();
        break;
      case "--out":
        opts.out = take();
        break;
      case "-h":
      case "--help":
        opts.help = true;
        break;
      default:
        throw new DetectorError(`unknown option ${JSON.stringify(arg)}`);
    }
  }
  return opts;
}

const USAGE = [
  "usage: node client/tools/citations/detect.mjs [--since <sha>] [--until <sha>] [--out <path>]",
  "",
  "  Emits the upstream citation REVIEW LIST to stdout.",
  "  --since/--until override the window read from the inventory's baseline.",
  "  --out writes the same report to a file as well (opt-in; nothing is written otherwise).",
  "",
  "  Exit 0: the detector ran (an empty list and a long list both exit 0).",
  "  Exit 1: the detector could not run honestly; the reason is named on stderr.",
].join("\n");

export function main(argv = [], cwd = process.cwd()) {
  let opts;
  try {
    opts = parseArgs(argv);
  } catch (err) {
    process.stderr.write(`citation-detect: ${err.message}\n${USAGE}\n`);
    return 1;
  }
  if (opts.help) {
    process.stdout.write(`${USAGE}\n`);
    return 0;
  }
  let report;
  let outcome;
  try {
    const repoRoot = findRepoRoot(cwd);
    outcome = runDetector({ repoRoot, since: opts.since, until: opts.until });
    report = formatReport(outcome);
  } catch (err) {
    if (err instanceof DetectorError) {
      // NO review list on a broken input, ever.
      process.stderr.write(`citation-detect: COULD NOT RUN HONESTLY\n  ${err.message}\n`);
      return 1;
    }
    throw err;
  }
  process.stdout.write(`${report}\n`);
  if (opts.out) {
    fs.mkdirSync(path.dirname(path.resolve(opts.out)), { recursive: true });
    fs.writeFileSync(path.resolve(opts.out), `${report}\n`, "utf8");
  }
  // THE EXIT CONTRACT. `outcome.rows.length` is deliberately NOT consulted here: a
  // review list with rows in it is the tool working, not the tool failing. Turning this
  // into `return outcome.rows.length ? 1 : 0` is what self-test fact F10 forbids.
  return 0;
}

if (process.argv[1] && import.meta.url === pathToFileURL(path.resolve(process.argv[1])).href) {
  process.exit(main(process.argv.slice(2), process.cwd()));
}

export const __testing = { fileURLToPath, CITATION_TOKEN, isFirstAttemptPath };
