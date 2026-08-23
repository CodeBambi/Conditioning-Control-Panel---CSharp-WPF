#!/usr/bin/env node
// Board row 49 part 2: mechanical skip/count floor for the client test suite.
// Board row 49 part 1, framing e: the pin is NAME-ANCHORED — per project
// { total, allowedSkips[] }; expected skips are pinned by fully-qualified test NAME,
// never by count (closes the earlier counts-not-identity limit; machine-portable).
// Runs BOTH test projects — as xunit v3 ASSEMBLIES with `-trx`, not through `dotnet test`;
// see runProject for the measurement that forced that choice — writing into a per-run temp
// dir OUTSIDE the worktree, then post-processes the results against the exact per-project
// pin (floor.json).
//
// Green requires ALL THREE: runner exit 0, every fail-closed TRX check, and the pin:
// zero bad outcomes, passed + skipped == total, and every NotExecuted result's testName
// present in allowedSkips (offender NAMED in the failure). May-skip semantics: a listed
// test that runs and passes is green.
// Exit 0 = floor met. Exit 1 = any violation (each prints a loud named reason).
//
// Never sets CCP_DATA_ROOT (port-workflow.md:103 — a process-wide override makes the
// data-root pin skip and the floor goes blind). Never writes inside the worktree: the TRX
// path targets os.tmpdir(); *.trx / TestResults/ are gitignored and the merge-time dirty
// check tolerates nothing.

import { execFileSync } from "node:child_process";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";

const HERE = path.dirname(fileURLToPath(import.meta.url));
const CLIENT = path.resolve(HERE, "..", "..");
const PIN_PATH = path.join(HERE, "floor.json");
const SLN_PATH = path.join(CLIENT, "CcpClient.sln");

const STALE_SKEW_MS = 15_000;
// Counter categories that must be zero for a run to mean anything (framing b/g).
const BAD_COUNTERS = ["failed", "error", "timeout", "aborted", "notRunnable", "inconclusive", "passedButRunAborted"];

export class FloorError extends Error {}

function fail(message) {
  throw new FloorError(message);
}

function readPin() {
  let raw;
  try {
    raw = fs.readFileSync(PIN_PATH, "utf8");
  } catch {
    fail(`pin file missing at ${PIN_PATH} — the floor refuses to run unpinned`);
  }
  let pin;
  try {
    pin = JSON.parse(raw);
  } catch (err) {
    fail(`pin file ${PIN_PATH} is not parseable JSON: ${err.message}`);
  }
  if (!pin || typeof pin !== "object" || !pin.projects || typeof pin.projects !== "object") {
    fail(`pin file ${PIN_PATH} has no "projects" object`);
  }
  for (const [name, entry] of Object.entries(pin.projects)) {
    if (!entry || !Number.isInteger(entry.total) || !Array.isArray(entry.allowedSkips) ||
        !entry.allowedSkips.every((s) => typeof s === "string" && s.length > 0)) {
      fail(`pin file ${PIN_PATH} has no {total: int, allowedSkips: string[]} entry for ${name}`);
    }
  }
  return pin;
}

// The testName match is exact on the pre-'(' portion (xunit theory rows serialize
// with arguments, e.g. Class.Method(x: 1)); a name with no parens matches whole.
function skipNameAllowed(testName, allowedSkips) {
  if (allowedSkips.includes(testName)) {
    return true;
  }
  const base = testName.split("(")[0];
  return base !== testName && allowedSkips.includes(base);
}

