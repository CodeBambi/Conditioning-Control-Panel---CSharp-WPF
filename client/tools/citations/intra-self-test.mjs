#!/usr/bin/env node
// intra-self-test.mjs — the facts that bind intra.mjs.
//
// RUN IT
//   node client/tools/citations/intra-self-test.mjs
// Exit 0 = every fact holds. Exit 1 = at least one fact failed, and node:test names it.
//
// HOW IT REACHES THE .NET FLOOR
// check-floor.mjs discovers only csproj entries under client/tests/, so a node file under
// client/tools/ is invisible to it. client/tests/CcpClient.Tests/IntraCitationTests.cs spawns
//   node --test-reporter=tap client/tools/citations/intra-self-test.mjs
// and reds the suite on a failing fact, anchoring each fact by its `In:` ID PREFIX — so renaming
// or deleting an ID reds on purpose. Precedent and mechanism: CitationSelfTestGateTests.cs, which
// does the same for detect.mjs's self-test.
//
// THE MUTATION FACTS, AND WHY HALF THIS FILE IS THEM
// A REFUSAL is a negative claim — "this shape produces no row" — and a fact asserting a negative
// stays green when the refusal it describes is DELETED, as long as nothing else in the fixture
// reaches it. The review of the sibling tool blocked on exactly that: three named refusals could
// be removed with the suite still green. So every refusal in intra.mjs is bound by a PAIR:
//   * the fact itself, asserting the refusal holds on a fixture that REACHES it; and
//   * a MUTATION, which rewrites one uniquely-occurring span of intra.mjs's own source, imports the
//     mutant, and asserts the fixture now produces the row the refusal was suppressing.
// withMutant() asserts the target span occurs EXACTLY ONCE before mutating, so a mutation that has
// drifted out of the source fails loudly instead of testing nothing. The mutant is written to
// os.tmpdir() with its `./detect.mjs` import rewritten to an absolute file URL, so no byte of the
// repository is written by this file.
//
// FIXTURES ARE TEMP-DIR REPOSITORIES, NEVER TODAY'S REAL TREE
// A self-test pinned to today's 673 intra-client references goes red the day someone writes a
// citation, which is the day the detector is most needed. Every fact below builds its own tiny
// repository under os.tmpdir(). Nothing here reads the real client tree or the real WPF tree.
// Unlike detect.mjs's fixtures these need NO git: intra.mjs runs no subprocess and reads no
// history, so a fixture is a handful of files and nothing else.
//
// CONCURRENCY
// Up to eight lanes share os.tmpdir(). Each fixture directory is ccp-intra-<randomUUID> (distinct
// prefix so a stray directory is attributable) and is removed in a finally.
//
// PORTABILITY / HOUSE RULES
// node:test + node:assert/strict are CORE modules, so the zero-npm-dependency rule holds. There is
// no wall-clock wait, no sleep and no retry loop anywhere in this file.

import assert from "node:assert/strict";
import crypto from "node:crypto";
import { execFileSync } from "node:child_process";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import test from "node:test";
import { fileURLToPath, pathToFileURL } from "node:url";

import {
  CLASS,
  REASON,
  formatIntraReport,
  normaliseProse,
  quotePresent,
  runIntraDetector,
  symbolBelongsToFile,
} from "./intra.mjs";

const INTRA = fileURLToPath(new URL("./intra.mjs", import.meta.url));

// ------------------------------------------------------------------ fixture repo

function withFixture(body) {
  const root = path.join(os.tmpdir(), `ccp-intra-${crypto.randomUUID()}`);
  fs.mkdirSync(root, { recursive: true });
  try {
    return body(makeFixture(root));
  } finally {
    fs.rmSync(root, { recursive: true, force: true });
  }
}

function makeFixture(root) {
  const fx = {
    root,
    /** Writes a repo-relative, forward-slashed path, creating parents. */
    write(rel, text) {
      const full = path.join(root, ...rel.split("/"));
      fs.mkdirSync(path.dirname(full), { recursive: true });
      fs.writeFileSync(full, text, "utf8");
      return fx;
    },
    /** The repo-root anchor findRepoRoot() walks up to, plus the two citation-fixture sources
     *  runIntraDetector refuses to run without. Every fixture starts here. */
    base() {
      fx.write("client/CcpClient.sln", "");
      fx.write("client/tools/citations/self-test.mjs", "// fixture stand-in\n");
      fx.write("client/tools/citations/intra-self-test.mjs", "// fixture stand-in\n");
      // The decision ledger LEDGER_DOCUMENTS names. Present in every fixture for the same reason
      // the two fixture sources are: runIntraDetector refuses to run when a named exclusion points
      // at nothing, and I28b is the fact that pins that refusal.
      fx.write("client/docs/task-board.md", "# Board\n\nNo rows.\n");
      return fx;
    },
    run() {
      return runIntraDetector({ repoRoot: root });
    },
    /** Runs the CLI as a real child process with cwd inside the fixture. */
    cli(args = [], cwd = root) {
      try {
        const stdout = execFileSync("node", [INTRA, ...args], {
          cwd,
          encoding: "utf8",
          stdio: ["ignore", "pipe", "pipe"],
        });
        return { status: 0, stdout, stderr: "" };
      } catch (err) {
        return {
          status: typeof err.status === "number" ? err.status : 1,
          stdout: typeof err.stdout === "string" ? err.stdout : "",
          stderr: typeof err.stderr === "string" ? err.stderr : String(err.message ?? err),
        };
      }
    },
  };
  return fx;
}

const rowsOf = (outcome, cls) => outcome.rows.filter((r) => r.cls === cls);
const citedOf = (outcome, cls) => rowsOf(outcome, cls).map((r) => r.cited);

/** Imports a COPY of intra.mjs with one uniquely-occurring span rewritten, so a refusal can be
 *  shown to be load-bearing rather than merely written. Asserts the span occurs exactly once
 *  BEFORE mutating: a mutation whose target has drifted out of the source tests nothing, and
 *  passing silently is the failure this whole mechanism exists to prevent. */
async function withMutant(find, replace, body) {
  // LINE ENDINGS ARE NORMALISED BEFORE MATCHING, and that is not tidying. The mutation targets are
  // multi-line template literals in THIS file, while intra.mjs is checked out CRLF on Windows and LF
  // on Linux - and this suite now runs on both. A literal match finds ZERO occurrences on whichever
  // side disagrees; because the guard below asserts EXACTLY ONE, that surfaces as a loud refusal
  // rather than as a mutation that quietly tested nothing. Normalising preserves the guard's meaning
  // (the span must be unique) while making it say the same thing on either checkout.
  const source = fs.readFileSync(INTRA, "utf8").replace(/\r\n/g, "\n");
  const target = find.replace(/\r\n/g, "\n");
  const swap = replace.replace(/\r\n/g, "\n");
  const occurrences = source.split(target).length - 1;
  assert.equal(occurrences, 1, `the mutation target must occur exactly once in intra.mjs, found ${occurrences}: ${target}`);
  const detect = pathToFileURL(path.join(path.dirname(INTRA), "detect.mjs")).href;
  const mutated = source
    .replace(target, swap)
    .replace('} from "./detect.mjs";', `} from ${JSON.stringify(detect)};`);
  assert.ok(mutated.includes(detect), "the mutant must import the REAL detect.mjs by absolute URL");
  const file = path.join(os.tmpdir(), `ccp-intra-mutant-${crypto.randomUUID()}.mjs`);
  fs.writeFileSync(file, mutated, "utf8");
  try {
    return await body(await import(pathToFileURL(file).href));
  } finally {
    fs.rmSync(file, { force: true });
  }
}

