#!/usr/bin/env node
// census.mjs — SP-121. Which SHIPPED types have ZERO executed lines?
//
// WHY THIS EXISTS (client/docs/task-board.md:33)
// The floor counts test results. The warning gate counts warnings. Neither can see a type whose
// only coverage is a reference or a property read. That blind spot produced a real defect twice:
// SP-101 (the first honest test killed the test host) and SP-118's SystemScheduleClock (default
// clock on every product path, doc comment asserting fault containment, executed by no test — and
// closing it found D188). This tool answers the question mechanically.
//
// IT ANSWERS "ZERO", NOT A PERCENTAGE, AND IT SETS NO THRESHOLD.
// A record whose synthesized Equals ran reports line-rate 0.5 while no behaviour was driven, so a
// percentage would answer a worse question. There is deliberately no target, no tolerance and no
// failing gate on the number here or anywhere: a tolerance sized to a number just observed is
// exactly the size of the defect it will next hide.
//
// USAGE
//   node client/tools/coverage/census.mjs                  regenerate the committed census
//   node client/tools/coverage/census.mjs --self-check     run the rule over its own fixture table
//   node client/tools/coverage/census.mjs --from <dir>     use cobertura reports already under <dir>
//   node client/tools/coverage/census.mjs --filter <expr>  unit project only, filtered (bite probe)
//   node client/tools/coverage/census.mjs --out <path>     write somewhere other than the doc
//   node client/tools/coverage/census.mjs --metadata-json  print this file's OWN reading of the
//                                                          shipped assembly's metadata as JSON and
//                                                          stop: no coverage run, no test host, no
//                                                          document. SP-124's cross-validation
//                                                          calls this and compares every name,
//                                                          kind and verdict against ordinary .NET
//                                                          reflection over the identical file.
//   node client/tools/coverage/census.mjs --check-stale    recompute the three metadata scalars
//                                                          from the built assembly and diff them
//                                                          against the committed census. Exit 1
//                                                          and name every drifted row. SP-124's
//                                                          land-time drift check; it always prints
//                                                          what it did NOT check.
//     --dll <path>     which assembly --metadata-json / --check-stale read (default: the Debug build)
//     --census <path>  which document --check-stale reads (default: the committed census)
//
// MECHANISM
//   --collect:"Code Coverage;Format=cobertura" on BOTH test projects. No new package:
//   Microsoft.NET.Test.Sdk 17.10.0 already brings Microsoft.CodeCoverage 17.10.0 transitively.
//   ";Format=cobertura" is load-bearing — without it the output is a binary .coverage needing a
//   converter, and that would be a dependency admission.
//
//   BOTH suites, unioned. A unit-only census would falsely list every View and Window, because
//   CcpClient.HeadlessTests is what drives them. Coverage is unioned per LINE NUMBER, never by
//   summing counts, so a line covered in both runs is not double counted.
//
// THE RULE IS DATA, NOT CODE. Every shape literal lives in shipped-type-rule.json, which
// ExecutionCensusTests.cs reads and applies independently in .NET Regex. This file must contain no
// inline shape literal of its own — a pattern inlined here would widen the census while the C#
// guard stayed green, so ExecutionCensusTests asserts that this file has none.
//
// ARTIFACTS NEVER TOUCH THE WORKTREE. Ten tests produce a 15 MB XML and a full run is larger;
// results go to os.tmpdir() and are deleted. Nothing .coverage/.cobertura.xml is ever committed.
//
// OUTPUT IS DETERMINISTIC. Type names, repo-relative source paths, instrumented line counts,
// ordinal sort. No timestamp, no machine name, no absolute path, no duration — it must diff
// cleanly next wave.

import { spawnSync } from "node:child_process";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { fileURLToPath } from "node:url";

const HERE = path.dirname(fileURLToPath(import.meta.url));
const REPO = path.resolve(HERE, "..", "..", "..");
const RULE_PATH = path.join(HERE, "shipped-type-rule.json");
const DEFAULT_OUT = path.join(REPO, "client", "docs", "execution-census.md");
const PRODUCT_DLL = path.join(REPO, "client", "src", "CcpClient.Desktop", "bin", "Debug", "net10.0", "CcpClient.Desktop.dll");
const UNIT_PROJECT = path.join(REPO, "client", "tests", "CcpClient.Tests", "CcpClient.Tests.csproj");
const HEADLESS_PROJECT = path.join(REPO, "client", "tests", "CcpClient.HeadlessTests", "CcpClient.HeadlessTests.csproj");
const WITH_SLOT = path.join(REPO, "client", "tools", "gate", "with-slot.mjs");

// ---------------------------------------------------------------- the rule

function loadRule() {
  const rule = JSON.parse(fs.readFileSync(RULE_PATH, "utf8"));
  for (const clause of rule.clauses) {
    if (clause.pattern) {
      clause.regex = new RegExp(clause.pattern);
    }
  }
  return rule;
}

/**
 * The whole classifier. Returns { verdict, clause } where verdict is one of
 * kept | excluded-<clauseId> | flagged-V1. Nothing is dropped silently: an entry that survives
 * every clause but sits outside the shipped namespace root is KEPT and FLAGGED, never removed.
 */
function classify(pkg, name, rule) {
  const segments = name.split(".");
  for (const clause of rule.clauses) {
    switch (clause.kind) {
      case "keepOnlyPackage":
        if (pkg !== clause.package) return { verdict: `excluded-${clause.id}`, clause: clause.id };
        break;
      case "anySegmentMatches":
        if (segments.some((s) => clause.regex.test(s))) return { verdict: `excluded-${clause.id}`, clause: clause.id };
        break;
      case "finalSegmentMatches":
        if (clause.regex.test(segments[segments.length - 1])) return { verdict: `excluded-${clause.id}`, clause: clause.id };
        break;
      default:
        throw new Error(`shipped-type-rule.json declares clause ${clause.id} of unknown kind "${clause.kind}"`);
    }
  }
  if (!name.startsWith(rule.shippedNamespaceRoot)) {
    return { verdict: `flagged-${rule.valve.id}`, clause: rule.valve.id };
  }
  return { verdict: "kept", clause: null };
}

