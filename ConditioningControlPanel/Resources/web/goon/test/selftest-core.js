// Self-contained sanity pass over core/ — no C# vectors needed (see vectors.js for those).
//   node Resources/web/goon/test/selftest-core.js

import { GoonRng, combineSeeds, saltSeed, seedFromAny, seedToString } from '../core/rng.js';
import { serialize, serializeForSend, parse, wireByteLength, MAX_WIRE_BYTES } from '../core/wire.js';
import { makeMatchStart, makePayload, makeTick, makeRound, costOf, GoonPayloadKind, GoonConsts } from '../core/contracts.js';
import { MatchClock } from '../core/clock.js';
import { GoonScheduler, ticker } from '../core/scheduler.js';

let failures = 0;
let n = 0;
function ok(cond, label, extra = '') {
  n++;
  if (!cond) { failures++; console.error(`  FAIL ${label} ${extra}`); }
}
const quiet = { info() {}, warn() {}, error() {}, log() {} };

// ------------------------------------------------------------------ rng
{
  // Published splitmix64 outputs for state 0 — validates the seeding leg against literature.
  const rng = new GoonRng(0n);
  ok(rng._s0 === 0xE220A8397B1DCDAFn, 'splitmix64(0)[0]', rng._s0.toString(16));
  ok(rng._s1 === 0x6E789E6AA1B965F4n, 'splitmix64(0)[1]', rng._s1.toString(16));
  ok(rng._s2 === 0x06C45D188009454Fn, 'splitmix64(0)[2]', rng._s2.toString(16));
  ok(rng._s3 === 0xF88BB8A8724C81ECn, 'splitmix64(0)[3]', rng._s3.toString(16));

  const a = new GoonRng(0x9E3779B97F4A7C15n);
  const b = new GoonRng(0x9E3779B97F4A7C15n);
  let same = true;
  for (let i = 0; i < 1000; i++) if (a.nextULong() !== b.nextULong()) same = false;
  ok(same, 'same seed -> identical stream');

  const c = new GoonRng(1n);
  let allIn = true;
  const hist = new Array(16).fill(0);
  for (let i = 0; i < 10000; i++) {
    const v = c.nextInt(0, 16);
    if (!Number.isInteger(v) || v < 0 || v >= 16) allIn = false;
    hist[v]++;
  }
  ok(allIn, 'nextInt(0,16) in range over 10k draws');
  ok(hist.every((h) => h > 400 && h < 900), 'nextInt(0,16) roughly uniform', hist.join(','));

  const d = new GoonRng(42n);
  let dOk = true;
  for (let i = 0; i < 10000; i++) { const v = d.nextDouble(); if (!(v >= 0 && v < 1)) dOk = false; }
  ok(dOk, 'nextDouble in [0,1)');

  ok(new GoonRng(7n).nextInt(2000, 6001) >= 2000, 'nextInt wide range low bound');
  ok(new GoonRng(7n).nextInt(5, 5) === 5, 'nextInt empty range returns min');

  // All 64 bits must survive: an accidental Number path would clamp these to 53 bits.
  const big = new GoonRng(0xFFFFFFFFFFFFFFFFn);
  let anyHigh = false;
  for (let i = 0; i < 64; i++) if (big.nextULong() > (1n << 53n)) anyHigh = true;
  ok(anyHigh, 'nextULong produces values above 2^53');

  const d1 = GoonRng.derive(12345n, 'bubbles');
  const d2 = GoonRng.derive(12345n, 'bubbles');
  const d3 = GoonRng.derive(12345n, 'lockcard');
  ok(d1.nextULong() === d2.nextULong(), 'derive deterministic');
  ok(d1.nextULong() !== d3.nextULong(), 'derive purpose-separated');
  ok(GoonRng.derive(1n, '').seed === GoonRng.derive(1n, null).seed, 'derive null purpose == empty');

  ok(combineSeeds(0xF0F0n, 0x0F0Fn) === 0xFFFFn, 'combineSeeds xor');
  ok(combineSeeds(5n, 5n) === 0n, 'combineSeeds self-cancels');
  ok(saltSeed(0n, 0) === 0x9E3779B97F4A7C15n, 'saltSeed element 0', saltSeed(0n, 0).toString(16));
  ok(saltSeed(0n, 7) === ((8n * 0x9E3779B97F4A7C15n) & ((1n << 64n) - 1n)), 'saltSeed element 7');

  ok(seedFromAny('18446744073709551615') === 0xFFFFFFFFFFFFFFFFn, 'seedFromAny max string');
  ok(seedFromAny('not a number') === 0n, 'seedFromAny garbage -> 0');
  ok(seedFromAny(-5) === 0n, 'seedFromAny negative -> 0');
  ok(seedFromAny(12345) === 12345n, 'seedFromAny number');
  ok(seedFromAny(null) === 0n, 'seedFromAny null -> 0');
  ok(seedToString(0xFFFFFFFFFFFFFFFFn) === '18446744073709551615', 'seedToString max');
}

