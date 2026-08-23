#!/usr/bin/env node
// self-test.mjs — the facts that bind detect.mjs.
//
// WHY THIS EXISTS, AND HOW IT REACHES THE .NET SUITE
// check-floor.mjs discovers only csproj entries under tests/ in client/CcpClient.sln
// (:80-107) and runs them (:364), so a node file under client/tools/ is invisible to it
// and NOTHING RAN THIS FILE until the .NET bridge below was added. It now runs on every floor run:
// client/tests/CcpClient.Tests/CitationSelfTestGateTests.cs spawns
//   node --test-reporter=tap client/tools/citations/self-test.mjs
// and reds the suite on a failing fact. THAT MAKES THE TAP TRANSCRIPT A CONTRACT: the
// bridge anchors each fact by its Fn: ID PREFIX, so renaming an ID reds it on purpose.
//
// RUN IT
//   node client/tools/citations/self-test.mjs
// Exit 0 = every fact holds. Exit 1 = at least one fact failed, and node:test names it.
// Precedent for a tool carrying its own self-test outside the suite:
// client/tools/verify/self-test.ps1 — which also sets the standard these facts follow,
// asserting the SPECIFIC NAMED CHECK that tripped (:38-42) rather than merely that
// something failed.
//
// FIXTURES ARE TEMP-DIR REPOSITORIES, NEVER TODAY'S REAL TREE
// A self-test pinned to today's 297 entries goes red the day someone adds a citation,
// which is the day the detector is most needed. Every fact below builds its own tiny
// repository under os.tmpdir() and asserts against that. Nothing here reads the real
// inventory, the real WPF tree, or the real port sources.
//
// CONCURRENCY
// Up to eight lanes share os.tmpdir() with other suites' fixtures. Each fixture directory is
// ccp-sp088-<randomUUID> (distinct prefix so a stray directory is attributable) and is
// removed in a finally, mirroring UpstreamPayloadInventoryTests.cs:546. A fixed fixture
// name would corrupt two lanes at 8-way concurrency.
//
// PORTABILITY / HOUSE RULES
// node:test + node:assert/strict are CORE modules, so the zero-npm-dependency rule holds:
// no package.json, no lockfile, no install step. git runs through execFileSync with an
// argv array, never a shell string. There is no wall-clock wait, no sleep and no retry
// loop anywhere in this file.

import assert from "node:assert/strict";
import crypto from "node:crypto";
import { execFileSync } from "node:child_process";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import test from "node:test";
import { fileURLToPath } from "node:url";

import {
  CLASS,
  formatNeedleCoverage,
  formatNeedleReport,
  formatRegenerateReport,
  formatReport,
  NEEDLE_CLASS,
  REASON,
  REGEN_CLASS,
  rewriteChangedAtSync,
  runDetector,
  runNeedleReview,
  runRegenerate,
  TREE_CCP,
  TREE_SHIPPING,
  UNREVIEWED_SENTINEL,
} from "./detect.mjs";

const DETECT = fileURLToPath(new URL("./detect.mjs", import.meta.url));

// ------------------------------------------------------------------ fixture repo

/** Unique per invocation, removed in a finally. Same shape as
 *  UpstreamPayloadInventoryTests.cs:540-586 (WithFixtureRepo). */
function withFixtureRepo(body) {
  const root = path.join(os.tmpdir(), `ccp-sp088-${crypto.randomUUID()}`);
  fs.mkdirSync(root, { recursive: true });
  try {
    return body(makeFixture(root));
  } finally {
    fs.rmSync(root, { recursive: true, force: true });
  }
}

