#!/usr/bin/env node
// The build-warning gate. Makes "0 warnings / 0 errors" a MEASURED fact.
//
// WHY THIS EXISTS
// Every landed wave of this port has reported "0 warnings / 0 errors" and not one of those
// claims was ever checked by anything but a human reading build output. An earlier wave found its own
// reading filter, `grep -E "error|warning CS|Build succ"`, CANNOT MATCH `warning xUnit2013`;
// it had reported clean four times off that filtered stream, and the two real warnings only
// surfaced because a reviewer forced a full rebuild. `check-floor.mjs` never had any warning
// handling at all — it is a TEST floor and runs `--no-build` by design.
//
// THE SECOND, INDEPENDENT TRAP, MEASURED ON THE BASE TREE (evidence: this packet's
// measure-01..04 logs)
//   1. cold `dotnet build client/CcpClient.sln -c Debug --nologo`, one induced CS0219 -> "1 Warning(s)"
//   2. THE SAME COMMAND AGAIN, source unchanged, warning still in the file -> "0 Warning(s)"
//   3. same command + `--no-incremental`, same tree                        -> "1 Warning(s)"
// MSBuild skipped CoreCompile for an up-to-date project, so the compiler never re-emitted the
// diagnostic. A warning is a property of the COMPILATION, not of the assembly, and a build that
// compiles nothing reports no warnings. So a warning gate that does not force compilation is
// VACUOUS: it would sign off a tree with live warnings in it. That is why `--no-incremental` is
// not negotiable here, and why `WarningGateGuardTests` pins it with this measurement as the reason.
//
// RELATIONSHIP TO THE TEST FLOOR — READ THIS BEFORE CHANGING EITHER FILE
// `check-floor.mjs` runs `dotnet test --no-build` DELIBERATELY (client/docs/port-lessons.md:204 @ a8d32c219).
// Run standalone it measures the LAST BUILD rather than the tree; that defect cost real time at
// three lands, which is why its `assertBuildIsFresh` stale-build guard exists and why it fires.
// THIS FILE DOES NOT CHANGE THAT. The floor still does not build. The warning gate builds because
// building is the thing it observes, and it builds the same solution and configuration into the
// same bin/obj the floor then reads — so the intended order
//     node client/tests/floor/check-warnings.mjs   (builds, and reads the warnings)
//     node client/tests/floor/check-floor.mjs      (still --no-build, still stale-guarded)
// SATISFIES the floor's freshness precondition instead of eroding it. Never merge the two.
//
// CONCURRENCY HAZARD, NAMED
// This gate REBUILDS the shared bin/obj of the worktree it runs in. Never run it concurrently
// with a floor run IN THE SAME WORKTREE: the floor's `--no-build` runner would be loading
// assemblies while they are being replaced. Across worktrees each lane has its own bin/obj, and
// machine-wide build concurrency is already bounded by client/tools/gate/with-slot.mjs.
//
// WHAT IT CANNOT SEE (the boundary; the long version is in client/docs/verification-harness.md)
//   - Only client/CcpClient.sln, only Debug|Any CPU, only net10.0. Not the Release/RID publish
//     path, not ConditioningControlPanel/, not CCP.*.
//   - A SUPPRESSED warning, by construction: NoWarn, an inline pragma disable, a suppression
//     attribute (including the [assembly:] / [module:] forms), an .editorconfig OR .globalconfig
//     severity of `none`, a lowered WarningLevel — all of them act before MSBuild prints anything,
//     so no output-reading gate can ever observe them. Measured: a two-line .globalconfig with
//     dotnet_diagnostic.CS0219.severity = none, and ZERO csproj references, turns this gate's own
//     forced-full build from "1 Warning(s)" naming a real CS0219 into "0 Warning(s)".
//     That hole is only PARTLY covered, by a different instrument: the lexical census in
//     WarningSuppressionCensusTests binds an ENUMERATED list of file shapes. A suppression in a
//     shape outside that list, or in a file above the repository root, is silent to both. Do not
//     read a green gate plus a green census as "no warning was suppressed"; read it as "no
//     suppression of a shape we enumerate was added".
//   - Whatever THIS SDK and THESE analyzer versions emit. A different SDK can emit more or fewer.
//   - Restore-time (NU*) warnings are re-evaluated only on a run where restore actually runs;
//     `--no-incremental` forces compilation, not restore. The gate REPORTS whether restore
//     no-opped so the reader knows which reading they hold, and `--cold` (opt-in) forces the
//     re-resolve when the diff touches a *.csproj, *.props, *.targets or a lock file.
//   - The zero-byte-output sweep weighs the four shapes that are never legitimately empty (.dll,
//     .exe, .deps.json, .runtimeconfig.json) under the output directories MSBuild NAMED in this
//     build. A truncated file of any other shape, or one in a project that produced no output line,
//     is outside it — and a file that is corrupt but non-empty is outside it by construction.
//   - It observes a build. It proves nothing about behaviour, rendering, timing or interaction.
//
// USAGE
//   node client/tests/floor/check-warnings.mjs            build + observe; exit 0 only if 0/0
//   node client/tests/floor/check-warnings.mjs --self-test  parser corpus only, no build
//   node client/tests/floor/check-warnings.mjs --cold       also force a full NuGet re-resolve
//   node client/tests/floor/check-warnings.mjs --log-dir D  put the full build log in D
// Exit 0 = zero warnings, zero errors and no zero-byte generated output, all positively observed.
// Exit 1 = anything else, including every "I could not tell" case: this gate never reports a count
// it did not read.

