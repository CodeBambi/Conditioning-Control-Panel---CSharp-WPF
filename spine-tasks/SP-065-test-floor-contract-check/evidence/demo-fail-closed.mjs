#!/usr/bin/env node
// SP-065 fail-closed demonstration harness (evidence, not shipped mechanism).
// Imports the REAL verifyProjectResults from the wrapper and feeds it sabotaged
// fixtures — one row per framing-(b) failure mode. Prints one PASS/FAIL line per case.
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { verifyProjectResults, FloorError } from "../../../client/tests/floor/check-floor.mjs";

const NOW = Date.now();
const PIN = { passed: 897, skipped: 0 };

function trx({ total = 897, executed = 897, passed = 897, notExecuted = 0, failed = 0,
  outcome = "Completed", creation = new Date(NOW).toISOString(), truncate = false } = {}) {
  const body =
`<?xml version="1.0" encoding="utf-8"?>
<TestRun id="11111111-2222-3333-4444-555555555555" name="demo" xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
  <Times creation="${creation}" queuing="${creation}" start="${creation}" finish="${creation}" />
  <ResultSummary outcome="${outcome}">
    <Counters total="${total}" executed="${executed}" passed="${passed}" failed="${failed}" error="0" timeout="0" aborted="0" inconclusive="0" passedButRunAborted="0" notRunnable="0" notExecuted="${notExecuted}" disconnected="0" warning="0" completed="0" inProgress="0" pending="0" />
  </ResultSummary>
</TestRun>
`;
  return truncate ? body.slice(0, body.indexOf("</ResultSummary>")) : body;
}

const root = fs.mkdtempSync(path.join(os.tmpdir(), "ccp-floor-demo-"));

function dir(name, files) {
  const d = path.join(root, name);
  fs.mkdirSync(d, { recursive: true });
  for (const [fname, content, mtime] of files) {
    const p = path.join(d, fname);
    fs.writeFileSync(p, content);
    if (mtime) {
      const t = new Date(mtime);
      fs.utimesSync(p, t, t);
    }
  }
  return d;
}

const stale = new Date(NOW - 3_600_000); // one hour before this "run"
const staleIso = stale.toISOString();

const cases = [
  ["results directory missing", path.join(root, "never-created"), "results directory missing"],
  ["no .trx in results dir", dir("empty", []), "no .trx"],
  ["two .trx files", dir("two", [["a.trx", trx()], ["b.trx", trx()]]), "expected exactly 1"],
  ["garbage (not XML)", dir("garbage", [["results.trx", "this is not xml at all"]]), "XML declaration"],
  ["truncated mid-write", dir("trunc", [["results.trx", trx({ truncate: true })]]), "truncated"],
  ["stale mtime + stale creation", dir("stale", [["results.trx", trx({ creation: staleIso }), staleIso]]), "stale results"],
  ["stale creation only (mtime fresh — copy-tool shape)", (() => {
    const d = dir("stale-creation", [["results.trx", trx({ creation: staleIso })]]);
    return d;
  })(), "stale results"],
  ["zero results (filter matched nothing)", dir("zero", [["results.trx", trx({ total: 0, executed: 0, passed: 0 })]]), "0 total results"],
  ["failed category nonzero", dir("failed", [["results.trx", trx({ failed: 1, passed: 896 })]]), "failed"],
  ["outcome not Completed", dir("outcome", [["results.trx", trx({ outcome: "Failed", failed: 1, passed: 896 })]]), "did not finish cleanly"],
  ["inconsistent arithmetic", dir("arith", [["results.trx", trx({ total: 900 })]]), "inconsistent counters"],
  ["off-floor count (pin 897, got 896)", dir("offloor", [["results.trx", trx({ total: 896, executed: 896, passed: 896 })]]), "FLOOR VIOLATION"],
  ["unexpected skip (pin 0, got 1)", dir("skip", [["results.trx", trx({ executed: 896, passed: 896, notExecuted: 1 })]]), "FLOOR VIOLATION"],
];

let allOk = true;
for (const [name, resultsDir, expectToken] of cases) {
  try {
    verifyProjectResults("CcpClient.Tests", resultsDir, PIN, NOW);
    console.log(`FAIL  ${name} — verifyProjectResults returned normally, expected FloorError`);
    allOk = false;
  } catch (err) {
    const ok = err instanceof FloorError && err.message.includes(expectToken);
    if (!ok) allOk = false;
    console.log(`${ok ? "PASS" : "FAIL"}  ${name}\n      -> ${err.message.split("\n")[0]}`);
  }
}

// Positive control: a valid on-floor fixture passes and returns counts.
try {
  const good = dir("good", [["results.trx", trx()]]);
  const summary = verifyProjectResults("CcpClient.Tests", good, PIN, NOW);
  console.log(`PASS  positive control — valid on-floor results accepted (${summary.passed} passed, ${summary.skipped} skipped)`);
} catch (err) {
  allOk = false;
  console.log(`FAIL  positive control — ${err.message}`);
}

console.log(allOk ? "ALL FAIL-CLOSED CASES HELD" : "SOME CASE DID NOT HOLD");
process.exit(allOk ? 0 : 1);
