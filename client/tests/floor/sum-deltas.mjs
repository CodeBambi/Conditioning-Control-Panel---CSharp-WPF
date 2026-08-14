#!/usr/bin/env node
// Wave-land delta summer for the client test-floor pin (client/tests/floor/floor.json).
//
// WHY THIS EXISTS: floor.json's bumpRule requires `total` to move in the SAME commit as the
// test change that moves it. With eight parallel worktree lanes per wave, that single line
// collides every wave, and the reflexive resolution — set `total` to whatever the runner just
// observed — recreates the exact vacuous-green failure the pin exists to prevent (SP-062's
// 896/1 green; SP-065/SP-066 framing e). So lanes stop touching floor.json at all: each packet
// DECLARES its delta in its own packet folder as spine-tasks/<packet>/floor-delta.json, and
// the land sums the declarations and applies ONE bump.
//
// A declaration is a CLAIM, not a measurement. This script never looks at an observed test
// count — check-floor.mjs remains the only thing that compares the pin to reality, and it
// still runs after the bump. The two numbers stay independently produced, so a lane that
// declares the wrong delta fails at the land instead of quietly re-pinning the suite to
// whatever it happened to run. Nothing here can be used to make a red suite green.
//
// Delta file shape (fixed):
//   { "packet": "SP-073-example-slug", "unit": 5, "headless": 0, "reason": "one line" }
// `unit` targets CcpClient.Tests, `headless` targets CcpClient.HeadlessTests. Both are
// integers and MAY be negative (a packet can delete tests). A packet that adds no tests
// declares zero — it does not omit the file, because a missing declaration and a zero
// declaration are indistinguishable to the land, and only one of them is a decision.
//
// Usage:
//   node client/tests/floor/sum-deltas.mjs [--check | --apply] [--packets <a,b,c>]
//     --check   (default) print base totals, each packet's contribution, expected totals.
//     --apply   write the summed totals into floor.json and regenerate lastMovedBy.
//     --packets restrict the sum to the named packet directories — the land case, where only
//               THIS wave's packets contribute. Without it every delta file on disk is summed,
//               which is correct only if consumed deltas are deleted in the landing commit;
//               that path prints a loud warning naming everything it summed.
//
// Exit 0 = sum reported (or written). Exit 1 = any violation (each prints a loud named reason).

import fs from "node:fs";
import path from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";

const HERE = path.dirname(fileURLToPath(import.meta.url));
const CLIENT = path.resolve(HERE, "..", "..");
const REPO = path.resolve(CLIENT, "..");
const PIN_PATH = path.join(HERE, "floor.json");
const SLN_PATH = path.join(CLIENT, "CcpClient.sln");
const SPINE_TASKS = path.join(REPO, "spine-tasks");

// The delta file speaks the lane's vocabulary; the pin speaks project names. This map is the
// only place the two meet, so a renamed test project breaks here loudly instead of silently
// summing into a project nobody pins.
export const DELTA_FIELD_TO_PROJECT = Object.freeze({
  unit: "CcpClient.Tests",
  headless: "CcpClient.HeadlessTests",
});

// Same ID rule as FloorWrapperGuardTests.cs: a packet directory is SP-<number>-<slug>.
const PACKET_DIR_RE = /^SP-(\d+)-/i;

export class FloorError extends Error {}

function fail(message) {
  throw new FloorError(message);
}

function signed(n) {
  return n >= 0 ? `+${n}` : `${n}`;
}

export function readPin(pinPath = PIN_PATH) {
  let raw;
  try {
    raw = fs.readFileSync(pinPath, "utf8");
  } catch {
    fail(`pin file missing at ${pinPath} — the sum refuses to run unpinned`);
  }
  let pin;
  try {
    pin = JSON.parse(raw);
  } catch (err) {
    fail(`pin file ${pinPath} is not parseable JSON: ${err.message}`);
  }
  if (!pin || typeof pin !== "object" || !pin.projects || typeof pin.projects !== "object") {
    fail(`pin file ${pinPath} has no "projects" object`);
  }
  for (const [field, project] of Object.entries(DELTA_FIELD_TO_PROJECT)) {
    const entry = pin.projects[project];
    if (!entry || typeof entry !== "object" || !Number.isInteger(entry.total)) {
      fail(`pin file ${pinPath} has no integer "total" for ${project} — the delta field "${field}" names a project the pin does not carry; a sum into an unpinned project moves nothing`);
    }
  }
  return { pin, raw };
}

