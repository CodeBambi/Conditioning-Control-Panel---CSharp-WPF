// Cross-language ROUND SPEC conformance check: the JS round builders vs specs dumped by the C#
// rounds, plus an exhaustive antisymmetry check of the round judge.
//
//   node Resources/web/goon/test/vectors-rounds.js
//
// Exit 0 all green, 1 on the first mismatch (kind / difficulty / seed / field / expected / got),
// 2 when the vectors file hasn't been generated yet.
//
// Doubles are compared with EXACT equality on purpose: both sides run the same IEEE 754 operations
// on the same 53-bit draws, so any drift is a real desync, not a rounding artifact.

import { readFileSync, existsSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';

import { GoonRng } from '../core/rng.js';
import { GoonRoundKind } from '../core/contracts.js';
import { GoonRoundVerdict } from '../core/rounds/model.js';
import { GoonRoundJudge } from '../core/suddenDeath.js';
import * as quickDraw from '../core/rounds/quickDraw.js';
import * as staringContest from '../core/rounds/staringContest.js';
import * as reactionDuel from '../core/rounds/reactionDuel.js';
import * as bubbleRace from '../core/rounds/bubbleRace.js';

const here = dirname(fileURLToPath(import.meta.url));
const vectorsPath = join(here, 'rng-vectors.json');

if (!existsSync(vectorsPath)) {
  console.error('vectors file missing — run --goon-vectors');
  process.exit(2);
}

let data;
try {
  data = JSON.parse(readFileSync(vectorsPath, 'utf8'));
} catch (e) {
  console.error(`vectors file unreadable: ${e.message}`);
  process.exit(2);
}

const specs = data.round_specs;
if (!Array.isArray(specs) || specs.length === 0) {
  console.error('vectors file has no round_specs — regenerate with a build that dumps them');
  process.exit(2);
}

let fieldChecks = 0;

function fail(entry, field, expected, got) {
  console.error('FAIL');
  console.error(`  kind:       ${entry.kind} (${kindName(entry.kind)})`);
  console.error(`  difficulty: ${entry.difficulty}`);
  console.error(`  seed:       ${entry.seed}`);
  console.error(`  field:      ${field}`);
  console.error(`  expected:   ${JSON.stringify(expected)}`);
  console.error(`  got:        ${JSON.stringify(got)}`);
  process.exit(1);
}

function kindName(k) {
  for (const [name, code] of Object.entries(GoonRoundKind)) if (code === k) return name;
  return String(k);
}

function eq(entry, field, expected, got) {
  fieldChecks++;
  // Exact for numbers (including doubles), strict for everything else.
  if (typeof expected === 'number' || typeof got === 'number') {
    if (!(Number(expected) === Number(got))) fail(entry, field, expected, got);
    return;
  }
  if (expected !== got) fail(entry, field, expected, got);
}

// ------------------------------------------------------------------ spec parity

function checkQuickDraw(entry, spec) {
  const e = entry.spec;
  eq(entry, 'phrase_index', e.phrase_index, quickDraw.PHRASE_POOL.indexOf(spec.phrase));
  eq(entry, 'phrase', e.phrase, spec.phrase);
  eq(entry, 'repeats', e.repeats, spec.repeats);
  eq(entry, 'strict', e.strict, spec.strict);
  eq(entry, 'voice', e.voice, spec.voice);
  eq(entry, 'time_limit_ms', e.time_limit_ms, spec.timeLimitMs);
}

function checkStaring(entry, spec) {
  const e = entry.spec;
  eq(entry, 'duration_ms', e.duration_ms, spec.durationMs);
  eq(entry, 'difficulty', e.difficulty, spec.difficulty);
  eq(entry, 'beats.length', e.beats.length, spec.beats.length);
  for (let i = 0; i < e.beats.length; i++) {
    const b = e.beats[i];
    const g = spec.beats[i] || {};
    eq(entry, `beats[${i}].offset_ms`, b.offset_ms, g.offsetMs);
    eq(entry, `beats[${i}].duration_ms`, b.duration_ms, g.durationMs);
    eq(entry, `beats[${i}].intensity`, b.intensity, g.intensity);
    eq(entry, `beats[${i}].norm_x`, b.norm_x, g.normX);
    eq(entry, `beats[${i}].norm_y`, b.norm_y, g.normY);
    eq(entry, `beats[${i}].scale`, b.scale, g.scale);
  }
}

function checkReaction(entry, spec) {
  const e = entry.spec;
  eq(entry, 'delay_ms', e.delay_ms, spec.delayMs);
  eq(entry, 'max_response_ms', e.max_response_ms, spec.maxResponseMs);
  eq(entry, 'difficulty', e.difficulty, spec.difficulty);
  eq(entry, 'decoy_offsets_ms.length', e.decoy_offsets_ms.length, spec.decoyOffsetsMs.length);
  for (let i = 0; i < e.decoy_offsets_ms.length; i++) {
    eq(entry, `decoy_offsets_ms[${i}]`, e.decoy_offsets_ms[i], spec.decoyOffsetsMs[i]);
  }
}

function checkBubbleRace(entry, spec) {
  const e = entry.spec;
  eq(entry, 'count', e.count, spec.count);
  eq(entry, 'timeout_ms', e.timeout_ms, spec.timeoutMs);
  eq(entry, 'difficulty', e.difficulty, spec.difficulty);
  eq(entry, 'bubbles.length', e.bubbles.length, spec.bubbles.length);
  for (let i = 0; i < e.bubbles.length; i++) {
    const b = e.bubbles[i];
    const g = spec.bubbles[i] || {};
    eq(entry, `bubbles[${i}].index`, b.index, g.index);
    eq(entry, `bubbles[${i}].norm_x`, b.norm_x, g.normX);
    eq(entry, `bubbles[${i}].norm_y`, b.norm_y, g.normY);
    eq(entry, `bubbles[${i}].scale`, b.scale, g.scale);
    eq(entry, `bubbles[${i}].spawn_offset_ms`, b.spawn_offset_ms, g.spawnOffsetMs);
    eq(entry, `bubbles[${i}].drift_angle_deg`, b.drift_angle_deg, g.driftAngleDeg);
    eq(entry, `bubbles[${i}].drift_speed`, b.drift_speed, g.driftSpeed);
  }
}

const BUILDERS = {
  [GoonRoundKind.QuickDrawLockCard]: [quickDraw.buildSpec, checkQuickDraw],
  [GoonRoundKind.StaringContest]: [staringContest.buildSpec, checkStaring],
  [GoonRoundKind.ReactionDuel]: [reactionDuel.buildSpec, checkReaction],
  [GoonRoundKind.BubbleRace]: [bubbleRace.buildSpec, checkBubbleRace],
};

const perKind = new Map();

for (const entry of specs) {
  const pair = BUILDERS[entry.kind];
  if (!pair) {
    console.error(`FAIL — unknown round kind ${entry.kind} in vectors`);
    process.exit(1);
  }
  const [buildSpec, check] = pair;
  // A FRESH generator per entry: the dumper builds each spec from a new GoonRng on the entry's seed.
  const rng = new GoonRng(BigInt(entry.seed));
  check(entry, buildSpec(rng, entry.difficulty));
  perKind.set(entry.kind, (perKind.get(entry.kind) || 0) + 1);
}

// ------------------------------------------------------------------ judge antisymmetry

function result(o = {}) {
  return {
    t: 'round_result',
    round_no: 1,
    completed: o.completed ?? false,
    elapsed_ms: o.elapsed_ms ?? 0,
    reaction_ms: o.reaction_ms ?? null,
    suspect: o.suspect ?? false,
    progress: o.progress ?? 0,
  };
}

// Representative measurements: both outcomes, ties and non-ties on every field the judge reads,
// plus the reaction-duel null (a false start / no input never carries a reaction_ms).
const SAMPLES = [];
for (const completed of [true, false]) {
  for (const elapsed_ms of [1200, 3400]) {
    for (const reaction_ms of [null, 180, 240]) {
      for (const progress of [0, 2, 7]) {
        SAMPLES.push(result({ completed, elapsed_ms, reaction_ms, progress }));
      }
    }
  }
}

const inverse = (v) =>
  v === GoonRoundVerdict.Win ? GoonRoundVerdict.Loss
    : v === GoonRoundVerdict.Loss ? GoonRoundVerdict.Win
      : GoonRoundVerdict.Draw;

const verdictName = (v) => (v === 0 ? 'Win' : v === 1 ? 'Loss' : 'Draw');

let judgeChecks = 0;

function judgeFail(msg) {
  console.error('FAIL (judge)');
  console.error(`  ${msg}`);
  process.exit(1);
}

for (const kind of Object.values(GoonRoundKind)) {
  for (const a of SAMPLES) {
    for (const b of SAMPLES) {
      const ab = GoonRoundJudge.decide(kind, a, b);
      const ba = GoonRoundJudge.decide(kind, b, a);
      judgeChecks++;
      if (ab !== inverse(ba)) {
        judgeFail(`kind ${kindName(kind)}: decide(a,b)=${verdictName(ab)} but decide(b,a)=${verdictName(ba)}\n` +
          `  a: ${JSON.stringify(a)}\n  b: ${JSON.stringify(b)}`);
      }
    }
  }
  // A missing result is never a win for anyone.
  for (const pair of [[null, SAMPLES[0]], [SAMPLES[0], null], [null, null]]) {
    judgeChecks++;
    if (GoonRoundJudge.decide(kind, pair[0], pair[1]) !== GoonRoundVerdict.Draw) {
      judgeFail(`kind ${kindName(kind)}: a null result did not judge as a draw`);
    }
  }
}

// Rule spot-checks: antisymmetry alone would also be satisfied by the WRONG comparison direction.
const RULES = [
  // [kind, mine, theirs, expected]
  [GoonRoundKind.QuickDrawLockCard, result({ completed: true, elapsed_ms: 900 }), result({ completed: true, elapsed_ms: 1500 }), GoonRoundVerdict.Win],
  [GoonRoundKind.QuickDrawLockCard, result({ completed: false, elapsed_ms: 100 }), result({ completed: true, elapsed_ms: 9000 }), GoonRoundVerdict.Loss],
  [GoonRoundKind.QuickDrawLockCard, result({ completed: false, elapsed_ms: 100 }), result({ completed: false, elapsed_ms: 9000 }), GoonRoundVerdict.Draw],
  [GoonRoundKind.StaringContest, result({ completed: true, progress: 80 }), result({ completed: true, progress: 60 }), GoonRoundVerdict.Win],
  [GoonRoundKind.StaringContest, result({ completed: false, elapsed_ms: 9000 }), result({ completed: false, elapsed_ms: 4000 }), GoonRoundVerdict.Win],
  [GoonRoundKind.StaringContest, result({ completed: true, progress: 0 }), result({ completed: false, elapsed_ms: 99999 }), GoonRoundVerdict.Win],
  [GoonRoundKind.ReactionDuel, result({ completed: true, reaction_ms: 210 }), result({ completed: true, reaction_ms: 260 }), GoonRoundVerdict.Win],
  [GoonRoundKind.ReactionDuel, result({ completed: false, progress: 1 }), result({ completed: true, reaction_ms: 1900 }), GoonRoundVerdict.Loss],
  [GoonRoundKind.ReactionDuel, result({ completed: true, reaction_ms: null }), result({ completed: true, reaction_ms: 500 }), GoonRoundVerdict.Loss],
  [GoonRoundKind.BubbleRace, result({ completed: true, elapsed_ms: 8000 }), result({ completed: true, elapsed_ms: 12000 }), GoonRoundVerdict.Win],
  [GoonRoundKind.BubbleRace, result({ completed: false, progress: 5 }), result({ completed: false, progress: 3 }), GoonRoundVerdict.Win],
  [GoonRoundKind.BubbleRace, result({ completed: false, progress: 17 }), result({ completed: true, elapsed_ms: 29999 }), GoonRoundVerdict.Loss],
];

for (const [kind, mine, theirs, expected] of RULES) {
  judgeChecks++;
  const got = GoonRoundJudge.decide(kind, mine, theirs);
  if (got !== expected) {
    judgeFail(`kind ${kindName(kind)}: expected ${verdictName(expected)}, got ${verdictName(got)}\n` +
      `  mine:   ${JSON.stringify(mine)}\n  theirs: ${JSON.stringify(theirs)}`);
  }
}

const byKind = [...perKind.entries()].map(([k, n]) => `${kindName(k)} ${n}`).join(', ');
console.log(`PASS — ${specs.length} round specs (${byKind}), ${fieldChecks} spec fields, ` +
  `${judgeChecks} judge checks`);
process.exit(0);