function selfCheck(rule) {
  const failures = [];
  for (const f of rule.fixtures) {
    const got = classify(f.package, f.name, rule).verdict;
    if (got !== f.expect) failures.push(`  ${f.name}\n    expected ${f.expect}, got ${got}  (${f.why})`);
  }
  if (failures.length) {
    console.error(`shipped-type rule self-check FAILED on ${failures.length}/${rule.fixtures.length} fixtures:`);
    console.error(failures.join("\n"));
    return false;
  }
  console.log(`shipped-type rule self-check: ${rule.fixtures.length}/${rule.fixtures.length} fixtures classify as declared`);
  return true;
}

// ---------------------------------------------------------------- cobertura

const XML_ENTITY = { "&lt;": "<", "&gt;": ">", "&amp;": "&", "&quot;": '"', "&apos;": "'" };
const decode = (s) => s.replace(/&(?:lt|gt|amp|quot|apos);/g, (m) => XML_ENTITY[m]);

// ATTRIBUTION — why an excluded entry's lines are not simply thrown away.
//
// The C# compiler moves the body of every async method, iterator and lambda into a nested
// compiler-generated class, and the SOURCE LINES go with it. C2 removes those entries, so a type
// whose executable source lines ALL live in state machines would never appear in the report as
// itself. That is not hypothetical: CcpClient.Desktop.Lifecycle.StartupPhaseRunner is a static
// class of async methods driven by more than twenty tests, and it appears in no <class> entry of
// its own. Discarding those lines would give the census two failure modes it exists to prevent —
// a type whose async body ran reading as ZERO, and a type whose async body never ran being MISSED
// entirely, which is the SP-118 shape exactly.
//
// So a compiler-generated entry's lines are ATTRIBUTED BACK to the type that declares it, which is
// the type whose source file those lines came from. This is attribution, never a widened
// exclusion: no line is dropped and no type is removed from the answer by it. Measured on this
// tree it changes the zero count by nothing (0 false zeros, 0 missed zeros) and adds three
// authored types to the universe - which is the point: it makes the census right by construction
// rather than right by luck.
const stripGenerics = (s) => s.replace(/<[^<>]*>$/, "");

export function declaringType(name) {
  const segments = name.split(".");
  while (segments.length > 0 && segments[segments.length - 1].startsWith("<")) segments.pop();
  return segments.length > 0 ? stripGenerics(segments.join(".")) : null;
}

function repoRelative(file) {
  const normalized = file.replace(/\\/g, "/");
  const at = normalized.lastIndexOf("/client/");
  return at >= 0 ? normalized.slice(at + 1) : normalized;
}

/**
 * Accumulate one cobertura report into `types`. Coverage is unioned per (file,line): a line is
 * covered if ANY report saw a hit on it. Partial classes merge here — one type is one entry keyed
 * by fully-qualified name, so App (App.axaml + App.axaml.cs) and DtrhLoom (its source half plus the
 * [GeneratedRegex] half under obj/) are each ONE type, zero-execution only if no half ran.
 */
function accumulate(xml, rule, types, tally) {
  const packageRe = /<package [^>]*name="([^"]+)"([\s\S]*?)<\/package>/g;
  let pkgMatch;
  while ((pkgMatch = packageRe.exec(xml)) !== null) {
    const pkg = pkgMatch[1];
    const classRe = /<class [^>]*name="([^"]*)" filename="([^"]*)">([\s\S]*?)<\/class>/g;
    let clsMatch;
    while ((clsMatch = classRe.exec(pkgMatch[2])) !== null) {
      const name = decode(clsMatch[1]);
      const file = decode(clsMatch[2]);
      const body = clsMatch[3];
      tally.universe++;
      const { verdict, clause } = classify(pkg, name, rule);

      let key = stripGenerics(name);
      let display = name;
      let attributed = false;
      if (verdict.startsWith("excluded-")) {
        tally.removed[clause] = (tally.removed[clause] ?? 0) + 1;
        if (pkg !== rule.shippedAssembly) {
          accumulateNonShipped(name, body, tally);
          continue;
        }
        const declaring = declaringType(name);
        // Attribute only to a type of the shipped assembly. The [GeneratedRegex] tree declares
        // itself under System.Text.RegularExpressions.Generated, so its lines are correctly not
        // attributed to anything and stay removed.
        if (!declaring || !declaring.startsWith(rule.shippedNamespaceRoot)) continue;
        key = declaring;
        display = declaring;
        attributed = true;
        tally.attributed++;
      } else {
        tally.kept++;
      }

      // The class-level <lines> block is the last one; the earlier blocks repeat it per method.
      const blocks = body.match(/<lines>[\s\S]*?<\/lines>/g);
      const lastBlock = blocks ? blocks[blocks.length - 1] : "";
      let entry = types.get(key);
      if (!entry) {
        entry = { name: display, flagged: false, files: new Set(), lines: new Set(), covered: new Set(), ownEntries: 0, methods: new Set() };
        types.set(key, entry);
      }
      if (!attributed) {
        entry.name = display; // a type's own entry always supplies the display name, arity included
        entry.flagged = verdict.startsWith("flagged-");
        entry.ownEntries++;
        entry.files.add(repoRelative(file));
        for (const method of body.matchAll(/<method [^>]*name="([^"]*)"/g)) entry.methods.add(decode(method[1]));
      }
      const lineRe = /<line number="(\d+)" hits="(\d+)"/g;
      let lineMatch;
      while ((lineMatch = lineRe.exec(lastBlock)) !== null) {
        const lineKey = `${file}:${lineMatch[1]}`;
        entry.lines.add(lineKey);
        if (Number(lineMatch[2]) > 0) entry.covered.add(lineKey);
      }
      if (attributed && entry.ownEntries === 0) entry.files.add(repoRelative(file));
    }
  }
}

// C1's counterfactual, published rather than assumed: how many types in the NOT-shipped assemblies
// have zero executed lines? If that is ~0 the clause removed nothing real. It is measured, not
// argued, and it never enters the answer.
function accumulateNonShipped(name, body, tally) {
  const blocks = body.match(/<lines>[\s\S]*?<\/lines>/g);
  const lastBlock = blocks ? blocks[blocks.length - 1] : "";
  const hit = /<line number="\d+" hits="([1-9]\d*)"/.test(lastBlock);
  const prior = tally.nonShipped.get(name) ?? false;
  tally.nonShipped.set(name, prior || hit);
}

function findReports(dir) {
  const found = [];
  const walk = (d) => {
    for (const e of fs.readdirSync(d, { withFileTypes: true })) {
      const p = path.join(d, e.name);
      if (e.isDirectory()) walk(p);
      else if (e.name.endsWith(".cobertura.xml")) found.push(p);
    }
  };
  walk(dir);
  return found.sort();
}

