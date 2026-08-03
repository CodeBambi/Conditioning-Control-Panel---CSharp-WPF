// Cross-language RNG conformance check: JS rng.js vs vectors dumped by the C# GoonRng.
//
//   node Resources/web/goon/test/vectors.js
//
// Exit 0 all green, 1 on the first mismatch (with seed/section/index/expected/got),
// 2 when the vectors file hasn't been generated yet.

import { readFileSync, existsSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';

import { GoonRng, saltSeed } from '../core/rng.js';

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

let checks = 0;

function fail(seed, section, index, expected, got) {
  console.error('FAIL');
  console.error(`  seed:     ${seed}`);
  console.error(`  section:  ${section}`);
  console.error(`  index:    ${index}`);
  console.error(`  expected: ${expected}`);
  console.error(`  got:      ${got}`);
  process.exit(1);
}

function expectEq(seed, section, index, expected, got) {
  checks++;
  if (String(expected) !== String(got)) fail(seed, section, index, expected, got);
}

// --------------------------------------------------------------- seeds[]
// A FRESH instance per section: the C# dumper draws each section from a new GoonRng, so sharing
// one instance here would compare against a stream that has already advanced.
for (const entry of data.seeds || []) {
  const seed = BigInt(entry.seed);

  if (entry.next_ulong) {
    const rng = new GoonRng(seed);
    for (let i = 0; i < entry.next_ulong.length; i++) {
      expectEq(entry.seed, 'next_ulong', i, entry.next_ulong[i], rng.nextULong().toString());
    }
  }

  if (entry.next_int_0_16) {
    const rng = new GoonRng(seed);
    for (let i = 0; i < entry.next_int_0_16.length; i++) {
      expectEq(entry.seed, 'next_int_0_16', i, entry.next_int_0_16[i], rng.nextInt(0, 16));
    }
  }

  if (entry.next_int_2000_6001) {
    const rng = new GoonRng(seed);
    for (let i = 0; i < entry.next_int_2000_6001.length; i++) {
      expectEq(entry.seed, 'next_int_2000_6001', i, entry.next_int_2000_6001[i], rng.nextInt(2000, 6001));
    }
  }

  if (entry.next_double) {
    const rng = new GoonRng(seed);
    for (let i = 0; i < entry.next_double.length; i++) {
      const got = rng.nextDouble();
      const want = Number(entry.next_double[i]);
      checks++;
      // Both sides compute (u >> 11) * 2^-53 exactly, so equality is the right bar; the tolerance
      // only absorbs the dumper's decimal round-trip.
      if (Math.abs(got - want) > 1e-15) fail(entry.seed, 'next_double', i, want, got);
    }
  }
}

// --------------------------------------------------------------- derive[]
for (const entry of data.derive || []) {
  const rng = GoonRng.derive(BigInt(entry.seed), entry.purpose);
  const draws = entry.first_draws || [];
  for (let i = 0; i < draws.length; i++) {
    expectEq(`${entry.seed} / "${entry.purpose}"`, 'derive', i, draws[i], rng.nextULong().toString());
  }
}

// --------------------------------------------------------------- salt_seed
if (data.salt_seed) {
  const matchSeed = BigInt(data.salt_seed.match_seed);
  for (const [code, expected] of Object.entries(data.salt_seed.elements || {})) {
    expectEq(data.salt_seed.match_seed, 'salt_seed', code, expected, saltSeed(matchSeed, Number(code)).toString());
  }
}

const skipped = ['round_specs', 'ramp'].filter((k) => data[k] !== undefined);
if (skipped.length) console.log(`note: skipping ${skipped.join(', ')} — Wave 2 consumes those`);

console.log(`PASS — ${checks} values matched across ${(data.seeds || []).length} seed(s), ` +
  `${(data.derive || []).length} derive case(s), ` +
  `${data.salt_seed ? Object.keys(data.salt_seed.elements || {}).length : 0} salt_seed element(s)`);
process.exit(0);