// Packet enumeration mirrors FloorWrapperGuardTests.cs: declarations live EXACTLY one level
// below spine-tasks/ (anything nested deeper is packet evidence or a review artifact, never a
// declaration), a missing spine-tasks/ is a failure rather than a silent zero, and a directory
// carrying a declaration whose name does not parse as SP-<number>-... fails closed rather than
// escaping the enumeration unnoticed.
export function discoverPackets(spineTasks = SPINE_TASKS) {
  if (!fs.existsSync(spineTasks)) {
    fail(`spine-tasks not found at ${spineTasks} — the delta sum refuses to read a missing queue as "no deltas declared"`);
  }
  const found = [];
  for (const entry of fs.readdirSync(spineTasks, { withFileTypes: true })) {
    if (!entry.isDirectory()) {
      continue;
    }
    const deltaPath = path.join(spineTasks, entry.name, "floor-delta.json");
    if (!fs.existsSync(deltaPath)) {
      continue;
    }
    if (!PACKET_DIR_RE.test(entry.name)) {
      fail(`${deltaPath}: packet directory "${entry.name}" does not parse as SP-<number>-... — the delta sum refuses to go blind on an unparseable packet ID (mirrors the FloorWrapperGuardTests enumeration)`);
    }
    found.push({ packet: entry.name, path: deltaPath });
  }
  found.sort((a, b) => a.packet.localeCompare(b.packet));
  return found;
}

// Every field is required and every field is checked. A delta the land cannot read is not a
// delta of zero, so nothing here degrades to a default.
export function parseDelta(packetName, deltaPath, text) {
  let delta;
  try {
    delta = JSON.parse(text);
  } catch (err) {
    fail(`${deltaPath}: not parseable JSON: ${err.message} — an unreadable declaration is not a declaration of zero`);
  }
  if (delta === null || typeof delta !== "object" || Array.isArray(delta)) {
    fail(`${deltaPath}: top level is ${delta === null ? "null" : Array.isArray(delta) ? "an array" : `a ${typeof delta}`}, expected a JSON object with packet/unit/headless/reason`);
  }
  for (const key of ["packet", "unit", "headless", "reason"]) {
    if (!Object.hasOwn(delta, key)) {
      const why = key === "unit" || key === "headless"
        ? "a packet that adds no tests to that project declares 0, it does not omit the field — an omitted count and a zero are indistinguishable to the land, and only one of them is a decision"
        : "every declaration carries packet, unit, headless and reason; the land will not infer a field it was not given";
      fail(`${deltaPath}: missing required field "${key}" — ${why}`);
    }
  }
  if (typeof delta.packet !== "string" || delta.packet.trim().length === 0) {
    fail(`${deltaPath}: "packet" is ${JSON.stringify(delta.packet)}, expected a non-empty string naming the packet directory`);
  }
  for (const key of ["unit", "headless"]) {
    if (!Number.isInteger(delta[key])) {
      fail(`${deltaPath}: "${key}" is ${JSON.stringify(delta[key])}, expected an integer — negative is legal (a packet may delete tests), but a string, a float, or null is a declaration nobody can sum`);
    }
  }
  if (typeof delta.reason !== "string" || delta.reason.trim().length === 0) {
    fail(`${deltaPath}: "reason" is empty or whitespace — floor.json's lastMovedBy line is generated from it, and an unexplained bump is exactly how a wrong pin survives review`);
  }
  if (delta.packet !== packetName) {
    fail(`${deltaPath}: "packet" is "${delta.packet}" but the containing directory is "${packetName}" — a delta file copy-pasted from another packet would otherwise be summed under the wrong name with the wrong reason, and the count it carries belongs to a different set of tests`);
  }
  return {
    packet: packetName,
    path: deltaPath,
    unit: delta.unit,
    headless: delta.headless,
    reason: delta.reason.trim(),
  };
}

export function selectPackets(discovered, selected) {
  if (selected === null) {
    return discovered;
  }
  const byName = new Map(discovered.map((d) => [d.packet, d]));
  const missing = selected.filter((n) => !byName.has(n));
  if (missing.length > 0) {
    fail(
      `--packets names packet(s) with no spine-tasks/<packet>/floor-delta.json on disk: ${missing.join(", ")} — ` +
      "a wave land that silently drops an undeclared packet bumps the pin short, and check-floor.mjs then fails " +
      "at the land with a drift nobody can attribute to a lane. Every packet in the wave declares, even a zero."
    );
  }
  return selected.map((n) => byName.get(n));
}