// ------------------------------------------------- assembly metadata (ECMA-335)
//
// THE MOST IMPORTANT SECTION IN THIS FILE, because it bounds what the census CANNOT see.
// A type with no instrumentable IL never enters the collector's universe at all — interfaces
// without default members, enums, and fields-only nested types such as
// OrphanSafePlayerFactory<TPlayer>.ConstructionSlot (Audio/AudioSeams.cs:228-233). Those types are
// INVISIBLE, not zero, and a census that did not say so would read as complete. Reading the
// TypeDef and MethodDef tables of the shipped assembly gives the exact denominator with no package:
// a method row whose RVA is 0 has no body, and a type none of whose methods has a body carries no
// instrumentable IL.

export function readMetadataTypeDefs(dllPath) {
  const buf = fs.readFileSync(dllPath);
  const peOffset = buf.readUInt32LE(0x3c);
  if (buf.readUInt32LE(peOffset) !== 0x00004550) throw new Error(`${dllPath} is not a PE image`);
  const coff = peOffset + 4;
  const sectionCount = buf.readUInt16LE(coff + 2);
  const optionalSize = buf.readUInt16LE(coff + 16);
  const optional = coff + 20;
  const magic = buf.readUInt16LE(optional);
  const dataDirs = optional + (magic === 0x20b ? 112 : 96);
  const cliRva = buf.readUInt32LE(dataDirs + 14 * 8);
  if (cliRva === 0) throw new Error(`${dllPath} has no CLI header — not a managed assembly`);

  const sections = [];
  for (let i = 0; i < sectionCount; i++) {
    const s = optional + optionalSize + i * 40;
    sections.push({ va: buf.readUInt32LE(s + 12), size: buf.readUInt32LE(s + 8), raw: buf.readUInt32LE(s + 20) });
  }
  const toOffset = (rva) => {
    for (const s of sections) if (rva >= s.va && rva < s.va + Math.max(s.size, 1)) return s.raw + (rva - s.va);
    throw new Error(`RVA 0x${rva.toString(16)} lies in no section`);
  };

  const cli = toOffset(cliRva);
  const metadata = toOffset(buf.readUInt32LE(cli + 8));
  if (buf.readUInt32LE(metadata) !== 0x424a5342) throw new Error("metadata root signature is not BSJB");
  const versionLength = buf.readUInt32LE(metadata + 12);
  let p = metadata + 16 + versionLength + 4; // + flags(2) + streams(2)
  const streamCount = buf.readUInt16LE(metadata + 16 + versionLength + 2);
  const streams = {};
  for (let i = 0; i < streamCount; i++) {
    const offset = buf.readUInt32LE(p);
    const size = buf.readUInt32LE(p + 4);
    let end = p + 8;
    while (buf[end] !== 0) end++;
    streams[buf.toString("ascii", p + 8, end)] = { offset: metadata + offset, size };
    p = p + 8 + (Math.floor((end - (p + 8)) / 4) + 1) * 4;
  }
  const tableStream = streams["#~"] ?? streams["#-"];
  if (!tableStream) throw new Error("no #~ metadata table stream");
  const strings = streams["#Strings"];

  const heapSizes = buf.readUInt8(tableStream.offset + 6);
  const valid = buf.readBigUInt64LE(tableStream.offset + 8);
  let cursor = tableStream.offset + 24;
  const rows = new Array(64).fill(0);
  for (let t = 0; t < 64; t++) {
    if ((valid >> BigInt(t)) & 1n) {
      rows[t] = buf.readUInt32LE(cursor);
      cursor += 4;
    }
  }
  // FieldPtr(0x03)/MethodPtr(0x05) exist only in uncompressed edit-and-continue metadata. If one
  // ever appeared the table offsets below would silently shift, so refuse rather than miscount.
  if (rows[0x03] || rows[0x05]) throw new Error("uncompressed ENC metadata (FieldPtr/MethodPtr present) is not supported");

  const strIdx = heapSizes & 0x01 ? 4 : 2;
  const guidIdx = heapSizes & 0x02 ? 4 : 2;
  const blobIdx = heapSizes & 0x04 ? 4 : 2;
  const simple = (table) => (rows[table] < 0x10000 ? 2 : 4);
  const coded = (tables, tagBits) => (Math.max(...tables.map((t) => rows[t])) < 1 << (16 - tagBits) ? 2 : 4);
  const resolutionScope = coded([0x00, 0x1a, 0x23, 0x01], 2);
  const typeDefOrRef = coded([0x02, 0x01, 0x1b], 2);

  const sizeModule = 2 + strIdx + 3 * guidIdx;
  const sizeTypeRef = resolutionScope + 2 * strIdx;
  const sizeTypeDef = 4 + 2 * strIdx + typeDefOrRef + simple(0x04) + simple(0x06);
  const sizeField = 2 + strIdx + blobIdx;
  const sizeMethodDef = 4 + 2 + 2 + strIdx + blobIdx + simple(0x08);

  const readIdx = (at, width) => (width === 2 ? buf.readUInt16LE(at) : buf.readUInt32LE(at));
  const readString = (at, width) => {
    const start = strings.offset + readIdx(at, width);
    let end = start;
    while (buf[end] !== 0) end++;
    return buf.toString("utf8", start, end);
  };

  const typeRefBase = cursor + sizeModule * rows[0x00];
  const typeDefBase = typeRefBase + sizeTypeRef * rows[0x01];
  const fieldBase = typeDefBase + sizeTypeDef * rows[0x02];
  const methodBase = fieldBase + sizeField * rows[0x04];

  const typeRefs = [];
  for (let i = 0; i < rows[0x01]; i++) {
    const at = typeRefBase + i * sizeTypeRef;
    typeRefs.push({
      name: readString(at + resolutionScope, strIdx),
      ns: readString(at + resolutionScope + strIdx, strIdx),
    });
  }

  const methodRva = [];
  for (let i = 0; i < rows[0x06]; i++) methodRva.push(buf.readUInt32LE(methodBase + i * sizeMethodDef));

  const typeDefs = [];
  for (let i = 0; i < rows[0x02]; i++) {
    const at = typeDefBase + i * sizeTypeDef;
    const flags = buf.readUInt32LE(at);
    const name = readString(at + 4, strIdx);
    const ns = readString(at + 4 + strIdx, strIdx);
    const extends_ = readIdx(at + 4 + 2 * strIdx, typeDefOrRef);
    const methodList = readIdx(at + 4 + 2 * strIdx + typeDefOrRef + simple(0x04), simple(0x06));
    typeDefs.push({ flags, name, ns, extends_, methodList });
  }

  const baseTypeName = (encoded) => {
    if ((encoded & 3) !== 1) return null; // only a TypeRef base can be System.Enum / System.ValueType
    const row = typeRefs[(encoded >> 2) - 1];
    return row ? `${row.ns}.${row.name}` : null;
  };

  return typeDefs.map((t, i) => {
    const start = t.methodList;
    const end = i + 1 < typeDefs.length ? typeDefs[i + 1].methodList : rows[0x06] + 1;
    let hasIl = false;
    for (let m = start; m < end; m++) {
      if (methodRva[m - 1]) { hasIl = true; break; }
    }
    const base = baseTypeName(t.extends_);
    const kind = t.flags & 0x20 ? "interface"
      : base === "System.Enum" ? "enum"
      : base === "System.ValueType" ? "struct"
      : "class";
    return { name: t.name, ns: t.ns, kind, hasIl };
  });
}