import { spawn } from "node:child_process";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";

const HERE = path.dirname(fileURLToPath(import.meta.url));
const CLIENT = path.resolve(HERE, "..", "..");
const SLN_PATH = path.join(CLIENT, "CcpClient.sln");
const CONFIGURATION = "Debug";

// The build, pinned so the parse does not depend on the machine.
//  --no-incremental : see THE SECOND TRAP above. Without it this gate is vacuous.
//  -tl:off          : never let MSBuild pick its terminal logger; the classic console logger's
//                     diagnostic and summary shapes are what the regexes below were measured on.
//  DOTNET_CLI_UI_LANGUAGE=en : "warning", "Warning(s)", "Error(s)" and "Build succeeded" are the
//                     literals this file matches; a localized SDK would otherwise print them
//                     translated and the gate would read a clean build off a stream it cannot
//                     parse. Probed and accepted on SDK 10.0.303 / MSBuild 18.6.14.
export const BUILD_ARGS = [
  "build", SLN_PATH, "-c", CONFIGURATION, "--nologo", "--no-incremental", "-tl:off",
];

// OPT-IN, appended by --cold. `--no-incremental` forces COMPILATION, not RESTORE: NuGet no-ops with
// "All projects are up-to-date for restore", so restore-time (NU*) warnings are not re-evaluated on
// an ordinary run and the gate says so in its own output rather than hiding it. `--force` resolves
// every dependency again, which is what makes those warnings reappear. It is opt-in and not the
// default because it costs a full re-resolve and can touch the network; use it when the diff
// touches a *.csproj, *.props, *.targets or a lock file. It weakens nothing: it only makes MORE
// of the build speak.
export const COLD_ARGS = ["--force"];

export class WarningGateError extends Error {}

function fail(message) {
  throw new WarningGateError(message);
}

// ---------------------------------------------------------------------------
// Parsing. Every function here is pure and exported so the --self-test corpus can
// exercise it against literal build output, including the exact line the retired filter
// could not match.
// ---------------------------------------------------------------------------

// MSBuild's canonical diagnostic format is
//   [origin[(line,col)] : ]warning <CODE>: <text>[ [<project>]]
// The category word is lower-case `warning` and is followed by the code. THE CODE IS A
// WILDCARD ON PURPOSE: the retired filter hardcoded `warning CS`, which matches CS0219 and
// CS0067 and is structurally incapable of matching xUnit2013, NU1701, MSB3277, AVLN3001,
// CA1416, IL2026 or any future analyzer prefix. Matching `warning <anything>:` is the whole
// correction.
const WARNING_WITH_CODE = /^\s*(?:(?<origin>.*?)\s*:\s*)?warning\s+(?<code>[A-Za-z][A-Za-z0-9_]*)\s*:\s*(?<text>.*)$/;
// MSBuild can also emit a codeless warning (`MSBUILD : warning : text`). Counted, with the code
// reported as "(none)", because a warning the gate cannot name is still a warning.
const WARNING_WITHOUT_CODE = /^\s*(?:(?<origin>.*?)\s*:\s*)?warning\s*:\s*(?<text>.*)$/;
const ERROR_WITH_CODE = /^\s*(?:(?<origin>.*?)\s*:\s*)?error\s+(?<code>[A-Za-z][A-Za-z0-9_]*)\s*:\s*(?<text>.*)$/;

const SUMMARY_WARNINGS = /^\s*(\d+)\s+Warning\(s\)\s*$/;
const SUMMARY_ERRORS = /^\s*(\d+)\s+Error\(s\)\s*$/;
const PROJECT_OUTPUT = /^\s*(?<name>[^\s>]+)\s+->\s+(?<output>\S.*?)\s*$/;
const RESTORE_NOOP = /All projects are up-to-date for restore\./;

