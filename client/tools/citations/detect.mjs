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
//   THE OTHER HALF OF THAT CONSEQUENCE, AND IT IS NOT CONSERVATIVE. CITATION_TOKEN reduces
//   every citation to a bare basename, so a citation whose text explicitly names the
//   first-attempt tree (`CCP.Core/Services/AIService/IAiService.cs:47-51`) is credited to
//   the SHIPPING path whenever that basename resolves uniquely there. The text named one
//   tree; the resolver credits the other, and nothing in the row shape records which. The
//   drop counters above are blind to exactly this case, because a colliding name is never
//   dropped. Measured on this checkout: 114 basenames exist in both universes; 13 distinct
//   names are credited this way; 4 shipping paths have NO other citer at all, so only the
//   misattribution keeps them out of CITATION-GONE — two of those four are tier 1. That is
//   why the summary carries a SECOND, SYMMETRIC counter (`summary.firstAttemptCredited`),
//   which names the sole-citation paths. It is a counter and not a class on purpose: the
//   detector reports the erasure, it never re-keys a citation. Bound by self-test fact F14.
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
// THE NEEDLE MODE (SP-131) — THE SECOND MODE, AND THE LINE-LEVEL HALF
//   The default mode answers a FILE-level question: did an upstream file the port cites
//   change? The rot is LINE-level: does the cited line still say what the citation claims?
//   A file can be untouched in a sync window and still have every citation into it shifted
//   by an edit ten lines above. `--needles` answers that, for the citations that opt in.
//
//   A NEEDLE, NOT A LINE. An inventory entry may carry `needles: [{id, needle}]`. There is
//   still NO line-number field anywhere in the schema, deliberately: a stored line rots
//   exactly as fast as the citation it describes. BOTH endpoints of every comparison are
//   DERIVED — one from `git show <endpoint>:<path>`, one from the working tree — so there is
//   nothing to re-record after a repair and nothing that can go stale in the data.
//
//   WHAT MAKES A GOOD NEEDLE (whoever adds the next one will copy what they see here):
//     1. A short distinctive SUBSTRING of the cited line, never the whole line. A whole line
//        rots on an indent change; a substring does not, which is why the match is a
//        substring match and not a line match.
//     2. It must occur EXACTLY ONCE in the file. `Math.Min(run.AffirmedMantras` was rejected
//        during seeding because it matches two lines (the XP sum and the credit cap); the
//        `var affirmed = ` prefix is what makes it an anchor. NEEDLE-AMBIGUOUS reports this.
//     3. Prefer a code token or a distinctive phrase. Avoid punctuation-only text and text
//        whose internal whitespace a reformat would collapse.
//     4. Never a line number and never a regex: the match is literal, case-sensitive and
//        Ordinal (the same match GoonGameCensusTests.cs's proven pin uses).
//
//   FOUR CLASSES, AND EACH STATES SOMETHING CERTAINLY TRUE. That is what keeps this out of
//   cry-wolf territory: NEEDLE-GONE (0 matches today), NEEDLE-AMBIGUOUS (>1 today),
//   NEEDLE-MOVED (exactly one match at both endpoints, at different lines) and
//   CITATION-OUT-OF-RANGE (a span past the file's end — arithmetic, not inference).
//
//   THE CLASS THAT WAS DESIGNED, MEASURED AND CUT — recorded so it is not re-invented.
//   A per-CITATION `CITATION-DRIFT` class (a span that contained a needle at the endpoint and
//   contains none now) was implemented and measured at the inventory's own previous baseline
//   42286638. It emitted FOUR rows and ALL FOUR were false: GradedRunAwards.cs:95,
//   trainer-card-census.md:180 and wpf-surface-reachability.md:1467/:1491 all cite
//   IntakeHostService.cs:418-420, which today is exactly the `held_back` comment they mean
//   (:1467 is D223, quoting :418-420 as a RECORDED historical mis-citation, which is a
//   different reason for the same verdict). They fire only because the emit needle happened
//   to sit at :419 at that endpoint. A 100% false-positive rate is the shape line 13 forbids
//   by name, so the class is CUT, not softened. The consequence, stated rather than hidden:
//   this mode issues a per-SUBJECT verdict and names the shift; re-basing the citations is
//   the reviewer's step. See divergence D261.
//
//   SCOPED TO NEEDLED ENTRIES, STRUCTURALLY. Every line-level check reads `entry.needles`
//   first and does nothing when it is absent, so "no blanket line-number validation" is a
//   property of the code and not a promise. Citations into the other entries stay FILE-level.
//
// WHAT THIS FILE DELIBERATELY DOES NOT DO
//   - The DEFAULT mode does not validate citation LINE NUMBERS: the `:NNN` suffix is matched
//     and then discarded, and there is no line-number field in the row shape. `--needles`
//     checks lines ONLY for entries that carry a needle (1 of 297 today) and never for any
//     other entry. The coverage block prints that split on every run, in both modes.
//   - It does not resolve BARE `:NNN` continuations to a file, in either mode. A bare
//     reference names no file, and the nearest-preceding-citation-token heuristic was
//     measured and REJECTED. THE MEASUREMENT, at commit e3aee3e21 and stated with its SHA
//     because no run re-derives it: the heuristic credits 200 of the port's bare references
//     to IntakeHostService.cs alone, and mis-binds on the first example examined —
//     DtrhUserMedia.cs's `FlashService.GetMediaFiles :2855-2867`, which is a FlashService
//     citation. This is the LARGEST gap in the needle mode: FIVE of the seven citations D232
//     records as rotted are of this form (enumerated at 3c38c3973; D232 and the repaired
//     comment at IntakeQuizRun.cs:136-137 both say seven, and both are wrong — see D264).
//     The COUNT of bare references is re-derived and printed on every run, in both modes.
//   - IT DOES NOT SEE CITATIONS INTO THE PORT'S OWN TOOLING, in either mode, and this packet
//     proved it the hard way. CITATION_WITH_LINE matches only `.cs` and `.xaml`, and the corpus
//     is client/src/** plus client/docs/** and nothing else. So a citation into client/tools/**,
//     and ANY citation into a .mjs file, is invisible to all six default classes and to all four
//     needle classes. SP-131's own citations INTO THIS FILE rotted inside it: six of them, all
//     correct at e3aee3e21 and all invalidated by SP-131's own commit a70371e21, which inserted
//     this header without re-deriving them. A human reviewer caught them; the tool could not,
//     because the tool cannot see itself.
//     THE SAME FACT, SEEN FROM THE OTHER SIDE, AND IT IS ONE PROPERTY AND NOT TWO: it is exactly
//     why editing this header, or spine-tasks/**, can never move the coverage counts — which is
//     what makes those counts a stable fixed point. A reader told only the reassuring half has
//     been told half a fact. See D260's seventh blind spot.
//   - It does not re-derive an entry's `citedBy`. NEW-CITATION (:640-651) skips any path
//     already in the inventory, and no class anywhere in runDetector diffs an existing
//     entry's citer list — the recorded citedBy is only copied into rows for display. A
//     citation ADDED to a file the inventory already carries is therefore invisible to all
//     six classes. Measured today: the IntakeHostService.cs entry's citedBy is missing
//     GradedRunAwards.cs, trainer-card-census.md and wpf-surface-reachability.md, all of
//     which cite it. Re-deriving citedBy is a regeneration job, not a detection one.
//   - The first-attempt counter reads the PATH PREFIX immediately before the token, and
//     nothing else. A bare basename sitting in first-attempt prose
//     (first-attempt-lessons.md:108 lists `LocalAiService.cs` unqualified beside a
//     path-qualified CCP.Core citation) is still credited to shipping AND is invisible to
//     the counter. The counter is a floor on the erasure, never a ceiling.
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