// THE THREE SCALARS THE DOCUMENT PRINTS FROM METADATA, named ONCE.
// render() writes these labels and --check-stale reads them back, so an edit to one that misses
// the other surfaces as a loud MISSING ROW rather than as a check that quietly stopped looking.
const METADATA_ROW_TYPEDEFS = "type definitions in `CcpClient.Desktop.dll`";
const METADATA_ROW_AUTHORED = "of those, authored name shape (would survive C2/C3)";
const METADATA_ROW_NO_BODY =
  "— no method body at all: interfaces without default members, enums, abstract-only";

/**
 * The metadata view the document reports, computed ONCE and shared by render(), --metadata-json
 * and --check-stale, so the JSON a test cross-validates is literally the numbers the document
 * would print rather than a second derivation that could agree with neither.
 *
 * Classified as the assembly's own package so C1 never fires: what remains is exactly the
 * name-shape clauses, applied to metadata simple names by the SAME classifier that reads the
 * coverage report. No shape literal appears here.
 */
function metadataView(rule, metadata) {
  const authored = metadata.filter(
    (t) => !classify(rule.shippedAssembly, t.name, rule).verdict.startsWith("excluded-"));
  return { authored, invisible: authored.filter((t) => !t.hasIl) };
}

/**
 * SP-124. The reader's own answer, in machine-readable form, for cross-validation against a
 * SECOND independent mechanism (ordinary .NET reflection over the identical file) at RUNTIME.
 *
 * WHY THIS MODE EXISTS. The census used to be cross-validated by pinning two of its scalars in
 * ExecutionCensusTests and recomputing them by reflection. That bound a LIVE measurement to a
 * STORED number, so every packet adding one ordinary shipped type reddened the suite and the only
 * remedy was regenerating a document closed to lanes. Emitting the reading instead moves BOTH
 * sides of the comparison to runtime: a new type moves both, a wrong reader moves only one.
 *
 * Names and kinds are emitted, never just counts: 295 of this assembly's 1325 TypeDef simple names
 * repeat (nested types reuse simple names), so a reader that dropped one row and invented another
 * would keep every count intact. Only the sorted multiset catches it.
 */
function metadataJson(rule, dllPath) {
  const metadata = readMetadataTypeDefs(dllPath);
  const { authored, invisible } = metadataView(rule, metadata);
  return {
    dll: path.basename(dllPath),
    rows: metadata.map((t) => ({
      name: t.name,
      kind: t.kind,
      hasIl: t.hasIl,
      verdict: classify(rule.shippedAssembly, t.name, rule).verdict,
    })),
    scalars: {
      [METADATA_ROW_TYPEDEFS]: metadata.length,
      [METADATA_ROW_AUTHORED]: authored.length,
      [METADATA_ROW_NO_BODY]: invisible.length,
    },
    authored: authored.map((t) => t.name),
    noMethodBody: invisible.map((t) => t.name),
  };
}

/**
 * SP-124. Does the COMMITTED census still describe the tree?
 *
 * This is the drift check that used to live in the test suite as a pinned scalar. It is here, and
 * not there, because a per-lane fact comparing a live count to a stored one is a chokepoint: every
 * packet adding a shipped type reds it, and no lane may regenerate the document. Run at the LAND,
 * where regenerating is the orchestrator's to do.
 *
 * IT ALWAYS PRINTS WHAT IT DID NOT CHECK. A quiet exit here means the three metadata scalars agree
 * with the built assembly, and NOTHING MORE — the coverage-derived rows and the embedded suite run
 * table are stale by construction the moment a test is added, and a reader who took silence for
 * "the census is current" would be misled by exactly the reassuring direction this tool exists to
 * refuse.
 */
function checkStale(rule, dllPath, censusPath) {
  const metadata = readMetadataTypeDefs(dllPath);
  const { authored, invisible } = metadataView(rule, metadata);
  const expected = [
    [METADATA_ROW_TYPEDEFS, metadata.length],
    [METADATA_ROW_AUTHORED, authored.length],
    [METADATA_ROW_NO_BODY, invisible.length],
  ];

  const document = fs.readFileSync(censusPath, "utf8");
  const drift = [];
  for (const [label, live] of expected) {
    const escaped = label.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
    const row = new RegExp(`^\\| (?:\\*\\*)?${escaped}(?:\\*\\*)? \\| (?:\\*\\*)?(\\d+)(?:\\*\\*)? \\|`, "m").exec(document);
    if (!row) {
      drift.push(`  MISSING ROW  "${label}"\n    the census carries no such row — this check refuses to go blind`);
    } else if (Number(row[1]) !== live) {
      drift.push(`  STALE ROW    "${label}"\n    census says ${row[1]}, the built assembly has ${live}`);
    }
  }

  // THE CHECKER'S OWN INPUT CAN BE STALE, so it never lets that pass unsaid. A leftover Debug
  // binary makes every verdict below wrong in the LOUD direction — a STALE ROW against a document
  // that describes the source tree perfectly — and review hit exactly that with a stale probe
  // build. The write time is printed on both outcomes because "rebuild and re-run" has to be the
  // first thing a reader checks, not the last. Deliberately console output and never the document:
  // the census itself must carry no timestamp (Census_IsDeterministicAndCarriesNoMachineIdentity).
  const built = fs.statSync(dllPath).mtime.toISOString().replace(/\.\d+Z$/, "Z");
  console.log(`read ${path.basename(dllPath)}, last written ${built} — if that predates your last`);
  console.log("source change, REBUILD and re-run: this tool compares the document against the");
  console.log("BINARY, so a stale binary produces a stale verdict in either direction.");
  console.log("checked: the three scalars this tool reads from the shipped assembly's own metadata.");
  console.log("NOT CHECKED, and stale by construction the moment a test or a covered line moves:");
  console.log("  * the embedded suite run table (passed/failed/skipped per project) — it is a");
  console.log("    snapshot of one run and nothing here or in the floor binds it (task-board.md:34)");
  console.log("  * every coverage-derived row: the census universe, the executed/zero split, the");
  console.log("    zero-execution list itself and its per-type line counts");
  console.log("  * the platform marks, the namespace headings and the prose");
  console.log("A quiet exit therefore means the metadata scalars agree, and nothing more.");

  if (drift.length === 0) {
    console.log(`census metadata scalars agree with ${path.basename(dllPath)}: ${expected.map(([, n]) => n).join(" / ")}`);
    return true;
  }

  console.error(`the committed census no longer describes ${path.basename(dllPath)}:`);
  console.error(drift.join("\n"));
  console.error("regenerate with `node client/tools/coverage/census.mjs` — that is the orchestrator's at a land, never a lane's");
  return false;
}