export function sumDeltas(pin, deltas) {
  const totals = {};
  for (const [field, project] of Object.entries(DELTA_FIELD_TO_PROJECT)) {
    totals[project] = { field, base: pin.projects[project].total, delta: 0, expected: 0 };
  }
  for (const d of deltas) {
    for (const [field, project] of Object.entries(DELTA_FIELD_TO_PROJECT)) {
      totals[project].delta += d[field];
    }
  }
  for (const [project, t] of Object.entries(totals)) {
    t.expected = t.base + t.delta;
    if (t.expected <= 0) {
      const blame = deltas.map((d) => `${d.packet} ${signed(d[t.field])}`).join(", ") || "no packets";
      fail(
        `${project}: the summed total would be ${t.expected} (pin ${t.base}, deltas ${signed(t.delta)} from ${blame}) — ` +
        "a floor of zero or less makes \"no tests ran\" read as green, the exact shape SP-065 exists to prevent. " +
        "At least one declaration is wrong."
      );
    }
  }
  return totals;
}

// Position-aware locator for exactly one value inside the pin's RAW text. We deliberately do
// not reserialize with JSON.stringify: floor.json is CRLF, hand-formatted, and carries prose
// fields (skipSemantics, admissionRule, bumpRule, the lastMovedBy history chain) that packets
// and reviewers cite verbatim. A reserialize silently rewrites line endings and reformats
// whatever the parser normalizes, turning a two-number bump into a whole-file diff nobody can
// review. Splicing the exact span of the value we mean to move keeps every other byte
// identical by construction, allowedSkips included.
export function locateValue(raw, pathParts) {
  let i = 0;
  const bad = (msg) => fail(`floor.json: ${msg} at offset ${i} — the pin locator refuses to guess where a value starts`);
  const skipWs = () => {
    while (i < raw.length && (raw[i] === " " || raw[i] === "\t" || raw[i] === "\r" || raw[i] === "\n")) i++;
  };
  const readString = () => {
    if (raw[i] !== '"') bad("expected a string");
    i++;
    while (i < raw.length) {
      const c = raw[i];
      if (c === '"') {
        i++;
        return;
      }
      i += c === "\\" ? (raw[i + 1] === "u" ? 6 : 2) : 1;
    }
    bad("unterminated string");
  };
  const readKey = () => {
    const start = i;
    readString();
    return JSON.parse(raw.slice(start, i));
  };
  const skipValue = () => {
    skipWs();
    const c = raw[i];
    if (c === '"') {
      readString();
      return;
    }
    if (c === "{" || c === "[") {
      const close = c === "{" ? "}" : "]";
      i++;
      for (;;) {
        skipWs();
        if (i >= raw.length) bad("unterminated object or array");
        if (raw[i] === close) {
          i++;
          return;
        }
        if (raw[i] === ",") {
          i++;
          continue;
        }
        if (c === "{") {
          readKey();
          skipWs();
          if (raw[i] !== ":") bad("expected ':' after an object key");
          i++;
        }
        skipValue();
      }
    }
    const m = /^(-?\d+(\.\d+)?([eE][+-]?\d+)?|true|false|null)/.exec(raw.slice(i));
    if (!m) bad("expected a JSON value");
    i += m[0].length;
  };
  const descend = (parts) => {
    skipWs();
    if (raw[i] !== "{") bad(`expected an object while walking to ${pathParts.join(".")}`);
    i++;
    for (;;) {
      skipWs();
      if (i >= raw.length) bad("unterminated object");
      if (raw[i] === "}") return null;
      if (raw[i] === ",") {
        i++;
        continue;
      }
      const key = readKey();
      skipWs();
      if (raw[i] !== ":") bad("expected ':' after an object key");
      i++;
      skipWs();
      if (key === parts[0]) {
        if (parts.length === 1) {
          const start = i;
          skipValue();
          return { start, end: i };
        }
        return descend(parts.slice(1));
      }
      skipValue();
    }
  };
  const span = descend(pathParts);
  if (!span) {
    fail(`floor.json: no value at path ${pathParts.join(".")} — the pin does not carry the field the sum means to move`);
  }
  return span;
}