/** The path prefix a citation token was written with, or "" when it was written bare.
 *  Walks BACKWARDS from the token's own start index over path characters only, so there is
 *  no lookback window to truncate a long prefix and no second regex whose token set could
 *  drift from CITATION_TOKEN's. A prefix that does not end at a separator is not a prefix. */
export function precedingPathPrefix(text, index) {
  let i = index;
  while (i > 0 && /[A-Za-z0-9_.\-/\\]/.test(text[i - 1])) i -= 1;
  const raw = toPosix(text.slice(i, index));
  return raw.endsWith("/") ? raw : "";
}

/** True when the citation's own text named the first-attempt tree.
 *  Two markers, both case-sensitive and both derived from the same csproj exclusion list
 *  isFirstAttemptPath() uses:
 *    - any segment starting with `CCP.` (CCP.Core, CCP.Avalonia, CCP.WindowsOnly, ...);
 *    - a `tests` segment DIRECTLY under `ConditioningControlPanel`. The anchor is required:
 *      `client/tests/CcpClient.Tests/...` is the port's own test tree, not the first
 *      attempt, and `CcpClient.Tests` does not start with `CCP.` (case matters). */
export function namesFirstAttemptTree(prefix) {
  if (!prefix) return false;
  const segments = prefix.split("/").filter(Boolean);
  if (segments.some((s) => s.startsWith("CCP."))) return true;
  for (let i = 1; i < segments.length; i++) {
    if (segments[i] === "tests" && segments[i - 1] === "ConditioningControlPanel") return true;
  }
  return false;
}

