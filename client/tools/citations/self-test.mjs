#!/usr/bin/env node
// self-test.mjs — the facts that bind detect.mjs (SP-088).
//
// WHY THIS EXISTS, AND WHY IT IS NOT IN THE .NET SUITE
// client/tests/floor/check-floor.mjs discovers only csproj entries under tests/ in
// client/CcpClient.sln (:80-107) and runs them (:253). A node file under client/tools/
// is invisible to it. This packet's file scope is client/tools/citations/** and the
// wave's scopes are pairwise disjoint, so the one-line .cs bridge that would put these
// facts on the floor belongs to a different packet. See the SCOPE PROBLEM section of
// spine-tasks/SP-088-upstream-citation-drift-detector/record.md: NO STANDING GATE IN
// THIS REPOSITORY RUNS THIS FILE.
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
// Up to eight lanes share os.tmpdir() with SP-056's fixtures. Each fixture directory is
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

import { CLASS, REASON, TREE_CCP, TREE_SHIPPING, runDetector, UNREVIEWED_SENTINEL } from "./detect.mjs";

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