// ---------------------------------------------------------------- the run

function runSuite(project, resultsDir, filter) {
  const args = [WITH_SLOT, "--slots", "3", "--", "dotnet", "test", project, "-c", "Debug", "--nologo",
    "--collect:Code Coverage;Format=cobertura", "--results-directory", resultsDir];
  if (filter) args.push("--filter", filter);
  const r = spawnSync(process.execPath, args, { stdio: ["ignore", "pipe", "pipe"], encoding: "utf8" });
  const output = `${r.stdout ?? ""}${r.stderr ?? ""}`;
  const counters = /Failed:\s+(\d+), Passed:\s+(\d+), Skipped:\s+(\d+), Total:\s+(\d+)/.exec(output);
  const failures = [...output.matchAll(/^\s*Failed\s+(\S+)/gm)].map((m) => m[1]);
  process.stdout.write(output.split("\n").filter((l) => /error|Failed|Passed!|Total:/.test(l)).join("\n") + "\n");
  return {
    project: path.basename(project, ".csproj"),
    exit: r.status,
    counters: counters ? { failed: +counters[1], passed: +counters[2], skipped: +counters[3], total: +counters[4] } : null,
    failures: [...new Set(failures)].sort(),
  };
}

// ---------------------------------------------------------------- the document

const ORDINAL = (a, b) => (a < b ? -1 : a > b ? 1 : 0);