function makeFixture(root) {
  const git = (...args) =>
    execFileSync("git", ["-C", root, ...args], { encoding: "utf8", stdio: ["ignore", "pipe", "pipe"] });

  const fx = {
    root,
    /** Writes a file, creating parents. Paths are repo-relative and forward-slashed. */
    write(rel, text) {
      const full = path.join(root, ...rel.split("/"));
      fs.mkdirSync(path.dirname(full), { recursive: true });
      fs.writeFileSync(full, text, "utf8");
      return fx;
    },
    /** The repo-root anchor findRepoRoot() walks up to. */
    sln() {
      return fx.write("client/CcpClient.sln", "");
    },
    /** git init plus one commit. Identity is passed per-invocation so the fixture is
     *  hermetic on a machine with no configured git user. */
    commit(message) {
      if (!fs.existsSync(path.join(root, ".git"))) git("init", "-q", "-b", "main");
      git("add", "-A");
      git("-c", "user.name=sp088", "-c", "user.email=sp088@example.invalid", "commit", "-q", "-m", message);
      return git("rev-parse", "HEAD").trim();
    },
    /** Real numstat for a path across a window — used to build known-answer DELTA
     *  fixtures without reimplementing the detector's comparison. */
    numstat(since, until, rel) {
      const out = git("diff", "--numstat", `${since}..${until}`, "--", rel).trim();
      const m = /^(\d+)\t(\d+)\t/.exec(out);
      assert.ok(m, `fixture setup: expected a numstat row for ${rel}, got ${JSON.stringify(out)}`);
      return { add: Number(m[1]), del: Number(m[2]) };
    },
    /** Writes the inventory. Written AFTER the commits so it never lands in the window. */
    inventory(obj) {
      return fx.write("client/docs/upstream-citation-inventory.json", JSON.stringify(obj, null, 2));
    },
    /** Writes a deliberately unparseable inventory (fact F11). */
    rawInventory(text) {
      return fx.write("client/docs/upstream-citation-inventory.json", text);
    },
    /** A commit's committer date as YYYY-MM-DD — the value the regenerate mode DERIVES for
     *  `sync`. Taken from git here too, so the fact never hard-codes today's date. */
    commitDate(sha) {
      return git("show", "-s", "--format=%cs", sha).trim();
    },
    /** The committed inventory's bytes, exactly as they sit on disk. */
    inventoryBytes() {
      return fs.readFileSync(path.join(root, "client", "docs", "upstream-citation-inventory.json"), "utf8");
    },
    /** Runs the CLI as a real child process with cwd inside the fixture. */
    cli(args = [], cwd = root) {
      try {
        const stdout = execFileSync("node", [DETECT, ...args], {
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
const pathsOf = (outcome, cls) => rowsOf(outcome, cls).map((r) => r.path);
const baseline = (since, until) => ({ merge: until, previous: { merge: since } });

// ============================================================ F1 / F2 NEEDS-VERDICT

/** Five tier/verdict/changed combinations in one repo, so all three gates of
 *  NEEDS-VERDICT (verdict, tier, changed-in-window) have a positive case and a control. */
function needsVerdictFixture(fx) {
  for (const name of ["A1", "A2", "A3", "A4", "A5"]) {
    fx.write(`ConditioningControlPanel/Services/${name}.cs`, "line1\n");
  }
  fx.sln();
  const since = fx.commit("base");
  // A1..A4 change in the window; A5 deliberately does not.
  for (const name of ["A1", "A2", "A3", "A4"]) {
    fx.write(`ConditioningControlPanel/Services/${name}.cs`, "line1\nline2\nline3\n");
  }
  const until = fx.commit("head");
  const changed = fx.numstat(since, until, "ConditioningControlPanel/Services/A1.cs");
  const at = { sync: "fixture", status: "M", add: changed.add, del: changed.del };
  fx.inventory({
    schemaVersion: 1,
    baseline: baseline(since, until),
    entries: [
      { path: "ConditioningControlPanel/Services/A1.cs", tier: 1, citedBy: [], changedAtSync: at, verdict: "" },
      {
        path: "ConditioningControlPanel/Services/A2.cs",
        tier: 1,
        citedBy: [],
        changedAtSync: at,
        verdict: `${UNREVIEWED_SENTINEL} - owed to board row "Tier-1 citation review for the v6.8.0 sync"`,
      },
      {
        path: "ConditioningControlPanel/Services/A3.cs",
        tier: 1,
        citedBy: [],
        changedAtSync: at,
        verdict: "reviewed at the fixture sync, no parity impact",
      },
      { path: "ConditioningControlPanel/Services/A4.cs", tier: 2, citedBy: [], changedAtSync: at, verdict: "" },
      { path: "ConditioningControlPanel/Services/A5.cs", tier: 1, citedBy: [], verdict: "" },
    ],
  });
  return runDetector({ repoRoot: fx.root });
}

test("F1: NEEDS-VERDICT fires on an empty verdict AND on the UNREVIEWED sentinel, with the sub-reason on each row", () => {
  withFixtureRepo((fx) => {
    const outcome = needsVerdictFixture(fx);
    const rows = rowsOf(outcome, CLASS.NEEDS_VERDICT);
    const byPath = new Map(rows.map((r) => [r.path, r]));

    const empty = byPath.get("ConditioningControlPanel/Services/A1.cs");
    assert.ok(empty, "NEEDS-VERDICT must contain the tier-1 changed entry whose verdict is empty");
    assert.equal(empty.reason, REASON.VERDICT_EMPTY);

    const sentinel = byPath.get("ConditioningControlPanel/Services/A2.cs");
    assert.ok(sentinel, `NEEDS-VERDICT must contain the tier-1 changed entry whose verdict starts with ${UNREVIEWED_SENTINEL}`);
    assert.equal(sentinel.reason, REASON.VERDICT_SENTINEL);

    assert.equal(outcome.summary.needsVerdict.empty, 1, "summary must carry the empty sub-count");
    assert.equal(outcome.summary.needsVerdict.sentinel, 1, "summary must carry the sentinel sub-count");

    // Control: a tier-1 changed entry that HAS a real verdict is not a row.
    assert.ok(!byPath.has("ConditioningControlPanel/Services/A3.cs"), "a real verdict must not produce a row");
  });
});

test("F2: NEEDS-VERDICT is gated on tier 1 — a changed tier-2 entry with an empty verdict is not a row", () => {
  withFixtureRepo((fx) => {
    const outcome = needsVerdictFixture(fx);
    assert.ok(
      !pathsOf(outcome, CLASS.NEEDS_VERDICT).includes("ConditioningControlPanel/Services/A4.cs"),
      "tier-2 entries must not reach NEEDS-VERDICT, however empty their verdict",
    );
  });
});

test("F2b: NEEDS-VERDICT is gated on changedAtSync — an UNCHANGED tier-1 entry with an empty verdict is not a row", () => {
  withFixtureRepo((fx) => {
    const outcome = needsVerdictFixture(fx);
    assert.ok(
      !pathsOf(outcome, CLASS.NEEDS_VERDICT).includes("ConditioningControlPanel/Services/A5.cs"),
      "an entry that did not change in the window owes no verdict for this sync",
    );
  });
});

// ================================================================ F3 NEW-CITATION

test("F3: NEW-CITATION names a cited real path the inventory does not carry, and its docs: source", () => {
  withFixtureRepo((fx) => {
    fx.write("ConditioningControlPanel/Services/Known.cs", "x\n");
    fx.write("ConditioningControlPanel/Services/NewGuy.cs", "x\n");
    // Cited ONLY from client/docs/**: 232 of the real inventory's 297 entries are, and a
    // src-only scan was one of the two named defects of the hand-rolled version.
    fx.write("client/docs/notes.md", "Behaviour lives in NewGuy.cs:12 and Known.cs:4.\n");
    fx.sln();
    const since = fx.commit("base");
    fx.write("ConditioningControlPanel/Services/Known.cs", "x\ny\n");
    const until = fx.commit("head");
    fx.inventory({
      schemaVersion: 1,
      baseline: baseline(since, until),
      entries: [{ path: "ConditioningControlPanel/Services/Known.cs", tier: 1, citedBy: ["docs:notes.md"], verdict: "ok" }],
    });

    const outcome = runDetector({ repoRoot: fx.root });
    const rows = rowsOf(outcome, CLASS.NEW_CITATION);
    assert.deepEqual(
      rows.map((r) => r.path),
      ["ConditioningControlPanel/Services/NewGuy.cs"],
      "NEW-CITATION must contain exactly the cited path the inventory lacks",
    );
    assert.deepEqual(rows[0].citedBy, ["docs:notes.md"], "the docs: citer must be named in the committed prefix format");
  });
});

// =============================================================== F4 CITATION-GONE

test("F4: CITATION-GONE names an inventory entry that exists on disk but nothing cites", () => {
  withFixtureRepo((fx) => {
    fx.write("ConditioningControlPanel/Services/Cited.cs", "x\n");
    fx.write("ConditioningControlPanel/Services/Orphan.cs", "x\n");
    fx.write("client/docs/notes.md", "Only Cited.cs:3 matters now.\n");
    fx.sln();
    const since = fx.commit("base");
    fx.write("ConditioningControlPanel/Services/Cited.cs", "x\ny\n");
    const until = fx.commit("head");
    fx.inventory({
      schemaVersion: 1,
      baseline: baseline(since, until),
      entries: [
        { path: "ConditioningControlPanel/Services/Cited.cs", tier: 1, citedBy: ["docs:notes.md"], verdict: "ok" },
        { path: "ConditioningControlPanel/Services/Orphan.cs", tier: 3, citedBy: ["docs:notes.md"], verdict: "ok" },
      ],
    });

    const outcome = runDetector({ repoRoot: fx.root });
    assert.deepEqual(
      pathsOf(outcome, CLASS.CITATION_GONE),
      ["ConditioningControlPanel/Services/Orphan.cs"],
      "CITATION-GONE must contain exactly the uncited entry — and never the still-cited control",
    );
  });
});

// ========================================================= F5 / F6 UNRESOLVED

/** All three Decision A branches plus both tree labels, in one repo. */
function unresolvedFixture(fx) {
  // The ONLY surviving Moved.cs is in the first-attempt tree.
  fx.write("ConditioningControlPanel/CCP.Foo/Moved.cs", "x\n");
  // The only surviving ShipMoved.cs is in the shipping tree.
  fx.write("ConditioningControlPanel/Services/ShipMoved.cs", "x\n");
  // Twin.cs exists twice, both shipping.
  fx.write("ConditioningControlPanel/Alpha/Twin.cs", "x\n");
  fx.write("ConditioningControlPanel/Beta/Twin.cs", "x\n");
  fx.sln();
  const since = fx.commit("base");
  fx.write("ConditioningControlPanel/Services/ShipMoved.cs", "x\ny\n");
  const until = fx.commit("head");
  fx.inventory({
    schemaVersion: 1,
    baseline: baseline(since, until),
    entries: [
      { path: "ConditioningControlPanel/Models/Moved.cs", tier: 1, citedBy: ["docs:a.md"], verdict: "ok" },
      { path: "ConditioningControlPanel/Models/ShipMoved.cs", tier: 1, citedBy: ["docs:a.md"], verdict: "ok" },
      { path: "ConditioningControlPanel/Models/Ghost.cs", tier: 2, citedBy: ["docs:a.md"], verdict: "ok" },
      { path: "ConditioningControlPanel/Models/Twin.cs", tier: 2, citedBy: ["docs:a.md"], verdict: "ok" },
    ],
  });
  return runDetector({ repoRoot: fx.root });
}

test("F5: UNRESOLVED labels the tree of every candidate — a lone first-attempt survivor is labelled ccp-first-attempt", () => {
  withFixtureRepo((fx) => {
    const outcome = unresolvedFixture(fx);
    const byPath = new Map(rowsOf(outcome, CLASS.UNRESOLVED).map((r) => [r.path, r]));
    assert.equal(byPath.size, 4, "all four non-existent inventory paths must produce an UNRESOLVED row");

    // The constitution-critical assertion: docs/constitution.md:32 makes CCP.* lessons-only
    // evidence, so a claim whose only surviving file is there must SAY so.
    const moved = byPath.get("ConditioningControlPanel/Models/Moved.cs");
    assert.equal(moved.reason, REASON.MOVED);
    assert.deepEqual(moved.candidates, [{ path: "ConditioningControlPanel/CCP.Foo/Moved.cs", tree: TREE_CCP }]);

    const shipMoved = byPath.get("ConditioningControlPanel/Models/ShipMoved.cs");
    assert.equal(shipMoved.reason, REASON.MOVED);
    assert.deepEqual(shipMoved.candidates, [{ path: "ConditioningControlPanel/Services/ShipMoved.cs", tree: TREE_SHIPPING }]);

    assert.equal(byPath.get("ConditioningControlPanel/Models/Ghost.cs").reason, REASON.VANISHED);

    const twin = byPath.get("ConditioningControlPanel/Models/Twin.cs");
    assert.equal(twin.reason, REASON.AMBIGUOUS);
    assert.deepEqual(
      twin.candidates.map((c) => c.path),
      ["ConditioningControlPanel/Alpha/Twin.cs", "ConditioningControlPanel/Beta/Twin.cs"],
      "every candidate is named and none is chosen",
    );
  });
});

test("F6: the UNRESOLVED candidate search covers the FULL tree — a first-attempt survivor is MOVED?, not VANISHED", () => {
  withFixtureRepo((fx) => {
    const outcome = unresolvedFixture(fx);
    const moved = rowsOf(outcome, CLASS.UNRESOLVED).find((r) => r.path === "ConditioningControlPanel/Models/Moved.cs");
    assert.equal(
      moved.reason,
      REASON.MOVED,
      "searching only the shipping universe would hide the move into CCP.* behind a VANISHED row",
    );
  });
});

// ==================================================== F7 / F8 AMBIGUOUS + ordering

/** Two real shipping paths share a basename and a client/src file cites the bare name.
 *  The citer is a src: source on purpose, so the client/src scan root has a fact biting
 *  on it (client/docs has F3 and F4). */
function ambiguousFixture(fx) {
  fx.write("ConditioningControlPanel/Alpha/Dup.cs", "x\n");
  fx.write("ConditioningControlPanel/Beta/Dup.cs", "x\n");
  fx.write("client/src/CcpClient.Desktop/Thing.cs", "// parity: Dup.cs:77 is the behaviour we mirror\n");
  fx.sln();
  const since = fx.commit("base");
  fx.write("ConditioningControlPanel/Alpha/Dup.cs", "x\ny\n");
  const until = fx.commit("head");
  fx.inventory({
    schemaVersion: 1,
    baseline: baseline(since, until),
    entries: [
      { path: "ConditioningControlPanel/Alpha/Dup.cs", tier: 1, citedBy: ["src:src/CcpClient.Desktop/Thing.cs"], verdict: "ok" },
      { path: "ConditioningControlPanel/Beta/Dup.cs", tier: 1, citedBy: ["src:src/CcpClient.Desktop/Thing.cs"], verdict: "ok" },
    ],
  });
  return runDetector({ repoRoot: fx.root });
}

test("F7: an ambiguous basename emits ONE row naming BOTH candidates, and neither path is keyed", () => {
  withFixtureRepo((fx) => {
    const outcome = ambiguousFixture(fx);
    const rows = rowsOf(outcome, CLASS.AMBIGUOUS);
    assert.equal(rows.length, 1, "one row per ambiguous basename");
    assert.equal(rows[0].path, "Dup.cs");
    assert.deepEqual(
      rows[0].candidates.map((c) => c.path),
      ["ConditioningControlPanel/Alpha/Dup.cs", "ConditioningControlPanel/Beta/Dup.cs"],
      "every candidate named, none chosen",
    );
    assert.deepEqual(
      rows[0].citedBy,
      ["src:src/CcpClient.Desktop/Thing.cs"],
      "the client/src scan root must contribute citers in the committed src: prefix format",
    );
    // The other half of the no-basename-dedup rule: an ambiguous token contributes
    // NOTHING to the keyed set, so it can never collapse two real paths into one.
    assert.equal(outcome.summary.regeneratedPaths, 0, "an ambiguous citation must key no path at all");
  });
});

test("F8: ordering invariant — an ambiguous candidate does not also become a spurious CITATION-GONE", () => {
  withFixtureRepo((fx) => {
    const outcome = ambiguousFixture(fx);
    assert.deepEqual(
      pathsOf(outcome, CLASS.CITATION_GONE),
      [],
      "CITATION-GONE is the residue AFTER UNRESOLVED and AMBIGUOUS claim their rows; both Dup paths are already reported",
    );
  });
});

// ============================================================= F9 DELTA-MISMATCH

test("F9: DELTA-MISMATCH fires on a del disagreement and on a missing numstat row, and stays silent on an exact match", () => {
  withFixtureRepo((fx) => {
    fx.write("ConditioningControlPanel/Services/D1.cs", "a\nb\nc\n");
    fx.write("ConditioningControlPanel/Services/D2.cs", "a\n");
    fx.write("ConditioningControlPanel/Services/D3.cs", "a\n");
    fx.sln();
    const since = fx.commit("base");
    fx.write("ConditioningControlPanel/Services/D1.cs", "a\nX\nY\nZ\nc\n");
    fx.write("ConditioningControlPanel/Services/D2.cs", "a\nb\n");
    // D3 is deliberately untouched in the window.
    const until = fx.commit("head");

    const d1 = fx.numstat(since, until, "ConditioningControlPanel/Services/D1.cs");
    const d2 = fx.numstat(since, until, "ConditioningControlPanel/Services/D2.cs");
    fx.inventory({
      schemaVersion: 1,
      baseline: baseline(since, until),
      entries: [
        // add agrees, del does NOT — so a rule that compares only `add` stops firing here.
        { path: "ConditioningControlPanel/Services/D1.cs", tier: 1, citedBy: [], verdict: "ok",
          changedAtSync: { sync: "fixture", status: "M", add: d1.add, del: d1.del + 7 } },
        // exact match: the negative control.
        { path: "ConditioningControlPanel/Services/D2.cs", tier: 1, citedBy: [], verdict: "ok",
          changedAtSync: { sync: "fixture", status: "M", add: d2.add, del: d2.del } },
        // recorded as changed, but the window has no numstat row at this path at all.
        { path: "ConditioningControlPanel/Services/D3.cs", tier: 1, citedBy: [], verdict: "ok",
          changedAtSync: { sync: "fixture", status: "M", add: 5, del: 5 } },
      ],
    });

    const outcome = runDetector({ repoRoot: fx.root });
    const byPath = new Map(rowsOf(outcome, CLASS.DELTA_MISMATCH).map((r) => [r.path, r]));

    const delRow = byPath.get("ConditioningControlPanel/Services/D1.cs");
    assert.ok(delRow, "a del-only disagreement must fire DELTA-MISMATCH");
    assert.match(delRow.reason, new RegExp(`recorded \\+${d1.add}/-${d1.del + 7}, numstat \\+${d1.add}/-${d1.del}`));

    const missingRow = byPath.get("ConditioningControlPanel/Services/D3.cs");
    assert.ok(missingRow, "a recorded change with no numstat row at the entry's own path must fire");
    assert.ok(missingRow.reason.includes(REASON.NO_NUMSTAT_ROW), "the row must name WHY it fired");

    assert.ok(!byPath.has("ConditioningControlPanel/Services/D2.cs"), "an exact match must stay silent");
    assert.equal(outcome.summary.deltaMismatch, 2);
  });
});

// ============================================================ F10-F13 exit contract

test("F10: a NON-EMPTY review list exits 0 — the whole difference between a useful tool and a disabled one", () => {
  withFixtureRepo((fx) => {
    fx.write("ConditioningControlPanel/Services/Orphan.cs", "x\n");
    fx.write("ConditioningControlPanel/Services/Fresh.cs", "x\n");
    fx.write("client/docs/notes.md", "See Fresh.cs:1.\n");
    fx.sln();
    const since = fx.commit("base");
    fx.write("ConditioningControlPanel/Services/Orphan.cs", "x\ny\n");
    const until = fx.commit("head");
    fx.inventory({
      schemaVersion: 1,
      baseline: baseline(since, until),
      entries: [
        { path: "ConditioningControlPanel/Services/Orphan.cs", tier: 1, citedBy: [], verdict: "" ,
          changedAtSync: { sync: "fixture", status: "M", add: 1, del: 0 } },
        { path: "ConditioningControlPanel/Models/Ghost.cs", tier: 1, citedBy: [], verdict: "ok" },
      ],
    });

    const run = fx.cli();
    assert.equal(run.status, 0, `a populated review list must still exit 0; stderr was: ${run.stderr}`);
    const total = /TOTAL ROWS: (\d+)/.exec(run.stdout);
    assert.ok(total, "the report must state a total");
    assert.ok(Number(total[1]) > 0, "this fixture must actually produce rows, or the fact proves nothing");
  });
});

test("F11: an unparseable inventory exits non-zero, names the inventory path, and prints NO review list", () => {
  withFixtureRepo((fx) => {
    fx.write("ConditioningControlPanel/Services/A.cs", "x\n");
    fx.sln();
    fx.commit("base");
    fx.write("ConditioningControlPanel/Services/A.cs", "x\ny\n");
    fx.commit("head");
    fx.rawInventory("{not json");

    const run = fx.cli();
    assert.notEqual(run.status, 0, "an unreadable input is exactly when the detector must refuse to run");
    assert.match(run.stderr, /upstream-citation-inventory\.json/, "the failure must name the inventory it could not read");
    assert.match(run.stderr, /not parseable JSON/, "the failure must name the specific check that tripped");
    assert.ok(!run.stdout.includes("REVIEW LIST"), "a broken input must never produce a partial or empty review list");
  });
});

test("F12: an unresolvable --since SHA exits non-zero naming THAT SHA and prints NO review list", () => {
  withFixtureRepo((fx) => {
    fx.write("ConditioningControlPanel/Services/A.cs", "x\n");
    fx.sln();
    const since = fx.commit("base");
    fx.write("ConditioningControlPanel/Services/A.cs", "x\ny\n");
    const until = fx.commit("head");
    fx.inventory({ schemaVersion: 1, baseline: baseline(since, until), entries: [] });

    const bogus = "deadbeefdeadbeefdeadbeefdeadbeefdeadbeef"; // valid 40-hex, absent from the repo
    const run = fx.cli(["--since", bogus]);
    assert.notEqual(run.status, 0, "a baseline that does not resolve must never yield a review list");
    assert.ok(run.stderr.includes(bogus), "the failure must name the SHA that did not resolve");
    // The NAMED check, not merely "something failed". Deleting the rev-parse precheck
    // leaves `git diff` to fail instead, whose message still contains the SHA (it is in
    // the range) — so only this assertion distinguishes the two.
    assert.match(run.stderr, /SHA does not resolve/, "the failure must name the rev-parse precheck that tripped");
    assert.ok(!run.stdout.includes("REVIEW LIST"), "no partial list on a broken window");
  });
});

test("F13: no repo-root anchor above cwd exits non-zero naming client/CcpClient.sln", () => {
  withFixtureRepo((fx) => {
    // A directory with NO client/CcpClient.sln at or above it: os.tmpdir() is not inside
    // any checkout of this repository.
    const rootless = path.join(fx.root, "nowhere");
    fs.mkdirSync(rootless, { recursive: true });

    const run = fx.cli([], rootless);
    assert.notEqual(run.status, 0, "an unfindable repo root is a hard failure, never a skip");
    assert.match(
      run.stderr,
      /client\/CcpClient\.sln/,
      "the failure must name the ANCHOR it looked for; falling back to cwd would fail for a different reason and hide the bug",
    );
    assert.ok(!run.stdout.includes("REVIEW LIST"), "no list without a root");
  });
});

// ============================================ F14 the first-attempt-credited counter
//
// The resolver reduces every citation to a basename, so a citation whose text names
// CCP.*/tests is credited to the SHIPPING path when that basename resolves uniquely there.
// The text named one tree and the tool credits the other. This fact pins the counter that
// makes the erasure visible, including the sole-citation number: the shipping paths that
// would be CITATION-GONE if the misattribution were removed.

test("F14: a citation naming CCP.*/tests but credited to a shipping path is counted, and the sole-citation paths are named", () => {
  withFixtureRepo((fx) => {
    // Twin: exists in both universes. The doc cites ONLY the first-attempt path.
    fx.write("ConditioningControlPanel/Services/Twin.cs", "x\n");
    fx.write("ConditioningControlPanel/CCP.Foo/Twin.cs", "x\n");
    // Legacy: the `tests` half of the first-attempt boundary, anchored under
    // ConditioningControlPanel/ (the csproj's own DefaultItemExcludes list).
    fx.write("ConditioningControlPanel/Services/Legacy.cs", "x\n");
    fx.write("ConditioningControlPanel/tests/Legacy.cs", "x\n");
    // Both: cited once qualified and once bare, so it is COUNTED but not SOLE.
    fx.write("ConditioningControlPanel/Services/Both.cs", "x\n");
    fx.write("ConditioningControlPanel/CCP.Foo/Both.cs", "x\n");
    // Plain: negative control — bare, and path-qualified into the SHIPPING tree.
    fx.write("ConditioningControlPanel/Services/Plain.cs", "x\n");
    // Ported: negative control for the `tests` marker. client/tests/** is the PORT's own
    // test tree, not the first attempt, and CcpClient.Tests does not start with `CCP.`.
    fx.write("ConditioningControlPanel/Services/Ported.cs", "x\n");

    fx.write(
      "client/docs/notes.md",
      [
        "First attempt only: `CCP.Foo/Twin.cs:12`.",
        "First attempt tests: `ConditioningControlPanel/tests/Legacy.cs:9`.",
        "Mixed: `CCP.Foo/Both.cs:1` and later just Both.cs:2.",
        "Shipping, bare and qualified: Plain.cs:4 and ConditioningControlPanel/Services/Plain.cs:7.",
        "Port test tree: `client/tests/CcpClient.Tests/Ported.cs:3`.",
        "",
      ].join("\n"),
    );
    fx.sln();
    const since = fx.commit("base");
    fx.write("ConditioningControlPanel/Services/Twin.cs", "x\ny\n");
    const until = fx.commit("head");
    fx.inventory({
      schemaVersion: 1,
      baseline: baseline(since, until),
      entries: [
        { path: "ConditioningControlPanel/Services/Twin.cs", tier: 1, citedBy: ["docs:notes.md"], verdict: "ok" },
        { path: "ConditioningControlPanel/Services/Legacy.cs", tier: 3, citedBy: ["docs:notes.md"], verdict: "ok" },
        { path: "ConditioningControlPanel/Services/Both.cs", tier: 2, citedBy: ["docs:notes.md"], verdict: "ok" },
        { path: "ConditioningControlPanel/Services/Plain.cs", tier: 3, citedBy: ["docs:notes.md"], verdict: "ok" },
        { path: "ConditioningControlPanel/Services/Ported.cs", tier: 3, citedBy: ["docs:notes.md"], verdict: "ok" },
      ],
    });

    const outcome = runDetector({ repoRoot: fx.root });
    const credited = outcome.summary.firstAttemptCredited;

    assert.equal(credited.occurrences, 3, "Twin, Legacy and the qualified half of Both are the three occurrences");
    assert.equal(credited.distinctNames, 3, "Plain and Ported name the shipping tree and must not be counted");
    assert.equal(credited.paths, 3, "three shipping paths were credited from first-attempt-qualified text");

    assert.deepEqual(
      credited.solePaths,
      ["ConditioningControlPanel/Services/Legacy.cs", "ConditioningControlPanel/Services/Twin.cs"],
      "a path is SOLE only when EVERY citer named the first attempt — Both.cs is also cited bare",
    );
    assert.equal(credited.soleCitation, 2);

    // The point of the number: without the misattribution these entries have no citer at
    // all, so the class that would have reported them is silent.
    assert.ok(
      !pathsOf(outcome, CLASS.CITATION_GONE).includes("ConditioningControlPanel/Services/Twin.cs"),
      "the misattributed citation is exactly what keeps this entry out of CITATION-GONE",
    );

    // The counter is not the drop counter: a colliding name is never dropped.
    assert.equal(outcome.summary.dropped.occurrences, 0, "every token here resolves in the shipping universe");

    const report = formatReport(outcome);
    assert.match(report, /CREDITED to a shipping path/, "the summary must print the counter, not merely carry it");
    assert.match(report, /ConditioningControlPanel\/Services\/Twin\.cs/, "the sole-citation paths must be named in the report");
  });
});

// ======================================================= F15-F24 the needle mode
//
// THESE ARE THE CLASSIFICATION HALF, AND THE .NET BRIDGE IS WHAT PUT THEM ON THE FLOOR: until then
// no standing gate ran this file, so their green held only as of the last hand-run.
// client/tests/CcpClient.Tests/CitationNeedleTests.cs pins the exit contract of BOTH modes,
// the coverage block, and every needle resolving at exactly one line in the FROZEN baseline
// snapshot — but NOT the mechanism below (moved / gone / ambiguous / out-of-range and the
// coverage arithmetic). That mechanism is fixtured here and gated by
// CitationSelfTestGateTests, which reds the unit suite when any fact below fails.
//
// Every fixture is a temp-dir repository, exactly as F1-F14. Nothing here reads the real
// inventory, the real WPF tree or the real port sources.

/** A repo whose upstream file moves a subject DOWN by `shift` lines between two commits, so
 *  the needle's position at the endpoint and in the working tree are known by construction.
 *  `rewrite` replaces the head revision outright; `cite` writes one port citer. */
function needleFixture(fx, { needles, shift = 0, rewrite = null, cite = null }) {
  const body = ["namespace X;", "class Y {", "  const int Anchor = 1;", "  void Emit() { Raise(); }", "}", ""];

  // THREE commits, not two, and the reason is the DEFAULT mode: its window is
  // previous.merge..merge, so a fixture with only one baseline endpoint makes every
  // both-modes fact (F24) fail on "no change window" rather than on anything it is testing.
  fx.write("ConditioningControlPanel/Services/Unneedled.cs", "one\ntwo\n");
  fx.sln();
  const previous = fx.commit("root");

  fx.write("ConditioningControlPanel/Services/Q.cs", body.join("\n"));
  const endpoint = fx.commit("endpoint");

  const after = rewrite ?? [...Array(shift).fill("// inserted above"), ...body];
  fx.write("ConditioningControlPanel/Services/Q.cs", after.join("\n"));
  // A churn file so the head commit is never empty: with shift 0 and no rewrite the subject
  // deliberately does not move, and `git commit` on a clean tree fails rather than no-opping.
  fx.write("ConditioningControlPanel/Services/Churn.cs", "head\n");
  fx.commit("head");

  if (cite) fx.write("client/src/CcpClient.Desktop/Citer.cs", cite);

  fx.inventory({
    schemaVersion: 1,
    baseline: { merge: endpoint, previous: { merge: previous } },
    entries: [
      {
        path: "ConditioningControlPanel/Services/Q.cs",
        tier: 1,
        citedBy: ["src:src/CcpClient.Desktop/Citer.cs"],
        verdict: "ok",
        needles,
      },
      // The opt-in control: no needles field at all, and it must contribute NOTHING.
      { path: "ConditioningControlPanel/Services/Unneedled.cs", tier: 1, citedBy: [], verdict: "ok" },
    ],
  });
  return { endpoint };
}

const needleRows = (outcome, cls) => outcome.rows.filter((r) => r.cls === cls);

test("F15: a needle that has NOT moved emits no row at all — silence on the happy path is the whole non-wolf property", () => {
  withFixtureRepo((fx) => {
    needleFixture(fx, { needles: [{ id: "emit", needle: "Raise()" }], shift: 0 });
    const outcome = runNeedleReview({ repoRoot: fx.root });

    assert.equal(outcome.rows.length, 0, "an unmoved needle is not a finding");
    assert.equal(outcome.coverage.comparison.unchanged, 1);
    assert.equal(outcome.coverage.comparison.moved, 0);
    assert.equal(outcome.coverage.needles.located, 1);
  });
});

test("F16: a needle that MOVED emits exactly one NEEDLE-MOVED row carrying old, new and the SIGNED delta", () => {
  withFixtureRepo((fx) => {
    const { endpoint } = needleFixture(fx, { needles: [{ id: "emit", needle: "Raise()" }], shift: 7 });
    const outcome = runNeedleReview({ repoRoot: fx.root });

    const moved = needleRows(outcome, NEEDLE_CLASS.MOVED);
    assert.equal(moved.length, 1);
    assert.equal(moved[0].id, "emit");
    // The subject sat on line 4 and seven lines were inserted above it.
    assert.match(moved[0].reason, /:4 -> :11 \(\+7\)/);
    assert.match(moved[0].reason, new RegExp(endpoint.slice(0, 7)));
    assert.equal(outcome.coverage.comparison.moved, 1);
    assert.equal(outcome.coverage.comparison.unchanged, 0);

    // The row NEVER names a citation as wrong: the cut CITATION-DRIFT class is why.
    assert.match(moved[0].action, /names the SHIFT, never which citation is wrong/);
    assert.match(formatNeedleReport(outcome), /NEEDLE-MOVED \(1\)/);
  });
});

test("F17: a needle whose subject was DELETED is NEEDLE-GONE, and is not also reported as moved", () => {
  withFixtureRepo((fx) => {
    needleFixture(fx, {
      needles: [{ id: "emit", needle: "Raise()" }],
      rewrite: ["namespace X;", "class Y {", "  const int Anchor = 1;", "}", ""],
    });
    const outcome = runNeedleReview({ repoRoot: fx.root });

    const gone = needleRows(outcome, NEEDLE_CLASS.GONE);
    assert.equal(gone.length, 1);
    assert.match(gone[0].reason, /no line in the file contains this needle any more/);
    assert.equal(needleRows(outcome, NEEDLE_CLASS.MOVED).length, 0, "a gone needle has no position to have moved");
    assert.equal(outcome.coverage.needles.gone, 1);
    assert.equal(outcome.coverage.needles.located, 0);
  });
});

test("F18: a needle matching TWO lines is NEEDLE-AMBIGUOUS, names both, and is excluded from the comparison", () => {
  withFixtureRepo((fx) => {
    needleFixture(fx, {
      needles: [{ id: "emit", needle: "Raise()" }],
      rewrite: ["namespace X;", "class Y {", "  void A() { Raise(); }", "  void B() { Raise(); }", "}", ""],
    });
    const outcome = runNeedleReview({ repoRoot: fx.root });

    const ambiguous = needleRows(outcome, NEEDLE_CLASS.AMBIGUOUS);
    assert.equal(ambiguous.length, 1);
    assert.match(ambiguous[0].reason, /matches 2 lines today \(3, 4\)/);
    assert.equal(outcome.coverage.needles.ambiguous, 1);
    assert.equal(
      outcome.coverage.comparison.unchanged,
      0,
      "an ambiguous needle anchors nothing, so it compares to nothing",
    );
    assert.equal(outcome.coverage.comparison.moved, 0);
  });
});

test("F19: an entry with NO needles contributes nothing and is COUNTED in the coverage gap — the opt-in property", () => {
  withFixtureRepo((fx) => {
    needleFixture(fx, { needles: [{ id: "emit", needle: "Raise()" }], shift: 7 });
    const outcome = runNeedleReview({ repoRoot: fx.root });

    assert.equal(outcome.coverage.entries.total, 2);
    assert.equal(outcome.coverage.entries.needled, 1);
    assert.equal(outcome.coverage.entries.bare, 1);
    assert.ok(
      outcome.rows.every((r) => r.path.endsWith("/Q.cs")),
      "no row may name the un-needled entry: a citation with no needle stays FILE-level, which is how " +
        "'no blanket line-number validation' is a property of the code rather than a promise",
    );
    assert.match(formatNeedleCoverage(outcome.coverage), /1 carry needle\(s\), 1 do not/);
  });
});

test("F20: a citation span past the end of the file is CITATION-OUT-OF-RANGE; one holding a needle is confirmed", () => {
  withFixtureRepo((fx) => {
    needleFixture(fx, {
      needles: [{ id: "emit", needle: "Raise()" }],
      cite: "// good: Q.cs:4, past the end: Q.cs:900-902, and no needle there: Q.cs:2\n",
    });
    const outcome = runNeedleReview({ repoRoot: fx.root });

    const out = needleRows(outcome, NEEDLE_CLASS.OUT_OF_RANGE);
    assert.equal(out.length, 1);
    assert.match(out[0].reason, /Q\.cs:900-902 cited from src:src\/CcpClient\.Desktop\/Citer\.cs:1, but the file has \d+ lines/);
    assert.equal(outcome.coverage.citations.confirmed, 1, "Q.cs:4 holds the needle");
    assert.equal(outcome.coverage.citations.uncovered, 1, "Q.cs:2 holds no needle, and that is a COUNTER, never a row");
    assert.equal(outcome.coverage.citations.outOfRange, 1);
  });
});

test("F21: a BARE :NNN continuation is counted and NEVER resolved to a file — the measured-and-rejected heuristic", () => {
  withFixtureRepo((fx) => {
    needleFixture(fx, {
      needles: [{ id: "emit", needle: "Raise()" }],
      // One file-qualified citation, then three bare continuations, then four NON-citations
      // the rule must exclude: a host port, a ratio, a timestamp and a URL port.
      cite: "// Q.cs:4 and (:900) and `:12-13` and at :7 — host:80, 1:3, 12:34:56, https://x.example:8080\n",
    });
    const outcome = runNeedleReview({ repoRoot: fx.root });

    assert.equal(
      outcome.coverage.citations.bare,
      3,
      "three bare continuations; the port, ratio, timestamp and URL port are excluded by the lookbehind",
    );
    assert.equal(
      needleRows(outcome, NEEDLE_CLASS.OUT_OF_RANGE).length,
      0,
      "the bare (:900) is past the end of the file and STILL produces no row: it names no file, so it is never bound",
    );
    assert.match(formatNeedleCoverage(outcome.coverage), /NOT CHECKED, in either mode/);
  });
});

test("F22: a needle absent at the ENDPOINT is counted as uncomparable, never reported as unmoved", () => {
  withFixtureRepo((fx) => {
    needleFixture(fx, {
      needles: [{ id: "fresh", needle: "AddedLater()" }],
      rewrite: ["namespace X;", "class Y {", "  void A() { AddedLater(); }", "}", ""],
    });
    const outcome = runNeedleReview({ repoRoot: fx.root });

    assert.equal(outcome.rows.length, 0, "a subject that did not exist at the endpoint has not moved");
    assert.equal(outcome.coverage.comparison.absentAtEndpoint, 1);
    assert.equal(outcome.coverage.comparison.unchanged, 0, "uncomparable is NOT unchanged, and the block says so");
    assert.match(formatNeedleCoverage(outcome.coverage), /CANNOT be compared, and are not "unmoved"/);
  });
});

test("F23: a NON-EMPTY needle review list exits 0, and --until is rejected rather than silently ignored", () => {
  withFixtureRepo((fx) => {
    needleFixture(fx, { needles: [{ id: "emit", needle: "Raise()" }], shift: 7 });

    const run = fx.cli(["--needles"]);
    assert.equal(run.status, 0, "a review list with rows in it is the tool working, not the tool failing");
    assert.match(run.stdout, /NEEDLE-MOVED \(1\)/);
    assert.match(run.stdout, /This is a REVIEW LIST, not a failure/);

    const rejected = fx.cli(["--needles", "--until", "HEAD"]);
    assert.equal(rejected.status, 1);
    assert.match(rejected.stderr, /--until has no meaning with --needles/);
    assert.equal(rejected.stdout, "", "no review list on a broken invocation, ever");
  });
});

test("F24: the coverage block is printed by BOTH modes, every run", () => {
  withFixtureRepo((fx) => {
    needleFixture(fx, { needles: [{ id: "emit", needle: "Raise()" }], shift: 7 });

    for (const args of [[], ["--needles"]]) {
      const run = fx.cli(args);
      assert.equal(run.status, 0, `mode ${JSON.stringify(args)} must exit 0`);
      assert.match(run.stdout, /## NEEDLE COVERAGE/, `mode ${JSON.stringify(args)} must print the coverage block`);
      assert.match(
        run.stdout,
        /bare :NNN continuations: \d+ in the port/,
        "the gap is printed as a NUMBER, never implied away",
      );
      assert.match(run.stdout, /stay FILE-level/, "the un-needled remainder must be named in both modes");
    }
  });
});

// ==================================================== F25-F29 the regenerate mode
//
// THE THIRD MODE, AND THE ONLY ONE THAT WRITES. `changedAtSync` was hand-maintained and it is
// what the default mode GATES on (NEEDS-VERDICT's changed-in-window gate, DELTA-MISMATCH's whole
// comparison), so a slip there shrinks the review list silently. These five facts pin the two
// halves that can go wrong quietly: the RECOMPUTATION must come from git rather than from the
// recorded value (F25, F26), and the WRITE must move nothing except the value spans it owns
// (F27, F28) while leaving the default mode's own comparison satisfied (F29).
//
// Fixtures are temp-dir repositories exactly as F1-F24. Nothing here reads the real inventory.

/** A repo with four subjects and a known window:
 *    R1 changes and the inventory records a WRONG delta      -> must be REWRITTEN from git
 *    R2 changes and the inventory records null               -> must be ADDED
 *    R3 does NOT change and the inventory records a delta    -> must be DROPPED
 *    R4 changes and the inventory already records the truth  -> must move nothing at all
 *  Returns the window plus the real numstat of each changed subject, so every expectation is a
 *  known answer taken from git rather than a number typed into the fact. */
function regenFixture(fx) {
  fx.write("ConditioningControlPanel/Services/R1.cs", "a\nb\nc\n");
  fx.write("ConditioningControlPanel/Services/R2.cs", "a\n");
  fx.write("ConditioningControlPanel/Services/R3.cs", "a\n");
  fx.write("ConditioningControlPanel/Services/R4.cs", "a\n");
  fx.sln();
  const since = fx.commit("base");
  fx.write("ConditioningControlPanel/Services/R1.cs", "a\nX\nY\nZ\nc\n");
  fx.write("ConditioningControlPanel/Services/R2.cs", "a\nb\n");
  // R3 is deliberately untouched in the window.
  fx.write("ConditioningControlPanel/Services/R4.cs", "a\nb\nc\n");
  const until = fx.commit("head");

  const n = (name) => fx.numstat(since, until, `ConditioningControlPanel/Services/${name}.cs`);
  const r1 = n("R1");
  const r2 = n("R2");
  const r4 = n("R4");
  const sync = fx.commitDate(until);
  const entry = (name, changedAtSync) => ({
    path: `ConditioningControlPanel/Services/${name}.cs`,
    tier: 1,
    citedBy: [],
    changedAtSync,
    verdict: "reviewed at the fixture sync, no parity impact",
  });

  fx.inventory({
    schemaVersion: 1,
    baseline: baseline(since, until),
    entries: [
      // A recorded delta that is wrong in BOTH numbers and carries a stale hand-typed sync date.
      entry("R1", { sync: "1999-01-01", status: "M", add: r1.add + 11, del: r1.del + 7 }),
      entry("R2", null),
      entry("R3", { sync: "1999-01-01", status: "M", add: 5, del: 5 }),
      entry("R4", { sync, status: "M", add: r4.add, del: r4.del }),
    ],
  });
  return { since, until, sync, r1, r2, r4 };
}

const regenRows = (outcome, cls) => outcome.rows.filter((r) => r.cls === cls);

test("F25: regenerate recomputes from GIT, never from the recorded value — a wrong delta and a stale sync date are both replaced", () => {
  withFixtureRepo((fx) => {
    const { until, sync, r1 } = regenFixture(fx);
    const outcome = runRegenerate({ repoRoot: fx.root });

    const rewritten = regenRows(outcome, REGEN_CLASS.REWRITTEN);
    assert.equal(rewritten.length, 1, "only R1's delta disagrees with git");
    const row = rewritten[0];
    assert.equal(row.path, "ConditioningControlPanel/Services/R1.cs");

    // The recorded value is CARRIED, not consulted: the regenerated half comes from numstat.
    assert.equal(row.recorded.add, r1.add + 11);
    assert.equal(row.recorded.del, r1.del + 7);
    assert.deepEqual(row.regenerated, { sync, status: "M", add: r1.add, del: r1.del });

    // `sync` is DERIVED from the window's until endpoint, so a hand-typed date cannot survive.
    assert.notEqual(sync, "1999-01-01");
    assert.equal(sync, fx.commitDate(until));
    assert.equal(outcome.window.syncDate, sync);

    // A verdict reasoning over a delta this pass changes is NAMED, never resolved.
    assert.equal(row.verdictAtRisk, true, "the row must flag the verdict the change puts at risk");
    assert.equal(outcome.summary.tier1.total, 4, "the tier-1 count is reported as the judgment work left");
  });
});

test("F26: regenerate ADDS a delta recorded as null and DROPS one the window does not contain, and leaves an exact match alone", () => {
  withFixtureRepo((fx) => {
    const { sync, r2 } = regenFixture(fx);
    const outcome = runRegenerate({ repoRoot: fx.root });

    const added = regenRows(outcome, REGEN_CLASS.ADDED);
    assert.equal(added.length, 1, "R2 changed in the window but was recorded as unchanged");
    assert.equal(added[0].path, "ConditioningControlPanel/Services/R2.cs");
    assert.equal(added[0].recorded, null);
    assert.deepEqual(added[0].regenerated, { sync, status: "M", add: r2.add, del: r2.del });

    const dropped = regenRows(outcome, REGEN_CLASS.DROPPED);
    assert.equal(dropped.length, 1, "R3 has no numstat row at its own path in this window");
    assert.equal(dropped[0].path, "ConditioningControlPanel/Services/R3.cs");
    assert.equal(dropped[0].regenerated, null);

    assert.ok(
      !outcome.rows.some((r) => r.path.endsWith("R4.cs")),
      "an entry already agreeing with git must move NOTHING — silence on the happy path",
    );
    assert.equal(outcome.summary.moved, 3);
    assert.equal(outcome.summary.unchanged, 1);
    assert.equal(outcome.summary.changedAfter, 3, "R1, R2 and R4 changed in the window; R3 did not");
    assert.match(formatRegenerateReport(outcome), /DELTA-DROPPED \(1\)/);
  });
});

test("F27: nothing is written without --write, the flag is rejected on its own, and a non-empty change list still exits 0", () => {
  withFixtureRepo((fx) => {
    regenFixture(fx);
    const before = fx.inventoryBytes();

    const dry = fx.cli(["--regenerate"]);
    assert.equal(dry.status, 0, "a long change list is the tool working, not the tool failing");
    assert.match(dry.stdout, /NOT WRITTEN: pass --write to apply this/);
    assert.equal(fx.inventoryBytes(), before, "the review pass must not touch a single byte");

    const applied = fx.cli(["--regenerate", "--write"]);
    assert.equal(applied.status, 0);
    assert.match(applied.stdout, /WRITTEN: /);
    assert.notEqual(fx.inventoryBytes(), before, "--write must actually apply the regeneration");

    // Both misuses are REJECTED rather than resolved by precedence, and one of them authorises a write.
    const lone = fx.cli(["--write"]);
    assert.equal(lone.status, 1);
    assert.match(lone.stderr, /--write only means something with --regenerate/);
    assert.equal(lone.stdout, "", "no report on a broken invocation, ever");

    const both = fx.cli(["--regenerate", "--needles"]);
    assert.equal(both.status, 1);
    assert.match(both.stderr, /--regenerate and --needles are different modes/);
  });
});

test("F28: the write is SURGICAL — CRLF, indentation, hand-inlined records and every unrelated byte survive it", () => {
  withFixtureRepo((fx) => {
    fx.write("ConditioningControlPanel/Services/R1.cs", "a\nb\nc\n");
    fx.sln();
    const since = fx.commit("base");
    fx.write("ConditioningControlPanel/Services/R1.cs", "a\nX\nY\nZ\nc\n");
    const until = fx.commit("head");
    const r1 = fx.numstat(since, until, "ConditioningControlPanel/Services/R1.cs");
    const sync = fx.commitDate(until);

    // Hand formatting the real inventory carries and a plain re-serialisation destroys: CRLF
    // endings, and a needle record written inline on one line rather than exploded over three.
    const handWritten = (changedAtSync) =>
      [
        "{",
        '  "schemaVersion": 1,',
        '  "baseline": {',
        `    "merge": "${until}",`,
        '    "previous": {',
        `      "merge": "${since}"`,
        "    }",
        "  },",
        '  "entries": [',
        "    {",
        '      "path": "ConditioningControlPanel/Services/R1.cs",',
        '      "tier": 1,',
        '      "citedBy": [],',
        `      "changedAtSync": ${changedAtSync},`,
        '      "needles": [',
        '        { "id": "anchor", "needle": "class Y" }',
        "      ],",
        '      "verdict": "ok"',
        "    }",
        "  ]",
        "}",
        "",
      ].join("\r\n");

    const before = handWritten("null");
    fx.rawInventory(before);
    const outcome = runRegenerate({ repoRoot: fx.root, write: true });
    const after = fx.inventoryBytes();

    // A byte-exact known answer: the regenerated object at the key's OWN indent, in the file's
    // OWN line ending, and nothing else anywhere in the file.
    const expectedValue = [
      "{",
      `        "sync": "${sync}",`,
      '        "status": "M",',
      `        "add": ${r1.add},`,
      `        "del": ${r1.del}`,
      "      }",
    ].join("\r\n");
    assert.equal(after, handWritten(expectedValue), "only the changedAtSync value span may differ");
    assert.equal(outcome.summary.written, true);

    assert.ok(
      after.includes('        { "id": "anchor", "needle": "class Y" }'),
      "the hand-inlined needle record must survive verbatim — JSON.stringify would explode it",
    );
    assert.ok(!/[^\r]\n/.test(after), "every newline must still be CRLF");
    assert.ok(after.includes('"verdict": "ok"'), "the verdict is not this mode's to touch");
    assert.ok(after.includes(`"merge": "${until}"`), "baseline is not this mode's to touch either");

    // The mapping from value spans to entries is only sound while every entry carries the key,
    // so an inventory missing one is REFUSED and nothing reaches disk.
    const missingKey = before.replace('      "changedAtSync": null,\r\n', "");
    fx.rawInventory(missingKey);
    assert.throws(
      () => runRegenerate({ repoRoot: fx.root, write: true }),
      /every entry must carry the key/,
      "an entry with no changedAtSync key must stop the write, not be silently appended to",
    );
    assert.equal(fx.inventoryBytes(), missingKey, "a refused regeneration writes nothing");
  });
});

test("F29: a regenerated inventory leaves DELTA-MISMATCH silent — the two modes agree by construction, not by luck", () => {
  withFixtureRepo((fx) => {
    regenFixture(fx);

    const before = runDetector({ repoRoot: fx.root });
    assert.ok(
      before.summary.deltaMismatch > 0,
      "the fixture must start with a real disagreement, or this fact would pass vacuously",
    );

    const outcome = runRegenerate({ repoRoot: fx.root, write: true });
    assert.equal(outcome.summary.written, true);

    const after = runDetector({ repoRoot: fx.root });
    assert.equal(
      after.summary.deltaMismatch,
      0,
      "regenerate reads add/del through readNumstat(), the same call DELTA-MISMATCH re-measures with, " +
        "so a regenerated inventory cannot leave that class firing",
    );
    assert.equal(rowsOf(after, CLASS.DELTA_MISMATCH).length, 0);

    // And running it twice is a no-op: the second pass has nothing left to move.
    const again = runRegenerate({ repoRoot: fx.root, write: true });
    assert.equal(again.summary.moved, 0);
    assert.equal(again.summary.written, false, "an inventory that already agrees with git is not rewritten");
  });
});

// ============ F30-F32 the regenerate mode's NAMED refusals and its one COUNTED ceiling
//
// F25-F29 pin what the mode COMPUTES and what it WRITES. They do not pin the four places it
// refuses to run or counts what it could not do, and three of those four were measured DELETABLE
// against a fully green suite: the rename counter (:1407-1410) could return nothing, the BINARY
// refusal (:1344-1350) could be `if (false)`, and the writer's own re-parse-and-compare
// (:1383-1401) could be `if (false)`, each leaving 30 of 30 facts passing. That is exactly the
// shape F11/F12/F13 (the default mode's three refusals) and F19 (its gap counter) exist to
// prevent, so the same standard applies here. The fourth — a numstat row with no name-status row
// at the same path (:1352-1358) — is left uncounted on purpose: both git calls share a range and
// a pathspec, so no fixture reaches it. That reasoning lives in its comment, not in a fact that
// could only assert vacuously.

test("F30: an entry RENAMED INTO its recorded path regenerates to null AND is named in the summary — the mode's one ceiling is counted, never silent", () => {
  withFixtureRepo((fx) => {
    // The subject starts at a DIFFERENT path and is moved onto the inventory's recorded path
    // inside the window. Measured on this fixture shape: plain `git diff --numstat` keys that row
    // by its combined path (`0\t0\t...Services/{Old.cs => Renamed.cs}`) while `--name-status`
    // keys it `R100` by the DESTINATION, so there is no numstat row at the destination and the
    // entry regenerates to null.
    fx.write("ConditioningControlPanel/Services/Old.cs", "a\nb\nc\nd\ne\nf\ng\nh\n");
    fx.write("ConditioningControlPanel/Services/Plain.cs", "a\n");
    fx.sln();
    const since = fx.commit("base");
    // Rename DETECTION is git's own default and the ceiling only exists while it is on, so the
    // fixture pins it repo-locally instead of inheriting whatever the machine's global config says.
    execFileSync("git", ["-C", fx.root, "config", "diff.renames", "true"], { stdio: ["ignore", "pipe", "pipe"] });
    fs.renameSync(
      path.join(fx.root, "ConditioningControlPanel", "Services", "Old.cs"),
      path.join(fx.root, "ConditioningControlPanel", "Services", "Renamed.cs"),
    );
    fx.write("ConditioningControlPanel/Services/Plain.cs", "a\nb\n");
    const until = fx.commit("head");
    const plain = fx.numstat(since, until, "ConditioningControlPanel/Services/Plain.cs");
    const sync = fx.commitDate(until);

    const renamed = "ConditioningControlPanel/Services/Renamed.cs";
    fx.inventory({
      schemaVersion: 1,
      baseline: baseline(since, until),
      entries: [
        {
          path: renamed,
          tier: 1,
          citedBy: [],
          changedAtSync: { sync: "1999-01-01", status: "M", add: 3, del: 1 },
          verdict: "reviewed at the fixture sync, no parity impact",
        },
        { path: "ConditioningControlPanel/Services/Plain.cs", tier: 1, citedBy: [], changedAtSync: null, verdict: "" },
      ],
    });

    const outcome = runRegenerate({ repoRoot: fx.root });

    // Half one: the ceiling is REAL. A recorded delta is dropped from an entry that did change
    // upstream — it changed under its old name.
    const dropped = regenRows(outcome, REGEN_CLASS.DROPPED);
    assert.equal(dropped.length, 1, "the rename destination has no numstat row at its own path");
    assert.equal(dropped[0].path, renamed);
    assert.equal(dropped[0].regenerated, null);

    // Half two, and it is the half a deleted counter kills silently: the summary NAMES the entry
    // it just nulled. A 0 here hands the operator a dropped delta and no reason for it, which is
    // the "silently shrinks the review list" failure this whole file exists to prevent.
    assert.deepEqual(outcome.summary.renameDestinations, [renamed]);
    const report = formatRegenerateReport(outcome);
    assert.match(report, /RENAME DESTINATION in this window: 1/);
    assert.ok(report.includes(`      ${renamed}`), "counting them is not enough — the report must NAME them");

    // The control: an ordinary modification in the SAME window regenerates normally and is not
    // swept into the rename count.
    const added = regenRows(outcome, REGEN_CLASS.ADDED);
    assert.equal(added.length, 1);
    assert.equal(added[0].path, "ConditioningControlPanel/Services/Plain.cs");
    assert.deepEqual(added[0].regenerated, { sync, status: "M", add: plain.add, del: plain.del });
  });
});

test("F31: a BINARY numstat row at an entry's path is a could-not-run condition, never a zero to record", () => {
  withFixtureRepo((fx) => {
    fx.write("ConditioningControlPanel/Services/B1.cs", "plain text\n");
    fx.sln();
    const since = fx.commit("base");
    // REACHABILITY, not a hypothetical row shape: a .cs carrying a NUL byte is BINARY to git, and
    // `git diff --numstat` prints `-\t-\t<path>` for the change. The fact is only non-vacuous
    // because this fixture actually produces that row — the refusal below fires on nothing else.
    fx.write("ConditioningControlPanel/Services/B1.cs", "plain" + String.fromCharCode(0) + "text\n");
    const until = fx.commit("head");

    fx.inventory({
      schemaVersion: 1,
      baseline: baseline(since, until),
      entries: [
        {
          path: "ConditioningControlPanel/Services/B1.cs",
          tier: 1,
          citedBy: [],
          changedAtSync: null,
          verdict: "reviewed at the fixture sync, no parity impact",
        },
      ],
    });
    const before = fx.inventoryBytes();

    assert.throws(
      () => runRegenerate({ repoRoot: fx.root, write: true }),
      /has a BINARY numstat row/,
      "an inventory entry names a WPF source file, so `-` add/del is a broken run, not a delta",
    );
    assert.equal(fx.inventoryBytes(), before, "a refused regeneration writes nothing");

    // WHY IT MUST REFUSE RATHER THAN RECORD WHAT IT READ, and why no downstream guard would catch
    // it: `Number("-")` is NaN, JSON.stringify turns NaN into `null`, so the writer's own
    // re-parse-and-compare (:1383-1401) compares EQUAL with `"add": null, "del": null` on BOTH
    // sides. MEASURED by removing the refusal on this fixture: the run does not fail, it reports
    // written = true and leaves `"add": null, "del": null` in the inventory it just wrote.
    assert.equal(JSON.stringify({ add: Number("-") }), '{"add":null}');
  });
});

test("F32: value spans out of step with the entry order are REFUSED by the writer's own re-parse, and nothing reaches disk", () => {
  withFixtureRepo((fx) => {
    fx.write("ConditioningControlPanel/Services/W1.cs", "a\nb\nc\n");
    fx.write("ConditioningControlPanel/Services/W2.cs", "a\n");
    fx.sln();
    const since = fx.commit("base");
    fx.write("ConditioningControlPanel/Services/W1.cs", "a\nX\nY\nc\n");
    // W2 is deliberately untouched in the window, so its regenerated value is null.
    const until = fx.commit("head");
    const w1 = fx.numstat(since, until, "ConditioningControlPanel/Services/W1.cs");
    const sync = fx.commitDate(until);

    // TWO `"changedAtSync":` keys and TWO entries, so the COUNT guard F28 pins (:1269-1275) is
    // SATISFIED — and the mapping is still wrong, because one of those keys is not an entry's: it
    // sits in `baseline`, and the second entry carries no key at all. Counting spans cannot see
    // this; only re-reading the produced bytes can.
    const before = [
      "{",
      '  "schemaVersion": 1,',
      '  "baseline": {',
      `    "merge": "${until}",`,
      '    "previous": {',
      `      "merge": "${since}"`,
      "    },",
      '    "changedAtSync": null',
      "  },",
      '  "entries": [',
      "    {",
      '      "path": "ConditioningControlPanel/Services/W1.cs",',
      '      "tier": 1,',
      '      "citedBy": [],',
      '      "changedAtSync": null,',
      '      "verdict": "ok"',
      "    },",
      "    {",
      '      "path": "ConditioningControlPanel/Services/W2.cs",',
      '      "tier": 2,',
      '      "citedBy": [],',
      '      "verdict": "ok"',
      "    }",
      "  ]",
      "}",
      "",
    ].join("\n");
    fx.rawInventory(before);

    // The surgical writer CANNOT see the misalignment. Driven directly — it is exported at :1267
    // for exactly this — it writes the entry's regenerated delta into `baseline`, the one record this
    // mode swears it never touches, and nulls the entry the delta belonged to.
    const delta = { sync, status: "M", add: w1.add, del: w1.del };
    const misaligned = JSON.parse(rewriteChangedAtSync(before, [delta, null]));
    assert.deepEqual(misaligned.baseline.changedAtSync, delta, "the first span is baseline's, not the entry's");
    assert.equal(misaligned.entries[0].changedAtSync, null, "so every value lands one span early");

    // Which makes the re-parse-and-compare the ONLY thing between that and a tracked document.
    assert.throws(
      () => runRegenerate({ repoRoot: fx.root, write: true }),
      /did not reproduce the intended inventory/,
      "a rewrite that fell out of step with the entry order is a could-not-run condition",
    );
    assert.equal(fx.inventoryBytes(), before, "never a partial write — not one byte");
  });
});