// Test-project DISCOVERY from the solution (pre-completion consult hole: a hardcoded list
// lets a NEW test project join the sln and silently sit outside the floor). Every csproj
// under the sln's tests/ folder must be pinned, and every pin must exist in the sln —
// both directions fail closed. Exported for the fail-closed demonstration harness.
export function discoverTestProjects(slnText, pin) {
  const discovered = new Map(); // project name -> sln-relative csproj path
  for (const m of slnText.matchAll(/Project\("[^"]*"\)\s*=\s*"[^"]*"\s*,\s*"([^"]+\.csproj)"/g)) {
    const rel = m[1].replace(/\\/g, "/");
    if (!rel.startsWith("tests/")) {
      continue;
    }
    const name = path.posix.basename(rel, ".csproj");
    discovered.set(name, rel);
  }
  if (discovered.size === 0) {
    fail("no test projects discovered under tests/ in client/CcpClient.sln — the floor refuses to go blind");
  }
  const pinned = new Set(Object.keys(pin.projects));
  const unpinned = [...discovered.keys()].filter((n) => !pinned.has(n));
  if (unpinned.length > 0) {
    fail(`test project(s) in client/CcpClient.sln with NO floor pin: ${unpinned.join(", ")} — ` +
      "a suite outside the floor is exactly what this floor exists to prevent; pin it in client/tests/floor/floor.json");
  }
  const missing = [...pinned].filter((n) => !discovered.has(n));
  if (missing.length > 0) {
    fail(`pinned project(s) absent from client/CcpClient.sln tests/: ${missing.join(", ")} — stale pin`);
  }
  return [...discovered.entries()].map(([name, rel]) => ({
    name,
    csproj: path.join(CLIENT, ...rel.split("/")),
  }));
}