/** Every distinct citation token in one source file, with how many of its occurrences were
 *  written with a first-attempt-qualified path prefix. The token set is produced by
 *  CITATION_TOKEN and by nothing else, so adding this measurement cannot change WHICH
 *  citations are credited — only what is known about them. */
function tokensWithProvenance(text) {
  const tokens = new Map();
  for (const match of text.matchAll(CITATION_TOKEN)) {
    let record = tokens.get(match[0]);
    if (!record) {
      record = { total: 0, firstAttempt: 0 };
      tokens.set(match[0], record);
    }
    record.total += 1;
    if (namesFirstAttemptTree(precedingPathPrefix(text, match.index))) record.firstAttempt += 1;
  }
  return tokens;
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
  // The symmetric counter: citations whose OWN TEXT named CCP.*/tests but whose basename
  // resolved uniquely in SHIPPING, so the shipping path is what got credited.
  const firstAttemptOnlyCiters = new Map(); // real -> Set(citer) that named ONLY the first attempt
  const firstAttemptNames = new Set();
  const firstAttemptPaths = new Set();
  let firstAttemptOccurrences = 0;

  for (const { file, label } of sources) {
    let text;
    try {
      text = fs.readFileSync(file, "utf8");
    } catch {
      continue; // a file that vanished mid-scan is not a reason to lie about the rest
    }
    for (const [token, provenance] of tokensWithProvenance(text)) {
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
      if (provenance.firstAttempt > 0) {
        firstAttemptOccurrences += provenance.firstAttempt;
        firstAttemptNames.add(token);
        firstAttemptPaths.add(real);
        if (provenance.firstAttempt === provenance.total) {
          // Every occurrence in THIS file named the first attempt, so this citer is not
          // evidence for the shipping path at all.
          if (!firstAttemptOnlyCiters.has(real)) firstAttemptOnlyCiters.set(real, new Set());
          firstAttemptOnlyCiters.get(real).add(label);
        }
      }
    }
  }

  // A shipping path whose EVERY citer named only the first attempt is held out of
  // CITATION-GONE by the misattribution alone. That is the number worth printing.
  const firstAttemptSolePaths = [...firstAttemptPaths]
    .filter((real) => (firstAttemptOnlyCiters.get(real)?.size ?? 0) === regenerated.get(real).size)
    .sort();

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
      firstAttemptCredited: {
        occurrences: firstAttemptOccurrences,
        distinctNames: firstAttemptNames.size,
        paths: firstAttemptPaths.size,
        soleCitation: firstAttemptSolePaths.length,
        solePaths: firstAttemptSolePaths,
      },
      totalRows: rows.length,
    },
  };
}

// ------------------------------------------------------- SP-131 the needle review

/** The needle mode's row classes. A typed vocabulary, like CLASS above, and for the same
 *  reason: a fact must be able to assert the SPECIFIC check that tripped. */