// ================================================== I1-I3 the checks that produce rows

test("I1: a citation whose file and line both exist produces NO row — silence on the happy path", () => {
  withFixture((fx) => {
    fx.base();
    fx.write("client/src/CcpClient.Desktop/Thing.cs", "a\nb\nc\nd\n");
    fx.write("client/docs/notes.md", "The behaviour lives at `Thing.cs:3`.\n");
    const outcome = fx.run();
    assert.deepEqual(outcome.rows, [], "a live citation is not a finding");
    assert.equal(outcome.summary.counts.intra, 1);
    assert.equal(outcome.summary.counts.resolved, 1);
  });
});

test("I2: a span past the file's last line is WRONG-LINE, names the line count, and the LAST line itself is not", () => {
  withFixture((fx) => {
    fx.base();
    // Four real lines and a trailing newline. split("\n") yields five elements; the last is empty.
    fx.write("client/src/CcpClient.Desktop/Thing.cs", "a\nb\nc\nd\n");
    fx.write("client/docs/notes.md", "Past the end: `Thing.cs:5`.\nExactly the last line: `Thing.cs:4`.\n");
    const outcome = fx.run();
    const rows = rowsOf(outcome, CLASS.WRONG_LINE);
    assert.equal(rows.length, 1, "the trailing newline must not make line 5 look real");
    assert.equal(rows[0].at, "client/docs/notes.md:1", "the row names WHERE the bad citation is");
    assert.match(rows[0].reason, new RegExp(`${REASON.PAST_END} \\(4 lines\\)`));
    assert.match(formatIntraReport(outcome), /client\/docs\/notes\.md:1: cited .*Thing\.cs:5 but past the end/);
  });
});

test("I3: a symbol beside a citation is checked against the cited line, and the row prints what the line READS", () => {
  withFixture((fx) => {
    fx.base();
    fx.write("client/src/CcpClient.Desktop/Sink.cs", "class Sink {\n  int Admitted;\n  int Other;\n}\n");
    fx.write(
      "client/docs/notes.md",
      ["Right: `Sink.cs:2`, `Admitted`.", "Wrong: `Sink.cs:3`, `Admitted`.", ""].join("\n"),
    );
    const outcome = fx.run();
    const rows = rowsOf(outcome, CLASS.WRONG_LINE);
    assert.equal(rows.length, 1, "the citation whose line DOES carry the symbol must stay silent");
    assert.equal(rows[0].reason, REASON.SYMBOL_ABSENT);
    assert.equal(rows[0].reads, "  int Other;");
    assert.equal(outcome.summary.counts.symbolChecked, 2, "both citations carry a symbol; only one is wrong");
    // The board row's own words: a failure must be actionable without opening anything.
    assert.match(
      formatIntraReport(outcome),
      /client\/docs\/notes\.md:2: cited client\/src\/CcpClient\.Desktop\/Sink\.cs:3 as `Admitted` but the line reads "int Other;"/,
    );
  });
});

// ============================================ I4 UNRESOLVABLE, and the attribution refusal

test("I4: UNRESOLVABLE fires only when the citation's own prefix says client/ — an unattributed name is COUNTED", () => {
  withFixture((fx) => {
    fx.base();
    fx.write(
      "client/docs/notes.md",
      ["A port claim: `client/src/CcpClient.Desktop/Gone.cs:12`.", "A nameless one: `Speech.cs:148`.", ""].join("\n"),
    );
    const outcome = fx.run();
    assert.deepEqual(citedOf(outcome, CLASS.UNRESOLVABLE), ["client/src/CcpClient.Desktop/Gone.cs:12"]);
    assert.equal(outcome.rows[0].reason, REASON.NO_SUCH_FILE);
    assert.equal(outcome.summary.counts.unattributed, 1, "a name that resolves nowhere and names no tree is a COUNTER");
    assert.deepEqual(outcome.summary.unattributedNames, ["Speech.cs"], "and the counter NAMES it rather than hiding it");
    assert.match(formatIntraReport(outcome), /resolving NOWHERE and naming no tree: 1 across 1 distinct name/);
  });
});

// ============================================ I5 / I6 AMBIGUOUS-BASENAME, both shapes

test("I5: two client files sharing a basename produce ONE row naming both candidates with their line counts, and pick neither", () => {
  withFixture((fx) => {
    fx.base();
    fx.write("client/src/CcpClient.Desktop/Views/Page.axaml", "a\nb\n");
    fx.write("client/tools/verify/Page.axaml", "a\nb\nc\n");
    fx.write("client/docs/notes.md", "See `Page.axaml:2`.\n");
    const outcome = fx.run();
    const rows = rowsOf(outcome, CLASS.AMBIGUOUS);
    assert.equal(rows.length, 1);
    assert.equal(rows[0].reason, REASON.MANY_IN_CLIENT);
    assert.deepEqual(rows[0].candidates, [
      { path: "client/src/CcpClient.Desktop/Views/Page.axaml", lines: 2 },
      { path: "client/tools/verify/Page.axaml", lines: 3 },
    ]);
    assert.match(formatIntraReport(outcome), /candidate: client\/tools\/verify\/Page\.axaml \(3 lines\)/);
  });
});

test("I6: a basename living in BOTH trees, cited bare, is AMBIGUOUS — and the same name with a real prefix is not", () => {
  withFixture((fx) => {
    fx.base();
    fx.write("ConditioningControlPanel/Helpers/Ramp.cs", "a\nb\nc\nd\ne\n");
    fx.write("client/src/CcpClient.Desktop/Effects/Ramp.cs", "a\nb\n");
    fx.write("client/docs/notes.md", "Bare: `Ramp.cs:5`.\nQualified: `Helpers/Ramp.cs:5`.\n");
    const outcome = fx.run();
    const rows = rowsOf(outcome, CLASS.AMBIGUOUS);
    assert.equal(rows.length, 1, "the qualified citation chose the WPF tree and left this tool's territory");
    assert.equal(rows[0].reason, REASON.BOTH_TREES);
    assert.deepEqual(
      rows[0].candidates.map((c) => c.path),
      ["ConditioningControlPanel/Helpers/Ramp.cs", "client/src/CcpClient.Desktop/Effects/Ramp.cs"],
    );
    assert.equal(
      rowsOf(outcome, CLASS.WRONG_LINE).length,
      0,
      "the qualified one names a 5-line WPF file; resolving it against the 2-line client file would fire WRONG-LINE",
    );
  });
});

