// Self-contained sanity pass over core/ — no C# vectors needed (see vectors.js for those).
//   node Resources/web/goon/test/selftest-core.js

import { GoonRng, combineSeeds, saltSeed, seedFromAny, seedToString } from '../core/rng.js';
import { serialize, serializeForSend, parse, wireByteLength, MAX_WIRE_BYTES } from '../core/wire.js';
import { makeMatchStart, makePayload, makeTick, makeRound, costOf, GoonElement, GoonPayloadKind, GoonConsts } from '../core/contracts.js';
import {
  ALWAYS_ON_ELEMENT, GoonCueAction, MAX_MATCH_RISK_TIER, MIN_ALLOWED_ELEMENTS, PoolV1, TogglePool,
  buildRamp, defaultAllowed, isValidAllowed, isValidSharedPool, matchRiskTier, normalizeAllowed,
  profileOf, riskTierOf, sharedPool,
} from '../core/draft.js';
import { local as localCaps } from '../core/caps.js';
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
  ok(costOf(GoonPayloadKind.Spiral) === 2, 'costOf Spiral == 2', String(costOf(GoonPayloadKind.Spiral)));
  ok(costOf(99) === Infinity, 'costOf unknown -> Infinity');
}

// -------------------------------------------------------- draft pool + caps
// The wire codes are FROZEN, so pin them literally: a renumber has to fail here, loudly, rather
// than at the far end of a duel against a client built from the other half of the change.
{
  ok(GoonElement.Spiral === 8, 'GoonElement.Spiral wire code is 8', String(GoonElement.Spiral));
  ok(GoonPayloadKind.Spiral === 7, 'GoonPayloadKind.Spiral wire code is 7', String(GoonPayloadKind.Spiral));

  ok(PoolV1.includes(GoonElement.Spiral), 'Spiral is in the v1 draft pool');
  ok(PoolV1.length === 9, 'v1 pool holds every element', String(PoolV1.length));

  const spiral = profileOf(GoonElement.Spiral);
  const drain = profileOf(GoonElement.BrainDrain);
  ok(riskTierOf(GoonElement.Spiral) === 2, 'Spiral risk tier 2', String(riskTierOf(GoonElement.Spiral)));
  ok(spiral.sustained, 'Spiral is sustained like the drain');
  // The design intent, not just the numbers: earlier onset, gentler ceiling than BrainDrain.
  ok(spiral.entryFraction < drain.entryFraction, 'Spiral opens earlier than BrainDrain',
    `${spiral.entryFraction} vs ${drain.entryFraction}`);
  ok(spiral.intensityEnd < drain.intensityEnd, 'Spiral tops out below BrainDrain',
    `${spiral.intensityEnd} vs ${drain.intensityEnd}`);
  ok(spiral.intensityStart < spiral.intensityEnd, 'Spiral ramps upward');

  // A tier-2 addition must not move the ceiling the score formula is clamped to.
  ok(matchRiskTier([GoonElement.BrainDrain, GoonElement.Spiral, GoonElement.Videos]) === MAX_MATCH_RISK_TIER,
    'drain + spiral + video is the max-risk draft', String(MAX_MATCH_RISK_TIER));

  // The pool is DERIVED from caps — advertising the element is what puts it in the draft.
  const caps = localCaps();
  ok(caps.elements.includes(GoonElement.Spiral), 'local caps advertise the Spiral element');
  ok(caps.payloads.includes(GoonPayloadKind.Spiral), 'local caps advertise the Spiral payload');
  ok(caps.elements.includes(GoonElement.BrainDrain) && caps.payloads.includes(GoonPayloadKind.BrainDrain),
    'local caps advertise BrainDrain');
}