export const NEEDLE_CLASS = Object.freeze({
  GONE: "NEEDLE-GONE",
  AMBIGUOUS: "NEEDLE-AMBIGUOUS",
  MOVED: "NEEDLE-MOVED",
  OUT_OF_RANGE: "CITATION-OUT-OF-RANGE",
});

const NEEDLE_CLASS_ORDER = [
  NEEDLE_CLASS.GONE,
  NEEDLE_CLASS.AMBIGUOUS,
  NEEDLE_CLASS.MOVED,
  NEEDLE_CLASS.OUT_OF_RANGE,
];

/** A FILE-QUALIFIED citation carrying an explicit line span: `Name.cs:426`, `Name.cs:427-429`,
 *  with or without a path prefix. The FILE half is character-for-character CITATION_TOKEN's own
 *  pattern (:248) and not a second, drifting copy of it — the two must agree about what a
 *  citation token is, or the needle mode would resolve names the default mode cannot. */
const CITATION_WITH_LINE = /([A-Za-z0-9_][A-Za-z0-9_.-]*\.(?:cs|xaml)\b):(\d+)(?:-(\d+))?/g;

/** A BARE `:NNN` / `:NNN-MMM` continuation — the form that names no file.
 *
 *  THE RULE, DEFINED HERE BECAUSE THE COVERAGE BLOCK PRINTS ITS COUNT AND A DIVERGENCE ROW
 *  QUOTES THAT COUNT. A number in a review row must come from the mechanism it describes, so
 *  the definition lives in the tool rather than in a one-off script:
 *    - the `:` must NOT follow a path or word character. That single lookbehind is what
 *      excludes the file-qualified form (`IntakeHostService.cs:433` — `s` precedes), a port
 *      number (`host:80`, `https://x.example:8080` — `t`/`e` precede), a ratio (`1:3`) and a
 *      timestamp (`12:34:56`, where BOTH colons follow a digit);
 *    - it must not be followed by a further digit, so a longer number cannot be split.
 *  What remains is `(:426`, `` `:418-420` ``, `at :55` — the continuation form as written.
 *  It is COUNTED and never resolved; see the header's second do-not-do bullet. */
const BARE_LINE_REFERENCE = /(?<![A-Za-z0-9_./\\-]):(\d+)(?:-(\d+))?(?!\d)/g;

/** Every 1-based line of `text` that CONTAINS `needle`. Literal, case-sensitive, Ordinal —
 *  the same match GoonGameCensusTests.cs's proven pin uses. A needle that is a substring is
 *  already immune to an indent change, which is why no whitespace normalisation is applied:
 *  deviating from a proven mechanism needs evidence, and there is none. */
export function needleLines(text, needle) {
  const hits = [];
  const lines = text.split("\n");
  for (let i = 0; i < lines.length; i++) if (lines[i].includes(needle)) hits.push(i + 1);
  return hits;
}

/** The file's bytes at a git endpoint, or null when the path did not exist there.
 *  null is "cannot compare", never "did not move": the difference is counted and printed. */
function readAtEndpoint(repoRoot, sha, relPath) {
  const result = runGit(repoRoot, ["show", `${sha}:${relPath}`]);
  return result.ok ? result.stdout : null;
}

/** Pure core of the second mode: {endpoint, rows, coverage}, PRINTS NOTHING.
 *  Same transposition as runDetector(), so fixtures can drive every branch. */