// ==================================== I7 the direction refusal, with its mutation

const upstreamOnlyFixture = (fx) => {
  fx.base();
  fx.write("ConditioningControlPanel/Services/Upstream.cs", "a\nb\n");
  fx.write("client/docs/notes.md", "Parity: `Upstream.cs:900`.\n");
};

test("I7: an upstream-only basename is NEVER reported — that is detect.mjs's territory", () => {
  withFixture((fx) => {
    upstreamOnlyFixture(fx);
    const outcome = fx.run();
    assert.deepEqual(outcome.rows, [], "reporting upstream citations here would duplicate the other tool's rows");
    assert.equal(outcome.summary.counts.outsideClient, 1, "and it is a COUNTER, so the silence is visible");
    assert.equal(outcome.summary.counts.intra, 0);
  });
});

test("I7m: MUTATION — deleting the outside-client skip turns that same citation into a row", async () => {
  await withMutant(
    `      if (inClient.length === 0) {
        // OUTSIDE-CLIENT: detect.mjs's territory. NEVER a row here.
        counts.outsideClient += 1;
        continue;
      }`,
    `      if (inClient.length === 0) {
        counts.outsideClient += 1;
        rows.push({ cls: CLASS.UNRESOLVABLE, at, cited: basename, reason: "mutant", reads: null, action: "mutant" });
        continue;
      }`,
    async (mutant) =>
      withFixture((fx) => {
        upstreamOnlyFixture(fx);
        const outcome = mutant.runIntraDetector({ repoRoot: fx.root });
        assert.equal(outcome.rows.length, 1, "the fixture REACHES the refusal, so removing it must produce a row");
      }),
  );
});

// ============================ I8 the author's word on direction, with its mutation

const namedTreeFixture = (fx) => {
  fx.base();
  fx.write("ConditioningControlPanel/Services/Other.cs", "a\n");
  fx.write("client/src/CcpClient.Desktop/App.axaml.cs", "a\nb\n");
  // The FIRST-ATTEMPT TREE IS ABSENT, exactly as it is in this checkout: the citation names it, the
  // path is not there, and the port's own two-line App.axaml.cs must not be range-checked instead.
  fx.write("client/docs/notes.md", "First attempt: `CCP.Avalonia.Desktop.Windows/App.axaml.cs:45-220`.\n");
};

test("I8: a prefix naming the WPF or first-attempt tree leaves this tool's territory even when that path is absent", () => {
  withFixture((fx) => {
    namedTreeFixture(fx);
    const outcome = fx.run();
    assert.deepEqual(outcome.rows, [], "the author said which tree; a 2-line client file is not what was cited");
    assert.equal(outcome.summary.counts.outsideClient, 1);
  });
});

test("I8m: MUTATION — dropping the named-tree refusal range-checks the port's own file and fires WRONG-LINE", async () => {
  await withMutant(
    `      if (namesOtherTree(prefix)) {
        counts.outsideClient += 1;
        continue;
      }`,
    "      // refusal removed by mutation",
    async (mutant) =>
      withFixture((fx) => {
        namedTreeFixture(fx);
        const outcome = mutant.runIntraDetector({ repoRoot: fx.root });
        assert.equal(rowsOf(outcome, CLASS.WRONG_LINE).length, 1, "without the refusal the wrong file is checked");
      }),
  );
});

// ============================== I9 the symbol-ownership refusal, with its mutation

const foreignSymbolFixture = (fx) => {
  fx.base();
  fx.write("client/src/CcpClient.Desktop/Effect.cs", "class Effect {\n  void Disarm() { }\n}\n");
  // The real shape that produced two false rows before this rule existed: the quoted identifier
  // after the citation is the subject of the NEXT clause, and names a different type.
  fx.write("client/docs/notes.md", "Reached from `Effect.cs:2`, `SessionEngine.Stop` disarms every module.\n");
};

test("I9: a dotted neighbour naming a DIFFERENT type is not a symbol for this citation", () => {
  withFixture((fx) => {
    foreignSymbolFixture(fx);
    const outcome = fx.run();
    assert.deepEqual(outcome.rows, [], "a neighbouring clause's subject says nothing about the cited line");
    assert.equal(outcome.summary.counts.symbolChecked, 0);
    assert.equal(outcome.summary.counts.bareCitations, 1, "it is counted as a bare citation, which is what it is");
    // The other half of the same rule, asserted directly so the boundary is legible.
    assert.equal(symbolBelongsToFile("Effect.Disarm", "Effect.cs"), true);
    assert.equal(symbolBelongsToFile("Disarm", "Effect.cs"), true, "a bare member belongs to whatever was cited");
    assert.equal(symbolBelongsToFile("SessionEngine.Stop", "Effect.cs"), false);
    assert.equal(symbolBelongsToFile("Window.Show", "Window.axaml.cs"), true, "the stem stops at the FIRST dot");
  });
});

test("I9m: MUTATION — accepting any dotted neighbour puts the false WRONG-LINE row back", async () => {
  await withMutant(
    "(NEIGHBOUR_IS_A_FILENAME.test(named) || !symbolBelongsToFile(named, basename))",
    "NEIGHBOUR_IS_A_FILENAME.test(named)",
    async (mutant) =>
      withFixture((fx) => {
        foreignSymbolFixture(fx);
        const outcome = mutant.runIntraDetector({ repoRoot: fx.root });
        assert.equal(rowsOf(outcome, CLASS.WRONG_LINE).length, 1, "this is the exact false row the rule removed");
      }),
  );
});

// ================================================================ I10-I12 anchors

test("I10: a §N or Dnnn anchor into a document that no longer carries it is DEAD-ANCHOR, across a wrapped comment", () => {
  withFixture((fx) => {
    fx.base();
    fx.write("client/docs/register.md", "# Register\n\n## Purpose\n\nNothing numbered here.\n");
    fx.write(
      "client/src/CcpClient.Desktop/Gate.cs",
      ["/// The refusal is recorded at (<c>register.md</c>", "/// D24).", "class Gate { }", ""].join("\n"),
    );
    const outcome = fx.run();
    const rows = rowsOf(outcome, CLASS.DEAD_ANCHOR);
    assert.equal(rows.length, 1, "the anchor sits on the NEXT line of a wrapped XML doc comment and must still bind");
    assert.equal(rows[0].cited, "client/docs/register.md D24");
    assert.equal(rows[0].reason, REASON.ANCHOR_GONE);
    // Without this class the citation reads as fine: the DOCUMENT still exists.
    assert.equal(rowsOf(outcome, CLASS.UNRESOLVABLE).length, 0);
    assert.equal(rowsOf(outcome, CLASS.WRONG_LINE).length, 0);
  });
});