function render(rule, tally, types, runs, metadata) {
  const namespaces = new Set(metadata.map((t) => t.ns).filter(Boolean));
  const shipped = [...types.values()].map((t) => ({
    name: t.name,
    flagged: t.flagged,
    lines: t.lines.size,
    covered: t.covered.size,
    stateMachineOnly: t.ownEntries === 0,
    // CONSTRUCTION-INVISIBLE. Measured with a controlled probe, not inferred: a member-less
    // record (`sealed record Exit : Base;`) emits no sequence point for the construction the
    // compiler synthesises, so its ONLY line-mapped member is the copy constructor nothing calls.
    // Such a type reads ZERO even when a passing test constructs it. The probe put a member-less
    // record and a one-member record side by side through the same fact: the member-less one
    // landed in the zero list, the one-member control did not.
    constructionInvisible: t.ownEntries > 0 && t.lines.size === 1 && t.methods.size === 1 && t.methods.has(".ctor"),
    files: [...t.files].sort(ORDINAL),
  })).sort((a, b) => ORDINAL(a.name, b.name));

  const zero = shipped.filter((t) => t.covered === 0 && !t.flagged);
  const executed = shipped.filter((t) => t.covered > 0 && !t.flagged);
  const flagged = shipped.filter((t) => t.flagged);
  const nested = zero.filter((t) => typePath(t.name, namespaces).path.includes("."));
  const generatedOn = os.platform() === "win32" ? "windows" : os.platform() === "darwin" ? "macos" : os.platform();
  const platformMarked = platformMarks(zero, generatedOn);
  const otherOsGated = [...platformMarked.values()].filter((m) => m.kind === "other-os-gated").length;
  const sameOsCode = [...platformMarked.values()].filter((m) => m.kind === "same-os-code").length;
  const constructionInvisible = zero.filter((t) => t.constructionInvisible).length;
  const deadLines = zero.reduce((n, t) => n + t.lines, 0);

  // The metadata's simple names are classified by the SAME clauses as the report's names, so the
  // denominator and the census cannot drift apart. metadataView is shared with --metadata-json and
  // --check-stale (SP-124), so what a test cross-validates is what this document prints.
  const { authored, invisible } = metadataView(rule, metadata);
  const invisibleBy = (kind) => invisible.filter((t) => t.kind === kind).length;
  const universeCount = shipped.length - flagged.length;

  const nonShippedTotal = tally.nonShipped.size;
  const nonShippedZero = [...tally.nonShipped.values()].filter((hit) => !hit).length;

  const out = [];
  const w = (s = "") => out.push(s);

  w("# Execution census — which shipped types have ZERO executed lines");
  w();
  w("<!-- GENERATED by `node client/tools/coverage/census.mjs`. Do not hand-edit: regenerate. -->");
  w();
  w("Answers `client/docs/task-board.md:33` mechanically. **This document sets no threshold, no");
  w("target and no failing gate.** The row asks which types have ZERO executed lines, not a");
  w("percentage: a record whose synthesized `Equals` ran reports `line-rate=\"0.5\"` while no");
  w("behaviour was driven, so a percentage would answer a worse question. A tolerance sized to a");
  w("number just observed is exactly the size of the defect it will next hide.");
  w();
  w("Regenerate with one command:");
  w();
  w("```");
  w("node client/tools/coverage/census.mjs");
  w("```");
  w();
  w("## The answer");
  w();
  w("| | |");
  w("|---|---|");
  w(`| **shipped types with ZERO executed lines** | **${zero.length}** |`);
  w(`| shipped types with at least one executed line | ${executed.length} |`);
  w(`| census universe (shipped types reaching this census) | ${universeCount} |`);
  w(`| instrumented lines inside the zero-execution types | ${deadLines} |`);
  w(`| of the zero-execution types, nested | ${nested.length} |`);
  w(`| of the zero-execution types, top-level | ${zero.length - nested.length} |`);
  w();
  w("The nested/top-level split is a breakdown, not an exclusion: a nested type is counted as its");
  w("own row (see the rule below), because a discriminated-union case no test ever constructs is");
  w("exactly the dead behaviour the row asks about.");
  w();
  w("### Attribution, so the number is right by construction rather than by luck");
  w();
  w("The compiler moves every async body, iterator and lambda into a nested generated class, and the");
  w("SOURCE LINES go with it. Those entries are excluded by C2, so their lines are **attributed back");
  w("to the type that declares them** rather than discarded. Without this, a type whose async body");
  w("ran would read as ZERO, and a type whose async body never ran would be MISSED entirely — the");
  w("SP-118 shape exactly.");
  w();
  w("| | |");
  w("|---|---|");
  w(`| excluded entries whose lines were attributed back to a shipped type | ${tally.attributed} |`);
  w(`| types with NO entry of their own — every source line lives in a state machine | ${shipped.filter((t) => t.stateMachineOnly).length} |`);
  w(`| of those, driven | ${shipped.filter((t) => t.stateMachineOnly && t.covered > 0).length} |`);
  w(`| of those, ZERO (would have been missed entirely without attribution) | ${shipped.filter((t) => t.stateMachineOnly && t.covered === 0).length} |`);
  w();
  w("Measured on this tree, attribution changes the zero count by **nothing**: no type in the list");
  w("below has a generated child that executed (0 false zeros), and no undriven type exists only as");
  w("a state machine (0 missed zeros). It is kept because the next tree is not this tree.");
  w();
  w("## WHAT THIS CENSUS CANNOT SEE — read this before the number");
  w();
  w("**1. A type with no source-mapped IL never enters the universe at all — it is INVISIBLE, not");
  w("zero, and this is the largest thing the census cannot see.** The collector reports coverage by");
  w("SOURCE LINE, so a type only enters the report when it owns at least one IL instruction carrying");
  w("a sequence point. Measured from the shipped assembly's own TypeDef and MethodDef tables:");
  w();
  w("| | |");
  w("|---|---|");
  w(`| ${METADATA_ROW_TYPEDEFS} | ${metadata.length} |`);
  w(`| ${METADATA_ROW_AUTHORED} | ${authored.length} |`);
  w(`| of those, reaching this census | ${universeCount} |`);
  w(`| **INVISIBLE rather than zero** | **${authored.length - universeCount}** |`);
  w(`| ${METADATA_ROW_NO_BODY} | ${invisible.length} |`);
  w(`|     interfaces ${invisibleBy("interface")}, enums ${invisibleBy("enum")}, structs ${invisibleBy("struct")}, classes ${invisibleBy("class")} | |`);
  w(`| — has a method body, but no source line maps to it | ${authored.length - universeCount - invisible.length} |`);
  w();
  w("The second category is the subtle one and it is worth naming precisely, because it is easy to");
  w("state wrongly. `OrphanSafePlayerFactory<TPlayer>.ConstructionSlot`");
  w("(`client/src/CcpClient.Desktop/Audio/AudioSeams.cs:228-233`) is a fields-only nested class, and");
  w("it does NOT lack IL — the compiler supplies it a default constructor. What it lacks is a");
  w("sequence point: no line of source maps to that constructor, so the collector emits nothing and");
  w("the type is absent. The rest of this category is Avalonia's XamlIl support surface");
  w("(`CompiledAvaloniaXaml.XamlIlHelpers`, `XamlDynamicSetters`, `!XamlLoader`, the per-file");
  w("`NamespaceInfo:/...` types), which is generated straight to IL from `.axaml` with no C# source");
  w("behind it.");
  w();
  w("**Those XamlIl types are why the UNCLASSIFIED valve below reads zero, and the reason is not the");
  w("one it looks like.** They exist in the shipped assembly, they carry IL, and their names would");
  w("pass every clause of the rule — but they never enter the report, so the valve never sees them.");
  w("An empty valve here means *nothing unexplained reached the report*, not *nothing unexplained");
  w("exists in the assembly*.");
  w();
  w("Nothing in this census speaks about any of those types. They are not dead and not alive here;");
  w("they are outside the instrument.");
  w();
  w("**2. A MEMBER-LESS RECORD READS ZERO EVEN WHEN A TEST CONSTRUCTS IT. Do not trust a");
  w("`construction-invisible` row without reading the code.** For `sealed record Exit : Base;` the");
  w("compiler emits no sequence point for the construction it synthesises, so the type's ONLY");
  w("line-mapped member is the copy constructor, which nothing calls. The type then reports one");
  w("instrumented line, zero covered — indistinguishable from genuinely dead.");
  w();
  w("This was MEASURED with a controlled probe rather than reasoned about: a member-less record and");
  w("an otherwise identical one-member record were constructed by the same passing fact; the");
  w("member-less one landed in the zero list and the one-member control did not. The live instance");
  w("is `DtrhProtocol.DtrhPageMessage.Exit`, which `DtrhProtocolTests.cs:24` parses from");
  w("`{\"type\":\"exit\"}` on a green theory row — and which this census still calls zero.");
  w();
  w(`**${constructionInvisible} of the ${zero.length} rows below carry this marker.** They are NOT`);
  w("excluded, because excluding them would be widening a rule to shorten a list. They are marked,");
  w("because a census that cannot tell you which of its own rows are weak evidence is worse than one");
  w("that can. Treat a `construction-invisible` row as *unproven in both directions*.");
  w();
  w("**3. Executed is not tested.** A line executed incidentally — a constructor run to build a");
  w("fixture, a `ToString` in an assertion message — counts as executed here. This census can prove");
  w("a type is untouched. It can never prove one is verified.");
  w();
  w("**4. A dead HALF of a live partial class is invisible.** Coverage is unioned across a type's");
  w("entries, so a type is zero-execution only when no half ran. That is a method-level question and");
  w("this is a type-level census.");
  w();
  w(`**5. The census is OS-CONDITIONED — but a platform predicate only excuses a zero when it selects`);
  w(`an OS this census did NOT run on.** Generated on \`${generatedOn}\`. A row is marked from a`);
  w("platform predicate in the type's own source file, with comments stripped so prose about a");
  w("predicate never marks anything, and **the mark records its polarity** — because a");
  w("direction-blind marker launders zeros in the reassuring direction, which is the one failure");
  w("this whole census exists to refuse.");
  w();
  w("| mark | count | what it means |");
  w("|---|---|---|");
  w(`| \`other-os-gated\` | ${otherOsGated} | the file's code selects an OS this run was not on, so the MACHINE may explain the zero — check the other OS before believing the row |`);
  w(`| \`same-os-code\` | ${sameOsCode} | the file's code selects \`${generatedOn}\`, the OS this run WAS on, so **the machine does not explain the zero at all** — the type would be MORE dead elsewhere, not less |`);
  w();
  w("The distinction matters most on the largest dead surface below. A `same-os-code` row is not");
  w("excused by anything: its platform predicate is satisfied here and the code still never ran.");
  w("**A type is confidently dead only when both OS legs agree, and neither mark ever changes the");
  w("count.**");
  w();
  w("**6. Zero is a fact about the suite, not a verdict on the code.** A type may be exercised by a");
  w("headed capture, by the publish gate, or by nothing at all. The census names candidates; it does");
  w("not file defects.");
  w();
  w("## The run this census was generated from");
  w();
  w("Recorded because a census is only as good as the run behind it. Note the self-reference:");
  w("`ExecutionCensusTests` reads THIS document, so a generation that changes the numbers records");
  w("those guards as failing — they were reading the previous document while the new one was being");
  w("written. That is a property of generating a document from a run of the suite that checks it,");
  w("not a defect in either.");
  w();
  w("| project | exit | total | passed | failed | skipped |");
  w("|---|---|---|---|---|---|");
  for (const r of runs) {
    const c = r.counters;
    w(`| ${r.project} | ${r.exit} | ${c ? c.total : "?"} | ${c ? c.passed : "?"} | ${c ? c.failed : "?"} | ${c ? c.skipped : "?"} |`);
  }
  w();
  const red = runs.filter((r) => r.exit !== 0);
  if (red.length) {
    w("**This census was generated from a RED run, and that is recorded rather than chased.** A");
    w("coverage run is diagnostic and is never a gate. A test that failed still executed the lines it");
    w("reached, so the executed set is real; the one distortion is that a test which stopped short may");
    w("leave a type in the list below that a green run would have driven. Failing tests:");
    w();
    for (const r of red) for (const f of r.failures) w(`- \`${f}\` (${r.project})`);
    w();
  }
  w("## The shipped-type rule");
  w();
  w("The exclusion rule is BIGGER than the answer, so it is the risk. Every clause is defended in");
  w("one sentence and states what it removed. Widening a clause to shorten the list would be");
  w("`allowedSkips`-as-quarantine wearing a new hat.");
  w();
  w(`Universe: **${tally.universe}** \`<class>\` entries across every cobertura report.`);
  w();
  w("| clause | what it removes | removed | defence |");
  w("|---|---|---|---|");
  for (const c of rule.clauses) {
    const what = c.kind === "keepOnlyPackage" ? `any entry outside package \`${c.package}\``
      : c.kind === "anySegmentMatches" ? `any entry with a dot-segment matching \`${c.pattern}\``
      : `any entry whose FINAL dot-segment matches \`${c.pattern}\``;
    w(`| **${c.id}** | ${what} | ${tally.removed[c.id] ?? 0} | ${c.defence} |`);
  }
  w(`| **kept** | merged by fully-qualified name into types | ${tally.kept} entries → ${universeCount} types | a partial class is ONE type; coverage is unioned |`);
  w(`| *(attribution)* | C2/C3 entries whose lines return to their declaring shipped type | ${tally.attributed} re-attributed, 0 dropped | the compiler moved those source lines out of the type; attribution moves them back |`);
  w();
  w("### Clauses deliberately NOT written");
  w();
  w("| would have been | removed | why it was refused |");
  w("|---|---|---|");
  for (const r of rule.rejectedClauses) w(`| ${r.wouldHaveBeen} | — | ${r.refusedBecause} |`);
  w();
  w("### C1's counterfactual, measured rather than argued");
  w();
  w(`C1 removes the unshipped test assemblies. Published so the clause is not taken on trust: of`);
  w(`**${nonShippedTotal}** authored-shape types in \`CcpClient.Tests\` and \`CcpClient.HeadlessTests\`,`);
  w(`**${nonShippedZero}** have zero executed lines. That number is a diagnostic about test-side`);
  w(`helpers and never enters the answer above.`);
  w();
  w(`### UNCLASSIFIED — the rule is incomplete (${flagged.length})`);
  w();
  w(`${rule.valve.defence}`);
  w();
  if (flagged.length === 0) {
    w("None. Every entry surviving the clauses is under `CcpClient.Desktop.*`.");
  } else {
    w("| type | instrumented lines | executed lines | source |");
    w("|---|---|---|---|");
    for (const t of flagged) w(`| \`${t.name}\` | ${t.lines} | ${t.covered} | ${t.files.join(", ")} |`);
  }
  w();
  w(`## The ${zero.length} shipped types with zero executed lines`);
  w();
  w("Sorted ordinally by fully-qualified name. `lines` is the instrumented line count inside the");
  w("type — the size of what nothing drove.");
  w();
  let currentNs = null;
  for (const t of zero) {
    const { ns, path: typeName } = typePath(t.name, namespaces);
    if (ns !== currentNs) {
      currentNs = ns;
      w();
      w(`### ${ns}`);
      w();
      w("| type | lines | source |");
      w("|---|---|---|");
    }
    const mark = platformMarked.get(t.name);
    const marker = (mark ? ` \`${mark.kind}(${mark.selects.join(",")})\`` : "")
      + (t.constructionInvisible ? " `construction-invisible`" : "");
    w(`| \`${typeName}\`${marker} | ${t.lines} | ${t.files.join(", ")} |`);
  }
  w();
  return out.join("\n");
}