export function runNeedleReview({ repoRoot, since, inventoryPath } = {}) {
  if (!repoRoot) throw new DetectorError("runNeedleReview requires a repoRoot");
  const root = toPosix(path.resolve(repoRoot));
  const invPath = inventoryPath
    ? toPosix(path.resolve(inventoryPath))
    : `${root}/client/docs/upstream-citation-inventory.json`;

  let inventory;
  try {
    inventory = JSON.parse(fs.readFileSync(invPath, "utf8"));
  } catch (err) {
    throw new DetectorError(`inventory at ${invPath} is not readable/parseable JSON: ${err.message}`);
  }
  if (!inventory || !Array.isArray(inventory.entries)) {
    throw new DetectorError(`inventory at ${invPath} has no "entries" array`);
  }

  const wpfRoot = path.join(root, "ConditioningControlPanel");
  if (!fs.existsSync(wpfRoot)) {
    throw new DetectorError(`ConditioningControlPanel/ is absent under ${root} — nothing to re-grep needles in`);
  }

  // The comparison endpoint. Default: the state the inventory was RECORDED against, which is
  // why this mode is silent on a tree whose upstream has not moved past its own baseline —
  // and fires at the next sync, which is when the wave-64 rot was created. Verified BEFORE
  // any read, by the same helper the default mode uses, so a bad SHA is still named.
  const endpoint = since ?? inventory.baseline?.merge;
  if (!endpoint) {
    throw new DetectorError("no comparison endpoint: pass --since <sha>, or give the inventory a baseline.merge");
  }
  verifyEndpoint(root, endpoint, "comparison endpoint");

  // Resolve citation tokens against the SHIPPING universe only, by the same rule and the same
  // index the default mode uses (:482-487): an ambiguous basename picks nothing.
  const isWpfSource = (name) => /\.(?:cs|xaml)$/i.test(name);
  const shippingUniverse = walkFiles(wpfRoot, isWpfSource)
    .map((p) => p.slice(root.length + 1))
    .filter((p) => !isFirstAttemptPath(p));
  const shippingIndex = indexByBasename(shippingUniverse);

  const needled = inventory.entries.filter((e) => Array.isArray(e.needles) && e.needles.length > 0);
  const needledPaths = new Set(needled.map((e) => e.path));

  // --- scan the port for citation spans and bare continuations, in ONE pass
  const srcFiles = walkFiles(path.join(root, "client", "src"), (n) => /\.(?:cs|axaml)$/i.test(n));
  const docFiles = walkFiles(path.join(root, "client", "docs"), (n) => /\.md$/i.test(n));
  const spans = []; // only those resolving to a NEEDLED path
  let explicitTotal = 0;
  let bareTotal = 0;

  for (const file of [...srcFiles, ...docFiles]) {
    let text;
    try {
      text = fs.readFileSync(file, "utf8");
    } catch {
      continue; // a file that vanished mid-scan is not a reason to lie about the rest
    }
    const label = file.startsWith(`${root}/client/docs/`)
      ? `docs:${file.slice(`${root}/client/docs/`.length)}`
      : `src:${file.slice(`${root}/client/`.length)}`;

    let lineNumber = 0;
    for (const raw of text.split("\n")) {
      lineNumber += 1;
      for (const m of raw.matchAll(CITATION_WITH_LINE)) {
        explicitTotal += 1;
        const candidates = shippingIndex.get(m[1]);
        if (!candidates || candidates.length !== 1) continue; // unresolved or ambiguous: never picked
        if (!needledPaths.has(candidates[0])) continue; // FILE-level only, by design
        const from = Number(m[2]);
        const to = m[3] ? Number(m[3]) : from;
        spans.push({ path: candidates[0], citer: `${label}:${lineNumber}`, text: m[0], from, to });
      }
      for (const _bare of raw.matchAll(BARE_LINE_REFERENCE)) bareTotal += 1;
    }
  }

  // --- the needles themselves
  const rows = [];
  const currentLine = new Map(); // `${path}#${id}` -> line, for needles that anchor cleanly
  let located = 0;
  let gone = 0;
  let ambiguous = 0;
  let unchanged = 0;
  let moved = 0;
  let absentAtEndpoint = 0;
  let ambiguousAtEndpoint = 0;
  let needleTotal = 0;

  for (const entry of needled) {
    let current = null;
    try {
      current = fs.readFileSync(path.join(root, entry.path), "utf8");
    } catch {
      current = null; // the default mode already classes this as UNRESOLVED; here it is per-needle
    }
    const before = current === null ? null : readAtEndpoint(root, endpoint, entry.path);

    for (const needle of entry.needles) {
      needleTotal += 1;
      if (current === null) {
        gone += 1;
        rows.push({
          cls: NEEDLE_CLASS.GONE,
          path: entry.path,
          id: needle.id,
          needle: needle.needle,
          reason: "the cited file is absent from the working tree",
          action: "the entry's own path no longer exists; the default mode's UNRESOLVED row names the move",
        });
        continue;
      }

      const now = needleLines(current, needle.needle);
      if (now.length === 0) {
        gone += 1;
        rows.push({
          cls: NEEDLE_CLASS.GONE,
          path: entry.path,
          id: needle.id,
          needle: needle.needle,
          reason: "no line in the file contains this needle any more",
          action:
            "upstream deleted or rewrote the subject a landed claim names — re-read the file, then re-cite or retire the claim",
        });
        continue;
      }
      if (now.length > 1) {
        ambiguous += 1;
        rows.push({
          cls: NEEDLE_CLASS.AMBIGUOUS,
          path: entry.path,
          id: needle.id,
          needle: needle.needle,
          reason: `matches ${now.length} lines today (${now.join(", ")})`,
          action: "this needle can no longer anchor a line — lengthen it until it matches exactly one",
        });
        continue;
      }

      located += 1;
      currentLine.set(`${entry.path}#${needle.id}`, now[0]);

      if (before === null) {
        absentAtEndpoint += 1;
        continue; // cannot compare, and counted as such rather than reported as unmoved
      }
      const then = needleLines(before, needle.needle);
      if (then.length !== 1) {
        if (then.length === 0) absentAtEndpoint += 1;
        else ambiguousAtEndpoint += 1;
        continue;
      }
      if (then[0] === now[0]) {
        unchanged += 1;
        continue;
      }
      moved += 1;
      const delta = now[0] - then[0];
      rows.push({
        cls: NEEDLE_CLASS.MOVED,
        path: entry.path,
        id: needle.id,
        needle: needle.needle,
        reason: `:${then[0]} -> :${now[0]} (${delta > 0 ? "+" : ""}${delta}) since ${endpoint}`,
        citedBy: [...(entry.citedBy ?? [])],
        action:
          `upstream moved this subject; a citation into this file written against ${endpoint} is off by ` +
          `that much. Re-base it — this mode names the SHIFT, never which citation is wrong`,
      });
    }
  }

  // --- the citation spans into needled paths
  const lineCount = new Map();
  for (const p of needledPaths) {
    try {
      lineCount.set(p, fs.readFileSync(path.join(root, p), "utf8").split("\n").length);
    } catch {
      lineCount.set(p, 0);
    }
  }

  let confirmed = 0;
  let uncovered = 0;
  let outOfRange = 0;
  for (const span of spans) {
    const total = lineCount.get(span.path) ?? 0;
    if (total > 0 && span.to > total) {
      outOfRange += 1;
      rows.push({
        cls: NEEDLE_CLASS.OUT_OF_RANGE,
        path: span.path,
        id: null,
        needle: null,
        reason: `${span.text} cited from ${span.citer}, but the file has ${total} lines`,
        action: "the file shrank under this citation; re-read it and re-cite, or retire the claim",
      });
      continue;
    }
    const inside = [...currentLine.entries()].some(
      ([key, line]) => key.startsWith(`${span.path}#`) && line >= span.from && line <= span.to,
    );
    if (inside) confirmed += 1;
    else uncovered += 1;
  }

  rows.sort((a, b) => {
    const ci = NEEDLE_CLASS_ORDER.indexOf(a.cls) - NEEDLE_CLASS_ORDER.indexOf(b.cls);
    if (ci !== 0) return ci;
    if (a.path !== b.path) return a.path < b.path ? -1 : 1;
    return String(a.id) < String(b.id) ? -1 : String(a.id) > String(b.id) ? 1 : 0;
  });

  return {
    endpoint,
    rows,
    coverage: {
      endpoint,
      entries: {
        total: inventory.entries.length,
        needled: needled.length,
        bare: inventory.entries.length - needled.length,
      },
      needles: { total: needleTotal, located, gone, ambiguous },
      comparison: { unchanged, moved, absentAtEndpoint, ambiguousAtEndpoint },
      citations: { explicitTotal, intoNeedled: spans.length, confirmed, uncovered, outOfRange, bare: bareTotal },
      byClass: Object.fromEntries(NEEDLE_CLASS_ORDER.map((c) => [c, rows.filter((r) => r.cls === c).length])),
      totalRows: rows.length,
    },
  };
}