const proseAnchorFixture = (fx) => {
  fx.base();
  fx.write("client/docs/register.md", "# Register\n\n## Purpose\n\nNothing numbered here.\n");
  // A `Dnnn` separated from the document name by PROSE belongs to a different sentence.
  fx.write("client/src/CcpClient.Desktop/Gate.cs", "// see register.md and the reasoning recorded at D24\n");
};

test("I11: an anchor separated from the document name by PROSE is not bound to it", () => {
  withFixture((fx) => {
    proseAnchorFixture(fx);
    const outcome = fx.run();
    assert.deepEqual(outcome.rows, [], "only markup and punctuation may sit between a document and its anchor");
    assert.equal(outcome.summary.counts.anchorsFound, 0);
  });
});

test("I11m: MUTATION — a lead that admits prose binds the distant anchor and invents a row", async () => {
  await withMutant(
    "const ANCHOR_LEAD = /^(?:<\\/c>|<c>|<\\/?i>|<\\/?b>|\\/\\/\\/|\\/\\/|[`*_,;:()[\\]\\s])*/;",
    "const ANCHOR_LEAD = /^[^\\u00a7D]*/;",
    async (mutant) =>
      withFixture((fx) => {
        proseAnchorFixture(fx);
        const outcome = mutant.runIntraDetector({ repoRoot: fx.root });
        assert.equal(rowsOf(outcome, CLASS.DEAD_ANCHOR).length, 1, "the loose lead reaches across the prose");
      }),
  );
});

test("I12: §N.M resolves against a numbered RULE inside section N, and is dead only when that rule is absent", () => {
  withFixture((fx) => {
    fx.base();
    fx.write(
      "client/docs/contract.md",
      [
        "# Contract",
        "",
        "## 4. Composition-root validation rules",
        "",
        "1. One place.",
        "2. Validate in phase 2.",
        "3. An explicit checklist.",
        "4. Constructors are cheap.",
        "",
        "## 5. Ownership",
        "",
        "1. Only one.",
        "",
      ].join("\n"),
    );
    fx.write(
      "client/src/CcpClient.Desktop/Part.cs",
      [
        "// live section: contract.md §4",
        "// live rule inside it: contract.md §4.4",
        "// section 5 has no rule 4: contract.md §5.4",
        "",
      ].join("\n"),
    );
    const outcome = fx.run();
    assert.deepEqual(
      rowsOf(outcome, CLASS.DEAD_ANCHOR).map((r) => r.cited),
      ["client/docs/contract.md §5.4"],
      "§4 and §4.4 are both live; only §5.4 names a rule that is not in section 5",
    );
    assert.equal(outcome.summary.counts.anchorsChecked, 3);
  });
});

// ================================= I13 / I14 the historical marker and its shape refusal

const historicalFixture = (fx) => {
  fx.base();
  fx.write("client/docs/register.md", "# Register\n\nNothing numbered here.\n");
  fx.write("client/src/CcpClient.Desktop/Thing.cs", "a\nb\n");
  fx.write(
    "client/docs/notes.md",
    [
      "Recorded then: `register.md` D24 @ 7527243e7.",
      "Measured then: `client/src/CcpClient.Desktop/Thing.cs:900` @ 7527243e7.",
      "",
    ].join("\n"),
  );
};

test("I13: a reference or anchor marked `@ <sha>` is a claim about a COMMIT — counted, never checked", () => {
  withFixture((fx) => {
    historicalFixture(fx);
    const outcome = fx.run();
    assert.deepEqual(outcome.rows, [], "the working tree cannot answer a question asked of a named commit");
    assert.equal(outcome.summary.counts.historical, 2, "one reference and one anchor");
    assert.match(formatIntraReport(outcome), /marked HISTORICAL \(`X @ <sha>`\): 2/);
  });
});

test("I13m: MUTATION — removing the marker check turns both back into rows", async () => {
  await withMutant(
    `      if (marked) {
        counts.historical += 1;
        continue;
      }`,
    "      // marker check removed by mutation",
    async (mutant) =>
      withFixture((fx) => {
        historicalFixture(fx);
        const outcome = mutant.runIntraDetector({ repoRoot: fx.root });
        assert.equal(rowsOf(outcome, CLASS.WRONG_LINE).length, 1, "the marked reference is a rotted one underneath");
      }),
  );
});

test("I14: the marker must be a git object name — an @ followed by a word suppresses NOTHING", () => {
  withFixture((fx) => {
    fx.base();
    fx.write("client/src/CcpClient.Desktop/Thing.cs", "a\nb\n");
    fx.write(
      "client/docs/notes.md",
      [
        "Not a sha: `client/src/CcpClient.Desktop/Thing.cs:900` @ sometime.",
        "Too short: `client/src/CcpClient.Desktop/Thing.cs:901` @ abc123.",
        "An email: `client/src/CcpClient.Desktop/Thing.cs:902` @example.invalid.",
        "",
      ].join("\n"),
    );
    const outcome = fx.run();
    assert.equal(rowsOf(outcome, CLASS.WRONG_LINE).length, 3, "the escape hatch is shaped so it cannot be used casually");
    assert.equal(outcome.summary.counts.historical, 0);
  });
});

// ============================ I15 the citation-fixture exclusion, and its refusal to run

test("I15: the two citation-fixture sources are read for a COUNT and never for rows", () => {
  withFixture((fx) => {
    fx.base();
    fx.write("client/tools/citations/self-test.mjs", 'fx.write("Q.cs", x); // cites Q.cs:900-902 and Dup.cs:77\n');
    fx.write("client/docs/notes.md", "A real one: `client/src/CcpClient.Desktop/Gone.cs:1`.\n");
    const outcome = fx.run();
    assert.deepEqual(citedOf(outcome, CLASS.UNRESOLVABLE), ["client/src/CcpClient.Desktop/Gone.cs:1"]);
    assert.equal(outcome.summary.counts.fixtureReferencesHidden, 2, "Q.cs:900-902 and Dup.cs:77");
    assert.equal(outcome.summary.counts.fixtureFilesSkipped, 2);
    assert.match(formatIntraReport(outcome), /hidden by the citation-fixture exclusion: 2 references in 2 file\(s\)/);
  });
});

test("I15b: a fixture-source path that does not exist is a COULD-NOT-RUN, never a silent widening", () => {
  withFixture((fx) => {
    fx.base();
    fs.rmSync(path.join(fx.root, "client", "tools", "citations", "self-test.mjs"));
    const run = fx.cli();
    assert.equal(run.status, 2, "an exclusion pointing at nothing has stopped excluding, and that is not a rot verdict");
    assert.match(run.stderr, /self-test\.mjs/, "the failure must name the path it could not find");
    assert.match(run.stderr, /COULD NOT RUN HONESTLY/);
    assert.equal(run.stdout, "", "no report on a broken input, ever");
  });
});