// ------------------------------------------------------------------ wire
{
  const seed = 0xDEADBEEFCAFEBABEn;
  const json = serialize(makeMatchStart({ start_match_ms: 123456, seed_contribution: seed }));
  ok(json.includes('"seed_contribution":"16045690984503098046"'), 'seed serializes as decimal string', json);
  const back = parse(json);
  ok(back.seed_contribution === seed, 'seed round-trips as BigInt');
  ok(back.t === 'match_start' && back.v === 1, 'discriminator + version');

  const rjson = serialize(makeRound({ round_no: 2, seed_contribution: 7n }));
  ok(parse(rjson).seed_contribution === 7n, 'round seed round-trips');

  const p = serialize(makePayload({ id: 'x1', kind: GoonPayloadKind.LockCard, text: null, tags: null }));
  ok(!p.includes('"text"') && !p.includes('"tags"'), 'nulls dropped', p);
  const pb = parse(p);
  ok(pb.text === null && pb.tags === null, 'dropped nulls read back as null');
  ok(pb.intensity === 0 && pb.voice === false, 'missing members read as C# defaults');

  ok(parse('{"t":"tick","score":5}').at_match_ms === 0, 'partial frame filled from defaults');
  ok(parse('{"t":"tick","score":null}').score === 0, 'explicit null on non-nullable -> default');
  ok(parse('{"t":"tick","score":5,"bogus":1}').bogus === undefined, 'unknown members dropped');
  ok(parse('{"t":"nope"}', { logger: quiet }) === null, 'unknown t -> null');
  ok(parse('{"v":1}', { logger: quiet }) === null, 'missing t -> null');
  ok(parse('not json', { logger: quiet }) === null, 'garbage -> null');
  ok(parse('', { logger: quiet }) === null, 'empty -> null');
  ok(parse('[1,2]', { logger: quiet }) === null, 'array -> null');
  ok(parse(null, { logger: quiet }) === null, 'null input -> null');
  ok(parse('{"t":"tick","v":99}', { logger: quiet }).v === 99, 'newer protocol version parses anyway');
  ok(parse('{"t":"match_start","seed_contribution":123}').seed_contribution === 123n, 'numeric seed accepted');

  // An unregistered BigInt field must fail loudly rather than ship a wrong seed.
  let threw = false;
  try { serialize({ t: 'tick', v: 1, mystery: 1n }); } catch { threw = true; }
  ok(threw, 'stray BigInt throws (no silent generic replacer)');

  const huge = serialize(makeTick({ active_effects: new Array(4000).fill('effectname') }));
  ok(wireByteLength(huge) > MAX_WIRE_BYTES, 'oversize frame built for the check');
  ok(serializeForSend(makeTick({ active_effects: new Array(4000).fill('effectname') }), { logger: quiet }) === null,
    'oversize send refused');
  ok(parse(huge, { logger: quiet }) === null, 'oversize parse refused');
  ok(wireByteLength('héllo') === 6, 'wireByteLength counts utf-8 bytes');

  ok(costOf(GoonPayloadKind.BrainDrain) === 3 && costOf(GoonPayloadKind.FlashBurst) === 1, 'costOf known kinds');
  ok(costOf(99) === Infinity, 'costOf unknown -> Infinity');
}

// ------------------------------------------------------------------ clock + scheduler
const main = async () => {
  // Loopback pair with a fake 5000 ms skew on the guest: sync must converge back to ~0 error.
  const host = new MatchClock({ isClockMaster: true, tag: 'host', logger: quiet });
  const guest = new MatchClock({ isClockMaster: false, tag: 'guest', logger: quiet, testSkewMs: 5000 });
  host.attach(async (m) => { await Promise.resolve(); guest.tryHandleMessage(m); });
  guest.attach(async (m) => { await Promise.resolve(); host.tryHandleMessage(m); });

  const [hOk, gOk] = await Promise.all([host.sync(), guest.sync()]);
  ok(hOk && gOk, 'both clocks synced');
  ok(host.offsetMs === 0, 'host offset pinned at 0', String(host.offsetMs));
  ok(Math.abs(guest.offsetMs + 5000) < 50, 'guest offset cancels the skew', String(guest.offsetMs));
  ok(Math.abs(guest.nowMatchMs() - host.nowMatchMs()) < 50, 'match clocks agree');
  ok(guest.matchMsToLocal(guest.nowMatchMs()) - guest.nowLocalMs() < 5, 'matchMsToLocal inverts');
  ok(host.safeFireAt(0) - host.nowMatchMs() >= GoonConsts.MinScheduleBufferMs, 'safeFireAt keeps the buffer');
  ok(!host.isFireable(host.nowMatchMs() + 10), 'isFireable rejects a near instant');

  const sched = new GoonScheduler(host, { logger: quiet });
  ok(sched.scheduleAt(host.nowMatchMs() + 500, () => {}, 'too-soon') === null, 'scheduler refuses <1000ms lead');
  ok(sched.scheduleAt(host.nowMatchMs() - 5000, () => {}, 'past') === null, 'scheduler refuses the past');

  const target = host.nowMatchMs() + 1200;
  let firedAt = 0;
  sched.scheduleAt(target, () => { firedAt = host.nowMatchMs(); }, 'ontime');
  const cancelled = sched.scheduleAt(host.nowMatchMs() + 1200, () => { failures++; console.error('  FAIL cancelled fire ran'); }, 'cancelme');
  ok(sched.cancel(cancelled), 'cancel returns true for a live handle');
  await new Promise((r) => setTimeout(r, 1600));
  ok(firedAt !== 0 && Math.abs(firedAt - target) < 260, 'fired within 260ms of target', String(firedAt - target));
  ok(sched.pendingCount === 0, 'scheduler drained');
  sched.dispose();

  let ticks = 0;
  let totalElapsed = 0;
  const t = ticker(100, (ms) => { ticks++; totalElapsed += ms; });
  await new Promise((r) => setTimeout(r, 550));
  t.stop();
  ok(ticks >= 4 && ticks <= 7, 'ticker fired ~5x in 550ms', String(ticks));
  ok(Math.abs(totalElapsed - 550) < 200, 'ticker elapsed sums to wall time (delta-based)', String(totalElapsed));

  host.dispose();
  guest.dispose();

  console.log(failures === 0 ? `PASS — ${n} checks` : `FAILED — ${failures}/${n} checks`);
  process.exit(failures === 0 ? 0 : 1);
};

main().catch((e) => { console.error(e); process.exit(1); });