/** The coverage block, printed by BOTH modes on every run. An unstated coverage gap is how a
 *  review list becomes a false reassurance, so the gap is printed as numbers, not implied. */
export function formatNeedleCoverage(coverage) {
  const { entries, needles, comparison, citations } = coverage;
  return [
    "## NEEDLE COVERAGE",
    `  inventory entries: ${entries.total} — ${entries.needled} carry needle(s), ${entries.bare} do not. ` +
      `The ${entries.bare} stay FILE-level: this mode says NOTHING about their citation lines.`,
    `  needles: ${needles.total} declared — ${needles.located} anchor at exactly one line, ` +
      `${needles.gone} gone, ${needles.ambiguous} ambiguous`,
    `  compared against ${coverage.endpoint}: ${comparison.unchanged} unchanged, ${comparison.moved} moved, ` +
      `${comparison.absentAtEndpoint} not present there, ${comparison.ambiguousAtEndpoint} ambiguous there ` +
      `(the last two CANNOT be compared, and are not "unmoved")`,
    `  file-qualified citations with a line: ${citations.explicitTotal} in the port, ` +
      `${citations.intoNeedled} into a needled path — ${citations.confirmed} confirmed ` +
      `(a needle's current line is inside the span), ${citations.uncovered} uncovered ` +
      `(no needle describes that span), ${citations.outOfRange} past the end of the file`,
    // Every figure on this line is DERIVED by the run that prints it. The size of the rejected
    // heuristic's mis-binding is a hand measurement at a named commit and therefore lives in this
    // file's header with its SHA, not here: a number in derived output that the run did not derive
    // is the next thing to go stale silently.
    `  bare :NNN continuations: ${citations.bare} in the port — NOT CHECKED, in either mode. ` +
      `A bare reference names no file, and the nearest-preceding-citation-token heuristic was ` +
      `measured and rejected (this file's header carries the measurement and its SHA). LARGEST gap:`,
    "    FIVE of the SEVEN citations that motivated this mode — D232's rotted corpus, the reason it",
    "    exists — are of that form, so the mode cannot reach them. It reaches the other two.",
    "  also not checked: a citation that was wrong the day it was written (no needle knows what a",
    "  citation intended); an entry's citedBy, which no class re-derives once the entry exists; and",
    "  any citation into client/tools/** or into a .mjs file, which is outside the corpus entirely —",
    "  SP-131's own citations into detect.mjs rotted inside it, unseen by this tool.",
  ].join("\n");
}