// Exported for the fail-closed demonstration harness; the contract path calls this with
// the real per-run results directory.
export function verifyProjectResults(projectName, resultsDir, pinEntry, runStartMs) {
  if (!fs.existsSync(resultsDir)) {
    fail(`${projectName}: results directory missing (${resultsDir}) — the project produced no results at all`);
  }
  const trxFiles = fs.readdirSync(resultsDir).filter((f) => f.toLowerCase().endsWith(".trx"));
  if (trxFiles.length === 0) {
    fail(`${projectName}: no .trx in ${resultsDir} — the project produced no results at all`);
  }
  if (trxFiles.length > 1) {
    fail(`${projectName}: ${trxFiles.length} .trx files in ${resultsDir}, expected exactly 1 (${trxFiles.join(", ")})`);
  }
  const trxPath = path.join(resultsDir, trxFiles[0]);

  // Staleness: mtime AND the runner-stamped Times/@creation must both be inside the run
  // window. mtime alone can be preserved by copy tools; creation alone trusts only XML.
  const mtime = fs.statSync(trxPath).mtimeMs;
  if (mtime < runStartMs - STALE_SKEW_MS) {
    fail(`${projectName}: ${trxPath} mtime ${new Date(mtime).toISOString()} predates this run — stale results from a previous run`);
  }

  const xml = fs.readFileSync(trxPath, "utf8");
  // Unparseable / truncated detection without a parser dependency: the envelope must be
  // complete and the summary must be singular and well-formed.
  // The head check exists to catch GARBAGE and a run that died before writing anything at all;
  // the truncation half is carried by the </TestRun> check below, which is the one that actually
  // detects a run dying mid-write. AN XML DECLARATION IS OPTIONAL in XML 1.0, and the xunit v3 TRX
  // writer omits it - its documents open straight on the root element, where VSTest's opened on
  // "<?xml". Requiring the declaration therefore rejected a perfectly well-formed result file for a
  // property this check was never about. Both openings are accepted, with or without a BOM;
  // anything else is still refused, and nothing else here is relaxed.
  const head = xml.startsWith("\uFEFF") ? xml.slice(1) : xml;
  if (!head.startsWith("<?xml") && !head.startsWith("<TestRun")) {
    fail(`${projectName}: ${trxPath} starts with neither an XML declaration nor <TestRun — unparseable results`);
  }
  if (!xml.trimEnd().endsWith("</TestRun>")) {
    fail(`${projectName}: ${trxPath} does not end with </TestRun> — truncated results (run died mid-write)`);
  }
  const count = (re) => (xml.match(re) ?? []).length;
  if (count(/<ResultSummary\s/g) !== 1 || count(/<Counters\s/g) !== 1) {
    fail(`${projectName}: ${trxPath} must contain exactly one <ResultSummary> and one <Counters> — unparseable results`);
  }
  const outcome = xml.match(/<ResultSummary\s+outcome="([^"]*)"/)?.[1];
  if (outcome !== "Completed") {
    fail(`${projectName}: ResultSummary outcome is "${outcome ?? "missing"}", expected "Completed" — the run did not finish cleanly`);
  }
  const creation = xml.match(/<Times\s+[^>]*creation="([^"]+)"/)?.[1];
  if (!creation || Number.isNaN(Date.parse(creation))) {
    fail(`${projectName}: ${trxPath} has no parseable Times/@creation — unparseable results`);
  }
  if (Date.parse(creation) < runStartMs - STALE_SKEW_MS) {
    fail(`${projectName}: ${trxPath} Times/@creation ${creation} predates this run — stale results from a previous run`);
  }

  const countersTag = xml.match(/<Counters\s+[^>]*\/>/)?.[0];
  if (!countersTag) {
    fail(`${projectName}: ${trxPath} <Counters> element is not a self-closing tag — unparseable results`);
  }
  const counters = {};
  for (const m of countersTag.matchAll(/(\w+)="(-?\d+)"/g)) {
    counters[m[1]] = Number(m[2]);
  }
  const REQUIRED = ["total", "executed", "passed", "notExecuted", ...BAD_COUNTERS];
  for (const key of REQUIRED) {
    if (!Number.isInteger(counters[key]) || counters[key] < 0) {
      fail(`${projectName}: Counters/@${key} missing or not a non-negative integer — unparseable results`);
    }
  }
  if (counters.total === 0) {
    fail(`${projectName}: 0 total results — "no tests ran" must never read as success (filter-excluded / never-ran shape)`);
  }
  for (const key of BAD_COUNTERS) {
    if (counters[key] !== 0) {
      fail(`${projectName}: ${counters[key]} ${key} result(s) — a dirty run is not a floor run`);
    }
  }

  // Per-test outcomes from the RESULT LIST are authoritative. Empirically on this stack
  // (xunit.runner.visualstudio 3.1.5, probe ccp-floor-5E7zGu): a dynamically-skipped test
  // (Assert.SkipWhen) yields a UnitTestResult with outcome="NotExecuted" while
  // Counters/@notExecuted stays 0 and @executed excludes it — the Counters arithmetic does
  // NOT close over skips (executed + notExecuted = 897 != total 898 with one skip). So the
  // skip count, the skip NAMES (the name-anchored pin), and all consistency checks
  // anchor on the result list, never on Counters arithmetic.
  const outcomes = {};
  const skippedNames = [];
  for (const m of xml.matchAll(/<UnitTestResult\s[^>]*?\/?>/g)) {
    const tag = m[0];
    const outcome = tag.match(/\boutcome="([^"]*)"/)?.[1];
    const testName = tag.match(/\btestName="([^"]*)"/)?.[1];
    if (!outcome || !testName) {
      fail(`${projectName}: a <UnitTestResult> lacks outcome or testName — unparseable results`);
    }
    outcomes[outcome] = (outcomes[outcome] ?? 0) + 1;
    if (outcome === "NotExecuted") {
      skippedNames.push(testName);
    }
  }
  const resultCount = Object.values(outcomes).reduce((a, b) => a + b, 0);
  if (resultCount === 0) {
    fail(`${projectName}: 0 <UnitTestResult> entries — "no tests ran" must never read as success`);
  }
  if (resultCount !== counters.total) {
    fail(`${projectName}: result list has ${resultCount} entries but Counters/@total is ${counters.total} — inconsistent results`);
  }
  const BAD_OUTCOMES = ["Failed", "Error", "Timeout", "Aborted", "NotRunnable", "Inconclusive", "PassedButRunAborted", "Disconnected"];
  for (const key of BAD_OUTCOMES) {
    if (outcomes[key]) {
      fail(`${projectName}: ${outcomes[key]} ${key} outcome(s) in the result list — a dirty run is not a floor run`);
    }
  }
  const passed = outcomes.Passed ?? 0;
  const skipped = outcomes.NotExecuted ?? 0;
  if (passed !== counters.passed) {
    fail(`${projectName}: result list Passed(${passed}) != Counters/@passed(${counters.passed}) — inconsistent results`);
  }
  if (passed + skipped !== resultCount) {
    const exotic = Object.entries(outcomes).filter(([k]) => k !== "Passed" && k !== "NotExecuted").map(([k, v]) => `${k}=${v}`).join(", ");
    fail(`${projectName}: Passed(${passed}) + NotExecuted(${skipped}) != ${resultCount} results — unexpected outcome(s): ${exotic || "none named"}`);
  }
  if (resultCount !== pinEntry.total) {
    fail(
      `${projectName}: FLOOR VIOLATION — total drift: ${resultCount} result(s) (pin total ${pinEntry.total}). ` +
      `A count drift in EITHER direction fails the contract. If this change ` +
      `is intentional, bump client/tests/floor/floor.json in the SAME commit and state the ` +
      `reason in the message.`
    );
  }
  for (const name of skippedNames) {
    if (!skipNameAllowed(name, pinEntry.allowedSkips)) {
      fail(
        `${projectName}: FLOOR VIOLATION — unexpected skip: ${name} is NotExecuted but NOT in ` +
        `allowedSkips. An unexpected skip fails the contract (framing e/f). Either fix ` +
        `the precondition so the test runs, or — only under the admission rule in floor.json ` +
        `(machine/OS property, never a quarantine; the data-root pin and the named privacy flake ` +
        `are permanently banned) — pin the name in allowedSkips in the SAME commit, naming the ` +
        `machine class where it executes.`
      );
    }
  }
  return { passed: counters.passed, skipped, skippedNames, total: counters.total, trxPath };
}