// =============================================================== I16 the exit contract

test("I16: exit 0 clean, exit 1 on rot, exit 2 with no report when the root cannot be found", () => {
  withFixture((fx) => {
    fx.base();
    fx.write("client/src/CcpClient.Desktop/Thing.cs", "a\nb\n");
    fx.write("client/docs/notes.md", "Fine: `Thing.cs:2`.\n");

    const clean = fx.cli();
    assert.equal(clean.status, 0, `a clean tree must exit 0; stderr was: ${clean.stderr}`);
    assert.match(clean.stdout, /TOTAL ROWS: 0/);
    assert.match(clean.stdout, /No intra-client citation rot\. Exit 0\./);

    fx.write("client/docs/notes.md", "Fine: `Thing.cs:2`. Rotten: `Thing.cs:900`.\n");
    const rotten = fx.cli();
    assert.equal(rotten.status, 1, "rot is a FAILURE here, unlike the upstream review list");
    assert.match(rotten.stdout, /TOTAL ROWS: 1/);

    // Exit 2 is separate from 1 so a broken detector never reads as a clean tree, or as a dirty one.
    // The directory must sit OUTSIDE the fixture: findRepoRoot walks UP, and the fixture's own root
    // carries the client/CcpClient.sln anchor, so `<fixture>/nowhere` would find it and run.
    const rootless = path.join(os.tmpdir(), `ccp-intra-rootless-${crypto.randomUUID()}`);
    fs.mkdirSync(rootless, { recursive: true });
    const lost = fx.cli([], rootless);
    fs.rmSync(rootless, { recursive: true, force: true });
    assert.equal(lost.status, 2);
    assert.match(lost.stderr, /client\/CcpClient\.sln/, "the failure must name the ANCHOR it looked for");
    assert.equal(lost.stdout, "", "no report without a root");

    const unknown = fx.cli(["--nope"]);
    assert.equal(unknown.status, 2, "a misinvocation is a could-not-run, not a rot verdict");
  });
});

// ===================================== I17 the spikes exclusion, with its mutation

const spikeFixture = (fx) => {
  fx.base();
  fx.write("client/src/CcpClient.Desktop/Views/MainWindow.axaml", "a\nb\nc\n");
  fx.write("client/spikes/CcpSpike.WebView/MainWindow.axaml", "a\n");
  fx.write("client/docs/notes.md", "The rack is at `MainWindow.axaml:3`.\n");
};

test("I17: client/spikes is READ for citations but is not a citation TARGET", () => {
  withFixture((fx) => {
    spikeFixture(fx);
    // A citation written INSIDE a spike is still read, so the exclusion is about resolution only.
    fx.write("client/spikes/CcpSpike.WebView/Probe.cs", "// broken: client/src/CcpClient.Desktop/Nope.cs:1\n");
    const outcome = fx.run();
    assert.equal(rowsOf(outcome, CLASS.AMBIGUOUS).length, 0, "a throwaway spike window must not shadow the product's");
    assert.deepEqual(citedOf(outcome, CLASS.UNRESOLVABLE), ["client/src/CcpClient.Desktop/Nope.cs:1"]);
  });
});

test("I17m: MUTATION — putting spikes back in the resolution universe makes the product citation ambiguous", async () => {
  await withMutant(
    'const RESOLUTION_ROOTS = ["src", "tests", "docs", "tools"];',
    'const RESOLUTION_ROOTS = ["src", "tests", "docs", "tools", "spikes"];',
    async (mutant) =>
      withFixture((fx) => {
        spikeFixture(fx);
        const outcome = mutant.runIntraDetector({ repoRoot: fx.root });
        assert.equal(rowsOf(outcome, CLASS.AMBIGUOUS).length, 1, "this is the noise the exclusion removes");
      }),
  );
});

// ================================ I18 the upstream prose marker, with its mutation

const proseMarkerFixture = (fx) => {
  fx.base();
  fx.write("ConditioningControlPanel/Services/Hygiene.cs", "a\nb\nc\nd\ne\n");
  fx.write("client/src/CcpClient.Desktop/Ai/Hygiene.cs", "a\nb\n");
  fx.write(
    "client/docs/notes.md",
    [
      "Ported from WPF `Hygiene.cs:5`.",
      "Unlike upstream, the port's own `Hygiene.cs:5` is two lines.",
      "",
    ].join("\n"),
  );
};