// The generated line follows floor.json's own convention: the new statement first, the prior
// statement chained after "Prior:" so the pin keeps its own movement history.
export function buildLastMovedBy(totals, deltas, priorLine) {
  const moves = Object.entries(totals).map(([project, t]) => `${project} ${t.base}->${t.expected}`).join(", ");
  const contributions = deltas
    .map((d) => `${d.packet}: ${signed(d.unit)} unit, ${signed(d.headless)} headless — ${d.reason}`)
    .join("; ");
  const head = `Wave land via sum-deltas --apply, ${deltas.length} packet${deltas.length === 1 ? "" : "s"}: ${moves}. Declared: ${contributions}.`;
  return priorLine ? `${head} Prior: ${priorLine}` : head;
}

export function applyPin(raw, totals, lastMovedBy) {
  let next = raw;
  for (const [project, t] of Object.entries(totals)) {
    const span = locateValue(next, ["projects", project, "total"]);
    next = next.slice(0, span.start) + String(t.expected) + next.slice(span.end);
  }
  const lastSpan = locateValue(next, ["lastMovedBy"]);
  next = next.slice(0, lastSpan.start) + JSON.stringify(lastMovedBy) + next.slice(lastSpan.end);

  // Byte preservation is guaranteed by construction above; these two checks PROVE it rather
  // than assert it, and they run before anything reaches disk. An unreadable pin is worse
  // than a stale one: check-floor.mjs refuses to run unpinned, so a bad write stops the land.
  let reparsed;
  try {
    reparsed = JSON.parse(next);
  } catch (err) {
    fail(`--apply produced JSON that does not round-trip parse: ${err.message} — refusing to write an unreadable pin`);
  }
  const expected = JSON.parse(raw);
  for (const [project, t] of Object.entries(totals)) {
    expected.projects[project].total = t.expected;
  }
  expected.lastMovedBy = lastMovedBy;
  if (JSON.stringify(reparsed) !== JSON.stringify(expected)) {
    fail(
      "--apply produced a pin whose parsed content differs from base+deltas in something other than " +
      "projects.*.total and lastMovedBy (allowedSkips, skipSemantics, admissionRule, bumpRule and key " +
      "order must survive byte-for-byte) — refusing to write"
    );
  }
  return next;
}

export function parseArgs(argv) {
  const USAGE = "usage: node client/tests/floor/sum-deltas.mjs [--check | --apply] [--packets <comma-separated packet dir names>]";
  let mode = null;
  const packetArgs = [];
  let sawPackets = false;
  for (let i = 0; i < argv.length; i++) {
    const arg = argv[i];
    if (arg === "--check" || arg === "--apply") {
      const requested = arg.slice(2);
      if (mode !== null && mode !== requested) {
        fail(`--check and --apply are mutually exclusive — one invocation reports or writes, never both. ${USAGE}`);
      }
      mode = requested;
      continue;
    }
    if (arg === "--packets" || arg.startsWith("--packets=")) {
      const value = arg === "--packets" ? argv[++i] : arg.slice("--packets=".length);
      if (value === undefined) {
        fail(`--packets given with no value — pass the comma-separated packet directory names for THIS wave. ${USAGE}`);
      }
      sawPackets = true;
      packetArgs.push(...value.split(",").map((s) => s.trim()).filter((s) => s.length > 0));
      continue;
    }
    fail(`unrecognised argument "${arg}" — a mistyped flag must never fall through to "sum everything on disk". ${USAGE}`);
  }
  if (sawPackets && packetArgs.length === 0) {
    fail(`--packets names no packets — an empty selection is not "sum everything"; omit the flag deliberately if that is what you mean. ${USAGE}`);
  }
  const dupes = [...new Set(packetArgs.filter((n, idx) => packetArgs.indexOf(n) !== idx))];
  if (dupes.length > 0) {
    fail(`--packets names ${dupes.join(", ")} more than once — a repeated packet is summed twice and double-bumps the pin`);
  }
  return { mode: mode ?? "check", packets: sawPackets ? packetArgs : null };
}