// ------------------------------------------------- the draft agreement (2026-08-03 redesign)
// Each player toggles what they ALLOW; the pool is the intersection; Bubbles are never in either.
// Every check here FAILS against the pre-redesign draft.js, which had no notion of an agreement.
{
  ok(ALWAYS_ON_ELEMENT === GoonElement.Bubbles, 'bubbles are the always-on element',
    String(ALWAYS_ON_ELEMENT));
  ok(!TogglePool.includes(GoonElement.Bubbles), 'and are NOT in the toggle set');
  ok(TogglePool.length === PoolV1.length - 1, 'the toggle set is everything else', String(TogglePool.length));
  ok(MIN_ALLOWED_ELEMENTS === 2, 'the agreement floor is 2', String(MIN_ALLOWED_ELEMENTS));

  // Canonicalisation: distinct, in-pool, bubbles stripped, ascending — both engines must derive
  // the SAME pool from the same pair, with no ordering to agree on.
  ok(JSON.stringify(normalizeAllowed([8, 0, 8, 3, 99, 2])) === JSON.stringify([0, 2, 8]),
    'normalizeAllowed sorts, de-dupes, strips bubbles and unknown codes',
    JSON.stringify(normalizeAllowed([8, 0, 8, 3, 99, 2])));
  ok(JSON.stringify(defaultAllowed(PoolV1)) === JSON.stringify(TogglePool.slice().sort((a, b) => a - b)),
    'the default agreement is "everything toggleable is on"');

  const a = [GoonElement.Flashes, GoonElement.Spiral, GoonElement.Videos, GoonElement.Bubbles];
  const b = [GoonElement.Spiral, GoonElement.Videos, GoonElement.LockCards];
  ok(JSON.stringify(sharedPool(a, b)) === JSON.stringify([GoonElement.Videos, GoonElement.Spiral]),
    'sharedPool is the intersection, canonically ordered', JSON.stringify(sharedPool(a, b)));
  ok(JSON.stringify(sharedPool(a, b)) === JSON.stringify(sharedPool(b, a)), 'and is symmetric');
  ok(sharedPool([GoonElement.Flashes], [GoonElement.Spiral]).length === 0, 'disjoint sets share nothing');

  ok(!isValidAllowed([GoonElement.Flashes]).ok, 'one element is not a legal agreement');
  ok(!isValidAllowed([GoonElement.Flashes, GoonElement.Bubbles]).ok,
    'and bubbles do not count toward the floor');
  ok(isValidAllowed([GoonElement.Flashes, GoonElement.Spiral]).ok, 'two do');
  ok(!isValidSharedPool([GoonElement.Spiral]).ok, 'a one-element intersection is refused');
  ok(isValidSharedPool([GoonElement.Spiral, GoonElement.Videos]).ok, 'a two-element one is fine');
  ok(isValidAllowed([]).error === isValidAllowed([]).error.toLowerCase(),
    'engine refusals stay lowercase', isValidAllowed([]).error);
}

// ------------------------------------------------------ the rolled ramp + always-on bubbles
{
  const LIVE = 720;
  const LIVE_MS = LIVE * 1000;
  const seed = 0xBEEF1234n;
  const pool = [GoonElement.Flashes, GoonElement.Spiral, GoonElement.Subliminals, GoonElement.LockCards];
  const ramp = buildRamp(pool, seed, LIVE, (s) => new GoonRng(s));

  // 1. The bubbles baseline is unconditional: t=0 -> end, 0.15 -> 1.00, sustained.
  const bub = ramp.filter((c) => c.element === ALWAYS_ON_ELEMENT);
  const bubStart = bub.find((c) => c.action === GoonCueAction.Start);
  const bubStop = bub.find((c) => c.action === GoonCueAction.Stop);
  ok(!!bubStart && bubStart.offsetMs === 0 && bubStart.durationMs === 0, 'bubbles start sustained at t=0');
  ok(!!bubStart && Math.abs(bubStart.intensity - 0.15) < 1e-12, 'bubbles open at 0.15',
    String(bubStart && bubStart.intensity));
  ok(!!bubStop && bubStop.offsetMs === LIVE_MS, 'bubbles stop at the final whistle',
    String(bubStop && bubStop.offsetMs));
  ok(bub.filter((c) => c.action === GoonCueAction.Start).length === 1, 'exactly one bubbles start');
  const lastRamp = bub.filter((c) => c.action === GoonCueAction.Intensity).pop();
  ok(!!lastRamp && Math.abs(lastRamp.intensity - (0.15 + 0.85 * (lastRamp.offsetMs / LIVE_MS))) < 1e-12,
    'bubbles ramp linearly toward 1.00', String(lastRamp && lastRamp.intensity));
  ok(buildRamp([], seed, LIVE, (s) => new GoonRng(s)).every((c) => c.element === ALWAYS_ON_ELEMENT),
    'an empty pool is still a bubbles-only ramp');
  ok(buildRamp([GoonElement.Bubbles], seed, LIVE, (s) => new GoonRng(s))
    .every((c) => c.element === ALWAYS_ON_ELEMENT),
  'bubbles cannot be rolled even when handed in as the pool');

  // 2. One roll, both players, and the input ORDER must not matter.
  const again = buildRamp(pool.slice().reverse(), seed, LIVE, (s) => new GoonRng(s));
  ok(JSON.stringify(ramp) === JSON.stringify(again), 'the roll is order-independent and deterministic');
  ok(JSON.stringify(buildRamp(pool, seed + 1n, LIVE, (s) => new GoonRng(s))) !== JSON.stringify(ramp),
    'a different seed rolls a different schedule');

  // 3. Balance: comparable total active time, and no element is ever left running.
  const active = new Map(pool.map((e) => [e, 0]));
  const open = new Map();
  let doubleStart = false;
  for (const c of ramp) {
    if (c.element === ALWAYS_ON_ELEMENT) continue;
    if (c.action === GoonCueAction.Start) {
      if (open.has(c.element)) doubleStart = true;
      open.set(c.element, c.offsetMs);
    } else if (c.action === GoonCueAction.Stop && open.has(c.element)) {
      active.set(c.element, active.get(c.element) + (c.offsetMs - open.get(c.element)));
      open.delete(c.element);
    }
  }
  const totals = Array.from(active.values());
  ok(!doubleStart, 'no element is started twice without a stop between');
  ok(open.size === 0, 'every rolled segment is closed');
  ok(Math.min(...totals) > 0, 'every element in the pool actually runs', JSON.stringify(totals));
  ok((Math.max(...totals) - Math.min(...totals)) <= 0.2 * Math.max(...totals),
    'and gets comparable total active time', JSON.stringify(totals));

  // 4. Escalation: an element's later segments are hotter than its first.
  const spiralStarts = ramp.filter((c) => c.element === GoonElement.Spiral && c.action === GoonCueAction.Start);
  ok(spiralStarts.length >= 2 && spiralStarts[spiralStarts.length - 1].intensity > spiralStarts[0].intensity,
    'intensity escalates toward the end of the match',
    spiralStarts.map((c) => c.intensity.toFixed(2)).join('>'));
  ok(ramp.every((c) => c.intensity >= 0 && c.intensity <= 1), 'no cue leaves the 0..1 band');

  // 5. The sort is a total order: offset, then Stop before Intensity before Start.
  let sorted = true;
  for (let i = 1; i < ramp.length; i++) {
    const p = ramp[i - 1];
    const c = ramp[i];
    if (p.offsetMs > c.offsetMs || (p.offsetMs === c.offsetMs && p.action < c.action)) sorted = false;
  }
  ok(sorted, 'cues come out sorted, Stop before Start at one instant');

  // 6. Risk still reads off the pool both players share.
  ok(matchRiskTier(pool) === matchRiskTier(pool.slice().reverse()), 'risk is order-independent');
}