/**
 * Every warning diagnostic in the build output, DEDUPLICATED BY EXACT LINE.
 * MSBuild's console logger prints each diagnostic twice — once inline where it happened and once
 * in the trailing summary block — byte-identically, so exact-line dedup collapses exactly that
 * pair and nothing else. Two projects emitting the same warning differ in their `[project]`
 * suffix and stay two entries.
 */
export function parseWarnings(output) {
  const seen = new Set();
  const rows = [];
  for (const raw of output.split(/\r?\n/)) {
    const line = raw.trimEnd();
    const withCode = WARNING_WITH_CODE.exec(line);
    const match = withCode ?? WARNING_WITHOUT_CODE.exec(line);
    if (!match) {
      continue;
    }
    if (seen.has(line)) {
      continue;
    }
    seen.add(line);
    rows.push({
      line,
      code: match.groups.code ?? "(none)",
      origin: (match.groups.origin ?? "").trim(),
      text: match.groups.text.trim(),
    });
  }
  return rows;
}

/** Every error diagnostic, same dedup rule. Reported so a red build names its cause here too. */
export function parseErrors(output) {
  const seen = new Set();
  const rows = [];
  for (const raw of output.split(/\r?\n/)) {
    const line = raw.trimEnd();
    const match = ERROR_WITH_CODE.exec(line);
    if (!match || seen.has(line)) {
      continue;
    }
    seen.add(line);
    rows.push({ line, code: match.groups.code, text: match.groups.text.trim() });
  }
  return rows;
}

/**
 * MSBuild's own summary counters. Returns null for either counter it did not find, and the
 * caller FAILS on a null: this gate never reports a count it did not read. That is the direct
 * lesson of the retired filter — a number obtained from a stream you did not verify is not a measurement.
 */
export function parseSummary(output) {
  let warnings = null;
  let errors = null;
  let warningLines = 0;
  let errorLines = 0;
  for (const raw of output.split(/\r?\n/)) {
    const line = raw.trimEnd();
    const w = SUMMARY_WARNINGS.exec(line);
    if (w) {
      warnings = Number(w[1]);
      warningLines += 1;
    }
    const e = SUMMARY_ERRORS.exec(line);
    if (e) {
      errors = Number(e[1]);
      errorLines += 1;
    }
  }
  return { warnings, errors, warningLines, errorLines };
}

/**
 * Every project in the solution, name -> sln-relative csproj path. Discovery from the SOLUTION
 * and never a hardcoded list, for the same reason check-floor.mjs discovers its test projects:
 * a project that joins the sln and is not observed by the gate is a hole exactly the size of
 * that project. Solution FOLDERS carry no .csproj and are excluded by the pattern.
 */
export function discoverSolutionProjects(slnText) {
  const projects = new Map();
  for (const m of slnText.matchAll(/Project\("[^"]*"\)\s*=\s*"[^"]*"\s*,\s*"([^"]+\.csproj)"/g)) {
    const rel = m[1].replace(/\\/g, "/");
    projects.set(path.posix.basename(rel, ".csproj"), rel);
  }
  return projects;
}

/** Project name -> the assembly path it reported (`Name -> path`), i.e. the projects that built. */
export function parseProjectOutputs(output) {
  const outputs = new Map();
  for (const raw of output.split(/\r?\n/)) {
    const m = PROJECT_OUTPUT.exec(raw.trimEnd());
    if (m) {
      outputs.set(m.groups.name, m.groups.output);
    }
  }
  return outputs;
}

/** Project names that reported an output (`Name -> path`) — i.e. that actually built. */
export function parseBuiltProjects(output) {
  return new Set(parseProjectOutputs(output).keys());
}