export function main(argv = process.argv.slice(2)) {
  try {
    const { mode, packets: selected } = parseArgs(argv);
    if (!fs.existsSync(SLN_PATH)) {
      fail(`repo anchor ${SLN_PATH} not found — sum-deltas resolves spine-tasks/ relative to its own location and refuses to guess where the repo root is`);
    }
    const { pin, raw } = readPin();
    const discovered = discoverPackets();
    const chosen = selectPackets(discovered, selected);

    const failures = [];
    const deltas = [];
    for (const p of chosen) {
      try {
        deltas.push(parseDelta(p.packet, p.path, fs.readFileSync(p.path, "utf8")));
      } catch (err) {
        if (err instanceof FloorError) {
          failures.push(err.message);
        } else {
          failures.push(`${p.path}: unexpected reader error: ${err.message}`);
        }
      }
    }
    if (failures.length > 0) {
      console.error("SUM-DELTAS FAILED:");
      for (const f of failures) console.error(`  ${f}`);
      return 1;
    }

    if (selected === null) {
      if (deltas.length === 0) {
        console.log("no spine-tasks/*/floor-delta.json on disk — nothing declared, the sum is the pin");
      } else {
        console.warn(`WARNING: no --packets given — summing EVERY floor-delta.json on disk (${deltas.length}):`);
        for (const d of deltas) console.warn(`  ${d.packet}`);
        console.warn("  A declaration is CONSUMED at land and must be deleted in the landing commit. If any packet");
        console.warn("  above has already landed, this sum double-bumps the pin and check-floor.mjs fails at the next");
        console.warn("  land. At a wave land pass --packets with exactly the packets in THIS wave.");
      }
    } else {
      const extra = discovered.filter((d) => !chosen.some((c) => c.packet === d.packet)).map((d) => d.packet);
      if (extra.length > 0) {
        console.log(`note: ${extra.length} floor-delta.json on disk not selected and NOT summed: ${extra.join(", ")}`);
      }
    }

    const totals = sumDeltas(pin, deltas);

    console.log("base totals (client/tests/floor/floor.json):");
    for (const [project, t] of Object.entries(totals)) console.log(`  ${project}: ${t.base}`);
    console.log(`contributions (${deltas.length} packet${deltas.length === 1 ? "" : "s"}):`);
    if (deltas.length === 0) {
      console.log("  (none)");
    }
    for (const d of deltas) {
      console.log(`  ${d.packet}: unit ${signed(d.unit)}, headless ${signed(d.headless)} — ${d.reason}`);
    }
    console.log("expected totals:");
    for (const [project, t] of Object.entries(totals)) {
      console.log(`  ${project}: ${t.base} ${signed(t.delta)} = ${t.expected}`);
    }

    if (mode === "check") {
      console.log("CHECK ONLY — nothing written. This sums DECLARATIONS; it never observes a test count. Run node client/tests/floor/check-floor.mjs to compare the pin against a real run.");
      return 0;
    }

    if (deltas.length === 0) {
      fail("--apply with no contributing packet — there is nothing to sum, and regenerating lastMovedBy to name nobody would erase the recorded reason the pin last moved");
    }
    const nonZero = deltas.filter((d) => d.unit !== 0 || d.headless !== 0);
    const movedNothing = Object.values(totals).every((t) => t.delta === 0);
    if (movedNothing && nonZero.length > 0) {
      fail(
        `--apply refused: the summed totals are identical to the current pin (${Object.entries(totals).map(([p, t]) => `${p} ${t.base}`).join(", ")}) ` +
        `yet ${nonZero.length} packet(s) declared a non-zero delta (${nonZero.map((d) => `${d.packet} ${signed(d.unit)}/${signed(d.headless)}`).join(", ")}). ` +
        "Non-zero declarations that cancel to exactly zero are indistinguishable from a wave that changed nothing, " +
        "and at least one of them is more likely wrong than both right. Resolve by hand and re-run."
      );
    }

    const next = applyPin(raw, totals, buildLastMovedBy(totals, deltas, pin.lastMovedBy));
    fs.writeFileSync(PIN_PATH, next);
    console.log(`pin written: ${PIN_PATH}`);
    for (const [project, t] of Object.entries(totals)) {
      console.log(`  ${project}: ${t.base} -> ${t.expected}`);
    }
    console.log("NEXT: run node client/tests/floor/check-floor.mjs — the declarations are now the pin, and only an observed run proves them. Then delete the consumed floor-delta.json files in the landing commit.");
    return 0;
  } catch (err) {
    if (err instanceof FloorError) {
      console.error(`SUM-DELTAS FAILED:\n  ${err.message}`);
      return 1;
    }
    throw err;
  }
}

if (process.argv[1] && import.meta.url === pathToFileURL(path.resolve(process.argv[1])).href) {
  process.exit(main());
}