// THE GATE MUST NOT MEASURE A STALE BUILD (added 2026-08-18 after it did, three times).
// runProject uses --no-build for speed, which is right: the caller is expected to have built.
// But when it has not -- most often right after a merge, when only some projects were rebuilt --
// dotnet happily runs YESTERDAY's assemblies and this gate reports yesterday's counts as though
// they were today's. That reads exactly like a regression the wave caused, and the operator then
// hunts a defect that does not exist. It cost real time at two lands and once more the same day.
//
// So: before running anything, compare the newest source file against the assembly under test.
// If a source is newer, FAIL LOUDLY and name the file, rather than producing a confident number
// about the wrong binaries. A gate that reports the wrong answer is worse than one that refuses.
function newestSourceMs(dir) {
  let newest = 0;
  const walk = (d) => {
    let entries;
    try { entries = fs.readdirSync(d, { withFileTypes: true }); } catch { return; }
    for (const e of entries) {
      if (e.name === "bin" || e.name === "obj" || e.name === ".git") continue;
      const full = path.join(d, e.name);
      if (e.isDirectory()) { walk(full); continue; }
      if (!/\.(cs|axaml|xaml|csproj|json)$/i.test(e.name)) continue;
      const m = fs.statSync(full).mtimeMs;
      if (m > newest) newest = m;
    }
  };
  walk(dir);
  return newest;
}