export function formatNeedleReport(outcome) {
  const { endpoint, rows, coverage } = outcome;
  const out = [];
  out.push("UPSTREAM CITATION NEEDLE REVIEW LIST");
  out.push(`comparison endpoint: ${endpoint} .. working tree`);
  out.push("");

  // Grouped by PATH inside each class, so a citer list — which belongs to the entry and not to
  // the needle — is printed ONCE rather than repeated under every needle that moved. Nine
  // identical sixteen-name lists is the shape that gets a report skimmed instead of read.
  for (const cls of NEEDLE_CLASS_ORDER) {
    const group = rows.filter((r) => r.cls === cls);
    out.push(`## ${cls} (${group.length})`);
    if (group.length === 0) out.push("  (none)");
    let lastPath = null;
    for (const row of group) {
      if (row.path !== lastPath) {
        lastPath = row.path;
        out.push(`  ${row.path}`);
        if (row.citedBy) {
          out.push(`      cited by ${row.citedBy.length}: ${row.citedBy.length > 0 ? row.citedBy.join(", ") : "(nothing)"}`);
        }
      }
      out.push(`    ${row.id ? `#${row.id}` : "-"}  (${row.reason})`);
      if (row.needle !== null && row.needle !== undefined) out.push(`        needle: ${JSON.stringify(row.needle)}`);
      out.push(`        action: ${row.action}`);
    }
    out.push("");
  }

  out.push(formatNeedleCoverage(coverage));
  out.push(`  TOTAL ROWS: ${coverage.totalRows}`);
  out.push("");
  out.push("This is a REVIEW LIST, not a failure. Exit 0 means the detector ran.");
  return out.join("\n");
}

// -------------------------------------------------------------------- reporting

/** `needleCoverage` is the SP-131 coverage block, optional so the fourteen facts that call
 *  this with one argument keep working. When present it is emitted BEFORE the closing line,
 *  because a coverage gap printed after "this is a review list, not a failure" reads as an
 *  appendix rather than as part of the finding. */
export function formatReport(outcome, needleCoverage) {
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
  const fa = summary.firstAttemptCredited;
  out.push(
    `  citations naming CCP.*/tests but CREDITED to a shipping path (basename collision): ` +
      `${fa.occurrences} occurrence(s), ${fa.distinctNames} distinct name(s), ${fa.paths} shipping path(s)`,
  );
  out.push(
    `    of those, ${fa.soleCitation} shipping path(s) have NO other citer, so only this ` +
      `misattribution keeps them out of CITATION-GONE:`,
  );
  if (fa.solePaths.length === 0) out.push("      (none)");
  for (const p of fa.solePaths) out.push(`      ${p}`);
  out.push(`  TOTAL ROWS: ${summary.totalRows}`);
  out.push("");
  if (needleCoverage) {
    out.push(needleCoverage);
    out.push("");
  }
  out.push("This is a REVIEW LIST, not a failure. Exit 0 means the detector ran.");
  return out.join("\n");
}

// -------------------------------------------------------------------------- CLI

function parseArgs(argv) {
  const opts = { since: undefined, until: undefined, out: undefined, needles: false };
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
      case "--needles":
        opts.needles = true;
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
  "usage: node client/tools/citations/detect.mjs [--needles] [--since <sha>] [--until <sha>] [--out <path>]",
  "",
  "  Emits the upstream citation REVIEW LIST to stdout.",
  "  --since/--until override the window read from the inventory's baseline.",
  "  --needles emits the LINE-LEVEL review list instead: every inventory needle is re-grepped in",
  "            the working tree and compared with its position at the comparison endpoint.",
  "            --since overrides that endpoint (default: the inventory's baseline.merge).",
  "            --until has no meaning here and is REJECTED rather than silently ignored,",
  "            because this mode always compares against the WORKING TREE.",
  "  --out writes the same report to a file as well (opt-in; nothing is written otherwise).",
  "",
  "  Both modes print the NEEDLE COVERAGE block, every run: an unstated coverage gap is how a",
  "  review list becomes a false reassurance.",
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
  try {
    const repoRoot = findRepoRoot(cwd);
    if (opts.needles) {
      // --until is REJECTED, not ignored: this mode always compares against the working tree,
      // and a flag that is silently dropped is a tool lying about what it measured.
      if (opts.until !== undefined) {
        throw new DetectorError(
          "--until has no meaning with --needles: the needle mode always compares the comparison " +
            "endpoint against the WORKING TREE. Use --since to move the endpoint.",
        );
      }
      report = formatNeedleReport(runNeedleReview({ repoRoot, since: opts.since }));
    } else {
      // The coverage block is printed in BOTH modes, so the default report can never read as
      // complete: it answers a FILE-level question and says so, with numbers.
      const outcome = runDetector({ repoRoot, since: opts.since, until: opts.until });
      const needles = runNeedleReview({ repoRoot });
      report = formatReport(outcome, formatNeedleCoverage(needles.coverage));
    }
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
  // THE EXIT CONTRACT, AND IT COVERS BOTH MODES. No row count of either kind is consulted
  // here — that is why neither `outcome` nor the needle outcome escapes the try block above.
  // A review list with rows in it is the tool working, not the tool failing. Turning this
  // into `return outcome.rows.length ? 1 : 0` is what self-test fact F10 forbids, and what
  // CitationNeedleTests pins from the .NET floor for both modes.
  return 0;
}

if (process.argv[1] && import.meta.url === pathToFileURL(path.resolve(process.argv[1])).href) {
  process.exit(main(process.argv.slice(2), process.cwd()));
}

export const __testing = { fileURLToPath, CITATION_TOKEN, isFirstAttemptPath };