// ---------------------------------------------------------------------------
// THE TREE THIS BUILD LEFT BEHIND, and the day a cold build signed off a broken one.
//
// 2026-08-25: a disk-full MSBuild run (414 x MSB3021) truncated
// client/tests/CcpClient.HeadlessTests/bin/Debug/net10.0/CcpClient.HeadlessTests.deps.json to ZERO
// BYTES. The runner then died with "Error initializing the dependency resolver". The dangerous half
// is what happened next: THIS GATE, forced non-incremental, reported 0 warnings and 0 errors over
// that same tree - because a build that leaves a generated file alone says nothing about it, and
// nothing in the build stream ever mentioned it.
//
// THE REPORTED MECHANISM DOES NOT REPRODUCE HERE, AND THAT IS RECORDED RATHER THAN REPEATED. The
// row explains the survival as "a cold build does not re-derive a generated file whose timestamp is
// newer than its inputs". Measured on SDK 10.0.x in this worktree: truncating that exact file to
// zero and running this gate REPAIRED it (63,971 bytes back, 0 zero-byte outputs) - `--no-incremental`
// maps to the Rebuild target, whose Clean deletes the file first. So the incident needed something
// more than a newer timestamp to survive, most likely a Clean that could not read its own
// FileListAbsolute.txt on the same full volume. What is NOT in doubt is the shape: a zero-byte
// output in a built tree, invisible to a 0/0 build, fatal to whatever loads it. Reproduced here
// end to end - the zeroed deps.json gives the runner's exact
// "Error initializing the dependency resolver", and this sweep names the file out of 1,154 weighed.
//
// WHY HERE AND NOT IN THE FLOOR GATE, which was the other candidate (it already stats the assembly
// under test in assertBuildIsFresh and could compare sizes for a line). MEASURED, not assumed: the
// floor over that same broken tree fails at "the test assembly exited 2147516555" and its output
// tail does carry the resolver message and the file's path - so it is not blind, and this is not
// about rescuing it. It is about COVERAGE and EARLINESS. The floor only ever looks at the two TEST
// projects, so a truncated CcpClient.Desktop.deps.json or a zero-byte Avalonia native is invisible
// to it; and it only speaks after a suite has run. This gate has just built EVERY project in the
// solution and is handed each one's output path by MSBuild itself, so it is the only place the
// check covers the whole tree - and it is the gate that told the lie.
//
// SCOPE: files that exist and are zero bytes, of the four shapes that are NEVER legitimately empty
// - a managed assembly, an apphost, and the two generated host files. An absent file is not this
// check's business (a project that produced no output at all is already a named failure above).
// ---------------------------------------------------------------------------

const NEVER_EMPTY_OUTPUT = /\.(dll|exe|deps\.json|runtimeconfig\.json)$/i;

/**
 * Every zero-byte generated output under the directories these build outputs live in, recursively
 * (a truncated native under runtimes/ is the same defect one layer down). Returns the offenders AND
 * how many files were weighed, because this file's standing rule is that a gate never reports a
 * count it did not read — a sweep that silently examined nothing would otherwise read as clean.
 * `io` is injected so the self-test can drive both directions without a build.
 */
export function findTruncatedOutputs(outputPaths, io = {
  list: (dir) => fs.readdirSync(dir, { recursive: true, withFileTypes: true })
    .filter((e) => e.isFile())
    .map((e) => path.join(e.parentPath ?? e.path, e.name)),
  size: (file) => fs.statSync(file).size,
}) {
  const dirs = [...new Set(outputPaths.map((p) => path.dirname(p)))].sort();
  const seen = new Set();
  const truncated = [];
  for (const dir of dirs) {
    let files;
    try {
      files = io.list(dir);
    } catch {
      continue; // an output directory that cannot be listed is not evidence of truncation
    }
    for (const file of files) {
      if (!NEVER_EMPTY_OUTPUT.test(file) || seen.has(file)) {
        continue;
      }
      let size;
      try {
        size = io.size(file);
      } catch {
        continue;
      }
      seen.add(file);
      if (size === 0) {
        truncated.push(file);
      }
    }
  }
  return { truncated: truncated.sort(), scanned: seen.size };
}

/**
 * The whole verdict, as a pure function of the build's exit code and its complete output.
 * Split out from the spawn so the self-test can drive it with literal output.
 */
