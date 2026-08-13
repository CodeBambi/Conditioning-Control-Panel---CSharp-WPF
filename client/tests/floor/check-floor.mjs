#!/usr/bin/env node
// SP-065 (board row 49 part 2): mechanical skip/count floor for the client test suite.
// Runs BOTH test projects with TRX loggers into a per-run temp dir OUTSIDE the worktree,
// then post-processes the results against an exact per-project pin (floor.json).
//
// Green requires ALL THREE: dotnet test exit 0, every fail-closed TRX check, and the pin.
// Exit 0 = floor met. Exit 1 = any violation (each prints a loud named reason).
//
// Never sets CCP_DATA_ROOT (port-workflow.md:204 — a process-wide override makes the
// SP-057 pin skip and the floor goes blind). Never writes inside the worktree:
// --results-directory targets os.tmpdir(); *.trx / TestResults/ are gitignored and the
// merge-time dirty check tolerates nothing.

import { execFileSync } from "node:child_process";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";

const HERE = path.dirname(fileURLToPath(import.meta.url));
const CLIENT = path.resolve(HERE, "..", "..");
const PIN_PATH = path.join(HERE, "floor.json");

const PROJECTS = [
  {
    name: "CcpClient.Tests",
    csproj: path.join(CLIENT, "tests", "CcpClient.Tests", "CcpClient.Tests.csproj"),
  },
  {
    name: "CcpClient.HeadlessTests",
    csproj: path.join(CLIENT, "tests", "CcpClient.HeadlessTests", "CcpClient.HeadlessTests.csproj"),
  },
];

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
  for (const p of PROJECTS) {
    const entry = pin?.projects?.[p.name];
    if (!entry || !Number.isInteger(entry.passed) || !Number.isInteger(entry.skipped)) {
      fail(`pin file ${PIN_PATH} has no integer {passed, skipped} entry for ${p.name}`);
    }
  }
  return pin;
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
  if (!xml.startsWith("\uFEFF<?xml") && !xml.startsWith("<?xml")) {
    fail(`${projectName}: ${trxPath} does not start with an XML declaration — unparseable results`);
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
  if (counters.executed + counters.notExecuted !== counters.total) {
    fail(`${projectName}: executed(${counters.executed}) + notExecuted(${counters.notExecuted}) != total(${counters.total}) — inconsistent counters`);
  }
  if (counters.passed !== counters.executed) {
    fail(`${projectName}: passed(${counters.passed}) != executed(${counters.executed}) with zero bad categories — inconsistent counters`);
  }

  const skipped = counters.notExecuted;
  if (counters.passed !== pinEntry.passed || skipped !== pinEntry.skipped) {
    fail(
      `${projectName}: FLOOR VIOLATION — passed ${counters.passed} (pin ${pinEntry.passed}), ` +
      `skipped ${skipped} (pin ${pinEntry.skipped}). A count drift in EITHER direction or an ` +
      `unexpected skip fails the contract (SP-065). If this change is intentional, bump ` +
      `client/tests/floor/floor.json in the SAME commit and state the reason in the message.`
    );
  }
  return { passed: counters.passed, skipped, total: counters.total, trxPath };
}

function runProject(project, runDir) {
  const resultsDir = path.join(runDir, project.name);
  fs.mkdirSync(resultsDir, { recursive: true });
  let exitCode = 0;
  let output = "";
  try {
    output = execFileSync(
      "dotnet",
      ["test", project.csproj, "-c", "Debug", "--nologo", "--no-build",
        "--results-directory", resultsDir, "--logger", "trx;LogFileName=results.trx"],
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
  const runDir = fs.mkdtempSync(path.join(os.tmpdir(), "ccp-floor-"));
  console.log(`floor run results directory: ${runDir}`);

  const failures = [];
  const summaries = [];
  for (const project of PROJECTS) {
    let exitCode = 1;
    let output = "";
    let resultsDir = path.join(runDir, project.name);
    try {
      ({ exitCode, output, resultsDir } = runProject(project, runDir));
      if (exitCode !== 0) {
        fail(`dotnet test exited ${exitCode} for ${projectName(project)} — runner-level failure (see tail below)`);
      }
      const summary = verifyProjectResults(project.name, resultsDir, pin.projects[project.name], runStartMs);
      summaries.push(`${project.name}: ${summary.passed}/${pin.projects[project.name].passed} passed, ${summary.skipped}/${pin.projects[project.name].skipped} skipped`);
    } catch (err) {
      if (err instanceof FloorError) {
        failures.push(err.message);
      } else {
        failures.push(`${project.name}: unexpected wrapper error: ${err.message}`);
      }
      if (output) {
        const tail = output.trimEnd().split("\n").slice(-6).join("\n");
        failures.push(`--- ${project.name} dotnet output tail ---\n${tail}`);
      }
    }
  }

  if (failures.length > 0) {
    console.error("FLOOR CHECK FAILED (SP-065):");
    for (const f of failures) console.error(`  ${f}`);
    console.error(`results directory (preserved for evidence): ${runDir}`);
    return 1;
  }
  console.log(`FLOOR OK: ${summaries.join("; ")}`);
  console.log(`results directory: ${runDir}`);
  return 0;
}

function projectName(project) {
  return project.name;
}

if (process.argv[1] && import.meta.url === pathToFileURL(path.resolve(process.argv[1])).href) {
  process.exit(main());
}
