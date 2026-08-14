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
// MIRROR note (do not let these two drift): packet enumeration and the testCommand row
// regex are ports of client/tests/CcpClient.Tests/FloorWrapperGuardTests.cs — packets are
// spine-tasks/<packet>/PROMPT.md at EXACTLY one level, the directory must parse as
// SP-<number>-, and the row is matched with the same expression. The grandfather rule is
// the same one and the ONLY one: packets below SP-065 are not bound by the wrapper check,
// by explicit ID and never by a suppression list. It is inherited here, not invented here,
// and it is irrelevant in practice because a wave authors NEW packets.
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
// The wave check
// ---------------------------------------------------------------------------

export function validateWave(packetNames, spineTasksDir) {
  const violations = [];
  const relPrompt = (name) => `spine-tasks/${name}/PROMPT.md`;

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

  // --- per-packet checks 1-5 ----------------------------------------------------------
  const packets = [];
  for (const name of unique) {
    const dir = path.join(spineTasksDir, name);
    const promptPath = path.join(dir, "PROMPT.md");

    // check 1
    if (!fs.existsSync(dir) || !fs.statSync(dir).isDirectory()) {
      violations.push(`[check 1] ${name}: no such packet directory at spine-tasks/${name} — a wave cannot launch a lane on a packet that is not authored`);
      continue;
    }
    const numberMatch = PACKET_NUMBER.exec(name);
    if (!numberMatch) {
      violations.push(`[check 1] ${name}: packet directory name does not parse as SP-<number>-... — every ID rule below would silently pass`);
      continue;
    }
    const number = Number.parseInt(numberMatch[1], 10);
    if (!fs.existsSync(promptPath)) {
      violations.push(`[check 2] ${name}: no PROMPT.md at ${relPrompt(name)} — nothing to validate; the lane would launch uncontracted`);
      packets.push({ name, number, mustChange: [] });
      continue;
    }
    const lines = fs.readFileSync(promptPath, "utf8").split(/\r?\n/);

    // check 2: testCommand row + the SP-065 floor-wrapper routing rule
    const testCommandRows = [];
    for (let i = 0; i < lines.length; i++) {
      const hit = TEST_COMMAND_ROW.exec(lines[i]);
      if (hit) {
        testCommandRows.push({ line: i + 1, command: hit[1] });
      }
    }
    if (testCommandRows.length === 0) {
      violations.push(
        `[check 2] ${relPrompt(name)}:1: no parseable \`| testCommand | \`...\` |\` row — ` +
        "the wave guard refuses to go blind on a packet it is about to launch (SP-065)"
      );
    } else if (testCommandRows.length > 1) {
      violations.push(
        `[check 2] ${relPrompt(name)}:${testCommandRows.map((r) => r.line).join(",")}: ${testCommandRows.length} testCommand rows — ` +
        "an ambiguous contract cannot be gated; leave exactly one"
      );
    }
    for (const row of testCommandRows) {
      if (number >= FIRST_BOUND_PACKET_NUMBER && DOTNET_TEST.test(row.command) && !row.command.includes(WRAPPER_TOKEN)) {
        violations.push(
          `[check 2] ${relPrompt(name)}:${row.line}: testCommand invokes \`dotnet test\` without routing through ${WRAPPER_TOKEN} — ` +
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
        `[check 3] ${relPrompt(name)}:1: no \`| floorDelta | \`${expectedDelta}\` |\` row — ` +
        "a lane with no declared delta file leaves the orchestrator reconciling the shared floor pin by guesswork at land"
      );
    } else if (floorDeltaRows.length > 1) {
      violations.push(
        `[check 3] ${relPrompt(name)}:${floorDeltaRows.map((r) => r.line).join(",")}: ${floorDeltaRows.length} floorDelta rows — ` +
        "an ambiguous delta declaration cannot be reconciled; leave exactly one"
      );
    } else {
      const row = floorDeltaRows[0];
      if (row.values.length !== 1) {
        violations.push(
          `[check 3] ${relPrompt(name)}:${row.line}: floorDelta cell must carry exactly one backtick-quoted path, found ${row.values.length} — ` +
          `expected \`${expectedDelta}\``
        );
      } else {
        const declared = row.values[0].replace(/\\/g, "/").replace(/^\.\//, "");
        if (declared !== expectedDelta) {
          violations.push(
            `[check 3] ${relPrompt(name)}:${row.line}: floorDelta declares '${declared}' but this packet is '${name}' — ` +
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
    const mustNotChange = mustNotChangeRows.flatMap((r) => r.values);
    if (mustChangeRows.length === 0) {
      violations.push(
        `[check 6] ${relPrompt(name)}:1: no \`| fileScopeMustChange | ... |\` row — ` +
        "disjointness cannot be computed for a lane that declares no scope, and an unknown scope overlaps everything"
      );
    } else if (mustChange.length === 0) {
      violations.push(`[check 6] ${relPrompt(name)}:${mustChangeRows[0].line}: fileScopeMustChange carries no backtick-quoted path`);
    }
    if (mustNotChangeRows.length === 0) {
      violations.push(
        `[check 4] ${relPrompt(name)}:1: no \`| fileScopeMustNotChange | ... |\` row — ` +
        `the shared chokepoints (${FLOOR_PIN_PATH}, ${TASK_BOARD_PATH}) are then declared by nobody`
      );
    } else {
      // check 4: the shared floor pin
      if (!mustNotChange.some((p) => patternCovers(p, FLOOR_PIN_PATH))) {
        violations.push(
          `[check 4] ${relPrompt(name)}:${mustNotChangeRows[0].line}: fileScopeMustNotChange does not cover ${FLOOR_PIN_PATH} — ` +
          "the shared floor pin is the guaranteed cross-lane collision (SP-072 amendment: every packet that adds a test bumps it in " +
          `the same commit, so any lane-mate collides there — green alone, RED at merge). Declare the delta in ${`spine-tasks/${name}/floor-delta.json`} instead. ` +
          `Declared: ${mustNotChange.length > 0 ? mustNotChange.join(", ") : "(nothing)"}`
        );
      }
      // check 5: the task board, only when there IS a lane-mate
      if (unique.length > 1 && !mustNotChange.some((p) => patternCovers(p, TASK_BOARD_PATH))) {
        violations.push(
          `[check 5] ${relPrompt(name)}:${mustNotChangeRows[0].line}: fileScopeMustNotChange does not cover ${TASK_BOARD_PATH}, and this wave runs ${unique.length} packets — ` +
          "the board is a shared chokepoint reconciled by the orchestrator at land; two lanes editing it in one wave is the defect. " +
          `Declared: ${mustNotChange.length > 0 ? mustNotChange.join(", ") : "(nothing)"}`
        );
      }
    }

    packets.push({ name, number, mustChange, deltaPath });
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
  const packetNames = argv
    .map((a) => a.replace(/\\/g, "/").replace(/\/+$/, "").replace(/^spine-tasks\//, ""))
    .filter((a) => a.length > 0);

  try {
    if (packetNames.length === 0) {
      fail("no packets named.\n  usage: node client/tools/wave/validate-wave.mjs SP-073-slug SP-074-slug [...]");
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