export function evaluate({ exitCode, output, slnText, outputSweep = null }) {
  const problems = [];
  const warnings = parseWarnings(output);
  const errors = parseErrors(output);
  const summary = parseSummary(output);
  const built = parseBuiltProjects(output);
  const projects = discoverSolutionProjects(slnText);

  if (projects.size === 0) {
    problems.push("no projects discovered in client/CcpClient.sln — the warning gate refuses to go blind");
  }

  // Fail closed on an unreadable summary BEFORE trusting any count.
  if (summary.warningLines !== 1 || summary.errorLines !== 1) {
    problems.push(
      `could not read MSBuild's summary counters (found ${summary.warningLines} "N Warning(s)" line(s) and ` +
      `${summary.errorLines} "N Error(s)" line(s), expected exactly 1 of each). The gate does not report a ` +
      "count it did not read — see the full log named below."
    );
  }

  if (exitCode !== 0) {
    problems.push(`dotnet build exited ${exitCode} — the build itself failed`);
  }

  const missing = [...projects.keys()].filter((n) => !built.has(n));
  if (missing.length > 0) {
    problems.push(
      `project(s) in client/CcpClient.sln that produced NO output line in this build: ${missing.join(", ")}. ` +
      "A project that quietly left the build is unobserved, and its warnings would read as zero " +
      "(same defect class as the test floor's 'a suite outside the floor')."
    );
  }

  // The zero-byte-output sweep. `null` means the caller did not run it (the pure self-test
  // corpora); a sweep that ran and weighed NOTHING is a failure, not a pass, for the same reason
  // an unreadable summary is.
  if (outputSweep !== null) {
    if (outputSweep.scanned === 0) {
      problems.push(
        "the zero-byte-output sweep weighed 0 files — it examined nothing and cannot be read as " +
        "clean (the build's own `Name -> path` lines named no readable output directory)."
      );
    }
    if (outputSweep.truncated.length > 0) {
      problems.push(
        `${outputSweep.truncated.length} ZERO-BYTE BUILD OUTPUT(S) — this tree is CORRUPT and the ` +
        "build just reported nothing about it: a build says what it COMPILED, not what is sitting " +
        "in the output folder, so an empty .deps.json is fatal to the runner and invisible to " +
        "0 warnings / 0 errors. Delete each one and rebuild; if it comes back empty, the volume is " +
        "the suspect:\n" +
        outputSweep.truncated.map((f) => `      ${f}`).join("\n")
      );
    }
  }

  if (warnings.length > 0) {
    problems.push(`${warnings.length} BUILD WARNING(S):\n` +
      warnings.map((w) => `      [${w.code}] ${w.line}`).join("\n"));
  }
  if (errors.length > 0) {
    problems.push(`${errors.length} BUILD ERROR(S):\n` +
      errors.map((e) => `      [${e.code}] ${e.line}`).join("\n"));
  }

  // Two independent readings of the same build must agree. A disagreement can only ever make
  // this gate REDDER, never greener, so it is safe to fail on and worth failing on: it means the
  // parser and MSBuild are describing different builds and neither number should be quoted.
  if (summary.warningLines === 1 && summary.warnings !== warnings.length) {
    problems.push(
      `the two readings disagree: MSBuild's summary says ${summary.warnings} warning(s), the ` +
      `diagnostic-line parse found ${warnings.length}. Neither number may be quoted until the full ` +
      "log is read."
    );
  }
  if (summary.errorLines === 1 && summary.errors !== errors.length) {
    problems.push(
      `the two readings disagree: MSBuild's summary says ${summary.errors} error(s), the ` +
      `diagnostic-line parse found ${errors.length}.`
    );
  }

  return {
    ok: problems.length === 0,
    problems,
    warnings,
    errors,
    summary,
    builtProjects: [...built].sort(),
    solutionProjects: [...projects.keys()].sort(),
    restoreNoOp: RESTORE_NOOP.test(output),
    outputSweep,
  };
}

// ---------------------------------------------------------------------------
// Self-test corpus. Literal build-output lines, including the exact line the retired filter missed.
// ---------------------------------------------------------------------------

const P = "C:\\repo\\client\\tests\\CcpClient.Tests\\CcpClient.Tests.csproj";

export const SELF_TEST_POSITIVES = [
  // The compiler warning this packet induced to prove the gate bites.
  `C:\\repo\\client\\tests\\CcpClient.Tests\\Probe.cs(7,13): warning CS0219: The variable 'unused' is assigned but its value is never used [${P}]`,
  // THE LINE THE RETIRED FILTER MISSED. `grep -E "error|warning CS|Build succ"` cannot match this, and four clean
  // reports were made off a stream filtered that way. It is in this corpus by name, forever.
  `C:\\repo\\client\\tests\\CcpClient.Tests\\Probe.cs(42,9): warning xUnit2013: Do not use Assert.Equal() to check for collection size. [${P}]`,
  // Restore-time and MSBuild-level warnings: origin is a project file, no (line,col).
  `${P} : warning NU1701: Package 'Foo 1.0.0' was restored using '.NETFramework,Version=v4.6.1' instead of the project target framework 'net10.0'.`,
  `${P} : warning MSB3277: Found conflicts between different versions of "System.Text.Json" that could not be resolved.`,
  // Avalonia's XAML compiler prefix — the one prefix this repo already suppresses by NoWarn, so
  // the gate must be able to name it if the suppression is ever removed.
  `C:\\repo\\client\\src\\CcpClient.Desktop\\MainWindow.axaml(3,4): warning AVLN3001: XAML resource not found [C:\\repo\\client\\src\\CcpClient.Desktop\\CcpClient.Desktop.csproj]`,
  // Origin without a file at all.
  "MSBUILD : warning MSB4078: The project file is not supported by MSBuild and cannot be built.",
  // Indented (nested/parallel build output).
  `    C:\\repo\\client\\src\\A.cs(1,1): warning CA1416: This call site is reachable on all platforms. [${P}]`,
  // Codeless.
  "MSBUILD : warning : an unnamed warning still counts",
];