// Nesting and namespace are resolved EXACTLY, from the shipped assembly's own namespace set, never
// guessed from segment counts: the longest declared namespace that prefixes the name is the
// namespace, and what follows is the type path. `CcpClient.Desktop.Haptics.HapticGateDecision.Allow`
// resolves to namespace `CcpClient.Desktop.Haptics` and type path `HapticGateDecision.Allow` —
// two segments, therefore nested.
function typePath(name, namespaces) {
  let best = "";
  for (const ns of namespaces) {
    if (ns.length > best.length && name.startsWith(ns + ".")) best = ns;
  }
  return best ? { ns: best, path: name.slice(best.length + 1) } : { ns: "(global namespace)", path: name };
}

// A zero row is marked from a platform predicate found in the type's OWN SOURCE FILE, and the mark
// RECORDS ITS POLARITY, because a direction-blind marker launders zeros in the reassuring direction.
//
// Two corrections, both of which this census got wrong on its first pass:
//
//  1. THE PREDICATE MUST BE CODE. Comments are stripped before matching. ChaosTunnelWin32.cs:8 is a
//     `///` line reading "every caller guards on OperatingSystem.IsWindows()" — prose, not a branch,
//     and it must not mark anything.
//
//  2. THE PREDICATE MUST SELECT AN OS THIS CENSUS DID NOT RUN ON, or it explains nothing. Four of
//     the original marks (DtrhProcessFailed.cs:45, DtrhProfileLock.cs:86, ChaosTunnelService.cs:278,
//     DtrhHostWindow.axaml.cs:701) are `!OperatingSystem.IsWindows()` guards selecting WINDOWS — the
//     OS this census was generated on. Such a type would be MORE dead on the other OS, not less, so
//     "the machine explains the zero" is exactly backwards for it. Those rows are marked
//     `same-os-code`, which says the opposite: the machine does NOT excuse this row.
//
// Neither mark is ever an exclusion; both kinds of row still count in the total.
const PLATFORM_SELECTORS = [
  [/OperatingSystem\.Is(Windows|Linux|MacOS|FreeBSD|Android|IOS|Browser)\s*\(/g, 1],
  [/IsOSPlatform\s*\(\s*OSPlatform\.(\w+)/g, 1],
  [/\[SupportedOSPlatform\s*\(\s*"([a-zA-Z]+)/g, 1],
];

// Naive but sufficient: strip block comments, then line comments. A `//` inside a string literal
// truncates that line early, which can only ever LOSE a predicate (making a mark less likely), never
// invent one — the safe direction for a marker that must not over-claim.
function stripComments(source) {
  return source.replace(/\/\*[\s\S]*?\*\//g, " ").replace(/\/\/[^\n]*/g, " ");
}

function platformSelectorsIn(source) {
  const code = stripComments(source);
  const found = new Set();
  for (const [pattern, group] of PLATFORM_SELECTORS) {
    for (const hit of code.matchAll(pattern)) found.add(hit[group].toLowerCase());
  }
  return found;
}

function platformMarks(zeroTypes, generatedOn) {
  const marks = new Map();
  const cache = new Map();
  for (const type of zeroTypes) {
    const selectors = new Set();
    for (const file of type.files) {
      if (!cache.has(file)) {
        let found = new Set();
        try {
          found = platformSelectorsIn(fs.readFileSync(path.join(REPO, file), "utf8"));
        } catch {
          found = new Set(); // a source file the census cannot read never marks anything
        }
        cache.set(file, found);
      }
      for (const os of cache.get(file)) selectors.add(os);
    }
    if (selectors.size === 0) continue;
    const other = [...selectors].filter((os) => os !== generatedOn).sort(ORDINAL);
    marks.set(type.name, other.length > 0
      ? { kind: "other-os-gated", selects: other }
      : { kind: "same-os-code", selects: [...selectors].sort(ORDINAL) });
  }
  return marks;
}

// ---------------------------------------------------------------- main

function main() {
  const argv = process.argv.slice(2);
  const flag = (n) => argv.includes(n);
  const value = (n) => (argv.indexOf(n) >= 0 ? argv[argv.indexOf(n) + 1] : null);
  const rule = loadRule();
  const dll = value("--dll") ?? PRODUCT_DLL;

  // SP-124's two non-generating modes, handled FIRST and deliberately ahead of selfCheck: both
  // must be usable on a tree with no coverage run behind them, and --metadata-json's stdout is
  // parsed as JSON by a test, so nothing else may write to it.
  if (flag("--metadata-json")) {
    process.stdout.write(`${JSON.stringify(metadataJson(rule, dll))}\n`);
    return;
  }
  if (flag("--check-stale")) {
    process.exit(checkStale(rule, dll, value("--census") ?? DEFAULT_OUT) ? 0 : 1);
  }

  if (flag("--self-check")) {
    process.exit(selfCheck(rule) ? 0 : 1);
  }
  if (!selfCheck(rule)) {
    console.error("refusing to generate a census from a rule that fails its own fixtures");
    process.exit(1);
  }

  const filter = value("--filter");
  let from = value("--from");
  let runs = [];
  let temp = null;
  if (!from) {
    temp = fs.mkdtempSync(path.join(os.tmpdir(), "ccp-census-"));
    from = temp;
    runs.push(runSuite(UNIT_PROJECT, path.join(temp, "unit"), filter));
    if (!filter) runs.push(runSuite(HEADLESS_PROJECT, path.join(temp, "headless")));
  }

  const reports = findReports(from);
  if (reports.length === 0) {
    console.error(`no *.cobertura.xml under ${from} — the census refuses to report over nothing`);
    process.exit(1);
  }
  const runsFile = path.join(from, "runs.json");
  if (runs.length === 0 && fs.existsSync(runsFile)) runs = JSON.parse(fs.readFileSync(runsFile, "utf8"));
  if (temp) fs.writeFileSync(runsFile, JSON.stringify(runs));

  const types = new Map();
  const tally = { universe: 0, kept: 0, attributed: 0, removed: {}, nonShipped: new Map() };
  for (const r of reports) accumulate(fs.readFileSync(r, "utf8"), rule, types, tally);

  const metadata = readMetadataTypeDefs(dll);
  const document = render(rule, tally, types, runs, metadata);

  const out = value("--out") ?? DEFAULT_OUT;
  if (flag("--print")) process.stdout.write(document);
  else {
    fs.writeFileSync(out, document);
    console.log(`census written to ${path.relative(REPO, out).replace(/\\/g, "/")} from ${reports.length} report(s)`);
  }
  if (temp && !flag("--keep")) fs.rmSync(temp, { recursive: true, force: true });
}

// Guarded so the metadata reader and the classifier can be imported by a diagnostic script
// without launching a census run.
if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  main();
}