test("I18: `WPF`/`upstream` immediately before a colliding citation sends it upstream; a loose mention does not", () => {
  withFixture((fx) => {
    proseMarkerFixture(fx);
    const outcome = fx.run();
    const rows = rowsOf(outcome, CLASS.AMBIGUOUS);
    assert.equal(rows.length, 1, "the marker must be the LAST word before the citation");
    assert.equal(rows[0].at, "client/docs/notes.md:2");
    assert.equal(outcome.summary.counts.upstreamByMarker, 1);
    assert.match(formatIntraReport(outcome), /sent upstream by the port's own `WPF`\/`upstream` marker: 1/);
  });
});

test("I18m: MUTATION — a loose marker swallows the sentence that merely mentions upstream", async () => {
  await withMutant(
    "const UPSTREAM_MARKER = /(?:\\bWPF|\\b[Uu]pstream)(?:'s)?(?:\\s|`|<c>|\\(|\\[)*$/;",
    "const UPSTREAM_MARKER = /(?:\\bWPF|\\b[Uu]pstream)/;",
    async (mutant) =>
      withFixture((fx) => {
        proseMarkerFixture(fx);
        const outcome = mutant.runIntraDetector({ repoRoot: fx.root });
        assert.equal(
          outcome.rows.length,
          0,
          "the loose form stops reporting the under-qualified citation entirely — a silent blind spot, not a noisy one",
        );
      }),
  );
});

// ============================================== I19 the shorthand-prefix fallback

test("I19: a written prefix that matches no real path falls back to the basename and is COUNTED, not reported", () => {
  withFixture((fx) => {
    fx.base();
    fx.write("client/src/CcpClient.Desktop/Ai/Awareness.cs", "a\nb\nc\n");
    // `client/Ai/` was never a real path; the port writes it as shorthand in several documents.
    fx.write("client/docs/notes.md", "Consent default at `client/Ai/Awareness.cs:2`.\n");
    const outcome = fx.run();
    assert.deepEqual(outcome.rows, [], "treating a shorthand prefix as a missing file was dozens of false rows");
    assert.equal(outcome.summary.counts.shorthandPrefixes, 1);
    assert.match(formatIntraReport(outcome), /shorthand path prefixes honoured as basenames: 1/);
  });
});

// ========================================== I20 the coverage block and row ordering

test("I20: every counter is PRINTED, and rows come out in a stable class-then-citer order", () => {
  withFixture((fx) => {
    fx.base();
    fx.write("client/docs/register.md", "# Register\n\nNothing numbered.\n");
    fx.write("client/src/CcpClient.Desktop/Thing.cs", "a\nb\n");
    fx.write("client/tools/verify/Thing.cs", "a\n");
    fx.write(
      "client/docs/notes.md",
      [
        "z rot: `client/src/CcpClient.Desktop/Missing.cs:1`.",
        "an anchor: `register.md` D9.",
        "ambiguous: `Thing.cs:1`.",
        "bare continuation (:12) and a comma one `Thing.cs:1,2`.",
        "",
      ].join("\n"),
    );
    const outcome = fx.run();
    assert.deepEqual(
      outcome.rows.map((r) => r.cls),
      [CLASS.DEAD_ANCHOR, CLASS.UNRESOLVABLE, CLASS.AMBIGUOUS, CLASS.AMBIGUOUS],
      "classes come out in report order regardless of the order the corpus produced them",
    );
    const report = formatIntraReport(outcome);
    for (const needle of [
      /corpus: \d+ files under client\/\{src,tests,docs,tools,spikes\}/,
      /direction: \d+ intra-client, \d+ outside client\//,
      /intra-client checked: \d+ resolved clean/,
      /document anchors: \d+ found/,
      /resolving NOWHERE and naming no tree: \d+/,
      /marked HISTORICAL/,
      /sent upstream by the port's own/,
      /shorthand path prefixes honoured as basenames: \d+/,
      /bare :NNN continuations and \d+ comma continuations/,
      /hidden by the citation-fixture exclusion/,
      /also not checked: whether a citation was correct the day it was written/,
    ]) {
      assert.match(report, needle, `the coverage block must print ${needle}`);
    }
    assert.equal(outcome.summary.counts.bareContinuations, 1, "(:12) is bare; every Foo.cs:1 is file-qualified");
    assert.equal(outcome.summary.counts.commaContinuations, 1, "the ,2 of Thing.cs:1,2");
  });
});

// ============================ I21-I28b the QUOTED needle: what it checks and what it refuses
//
// A REMINDER OF WHY THE REFUSALS OUTNUMBER THE ASSERTIONS HERE. A quoted-string check is the one
// addition to this detector that could plausibly fire on CORRECT prose, and a guard that cries wolf
// gets disabled rather than fixed. So the shape of this block mirrors the shape of the risk: two
// facts say what the check catches, and five pairs pin the five refusals that keep it from catching
// anything else. Every refusal was forced by a REAL false row measured on the corpus, and every
// mutation puts that exact row back.

test("I21: a quotation beside a citation is checked against the cited lines, and a stale one is WRONG-LINE", () => {
  withFixture((fx) => {
    fx.base();
    fx.write(
      "client/src/CcpClient.Desktop/Thing.cs",
      ["// header", "// EFFECT BACKENDS EXIST, and this comment used to say they did not.", "// tail", ""].join("\n"),
    );
    fx.write(
      "client/docs/notes.md",
      [
        'live: `Thing.cs:2` ("EFFECT BACKENDS EXIST, and this comment used to say they did not").',
        'stale: `Thing.cs:2` ("NO effect backends exist in the greenfield client").',
        "",
      ].join("\n"),
    );
    const outcome = fx.run();
    const rows = rowsOf(outcome, CLASS.WRONG_LINE);
    assert.equal(rows.length, 1, "the live quotation is not a finding; only the stale one is");
    assert.equal(rows[0].reason, `${REASON.QUOTE_ABSENT} (searched 1-3)`);
    assert.match(rows[0].cited, /Thing\.cs:2 quoting "NO effect backends exist in the greenfield client"/);
    assert.match(
      rows[0].reads,
      /EFFECT BACKENDS EXIST/,
      "the row prints what the cited line ACTUALLY reads, so the fix needs no file opened",
    );
    assert.equal(outcome.summary.counts.quotesChecked, 2, "both quotations were checked, not just the failing one");
  });
});

test("I21m: MUTATION — removing the quoted comparison lets the stale quotation through in silence", async () => {
  await withMutant(
    `          if (!quotePresent(lines.slice(lo, hi).join("\\n"), quoted)) {`,
    `          if (false) {`,
    async (mutant) =>
      withFixture((fx) => {
        fx.base();
        fx.write("client/src/CcpClient.Desktop/Thing.cs", "// the file says one thing\n");
        fx.write("client/docs/notes.md", '`Thing.cs:1` ("the file says something else entirely").\n');
        assert.equal(rowsOf(fx.run(), CLASS.WRONG_LINE).length, 1, "unmutated, this is a row");
        const mutated = mutant.runIntraDetector({ repoRoot: fx.root });
        assert.deepEqual(mutated.rows, [], "with the comparison gone the claim is unwatched — that is the gap this closes");
      }),
  );
});

test("I22: the bleed is ONE line — a quotation just outside the cited range passes, two lines out does not", () => {
  withFixture((fx) => {
    fx.base();
    fx.write(
      "client/src/CcpClient.Desktop/Thing.cs",
      [
        "// alpha the first sentence",
        "// beta the second sentence",
        "// gamma the third sentence",
        "// delta the fourth sentence",
        "// epsilon the fifth sentence",
        "",
      ].join("\n"),
    );
    fx.write(
      "client/docs/notes.md",
      [
        'one line early: `Thing.cs:3` ("beta the second sentence").',
        'one line late: `Thing.cs:3` ("delta the fourth sentence").',
        'two lines out: `Thing.cs:3` ("alpha the first sentence").',
        "",
      ].join("\n"),
    );
    const rows = rowsOf(fx.run(), CLASS.WRONG_LINE);
    assert.equal(rows.length, 1, "one line of slack either way is imprecision, not rot");
    assert.match(rows[0].cited, /"alpha the first sentence"/);
    assert.equal(rows[0].reason, `${REASON.QUOTE_ABSENT} (searched 2-4)`, "the row states the window it searched");
  });
});

test("I22m: MUTATION — a zero bleed reds the one-line imprecision the corpus is full of", async () => {
  await withMutant("const QUOTE_BLEED = 1;", "const QUOTE_BLEED = 0;", async (mutant) =>
    withFixture((fx) => {
      fx.base();
      fx.write("client/src/CcpClient.Desktop/Thing.cs", "// alpha the first sentence\n// beta the second sentence\n");
      fx.write("client/docs/notes.md", '`Thing.cs:2` ("alpha the first sentence").\n');
      assert.deepEqual(fx.run().rows, [], "unmutated, a quotation one line above the cited line is fine");
      assert.equal(
        mutant.runIntraDetector({ repoRoot: fx.root }).rows.length,
        1,
        "at zero bleed the tolerance is gone and correct prose reds — 10 of the corpus's 26 live quotations are this shape",
      );
    }),
  );
});

test("I23: normalisation survives markup, entities, wrapping and a C# concatenation seam; ellipsis elides IN ORDER", () => {
  // quotePresent is exported and pure, so these are asserted directly rather than through a
  // fixture repository. Each pair is a REAL shape from the corpus, named in normaliseProse's own
  // comment; asserting them here is what stops a "tidy-up" of that function silently narrowing it.
  const holds = [
    [
      "<para><b>WPF's fourth dial, <c>BubbleCountStrictLock</c>, is ABSENT</b> rather than",
      "WPF's fourth dial, BubbleCountStrictLock, is ABSENT",
    ],
    ["/// Flat pool (phrase -&gt; bool) counts its keys", "Flat pool (phrase -> bool) counts its keys"],
    ["// endpoint down — stay quiet, don't spin", "Endpoint down — stay quiet, don’t spin"],
    ["/// a document that is **marked never-runnable** (the row's", "marked never-runnable"],
    [
      '        + "dead controls: attention "\n        + "checks and the strict/retry apparatus"',
      "dead controls: attention checks and the strict/retry apparatus",
    ],
    [
      "/// the port has no <c>DailyFreeService</c> and no <c>/config/daily-feature</c> fetch,\n/// and the override, never the local rotation",
      "the port has no DailyFreeService and no … override, never the local rotation",
    ],
  ];
  for (const [span, needle] of holds) {
    assert.ok(quotePresent(span, needle), `should still resolve: ${JSON.stringify(needle)}`);
  }
  assert.ok(
    !quotePresent("first alpha then beta", "beta ... alpha"),
    "ORDER is required: an elision means text was cut out, not that the pieces may be reassembled backwards",
  );
  assert.ok(!quotePresent("the file says one thing", "the file says another"), "a genuinely absent sentence stays absent");
  assert.equal(normaliseProse("  <c>A</c>,  B  "), "a, b", "tag removal must not leave a space before the comma it exposed");
});

test("I24: a quotation before a PARENTHESISED citation binds to it; the list form binds to nothing", () => {
  withFixture((fx) => {
    fx.base();
    fx.write("client/src/CcpClient.Desktop/Alpha.cs", "// alpha carries its own sentence\n");
    fx.write("client/src/CcpClient.Desktop/Beta.cs", "// beta carries a different sentence\n");
    fx.write(
      "client/docs/notes.md",
      [
        // The list form. Beta's PRECEDING quotation is Alpha's, and binding it would be a false row.
        'the sources say so: Alpha.cs:1 "alpha carries its own sentence", Beta.cs:1 "beta carries a different sentence".',
        // The list form again, with the second citation carrying NO quotation of its own. This is
        // the line that REACHES the refusal: with a trailing quotation present the AFTER form wins
        // first and QUOTED_PAREN_BEFORE is never consulted, so a fact built only on the line above
        // would assert a refusal it never exercised.
        'and again: Alpha.cs:1 "alpha carries its own sentence", Beta.cs:1 and nothing quoted here.',
        // The parenthesised form, and it is checked.
        '"alpha carries its own sentence" (Alpha.cs:1).',
        "",
      ].join("\n"),
    );
    const outcome = fx.run();
    assert.deepEqual(outcome.rows, [], "every binding here is correct, so there is nothing to report");
    assert.equal(
      outcome.summary.counts.quotesChecked,
      4,
      "three trailing quotations and one parenthesised leading one — the unquoted Beta.cs:1 is checked for range only",
    );
  });
});

test("I24m: MUTATION — dropping the mandatory parenthesis binds the PREVIOUS citation's quotation", async () => {
  await withMutant(
    'const QUOTED_PAREN_BEFORE = /["“]([^"”\\n]+)["”]\\s*\\((?:`|<c>)?$/;',
    'const QUOTED_PAREN_BEFORE = /["“]([^"”\\n]+)["”][\\s,]*(?:`|<c>)?$/;',
    async (mutant) =>
      withFixture((fx) => {
        fx.base();
        fx.write("client/src/CcpClient.Desktop/Alpha.cs", "// alpha carries its own sentence\n");
        fx.write("client/src/CcpClient.Desktop/Beta.cs", "// beta carries a different sentence\n");
        // Beta carries no quotation of its own, so the leading form is the one under test: with a
        // trailing quotation present the AFTER form would win first and the mutation would prove
        // nothing.
        fx.write("client/docs/notes.md", 'Alpha.cs:1 "alpha carries its own sentence", Beta.cs:1 and nothing quoted here.\n');
        assert.deepEqual(fx.run().rows, [], "unmutated, the list form is correct prose and is silent");
        const rows = mutant.runIntraDetector({ repoRoot: fx.root }).rows;
        assert.equal(rows.length, 1, "the loose rule reads Alpha's quotation as a claim about Beta");
        assert.match(rows[0].cited, /Beta\.cs:1 quoting "alpha carries its own sentence"/);
      }),
  );
});

test("I25: a TRAILING parenthesised citation owns the quotation, and the citation before it is not held to it", () => {
  withFixture((fx) => {
    fx.base();
    fx.write(
      "client/src/CcpClient.Desktop/Thing.cs",
      ["// line one is the creation call", "// filler", "// filler", "// line four is the reason", ""].join("\n"),
    );
    // The window manifest's real shape: a creation-site citation, the quotation, and THEN the
    // citation the quotation actually came from. Adjacency alone holds :1 to text four lines away.
    fx.write("client/docs/notes.md", 'the flag alone (`Thing.cs:1`) — "line four is the reason" (`Thing.cs:4`)\n');
    const outcome = fx.run();
    assert.deepEqual(outcome.rows, [], "the trailing attribution is the author's own, and it is correct");
    assert.equal(outcome.summary.counts.quotesChecked, 1, "checked ONCE, against the citation that claimed it");
  });
});

test("I25m: MUTATION — without the trailing-attribution refusal the earlier citation is held to it and reds", async () => {
  await withMutant(
    "  if (after && !QUOTE_CLAIMED_BY_NEXT.test(tail.slice(after[0].length))) return after[1];",
    "  if (after) return after[1];",
    async (mutant) =>
      withFixture((fx) => {
        fx.base();
        fx.write(
          "client/src/CcpClient.Desktop/Thing.cs",
          ["// line one is the creation call", "// filler", "// filler", "// line four is the reason", ""].join("\n"),
        );
        fx.write("client/docs/notes.md", '(`Thing.cs:1`) — "line four is the reason" (`Thing.cs:4`)\n');
        assert.deepEqual(fx.run().rows, [], "unmutated, correct prose with a trailing attribution is silent");
        const rows = mutant.runIntraDetector({ repoRoot: fx.root }).rows;
        assert.equal(rows.length, 1, "adjacency alone invents a row about a sentence the author cited correctly");
        assert.match(rows[0].cited, /Thing\.cs:1 quoting "line four is the reason"/);
      }),
  );
});

test("I26: a C# string literal beside a citation is NOT a quotation — the closing quote blocks the lead", () => {
  withFixture((fx) => {
    fx.base();
    fx.write("client/src/CcpClient.Desktop/Thing.cs", "// nothing like the literal below\n");
    fx.write(
      "client/tests/CcpClient.Tests/ThingTests.cs",
      '        Assert.Equal("Thing.cs:1", "a totally unrelated expected value");\n',
    );
    const outcome = fx.run();
    assert.deepEqual(outcome.rows, [], "the second literal is not a claim about the first literal's file");
    assert.equal(outcome.summary.counts.quotesChecked, 0, "nothing here was read as a quotation at all");
  });
});

test("I26m: MUTATION — admitting a bare quote into the lead reads the next literal as a quotation and fires", async () => {
  await withMutant(
    'const QUOTED_AFTER = /^(?:<\\/c>|<\\/code>|\\/\\/\\/|\\/\\/|[`*_\\s,;|()[\\]:—-])*["“]([^"”\\n]+)["”]/;',
    'const QUOTED_AFTER = /^(?:<\\/c>|<\\/code>|\\/\\/\\/|\\/\\/|["`*_\\s,;|()[\\]:—-])*["“]([^"”\\n]+)["”]/;',
    async (mutant) =>
      withFixture((fx) => {
        fx.base();
        fx.write("client/src/CcpClient.Desktop/Thing.cs", "// nothing like the literal below\n");
        fx.write(
          "client/tests/CcpClient.Tests/ThingTests.cs",
          '        Assert.Equal("Thing.cs:1", "a totally unrelated expected value");\n',
        );
        assert.deepEqual(fx.run().rows, [], "unmutated, a test's own expected value is not a citation claim");
        const rows = mutant.runIntraDetector({ repoRoot: fx.root }).rows;
        assert.equal(rows.length, 1, "this is the shape that returned garbage on the first corpus sweep");
        assert.match(rows[0].cited, /"a totally unrelated expected value"/);
      }),
  );
});

test("I27: a scare-quoted WORD is below the floor — counted, never checked", () => {
  withFixture((fx) => {
    fx.base();
    fx.write("client/src/CcpClient.Desktop/Thing.cs", "// this line says nothing of the sort\n");
    fx.write("client/docs/notes.md", '`Thing.cs:1` ("quests") and `Thing.cs:1` ("open").\n');
    const outcome = fx.run();
    assert.deepEqual(outcome.rows, [], "naming a term in quotes is not a promise the cited line carries it verbatim");
    assert.equal(outcome.summary.counts.quotesTooShort, 2);
    assert.equal(outcome.summary.counts.quotesChecked, 0);
    assert.match(formatIntraReport(outcome), /2 shorter than 12 characters/, "the floor's cost is printed, not implied");
  });
});

test("I27m: MUTATION — a floor of zero holds scare-quoted words to a verbatim match", async () => {
  await withMutant("export const QUOTE_MIN_LENGTH = 12;", "export const QUOTE_MIN_LENGTH = 0;", async (mutant) =>
    withFixture((fx) => {
      fx.base();
      fx.write("client/src/CcpClient.Desktop/Thing.cs", "// this line says nothing of the sort\n");
      fx.write("client/docs/notes.md", '`Thing.cs:1` ("quests").\n');
      assert.deepEqual(fx.run().rows, [], "unmutated, a scare-quoted word is not a quotation");
      assert.equal(mutant.runIntraDetector({ repoRoot: fx.root }).rows.length, 1, "without the floor the word is a claim");
    }),
  );
});

test("I28: a decision ledger's quotations are counted and never rowed — every OTHER class still watches it", () => {
  withFixture((fx) => {
    fx.base();
    fx.write("client/src/CcpClient.Desktop/Thing.cs", "// the store exists now\n");
    fx.write(
      "client/docs/task-board.md",
      [
        "# Board",
        "",
        '| P1 | DONE | ADMITTED because `Thing.cs:1` ("COMPUTED, never granted - no store") said so. |',
        "| P2 | WIP | a range that is gone: `Thing.cs:900`. |",
        "",
      ].join("\n"),
    );
    const outcome = fx.run();
    const rows = rowsOf(outcome, CLASS.WRONG_LINE);
    assert.equal(rows.length, 1, "the ledger is still range-checked in full");
    assert.equal(rows[0].reason, `${REASON.PAST_END} (1 lines)`, "and the row it produces is the RANGE one, not the quote one");
    assert.equal(outcome.summary.counts.quotesInLedger, 1, "the suppressed quotation is counted rather than hidden");
    assert.equal(outcome.summary.counts.quotesChecked, 0);
    assert.match(formatIntraReport(outcome), /1 in a decision ledger \(client\/docs\/task-board\.md\)/);
  });
});

test("I28m: MUTATION — an empty ledger list reds the closed row's own justification", async () => {
  await withMutant(
    'export const LEDGER_DOCUMENTS = Object.freeze(["client/docs/task-board.md"]);',
    "export const LEDGER_DOCUMENTS = Object.freeze([]);",
    async (mutant) =>
      withFixture((fx) => {
        fx.base();
        fx.write("client/src/CcpClient.Desktop/Thing.cs", "// the store exists now\n");
        fx.write(
          "client/docs/task-board.md",
          '| P1 | DONE | ADMITTED because `Thing.cs:1` ("COMPUTED, never granted - no store") said so. |\n',
        );
        assert.deepEqual(fx.run().rows, [], "unmutated, a closed row's dated evidence is left alone");
        const rows = mutant.runIntraDetector({ repoRoot: fx.root }).rows;
        assert.equal(rows.length, 1, "without the exclusion, finishing the work is what breaks the row that authorised it");
        assert.equal(rows[0].reason, `${REASON.QUOTE_ABSENT} (searched 1-1)`);
      }),
  );
});

test("I28b: a ledger path that does not exist is a COULD-NOT-RUN, never a silent widening", () => {
  withFixture((fx) => {
    fx.base();
    fs.rmSync(path.join(fx.root, "client", "docs", "task-board.md"));
    // Through the CLI, the same way I15b pins the fixture exclusion: exit 2 is COULD-NOT-RUN and is
    // deliberately not exit 1, so a suppression that stopped suppressing can never read as rot.
    const run = fx.cli();
    assert.equal(run.status, 2, "an exclusion pointing at nothing has stopped excluding, and that is not a rot verdict");
    assert.match(run.stderr, /decision-ledger exclusion/, "the failure must name which exclusion broke");
    assert.match(run.stderr, /LEDGER_DOCUMENTS/, "and the constant to update");
    assert.match(run.stderr, /task-board\.md/, "and the path it could not find");
    assert.equal(run.stdout, "", "no report on a broken input, ever");
  });
});
