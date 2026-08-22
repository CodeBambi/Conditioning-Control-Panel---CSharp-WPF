#!/usr/bin/env node
// Wave authoring guard: refuse a bad wave BEFORE any lane launches.
//
// A "wave" is a set of task packets executed CONCURRENTLY by subagent lanes in separate
// git worktrees. Two packets in one wave that claim the same path in File Scope is an
// AUTHORING defect, not a worker defect — and today it is discovered as a merge conflict
// after both lanes have already burned their budget. SP-072's own amendment records the
// class in the orchestrator's words: a two-lane plan was killed because every packet that
// adds a test bumps client/tests/floor/floor.json in the same commit, so any lane-mate
// collides there — "green alone, RED at merge" (the SP-057/SP-058 Program.cs precedent).
// That reasoning was done by hand. This is the mechanical version of it.
//
// Usage:
//   node client/tools/wave/validate-wave.mjs SP-073-slug SP-074-slug [...]
//
// Exit 0 = every check passed for every packet (prints one WAVE OK line).
// Exit 1 = at least one violation. EVERY violation is listed — never stop at the first,
//          because an author who fixes one and relaunches only to hit the next has paid
//          the round-trip twice.
//
// The eight checks, and WHY each exists:
//   1. Packet directory exists and parses as SP-<number>-...  — an unparseable ID escapes
//      the ID rules below (same refusal-to-go-blind as FloorWrapperGuardTests).
//   2. A parseable `| testCommand | `...` |` row, and if it invokes `dotnet test` it routes
//      through check-floor.mjs — the SP-065 half-install failure. Identical rule, identical
//      regex and identical grandfather ID to FloorWrapperGuardTests.cs; see MIRROR note.
//   3. A `| floorDelta | `spine-tasks/<this packet>/floor-delta.json` |` row naming ITS OWN
//      folder — a lane that writes its delta into a neighbour's folder (or into a folder
//      that does not exist) hands the orchestrator a delta it cannot attribute at land.
//   4. client/tests/floor/floor.json declared in fileScopeMustNotChange — the shared floor
//      pin is the exact file SP-072's amendment names as the guaranteed collision. Lanes
//      declare deltas; the orchestrator reconciles the pin at land.
//   5. client/docs/task-board.md declared in fileScopeMustNotChange whenever the wave has
//      MORE THAN ONE packet — the board is a shared chokepoint reconciled at land. One
//      lane may hold it; two lanes editing it in one wave is the defect. (A single-packet
//      wave has no lane-mate, so this check does not apply — stated, not silent.)
//   6. File Scope disjointness across the wave, GLOB-AWARE. The whole point of the script.
//   7. No packet named twice — a duplicated lane is two workers in one scope by definition.
//   8. No shared SP-<number> in the wave, and no wave number at or below the highest number
//      already on disk — a reissued ID silently overwrites history.
//
// CONVERGENCE note (SP-136 — this REPLACED a MIRROR note, and the replacement is the point):
// checks 2-5, the packet enumeration and the contract-row parsing are CONSUMED BY
// client/tests/CcpClient.Tests/FloorWrapperGuardTests.cs, not re-implemented there. That guard
// runs `--emit-packet-scopes` (below), reads the verdicts out of the JSON, and applies only its
// own two grandfather IDs and its own message text on top. It holds no coverage predicate, no
// contract-row regex and no wrapper-token test of its own.
//
// This used to be a comment saying "do not let these two drift", sitting between two
// implementations. They drifted anyway, on check 4: this file asked glob-aware coverage
// (patternCovers) and FloorWrapperGuardTests.cs:224 asked a literal substring (String.Contains),
// so `client/tests/floor/**` printed WAVE OK here and RED there. Both wave-60 packets were
// authored that way and the base was red from the authoring commit onward. A comment cannot
// enforce anything. There is now nothing to drift because there is only one implementation.
//
// The GRANDFATHER RULES are deliberately NOT shared: they are each guard's own binding question,
// not a shared semantics, and the two are genuinely different. This file binds check 2 from
// SP-065 (FIRST_BOUND_PACKET_NUMBER, referenced there and nowhere else) and binds checks 3-5 with
// NO packet-number condition at all; the C# guard binds its floorDelta and shared-pin test from
// SP-073 and re-applies that AFTER consuming these verdicts. 60 of the 128 packets on disk
// violate check 4 here and are correctly invisible to the C# guard, which is why the C# side
// must keep re-applying its own rule and why a fact pins that it still does.
//
// Usage of the projection mode, and its EXIT-CODE CONTRACT:
//   node client/tools/wave/validate-wave.mjs --emit-packet-scopes <spineTasksDir>
// Exit 0 = a projection was produced and written to stdout as JSON. A corpus full of violations
//          is still exit 0: violations are reported as DATA, per packet, because the consumer
//          applies its own binding rule to them. Exit 0 never means "the corpus is clean".
// Exit 2 = no projection could be produced (no directory named, or it is not a directory). The
//          consumer must treat any non-zero exit, any abnormal termination and any unparseable
//          stdout as a hard failure, never as an empty corpus — an oracle that returns nothing
//          would make every guard consuming it vacuously green.
// The mode takes an EXPLICIT directory and never guesses a tree, so it is safe to run from a
// copy of this file anywhere on disk. That is what makes the one-sided-update demonstration in
// WaveGuardConvergenceTests possible.
//
// This script reads. It never writes, never touches git, and never runs a test.

import fs from "node:fs";
import path from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";

const HERE = path.dirname(fileURLToPath(import.meta.url));
// client/tools/wave -> client/tools -> client -> repo root
const REPO_ROOT = path.resolve(HERE, "..", "..", "..");
const REPO_ANCHOR = path.join(REPO_ROOT, "client", "CcpClient.sln");
const SPINE_TASKS = path.join(REPO_ROOT, "spine-tasks");

// The first packet the floor-wrapper rule binds. Byte-for-byte the C# guard's constant.
const FIRST_BOUND_PACKET_NUMBER = 65;
const WRAPPER_TOKEN = "check-floor.mjs";