function assertBuildIsFresh(project) {
  const projDir = path.dirname(project.csproj);
  const dll = path.join(projDir, "bin", "Debug", "net10.0", `${project.name}.dll`);
  if (!fs.existsSync(dll)) {
    fail(`${project.name}: no built assembly at ${dll} — build the solution before running the floor`);
  }
  const dllMs = fs.statSync(dll).mtimeMs;
  const srcRoots = [projDir, path.join(CLIENT, "src")];
  for (const root of srcRoots) {
    const newest = newestSourceMs(root);
    if (newest > dllMs) {
      fail(`${project.name}: STALE BUILD — source under ${root} is newer than ${path.basename(dll)}. ` +
        `The gate runs --no-build, so it would have measured the PREVIOUS build's counts and reported them ` +
        `as this tree's. Run: dotnet build client/CcpClient.sln -c Debug --nologo`);
    }
  }
}

// NAME the failures on red, from the TRX rather than from a six-line stdout tail.
// The tail is where a red goes to die: an earlier investigation's run 7 had SIX failing tests and the tail could
// only carry a couple of lines of them, so the flake had to be reconstructed by hand from the
// preserved results directory. A gate that says WHICH fact failed and WHY is the difference
// between "the floor is flaky" and "two runs contended for the same point on the desktop".
//
// THIS IS THE ONLY THING THIS FUNCTION MAY EVER DO. It reports; it never re-runs anything, and
// check-floor.mjs must never gain a retry: re-running until green is how an intermittent gets
// laundered into a pass, which is the exact failure this reporting exists to end.
function namedFailures(resultsDir) {
  let xml;
  try {
    const trx = fs.readdirSync(resultsDir).filter((f) => f.toLowerCase().endsWith(".trx"));
    if (trx.length === 0) return [];
    xml = fs.readFileSync(path.join(resultsDir, trx[0]), "utf8");
  } catch {
    return []; // no TRX to read is itself reported by the checks above
  }
  const unescape = (s) => s
    .replace(/&#xD;/g, "").replace(/&#xA;/g, " ")
    .replace(/&lt;/g, "<").replace(/&gt;/g, ">").replace(/&quot;/g, '"').replace(/&amp;/g, "&");
  const rows = [];
  for (const m of xml.matchAll(/<UnitTestResult\b[^>]*?outcome="(Failed|Error|Timeout|Aborted)"[\s\S]*?<\/UnitTestResult>/g)) {
    const name = m[0].match(/testName="([^"]*)"/)?.[1] ?? "(unnamed)";
    const message = unescape(m[0].match(/<Message>([\s\S]*?)<\/Message>/)?.[1] ?? "").trim();
    rows.push(`${name}\n      ${message.slice(0, 600)}`);
  }
  return rows;
}

// THE RUNNER IS PART OF THE MEASUREMENT, and choosing the wrong one cost this board a long-lived
// row (2026-08-23). This gate used to shell out to `dotnet test`, i.e. VSTest hosting the xunit v3
// adapter. Measured back to back on ONE build, interleaved A-B-A to rule out drift:
//
//     dotnet test (VSTest)            -> 4 failed / 2643 passed
//     the xunit v3 assembly directly  -> 0 failed / 2665 total
//     dotnet test (VSTest)            -> 4 failed / 2643 passed
//
// The four are exactly the real-desktop Win32 band facts (PointerCoexistenceTests and the tray
// band regression). They lose WS_EX_TOPMOST only under the VSTest test host, which is a property
// of that host's process, not of the product: no product change can close it, and for a long time
// the board carried it as "the floor is not reliably green on this machine" as though it were one.
//
// xunit v3 test projects ARE executables and that is their first-class run mode; `-trx` emits the
// same format this file already parses. Its Counters are in fact stricter than VSTest's: they
// CLOSE OVER SKIPS (passed 2663 + notExecuted 2 == total 2665), where the VSTest arithmetic did
// not, which is why the result-list anchoring below was needed in the first place. That anchoring
// stays exactly as it is — it is correct for both, and it is not being relaxed on the strength of
// a nicer runner.
//
// This does NOT retry, soften, or filter anything. It runs every fact the assembly has and holds
// them to the same pins. A red here is still a red.
function testAssemblyPath(project) {
  const projDir = path.dirname(project.csproj);
  const base = path.join(projDir, "bin", "Debug", "net10.0", project.name);
  // The apphost carries .exe on Windows and no extension elsewhere; Linux is a supported target
  // for this suite's non-UI legs, so the gate must find it on both rather than assume Windows.
  const withExe = `${base}.exe`;
  if (fs.existsSync(withExe)) return withExe;
  if (fs.existsSync(base)) return base;
  fail(`${project.name}: no test assembly executable at ${withExe} or ${base} — ` +
    "the floor runs the xunit v3 assembly directly; build the solution before running it");
  return null; // unreachable; fail() throws
}

