#!/usr/bin/env node
// SP-065 fail-closed demonstration harness (evidence, not shipped mechanism).
// Imports the REAL verifyProjectResults / discoverTestProjects from the wrapper and feeds
// them sabotaged fixtures — one row per framing-(b) failure mode. Prints PASS/FAIL per case.
//
// Fixture counters MIRROR the observed xunit.runner.visualstudio 3.1.5 behavior: a skipped
// test yields outcome="NotExecuted" in the result list while Counters/@notExecuted stays 0
// and @executed excludes it (probe ccp-floor-5E7zGu — the shape that caught the first
// version of this wrapper trusting Counters arithmetic).
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { verifyProjectResults, discoverTestProjects, FloorError } from "../../../client/tests/floor/check-floor.mjs";

const NOW = Date.now();
const PIN = { passed: 897, skipped: 0 };

function trx({ passed = 897, skipped = 0, failed = 0, exotic = null,
  outcome = "Completed", creation = new Date(NOW).toISOString(),
  truncate = false, totalOverride = null, countersPassedOverride = null } = {}) {
  const results = [];
  let id = 0;
  const push = (o, n) => {
    for (let i = 0; i < n; i++) {
      id += 1;
      results.push(`    <UnitTestResult executionId="${id}" testId="${id}" testName="Fake.T${id}" outcome="${o}" />`);
    }
  };
  push("Passed", passed);
  push("NotExecuted", skipped);
  push("Failed", failed);
  if (exotic) push(exotic, 1);
  const total = totalOverride ?? results.length;
  const executed = passed + failed; // the adapter's observed shape: skips excluded
  const countersPassed = countersPassedOverride ?? passed;
  const body =
`<?xml version="1.0" encoding="utf-8"?>
<TestRun id="11111111-2222-3333-4444-555555555555" name="demo" xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
  <Times creation="${creation}" queuing="${creation}" start="${creation}" finish="${creation}" />
  <Results>
${results.join("\n")}
  </Results>
  <ResultSummary outcome="${outcome}">
    <Counters total="${total}" executed="${executed}" passed="${countersPassed}" failed="${failed}" error="0" timeout="0" aborted="0" inconclusive="0" passedButRunAborted="0" notRunnable="0" notExecuted="0" disconnected="0" warning="0" completed="0" inProgress="0" pending="0" />
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
  ["results directory missing", path.join(root, "never-created"), "results directory missing", PIN],
  ["no .trx in results dir", dir("empty", []), "no .trx", PIN],
  ["two .trx files", dir("two", [["a.trx", trx()], ["b.trx", trx()]]), "expected exactly 1", PIN],
  ["garbage (not XML)", dir("garbage", [["results.trx", "this is not xml at all"]]), "XML declaration", PIN],
  ["truncated mid-write", dir("trunc", [["results.trx", trx({ truncate: true })]]), "truncated", PIN],
  ["stale mtime + stale creation", dir("stale", [["results.trx", trx({ creation: staleIso }), staleIso]]), "stale results", PIN],
  ["stale creation only (mtime fresh — copy-tool shape)", dir("stale-creation", [["results.trx", trx({ creation: staleIso })]]), "stale results", PIN],
  ["zero results (filter matched nothing)", dir("zero", [["results.trx", trx({ passed: 0 })]]), "0 total results", PIN],
  ["failed category nonzero", dir("failed", [["results.trx", trx({ passed: 896, failed: 1 })]]), "failed", PIN],
  ["outcome not Completed", dir("outcome", [["results.trx", trx({ passed: 896, failed: 1, outcome: "Failed" })]]), "did not finish cleanly", PIN],
  ["result list vs Counters total mismatch", dir("arith", [["results.trx", trx({ totalOverride: 900 })]]), "inconsistent results", PIN],
  ["result list Passed vs Counters passed mismatch", dir("arith2", [["results.trx", trx({ countersPassedOverride: 896 })]]), "inconsistent results", PIN],
  ["exotic outcome in result list", dir("exotic", [["results.trx", trx({ passed: 896, exotic: "Warning" })]]), "unexpected outcome", PIN],
  ["counters not self-closing", dir("counters", [["results.trx", trx().replace('pending="0" />', 'pending="0"></Counters>')]]), "not a self-closing tag", PIN],
  ["off-floor count (pin 897, got 896)", dir("offloor", [["results.trx", trx({ passed: 896 })]]), "FLOOR VIOLATION", PIN],
  ["unexpected skip (pin 0, got 1) — result-list skip, counters blind", dir("skip", [["results.trx", trx({ passed: 896, skipped: 1 })]]), "FLOOR VIOLATION", PIN],
];

let allOk = true;
for (const [name, resultsDir, expectToken, pin] of cases) {
  try {
    verifyProjectResults("CcpClient.Tests", resultsDir, pin, NOW);
    console.log(`FAIL  ${name} — verifyProjectResults returned normally, expected FloorError`);
    allOk = false;
  } catch (err) {
    const ok = err instanceof FloorError && err.message.includes(expectToken);
    if (!ok) allOk = false;
    console.log(`${ok ? "PASS" : "FAIL"}  ${name}\n      -> ${err.message.split("\n")[0]}`);
  }
}

// Positive controls.
const positives = [
  ["positive control — valid on-floor results accepted", trx(), PIN],
  ["REGRESSION positive — one skip, adapter-blind counters, pin {897,1} accepted\n      (the exact ccp-floor-5E7zGu shape that false-redded the first wrapper version)",
    trx({ passed: 897, skipped: 1 }), { passed: 897, skipped: 1 }],
];
for (const [name, content, pin] of positives) {
  try {
    const summary = verifyProjectResults("CcpClient.Tests", dir(`good-${Math.abs(hashCode(name))}`, [["results.trx", content]]), pin, NOW);
    console.log(`PASS  ${name} (${summary.passed} passed, ${summary.skipped} skipped)`);
  } catch (err) {
    allOk = false;
    console.log(`FAIL  ${name} — ${err.message.split("\n")[0]}`);
  }
}

function hashCode(s) {
  let h = 0;
  for (let i = 0; i < s.length; i++) h = (h * 31 + s.charCodeAt(i)) | 0;
  return h;
}

// Discovery fail-closed cases (pre-completion consult hole: a new test project in the sln
// must not sit outside the floor).
const SLN_OK = `
Project("{FAE}") = "CcpClient.Desktop", "src\\CcpClient.Desktop\\CcpClient.Desktop.csproj", "{1}"
EndProject
Project("{FAE}") = "CcpClient.Tests", "tests\\CcpClient.Tests\\CcpClient.Tests.csproj", "{2}"
EndProject
Project("{FAE}") = "CcpClient.HeadlessTests", "tests\\CcpClient.HeadlessTests\\CcpClient.HeadlessTests.csproj", "{3}"
EndProject
`;
const PIN_BOTH = { projects: { "CcpClient.Tests": { passed: 1, skipped: 0 }, "CcpClient.HeadlessTests": { passed: 1, skipped: 0 } } };

const discoveryCases = [
  ["new unpinned test project in sln", SLN_OK + `Project("{FAE}") = "CcpClient.NewTests", "tests\\CcpClient.NewTests\\CcpClient.NewTests.csproj", "{4}"\nEndProject\n`, PIN_BOTH, "NO floor pin"],
  ["pinned project absent from sln", SLN_OK, { projects: { ...PIN_BOTH.projects, "CcpClient.GoneTests": { passed: 1, skipped: 0 } } }, "stale pin"],
  ["sln with no test projects at all", `Project("{FAE}") = "CcpClient.Desktop", "src\\CcpClient.Desktop\\CcpClient.Desktop.csproj", "{1}"\nEndProject\n`, PIN_BOTH, "refuses to go blind"],
];
for (const [name, sln, pin, expectToken] of discoveryCases) {
  try {
    discoverTestProjects(sln, pin);
    console.log(`FAIL  ${name} — discoverTestProjects returned normally, expected FloorError`);
    allOk = false;
  } catch (err) {
    const ok = err instanceof FloorError && err.message.includes(expectToken);
    if (!ok) allOk = false;
    console.log(`${ok ? "PASS" : "FAIL"}  ${name}\n      -> ${err.message.split("\n")[0]}`);
  }
}
try {
  const found = discoverTestProjects(SLN_OK, PIN_BOTH);
  const ok = found.length === 2 && found[0].name === "CcpClient.Tests" && found[1].name === "CcpClient.HeadlessTests";
  if (!ok) allOk = false;
  console.log(`${ok ? "PASS" : "FAIL"}  discovery positive control — both real projects discovered (${found.map((p) => p.name).join(", ")})`);
} catch (err) {
  allOk = false;
  console.log(`FAIL  discovery positive control — ${err.message}`);
}

console.log(allOk ? "ALL FAIL-CLOSED CASES HELD" : "SOME CASE DID NOT HOLD");
process.exit(allOk ? 0 : 1);