export const SELF_TEST_NEGATIVES = [
  "    0 Warning(s)",
  "    1 Warning(s)",
  "    0 Error(s)",
  "Build succeeded.",
  "  CcpClient.Tests -> C:\\repo\\client\\tests\\CcpClient.Tests\\bin\\Debug\\net10.0\\CcpClient.Tests.dll",
  "  Determining projects to restore...",
  "  All projects are up-to-date for restore.",
  "  Passed CcpClient.Tests.WarningGateGuardTests.TheGateForcesANonIncrementalBuild [3 ms]",
  "Time Elapsed 00:00:47.29",
  // An error is not a warning; it is counted by parseErrors instead.
  `C:\\repo\\client\\src\\A.cs(1,1): error CS0103: The name 'x' does not exist in the current context [${P}]`,
];

// The retired filter, kept here as an executable exhibit rather than a story.
const SP113_FILTER = /error|warning CS|Build succ/;

export function runSelfTest(log = console.log) {
  const failures = [];

  for (const line of SELF_TEST_POSITIVES) {
    const rows = parseWarnings(line);
    if (rows.length !== 1) {
      failures.push(`positive corpus line NOT matched as a warning: ${line}`);
    }
  }
  for (const line of SELF_TEST_NEGATIVES) {
    const rows = parseWarnings(line);
    if (rows.length !== 0) {
      failures.push(`negative corpus line matched as a warning [${rows[0]?.code}]: ${line}`);
    }
  }

  // Dedup: the same line inline and again in the summary block is ONE warning.
  const twice = `${SELF_TEST_POSITIVES[0]}\nsomething else\n${SELF_TEST_POSITIVES[0]}`;
  if (parseWarnings(twice).length !== 1) {
    failures.push("a byte-identical repeat of one diagnostic did not deduplicate to a single warning");
  }
  // ...but two projects emitting the same warning are two warnings.
  const twoProjects = [
    "C:\\repo\\a.cs(1,1): warning CS0219: x [C:\\repo\\A.csproj]",
    "C:\\repo\\a.cs(1,1): warning CS0219: x [C:\\repo\\B.csproj]",
  ].join("\n");
  if (parseWarnings(twoProjects).length !== 2) {
    failures.push("the same warning from two projects collapsed to one");
  }

  // THE EXHIBIT: the retired filter versus this one, on the line that fooled it.
  const sp113Line = SELF_TEST_POSITIVES[1];
  if (SP113_FILTER.test(sp113Line)) {
    failures.push("the retired filter is recorded as unable to match the xUnit2013 line, but it matched it — " +
      "the exhibit is wrong and the record built on it must be re-checked");
  }
  if (parseWarnings(sp113Line).length !== 1) {
    failures.push("this gate failed to match the xUnit2013 line the retired filter missed — the correction does not work");
  }

  // Summary reading, including the refusal case.
  const s = parseSummary("Build succeeded.\n    2 Warning(s)\n    0 Error(s)\n");
  if (s.warnings !== 2 || s.errors !== 0 || s.warningLines !== 1 || s.errorLines !== 1) {
    failures.push(`summary parse wrong: ${JSON.stringify(s)}`);
  }
  const none = parseSummary("Build succeeded.\n");
  if (none.warnings !== null || none.warningLines !== 0) {
    failures.push("a build output with no summary block must read as UNKNOWN, not as zero");
  }

  // evaluate(): a missing summary is a failure even when no diagnostic line was found.
  const sln = 'Project("{X}") = "A", "src\\A\\A.csproj", "{Y}"';
  const blind = evaluate({ exitCode: 0, output: "  A -> C:\\out\\A.dll\n", slnText: sln });
  if (blind.ok) {
    failures.push("evaluate() reported OK for a build whose summary counters were unreadable");
  }
  // evaluate(): a project that never built is a failure.
  const skipped = evaluate({
    exitCode: 0,
    output: "Build succeeded.\n    0 Warning(s)\n    0 Error(s)\n",
    slnText: sln,
  });
  if (skipped.ok) {
    failures.push("evaluate() reported OK for a solution project that produced no output line");
  }
  // evaluate(): the clean case really is clean.
  const clean = evaluate({
    exitCode: 0,
    output: "  A -> C:\\out\\A.dll\n\nBuild succeeded.\n    0 Warning(s)\n    0 Error(s)\n",
    slnText: sln,
  });
  if (!clean.ok) {
    failures.push(`evaluate() reported NOT ok for a clean build: ${clean.problems.join(" | ")}`);
  }
  // evaluate(): one warning is one red, and the summary agrees with the line parse.
  const dirty = evaluate({
    exitCode: 0,
    output: `  A -> C:\\out\\A.dll\n${SELF_TEST_POSITIVES[1]}\n\nBuild succeeded.\n${SELF_TEST_POSITIVES[1]}\n    1 Warning(s)\n    0 Error(s)\n`,
    slnText: sln,
  });
  if (dirty.ok || dirty.warnings.length !== 1) {
    failures.push(`evaluate() did not redden on a single xUnit2013 warning: ${JSON.stringify(dirty.problems)}`);
  }
  // evaluate(): readings that disagree are a failure, not a reconciliation.
  const disagreeing = evaluate({
    exitCode: 0,
    output: "  A -> C:\\out\\A.dll\n\nBuild succeeded.\n    3 Warning(s)\n    0 Error(s)\n",
    slnText: sln,
  });
  if (disagreeing.ok) {
    failures.push("evaluate() reported OK when the summary and the diagnostic-line parse disagreed");
  }

  // THE ZERO-BYTE-OUTPUT SWEEP, both directions, over an INJECTED filesystem so no build is
  // needed. The corpus is the real incident's tree shape: a healthy output folder, and the same
  // folder with CcpClient.HeadlessTests.deps.json at zero bytes.
  const OUT = "C:\\repo\\client\\tests\\CcpClient.HeadlessTests\\bin\\Debug\\net10.0";
  const treeFiles = [
    `${OUT}\\CcpClient.HeadlessTests.dll`,
    `${OUT}\\CcpClient.HeadlessTests.exe`,
    `${OUT}\\CcpClient.HeadlessTests.deps.json`,
    `${OUT}\\CcpClient.HeadlessTests.runtimeconfig.json`,
    `${OUT}\\CcpClient.HeadlessTests.pdb`,
    `${OUT}\\runtimes\\win-x64\\native\\libSkiaSharp.dll`,
    `${OUT}\\xunit.runner.json`,
  ];
  const io = (zeroByte) => ({
    list: () => treeFiles,
    size: (f) => (zeroByte.includes(f) ? 0 : 4096),
  });
  const healthy = findTruncatedOutputs([`${OUT}\\CcpClient.HeadlessTests.dll`], io([]));
  if (healthy.truncated.length !== 0) {
    failures.push(`the sweep named an offender in a healthy tree: ${healthy.truncated.join(", ")}`);
  }
  // Five of the seven are of a never-empty shape; the .pdb and the .json config are not this
  // check's business, and a sweep that weighed all seven would be claiming a rule it does not have.
  if (healthy.scanned !== 5) {
    failures.push(`the sweep weighed ${healthy.scanned} file(s) of the corpus, expected the 5 never-empty shapes`);
  }
  const broken = findTruncatedOutputs(
    [`${OUT}\\CcpClient.HeadlessTests.dll`],
    io([`${OUT}\\CcpClient.HeadlessTests.deps.json`]));
  if (broken.truncated.length !== 1 || !broken.truncated[0].endsWith("CcpClient.HeadlessTests.deps.json")) {
    failures.push(`the sweep did not name the zero-byte deps.json: ${JSON.stringify(broken.truncated)}`);
  }
  // A zero-byte NATIVE one directory down is the same defect, and the sweep is recursive.
  const brokenNative = findTruncatedOutputs(
    [`${OUT}\\CcpClient.HeadlessTests.dll`],
    io([`${OUT}\\runtimes\\win-x64\\native\\libSkiaSharp.dll`]));
  if (brokenNative.truncated.length !== 1) {
    failures.push(`the sweep missed a zero-byte native under runtimes/: ${JSON.stringify(brokenNative.truncated)}`);
  }
  // evaluate(): a clean build over a CORRUPT tree is a failure — the whole point of the incident.
  const corruptTree = evaluate({
    exitCode: 0,
    output: "  A -> C:\\out\\A.dll\n\nBuild succeeded.\n    0 Warning(s)\n    0 Error(s)\n",
    slnText: sln,
    outputSweep: { truncated: [`${OUT}\\CcpClient.HeadlessTests.deps.json`], scanned: 5 },
  });
  if (corruptTree.ok) {
    failures.push("evaluate() reported OK for a 0/0 build over a tree with a zero-byte deps.json in it");
  }
  // ...and a sweep that weighed nothing is not a clean sweep.
  const blindSweep = evaluate({
    exitCode: 0,
    output: "  A -> C:\\out\\A.dll\n\nBuild succeeded.\n    0 Warning(s)\n    0 Error(s)\n",
    slnText: sln,
    outputSweep: { truncated: [], scanned: 0 },
  });
  if (blindSweep.ok) {
    failures.push("evaluate() reported OK for an output sweep that examined zero files");
  }
  // ...and the healthy sweep does NOT redden a clean build, or the gate would only ever fire.
  const cleanTree = evaluate({
    exitCode: 0,
    output: "  A -> C:\\out\\A.dll\n\nBuild succeeded.\n    0 Warning(s)\n    0 Error(s)\n",
    slnText: sln,
    outputSweep: { truncated: [], scanned: 5 },
  });
  if (!cleanTree.ok) {
    failures.push(`evaluate() reddened a clean build over a healthy tree: ${cleanTree.problems.join(" | ")}`);
  }
  // The output path the sweep depends on really is parsed off MSBuild's own line.
  const parsedOutputs = parseProjectOutputs(
    "  CcpClient.Tests -> C:\\repo\\client\\tests\\CcpClient.Tests\\bin\\Debug\\net10.0\\CcpClient.Tests.dll\n");
  if (parsedOutputs.get("CcpClient.Tests") !==
      "C:\\repo\\client\\tests\\CcpClient.Tests\\bin\\Debug\\net10.0\\CcpClient.Tests.dll") {
    failures.push(`the project output path was not parsed off the build line: ${JSON.stringify([...parsedOutputs])}`);
  }

  if (failures.length > 0) {
    log("WARNING GATE SELF-TEST FAILED:");
    for (const f of failures) log(`  ${f}`);
    return 1;
  }
  log(`WARNING GATE SELF-TEST OK: ${SELF_TEST_POSITIVES.length} positive, ` +
    `${SELF_TEST_NEGATIVES.length} negative corpus lines, plus dedup, summary, ` +
    "fail-closed, retired-filter and zero-byte-output exhibits.");
  return 0;
}