// ------------------------------------------- vwin: the tick's window count (2026-08-04)
//
// The opponent monitor draws how many FLOATING VIDEO WINDOWS the other player has up. The count
// cannot be inferred from active_effects — a window they popped off their own bubble field is
// invisible to us, and a thrown one is a payload, which never appears there either — so the tick
// grew one optional integer, APPEND-ONLY, on the DraftMsg allowed/confirmed precedent.
//
// Three seams, all here: the CLAMP (nothing downstream may ever see an impossible number), the
// SETTER exec/executor.js calls (phase-gated one way, so a stale claim cannot outlive the match),
// and the INGEST that feeds the monitor. Imported dynamically to leave this file's import block
// exactly as it was.
{
  const { clampWindowCount, GoonMatchPhase } = await import('../core/contracts.js');
  const { GoonMatchService } = await import('../core/match.js');

  // --- the clamp: wire cap first, then the display cap ------------------------------------
  ok(clampWindowCount(0) === 0 && clampWindowCount(1) === 1 && clampWindowCount(4) === 4,
    'a legal count passes through untouched');
  ok(clampWindowCount(5) === 4 && clampWindowCount(8) === 4 && clampWindowCount(9999) === 4,
    'anything above the pool reads as a full pool');
  ok(clampWindowCount(-1) === 0 && clampWindowCount(-9999) === 0, 'negatives are zero');
  ok(clampWindowCount(2.9) === 2, 'fractions truncate', String(clampWindowCount(2.9)));
  ok(clampWindowCount('3') === 3, 'a quoted number is read forgivingly');
  ok(clampWindowCount(undefined) === 0 && clampWindowCount(null) === 0 && clampWindowCount(NaN) === 0,
    'absent / null / NaN all mean no windows');
  ok(clampWindowCount([3]) === 0 && clampWindowCount(true) === 0 && clampWindowCount({}) === 0,
    'and a shape that is not a number at all is zero, not whatever JS would coerce it to');

  // --- the factory + the serializer -------------------------------------------------------
  ok(makeTick().vwin === 0, 'a tick built with no opinion reports no windows');
  ok(makeTick({ vwin: 3 }).vwin === 3, 'the factory carries the count');
  ok(makeTick({ vwin: 77 }).vwin === 4, 'and clamps it at the door');
  ok(JSON.parse(serialize(makeTick({ vwin: 2 }))).vwin === 2, 'it survives serialize');
  ok(parse(serialize(makeTick({ vwin: 3 }))).vwin === 3, 'and the full round trip');
  ok(parse('{"t":"tick","v":1}').vwin === 0, 'a frame that never mentions it parses as zero');
  ok(parse('{"t":"tick","v":1,"vwin":6}').vwin === 4, 'parse clamps an over-claim');
  ok(JSON.parse(serialize({ t: 'tick', v: 1, score: 1 })).vwin === 0,
    'even a hand-built tick leaves the serializer with a legal vwin');

  // --- the setter, and the phase gate -----------------------------------------------------
  const sent = [];
  const transport = {
    send(m) { sent.push(m); return Promise.resolve(true); },
    onMessageReceived() { return () => {}; },
    onStateChanged() { return () => {}; },
  };
  const match = new GoonMatchService(transport, true, { logger: quiet, tag: 'GG:vwin' });
  ok(match.localWindowCount === 0, 'a fresh match reports no windows');
  ok(match.setLocalWindowCount(3) === false && match.localWindowCount === 0,
    'and refuses a count in Idle — a window cannot exist before the run does');

  match._phase = GoonMatchPhase.Live;
  ok(match.setLocalWindowCount(3) === true && match.localWindowCount === 3, 'Live takes the count');
  ok(match.setLocalWindowCount(99) === true && match.localWindowCount === 4, 'clamped on the way in',
    String(match.localWindowCount));
  ok(match.setLocalWindowCount(-2) === true && match.localWindowCount === 0, 'a negative lands as zero');
  ok(match.setLocalWindowCount('2') === true && match.localWindowCount === 2, 'a quoted number is taken too');
  ok(match.setLocalWindowCount({}) === true && match.localWindowCount === 0,
    'and garbage empties the report rather than sticking');

  // --- what the tick actually carries ------------------------------------------------------
  match.setLocalWindowCount(2);
  // Backdated well past TickIntervalMs — the pump is a rate limiter, and 0 is only "long ago"
  // if the process has already been running for three seconds.
  match._lastTickSentLocalMs = -1e6;
  match._maybeSendStateTick();
  const built = sent.filter((m) => m && m.t === 'tick').pop() || null;
  ok(!!built, 'the tick pump built a tick');
  ok(!!built && built.vwin === 2, 'and it carries the local window count', String(built && built.vwin));
  ok(!!built && built.charges === 0 && Array.isArray(built.active_effects),
    'alongside everything the tick already carried');

  match._phase = GoonMatchPhase.SuddenDeath;
  ok(match.setLocalWindowCount(1) === true && match.localWindowCount === 1, 'sudden death still has windows');
  match._phase = GoonMatchPhase.Recap;
  ok(match.setLocalWindowCount(3) === false && match.localWindowCount === 1,
    'a claim after the match ends is refused — no phantom stack on their little screen');
  ok(match.setLocalWindowCount(0) === true && match.localWindowCount === 0,
    'but the teardown zero is ALWAYS taken (executor.stopAll runs on the way out)');

  // --- the ingest that feeds the monitor ---------------------------------------------------
  ok(match.opponent.vwin === 0, 'the opponent starts with an empty stack');
  let painted = 0;
  match.onOpponentStateChanged(() => { painted++; });

  match._handleTick(parse(serialize(makeTick({ vwin: 3 }))));
  ok(match.opponent.vwin === 3, 'their tick fills it', String(match.opponent.vwin));
  ok(painted === 1, 'and raises the event the monitor repaints on', String(painted));

  match._handleTick(makeTick({}));
  ok(match.opponent.vwin === 0, 'a tick without the field empties it again (an older peer, or C#)');

  // A tick that never went through parse() — the solo driver and the play-test harness both
  // build one by hand — is clamped by the ingest itself rather than trusted.
  match._handleTick(Object.assign(makeTick({}), { vwin: 99 }));
  ok(match.opponent.vwin === 4, 'ingest clamps on its own', String(match.opponent.vwin));
  match._handleTick(Object.assign(makeTick({}), { vwin: -5 }));
  ok(match.opponent.vwin === 0, 'in both directions');

  ok(match.setLocalWindowCount(2) === false,
    'and none of that ingest resurrected the local setter outside Live');
  match.dispose();
  ok(match.setLocalWindowCount(0) === false, 'a disposed match takes nothing at all');
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