function runProject(project, runDir) {
  assertBuildIsFresh(project);
  const resultsDir = path.join(runDir, project.name);
  fs.mkdirSync(resultsDir, { recursive: true });
  const exe = testAssemblyPath(project);
  let exitCode = 0;
  let output = "";
  try {
    output = execFileSync(
      exe,
      ["-trx", path.join(resultsDir, "results.trx")],
      { encoding: "utf8", stdio: ["ignore", "pipe", "pipe"] }
    );
  } catch (err) {
    exitCode = err.status ?? 1;
    output = `${err.stdout ?? ""}${err.stderr ?? ""}`;
  }
  return { exitCode, output, resultsDir };
}

export function main() {
  const runStartMs = Date.now();
  const pin = readPin();
  let projects;
  try {
    projects = discoverTestProjects(fs.readFileSync(SLN_PATH, "utf8"), pin);
  } catch (err) {
    if (err instanceof FloorError) {
      console.error(`FLOOR CHECK FAILED:\n  ${err.message}`);
      return 1;
    }
    throw err;
  }
  const runDir = fs.mkdtempSync(path.join(os.tmpdir(), "ccp-floor-"));
  console.log(`floor run results directory: ${runDir}`);

  const failures = [];
  const summaries = [];
  for (const project of projects) {
    let exitCode = 1;
    let output = "";
    let resultsDir = path.join(runDir, project.name);
    try {
      ({ exitCode, output, resultsDir } = runProject(project, runDir));
      if (exitCode !== 0) {
        fail(`the test assembly exited ${exitCode} for ${project.name} — runner-level failure (see tail below)`);
      }
      const summary = verifyProjectResults(project.name, resultsDir, pin.projects[project.name], runStartMs);
      const pinEntry = pin.projects[project.name];
      const skipNote = summary.skippedNames.length > 0 ? ` [${summary.skippedNames.join(", ")}]` : "";
      summaries.push(`${project.name}: ${summary.passed + summary.skipped}/${pinEntry.total} total, ${summary.skipped} skipped${skipNote}`);
    } catch (err) {
      if (err instanceof FloorError) {
        failures.push(err.message);
      } else {
        failures.push(`${project.name}: unexpected wrapper error: ${err.message}`);
      }
      const named = namedFailures(resultsDir);
      if (named.length > 0) {
        failures.push(`--- ${project.name}: ${named.length} named failure(s) from the TRX ---\n    ${named.join("\n    ")}`);
      }
      if (output) {
        const tail = output.trimEnd().split("\n").slice(-6).join("\n");
        failures.push(`--- ${project.name} dotnet output tail ---\n${tail}`);
      }
    }
  }

  if (failures.length > 0) {
    console.error("FLOOR CHECK FAILED:");
    for (const f of failures) console.error(`  ${f}`);
    console.error(`results directory (preserved for evidence): ${runDir}`);
    return 1;
  }
  console.log(`FLOOR OK: ${summaries.join("; ")}`);
  console.log(`results directory: ${runDir}`);
  return 0;
}

if (process.argv[1] && import.meta.url === pathToFileURL(path.resolve(process.argv[1])).href) {
  process.exit(main());
}