// ---------------------------------------------------------------------------
// The gate.
// ---------------------------------------------------------------------------

function runBuild(cold) {
  const args = cold ? [...BUILD_ARGS, ...COLD_ARGS] : BUILD_ARGS;
  return new Promise((resolve) => {
    const child = spawn("dotnet", args, {
      cwd: CLIENT,
      shell: false,
      stdio: ["ignore", "pipe", "pipe"],
      env: { ...process.env, DOTNET_CLI_UI_LANGUAGE: "en" },
    });
    let output = "";
    child.stdout.on("data", (d) => { output += d.toString(); });
    child.stderr.on("data", (d) => { output += d.toString(); });
    child.on("error", (err) => resolve({ exitCode: 127, output: `${output}\nspawn failed: ${err.message}` }));
    child.on("close", (code) => resolve({ exitCode: code ?? 1, output }));
  });
}

export async function main(argv = []) {
  if (argv.includes("--self-test")) {
    return runSelfTest();
  }

  let slnText;
  try {
    slnText = fs.readFileSync(SLN_PATH, "utf8");
  } catch {
    console.error(`WARNING GATE FAILED:\n  solution not found at ${SLN_PATH}`);
    return 1;
  }

  const logDirIndex = argv.indexOf("--log-dir");
  const logDir = logDirIndex >= 0 && argv[logDirIndex + 1]
    ? path.resolve(argv[logDirIndex + 1])
    : fs.mkdtempSync(path.join(os.tmpdir(), "ccp-warn-"));
  fs.mkdirSync(logDir, { recursive: true });
  const logPath = path.join(logDir, "build.log");

  const cold = argv.includes("--cold");
  const shown = cold ? [...BUILD_ARGS, ...COLD_ARGS] : BUILD_ARGS;
  console.log(`warning gate: dotnet ${shown.map((a) => (a.includes(" ") ? `"${a}"` : a)).join(" ")}`);
  const { exitCode, output } = await runBuild(cold);
  // The COMPLETE, unfiltered stream, on disk, before anything is read out of it.
  fs.writeFileSync(logPath, output, "utf8");

  // Read the TREE the build left, from the output paths MSBuild itself printed.
  const outputSweep = findTruncatedOutputs([...parseProjectOutputs(output).values()]);

  const verdict = evaluate({ exitCode, output, slnText, outputSweep });

  if (!verdict.ok) {
    console.error("WARNING GATE FAILED:");
    for (const p of verdict.problems) {
      console.error(`  ${p}`);
    }
    console.error(`  full unfiltered build log: ${logPath}`);
    return 1;
  }

  console.log(
    `WARNING GATE OK: 0 warnings, 0 errors across ${verdict.builtProjects.length} project(s) ` +
    `[${verdict.builtProjects.join(", ")}] in ${CONFIGURATION}, forced non-incremental; ` +
    `${outputSweep.scanned} generated output(s) weighed, 0 of them zero-byte.`
  );
  if (verdict.restoreNoOp) {
    console.log("  note: NuGet restore was a no-op this run, so restore-time (NU*) warnings were not re-evaluated." +
      (cold ? "" : " Re-run with --cold when the diff touches a *.csproj, *.props, *.targets or a lock file."));
  }
  console.log(`  full unfiltered build log: ${logPath}`);
  return 0;
}

if (process.argv[1] && import.meta.url === pathToFileURL(path.resolve(process.argv[1])).href) {
  main(process.argv.slice(2)).then((code) => process.exit(code));
}