// Shared chokepoints. These are paths, not patterns: a declaration COVERS them if it
// matches them (so `client/docs/**` satisfies the task-board requirement).
const FLOOR_PIN_PATH = "client/tests/floor/floor.json";
const TASK_BOARD_PATH = "client/docs/task-board.md";

const PACKET_NUMBER = /^SP-(\d+)-/i;
// Exact port of FloorWrapperGuardTests.TestCommandRow(). The cell is ONE backticked value,
// so `[^`]+` tolerates the pipes and ampersands a real testCommand contains.
const TEST_COMMAND_ROW = /\|\s*testCommand\s*\|\s*`([^`]+)`\s*\|/i;
const DOTNET_TEST = /\bdotnet\s+test\b/i;

export class WaveError extends Error {}

// fail() is for the can't-proceed class ONLY (no arguments, no spine-tasks/, wrong tree).
// Per-packet and per-wave findings accumulate in a violations array instead, because the
// contract is "list every violation", not "abort on the first".
function fail(message) {
  throw new WaveError(message);
}

// ---------------------------------------------------------------------------
// Path/glob machinery
// ---------------------------------------------------------------------------

export function toSegments(pattern) {
  return pattern
    .replace(/\\/g, "/")
    .replace(/^\.\//, "")
    .replace(/^\/+/, "")
    .replace(/\/+$/, "")
    .split("/")
    .filter((s) => s.length > 0 && s !== ".");
}

// Char-level intersection of two SINGLE-segment globs ('*' = any run within the segment,
// '?' = one char). Answers "does some string match both", not "does one match the other" —
// needed because both sides of a File Scope comparison may carry wildcards.
// Comparison is case-INSENSITIVE on purpose: a case-only difference between two lanes'
// declarations is the same file on a Windows or macOS checkout, so treating it as distinct
// would hand back exactly the false confidence this script exists to remove.
function segmentIntersects(x, y) {
  const memo = new Map();
  const walk = (i, j) => {
    const key = `${i},${j}`;
    const hit = memo.get(key);
    if (hit !== undefined) {
      return hit;
    }
    let result;
    if (i >= x.length && j >= y.length) {
      result = true;
    } else if (i >= x.length) {
      result = /^\*+$/.test(y.slice(j));
    } else if (j >= y.length) {
      result = /^\*+$/.test(x.slice(i));
    } else if (x[i] === "*") {
      result = walk(i + 1, j) || walk(i, j + 1);
    } else if (y[j] === "*") {
      result = walk(i, j + 1) || walk(i + 1, j);
    } else if (x[i] === "?" || y[j] === "?" || x[i].toLowerCase() === y[j].toLowerCase()) {
      result = walk(i + 1, j + 1);
    } else {
      result = false;
    }
    memo.set(key, result);
    return result;
  };
  return walk(0, 0);
}

// Do two File Scope patterns claim any path in common?
//
// Deliberately NOT string equality: a naive check misses `client/src/Foo/**` vs
// `client/src/Foo/Bar.cs` and `a/**` vs `a/b/**`, and a disjointness check that misses a
// prefix overlap is worse than no check at all — it grants false confidence at exactly the
// moment the author is deciding to launch two lanes.
//
// Two semantics, both chosen to err toward REPORTING an overlap (the safe direction — a
// false report costs the author one re-read, a missed one costs two lanes their budget):
//   * '**' matches zero or more segments and is split EXACTLY (every alignment tried), not
//     short-circuited — so `client/tests/**/Audio.cs` and `client/src/**/Video.cs` are
//     correctly disjoint on their second segment rather than waved through by the '**'.
//   * a declared path also claims everything BENEATH it, so `client/src/Foo` overlaps
//     `client/src/Foo/Bar.cs`. This is the rule that catches the prefix case, and it is
//     applied by exhaustion rather than by guessing which segments are files.
//
// KNOWN over-report, stated rather than hidden: because a declared path claims its subtree
// and nothing here decides that `Audio.cs` is a leaf, `client/tests/**/Audio.cs` and
// `client/tests/**/Video.cs` are reported as overlapping (both subtrees contain
// `client/tests/x/Audio.cs/Video.cs`). The obvious fix — treat a dotted segment as a file —
// is REJECTED: `client/src/CcpClient.Desktop` is a real directory with a dot in it, and
// that heuristic would stop it covering its own contents. A missed overlap is the failure
// this script exists to prevent; an over-report is not.
export function patternsIntersect(patternA, patternB) {
  const a = toSegments(patternA);
  const b = toSegments(patternB);
  const memo = new Map();
  const walk = (i, j) => {
    const key = `${i},${j}`;
    const hit = memo.get(key);
    if (hit !== undefined) {
      return hit;
    }
    let result;
    if (i >= a.length || j >= b.length) {
      // Every segment so far intersected and one side is exhausted: the exhausted side is
      // a directory prefix of the other (or they are the same length and equal).
      result = true;
    } else if (a[i] === "**") {
      // Try every alignment: a's '**' absorbing 0..n of b's remaining segments.
      result = false;
      for (let k = j; k <= b.length && !result; k++) {
        result = walk(i + 1, k);
      }
    } else if (b[j] === "**") {
      result = false;
      for (let k = i; k <= a.length && !result; k++) {
        result = walk(k, j + 1);
      }
    } else if (!segmentIntersects(a[i], b[j])) {
      result = false;
    } else {
      result = walk(i + 1, j + 1);
    }
    memo.set(key, result);
    return result;
  };
  return walk(0, 0);
}

// Does a declared pattern COVER a specific literal path? One-sided and strict, unlike
// patternsIntersect: used for the "must declare this exact chokepoint" checks (4 and 5),
// where accepting a pattern that merely brushes the path would let a lane declare its way
// out of the requirement.
export function patternCovers(pattern, literalPath) {
  const pat = toSegments(pattern);
  const target = toSegments(literalPath);
  const walk = (i, j) => {
    if (i >= pat.length) {
      return true; // directory-prefix declaration covers everything beneath it
    }
    if (pat[i] === "**") {
      for (let k = j; k <= target.length; k++) {
        if (walk(i + 1, k)) {
          return true;
        }
      }
      return false;
    }
    if (j >= target.length) {
      return false; // the pattern demands more segments than the path has
    }
    if (!segmentIntersects(pat[i], target[j])) {
      return false;
    }
    return walk(i + 1, j + 1);
  };
  return walk(0, 0);
}

// ---------------------------------------------------------------------------
// THE SHARED CHOKEPOINT DECISION — one implementation, consulted by two guards
// ---------------------------------------------------------------------------

// SP-136. This IS check 4, it IS check 5, and it is ALSO the whole of what
// FloorWrapperGuardTests asks about the shared floor pin. The C# guard consults it through
// --emit-packet-scopes and holds no predicate of its own, so the two cannot disagree.
//
// WHY GLOB COVERAGE IS THE SURVIVING SEMANTICS, decided against what the rule is FOR. The rule
// exists so a lane cannot edit client/tests/floor/floor.json — every test-adding packet would
// otherwise bump one line of one file and collide with every lane-mate (SP-072 amendment: green
// alone, RED at merge). A packet declaring `client/tests/**` forbids the pin at least as
// completely as one naming it, so it satisfies the PROPERTY. The literal substring tested the
// SPELLING instead, and was wrong in BOTH directions, not merely narrower: unanchored to a
// backticked value, it ACCEPTED a cell declaring `client/tests/floor/floor.json.bak` — a
// different file the lane may freely edit — while rejecting a glob that genuinely covers the pin.
//
// MEASURED over the 128 packets on disk at 766be7ac0 before the choice was made, in both
// directions: adopting coverage changes the verdict of ZERO bound packets (>= SP-073), while
// adopting the literal would newly REJECT twelve packets the validator accepts today, every one
// of which declares `client/tests/**`. Stated plainly because the record must not imply more:
// on today's corpus this is a FORWARD-LOOKING hardening and not a repair of a live failure —
// all 16 bound packets that declare `client/tests/floor/**` also carry the literal in the same
// cell, which is why the tree is green. What it buys is that the next packet declaring only the
// glob cannot print WAVE OK and red the suite.
export function declarationCoversChokepoint(declaredValues, chokepointPath) {
  return declaredValues.some((p) => patternCovers(p, chokepointPath));
}

// One guard's whole question about one PROMPT.md: is there a fileScopeMustNotChange row at all,
// and does what it declares cover this chokepoint? Row absence is reported SEPARATELY from
// non-coverage because the two are different failures with different messages on both sides.
export function chokepointVerdict(lines, chokepointPath) {
  const rows = matchContractRows(lines, "fileScopeMustNotChange");
  const declared = rows.flatMap((r) => r.values);
  return {
    rowFound: rows.length > 0,
    rowLine: rows.length > 0 ? rows[0].line : 0,
    declared,
    covered: declarationCoversChokepoint(declared, chokepointPath),
  };
}

// THE FIXTURE BOTH SIDES MUST SATISFY. Each case is a synthetic PROMPT.md fragment with the
// verdict the shared decision must return. The JS refuses a wave when it disagrees with this
// (see main()); the C# pins the SAME verdicts in its own source — deliberately not read from
// here, because a fact whose expected and actual both come out of this file would self-certify
// and a lockstep edit would sail through it.
//
// Case `covering-glob-floor-dir` is the one that started this: it is the exact declaration both
// wave-60 packets carried.
export const FLOOR_PIN_COVERAGE_CASES = [
  {
    id: "literal-only",
    why: "naming the pin exactly is the declaration the C# guard used to demand, and it still passes",
    chokepoint: "client/tests/floor/floor.json",
    lines: ["| fileScopeMustNotChange | `client/tests/floor/floor.json`, `client/src/**` |"],
    expect: { rowFound: true, covered: true },
  },
  {
    id: "covering-glob-floor-dir",
    why: "THE CASE THAT STARTED THIS — both wave-60 packets declared exactly this and reddened the base",
    chokepoint: "client/tests/floor/floor.json",
    lines: ["| fileScopeMustNotChange | `client/tests/floor/**`, `client/src/**` |"],
    expect: { rowFound: true, covered: true },
  },
  {
    id: "covering-glob-tests-tree",
    why: "the declaration all twelve pre-SP-073 packets carry; it forbids the pin and more",
    chokepoint: "client/tests/floor/floor.json",
    lines: ["| fileScopeMustNotChange | `ConditioningControlPanel/**`, `client/tests/**` |"],
    expect: { rowFound: true, covered: true },
  },
  {
    id: "covering-glob-client-tree",
    why: "a blanket ban over the whole client tree covers the pin a fortiori",
    chokepoint: "client/tests/floor/floor.json",
    lines: ["| fileScopeMustNotChange | `client/**` |"],
    expect: { rowFound: true, covered: true },
  },
  {
    id: "directory-prefix-without-a-glob",
    why: "a declared directory claims everything beneath it, the same rule patternsIntersect uses",
    chokepoint: "client/tests/floor/floor.json",
    lines: ["| fileScopeMustNotChange | `client/tests/floor` |"],
    expect: { rowFound: true, covered: true },
  },
  {
    id: "sibling-file-that-is-not-the-pin",
    why: "THE LITERAL RULE'S OTHER DEFECT: String.Contains ACCEPTS this cell, because the pin's path is a substring of a DIFFERENT file the lane may freely edit. Coverage refuses it",
    chokepoint: "client/tests/floor/floor.json",
    lines: ["| fileScopeMustNotChange | `client/tests/floor/floor.json.bak` |"],
    expect: { rowFound: true, covered: false },
  },
  {
    id: "near-miss-directory",
    why: "a segment that merely starts the same must not cover — coverage is per segment, not per prefix string",
    chokepoint: "client/tests/floor/floor.json",
    lines: ["| fileScopeMustNotChange | `client/tests/floorX/**` |"],
    expect: { rowFound: true, covered: false },
  },
  {
    id: "unrelated-tree",
    why: "the ordinary refusal: a real declaration that simply does not reach the pin",
    chokepoint: "client/tests/floor/floor.json",
    lines: ["| fileScopeMustNotChange | `client/docs/**`, `ConditioningControlPanel/**` |"],
    expect: { rowFound: true, covered: false },
  },
  {
    id: "prose-mention-not-backticked",
    why: "the path named in prose inside the cell is not a DECLARATION; String.Contains counted it, backtick extraction does not",
    chokepoint: "client/tests/floor/floor.json",
    lines: ["| fileScopeMustNotChange | do not edit client/tests/floor/floor.json under any circumstances |"],
    expect: { rowFound: true, covered: false },
  },
  {
    id: "windows-separators",
    why: "a packet authored on Windows may carry backslashes; the same file must not read as two",
    chokepoint: "client/tests/floor/floor.json",
    lines: ["| fileScopeMustNotChange | `client\\tests\\floor\\**` |"],
    expect: { rowFound: true, covered: true },
  },
  {
    id: "case-only-difference",
    why: "a case-only difference is the same file on a Windows or macOS checkout, so it must not read as a different one",
    chokepoint: "client/tests/floor/floor.json",
    lines: ["| fileScopeMustNotChange | `Client/Tests/Floor/**` |"],
    expect: { rowFound: true, covered: true },
  },
  {
    id: "second-row-carries-it",
    why: "the declared set is the UNION across every matching row; one covering value anywhere in it satisfies the rule",
    chokepoint: "client/tests/floor/floor.json",
    lines: [
      "| fileScopeMustNotChange | `client/docs/**` |",
      "| fileScopeMustNotChange | `client/tests/floor/**` |",
    ],
    expect: { rowFound: true, covered: true },
  },
  {
    id: "no-scope-row-at-all",
    why: "refusal to go blind: an absent row is its own failure on both sides and must never read as covered",
    chokepoint: "client/tests/floor/floor.json",
    lines: ["| testCommand | `node client/tests/floor/check-floor.mjs` |"],
    expect: { rowFound: false, covered: false },
  },
];

// The fixture run through the shared decision. Returned as data (never thrown) so the projection
// can carry it to the C# side and both can judge it.
export function evaluateCoverageCases() {
  return FLOOR_PIN_COVERAGE_CASES.map((c) => {
    const verdict = chokepointVerdict(c.lines, c.chokepoint);
    return {
      id: c.id,
      why: c.why,
      chokepoint: c.chokepoint,
      expect: c.expect,
      actual: { rowFound: verdict.rowFound, covered: verdict.covered },
      declared: verdict.declared,
    };
  });
}

export function coverageCaseMismatches() {
  const bad = [];
  for (const c of evaluateCoverageCases()) {
    if (c.actual.rowFound !== c.expect.rowFound || c.actual.covered !== c.expect.covered) {
      bad.push(
        `[fixture] case '${c.id}': the shared chokepoint decision returned ` +
        `{rowFound:${c.actual.rowFound}, covered:${c.actual.covered}} but this fixture pins ` +
        `{rowFound:${c.expect.rowFound}, covered:${c.expect.covered}} — ${c.why}`
      );
    }
  }
  return bad;
}

// ---------------------------------------------------------------------------
// THE SHARED WRAPPER-ROUTING DECISION — the SP-065 rule, decided once
// ---------------------------------------------------------------------------
//
// SP-136 REVIEW FIX. The first cut of this convergence closed the drift on check 4 and OPENED it
// on check 2: routing the SP-065 verdict through the projection deleted the C# guard's own
// `DOTNET_TEST.test(command) && !command.includes(WRAPPER_TOKEN)` and replaced it with two
// booleans that NOTHING pinned. A single substitution below — `routesThroughWrapper: true` —
// silences check 2 in the wave gate AND
// PacketsAtOrAboveSp065_RouteDotnetTestThroughTheFloorWrapper at the same moment; before the
// convergence, that same edit was caught by the C# side. A decision consumed in two places needs a
// fixture exactly as the coverage decision does. Having built one for coverage only was the same
// defect class, one check to the left.
export function wrapperRoutingVerdict(command) {
  return {
    invokesDotnetTest: DOTNET_TEST.test(command),
    routesThroughWrapper: command.includes(WRAPPER_TOKEN),
  };
}

// The fixture both sides must satisfy for check 2. Same arrangement as the coverage fixture: the
// C# pins these verdicts in its OWN source, so a lockstep edit to this file and its fixture still
// reds there.
export const WRAPPER_ROUTING_CASES = [
  {
    id: "bare-dotnet-test",
    why: "THE SP-065 HALF-INSTALL FAILURE: a bare invocation lets an unexpected skip read green, and it must be refused",
    command: "dotnet test client/tests/CcpClient.Tests/CcpClient.Tests.csproj -c Debug",
    expect: { invokesDotnetTest: true, routesThroughWrapper: false },
  },
  {
    id: "wrapper-routed",
    why: "the only accepted shape for a bound packet: the suite runs through the floor wrapper",
    command: "node client/tests/floor/check-floor.mjs",
    expect: { invokesDotnetTest: false, routesThroughWrapper: true },
  },
  {
    id: "wrapper-routed-alongside-dotnet-test",
    why: "a command that does both is accepted — the rule bans an UNWRAPPED invocation, never `dotnet test` itself",
    command: "node client/tests/floor/check-floor.mjs && dotnet test client/tests/CcpClient.Tests/CcpClient.Tests.csproj",
    expect: { invokesDotnetTest: true, routesThroughWrapper: true },
  },
  {
    id: "not-a-dotnet-test-command",
    why: "a packet whose contract runs something else entirely is not bound by the wrapper rule at all",
    command: "pwsh client/tools/verify/capture.ps1 -Surface rail-door -State selected",
    expect: { invokesDotnetTest: false, routesThroughWrapper: false },
  },
  {
    id: "dotnet-test-with-extra-whitespace",
    why: "the token is matched on word boundaries with flexible spacing, so `dotnet  test` is still an invocation",
    command: "dotnet  test client/tests/CcpClient.Tests/CcpClient.Tests.csproj",
    expect: { invokesDotnetTest: true, routesThroughWrapper: false },
  },
  {
    id: "dotnet-build-is-not-dotnet-test",
    why: "a near miss that must NOT read as an invocation, or every build-only packet would be refused",
    command: "dotnet build client/CcpClient.sln -c Debug",
    expect: { invokesDotnetTest: false, routesThroughWrapper: false },
  },
];

export function evaluateWrapperCases() {
  return WRAPPER_ROUTING_CASES.map((c) => ({
    id: c.id,
    why: c.why,
    command: c.command,
    expect: c.expect,
    actual: wrapperRoutingVerdict(c.command),
  }));
}

export function wrapperCaseMismatches() {
  const bad = [];
  for (const c of evaluateWrapperCases()) {
    if (c.actual.invokesDotnetTest !== c.expect.invokesDotnetTest || c.actual.routesThroughWrapper !== c.expect.routesThroughWrapper) {
      bad.push(
        `[fixture] wrapper case '${c.id}': the shared routing decision returned ` +
        `{invokesDotnetTest:${c.actual.invokesDotnetTest}, routesThroughWrapper:${c.actual.routesThroughWrapper}} but this ` +
        `fixture pins {invokesDotnetTest:${c.expect.invokesDotnetTest}, routesThroughWrapper:${c.expect.routesThroughWrapper}} — ${c.why}`
      );
    }
  }
  return bad;
}

// ---------------------------------------------------------------------------
// Packet enumeration and PROMPT.md contract parsing
// ---------------------------------------------------------------------------

// Every directory in spine-tasks/ whose name parses as SP-<number>-, with its number.
// Directory presence — not PROMPT.md presence — is what OCCUPIES an ID, so check 8 reads
// this set: a packet folder with no PROMPT.md yet still owns its number.
export function enumerateOnDiskPackets(spineTasksDir) {
  const packets = [];
  for (const entry of fs.readdirSync(spineTasksDir, { withFileTypes: true })) {
    if (!entry.isDirectory()) {
      continue;
    }
    const match = PACKET_NUMBER.exec(entry.name);
    if (match) {
      packets.push({ dir: entry.name, number: Number.parseInt(match[1], 10) });
    }
  }
  return packets;
}

// The FloorWrapperGuardTests enumeration: spine-tasks/<packet>/PROMPT.md at EXACTLY one
// level (nested evidence/review artifacts are not packets). A directory that holds a
// packet-root PROMPT.md but does not parse as SP-<number>- is a violation there and is a
// violation here for the same reason — it would silently escape every ID rule.
export function enumeratePromptDirs(spineTasksDir) {
  const dirs = [];
  for (const entry of fs.readdirSync(spineTasksDir, { withFileTypes: true })) {
    if (entry.isDirectory() && fs.existsSync(path.join(spineTasksDir, entry.name, "PROMPT.md"))) {
      dirs.push(entry.name);
    }
  }
  return dirs;
}

// All `| <field> | ... |` rows in a PROMPT.md, with 1-based line numbers. Same row anchor
// as the C# guard, extended to cells that carry a comma-separated list of backticked
// values (fileScopeMustChange / fileScopeMustNotChange / artifactsMustExist). The values
// are pulled by backtick pairs rather than by splitting on '|' or ',', so a path is never
// mangled by a stray separator.
export function matchContractRows(lines, field) {
  const anchor = new RegExp(`\\|\\s*${field}\\s*\\|(.*)$`, "i");
  const rows = [];
  for (let i = 0; i < lines.length; i++) {
    const hit = anchor.exec(lines[i]);
    if (!hit) {
      continue;
    }
    const values = [...hit[1].matchAll(/`([^`]+)`/g)].map((m) => m[1].trim()).filter((v) => v.length > 0);
    rows.push({ line: i + 1, raw: lines[i], values });
  }
  return rows;
}

// The delta SCHEMA is owned by client/tests/floor/sum-deltas.mjs, which is what actually
// sums these at land, and by FloorWrapperGuardTests, which binds the row. This reader must
// agree with them exactly: { packet, unit, headless, reason }, where `unit` targets
// CcpClient.Tests and `headless` targets CcpClient.HeadlessTests, both integers that MAY be
// negative (a packet may delete tests). Two validators disagreeing about the shape of the
// file is worse than one validator, because a wave would pass here and fail at land — so
// this deliberately re-checks only what it needs and defers every richer rule to
// sum-deltas.mjs rather than growing a second, divergent opinion.
function readFloorDelta(deltaPath) {
  if (!fs.existsSync(deltaPath)) {
    return { present: false, unit: 0, headless: 0 };
  }
  let parsed;
  try {
    parsed = JSON.parse(fs.readFileSync(deltaPath, "utf8"));
  } catch (err) {
    return { present: true, error: `is not parseable JSON: ${err.message}` };
  }
  if (!parsed || typeof parsed !== "object") {
    return { present: true, error: "is not a JSON object" };
  }
  for (const field of ["unit", "headless"]) {
    if (!Number.isInteger(parsed[field])) {
      return {
        present: true,
        error: `has no integer "${field}" field (schema is {packet, unit, headless, reason}, ` +
          "the shape client/tests/floor/sum-deltas.mjs sums at land)",
      };
    }
  }
  if (typeof parsed.reason !== "string" || parsed.reason.trim().length === 0) {
    return { present: true, error: 'has an empty or missing "reason" — an unexplained bump is how a wrong pin survives review' };
  }
  return { present: true, unit: parsed.unit, headless: parsed.headless };
}

// ---------------------------------------------------------------------------
// One packet, inspected once — checks 1-5 and the parsed rows they rest on
// ---------------------------------------------------------------------------
//
// validateWave() calls this for each packet the wave names; emitPacketScopes() calls it for
// every packet on disk so the C# floor guard consumes THESE verdicts instead of computing its
// own. `waveSize` gates check 5 and nothing else — the task board is a chokepoint only when
// there IS a lane-mate — so the projection passes 1 and therefore never reports a board
// violation, which is correct because the C# guard has no board rule to compare against.
//
// Returns { violations, packet, record }. `packet` is null on the two check-1 failures (the
// wave cannot carry a packet it could not identify); `record` is null whenever there is no
// PROMPT.md to parse.
export function inspectPacket(name, spineTasksDir, waveSize) {
  const violations = [];
  const relPrompt = `spine-tasks/${name}/PROMPT.md`;
  const dir = path.join(spineTasksDir, name);
  const promptPath = path.join(dir, "PROMPT.md");

  // check 1
  if (!fs.existsSync(dir) || !fs.statSync(dir).isDirectory()) {
    violations.push(`[check 1] ${name}: no such packet directory at spine-tasks/${name} — a wave cannot launch a lane on a packet that is not authored`);
    return { violations, packet: null, record: null };
  }
  const numberMatch = PACKET_NUMBER.exec(name);
  if (!numberMatch) {
    violations.push(`[check 1] ${name}: packet directory name does not parse as SP-<number>-... — every ID rule below would silently pass`);
    return { violations, packet: null, record: null };
  }
  const number = Number.parseInt(numberMatch[1], 10);
  if (!fs.existsSync(promptPath)) {
    violations.push(`[check 2] ${name}: no PROMPT.md at ${relPrompt} — nothing to validate; the lane would launch uncontracted`);
    return { violations, packet: { name, number, mustChange: [] }, record: null };
  }
  const lines = fs.readFileSync(promptPath, "utf8").split(/\r?\n/);

  // check 2: testCommand row + the SP-065 floor-wrapper routing rule
  const testCommandRows = [];
  for (let i = 0; i < lines.length; i++) {
    const hit = TEST_COMMAND_ROW.exec(lines[i]);
    if (hit) {
      testCommandRows.push({
        line: i + 1,
        command: hit[1],
        ...wrapperRoutingVerdict(hit[1]),
      });
    }
  }
  if (testCommandRows.length === 0) {
    violations.push(
      `[check 2] ${relPrompt}:1: no parseable \`| testCommand | \`...\` |\` row — ` +
      "the wave guard refuses to go blind on a packet it is about to launch (SP-065)"
    );
  } else if (testCommandRows.length > 1) {
    violations.push(
      `[check 2] ${relPrompt}:${testCommandRows.map((r) => r.line).join(",")}: ${testCommandRows.length} testCommand rows — ` +
      "an ambiguous contract cannot be gated; leave exactly one"
    );
  }
  for (const row of testCommandRows) {
    if (number >= FIRST_BOUND_PACKET_NUMBER && row.invokesDotnetTest && !row.routesThroughWrapper) {
      violations.push(
        `[check 2] ${relPrompt}:${row.line}: testCommand invokes \`dotnet test\` without routing through ${WRAPPER_TOKEN} — ` +
        "every packet >= SP-065 must run the suite through the floor wrapper (the SP-065 half-install failure; " +
        "an unexpected skip must fail the contract, not read green)"
      );
    }
  }

  // check 3: floorDelta row naming THIS packet's own folder
  const expectedDelta = `spine-tasks/${name}/floor-delta.json`;
  const floorDeltaRows = matchContractRows(lines, "floorDelta");
  let deltaPath = null;
  if (floorDeltaRows.length === 0) {
    violations.push(
      `[check 3] ${relPrompt}:1: no \`| floorDelta | \`${expectedDelta}\` |\` row — ` +
      "a lane with no declared delta file leaves the orchestrator reconciling the shared floor pin by guesswork at land"
    );
  } else if (floorDeltaRows.length > 1) {
    violations.push(
      `[check 3] ${relPrompt}:${floorDeltaRows.map((r) => r.line).join(",")}: ${floorDeltaRows.length} floorDelta rows — ` +
      "an ambiguous delta declaration cannot be reconciled; leave exactly one"
    );
  } else {
    const row = floorDeltaRows[0];
    if (row.values.length !== 1) {
      violations.push(
        `[check 3] ${relPrompt}:${row.line}: floorDelta cell must carry exactly one backtick-quoted path, found ${row.values.length} — ` +
        `expected \`${expectedDelta}\``
      );
    } else {
      const declared = row.values[0].replace(/\\/g, "/").replace(/^\.\//, "");
      if (declared !== expectedDelta) {
        violations.push(
          `[check 3] ${relPrompt}:${row.line}: floorDelta declares '${declared}' but this packet is '${name}' — ` +
          `it must name ITS OWN folder (\`${expectedDelta}\`); a delta written into another packet's folder cannot be attributed at land`
        );
      } else {
        deltaPath = path.join(spineTasksDir, name, "floor-delta.json");
      }
    }
  }

  // File Scope cells, shared by checks 4, 5 and 6.
  const mustChangeRows = matchContractRows(lines, "fileScopeMustChange");
  const mustNotChangeRows = matchContractRows(lines, "fileScopeMustNotChange");
  const mustChange = mustChangeRows.flatMap((r) => r.values);
  const floorPin = chokepointVerdict(lines, FLOOR_PIN_PATH);
  const taskBoard = chokepointVerdict(lines, TASK_BOARD_PATH);
  if (mustChangeRows.length === 0) {
    violations.push(
      `[check 6] ${relPrompt}:1: no \`| fileScopeMustChange | ... |\` row — ` +
      "disjointness cannot be computed for a lane that declares no scope, and an unknown scope overlaps everything"
    );
  } else if (mustChange.length === 0) {
    violations.push(`[check 6] ${relPrompt}:${mustChangeRows[0].line}: fileScopeMustChange carries no backtick-quoted path`);
  }
  if (!floorPin.rowFound) {
    violations.push(
      `[check 4] ${relPrompt}:1: no \`| fileScopeMustNotChange | ... |\` row — ` +
      `the shared chokepoints (${FLOOR_PIN_PATH}, ${TASK_BOARD_PATH}) are then declared by nobody`
    );
  } else {
    // check 4: the shared floor pin
    if (!floorPin.covered) {
      violations.push(
        `[check 4] ${relPrompt}:${floorPin.rowLine}: fileScopeMustNotChange does not cover ${FLOOR_PIN_PATH} — ` +
        "the shared floor pin is the guaranteed cross-lane collision (SP-072 amendment: every packet that adds a test bumps it in " +
        `the same commit, so any lane-mate collides there — green alone, RED at merge). Declare the delta in ${`spine-tasks/${name}/floor-delta.json`} instead. ` +
        `Declared: ${floorPin.declared.length > 0 ? floorPin.declared.join(", ") : "(nothing)"}`
      );
    }
    // check 5: the task board, only when there IS a lane-mate
    if (waveSize > 1 && !taskBoard.covered) {
      violations.push(
        `[check 5] ${relPrompt}:${taskBoard.rowLine}: fileScopeMustNotChange does not cover ${TASK_BOARD_PATH}, and this wave runs ${waveSize} packets — ` +
        "the board is a shared chokepoint reconciled by the orchestrator at land; two lanes editing it in one wave is the defect. " +
        `Declared: ${taskBoard.declared.length > 0 ? taskBoard.declared.join(", ") : "(nothing)"}`
      );
    }
  }

  const record = {
    dir: name,
    number,
    promptPath: promptPath.replace(/\\/g, "/"),
    testCommandRows,
    floorDeltaRows: floorDeltaRows.map((r) => ({ line: r.line, values: r.values })),
    mustNotChangeRows: mustNotChangeRows.map((r) => ({ line: r.line, values: r.values })),
    floorPin,
    taskBoard,
    violations,
  };

  return { violations, packet: { name, number, mustChange, deltaPath }, record };
}

// ---------------------------------------------------------------------------
// The read-only projection the C# floor guard consumes
// ---------------------------------------------------------------------------
//
// Every packet-root PROMPT.md under the named directory, with the verdicts above. Per-packet
// violations are DATA, not an exit code: the consumer binds a different population than this
// script does (SP-073 upward, versus everything) and must re-apply its own rule to them.
export function emitPacketScopes(spineTasksDir) {
  const dirs = enumeratePromptDirs(spineTasksDir);
  const packets = [];
  for (const dir of dirs) {
    if (!PACKET_NUMBER.test(dir)) {
      continue; // reported separately as strayPromptDirs; it has no packet number to bind
    }
    const { record } = inspectPacket(dir, spineTasksDir, 1);
    if (record) {
      packets.push(record);
    }
  }
  return {
    schema: "ccp.wave.packet-scopes.v1",
    spineTasksDir: spineTasksDir.replace(/\\/g, "/"),
    waveSize: 1,
    floorPinPath: FLOOR_PIN_PATH,
    taskBoardPath: TASK_BOARD_PATH,
    wrapperToken: WRAPPER_TOKEN,
    firstBoundPacketNumber: FIRST_BOUND_PACKET_NUMBER,
    strayPromptDirs: dirs.filter((d) => !PACKET_NUMBER.test(d)),
    cases: evaluateCoverageCases(),
    wrapperCases: evaluateWrapperCases(),
    packets,
  };
}

// ---------------------------------------------------------------------------
// The wave check
// ---------------------------------------------------------------------------

export function validateWave(packetNames, spineTasksDir) {
  const violations = [];

  // --- check 7: no packet named twice ------------------------------------------------
  // Runs FIRST and on the NORMALIZED names, so `SP-073-a`, `spine-tasks/SP-073-a` and
  // `SP-073-a/` are caught as the same lane rather than launched as two.
  const seen = new Map();
  for (const name of packetNames) {
    seen.set(name, (seen.get(name) ?? 0) + 1);
  }
  for (const [name, count] of seen) {
    if (count > 1) {
      violations.push(
        `[check 7] wave: packet ${name} appears ${count} times in the argument list — ` +
        "two lanes on one packet share a worktree scope by definition; name it once"
      );
    }
  }
  const unique = [...seen.keys()];

  // --- refusal to go blind on the on-disk packet set ----------------------------------
  const strayPromptDirs = enumeratePromptDirs(spineTasksDir).filter((d) => !PACKET_NUMBER.test(d));
  for (const dir of strayPromptDirs) {
    violations.push(
      `[check 1] spine-tasks/${dir}/PROMPT.md:1: packet directory '${dir}' does not parse as SP-<number>-... — ` +
      "the wave guard refuses to go blind on an unparseable packet ID (same rule as FloorWrapperGuardTests)"
    );
  }
  const onDisk = enumerateOnDiskPackets(spineTasksDir);

  // --- per-packet checks 1-5, ONE implementation, shared with the projection --------------
  const packets = [];
  for (const name of unique) {
    const result = inspectPacket(name, spineTasksDir, unique.length);
    violations.push(...result.violations);
    if (result.packet) {
      packets.push(result.packet);
    }
  }

  // --- check 6: File Scope disjointness, every offending PAIR named --------------------
  for (let i = 0; i < packets.length; i++) {
    for (let j = i + 1; j < packets.length; j++) {
      for (const patternA of packets[i].mustChange) {
        for (const patternB of packets[j].mustChange) {
          if (patternsIntersect(patternA, patternB)) {
            violations.push(
              `[check 6] FILE SCOPE OVERLAP: ${packets[i].name} claims \`${patternA}\` and ${packets[j].name} claims \`${patternB}\` — ` +
              "both lanes would write the same path in separate worktrees; each is green alone and the wave is RED at merge"
            );
          }
        }
      }
    }
  }

  // --- check 8: task ID reuse ----------------------------------------------------------
  const byNumber = new Map();
  for (const packet of packets) {
    if (!byNumber.has(packet.number)) {
      byNumber.set(packet.number, []);
    }
    byNumber.get(packet.number).push(packet.name);
  }
  for (const [number, names] of byNumber) {
    if (names.length > 1) {
      violations.push(
        `[check 8] TASK ID REUSE: SP-${number} is claimed by ${names.length} packets in this wave (${names.join(", ")}) — ` +
        "one ID, one history; a reissued ID silently overwrites the other's record"
      );
    }
  }
  // The wave's own directories are excluded from the "already on disk" set: they exist by
  // check 1, so including them would make every packet fail against its lane-mate.
  const waveDirs = new Set(packets.map((p) => p.name));
  const others = onDisk.filter((p) => !waveDirs.has(p.dir));
  if (others.length > 0) {
    const highest = others.reduce((best, p) => (p.number > best.number ? p : best), others[0]);
    for (const packet of packets) {
      if (packet.number <= highest.number) {
        violations.push(
          `[check 8] TASK ID REUSE: ${packet.name} is SP-${packet.number}, at or below the highest ID already in spine-tasks/ ` +
          `(${highest.dir} = SP-${highest.number}) — a new packet must take the next unused number; a reissued ID silently overwrites history`
        );
      }
    }
  }

  // --- declared floor delta (summary input, and fail-closed when the file is malformed) -
  // Check 3 binds the ROW; the FILE is normally authored by the lane, so an absent
  // floor-delta.json is not a violation here. One that exists but cannot be read is: a
  // delta the orchestrator cannot total is a delta it will get wrong at land.
  let totalUnitDelta = 0;
  let totalHeadlessDelta = 0;
  let deltasPresent = 0;
  for (const packet of packets) {
    if (!packet.deltaPath) {
      continue;
    }
    const result = readFloorDelta(packet.deltaPath);
    if (result.error) {
      violations.push(`[check 3] ${packet.name}: spine-tasks/${packet.name}/floor-delta.json ${result.error} — a delta that cannot be totalled cannot be reconciled at land`);
    } else if (result.present) {
      deltasPresent += 1;
      totalUnitDelta += result.unit;
      totalHeadlessDelta += result.headless;
    }
  }

  return { violations, packets, totalUnitDelta, totalHeadlessDelta, deltasPresent };
}

export function main(argv) {
  // The projection mode. It takes an EXPLICIT directory, never guesses a tree, and exits 0 for
  // any corpus it could read — violations travel as data. Exit 2 means "no projection", which
  // the consumer must treat as a hard failure and never as an empty corpus.
  if (argv[0] === "--emit-packet-scopes") {
    const dir = argv[1];
    if (!dir) {
      console.error(
        "WAVE CHECK FAILED:\n  --emit-packet-scopes requires an explicit spine-tasks directory " +
        "(usage: node client/tools/wave/validate-wave.mjs --emit-packet-scopes <spineTasksDir>) — " +
        "it refuses to guess a tree so that a copy of this file can be pointed at a fixture"
      );
      return 2;
    }
    if (!fs.existsSync(dir) || !fs.statSync(dir).isDirectory()) {
      console.error(`WAVE CHECK FAILED:\n  --emit-packet-scopes: '${dir}' is not a directory — the projection refuses to report an empty corpus it never read`);
      return 2;
    }
    process.stdout.write(`${JSON.stringify(emitPacketScopes(dir), null, 2)}\n`);
    return 0;
  }

  const packetNames = argv
    .map((a) => a.replace(/\\/g, "/").replace(/\/+$/, "").replace(/^spine-tasks\//, ""))
    .filter((a) => a.length > 0);

  // The shared decision is checked against its own fixture BEFORE any packet is judged. If this
  // file disagrees with the cases it pins, every verdict below it — and every verdict the C#
  // guard consumes from it — is suspect, so no wave may launch on it.
  const fixtureMismatches = [...coverageCaseMismatches(), ...wrapperCaseMismatches()];
  if (fixtureMismatches.length > 0) {
    console.error(`WAVE CHECK FAILED: a shared decision disagrees with its own fixture (${fixtureMismatches.length} case(s))`);
    for (const m of fixtureMismatches) {
      console.error(`  ${m}`);
    }
    console.error("  NO LANE MAY LAUNCH: this is the SP-136 convergence failing at its own gate, not a packet defect.");
    return 1;
  }

  try {
    if (packetNames.length === 0) {
      fail("no packets named.\n  usage: node client/tools/wave/validate-wave.mjs SP-073-slug SP-074-slug [...]\n     or: node client/tools/wave/validate-wave.mjs --emit-packet-scopes <spineTasksDir>");
    }
    if (!fs.existsSync(REPO_ANCHOR)) {
      fail(`repo anchor missing at ${REPO_ANCHOR} — this script must run from client/tools/wave/ inside the repo; it refuses to guess a tree`);
    }
    if (!fs.existsSync(SPINE_TASKS)) {
      fail(`spine-tasks not found at ${SPINE_TASKS} — the wave guard refuses to skip`);
    }
  } catch (err) {
    if (err instanceof WaveError) {
      console.error(`WAVE CHECK FAILED:\n  ${err.message}`);
      return 1;
    }
    throw err;
  }

  const { violations, packets, totalUnitDelta, totalHeadlessDelta, deltasPresent } = validateWave(packetNames, SPINE_TASKS);

  if (violations.length > 0) {
    console.error(`WAVE CHECK FAILED: ${violations.length} violation(s) across ${packetNames.length} named packet(s)`);
    for (const v of violations) {
      console.error(`  ${v}`);
    }
    console.error("  NO LANE MAY LAUNCH until every violation above is fixed in the packet(s), not in this script.");
    return 1;
  }

  const deltaNote = deltasPresent === packets.length
    ? `${deltasPresent}/${packets.length} floor-delta.json read`
    : `${deltasPresent}/${packets.length} floor-delta.json present — the rest are authored by their lanes`;
  const sign = (n) => `${n >= 0 ? "+" : ""}${n}`;
  console.log(
    `WAVE OK: ${packets.length} packet(s) [${packets.map((p) => p.name).join(", ")}]; ` +
    `scopes disjoint; declared floor delta unit ${sign(totalUnitDelta)}, headless ${sign(totalHeadlessDelta)} (${deltaNote})`
  );
  return 0;
}

if (process.argv[1] && import.meta.url === pathToFileURL(path.resolve(process.argv[1])).href) {
  process.exit(main(process.argv.slice(2)));
}
